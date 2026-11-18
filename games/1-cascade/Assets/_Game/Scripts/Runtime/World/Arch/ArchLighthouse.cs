using System.Collections.Generic;
using UnityEngine;

namespace CASCADE
{
    /// <summary>
    /// The goal: the keeper's house across the end of the path (blue door 1.3 m beyond the last path point, facing the
    /// walker, the keeper's window beside it), a paved apron, a low white wall, a stone bench, and the lighthouse tower
    /// beyond and to the right on the crest. House frame: x = right of the walking direction, z = forward, y above the floor.
    /// </summary>
    internal static class ArchLighthouse
    {
        public const float DoorAhead = 1.3f;                  // facade distance beyond the path end
        public const float HouseLeft = -1.7f, HouseRight = 5.3f, HouseDepth = 5f, HouseH = 3.4f, RoofParapet = 0.25f;
        public const float DoorW = 0.95f, DoorH = 2.0f, DoorRecess = 0.14f;
        public const float WinX = 1.8f, WinSill = 1.5f, WinW = 0.8f, WinH = 1.0f, WinRecess = 0.10f;
        public const float TowerAhead = 13f;                  // tower axis beyond the facade
        public const float TowerRightMin = 8f, TowerRightMax = 20f, TowerBehindCrest = 3.6f;   // it stands just behind the crest, right of the path
        public const float TowerH = 21f, TowerR0 = 2.6f, TowerR1 = 1.9f;
        public const float LanternH = 2.2f, LanternR = 1.35f;

        static readonly Vector3 Up = ArchGeo.Up;
        static Vector3 O, R, Fw;      // facade origin (door centre at floor level), right, forward

        static Vector3 H(float x, float y, float z) { return O + R * x + Up * y + Fw * z; }

        public static void Build(ArchCtx c)
        {
            float L = PathDef.Length;
            Vector3 E = PathDef.Pos(L);
            Fw = PathDef.FlatDir(L);
            R = -ArchGeo.Left(Fw);
            O = E + Fw * DoorAhead;
            House(c);
            Forecourt(c, L, E);
            Tower(c, E);
        }

