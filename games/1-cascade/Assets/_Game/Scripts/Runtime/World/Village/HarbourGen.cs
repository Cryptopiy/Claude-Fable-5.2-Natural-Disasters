using System.Collections.Generic;
using UnityEngine;

namespace CASCADE
{
    /// <summary>Traces of people on the quay: nets, crates, rope coils, lobster pots, barrels, laid cafe tables and chairs.</summary>
    internal static class HarbourGen
    {
        // the walking strip of the quay (x in [-11.5,-4.5]) stays empty; props keep to these two strips
        public const float SeaX0 = -18.5f, SeaX1 = -12.6f, LandX0 = -3.8f, LandX1 = 0.25f;
        static readonly List<Vector3> pts = new List<Vector3>(16);
        static readonly List<float> usedZ = new List<float>(48);

        public static void Build(MeshBuilder mb, System.Random rng, List<Vector3> perches)
        {
            float y = Config.QuayHeight;
            usedZ.Clear();
            // ---- seaward strip: working gear ----
            for (float z = -257f; z < -27f; z += VKit.Rnd(rng, 5.5f, 9.5f))
            {
                int kind = rng.Next(6);
                float x = VKit.Rnd(rng, SeaX0 + 1.2f, SeaX1 - 1.4f);
                if (VKit.PathGap(x + 1.6f, z) < 0.6f) continue;
                usedZ.Add(z);
                Vector3 p = new Vector3(x, y, z);
                float yaw = VKit.Rnd(rng, 0f, 360f);
                if (kind == 0 || kind == 5) Nets(mb, p, yaw, rng);
                else if (kind == 1) { float top = Crates(mb, p, yaw, rng, 3 + rng.Next(5)); if (perches != null && perches.Count < 60) perches.Add(p + Vector3.up * top); }
                else if (kind == 2) { Coil(mb, p, rng); if (VKit.Chance(rng, 0.5f)) Coil(mb, p + new Vector3(VKit.Rnd(rng, 0.9f, 1.3f), 0f, VKit.Rnd(rng, -0.6f, 0.6f)), rng); }
                else if (kind == 3)
                {
                    int n = 2 + rng.Next(4);
                    for (int i = 0; i < n; i++)
                    {
                        bool onTop = i >= 2 && VKit.Chance(rng, 0.6f);
                        Vector3 q = p + VKit.Dir(yaw + 90f) * ((onTop ? i - 2 : i) * 0.62f - 0.5f) + (onTop ? Vector3.up * 0.47f : Vector3.zero);
                        LobsterPot(mb, q, yaw + VKit.Rnd(rng, -9f, 9f));
                    }
                }
                else
                {
                    int n = 1 + rng.Next(3);
                    for (int i = 0; i < n; i++) Barrel(mb, p + VKit.Dir(yaw + i * 115f) * (i == 0 ? 0f : 0.72f), rng);
                    if (perches != null && perches.Count < 60) perches.Add(p + Vector3.up * 0.92f);
                }
            }
            // ---- landward strip: things left by the doors, and the two cafes ----
            List<HouseDef> hs = VillagePlan.Houses;
            for (int i = 0; i < hs.Count; i++)
            {
                HouseDef h = hs[i];
                if (h.zone != 0) continue;
                float xf = h.c.x - h.hd;                                   // facade plane
                if (h.cafe) { Cafe(mb, h, xf, rng); continue; }
                if (!VKit.Chance(rng, 0.55f)) continue;
                float s = h.doorS < 90f ? (h.doorS > 0f ? h.doorS - VKit.Rnd(rng, 1.6f, 2.6f) : h.doorS + VKit.Rnd(rng, 1.6f, 2.6f)) : 0f;
                s = Mathf.Clamp(s, -h.hw + 0.6f, h.hw - 0.6f);
                Vector3 p = new Vector3(Mathf.Min(xf - VKit.Rnd(rng, 0.75f, 1.3f), LandX1 - 0.5f), y, h.c.z + s);
                int kind = rng.Next(3);
                if (kind == 0) Crates(mb, p, VKit.Rnd(rng, -12f, 12f), rng, 2 + rng.Next(3));
                else if (kind == 1) { Barrel(mb, p, rng); if (VKit.Chance(rng, 0.5f)) Barrel(mb, p + new Vector3(-0.1f, 0f, 0.7f), rng); }
                else TableSet(mb, p + new Vector3(-0.5f, 0f, 0f), VKit.Rnd(rng, -15f, 15f), rng, 2, false);
            }
            Gear(mb, new System.Random(Village.Seed + 41));
        }

