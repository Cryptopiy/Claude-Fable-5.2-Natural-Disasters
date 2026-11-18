using System.Collections.Generic;
using UnityEngine;

namespace CASCADE
{
    /// <summary>An opening in one storey of a wall. t along the wall, y above the storey base, metres.</summary>
    internal struct Opening
    {
        public float t0, t1, y0, y1;
        public int kind;          // 0 window, 1 closed door, 2 open door (dark recess, leaf swung out)
        public int shutters;      // windows: 0 none, 1 open either side, 2 closed
        public Material glass;
        public bool arch;         // round head: the top half-width of the opening is a half circle
    }

    /// <summary>Walls with real recessed openings, roofs, vaults and stairs: the vocabulary of the white houses.</summary>
    internal static class VWalls
    {
        public const float WindowW = 0.8f, WindowH = 1.1f, Sill = 0.9f, DoorW = 0.9f, DoorH = 2.0f;
        public const float Reveal = 0.14f;        // depth of a window recess (the white reveal)
        public const float ParapetH = 0.5f, ParapetT = 0.24f;
        public const float StepRise = 0.18f;
        public const float TrimW = 0.1f, TrimOut = 0.03f;   // plaster surrounds of doors and windows: width, and how far they stand proud
        public const float CopeH = 0.085f;                  // crown of a parapet coping above the parapet top
        public const float DoorStep = 0.12f;                // dressed doors stand one stone step above the floor
        const int ArchSeg = 6;

        // ---- the style of the house being built (HouseGen and ChapelGen set it, ResetStyle clears it) ----
        static bool trim, lite, swing = true;
        static Material paint;
        static System.Random dr;
        static float boxChance;
        static readonly List<Vector2> prof = new List<Vector2>(12);
        static readonly List<Vector3> loop = new List<Vector3>(8), pts = new List<Vector3>(8);

        /// <summary>Painted joinery of the current house: shutters, door leaves, mullions.</summary>
        public static Material Paint { get { return paint != null ? paint : VKit.Blue; } }
        public static bool Trim { get { return trim; } }
        public static bool Lite { get { return lite; } }

        /// <summary>
        /// Dressing of the walls built next: plaster surrounds, sills and door steps (trimOn), painted joinery in paintMat,
        /// flower boxes under some windows. liteDetail = the house is far from the path (fewer faces on small things).
        /// seed drives the dressing choices only, never the plan. swingLeaves = false keeps every open shutter flat on its wall
        /// (houses standing close behind other walls).
        /// </summary>
        public static void Style(bool trimOn, bool liteDetail, Material paintMat, int seed, float windowBoxChance = 0f, bool swingLeaves = true)
        {
            trim = trimOn; lite = liteDetail; paint = paintMat; dr = new System.Random(seed); boxChance = windowBoxChance; swing = swingLeaves;
        }

        public static void ResetStyle() { trim = false; lite = false; paint = null; dr = null; boxChance = 0f; swing = true; }

        static float DR() { return dr != null ? (float)dr.NextDouble() : 0.5f; }

        static Vector3 P(Vector3 p0, Vector3 u, Vector3 n, float t, float y, float off) { return p0 + u * t + Vector3.up * y + n * off; }

        static void WQ(MeshBuilder mb, Material m, Vector3 p0, Vector3 u, Vector3 n, float t0, float t1, float y0, float y1, float off = 0f)
        {
            if (t1 - t0 < 0.005f || y1 - y0 < 0.005f) return;
            VKit.Q(mb, m, P(p0, u, n, t0, y0, off), P(p0, u, n, t1, y0, off), P(p0, u, n, t1, y1, off), P(p0, u, n, t0, y1, off), n);
        }

        /// <summary>
        /// One storey of a wall: p0 is the bottom corner, u runs along the wall (u x up = n), n is the outward normal.
        /// Openings must be sorted by t and must not overlap; they are cut out of the wall and built as recesses.
        /// </summary>
        public static void Band(MeshBuilder mb, Material wall, Vector3 p0, Vector3 u, Vector3 n, float len, float h, List<Opening> ops, System.Random rng)
        {
            if (ops == null || ops.Count == 0) { WQ(mb, wall, p0, u, n, 0f, len, 0f, h); return; }
            float top = 0f;
            for (int i = 0; i < ops.Count; i++) top = Mathf.Max(top, ops[i].y1);
            WQ(mb, wall, p0, u, n, 0f, len, top, h);                         // above the lintels
            float t = 0f;
            for (int i = 0; i < ops.Count; i++)
            {
                Opening o = ops[i];
                WQ(mb, wall, p0, u, n, t, o.t0, 0f, top);                   // pier
                WQ(mb, wall, p0, u, n, o.t0, o.t1, 0f, o.y0);               // below the sill
                WQ(mb, wall, p0, u, n, o.t0, o.t1, o.y1, top);
                Recess(mb, wall, p0, u, n, o, rng, len);
                t = o.t1;
            }
            WQ(mb, wall, p0, u, n, t, len, 0f, top);
        }

