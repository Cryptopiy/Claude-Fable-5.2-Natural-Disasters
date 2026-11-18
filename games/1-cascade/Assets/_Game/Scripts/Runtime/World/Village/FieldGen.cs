using UnityEngine;

namespace CASCADE
{
    /// <summary>Dry-stone field walls on the plateau and the headland's inland side, following the ground; broken where the wave
    /// passed. Plus contour terrace walls (pezoules) across the slope behind the harbour row, which the quay walk looks up at.</summary>
    internal static class FieldGen
    {
        public const float WallH = 0.9f, WallT = 0.55f, Seg = 2.6f;
        public const float MaxStep = 1.7f;          // a segment that would climb more than this is left out (ledges, cliffs)

        // ---- terraces (pezoules) on the toe of the cascade: frames 20 and 45 look up at a bare 10-35 m band of earth there ----
        // Altitudes of the contour walls. The ground behind the row rises about 1.1 m per metre, so 5.5 m apart in height is
        // about 5 m apart on the slope. The 12 m contour runs inside the harbour row (x about 5.6); 14.5 m runs at x about 8.3,
        // just behind the row's backs (x 5.6-7.2) and their 0.8 m margin: the first wall right above the roofline.
        public static readonly float[] TerraceAlts = { 14.5f, 20f, 25.5f, 31f, 36.5f };
        public const float TerraceTop = 0.9f;       // wall crown above the contour: about 1.25 m of stone face shows from below
        public const float TerraceFoot = 1.3f;      // footing below the contour, so the downhill face never floats on a 50 degree slope
        public const float TerraceT = 0.6f;
        public const float TerraceGap = 0.15f;      // share of the segments left out: fallen stretches, a terrace that ends
        public const float TerracePathGap = 8f;     // clear of the stair-road's retaining terraces (RetainReach 7 m) and landings
        public const float TerraceMaxDx = 3f;       // a contour jumping further than this across one step is a ledge or gully: gap

        public static void Build(MeshBuilder[] intact, MeshBuilder[] ruined, System.Random rng)
        {
            var hs = VillagePlan.Houses;
            // enclosures around the farms
            for (int i = 0; i < hs.Count; i++)
            {
                HouseDef h = hs[i];
                if (h.zone != 5) continue;
                float s0 = -VKit.Rnd(rng, 11f, 19f), s1 = VKit.Rnd(rng, 11f, 19f), f0 = -VKit.Rnd(rng, 8f, 14f), f1 = VKit.Rnd(rng, 9f, 17f);
                Vector3 a = h.At(s0, 0f, f0), b = h.At(s1, 0f, f0 + VKit.Rnd(rng, -3f, 3f)), c = h.At(s1 + VKit.Rnd(rng, -3f, 3f), 0f, f1), d = h.At(s0, 0f, f1 + VKit.Rnd(rng, -3f, 3f));
                Line(intact, ruined, rng, a, b, -1f); Line(intact, ruined, rng, b, c, -1f); Line(intact, ruined, rng, d, a, -1f);
                Line(intact, ruined, rng, c, d, VKit.Rnd(rng, 0.3f, 0.7f));            // the front side has a gate
                // a wall running off across the fields
                Vector3 e = VKit.Chance(rng, 0.5f) ? b : c;
                Line(intact, ruined, rng, e, e + VKit.Dir(VKit.Rnd(rng, 0f, 360f)) * VKit.Rnd(rng, 30f, 70f), -1f);
            }
            // long boundary walls on the plateau east of the rim
            for (int i = 0; i < 9; i++)
            {
                Vector3 a = new Vector3(VKit.Rnd(rng, 112f, 215f), 0f, -250f + i * 72f + VKit.Rnd(rng, -20f, 20f));
                float yaw = (VKit.Chance(rng, 0.5f) ? 0f : 90f) + VKit.Rnd(rng, -18f, 18f);
                Line(intact, ruined, rng, a, a + VKit.Dir(yaw) * VKit.Rnd(rng, 40f, 105f), -1f);
            }
            // walls running inland from the headland path
            float[] along = { 18f, 78f, 146f, 214f };
            for (int i = 0; i < along.Length; i++)
            {
                float s = PathDef.SPromEnd + along[i];
                Vector3 p = PathDef.Pos(s), dir = PathDef.FlatDir(s);
                Vector3 r = new Vector3(dir.z, 0f, -dir.x);
                Vector3 a = p + r * (PathDef.HalfWidth(s) + 4f);
                Line(intact, ruined, rng, a, a + (r + dir * VKit.Rnd(rng, -0.25f, 0.25f)).normalized * VKit.Rnd(rng, 45f, 90f), -1f);
            }
            Terraces(intact, ruined, new System.Random(Village.Seed + 307));   // own stream, last: the walls above stay put
        }

