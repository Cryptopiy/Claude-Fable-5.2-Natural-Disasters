using UnityEngine;

namespace CASCADE
{
    /// <summary>What the wave leaves: the same footprints as broken wall stumps, foundation outlines or roofless shells, with rubble, mud and timber.</summary>
    internal static class RuinGen
    {
        public const float T = 0.42f;      // thickness of the wall stumps

        public static void Build(MeshBuilder mb, HouseDef h)
        {
            var rng = new System.Random(h.seed ^ 0x2545F491);
            Foundation(mb, h, rng);
            double v = rng.NextDouble();
            // 0 = stumps, 1 = foundation outline only, 2 = roofless shell. The harbour row is hit hardest.
            int type = h.zone == 0 ? (v < 0.45 ? 0 : v < 0.9 ? 1 : 2) : (v < 0.55 ? 0 : v < 0.78 ? 1 : 2);
            Material outer = HouseGen.ExtraOf(h).wall;          // a coloured house keeps its colour on what still stands
            Shell(mb, h, rng, 0f, 0f, h.hw, h.hd, h.MainH, type, outer);
            if (h.wingSide != 0) Shell(mb, h, rng, h.WingS, h.WingF, h.wingHw, h.wingHd, h.WingH, type == 2 && VKit.Chance(rng, 0.5f) ? 0 : type, outer);
            Debris(mb, h, rng, type);
            Remnants(mb, h, new System.Random(h.seed ^ 0x6C8E9CF5));
        }

        /// <summary>
        /// Height (above the foundation's base) up to which a retaining wall of height hgt is coursed masonry, as the intact
        /// house builds it (rows of cave windows and three metres of whitewash on top; cellar-door walls are not told apart).
        /// </summary>
        static float StoneHeight(HouseDef h, float hgt, bool windows)
        {
            float rest = hgt - 3f * HouseGen.CaveRows(h, hgt, windows);
            return rest > 6f ? rest - 3f : 0f;
        }

        /// <summary>A wall face from y0 to y1 (house frame) over the corners a, b (house frame at y = 0): masonry below yStone, wet whitewash above.</summary>
        static void SplitFace(MeshBuilder mb, HouseDef h, Material stone, float sa, float fa, float sb, float fb, float y0, float y1, float yStone, Vector3 n)
        {
            float ym = Mathf.Clamp(yStone, y0, y1);
            if (ym > y0 + 0.01f) VKit.Q(mb, stone, h.At(sa, y0, fa), h.At(sb, y0, fb), h.At(sb, ym, fb), h.At(sa, ym, fa), n);
            if (y1 > ym + 0.01f) VKit.Q(mb, VKit.Wet, h.At(sa, ym, fa), h.At(sb, ym, fb), h.At(sb, y1, fb), h.At(sa, y1, fa), n);
        }

