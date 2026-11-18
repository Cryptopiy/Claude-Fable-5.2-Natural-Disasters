using System;

namespace CASCADE
{
    /// <summary>RBJ cookbook biquad, transposed direct form 2, double precision (stable at 20 Hz corners).</summary>
    public struct Biquad
    {
        double b0, b1, b2, a1, a2, z1, z2;

        public void Clear() { z1 = 0.0; z2 = 0.0; }

        public float Next(float x)
        {
            double y = b0 * x + z1;
            z1 = b1 * x - a1 * y + z2 + 1e-30;
            z2 = b2 * x - a2 * y;
            return (float)y;
        }

        static void Prep(int fs, double f, double q, out double cs, out double alpha)
        {
            if (f < 5.0) f = 5.0;
            if (f > fs * 0.45) f = fs * 0.45;
            double w = Dsp.Tau * f / fs;
            cs = Math.Cos(w);
            alpha = Math.Sin(w) / (2.0 * q);
        }

        void Norm(double nb0, double nb1, double nb2, double a0, double na1, double na2)
        {
            double k = 1.0 / a0;
            b0 = nb0 * k; b1 = nb1 * k; b2 = nb2 * k; a1 = na1 * k; a2 = na2 * k;
        }

        public void LowPass(int fs, double f, double q)
        {
            double c, al; Prep(fs, f, q, out c, out al);
            Norm((1 - c) * 0.5, 1 - c, (1 - c) * 0.5, 1 + al, -2 * c, 1 - al);
        }

        public void HighPass(int fs, double f, double q)
        {
            double c, al; Prep(fs, f, q, out c, out al);
            Norm((1 + c) * 0.5, -(1 + c), (1 + c) * 0.5, 1 + al, -2 * c, 1 - al);
        }

        /// <summary>Band-pass with 0 dB peak gain.</summary>
        public void BandPass(int fs, double f, double q)
        {
            double c, al; Prep(fs, f, q, out c, out al);
            Norm(al, 0.0, -al, 1 + al, -2 * c, 1 - al);
        }

        public void Notch(int fs, double f, double q)
        {
            double c, al; Prep(fs, f, q, out c, out al);
            Norm(1.0, -2 * c, 1.0, 1 + al, -2 * c, 1 - al);
        }

        public void Peak(int fs, double f, double q, double gainDb)
        {
            double c, al; Prep(fs, f, q, out c, out al);
            double A = Math.Pow(10.0, gainDb / 40.0);
            Norm(1 + al * A, -2 * c, 1 - al * A, 1 + al / A, -2 * c, 1 - al / A);
        }

        public void LowShelf(int fs, double f, double gainDb)
        {
            double c, al; Prep(fs, f, 0.7071, out c, out al);
            double A = Math.Pow(10.0, gainDb / 40.0), s = 2.0 * Math.Sqrt(A) * al;
            Norm(A * ((A + 1) - (A - 1) * c + s), 2 * A * ((A - 1) - (A + 1) * c), A * ((A + 1) - (A - 1) * c - s),
                 (A + 1) + (A - 1) * c + s, -2 * ((A - 1) + (A + 1) * c), (A + 1) + (A - 1) * c - s);
        }

        public void HighShelf(int fs, double f, double gainDb)
        {
            double c, al; Prep(fs, f, 0.7071, out c, out al);
            double A = Math.Pow(10.0, gainDb / 40.0), s = 2.0 * Math.Sqrt(A) * al;
            Norm(A * ((A + 1) + (A - 1) * c + s), -2 * A * ((A - 1) + (A + 1) * c), A * ((A + 1) + (A - 1) * c - s),
                 (A + 1) - (A - 1) * c + s, 2 * ((A - 1) - (A + 1) * c), (A + 1) - (A - 1) * c - s);
        }
    }

    /// <summary>
    /// Topology-preserving state-variable filter (Zavalishin / Simper). Safe to retune every control block,
    /// so every moving cutoff in the score uses it. Call Tick, then read low / band / high.
    /// </summary>
    public struct Svf
    {
        double g, k, a1, a2, a3, ic1, ic2;
        public float low, band, high;

        public void Clear() { ic1 = 0.0; ic2 = 0.0; low = band = high = 0f; }

        public void Set(int fs, float fc, float q)
        {
            if (fc < 8f) fc = 8f;
            float max = fs * 0.45f;
            if (fc > max) fc = max;
            g = Math.Tan(Math.PI * fc / fs);
            k = 1.0 / q;
            a1 = 1.0 / (1.0 + g * (g + k));
            a2 = g * a1;
            a3 = g * a2;
        }

        public void Tick(float x)
        {
            double v3 = x - ic2;
            double v1 = a1 * ic1 + a2 * v3;
            double v2 = ic2 + a2 * ic1 + a3 * v3;
            ic1 = 2.0 * v1 - ic1 + 1e-30;
            ic2 = 2.0 * v2 - ic2;
            low = (float)v2;
            band = (float)(v1 * k);                 // 0 dB peak
            high = (float)(x - k * v1 - v2);
        }

        public float Lp(float x) { Tick(x); return low; }
        public float Bp(float x) { Tick(x); return band; }
        public float Hp(float x) { Tick(x); return high; }
    }
}
