using System.Collections.Generic;
using UnityEngine;

namespace CASCADE
{
    /// <summary>The seeded layout: harbour row, narrow-stair flanks, rim village, houses below the rim, cliff terraces, farms,
    /// the south hamlet and the toe of the cascade behind the harbour row.</summary>
    internal static class VillageZones
    {
        // ---- tunables ----
        public const float RowZ0 = -261.5f, RowZ1 = -30.5f;      // harbour row extent
        public const float RowFrontMin = 0.6f, RowFrontMax = 1.2f;
        public static readonly float[] Alleys = { -186f, -109f };  // gaps in the harbour row
        public const float AlleyHalf = 1.3f;
        public const float RowStairClear = 2.3f;                 // the row keeps this far from the first stair leg (room for the bank that carries it)
        public const float HarbourFloor = 0.36f;                 // harbour-row floors stand two steps above the quay
        public const int CliffTarget = 56;                       // free cliff-terrace houses (the stair flanks come on top)
        public const float CliffMaxFoundation = 13.9f;           // tall retaining walls get rows of windows and read as lower storeys
        public const float StairMaxFoundation = 24f;             // houses lining the stair-road stand on the steep banks between its legs
        public const float CliffMinXQuay = 11.5f;                // cliff houses stay behind the harbour row
        public const float ChapelZ = 40f, ChapelDist = 16.5f;    // chapel: z on the promenade and distance of the nave centre from the path
        public const float RimZEnd = 380f;
        // the hamlet on the southern cliff (seen across the harbour from the stairs, and from the headland as the wave comes)
        public const float HamletZ0 = -655f, HamletZ1 = -292f;  // z window (the village cliff ends at -276)
        public const int HamletTarget = 36;
        public const float HamletFaceWidth = 75f;               // houses stand at most this far (in x) in front of the crest
        public const float HamletMinFloor = 42f;                // ... and no lower: the shore road and the lowest scree stay bare
        public const float HamletExtraFoundation = 1.6f;        // the cliff there is meshed at 4 m: retaining walls reach deeper
        // the toe of the cascade: the slope straight behind the harbour row, which the quay walk looks up at (frames 20, 45)
        public const float ToeZ0 = -258f, ToeZ1 = -50f;          // z window of the seed houses (the stair-road's hairpins begin at -57)
        public const float ToeStepMin = 4f, ToeStepMax = 6f;     // m along z between seed attempts (taken spots simply fail)
        public const float ToeAltMin = 10f, ToeAltMax = 34f;     // a seed's terrace front stands where the ground reaches this altitude
                                                                 // (anything up to ~14 m lands on the bank right behind the row)
        public const float ToeFrontMin = 0.9f;                   // ... and at least this far uphill of the keep-out
                                                                 // (a flight or a wing reaching further out is refused, the next try moves)
        public const float ToeAltBias = 1.6f;                    // altitude = lerp(min, max, r^bias): about a third of the seeds try the bank
        public const int ToeTarget = 34;                         // at most (taken spots fail: ~27 fit, see the CASCADE_INFO toe line)
        // The cliff houses' keep-out (CliffMinXQuay, x 11.5) already stands on 16-17 m ground: the bank between the row's
        // backs (x <= 7.7) and it climbs from the quay to there (Village.QuayGround lowers it under the row), so no toe house
        // got below a ~21 m floor and a lower ToeAltMin alone changed nothing. Toe houses may step down onto that bank, so the
        // band between the row's roofline (~9 m) and the cascade (frames 20, 45) shows white foundations with cave windows:
        public const float ToeBankX = 8.1f;                      // their keep-out (the row's backs end by x 7.2-7.7; Accept keeps 0.2 m)
        public const float ToeBankFoundation = 16.5f;            // m: their retaining wall, measured down to the bank as drawn (lowered)
        public const int ToeZone = 6;                            // HouseDef.zone of the toe houses: built and ruined like cliff
                                                                 // terraces (zone 1), but HouseGen's rooftop-church, cafe and wreck
                                                                 // picks only take zones 1, 3 and 4, so the quay church look keeps its house
                                                                 // (ToeFlight and RimClusters use it too, for the same reason)
        // The first flight (92-104 s: leg 1 heads south, the R(-22, 30) cue looks up the slope across z -60..-82 above the
        // hairpin's bank box). Toe's houses there stop at floors ~36 m, so a small stepped stack of its own goes above them: a
        // seed whose terrace front stands where the ground reaches ToeFlightAlt, houses climbing right above it, a neighbour.
        // Measured on the plan (CPU replay of this class): fronts under ~26 m find no site clear of the hairpin and the
        // stair-road's roof rule; the stack lands at floors 39-61 m, x 27-39, z -66..-81, over x 580-890, y 160-660 of the
        // 100 s frame. It hides the dome of one walk church (the cliff house at z -40) from the opening eye, so
        // HouseGen.SelectChurches takes the stair-road house 14 m north of it instead (30-90 px away in the 26-50 s frames).
        public const float ToeFlightZ0 = -80f, ToeFlightZ1 = -66f;       // z window of the seed
        public const float ToeFlightAlt0 = 26f, ToeFlightAlt1 = 40f;     // its terrace front altitude: the frame's centre-left
        public const int ToeFlightClimb = 3, ToeFlightMax = 5;           // houses stacked above the seed; houses in all
        // Below the promenade (440-476 s): the five lone houses there (BelowRim's zs 86..348) hang on tall walls at a regular
        // pitch and read as teeth. Each grows a few neighbours below and beside it (cliff rules, CliffMaxFoundation).
        public const int RimClusterMin = 2, RimClusterMax = 4;           // neighbours per lone house (GrowDown may place fewer)
        public const float RimStepDown0 = 2f, RimStepDown1 = 8f;         // m: a neighbour beside one of the cluster stands this much lower

