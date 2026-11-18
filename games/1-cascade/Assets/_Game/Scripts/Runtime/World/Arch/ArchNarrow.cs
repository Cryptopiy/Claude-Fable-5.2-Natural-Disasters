using System.Collections.Generic;
using UnityEngine;

namespace CASCADE
{
    /// <summary>
    /// Section 2, the compression before the vista: white walls about 4.2 m high on both sides of the narrow stair
    /// (2.3 m apart), a stepped, slightly irregular top line, closed blue doors and shuttered windows set into the
    /// walls, two wall lamps and one arch near the top. The walls end exactly at PathDef.SStairEnd.
    /// </summary>
    internal static class ArchNarrow
    {
        public const float WallH = 4.2f;
        public const float SegMin = 2.8f, SegMax = 4.4f;     // length of one wall piece along the path
        public const float DoorW = 0.9f, DoorH = 2.0f, DoorRecess = 0.15f;
        public const float WinW = 0.6f, WinH = 0.8f, WinSill = 1.75f, WinRecess = 0.09f;
        public const float ArchBack = 6.5f;                  // the arch stands this far before the top of the stair
        public const float ArchSpring = 2.5f, ArchThick = 0.7f;
        public const float BracketY = 3.3f;                  // wall lamp bracket above the path (lantern bottom about 2.65 m)

        static readonly int[] DoorSegL = { 2, 8 }, DoorSegR = { 5, 10 };
        static readonly int[] WinSegL = { 4, 6, 10 }, WinSegR = { 2, 7, 11 };
        static readonly int[] LampSegL = { 3 }, LampSegR = { 9 };

        static readonly Vector3 Up = ArchGeo.Up;

        public static void Build(ArchCtx c)
        {
            var rng = new System.Random(4207);
            var plantRng = new System.Random(4211);          // separate stream: the wall layout (and the lamps) stay as they were
            float sA = PathDef.SNarrowStart, sB = PathDef.SStairEnd;
            for (int side = 1; side >= -1; side -= 2)
            {
                // cut the wall into pieces
                var cuts = new List<float> { sA };
                float s = sA;
                while (true)
                {
                    float len = Mathf.Lerp(SegMin, SegMax, (float)rng.NextDouble());
                    if (s + len > sB - 2.2f) break;
                    s += len;
                    cuts.Add(s);
                }
                cuts.Add(sB);
                int n = cuts.Count - 1;
                var ops = new List<ArchOpening>(1);
                for (int i = 0; i < n; i++)
                {
                    float s0 = cuts[i], s1 = cuts[i + 1];
                    ArchFrame a = ArchGeo.Frame(s0), b = ArchGeo.Frame(s1);
                    Vector3 A = a.At(side * a.hw), B = b.At(side * b.hw);
                    float thick = 0.7f + 0.6f * (float)rng.NextDouble();
                    float jitter = Mathf.Lerp(-0.25f, 0.55f, (float)rng.NextDouble());
                    Vector3 A2 = a.At(side * (a.hw + thick)), B2 = b.At(side * (b.hw + thick));
                    float yMid = PathDef.Pos((s0 + s1) * 0.5f).y;
                    float yTop = yMid + WallH + jitter;
                    float yBot = a.p.y - 1.2f;
                    float yBotOut = a.p.y - Mathf.Max(ArchGeo.Depth(A2, a.l * side, 1.5f, 1.2f, 9f), ArchGeo.Depth(ArchGeo.Y(B2, a.p.y), b.l * side, 1.5f, 1.2f, 9f));
                    Material wall = (i % 4 == 2) ? c.WhiteOld : c.White;
                    MeshBuilder mb = c.S(a.p.y);
                    Vector3 inward = -side * ArchGeo.Left((b.p - a.p).normalized);
                    inward.y = 0f; inward.Normalize();
                    float lh = ArchGeo.FlatDist(A, B);

                    ops.Clear();
                    bool inner = i > 0 && i < n - 1 && lh > 2.2f;       // no openings in the funnel pieces at both ends
                    Vector3 uDir = ArchGeo.Flat(B - A).normalized;
                    if (inner && Has(side > 0 ? DoorSegL : DoorSegR, i))
                    {
                        float u0 = lh * 0.5f - DoorW * 0.5f, u1 = u0 + DoorW;
                        float v0 = PathDef.Pos(Mathf.Lerp(s0, s1, u1 / lh)).y + 0.03f;
                        Material leaf = (i & 1) == 0 ? c.Blue : c.ShutterGreen;
                        ops.Add(new ArchOpening { u0 = u0, u1 = u1, v0 = v0, v1 = v0 + DoorH, depth = DoorRecess, panel = leaf });
                        Door(c, mb, A, uDir, inward, u0, u1, v0, Mathf.Lerp(s0, s1, u0 / lh), Mathf.Lerp(s0, s1, u1 / lh), leaf);
                    }
                    else if (inner && Has(side > 0 ? WinSegL : WinSegR, i))
                    {
                        float u0 = lh * 0.5f - WinW * 0.5f, v0 = yMid + WinSill;
                        Material leaf = i % 3 == 0 ? c.ShutterRed : i % 3 == 1 ? c.ShutterGreen : c.Blue;
                        ops.Add(new ArchOpening { u0 = u0, u1 = u0 + WinW, v0 = v0, v1 = v0 + WinH, depth = WinRecess, panel = leaf });
                        Window(c, mb, A, uDir, inward, u0, v0, leaf);
                    }
                    ArchGeo.FaceWithOpenings(mb, wall, A, B, yBot, yTop, inward, ops);
                    ArchGeo.VQuad(mb, wall, A2, B2, yBotOut, yTop, -inward);
                    ArchGeo.VQuad(mb, wall, A, A2, yBotOut, yTop, -a.f);
                    ArchGeo.VQuad(mb, wall, B, B2, yBotOut, yTop, b.f);
                    // plaster coping over the stepped top (it replaces the flat top of the piece)
                    Vector3 ci = inward * CopeOver;
                    ArchDetail.Slab(mb, c.Trim, A + ci, B + ci, B2 - ci, A2 - ci, yTop, yTop + CopeH, true);

                    if (inner && Has(side > 0 ? LampSegL : LampSegR, i)) WallLamp(c, (s0 + s1) * 0.5f, side);
                    if ((i + (side > 0 ? 0 : 2)) % 4 == 1) Bougainvillea(c, plantRng, s0, s1, A, B, inward, yTop + CopeH);
                }
            }
            Arch(c, sB - ArchBack);
        }

