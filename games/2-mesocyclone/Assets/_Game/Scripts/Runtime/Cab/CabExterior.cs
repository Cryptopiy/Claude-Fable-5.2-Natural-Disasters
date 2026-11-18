using UnityEngine;

namespace MESOCYCLONE
{
    /// <summary>What is seen of the truck from the driver's seat: the hood (the surface of CabDims.HoodY with a
    /// subtle centre bulge, rolled over at the nose), fender tops, the cowl with the wiper posts, and the pickup
    /// bed behind the rear window. Smooth shared normals everywhere on the paint.</summary>
    public static class CabExterior
    {
        const float Split = 0.88f, NoseR = 0.07f;
        static readonly float[] FenderX = { 0f, 0.03f, 0.06f, 0.085f, 0.10f, 0.107f, 0.11f, 0.11f };
        static readonly float[] FenderY = { 0f, -0.002f, -0.008f, -0.022f, -0.045f, -0.08f, -0.14f, -0.42f };

        static Vector3 V(float x, float y, float z) { return new Vector3(x, y, z); }

        static float Ss(float e0, float e1, float x)
        {
            float t = Mathf.Clamp01((x - e0) / (e1 - e0));
            return t * t * (3f - 2f * t);
        }

        /// <summary>v 0..Split runs from the cowl to the nose; beyond it the surface rolls down around the nose.</summary>
        static void Nose(float v, out float z, out float drop)
        {
            drop = 0f;
            if (v <= Split) { z = Mathf.Lerp(CabDims.HoodZ0, CabDims.HoodZ1, v / Split); return; }
            float a = (v - Split) / (1f - Split) * 80f * Mathf.Deg2Rad;
            z = CabDims.HoodZ1 + NoseR * Mathf.Sin(a);
            drop = NoseR * (1f - Mathf.Cos(a));
        }

        static float HoodSurface(float x, float z)
        {
            float zc = Mathf.Min(z, CabDims.HoodZ1);
            float t = Mathf.InverseLerp(CabDims.HoodZ0, CabDims.HoodZ1, zc);
            float bulge = 0.007f * (1f - Ss(0.20f, 0.34f, Mathf.Abs(x))) * (1f - 0.6f * t);
            return CabDims.HoodY(x, zc) + bulge;
        }

        public static void Build(CabMesh g)
        {
            g.World(); g.mirror = false;
            float hw = CabDims.HoodHalfWidth;

            // ---- hood ----
            g.Mat(CabMesh.Paint);
            g.Toward(V(0f, 6f, 1.8f));
            g.Grid(28, 32, (u, v) =>
            {
                float z, drop;
                Nose(v, out z, out drop);
                float x = Mathf.Lerp(-hw, hw, u);
                return new Vector3(x, HoodSurface(x, z) - drop, z);
            }, 1.72f, 2f);
            // trailing edge of the hood (toward the cab)
            g.Toward(V(0f, 1.3f, 0f));
            g.Grid(14, 1, (u, v) =>
            {
                float x = Mathf.Lerp(-hw, hw, u);
                return new Vector3(x, HoodSurface(x, CabDims.HoodZ0) - 0.045f * v, CabDims.HoodZ0 - 0.002f * v);
            });

            // ---- fenders and the hood's side edges (left, then mirrored) ----
            for (int s = 0; s < 2; s++)
            {
                g.mirror = s == 1;
                g.Mat(CabMesh.Paint);
                g.Toward(V(-3f, 3f, 1.8f));
                g.Grid(7, 24, (u, v) =>
                {
                    float z, drop;
                    Nose(v, out z, out drop);
                    int i = Mathf.Clamp(Mathf.RoundToInt(u * 7f), 0, 7);
                    float edge = CabDims.HoodY(hw, Mathf.Min(z, CabDims.HoodZ1)) - 0.003f - 0.031f * (1f - Ss(0.84f, 0.96f, z));
                    return new Vector3(-(hw + FenderX[i]), edge + FenderY[i] - drop * (i < 7 ? 1f : 0f), z);
                }, 0.3f, 2f);
                g.Toward(V(-3f, 1.1f, 1.8f));
                g.Grid(1, 12, (u, v) =>
                {
                    float z = Mathf.Lerp(CabDims.HoodZ0, CabDims.HoodZ1, v);
                    return new Vector3(-hw, HoodSurface(-hw, z) - 0.05f * u, z);
                });
            }
            g.mirror = false;

            // ---- cowl under the base of the glass, wiper posts ----
            g.Toward(V(0f, 6f, 0.8f));
            g.Mat(CabMesh.Plastic, Sw.Black);
            g.Q(V(-0.90f, 1.125f, 0.775f), V(-0.90f, 1.125f, 0.97f), V(0.90f, 1.125f, 0.97f), V(0.90f, 1.125f, 0.775f));
            for (int i = -14; i <= 14; i++)
                g.Q(V(i * 0.06f - 0.02f, 1.128f, 0.80f), V(i * 0.06f - 0.02f, 1.128f, 0.835f), V(i * 0.06f + 0.02f, 1.128f, 0.835f), V(i * 0.06f + 0.02f, 1.128f, 0.80f));
            g.NoToward();
            Vector3 down = CabLayout.GlassOut * 0.006f;
            g.Cyl(V(CabDims.WiperPivotL.x, 1.12f, CabDims.WiperPivotL.z), CabDims.WiperPivotL - down, 0.014f, 0.011f, 10, false);
            g.Cyl(V(CabDims.WiperPivotR.x, 1.12f, CabDims.WiperPivotR.z), CabDims.WiperPivotR - down, 0.014f, 0.011f, 10, false);

            Bed(g);
            g.NoToward(); g.World();
        }