        static readonly List<HouseDef> rimSeeds = new List<HouseDef>(5);

        public static void Build(int seed)
        {
            VillagePlan.Houses.Clear();
            rimSeeds.Clear();
            var rng = new System.Random(seed);
            SiteChapel();
            HarbourRow(rng);
            StairFlanks(rng);
            Rim(rng);
            BelowRim(rng);
            Cliff(rng);
            Farms(rng);
            HamletSouth(new System.Random(seed + 101));       // own stream, appended last: the plan above is unchanged
            Toe(new System.Random(seed + 211));               // likewise
            ToeFlight(new System.Random(seed + 229));         // likewise: Toe's houses (frames 20, 45) stay where they are
            RimClusters(new System.Random(seed + 233));       // likewise
        }

        static float ChainageAtZ(float z, float from)
        {
            float s = from;
            while (s < PathDef.Length && PathDef.Pos(s).z < z) s += 0.5f;
            return s;
        }

        static Vector3 Right(float s) { Vector3 d = PathDef.FlatDir(s); return new Vector3(d.z, 0f, -d.x); }

        static void SiteChapel()
        {
            float s = ChainageAtZ(ChapelZ, PathDef.SStairEnd);
            Vector3 p = PathDef.Pos(s), r = Right(s);
            Vector3 c = p + r * ChapelDist;
            float hi = -1e9f;
            for (int i = -2; i <= 2; i++)
                for (int j = -1; j <= 1; j++)
                    hi = Mathf.Max(hi, VKit.Ground(c.x + r.x * i * 2.6f - r.z * j * 3.4f, c.z + r.z * i * 2.6f + r.x * j * 3.4f));
            c.y = hi + 0.12f;
            VillagePlan.ChapelC = c; VillagePlan.ChapelAxis = r;
        }

        static void HarbourRow(System.Random rng)
        {
            float z = RowZ0; int n = 0;
            int cafeA = 3 + rng.Next(2), cafeB = 9 + rng.Next(2);
            while (z < RowZ1 - 4.6f && n < 17)
            {
                float limit = RowZ1;
                bool jumped = false;
                for (int i = 0; i < Alleys.Length; i++)
                {
                    if (z >= Alleys[i] + AlleyHalf) continue;
                    if (Alleys[i] - AlleyHalf - z < 4.8f) { z = Alleys[i] + AlleyHalf; jumped = true; }
                    else limit = Mathf.Min(limit, Alleys[i] - AlleyHalf);
                    break;
                }
                if (jumped) continue;
                float room = limit - z;
                float wMain = VKit.Rnd(rng, 7.5f, 9f), wWing = VKit.Rnd(rng, 6f, 8.6f);
                bool wing = !VKit.Chance(rng, 0.08f);
                if (wMain + (wing ? wWing : 0f) > room)
                {
                    if (room >= 10.8f) { wMain = Mathf.Min(wMain, room - 4.6f); wWing = room - wMain; wing = true; }
                    else { wing = false; wMain = Mathf.Min(9f, room); }
                }
                else if (room - wMain - (wing ? wWing : 0f) < 4.8f)
                {
                    // do not leave a sliver before the alley or the end of the row
                    float extra = room - wMain - (wing ? wWing : 0f);
                    if (wing) wWing += extra; else wMain = Mathf.Min(9f, wMain + extra);
                }
                int side = VKit.Chance(rng, 0.5f) ? 1 : -1;               // S = +z here: +1 puts the wing north of the main box
                float zc = side > 0 || !wing ? z + wMain * 0.5f : z + wWing + wMain * 0.5f;
                float xf = VKit.Rnd(rng, RowFrontMin, RowFrontMax), depth = VKit.Rnd(rng, 5f, 6f);
                HouseDef h = VillagePlan.New(rng, 0, new Vector3(xf + depth * 0.5f, Config.QuayHeight + HarbourFloor, zc), Vector3.left, wMain, depth);
                h.storeys = 2; h.storeyH = VKit.Rnd(rng, 2.9f, 3.15f);
                if (wing)
                {
                    VillagePlan.AddWing(h, rng, wWing, depth * VKit.Rnd(rng, 0.82f, 1f), VKit.Chance(rng, 0.22f) ? 1 : 2, side);
                    h.wingFront = Mathf.Clamp(VKit.Rnd(rng, -0.45f, 0.35f), xf - RowFrontMax, xf - RowFrontMin);
                }
                h.cafe = n == cafeA || n == cafeB;
                h.awning = h.cafe || VKit.Chance(rng, 0.22f);
                h.plainSides = true;
                // the first stair leg runs right behind the northern houses: make them shallower until they clear it
                bool ok = false;
                for (int guard = 0; guard < 16 && !ok; guard++)
                {
                    ok = VillagePlan.Accept(h, false, false, -0.2f, VillagePlan.MaxFoundation, RowStairClear);
                    if (ok) break;
                    depth -= 0.3f;
                    if (depth < 2.2f) break;
                    h.hd = depth * 0.5f; h.c.x = xf + h.hd;
                    h.wingHd = Mathf.Min(h.wingHd, h.hd + Mathf.Min(h.wingFront, 0f) * 0.5f);      // the wing never reaches further back than the main box
                }
                if (ok) { VillagePlan.Commit(h); n++; }
                z += wMain + (wing ? wWing : 0f) + VKit.Rnd(rng, 0f, 0.35f);
            }
        }