        public const float CopeOver = 0.04f, CopeH = 0.07f;

        /// <summary>Panelled leaf in the door recess, plaster surround standing on the steps, a bronze knob and a stone sill.</summary>
        static void Door(ArchCtx c, MeshBuilder mb, Vector3 A, Vector3 u, Vector3 n, float u0, float u1, float v0, float sL, float sR, Material leaf)
        {
            float um = (u0 + u1) * 0.5f;
            Vector3 o = ArchGeo.Y(A + u * um, v0);
            float yL = Mathf.Min(ArchStairs.TreadY(sL - 0.08f), ArchStairs.TreadY(sL + 0.08f)) - 0.02f;
            float yR = Mathf.Min(ArchStairs.TreadY(sR - 0.08f), ArchStairs.TreadY(sR + 0.08f)) - 0.02f;
            ArchDetail.Surround(mb, c.Trim, o, u, n, DoorW, DoorH, 0.1f, 0.025f, false, Mathf.Min(yL, v0), Mathf.Min(yR, v0));
            Quaternion rot = Quaternion.LookRotation(n, Vector3.up);
            Vector3 back = -n * DoorRecess;
            for (int col = -1; col <= 1; col += 2)
                for (int row = 0; row < 2; row++)
                {
                    float h = row == 0 ? 0.72f : 0.86f, y = row == 0 ? 0.14f + h * 0.5f : 1.02f + h * 0.5f;
                    mb.Box(leaf, o + back + u * (col * 0.2f) + Up * y + n * 0.0125f, new Vector3(0.3f, h, 0.025f), rot, 32);
                }
            mb.Cylinder(c.Bronze, o + back + Up * 1.0f, o + back + Up * 1.0f + n * 0.07f, 0.012f, 0.03f, 6, true);   // knob on the meeting stile
            mb.Box(c.Pumice, o + back * 0.5f + Up * 0.005f, new Vector3(DoorW - 0.004f, 0.04f, DoorRecess - 0.004f), rot, 0);   // raised threshold
        }