        static void House(ArchCtx c)
        {
            MeshBuilder mb = c.S(O.y);
            Quaternion rot = Quaternion.LookRotation(Fw, Vector3.up);
            // foundation: 2 m below the lowest ground under the footprint
            float lo = O.y;
            for (int i = 0; i <= 4; i++)
                for (int j = 0; j <= 3; j++)
                    lo = Mathf.Min(lo, ArchGeo.Ground(H(Mathf.Lerp(HouseLeft, HouseRight + 2.6f, i / 4f), 0f, Mathf.Lerp(0f, HouseDepth, j / 3f))));
            float yb = lo - 2f - O.y;                         // relative to the floor

            // facade with the door and the keeper's window
            var ops = new List<ArchOpening>(2);
            float du0 = -HouseLeft - DoorW * 0.5f, wu0 = -HouseLeft + WinX - WinW * 0.5f;
            ops.Add(new ArchOpening { u0 = du0, u1 = du0 + DoorW, v0 = O.y, v1 = O.y + DoorH, depth = DoorRecess, panel = c.Blue });
            ops.Add(new ArchOpening { u0 = wu0, u1 = wu0 + WinW, v0 = O.y + WinSill, v1 = O.y + WinSill + WinH, depth = WinRecess, panel = c.WinKeeper });
            ArchGeo.FaceWithOpenings(mb, c.White, H(HouseLeft, 0f, 0f), H(HouseRight, 0f, 0f), O.y + yb, O.y + HouseH, -Fw, ops);
            ArchGeo.VQuad(mb, c.White, H(HouseLeft, 0f, 0f), H(HouseLeft, 0f, HouseDepth), O.y + yb, O.y + HouseH, -R);
            ArchGeo.VQuad(mb, c.White, H(HouseRight, 0f, 0f), H(HouseRight, 0f, HouseDepth), O.y + yb, O.y + HouseH, R);
            ArchGeo.VQuad(mb, c.White, H(HouseLeft, 0f, HouseDepth), H(HouseRight, 0f, HouseDepth), O.y + yb, O.y + HouseH, Fw);
            ArchGeo.Quad(mb, c.WhiteOld, H(HouseLeft, HouseH, 0f), H(HouseRight, HouseH, 0f), H(HouseRight, HouseH, HouseDepth), H(HouseLeft, HouseH, HouseDepth), Up);
            // roof parapet with a plaster coping, cornice band round the house
            float w = HouseRight - HouseLeft, cx = (HouseLeft + HouseRight) * 0.5f, py = HouseH + RoofParapet * 0.5f;
            mb.Box(c.White, H(cx, py, 0.1f), new Vector3(w, RoofParapet, 0.2f), rot, 8 | 4);
            mb.Box(c.White, H(cx, py, HouseDepth - 0.1f), new Vector3(w, RoofParapet, 0.2f), rot, 8 | 4);
            mb.Box(c.White, H(HouseLeft + 0.1f, py, HouseDepth * 0.5f), new Vector3(0.2f, RoofParapet, HouseDepth - 0.4f), rot, 4 | 8 | 16 | 32);
            mb.Box(c.White, H(HouseRight - 0.1f, py, HouseDepth * 0.5f), new Vector3(0.2f, RoofParapet, HouseDepth - 0.4f), rot, 4 | 8 | 16 | 32);
            float cy = HouseH + RoofParapet + 0.028f;
            mb.Box(c.Trim, H(cx, cy, 0.1f), new Vector3(w + 0.06f, 0.056f, 0.26f), rot, 0);
            mb.Box(c.Trim, H(cx, cy, HouseDepth - 0.1f), new Vector3(w + 0.06f, 0.056f, 0.26f), rot, 0);
            mb.Box(c.Trim, H(HouseLeft + 0.1f, cy, HouseDepth * 0.5f), new Vector3(0.26f, 0.056f, HouseDepth - 0.46f), rot, 16 | 32);
            mb.Box(c.Trim, H(HouseRight - 0.1f, cy, HouseDepth * 0.5f), new Vector3(0.26f, 0.056f, HouseDepth - 0.46f), rot, 16 | 32);
            float by = HouseH - 0.05f;
            mb.Box(c.Trim, H(cx, by, -0.015f), new Vector3(w + 0.06f, 0.1f, 0.03f), rot, 16);
            mb.Box(c.Trim, H(cx, by, HouseDepth + 0.015f), new Vector3(w + 0.06f, 0.1f, 0.03f), rot, 32);
            mb.Box(c.Trim, H(HouseLeft - 0.015f, by, HouseDepth * 0.5f), new Vector3(0.03f, 0.1f, HouseDepth), rot, 1);
            mb.Box(c.Trim, H(HouseRight + 0.015f, by, HouseDepth * 0.5f), new Vector3(0.03f, 0.1f, HouseDepth), rot, 2);
            // pumice plinth along the facade (either side of the door)
            float jamb = DoorW * 0.5f + 0.12f;
            mb.Box(c.Pumice, H((HouseLeft - jamb) * 0.5f, 0.15f, -0.0125f), new Vector3(-jamb - HouseLeft, 0.4f, 0.025f), rot, 16 | 8);
            mb.Box(c.Pumice, H((HouseRight + jamb) * 0.5f, 0.15f, -0.0125f), new Vector3(HouseRight - jamb, 0.4f, 0.025f), rot, 16 | 8);
            // chimney with a plaster cap and a hood, and a low annex on the right with a coping and a shuttered window
            mb.Box(c.White, H(HouseRight - 1.0f, HouseH + 0.5f, HouseDepth - 1.1f), new Vector3(0.5f, 1.0f, 0.5f), rot, 8);
            mb.Box(c.Trim, H(HouseRight - 1.0f, HouseH + 1.03f, HouseDepth - 1.1f), new Vector3(0.62f, 0.06f, 0.62f), rot, 0);
            mb.Box(c.WhiteOld, H(HouseRight - 1.0f, HouseH + 1.14f, HouseDepth - 1.1f), new Vector3(0.3f, 0.16f, 0.3f), rot, 8);
            mb.Box(c.Trim, H(HouseRight - 1.0f, HouseH + 1.24f, HouseDepth - 1.1f), new Vector3(0.44f, 0.04f, 0.44f), rot, 0);
            float ah = 2.5f;
            mb.Box(c.White, H(HouseRight + 1.3f, (ah + yb) * 0.5f, 3.0f), new Vector3(2.6f, ah - yb, 3.0f), rot, 2 | 4 | 8);
            mb.Box(c.Trim, H(HouseRight + 1.315f, ah + 0.03f, 3.0f), new Vector3(2.63f, 0.06f, 3.06f), rot, 2);
            ArchDetail.Surround(mb, c.Trim, H(HouseRight + 1.3f, 0.95f, 1.5f), R, -Fw, 0.66f, 0.78f, 0.07f, 0.03f, true, O.y + 0.89f, O.y + 0.89f);
            for (int e = -1; e <= 1; e += 2)
                ArchDetail.Shutter(mb, c.ShutterRed, H(HouseRight + 1.3f + e * 0.165f, 0.95f + 0.39f, 1.5f), -Fw, 0.326f, 0.776f);

            // door: pumice step, plaster surround and hood on consoles, panelled leaf, bronze knob
            mb.Box(c.Pumice, H(0f, 0.03f, -0.22f), new Vector3(DoorW + 0.4f, 0.06f, 0.44f), rot, 8);
            ArchDetail.Surround(mb, c.Trim, H(0f, 0f, 0f), R, -Fw, DoorW, DoorH, 0.12f, 0.035f, false, O.y, O.y);
            mb.Box(c.Trim, H(0f, DoorH + 0.42f, -0.2f), new Vector3(DoorW + 0.56f, 0.07f, 0.4f), rot, 16);
            for (int e = -1; e <= 1; e += 2)
                mb.Box(c.Trim, H(e * (DoorW * 0.5f + 0.1f), DoorH + 0.29f, -0.13f), new Vector3(0.08f, 0.2f, 0.26f), rot, 16 | 4);
            for (int e = -1; e <= 1; e += 2)
                for (int row = 0; row < 2; row++)
                {
                    float ph = row == 0 ? 0.7f : 0.86f, pyc = row == 0 ? 0.16f + ph * 0.5f : 1.04f + ph * 0.5f;
                    mb.Box(c.Blue, H(e * 0.21f, pyc, DoorRecess - 0.0125f), new Vector3(0.34f, ph, 0.025f), rot, 16);
                }
            mb.Cylinder(c.Bronze, H(0.33f, 1.0f, DoorRecess), H(0.33f, 1.0f, DoorRecess - 0.07f), 0.012f, 0.03f, 6, true);

            // keeper's window: plaster surround and sill, glazing bars, open louvred green shutters
            float wy = WinSill + WinH * 0.5f;
            ArchDetail.Surround(mb, c.Trim, H(WinX, WinSill, 0f), R, -Fw, WinW, WinH, 0.08f, 0.035f, true, O.y + WinSill - 0.06f, O.y + WinSill - 0.06f);
            mb.Box(c.White, H(WinX, wy, WinRecess - 0.02f), new Vector3(0.04f, WinH, 0.03f), rot, 16 | 4 | 8);                       // glazing bars
            mb.Box(c.White, H(WinX, wy, WinRecess - 0.02f), new Vector3(WinW, 0.04f, 0.03f), rot, 16 | 1 | 2);
            float sw = WinW * 0.5f, sx = WinW * 0.5f + 0.08f + sw * 0.5f + 0.03f;
            ArchDetail.Shutter(mb, c.ShutterGreen, H(WinX - sx, wy, 0f), -Fw, sw, WinH);
            ArchDetail.Shutter(mb, c.ShutterGreen, H(WinX + sx, wy, 0f), -Fw, sw, WinH);

            c.res.keeperDoorPos = H(0f, DoorH * 0.5f, DoorRecess);
            c.res.keeperWindowPos = H(WinX, wy, WinRecess);
            c.res.keeperWindowNormal = -Fw;
        }