        /// <summary>
        /// More working gear in the long gaps the first pass left on the seaward strip: stacks of plastic fish boxes in the boats'
        /// colours and pyramids of lobster pots (own stream, so the first pass and its gull perches are unchanged). Keeps off the
        /// quay lamps.
        /// </summary>
        static void Gear(MeshBuilder mb, System.Random xr)
        {
            float y = Config.QuayHeight, prev = -261f;
            for (int i = 0; i <= usedZ.Count; i++)
            {
                float next = i < usedZ.Count ? usedZ[i] : -23f;
                if (next - prev > 8.5f)
                {
                    float z = (prev + next) * 0.5f + VKit.Rnd(xr, -0.8f, 0.8f);
                    float x = VKit.Rnd(xr, SeaX0 + 1.4f, SeaX1 - 1.6f);
                    float yaw = VKit.Rnd(xr, -15f, 15f) + (VKit.Chance(xr, 0.5f) ? 0f : 90f);
                    bool boxes = VKit.Chance(xr, 0.55f);
                    bool clear = VKit.PathGap(x + 1.6f, z) >= 0.6f;
                    for (int k = 0; k < ArchQuay.QuayLamps; k++)
                        if (Mathf.Abs(z - (-222f + k * ArchQuay.QuayLampSpacing)) < 2f) clear = false;
                    if (clear)
                    {
                        Vector3 p = new Vector3(x, y, z);
                        if (boxes) BoxStack(mb, p, yaw, xr); else PotPyramid(mb, p, yaw, xr);
                    }
                }
                prev = next;
            }
        }

        /// <summary>Columns of plastic fish boxes, each column mostly one boat colour.</summary>
        static void BoxStack(MeshBuilder mb, Vector3 p, float yaw, System.Random rng)
        {
            const float w = 0.6f, hgt = 0.26f, d = 0.4f;
            Vector3 along = VKit.Dir(yaw + 90f), fw = VKit.Dir(yaw);
            int cols = 2 + rng.Next(2);
            for (int c = 0; c < cols; c++)
            {
                int levels = 2 + rng.Next(4), col = rng.Next(4);
                Vector3 q0 = p + along * ((c - (cols - 1) * 0.5f) * (w + 0.04f)) + fw * VKit.Rnd(rng, -0.06f, 0.06f);
                for (int l = 0; l < levels; l++)
                {
                    int cc = VKit.Chance(rng, 0.2f) ? rng.Next(4) : col;
                    Material m = cc == 0 ? VKit.BoatBlue : cc == 1 ? VKit.BoatRed : cc == 2 ? VKit.BoatYellow : VKit.BoatWhite;
                    VKit.BoxOn(mb, m, q0 + Vector3.up * (l * hgt), w, hgt - 0.02f, d, yaw + VKit.Rnd(rng, -4f, 4f));
                }
            }
        }

        /// <summary>Lobster pots stacked 3-2-1 (or 2-1); only the top row shows its hoops.</summary>
        static void PotPyramid(MeshBuilder mb, Vector3 p, float yaw, System.Random rng)
        {
            Vector3 along = VKit.Dir(yaw + 90f);
            int bottom = VKit.Chance(rng, 0.5f) ? 3 : 2;
            for (int l = 0; l < bottom; l++)
            {
                int n = bottom - l;
                for (int i = 0; i < n; i++)
                    LobsterPot(mb, p + along * ((i - (n - 1) * 0.5f) * 0.5f) + Vector3.up * (l * 0.44f), yaw + VKit.Rnd(rng, -5f, 5f), l == bottom - 1);
            }
        }

        static void Cafe(MeshBuilder mb, HouseDef h, float xf, System.Random rng)
        {
            float y = Config.QuayHeight;
            float z0 = h.c.z - h.hw + 0.9f, z1 = h.c.z + h.hw - 0.9f;
            float doorZ = h.doorS < 90f ? h.c.z + h.doorS : 1e9f;          // S = +z for the harbour row
            bool tipped = false;
            for (int row = 0; row < 2; row++)
            {
                float x = Mathf.Min(xf - 1.75f - row * 1.75f, LandX1 - 1f - row * 1.75f);
                if (x < LandX0 + 0.75f) x = LandX0 + 0.75f;
                for (float z = z0 + row * 0.9f; z <= z1; z += VKit.Rnd(rng, 2.15f, 2.6f))
                {
                    if (row == 0 && Mathf.Abs(z - doorZ) < 0.95f) continue;
                    bool tip = !tipped && row == 1 && VKit.Chance(rng, 0.4f);
                    tipped |= tip;
                    TableSet(mb, new Vector3(x + VKit.Rnd(rng, -0.12f, 0.12f), y, z), VKit.Rnd(rng, -10f, 10f), rng, 2 + rng.Next(2), tip);
                }
            }
            Tavern(mb, h);
        }

