using UnityEngine;

namespace CASCADE
{
    /// <summary>
    /// Ground vegetation on the bare banks beside the path: dry grass and phrygana tufts (crossed leaf cards), small shrubs,
    /// prickly pears and agaves on a jittered grid (denser, shrubbier and with lava boulders on the stair-road's banks), the
    /// same plus lava boulders on the steep toe behind the harbour row (its
    /// own grid, ToeScatter), and three trees for the skyline (a weeping pepper tree at a harbour alley,
    /// a cypress by the chapel door, an olive on a bank of the stair-road). Pure functions of hashes and its own streams, so
    /// the village plan is untouched. Built after the village into its altitude bands (ClothDirector.Build): the wave takes
    /// it with them, and the ruined bands keep a thinned copy below the run-up.
    /// </summary>
    internal static class ScrubGen
    {
        // ---- tunables ----
        public const float Cell = 1.6f;                     // jittered grid spacing, metres
        public const float MinGap = 1.2f;                   // plants keep this far off the walkable path edge
        public const float Density = 0.6f, Falloff = 18f;   // chance per cell = Density * exp(-gap / Falloff) * SectionK
        public const float HeadlandK = 0.8f;                // the headland (walked in the dark) gets a little less
        public const float MinNormalY = 0.55f;              // steeper ground (cliff faces) stays bare
        public const float MinGround = 3f;                  // nor below this height (beach, the backs of the quay)
        public const float RuinKeep = 0.5f;                 // share of the plants below the run-up that the ruined bands keep
        const float PTuft = 0.8f, PShrub = 0.92f, PPear = 0.98f;   // cumulative: tufts, shrubs, prickly pears, the rest agaves
        // The stair-road's banks (sec 1, 92-180 s) are the closest landscape of the climb, seen from the treads at 3-20 m: a
        // 0.35-0.8 m grass card is lost against the dry earth there (100: one brown bank, three tufts). They get more plants and
        // a phrygana mix: fewer tufts, more shrub cushions, pears and agaves, and half-buried lava boulders as on the toe.
        public const float StairRoadK = 1.35f;              // density factor on the stair-road (Density * exp(-gap / Falloff) * this)
        const float SrTuft = 0.45f, SrShrub = 0.80f, SrPear = 0.92f;   // cumulative there: tufts, shrubs, prickly pears, the rest agaves
        public const float SrRocks = 0.10f;                 // share of the stair-road sites that are a lava boulder instead
        const float SrRockGap = 2.4f;                       // boulders (up to ~1.7 m with a split-off piece) keep this far off the path edge
        const float SrBigGap = 2.0f;                        // and pears (0.85 m half-width) and agaves (1.1 m blades): the side walls end 0.35 m out
        const float TuftPair = 0.3f, TuftTriple = 0.1f;     // chance of a second and a third tuft at a grass site (1.4 on average)
        const float TuftSink = 0.06f;                       // card feet below the ground (the terrain mesh is coarser than its function)
        const float PepperZ = -109f;                        // the pepper tree stands in the mouth of this harbour alley (VillageZones.Alleys)
        // Beyond the caldera parapet / headland railing (promenade and headland) only tufts and shrubs grow within this many
        // metres: a pear fan or agave there stands up behind the rails as a dark cairn against the bright sea.
        const float ParapetClear = 8f;
        // Tree crowns: clusters carry a full shell of cards (shrubs and pots stay capped at 14 per lobe) round a small dark
        // lumpy heart, so the crown reads as layered foliage with depth in the gaps, not lit green eggs under a sparse veil.
        const int CrownCards = 42;                          // leaf cards per crown cluster, at most (about 3x the shrub cap)
        const float CrownCardK = 1f;                        // card size factor: 0.62-0.64 m cards at the 0.32 m half-size cap
        const float CrownCoreK = 0.36f, CrownCoreNoise = 0.42f;   // solid heart of a cluster, relative to it, and its lumpiness

        // ---- the toe: the 45-55 degree slope behind the harbour row, beyond Scatter's 17 m reach of the path. Frames 20 and 45
        //      look straight up at it; it was a bare smear of the land material between the row's roofs and the houses above ----
        public const float ToeCell = 1.8f;                  // jittered grid spacing, metres
        public const float ToeDensity = 0.36f;              // chance per cell, no falloff (the whole slope faces the quay). Scatter's
                                                            // chance where its reach ends (gap 14 m) is 0.28, so the seam is invisible
        public const float ToeXFrom = 4f, ToeXTo = 70f;     // x window beyond ArchQuay.XLand (z: the harbour row's extent)
        public const float ToeYMin = 6f, ToeYMax = 45f;     // altitude window: above the row's backs, up to the free cliff clusters
        public const float ToeMinNormalY = 0.42f;           // up to 65 degrees: the toe is steeper than the banks Scatter dresses
        const float TShrub = 0.35f, TPear = 0.55f, TAgave = 0.65f, TBoulder = 0.85f;   // cumulative; the rest are tuft groups
        const float ToeLean = 0.6f;                         // shrub cushions lie this far (slerp) from upright into the slope
        const float ToeMaxTan = 1.5f;                       // slope used to sink plants is capped here (56 degrees)
        const int ToeCards = 9; const float ToeCardK = 1.25f;   // fewer, larger leaf cards per lobe: seen from 15-60 m, same cover
        const float TerraceClear = 0.55f;                   // plant sites keep this far (in height) off the FieldGen terrace contours:
                                                            // the 0.6 m wall spans about +-0.36 m of ground height on the slope

        static MeshBuilder[] tuftI, tuftR, bushI, bushR;   // per band: tufts (no shadows), shrubs, succulents and trees (shadows)
        static Material cactus, bark, lava;
        static int nTuft, nShrub, nPear, nAgave, nTree, nBoulder, nToe, nRoad;
        static readonly Vector3 Up = Vector3.up;