        /// <summary>Paved apron between the path end and the facade, the low white wall on the bay side, a stone bench.</summary>
        static void Forecourt(ArchCtx c, float L, Vector3 E)
        {
            MeshBuilder mb = c.S(O.y);
            Quaternion rot = Quaternion.LookRotation(Fw, Vector3.up);
            float hw = ArchGeo.Hw(L);
            float x0 = -(hw + 0.4f), x1 = HouseRight + 0.5f;
            mb.Box(c.Paving, H((x0 + x1) * 0.5f, -0.6f, -DoorAhead * 0.5f + 0.01f), new Vector3(x1 - x0, 1.2f, DoorAhead + 0.02f), rot, 8 | 16);

            // low wall: along the bay edge of the last metres, round the corner, into the side of the house
            var pts = new List<Vector3>(8);
            float sA = L - ArchPromenade.RailStopBeforeEnd;
            for (int i = 0; i <= 4; i++)
            {
                ArchFrame f = ArchGeo.Frame(Mathf.Lerp(sA + 0.2f, L, i / 4f));
                pts.Add(f.At(f.hw + 0.3f));
            }
            pts.Add(H(-(hw + 0.3f), 0f, 0.3f));
            pts.Add(H(HouseLeft + 0.1f, 0f, 0.3f));
            ArchGeo.CopedWall(c, pts, 0.7f, 0.3f, 2.5f);

            ArchParts.Bench(c, H(4.45f, 0f, -0.3f), R);
            var rng = new System.Random(7741);
            ArchDetail.Pot(c.F(O.y), c, H(-2.9f, 0f, -0.3f), 1.3f, rng, true);
            ArchDetail.Pot(c.F(O.y), c, H(3.36f, 0f, -0.32f), 1.0f, rng, false);
            Cistern(c);
        }