        static void Recess(MeshBuilder mb, Material wall, Vector3 p0, Vector3 u, Vector3 n, Opening o, System.Random rng, float len)
        {
            float e = o.kind == 2 ? 0.9f : o.kind == 1 ? 0.12f : Reveal;
            Material side = o.kind == 2 ? VKit.Ruin : wall;
            Material back = o.kind == 0 ? o.glass : o.kind == 1 ? Paint : VKit.Ruin;
            Vector3 a0 = P(p0, u, n, o.t0, o.y0, 0f), a1 = P(p0, u, n, o.t1, o.y0, 0f), a2 = P(p0, u, n, o.t1, o.y1, 0f), a3 = P(p0, u, n, o.t0, o.y1, 0f);
            Vector3 d = n * -e;
            Vector3 b0 = a0 + d, b1 = a1 + d, b2 = a2 + d, b3 = a3 + d;
            VKit.Q(mb, side, a0, b0, b3, a3, u);                 // jamb at t0
            VKit.Q(mb, side, a1, b1, b2, a2, -u);                // jamb at t1
            Vector3 lift = Vector3.up * (o.kind == 0 || o.y0 > 0.06f ? 0f : 0.02f);   // a threshold on the floor lies a finger above the slab; a raised one meets its step
            VKit.Q(mb, side, a0 + lift, a1 + lift, b1 + lift, b0 + lift, Vector3.up);        // sill or threshold
            VKit.Q(mb, side, a3, a2, b2, b3, Vector3.down);      // head
            bool closedShutters = o.kind == 0 && o.shutters == 2;
            if (!closedShutters) VKit.Q(mb, back, b0, b1, b2, b3, n);
            float w = o.t1 - o.t0, hh = o.y1 - o.y0, ym = (o.y0 + o.y1) * 0.5f, tc = (o.t0 + o.t1) * 0.5f;
            bool arch = o.arch && hh > w * 0.5f + 0.25f;
            float ys = arch ? o.y1 - w * 0.5f : o.y1;           // spring line of an arched head
            if (arch) ArchHead(mb, wall, side, p0, u, n, o.t0, o.t1, o.y1, e);
            Vector3 hy = Vector3.up * (hh * 0.5f);
            if (o.kind == 0)
            {
                if (closedShutters)
                {
                    // two leaves closing the opening, a little inside the wall face
                    VKit.Box(mb, Paint, P(p0, u, n, o.t0 + w * 0.25f, ym, -0.04f), u * (w * 0.25f - 0.008f), hy, n * 0.02f, 32);
                    VKit.Box(mb, Paint, P(p0, u, n, o.t1 - w * 0.25f, ym, -0.04f), u * (w * 0.25f - 0.008f), hy, n * 0.02f, 32);
                }
                else
                {
                    // glazing bar in front of the pane (painted on a dressed house), and a transom on arched and tall ones
                    Material bar = trim ? Paint : wall;
                    VKit.Box(mb, bar, P(p0, u, n, tc, ym, -e + 0.02f), u * 0.03f, hy, n * 0.02f, 4 | 8 | 32);
                    if (trim && !lite && (arch || hh > 1.6f))
                        VKit.Box(mb, bar, P(p0, u, n, tc, arch ? ys : o.y0 + hh * 0.7f, -e + 0.02f), u * (w * 0.5f - 0.004f), Vector3.up * 0.025f, n * 0.02f, 1 | 2 | 32);
                    if (o.shutters == 1)
                    {
                        if (trim) OpenLeaves(mb, p0, u, n, o, len);
                        else
                        {
                            float sw = w * 0.5f;
                            VKit.Box(mb, Paint, P(p0, u, n, o.t0 - sw * 0.5f - 0.015f, ym, 0.025f), u * (sw * 0.5f), hy, n * 0.022f, 32);
                            VKit.Box(mb, Paint, P(p0, u, n, o.t1 + sw * 0.5f + 0.015f, ym, 0.025f), u * (sw * 0.5f), hy, n * 0.022f, 32);
                        }
                    }
                }
            }
            else if (o.kind == 2)
            {
                // the leaf, swung out on its hinge
                bool atT0 = VKit.Chance(rng, 0.5f);
                float ang = VKit.Rnd(rng, 95f, 150f) * Mathf.Deg2Rad;
                Vector3 hinge = P(p0, u, n, atT0 ? o.t0 : o.t1, o.y0, 0.01f);
                Vector3 dir = (atT0 ? u : -u) * Mathf.Cos(ang) + n * Mathf.Sin(ang);
                Vector3 thick = Vector3.Cross(Vector3.up, dir) * 0.022f;
                float lh = arch ? hh - w * 0.12f : hh;
                VKit.Box(mb, Paint, hinge + dir * (w * 0.5f) + Vector3.up * (lh * 0.5f), dir * (w * 0.5f), Vector3.up * (lh * 0.5f), thick, 8);
            }
            else if (trim && !lite)
            {
                // a closed door: two raised panels and a bronze knob
                float pw = w * 0.5f - 0.11f, top = Mathf.Min(o.y1 - 0.14f, ys - 0.06f);
                float l0 = o.y0 + 0.14f, l1 = o.y0 + hh * 0.44f, u0 = l1 + 0.1f;
                VKit.Box(mb, Paint, P(p0, u, n, tc, (l0 + l1) * 0.5f, -e + 0.015f), u * pw, Vector3.up * ((l1 - l0) * 0.5f), n * 0.015f, 32);
                if (top - u0 > 0.2f) VKit.Box(mb, Paint, P(p0, u, n, tc, (u0 + top) * 0.5f, -e + 0.015f), u * pw, Vector3.up * ((top - u0) * 0.5f), n * 0.015f, 32);
                Vector3 kb = P(p0, u, n, o.t1 - 0.11f, l1 + 0.05f, -e);
                mb.Cylinder(VKit.Bronze, kb, kb + n * 0.07f, 0.026f, 0.026f, 6, true);
            }
            if (!trim) return;
            Surround(mb, p0, u, n, o, arch);
            if (o.kind == 0)
            {
                SillStone(mb, p0, u, n, o.t0, o.t1, o.y0);
                if (boxChance > 0f && o.y0 > 0.5f && DR() < boxChance)
                {
                    Vector3 bp = P(p0, u, n, tc, o.y0 - 0.3f, 0.1f);
                    Vector3 tip = bp + n * 0.35f;
                    if (VKit.PathGap(tip.x, tip.z) > 0.5f) VKit.FlowerBox(mb, bp, u, n, w + 0.1f, dr);
                }
            }
            else if (o.y0 > 0.06f) Step(mb, p0, u, n, o.t0, o.t1, o.y0);
        }