        /// <summary>Closed louvred shutters in the window recess, plaster surround and sill.</summary>
        static void Window(ArchCtx c, MeshBuilder mb, Vector3 A, Vector3 u, Vector3 n, float u0, float v0, Material leaf)
        {
            Vector3 o = ArchGeo.Y(A + u * (u0 + WinW * 0.5f), v0);
            ArchDetail.Surround(mb, c.Trim, o, u, n, WinW, WinH, 0.08f, 0.025f, true, v0 - 0.06f, v0 - 0.06f);
            Vector3 back = -n * WinRecess + Up * (WinH * 0.5f);
            ArchDetail.Shutter(mb, leaf, o + back + u * (WinW * 0.25f), n, WinW * 0.5f - 0.004f, WinH - 0.004f);
            ArchDetail.Shutter(mb, leaf, o + back - u * (WinW * 0.25f), n, WinW * 0.5f - 0.004f, WinH - 0.004f);
        }

        /// <summary>Bougainvillea spilling over a wall top and down its inner face, always well above head height.</summary>
        static void Bougainvillea(ArchCtx c, System.Random rng, float s0, float s1, Vector3 A, Vector3 B, Vector3 inward, float yTop)
        {
            float t = 0.3f + 0.4f * (float)rng.NextDouble();
            float s = Mathf.Lerp(s0, s1, t);
            float yPath = Mathf.Max(PathDef.Pos(s0).y, PathDef.Pos(s1).y);
            if (yTop - yPath < 3.6f) return;
            MeshBuilder mb = c.F(PathDef.Pos(s).y);
            Vector3 top = ArchGeo.Y(Vector3.Lerp(A, B, t), yTop);
            Vector3 along = ArchGeo.Flat(B - A).normalized;
            ArchDetail.Blob(mb, c.Plant, top - inward * 0.12f + Up * 0.14f, along * 0.8f, Up * 0.32f, inward * 0.55f, rng, 0.25f, 8, 4);
            ArchDetail.Blob(mb, c.Boug, top - inward * 0.05f + Up * 0.24f + along * 0.15f, along * 0.62f, Up * 0.3f, inward * 0.45f, rng, 0.3f, 8, 4);
            float lowest = yPath + 2.9f;
            for (int k = 0; k < 3; k++)
            {
                float y = yTop - 0.35f - k * 0.3f;
                if (y - 0.25f < lowest) break;
                Vector3 p = top + along * ((float)rng.NextDouble() * 0.8f - 0.4f) + inward * 0.05f;
                ArchDetail.Blob(mb, k == 1 ? c.Plant : c.Boug, ArchGeo.Y(p, y), along * (0.34f - k * 0.05f), Up * 0.24f, inward * 0.12f, rng, 0.3f, 6, 3);
            }
        }

        static bool Has(int[] list, int v)
        {
            for (int i = 0; i < list.Length; i++) if (list[i] == v) return true;
            return false;
        }

        /// <summary>Iron bracket with a hanging lantern high on the wall (above head height).</summary>
        static void WallLamp(ArchCtx c, float s, int side)
        {
            ArchFrame f = ArchGeo.Frame(s);
            MeshBuilder mb = c.F(f.p.y);
            Vector3 wallPt = f.At(side * f.hw, BracketY);
            Vector3 tip = f.At(side * (f.hw - 0.42f), BracketY);
            mb.Cylinder(c.Iron, wallPt, tip, 0.024f, 0.024f, 5, false);
            mb.Cylinder(c.Iron, f.At(side * f.hw, BracketY - 0.33f), f.At(side * (f.hw - 0.36f), BracketY - 0.02f), 0.022f, 0.022f, 5, false);
            mb.Box(c.Iron, f.At(side * (f.hw - 0.012f), BracketY - 0.15f), new Vector3(0.025f, 0.5f, 0.12f), Quaternion.LookRotation(f.f, Vector3.up), 0);
            Vector3 head = tip - Up * 0.37f;
            mb.Cylinder(c.Iron, tip, head + Up * 0.34f, 0.02f, 0.02f, 5, false);
            ArchParts.LampHead(mb, c, head, -side * f.l, c.NextGlass());
            c.AddLamp(s, head);
        }

