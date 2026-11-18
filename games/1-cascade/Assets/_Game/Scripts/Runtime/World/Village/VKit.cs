using System.Collections.Generic;
using UnityEngine;

namespace CASCADE
{
    /// <summary>Materials and low-level geometry helpers shared by the village generators. World space, build time only.</summary>
    internal static class VKit
    {
        public static Material Wh, WhWarm, WhOld, Wet, Ruin, Mud, Blue, Rock, Stone, Wood, WoodDk, Iron, Rope, Net, Canvas, Terra, Bronze, Plant, Boug;
        public static Material WinA, WinB, WinC, WinDark, BoatRed, BoatYellow, BoatWhite, BoatBlue;
        /// <summary>Dressing: raised plaster (surrounds, copings, cornices), painted dome or barrel roof, painted joinery, pumice masonry.</summary>
        public static Material Trim, DomeBlue, ShutGreen, ShutRed, Pumice;
        public static readonly Vector3 Up = Vector3.up;
        static readonly List<Vector3> tmpPts = new List<Vector3>(64);
        static readonly List<Vector3> mitre = new List<Vector3>(16), edgeN = new List<Vector3>(16);

        public static void Init()
        {
            GameAssets a = GameAssets.I;
            Wh = a.Mat("M_Whitewash"); WhWarm = a.Mat("M_WhitewashWarm"); WhOld = a.Mat("M_WhitewashOld"); Wet = a.Mat("M_WhitewashWet");
            Ruin = a.Mat("M_Ruin"); Mud = a.Mat("M_Mud"); Blue = a.Mat("M_DoorBlue"); Rock = a.Mat("M_RockWall"); Stone = a.Mat("M_Stone");
            Wood = a.Mat("M_Wood"); WoodDk = a.Mat("M_WoodDark"); Iron = a.Mat("M_Iron"); Rope = a.Mat("M_Rope"); Net = a.Mat("M_Net");
            Canvas = a.Mat("M_Canvas"); Terra = a.Mat("M_Terracotta"); Bronze = a.Mat("M_Bronze"); Plant = a.Mat("M_Plant"); Boug = a.Mat("M_Bougainvillea");
            WinA = a.Mat("M_WindowA"); WinB = a.Mat("M_WindowB"); WinC = a.Mat("M_WindowC"); WinDark = a.Mat("M_WindowDark");
            BoatRed = a.Mat("M_BoatRed"); BoatYellow = a.Mat("M_BoatYellow"); BoatWhite = a.Mat("M_BoatWhite"); BoatBlue = a.Mat("M_BoatBlue");
            Trim = a.Mat("M_PlasterTrim"); DomeBlue = a.Mat("M_DomeBlue"); ShutGreen = a.Mat("M_ShutterGreen"); ShutRed = a.Mat("M_ShutterRed"); Pumice = a.Mat("M_Pumice");
            LeafInit(true);
            VWalls.ResetStyle();
        }

        /// <summary>A generated material by name, or null while the asset build has not made it (no error: callers fall back).</summary>
        public static Material Find(string name)
        {
            GameAssets a = GameAssets.I;
            if (a == null || a.materials == null) return null;
            for (int i = 0; i < a.materials.Length; i++)
                if (a.materials[i] != null && a.materials[i].name == name) return a.materials[i];
            return null;
        }

        // ---------- leaf cards: alpha-tested foliage near the path (the far village and Lite houses keep the solid lumps) ----------
        public const float CardRange = 16f;        // plants get cards at most this far from the path edge, metres
        public const float CoreShrink = 0.8f;      // the solid lobes under the cards shrink to this, so the gaps show depth
        public const float CardArea = 0.06f;       // lobe surface (m^2) per card: the main knob of the card vertex count
        const float CardTilt = 50f;                // cards lean up to this far off the lobe surface, degrees
        const float CardLift = 0.12f;              // canopy normals lean this much towards the sky (light from above)
        const float CellInset = 0.008f;            // UV inset from the edges of an atlas cell (no bleeding from its neighbour)
        public const int CellShrub = 0, CellBoug = 1, CellGeranium = 2, CellGrass = 3;          // T_LeafAtlas cells
        public const int MixNone = 0, MixPot = 1, MixBoug = 2, MixWild = 3;                     // which cells a plant draws from
        static Material leafCard, bougCard, plantCore, bougCore, plantSolid, bougSolid;
        public static Material LeafCard { get { LeafInit(false); return leafCard; } }
        public static Material PlantCore { get { LeafInit(false); return plantCore; } }
        static bool leafReady;
        /// <summary>Vertices of leaf cards emitted in this world build (load statistics; ArchCtx clears it).</summary>
        public static int CardVerts;
        static readonly List<Vector3> cv = new List<Vector3>(96), cn = new List<Vector3>(96);
        static readonly List<Vector2> cu = new List<Vector2>(96);
        static readonly List<int> ct = new List<int>(144);