        /// <summary>Spandrels (wall) that turn the top of a rectangular opening into a half circle, and the curved soffit of the arch.</summary>
        static void ArchHead(MeshBuilder mb, Material wall, Material soffit, Vector3 p0, Vector3 u, Vector3 n, float t0, float t1, float y1, float e)
        {
            float r = (t1 - t0) * 0.5f, tc = t0 + r, ys = y1 - r;
            Vector3 cen = P(p0, u, n, tc, ys, 0f), cR = P(p0, u, n, t1, y1, 0f), cL = P(p0, u, n, t0, y1, 0f), up = Vector3.up;
            Vector3 d = -n * e;
            for (int k = 0; k < ArchSeg; k++)
            {
                float g0 = Mathf.PI * k / ArchSeg, g1 = Mathf.PI * (k + 1) / ArchSeg;
                Vector3 q0 = cen + u * (r * Mathf.Cos(g0)) + up * (r * Mathf.Sin(g0)), q1 = cen + u * (r * Mathf.Cos(g1)) + up * (r * Mathf.Sin(g1));
                VKit.T(mb, wall, k < ArchSeg / 2 ? cR : cL, q0, q1, n);
                VKit.Q(mb, soffit, q0, q1, q1 + d, q0 + d, cen - (q0 + q1) * 0.5f);
            }
        }

        /// <summary>Raised plaster surround (TrimOut proud, TrimW wide) round an opening; follows a round head.</summary>
        static void Surround(MeshBuilder mb, Vector3 p0, Vector3 u, Vector3 n, Opening o, bool arch)
        {
            const float fw = TrimW, pr = TrimOut;
            Material m = VKit.Trim;
            float r = (o.t1 - o.t0) * 0.5f, tc = o.t0 + r, ys = arch ? o.y1 - r : o.y1;
            float yb = o.y0, jt = arch ? ys : o.y1 + fw;
            Vector3 up = Vector3.up;
            WQ(mb, m, p0, u, n, o.t0 - fw, o.t0, yb, jt, pr);
            WQ(mb, m, p0, u, n, o.t1, o.t1 + fw, yb, jt, pr);
            if (!lite)
            {
                VKit.Q(mb, m, P(p0, u, n, o.t0 - fw, yb, 0f), P(p0, u, n, o.t0 - fw, jt, 0f), P(p0, u, n, o.t0 - fw, jt, pr), P(p0, u, n, o.t0 - fw, yb, pr), -u);
                VKit.Q(mb, m, P(p0, u, n, o.t1 + fw, yb, 0f), P(p0, u, n, o.t1 + fw, jt, 0f), P(p0, u, n, o.t1 + fw, jt, pr), P(p0, u, n, o.t1 + fw, yb, pr), u);
                VKit.Q(mb, m, P(p0, u, n, o.t0, yb, 0f), P(p0, u, n, o.t0, ys, 0f), P(p0, u, n, o.t0, ys, pr), P(p0, u, n, o.t0, yb, pr), u);
                VKit.Q(mb, m, P(p0, u, n, o.t1, yb, 0f), P(p0, u, n, o.t1, ys, 0f), P(p0, u, n, o.t1, ys, pr), P(p0, u, n, o.t1, yb, pr), -u);
            }
            if (!arch)
            {
                WQ(mb, m, p0, u, n, o.t0, o.t1, o.y1, o.y1 + fw, pr);
                VKit.Q(mb, m, P(p0, u, n, o.t0 - fw, o.y1 + fw, 0f), P(p0, u, n, o.t1 + fw, o.y1 + fw, 0f), P(p0, u, n, o.t1 + fw, o.y1 + fw, pr), P(p0, u, n, o.t0 - fw, o.y1 + fw, pr), up);
                if (!lite) VKit.Q(mb, m, P(p0, u, n, o.t0, o.y1, 0f), P(p0, u, n, o.t1, o.y1, 0f), P(p0, u, n, o.t1, o.y1, pr), P(p0, u, n, o.t0, o.y1, pr), -up);
                return;
            }
            Vector3 cen = P(p0, u, n, tc, ys, 0f), f = n * pr;
            for (int k = 0; k < ArchSeg; k++)
            {
                float g0 = Mathf.PI * k / ArchSeg, g1 = Mathf.PI * (k + 1) / ArchSeg;
                Vector3 d0 = u * Mathf.Cos(g0) + up * Mathf.Sin(g0), d1 = u * Mathf.Cos(g1) + up * Mathf.Sin(g1);
                Vector3 i0 = cen + d0 * r, i1 = cen + d1 * r, o0 = cen + d0 * (r + fw), o1 = cen + d1 * (r + fw);
                VKit.Q(mb, m, i0 + f, i1 + f, o1 + f, o0 + f, n);
                VKit.Q(mb, m, o0, o1, o1 + f, o0 + f, d0 + d1);
                if (!lite) VKit.Q(mb, m, i0, i1, i1 + f, i0 + f, -(d0 + d1));
            }
        }