        /// <summary>
        /// The terrace after the wave: the part under the house stands, the terrace in front of it has slumped. Its
        /// retaining wall is broken off lower down with a jagged top, and a scree of rubble runs down to it.
        /// </summary>
        static void Foundation(MeshBuilder mb, HouseDef h, System.Random rng)
        {
            float depthY = h.baseY - h.c.y, len = h.pS1 - h.pS0;
            Vector3 up = Vector3.up;
            Material stone = HouseGen.ExtraOf(h).stone;
            float sideStone = depthY < -6f ? -3f : depthY;               // as the intact side walls
            bool slump = h.apron >= 0.8f;
            float fCut = slump ? h.pF1 - h.apron * VKit.Rnd(rng, 0.75f, 1f) : h.pF1;
            float visible = Mathf.Max(-depthY - VillagePlan.FoundationDepth, 0.2f);
            float drop = !slump ? 0f : visible * (VKit.Chance(rng, 0.22f) ? VKit.Rnd(rng, 0.06f, 0.2f) : VKit.Rnd(rng, 0.35f, 0.72f));
            // standing part and scree
            VKit.Q(mb, VKit.Wet, h.At(h.pS0, 0f, h.pF0), h.At(h.pS1, 0f, h.pF0), h.At(h.pS1, 0f, fCut), h.At(h.pS0, 0f, fCut), up);
            if (slump) VKit.Q(mb, VKit.Ruin, h.At(h.pS0, 0f, fCut), h.At(h.pS1, 0f, fCut), h.At(h.pS1, -drop, h.pF1), h.At(h.pS0, -drop, h.pF1), up);
            for (int k = 0; k < 2; k++)
            {
                float s = k == 0 ? h.pS1 : h.pS0; Vector3 n = k == 0 ? h.S : -h.S;
                SplitFace(mb, h, stone, s, h.pF0, s, fCut, depthY, 0f, sideStone, n);
                if (slump)
                {
                    float ys = Mathf.Min(sideStone, -drop);
                    if (ys > depthY + 0.01f) VKit.Q(mb, stone, h.At(s, depthY, fCut), h.At(s, depthY, h.pF1), h.At(s, ys, h.pF1), h.At(s, ys, fCut), n);
                    float yb = Mathf.Max(ys, depthY);
                    VKit.Q(mb, VKit.Wet, h.At(s, yb, fCut), h.At(s, yb, h.pF1), h.At(s, -drop, h.pF1), h.At(s, 0f, fCut), n);
                }
            }
            SplitFace(mb, h, stone, h.pS0, h.pF0, h.pS1, h.pF0, depthY, 0f, depthY + StoneHeight(h, -depthY, h.backWindows), -h.F);
            // front wall: solid below, a row of broken teeth on top (never lower than the scree, so nothing is hollow)
            float solidTop = slump ? Mathf.Max(-drop - 0.9f, depthY + 0.2f) : 0f;
            SplitFace(mb, h, stone, h.pS0, h.pF1, h.pS1, h.pF1, depthY, solidTop, depthY + StoneHeight(h, -depthY, h.lowerWindows), h.F);
            if (slump)
            {
                float need = -drop - solidTop + 0.05f, t = 0f, prev = need + VKit.Rnd(rng, 0f, 1f);
                while (t < len - 0.05f)
                {
                    float piece = Mathf.Min(VKit.Rnd(rng, 0.8f, 2f), len - t);
                    if (len - t - piece < 0.5f) piece = len - t;
                    float hgt = need + VKit.Rnd(rng, 0f, 1f) * VKit.Rnd(rng, 0f, 1.3f);
                    Stump(mb, h.At(h.pS0 + t, solidTop, h.pF1), h.S * piece, -h.F * T, Mathf.Lerp(hgt, prev, 0.5f), hgt);
                    prev = hgt; t += piece;
                }
            }
            if (h.zone == 0) HouseGen.Pavement(mb, h);
        }

        static float PieceHeight(int type, float H, System.Random rng)
        {
            double v = rng.NextDouble();
            if (type == 1) return v < 0.1 ? 0f : VKit.Rnd(rng, 0.12f, 0.5f);
            if (type == 2)
            {
                float hc = Mathf.Min(H, 3.3f);
                return v < 0.08 ? 0f : v < 0.3 ? VKit.Rnd(rng, 0.8f, 1.5f) : VKit.Rnd(rng, 0.72f * hc, hc);
            }
            return v < 0.12 ? 0f : v < 0.27 ? VKit.Rnd(rng, 1.8f, Mathf.Min(H, 3.6f)) : VKit.Rnd(rng, 0.3f, 1.5f);
        }