        /// <summary>Vaulted whitewashed cistern beside the forecourt with a well-head, an iron lid and a pulley hoop.</summary>
        static void Cistern(ArchCtx c)
        {
            const float X = 7.1f, Z = -0.4f, HL = 0.9f, HD = 0.8f, Body = 0.75f;
            MeshBuilder mb = c.S(O.y);
            Quaternion rot = Quaternion.LookRotation(Fw, Vector3.up);
            float lo = O.y;
            for (int i = -1; i <= 1; i++)
                for (int j = -1; j <= 1; j++)
                    lo = Mathf.Min(lo, ArchGeo.Ground(H(X + i * HL, 0f, Z + j * HD)));
            float yb = lo - 0.6f - O.y;
            mb.Box(c.White, H(X, (Body + yb) * 0.5f, Z), new Vector3(HL * 2f, Body - yb, HD * 2f), rot, 4 | 8);
            mb.Box(c.Trim, H(X, Body + 0.02f, Z), new Vector3(HL * 2f + 0.06f, 0.04f, HD * 2f + 0.06f), rot, 0);
            mb.Cylinder(c.WhiteOld, H(X - HL + 0.03f, Body + 0.04f, Z), H(X + HL - 0.03f, Body + 0.04f, Z), HD - 0.03f, HD - 0.03f, 16, true);   // barrel vault
            Vector3 wb = H(X, Body + 0.04f + HD - 0.16f, Z);
            ArchDetail.Lathe(mb, c.White, wb, new[] { 0f, 0.34f, 0.38f, 0.34f, 0.38f, 0.39f, 0.46f, 0.39f }, 12, true);
            mb.Cylinder(c.Iron, wb + Up * 0.46f, wb + Up * 0.49f, 0.29f, 0.29f, 12, true);
            tmp.Clear();
            for (int k = 0; k <= 8; k++)
            {
                float a = Mathf.PI * k / 8f;
                tmp.Add(wb + Up * 0.46f + R * (Mathf.Cos(a) * 0.34f) + Up * (Mathf.Sin(a) * 0.5f));
            }
            c.F(O.y).Tube(c.Iron, tmp, 0.02f, 5);
        }

        static readonly List<Vector3> tmp = new List<Vector3>(12);

