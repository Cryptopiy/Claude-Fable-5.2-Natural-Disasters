using System;

namespace ZERO
{
    /// <summary>The airframe under stress: a metallic sustained groan of seven inharmonic low partials (50-190 Hz),
    /// each a pair of sines a fraction of a hertz apart (very slow beating that drifts across the stereo field),
    /// gently saturated, plus a little friction noise, through three slowly wandering resonant filters.
    /// When the wave hits, the partials slide up 3-4 octaves: metal tearing.</summary>
    internal sealed class GroanBank
    {
        public const int N = 7;
        const float Norm = 0.34f, Drive = 0.75f;
        static readonly float[] BaseHz = { 51.3f, 66.7f, 83.1f, 104.9f, 127.4f, 158.8f, 187.6f };
        static readonly float[] Detune = { 0.11f, 0.07f, 0.17f, 0.23f, 0.13f, 0.19f, 0.29f };
        static readonly float[] Weight = { 1.00f, 0.90f, 0.78f, 0.62f, 0.50f, 0.40f, 0.30f };
        static readonly float[] Slide = { 3.9f, 3.3f, 3.7f, 3.1f, 3.5f, 3.0f, 3.4f };      // octaves, when the wave hits
        static readonly float[] PanA = { -0.5f, 0.6f, -0.3f, 0.7f, -0.7f, 0.4f, -0.2f };

        readonly float fs;
        readonly double[] phA = new double[N], phB = new double[N], incA = new double[N], incB = new double[N];
        readonly float[] amp = new float[N], ampT = new float[N];
        readonly float[] gAl = new float[N], gAr = new float[N], gBl = new float[N], gBr = new float[N];
        readonly SlowRandom[] wander = new SlowRandom[N];
        SlowRandom res1, res2, res3, fric;
        NoiseGen noiseL, noiseR;
        Svf r1L, r1R, r2L, r2R, r3L, r3R;
        float fricAmp, fricT, gain;
        readonly float ampCoef, gainCoef;
        readonly float[] tl, tr;

        public GroanBank(int sampleRate, uint seed, int block)
        {
            fs = sampleRate;
            var rng = new Rng(seed);
            for (int k = 0; k < N; k++)
            {
                phA[k] = rng.F(); phB[k] = rng.F();
                wander[k] = new SlowRandom(seed + 11u * (uint)(k + 1), 9f, 25f);
                Dsp.Pan(PanA[k], out gAl[k], out gAr[k]);
                Dsp.Pan(-PanA[k], out gBl[k], out gBr[k]);
            }
            res1 = new SlowRandom(seed + 101u, 14f, 30f);
            res2 = new SlowRandom(seed + 102u, 11f, 26f);
            res3 = new SlowRandom(seed + 106u, 8f, 19f);
            fric = new SlowRandom(seed + 103u, 5f, 13f);
            noiseL = new NoiseGen(seed + 104u); noiseR = new NoiseGen(seed + 105u);
            ampCoef = Dsp.TauCoef(0.05f, fs);
            gainCoef = Dsp.TauCoef(0.03f, fs);
            tl = new float[block]; tr = new float[block];
            Control(0f, 1f, 0f);
        }

        public void Reset()
        {
            r1L.Reset(); r1R.Reset(); r2L.Reset(); r2R.Reset(); r3L.Reset(); r3R.Reset();
            noiseL.Reset(); noiseR.Reset();
            gain = 0f;
        }

        /// <param name="dt">seconds</param>
        /// <param name="pitch">pitch multiplier of all partials</param>
        /// <param name="slide">0..1: how far the partials have slid up (the hit)</param>
        public void Control(float dt, float pitch, float slide)
        {
            float top = 1f;
            for (int k = 0; k < N; k++)
            {
                float m = pitch;
                if (slide > 0f) m *= Dsp.Pow2(Slide[k] * slide);
                incA[k] = BaseHz[k] * m / fs;
                incB[k] = (BaseHz[k] + Detune[k]) * m / fs;
                ampT[k] = Weight[k] * (0.25f + 0.75f * wander[k].Next(dt));
                if (m > top) top = m;
            }
            float c1 = (62f + 50f * res1.Next(dt)) * top;
            float c2 = (112f + 70f * res2.Next(dt)) * top;
            r1L.Set(c1, 5f, fs); r1R.Set(c1 * 1.03f, 5f, fs);
            r2L.Set(c2, 4.5f, fs); r2R.Set(c2 * 0.97f, 4.5f, fs);
            float c3 = (230f + 170f * res3.Next(dt)) * top;          // a higher overtone that sings and wanders
            r3L.Set(c3, 7f, fs); r3R.Set(c3 * 1.05f, 7f, fs);
            fricT = 0.25f + 0.75f * fric.Next(dt);
        }

