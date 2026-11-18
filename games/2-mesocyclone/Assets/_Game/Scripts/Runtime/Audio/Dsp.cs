using System;

namespace MESOCYCLONE.Audio
{
    /// <summary>xorshift32 noise source: deterministic, allocation free. Never mark a field of this type readonly
    /// (the methods mutate the state in place).</summary>
    public struct Rng
    {
        public uint s;

        public void Seed(uint seed)
        {
            s = Dsp.Hash(seed * 2654435761u + 0x9E3779B9u);
            if (s == 0u) s = 0x1234567u;
        }

        /// <summary>White noise sample in [-1, 1).</summary>
        public float Bi()
        {
            uint x = s; x ^= x << 13; x ^= x >> 17; x ^= x << 5; s = x;
            return (int)x * 4.656613e-10f;
        }

        /// <summary>Uniform value in [0, 1).</summary>
        public float Uni()
        {
            uint x = s; x ^= x << 13; x ^= x >> 17; x ^= x << 5; s = x;
            return (x >> 8) * 5.9604645e-8f;
        }

        public float Range(float a, float b) { return a + (b - a) * Uni(); }
    }

    /// <summary>Small maths shared by the whole audio engine. Everything here is allocation free and safe on the
    /// audio thread (System.MathF only).</summary>
    public static class Dsp
    {
        public const float TwoPi = 6.2831853f;
        public const float Deg2Rad = 0.017453293f;
        public const float Rad2Deg = 57.29578f;
        public const float Tiny = 1e-18f;             // keeps recursive filters out of the denormal range

        public static uint Hash(uint x)
        {
            x ^= x >> 16; x *= 0x7feb352du; x ^= x >> 15; x *= 0x846ca68bu; x ^= x >> 16;
            return x;
        }

        public static float Hash01(uint x) { return (Hash(x) & 0xFFFFFF) / 16777216f; }

        public static float Clamp01(float x) { return x < 0f ? 0f : (x > 1f ? 1f : x); }
        public static float Clamp(float x, float a, float b) { return x < a ? a : (x > b ? b : x); }
        public static float Lerp(float a, float b, float t) { return a + (b - a) * t; }

        /// <summary>Smoothstep from a to b (works with a greater than b as a falling edge).</summary>
        public static float Step(float a, float b, float x)
        {
            float u = Clamp01((x - a) / (b - a));
            return u * u * (3f - 2f * u);
        }

        public static float Db(float db) { return MathF.Pow(10f, db * 0.05f); }

        /// <summary>One-pole low-pass coefficient for a cutoff in Hz.</summary>
        public static float Pole(float fc, float sr)
        {
            float a = 1f - MathF.Exp(-TwoPi * fc / sr);
            return a > 1f ? 1f : (a < 0f ? 0f : a);
        }

        /// <summary>One-pole smoothing coefficient for a time constant tau when ticked every step seconds.</summary>
        public static float Ease(float tau, float step)
        {
            return 1f - MathF.Exp(-step / (tau > 1e-5f ? tau : 1e-5f));
        }

        /// <summary>Equal-power pan, -1 = left, +1 = right.</summary>
        public static void Pan(float pan, out float l, out float r)
        {
            float th = (Clamp(pan, -1f, 1f) + 1f) * 0.78539816f;
            l = MathF.Cos(th); r = MathF.Sin(th);
        }

        /// <summary>Soft saturation with unity slope at zero and asymptotes at +/-1 (a rational tanh).</summary>
        public static float Soft(float x)
        {
            if (x > 3f) return 1f;
            if (x < -3f) return -1f;
            float x2 = x * x;
            return x * (27f + x2) / (27f + 9f * x2);
        }

        /// <summary>Low-pass cutoff of the air for a source at a distance (metres): 4 kHz at 300 m, 1.5 kHz at 1 km,
        /// 600 Hz at 3 km, 220 Hz at 10 km, 120 Hz at 20 km.</summary>
        public static float AirCutoff(float dist)
        {
            if (dist < 100f) dist = 100f;
            float fc = 1500f * MathF.Pow(1000f / dist, 0.83f);
            return fc > 9000f ? 9000f : (fc < 90f ? 90f : fc);
        }

