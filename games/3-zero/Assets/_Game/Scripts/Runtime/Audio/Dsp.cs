using System;

namespace ZERO
{
    /// <summary>Small DSP helpers. Pure C#, allocation-free after static init, safe on the audio thread.</summary>
    public static class Dsp
    {
        public const float Pi = 3.14159265f;
        public const float TwoPi = 6.2831853f;
        /// <summary>-1 dBFS: the absolute output ceiling of the game.</summary>
        public const float Ceiling = 0.891f;

        const int SinN = 4096;
        static readonly float[] sinTab = MakeSin();

        static float[] MakeSin()
        {
            var t = new float[SinN + 2];
            for (int i = 0; i < t.Length; i++) t[i] = (float)Math.Sin(2.0 * Math.PI * i / SinN);
            return t;
        }

        /// <summary>sin(2 pi p). p in turns, p >= 0 (only the fraction is used).</summary>
        public static float Sin01(float p)
        {
            float x = p * SinN;
            int i = (int)x;
            float f = x - i;
            i &= SinN - 1;
            float a = sinTab[i];
            return a + (sinTab[i + 1] - a) * f;
        }

        /// <summary>sin(2 pi p) for a double phase (slow beating needs the precision).</summary>
        public static float Sin01(double p)
        {
            double x = p * SinN;
            int i = (int)x;
            float f = (float)(x - i);
            i &= SinN - 1;
            float a = sinTab[i];
            return a + (sinTab[i + 1] - a) * f;
        }

        public static float DbToLin(float db) { return (float)Math.Exp(db * 0.11512925464970229); }
        public static float LinToDb(float lin) { return lin > 1e-10f ? (float)(8.685889638065035 * Math.Log(lin)) : -200f; }
        /// <summary>2^oct.</summary>
        public static float Pow2(float oct) { return (float)Math.Exp(oct * 0.6931471805599453); }
        public static float Clamp(float x, float a, float b) { return x < a ? a : (x > b ? b : x); }
        public static float Clamp01(float x) { return x < 0f ? 0f : (x > 1f ? 1f : x); }
        public static float Lerp(float a, float b, float t) { return a + (b - a) * t; }
        /// <summary>Smoothstep of a clamped 0..1 value.</summary>
        public static float Smooth01(float t) { t = t < 0f ? 0f : (t > 1f ? 1f : t); return t * t * (3f - 2f * t); }
        public static float SmoothStep(float a, float b, float x) { return Smooth01((x - a) / (b - a)); }
        /// <summary>Coefficient of a one-pole low-pass with cutoff fc.</summary>
        public static float LpCoef(float fc, float fs) { return 1f - (float)Math.Exp(-2.0 * Math.PI * fc / fs); }
        /// <summary>Coefficient of a one-pole smoother with time constant "seconds" running at rate fs.</summary>
        public static float TauCoef(float seconds, float fs) { return 1f - (float)Math.Exp(-1.0 / (seconds * fs)); }
        /// <summary>Per-sample multiplier of an exponential decay with time constant "seconds".</summary>
        public static float Decay(float seconds, float fs) { return (float)Math.Exp(-1.0 / (seconds * fs)); }
        /// <summary>Equal-power pan, pan in -1 (left) .. +1 (right); centre gives 0.7071 each.</summary>
        public static void Pan(float pan, out float l, out float r)
        {
            float a = (Clamp(pan, -1f, 1f) + 1f) * 0.125f;     // 0..0.25 turns
            l = Sin01(a + 0.25f); r = Sin01(a);
        }
        /// <summary>Kills values that are about to become denormal.</summary>
        public static void Zap(ref float v) { if (v < 1e-15f && v > -1e-15f) v = 0f; }

        /// <summary>Piecewise-linear table look-up: xs ascending, clamped at both ends.</summary>
        public static float Curve(float x, float[] xs, float[] ys)
        {
            int n = xs.Length;
            if (x <= xs[0]) return ys[0];
            if (x >= xs[n - 1]) return ys[n - 1];
            int i = 1;
            while (i < n - 1 && x > xs[i]) i++;
            float t = (x - xs[i - 1]) / (xs[i] - xs[i - 1]);
            return ys[i - 1] + (ys[i] - ys[i - 1]) * t;
        }
    }

    /// <summary>One-pole low-pass / high-pass (6 dB per octave).</summary>
    public struct OnePole
    {
        float a, y;
        public void SetCutoff(float fc, float fs) { a = Dsp.LpCoef(fc, fs); }
        public void SetTime(float seconds, float fs) { a = Dsp.TauCoef(seconds, fs); }
        public float Lp(float x) { y += a * (x - y); return y; }
        public float Hp(float x) { y += a * (x - y); return x - y; }
        public float Value { get { return y; } }
        public void Reset(float v) { y = v; }
        public void Zap() { Dsp.Zap(ref y); }
    }