        /// <summary>Looks the card materials up (again when reload: every world build, as the assets may have been regenerated).</summary>
        public static void LeafInit(bool reload)
        {
            if (leafReady && !reload) return;
            leafReady = true;
            leafCard = Find("M_LeafCard"); bougCard = Find("M_BougCard"); plantCore = Find("M_PlantCore"); bougCore = Find("M_BougCore");
            plantSolid = Find("M_Plant"); bougSolid = Find("M_Bougainvillea");
        }

        /// <summary>The card mix of a blob drawn in m at c: none far from the path, on a Lite house, or before the assets exist.</summary>
        public static int LeafMix(Material m, Vector3 c)
        {
            LeafInit(false);
            if (leafCard == null || bougCard == null || plantCore == null || bougCore == null || VWalls.Lite) return MixNone;
            int mix = m == plantSolid ? MixPot : m == bougSolid ? MixBoug : MixNone;
            if (mix == MixNone || PathGap(c.x, c.z) > CardRange) return MixNone;
            return mix;
        }

        /// <summary>Material of the solid lobes under the cards of a mix.</summary>
        public static Material CoreOf(int mix) { return mix == MixBoug ? bougCore : plantCore; }

        static int PickCell(int mix, System.Random r)
        {
            double v = r.NextDouble();
            if (mix == MixBoug) return v < 0.75 ? CellBoug : CellShrub;
            if (mix == MixPot) return v < 0.1 ? CellGeranium : CellShrub;
            return CellShrub;
        }

        /// <summary>
        /// A shell of leaf cards round one lobe (centre c, half-axes hx, hy up, hz): N = 4 pi r^2 / CardArea quads (5 to maxN)
        /// on the lobe above dyMin (-0.6 = its upper 80 %: pots and shrubs are seen from above; -1 = all round, for tree crowns
        /// seen from below), leaning up to CardTilt off its surface, each with a random spin. Normals point out of the whole
        /// plant's ellipsoid (centre pc, half-axes ax, ay, az), so all cards of a plant shade as one rounded canopy.
        /// </summary>
        public static void LeafCards(MeshBuilder mb, int mix, Vector3 c, Vector3 hx, Vector3 hy, Vector3 hz, Vector3 pc, Vector3 ax, Vector3 ay, Vector3 az, System.Random r,
                                     float sizeK = 1f, int maxN = 14, float dyMin = -0.6f)
        {
            float lx = Mathf.Max(hx.magnitude, 1e-4f), ly = Mathf.Max(hy.magnitude, 1e-4f), lz = Mathf.Max(hz.magnitude, 1e-4f), rm = (lx + ly + lz) / 3f;
            if (mix == MixNone || rm < 0.03f) return;
            Vector3 nx = hx / lx, ny = hy / ly, nz = hz / lz;
            int n = Mathf.Clamp(Mathf.RoundToInt(4f * Mathf.PI * rm * rm / CardArea), 5, Mathf.Max(5, maxN));
            float hs = Mathf.Clamp(0.55f * rm, 0.08f, 0.32f) * sizeK;
            dyMin = Mathf.Clamp(dyMin, -1f, 0.9f);
            cv.Clear(); cn.Clear(); cu.Clear(); ct.Clear();
            for (int i = 0; i < n; i++)
            {
                // a direction on the unit sphere above dyMin (uniform in y = uniform in area), mapped onto the lobe
                float dy = Rnd(r, dyMin, 1f), phi = Rnd(r, 0f, Mathf.PI * 2f), ring = Mathf.Sqrt(Mathf.Max(0f, 1f - dy * dy));
                float dx = Mathf.Cos(phi) * ring, dz = Mathf.Sin(phi) * ring;
                Vector3 p = c + (hx * dx + hy * dy + hz * dz) * Rnd(r, 0.85f, 1.05f);
                Vector3 sn = (nx * (dx / lx) + ny * (dy / ly) + nz * (dz / lz)).normalized;
                Card(p, sn, hs, PickCell(mix, r), r, pc, ax, ay, az);
            }
            EmitCards(mb, mix == MixBoug ? bougCard : leafCard);
        }