        /// <summary>Projecting stone sill under a window, a little wider than its surround.</summary>
        static void SillStone(MeshBuilder mb, Vector3 p0, Vector3 u, Vector3 n, float t0, float t1, float y0)
        {
            float a = t0 - TrimW - 0.04f, b = t1 + TrimW + 0.04f;
            VKit.Box(mb, VKit.Trim, P(p0, u, n, (a + b) * 0.5f, y0 - 0.04f, 0.035f), u * ((b - a) * 0.5f), Vector3.up * 0.04f, n * 0.035f, 32 | (lite ? 1 | 2 : 0));
        }

        /// <summary>The stone step in front of a raised door (its top is the door's sill).</summary>
        static void Step(MeshBuilder mb, Vector3 p0, Vector3 u, Vector3 n, float t0, float t1, float y0)
        {
            float a = t0 - TrimW - 0.08f, b = t1 + TrimW + 0.08f;
            VKit.Box(mb, VKit.Trim, P(p0, u, n, (a + b) * 0.5f, y0 * 0.5f, 0.16f), u * ((b - a) * 0.5f), Vector3.up * (y0 * 0.5f), n * 0.16f, 8 | 32);
        }

        /// <summary>
        /// Two shutter leaves hinged outside the surround: most lie open against the wall, some stand open at an angle. A leaf
        /// that has no room along the wall stands out square; one that would reach towards the path stays flat or is left off.
        /// </summary>
        static void OpenLeaves(MeshBuilder mb, Vector3 p0, Vector3 u, Vector3 n, Opening o, float len)
        {
            float w = o.t1 - o.t0, sw = w * 0.5f, hh = o.y1 - o.y0;
            const float edge = TrimW + 0.05f;
            bool open = DR() < 0.4f && swing;
            float baseAng = open ? Mathf.Lerp(25f, 75f, DR()) : Mathf.Lerp(0f, 5f, DR());
            for (int s = 0; s < 2; s++)
            {
                float th = s == 0 ? o.t0 - edge : o.t1 + edge;
                Vector3 away = s == 0 ? -u : u;
                float ang = baseAng + (open ? Mathf.Lerp(-12f, 12f, DR()) : 0f);
                float flatEnd = s == 0 ? th - sw : th + sw;
                bool roomFlat = flatEnd > 0.04f && flatEnd < len - 0.04f;
                if (!roomFlat)
                {
                    if (!swing) continue;
                    ang = 82f;
                }
                for (int pass = 0; pass < 2; pass++)
                {
                    float a = ang * Mathf.Deg2Rad;
                    Vector3 dir = away * Mathf.Cos(a) + n * Mathf.Sin(a), ln = n * Mathf.Cos(a) - away * Mathf.Sin(a);
                    Vector3 hinge = P(p0, u, n, th, o.y0, 0f);
                    Vector3 tip = hinge + dir * sw;
                    if (ang > 6f && VKit.PathGap(tip.x, tip.z) < 0.4f)
                    {
                        if (!roomFlat) break;
                        ang = 0f;
                        continue;
                    }
                    VKit.Box(mb, Paint, hinge + dir * (sw * 0.5f) + ln * 0.024f + Vector3.up * (hh * 0.5f), dir * (sw * 0.5f - 0.004f), Vector3.up * (hh * 0.5f - 0.004f), ln * 0.02f, ang <= 6f ? 32 : 0);
                    break;
                }
            }
        }

        /// <summary>Flat roof terrace inside a parapet. c is the centre at roof-floor level; the outer parapet faces belong to the walls.</summary>
        public static void FlatRoof(MeshBuilder mb, Material wall, Material floor, Vector3 c, Vector3 S, Vector3 F, float a, float b)
        {
            float t = ParapetT; Vector3 up = Vector3.up * ParapetH;
            Vector3 o0 = c - S * a - F * b, o1 = c + S * a - F * b, o2 = c + S * a + F * b, o3 = c - S * a + F * b;
            Vector3 i0 = o0 + (S + F) * t, i1 = o1 + (-S + F) * t, i2 = o2 + (-S - F) * t, i3 = o3 + (S - F) * t;
            VKit.Q(mb, floor, i0, i1, i2, i3, Vector3.up);
            // parapet top ring
            VKit.Q(mb, wall, o0 + up, o1 + up, i1 + up, i0 + up, Vector3.up);
            VKit.Q(mb, wall, o1 + up, o2 + up, i2 + up, i1 + up, Vector3.up);
            VKit.Q(mb, wall, o2 + up, o3 + up, i3 + up, i2 + up, Vector3.up);
            VKit.Q(mb, wall, o3 + up, o0 + up, i0 + up, i3 + up, Vector3.up);
            // inner faces
            VKit.Q(mb, wall, i0, i1, i1 + up, i0 + up, F);
            VKit.Q(mb, wall, i1, i2, i2 + up, i1 + up, -S);
            VKit.Q(mb, wall, i2, i3, i3 + up, i2 + up, -F);
            VKit.Q(mb, wall, i3, i0, i0 + up, i3 + up, S);
            if (trim) Coping(mb, c + up, S, F, a - t * 0.5f, b - t * 0.5f, t);
        }