        /// <summary>
        /// Contour walls across the toe of the cascade (VillageZones.ToeZ0..ToeZ1), one run per TerraceAlts altitude, in
        /// Seg steps along z. Each segment joins two points of the contour on the drawn ground; houses, the path and its
        /// terraces, jumps of the contour and bumps under the segment leave gaps, as do random fallen stretches. The wave
        /// sweeps a third of them away and lowers most of the rest.
        /// </summary>
        static void Terraces(MeshBuilder[] intact, MeshBuilder[] ruined, System.Random rng)
        {
            Material stone = VKit.Pumice != null ? VKit.Pumice : VKit.Rock;   // the grey dry-stone of the house foundations
            int built = 0;
            for (int k = 0; k < TerraceAlts.Length; k++)
            {
                float alt = TerraceAlts[k];
                float xa = ContourX(alt, VillageZones.ToeZ0);
                for (float z = VillageZones.ToeZ0; z + Seg <= VillageZones.ToeZ1; z += Seg)
                {
                    float xb = ContourX(alt, z + Seg);
                    float x0 = xa; xa = xb;
                    // drawn every step, so a gap never shifts the stream: v gaps, w crown height, u what the wave leaves
                    double v = rng.NextDouble(), w = rng.NextDouble(), u = rng.NextDouble();
                    if (x0 < 0f || xb < 0f || Mathf.Abs(xb - x0) > TerraceMaxDx || v < TerraceGap) continue;
                    Vector3 p = new Vector3(x0, alt, z), q = new Vector3(xb, alt, z + Seg), mid = (p + q) * 0.5f;
                    float gm = TerrainBuilder.Height(mid.x, mid.z);
                    if (gm > alt + 0.8f || gm < alt - 1.0f) continue;                // the straight wall would bury or float in the middle
                    if (VKit.PathGap(p.x, p.z) < TerracePathGap || VKit.PathGap(q.x, q.z) < TerracePathGap || VKit.PathGap(mid.x, mid.z) < TerracePathGap) continue;
                    if (VillagePlan.Occupied(p.x, p.z, 0.8f) || VillagePlan.Occupied(q.x, q.z, 0.8f) || VillagePlan.Occupied(mid.x, mid.z, 0.8f)) continue;
                    Vector3 dir = (q - p).normalized, side = Vector3.Cross(Vector3.up, dir);
                    float top = TerraceTop * (0.85f + 0.3f * (float)w), th = TerraceT;
                    int band = VillagePlan.BandOf(alt);
                    TerraceSeg(intact[band], stone, p, q, dir, side, top, th);
                    built++;
                    if (band > 6) continue;
                    if (alt >= VillagePlan.IntactAbove) { TerraceSeg(ruined[band], stone, p, q, dir, side, top, th); continue; }
                    if (u < 0.35) continue;                                             // swept away
                    TerraceSeg(ruined[band], stone, p, q, dir, side, top * (0.2f + 0.5f * (float)((u - 0.35) / 0.65)), th * 1.1f);   // knocked down
                }
            }
            Debug.Log("CASCADE_INFO terraces: " + built + " contour wall segments behind the harbour row");
        }

        /// <summary>x of the contour at altitude alt behind the harbour row at z: the first uphill crossing of the drawn ground
        /// (TerrainBuilder, what the plants and walls stand on) from the land edge of the quay; -1 where the ground there already
        /// stands higher or never gets there. (VillageZones.FrontAt starts at the cliff houses' keep-out instead, so the low
        /// contours would collapse onto that line.)</summary>
        static float ContourX(float alt, float z)
        {
            const float Step = 0.5f;
            float x = ArchQuay.XLand + 1f, prev = TerrainBuilder.Height(x, z);
            if (prev >= alt) return -1f;
            for (x += Step; x <= 90f; x += Step)
            {
                float y = TerrainBuilder.Height(x, z);
                if (y >= alt) return x - Step * (y - alt) / Mathf.Max(y - prev, 1e-3f);
                prev = y;
            }
            return -1f;
        }