        /// <summary>The four walls of one box as a row of broken pieces with slanted, jagged tops (outer faces in outer, or wet whitewash).</summary>
        public static void Shell(MeshBuilder mb, HouseDef h, System.Random rng, float cs, float cf, float a, float b, float H, int type, Material outer = null)
        {
            Vector3 c = h.At(cs, 0f, cf);
            for (int w = 0; w < 4; w++)
            {
                Vector3 n = w == 0 ? h.F : w == 1 ? -h.F : w == 2 ? h.S : -h.S;
                Vector3 u = VKit.WallU(n);
                float halfN = w < 2 ? b : a, halfU = w < 2 ? a : b;
                float trim = w < 2 ? 0f : T;                     // side walls butt against the front and back walls
                Vector3 p0 = c + n * halfN - u * (halfU - trim);
                Pieces(mb, p0, u, n, halfU * 2f - trim * 2f, H, type, rng, outer);
            }
            // what is left of the floor
            float ia = a - T, ib = b - T;
            if (ia > 0.3f && ib > 0.3f)
                VKit.Q(mb, type == 1 ? VKit.Mud : VKit.Ruin, h.At(cs - ia, 0.04f, cf - ib), h.At(cs + ia, 0.04f, cf - ib), h.At(cs + ia, 0.04f, cf + ib), h.At(cs - ia, 0.04f, cf + ib), Vector3.up);
        }

        public static void Pieces(MeshBuilder mb, Vector3 p0, Vector3 u, Vector3 n, float len, float H, int type, System.Random rng, Material outer = null)
        {
            float t = 0f, prev = PieceHeight(type, H, rng);
            while (t < len - 0.05f)
            {
                float piece = Mathf.Min(VKit.Rnd(rng, 0.7f, 1.7f), len - t);
                if (len - t - piece < 0.45f) piece = len - t;
                float hgt = PieceHeight(type, H, rng);
                if (hgt > 0.05f)
                {
                    // the top runs from the previous break towards this one, never down to nothing
                    float hA = Mathf.Max(0.1f, Mathf.Lerp(hgt, prev, VKit.Rnd(rng, 0f, 0.6f))), hB = Mathf.Max(0.1f, hgt * VKit.Rnd(rng, 0.6f, 1f));
                    Stump(mb, p0 + u * t, u * piece, -n * T, hA, hB, outer);
                }
                prev = hgt; t += piece;
            }
        }

        /// <summary>
        /// Wall fragment on the base parallelogram (o, o+du, o+dn) with heights hA at o and hB at o+du; wet sides, dark broken
        /// top. outer (optional) paints the face on the o..o+du side (the wall's outer face when dn points inwards).
        /// </summary>
        public static void Stump(MeshBuilder mb, Vector3 o, Vector3 du, Vector3 dn, float hA, float hB, Material outer = null)
        {
            Vector3 a0 = o, a1 = o + du, b0 = o + dn, b1 = o + du + dn;
            Vector3 ua = Vector3.up * hA, ub = Vector3.up * hB;
            VKit.Q(mb, outer != null ? outer : VKit.Wet, a0, a1, a1 + ub, a0 + ua, -dn);
            VKit.Q(mb, VKit.Wet, b0, b1, b1 + ub, b0 + ua, dn);
            VKit.Q(mb, VKit.Wet, a0, b0, b0 + ua, a0 + ua, -du);
            VKit.Q(mb, VKit.Wet, a1, b1, b1 + ub, a1 + ub, du);
            VKit.Q(mb, VKit.Ruin, a0 + ua, a1 + ub, b1 + ub, b0 + ua, Vector3.up);
        }