        /// <summary>
        /// The tavern front of a harbour cafe: a name board between the storeys (painted frame, off-white field, dark lettering)
        /// with a wall lantern either side whose glass lights up with the windows.
        /// </summary>
        static void Tavern(MeshBuilder mb, HouseDef h)
        {
            var xr = new System.Random(h.seed ^ 0x51ED27);
            Material frame = HouseGen.ExtraOf(h).paint;
            if (frame == VKit.WoodDk) frame = VKit.Blue;
            Vector3 n = h.F, u = h.S, up = Vector3.up;
            float half = Mathf.Min(1.1f, h.hw - 0.7f);
            float sc = Mathf.Clamp(h.doorS < 90f ? h.doorS : 0f, -h.hw + half + 0.45f, h.hw - half - 0.45f);
            const float yc = 3.13f;                                                 // above the awning, under the upper sills
            Vector3 c = h.At(sc, yc, h.hd);
            VKit.Box(mb, frame, c + n * 0.025f, u * half, up * 0.22f, n * 0.025f, 32);
            VKit.Box(mb, VKit.Trim, c + n * 0.06f, u * (half - 0.08f), up * 0.15f, n * 0.01f, 32);
            int letters = Mathf.RoundToInt((half - 0.2f) * 2f / 0.17f);
            for (int i = 0; i < letters; i++)
            {
                if (i == letters / 2 && letters > 6) continue;                      // a gap between two words
                float t = Mathf.Lerp(-(half - 0.2f), half - 0.2f, letters > 1 ? i / (float)(letters - 1) : 0.5f);
                float lh = VKit.Rnd(xr, 0.15f, 0.19f);
                VKit.Box(mb, VKit.WoodDk, c + u * t + n * 0.08f + up * VKit.Rnd(xr, -0.01f, 0.01f), u * 0.045f, up * (lh * 0.5f), n * 0.01f, 32);
            }
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 w = c + u * (s * (half + 0.28f)) + up * 0.05f;
                VKit.Box(mb, VKit.Iron, w + n * 0.17f, u * 0.02f, up * 0.02f, n * 0.17f, 32);                       // bracket
                Vector3 lb = w + n * 0.3f - up * 0.24f;
                mb.Cylinder(VKit.Iron, lb - up * 0.03f, lb, 0.06f, 0.075f, 6, true);
                mb.Cylinder(VKit.WinA, lb, lb + up * 0.2f, 0.07f, 0.07f, 6, false);                                // lantern glass
                mb.Cylinder(VKit.Iron, lb + up * 0.2f, lb + up * 0.3f, 0.09f, 0.02f, 6, true);
                mb.Cylinder(VKit.Iron, lb + up * 0.3f, w + n * 0.3f - up * 0.02f, 0.02f, 0.02f, 4, false);           // hanger
            }
        }

