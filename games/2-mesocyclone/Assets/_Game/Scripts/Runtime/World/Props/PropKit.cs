using UnityEngine;

namespace MESOCYCLONE
{
    /// <summary>Small reusable shapes: trees, cars, a tractor, ruins. All of them write into a MeshBuilder; the
    /// "p" arguments are points in the builder's (chunk) space, yaw is a world heading in degrees.</summary>
    public static class PropKit
    {
        /// <summary>A tree of total height h standing at p: trunk and 2-3 squashed spheres. Stripped = a bare,
        /// leaning trunk with two branch stubs (the damage swath).</summary>
        public static void Tree(MeshBuilder mb, Vector3 p, float h, ref Rng r, bool stripped)
        {
            float yaw = r.Range(0f, 360f);
            float lean = stripped ? r.Range(5f, 24f) : r.Range(0f, 3f);
            mb.Push(p, Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(lean, 0f, 0f));
            mb.Swatch(Sw.Trunk);
            float th = stripped ? h * r.Range(0.4f, 0.75f) : h * 0.5f;
            float r0 = 0.016f * h + 0.10f, r1 = 0.008f * h + 0.06f;
            mb.Cylinder(Vector3.zero, new Vector3(0f, th, 0f), r0, r1, 5, stripped);
            if (stripped)
            {
                float by = th * r.Range(0.5f, 0.7f);
                mb.Cylinder(new Vector3(0f, by, 0f), new Vector3(0.16f * h, by + 0.12f * h, 0.05f * h), r1, 0.04f, 4, false);
                by = th * r.Range(0.75f, 0.92f);
                mb.Cylinder(new Vector3(0f, by, 0f), new Vector3(-0.12f * h, by + 0.1f * h, -0.08f * h), r1 * 0.9f, 0.04f, 4, false);
                mb.Reset();
                return;
            }
            // the crown: a lumpy main mass, a lumpy top, sometimes a side mass, and small lobes around the rim that
            // break the outline (the draws on the shared rng are the same as before, so the land layout is unchanged)
            mb.Swatch(r.Chance(0.5f) ? Sw.Tree : Sw.DarkGreen);
            float ox = r.Range(-0.08f, 0.08f) * h, oz = r.Range(-0.08f, 0.08f) * h;
            bool side = r.Chance(0.55f);
            float sx = side ? r.Range(-0.16f, 0.16f) * h : 0f, sz = side ? r.Range(-0.16f, 0.16f) * h : 0f;
            uint seed = (uint)(int)(yaw * 977f) ^ (uint)(int)(h * 7919f) ^ (uint)(int)(p.x * 131f) ^ (uint)(int)(p.z * 197f);
            mb.LumpySphere(new Vector3(0f, 0.54f * h, 0f), 0.31f * h, 11, 7, 0.78f, seed, 0.24f);
            mb.LumpySphere(new Vector3(ox, 0.75f * h, oz), 0.23f * h, 9, 6, 1.0f, seed + 1u, 0.28f);
            if (side) mb.LumpySphere(new Vector3(sx, 0.6f * h, sz), 0.21f * h, 9, 6, 0.88f, seed + 2u, 0.28f);
            var lr = new Rng(seed | 1u);
            int lobes = 3 + (int)(seed % 3u);
            for (int k = 0; k < lobes; k++)
            {
                float a = (k + lr.Range(0f, 0.7f)) / lobes * Mathf.PI * 2f, rr = lr.Range(0.22f, 0.3f) * h;
                mb.LumpySphere(new Vector3(Mathf.Cos(a) * rr, lr.Range(0.46f, 0.7f) * h, Mathf.Sin(a) * rr), lr.Range(0.11f, 0.16f) * h, 8, 5, 0.85f, seed + 3u + (uint)k, 0.3f);
            }
            mb.Reset();
        }

        /// <summary>An empty car in the current frame (origin on the ground under its centre, +Z forward).
        /// 4.6 m saloon, or a 5.3 m pickup. No brand, no markings.</summary>
        /// <summary>Body colour with the clear-coated car paint class (Tan or dark green would otherwise read as siding or leaves).</summary>
        static void Paint(MeshBuilder mb, int paint) { mb.Swatch(paint); mb.Surface(Surf.CarPaint); }

