using System.Collections.Generic;
using UnityEngine;

namespace CASCADE
{
    /// <summary>The landmark: a white chapel with a barrel roof and a blue dome, and a separate three-tier bell tower. Also its ruin.</summary>
    internal static class ChapelGen
    {
        public const float NaveHalfL = 4.5f, NaveHalfW = 3f, NaveH = 5f, VaultRise = 2f;
        public const float DomeR = 2.6f, DrumR = 2.72f;
        public const float TowerHalf = 1.4f;
        public const float TowerTop = 11.8f;     // top of the tower cross above the chapel floor
        public const float CrossH = 0.9f;        // height of a cross above its foot
        // bell gable at scale 1: lower tier 2.4 m wide, 2.0 m high, 0.32 m thick with two bell arches; its coping; the upper tier
        // with one arch and its coping. GableTop = top of the upper coping (the cross stands on it), in units of the scale.
        const float GableW = 2.4f, GableH1 = 2.0f, GableT = 0.32f, GableCope1 = 0.1f, GableW2 = 1.0f, GableH2 = 1.1f, GableT2 = 0.28f, GableCope2 = 0.08f;
        public const float GableTop = GableH1 + GableCope1 + GableH2 + GableCope2;
        public const float GableHalfW = GableW * 0.5f, GableDepth = GableT;
        static readonly List<Opening> ops = new List<Opening>(4);
        static readonly List<Vector3> v = new List<Vector3>(64), nn = new List<Vector3>(64);
        static readonly List<int> tr = new List<int>(256);

        static Vector3 TowerBase(Vector3 C, Vector3 A, Vector3 B) { return C - A * (NaveHalfL - TowerHalf) + B * (NaveHalfW + 0.7f + TowerHalf); }

        /// <summary>Builds the intact chapel. Returns the centre of the bell.</summary>
        public static Vector3 Build(MeshBuilder mb, Vector3 C, Vector3 A, System.Random rng)
        {
            Vector3 B = Vector3.Cross(Vector3.up, A), up = Vector3.up;
            VWalls.Style(true, false, VKit.Blue, 0x0C4A9E1);          // plaster surrounds, blue joinery
            Plinth(mb, C, A, B, VKit.WhOld);
            // ---- nave walls: round-headed door, window over it and three round-headed windows each side ----
            for (int w = 0; w < 4; w++)
            {
                Vector3 n = w == 0 ? -A : w == 1 ? A : w == 2 ? B : -B;
                Vector3 u = VKit.WallU(n);
                float halfN = w < 2 ? NaveHalfL : NaveHalfW, halfU = w < 2 ? NaveHalfW : NaveHalfL, len = halfU * 2f;
                Vector3 p0 = C + n * halfN - u * halfU;
                ops.Clear();
                if (w == 0)
                {
                    ops.Add(new Opening { t0 = len * 0.5f - 0.7f, t1 = len * 0.5f + 0.7f, y0 = 0f, y1 = 2.4f, kind = 1, arch = true });
                    VWalls.Band(mb, VKit.Wh, p0, u, n, len, 2.7f, ops, rng);
                    ops.Clear();
                    ops.Add(new Opening { t0 = len * 0.5f - 0.35f, t1 = len * 0.5f + 0.35f, y0 = 0.55f, y1 = 1.55f, kind = 0, shutters = 0, glass = VKit.WinA, arch = true });
                    VWalls.Band(mb, VKit.Wh, p0 + up * 2.7f, u, n, len, NaveH - 2.7f, ops, rng);
                }
                else
                {
                    if (w >= 2)
                        for (int i = 0; i < 3; i++)
                        {
                            float tc = len * (0.2f + 0.3f * i);
                            ops.Add(new Opening { t0 = tc - 0.3f, t1 = tc + 0.3f, y0 = 1.7f, y1 = 3.1f, kind = 0, shutters = 0, glass = i == 1 ? VKit.WinB : VKit.WinA, arch = true });
                        }
                    VWalls.Band(mb, VKit.Wh, p0, u, n, len, NaveH, ops, rng);
                }
            }
            VWalls.VaultRoof(mb, VKit.Wh, VKit.Wh, C + up * NaveH, A, B, NaveHalfL, NaveHalfW, VaultRise, 14);
            // apse with a plaster cornice under its half dome
            Vector3 ap = C + A * NaveHalfL;
            mb.Cylinder(VKit.Wh, ap, ap + up * 3.3f, 1.7f, 1.7f, 14, false);
            mb.Cylinder(VKit.Trim, ap + up * 3.2f, ap + up * 3.21f, 1.7f, 1.79f, 14, false);
            mb.Cylinder(VKit.Trim, ap + up * 3.21f, ap + up * 3.3f, 1.79f, 1.79f, 14, false);
            mb.Cylinder(VKit.Trim, ap + up * 3.3f, ap + up * 3.32f, 1.79f, 1.66f, 14, false);
            mb.Dome(VKit.Wh, ap + up * 3.3f, 1.7f, 0.62f, 14, 4);
            // ---- drum with a ring of lit windows, plaster cornice, Santorini-blue dome, cross ----
            Vector3 dc = C + up * (NaveH + 0.5f);
            mb.Cylinder(VKit.Wh, dc, dc + up * 2.4f, DrumR, DrumR, 20, false);
            DrumWindows(mb, dc, 20);
            mb.Cylinder(VKit.Trim, dc + up * 2.39f, dc + up * 2.4f, DrumR, DrumR + 0.14f, 20, false);
            mb.Cylinder(VKit.Trim, dc + up * 2.4f, dc + up * 2.52f, DrumR + 0.14f, DrumR + 0.14f, 20, true);
            Vector3 dt = dc + up * 2.52f;
            mb.Dome(VKit.DomeBlue, dt, DomeR, 1f, 24, 9);
            Cross(mb, dt + up * (DomeR - 0.04f), B);
            Vector3 bell = Tower(mb, TowerBase(C, A, B), A, B, rng);
            VWalls.ResetStyle();
            return bell;
        }