        /// <summary>Amplitude factor that brings white noise through two equal cascaded one-pole low-passes
        /// (coefficient a) back to the RMS of its input.</summary>
        public static float Norm2Pole(float a)
        {
            float b = 1f - a, b2 = b * b, q = 1f - b2;
            float var = a * a * a * a * (1f + b2) / (q * q * q);
            return 1f / MathF.Sqrt(var > 1e-12f ? var : 1e-12f);
        }
    }

    /// <summary>State-variable filter (trapezoidal, stable when the cutoff moves). Set() at control rate.</summary>
    public struct Svf
    {
        public float ic1, ic2, a1, a2, a3, k;

        public void Set(float fc, float q, float sr)
        {
            if (fc > 0.45f * sr) fc = 0.45f * sr;
            if (fc < 8f) fc = 8f;
            float g = MathF.Tan(MathF.PI * fc / sr);
            k = 1f / q; a1 = 1f / (1f + g * (g + k)); a2 = g * a1; a3 = g * a2;
        }

        public float Lp(float x)
        {
            float v3 = x - ic2, v1 = a1 * ic1 + a2 * v3, v2 = ic2 + a2 * ic1 + a3 * v3;
            ic1 = 2f * v1 - ic1; ic2 = 2f * v2 - ic2;
            return v2;
        }

        public float Bp(float x)
        {
            float v3 = x - ic2, v1 = a1 * ic1 + a2 * v3, v2 = ic2 + a2 * ic1 + a3 * v3;
            ic1 = 2f * v1 - ic1; ic2 = 2f * v2 - ic2;
            return v1;
        }

        public float Hp(float x)
        {
            float v3 = x - ic2, v1 = a1 * ic1 + a2 * v3, v2 = ic2 + a2 * ic1 + a3 * v3;
            ic1 = 2f * v1 - ic1; ic2 = 2f * v2 - ic2;
            return x - k * v1 - v2;
        }

        public void Reset() { ic1 = 0f; ic2 = 0f; }
    }

    /// <summary>Sine oscillator as a rotating phasor: two multiply-adds per sample, no trigonometry per sample.
    /// Call Init once, SetFreq when the pitch changes and Norm at control rate.</summary>
    public struct Osc
    {
        public float re, im, c, s;

        public void Init(float phase01)
        {
            re = MathF.Cos(Dsp.TwoPi * phase01); im = MathF.Sin(Dsp.TwoPi * phase01);
            c = 1f; s = 0f;
        }

        public void SetFreq(float f, float sr)
        {
            float w = Dsp.TwoPi * f / sr;
            c = MathF.Cos(w); s = MathF.Sin(w);
        }

        public float Tick()
        {
            float nr = re * c - im * s;
            im = re * s + im * c; re = nr;
            return im;
        }

        public void Norm()
        {
            float m = re * re + im * im;
            if (m < 0.25f || m > 4f) { re = 1f; im = 0f; return; }
            float g = 1.5f - 0.5f * m;
            re *= g; im *= g;
        }
    }

    /// <summary>Pink noise from white (Paul Kellett's economy filter, about -3 dB per octave).</summary>
    public struct Pink
    {
        float b0, b1, b2;

        public float Tick(float w)
        {
            b0 = 0.99765f * b0 + w * 0.0990460f;
            b1 = 0.96300f * b1 + w * 0.2965164f;
            b2 = 0.57000f * b2 + w * 1.0526913f;
            return (b0 + b1 + b2 + w * 0.1848f) * 0.25f;
        }

        public void Reset() { b0 = 0f; b1 = 0f; b2 = 0f; }
    }

    /// <summary>Slowly wandering control value in -1..1 (low-passed noise ticked at control rate). Used wherever the
    /// spec asks for "breathing", wander or gust fluctuation.</summary>
    public struct Wander
    {
        public float v, target;
        float hold;

        /// <summary>rate = new targets per second, tau = smoothing time constant, step = seconds per tick.</summary>
        public float Tick(ref Rng rng, float rate, float tau, float step)
        {
            hold -= step * rate;
            if (hold <= 0f) { hold = 0.5f + rng.Uni(); target = rng.Bi(); }
            v += (target - v) * Dsp.Ease(tau, step);
            return v;
        }

        public void Reset() { v = 0f; target = 0f; hold = 0f; }
    }
}