        /// <summary>
        /// Rounded, hand-plastered coping on a rectangular parapet: c = centre at the parapet top, a/b = half sizes of the parapet's
        /// centre line. The corners sit a centimetre or two apart in height, as a wall finished by hand does.
        /// </summary>
        public static void Coping(MeshBuilder mb, Vector3 c, Vector3 S, Vector3 F, float a, float b, float t)
        {
            VKit.CopeProfile(prof, t, CopeH, !lite);
            loop.Clear();
            loop.Add(c - S * a - F * b + Vector3.up * Jit()); loop.Add(c + S * a - F * b + Vector3.up * Jit());
            loop.Add(c + S * a + F * b + Vector3.up * Jit()); loop.Add(c - S * a + F * b + Vector3.up * Jit());
            VKit.SweepLoop(mb, VKit.Trim, loop, prof);
        }

        static float Jit() { return (DR() - 0.5f) * 0.024f; }

        /// <summary>Rounded coping along a straight wall top from a to b (wall thickness t across n), ends closed.</summary>
        public static void CopeRun(MeshBuilder mb, Vector3 a, Vector3 b, Vector3 n, float t)
        {
            if ((b - a).sqrMagnitude < 0.0025f) return;
            VKit.CopeProfile(prof, t, CopeH * 0.85f, !lite);
            VKit.SweepRun(mb, VKit.Trim, a, b, n, prof);
        }

        /// <summary>A moulded plaster cornice round the top edge of a flat-topped block (roof room, cistern): loop of its outer corners at the top.</summary>
        public static void Cornice(MeshBuilder mb, Vector3 c, Vector3 S, Vector3 F, float a, float b, float size = 1f)
        {
            prof.Clear();
            float k = size;
            prof.Add(new Vector2(0f, -0.08f * k)); prof.Add(new Vector2(0.04f * k, -0.08f * k)); prof.Add(new Vector2(0.04f * k, -0.012f * k));
            prof.Add(new Vector2(0.016f * k, 0.026f * k)); prof.Add(new Vector2(-0.07f * k, 0.026f * k)); prof.Add(new Vector2(-0.095f * k, 0f));
            loop.Clear();
            loop.Add(c - S * a - F * b); loop.Add(c + S * a - F * b); loop.Add(c + S * a + F * b); loop.Add(c - S * a + F * b);
            VKit.SweepLoop(mb, VKit.Trim, loop, prof);
        }

        /// <summary>Barrel vault on a box: runs along axis A (half length a), spans axis B (half width b), springs at c, rises by rise.</summary>
        public static void Vault(MeshBuilder mb, Material m, Vector3 c, Vector3 A, Vector3 B, float a, float b, float rise, int seg = 8)
        {
            var v = new List<Vector3>((seg + 1) * 2); var nn = new List<Vector3>((seg + 1) * 2); var tr = new List<int>(seg * 6);
            VaultSurface(mb, m, c, A, B, a, b, rise, seg, v, nn, tr);
            // gable ends
            for (int i = 0; i < seg; i++)
            {
                VKit.T(mb, m, c - A * a, v[i * 2], v[i * 2 + 2], -A);
                VKit.T(mb, m, c + A * a, v[i * 2 + 1], v[i * 2 + 3], A);
            }
        }

        static void VaultSurface(MeshBuilder mb, Material m, Vector3 c, Vector3 A, Vector3 B, float a, float b, float rise, int seg, List<Vector3> v, List<Vector3> nn, List<int> tr)
        {
            bool flip = Vector3.Dot(Vector3.Cross(A, B), Vector3.up) > 0f;
            for (int i = 0; i <= seg; i++)
            {
                float ang = Mathf.PI * i / seg;
                Vector3 off = B * (-Mathf.Cos(ang) * b) + Vector3.up * (Mathf.Sin(ang) * rise);
                Vector3 nrm = (B * (-Mathf.Cos(ang) * rise) + Vector3.up * (Mathf.Sin(ang) * b)).normalized;
                v.Add(c - A * a + off); nn.Add(nrm);
                v.Add(c + A * a + off); nn.Add(nrm);
            }
            for (int i = 0; i < seg; i++)
            {
                int p = i * 2;
                if (flip) { tr.Add(p); tr.Add(p + 1); tr.Add(p + 3); tr.Add(p); tr.Add(p + 3); tr.Add(p + 2); }
                else { tr.Add(p); tr.Add(p + 3); tr.Add(p + 1); tr.Add(p); tr.Add(p + 2); tr.Add(p + 3); }
            }
            mb.Raw(m, v, nn, null, tr);
        }