        static void Debris(MeshBuilder mb, HouseDef h, System.Random rng, int type)
        {
            float a = Mathf.Max(h.hw - 0.9f, 0.3f), b = Mathf.Max(h.hd - 0.8f, 0.3f);
            int mounds = type == 1 ? 1 + rng.Next(2) : 2 + rng.Next(3);
            for (int i = 0; i < mounds; i++)
            {
                // heaps may spill 0.4 m over the wall line, never further (the path corridor stays clear)
                float cs = VKit.Rnd(rng, -a, a), cf = VKit.Rnd(rng, -b, b);
                float rx = Mathf.Min(VKit.Rnd(rng, 0.8f, 2f), (h.hw + 0.4f - Mathf.Abs(cs)) / 1.13f), rz = Mathf.Min(VKit.Rnd(rng, 0.7f, 1.6f), (h.hd + 0.4f - Mathf.Abs(cf)) / 1.13f);
                VKit.Mound(mb, VKit.Ruin, h.At(cs, 0.03f, cf), rx, rz, VKit.Rnd(rng, 0.3f, 0.85f), HouseGen.Yaw(h), rng, 8);
            }
            int blocks = 3 + rng.Next(4);
            for (int i = 0; i < blocks; i++)
            {
                float sz = VKit.Rnd(rng, 0.4f, 1f);
                Vector3 p = h.At(VKit.Rnd(rng, -a, a), sz * 0.28f, VKit.Rnd(rng, -b, b));
                VKit.BoxTilt(mb, VKit.Wet, p, sz, sz * VKit.Rnd(rng, 0.45f, 0.8f), sz * VKit.Rnd(rng, 0.6f, 1.1f), VKit.Rnd(rng, 0f, 360f), VKit.Rnd(rng, -22f, 22f), VKit.Rnd(rng, -22f, 22f));
            }
            int timbers = 2 + rng.Next(4);
            float ti = Mathf.Max(h.hw - 1f, 0.2f), tj = Mathf.Max(h.hd - 1f, 0.2f);
            for (int i = 0; i < timbers; i++)
            {
                Vector3 p = h.At(VKit.Rnd(rng, -ti, ti), VKit.Rnd(rng, 0.25f, 1f), VKit.Rnd(rng, -tj, tj));
                VKit.BoxTilt(mb, VKit.WoodDk, p, 0.13f, 0.13f, VKit.Rnd(rng, 1.8f, 3.4f), VKit.Rnd(rng, 0f, 360f), VKit.Rnd(rng, -28f, 28f), VKit.Rnd(rng, 0f, 90f));
            }
            // silt banked up inside
            VKit.Mound(mb, VKit.Mud, h.At(VKit.Rnd(rng, -a, a) * 0.5f, 0.02f, 0f), Mathf.Min(h.hw * 0.9f, 3.4f), Mathf.Min(h.hd * 0.75f, 2f), VKit.Rnd(rng, 0.2f, 0.45f), HouseGen.Yaw(h), rng, 9);
        }