        /// <summary>Adds the groan to l/r; targetGain is the linear level (about the peak).</summary>
        public void Process(float[] l, float[] r, int n, float targetGain)
        {
            if (targetGain <= 0f && gain < 1e-7f) { gain = 0f; return; }
            for (int i = 0; i < n; i++) { tl[i] = 0f; tr[i] = 0f; }
            for (int k = 0; k < N; k++)
            {
                double pa = phA[k], pb = phB[k], ia = incA[k], ib = incB[k];
                float a = amp[k], at = ampT[k];
                float al = gAl[k], ar = gAr[k], bl = gBl[k], br = gBr[k];
                for (int i = 0; i < n; i++)
                {
                    a += ampCoef * (at - a);
                    float sa = Dsp.Sin01(pa) * a, sb = Dsp.Sin01(pb) * a;
                    tl[i] += sa * al + sb * bl;
                    tr[i] += sa * ar + sb * br;
                    pa += ia; if (pa >= 1.0) pa -= 1.0;
                    pb += ib; if (pb >= 1.0) pb -= 1.0;
                }
                phA[k] = pa; phB[k] = pb; amp[k] = a;
            }
            float ft = fricT * 0.9f;
            for (int i = 0; i < n; i++)
            {
                gain += gainCoef * (targetGain - gain);
                fricAmp += ampCoef * (ft - fricAmp);
                // soft saturation: sum and difference tones of the inharmonic partials give the metal its grain
                float sl = tl[i] * Drive, sr = tr[i] * Drive;
                if (sl > 1f) sl = 1f; else if (sl < -1f) sl = -1f;
                if (sr > 1f) sr = 1f; else if (sr < -1f) sr = -1f;
                sl = (1.5f * sl - 0.5f * sl * sl * sl) * (1f / Drive);
                sr = (1.5f * sr - 0.5f * sr * sr * sr) * (1f / Drive);
                float xl = sl + noiseL.Brown() * fricAmp;
                float xr = sr + noiseR.Brown() * fricAmp;
                float g = gain * Norm;
                l[i] += (0.55f * sl + 0.9f * r1L.Bp(xl) + 0.7f * r2L.Bp(xl) + 0.9f * r3L.Bp(xl)) * g;
                r[i] += (0.55f * sr + 0.9f * r1R.Bp(xr) + 0.7f * r2R.Bp(xr) + 0.9f * r3R.Bp(xr)) * g;
            }
        }
    }

    /// <summary>One discrete sound heard in the cabin (see <see cref="AudioEventKind"/>), synthesized from sines, noise
    /// and resonators: the chime, a boom of re-entering ejecta felt through the fuselage, a creak of the airframe,
    /// a rattle of loose things, the cockpit alarm behind its door. It sounds at once; the distance only makes it
    /// quieter and duller. A voice that is stolen fades out in 6 ms and then starts the new sound: never a click.</summary>
    internal sealed class EventVoice
    {
        public const int Kinds = 5;
        const int NP = 3, NO = 3;
        // Levels: the peak of each kind at zero distance and size 1 is about its gain (measured offline).
        const float ChimeGain = 0.14f, BoomGain = 0.23f, CreakGain = 0.22f, RattleGain = 0.22f, AlarmGain = 0.05f;
        // internal balances that bring the raw recipes to a peak of about 1
        const float ChimeMix = 0.80f, BoomMix = 0.47f, CreakRes = 25f, RattleMix = 1.6f, AlarmMix = 1.1f;
        const float ChimeHi = 698.46f, ChimeLo = 554.37f, ChimeGap = 0.42f;
        const float AlarmA = 960f, AlarmB = 1280f, BeepPeriod = 0.30f, BeepOn = 0.17f;

