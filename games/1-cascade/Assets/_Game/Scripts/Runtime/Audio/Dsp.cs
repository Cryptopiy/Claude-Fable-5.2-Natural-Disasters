using System;

namespace CASCADE
{
    /// <summary>
    /// Pure helpers of the audio engine. No UnityEngine, no allocation: safe on the audio thread and in offline renders.
    /// Envelopes are closed forms of "seconds since the event", so the score can be evaluated at any time (seekable).
    /// </summary>
    public static class Dsp
    {
        public const float Ceiling = 0.891f;                 // -1 dBFS
        public const double Tau = 6.283185307179586;

        public static float Db(float db) { return (float)Math.Exp(db * 0.11512925464970229); }
        public static float Sat(float x) { return x < 0f ? 0f : x > 1f ? 1f : x; }
        public static float Clamp(float x, float a, float b) { return x < a ? a : x > b ? b : x; }
        public static float Lerp(float a, float b, float t) { return a + (b - a) * t; }
        public static float Exp(float x) { return (float)Math.Exp(x); }

        public static float Smooth(float a, float b, float x)
        {
            float t = Sat((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        /// <summary>0 outside (0, dur); smooth rise over 'rise', smooth fall over the last 'fall' seconds.</summary>
        public static float Win(float t, float rise, float dur, float fall)
        {
            if (t <= 0f || t >= dur) return 0f;
            return Smooth(0f, rise, t) * (1f - Smooth(dur - fall, dur, t));
        }

        /// <summary>A struck sound: smooth attack, then exponential decay with time constant tau.</summary>
        public static float Hit(float t, float attack, float tau)
        {
            if (t <= 0f) return 0f;
            float x = t / tau;
            if (x > 14f) return 0f;
            float a = t >= attack ? 1f : Smooth(0f, attack, t);
            return a * (float)Math.Exp(-x);
        }

        /// <summary>Tremor-shaped swell: rise over 15 % of dur, hold, fall to zero at 1.35 dur (the sound outlasts the shaking).</summary>
        public static float Swell(float t, float dur)
        {
            float end = dur * 1.35f;
            if (t <= 0f || t >= end) return 0f;
            return Smooth(0f, 0.15f * dur + 0.3f, t) * (1f - Smooth(0.5f * dur, end, t));
        }

        public static float Hash(int n)
        {
            unchecked
            {
                uint x = (uint)n * 747796405u + 2891336453u;
                x = ((x >> (int)((x >> 28) + 4u)) ^ x) * 277803737u;
                x = (x >> 22) ^ x;
                return (x & 0xFFFFFF) / 16777216f;
            }
        }
        public static float Hash(int a, int b) { unchecked { return Hash(a * 73856093 ^ b * 19349663); } }

        /// <summary>Smooth value noise of absolute time, -1..1: the seekable replacement for a random LFO.</summary>
        public static float Noise(double x, int seed)
        {
            double fl = Math.Floor(x);
            int i = (int)fl;
            float f = (float)(x - fl);
            f = f * f * (3f - 2f * f);
            float a = Hash(i, seed), b = Hash(i + 1, seed);
            return (a + (b - a) * f) * 2f - 1f;
        }

        /// <summary>Cheap tanh (rational), exact enough for waveshaping.</summary>
        public static float Tanh(float x)
        {
            if (x > 3f) return 1f;
            if (x < -3f) return -1f;
            float x2 = x * x;
            return x * (27f + x2) / (27f + 9f * x2);
        }

        /// <summary>Waveshaper with unity small-signal gain removed: tanh(k x) / tanh(k).</summary>
        public static float Shape(float x, float k) { return Tanh(k * x) / Tanh(k); }

        /// <summary>Equal-power pan, pan in -1 (left) .. +1 (right).</summary>
        public static void Pan(float pan, out float l, out float r)
        {
            double th = (Clamp(pan, -1f, 1f) + 1f) * 0.7853981633974483;
            l = (float)Math.Cos(th);
            r = (float)Math.Sin(th);
        }

        public static bool Crossed(double prev, double now, double at) { return prev < at && at <= now; }
    }

    /// <summary>xorshift32, seeded. One per voice so that renders are reproducible.</summary>
    public struct Rng
    {
        uint s;
        public Rng(uint seed)
        {
            unchecked { s = seed * 2654435761u + 0x9E3779B9u; }
            if (s == 0u) s = 0x1234567u;
            Next(); Next(); Next();
        }
        public uint Next() { s ^= s << 13; s ^= s >> 17; s ^= s << 5; return s; }
        /// <summary>White noise sample, -1..1.</summary>
        public float Bi() { return (int)Next() * 4.656613e-10f; }
        /// <summary>Uniform 0..1.</summary>
        public float Uni() { return (Next() >> 8) * 5.9604645e-8f; }
    }

    /// <summary>Pink noise (Paul Kellet's refined filter), about 0.3 RMS.</summary>
    public struct PinkNoise
    {
        Rng r; float b0, b1, b2, b3, b4, b5, b6;
        public PinkNoise(uint seed) { r = new Rng(seed); b0 = b1 = b2 = b3 = b4 = b5 = b6 = 0f; }
        public float Next()
        {
            float w = r.Bi();
            b0 = 0.99886f * b0 + w * 0.0555179f;
            b1 = 0.99332f * b1 + w * 0.0750759f;
            b2 = 0.96900f * b2 + w * 0.1538520f;
            b3 = 0.86650f * b3 + w * 0.3104856f;
            b4 = 0.55000f * b4 + w * 0.5329522f;
            b5 = -0.7616f * b5 - w * 0.0168980f;
            float p = b0 + b1 + b2 + b3 + b4 + b5 + b6 + w * 0.5362f;
            b6 = w * 0.115926f;
            return p * 0.16f;
        }
    }

    /// <summary>Brown (red) noise: white through a leaky integrator with a 12 Hz corner, about 0.3 RMS.</summary>
    public struct BrownNoise
    {
        Rng r; float b, p, g;
        public BrownNoise(uint seed, int fs)
        {
            r = new Rng(seed); b = 0f;
            p = (float)Math.Exp(-Dsp.Tau * 12.0 / fs);
            g = 0.3f * (float)Math.Sqrt(3.0 * (1.0 - p * p));
        }
        public float Next() { b = b * p + r.Bi() * g; return b; }
    }

    /// <summary>Sparse random impulses ("dust"): density is the probability per sample.</summary>
    public struct Dust
    {
        Rng r;
        public Dust(uint seed) { r = new Rng(seed); }
        public float Next(float density)
        {
            if ((r.Next() >> 8) * 5.9604645e-8f >= density) return 0f;
            return r.Bi();
        }
    }

    /// <summary>Linear per-sample ramp towards a control value: every gain in the engine goes through one (no zipper, no clicks).</summary>
    public struct Ramp
    {
        public float v, d;
        /// <summary>Reach 'target' in n samples. A ramp that arrives at zero switches Off, so silent layers cost nothing.</summary>
        public void To(float target, int n)
        {
            if (target == 0f && v < 1e-6f && v > -1e-6f) { v = 0f; d = 0f; }
            else d = (target - v) / n;
        }
        /// <summary>Like To, but moves at most maxStep per call: a fade that is longer than one control block.</summary>
        public void Slew(float target, float maxStep, int n)
        {
            float x = target - v;
            if (x > maxStep) To(v + maxStep, n);
            else if (x < -maxStep) To(v - maxStep, n);
            else To(target, n);
        }
        public void Snap(float x) { v = x; d = 0f; }
        public float Next() { v += d; return v; }
        public bool Off { get { return v == 0f && d == 0f; } }
    }

    /// <summary>Phase accumulator with sine, polyBLEP saw and polyBLEP pulse outputs.</summary>
    public struct Osc
    {
        public double ph;
        public float Sine(double inc)
        {
            ph += inc; if (ph >= 1.0) ph -= 1.0;
            return (float)Math.Sin(ph * Dsp.Tau);
        }
        /// <summary>Sine of a multiple of the current phase (harmonics locked to the fundamental).</summary>
        public float Harm(int k) { return (float)Math.Sin(ph * k * Dsp.Tau); }
        public float Saw(double inc)
        {
            ph += inc; if (ph >= 1.0) ph -= 1.0;
            return (float)(2.0 * ph - 1.0 - Blep(ph, inc));
        }
        public float Pulse(double inc)
        {
            ph += inc; if (ph >= 1.0) ph -= 1.0;
            double q = ph + 0.5; if (q >= 1.0) q -= 1.0;
            return (float)((ph < 0.5 ? 1.0 : -1.0) + Blep(ph, inc) - Blep(q, inc));
        }
        static double Blep(double t, double dt)
        {
            if (t < dt) { t /= dt; return t + t - t * t - 1.0; }
            if (t > 1.0 - dt) { t = (t - 1.0) / dt; return t * t + t + t + 1.0; }
            return 0.0;
        }
    }

    /// <summary>Sine LFO on its own phase (used where a free-running wobble is fine).</summary>
    public struct Lfo
    {
        double ph;
        public float Next(double inc)
        {
            ph += inc; if (ph >= 1.0) ph -= 1.0;
            return (float)Math.Sin(ph * Dsp.Tau);
        }
    }

    /// <summary>One-pole low-pass / high-pass and parameter smoother.</summary>
    public struct OnePole
    {
        public float a, y;
        public void SetHz(int fs, float fc) { a = 1f - (float)Math.Exp(-Dsp.Tau * fc / fs); }
        public void SetTime(int fs, float seconds) { a = 1f - (float)Math.Exp(-1.0 / (seconds * fs)); }
        public float Lp(float x) { y += a * (x - y) + 1e-20f; return y; }
        public float Hp(float x) { y += a * (x - y) + 1e-20f; return x - y; }
    }

    /// <summary>DC blocker with a 5 Hz corner (25 Hz sub-bass survives).</summary>
    public struct DcBlock
    {
        float x1, y1, r;
        public DcBlock(int fs) { x1 = 0f; y1 = 0f; r = 1f - (float)(Dsp.Tau * 5.0 / fs); }
        public float Next(float x) { float y = x - x1 + r * y1; x1 = x; y1 = y + 1e-20f; return y; }
    }

    /// <summary>Stateful attack-release envelope (gate driven).</summary>
    public struct ArEnv
    {
        float e, ka, kr;
        public void Set(int fs, float attack, float release)
        {
            ka = 1f - (float)Math.Exp(-1.0 / (attack * fs));
            kr = 1f - (float)Math.Exp(-1.0 / (release * fs));
        }
        public float Next(bool gate) { e += ((gate ? 1f : 0f) - e) * (gate ? ka : kr); return e; }
    }

    /// <summary>Stateful linear ADSR, times in seconds.</summary>
    public struct Adsr
    {
        float a, d, s, r, e; int stage;   // 0 idle, 1 attack, 2 decay, 3 sustain, 4 release
        public void Set(int fs, float attack, float decay, float sustain, float release)
        {
            a = 1f / (attack * fs); d = (1f - sustain) / (decay * fs); s = sustain; r = 1f / (release * fs);
        }
        public void On() { stage = 1; }
        public void Off() { if (stage != 0) stage = 4; }
        public bool Active { get { return stage != 0; } }
        public float Next()
        {
            switch (stage)
            {
                case 1: e += a; if (e >= 1f) { e = 1f; stage = 2; } break;
                case 2: e -= d; if (e <= s) { e = s; stage = 3; } break;
                case 4: e -= r; if (e <= 0f) { e = 0f; stage = 0; } break;
            }
            return e;
        }
    }
}