        public static void Build(Village.VillageResult vil, PathArchitecture.ArchResult arch)
        {
            if (vil == null || vil.intactBands == null || VKit.LeafCard == null || VKit.PlantCore == null)
            {
                Debug.Log("CASCADE_INFO scrub skipped: the leaf-card materials are not built yet");
                return;
            }
            cactus = VKit.Find("M_Cactus"); bark = VKit.Find("M_Bark");
            if (cactus == null) cactus = VKit.Plant;
            lava = bark != null ? bark : VKit.Rock;            // M_Bark is the dark_rock_02 photo: lava rock, ash-coated with the rest
            if (bark == null) bark = VKit.WoodDk;
            tuftI = new MeshBuilder[8]; tuftR = new MeshBuilder[8]; bushI = new MeshBuilder[8]; bushR = new MeshBuilder[8];
            for (int b = 0; b < 8; b++) { tuftI[b] = new MeshBuilder(); tuftR[b] = new MeshBuilder(); bushI[b] = new MeshBuilder(); bushR[b] = new MeshBuilder(); }
            nTuft = nShrub = nPear = nAgave = nTree = nBoulder = nToe = nRoad = 0;
            int v0 = VKit.CardVerts;
            Scatter(arch);
            ToeScatter();
            var tr = new System.Random(Village.Seed + 103);
            PepperTree(tr);
            Cypress(tr);
            Olive(tr);
            int vScrub = 0;
            for (int b = 0; b < 8; b++) vScrub += tuftI[b].VertexCount + bushI[b].VertexCount;
            for (int b = 0; b < 8; b++)
            {
                GameObject gi = vil.intactBands[b];
                if (gi != null) { tuftI[b].Build(gi.transform, "Scrub_Tufts", false); bushI[b].Build(gi.transform, "Scrub_Plants", true); }
                GameObject gr = vil.ruinedBands != null && b < vil.ruinedBands.Length ? vil.ruinedBands[b] : null;
                if (gr != null) { tuftR[b].Build(gr.transform, "Scrub_Tufts", false); bushR[b].Build(gr.transform, "Scrub_Plants", true); }
            }
            Debug.Log("CASCADE_INFO scrub: " + nTuft + " tufts, " + nShrub + " shrubs, " + nPear + " prickly pears, " + nAgave + " agaves, " + nBoulder +
                      " boulders, " + nTree + " trees (" + nToe + " sites on the toe behind the harbour row, " + nRoad + " on the stair-road's banks); " + vScrub + " intact vertices (" +
                      (VKit.CardVerts - v0) + " of them leaf cards, all builders); leaf cards in the whole world " + VKit.CardVerts);
            tuftI = tuftR = bushI = bushR = null;
        }

        static float H(int ix, int iz, int salt) { return MathX.Hash(ix + salt * 100003, iz + salt * 7); }

        /// <summary>Maps a uniform kind hash on the stair-road's shares (SrTuft, SrShrub, SrPear) onto the bands of the base
        /// table (PTuft, PShrub, PPear), piecewise linearly, so the kind tests in Scatter stay as they are.</summary>
        static float StairRoadKind(float h)
        {
            if (h < SrTuft) return h * (PTuft / SrTuft);
            if (h < SrShrub) return PTuft + (h - SrTuft) * ((PShrub - PTuft) / (SrShrub - SrTuft));
            if (h < SrPear) return PShrub + (h - SrShrub) * ((PPear - PShrub) / (SrPear - SrShrub));
            return PPear + (h - SrPear) * ((1f - PPear) / (1f - SrPear));
        }

        /// <summary>The jittered grid over the path's surroundings (PathDef.Nearest reaches about 17 m).</summary>
        static void Scatter(PathArchitecture.ArchResult arch)
        {
            float x0 = 1e9f, x1 = -1e9f, z0 = 1e9f, z1 = -1e9f;
            for (int i = 0; i < PathDef.Count; i++)
            {
                Vector3 p = PathDef.P[i];
                x0 = Mathf.Min(x0, p.x); x1 = Mathf.Max(x1, p.x); z0 = Mathf.Min(z0, p.z); z1 = Mathf.Max(z1, p.z);
            }
            x0 -= 18f; x1 += 18f; z0 -= 18f; z1 += 18f;
            int nx = Mathf.CeilToInt((x1 - x0) / Cell), nz = Mathf.CeilToInt((z1 - z0) / Cell);
            Vector3 tower = arch != null ? arch.beaconPos : new Vector3(1e6f, 0f, 1e6f);
            for (int iz = 0; iz < nz; iz++)
                for (int ix = 0; ix < nx; ix++)
                {
                    float x = x0 + (ix + 0.15f + 0.7f * H(ix, iz, 1)) * Cell, z = z0 + (iz + 0.15f + 0.7f * H(ix, iz, 2)) * Cell;
                    float s, d;
                    if (!PathDef.Nearest(x, z, out s, out d)) continue;
                    float gap = d - PathDef.HalfWidth(s);
                    if (gap < MinGap) continue;
                    int sec = PathDef.Section(s);
                    float k = sec == 2 ? 0f : sec == 4 ? HeadlandK : sec == 1 ? StairRoadK : 1f;   // the narrow stair is walled in
                    if (H(ix, iz, 3) > Density * Mathf.Exp(-gap / Falloff) * k) continue;
                    if (Blocked(x, z, s, gap, tower)) continue;
                    float y = TerrainBuilder.Height(x, z);
                    if (y < MinGround) continue;
                    if (gap > FieldGen.TerracePathGap - 1f && OnToe(x, z) && FieldGen.NearTerrace(y, TerraceClear)) continue;   // a terrace wall's strip
                    Vector3 n = GroundNormal(x, z);
                    if (n.y < MinNormalY || VillagePlan.Occupied(x, z, 0.8f)) continue;
                    Vector3 g = new Vector3(x, y, z);
                    float kind = H(ix, iz, 4);
                    if (sec == 1) kind = StairRoadKind(kind);                           // same hash, remapped onto the phrygana mix
                    bool rock = sec == 1 && gap > SrRockGap && H(ix, iz, 9) < SrRocks;   // a new salt: the other draws stay put
                    if (sec == 1) nRoad++;
                    if (sec == 1 && kind >= PShrub && gap < SrBigGap) kind = StairRoadKind(H(ix, iz, 8) * SrShrub);   // a tuft or shrub: no pad or blade through the side walls
                    if (kind >= PShrub && sec >= 3 && gap < ArchPromenade.Margin + ArchPromenade.Bulge(s) + ParapetClear && Side(x, z, s) > 0)
                        kind = H(ix, iz, 8) * PShrub;                                   // a tuft or shrub instead (hash: the same in both bands)
                    int band = VillagePlan.BandOf(y), seed = unchecked(ix * 7919 + iz * 104729 + 17);
                    bool ruin = band <= 6 && (rock || y >= VillagePlan.IntactAbove || H(ix, iz, 5) < RuinKeep);   // boulders survive the wave
                    for (int pass = 0; pass < (ruin ? 2 : 1); pass++)
                    {
                        var r = new System.Random(seed);                                    // the same plant in both bands
                        MeshBuilder tb = pass == 0 ? tuftI[band] : tuftR[band], bb = pass == 0 ? bushI[band] : bushR[band];
                        if (rock) { Boulder(bb, g, n, r); if (pass == 0) nBoulder++; }
                        else if (kind < PTuft)
                        {
                            int m = 1 + (H(ix, iz, 6) < TuftPair ? 1 : 0) + (H(ix, iz, 7) < TuftTriple ? 1 : 0);   // tufts grow in little groups
                            for (int j = 0; j < m; j++)
                            {
                                Vector3 q = g;
                                if (j > 0)
                                {
                                    q += VKit.Dir(VKit.Rnd(r, 0f, 360f)) * VKit.Rnd(r, 0.3f, 0.6f);
                                    q.y = TerrainBuilder.Height(q.x, q.z);
                                }
                                Tuft(tb, q, n, r);
                                if (pass == 0) nTuft++;
                            }
                        }
                        else if (kind < PShrub) { Shrub(bb, g, r); if (pass == 0) nShrub++; }
                        else if (kind < PPear) { PricklyPear(bb, g, n, r); if (pass == 0) nPear++; }
                        else { Agave(bb, g, n, r); if (pass == 0) nAgave++; }
                    }
                }
        }