        /// <summary>
        /// Barrel roof of a house or the chapel: the vault (roof material: whitewash or painted blue), its end walls in the wall
        /// material, a raised plaster rim that follows the curve over each end wall and a plaster lip along both eaves.
        /// Rim and lips rise less than 0.09 m above the vault, so the roof line used by the plan stays true.
        /// </summary>
        public static void VaultRoof(MeshBuilder mb, Material wall, Material roof, Vector3 c, Vector3 A, Vector3 B, float a, float b, float rise, int seg = 12)
        {
            if (lite) seg = Mathf.Max(8, seg - 4);
            var v = new List<Vector3>((seg + 1) * 2); var nn = new List<Vector3>((seg + 1) * 2); var tr = new List<int>(seg * 6);
            VaultSurface(mb, roof, c, A, B, a, b, rise, seg, v, nn, tr);
            for (int i = 0; i < seg; i++)
            {
                VKit.T(mb, wall, c - A * a, v[i * 2], v[i * 2 + 2], -A);
                VKit.T(mb, wall, c + A * a, v[i * 2 + 1], v[i * 2 + 3], A);
            }
            Material m = VKit.Trim;
            Vector3 up = Vector3.up;
            const float rimOut = 0.035f, rimIn = 0.16f, rimW = 0.05f, rimH = 0.08f;
            for (int end = 0; end < 2; end++)
            {
                Vector3 ax = end == 0 ? -A : A;
                Vector3 pOut = c + ax * (a + rimOut), pIn = c + ax * (a - rimIn), pWall = c + ax * a;
                for (int i = 0; i < seg; i++)
                {
                    float g0 = Mathf.PI * i / seg, g1 = Mathf.PI * (i + 1) / seg;
                    Vector3 in0 = B * (-Mathf.Cos(g0) * b) + up * (Mathf.Sin(g0) * rise), in1 = B * (-Mathf.Cos(g1) * b) + up * (Mathf.Sin(g1) * rise);
                    Vector3 ou0 = B * (-Mathf.Cos(g0) * (b + rimW)) + up * (Mathf.Sin(g0) * (rise + rimH)), ou1 = B * (-Mathf.Cos(g1) * (b + rimW)) + up * (Mathf.Sin(g1) * (rise + rimH));
                    Vector3 radial = (ou0 + ou1) - (in0 + in1);
                    VKit.Q(mb, m, pOut + in0, pOut + in1, pOut + ou1, pOut + ou0, ax);                 // outer face of the rim
                    VKit.Q(mb, m, pIn + in0, pIn + in1, pIn + ou1, pIn + ou0, -ax);                    // its back, seen from the roof
                    VKit.Q(mb, m, pIn + ou0, pIn + ou1, pOut + ou1, pOut + ou0, radial);              // its top
                    VKit.Q(mb, m, pWall + in0, pWall + in1, pOut + in1, pOut + in0, -(in0 + in1));      // soffit over the end wall
                }
                for (int k = 0; k < 2; k++)
                {
                    Vector3 i0 = B * ((k == 0 ? -1f : 1f) * b), o0 = B * ((k == 0 ? -1f : 1f) * (b + rimW));
                    VKit.Q(mb, m, pIn + i0, pOut + i0, pOut + o0, pIn + o0, -up);
                }
            }
            for (int k = 0; k < 2; k++)
            {
                float sg = k == 0 ? -1f : 1f;
                VKit.Box(mb, m, c + B * (sg * (b - 0.045f)) + up * 0.05f, A * (a + 0.02f), up * 0.07f, B * 0.08f);
            }
        }

        /// <summary>
        /// Solid white stair: starts at start (ground level), runs along dir (unit, horizontal) for run metres and climbs rise.
        /// side points to the open side (the other side leans on a wall). Steps are 0.18 m high.
        /// </summary>
        public static void Stair(MeshBuilder mb, Material m, Vector3 start, Vector3 dir, Vector3 side, float width, float run, float rise, bool closeInner = false)
        {
            int n = Mathf.Max(2, Mathf.RoundToInt(rise / StepRise));
            float sr = rise / n, sd = run / n;
            Vector3 w = side * width;
            for (int i = 0; i < n; i++)
            {
                Vector3 a = start + dir * (sd * i), b = start + dir * (sd * (i + 1));
                float y0 = sr * i, y1 = sr * (i + 1);
                Vector3 u0 = Vector3.up * y0, u1 = Vector3.up * y1;
                VKit.Q(mb, m, a + u0, a + w + u0, a + w + u1, a + u1, -dir);            // riser
                VKit.Q(mb, m, a + u1, a + w + u1, b + w + u1, b + u1, Vector3.up);       // tread
                VKit.Q(mb, m, a + w, b + w, b + w + u1, a + w + u1, side);               // open flank
                if (closeInner) VKit.Q(mb, m, a, b, b + u1, a + u1, -side);
            }
            Vector3 e = start + dir * run;
            VKit.Q(mb, m, e, e + w, e + w + Vector3.up * rise, e + Vector3.up * rise, dir);
        }

        /// <summary>
        /// Cycladic chimney: a white stem, a plaster band, a pierced crown (four corner piers round the dark flue), a capping slab
        /// and a little pitched hood. Its overall height is the same as the old plain chimney (h + 0.09).
        /// </summary>
        public static void Chimney(MeshBuilder mb, Material m, Vector3 basePos, float yaw, float h)
        {
            Vector3 up = Vector3.up, f = VKit.Dir(yaw), r = Vector3.Cross(up, f);
            float stem = h - 0.36f;
            VKit.BoxOn(mb, m, basePos, 0.46f, stem, 0.46f, yaw, 4);
            VKit.Box(mb, VKit.Trim, basePos + up * (stem - 0.025f), r * 0.26f, up * 0.035f, f * 0.26f);       // band (its underside closes the ledge)
            float y0 = stem + 0.01f, ph = 0.24f;
            if (!lite)
            {
                for (int i = 0; i < 4; i++)
                {
                    float sx = (i & 1) == 0 ? -0.16f : 0.16f, sz = i < 2 ? -0.16f : 0.16f;
                    VKit.BoxOn(mb, m, basePos + up * y0 + r * sx + f * sz, 0.14f, ph, 0.14f, yaw, 4);
                }
                VKit.BoxOn(mb, VKit.Ruin, basePos + up * y0, 0.2f, ph, 0.2f, yaw, 4);
            }
            else VKit.BoxOn(mb, m, basePos + up * y0, 0.46f, ph, 0.46f, yaw, 4);
            Vector3 cy = basePos + up * (y0 + ph);
            VKit.Box(mb, VKit.Trim, cy + up * 0.03f, r * 0.31f, up * 0.03f, f * 0.31f);
            // hood: two sloping planes and their gable triangles
            Vector3 t = cy + up * 0.06f, ridge = up * 0.11f;
            Vector3 e0 = t - f * 0.27f, e1 = t + f * 0.27f;
            VKit.Q(mb, m, e0 - r * 0.27f, e1 - r * 0.27f, e1 + ridge, e0 + ridge, -r + up);
            VKit.Q(mb, m, e0 + r * 0.27f, e1 + r * 0.27f, e1 + ridge, e0 + ridge, r + up);
            VKit.T(mb, m, e0 - r * 0.27f, e0 + r * 0.27f, e0 + ridge, -f);
            VKit.T(mb, m, e1 - r * 0.27f, e1 + r * 0.27f, e1 + ridge, f);
        }