        /// <summary>Ten narrow windows round the chapel's drum, above the vault.</summary>
        static void DrumWindows(MeshBuilder mb, Vector3 dc, int seg) { DrumWindows(mb, dc, seg, DrumR, 1.62f, 2.22f, 0.17f); }

        /// <summary>
        /// Narrow windows round a drum of radius drumR (its vertices) standing on dc: lit glass a little in front of every
        /// other face (seg sides, even, matching MeshBuilder.Cylinder's orientation) from y0 to y1 above dc, half width hw,
        /// each in a plaster frame with a sill. Also used by the rooftop churches (HouseGen).
        /// </summary>
        internal static void DrumWindows(MeshBuilder mb, Vector3 dc, int seg, float drumR, float y0, float y1, float hw)
        {
            Vector3 up = Vector3.up, cu = new Vector3(0f, 0f, -1f), cw = new Vector3(-1f, 0f, 0f);    // Cylinder's u and w for an upright axis
            float face = drumR * Mathf.Cos(Mathf.PI / seg);                                               // distance of a face from the axis
            const float fw = 0.06f;
            for (int k = 0; k < seg; k += 2)
            {
                float ang = (k + 0.5f) * Mathf.PI * 2f / seg;
                Vector3 r = (cu * Mathf.Cos(ang) + cw * Mathf.Sin(ang)).normalized, t = Vector3.Cross(up, r);
                Vector3 c = dc + r * (face + 0.025f);
                Vector3 g0 = c - t * hw + up * y0, g1 = c + t * hw + up * y0, g2 = c + t * hw + up * y1, g3 = c - t * hw + up * y1;
                VKit.Q(mb, (k / 2) % 2 == 0 ? VKit.WinA : VKit.WinB, g0, g1, g2, g3, r);
                Vector3 fc = dc + r * (face + 0.03f);
                VKit.Box(mb, VKit.Trim, fc - t * (hw + fw * 0.5f) + up * ((y0 + y1) * 0.5f), t * (fw * 0.5f), up * ((y1 - y0) * 0.5f), r * 0.03f, 32);
                VKit.Box(mb, VKit.Trim, fc + t * (hw + fw * 0.5f) + up * ((y0 + y1) * 0.5f), t * (fw * 0.5f), up * ((y1 - y0) * 0.5f), r * 0.03f, 32);
                VKit.Box(mb, VKit.Trim, fc + up * (y1 + fw * 0.5f), t * (hw + fw), up * (fw * 0.5f), r * 0.03f, 32);
                VKit.Box(mb, VKit.Trim, fc + r * 0.02f + up * (y0 - 0.035f), t * (hw + fw + 0.03f), up * 0.035f, r * 0.05f, 32);
            }
        }