        /// <summary>
        /// The toe of the cascade: the slope straight behind the harbour row (frames 20 and 45 look up at it), on its own
        /// jittered grid wherever Scatter does not reach (more than about 17 m from the path). Shrubs, prickly pears, agaves,
        /// half-buried lava boulders and tuft groups at an even density, set into the 45-55 degree ground: cushions lean with
        /// it and everything sinks by the slope, so no downhill edge floats in the view from the quay. Houses and the FieldGen
        /// terrace walls are kept clear. Bands and ruins as in Scatter, except that boulders always survive the wave.
        /// </summary>
        static void ToeScatter()
        {
            float x0 = ArchQuay.XLand + ToeXFrom, x1 = ArchQuay.XLand + ToeXTo, z0 = VillageZones.RowZ0, z1 = VillageZones.RowZ1;
            int nx = Mathf.CeilToInt((x1 - x0) / ToeCell), nz = Mathf.CeilToInt((z1 - z0) / ToeCell);
            for (int iz = 0; iz < nz; iz++)
                for (int ix = 0; ix < nx; ix++)
                {
                    if (H(ix, iz, 23) > ToeDensity) continue;
                    float x = x0 + (ix + 0.15f + 0.7f * H(ix, iz, 21)) * ToeCell, z = z0 + (iz + 0.15f + 0.7f * H(ix, iz, 22)) * ToeCell;
                    float s, d;
                    if (PathDef.Nearest(x, z, out s, out d)) continue;                 // Scatter's ground (and the stair-road's banks)
                    float y = TerrainBuilder.Height(x, z);
                    if (y < ToeYMin || y > ToeYMax || FieldGen.NearTerrace(y, TerraceClear)) continue;
                    Vector3 n = GroundNormal(x, z);
                    if (n.y < ToeMinNormalY || VillagePlan.Occupied(x, z, 1.0f)) continue;
                    float tan = Mathf.Min(Mathf.Sqrt(Mathf.Max(0f, 1f - n.y * n.y)) / n.y, ToeMaxTan);
                    Vector3 g = new Vector3(x, y, z);
                    float kind = H(ix, iz, 24);
                    int band = VillagePlan.BandOf(y), seed = unchecked(ix * 6007 + iz * 92821 + 4099);
                    bool boulder = kind >= TAgave && kind < TBoulder;
                    bool ruin = band <= 6 && (boulder || y >= VillagePlan.IntactAbove || H(ix, iz, 25) < RuinKeep);
                    nToe++;
                    for (int pass = 0; pass < (ruin ? 2 : 1); pass++)
                    {
                        var r = new System.Random(seed);                                    // the same plant in both bands
                        MeshBuilder tb = pass == 0 ? tuftI[band] : tuftR[band], bb = pass == 0 ? bushI[band] : bushR[band];
                        bool count = pass == 0;
                        if (kind < TShrub)
                        {
                            Vector3 up = Vector3.Slerp(Up, n, ToeLean).normalized;
                            Shrub(bb, g, r, up, ResidTan(up, n), ToeCards, ToeCardK);
                            if (count) nShrub++;
                        }
                        // the root pad leans 30 % with the slope; its downhill corner is up to 0.16 m out
                        else if (kind < TPear) { PricklyPear(bb, g - Up * (0.14f * tan), n, r); if (count) nPear++; }
                        // downhill blades stand out over the slope as on any cliff agave; the base settles a little
                        else if (kind < TAgave) { Agave(bb, g - Up * (0.1f * tan), n, r); if (count) nAgave++; }
                        else if (boulder) { Boulder(bb, g, n, r); if (count) nBoulder++; }
                        else
                        {
                            int m = 2 + (H(ix, iz, 26) < 0.4f ? 1 : 0);                   // tufts only read in groups at this range
                            float rt = ResidTan(Vector3.Slerp(Up, n, 0.35f).normalized, n);
                            for (int j = 0; j < m; j++)
                            {
                                Vector3 q = g;
                                if (j > 0)
                                {
                                    q += VKit.Dir(VKit.Rnd(r, 0f, 360f)) * VKit.Rnd(r, 0.35f, 0.75f);
                                    q.y = TerrainBuilder.Height(q.x, q.z);
                                }
                                Tuft(tb, q, n, r, rt);
                                if (count) nTuft++;
                            }
                        }
                    }
                }
        }