        /// <summary>Low coped wall round the tower on its own ground, open towards the house with two gate piers.</summary>
        static void CompoundWall(ArchCtx c, Vector3 B, Vector3 dT)
        {
            const float Rad = 5.4f, Gate = 0.42f;
            const int N = 22;
            Vector3 side = ArchGeo.Left(dT);
            var pts = new List<Vector3>(N + 1);
            var below = new List<float>(N + 1);
            for (int i = 0; i <= N; i++)
            {
                float a = Mathf.Lerp(Gate, Mathf.PI * 2f - Gate, i / (float)N);
                Vector3 d = dT * Mathf.Cos(a) + side * Mathf.Sin(a);
                Vector3 p = B + d * Rad;
                p.y = WorldGen.Height(p.x, p.z);
                pts.Add(p);
                below.Add(ArchGeo.Depth(p + Up * 0.3f, d, 1.4f, 0.8f, 6f));
            }
            ArchGeo.CopedWall(c, pts, 0.8f, 0.32f, 1f, below);
            for (int e = 0; e <= 1; e++)
            {
                Vector3 p = pts[e == 0 ? 0 : N];
                Vector3 d = ArchGeo.Flat(p - B).normalized;
                ArchDetail.Pier(c.S(p.y), c, p + d * 0.0f, d, 0.46f, 1.05f, 0.9f);
            }
        }