        /// <summary>A small table laid with a cloth, plates and a jug, and chairs around it. One chair may lie on its side.</summary>
        public static void TableSet(MeshBuilder mb, Vector3 p, float yaw, System.Random rng, int chairs, bool oneTipped)
        {
            Vector3 up = Vector3.up;
            mb.Cylinder(VKit.Iron, p, p + up * 0.03f, 0.2f, 0.17f, 7, true);
            mb.Cylinder(VKit.Iron, p, p + up * 0.5f, 0.03f, 0.03f, 6, false);
            VKit.BoxOn(mb, VKit.Canvas, p + up * 0.5f, 0.82f, 0.255f, 0.82f, yaw);                    // cloth hanging over the top
            float ty = 0.755f;
            Vector3 f = VKit.Dir(yaw), r = Vector3.Cross(up, f);
            for (int i = 0; i < 2; i++)
            {
                Vector3 q = p + up * ty + f * (i == 0 ? 0.2f : -0.2f) + r * VKit.Rnd(rng, -0.08f, 0.08f);
                mb.Cylinder(VKit.Wh, q, q + up * 0.015f, 0.11f, 0.12f, 7, true);
            }
            Vector3 j = p + up * ty + r * VKit.Rnd(rng, 0.15f, 0.26f) * VKit.Sgn(rng);
            mb.Cylinder(VKit.Terra, j, j + up * 0.2f, 0.055f, 0.035f, 6, true);
            Material cm = VKit.Chance(rng, 0.3f) ? VKit.Blue : VKit.Wood;
            for (int i = 0; i < chairs; i++)
            {
                float a = yaw + (i == 0 ? 0f : i == 1 ? 180f : i == 2 ? 90f : 270f) + VKit.Rnd(rng, -14f, 14f);
                bool tip = oneTipped && i == chairs - 1;
                float dist = tip ? VKit.Rnd(rng, 1.0f, 1.3f) : VKit.Chance(rng, 0.3f) ? VKit.Rnd(rng, 0.85f, 1.05f) : 0.64f;
                Chair(mb, p + VKit.Dir(a) * dist, a + 180f + VKit.Rnd(rng, -25f, 25f) * (dist > 0.7f ? 1f : 0.2f), cm, tip);
            }
        }

        /// <summary>Wooden taverna chair standing on p and facing yaw; tipped = lying on its side.</summary>
        public static void Chair(MeshBuilder mb, Vector3 p, float yaw, Material m, bool tipped)
        {
            Vector3 r, u, f;
            VKit.Axes(yaw, 0f, tipped ? 90f : 0f, out r, out u, out f);
            Vector3 o = tipped ? p + Vector3.up * 0.21f - u * 0.22f : p;
            const float seat = 0.45f, hs = 0.2f, leg = 0.019f;
            VKit.Box(mb, m, o + u * seat, r * 0.21f, u * 0.02f, f * 0.21f);
            for (int i = 0; i < 4; i++)
            {
                float sx = (i & 1) == 0 ? -hs + leg : hs - leg, sz = i < 2 ? hs - leg : -hs + leg;
                float top = i < 2 ? seat - 0.02f : 0.9f;                  // rear legs carry the back
                VKit.Box(mb, m, o + r * sx + f * sz + u * (top * 0.5f), r * leg, u * (top * 0.5f), f * leg, i < 2 ? 4 | 8 : 8);
            }
            VKit.Box(mb, m, o + f * (-hs + leg) + u * 0.84f, r * (hs - leg * 2f), u * 0.035f, f * 0.014f, 1 | 2);
            VKit.Box(mb, m, o + f * (-hs + leg) + u * 0.64f, r * (hs - leg * 2f), u * 0.025f, f * 0.014f, 1 | 2);
        }

        static void Nets(MeshBuilder mb, Vector3 p, float yaw, System.Random rng)
        {
            float rx = VKit.Rnd(rng, 0.8f, 1.35f), rz = VKit.Rnd(rng, 0.6f, 1.0f), h = VKit.Rnd(rng, 0.22f, 0.42f);
            VKit.Mound(mb, VKit.Net, p, rx, rz, h, yaw, rng, 9, 0.02f);
            int floats = 3 + rng.Next(4);
            for (int i = 0; i < floats; i++)
            {
                float a = VKit.Rnd(rng, 0f, 6.28f), k = VKit.Rnd(rng, 0.25f, 0.8f);
                Vector3 q = p + VKit.Dir(yaw) * (Mathf.Sin(a) * rz * k) + VKit.Dir(yaw + 90f) * (Mathf.Cos(a) * rx * k) + Vector3.up * (h * (1.05f - k * 0.75f) + 0.03f);
                VKit.BoxTilt(mb, i % 3 == 0 ? VKit.BoatYellow : VKit.Terra, q, 0.13f, 0.09f, 0.13f, VKit.Rnd(rng, 0f, 90f), VKit.Rnd(rng, -25f, 25f), VKit.Rnd(rng, -25f, 25f));
            }
            // a line trailing from the heap
            pts.Clear();
            Vector3 d = VKit.Dir(yaw + VKit.Rnd(rng, 60f, 120f));
            Vector3 side = Vector3.Cross(Vector3.up, d);
            for (int i = 0; i < 6; i++)
            {
                Vector3 q = p + d * (rx * 0.8f + i * 0.42f) + side * (Mathf.Sin(i * 1.7f) * 0.22f) + Vector3.up * 0.03f;
                if (VKit.PathGap(q.x, q.z) < 0.55f || q.x < -19.2f) break;
                pts.Add(q);
            }
            if (pts.Count >= 2) mb.Tube(VKit.Rope, pts, 0.025f, 5);
        }