        /// <summary>A white arch spanning the stair: semicircular soffit, flat top, embedded in both walls.</summary>
        static void Arch(ArchCtx c, float s)
        {
            const int N = 10;
            ArchFrame f = ArchGeo.Frame(s);
            ArchFrame fHi = ArchGeo.Frame(s + ArchThick * 0.5f);
            float r = f.hw;
            float ySpring = fHi.p.y + ArchSpring;
            float yTop = ySpring + r + 0.45f;
            float embed = 0.4f;
            Vector3 front = f.p - f.f * (ArchThick * 0.5f), back = f.p + f.f * (ArchThick * 0.5f);
            MeshBuilder mb = c.S(f.p.y);
            Vector3 pf0 = Vector3.zero, pb0 = Vector3.zero;
            float x0 = 0f;
            for (int i = 0; i <= N; i++)
            {
                float ang = Mathf.PI * i / N;
                float x = -r * Mathf.Cos(ang), y = ySpring + r * Mathf.Sin(ang);
                Vector3 pf = ArchGeo.Y(front + f.l * x, y), pb = ArchGeo.Y(back + f.l * x, y);
                if (i > 0)
                {
                    ArchGeo.Quad(mb, c.White, pf0, pf, ArchGeo.Y(front + f.l * x, yTop), ArchGeo.Y(front + f.l * x0, yTop), -f.f);
                    ArchGeo.Quad(mb, c.White, pb0, pb, ArchGeo.Y(back + f.l * x, yTop), ArchGeo.Y(back + f.l * x0, yTop), f.f);
                    Vector3 mid = (pf0 + pf) * 0.5f;
                    ArchGeo.Quad(mb, c.White, pf0, pf, pb, pb0, ArchGeo.Y(f.p, ySpring) - mid);   // soffit faces the arch centre
                }
                pf0 = pf; pb0 = pb; x0 = x;
            }
            // haunches inside the walls and the flat top
            for (int sgn = -1; sgn <= 1; sgn += 2)
            {
                Vector3 a = front + f.l * (sgn * r), b = front + f.l * (sgn * (r + embed));
                Vector3 a2 = back + f.l * (sgn * r), b2 = back + f.l * (sgn * (r + embed));
                ArchGeo.VQuad(mb, c.White, a, b, ySpring - 0.3f, yTop, -f.f);
                ArchGeo.VQuad(mb, c.White, a2, b2, ySpring - 0.3f, yTop, f.f);
            }
            Vector3 tl = f.l * (r + embed);
            ArchGeo.Quad(mb, c.White, ArchGeo.Y(front - tl, yTop), ArchGeo.Y(front + tl, yTop), ArchGeo.Y(back + tl, yTop), ArchGeo.Y(back - tl, yTop), Up);

            // plaster dressing: archivolt bands on both faces, impost blocks at the springs, keystones, a coping on top
            const float band = 0.16f, proud = 0.03f;
            for (int face = -1; face <= 1; face += 2)
            {
                Vector3 pl = face < 0 ? front : back, out1 = f.f * (face * proud);
                for (int i = 0; i < N; i++)
                {
                    float a0 = Mathf.PI * i / N, a1 = Mathf.PI * (i + 1) / N;
                    Vector3 d0 = -f.l * Mathf.Cos(a0) + Up * Mathf.Sin(a0), d1 = -f.l * Mathf.Cos(a1) + Up * Mathf.Sin(a1);
                    Vector3 c0 = ArchGeo.Y(pl, ySpring);
                    Vector3 i0 = c0 + d0 * r, i1 = c0 + d1 * r, o0 = c0 + d0 * (r + band), o1 = c0 + d1 * (r + band);
                    ArchGeo.Quad(mb, c.Trim, i0 + out1, o0 + out1, o1 + out1, i1 + out1, f.f * face);
                    ArchGeo.Quad(mb, c.Trim, o0, o1, o1 + out1, o0 + out1, d0 + d1);
                    ArchGeo.Quad(mb, c.Trim, i0, i1, i1 + out1, i0 + out1, -(d0 + d1));
                    if (i == 0 || i == N - 1)
                    {
                        Vector3 ie = i == 0 ? i0 : i1, oe = i == 0 ? o0 : o1;
                        ArchGeo.Quad(mb, c.Trim, ie, oe, oe + out1, ie + out1, -Up);
                    }
                }
            }
            Quaternion rot = Quaternion.LookRotation(f.f, Vector3.up);
            for (int sgn = -1; sgn <= 1; sgn += 2)
                mb.Box(c.Trim, ArchGeo.Y(f.p + f.l * (sgn * (r + 0.03f)), ySpring - 0.07f), new Vector3(0.14f, 0.14f, ArchThick + 0.1f), rot, 0);
            mb.Box(c.Trim, ArchGeo.Y(f.p, ySpring + r + 0.1f), new Vector3(0.22f, 0.36f, ArchThick + 0.1f), rot, 0);
            Vector3 ce = f.l * (r + embed - 0.02f), cf = f.f * (ArchThick * 0.5f + 0.04f);
            ArchDetail.Slab(mb, c.Trim, f.p - ce - cf, f.p + ce - cf, f.p + ce + cf, f.p - ce + cf, yTop, yTop + 0.08f, true);
        }
    }
}