        /// <summary>One terrace wall segment from p to q (both on the contour): crown top above it, footing TerraceFoot below
        /// it, overlapping its neighbours by half its thickness so the run reads continuous where the contour bends.</summary>
        static void TerraceSeg(MeshBuilder mb, Material m, Vector3 p, Vector3 q, Vector3 dir, Vector3 side, float top, float th)
        {
            Vector3 mid = (p + q) * 0.5f + Vector3.up * ((top - TerraceFoot) * 0.5f);
            VKit.Box(mb, m, mid, (q - p) * 0.5f + dir * (th * 0.5f), Vector3.up * ((top + TerraceFoot) * 0.5f), side * (th * 0.5f), 8);
        }

        /// <summary>True where ground at height y lies within margin of a terrace contour (ScrubGen keeps its plants off the walls).</summary>
        public static bool NearTerrace(float y, float margin)
        {
            for (int k = 0; k < TerraceAlts.Length; k++) if (Mathf.Abs(y - TerraceAlts[k]) < margin) return true;
            return false;
        }

        /// <summary>One straight wall from a to b (plan); gate = fraction along it where a 2.5 m opening is left, or negative for none.</summary>
        static void Line(MeshBuilder[] intact, MeshBuilder[] ruined, System.Random rng, Vector3 a, Vector3 b, float gate)
        {
            a.y = 0f; b.y = 0f;
            float len = Vector3.Distance(a, b);
            int n = Mathf.Max(1, Mathf.RoundToInt(len / Seg));
            Vector3 dir = (b - a) / len, side = Vector3.Cross(Vector3.up, dir);
            float gateAt = gate * len;
            for (int i = 0; i < n; i++)
            {
                float t0 = len * i / n, t1 = len * (i + 1) / n;
                if (gate >= 0f && t1 > gateAt - 1.25f && t0 < gateAt + 1.25f) continue;
                Vector3 p = a + dir * t0, q = a + dir * t1;
                p.y = VKit.Ground(p.x, p.z); q.y = VKit.Ground(q.x, q.z);
                if (Mathf.Abs(p.y - q.y) > MaxStep || p.y < 3f || q.y < 3f) continue;
                if (VKit.PathGap(p.x, p.z) < 2f || VKit.PathGap(q.x, q.z) < 2f) continue;
                if (VillagePlan.Occupied(p.x, p.z, 0.8f) || VillagePlan.Occupied(q.x, q.z, 0.8f)) continue;
                Vector3 mid = (p + q) * 0.5f;
                int band = VillagePlan.BandOf(mid.y);
                float h = WallH * VKit.Rnd(rng, 0.88f, 1.08f), th = WallT * VKit.Rnd(rng, 0.9f, 1.1f);
                Segment(intact[band], p, q, side, h, th);
                if (band > 6) continue;
                if (mid.y >= VillagePlan.IntactAbove) { Segment(ruined[band], p, q, side, h, th); continue; }
                double v = rng.NextDouble();
                if (v < 0.45) continue;                                         // swept away
                Segment(ruined[band], p, q, side, h * VKit.Rnd(rng, 0.25f, 0.7f), th * 1.15f);
                if (v <= 0.8) continue;
                Vector3 heap = mid + side * VKit.Rnd(rng, -1.2f, 1.2f);
                if (VKit.PathGap(heap.x, heap.z) > 2.2f) VKit.Mound(ruined[band], VKit.Rock, heap, 1.1f, 0.8f, 0.35f, VKit.Rnd(rng, 0f, 180f), rng, 7);
            }
        }

        static void Segment(MeshBuilder mb, Vector3 p, Vector3 q, Vector3 side, float h, float th)
        {
            const float sunk = 0.2f;
            Vector3 mid = (p + q) * 0.5f + Vector3.up * ((h - sunk) * 0.5f);
            VKit.Box(mb, VKit.Rock, mid, (q - p) * 0.5f, Vector3.up * ((h + sunk) * 0.5f), side * (th * 0.5f), 8);
        }
    }
}