        public static void Car(MeshBuilder mb, int paint, bool pickup)
        {
            float L = pickup ? 5.3f : 4.6f, W = 1.82f;
            Paint(mb, paint);
            mb.Box(new Vector3(0f, 0.66f, 0f), new Vector3(W, 0.56f, L));
            if (!pickup)
            {
                mb.Swatch(Sw.GlassDark);
                mb.Box(new Vector3(0f, 1.17f, -0.25f), new Vector3(W - 0.20f, 0.46f, 2.4f), 4 | 8);
                Paint(mb, paint);
                mb.Box(new Vector3(0f, 1.43f, -0.25f), new Vector3(W - 0.14f, 0.07f, 2.3f));
            }
            else
            {
                mb.Swatch(Sw.GlassDark);
                mb.Box(new Vector3(0f, 1.20f, 0.55f), new Vector3(W - 0.20f, 0.52f, 1.7f), 4 | 8);
                Paint(mb, paint);
                mb.Box(new Vector3(0f, 1.49f, 0.55f), new Vector3(W - 0.14f, 0.07f, 1.6f));
                mb.Box(new Vector3(W * 0.5f - 0.06f, 1.08f, -1.55f), new Vector3(0.10f, 0.28f, 2.1f), 4);
                mb.Box(new Vector3(-W * 0.5f + 0.06f, 1.08f, -1.55f), new Vector3(0.10f, 0.28f, 2.1f), 4);
                mb.Box(new Vector3(0f, 1.08f, -L * 0.5f + 0.06f), new Vector3(W - 0.24f, 0.28f, 0.10f), 4);
            }
            mb.Swatch(Sw.Tyre);
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    float z = sz * L * 0.31f;
                    mb.Cylinder(new Vector3(sx * 0.68f, 0.34f, z), new Vector3(sx * (W * 0.5f + 0.02f), 0.34f, z), 0.34f, 0.34f, 12, true);
                    mb.Swatch(Sw.Aluminium);                                                 // wheel disc
                    mb.Cylinder(new Vector3(sx * (W * 0.5f + 0.02f), 0.34f, z), new Vector3(sx * (W * 0.5f + 0.03f), 0.34f, z), 0.2f, 0.17f, 12, true);
                    mb.Swatch(Sw.Tyre);
                }
            // bumpers, grille, side mirrors
            mb.Swatch(Sw.Grey20);
            mb.Box(new Vector3(0f, 0.48f, L * 0.5f + 0.04f), new Vector3(W + 0.02f, 0.16f, 0.1f));
            mb.Box(new Vector3(0f, 0.48f, -L * 0.5f - 0.04f), new Vector3(W + 0.02f, 0.16f, 0.1f));
            mb.Swatch(Sw.Black);
            mb.Box(new Vector3(0f, 0.74f, L * 0.5f + 0.012f), new Vector3(0.78f, 0.18f, 0.03f));
            mb.Box(new Vector3(W * 0.5f + 0.06f, 1.08f, pickup ? 1.32f : 0.86f), new Vector3(0.1f, 0.12f, 0.05f));
            mb.Box(new Vector3(-W * 0.5f - 0.06f, 1.08f, pickup ? 1.32f : 0.86f), new Vector3(0.1f, 0.12f, 0.05f));
            mb.Swatch(Sw.WarmWhite);
            mb.Box(new Vector3(0.62f, 0.76f, L * 0.5f + 0.01f), new Vector3(0.36f, 0.16f, 0.05f));
            mb.Box(new Vector3(-0.62f, 0.76f, L * 0.5f + 0.01f), new Vector3(0.36f, 0.16f, 0.05f));
            mb.Swatch(Sw.DeepRed);
            mb.Box(new Vector3(0.66f, 0.80f, -L * 0.5f - 0.01f), new Vector3(0.30f, 0.16f, 0.05f));
            mb.Box(new Vector3(-0.66f, 0.80f, -L * 0.5f - 0.01f), new Vector3(0.30f, 0.16f, 0.05f));
        }

