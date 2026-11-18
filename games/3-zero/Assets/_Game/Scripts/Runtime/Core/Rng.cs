using UnityEngine;

namespace ZERO
{
    /// <summary>Small deterministic PRNG (xorshift32). All randomness in the game is seeded through this.</summary>
    public struct Rng
    {
        uint s;
        public Rng(uint seed) { s = seed * 747796405u + 2891336453u; if (s == 0) s = 1; Next(); Next(); }
        public uint Next() { s ^= s << 13; s ^= s >> 17; s ^= s << 5; return s; }
        /// <summary>[0,1)</summary>
        public float F() { return (Next() & 0xFFFFFF) / 16777216f; }
        public float Range(float a, float b) { return a + (b - a) * F(); }
        public int Int(int a, int bExclusive) { return a + (int)(Next() % (uint)Mathf.Max(1, bExclusive - a)); }
        public float Signed() { return F() * 2f - 1f; }
        public bool Chance(float p) { return F() < p; }
        public Vector3 InSphere()
        {
            for (int i = 0; i < 16; i++)
            {
                var v = new Vector3(Signed(), Signed(), Signed());
                if (v.sqrMagnitude <= 1f) return v;
            }
            return Vector3.zero;
        }
        public Vector3 OnSphere() { var v = InSphere(); return v.sqrMagnitude < 1e-6f ? Vector3.up : v.normalized; }
        public Vector2 InCircle()
        {
            for (int i = 0; i < 16; i++)
            {
                var v = new Vector2(Signed(), Signed());
                if (v.sqrMagnitude <= 1f) return v;
            }
            return Vector2.zero;
        }
        public Quaternion Rotation() { return Quaternion.AngleAxis(F() * 360f, OnSphere()); }
        /// <summary>Approximate gaussian (mean 0, sigma 1).</summary>
        public float Gauss() { return (F() + F() + F() + F() - 2f) * 1.7320508f; }
    }
}