        static void Tower(ArchCtx c, Vector3 E)
        {
            // the ground falls steeply to the bay right up to the crest: find the crest to the right of the path and stand behind it
            float crest = TowerRightMin;
            for (float x = 3f; x < TowerRightMax; x += 0.5f)
            {
                crest = x;
                if (ArchGeo.Ground(H(x + 0.5f, 0f, TowerAhead)) - ArchGeo.Ground(H(x, 0f, TowerAhead)) < 0.2f) break;
            }
            Vector3 T = H(Mathf.Clamp(crest + TowerBehindCrest, TowerRightMin, TowerRightMax), 0f, TowerAhead);
            float gMin = 1e9f, gMax = -1e9f;
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI * 0.25f;
                float g = WorldGen.Height(T.x + Mathf.Cos(a) * 3f, T.z + Mathf.Sin(a) * 3f);
                gMin = Mathf.Min(gMin, g); gMax = Mathf.Max(gMax, g);
            }
            float yg = Mathf.Max(gMax, WorldGen.Height(T.x, T.z));      // the tower stands on the highest ground around its foot
            Vector3 B = new Vector3(T.x, yg, T.z);
            MeshBuilder mb = c.S(yg);
            mb.Cylinder(c.WhiteOld, ArchGeo.Y(B, gMin - 2f), B + Up * 0.5f, 3.0f, 3.0f, 20, false);                // plinth and foundation
            ArchDetail.Lathe(mb, c.Trim, B + Up * 0.38f, new[] { 0f, 2.98f, 0f, 3.04f, 0.12f, 3.04f, 0.12f, 2.5f }, 20, false);   // plinth band (closes the top)
            mb.Cylinder(c.White, B + Up * 0.5f, B + Up * (TowerH - 0.6f), TowerR0, TowerR1 + 0.02f, 20, false);   // shaft
            ArchDetail.Lathe(mb, c.Trim, B + Up * (TowerH - 0.76f), new[] { 0f, 1.9f, 0f, 1.99f, 0.14f, 1.99f, 0.14f, 1.9f }, 20, false);   // string course
            mb.Cylinder(c.White, B + Up * (TowerH - 0.6f), B + Up * TowerH, TowerR1 + 0.02f, 2.8f, 20, false);    // corbel
            for (int i = 0; i < 16; i++)                                                                          // stone consoles under the gallery
            {
                float a = (i + 0.5f) * Mathf.PI / 8f;
                Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)), t = new Vector3(-d.z, 0f, d.x) * 0.065f;
                Vector3 p0 = B + d * 1.9f + Up * (TowerH - 0.85f), p1 = B + d * 2.96f + Up * (TowerH - 0.01f), p2 = B + d * 2.0f + Up * (TowerH - 0.01f);
                ArchGeo.Tri(mb, c.White, p0 + t, p1 + t, p2 + t, t);
                ArchGeo.Tri(mb, c.White, p0 - t, p1 - t, p2 - t, -t);
                ArchGeo.Quad(mb, c.White, p0 - t, p0 + t, p1 + t, p1 - t, d - Up);
            }
            mb.Cylinder(c.Iron, B + Up * TowerH, B + Up * (TowerH + 0.14f), 3.0f, 3.0f, 20, true);                // gallery deck
            float yd = TowerH + 0.14f;
            mb.Cylinder(c.White, B + Up * yd, B + Up * (yd + 0.7f), 1.42f, 1.42f, 16, true);                      // lantern base
            ArchDetail.Lathe(mb, c.Trim, B + Up * (yd + 0.62f), new[] { 0f, 1.4f, 0f, 1.48f, 0.1f, 1.48f, 0.1f, 1.38f }, 16, false);   // its cornice (top 2 cm above the base)
            float yl = yd + 0.7f;
            mb.Cylinder(c.Beacon, B + Up * yl, B + Up * (yl + LanternH), LanternR, LanternR, 8, false);           // glazing
            for (int i = 0; i < 8; i++)                                                                           // 8 mullions on the glass edges
            {
                float a = i * Mathf.PI * 0.25f;
                Vector3 d = new Vector3(-Mathf.Sin(a), 0f, -Mathf.Cos(a)) * (LanternR + 0.01f);                   // same basis as MeshBuilder.Cylinder
                mb.Cylinder(c.Iron, B + d + Up * yl, B + d + Up * (yl + LanternH), 0.045f, 0.045f, 6, false);
            }
            for (int k = 1; k <= 2; k++)                                                                          // glazing bars across the panes
                ArchGeo.RingTube(mb, c.Iron, B + Up * (yl + LanternH * k / 3f), new Vector3(0f, 0f, -1f), new Vector3(-1f, 0f, 0f), LanternR + 0.03f, 0.03f, 8, 5);
            float ye = yl + LanternH;
            mb.Cylinder(c.Iron, B + Up * ye, B + Up * (ye + 0.14f), 1.52f, 1.52f, 16, true);                      // eave
            mb.Dome(c.Iron, B + Up * (ye + 0.14f), 1.45f, 0.72f, 16, 5);
            Vector3 dc = B + Up * (ye + 0.14f);
            for (int i = 0; i < 8; i++)                                                                           // ribs on the cupola, on its meridians
            {
                float a = (i * 2 + 1) * Mathf.PI / 8f;
                Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                tmp.Clear();
                for (int k = 0; k <= 5; k++)
                {
                    float el = k * 1.3f / 5f;
                    tmp.Add(dc + d * (Mathf.Cos(el) * 1.465f) + Up * (Mathf.Sin(el) * 1.45f * 0.72f + 0.012f));
                }
                mb.Tube(c.Iron, tmp, 0.03f, 5);
            }
            ArchDetail.Lathe(mb, c.Iron, B + Up * (ye + 1.08f), new[] { 0f, 0.2f, 0.12f, 0.17f, 0.12f, 0.21f, 0.18f, 0.21f, 0.18f, 0.12f, 0.3f, 0.11f }, 10, false);   // vent stack
            ArchDetail.Lathe(mb, c.Iron, B + Up * (ye + 1.31f), new[] { 0f, 0f, 0.04f, 0.145f, 0.14f, 0.24f, 0.28f, 0.28f, 0.42f, 0.24f, 0.52f, 0.145f, 0.56f, 0f }, 10, false);   // vent ball
            ArchDetail.Lathe(mb, c.Iron, B + Up * (ye + 1.84f), new[] { 0f, 0.05f, 0.08f, 0.04f, 0.08f, 0.06f, 0.12f, 0.06f, 0.12f, 0.03f, 0.6f, 0.012f }, 6, true);   // finial and conductor
            c.res.beaconPos = B + Up * (yl + LanternH * 0.5f);

            // gallery railing: square posts with acorn finials, three ring rails, pickets
            MeshBuilder fb = c.F(yg);
            Vector3 gc = B + Up * yd;
            const float GR = 2.88f;
            const int Posts = 24, Pick = 4;
            for (int i = 0; i < Posts; i++)
            {
                float a = i * Mathf.PI * 2f / Posts;
                Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 p = gc + d * GR;
                fb.Box(c.Iron, p + Up * 0.53f, new Vector3(0.05f, 1.06f, 0.05f), Quaternion.LookRotation(d, Vector3.up), 8);
                ArchDetail.Lathe(fb, c.Iron, p + Up * 1.06f, new[] { 0f, 0f, 0f, 0.035f, 0.03f, 0.035f, 0.065f, 0.038f, 0.11f, 0f }, 6, false);
                for (int j = 1; j < Pick; j++)
                {
                    float aj = a + j * Mathf.PI * 2f / (Posts * Pick);
                    Vector3 q = gc + new Vector3(Mathf.Cos(aj), 0f, Mathf.Sin(aj)) * GR;
                    fb.Cylinder(c.Iron, q, q + Up * 0.99f, ArchParts.PicketRadius, ArchParts.PicketRadius, 4, false);
                }
            }
            ArchGeo.RingTube(fb, c.Iron, gc + Up * 1.0f, Vector3.right, Vector3.forward, GR, 0.026f, 48, 6);
            ArchGeo.RingTube(fb, c.Iron, gc + Up * 0.55f, Vector3.right, Vector3.forward, GR, 0.016f, 48, 5);
            ArchGeo.RingTube(fb, c.Iron, gc + Up * 0.1f, Vector3.right, Vector3.forward, GR, 0.016f, 48, 5);

            // door (plaster frame, cornice, panelled leaf) up three pumice steps; three slit windows with plaster surrounds
            Vector3 dT = ArchGeo.Flat(E - B).normalized;
            Quaternion rot = Quaternion.LookRotation(dT, Vector3.up);
            Vector3 dr = rot * Vector3.right;
            mb.Box(c.Trim, B + dT * (TowerR0 - 0.02f) + Up * 1.65f, new Vector3(1.4f, 2.3f, 0.3f), rot, 32);
            mb.Box(c.Trim, B + dT * (TowerR0 + 0.02f) + Up * 2.85f, new Vector3(1.62f, 0.1f, 0.38f), rot, 0);
            mb.Box(c.Blue, B + dT * (TowerR0 + 0.05f) + Up * 1.5f, new Vector3(1.0f, 2.0f, 0.2f), rot, 32);
            for (int e = -1; e <= 1; e += 2)
                for (int row = 0; row < 2; row++)
                {
                    float ph = row == 0 ? 0.72f : 0.88f, pyc = row == 0 ? 0.68f + ph * 0.5f : 1.52f + ph * 0.5f;
                    mb.Box(c.Blue, B + dT * (TowerR0 + 0.1625f) + dr * (e * 0.23f) + Up * pyc, new Vector3(0.36f, ph, 0.025f), rot, 32);
                }
            mb.Cylinder(c.Bronze, B + dT * (TowerR0 + 0.15f) + dr * 0.36f + Up * 1.5f, B + dT * (TowerR0 + 0.22f) + dr * 0.36f + Up * 1.5f, 0.012f, 0.03f, 6, true);
            for (int k = 1; k <= 3; k++)
            {
                float top = k * 0.16f, rOut = 3.0f + (4 - k) * 0.3f;
                mb.Box(c.Pumice, B + dT * ((2.9f + rOut) * 0.5f) + Up * ((top - 1.5f) * 0.5f), new Vector3(1.7f - 0.1f * k, top + 1.5f, rOut - 2.9f), rot, 8);
            }
            for (int i = 0; i < 3; i++)
            {
                float h = 6.5f + i * 5f;
                float r = Mathf.Lerp(TowerR0, TowerR1, (h - 0.5f) / (TowerH - 1.1f));
                mb.Box(c.WinDark, B + dT * (r - 0.08f) + Up * h, new Vector3(0.4f, 0.9f, 0.2f), rot, 32);
                for (int e = -1; e <= 1; e += 2)
                    mb.Box(c.Trim, B + dT * (r - 0.02f) + dr * (e * 0.24f) + Up * h, new Vector3(0.08f, 1.06f, 0.16f), rot, 0);
                mb.Box(c.Trim, B + dT * (r - 0.02f) + Up * (h + 0.49f), new Vector3(0.4f, 0.08f, 0.16f), rot, 0);
                mb.Box(c.Trim, B + dT * (r - 0.01f) + Up * (h - 0.485f), new Vector3(0.56f, 0.07f, 0.2f), rot, 0);
            }
            CompoundWall(c, B, dT);
        }
    }
}