        /// <summary>One card of half size hs at p, its plane tilted off the surface normal sn by up to CardTilt, with the canopy
        /// normals of the ellipsoid (pc, ax, ay, az); queued for EmitCards.</summary>
        internal static void Card(Vector3 p, Vector3 sn, float hs, int cell, System.Random r, Vector3 pc, Vector3 ax, Vector3 ay, Vector3 az)
        {
            Vector3 t0 = Vector3.Cross(sn, Mathf.Abs(sn.y) < 0.9f ? Vector3.up : Vector3.right).normalized, t1 = Vector3.Cross(sn, t0);
            float spin = Rnd(r, 0f, Mathf.PI * 2f);
            Vector3 n = Quaternion.AngleAxis(Rnd(r, -CardTilt, CardTilt), t0 * Mathf.Cos(spin) + t1 * Mathf.Sin(spin)) * sn;
            Vector3 e0 = Vector3.Cross(n, Mathf.Abs(n.y) < 0.9f ? Vector3.up : Vector3.right).normalized, e1 = Vector3.Cross(n, e0);
            float roll = Rnd(r, 0f, Mathf.PI * 2f);
            Vector3 u = (e0 * Mathf.Cos(roll) + e1 * Mathf.Sin(roll)) * hs, v = Vector3.Cross(n, u);
            Vector3 a = p - u - v, b = p + u - v, cc = p + u + v, d = p - u + v;
            QueueCard(a, b, cc, d, cell, r.NextDouble() < 0.5, CanopyNormal(a, pc, ax, ay, az), CanopyNormal(b, pc, ax, ay, az), CanopyNormal(cc, pc, ax, ay, az), CanopyNormal(d, pc, ax, ay, az));
        }

        /// <summary>Queues a card quad a-b-c-d with its corner normals; a-b is the bottom edge of the atlas cell (the foot of a
        /// grass tuft), mirror flips the cell left to right.</summary>
        internal static void QueueCard(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int cell, bool mirror, Vector3 na, Vector3 nb, Vector3 nc, Vector3 nd)
        {
            int i0 = cv.Count;
            Vector2 o = new Vector2((cell & 1) * 0.5f, (cell >> 1) * 0.5f);
            float lo = CellInset, hi = 0.5f - CellInset, ua = mirror ? hi : lo, ub = mirror ? lo : hi;
            cv.Add(a); cv.Add(b); cv.Add(c); cv.Add(d);
            cu.Add(o + new Vector2(ua, lo)); cu.Add(o + new Vector2(ub, lo)); cu.Add(o + new Vector2(ub, hi)); cu.Add(o + new Vector2(ua, hi));
            cn.Add(na); cn.Add(nb); cn.Add(nc); cn.Add(nd);
            ct.Add(i0); ct.Add(i0 + 2); ct.Add(i0 + 1); ct.Add(i0); ct.Add(i0 + 3); ct.Add(i0 + 2);
        }

        /// <summary>Bakes the queued cards into mb (the material is double sided, so the winding does not matter).</summary>
        internal static void EmitCards(MeshBuilder mb, Material m)
        {
            if (cv.Count > 0 && m != null) { mb.Raw(m, cv, cn, cu, ct); CardVerts += cv.Count; }
            cv.Clear(); cn.Clear(); cu.Clear(); ct.Clear();
        }

        /// <summary>Outward normal of the ellipsoid (centre pc, half-axes ax, ay, az) through p, leaning a little towards the sky.</summary>
        public static Vector3 CanopyNormal(Vector3 p, Vector3 pc, Vector3 ax, Vector3 ay, Vector3 az)
        {
            Vector3 o = p - pc;
            float a2 = Mathf.Max(ax.sqrMagnitude, 1e-4f), b2 = Mathf.Max(ay.sqrMagnitude, 1e-4f), c2 = Mathf.Max(az.sqrMagnitude, 1e-4f);
            Vector3 g = ax * (Vector3.Dot(o, ax) / (a2 * a2)) + ay * (Vector3.Dot(o, ay) / (b2 * b2)) + az * (Vector3.Dot(o, az) / (c2 * c2));
            if (g.sqrMagnitude < 1e-12f) return Vector3.up;
            return (g.normalized + Vector3.up * CardLift).normalized;
        }

