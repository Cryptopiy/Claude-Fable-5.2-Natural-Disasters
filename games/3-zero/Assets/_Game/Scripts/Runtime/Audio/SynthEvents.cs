using System;

namespace ZERO
{
    /// <summary>The blast wave strikes the airframe: a body blow. A pressure slam (52 Hz sinking to 28 Hz, 30 ms
    /// rise), a broadband blast whose top closes within a second, a long pressure rumble. The slam is mono (pressure
    /// is felt in the head, not located); the blast is wide and comes from the window side first.</summary>
    internal sealed class WaveHit
    {
        const float BodyGain = 0.55f, BlastGain = 0.26f, PressGain = 1.1f, SlapGain = 0.6f;

        readonly float fs;
        NoiseGen nL, nR, nC;
        Env body, blast, press, slap;
        Svf blL, blR, lpP, lpS;
        float phase, fe, t;
        readonly float feDecay;

        public bool Active { get { return body.Active || blast.Active || press.Active || slap.Active; } }

        public WaveHit(int sampleRate, uint seed)
        {
            fs = sampleRate;
            nL = new NoiseGen(seed); nR = new NoiseGen(seed + 1u); nC = new NoiseGen(seed + 2u);
            lpP.Set(140f, 0.7f, fs); lpS.Set(500f, 0.7f, fs);
            blL.Set(9000f, 0.7f, fs); blR.Set(9400f, 0.7f, fs);
            feDecay = Dsp.Decay(0.30f, fs);
        }

        public void Trigger()
        {
            body.Trigger(0.030f, 0.9f, 6f, fs);
            blast.Trigger(0.020f, 0.30f, 2.6f, fs);
            press.Trigger(0.10f, 1.5f, 7f, fs);
            slap.Trigger(0.012f, 0.14f, 1.4f, fs);
            phase = 0f; fe = 1f; t = 0f;
            lpP.Reset(); lpS.Reset(); blL.Reset(); blR.Reset();
            nL.Reset(); nR.Reset(); nC.Reset();
            Control(0f);
        }

        public void Stop() { body.Stop(); blast.Stop(); press.Stop(); slap.Stop(); }

        /// <summary>The top of the blast closes: 9 kHz down to 350 Hz.</summary>
        public void Control(float dt)
        {
            t += dt;
            float c = 350f + 8650f * (float)Math.Exp(-t / 0.35f);
            blL.Set(c, 0.7f, fs); blR.Set(c * 1.05f, 0.7f, fs);
        }

        /// <summary>Adds the blow to l/r.</summary>
        public void Process(float[] l, float[] r, int n, float gain)
        {
            for (int i = 0; i < n; i++)
            {
                float eb = body.Tick(), ex = blast.Tick(), ep = press.Tick(), es = slap.Tick();
                phase += (28f + 24f * fe) / fs;
                if (phase >= 1f) phase -= 1f;
                fe *= feDecay;
                float m = (Dsp.Sin01(phase) + 0.3f * Dsp.Sin01(phase * 2f)) * eb * BodyGain
                        + lpP.Lp(nC.Brown()) * ep * PressGain
                        + lpS.Lp(nC.Pink()) * es * SlapGain;
                float a = blL.Lp(nL.White() * 0.6f + nL.Pink() * 2.5f) * ex * BlastGain * 1.15f;
                float b = blR.Lp(nR.White() * 0.6f + nR.Pink() * 2.5f) * ex * BlastGain * 0.85f;
                l[i] += (m + a) * gain; r[i] += (m + b) * gain;
            }
            lpP.Zap(); lpS.Zap(); blL.Zap(); blR.Zap();
        }
    }

    /// <summary>The roar of the hit: after the breath the sound comes back as one tearing rush. Pitch ramps from deep
    /// to true over 0.4 s, level ramps in over 0.35 s (never a step). A broadband rush (noise through a low-pass
    /// sweeping 200 Hz to 12 kHz), the groan of the airframe sliding up 3-4 octaves, a rising sub, a low roar,
    /// everything loose in the cabin rattling. It keeps roaring until it is stopped (the white).</summary>
    internal sealed class HitRoar
    {
        const int NR = 6;
        const float Gain = 0.34f, RushGain = 1.5f, GroanGain = 0.55f, RattleGain = 0.55f;
        static readonly float[] RattleHz = { 640f, 1150f, 1900f, 2800f, 4300f, 6100f };
        static readonly float[] RattleTau = { 0.020f, 0.014f, 0.009f, 0.007f, 0.006f, 0.010f };