        /// <summary>A very simple farm tractor in the current frame (+Z forward).</summary>
        public static void Tractor(MeshBuilder mb)
        {
            mb.Swatch(Sw.DarkGreen);
            mb.Surface(Surf.Paint);
            mb.Box(new Vector3(0f, 1.25f, 0.9f), new Vector3(0.9f, 0.8f, 2.2f));
            mb.Box(new Vector3(0f, 2.75f, -0.75f), new Vector3(1.45f, 0.1f, 1.5f));
            mb.Swatch(Sw.GlassDark);
            mb.Box(new Vector3(0f, 2.0f, -0.75f), new Vector3(1.3f, 1.4f, 1.3f), 8);
            mb.Swatch(Sw.Tyre);
            for (int sx = -1; sx <= 1; sx += 2)
            {
                mb.Cylinder(new Vector3(sx * 0.62f, 0.85f, -0.8f), new Vector3(sx * 1.08f, 0.85f, -0.8f), 0.85f, 0.85f, 10, true);
                mb.Cylinder(new Vector3(sx * 0.55f, 0.5f, 1.55f), new Vector3(sx * 0.85f, 0.5f, 1.55f), 0.5f, 0.5f, 8, true);
            }
            mb.Swatch(Sw.Grey20);
            mb.Cylinder(new Vector3(0.3f, 1.65f, 1.5f), new Vector3(0.3f, 2.5f, 1.5f), 0.05f, 0.05f, 5, true);
        }

        /// <summary>What is left of a building: a low heap of slabs and broken boards on a w x d footprint
        /// (never higher than 1.5 m).</summary>
        public static void Ruin(MeshBuilder mb, Vector3 p, float yaw, float w, float d, int pieces, int swA, int swB, ref Rng r)
        {
            Quaternion q0 = Quaternion.Euler(0f, yaw, 0f);
            mb.Push(p, q0);
            mb.Swatch(Sw.Concrete);
            mb.Box(new Vector3(0f, 0.09f, 0f), new Vector3(w * 0.92f, 0.18f, d * 0.92f), 4);
            for (int i = 0; i < pieces; i++)
            {
                float sx = r.Range(1.2f, Mathf.Clamp(w * 0.5f, 1.6f, 4f));
                float sz = r.Range(0.5f, Mathf.Clamp(d * 0.35f, 0.8f, 2.6f));
                float sy = r.Range(0.12f, 0.3f);
                var o = new Vector3(r.Range(-0.48f, 0.48f) * w, r.Range(0.2f, 0.62f), r.Range(-0.48f, 0.48f) * d);
                Quaternion q = q0 * Quaternion.Euler(r.Range(-15f, 15f), r.Range(0f, 180f), r.Range(-15f, 15f));
                mb.Push(p + q0 * o, q);
                int k = i % 3;
                mb.Swatch(k == 0 ? swA : (k == 1 ? swB : Sw.DebrisWood));
                mb.Box(Vector3.zero, new Vector3(sx, sy, sz));
            }
            mb.Reset();
        }

        /// <summary>A lamp on a pole in the current frame: pole at the origin, arm toward local +X.
        /// Returns nothing; the light itself is a LightDots dot placed by the caller at (arm, height - 0.15, 0).</summary>
        public static void LampPole(MeshBuilder mb, float height, float arm)
        {
            mb.Swatch(Sw.Grey40);
            mb.Cylinder(Vector3.zero, new Vector3(0f, height, 0f), 0.10f, 0.07f, 5, false);
            mb.Box(new Vector3(arm * 0.5f, height - 0.05f, 0f), new Vector3(arm + 0.1f, 0.09f, 0.09f));
            mb.Swatch(Sw.Grey20);
            mb.Box(new Vector3(arm, height - 0.02f, 0f), new Vector3(0.62f, 0.16f, 0.3f));
        }
    }
}