        static void Plinth(MeshBuilder mb, Vector3 C, Vector3 A, Vector3 B, Material m)
        {
            // floor slab under nave and tower, going into the ground
            Vector3 c = C - A * 0.6f + B * 1.3f - Vector3.up * 0.75f;
            VKit.Box(mb, m, c, A * (NaveHalfL + 1.7f), Vector3.up * 0.75f, B * (NaveHalfW + 2.6f), 8);
        }

        /// <summary>Whitewashed cross, CrossH tall above foot, its arm along arm.</summary>
        internal static void Cross(MeshBuilder mb, Vector3 foot, Vector3 arm)
        {
            Vector3 up = Vector3.up, d = Vector3.Cross(up, arm);
            VKit.Box(mb, VKit.Wh, foot + up * 0.45f, arm * 0.04f, up * 0.45f, d * 0.04f, 8);
            VKit.Box(mb, VKit.Wh, foot + up * 0.62f, arm * 0.28f, up * 0.04f, d * 0.04f);
        }

        /// <summary>Three tiers: a solid base with a door, an open arcade, the belfry with the bell, a cap and a cross.</summary>
        static Vector3 Tower(MeshBuilder mb, Vector3 T, Vector3 A, Vector3 B, System.Random rng)
        {
            Vector3 up = Vector3.up;
            float y1 = 4.6f, y2 = 7.7f, y3 = 10f;
            float[] half = { TowerHalf, 1.2f, 1.0f };
            // tier 1
            for (int w = 0; w < 4; w++)
            {
                Vector3 n = w == 0 ? -A : w == 1 ? A : w == 2 ? B : -B;
                Vector3 u = VKit.WallU(n);
                ops.Clear();
                if (w == 0) ops.Add(new Opening { t0 = TowerHalf - 0.4f, t1 = TowerHalf + 0.4f, y0 = 0f, y1 = 1.9f, kind = 1, arch = true });
                if (w == 2) ops.Add(new Opening { t0 = TowerHalf - 0.25f, t1 = TowerHalf + 0.25f, y0 = 2.6f, y1 = 3.4f, kind = 0, shutters = 0, glass = VKit.WinDark, arch = true });
                VWalls.Band(mb, VKit.Wh, T + n * TowerHalf - u * TowerHalf, u, n, TowerHalf * 2f, y1, ops, rng);
            }
            Slab(mb, T + up * y1, A, B, TowerHalf + 0.15f, 0.16f);
            // tier 2 and 3: arcades
            for (int w = 0; w < 4; w++)
            {
                Vector3 n = w == 0 ? -A : w == 1 ? A : w == 2 ? B : -B;
                Vector3 u = VKit.WallU(n);
                ArchPanel(mb, VKit.Wh, T + up * (y1 + 0.16f) + n * half[1] - u * half[1], u, n, half[1] * 2f, y2 - y1 - 0.16f, 0.34f, 1.1f, 0.3f, 1.35f);
                ArchPanel(mb, VKit.Wh, T + up * (y2 + 0.14f) + n * half[2] - u * half[2], u, n, half[2] * 2f, y3 - y2 - 0.14f, 0.3f, 1.0f, 0.25f, 0.95f);
            }
            Slab(mb, T + up * y2, A, B, half[1] + 0.14f, 0.14f);
            Slab(mb, T + up * y3, A, B, half[2] + 0.12f, 0.12f);
            mb.Dome(VKit.Wh, T + up * (y3 + 0.12f), 0.85f, 0.8f, 12, 4);
            Cross(mb, T + up * (y3 + 0.12f + 0.66f), B);
            // the bell in the belfry
            float by = y2 + 0.14f;
            VKit.Box(mb, VKit.WoodDk, T + up * (by + 1.62f), B * 0.86f, up * 0.05f, A * 0.05f, 1 | 2);
            Vector3 bell = T + up * (by + 1.22f);
            mb.Cylinder(VKit.Iron, bell + up * 0.22f, T + up * (by + 1.58f), 0.022f, 0.022f, 5, false);
            mb.Cylinder(VKit.Bronze, bell - up * 0.25f, bell + up * 0.14f, 0.25f, 0.13f, 10, true);
            mb.Dome(VKit.Bronze, bell + up * 0.14f, 0.13f, 0.75f, 10, 3);
            return bell;
        }