        readonly float fs, invFs;
        readonly GroanBank groan;
        NoiseGen nL, nR, nRoar, nDust;
        Rng rng;
        Svf lpL, lpR, clat;
        Biquad roarLp, roarHp;
        readonly Ping[] rattle = new Ping[NR];
        readonly float[] gl, gr;
        float t, subPhase, tremPhase, grain;
        float u, subInc, tremInc, dustProb;
        readonly float grainDecay;
        public bool Active;

        public HitRoar(int sampleRate, uint seed, int block, GroanBank sharedGroan)
        {
            fs = sampleRate; invFs = 1f / fs;
            groan = sharedGroan;
            nL = new NoiseGen(seed + 1u); nR = new NoiseGen(seed + 2u);
            nRoar = new NoiseGen(seed + 3u); nDust = new NoiseGen(seed + 4u);
            rng = new Rng(seed + 5u);
            roarLp.SetLowpass(170f, 0.707f, fs); roarHp.SetHighpass(32f, 0.707f, fs);
            clat.Set(3200f, 1.2f, fs);
            for (int k = 0; k < NR; k++) rattle[k].Set(RattleHz[k], RattleTau[k], fs);
            grainDecay = Dsp.Decay(0.0006f, fs);
            gl = new float[block]; gr = new float[block];
        }

        public void Start()
        {
            Active = true; t = 0f; subPhase = 0f; tremPhase = 0f; grain = 0f;
            lpL.Reset(); lpR.Reset(); clat.Reset(); roarLp.Reset(); roarHp.Reset();
            nL.Reset(); nR.Reset(); nRoar.Reset();
            for (int k = 0; k < NR; k++) rattle[k].Reset();
            groan.Reset();
            Control(0f);
        }

        public void Stop()
        {
            Active = false;
            for (int k = 0; k < NR; k++) rattle[k].Reset();
        }

        public void Control(float dt)
        {
            u = Dsp.Smooth01(t / 0.4f);                               // pitch: deep to true over 0.4 s
            float cut = 200f * (float)Math.Pow(60.0, u);              // 200 Hz -> 12 kHz
            lpL.Set(cut, 1.1f, fs); lpR.Set(cut * 1.04f, 1.1f, fs);
            subInc = (24f + 34f * u) / fs;                            // rising sub, 24 -> 58 Hz
            tremInc = (0.5f + 25f * u) / fs;                          // the shudder runs away
            dustProb = (12f + 260f * u * u) / fs;                     // the rattle thickens
            groan.Control(dt * (1f + 40f * u), 1f, u);
            for (int k = 0; k < NR; k++) rattle[k].Zap();
        }

        /// <summary>Adds the roar to l/r.</summary>
        public void Process(float[] l, float[] r, int n)
        {
            for (int i = 0; i < n; i++) { gl[i] = 0f; gr[i] = 0f; }
            groan.Process(gl, gr, n, GroanGain);
            for (int i = 0; i < n; i++)
            {
                float x = t / 0.35f;
                float lvl = x >= 1f ? 1f : x * x * (3f - 2f * x);
                if (t > 0.4f) lvl *= 1f + 0.3f * Dsp.Smooth01((t - 0.4f) / 0.8f);
                t += invFs;

                float rl = lpL.Lp(nL.White() * 0.30f + nL.Pink() * 1.6f) * RushGain;
                float rr = lpR.Lp(nR.White() * 0.30f + nR.Pink() * 1.6f) * RushGain;

                subPhase += subInc; if (subPhase >= 1f) subPhase -= 1f;
                float lowS = Dsp.Sin01(subPhase) * 0.50f + Dsp.Sin01(subPhase * 2f) * 0.14f
                           + roarLp.Tick(roarHp.Tick(nRoar.Brown())) * 2.0f;

                tremPhase += tremInc; if (tremPhase >= 1f) tremPhase -= 1f;
                float trem = 1f - 0.28f * u * (0.5f + 0.5f * Dsp.Sin01(tremPhase));

                if (nDust.Uniform() < dustProb)
                {
                    int k = (int)(rng.F() * NR); if (k >= NR) k = NR - 1;
                    float a = rng.F();
                    rattle[k].Hit(0.05f + 0.30f * a * a);
                    grain += 0.6f * a;
                }
                grain *= grainDecay; if (grain < 1e-7f) grain = 0f;
                float rt = clat.Bp(nDust.White() * (grain + 1e-9f));
                for (int k = 0; k < NR; k++) if (rattle[k].Active) rt += rattle[k].Tick();
                rt *= RattleGain;

                float g = lvl * Gain;
                l[i] += ((rl + lowS) * trem + rt + gl[i]) * g;
                r[i] += ((rr + lowS) * trem + rt + gr[i]) * g;
            }
        }
    }