        /// <summary>
        /// Broken pieces of the dressing, inside the footprint (the path corridor stays clear): lengths of plaster coping, shutters
        /// or door leaves in the house's colour, a toppled pot, a shard of its blue dome or barrel roof, pergola timbers.
        /// </summary>
        static void Remnants(MeshBuilder mb, HouseDef h, System.Random r)
        {
            HouseGen.Extra x = HouseGen.ExtraOf(h);
            float a = Mathf.Max(h.hw - 0.9f, 0.3f), b = Mathf.Max(h.hd - 0.8f, 0.3f), yaw = HouseGen.Yaw(h);
            int n = 1 + r.Next(3);
            for (int i = 0; i < n; i++)
                VKit.BoxTilt(mb, VKit.Trim, h.At(VKit.Rnd(r, -a, a), 0.1f, VKit.Rnd(r, -b, b)), VKit.Rnd(r, 0.45f, 1.1f), 0.11f, 0.32f,
                    yaw + VKit.Rnd(r, 0f, 360f), VKit.Rnd(r, -12f, 12f), VKit.Rnd(r, -25f, 25f));
            int k = 1 + r.Next(2);
            for (int i = 0; i < k; i++)
                VKit.BoxTilt(mb, x.paint, h.At(VKit.Rnd(r, -a, a), VKit.Rnd(r, 0.22f, 0.4f), VKit.Rnd(r, -b, b)), 0.42f, 0.045f, VKit.Rnd(r, 1.0f, 1.25f),
                    VKit.Rnd(r, 0f, 360f), VKit.Rnd(r, -28f, 28f), VKit.Rnd(r, -20f, 20f));
            if (VKit.Chance(r, 0.45f))
            {
                // a pot on its side, its plant spilled
                Vector3 p = h.At(VKit.Rnd(r, -a, a), 0.16f, VKit.Rnd(r, -b, b)), d = VKit.Dir(VKit.Rnd(r, 0f, 360f));
                mb.Cylinder(VKit.Terra, p - d * 0.17f, p + d * 0.17f, 0.13f, 0.19f, 7, false);
                VKit.Blob(mb, VKit.Plant, p + d * 0.36f - Vector3.up * 0.06f, d * 0.22f, Vector3.up * 0.12f, Vector3.Cross(Vector3.up, d) * 0.2f, r, 0.25f, 5, 3);
            }
            if (x.dome != 0 || x.roof == VKit.DomeBlue)
            {
                Vector3 rr, uu, ff;
                VKit.Axes(yaw + VKit.Rnd(r, 0f, 360f), VKit.Rnd(r, 15f, 35f), VKit.Rnd(r, -25f, 25f), out rr, out uu, out ff);
                // a church's dome leaves a bigger piece (its shell is still within the footprint: the shard spans < 110 degrees)
                float rad = Mathf.Min(x.dome != 0 ? x.domeR : 1.3f, (x.dome == 3 ? 0.4f : 0.5f) + (x.dome == 3 ? 0.55f : 0.4f) * h.hd);
                Vector3 c = h.At(VKit.Rnd(r, -a, a) * 0.5f, 0.05f, VKit.Rnd(r, -b, b) * 0.5f) - uu * (rad * 0.45f);
                ChapelGen.DomeShard(mb, c, rr, uu, ff, rad, VKit.Rnd(r, 70f, 110f), VKit.DomeBlue);
            }
            if (x.dome == 3)
            {
                // the church's bell, thrown down on its side among the rubble
                Vector3 p = h.At(VKit.Rnd(r, -a, a) * 0.6f, 0.2f, VKit.Rnd(r, -b, b) * 0.6f), d = VKit.Dir(VKit.Rnd(r, 0f, 360f));
                d = (d + Vector3.up * 0.25f).normalized;
                mb.Cylinder(VKit.Bronze, p - d * 0.17f, p + d * 0.17f, 0.2f, 0.11f, 10, true);
            }
            if (x.pergola)
                for (int i = 0; i < 2; i++)
                    VKit.BoxTilt(mb, VKit.Wood, h.At(VKit.Rnd(r, -a, a), VKit.Rnd(r, 0.15f, 0.4f), VKit.Rnd(r, h.hd - 1f, h.hd - 0.25f)), 0.08f, 0.11f, VKit.Rnd(r, 1.2f, 2.2f),
                        yaw + 90f + VKit.Rnd(r, -40f, 40f), VKit.Rnd(r, -15f, 15f), VKit.Rnd(r, -15f, 15f));
        }