        /// <summary>
        /// Cycladic bell gable (kampanario): a whitewashed screen wall with two round-arched bell openings, a plaster coping, a
        /// smaller arched tier with a third bell, its coping and a cross. foot = centre of its base, face = the side it shows
        /// (horizontal), k = scale (1: 2.4 m wide, GableTop to the upper coping, plus CrossH). Bells in bronze on timber yokes.
        /// </summary>
        internal static void BellGable(MeshBuilder mb, Vector3 foot, Vector3 face, float k, Material wall)
        {
            Vector3 up = Vector3.up, n = face, u;
            n.y = 0f; n.Normalize();
            u = VKit.WallU(n);
            float W = GableW * k, H1 = GableH1 * k, T = GableT * k, half = W * 0.5f, pw = W * 0.5f;
            float ow = 0.62f * k, sill = 0.25f * k, spring = 1.15f * k;
            Vector3 p0 = foot - u * half + n * (T * 0.5f);                 // outer bottom corner of the front face
            for (int i = 0; i < 2; i++) ArchPanel(mb, wall, p0 + u * (pw * i), u, n, pw, H1, T, ow, sill, spring);
            EndCaps(mb, wall, p0, u, n, W, H1, T);
            float c1 = GableCope1 * k;
            VKit.Box(mb, VKit.Trim, foot + up * (H1 + c1 * 0.5f), u * (half + 0.06f * k), up * (c1 * 0.5f), n * (T * 0.5f + 0.05f * k));
            // upper tier
            float W2 = GableW2 * k, H2 = GableH2 * k, T2 = GableT2 * k, y2 = H1 + c1;
            Vector3 q0 = foot + up * y2 - u * (W2 * 0.5f) + n * (T2 * 0.5f);
            ArchPanel(mb, wall, q0, u, n, W2, H2, T2, 0.5f * k, 0.1f * k, 0.62f * k);
            EndCaps(mb, wall, q0, u, n, W2, H2, T2);
            float c2 = GableCope2 * k;
            VKit.Box(mb, VKit.Trim, foot + up * (y2 + H2 + c2 * 0.5f), u * (W2 * 0.5f + 0.05f * k), up * (c2 * 0.5f), n * (T2 * 0.5f + 0.04f * k));
            Cross(mb, foot + up * (GableTop * k), u);
            // bells hang from yokes at the spring of each arch, in the middle of the wall's thickness
            for (int i = 0; i < 2; i++) Bell(mb, foot - u * half + u * (pw * (i + 0.5f)) + up * spring, u, ow, 0.85f * k);
            Bell(mb, foot + up * (y2 + 0.62f * k), u, 0.5f * k, 0.62f * k);
        }