        readonly float fs, invFs, sm, gDecay;
        Rng rng;
        NoiseGen nz, nz2;
        Svf fa, fb, fc, dl;
        readonly Ping[] pg = new Ping[NP];
        readonly float[] oph = new float[NO], orate = new float[NO], onext = new float[NO], ow = new float[NO];
        readonly float[] mid, low;
        Ramp kill;
        readonly int killSamples;

        public bool Active;
        public int Kind;
        float t, dur, size, amp, gl, gr, send;
        bool dying, hasNext, fired1;
        int nKind, beeps;
        float nDist, nSize, nPan;
        float ph1, ph2, next1, e1, e2, e3, e4, g1, g2, bend1, bend2, grain, exc, p1, p2, p3, p4, d1, d2, d3, d4;

        public EventVoice(int sampleRate, uint seed, int block)
        {
            fs = sampleRate; invFs = 1f / fs;
            rng = new Rng(seed);
            nz = new NoiseGen(seed + 1u); nz2 = new NoiseGen(seed + 2u);
            mid = new float[block]; low = new float[block];
            sm = Dsp.TauCoef(0.004f, fs);
            gDecay = Dsp.Decay(0.0015f, fs);
            killSamples = Math.Max(8, (int)(0.006f * fs));
            kill.Jump(1f);
        }

        /// <summary>How much would be lost by stealing this voice (0 = free).</summary>
        public float Priority { get { return !Active ? 0f : (hasNext ? float.MaxValue : amp * (1f - t / dur)); } }

        public void Reset() { Active = false; dying = false; hasNext = false; kill.Jump(1f); }

        /// <summary>Fade out in 6 ms, then start this sound.</summary>
        public void Steal(int kind, float dist, float sz, float pan)
        {
            nKind = kind; nDist = dist; nSize = sz; nPan = pan; hasNext = true;
            if (!dying) { dying = true; kill.To(0f, killSamples); }
        }

        public void Start(int kind, float dist, float sz, float pan)
        {
            Kind = kind; Active = true; dying = false; hasNext = false;
            kill.Jump(1f);
            t = 0f;
            size = Dsp.Clamp(sz, 0.05f, 1f);
            float d = dist > 0f ? dist : 0f;
            Dsp.Pan(Dsp.Clamp(pan, -1f, 1f) * 0.9f, out gl, out gr);
            gl *= 1.4142f; gr *= 1.4142f;
            fa.Reset(); fb.Reset(); fc.Reset(); dl.Reset();
            for (int i = 0; i < NP; i++) pg[i].Reset();
            nz.Reset(); nz2.Reset();
            ph1 = 0f; ph2 = 0f; next1 = 1f; e1 = 0f; e2 = 0f; e3 = 0f; e4 = 0f; g1 = 0f; g2 = 0f;
            bend1 = 0f; bend2 = 0f; grain = 0f; exc = 0f; fired1 = false;
            if (kind == 1)
            {
                // kilometres away, heard through the hull: the distance takes the top off and a little of the level
                float dk = d * 0.001f;
                dl.Set(Dsp.Clamp(600f / (1f + dk / 5f), 50f, 600f), 0.7f, fs);
                send = 0.05f;
                StartBoom(dk);
            }
            else
            {
                // in the cabin, metres away
                float att = 1f / (1f + d / 15f);
                dl.Set(Dsp.Clamp(16000f / (1f + d / 25f), 1000f, 16000f), 0.6f, fs);
                send = 0.20f + 0.25f * d / (d + 8f);
                if (kind == 0) StartChime(att);
                else if (kind == 2) StartCreak(att);
                else if (kind == 3) StartRattle(att);
                else StartAlarm(att);
            }
        }

        /// <summary>Adds the voice to l/r and its send for the cabin reverb to sl/sr.</summary>
        public void Process(float[] l, float[] r, float[] sl, float[] sr, int n, float trim)
        {
            if (!Active) return;
            float t0 = t;
            switch (Kind)
            {
                case 0: Chime(n); break;
                case 1: Boom(n); break;
                case 2: Creak(n); break;
                case 3: Rattle(n); break;
                default: Alarm(n); break;
            }
            float a = amp * trim, sd = send, left = dur - t0;
            for (int i = 0; i < n; i++)
            {
                float kg = kill.Next();
                float tail = (left - i * invFs) * 20f;               // the last 50 ms always fade to nothing
                if (tail < 1f) kg *= tail > 0f ? tail : 0f;
                float y = (dl.Lp(mid[i]) + low[i]) * a * kg;
                float x = y * gl; y *= gr;
                l[i] += x; r[i] += y;
                sl[i] += x * sd; sr[i] += y * sd;
            }
            fa.Zap(); fb.Zap(); fc.Zap(); dl.Zap();
            for (int i = 0; i < NP; i++) pg[i].Zap();
            if (t >= dur || (dying && kill.Done)) Active = false;
            if (!Active && hasNext) { hasNext = false; Start(nKind, nDist, nSize, nPan); }
        }