        /// <summary>
        /// Canvas awning on a facade: p0 is the facade point under its start, u along the wall, n outwards. The cloth sags a
        /// little between the wall and the front rail and ends in a scalloped valance.
        /// </summary>
        public static void Awning(MeshBuilder mb, Vector3 p0, Vector3 u, Vector3 n, float len, float yWall, float reach, float drop, bool postsToGround = false)
        {
            Vector3 a = p0 + Vector3.up * yWall + n * 0.03f, b = a + u * len;
            Vector3 c = b + n * reach - Vector3.up * drop, d = a + n * reach - Vector3.up * drop;
            Vector3 sag = Vector3.down * Mathf.Min(0.08f, reach * 0.03f);
            Vector3 ma = Vector3.Lerp(a, d, 0.5f) + sag, mbp = Vector3.Lerp(b, c, 0.5f) + sag;
            VKit.Q2(mb, VKit.Canvas, a, b, mbp, ma);
            VKit.Q2(mb, VKit.Canvas, ma, mbp, c, d);
            Vector3 fl = Vector3.up * -0.16f;
            VKit.Q2(mb, VKit.Canvas, d, c, c + fl, d + fl);                   // valance
            int sc = Mathf.Max(2, Mathf.RoundToInt(len / 0.32f));
            for (int i = 0; i < sc; i++)
            {
                Vector3 s0 = Vector3.Lerp(d, c, i / (float)sc) + fl, s1 = Vector3.Lerp(d, c, (i + 1) / (float)sc) + fl;
                Vector3 tip = (s0 + s1) * 0.5f + Vector3.down * 0.09f;
                VKit.T(mb, VKit.Canvas, s0, s1, tip, n);
                VKit.T(mb, VKit.Canvas, s0, s1, tip, -n);
            }
            int posts = Mathf.Max(2, Mathf.RoundToInt(len / 3.2f) + 1);
            for (int i = 0; i < posts; i++)
            {
                Vector3 top = Vector3.Lerp(d, c, i / (float)(posts - 1)) - n * 0.06f;
                Vector3 foot = new Vector3(top.x, postsToGround ? VKit.Ground(top.x, top.z) - 0.05f : p0.y, top.z);
                mb.Cylinder(VKit.Iron, foot, top, 0.028f, 0.028f, 6, false);
            }
            mb.Cylinder(VKit.Iron, d - n * 0.06f, c - n * 0.06f, 0.025f, 0.025f, 5, false);
        }

        /// <summary>Bougainvillea climbing a wall: a stem at the foot p0 (n = wall normal), 9-14 blossom lumps spreading along u
        /// into the facade (away from the stem they stay over the lintels), and for arc > 0.6 a second branch trained along the
        /// band over the door head, arc metres along u.</summary>
        public static void Bougainvillea(MeshBuilder mb, Vector3 p0, Vector3 u, Vector3 n, float height, float spread, System.Random rng, float arc = 0f)
        {
            const float Lintels = 2.35f;              // top of the door and window surrounds above the floor
            Vector3 foot = p0 + n * 0.12f;
            mb.Cylinder(VKit.WoodDk, foot, foot + Vector3.up * (height * 0.8f) + u * (spread * 0.15f), 0.04f, 0.03f, 5, false);
            int lumps = 9 + rng.Next(6);
            for (int i = 0; i < lumps; i++)
            {
                float t = VKit.Rnd(rng, -0.15f, 0.85f) * spread, y = height * VKit.Rnd(rng, 0.62f, 1.05f);
                float r = VKit.Rnd(rng, 0.45f, 0.85f);
                if (t > 0.9f) y = Mathf.Max(y, Lintels + r * 0.55f);
                VKit.Blob(mb, VKit.Boug, foot + u * t + Vector3.up * y + n * 0.1f, u * r, Vector3.up * (r * 0.7f), n * (r * 0.45f), rng, 0.22f, 6, 3);
            }
            for (int i = 0; i < 2; i++)
                VKit.Blob(mb, VKit.Plant, foot + u * VKit.Rnd(rng, -0.3f, 0.3f) * spread + Vector3.up * height * VKit.Rnd(rng, 0.4f, 0.6f) + n * 0.05f,
                    u * 0.5f, Vector3.up * 0.45f, n * 0.25f, rng, 0.2f, 6, 3);
            if (arc > 0.6f)
            {
                int m = Mathf.Clamp(Mathf.RoundToInt(arc / 0.55f), 3, 8);
                for (int i = 1; i <= m; i++)
                {
                    float k = i / (float)m, r = VKit.Rnd(rng, 0.3f, 0.45f);
                    float y = Mathf.Max(Lintels + r * 0.55f, Mathf.Min(height, Lintels + 0.3f) + 0.2f * Mathf.Sin(Mathf.PI * k));
                    VKit.Blob(mb, VKit.Boug, foot + u * (arc * k) + Vector3.up * y + n * 0.08f, u * r, Vector3.up * (r * 0.6f), n * (r * 0.4f), rng, 0.22f, 6, 3);
                }
            }
        }

