using System;

namespace CASCADE
{
    /// <summary>Everything the score needs: the story clock and the start time of every event (Story.Never = not yet).</summary>
    public struct ScoreClock
    {
        public double storyTime;        // story seconds at the first sample of the next Render call (Render advances it)
        public double dtPerSample;      // story seconds per output sample; 0 while paused
        public float speed;             // walking speed, m/s
        public int footstepCount;       // one footstep sounds per increment
        public int footSurface;         // 0 quay stone, 1 stairs, 2 promenade paving, 3 gravel
        public float tGulls, tHum, tTremor1, tTremor2, tVista, tEruption, tTremor3, tCollapse, tDrawdown, tWave,
                     tAshStorm, tGlimpse1, tGlimpse2, tSilence, tArrival;

        public void ClearEvents()
        {
            tGulls = tHum = tTremor1 = tTremor2 = tVista = tEruption = tTremor3 = tCollapse = tDrawdown = tWave =
                tAshStorm = tGlimpse1 = tGlimpse2 = tSilence = tArrival = Story.Never;
        }
    }

    /// <summary>
    /// The whole sound of CASCADE as one deterministic renderer. Sample-level DSP runs on the sample clock; every level,
    /// cutoff and trigger is a pure function of the story clock evaluated every Block samples, so a seek simply lands in
    /// the right state. No allocation, no locks, no Unity calls: usable on the audio thread and offline.
    /// Signal flow: layers -> dry / near / far / low / echo buses -> far-field absorption, reverb, cliff echoes
    /// -> master gain -> 20 Hz high-pass -> soft clipper (knee -6 dBFS) -> look-ahead limiter (-1 dBFS).
    /// </summary>
    public sealed partial class SoundScore
    {
        public const int Block = 64;                                     // control period in samples (1.3 ms at 48 kHz)

        // layer ids for Trim
        public const int LGulls = 0, LLap = 1, LBell = 2, LWind = 3, LHum = 4, LTremor = 5, LRattle = 6, LRocks = 7, LThump = 8,
                         LRoar = 9, LThunder = 10, LAsh = 11, LCollapse = 12, LSuck = 13, LHarbour = 14, LDrone = 15, LWave = 16,
                         LImpact = 17, LAfter = 18, LSteam = 19, LEnding = 20, LSteps = 21, LayerCount = 22;

        /// <summary>Per-layer linear trim, 1 = as designed. Set from the main thread before or between renders.</summary>
        public readonly float[] Trim = new float[LayerCount];
        public float MasterGain = 1f;
        /// <summary>Largest sample that entered the limiter since Reset (diagnostics).</summary>
        public float PeakIn;
        public float LimiterMinGain { get { return lim.MinGain; } }

        readonly int fs;
        readonly float invFs, ctlDt;
        readonly float[] dryL, dryR, nearL, nearR, farL, farR, low, echoIn, tA, tB, tC, rvL, rvR, outL, outR;
        readonly Reverb rev; readonly EchoTaps echo; readonly Limiter lim;
        readonly int limLat, seekInLen;
        Svf muffL, muffR; Biquad hpL, hpR; Ramp gMaster;
        float muffSet, revSendNear, revSendFar;

        // clock state
        double T, prevT, expectT;
        bool started, seekPending, advancing;
        int ctlLeft, seekInPos, seekOutPos;

        // seconds since each event (large negative before it)
        float eGulls, eHum, eT1, eT2, eVista, eEr, eT3, eCol, eDraw, eWave, eStorm, eG1, eG2, eSil, eArr;
        float eThump, eColSnd, eBoom, eImp, eImp2;
        // absolute start times (story seconds, double) for exact trigger scheduling; about 1e9 when not yet started
        double sT1, sT2, sT3, sThump, sDraw, sImp, sSil;
        // shared staging values
        float tremor, bedDip, impDip, endFade, storm, glimpse, silence, hush;

        public SoundScore(int sampleRate)
        {
            fs = sampleRate < 8000 ? 48000 : sampleRate;
            invFs = 1f / fs; ctlDt = Block * invFs;
            dryL = new float[Block]; dryR = new float[Block]; nearL = new float[Block]; nearR = new float[Block];
            farL = new float[Block]; farR = new float[Block]; low = new float[Block]; echoIn = new float[Block];
            tA = new float[Block]; tB = new float[Block]; tC = new float[Block];
            rvL = new float[Block]; rvR = new float[Block]; outL = new float[Block]; outR = new float[Block];
            rev = new Reverb(fs); echo = new EchoTaps(fs); lim = new Limiter(fs, Dsp.Ceiling);
            limLat = lim.Latency; seekInLen = (int)(0.04f * fs);
            for (int i = 0; i < LayerCount; i++) Trim[i] = 1f;
            InitAmbience(); InitTremor(); InitEruption(); InitWater(); InitStorm(); InitSteps();
            Reset();
        }

        public int SampleRate { get { return fs; } }