        // ---------------------------------------------------------------- Chime: one soft two-tone ding, high then low
        void StartChime(float att)
        {
            dur = 2.6f;
            amp = att * ChimeGain * (0.5f + 0.5f * size);
            d1 = Dsp.Decay(0.55f, fs);
        }

        void Chime(int n)
        {
            for (int i = 0; i < n; i++)
            {
                float a = t < 0.005f ? 0.5f - 0.5f * Dsp.Sin01(t * (0.5f / 0.005f) + 0.25f) : 1f;
                if (t < 0.005f) e1 = 1f; else e1 *= d1;
                ph1 += ChimeHi * invFs; if (ph1 >= 1f) ph1 -= 1f;
                float y = (Dsp.Sin01(ph1) + 0.20f * Dsp.Sin01(ph1 * 2f) + 0.06f * Dsp.Sin01(ph1 * 3f)) * e1 * a;
                float u = t - ChimeGap;
                if (u >= 0f)
                {
                    float b = u < 0.005f ? 0.5f - 0.5f * Dsp.Sin01(u * (0.5f / 0.005f) + 0.25f) : 1f;
                    if (u < 0.005f) e2 = 1f; else e2 *= d1;
                    ph2 += ChimeLo * invFs; if (ph2 >= 1f) ph2 -= 1f;
                    y += (Dsp.Sin01(ph2) + 0.20f * Dsp.Sin01(ph2 * 2f) + 0.06f * Dsp.Sin01(ph2 * 3f)) * e2 * b * 0.9f;
                }
                mid[i] = y * ChimeMix;
                low[i] = 0f;
                t += invFs;
            }
        }

        // ---------------------------------------------------------------- Boom: a double thump through the fuselage, a rolling tail
        void StartBoom(float dk)
        {
            float far = Dsp.Clamp01(dk / 60f);
            dur = 2.6f + 1.6f * far;
            p1 = 0.09f + 0.10f * size;                          // seconds between the two thumps
            p2 = (58f - 20f * size) * (1f - 0.25f * far);       // thump, Hz
            p3 = 0.010f + 0.060f * far;                         // rise of a thump: far away it is a soft push
            p4 = 0.7f + 1.1f * far;                             // the tail rolls longer from far away
            d1 = Dsp.Decay(0.10f + 0.05f * size + 0.05f * far, fs);
            d2 = Dsp.Decay(0.07f, fs);
            d4 = Dsp.Decay(0.03f, fs);
            fc.Set(70f, 0.7f, fs);
            pg[0].Set(96f, 0.11f, fs); pg[1].Set(163f, 0.06f, fs);
            amp = BoomGain * (0.4f + 0.6f * size) / (1f + dk / 20f);
        }