        static void Bed(CabMesh g)
        {
            float z0 = CabDims.BedZ0, z1 = CabDims.BedZ1, top = CabDims.BedTopY;
            const float fl = 0.78f, xi = 0.82f, xo = 0.96f;
            g.World(); g.mirror = false; g.NoToward();

            // bulkhead behind the cab and tailgate, with rounded top caps
            g.Mat(CabMesh.Paint);
            g.Box(V(0f, (fl + top - 0.03f) * 0.5f, z0 - 0.02f), V(2f * xo, top - 0.03f - fl, 0.04f), 4 | 8);
            g.Box(V(0f, (fl + top - 0.03f) * 0.5f, z1 + 0.025f), V(2f * xo, top - 0.03f - fl, 0.05f), 4 | 8);
            g.Pill(CabMesh.RR(z0 - 0.02f, top - 0.027f, 0.06f, 0.05f, 0.02f, 3), -xo, xo, 0.008f);       // 2 mm under the side rails: no coplanar tops
            g.Pill(CabMesh.RR(z1 + 0.025f, top - 0.027f, 0.07f, 0.05f, 0.02f, 3), -xo, xo, 0.008f);

            // bed floor (dark liner) with pressed ribs
            g.Toward(V(0f, 5f, -2.1f));
            g.Mat(CabMesh.Plastic, Sw.CarDark);
            g.Q(V(-xi, fl, z1), V(-xi, fl, z0), V(xi, fl, z0), V(xi, fl, z1));
            for (int i = -5; i <= 5; i++)
                g.Q(V(i * 0.14f - 0.03f, fl + 0.012f, z1 + 0.06f), V(i * 0.14f - 0.03f, fl + 0.012f, z0 - 0.06f),
                    V(i * 0.14f + 0.03f, fl + 0.012f, z0 - 0.06f), V(i * 0.14f + 0.03f, fl + 0.012f, z1 + 0.06f));

            // side walls: inner faces and the rounded top rails (left, then mirrored)
            for (int s = 0; s < 2; s++)
            {
                g.mirror = s == 1;
                g.World();
                g.Toward(V(0f, 1.1f, -2.1f));
                g.Mat(CabMesh.Paint);
                g.Q(V(-xi, fl, z1), V(-xi, top - 0.03f, z1), V(-xi, top - 0.03f, z0), V(-xi, fl, z0));
                g.NoToward();
                g.Frame(Vector3.zero, Vector3.forward, Vector3.up);          // profile z = -world x
                g.Pill(CabMesh.RR((xi + xo) * 0.5f, top - 0.025f, xo - xi + 0.012f, 0.05f, 0.02f, 3), z1, z0, 0.008f);
            }
            g.mirror = false;
            g.World();
        }
    }
}