        /// <summary>The narrow end faces of a screen wall built from ArchPanels (p0 = outer bottom corner of its front face).</summary>
        static void EndCaps(MeshBuilder mb, Material m, Vector3 p0, Vector3 u, Vector3 n, float W, float H, float T)
        {
            Vector3 up = Vector3.up * H, b = -n * T;
            VKit.Q(mb, m, p0, p0 + b, p0 + b + up, p0 + up, -u);
            Vector3 e = p0 + u * W;
            VKit.Q(mb, m, e, e + b, e + b + up, e + up, u);
        }

        /// <summary>A bronze bell hanging from a timber yoke that spans an opening of width ow; yoke = centre of the yoke's underside.</summary>
        static void Bell(MeshBuilder mb, Vector3 yoke, Vector3 u, float ow, float k)
        {
            Vector3 up = Vector3.up, d = Vector3.Cross(up, u);
            VKit.Box(mb, VKit.WoodDk, yoke + up * (0.04f * k), u * (ow * 0.5f), up * (0.04f * k), d * (0.045f * k), 1 | 2);
            Vector3 bell = yoke - up * (0.36f * k);
            mb.Cylinder(VKit.Iron, bell + up * (0.22f * k), yoke, 0.02f, 0.02f, 5, false);
            mb.Cylinder(VKit.Bronze, bell - up * (0.25f * k), bell + up * (0.14f * k), 0.25f * k, 0.13f * k, 10, true);
            mb.Dome(VKit.Bronze, bell + up * (0.14f * k), 0.13f * k, 0.75f, 10, 3);
        }

        static void Slab(MeshBuilder mb, Vector3 c, Vector3 A, Vector3 B, float half, float th)
        {
            VKit.Box(mb, VKit.Trim, c + Vector3.up * (th * 0.5f), A * half, Vector3.up * (th * 0.5f), B * half);
        }

        /// <summary>Wall panel (p0 = outer bottom corner, u along, n out) with a round-arched through opening centred in it.</summary>
        static void ArchPanel(MeshBuilder mb, Material m, Vector3 p0, Vector3 u, Vector3 n, float W, float H, float thick, float ow, float sill, float spring)
        {
            const int N = 6;
            float t0 = (W - ow) * 0.5f, t1 = t0 + ow, r = ow * 0.5f, tc = W * 0.5f;
            Vector3 up = Vector3.up;
            for (int face = 0; face < 2; face++)
            {
                Vector3 o = face == 0 ? p0 : p0 - n * thick, d = face == 0 ? n : -n;
                VKit.Q(mb, m, o, o + u * t0, o + u * t0 + up * H, o + up * H, d);
                VKit.Q(mb, m, o + u * t1, o + u * W, o + u * W + up * H, o + u * t1 + up * H, d);
                VKit.Q(mb, m, o + u * t0, o + u * t1, o + u * t1 + up * sill, o + u * t0 + up * sill, d);
                for (int k = 0; k < N; k++)
                {
                    float a0 = Mathf.PI * (1f - k / (float)N), a1 = Mathf.PI * (1f - (k + 1) / (float)N);
                    Vector3 q0 = o + u * (tc + r * Mathf.Cos(a0)) + up * (spring + r * Mathf.Sin(a0)), q1 = o + u * (tc + r * Mathf.Cos(a1)) + up * (spring + r * Mathf.Sin(a1));
                    VKit.Q(mb, m, q0, q1, new Vector3(q1.x, o.y + H, q1.z), new Vector3(q0.x, o.y + H, q0.z), d);
                }
            }
            // reveals of the opening
            Vector3 b = -n * thick;
            VKit.Q(mb, m, p0 + u * t0 + up * sill, p0 + u * t0 + up * sill + b, p0 + u * t0 + up * spring + b, p0 + u * t0 + up * spring, u);
            VKit.Q(mb, m, p0 + u * t1 + up * sill, p0 + u * t1 + up * sill + b, p0 + u * t1 + up * spring + b, p0 + u * t1 + up * spring, -u);
            VKit.Q(mb, m, p0 + u * t0 + up * sill, p0 + u * t1 + up * sill, p0 + u * t1 + up * sill + b, p0 + u * t0 + up * sill + b, up);
            Vector3 cen = p0 + u * tc + up * spring;
            for (int k = 0; k < N; k++)
            {
                float a0 = Mathf.PI * (1f - k / (float)N), a1 = Mathf.PI * (1f - (k + 1) / (float)N);
                Vector3 q0 = cen + u * (r * Mathf.Cos(a0)) + up * (r * Mathf.Sin(a0)), q1 = cen + u * (r * Mathf.Cos(a1)) + up * (r * Mathf.Sin(a1));
                VKit.Q(mb, m, q0, q1, q1 + b, q0 + b, cen - (q0 + q1) * 0.5f);
            }
        }