    /// <summary>DC blocker: y = x - x1 + R*y1.</summary>
    public struct DcBlock
    {
        float x1, y1, r;
        public void Set(float fc, float fs) { r = 1f - Dsp.TwoPi * fc / fs; }
        public float Tick(float x) { float y = x - x1 + r * y1; x1 = x; y1 = y; return y; }
        public void Reset() { x1 = 0f; y1 = 0f; }
        public void Zap() { Dsp.Zap(ref x1); Dsp.Zap(ref y1); }
    }

    /// <summary>State-variable filter, TPT form (Simper). Safe to modulate at control rate.</summary>
    public struct Svf
    {
        float a1, a2, a3, k, ic1, ic2;

        public void Set(float fc, float q, float fs)
        {
            float top = fs * 0.45f;
            if (fc > top) fc = top;
            if (fc < 5f) fc = 5f;
            float g = (float)Math.Tan(Math.PI * fc / fs);
            k = 1f / q;
            a1 = 1f / (1f + g * (g + k));
            a2 = g * a1;
            a3 = g * a2;
        }

        public float Lp(float x)
        {
            float v3 = x - ic2;
            float v1 = a1 * ic1 + a2 * v3;
            float v2 = ic2 + a2 * ic1 + a3 * v3;
            ic1 = 2f * v1 - ic1; ic2 = 2f * v2 - ic2;
            return v2;
        }

        /// <summary>Band-pass with unity gain at the centre frequency.</summary>
        public float Bp(float x)
        {
            float v3 = x - ic2;
            float v1 = a1 * ic1 + a2 * v3;
            float v2 = ic2 + a2 * ic1 + a3 * v3;
            ic1 = 2f * v1 - ic1; ic2 = 2f * v2 - ic2;
            return v1 * k;
        }

        public float Hp(float x)
        {
            float v3 = x - ic2;
            float v1 = a1 * ic1 + a2 * v3;
            float v2 = ic2 + a2 * ic1 + a3 * v3;
            ic1 = 2f * v1 - ic1; ic2 = 2f * v2 - ic2;
            return x - k * v1 - v2;
        }

        /// <summary>Takes the tuning of another filter and keeps the own memory (click-free retuning without a tan).</summary>
        public void CoefsFrom(ref Svf o) { a1 = o.a1; a2 = o.a2; a3 = o.a3; k = o.k; }
        public void Reset() { ic1 = 0f; ic2 = 0f; }
        public void Zap() { Dsp.Zap(ref ic1); Dsp.Zap(ref ic2); }
    }

    /// <summary>RBJ cookbook biquad, transposed direct form II. For fixed (not modulated) filters.</summary>
    public struct Biquad
    {
        float b0, b1, b2, a1, a2, z1, z2;

        void Norm(double nb0, double nb1, double nb2, double a0, double na1, double na2)
        {
            double inv = 1.0 / a0;
            b0 = (float)(nb0 * inv); b1 = (float)(nb1 * inv); b2 = (float)(nb2 * inv);
            a1 = (float)(na1 * inv); a2 = (float)(na2 * inv);
        }

        public void SetLowpass(float f, float q, float fs)
        {
            double w = 2.0 * Math.PI * Math.Min(f, fs * 0.45f) / fs, c = Math.Cos(w), al = Math.Sin(w) / (2.0 * q);
            Norm((1 - c) * 0.5, 1 - c, (1 - c) * 0.5, 1 + al, -2 * c, 1 - al);
        }

        public void SetHighpass(float f, float q, float fs)
        {
            double w = 2.0 * Math.PI * Math.Min(f, fs * 0.45f) / fs, c = Math.Cos(w), al = Math.Sin(w) / (2.0 * q);
            Norm((1 + c) * 0.5, -(1 + c), (1 + c) * 0.5, 1 + al, -2 * c, 1 - al);
        }

        /// <summary>Band-pass with 0 dB peak gain.</summary>
        public void SetBandpass(float f, float q, float fs)
        {
            double w = 2.0 * Math.PI * Math.Min(f, fs * 0.45f) / fs, c = Math.Cos(w), al = Math.Sin(w) / (2.0 * q);
            Norm(al, 0, -al, 1 + al, -2 * c, 1 - al);
        }

        public float Tick(float x)
        {
            float y = b0 * x + z1;
            z1 = b1 * x - a1 * y + z2;
            z2 = b2 * x - a2 * y;
            return y;
        }

        public void Reset() { z1 = 0f; z2 = 0f; }
        public void Zap() { Dsp.Zap(ref z1); Dsp.Zap(ref z2); }
    }

    /// <summary>Linear ramp that arrives exactly at its target (gates, fades). Never steps a gain.</summary>
    public struct Ramp
    {
        public float Value;
        float target, step;
        int left;

        public float Target { get { return target; } }
        public bool Done { get { return left == 0; } }
        public void Jump(float v) { Value = v; target = v; left = 0; step = 0f; }

        public void To(float t, int samples)
        {
            if (samples < 1) { Jump(t); return; }
            target = t; left = samples; step = (t - Value) / samples;
        }

        public float Next()
        {
            if (left > 0)
            {
                Value += step;
                if (--left == 0) Value = target;
            }
            return Value;
        }
    }
}