        /// <summary>Inside the window where FieldGen builds its terrace walls (the toe behind the harbour row).</summary>
        static bool OnToe(float x, float z) { return z > VillageZones.ToeZ0 - 2f && z < VillageZones.ToeZ1 + 2f && x > ArchQuay.XLand + 2f; }

        /// <summary>Tangent of the angle between a plant's axis and the ground normal (the slope it still has to settle into), capped.</summary>
        static float ResidTan(Vector3 up, Vector3 n) { return Mathf.Min(Mathf.Tan(Vector3.Angle(up, n) * Mathf.Deg2Rad), ToeMaxTan); }

        /// <summary>Places where the ground is paved or built over although the plan does not know it: the quay slab and mole,
        /// the landing terraces of the stair-road, the belvedere and the caldera parapet, the keeper's house, the lighthouse.</summary>
        static bool Blocked(float x, float z, float s, float gap, Vector3 tower)
        {
            if (z > ArchQuay.ZSouth - 3f && z < ArchQuay.ZNorth + 3f && x < ArchQuay.XLand + 0.8f) return true;
            int side = Side(x, z, s);
            for (int i = 1; i < ArchStairs.Bends.Count; i++)
            {
                ArchBend b = ArchStairs.Bends[i];
                if (b.outer == side && s > b.terrace - ArchLandings.TerraceHalf - 0.6f && s < b.terrace + ArchLandings.TerraceHalf + 0.6f &&
                    gap < ArchStairs.StepExt + ArchLandings.TerraceDepth + 0.6f) return true;
            }
            int sec = PathDef.Section(s);
            if (sec >= 3 && side > 0 && gap < ArchPromenade.Margin + ArchPromenade.Bulge(s) + 0.8f) return true;
            // keeper's house frame: x right of the last walking direction, z forward from the door
            Vector3 e = PathDef.Pos(PathDef.Length), fw = PathDef.FlatDir(PathDef.Length);
            Vector3 o = e + fw * ArchLighthouse.DoorAhead;
            float hx = (x - o.x) * fw.z - (z - o.z) * fw.x, hz = (x - o.x) * fw.x + (z - o.z) * fw.z;
            if (hx > ArchLighthouse.HouseLeft - 1.2f && hx < 9f && hz > -ArchLighthouse.DoorAhead - 1.2f && hz < ArchLighthouse.HouseDepth + 1f) return true;
            float tx = x - tower.x, tz = z - tower.z;
            return tx * tx + tz * tz < 3.8f * 3.8f;
        }

        /// <summary>Which side of the path (at chainage s) the point is on: +1 left of the walking direction (the caldera / bay
        /// side, with the parapet and railing from the promenade on), -1 right.</summary>
        static int Side(float x, float z, float s)
        {
            Vector3 p = PathDef.Pos(s), f = PathDef.FlatDir(s);
            float lat = (x - p.x) * -f.z + (z - p.z) * f.x;
            return lat > 0f ? 1 : -1;
        }

        static Vector3 GroundNormal(float x, float z)
        {
            const float e = 0.6f;
            float hx = TerrainBuilder.Height(x + e, z) - TerrainBuilder.Height(x - e, z), hz = TerrainBuilder.Height(x, z + e) - TerrainBuilder.Height(x, z - e);
            return new Vector3(-hx, 2f * e, -hz).normalized;
        }

        // ---------------- plants ----------------

        static void Tuft(MeshBuilder mb, Vector3 g, Vector3 gn, System.Random r) { Tuft(mb, g, gn, r, 0f); }

        /// <summary>Three crossed cards of the grass cell, 0.35-0.8 m, leaning a little with the slope; dome-shaded normals.
        /// residTan: the slope left over against the cards' lean; they sink by it so the downhill foot of a card that runs
        /// down the slope stays in the ground (0 on the banks Scatter dresses: unchanged there).</summary>
        static void Tuft(MeshBuilder mb, Vector3 g, Vector3 gn, System.Random r, float residTan)
        {
            float h = VKit.Rnd(r, 0.35f, 0.8f), w = h * VKit.Rnd(r, 0.8f, 1.05f);
            Vector3 up = Vector3.Slerp(Up, gn, 0.35f).normalized;
            float sink = TuftSink + 0.5f * w * residTan;
            Vector3 foot = g - up * sink, top = up * (h + sink);
            float yaw = VKit.Rnd(r, 0f, 180f);
            for (int k = 0; k < 3; k++)
            {
                Vector3 a = VKit.Dir(yaw + k * 60f + VKit.Rnd(r, -10f, 10f));
                a = (a - up * Vector3.Dot(a, up)).normalized;
                Vector3 lean = Vector3.Cross(a, up) * (h * VKit.Rnd(r, -0.15f, 0.15f));
                Vector3 hw = a * (w * 0.5f);
                Vector3 nl = (gn * 0.8f - a * 0.6f).normalized, nr = (gn * 0.8f + a * 0.6f).normalized;
                Vector3 tl = (up - a * 0.8f).normalized, trn = (up + a * 0.8f).normalized;
                VKit.QueueCard(foot - hw, foot + hw, foot + hw + top + lean, foot - hw + top + lean, VKit.CellGrass, r.NextDouble() < 0.5, nl, nr, trn, tl);
            }
            VKit.EmitCards(mb, VKit.LeafCard);
        }

        static void Shrub(MeshBuilder mb, Vector3 g, System.Random r) { Shrub(mb, g, r, Up, 0f, 14, 1f); }