        /// <summary>The ruin: nave walls as a roofless shell, the tower stump, half the dome drum fallen into the nave, a shard of the blue dome.</summary>
        public static void BuildRuin(MeshBuilder mb, Vector3 C, Vector3 A, System.Random rng)
        {
            Vector3 B = Vector3.Cross(Vector3.up, A), up = Vector3.up;
            Plinth(mb, C, A, B, VKit.Wet);
            for (int w = 0; w < 4; w++)
            {
                Vector3 n = w == 0 ? -A : w == 1 ? A : w == 2 ? B : -B;
                Vector3 u = VKit.WallU(n);
                float halfN = w < 2 ? NaveHalfL : NaveHalfW, halfU = w < 2 ? NaveHalfW : NaveHalfL;
                float trim = w < 2 ? 0f : RuinGen.T;
                RuinGen.Pieces(mb, C + n * halfN - u * (halfU - trim), u, n, halfU * 2f - trim * 2f, 4.4f, 2, rng);
            }
            VKit.Q(mb, VKit.Ruin, C - A * 4f - B * 2.5f + up * 0.04f, C + A * 4f - B * 2.5f + up * 0.04f, C + A * 4f + B * 2.5f + up * 0.04f, C - A * 4f + B * 2.5f + up * 0.04f, up);
            // tower stump: the solid tier survives, the arcade is broken off
            Vector3 T = TowerBase(C, A, B);
            float y1 = 4.6f;
            VKit.Box(mb, VKit.Wet, T + up * (y1 * 0.5f), A * TowerHalf, up * (y1 * 0.5f), B * TowerHalf, 4 | 8);
            VKit.Q(mb, VKit.Ruin, T + up * y1 - A * TowerHalf - B * TowerHalf, T + up * y1 + A * TowerHalf - B * TowerHalf, T + up * y1 + A * TowerHalf + B * TowerHalf, T + up * y1 - A * TowerHalf + B * TowerHalf, up);
            for (int w = 0; w < 4; w++)
            {
                Vector3 n = w == 0 ? -A : w == 1 ? A : w == 2 ? B : -B;
                Vector3 u = VKit.WallU(n);
                float trim = w < 2 ? 0f : RuinGen.T;
                RuinGen.Pieces(mb, T + up * y1 + n * 1.2f - u * (1.2f - trim), u, n, 2.4f - trim * 2f, 2.6f, 0, rng);
            }
            // half the drum, dropped onto the floor at the east end, and a shard of the dome leaning in it
            Vector3 dc = C + A * 1.2f + up * 0.05f;
            const int segs = 7;
            for (int i = 0; i < segs; i++)
            {
                float a0 = Mathf.PI * (i / (float)segs) - Mathf.PI * 0.5f, a1 = Mathf.PI * ((i + 1) / (float)segs) - Mathf.PI * 0.5f;
                Vector3 q0 = dc + A * (Mathf.Cos(a0) * DrumR) + B * (Mathf.Sin(a0) * DrumR), q1 = dc + A * (Mathf.Cos(a1) * DrumR) + B * (Mathf.Sin(a1) * DrumR);
                Vector3 inw = (dc - (q0 + q1) * 0.5f).normalized * 0.36f;
                RuinGen.Stump(mb, q0, q1 - q0, inw, VKit.Rnd(rng, 0.7f, 1.5f), VKit.Rnd(rng, 0.7f, 1.5f));
            }
            Vector3 r, u2, f;
            VKit.Axes(HouseYaw(A) + 25f, 24f, -18f, out r, out u2, out f);
            DomeShard(mb, dc + A * 0.5f - B * 0.3f - u2 * 1.25f, r, u2, f, DomeR, 105f, VKit.DomeBlue);
            // rubble and timber
            for (int i = 0; i < 6; i++)
                VKit.Mound(mb, VKit.Ruin, C + A * VKit.Rnd(rng, -4.8f, 3.2f) + B * VKit.Rnd(rng, -3.4f, 5.6f) + up * 0.03f, VKit.Rnd(rng, 1f, 2.2f), VKit.Rnd(rng, 0.8f, 1.6f), VKit.Rnd(rng, 0.4f, 1f), VKit.Rnd(rng, 0f, 180f), rng, 8);
            for (int i = 0; i < 5; i++)
                VKit.BoxTilt(mb, VKit.WoodDk, C + A * VKit.Rnd(rng, -3f, 3f) + B * VKit.Rnd(rng, -2f, 2f) + up * VKit.Rnd(rng, 0.3f, 1.3f), 0.15f, 0.15f, VKit.Rnd(rng, 2.2f, 4.5f), VKit.Rnd(rng, 0f, 360f), VKit.Rnd(rng, -30f, 30f), VKit.Rnd(rng, 0f, 90f));
            VKit.Mound(mb, VKit.Mud, C - A * 6.4f + B * 1.5f + up * 0.02f, 1.6f, 3.4f, 0.4f, HouseYaw(A), rng, 9);
        }

