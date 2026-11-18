using UnityEngine;

namespace CASCADE
{
    /// <summary>Small allocation-free math helpers shared by staging, camera and sound.</summary>
    public static class MathX
    {
        public static float Saturate(float x) { return x < 0f ? 0f : x > 1f ? 1f : x; }
        public static float Smooth(float a, float b, float x)
        {
            float t = Saturate((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }
        public static float Remap(float a, float b, float c, float d, float x) { return c + (d - c) * Saturate((x - a) / (b - a)); }

        /// <summary>Monotone cubic interpolation through keys (xs ascending). Clamped outside the range.</summary>
        public static float Curve(float[] xs, float[] ys, float x)
        {
            int n = xs.Length;
            if (x <= xs[0]) return ys[0];
            if (x >= xs[n - 1]) return ys[n - 1];
            int i = 0;
            while (i < n - 2 && x > xs[i + 1]) i++;
            float h = xs[i + 1] - xs[i];
            float d = (ys[i + 1] - ys[i]) / h;
            float m0 = Tangent(xs, ys, i), m1 = Tangent(xs, ys, i + 1);
            // Fritsch-Carlson limiter keeps the curve monotone between keys
            if (Mathf.Abs(d) < 1e-12f) { m0 = 0f; m1 = 0f; }
            else
            {
                float a = m0 / d, b = m1 / d;
                if (a < 0f) { m0 = 0f; a = 0f; }
                if (b < 0f) { m1 = 0f; b = 0f; }
                float r = a * a + b * b;
                if (r > 9f) { float k = 3f / Mathf.Sqrt(r); m0 = k * a * d; m1 = k * b * d; }
            }
            float t = (x - xs[i]) / h, t2 = t * t, t3 = t2 * t;
            return (2f * t3 - 3f * t2 + 1f) * ys[i] + (t3 - 2f * t2 + t) * h * m0 + (-2f * t3 + 3f * t2) * ys[i + 1] + (t3 - t2) * h * m1;
        }

        static float Tangent(float[] xs, float[] ys, int i)
        {
            int n = xs.Length;
            if (i == 0) return (ys[1] - ys[0]) / (xs[1] - xs[0]);
            if (i == n - 1) return (ys[n - 1] - ys[n - 2]) / (xs[n - 1] - xs[n - 2]);
            float a = (ys[i] - ys[i - 1]) / (xs[i] - xs[i - 1]);
            float b = (ys[i + 1] - ys[i]) / (xs[i + 1] - xs[i]);
            return a * b <= 0f ? 0f : 0.5f * (a + b);
        }

        /// <summary>Deterministic hash 0..1.</summary>
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
        public static float Hash(int a, int b) { return Hash(a * 73856093 ^ b * 19349663); }

        /// <summary>Smooth 1D value noise, -1..1.</summary>
        public static float Noise1(float x, int seed)
        {
            int i = Mathf.FloorToInt(x);
            float f = x - i;
            f = f * f * (3f - 2f * f);
            return Mathf.Lerp(Hash(i, seed), Hash(i + 1, seed), f) * 2f - 1f;
        }

        public static Vector3 DirFromYawPitch(float yawDeg, float pitchDeg)
        {
            float y = yawDeg * Mathf.Deg2Rad, p = pitchDeg * Mathf.Deg2Rad;
            float c = Mathf.Cos(p);
            return new Vector3(Mathf.Sin(y) * c, Mathf.Sin(p), Mathf.Cos(y) * c);
        }

        public static void YawPitchFromDir(Vector3 d, out float yawDeg, out float pitchDeg)
        {
            d.Normalize();
            yawDeg = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            pitchDeg = Mathf.Asin(Mathf.Clamp(d.y, -1f, 1f)) * Mathf.Rad2Deg;
        }
    }
}