        /// <summary>Deterministic value in [0,1) from a seed and a salt (decisions that must agree between generators).</summary>
        public static float Hash(int seed, int salt) { return MathX.Hash(seed, salt); }

        // ---------- random ----------
        public static float Rnd(System.Random r, float a, float b) { return a + (b - a) * (float)r.NextDouble(); }
        public static bool Chance(System.Random r, float p) { return r.NextDouble() < p; }
        public static float Sgn(System.Random r) { return r.NextDouble() < 0.5 ? -1f : 1f; }

        /// <summary>Window glass: groups A/B/C light up at different moments, Dark never (40/30/20/10 %).</summary>
        public static Material Glass(System.Random r)
        {
            double v = r.NextDouble();
            return v < 0.4 ? WinA : v < 0.7 ? WinB : v < 0.9 ? WinC : WinDark;
        }

        // ---------- directions ----------
        /// <summary>Horizontal unit vector for a yaw in degrees (0 = +z north, 90 = +x east).</summary>
        public static Vector3 Dir(float yawDeg) { float y = yawDeg * Mathf.Deg2Rad; return new Vector3(Mathf.Sin(y), 0f, Mathf.Cos(y)); }
        /// <summary>Along-wall axis u of a wall with outward horizontal normal n (u x up = n).</summary>
        public static Vector3 WallU(Vector3 n) { return Vector3.Cross(Vector3.up, n); }

        /// <summary>Orthonormal axes of a body with yaw, pitch (nose up) and roll, degrees.</summary>
        public static void Axes(float yaw, float pitch, float roll, out Vector3 r, out Vector3 u, out Vector3 f)
        {
            Vector3 fl = Dir(yaw);
            float p = pitch * Mathf.Deg2Rad, q = roll * Mathf.Deg2Rad;
            f = fl * Mathf.Cos(p) + Vector3.up * Mathf.Sin(p);
            Vector3 r0 = Vector3.Cross(Vector3.up, fl);
            Vector3 u0 = Vector3.Cross(f, r0);
            r = r0 * Mathf.Cos(q) + u0 * Mathf.Sin(q);
            u = u0 * Mathf.Cos(q) - r0 * Mathf.Sin(q);
        }

        // ---------- ground and path ----------
        public static float Ground(float x, float z) { return WorldGen.Height(x, z); }

        /// <summary>Horizontal gap to the edge of the walkable path (distance to the centre line minus the half width). 99 when far.</summary>
        public static float PathGap(float x, float z)
        {
            float s, d;
            if (!PathDef.Nearest(x, z, out s, out d)) return 99f;
            return d - PathDef.HalfWidth(s);
        }