        void Boom(int n)
        {
            float tail = Dsp.Smooth01(t / 0.2f) * (float)Math.Exp(-t / p4) * (1f - Dsp.Smooth01((t - (dur - 0.6f)) / 0.6f));
            float rise = p3, half = 0.5f / p3, gap = p1;
            for (int i = 0; i < n; i++)
            {
                g2 += sm * (tail - g2);
                // first thump
                float a = t < rise ? 0.5f - 0.5f * Dsp.Sin01(t * half + 0.25f) : 1f;
                if (t < rise) e1 = 1f; else e1 *= d1;
                if (t < 0.004f) e3 = t * 250f; else e3 *= d2;
                if (t == 0f) { bend1 = 0.6f; pg[0].Hit(0.30f); pg[1].Hit(0.20f); }
                ph1 += p2 * (1f + bend1) * invFs; if (ph1 >= 1f) ph1 -= 1f;
                bend1 *= d4;
                float th = (Dsp.Sin01(ph1) + 0.4f * e1 * Dsp.Sin01(ph1 * 2.4f)) * e1 * a;
                float nb = e3;
                // second thump, a little lower and softer
                float u = t - gap;
                if (u >= 0f)
                {
                    if (!fired1) { fired1 = true; bend2 = 0.6f; pg[0].Hit(0.25f); pg[1].Hit(0.15f); }
                    float b = u < rise ? 0.5f - 0.5f * Dsp.Sin01(u * half + 0.25f) : 1f;
                    if (u < rise) e2 = 1f; else e2 *= d1;
                    if (u < 0.004f) e4 = u * 250f; else e4 *= d2;
                    ph2 += p2 * 0.9f * (1f + bend2) * invFs; if (ph2 >= 1f) ph2 -= 1f;
                    bend2 *= d4;
                    th += (Dsp.Sin01(ph2) + 0.4f * e2 * Dsp.Sin01(ph2 * 2.4f)) * e2 * b * 0.8f;
                    nb += e4 * 0.8f;
                }
                mid[i] = nz.Pink() * 3f * nb * BoomMix;
                // the thumps and the answer of the fuselage are felt through the structure: the distance does not dull them
                low[i] = (th + pg[0].Tick() + pg[1].Tick() + fc.Lp(nz2.Brown()) * 6f * g2 * 0.4f) * BoomMix;
                t += invFs;
            }
        }

        // ---------------------------------------------------------------- Creak: the airframe groans under a load
        void StartCreak(float att)
        {
            dur = 0.7f + 1.3f * size;
            amp = att * CreakGain * (0.15f + 0.85f * size * size);
            p1 = rng.Range(26f, 40f); p2 = p1 * rng.Range(1.5f, 2.2f);
            if (rng.F() < 0.4f) { float x = p1; p1 = p2; p2 = x; }                  // some creaks sink
            p3 = rng.Range(0.94f, 1.06f); p4 = rng.F();
            d1 = rng.Range(210f, 320f); d2 = rng.Range(430f, 640f); d3 = rng.Range(800f, 1150f);
        }

        void Creak(int n)
        {
            float x = Dsp.Clamp01(t / (dur - 0.1f));
            float g = Dsp.Lerp(1f, p3, x);
            fa.Set(d1 * g, 14f, fs); fb.Set(d2 * g, 16f, fs); fc.Set(d3 * g, 14f, fs);
            float rate = Dsp.Lerp(p1, p2, Dsp.Smooth01(x)) * invFs;
            float env = Dsp.Smooth01(t / 0.12f) * (1f - Dsp.Smooth01((t - (dur - 0.4f)) / 0.33f))
                      * (0.6f + 0.4f * Dsp.Sin01(t * 1.7f + p4)) * CreakRes;
            for (int i = 0; i < n; i++)
            {
                g1 += sm * (env - g1);
                ph1 += rate;
                if (ph1 >= next1) { ph1 -= next1; next1 = 1f + 0.22f * rng.Signed(); exc += rng.Range(0.5f, 1f); }
                exc *= 0.86f; if (exc < 1e-9f) exc = 0f;
                float s = exc + nz.White() * 0.015f;
                mid[i] = (fa.Bp(s) + 0.8f * fb.Bp(s) + 0.55f * fc.Bp(s)) * g1;
                low[i] = 0f;
            }
            t += n * invFs;
        }

        // ---------------------------------------------------------------- Rattle: a bin, a latch and a panel shake for a moment
        void StartRattle(float att)
        {
            dur = 0.6f + 1.0f * size;
            amp = att * RattleGain * (0.2f + 0.8f * size);
            pg[0].Set(rng.Range(420f, 900f), rng.Range(0.009f, 0.016f), fs);        // plastic
            pg[1].Set(rng.Range(1500f, 2600f), rng.Range(0.004f, 0.008f), fs);      // a latch
            pg[2].Set(rng.Range(180f, 260f), rng.Range(0.020f, 0.035f), fs);        // a panel
            ow[0] = 1f; ow[1] = 0.8f; ow[2] = 0.45f;
            for (int i = 0; i < NO; i++) { orate[i] = rng.Range(17f, 38f) * invFs; oph[i] = rng.F(); onext[i] = 1f; }
            fa.Set(2200f, 0.9f, fs);
            d1 = Dsp.Decay(0.2f + 0.3f * size, fs);
        }