        /// <summary>Stack of fish crates; returns the height of the stack.</summary>
        static float Crates(MeshBuilder mb, Vector3 p, float yaw, System.Random rng, int n)
        {
            const float w = 0.62f, hgt = 0.34f, d = 0.42f;
            int perRow = n >= 5 ? 3 : 2, placed = 0, level = 0;
            Vector3 along = VKit.Dir(yaw + 90f);
            while (placed < n)
            {
                int cnt = Mathf.Min(perRow - (level > 1 ? 1 : 0), n - placed);
                if (cnt < 1) cnt = 1;
                for (int i = 0; i < cnt; i++)
                {
                    Vector3 q = p + along * ((i - (cnt - 1) * 0.5f) * (w + 0.03f) + VKit.Rnd(rng, -0.04f, 0.04f)) + Vector3.up * (level * hgt);
                    VKit.BoxOn(mb, (placed + level) % 3 == 0 ? VKit.WoodDk : VKit.Wood, q, w, hgt - 0.012f, d, yaw + VKit.Rnd(rng, -5f, 5f));
                    placed++;
                }
                level++;
            }
            return level * hgt;
        }

        static void Coil(MeshBuilder mb, Vector3 p, System.Random rng)
        {
            float R = VKit.Rnd(rng, 0.3f, 0.42f);
            int rings = 2 + rng.Next(3);
            for (int k = 0; k < rings; k++)
                VKit.ArcTube(mb, VKit.Rope, p + Vector3.up * (0.03f + 0.056f * k), Vector3.right * (R - 0.015f * k), Vector3.forward * (R - 0.015f * k), k * 37f, k * 37f + 360f, 10, 0.03f, 5);
            // the loose end
            pts.Clear();
            float a = VKit.Rnd(rng, 0f, 6.28f);
            Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            pts.Add(p + d * R + Vector3.up * (0.03f + 0.056f * (rings - 1)));
            pts.Add(p + d * (R + 0.3f) + Vector3.up * 0.04f);
            pts.Add(p + d * (R + 0.75f) + Vector3.Cross(Vector3.up, d) * 0.2f + Vector3.up * 0.03f);
            mb.Tube(VKit.Rope, pts, 0.03f, 5);
        }

        static void LobsterPot(MeshBuilder mb, Vector3 p, float yaw, bool hoops = true)
        {
            Vector3 f = VKit.Dir(yaw), r = Vector3.Cross(Vector3.up, f), up = Vector3.up;
            VKit.Box(mb, VKit.Wood, p + up * 0.02f, r * 0.24f, up * 0.02f, f * 0.36f, 8);
            VWalls.Vault(mb, VKit.Net, p + up * 0.04f, f, r, 0.34f, 0.215f, 0.4f, 6);
            if (!hoops) return;
            for (int i = -1; i <= 1; i++)
                VKit.ArcTube(mb, VKit.WoodDk, p + up * 0.04f + f * (i * 0.34f), r * 0.225f, up * 0.41f, 0f, 180f, 6, 0.02f, 4);
        }

        static void Barrel(MeshBuilder mb, Vector3 p, System.Random rng)
        {
            Vector3 up = Vector3.up;
            float r0 = VKit.Rnd(rng, 0.25f, 0.28f), r1 = r0 + 0.055f;
            mb.Cylinder(VKit.Wood, p, p + up * 0.45f, r0, r1, 9, false);
            mb.Cylinder(VKit.Wood, p + up * 0.45f, p + up * 0.9f, r1, r0, 9, false);
            mb.Cylinder(VKit.WoodDk, p + up * 0.87f, p + up * 0.871f, r0 - 0.02f, 0.001f, 9, false);   // lid, a little below the rim
            mb.Cylinder(VKit.Iron, p + up * 0.16f, p + up * 0.21f, r0 + 0.028f, r0 + 0.034f, 9, false);
            mb.Cylinder(VKit.Iron, p + up * 0.69f, p + up * 0.74f, r0 + 0.034f, r0 + 0.028f, 9, false);
        }
    }
}