    /// <summary>A thin ringing in the ears (after the light of the contact, and alone in the breath before the hit):
    /// 7.2 kHz, a partner 0.31 Hz above it for a slow beat that alternates between the ears, and a faint 7.26 kHz
    /// shimmer.</summary>
    internal sealed class RingTone
    {
        double p1, p2, p3;
        readonly double i1, i2, i3;
        readonly float coef;
        float amp;

        public RingTone(int sampleRate)
        {
            i1 = 7200.0 / sampleRate; i2 = 7200.31 / sampleRate; i3 = 7260.0 / sampleRate;
            coef = Dsp.TauCoef(0.05f, sampleRate);
        }

        public bool Sounding { get { return amp >= 1e-7f; } }
        public void Reset() { amp = 0f; }

        /// <summary>Adds the ring to l/r; target is the linear peak level.</summary>
        public void Process(float[] l, float[] r, int n, float target)
        {
            if (target <= 0f && amp < 1e-7f) { amp = 0f; return; }
            for (int i = 0; i < n; i++)
            {
                amp += coef * (target - amp);
                float a = Dsp.Sin01(p1) * 0.60f, b = Dsp.Sin01(p2) * 0.30f, c = Dsp.Sin01(p3) * 0.14f;
                p1 += i1; if (p1 >= 1.0) p1 -= 1.0;
                p2 += i2; if (p2 >= 1.0) p2 -= 1.0;
                p3 += i3; if (p3 >= 1.0) p3 -= 1.0;
                l[i] += amp * (a + b + c);
                r[i] += amp * (a - b + c);
            }
        }
    }

    /// <summary>The ring left after the white: 7.2 kHz at -40 dBFS, decaying to nothing over 6 s. Mono.</summary>
    internal sealed class EndRing
    {
        const float Level = 0.01f, Length = 6f;
        readonly float invFs;
        readonly int killSamples;
        readonly double i1, i2;
        double p1, p2;
        float t;
        Ramp kill;
        public bool Active;

        public EndRing(int sampleRate)
        {
            invFs = 1f / sampleRate;
            killSamples = (int)(0.05f * sampleRate);
            i1 = 7200.0 / sampleRate; i2 = 7200.31 / sampleRate;
        }

        public void Start() { Active = true; t = 0f; p1 = 0.0; p2 = 0.0; kill.Jump(1f); }
        /// <summary>Fade out over 50 ms (entering the black).</summary>
        public void Release() { if (Active) kill.To(0f, killSamples); }
        public void Stop() { Active = false; }

        public float Tick()
        {
            float x = t / Length;
            float k = kill.Next();
            if (x >= 1f || (k <= 0f && kill.Done)) { Active = false; return 0f; }
            float env = (1f - x) * (1f - x) * Dsp.Smooth01(t / 0.03f) * Level * k;
            t += invFs;
            float v = Dsp.Sin01(p1) * 0.72f + Dsp.Sin01(p2) * 0.28f;
            p1 += i1; if (p1 >= 1.0) p1 -= 1.0;
            p2 += i2; if (p2 >= 1.0) p2 -= 1.0;
            return v * env;
        }
    }
}