        void Rattle(int n)
        {
            for (int i = 0; i < n; i++)
            {
                if (t < 0.03f) e1 = t * (1f / 0.03f); else e1 *= d1;
                for (int q = 0; q < NO; q++)
                {
                    oph[q] += orate[q];
                    if (oph[q] >= onext[q])
                    {
                        oph[q] -= onext[q]; onext[q] = 1f + 0.3f * rng.Signed();
                        float u = rng.Range(0.5f, 1f) * e1;
                        pg[q].Hit(u * ow[q] * 0.5f);
                        grain += 0.4f * u;
                    }
                }
                grain *= gDecay; if (grain < 1e-7f) grain = 0f;
                mid[i] = (pg[0].Tick() + pg[1].Tick() + pg[2].Tick() + fa.Bp(nz.White() * (grain + 1e-9f))) * RattleMix;
                low[i] = 0f;
                t += invFs;
            }
        }

        // ---------------------------------------------------------------- Alarm: a cockpit warning behind the door, a few beeps
        void StartAlarm(float att)
        {
            beeps = 4 + (int)(size * 4f);
            dur = beeps * BeepPeriod + 0.1f;
            amp = att * AlarmGain * (0.4f + 0.6f * size);
            fa.Set(1500f, 0.7f, fs); fb.Set(1500f, 0.7f, fs);                       // the door
        }

        void Alarm(int n)
        {
            float last = beeps * BeepPeriod;
            for (int i = 0; i < n; i++)
            {
                float c = t - BeepPeriod * (int)(t / BeepPeriod);
                float target = (c < BeepOn && t < last) ? 1f : 0f;
                g1 += sm * (target - g1);
                ph1 += AlarmA * invFs; if (ph1 >= 1f) ph1 -= 1f;
                ph2 += AlarmB * invFs; if (ph2 >= 1f) ph2 -= 1f;
                float s = (Dsp.Sin01(ph1) + 0.6f * Dsp.Sin01(ph2)) * 0.8f;
                if (s > 1f) s = 1f; else if (s < -1f) s = -1f;
                s = 1.5f * s - 0.5f * s * s * s;                                    // a little edge, like a small loudspeaker
                mid[i] = fb.Lp(fa.Lp(s)) * g1 * AlarmMix;
                low[i] = 0f;
                t += invFs;
            }
        }
    }

    /// <summary>The discrete sounds of the cabin: ten voices; when all are busy the one with the least left to say
    /// is stolen.</summary>
    internal sealed class EventBank
    {
        public const int NV = 10;

        readonly EventVoice[] v = new EventVoice[NV];
        /// <summary>Events that found no voice to steal (should stay 0).</summary>
        public int Dropped;

        public EventBank(int sampleRate, uint seed, int block)
        {
            for (int i = 0; i < NV; i++) v[i] = new EventVoice(sampleRate, seed + 17u * (uint)(i + 1), block);
        }

        public void Reset() { for (int i = 0; i < NV; i++) v[i].Reset(); }

        /// <summary>Audio thread: the event is heard now.</summary>
        public void Trigger(int kind, float dist, float size, float pan)
        {
            if (kind < 0 || kind >= EventVoice.Kinds) return;
            if (float.IsNaN(dist) || float.IsNaN(size) || float.IsNaN(pan)) return;
            dist = Dsp.Clamp(dist, 0f, 1000000f); size = Dsp.Clamp01(size); pan = Dsp.Clamp(pan, -1f, 1f);
            int best = -1; float least = float.MaxValue;
            for (int i = 0; i < NV; i++)
            {
                if (!v[i].Active) { v[i].Start(kind, dist, size, pan); return; }
                float p = v[i].Priority;
                if (p < least) { least = p; best = i; }
            }
            if (best >= 0 && least < float.MaxValue) v[best].Steal(kind, dist, size, pan);
            else Dropped++;
        }

        /// <summary>Adds all sounding events to l/r and their reverb sends to sl/sr.</summary>
        public void Process(float[] l, float[] r, float[] sl, float[] sr, int n, float trim)
        {
            for (int i = 0; i < NV; i++)
                if (v[i].Active) v[i].Process(l, r, sl, sr, n, trim);
        }
    }
}
