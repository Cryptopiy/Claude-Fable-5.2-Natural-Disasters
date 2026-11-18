using System;

namespace MESOCYCLONE.Audio
{
    /// <summary>What happens inside the listener's ears, applied to the whole mix: the blocked ear of the pressure
    /// drop (highs dulled, a slow low swell), the ringing after the hail (low-pass near 1.5 kHz, the world 8 dB
    /// down, a faint 4-6 kHz tone, all recovering with StoryState.ringing), the dulling of an inhale, and the
    /// pressure thump of the tornado's nearest pass (a rise of 0.6 s, a deep thump, ears blocked for seconds).</summary>
    public sealed class Ear
    {
        public const double Lead = 0.7;
        public float Block;                           // 0..1 blocked ear from the thump (the beds step back with it)

        readonly int sr;
        float l1, l2, r1, r2, fc = 16000f, mixPrev, gainPrev = 1f;
        Osc t1, t2, t3, thOsc;
        float tinPrev, swPrev, thPrev;
        Svf swA, swB;
        Wander swAm, beat;
        float thumpAge;
        bool thumpOn;

        public Ear(int sampleRate) { sr = sampleRate; Reset(); }

        public void Reset()
        {
            l1 = l2 = r1 = r2 = 0f; fc = 16000f; mixPrev = 0f; gainPrev = 1f;
            t1.Init(0f); t2.Init(0.3f); t3.Init(0.6f); thOsc.Init(0f);
            tinPrev = swPrev = thPrev = 0f; swA.Reset(); swB.Reset(); swAm.Reset(); beat.Reset();
            thumpOn = false; thumpAge = 0f; Block = 0f;
        }

        /// <summary>late = seconds since the thump landed (negative: still rising).</summary>
        public void Thump(float late) { thumpOn = true; thumpAge = late; thOsc.Init(0f); }

        /// <summary>Processes the summed mix in place.</summary>
        public void Process(Ctx c, float[] L, float[] R)
        {
            int n = c.n; float dt = c.dt;
            float p = Dsp.Clamp01(c.s.pressure), ring = Dsp.Clamp01(c.s.ringing);

            // the thump: rise, land, decay; the ear stays blocked much longer than the thump lasts
            float thAmp = 0f, blk = 0f;
            if (thumpOn)
            {
                thumpAge += dt;
                if (thumpAge < 0f) { float u = Dsp.Clamp01(1f + thumpAge / (float)Lead); thAmp = u * u * (3f - 2f * u); blk = thAmp; }
                else { thAmp = MathF.Exp(-thumpAge / 0.8f); blk = MathF.Exp(-thumpAge / 3.2f); }
                if (thumpAge > 16f) thumpOn = false;
            }
            Block += (blk - Block) * c.k50;

            // one low-pass for all the reasons an ear can be dull; the lowest cutoff wins
            float f = 16000f, q;
            if (p > 0.001f) { q = 16000f * MathF.Pow(0.25f, p); if (q < f) f = q; }                       // 4 kHz
            if (ring > 0.001f) { q = 16000f * MathF.Pow(0.09375f, Dsp.Clamp01(ring * 1.15f)); if (q < f) f = q; }   // 1.5 kHz
            if (Block > 0.001f) { q = 16000f * MathF.Pow(0.14f, Block); if (q < f) f = q; }                // 2.2 kHz
            if (c.dull > 0.001f) { q = 16000f * MathF.Pow(0.2f, Dsp.Clamp01(c.dull)); if (q < f) f = q; }  // 3.2 kHz
            fc += (f - fc) * c.k50;
            float a = Dsp.Pole(fc, sr);
            float mix = Dsp.Clamp01((16000f - fc) / 3000f);
            float gain = (1f - 0.75f * ring) * (1f - 0.3f * Block);

            // tinnitus: three high partials that beat slowly
            float ringAll = Dsp.Clamp01(ring + 0.3f * Block);
            float tin = Mix.Tinnitus * ringAll * MathF.Sqrt(ringAll) * (1f + 0.12f * beat.Tick(ref c.rng, 0.8f, 0.6f, dt));
            // the swell of the pressure drop: band noise, felt more than heard, plus its octave for small speakers
            float sw = Dsp.Db(-31f) * p * MathF.Sqrt(p) * (0.8f + 0.2f * swAm.Tick(ref c.rng, 0.4f, 1.5f, dt));
            float th = Dsp.Db(-11f) * thAmp;
            bool tones = tin > 1e-6f || tinPrev > 1e-6f;
            bool lows = sw > 1e-6f || swPrev > 1e-6f || th > 1e-6f || thPrev > 1e-6f;
            if (tones) { t1.SetFreq(4100f, sr); t2.SetFreq(5700f, sr); t3.SetFreq(6300f, sr); t1.Norm(); t2.Norm(); t3.Norm(); }
            if (lows) { swA.Set(34f, 1.1f, sr); swB.Set(68f, 1.4f, sr); thOsc.SetFreq(31f, sr); thOsc.Norm(); }

            float m = mixPrev, dm = (mix - mixPrev) / n, g = gainPrev, dg = (gain - gainPrev) / n;
            float gt = tinPrev, dgt = (tin - tinPrev) / n, gs = swPrev, dgs = (sw - swPrev) / n, gh = thPrev, dgh = (th - thPrev) / n;
            float[] br = c.brown;
            for (int j = 0; j < n; j++)
            {
                float xl = L[j] + Dsp.Tiny, xr = R[j] + Dsp.Tiny;
                l1 += a * (xl - l1); l2 += a * (l1 - l2);
                r1 += a * (xr - r1); r2 += a * (r1 - r2);
                m += dm; g += dg;
                float ol = (xl + m * (l2 - xl)) * g, or_ = (xr + m * (r2 - xr)) * g;
                if (tones)
                {
                    gt += dgt;
                    float x1 = t1.Tick(), x2 = t2.Tick(), x3 = t3.Tick();
                    ol += gt * (x1 + 0.7f * x2 + 0.2f * x3); or_ += gt * (0.9f * x1 + 0.35f * x2 + 0.5f * x3);
                }
                if (lows)
                {
                    gs += dgs; gh += dgh;
                    float b = br[j];
                    float low = gs * (swA.Bp(b) * 7f + swB.Bp(b) * 1.6f) + gh * thOsc.Tick() * (1f + 0.5f * b);
                    ol += low; or_ += low;
                }
                L[j] = ol; R[j] = or_;
            }
            mixPrev = mix; gainPrev = gain; tinPrev = tin; swPrev = sw; thPrev = th;
        }
    }
}