        /// <summary>House masses directly behind the walls of the narrow stair: their upper storeys show above the walls.</summary>
        static void StairFlanks(System.Random rng)
        {
            float zA = PathDef.Pos(PathDef.SNarrowStart).z + 1.5f, zB = PathDef.Pos(PathDef.SStairEnd).z - 4.5f;
            float cap = PathDef.Pos(PathDef.SStairEnd).y - VillagePlan.RoofBelowProm;      // west flank must not block the vista at the top
            for (int side = 1; side >= -1; side -= 2)
            {
                float z = zA + (side > 0 ? 0f : 2.4f);
                while (z + 5.4f < zB)
                {
                    float len = Mathf.Min(VKit.Rnd(rng, 5.6f, 7.2f), zB - z), depth = VKit.Rnd(rng, 4.6f, 5.6f);
                    float zc = z + len * 0.5f, s, d;
                    PathDef.Nearest(74f, zc, out s, out d);
                    Vector3 pc = PathDef.Pos(s);
                    HouseDef h = VillagePlan.New(rng, 2, new Vector3(0f, pc.y + 1f, zc), new Vector3(-side, 0f, 0f), len, depth);
                    h.storeys = 2; h.noGroundOpenings = true; h.plainSides = true; h.roofBox = side > 0 && VKit.Chance(rng, 0.35f);
                    h.backWindows = side < 0;          // the west flank's back is the face the caldera sees
                    bool ok = false;
                    for (float off = 2.75f; off < 4.2f && !ok; off += 0.2f)
                    {
                        h.c.x = pc.x + side * (off + depth * 0.5f);
                        VillagePlan.Seat(h, false);
                        if (side < 0 && h.roofTop > cap) { h.storeys = 1; VillagePlan.Seat(h, false); }
                        if (side < 0 && h.roofTop > cap) { h.c.y -= h.roofTop - cap; }
                        ok = VillagePlan.Accept(h, false, false, 0.05f, 16.4f);
                    }
                    if (ok) VillagePlan.Commit(h);
                    z += len + VKit.Rnd(rng, 0.25f, 0.7f);
                }
            }
        }

        static void Dress(HouseDef h, System.Random rng, float wingChance, float twoStoreyChance)
        {
            h.storeys = VKit.Chance(rng, twoStoreyChance) ? 2 : 1;
            if (VKit.Chance(rng, wingChance)) VillagePlan.AddWing(h, rng, VKit.Rnd(rng, 3.6f, 5.6f), h.hd * 2f * VKit.Rnd(rng, 0.72f, 1f), 1);
            if (h.storeys == 1)
            {
                if (h.wingSide == 0 && VKit.Chance(rng, 0.3f)) h.vault = true;
                else if (VKit.Chance(rng, 0.22f)) h.roofBox = true;
                if (!h.vault && VKit.Chance(rng, 0.38f)) h.stairSide = h.wingSide != 0 ? -h.wingSide : (VKit.Chance(rng, 0.5f) ? 1 : -1);
            }
            else if (h.wingSide != 0 && VKit.Chance(rng, 0.3f)) h.wingVault = true;
        }

        static void Rim(System.Random rng)
        {
            float s = PathDef.SStairEnd + 5f;
            while (s < PathDef.SPromEnd)
            {
                Vector3 p = PathDef.Pos(s);
                if (p.z > RimZEnd) break;
                float width = VKit.Rnd(rng, 5.2f, 9f), depth = VKit.Rnd(rng, 4.6f, 6.4f);
                float used = TryRim(rng, s, width, depth, VKit.Rnd(rng, 3f, 10f));
                if (VKit.Chance(rng, 0.3f)) TryRim(rng, s + VKit.Rnd(rng, -2f, 4f), VKit.Rnd(rng, 5.2f, 8f), VKit.Rnd(rng, 4.6f, 6f), VKit.Rnd(rng, 17f, 27f));
                s += (used > 0f ? used : 4f) + VKit.Rnd(rng, 2f, 6f);
            }
        }

        static float TryRim(System.Random rng, float s, float width, float depth, float setback)
        {
            s += width * 0.5f;
            Vector3 p = PathDef.Pos(s), r = Right(s);
            HouseDef h = VillagePlan.New(rng, 3, p + r * (PathDef.HalfWidth(s) + setback + depth * 0.5f), -r, width, depth);
            Dress(h, rng, 0.42f, 0.5f);
            if (setback > 5f && VKit.Chance(rng, 0.35f)) h.apron = VKit.Rnd(rng, 1.2f, 2f);
            h.awning = VKit.Chance(rng, 0.1f);
            if (!VillagePlan.Accept(h, true, false)) return 0f;
            VillagePlan.Commit(h);
            return width + (h.wingSide != 0 ? h.wingHw * 2f : 0f) + (h.stairSide != 0 ? 1f : 0f);
        }