        /// <summary>The quay after the wave: mud banks either side of the walking strip, splintered timber, a few broken crates.</summary>
        public static void Quay(MeshBuilder mb, System.Random rng)
        {
            float y = Config.QuayHeight;
            for (float z = -256f; z < -30f; z += VKit.Rnd(rng, 6f, 12f))
            {
                // seaward
                float rx = VKit.Rnd(rng, 1.5f, 2.4f), rz = VKit.Rnd(rng, 2.4f, 5f);
                Vector3 p = new Vector3(VKit.Rnd(rng, -16.8f, -15.3f), y, z);
                if (VKit.PathGap(p.x + rx * 1.13f, z) > 0.5f && VKit.PathGap(p.x + rx, z + rz) > 0.5f && VKit.PathGap(p.x + rx, z - rz) > 0.5f)
                    VKit.Mound(mb, VKit.Mud, p, rx, rz, VKit.Rnd(rng, 0.25f, 0.6f), VKit.Rnd(rng, -8f, 8f), rng, 10);
                // landward
                rx = VKit.Rnd(rng, 1f, 1.45f); rz = VKit.Rnd(rng, 2.4f, 5f);
                Vector3 q = new Vector3(VKit.Rnd(rng, -2.3f, -1.5f), y, z + VKit.Rnd(rng, 2f, 5f));
                if (VKit.PathGap(q.x - rx * 1.13f, q.z) > 0.5f && VKit.PathGap(q.x - rx, q.z + rz) > 0.5f && VKit.PathGap(q.x - rx, q.z - rz) > 0.5f)
                    VKit.Mound(mb, VKit.Mud, q, rx, rz, VKit.Rnd(rng, 0.3f, 0.7f), VKit.Rnd(rng, -8f, 8f), rng, 10);
                // timber and crate fragments
                for (int i = 0; i < 2; i++)
                {
                    bool sea = VKit.Chance(rng, 0.5f);
                    Vector3 d = new Vector3(sea ? VKit.Rnd(rng, -17.5f, -13.8f) : VKit.Rnd(rng, -2.8f, -0.6f), y + VKit.Rnd(rng, 0.1f, 0.45f), z + VKit.Rnd(rng, -4f, 4f));
                    if (VKit.PathGap(d.x, d.z) < 1.9f) continue;
                    if (VKit.Chance(rng, 0.65f)) VKit.BoxTilt(mb, VKit.WoodDk, d, 0.13f, 0.13f, VKit.Rnd(rng, 1.4f, 2.6f), VKit.Rnd(rng, -30f, 30f), VKit.Rnd(rng, -12f, 12f), VKit.Rnd(rng, 0f, 90f));
                    else VKit.BoxTilt(mb, VKit.Wood, d, 0.62f, 0.3f, 0.42f, VKit.Rnd(rng, 0f, 360f), VKit.Rnd(rng, -30f, 30f), VKit.Rnd(rng, -40f, 40f));
                }
            }
            // the harbour gear thrown about: crushed lobster pots and tipped plastic fish boxes in the boats' colours
            Vector3 up = Vector3.up;
            for (int i = 0; i < 16; i++)
            {
                bool sea = VKit.Chance(rng, 0.6f);
                Vector3 p = new Vector3(sea ? VKit.Rnd(rng, -17.6f, -13.6f) : VKit.Rnd(rng, -2.9f, -0.7f), y, VKit.Rnd(rng, -255f, -32f));
                bool pot = VKit.Chance(rng, 0.5f);
                float yaw = VKit.Rnd(rng, 0f, 360f);
                int colour = rng.Next(4);
                if (VKit.PathGap(p.x, p.z) < 1.9f) continue;
                if (pot)
                {
                    Vector3 f = VKit.Dir(yaw), r = Vector3.Cross(up, f);
                    VKit.BoxTilt(mb, VKit.Wood, p + up * 0.03f, 0.48f, 0.04f, 0.72f, yaw, 4f, -6f);
                    VKit.Mound(mb, VKit.Net, p + up * 0.05f, 0.3f, 0.42f, 0.2f, yaw, rng, 7, 0f);
                    VKit.ArcTube(mb, VKit.WoodDk, p + up * 0.05f + f * 0.22f, r * 0.24f, up * 0.28f, 0f, 150f, 5, 0.02f, 4);
                }
                else
                {
                    Material m = colour == 0 ? VKit.BoatBlue : colour == 1 ? VKit.BoatRed : colour == 2 ? VKit.BoatYellow : VKit.BoatWhite;
                    VKit.BoxTilt(mb, m, p + up * 0.18f, 0.6f, 0.26f, 0.4f, yaw, VKit.Rnd(rng, -12f, 12f), VKit.Rnd(rng, -30f, 30f));
                }
            }
        }
    }
}