        /// <summary>Back to the state of a new instance (same seeds): two renders of the same clock are identical.</summary>
        public void Reset()
        {
            rev.Clear(); echo.Clear(); lim.Clear();
            muffL.Clear(); muffR.Clear(); muffSet = 0f;
            hpL.Clear(); hpR.Clear(); hpL.HighPass(fs, 20.0, 0.707); hpR.HighPass(fs, 20.0, 0.707);
            gMaster.Snap(0f);
            started = false; seekPending = true; ctlLeft = 0; seekInPos = 0; seekOutPos = 0;
            T = prevT = expectT = 0.0; PeakIn = 0f;
            ResetAmbience(); ResetTremor(); ResetEruption(); ResetWater(); ResetStorm(); ResetSteps();
        }

        /// <summary>
        /// Fills 'interleaved' with frames x channels samples and advances clock.storyTime by dtPerSample per sample.
        /// A jump of the story time is treated as a seek: sustained layers continue from the new time, past one-shots stay silent.
        /// </summary>
        public void Render(float[] interleaved, int frames, int channels, ref ScoreClock clock)
        {
            if (frames <= 0 || channels <= 0) return;
            if (!started || Math.Abs(clock.storyTime - expectT) > 0.05) BeginSeek();
            double dt = clock.dtPerSample < 0.0 ? 0.0 : clock.dtPerSample;
            int done = 0;
            while (done < frames)
            {
                if (ctlLeft <= 0) { Control(ref clock); ctlLeft = Block; }
                int n = frames - done;
                if (n > ctlLeft) n = ctlLeft;
                RunBlock(n);
                int o = done * channels;
                if (channels == 1)
                    for (int s = 0; s < n; s++) interleaved[o + s] = 0.5f * (outL[s] + outR[s]);
                else
                    for (int s = 0; s < n; s++)
                    {
                        interleaved[o] = outL[s]; interleaved[o + 1] = outR[s];
                        for (int ch = 2; ch < channels; ch++) interleaved[o + ch] = 0f;
                        o += channels;
                    }
                ctlLeft -= n; done += n;
                clock.storyTime += dt * n;
            }
            expectT = clock.storyTime;
        }

        void BeginSeek()
        {
            // the 5 ms still inside the limiter fade out, the new material fades in: a seek never clicks
            started = true; seekPending = true; ctlLeft = 0;
            seekInPos = 0; seekOutPos = 0;
            rev.Clear(); echo.Clear();
            KillAmbienceVoices(); KillTremorVoices(); KillWaterVoices(); KillStormVoices(); KillStepVoices();
        }

        float Rel(float te) { return te >= 5e8f ? -1e6f : (float)(T - te); }

        void Control(ref ScoreClock c)
        {
            T = c.storyTime;
            if (seekPending) prevT = T;
            advancing = T > prevT;
            eGulls = Rel(c.tGulls); eHum = Rel(c.tHum); eT1 = Rel(c.tTremor1); eT2 = Rel(c.tTremor2); eVista = Rel(c.tVista);
            eEr = Rel(c.tEruption); eT3 = Rel(c.tTremor3); eCol = Rel(c.tCollapse); eDraw = Rel(c.tDrawdown); eWave = Rel(c.tWave);
            eStorm = Rel(c.tAshStorm); eG1 = Rel(c.tGlimpse1); eG2 = Rel(c.tGlimpse2); eSil = Rel(c.tSilence); eArr = Rel(c.tArrival);
            eThump = eEr - Config.ShockDelay;
            eColSnd = eCol - Config.CollapseSoundDelay;
            eBoom = eColSnd - Config.SplashAt;
            eImp = eWave - Config.WaveCross - Config.ImpactSoundDelay;
            eImp2 = eImp - Config.WaveSecondDelay;
            sT1 = c.tTremor1; sT2 = c.tTremor2; sT3 = c.tTremor3; sDraw = c.tDrawdown; sSil = c.tSilence;
            sThump = (double)c.tEruption + Config.ShockDelay;
            sImp = (double)c.tWave + Config.WaveCross + Config.ImpactSoundDelay;

            CtlGlobals();
            CtlAmbience(); CtlTremor(); CtlEruption(); CtlWater(); CtlStorm(); CtlSteps(ref c);
            seekPending = false;
            prevT = T;
        }

        static float Glimpse(float t)
        {
            if (t <= 0f || t >= Config.GlimpseDuration) return 0f;
            return Dsp.Smooth(0f, 4.5f, t) * (1f - Dsp.Smooth(Config.GlimpseDuration - 5f, Config.GlimpseDuration, t));
        }