        static void BelowRim(System.Random rng)
        {
            // the first five sit under the belvedere and give the vista its white roofs; the rest line the promenade
            float[] zs = { 2f, 15f, 28f, 41f, 55f, 86f, 150f, 215f, 282f, 348f };
            for (int i = 0; i < zs.Length; i++)
            {
                float s = ChainageAtZ(zs[i] + VKit.Rnd(rng, -5f, 5f) * (i < 5 ? 0.3f : 1.8f), PathDef.SStairEnd);
                Vector3 p = PathDef.Pos(s), r = Right(s);
                // wide and shallow: they hang on the steepest part of the cliff and must not read as towers
                float width = VKit.Rnd(rng, 6f, 8.5f), depth = VKit.Rnd(rng, 3.4f, 3.9f);
                for (float off = PathDef.HalfWidth(s) + 2.2f; off < 16f; off += 1f)
                {
                    HouseDef h = VillagePlan.New(rng, 4, p - r * (off + depth * 0.5f), -r, width, depth);
                    h.apron = VKit.Rnd(rng, 0.8f, 1.2f);
                    h.lowerWindows = true;
                    if (i >= 5 || VKit.Chance(rng, 0.4f)) VillagePlan.AddWing(h, rng, VKit.Rnd(rng, 4.2f, 5.8f), depth * 0.9f, 1);
                    h.wingFront = Mathf.Min(h.wingFront, 0f);
                    if (VKit.Chance(rng, 0.3f) && h.wingSide == 0) h.vault = true;
                    if (VillagePlan.Accept(h, true, true, 0.7f, 17.9f))
                    {
                        VillagePlan.Commit(h);
                        if (i >= 5) rimSeeds.Add(h);              // the lone ones along the promenade: RimClusters grows them (no draw)
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Cliff terraces. First the stair-road is lined with houses: each stands on a tall terrace wall right at the
        /// uphill side of a leg, and its roof lies below the next leg up, so the walker there looks west over it.
        /// Then clusters grow: a seed house and neighbours stacked right above, below or beside it, so the village
        /// reads as white masses, not as dots.
        /// </summary>
        static void Cliff(System.Random rng)
        {
            var cluster = new List<HouseDef>(12);
            int placed = 0;
            // ---- the strips between the legs of the stair-road (x = 8, 30, 52 and the narrow stair at 74; z from -46 to -22):
            //      houses standing right at the lower leg (a tall terrace wall beside the walker); their roofs lie
            //      below the upper leg, so the walker there looks west over them ----
            float zHi = PathDef.Pos(PathDef.SQuayEnd).z, zLo = zHi - 24f;
            float edge = PathDef.HalfWidth(PathDef.SQuayEnd + 40f) + VillagePlan.HouseClear + 0.15f;
            var lining = new List<HouseDef>(8);
            for (int strip = 0; strip < 3; strip++)
                for (int slot = 0; slot < 2; slot++)
                    for (int k = 0; k < 6; k++)
                    {
                        // later attempts move towards the middle of the leg, away from the hairpins
                        float z = Mathf.Lerp(zLo, zHi, Mathf.Lerp(slot == 0 ? 0.24f : 0.76f, 0.5f, k * 0.12f)) + VKit.Rnd(rng, -1.2f, 1.2f);
                        HouseDef h = TryCliff(rng, z, 8f + 22f * strip + edge, true, -90f, StairMaxFoundation, 0f, true);
                        if (h == null) continue;
                        lining.Add(h); placed++;
                        break;
                    }
            for (int i = 0; i < lining.Count; i++)
            {
                cluster.Clear(); cluster.Add(lining[i]);
                placed += Grow(rng, cluster, 1 + rng.Next(2), -90f, CliffTarget - placed, StairMaxFoundation);
            }
            // ---- free clusters: z, altitude of the seed house. Central ones around the stair-road alternate with ones along
            //      the cliff above the quay, so that the whole walk along the harbour has the village over it ----
            var seeds = new List<Vector2>(32);
            float[] fix =
            {
                -70f, 30f, -135f, 24f, -3f, 28f, -188f, 22f, -73f, 64f, -160f, 40f, 3f, 70f, -215f, 34f, 28f, 48f, -243f, 20f,
                -104f, 44f, -150f, 66f, 42f, 90f, -205f, 62f, -64f, 92f, -240f, 50f, -98f, 80f
            };
            for (int i = 0; i < fix.Length; i += 2) seeds.Add(new Vector2(fix[i], fix[i + 1]));
            for (int i = 0; i < 6; i++)
                seeds.Add(new Vector2(i % 2 == 0 ? VKit.Rnd(rng, -150f, 55f) : VKit.Rnd(rng, -268f, -125f), VKit.Rnd(rng, 15f, 97f)));
            for (int si = 0; si < seeds.Count && placed < CliffTarget; si++)
            {
                cluster.Clear();
                float yaw = -90f + VKit.Rnd(rng, -10f, 10f);
                for (int k = 0; k < 4 && cluster.Count == 0; k++)
                {
                    float z = seeds[si].x + (k == 0 ? 0f : VKit.Rnd(rng, -9f, 9f));
                    float xh = FindX(seeds[si].y + (k == 0 ? 0f : VKit.Rnd(rng, -4f, 4f)), z);
                    if (xh < 0f) continue;
                    HouseDef h = TryCliff(rng, z, xh, false, yaw, CliffMaxFoundation, 0.4f, false);
                    if (h != null) cluster.Add(h);
                }
                if (cluster.Count == 0) continue;
                placed++;
                placed += Grow(rng, cluster, 1 + rng.Next(4), yaw, CliffTarget - placed, CliffMaxFoundation);
            }
        }

        /// <summary>Adds up to want neighbours (at most room) to a cluster; returns how many were placed.</summary>
        static int Grow(System.Random rng, List<HouseDef> cluster, int want, float yaw, int room, float maxFoundation)
        {
            int added = 0;
            for (int tries = 0; added < want && added < room && tries < want * 6 + 6; tries++)
            {
                HouseDef m = cluster[rng.Next(cluster.Count)];
                int way = rng.Next(4);
                HouseDef n;
                if (way == 0)            // right above m
                    n = TryCliff(rng, m.c.z + VKit.Rnd(rng, -0.6f, 0.6f) * m.hw, m.c.x + m.hd + VKit.Rnd(rng, 0.2f, 0.8f), true, yaw, maxFoundation, 0.4f, false);
                else if (way == 1)       // right below m
                    n = TryCliff(rng, m.c.z + VKit.Rnd(rng, -0.6f, 0.6f) * m.hw, m.c.x - m.hd - m.apron - m.reach - VKit.Rnd(rng, 0.2f, 0.8f), false, yaw, maxFoundation, 0.4f, false);
                else                     // beside m, on the same level
                {
                    float z = m.c.z + (way == 2 ? 1f : -1f) * (Mathf.Max(-m.pS0, m.pS1) + VKit.Rnd(rng, 3.4f, 6.2f));
                    float xh = FindX(m.c.y + VKit.Rnd(rng, -1.6f, 1.6f), z);
                    n = xh < 0f ? null : TryCliff(rng, z, xh, false, yaw, maxFoundation, 0.4f, false);
                }
                if (n == null) continue;
                cluster.Add(n); added++;
            }
            return added;
        }

        // while the hamlet is planned, TryCliff and FindX use the southern cliff's window instead of the village's
        static bool hamlet;
        // while the toe is planned, the keep-out behind the harbour row moves down onto the bank (ToeBankX)
        static bool toe;
        // while the lone houses below the promenade grow, cliff houses may stand north of the stair-road's z window (up to RimZEnd)
        static bool rimGrow;
        const float CrestZ0 = HamletZ0 - 20f, CrestStep = 5f;
        static float[] crestX;                                  // crest of the southern cliff, x per z sample

        static float XMin(float z) { return hamlet ? CrestX(z) - HamletFaceWidth : z < -12f ? (toe ? ToeBankX : CliffMinXQuay) : -4f; }
        static float XMax(float z) { return hamlet ? CrestX(z) + 4f : 80f; }

        /// <summary>First x (going uphill) where the ground reaches the altitude, or -1.</summary>
        static float FindX(float alt, float z)
        {
            for (float x = XMin(z) + 4f; x <= XMax(z) - 2f; x += 0.5f)
                if (VKit.Ground(x, z) >= alt) return x;
            return -1f;
        }

        static float CrestX(float z)
        {
            float f = Mathf.Clamp((z - CrestZ0) / CrestStep, 0f, crestX.Length - 1.001f);
            int i = (int)f;
            return Mathf.Lerp(crestX[i], crestX[i + 1], f - i);
        }

        /// <summary>
        /// A hamlet of about 35 houses on the southern cliff, in clusters stacked on its face below the crest (the cliff
        /// houses' rules, its own z window and stream). The houses join the altitude bands like any other, so they are
        /// ruined with their band when the wave comes.
        /// </summary>
        static void HamletSouth(System.Random rng)
        {
            // the crest: the highest ground across the cliff top, smoothed along z so that clusters line up with it
            int n = Mathf.CeilToInt((HamletZ1 + 20f - CrestZ0) / CrestStep) + 1;
            var raw = new float[n];
            for (int k = 0; k < n; k++)
            {
                float z = CrestZ0 + k * CrestStep, best = -1e9f;
                for (float x = -60f; x <= 150f; x += 1f)
                {
                    float g = VKit.Ground(x, z);
                    if (g > best) { best = g; raw[k] = x; }
                }
            }
            crestX = new float[n];
            for (int k = 0; k < n; k++)
            {
                float s = 0f; int c = 0;
                for (int q = -3; q <= 3; q++) { s += raw[Mathf.Clamp(k + q, 0, n - 1)]; c++; }
                crestX[k] = s / c;
            }

            hamlet = true;
            var cluster = new List<HouseDef>(8);
            int placed = 0;
            for (float z = HamletZ1 - 12f; z > HamletZ0 + 6f && placed < HamletTarget; z -= VKit.Rnd(rng, 34f, 50f))
            {
                // face the caldera square to the local slope (the cliff swings from west to south-west along the hamlet)
                Vector3 nrm = WorldGen.Normal(CrestX(z) - 30f, z, 8f);
                float yaw = Mathf.Clamp(Mathf.Atan2(nrm.x, nrm.z) * Mathf.Rad2Deg, -125f, -70f);
                float crestY = VKit.Ground(CrestX(z), z);
                cluster.Clear();
                for (int k = 0; k < 5 && cluster.Count == 0; k++)
                {
                    float zz = z + (k == 0 ? 0f : VKit.Rnd(rng, -9f, 9f));
                    float xh = FindX(Mathf.Lerp(HamletMinFloor + 4f, crestY - 7f, VKit.Rnd(rng, 0.15f, 1f)), zz);
                    if (xh < 0f) continue;
                    HouseDef h = TryCliff(rng, zz, xh, false, yaw, CliffMaxFoundation, 0.3f, false);
                    if (h != null) cluster.Add(h);
                }
                if (cluster.Count == 0) continue;
                placed++;
                placed += Grow(rng, cluster, 2 + rng.Next(4), yaw, HamletTarget - placed, CliffMaxFoundation);
            }
            hamlet = false;
        }

        /// <summary>
        /// The toe of the cascade: houses on the slope straight behind the harbour row, so that the white cascade reaches
        /// down to the quay instead of stopping above a bare 10-30 m band of earth (frames 20 and 45). The free clusters
        /// never land there: FindX anchors a house's uphill edge at least 4 m inside the keep-out, so a low house's terrace
        /// always crossed CliffMinXQuay and was refused. Here a seed's terrace FRONT is put where the ground reaches a
        /// chosen altitude (the lowest ones on the bank right behind the row, ToeBankX), and each seed gets two or three
        /// neighbours stacked above or beside it. Spots already built on simply fail. Own stream, appended last: every
        /// house above stays put.
        /// </summary>
        static void Toe(System.Random rng)
        {
            var cluster = new List<HouseDef>(6);
            int first = VillagePlan.Houses.Count, placed = 0;
            toe = true;
            for (float z = ToeZ0; z < ToeZ1 && placed < ToeTarget; z += VKit.Rnd(rng, ToeStepMin, ToeStepMax))
            {
                float yaw = -90f + VKit.Rnd(rng, -6f, 6f);
                cluster.Clear();
                for (int k = 0; k < 4 && cluster.Count == 0; k++)
                {
                    float zz = z + (k == 0 ? 0f : VKit.Rnd(rng, -3f, 3f));
                    // biased low: only altitudes up to ~14 m reach the bank, the band the quay looks at
                    float front = FrontAt(Mathf.Lerp(ToeAltMin, ToeAltMax, Mathf.Pow((float)rng.NextDouble(), ToeAltBias)), zz);
                    if (front < 0f) continue;
                    HouseDef h = TryCliff(rng, zz, front, true, yaw, CliffMaxFoundation, 0.4f, false);
                    if (h != null) cluster.Add(h);
                }
                if (cluster.Count == 0) continue;
                placed++;
                placed += Grow(rng, cluster, 2 + rng.Next(2), yaw, ToeTarget - placed, CliffMaxFoundation);
            }
            toe = false;
            // where they landed: the quay look at 45 needs them at z -150..-60, floors 10-35 m
            int inLook = 0;
            float zMin = 1e9f, zMax = -1e9f, yMin = 1e9f, yMax = -1e9f;
            for (int i = first; i < VillagePlan.Houses.Count; i++)
            {
                HouseDef h = VillagePlan.Houses[i];
                h.zone = ToeZone;
                if (h.c.z > -150f && h.c.z < -60f) inLook++;
                zMin = Mathf.Min(zMin, h.c.z); zMax = Mathf.Max(zMax, h.c.z); yMin = Mathf.Min(yMin, h.c.y); yMax = Mathf.Max(yMax, h.c.y);
            }
            Debug.Log("CASCADE_INFO toe houses " + placed + " (" + inLook + " at z -150..-60), z " + zMin.ToString("F0") + ".." + zMax.ToString("F0")
                      + ", floors " + yMin.ToString("F1") + ".." + yMax.ToString("F1") + " m");
        }

        /// <summary>
        /// The stack over the first flight (frame 100): the slope the 92-104 s look climbs (z -60..-82, above the hairpin's
        /// bank box) held Toe's houses only up to floors ~36 m, below the frame. A seed on the toe rules (terrace front at
        /// ToeFlightAlt), houses each standing right behind and above the last one, then neighbours beside or below. Own stream.
        /// </summary>
        static void ToeFlight(System.Random rng)
        {
            int first = VillagePlan.Houses.Count;
            var cl = new List<HouseDef>(ToeFlightMax);
            toe = true;
            HouseDef top = null;
            for (int k = 0; k < 24 && top == null; k++)
            {
                float zz = VKit.Rnd(rng, ToeFlightZ0, ToeFlightZ1);
                float front = FrontAt(VKit.Rnd(rng, ToeFlightAlt0, ToeFlightAlt1), zz);
                if (front < 0f) continue;
                top = TryCliff(rng, zz, front, true, -90f + VKit.Rnd(rng, -6f, 6f), CliffMaxFoundation, 0.4f, false);
            }
            if (top != null)
            {
                cl.Add(top);
                // climb: each terrace starts just uphill of the back of the house below (its roof is the next one's forecourt)
                for (int k = 0; k < ToeFlightClimb * 5 && cl.Count < 1 + ToeFlightClimb; k++)
                {
                    HouseDef n = TryCliff(rng, top.c.z + VKit.Rnd(rng, -0.5f, 0.5f) * top.hw, BackX(top) + VKit.Rnd(rng, 0.2f, 0.8f), true,
                                          -90f, CliffMaxFoundation, 0.4f, false);
                    if (n == null) continue;
                    cl.Add(n); top = n;
                }
                // a neighbour beside or below: a mass, not a single file
                if (cl.Count < ToeFlightMax) Grow(rng, cl, ToeFlightMax - cl.Count, -90f, ToeFlightMax - cl.Count, CliffMaxFoundation);
            }
            toe = false;
            float yMin = 1e9f, yMax = -1e9f;
            for (int i = first; i < VillagePlan.Houses.Count; i++)
            {
                HouseDef h = VillagePlan.Houses[i];
                h.zone = ToeZone;
                yMin = Mathf.Min(yMin, h.c.y); yMax = Mathf.Max(yMax, h.c.y);
            }
            Debug.Log("CASCADE_INFO toe flight " + (VillagePlan.Houses.Count - first) + " houses over the first flight"
                      + (VillagePlan.Houses.Count > first ? ", floors " + yMin.ToString("F1") + ".." + yMax.ToString("F1") + " m" : " (none fit: widen ToeFlightAlt)"));
        }

        /// <summary>Highest x of the back edge of a house's platform (the uphill side of a west-facing house).</summary>
        static float BackX(HouseDef h)
        {
            return Mathf.Max(h.At(h.pS0, 0f, h.pF0).x, h.At(h.pS1, 0f, h.pF0).x);
        }

        /// <summary>
        /// The lone houses below the promenade (BelowRim after the first five, 64 m apart on 14-18 m walls) read as a row of
        /// teeth from the headland (440-476 s). Each grows 2-4 neighbours below and beside it on the cliff rules, so it
        /// becomes a small white mass hanging off the promenade. They take ToeZone: built and ruined as cliff houses, but out
        /// of HouseGen's church and cafe picks (as zone 1 or 4, two of them took walk-church slots from the quay frames and
        /// one could take the promenade cafe). Own stream, appended last.
        /// </summary>
        static void RimClusters(System.Random rng)
        {
            int first = VillagePlan.Houses.Count;
            var cl = new List<HouseDef>(1 + RimClusterMax);
            rimGrow = true;
            for (int i = 0; i < rimSeeds.Count; i++)
            {
                HouseDef s = rimSeeds[i];
                cl.Clear(); cl.Add(s);
                int want = RimClusterMin + rng.Next(RimClusterMax - RimClusterMin + 1);
                GrowDown(rng, cl, want, Mathf.Atan2(s.F.x, s.F.z) * Mathf.Rad2Deg);
            }
            rimGrow = false;
            for (int i = first; i < VillagePlan.Houses.Count; i++) VillagePlan.Houses[i].zone = ToeZone;
            Debug.Log("CASCADE_INFO rim clusters: " + (VillagePlan.Houses.Count - first) + " houses round " + rimSeeds.Count + " lone houses below the promenade");
        }

        /// <summary>
        /// Grow for the top of the cliff: nothing fits above a house that hangs off the promenade, and beside it at the same
        /// level the cliff is too steep for a CliffMaxFoundation wall (that is why the seeds stand on 14-18 m walls). So a
        /// neighbour goes right below one of the cluster, or beside it a step down (RimStepDown), where the cliff eases:
        /// the mass widens as it descends instead of hanging as a column.
        /// </summary>
        static int GrowDown(System.Random rng, List<HouseDef> cl, int want, float yaw)
        {
            int added = 0;
            for (int tries = 0; added < want && tries < want * 8 + 8; tries++)
            {
                HouseDef m = cl[rng.Next(cl.Count)];
                HouseDef n;
                if (rng.Next(3) == 0)        // right below m: its back wall just downhill of m's terrace front
                    n = TryCliff(rng, m.c.z + VKit.Rnd(rng, -0.7f, 0.7f) * m.hw, FrontX(m) - VKit.Rnd(rng, 0.2f, 0.8f), false, yaw, CliffMaxFoundation, 0.3f, false);
                else                         // beside m, a step down
                {
                    float z = m.c.z + (rng.Next(2) == 0 ? 1f : -1f) * (Mathf.Max(-m.pS0, m.pS1) + VKit.Rnd(rng, 3.6f, 6f));
                    float xh = FindX(m.c.y - VKit.Rnd(rng, RimStepDown0, RimStepDown1), z);
                    n = xh < 0f ? null : TryCliff(rng, z, xh, false, yaw, CliffMaxFoundation, 0.3f, false);
                }
                if (n == null) continue;
                cl.Add(n); added++;
            }
            return added;
        }

        /// <summary>Lowest x of the front edge of a house's platform and flight (the downhill side of a west-facing house).</summary>
        static float FrontX(HouseDef h)
        {
            return Mathf.Min(h.At(h.pS0, 0f, h.pF1 + h.reach).x, h.At(h.pS1, 0f, h.pF1 + h.reach).x);
        }

        /// <summary>
        /// Takes storeys off toe houses (ToeZone) whose block cuts the sight line eye -> target (a roof room first, never
        /// below one storey), so a dome chosen after the plan is not hidden by a toe house standing in front of it on the
        /// quay walk (frame 20: the hero dome behind two 2-storey toe houses). No random draw, footprints unchanged. Call
        /// before the houses are built; returns how many houses were lowered.
        /// </summary>
        internal static int ClearToeSight(Vector3 eye, Vector3 target)
        {
            int lowered = 0;
            for (int i = 0; i < VillagePlan.Houses.Count; i++)
            {
                HouseDef h = VillagePlan.Houses[i];
                if (h.zone != ToeZone || !Cuts(h, eye, target)) continue;
                bool any = false;
                while (Cuts(h, eye, target))
                {
                    if (h.roofBox) h.roofBox = false;
                    else if (h.storeys > 1) h.storeys--;
                    else if (h.wingSide != 0 && h.wingStoreys > 1) h.wingStoreys--;
                    else break;
                    if (h.wingStoreys > h.storeys) h.wingStoreys = h.storeys;
                    RoofTop(h);
                    any = true;
                }
                if (any) lowered++;
            }
            return lowered;
        }

        /// <summary>True when the segment eye -> target passes through the house: its main box or wing up to their roof lines,
        /// or its terrace up to the parapet. Sampled every 0.5 m; the last 3 m are the target's own roof.</summary>
        static bool Cuts(HouseDef h, Vector3 eye, Vector3 target)
        {
            Vector3 d = target - eye;
            float len = d.magnitude;
            int n = Mathf.CeilToInt(len / 0.5f);
            float main = h.c.y + MainTop(h), wing = h.c.y + WingTop(h), terrace = h.c.y + VWalls.ParapetH;
            for (int k = 1; k < n; k++)
            {
                float t = k / (float)n;
                if (len * (1f - t) < 3f) break;
                Vector3 p = eye + d * t;
                if (p.y > h.roofTop) continue;
                float dx = p.x - h.c.x, dz = p.z - h.c.z;
                float s = dx * h.S.x + dz * h.S.z, f = dx * h.F.x + dz * h.F.z;
                if (p.y < main && s > -h.hw && s < h.hw && f > -h.hd && f < h.hd) return true;
                if (h.wingSide != 0 && p.y < wing && Mathf.Abs(s - h.WingS) < h.wingHw && Mathf.Abs(f - h.WingF) < h.wingHd) return true;
                if (p.y < terrace && s > h.pS0 && s < h.pS1 && f > h.pF0 && f < h.pF1 + h.reach) return true;
            }
            return false;
        }

        // roof lines above the floor, as VillagePlan.Seat computes them
        static float MainTop(HouseDef h)
        {
            return h.MainH + (h.vault ? Mathf.Min(Mathf.Min(h.hd, h.hw), 2.6f) * 0.62f : VWalls.ParapetH) + (h.roofBox ? 2.3f - VWalls.ParapetH : 0f);
        }

        static float WingTop(HouseDef h)
        {
            return h.wingSide == 0 ? 0f : h.WingH + (h.wingVault ? Mathf.Min(Mathf.Min(h.wingHd, h.wingHw), 2.6f) * 0.62f : VWalls.ParapetH);
        }

        /// <summary>The roof line as VillagePlan.Seat sets it, leaving the floor and the foundation as seated.</summary>
        static void RoofTop(HouseDef h) { h.roofTop = h.c.y + Mathf.Max(MainTop(h), WingTop(h)); }

        /// <summary>First x, going uphill from ToeFrontMin behind the keep-out, where the ground function reaches alt (the
        /// keep-out itself when the ground there is already higher), or -1. Outside the toe pass the keep-out is the cliff
        /// houses' (CliffMinXQuay); inside it, the bank's (ToeBankX). Internal: other generators may lay things by it.</summary>
        internal static float FrontAt(float alt, float z)
        {
            for (float x = XMin(z) + ToeFrontMin; x <= XMax(z) - 2f; x += 0.5f)
                if (VKit.Ground(x, z) >= alt) return x;
            return -1f;
        }

        /// <summary>
        /// One cliff house. anchorX is its uphill edge, or (anchorIsFront) the downhill edge of its terrace.
        /// lining = it is placed against the stair-road: square to it, and free of the keep-out behind the harbour row.
        /// </summary>
        static HouseDef TryCliff(System.Random rng, float z, float anchorX, bool anchorIsFront, float yaw, float maxFoundation, float flightChance, bool lining)
        {
            if (hamlet ? z < HamletZ0 || z > HamletZ1 : z < -276f || z > (rimGrow ? RimZEnd : 62f)) return null;
            float width = VKit.Rnd(rng, 5f, 8.6f), depth = VKit.Rnd(rng, 3.6f, 4.6f), apron = VKit.Rnd(rng, 0.9f, 1.7f);
            float cx = anchorIsFront ? anchorX + apron + depth * 0.5f : anchorX - depth * 0.5f;
            HouseDef h = VillagePlan.New(rng, 1, new Vector3(cx, 0f, z), VKit.Dir(yaw + (lining ? 0f : VKit.Rnd(rng, -4f, 4f))), width, depth);
            Dress(h, rng, 0.5f, 0.36f);
            h.apron = apron;
            h.lowerWindows = true;
            if (VKit.Chance(rng, flightChance)) { h.frontStair = VKit.Chance(rng, 0.5f) ? 1 : -1; h.reach = 0.95f; }
            VillagePlan.Platform(h);
            float west = Mathf.Min(h.At(h.pS0, 0f, h.pF1 + h.reach).x, h.At(h.pS1, 0f, h.pF1 + h.reach).x);
            float east = Mathf.Max(h.At(h.pS0, 0f, h.pF0).x, h.At(h.pS1, 0f, h.pF0).x);
            if ((!lining && west < XMin(z)) || east > XMax(z)) return null;
            if (!VillagePlan.Accept(h, true, true, 0.2f, maxFoundation)) return null;
            if (hamlet)
            {
                if (h.c.y < HamletMinFloor) return null;
                h.baseY -= HamletExtraFoundation;
            }
            if (toe && !SeatOnBank(h)) return null;
            VillagePlan.Commit(h);
            return h;
        }

        /// <summary>
        /// A toe house reaching onto the bank behind the harbour row: Seat measured its foundation against the ground
        /// function, but the terrain is drawn lowered there (Village.QuayGround), so the retaining wall is taken down to the
        /// drawn bank instead of floating over it. Refused when that wall would pass ToeBankFoundation. A flight down the
        /// face would end in the air over the lowered bank: such houses keep their terrace without one.
        /// </summary>
        static bool SeatOnBank(HouseDef h)
        {
            float lo = 1e9f;
            for (int i = 0; i <= 4; i++)
                for (int j = 0; j <= 4; j++)
                {
                    Vector3 p = h.At(Mathf.Lerp(h.pS0, h.pS1, i / 4f), 0f, Mathf.Lerp(h.pF0, h.pF1 + h.reach, j / 4f));
                    lo = Mathf.Min(lo, Village.QuayGround(p.x, p.z, VKit.Ground(p.x, p.z)));
                }
            float baseY = lo - VillagePlan.FoundationDepth;
            if (baseY >= h.baseY - 0.05f) return true;                       // clear of the lowered bank: as seated
            if (h.c.y - baseY > ToeBankFoundation) return false;
            h.baseY = baseY;
            h.frontStair = 0; h.reach = 0f;
            return true;
        }

        static void Farms(System.Random rng)
        {
            float[] spots = { 128f, -215f, 166f, -118f, 142f, -22f, 176f, 74f, 152f, 172f, 192f, 262f, 140f, 332f };
            for (int i = 0; i < spots.Length; i += 2)
                TryFarm(rng, new Vector3(spots[i], 0f, spots[i + 1]), VKit.Dir(-90f + VKit.Rnd(rng, -30f, 30f)));
            float[] along = { 42f, 112f, 182f }, outw = { 30f, 58f, 36f };
            for (int i = 0; i < along.Length; i++)
            {
                float s = PathDef.SPromEnd + along[i];
                TryFarm(rng, PathDef.Pos(s) + Right(s) * outw[i], -Right(s));
            }
        }

        static void TryFarm(System.Random rng, Vector3 at, Vector3 facing)
        {
            for (int k = 0; k < 8; k++)
            {
                Vector3 p = at + new Vector3(VKit.Rnd(rng, -7f, 7f), 0f, VKit.Rnd(rng, -7f, 7f)) * (k > 0 ? 1f : 0f);
                HouseDef h = VillagePlan.New(rng, 5, p, facing, VKit.Rnd(rng, 6.5f, 9f), VKit.Rnd(rng, 4.6f, 5.6f));
                h.wall = VKit.Chance(rng, 0.5f) ? VKit.WhOld : VKit.Wh;
                if (VKit.Chance(rng, 0.7f)) VillagePlan.AddWing(h, rng, VKit.Rnd(rng, 3.6f, 5f), 4.2f, 1);
                if (VKit.Chance(rng, 0.3f) && h.wingSide == 0) h.vault = true;
                if (VillagePlan.Accept(h, true, false, 1.5f, 4.9f)) { VillagePlan.Commit(h); return; }
            }
        }
    }
}