        /// <summary>Bougainvillea spilling over a wall top: a row of blossom masses along a->b, hanging down its outer face n.</summary>
        public static void Drape(MeshBuilder mb, Vector3 a, Vector3 b, Vector3 n, System.Random rng)
        {
            float len = Vector3.Distance(a, b);
            Vector3 u = (b - a) / Mathf.Max(len, 0.01f);
            int k = Mathf.Clamp(Mathf.RoundToInt(len / 0.55f), 2, 6);
            for (int i = 0; i < k; i++)
            {
                float t = (i + VKit.Rnd(rng, 0.25f, 0.75f)) / k * len, r = VKit.Rnd(rng, 0.32f, 0.5f);
                Vector3 c = a + u * t + n * (r * 0.35f) + Vector3.up * (VKit.Rnd(rng, -0.35f, 0.05f));
                VKit.Blob(mb, i % 3 == 2 ? VKit.Plant : VKit.Boug, c, u * (r * 1.1f), Vector3.up * (r * 0.9f), n * (r * 0.5f), rng, 0.22f, 6, 3);
            }
        }

        /// <summary>
        /// Small balcony in front of a tall window: p0 = the wall's bottom corner at the storey floor, tc its centre along u.
        /// A plaster slab on two brackets with an iron railing, or a low whitewashed balustrade with a rounded coping.
        /// </summary>
        public static void Balcony(MeshBuilder mb, Material wall, Vector3 p0, Vector3 u, Vector3 n, float tc, float halfW, float depth, bool iron)
        {
            Vector3 up = Vector3.up, c = p0 + u * tc;
            VKit.Box(mb, VKit.Trim, c + n * (depth * 0.5f) - up * 0.04f, u * halfW, up * 0.07f, n * (depth * 0.5f), 32);       // slab: -0.11 .. +0.03
            for (int s = -1; s <= 1; s += 2)
                VKit.Box(mb, wall, c + u * (s * (halfW - 0.22f)) + n * (depth * 0.32f) - up * 0.27f, u * 0.06f, up * 0.16f, n * (depth * 0.32f), 4 | 32);
            float y0 = 0.03f;
            if (iron)
            {
                float inset = 0.035f;
                for (int rail = 0; rail < 2; rail++)
                {
                    float y = y0 + (rail == 0 ? 0.92f : 0.1f);
                    pts.Clear();
                    pts.Add(c - u * (halfW - inset) + n * 0.02f + up * y);
                    pts.Add(c - u * (halfW - inset) + n * (depth - inset) + up * y);
                    pts.Add(c + u * (halfW - inset) + n * (depth - inset) + up * y);
                    pts.Add(c + u * (halfW - inset) + n * 0.02f + up * y);
                    mb.Tube(VKit.Iron, pts, rail == 0 ? 0.024f : 0.016f, 5);
                }
                int nf = Mathf.Max(2, Mathf.RoundToInt((halfW * 2f - inset * 2f) / (lite ? 0.24f : 0.13f)));
                for (int i = 0; i <= nf; i++)
                {
                    Vector3 q = c + u * Mathf.Lerp(-(halfW - inset), halfW - inset, i / (float)nf) + n * (depth - inset);
                    mb.Cylinder(VKit.Iron, q + up * y0, q + up * (y0 + 0.92f), 0.012f, 0.012f, 4, false);
                }
                int ns = Mathf.Max(1, Mathf.RoundToInt((depth - inset) / (lite ? 0.24f : 0.13f)));
                for (int s = -1; s <= 1; s += 2)
                    for (int i = 0; i < ns; i++)
                    {
                        Vector3 q = c + u * (s * (halfW - inset)) + n * Mathf.Lerp(0.08f, depth - inset, i / (float)ns);
                        mb.Cylinder(VKit.Iron, q + up * y0, q + up * (y0 + 0.92f), 0.012f, 0.012f, 4, false);
                    }
            }
            else
            {
                const float t = 0.12f, h = 0.86f;
                VKit.Box(mb, wall, c + n * (depth - t * 0.5f) + up * (y0 + h * 0.5f), u * halfW, up * (h * 0.5f), n * (t * 0.5f), 8);
                for (int s = -1; s <= 1; s += 2)
                    VKit.Box(mb, wall, c + u * (s * (halfW - t * 0.5f)) + n * ((depth - t) * 0.5f) + up * (y0 + h * 0.5f), u * (t * 0.5f), up * (h * 0.5f), n * ((depth - t) * 0.5f), 8 | 16 | 32);
                CopeRun(mb, c - u * halfW + n * (depth - t * 0.5f) + up * (y0 + h), c + u * halfW + n * (depth - t * 0.5f) + up * (y0 + h), n, t);
                for (int s = -1; s <= 1; s += 2)
                    CopeRun(mb, c + u * (s * (halfW - t * 0.5f)) + up * (y0 + h), c + u * (s * (halfW - t * 0.5f)) + n * (depth - t) + up * (y0 + h), u * s, t);
            }
        }
    }
}