        /// <summary>A small lentisk or thyme cushion: one to three dark solid lobes under leaf cards. up: the cushion's axis
        /// (leaning into a steep slope); residTan: the slope left over against its base plane, which it settles into so that
        /// no downhill edge floats (the side lobes reach 0.89 rad out, their lowest cards 0.48 hgt below the centre);
        /// maxCards, cardK: leaf cards per lobe and their size. The defaults (upright, 0, 14, 1) are the banks' shrub.</summary>
        static void Shrub(MeshBuilder mb, Vector3 g, System.Random r, Vector3 up, float residTan, int maxCards, float cardK)
        {
            float rad = VKit.Rnd(r, 0.25f, 0.6f), hgt = rad * VKit.Rnd(r, 0.6f, 0.9f);
            Vector3 c = g + up * (hgt * 0.7f);
            if (residTan > 0f) c -= Up * (0.25f * hgt + 0.89f * rad * residTan);
            Vector3 hd = VKit.Dir(VKit.Rnd(r, 0f, 180f));
            if (up != Up) hd = (hd - up * Vector3.Dot(hd, up)).normalized;
            Vector3 ax = hd * rad, ay = up * hgt, az = Vector3.Cross(up, ax.normalized) * (rad * VKit.Rnd(r, 0.75f, 1f));
            int lobes = rad > 0.42f ? 3 : 1;
            for (int i = 0; i < lobes; i++)
            {
                float a = i * 2.4f + VKit.Rnd(r, -0.4f, 0.4f);
                Vector3 off = i == 0 ? Vector3.zero : (ax * Mathf.Cos(a) + az * Mathf.Sin(a)) * 0.45f - ay * 0.15f;
                float k = i == 0 ? 0.75f : 0.55f;
                VKit.BlobOne(mb, VKit.PlantCore, c + off, ax * (k * VKit.CoreShrink), ay * (k * VKit.CoreShrink), az * (k * VKit.CoreShrink), r, 0.25f, 6, 4);
                VKit.LeafCards(mb, VKit.MixWild, c + off, ax * k, ay * k, az * k, c, ax, ay, az, r, cardK, maxCards);
            }
        }

        /// <summary>A boulder of dark lava rock settled into a slope: a lumpy ellipsoid 0.7-1.8 m long whose long axes lie
        /// (three quarters of the way) in the ground plane, about a third of it below ground and sunk further by the slope left
        /// over, so its downhill rim never floats in the view from below; now and then a split-off piece beside it.
        /// It uses M_Bark, the dark_rock_02 photo (ash-coated with the rest), not the masonry of M_RockWall.</summary>
        static void Boulder(MeshBuilder mb, Vector3 g, Vector3 gn, System.Random r)
        {
            Vector3 up = Vector3.Slerp(Up, gn, 0.75f).normalized;
            float resid = ResidTan(up, gn);
            float a = VKit.Rnd(r, 0.35f, 0.9f), b = a * VKit.Rnd(r, 0.6f, 0.95f), h = a * VKit.Rnd(r, 0.5f, 0.75f);
            Vector3 hd = VKit.Dir(VKit.Rnd(r, 0f, 180f));
            hd = (hd - up * Vector3.Dot(hd, up)).normalized;
            Vector3 sd = Vector3.Cross(up, hd);
            VKit.BlobOne(mb, lava, g + up * (h * 0.3f) - Up * (a * resid + 0.05f), hd * a, up * h, sd * b, r, 0.22f, 8, 5);
            if (r.NextDouble() < 0.3)
            {
                float a2 = a * VKit.Rnd(r, 0.4f, 0.6f), h2 = a2 * VKit.Rnd(r, 0.5f, 0.75f);
                Vector3 g2 = g + hd * (VKit.Sgn(r) * (a + a2 * 0.5f)) + sd * (b * VKit.Rnd(r, -0.4f, 0.4f));
                g2.y = TerrainBuilder.Height(g2.x, g2.z);
                VKit.BlobOne(mb, lava, g2 + up * (h2 * 0.3f) - Up * (a2 * resid + 0.04f), hd * a2, up * h2, sd * (a2 * 0.8f), r, 0.25f, 7, 4);
            }
        }