        static float HouseYaw(Vector3 d) { return Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg; }

        /// <summary>A piece of the dome shell (both sides: painted outside, broken plaster inside), spanning azDeg of azimuth, in the frame r/u/f.</summary>
        internal static void DomeShard(MeshBuilder mb, Vector3 c, Vector3 r, Vector3 u, Vector3 f, float radius, float azDeg, Material outside)
        {
            const int seg = 6, rings = 4;
            for (int pass = 0; pass < 2; pass++)
            {
                v.Clear(); nn.Clear(); tr.Clear();
                for (int i = 0; i <= rings; i++)
                {
                    float el = Mathf.Deg2Rad * Mathf.Lerp(8f, 78f, i / (float)rings);
                    for (int s = 0; s <= seg; s++)
                    {
                        float az = Mathf.Deg2Rad * azDeg * (s / (float)seg - 0.5f);
                        Vector3 d = r * (Mathf.Sin(az) * Mathf.Cos(el)) + u * Mathf.Sin(el) + f * (Mathf.Cos(az) * Mathf.Cos(el));
                        v.Add(c + d * radius); nn.Add(pass == 0 ? d : -d);
                    }
                }
                for (int i = 0; i < rings; i++)
                    for (int s = 0; s < seg; s++)
                    {
                        int p = i * (seg + 1) + s, q = p + seg + 1;
                        Vector3 fn = Vector3.Cross(v[q] - v[p], v[p + 1] - v[p]);
                        bool ok = Vector3.Dot(fn, nn[p]) >= 0f;
                        if (ok) { tr.Add(p); tr.Add(q); tr.Add(p + 1); tr.Add(p + 1); tr.Add(q); tr.Add(q + 1); }
                        else { tr.Add(p); tr.Add(p + 1); tr.Add(q); tr.Add(p + 1); tr.Add(q + 1); tr.Add(q); }
                    }
                mb.Raw(pass == 0 ? outside : VKit.Ruin, v, nn, null, tr);
            }
        }
    }
}