        // ---------- primitives (winding is chosen from the wanted facing, so callers cannot get it wrong) ----------
        public static void Q(MeshBuilder mb, Material m, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 facing)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, d - a), facing) >= 0f) mb.Quad(m, a, b, c, d); else mb.Quad(m, a, d, c, b);
        }

        public static void T(MeshBuilder mb, Material m, Vector3 a, Vector3 b, Vector3 c, Vector3 facing)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), facing) >= 0f) mb.Tri(m, a, b, c); else mb.Tri(m, a, c, b);
        }

        /// <summary>Both sides of a thin sheet (canvas, sails).</summary>
        public static void Q2(MeshBuilder mb, Material m, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            mb.Quad(m, a, b, c, d); mb.Quad(m, a, d, c, b);
        }

        /// <summary>Box from a centre and three half-axis vectors. skip bits: 1 +x, 2 -x, 4 +y, 8 -y, 16 +z, 32 -z.</summary>
        public static void Box(MeshBuilder mb, Material m, Vector3 c, Vector3 hx, Vector3 hy, Vector3 hz, int skip = 0)
        {
            if ((skip & 1) == 0) Q(mb, m, c + hx - hy - hz, c + hx + hy - hz, c + hx + hy + hz, c + hx - hy + hz, hx);
            if ((skip & 2) == 0) Q(mb, m, c - hx - hy - hz, c - hx + hy - hz, c - hx + hy + hz, c - hx - hy + hz, -hx);
            if ((skip & 4) == 0) Q(mb, m, c + hy - hx - hz, c + hy + hx - hz, c + hy + hx + hz, c + hy - hx + hz, hy);
            if ((skip & 8) == 0) Q(mb, m, c - hy - hx - hz, c - hy + hx - hz, c - hy + hx + hz, c - hy - hx + hz, -hy);
            if ((skip & 16) == 0) Q(mb, m, c + hz - hx - hy, c + hz + hx - hy, c + hz + hx + hy, c + hz - hx + hy, hz);
            if ((skip & 32) == 0) Q(mb, m, c - hz - hx - hy, c - hz + hx - hy, c - hz + hx + hy, c - hz - hx + hy, -hz);
        }

        /// <summary>Upright box standing on basePos (centre of its bottom face), rotated by yaw. The bottom face is never built.</summary>
        public static void BoxOn(MeshBuilder mb, Material m, Vector3 basePos, float sx, float sy, float sz, float yawDeg = 0f, int skip = 0)
        {
            Vector3 f = Dir(yawDeg), r = Vector3.Cross(Vector3.up, f);
            Box(mb, m, basePos + Vector3.up * (sy * 0.5f), r * (sx * 0.5f), Vector3.up * (sy * 0.5f), f * (sz * 0.5f), skip | 8);
        }

        /// <summary>Box with a free orientation (debris, beams). size along right/up/forward of the given yaw, pitch, roll.</summary>
        public static void BoxTilt(MeshBuilder mb, Material m, Vector3 c, float sx, float sy, float sz, float yaw, float pitch, float roll, int skip = 0)
        {
            Vector3 r, u, f;
            Axes(yaw, pitch, roll, out r, out u, out f);
            Box(mb, m, c, r * (sx * 0.5f), u * (sy * 0.5f), f * (sz * 0.5f), skip);
        }

        // Bougainvillea beyond CardRange has no cards: drawn all in M_Bougainvillea it was a saturated candy-floss lump with no
        // leaf (20, 45). There its heart and the lobes on its back (towards the wall: the callers give hz along the wall normal)
        // are its green leaf, and the heart sits a little back and down, so the front lobes read as blossom over dark leaf and
        // the green shows in the holes and at the edges. A material swap only: the shapes and every stream are unchanged.
        const float FarBougBack = 0.2f, FarBougDrop = 0.12f;   // heart offset, in units of hz (back) and hy (down)
        const float FarBougLeafSin = -0.55f;                    // side lobes with sin(angle) below this (about 30 %) are leaf

        /// <summary>
        /// Foliage as a cluster of lumpy lobes (a shrub silhouette, not one smooth gem). Its own seeded random, so the
        /// caller's stream is not disturbed. Near the path (LeafMix) every lobe also gets a shell of leaf cards and the
        /// solid lobes shrink and darken under them; the cards draw from a third stream, so the lobes keep their shapes.
        /// Far bougainvillea mixes green leaf lobes into the blossom (FarBoug*).
        /// </summary>
        public static void Blob(MeshBuilder mb, Material m, Vector3 c, Vector3 hx, Vector3 hy, Vector3 hz, System.Random rng, float noise = 0.18f, int seg = 6, int rings = 4)
        {
            unchecked
            {
                int seed = (int)(c.x * 73.13f) * 92821 ^ (int)(c.y * 41.7f) * 68917 ^ (int)(c.z * 59.3f) * 12289;
                var lr = new System.Random(seed);
                int mix = LeafMix(m, c);
                Material solid = mix == MixNone ? m : CoreOf(mix);
                float k0 = mix == MixNone ? 1f : CoreShrink;
                System.Random cr = mix == MixNone ? null : new System.Random(seed ^ 0x5EED1EAF);
                bool farBoug = mix == MixNone && m != null && Plant != null && (m == Boug || m == bougSolid);
                Material leaf = farBoug ? Plant : solid;
                Vector3 hc = farBoug ? c - hz * FarBougBack - hy * FarBougDrop : c;
                BlobOne(mb, leaf, hc, hx * (0.72f * k0), hy * (0.72f * k0), hz * (0.72f * k0), lr, Mathf.Max(noise, 0.28f), Mathf.Max(seg, 8), Mathf.Max(rings, 5));
                if (cr != null) LeafCards(mb, mix, c, hx * 0.72f, hy * 0.72f, hz * 0.72f, c, hx, hy, hz, cr);
                int n = 4 + lr.Next(4);
                for (int i = 0; i < n; i++)
                {
                    float a = (float)lr.NextDouble() * Mathf.PI * 2f;
                    float ry = -0.25f + (float)lr.NextDouble() * 0.95f;
                    Vector3 off = hx * (Mathf.Cos(a) * 0.48f) + hz * (Mathf.Sin(a) * 0.48f) + hy * (ry * 0.5f);
                    float k = 0.36f + (float)lr.NextDouble() * 0.22f;
                    BlobOne(mb, Mathf.Sin(a) < FarBougLeafSin ? leaf : solid, c + off, hx * (k * k0), hy * (k * k0), hz * (k * k0), lr, 0.3f, 7, 4);
                    if (cr != null) LeafCards(mb, mix, c + off, hx * k, hy * k, hz * k, c, hx, hy, hz, cr);
                }
            }
        }

        /// <summary>One lumpy ellipsoid lobe with smooth normals (noise = relative radius jitter per vertex, drawn from rng).</summary>
        internal static void BlobOne(MeshBuilder mb, Material m, Vector3 c, Vector3 hx, Vector3 hy, Vector3 hz, System.Random rng, float noise = 0.18f, int seg = 6, int rings = 4)
        {
            var v = new List<Vector3>((seg + 1) * (rings + 1)); var n = new List<Vector3>(v.Capacity); var t = new List<int>(seg * rings * 6);
            Vector3 nx = hx.normalized, ny = hy.normalized, nz = hz.normalized;
            float lx = hx.magnitude, ly = hy.magnitude, lz = hz.magnitude;
            for (int r = 0; r <= rings; r++)
            {
                float el = -Mathf.PI * 0.5f + Mathf.PI * r / rings;
                float cy = Mathf.Sin(el), cr = Mathf.Cos(el);
                for (int s = 0; s <= seg; s++)
                {
                    float az = s * Mathf.PI * 2f / seg;
                    float k = (r == 0 || r == rings || s == seg) ? 1f : 1f + Rnd(rng, -noise, noise);
                    float dx = Mathf.Cos(az) * cr, dz = Mathf.Sin(az) * cr;
                    v.Add(c + (hx * dx + hy * cy + hz * dz) * k);
                    n.Add((nx * (dx / lx) + ny * (cy / ly) + nz * (dz / lz)).normalized);
                }
            }
            // the seam column repeats column 0 exactly
            for (int r = 0; r <= rings; r++) v[r * (seg + 1) + seg] = v[r * (seg + 1)];
            bool flip = Vector3.Dot(Vector3.Cross(hz, hx), hy) < 0f;
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < seg; s++)
                {
                    int p = r * (seg + 1) + s, q = p + seg + 1;
                    if (flip) { t.Add(p); t.Add(q + 1); t.Add(q); t.Add(p); t.Add(p + 1); t.Add(q + 1); }
                    else { t.Add(p); t.Add(q); t.Add(q + 1); t.Add(p); t.Add(q + 1); t.Add(p + 1); }
                }
            mb.Raw(m, v, n, null, t);
        }

        /// <summary>Faceted low heap on the ground (nets, rubble, mud): a fan with a raised, off-centre top and an irregular rim.</summary>
        public static void Mound(MeshBuilder mb, Material m, Vector3 basePos, float rx, float rz, float h, float yawDeg, System.Random rng, int seg = 9, float rimDrop = 0.05f)
        {
            Vector3 f = Dir(yawDeg), r = Vector3.Cross(Vector3.up, f);
            Vector3 top = basePos + r * Rnd(rng, -0.25f, 0.25f) * rx + f * Rnd(rng, -0.25f, 0.25f) * rz + Vector3.up * h;
            tmpPts.Clear();
            for (int i = 0; i < seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg, k = Rnd(rng, 0.72f, 1.12f);
                tmpPts.Add(basePos + r * (Mathf.Cos(a) * rx * k) + f * (Mathf.Sin(a) * rz * k) - Vector3.up * rimDrop);
            }
            for (int i = 0; i < seg; i++)
            {
                Vector3 a0 = tmpPts[i], a1 = tmpPts[(i + 1) % seg];
                // shoulder ring half way up makes the heap rounded instead of a cone
                Vector3 s0 = Vector3.Lerp(a0, top, 0.55f) + Vector3.up * (h * 0.22f), s1 = Vector3.Lerp(a1, top, 0.55f) + Vector3.up * (h * 0.22f);
                Vector3 outw = (a0 + a1) * 0.5f - basePos + Vector3.up * (rx + rz) * 0.5f;
                Q(mb, m, a0, a1, s1, s0, outw);
                T(mb, m, s0, s1, top, Vector3.up);
            }
        }

        /// <summary>Tube along a circular arc (hoops, rope coils). Radius of the tube at least 0.02 m.</summary>
        public static void ArcTube(MeshBuilder mb, Material m, Vector3 c, Vector3 ax, Vector3 ay, float a0Deg, float a1Deg, int seg, float tubeR, int sides = 5)
        {
            tmpPts.Clear();
            for (int i = 0; i <= seg; i++)
            {
                float a = Mathf.Lerp(a0Deg, a1Deg, i / (float)seg) * Mathf.Deg2Rad;
                tmpPts.Add(c + ax * Mathf.Cos(a) + ay * Mathf.Sin(a));
            }
            mb.Tube(m, tmpPts, Mathf.Max(tubeR, 0.02f), sides);
        }

        /// <summary>Terracotta pot with a plant, standing on p.</summary>
        public static void Pot(MeshBuilder mb, Vector3 p, float scale, System.Random rng, bool flower = false)
        {
            float h = 0.34f * scale, r0 = 0.13f * scale, r1 = 0.19f * scale;
            mb.Cylinder(Terra, p, p + Vector3.up * h, r0, r1, 7, false);
            float pr = Rnd(rng, 0.2f, 0.3f) * scale, ph = Rnd(rng, 0.25f, 0.5f) * scale;
            Blob(mb, flower ? Boug : Plant, p + Vector3.up * (h + ph * 0.75f), Vector3.right * pr, Vector3.up * ph, Vector3.forward * pr, rng, 0.2f, 6, 3);
        }

        // ---------- swept profiles (copings, cornices, rims) ----------
        /// <summary>
        /// Hand-plastered coping over a wall of thickness t: x across the wall (0 = its centre line, + = outwards), y above the wall
        /// top; crown height h. A drip overhang each side whose underside closes against the wall faces, so no sky shows from below.
        /// full = a rounder crown (two facets per shoulder). Symmetric with an even point count (SweepRun closes the ends with quads).
        /// </summary>
        public static void CopeProfile(List<Vector2> p, float t, float h, bool full)
        {
            p.Clear();
            float o = t * 0.5f, ov = 0.03f, d = 0.05f;
            p.Add(new Vector2(o, -d));
            p.Add(new Vector2(o + ov, -d));
            p.Add(new Vector2(o + ov, h * (full ? 0.35f : 0.4f)));
            if (full) p.Add(new Vector2(o + ov - 0.012f, h * 0.76f));
            p.Add(new Vector2(o - 0.015f, h));
            p.Add(new Vector2(-(o - 0.015f), h));
            if (full) p.Add(new Vector2(-(o + ov - 0.012f), h * 0.76f));
            p.Add(new Vector2(-(o + ov), h * (full ? 0.35f : 0.4f)));
            p.Add(new Vector2(-(o + ov), -d));
            p.Add(new Vector2(-o, -d));
        }

        /// <summary>
        /// Sweeps a profile (x outwards from the loop, y up) along a closed convex loop in plan; the corners are mitred and each
        /// loop point's y is the base height there (a slightly uneven loop gives a hand-made line).
        /// </summary>
        public static void SweepLoop(MeshBuilder mb, Material m, IList<Vector3> loop, IList<Vector2> prof)
        {
            int n = loop.Count, np = prof.Count;
            Vector3 cen = Vector3.zero;
            for (int i = 0; i < n; i++) cen += loop[i];
            cen /= n;
            edgeN.Clear(); mitre.Clear();
            for (int i = 0; i < n; i++)
            {
                Vector3 d = loop[(i + 1) % n] - loop[i]; d.y = 0f; d.Normalize();
                Vector3 o = new Vector3(d.z, 0f, -d.x);
                Vector3 mid = (loop[i] + loop[(i + 1) % n]) * 0.5f - cen; mid.y = 0f;
                if (Vector3.Dot(o, mid) < 0f) o = -o;
                edgeN.Add(o);
            }
            for (int i = 0; i < n; i++)
            {
                Vector3 a = edgeN[(i + n - 1) % n], b = edgeN[i];
                mitre.Add((a + b) / Mathf.Max(1f + Vector3.Dot(a, b), 0.2f));
            }
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                for (int k = 0; k < np - 1; k++)
                {
                    Vector2 p = prof[k], q = prof[k + 1];
                    Vector3 facing = edgeN[i] * (q.y - p.y) - Up * (q.x - p.x);
                    Q(mb, m, loop[i] + mitre[i] * p.x + Up * p.y, loop[j] + mitre[j] * p.x + Up * p.y,
                             loop[j] + mitre[j] * q.x + Up * q.y, loop[i] + mitre[i] * q.x + Up * q.y, facing);
                }
            }
        }

        /// <summary>Sweeps a symmetric profile (see CopeProfile) along a straight run a->b, x of the profile along n; closes both ends.</summary>
        public static void SweepRun(MeshBuilder mb, Material m, Vector3 a, Vector3 b, Vector3 n, IList<Vector2> prof, bool caps = true)
        {
            int np = prof.Count;
            Vector3 dir = b - a; dir.y = 0f; dir.Normalize();
            for (int k = 0; k < np - 1; k++)
            {
                Vector2 p = prof[k], q = prof[k + 1];
                Vector3 facing = n * (q.y - p.y) - Up * (q.x - p.x);
                Q(mb, m, a + n * p.x + Up * p.y, b + n * p.x + Up * p.y, b + n * q.x + Up * q.y, a + n * q.x + Up * q.y, facing);
            }
            if (!caps) return;
            for (int e = 0; e < 2; e++)
            {
                Vector3 o = e == 0 ? a : b, f = e == 0 ? -dir : dir;
                for (int k = 1; k < np / 2 - 1; k++)
                {
                    Vector2 p0 = prof[k], p1 = prof[k + 1], p2 = prof[np - 2 - k], p3 = prof[np - 1 - k];
                    Q(mb, m, o + n * p0.x + Up * p0.y, o + n * p1.x + Up * p1.y, o + n * p2.x + Up * p2.y, o + n * p3.x + Up * p3.y, f);
                }
            }
        }

        /// <summary>Proud plaster strip on a wall face: from p0 along u for len, bottom at p0.y, height h, standing out by depth along n.</summary>
        public static void Strip(MeshBuilder mb, Material m, Vector3 p0, Vector3 u, Vector3 n, float len, float h, float depth = 0.03f)
        {
            if (len < 0.05f) return;
            Box(mb, m, p0 + u * (len * 0.5f) + Up * (h * 0.5f) + n * (depth * 0.5f), u * (len * 0.5f), Up * (h * 0.5f), n * (depth * 0.5f), 32);
        }

        /// <summary>A flower box: a terracotta trough with plants (or blossom) spilling over it. p = centre of its bottom, u along it, n out.</summary>
        public static void FlowerBox(MeshBuilder mb, Vector3 p, Vector3 u, Vector3 n, float len, System.Random rng)
        {
            Box(mb, Terra, p + Up * 0.09f, u * (len * 0.5f), Up * 0.09f, n * 0.1f, 32);
            int k = len > 0.8f ? 3 : 2;
            for (int i = 0; i < k; i++)
            {
                float t = (i + 0.5f) / k - 0.5f, r = Rnd(rng, 0.17f, 0.24f);
                Blob(mb, Chance(rng, 0.45f) ? Boug : Plant, p + u * (t * len) + Up * (0.2f + r * 0.4f) + n * 0.04f, u * r, Up * (r * 0.8f), n * (r * 0.75f), rng, 0.2f, 5, 3);
            }
        }

        /// <summary>Oriented rectangle overlap test in plan (separating axes). Rect: centre, unit axis a (b is perpendicular), half sizes.</summary>
        public static bool RectsOverlap(Vector2 c0, Vector2 a0, float ha0, float hb0, Vector2 c1, Vector2 a1, float ha1, float hb1)
        {
            Vector2 b0 = new Vector2(-a0.y, a0.x), b1 = new Vector2(-a1.y, a1.x), d = c1 - c0;
            return !(Sep(d, a0, ha0, a1, b1, ha1, hb1) || Sep(d, b0, hb0, a1, b1, ha1, hb1) || Sep(-d, a1, ha1, a0, b0, ha0, hb0) || Sep(-d, b1, hb1, a0, b0, ha0, hb0));
        }

        static bool Sep(Vector2 d, Vector2 axis, float own, Vector2 oa, Vector2 ob, float ha, float hb)
        {
            float proj = Mathf.Abs(Vector2.Dot(oa, axis)) * ha + Mathf.Abs(Vector2.Dot(ob, axis)) * hb;
            return Mathf.Abs(Vector2.Dot(d, axis)) > own + proj;
        }
    }
}