        /// <summary>
        /// Prickly pear: 7-14 squashed pads branching as a bush, not a chain. Each pad grows from the rim of a random earlier
        /// one (at most three on a pad: the first two lean out to opposite shoulders, a third rises from the top; at most
        /// PearGens pads above the root), leaning 28-65 degrees in the parent's plane, a little out of it and twisted about
        /// itself, so the plant is a fan about 0.8-1.2 m tall and 0.9-1.7 m wide; the old pads near the root are the largest.
        /// Red fruit on the rims of the end pads.
        /// </summary>
        static void PricklyPear(MeshBuilder mb, Vector3 g, Vector3 gn, System.Random r)
        {
            const float MaxLean = 70f * Mathf.Deg2Rad;          // no pad grows flatter than this off the vertical or off the ground
            const int PearGens = 3;                             // generations above the root pad: caps the height (no cairn)
            int n = 7 + r.Next(8);
            var cen = new Vector3[n]; var up = new Vector3[n]; var side = new Vector3[n]; var nrm = new Vector3[n]; var hh = new float[n]; var hwid = new float[n];
            var kids = new int[n]; var gen = new int[n]; var firstSgn = new float[n];
            int built = n;
            for (int i = 0; i < n; i++)
            {
                float age = Mathf.Lerp(1.12f, 0.88f, i / (float)(n - 1));
                hwid[i] = VKit.Rnd(r, 0.1f, 0.145f) * age; hh[i] = VKit.Rnd(r, 0.15f, 0.21f) * age;
                if (i == 0)
                {
                    up[i] = Quaternion.AngleAxis(VKit.Rnd(r, -12f, 12f), VKit.Dir(VKit.Rnd(r, 0f, 360f))) * Vector3.Slerp(Up, gn, 0.3f);
                    side[i] = Vector3.Cross(up[i], VKit.Dir(VKit.Rnd(r, 0f, 360f))).normalized;
                    nrm[i] = Vector3.Cross(side[i], up[i]).normalized;
                    cen[i] = g + up[i] * (hh[i] * 0.85f);
                    continue;
                }
                int pa = -1;
                for (int t = 0; t < 8 && pa < 0; t++)
                {
                    int q = r.Next(i);
                    if (kids[q] < 3 && gen[q] < PearGens) pa = q;
                }
                if (pa < 0) { built = i; break; }                 // the bush is full
                gen[i] = gen[pa] + 1;
                int order = kids[pa]++;
                float sgn = order == 0 ? VKit.Sgn(r) : -firstSgn[pa];
                if (order == 0) firstSgn[pa] = sgn;
                float tilt = order >= 2 ? VKit.Rnd(r, -14f, 14f) : VKit.Rnd(r, 28f, 65f) * sgn;
                Vector3 u = Quaternion.AngleAxis(tilt, nrm[pa]) * up[pa];                  // in the parent's plane
                u = Quaternion.AngleAxis(VKit.Rnd(r, -25f, 25f), side[pa]) * u;           // and a little out of it: the bush has depth
                float lean = Mathf.Acos(Mathf.Clamp(u.y, -1f, 1f));
                if (lean > MaxLean) u = Vector3.RotateTowards(u, Up, lean - MaxLean, 0f);   // pads grow upwards
                lean = Mathf.Acos(Mathf.Clamp(Vector3.Dot(u, gn), -1f, 1f));
                if (lean > MaxLean) u = Vector3.RotateTowards(u, gn, lean - MaxLean, 0f);   // and not into an uphill slope
                up[i] = u.normalized;
                Quaternion twist = Quaternion.AngleAxis(VKit.Rnd(r, -60f, 60f), up[i]);
                Vector3 nn = Vector3.Cross(twist * side[pa], up[i]);
                if (nn.sqrMagnitude < 0.04f) nn = nrm[pa] - up[i] * Vector3.Dot(nrm[pa], up[i]);   // the lean clamps turned it onto the parent's side axis
                nrm[i] = nn.normalized; side[i] = Vector3.Cross(up[i], nrm[i]).normalized;
                // the joint sits on the parent's rim where the child points: a pad leaning right grows from the right shoulder
                float rim = Mathf.Atan2(Vector3.Dot(up[i], side[pa]), Vector3.Dot(up[i], up[pa]));
                rim = Mathf.Clamp(rim, -1.2f, 1.2f);
                Vector3 attach = cen[pa] + (up[pa] * (hh[pa] * Mathf.Cos(rim)) + side[pa] * (hwid[pa] * Mathf.Sin(rim))) * 0.82f;
                cen[i] = attach + up[i] * (hh[i] * 0.9f);
            }
            // rings set the face-on outline (2 points per ring: 14-gon), seg only the thin edge-on section
            for (int i = 0; i < built; i++)
                VKit.BlobOne(mb, cactus, cen[i], side[i] * hwid[i], up[i] * hh[i], nrm[i] * 0.028f, r, 0.07f, 6, 7);
            int fruit = 2 + r.Next(3);
            for (int i = built - 1; i >= 0 && fruit > 0; i--)
            {
                if (kids[i] > 0) continue;
                int m = Mathf.Min(fruit, 1 + r.Next(2));
                for (int j = 0; j < m; j++)
                {
                    Vector3 p = cen[i] + up[i] * (hh[i] * 0.95f) + side[i] * (hwid[i] * (j == 0 ? -0.35f : 0.35f));
                    VKit.BlobOne(mb, VKit.Terra, p + up[i] * 0.03f, side[i] * 0.03f, up[i] * 0.045f, nrm[i] * 0.03f, r, 0.04f, 6, 3);
                }
                fruit -= m;
            }
        }

        /// <summary>Agave rosette: 14 tapered blades on the golden angle, the inner ones steeper and shorter; both faces built.</summary>
        static void Agave(MeshBuilder mb, Vector3 g, Vector3 gn, System.Random r)
        {
            const int Blades = 14;
            float L = VKit.Rnd(r, 0.6f, 1.1f), yaw0 = VKit.Rnd(r, 0f, 360f);
            Vector3 b0 = g + gn * 0.05f;
            for (int i = 0; i < Blades; i++)
            {
                float inner = i / (float)Blades, az = yaw0 + i * 137.5f + VKit.Rnd(r, -8f, 8f);
                float el = (Mathf.Lerp(15f, 70f, inner) + VKit.Rnd(r, -6f, 6f)) * Mathf.Deg2Rad;
                float len = L * Mathf.Lerp(1f, 0.65f, inner) * VKit.Rnd(r, 0.85f, 1.1f);
                Vector3 hd = VKit.Dir(az), s = Vector3.Cross(Up, hd);
                Vector3 d0 = hd * Mathf.Cos(el) + Up * Mathf.Sin(el), d1 = hd * Mathf.Cos(el * 0.55f) + Up * Mathf.Sin(el * 0.55f);
                Vector3 p1 = b0 + d0 * (len * 0.55f), p2 = p1 + d1 * (len * 0.45f);
                float w0 = 0.075f * Mathf.Lerp(1f, 0.8f, inner), w1 = w0 * 0.7f;
                VKit.Q2(mb, cactus, b0 - s * w0, b0 + s * w0, p1 + s * w1, p1 - s * w1);
                Vector3 face = Vector3.Cross(s, d1);
                VKit.T(mb, cactus, p1 - s * w1, p1 + s * w1, p2, face);
                VKit.T(mb, cactus, p1 - s * w1, p1 + s * w1, p2, -face);
            }
        }

        // ---------------- trees ----------------

        /// <summary>A tapering limb from a to b (bark), 7 sides.</summary>
        static void Limb(MeshBuilder mb, Vector3 a, Vector3 b, float ra, float rb) { mb.Cylinder(bark, a, b, ra, rb, 7, false); }