        void CtlGlobals()
        {
            storm = Dsp.Smooth(0f, Config.StormCloseIn, eStorm);
            glimpse = Math.Max(Glimpse(eG1), Glimpse(eG2));
            silence = Dsp.Smooth(0f, 15f, eSil);
            endFade = 1f - 0.92f * Dsp.Smooth(0f, 10f, eArr);                       // near silence, never zero
            // the hush of the drawdown: from tDrawdown + 8 s until the wave is close
            hush = Dsp.Smooth(0f, 8f, eDraw) * (1f - Dsp.Smooth(55f, 85f, eWave));

            // the picture is silent for ShockDelay: the bed holds its breath, dips in the last 2 s, returns after the thump
            float hold = 1f - 0.16f * Dsp.Smooth(0f, 4f, eEr) * (1f - Dsp.Smooth(0f, 2f, eThump));
            float dip = eThump < 0f ? 1f - 0.5f * Dsp.Smooth(-2.1f, -0.4f, eThump) : 0.5f + 0.5f * Dsp.Smooth(0f, 1.5f, eThump);
            bedDip = hold * dip;
            // 0.4 s of withdrawal before the wave impact (no jump scare)
            impDip = eImp < 0f ? 1f - 0.8f * Dsp.Smooth(-0.5f, -0.18f, eImp) : 0.2f + 0.8f * Dsp.Smooth(0f, 0.15f, eImp);

            tremor = Math.Max(Math.Max(0.35f * Dsp.Swell(eT1, 4f), 0.7f * Dsp.Swell(eT2, 8f)), Dsp.Swell(eT3, 7f));

            // far-field absorption: clear air 16 kHz; ash muffles like snow (2.6 kHz), clearings open it; the thump presses on the ears
            float muff = 16000f * Dsp.Exp(-1.82f * storm * (1f - 0.6f * glimpse) * (1f - 0.35f * Dsp.Smooth(0f, 30f, eSil)));
            float press = Dsp.Hit(eThump, 0.05f, 2.2f);
            muff *= 1f - 0.85f * press;
            if (Math.Abs(muff - muffSet) > 0.01f * muffSet)
            {
                muffSet = muff;
                muffL.Set(fs, muff, 0.707f); muffR.Set(fs, muff, 0.707f);
            }
            // the space: open caldera air in the morning, short dull tails inside the ash
            rev.Set(Dsp.Lerp(2.7f, 1.1f, storm * (1f - 0.5f * silence)), Dsp.Lerp(4200f, 1700f, storm));
            revSendNear = 0.09f; revSendFar = 0.22f;

            gMaster.To(MasterGain * endFade, Block);
        }

        void RunBlock(int n)
        {
            for (int s = 0; s < n; s++)
            {
                dryL[s] = 0f; dryR[s] = 0f; nearL[s] = 0f; nearR[s] = 0f; farL[s] = 0f; farR[s] = 0f; low[s] = 0f; echoIn[s] = 0f;
            }
            RunAmbience(n); RunTremor(n); RunEruption(n); RunWater(n); RunStorm(n); RunSteps(n);

            for (int s = 0; s < n; s++)
            {
                farL[s] = muffL.Lp(farL[s]); farR[s] = muffR.Lp(farR[s]);
                rvL[s] = nearL[s] * revSendNear + farL[s] * revSendFar;
                rvR[s] = nearR[s] * revSendNear + farR[s] * revSendFar;
            }
            rev.Process(rvL, rvR, dryL, dryR, n, 0.55f);
            echo.Process(echoIn, dryL, dryR, n, 1f);

            for (int s = 0; s < n; s++)
            {
                float g = gMaster.Next();
                if (seekInPos < seekInLen) { g *= seekInPos / (float)seekInLen; seekInPos++; }
                float l = hpL.Next((dryL[s] + nearL[s] + farL[s] + low[s]) * g);
                float r = hpR.Next((dryR[s] + nearR[s] + farR[s] + low[s]) * g);
                float a = l < 0f ? -l : l, b = r < 0f ? -r : r;
                if (a > PeakIn) PeakIn = a;
                if (b > PeakIn) PeakIn = b;
                if (a > ClipKnee) l = SoftClip(l);
                if (b > ClipKnee) r = SoftClip(r);
                lim.Process(ref l, ref r);
                if (seekOutPos < limLat) { float k = 1f - (seekOutPos + 1) / (float)limLat; l *= k; r *= k; seekOutPos++; }
                outL[s] = l; outR[s] = r;
            }
        }

        const float ClipKnee = 0.5f, ClipTop = 0.94f;

        /// <summary>
        /// Transparent below -6 dBFS; above it the wave bends towards 0.94. The big hits (thump, boom, impact) keep their
        /// weight instead of being turned down by the limiter, and their sub-bass gains harmonics that small speakers carry.
        /// </summary>
        static float SoftClip(float x)
        {
            const float r = ClipTop - ClipKnee;
            float a = x < 0f ? -x : x;
            float y = ClipKnee + r * Dsp.Tanh((a - ClipKnee) / r);
            return x < 0f ? -y : y;
        }

        // ---- small helpers shared by the layer files ----

        /// <summary>True when the scheduled time 'at' (story seconds) was passed during the last control period.</summary>
        bool Fire(double at) { return advancing && prevT < at && at <= T; }

        /// <summary>Gain that makes one unit impulse through a 0 dB band-pass ring at about unit amplitude.</summary>
        float ImpulseGain(float hz, float q) { return q * fs / ((float)Dsp.Tau * hz); }
    }
}