        /// <summary>
        /// A canopy of n leaf clusters on the upper part of the ellipsoid (centre c, half-axes ax, ay, az), each a small, dark,
        /// lumpy heart inside a full shell of cards (all round: the crowns are seen from below) that all shade as one crown;
        /// thin limbs reach from the fork to the upper clusters.
        /// </summary>
        static void Crown(MeshBuilder mb, Vector3 c, Vector3 ax, Vector3 ay, Vector3 az, int n, float cr, Vector3 fork, System.Random r)
        {
            for (int k = 0; k < n; k++)
            {
                float y = 1f - (k + 0.5f) / n * 1.35f, ring = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y)) * 0.78f, phi = k * 2.39996f + VKit.Rnd(r, -0.3f, 0.3f);
                Vector3 p = c + ax * (Mathf.Cos(phi) * ring) + ay * (y * 0.8f) + az * (Mathf.Sin(phi) * ring);
                float rr = cr * VKit.Rnd(r, 0.85f, 1.15f);
                Vector3 hx = ax.normalized * rr, hy = ay.normalized * (rr * 0.7f), hz = az.normalized * rr;
                VKit.BlobOne(mb, VKit.PlantCore, p, hx * CrownCoreK, hy * CrownCoreK, hz * CrownCoreK, r, CrownCoreNoise, 8, 5);
                VKit.LeafCards(mb, VKit.MixWild, p, hx, hy, hz, c, ax, ay, az, r, CrownCardK, CrownCards, -1f);
                if (y > -0.2f && (k & 1) == 0) Limb(mb, fork, p - (p - fork).normalized * (rr * 0.5f), 0.07f, 0.035f);
            }
        }

        /// <summary>The weeping pepper tree in the mouth of a harbour alley: a leaning trunk, a crown at roof height and curtains
        /// of hanging cards that stop well above the heads on the quay.</summary>
        static void PepperTree(System.Random r)
        {
            Vector3 foot = new Vector3(-1f, Config.QuayHeight, PepperZ);
            if (VillagePlan.Occupied(foot.x, foot.z, 0.5f)) return;
            int band = VillagePlan.BandOf(foot.y);
            Vector3 k1 = foot + new Vector3(-0.15f, 1.6f, 0.1f), k2 = foot + new Vector3(-0.55f, 3.2f, -0.05f), fork = foot + new Vector3(-1.05f, 4.6f, 0.05f);
            for (int pass = 0; pass < 2; pass++)
            {
                MeshBuilder mb = pass == 0 ? bushI[band] : bushR[band];
                if (pass == 1)
                {
                    // the wave snapped it: a split stump, the trunk lying along the frontage
                    Limb(mb, foot - Up * 0.2f, k1 - Up * 0.4f, 0.21f, 0.18f);
                    Vector3 lie = foot + new Vector3(-0.4f, 0.22f, 0.6f);
                    Limb(mb, lie, lie + new Vector3(-0.3f, 0.05f, 3.4f), 0.17f, 0.12f);
                    break;
                }
                Limb(mb, foot - Up * 0.2f, k1, 0.21f, 0.18f); Limb(mb, k1, k2, 0.18f, 0.15f); Limb(mb, k2, fork, 0.15f, 0.12f);
                Vector3 c = foot + new Vector3(-1.3f, 6.3f, 0f);
                Vector3 ax = new Vector3(2.3f, 0f, 0f), ay = new Vector3(0f, 1.4f, 0f), az = new Vector3(0f, 0f, 2.6f);
                Crown(mb, c, ax, ay, az, 17, 0.7f, fork, r);
                // weeping curtains round the rim: chains of hanging cards, the lowest 2.6 m above the quay on the sea side and
                // clear of the awnings (3.1 m) over the frontages either side of the alley
                for (int i = 0; i < 18; i++)
                {
                    float phi = i * Mathf.PI * 2f / 18f + VKit.Rnd(r, -0.12f, 0.12f);
                    Vector3 rim = c + ax * (Mathf.Cos(phi) * 0.92f) + az * (Mathf.Sin(phi) * 0.92f) - ay * 0.25f;
                    float lowest = Config.QuayHeight + (rim.x > -2.2f ? 3.3f : 2.6f);
                    float drop = Mathf.Min(VKit.Rnd(r, 1.2f, 2.6f), rim.y - lowest);
                    Vector3 outw = (rim - c); outw.y = 0f; outw.Normalize();
                    Vector3 tan = Vector3.Cross(Up, outw);
                    for (float d = 0.25f; d < drop; d += 0.42f)
                    {
                        Vector3 p = rim - Up * d + outw * (0.1f * d / Mathf.Max(drop, 0.1f));
                        VKit.Card(p, (outw + Up * 0.15f).normalized, 0.24f, VKit.CellShrub, r, c, ax, ay, az);
                        // a second, smaller sprig beside it half a step lower: a leafy tassel, not a string of beads
                        if (d + 0.21f < drop)
                            VKit.Card(p - Up * 0.21f + tan * VKit.Rnd(r, -0.14f, 0.14f) + outw * 0.04f, (outw + tan * VKit.Rnd(r, -0.5f, 0.5f) + Up * 0.1f).normalized,
                                      0.19f, VKit.CellShrub, r, c, ax, ay, az);
                    }
                }
                VKit.EmitCards(mb, VKit.LeafCard);
                nTree++;
                if (band > 6) break;
            }
        }

        /// <summary>A cypress by the chapel door: a dark solid spindle under rings of leaf cards, 9-11 m tall.</summary>
        static void Cypress(System.Random r)
        {
            Vector3 C = VillagePlan.ChapelC, A = VillagePlan.ChapelAxis, B = Vector3.Cross(Up, A);
            if (A.sqrMagnitude < 0.5f) return;
            Vector3 foot = C - A * (ChapelGen.NaveHalfL + 1.6f) - B * (ChapelGen.NaveHalfW + 2f);    // beside the door, off the plinth (B > -4.3)
            if (VKit.PathGap(foot.x, foot.z) < 2.5f) return;
            foot.y = Mathf.Min(TerrainBuilder.Height(foot.x, foot.z), C.y);
            float Hh = VKit.Rnd(r, 9f, 11f), R = VKit.Rnd(r, 0.75f, 0.95f);
            int band = VillagePlan.BandOf(foot.y);
            bool survives = foot.y >= VillagePlan.IntactAbove;
            Vector3 pc = foot + Up * (Hh * 0.5f), ax = Vector3.right * (R * 1.6f), ay = Up * (Hh * 0.55f), az = Vector3.forward * (R * 1.6f);
            for (int pass = 0; pass < 2; pass++)
            {
                if (pass == 1 && band > 6) break;
                MeshBuilder mb = pass == 0 ? bushI[band] : bushR[band];
                if (pass == 1 && !survives)
                {
                    Limb(mb, foot - Up * 0.3f, foot + Up * 2.4f + B * 0.25f, 0.17f, 0.11f);       // snapped by the wave
                    break;
                }
                var cr = new System.Random(4021);                                                   // the same tree in both bands
                Limb(mb, foot - Up * 0.3f, foot + Up * 1.2f, 0.17f, 0.13f);
                const int Rings = 16;
                for (int i = 0; i < Rings; i++)
                {
                    float t0 = i / (float)Rings, t1 = (i + 1) / (float)Rings;
                    mb.Cylinder(VKit.PlantCore, foot + Up * Mathf.Lerp(0.6f, Hh, t0), foot + Up * Mathf.Lerp(0.6f, Hh, t1), SpindleR(t0, R) * 0.72f, SpindleR(t1, R) * 0.72f, 8, false);
                }
                for (float y = 0.8f; y < Hh - 0.2f; y += 0.45f)
                {
                    float rr = SpindleR((y - 0.6f) / (Hh - 0.6f), R);
                    int n = Mathf.Max(3, Mathf.RoundToInt(2f * Mathf.PI * rr / 0.42f));
                    float ph = VKit.Rnd(cr, 0f, 6.28f);
                    for (int k = 0; k < n; k++)
                    {
                        float a = ph + k * Mathf.PI * 2f / n;
                        Vector3 rad = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                        Vector3 p = foot + Up * (y + VKit.Rnd(cr, -0.15f, 0.15f)) + rad * (rr * VKit.Rnd(cr, 0.85f, 1.05f));
                        VKit.Card(p, (rad + Up * 0.25f).normalized, Mathf.Clamp(rr * 0.5f, 0.12f, 0.3f), VKit.CellShrub, cr, pc, ax, ay, az);
                    }
                }
                VKit.EmitCards(mb, VKit.LeafCard);
                if (pass == 0) nTree++;
            }
        }

        /// <summary>Radius of the cypress spindle at height fraction t (0 foot of the foliage, 1 tip).</summary>
        static float SpindleR(float t, float R)
        {
            t = Mathf.Clamp01(t);
            return t < 0.25f ? R * Mathf.Lerp(0.55f, 1f, t / 0.25f) : R * Mathf.Lerp(1f, 0.04f, Mathf.Pow((t - 0.25f) / 0.75f, 1.3f));
        }

        /// <summary>An olive on a bank of the stair-road, near the middle of the climb and near the walker's level.</summary>
        static void Olive(System.Random r)
        {
            float sA = PathDef.SQuayEnd + 20f, sB = PathDef.SNarrowStart - 8f, target = Mathf.Lerp(sA, sB, 0.45f);
            Vector3 best = Vector3.zero; float bestScore = 1e9f;
            for (float s = sA; s < sB; s += 1.5f)
            {
                Vector3 p = PathDef.Pos(s), f = PathDef.FlatDir(s), l = new Vector3(-f.z, 0f, f.x);
                for (int side = -1; side <= 1; side += 2)
                    for (float off = 4.5f; off <= 7.6f; off += 1.5f)
                    {
                        Vector3 q = p + l * (side * (PathDef.HalfWidth(s) + off));
                        float gs, gd;
                        if (!PathDef.Nearest(q.x, q.z, out gs, out gd) || gd - PathDef.HalfWidth(gs) < 3.5f) continue;
                        float y = TerrainBuilder.Height(q.x, q.z);
                        if (y < p.y - 2.5f || y > p.y + 1.2f || GroundNormal(q.x, q.z).y < 0.75f) continue;
                        if (VillagePlan.Occupied(q.x, q.z, 3f) || Blocked(q.x, q.z, gs, gd - PathDef.HalfWidth(gs), new Vector3(1e6f, 0f, 1e6f))) continue;
                        float score = Mathf.Abs(s - target) + off * 0.5f;
                        if (score < bestScore) { bestScore = score; best = new Vector3(q.x, y, q.z); }
                    }
            }
            if (bestScore > 1e8f) return;
            int band = VillagePlan.BandOf(best.y);
            Vector3 k1 = best + new Vector3(0.2f, 0.8f, -0.1f), fork = best + new Vector3(-0.1f, 1.7f, 0.15f);
            Vector3 c = best + Up * 3.1f;
            Vector3 ax = new Vector3(2f, 0f, 0f), ay = new Vector3(0f, 1.2f, 0f), az = new Vector3(0f, 0f, 1.9f);
            bool survives = best.y >= VillagePlan.IntactAbove;
            for (int pass = 0; pass < 2; pass++)
            {
                if (pass == 1 && band > 6) break;
                MeshBuilder mb = pass == 0 ? bushI[band] : bushR[band];
                Limb(mb, best - Up * 0.3f, k1, 0.24f, 0.2f);
                if (pass == 1 && !survives) { Limb(mb, k1, k1 + new Vector3(0.5f, 0.6f, 0.3f), 0.2f, 0.15f); break; }   // a broken stump
                Limb(mb, k1, fork, 0.2f, 0.16f);
                Crown(mb, c, ax, ay, az, 15, 0.62f, fork, new System.Random(5077));
                VKit.EmitCards(mb, VKit.LeafCard);
                if (pass == 0) nTree++;
            }
        }
    }
}
