using System.Collections.Generic;
using UnityEngine;

namespace CASCADE
{
    /// <summary>
    /// Builds one intact white house from its HouseDef: foundation terrace, 1-3 boxes with recessed doors and windows, roofs,
    /// stairs, pots; then the dressing: plaster surrounds, painted shutters, copings, pierced chimneys, barrel roofs with rims,
    /// blue domes, rooftop churches with bell gables, cisterns, pergolas, balconies, bougainvillea, masonry foundations,
    /// the Oia palette (ochre, sand, rose, terracotta, sky-blue lime-wash on a share of the houses), painted plinths and
    /// the small things on blank walls near the path (lanterns, number tiles, meter boxes and cables, scuppers).
    /// The plan's random stream (h.seed) is consumed exactly as before, so doors, windows and perches stay where they were;
    /// every dressing choice comes from a second stream or from hashes of the seed (shared with RuinGen).
    /// </summary>
    internal static class HouseGen
    {
        const float LowH = 3f;             // height of one row of cave-room windows in a tall foundation wall
        // the toe of the cascade (VillageZones.ToeZone), the walls the quay looks up at in frames 20 and 45
        const float ToeCaveShare = 0.88f;  // share of the cave-window slots a toe wall fills (others 0.72; house fronts 0.88)
        const float ToeSideShare = 0.8f;   // ... and of a toe house's side-wall window slots (others 0.55)
        const bool CellarDoors = true;     // doors at the foot of terrace walls that stand right at the path
        static readonly List<Opening> ops = new List<Opening>(8);
        static readonly List<Opening> groundOps = new List<Opening>(8);    // the ground storey's openings of the wall being built
        static readonly List<Vector2> spans = new List<Vector2>(6);        // free stretches of that wall (t0, t1)
        static readonly List<Vector3> pts = new List<Vector3>(16);
        static readonly float[] slotT = new float[6];
        static float doorS;                // where the door ended up on the main facade (house frame, along S)
        static bool doorPlaced;

        // ---------------- dressing ----------------
        /// <summary>Dressing decisions of one house, made once for the whole plan so that the intact and the ruined builds agree.</summary>
        internal sealed class Extra
        {
            public Material paint;        // shutters and doors
            public Material roof;         // barrel roofs: whitewash or painted blue
            public Material stone;        // masonry of the tall retaining walls
            public Material wall;         // Oia palette: coloured lime-wash of the boxes (null: the plan's whitewash)
            public Material plinth;       // painted base band along the foot of the facades (null: none)
            public float room;            // how far things on the roof may rise above the plan's roof line (walker's view, vista caps)
            public int dome;              // 0 none, 1 on a drum on the flat roof, 2 on the roof room, 3 rooftop church (big dome, bell gable)
            public float domeR;
            public float domeS;           // church: dome centre along S (on the main roof, f = 0)
            public float gableS, gableK;  // church: bell gable centre along S on the front parapet, and its scale (0: no gable)
            public bool hero;             // church: the hero church of the quay look (bigger dome, taller drum, bigger gable)
            public float vaultRise, wingRise; // barrel roofs: a true half circle where the roof rule leaves room, else the plan's flatter arch
            public bool cistern, pergola, cafe, balcony, ironRail, roofSeats, roofPots, boug;
            public float windowBoxes;     // chance of a flower box under a window
        }

        const int MaxDomes = 24;
        const float DomeRMin = 0.9f, DomeRMax = 1.35f;   // small roof domes: big enough to read from the quay, 60-150 m off
        const float BalHalfW = 1.0f, BalDepth = 0.62f, BeamY = 2.42f;

        // ---- rooftop churches (Prepare picks the houses the camera sees) ----
        const int QuayChurches = 4;                      // churches placed for the frames of the harbour walk
        const float ChurchSpacing = 35f;                 // ... at least this far apart (and from the stair-road one)
        const float ChurchRMin = 1.5f, ChurchRMax = 2.3f;
        const float ChurchDrumH = 0.9f;                  // drum from the roof floor to the cornice
        const float ChurchLanternH = 0.5f;               // dome apex to the foot of the cross
        const float GableKMin = 0.65f, GableKMax = 0.9f; // bell gable scale (1 = 2.4 m wide, 3.3 m to the upper coping)
        const float ChurchClear = 0.5f;                  // churches stay this far below the roof rule's limit
        const float ClimbChurchGap = 8f;                 // the stair-road church stands this close to the path
        // the Ahead gaze on the quay (Beat01_Harbour's A cues at 0, 18, 46 and 66 s; 2.7 m/s): chainage of the eyes and the
        // pitch there. FrameWeight projects into that cue's lens (Beat01_Harbour.WalkFov, 16:9).
        static readonly float[] QuayEyeS = { 10f, 50f, 70f, 128f, 150f, 185f };
        static readonly float[] QuayEyePitch = { 6f, 7f, 7f, 6f, 6f, 4f };
        const float Aspect = 16f / 9f;
        const float LookS = 108f;                        // where the walker turns to look up at a church (Beat01_Harbour, t ~ 40 s)
        const float LookLen = 16f;                       // ... and walks on while looking (to t ~ 46 s)
        const float LookAimS = 11f;                      // the dome sits exactly on the upper third from here (t ~ 44 s, the look has settled)
        // ---- the hero church: the one the walker looks up at. It is chosen first (the nearest good one, not one of the walk's
        // churches far up the cliff), with its dome on the end of the roof that keeps the bell gable beside it in the look, and
        // gets a bigger dome on a taller drum that shows its lit windows, a bigger lantern and bell gable. The dome can be
        // wider than a cliff house is deep: its drum then stands proud of the facade and the back wall like a heavy cornice,
        // on a moulded corbel with a closed underside.
        const float HeroOverhang = 0.45f;                // dome radius = half depth + this (drum face 0.5 m proud at most)
        const float HeroRMin = 2.35f, HeroRMax = 3.0f;
        const float HeroDrumH = 1.4f;                    // roof to cornice (the ordinary church's 0.9 barely clears the parapet)
        const float HeroLanternK = 1.3f;                 // lantern scale, so it is not a pin on the big dome
        const float HeroGableKMax = 1.1f;
        const float HeroGableKWant = 0.85f, HeroRFloor = 2.2f;   // on a narrow roof the dome shrinks (not below the floor) until the gable reaches this
        const int HeroSeg = 28;                          // even (drum windows on every other face): 14 windows
        const float LookSizeCap = 0.14f;                 // dome angular width (rad, ~180 px at the look's lens) beyond which nearer scores no better
        const float LookPairShift = 0.35f;               // the look aims this far from the dome towards its gable (0: the dome dead centre)

        // ---- Oia palette, plinths and wall clutter ----
        // share of coloured houses per zone (0 harbour row, 1 cliff, 2 stair flank, 3 rim, 4 below the rim, 5 farm)
        static readonly float[] PaintShare = { 0.25f, 0.16f, 0.04f, 0.20f, 0.12f, 0.04f, 0.16f };   // 6: toe of the cascade
        static readonly string[] PaletteNames = { "M_WallOchre", "M_WallSand", "M_WallRose", "M_WallTerracotta", "M_WallSky" };
        static readonly float[] PaletteCum = { 0.32f, 0.56f, 0.76f, 0.92f, 1f };   // ochre .32, sand .24, rose .20, terracotta .16, sky .08
        const float PlinthShare = 0.45f, PlinthShareHarbour = 0.7f;
        const float PlinthH = 0.42f, PlinthHQuay = 0.62f;   // above the floor; on the quay from the pavement up
        const float PlinthOut = 0.006f;                  // paint, a few millimetres proud of the wall
        const float ClutterReach = 10f;                  // walls whose foot is this close to the path get the clutter
        static Material[] palette;
        static Material plinthMat, lanternGlass;

        /// <summary>A rooftop church, for the gaze (Beat01_Harbour) and the cloth (flags on the gables).</summary>
        internal struct ChurchInfo { public Vector3 dome, gableTop, face; public float gableK; public int band; }
        internal static readonly List<ChurchInfo> Churches = new List<ChurchInfo>(6);
        /// <summary>Centre of the dome the walker looks up at from the quay, and the point that look aims at (valid when HaveQuayChurch).</summary>
        internal static Vector3 QuayChurchDome, QuayChurchLook;
        internal static bool HaveQuayChurch;
        /// <summary>The look's church: a hero church (else an ordinary one), its dome radius and distance from the settled look (log).</summary>
        internal static bool QuayChurchHero;
        internal static float QuayChurchR, QuayChurchDist;
        internal static int Coloured;
        static float[] occR2;

        static Extra[] extras;
        static Extra ex;
        static System.Random dr;           // dressing stream of the house being built
        static bool near;                  // within reach of the path: full detail
        static bool balconyDone, roomBuilt, hadBoug;
        static float gateA = 1f, gateB = -1f;   // gap in the terrace front wall along S (gate or head of the flight); gateA > gateB: none
        static float chimS = 99f;          // the chimney's position along S on the main roof (99: none)
        static Vector3 roomTop;            // centre of the roof room's top
        /// <summary>Highest point of the last house built (chimney, dome, roof room, cistern): the top of its band for the ruin swap.</summary>
        public static float TopY;

        /// <summary>Makes the dressing decisions for the whole plan (call once after the zones are built).</summary>
        public static void Prepare(List<HouseDef> hs)
        {
            extras = new Extra[hs.Count];
            int domes = 0;
            for (int i = 0; i < hs.Count; i++) extras[i] = Decide(hs[i], ref domes);
            // the two cafe terraces: terraced houses right beside the walk, on the promenade first
            int b0 = -1, b1 = -1; float s0 = 1e9f, s1 = 1e9f;
            for (int i = 0; i < hs.Count; i++)
            {
                HouseDef h = hs[i];
                if ((h.zone != 3 && h.zone != 1) || h.apron < 1.45f || h.awning || h.hw < 2.4f) continue;
                float g = VillagePlan.MinPathGap(h, 0f);
                if (g > 9f) continue;
                float score = g + (h.zone == 3 ? 0f : 6f);
                if (score < s0) { b1 = b0; s1 = s0; b0 = i; s0 = score; }
                else if (score < s1) { b1 = i; s1 = score; }
            }
            for (int k = 0; k < 2; k++)
            {
                int i = k == 0 ? b0 : b1;
                if (i < 0) continue;
                Extra x = extras[i];
                x.cafe = true; x.pergola = true; x.balcony = false; x.boug = true; x.windowBoxes = 0.5f;
            }
            SelectChurches(hs);
            Recolour(hs);
        }

        // ---------------- rooftop churches ----------------
        /// <summary>
        /// Layout of a rooftop church on h, or false where none fits: a flat main roof with room under the roof rule. The dome
        /// stands at one end of the roof (radius from the smaller half size), the bell gable on the front parapet at the other
        /// end, clear of the dome's cornice (left off where it would not fit or would break the roof rule).
        /// rise = height of the church's top above the plan's main roof line (parapet top). hero: the hero church's layout
        /// (bigger dome on a taller drum, bigger gable; false where the gable would not fit, since the look is the dome and its gable).
        /// domeEnd: +1 / -1 puts the dome at that end along S (the hero's is chosen for the look), 0 a hash of the seed.
        /// </summary>
        static bool ChurchLayout(HouseDef h, bool hero, int domeEnd, out float r, out float ds, out float gs, out float gk, out float rise)
        {
            r = ds = gs = gk = rise = 0f;
            if ((h.zone != 1 && h.zone != 4) || h.vault || h.roofBox || h.hw < 2.5f || h.hd < 1.9f) return false;
            float line = h.c.y + h.MainH + VWalls.ParapetH;
            float room = -VillagePlan.RoofExcess(h) + (h.roofTop - line);          // RoofExcess is -99 away from the roof rule
            r = hero ? Mathf.Clamp(h.hd + HeroOverhang, HeroRMin, HeroRMax) : Mathf.Clamp(Mathf.Min(h.hw, h.hd) - 0.25f, ChurchRMin, ChurchRMax);
            gk = GableFit(h, r, hero);
            // the hero gives up a little dome for a bell gable that reads (the look is the dome and its gable)
            if (hero) while (gk < HeroGableKWant && r - 0.05f >= HeroRFloor) { r -= 0.05f; gk = GableFit(h, r, true); }
            float domeRise = DrumH(hero) + 0.08f + r + LanternH(hero) + ChapelGen.CrossH - VWalls.ParapetH;
            if (room < domeRise + ChurchClear) return false;
            float side = domeEnd != 0 ? domeEnd : VKit.Hash(h.seed, 26) < 0.5f ? 1f : -1f;
            ds = side * (h.hw - VWalls.ParapetT - 0.05f - r);                        // the drum touches the parapet's inner face
            float gableRise = ChapelGen.GableTop * gk + ChapelGen.CrossH;
            if (gk < GableKMin || room < gableRise + ChurchClear) gk = 0f;
            if (hero && gk <= 0f) return false;
            gs = -side * (h.hw - 0.12f - ChapelGen.GableHalfW * gk);
            rise = Mathf.Max(domeRise, gk > 0f ? gableRise : 0f);
            return true;
        }

        /// <summary>
        /// Scale of the bell gable that fits on the front parapet beside a dome of radius r (below GableKMin: none fits). The
        /// gable's back face lies 0.32 m behind the facade (0.35 at the hero's scale, its coping 0.41); there the cornice
        /// (r + 0.15) is only a chord wide.
        /// </summary>
        static float GableFit(HouseDef h, float r, bool hero)
        {
            float fb = h.hd - (hero ? 0.42f : 0.37f), rc = r + 0.15f;
            float chord = Mathf.Sqrt(Mathf.Max(rc * rc - fb * fb, 0f));
            return Mathf.Min((2f * h.hw - r - chord - 0.53f) / (2f * ChapelGen.GableHalfW), hero ? HeroGableKMax : GableKMax);
        }

        static float DrumH(bool hero) { return hero ? HeroDrumH : ChurchDrumH; }
        static float LanternH(bool hero) { return hero ? ChurchLanternH * HeroLanternK : ChurchLanternH; }

        /// <summary>Centre of a church's dome (world).</summary>
        static Vector3 DomeCentre(HouseDef h, float ds, float r, bool hero) { return h.At(ds, h.MainH + DrumH(hero) + 0.08f + r * 0.4f, 0f); }
        /// <summary>Centre of the foot of a church's bell gable on the front parapet, flush with the facade (world).</summary>
        static Vector3 GableFoot(HouseDef h, float gs, float gk) { return h.At(gs, h.MainH + VWalls.ParapetH, h.hd - ChapelGen.GableDepth * 0.5f * gk); }

        /// <summary>
        /// The pair in the look (seen from where it settles): 1 when the bell gable stands clear beside the dome, down to 0.25
        /// when one hides the other (a church seen end-on, its gable in front of or behind the dome).
        /// </summary>
        static float PairScore(Vector3 dome, float r, Vector3 gableFoot, float gk)
        {
            Vector3 f = dome - EyeAt(LookS + LookAimS); f.y = 0f; f.Normalize();
            float lateral = Mathf.Abs(Vector3.Dot(gableFoot - dome, new Vector3(f.z, 0f, -f.x)));
            return 0.25f + 0.75f * MathX.Smooth(0.5f, 0.95f, lateral / (r + 0.2f + ChapelGen.GableHalfW * gk));
        }

        static Vector3 EyeAt(float s) { return PathDef.Pos(s) + Vector3.up * Config.EyeHeight; }

        /// <summary>
        /// Turns a few houses into rooftop churches where the camera looks. First the hero: the church the walker turns to look
        /// up at from the quay (LookScore with the hero layout: well up and to the right, clear, as big in the look's frame as
        /// possible). Then the walk's churches: each candidate is scored by how much the Ahead gaze on the quay sees of its dome
        /// (right half and upper part of the frame, clear of the terrain and of the other houses, bigger when nearer); the best
        /// QuayChurches at least ChurchSpacing apart are taken, plus one lining the stair-road that the climb passes within
        /// ClimbChurchGap. A pure function of the plan: no random stream is touched.
        /// </summary>
        static void SelectChurches(List<HouseDef> hs)
        {
            Churches.Clear(); HaveQuayChurch = false; QuayChurchHero = false; QuayChurchR = QuayChurchDist = 0f;
            int n = hs.Count;
            occR2 = new float[n];
            for (int i = 0; i < n; i++)
            {
                HouseDef o = hs[i];
                float a = Mathf.Max(-o.pS0, o.pS1), b = Mathf.Max(-o.pF0, o.pF1 + o.reach);
                occR2[i] = a * a + b * b;
            }
            var score = new float[n];
            var cand = new List<int>(48);
            float r, ds, gs, gk, rise;
            for (int i = 0; i < n; i++)
            {
                HouseDef h = hs[i];
                if (h.c.z < VillageZones.HamletZ1) continue;                              // the south hamlet is not in the quay frames
                if (!ChurchLayout(h, false, 0, out r, out ds, out gs, out gk, out rise)) continue;
                cand.Add(i);
                Vector3 p = DomeCentre(h, ds, r, false);
                // the springing too: a roof in front that hides the lower half of the dome passed the centre's test alone
                // (frame 20, a toe house under a walk church's dome). An eye counts only where the whole dome clears.
                Vector3 pb = p - Vector3.up * (r * 0.4f);
                float sum = 0f;
                for (int e = 0; e < QuayEyeS.Length; e++)
                {
                    float s = QuayEyeS[e];
                    Vector3 eye = EyeAt(s);
                    float w = FrameWeight(eye, PathDef.FlatDir(s + Gaze.AheadLead), QuayEyePitch[e], Beat01_Harbour.WalkFov, p);
                    if (w <= 0f) continue;
                    float dist = Vector3.Distance(eye, p);
                    float seen = Clear(eye, p, hs, i);
                    if (seen > 0f) seen = Clear(eye, pb, hs, i);
                    sum += w * Mathf.Clamp(r * 60f / dist, 0.4f, 1.6f) * seen;
                }
                score[i] = sum;
            }
            var chosen = new List<int>(QuayChurches + 2);
            // the hero, with its dome on the end of the roof that keeps the bell gable beside it in the look
            int hero = -1, heroEnd = 0;
            {
                float bestLook = 0f;
                for (int c = 0; c < cand.Count; c++)
                {
                    int i = cand[c];
                    HouseDef h = hs[i];
                    for (int end = -1; end <= 1; end += 2)
                    {
                        if (!ChurchLayout(h, true, end, out r, out ds, out gs, out gk, out rise)) continue;
                        Vector3 p = DomeCentre(h, ds, r, true);
                        float q = LookScore(hs, i, p, r);
                        if (q > bestLook) q *= PairScore(p, r, GableFoot(h, gs, gk), gk);
                        if (q > bestLook) { bestLook = q; hero = i; heroEnd = end; }
                    }
                }
                if (hero >= 0) chosen.Add(hero);
            }
            // the best seen in the walk, spaced
            for (int k = 0; k < QuayChurches; k++)
            {
                int best = -1; float bs = 0.05f;
                for (int c = 0; c < cand.Count; c++)
                {
                    int i = cand[c];
                    if (i != hero && score[i] > bs && Spaced(hs, chosen, i, ChurchSpacing)) { bs = score[i]; best = i; }
                }
                if (best < 0) break;
                chosen.Add(best);
            }
            // the climb's church: a house lining the stair-road (between the ends of its legs), right beside the path
            float zHi = PathDef.Pos(PathDef.SQuayEnd).z, zLo = zHi - 24f;
            {
                int best = -1; float bs = -1e9f;
                for (int c = 0; c < cand.Count; c++)
                {
                    int i = cand[c];
                    HouseDef h = hs[i];
                    if (h.zone != 1 || h.c.z < zLo - 4f || h.c.z > zHi + 4f || chosen.Contains(i)) continue;
                    float gap = VillagePlan.MinPathGap(h, 0f);
                    if (gap > ClimbChurchGap || !Spaced(hs, chosen, i, ChurchSpacing * 0.6f)) continue;
                    ChurchLayout(h, false, 0, out r, out ds, out gs, out gk, out rise);
                    float sc = (gk > 0f ? 2f : 0f) + r - gap * 0.15f;                   // a gable and a big dome first, then the nearer
                    if (sc > bs) { bs = sc; best = i; }
                }
                if (best >= 0) chosen.Add(best);
            }
            for (int c = 0; c < chosen.Count; c++) MakeChurch(hs[chosen[c]], extras[chosen[c]], chosen[c] == hero, chosen[c] == hero ? heroEnd : 0);
            // the look: the hero, or (no house could carry one) the best placed of the ordinary churches
            int look = hero;
            if (look < 0)
            {
                float bestLook = 0f;
                for (int c = 0; c < chosen.Count; c++)
                {
                    Extra x = extras[chosen[c]];
                    if (x.dome != 3) continue;
                    float q = LookScore(hs, chosen[c], DomeCentre(hs[chosen[c]], x.domeS, x.domeR, false), x.domeR);
                    if (q > bestLook) { bestLook = q; look = chosen[c]; }
                }
            }
            if (look >= 0)
            {
                HouseDef h = hs[look];
                Extra x = extras[look];
                Vector3 p = DomeCentre(h, x.domeS, x.domeR, x.hero), em = EyeAt(LookS + LookAimS), d = p - em;
                // across: between the dome and its bell gable (nearer the dome), so the pair shares the middle of the frame
                Vector3 a = x.gableK > 0f ? Vector3.Lerp(p, GableFoot(h, x.gableS, x.gableK), LookPairShift) : p, da = a - em;
                float horiz = Mathf.Max(Mathf.Sqrt(d.x * d.x + d.z * d.z), 1f), horizA = Mathf.Max(Mathf.Sqrt(da.x * da.x + da.z * da.z), 1f);
                // up: below the dome by the angle that puts it on the upper third of the look's lens (the old fixed 10-degree
                // offset in height dropped a steep dome towards the centre)
                float above = Mathf.Atan(Mathf.Tan(Beat01_Harbour.ChurchFov * 0.5f * Mathf.Deg2Rad) / 3f);
                float el = Mathf.Atan2(d.y, horiz) - above;
                QuayChurchDome = p; HaveQuayChurch = true;
                QuayChurchLook = new Vector3(a.x, em.y + horizA * Mathf.Tan(el), a.z);
                QuayChurchHero = x.hero; QuayChurchR = x.domeR; QuayChurchDist = d.magnitude;
            }
            occR2 = null;
        }

        /// <summary>
        /// How well a church dome at p (radius r) serves the look up from the quay (LookS to LookS + LookLen): well up and to the
        /// right of the walk where the look starts, in sight where it starts, settles and ends, and as wide in the frame as
        /// possible up to LookSizeCap (nearer than that is no better). 0 where it does not qualify.
        /// </summary>
        static float LookScore(List<HouseDef> hs, int id, Vector3 p, float r)
        {
            Vector3 e0 = EyeAt(LookS), f0 = PathDef.FlatDir(LookS), right = new Vector3(f0.z, 0f, -f0.x);
            Vector3 d = p - e0;
            float horiz = Mathf.Sqrt(d.x * d.x + d.z * d.z);
            float el = Mathf.Atan2(d.y, horiz) * Mathf.Rad2Deg;
            float yaw = Mathf.Atan2(d.x * right.x + d.z * right.z, d.x * f0.x + d.z * f0.z) * Mathf.Rad2Deg;
            float q = MathX.Smooth(8f, 14f, el) * (1f - MathX.Smooth(38f, 46f, el)) * MathX.Smooth(12f, 22f, yaw) * (1f - MathX.Smooth(80f, 95f, yaw));
            if (q <= 0f) return 0f;
            q *= Mathf.Min(2f * (r + 0.15f) / Mathf.Max(d.magnitude, 1f), LookSizeCap);
            return q * Clear(e0, p, hs, id) * Clear(EyeAt(LookS + LookAimS), p, hs, id) * Clear(EyeAt(LookS + LookLen), p, hs, id);
        }

        static bool Spaced(List<HouseDef> hs, List<int> chosen, int i, float min)
        {
            for (int k = 0; k < chosen.Count; k++)
            {
                Vector3 d = hs[chosen[k]].c - hs[i].c; d.y = 0f;
                if (d.sqrMagnitude < min * min) return false;
            }
            return true;
        }

        static void MakeChurch(HouseDef h, Extra x, bool hero, int domeEnd)
        {
            float r, ds, gs, gk, rise;
            if (!ChurchLayout(h, hero, domeEnd, out r, out ds, out gs, out gk, out rise)) return;
            x.dome = 3; x.domeR = r; x.domeS = ds; x.gableS = gs; x.gableK = gk; x.hero = hero;
            x.paint = VKit.Blue;                                        // blue doors and shutters on the church
            x.cistern = false; x.roofSeats = false; x.roofPots = false;
            if (h.apron < 0.9f) x.boug = false;                         // its blossom would hang over the gable's parapet
            if (hero) { x.boug = false; x.pergola = false; }            // a clean white front under the drum that stands proud of it
            float baseY = h.c.y + h.MainH + VWalls.ParapetH;
            Churches.Add(new ChurchInfo
            {
                dome = DomeCentre(h, ds, r, hero), face = h.F, gableK = gk, band = h.band,
                gableTop = gk > 0f ? h.At(gs, baseY - h.c.y + ChapelGen.GableTop * gk, h.hd - ChapelGen.GableDepth * 0.5f * gk) : Vector3.zero
            });
        }

        /// <summary>
        /// How well p sits in the right half and upper part of the frame of a gaze along fwd (flat, unit) at pitch with a vertical
        /// fov (degrees, 16:9); 0 outside. Projected to the screen, so the window follows the lens: x runs from the centre (0) to
        /// the right edge (1), y from the centre to the top edge. The bounds are the old ones of the 70-degree lens.
        /// </summary>
        static float FrameWeight(Vector3 eye, Vector3 fwd, float pitch, float fov, Vector3 p)
        {
            float cp = Mathf.Cos(pitch * Mathf.Deg2Rad), sp = Mathf.Sin(pitch * Mathf.Deg2Rad);
            Vector3 f = new Vector3(fwd.x * cp, sp, fwd.z * cp), rt = new Vector3(fwd.z, 0f, -fwd.x), up = Vector3.Cross(f, rt);
            Vector3 d = p - eye;
            float z = Vector3.Dot(d, f);
            if (z < 1f) return 0f;
            float tv = Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
            float x = Vector3.Dot(d, rt) / (z * tv * Aspect), y = Vector3.Dot(d, up) / (z * tv);
            return MathX.Smooth(0.056f, 0.2f, x) * (1f - MathX.Smooth(0.63f, 0.89f, x)) * MathX.Smooth(-0.075f, 0.125f, y) * (1f - MathX.Smooth(0.64f, 0.86f, y));
        }

        /// <summary>1 when the sight line from eye to target clears the ground and every other house's block, else 0 (house self excluded).</summary>
        static float Clear(Vector3 eye, Vector3 target, List<HouseDef> hs, int self)
        {
            Vector3 d = target - eye;
            float len = d.magnitude;
            int n = Mathf.Clamp(Mathf.CeilToInt(len / 2.5f), 4, 160);
            for (int k = 1; k < n; k++)
            {
                float t = k / (float)n;
                if (len * (1f - t) < 3f) break;                         // the last metres are the house's own roof
                Vector3 p = eye + d * t;
                if (TerrainBuilder.Height(p.x, p.z) > p.y) return 0f;     // the baked 2 m field: the ground as drawn, and cheap
                for (int i = 0; i < hs.Count; i++)
                {
                    HouseDef o = hs[i];
                    if (i == self || o.roofTop < p.y) continue;
                    float dx = p.x - o.c.x, dz = p.z - o.c.z;
                    if (dx * dx + dz * dz > occR2[i]) continue;
                    float s = dx * o.S.x + dz * o.S.z, f = dx * o.F.x + dz * o.F.z;
                    if (s > o.pS0 && s < o.pS1 && f > o.pF0 && f < o.pF1) return 0f;
                }
            }
            return 1f;
        }

        // ---------------- Oia palette and plinths ----------------
        /// <summary>A generated material by name, or null when the asset build predates it (the house then stays as it was).</summary>
        static Material Opt(string name)
        {
            GameAssets a = GameAssets.I;
            if (a == null || a.materials == null) return null;
            for (int i = 0; i < a.materials.Length; i++)
                if (a.materials[i] != null && a.materials[i].name == name) return a.materials[i];
            return null;
        }

        /// <summary>
        /// Lime-wash colours on a share of the houses by zone (churches stay white), a white barrel over a coloured box,
        /// joinery that does not clash, and painted plinths. Hashes of the seed only.
        /// </summary>
        static void Recolour(List<HouseDef> hs)
        {
            palette = new Material[PaletteNames.Length];
            for (int i = 0; i < palette.Length; i++) palette[i] = Opt(PaletteNames[i]);
            plinthMat = Opt("M_Plinth");
            lanternGlass = Opt("M_LampB");
            Coloured = 0;
            for (int i = 0; i < hs.Count; i++)
            {
                HouseDef h = hs[i];
                Extra x = extras[i];
                int sd = h.seed;
                if (x.dome != 3 && VKit.Hash(sd, 21) < PaintShare[Mathf.Clamp(h.zone, 0, PaintShare.Length - 1)])
                {
                    float v = VKit.Hash(sd, 22);
                    int k = 0;
                    while (k < PaletteCum.Length - 1 && v >= PaletteCum[k]) k++;
                    if (palette[k] != null)
                    {
                        x.wall = palette[k];
                        x.roof = VKit.Wh;                                       // a white barrel over a coloured box
                        if (k == 3 && x.paint == VKit.ShutRed) x.paint = VKit.ShutGreen;
                        Coloured++;
                    }
                }
                float share = h.zone == 0 ? PlinthShareHarbour : h.zone == 2 ? 0f : PlinthShare;
                if (VKit.Hash(sd, 24) < share)
                    x.plinth = x.paint == VKit.Blue && VKit.Hash(sd, 25) < 0.4f ? VKit.DomeBlue : plinthMat;
            }
        }

        /// <summary>The material of the house's boxes (the palette colour, or the plan's whitewash).</summary>
        static Material WallOf(HouseDef h) { return ex.wall != null ? ex.wall : h.wall; }

        /// <summary>The dressing decisions of a house (Prepare runs on first use if the caller did not).</summary>
        public static Extra ExtraOf(HouseDef h)
        {
            List<HouseDef> hs = VillagePlan.Houses;
            if (extras == null || extras.Length != hs.Count) Prepare(hs);
            return h.id >= 0 && h.id < extras.Length && hs[h.id] == h ? extras[h.id] : Decide(h, ref dummyDomes);
        }
        static int dummyDomes = MaxDomes;

        static Extra Decide(HouseDef h, ref int domes)
        {
            var x = new Extra();
            int sd = h.seed;
            float v = VKit.Hash(sd, 1);
            if (h.zone == 0) x.paint = v < 0.38f ? VKit.Blue : v < 0.66f ? VKit.ShutGreen : v < 0.88f ? VKit.ShutRed : VKit.WoodDk;
            else x.paint = v < 0.56f ? VKit.Blue : v < 0.8f ? VKit.ShutGreen : v < 0.92f ? VKit.ShutRed : VKit.WoodDk;
            x.roof = VKit.Hash(sd, 2) < 0.24f ? VKit.DomeBlue : h.wall;
            x.stone = VKit.Hash(sd, 3) < 0.7f ? VKit.Pumice : VKit.Rock;
            x.room = RoofRoom(h);
            x.windowBoxes = h.zone == 0 ? 0.3f : h.zone == 3 ? 0.16f : 0.07f;
            bool flat = !h.vault;
            float vb = Mathf.Min(Mathf.Min(h.hw, h.hd), 2.6f), wb = Mathf.Min(Mathf.Min(h.wingHw, h.wingHd), 2.6f);
            x.vaultRise = x.room > vb * 0.38f + 0.15f ? vb : vb * 0.62f;
            x.wingRise = x.room > wb * 0.38f + 0.15f ? wb : wb * 0.62f;
            if (domes < MaxDomes && flat && h.zone != 0 && h.zone != 2 && VKit.Hash(sd, 4) < 0.2f)
            {
                float r = Mathf.Clamp(Mathf.Min(h.hw, h.hd) * 0.42f, DomeRMin, DomeRMax);
                if (h.roofBox) { r = 1.05f; if (x.room > r + 0.75f) x.dome = 2; }       // the roof room is 2.4 m deep: cornice r + 0.13 < 1.2
                else if (h.hw >= r + 1.05f && h.hd >= r + 0.5f && x.room > r + 0.72f) x.dome = 1;
                if (x.dome != 0) { x.domeR = r; domes++; }
            }
            x.cistern = flat && !h.roofBox && x.dome == 0 && h.hw >= 1.9f && h.hd >= 1.5f && x.room > 0.6f && VKit.Hash(sd, 5) < 0.28f;
            x.pergola = h.zone != 0 && h.zone != 2 && h.apron >= 1.4f && !h.awning && VKit.Hash(sd, 6) < 0.32f;
            x.balcony = h.zone != 2 && h.zone != 5 && h.storeys >= 2 && !h.awning && !x.pergola && VKit.Hash(sd, 7) < 0.4f;
            x.ironRail = VKit.Hash(sd, 8) < 0.55f;
            x.roofSeats = h.stairSide != 0 && flat && !h.roofBox && x.dome == 0 && !x.cistern && x.room > 0.6f && VKit.Hash(sd, 9) < 0.45f;
            x.roofPots = h.zone != 0 && flat && x.room > 0.75f && VKit.Hash(sd, 10) < 0.25f;
            x.boug = h.zone != 0 && h.zone != 2 && VKit.Hash(sd, 11) < 0.26f;
            return x;
        }

        /// <summary>How far roof dressing may rise above the plan's roof line: the roof rule's margin, the vista cap of the west stair flank.</summary>
        static float RoofRoom(HouseDef h)
        {
            float room = -VillagePlan.RoofExcess(h);
            if (h.zone == 2 && h.F.x > 0f) room = Mathf.Min(room, PathDef.Pos(PathDef.SStairEnd).y - VillagePlan.RoofBelowProm - h.roofTop);
            return Mathf.Clamp(room, 0f, 3f);
        }

        public static void Build(MeshBuilder mb, HouseDef h, List<Vector3> perches)
        {
            var rng = new System.Random(h.seed);
            ex = ExtraOf(h);
            dr = new System.Random(h.seed ^ 0x3C6EF372);
            near = VKit.PathGap(h.c.x, h.c.z) < 16f;
            TopY = h.roofTop;
            gateA = 1f; gateB = -1f; chimS = 99f; balconyDone = false; roomBuilt = false; hadBoug = false;
            VWalls.Style(true, !near, ex.paint, h.seed ^ 0x1B873593, ex.windowBoxes, h.zone != 2);
            Foundation(mb, h, rng);
            doorPlaced = false; doorS = 0f;
            Body(mb, h, rng, 0f, 0f, h.hw, h.hd, h.storeys, h.vault, true);
            if (h.wingSide != 0) Body(mb, h, rng, h.WingS, h.WingF, h.wingHw, h.wingHd, h.wingStoreys, h.wingVault, false);
            float H = h.MainH;
            if (h.roofBox && !h.vault)
            {
                float sx = (h.hw - 1.75f) * (h.wingSide != 0 ? -h.wingSide : VKit.Sgn(rng));
                RoofCube(mb, h, rng, h.At(sx, H, -(h.hd - 1.55f)), 1.4f, 1.2f, 2.3f);
            }
            if (h.stairSide != 0)
                VWalls.Stair(mb, h.wall, h.At(h.stairSide * h.hw, 0f, h.hd), -h.F, h.S * h.stairSide, 0.9f, h.hd * 2f, H);
            // chimney (left off where it would stick up into a walker's view)
            if (!h.noChimney && !h.vault)
            {
                float cs = (h.hw - 0.6f) * (h.roofBox ? (h.wingSide != 0 ? h.wingSide : 1f) : VKit.Sgn(rng));
                float ch = VKit.Rnd(rng, 1.0f, 1.35f);
                if (ex.dome != 3)          // a church keeps its roof to the dome and the gable (the draws above keep the stream)
                {
                    VWalls.Chimney(mb, WallOf(h), h.At(cs, H, -(h.hd - 0.62f)), Yaw(h), ch);
                    chimS = cs;
                    TopY = Mathf.Max(TopY, h.c.y + H + ch + 0.1f);
                }
            }
            else if (!h.noChimney && h.wingSide != 0 && !h.wingVault)
            {
                VWalls.Chimney(mb, WallOf(h), h.At(h.WingS, h.WingH, h.WingF - h.wingHd + 0.62f), Yaw(h), 1.1f);
                TopY = Mathf.Max(TopY, h.c.y + h.WingH + 1.2f);
            }
            Dressing(mb, h, rng);
            RoofDressing(mb, h);
            if (perches != null && h.zone == 0)
            {
                float top = H + VWalls.ParapetH + VWalls.CopeH;      // on the coping
                perches.Add(h.At(-h.hw + 0.15f, top, h.hd - 0.12f));
                if (h.id % 2 == 0) perches.Add(h.At(h.hw - 0.15f, top, h.hd - 0.12f));
            }
            VWalls.ResetStyle();
        }

        public static float Yaw(HouseDef h) { return Mathf.Atan2(h.F.x, h.F.z) * Mathf.Rad2Deg; }

        /// <summary>The levelled terrace the house stands on; its retaining walls go below the lowest ground under it.</summary>
        static void Foundation(MeshBuilder mb, HouseDef h, System.Random rng)
        {
            Material m = VKit.WhOld, st = ex.stone;
            float depthY = h.baseY - h.c.y;                       // negative
            float len = h.pS1 - h.pS0;
            Vector3 up = Vector3.up;
            VKit.Q(mb, m, h.At(h.pS0, 0f, h.pF0), h.At(h.pS1, 0f, h.pF0), h.At(h.pS1, 0f, h.pF1), h.At(h.pS0, 0f, h.pF1), up);
            // side walls: coursed masonry below, whitewash only for the top three metres of a tall foundation, a plaster band between
            float cutY = depthY < -6f ? -3f : depthY;
            VKit.Q(mb, m, h.At(h.pS1, cutY, h.pF0), h.At(h.pS1, cutY, h.pF1), h.At(h.pS1, 0f, h.pF1), h.At(h.pS1, 0f, h.pF0), h.S);
            VKit.Q(mb, m, h.At(h.pS0, cutY, h.pF0), h.At(h.pS0, cutY, h.pF1), h.At(h.pS0, 0f, h.pF1), h.At(h.pS0, 0f, h.pF0), -h.S);
            if (cutY > depthY)
            {
                VKit.Q(mb, st, h.At(h.pS1, depthY, h.pF0), h.At(h.pS1, depthY, h.pF1), h.At(h.pS1, cutY, h.pF1), h.At(h.pS1, cutY, h.pF0), h.S);
                VKit.Q(mb, st, h.At(h.pS0, depthY, h.pF0), h.At(h.pS0, depthY, h.pF1), h.At(h.pS0, cutY, h.pF1), h.At(h.pS0, cutY, h.pF0), -h.S);
                VKit.Strip(mb, VKit.Trim, h.At(h.pS1, cutY - 0.1f, h.pF0), h.F, h.S, h.pF1 - h.pF0, 0.2f);
                VKit.Strip(mb, VKit.Trim, h.At(h.pS0, cutY - 0.1f, h.pF1), -h.F, -h.S, h.pF1 - h.pF0, 0.2f);
            }

            // ---- steps down the face of the foundation (cliff houses) ----
            float stA = 0f, stB = 0f; bool flight = false;
            if (h.frontStair != 0)
            {
                float e = h.frontStair > 0 ? h.pS1 : h.pS0;
                int n = Mathf.Min(17, Mathf.FloorToInt((len - 1.2f) / 0.27f));
                Vector3 gb = h.At(e - h.frontStair * n * 0.27f, 0f, h.pF1 + 0.6f);
                n = Mathf.Min(n, Mathf.FloorToInt((h.c.y - VKit.Ground(gb.x, gb.z) - 0.15f) / VWalls.StepRise));
                if (n >= 5)
                {
                    float run = n * 0.27f, rise = n * VWalls.StepRise, sb = e - h.frontStair * run;
                    VWalls.Stair(mb, m, h.At(sb, -rise, h.pF1), h.S * h.frontStair, h.F, 0.9f, run, rise);
                    VKit.Box(mb, m, h.At((sb + e) * 0.5f, (depthY - rise) * 0.5f, h.pF1 + 0.45f), h.S * (run * 0.5f), up * ((-rise - depthY) * 0.5f), h.F * 0.45f, 4 | 8 | 32);
                    stA = Mathf.Min(sb, e); stB = Mathf.Max(sb, e); flight = true;
                }
            }
            // ---- front wall (and, for houses whose back hangs over the cliff, the back wall) with rows of cave-room windows ----
            float frontStone = Face(mb, h, rng, m, h.At(h.pS0, depthY, h.pF1), h.S, h.F, len, -depthY, h.lowerWindows, flight, stA - h.pS0, stB - h.pS0);
            Face(mb, h, rng, m, h.At(h.pS1, depthY, h.pF0), -h.S, -h.F, len, -depthY, h.backWindows, false, 0f, 0f);
            // dressed stone quoins up the front corners where both faces are masonry
            if (near && cutY > depthY && frontStone > 0.5f)
            {
                float qTop = Mathf.Min(depthY + frontStone, cutY) - 0.12f;
                Quoins(mb, h, st, h.pS0, h.pF1, -h.S, h.F, qTop);
                Quoins(mb, h, st, h.pS1, h.pF1, h.S, h.F, qTop);
            }
            // plaster band along the edge of the terrace (the front, and the sides away from the harbour row)
            float bA = h.pS0 - 0.03f, bB = h.pS1 + 0.03f;
            if (flight) { if (h.frontStair > 0) bB = stA; else bA = stB; }           // not through the top steps of the flight
            VKit.Strip(mb, VKit.Trim, h.At(bA, -0.2f, h.pF1), h.S, h.F, bB - bA, 0.2f);
            if (h.zone != 0)
            {
                VKit.Strip(mb, VKit.Trim, h.At(h.pS1, -0.2f, h.pF0), h.F, h.S, h.pF1 - h.pF0, 0.2f);
                VKit.Strip(mb, VKit.Trim, h.At(h.pS0, -0.2f, h.pF1), -h.F, -h.S, h.pF1 - h.pF0, 0.2f);
            }

            if (h.zone == 0) Pavement(mb, h);
            if (h.zone == 0 || h.apron < 0.9f) return;
            // ---- terrace wall around the apron ----
            float t = 0.24f, ph = 0.9f, fa = h.pF1 - h.apron;
            Material pm = h.wall;
            float g0 = -99f, g1 = -99f;                                   // a gap in the front wall: gate, or the head of the flight
            if (h.zone == 3 || h.zone == 5) { float gate = h.pS0 + len * VKit.Rnd(rng, 0.3f, 0.7f); g0 = gate - 0.6f; g1 = gate + 0.6f; Steps(mb, h, h.At(gate, 0f, h.pF1), h.S, h.F); }
            else if (flight) { g0 = h.frontStair > 0 ? h.pS1 - 0.95f : h.pS0 - 0.01f; g1 = h.frontStair > 0 ? h.pS1 + 0.01f : h.pS0 + 0.95f; }
            if (g0 > -90f)
            {
                gateA = g0; gateB = g1;
                LowWall(mb, pm, h, h.pS0, g0, h.pF1 - t, h.pF1, ph);
                LowWall(mb, pm, h, g1, h.pS1, h.pF1 - t, h.pF1, ph);
            }
            else LowWall(mb, pm, h, h.pS0, h.pS1, h.pF1 - t, h.pF1, ph);
            LowWall(mb, pm, h, h.pS0, h.pS0 + t, fa, h.pF1 - t, ph);
            LowWall(mb, pm, h, h.pS1 - t, h.pS1, fa, h.pF1 - t, ph);
        }

        /// <summary>Alternating long and short corner stones, a few centimetres proud, down from topY towards the ground (at most seven).</summary>
        static void Quoins(MeshBuilder mb, HouseDef h, Material m, float s, float f, Vector3 nS, Vector3 nF, float topY)
        {
            Vector3 k = h.At(s, 0f, f);
            float g = VKit.Ground(k.x, k.z) - h.c.y;
            const float pr = 0.035f;
            float y = topY;
            for (int i = 0; i < 7 && y - 0.36f > g - 0.15f; i++)
            {
                float hb = VKit.Rnd(dr, 0.3f, 0.36f);
                bool longFront = (i & 1) == 0;
                float lf = longFront ? 0.56f : 0.3f, ls = longFront ? 0.3f : 0.56f;   // legs along the front face and along the side face
                Vector3 c = k + Vector3.up * (y - hb * 0.5f) + nS * ((pr - lf) * 0.5f) + nF * ((pr - ls) * 0.5f);
                VKit.Box(mb, m, c, nS * ((lf + pr) * 0.5f), Vector3.up * (hb * 0.5f - 0.012f), nF * ((ls + pr) * 0.5f));
                y -= hb;
            }
        }

        /// <summary>Harbour row only: a strip of pavement between the quay slab (x up to 0.5) and the facades, a finger above the quay.</summary>
        public static void Pavement(MeshBuilder mb, HouseDef h)
        {
            float y = Config.QuayHeight + 0.03f - h.c.y, f0 = h.c.x - 1.4f, f1 = h.c.x - 0.3f;
            VKit.Q(mb, VKit.Stone, h.At(h.pS0 - 0.2f, y, f0), h.At(h.pS1 + 0.2f, y, f0), h.At(h.pS1 + 0.2f, y, f1), h.At(h.pS0 - 0.2f, y, f1), Vector3.up);
            VKit.Q(mb, VKit.Stone, h.At(h.pS0 - 0.2f, y - 0.5f, f1), h.At(h.pS1 + 0.2f, y - 0.5f, f1), h.At(h.pS1 + 0.2f, y, f1), h.At(h.pS0 - 0.2f, y, f1), h.F);
        }

        /// <summary>
        /// One retaining wall of the foundation: plain below, rows of windows above where it stands tall and clear of the ground.
        /// Returns how high (above p0) the wall is coursed masonry (0: none).
        /// </summary>
        static float Face(MeshBuilder mb, HouseDef h, System.Random rng, Material m, Vector3 p0, Vector3 u, Vector3 n, float len, float hgt, bool windows, bool flight, float fa, float fb)
        {
            Vector3 up = Vector3.up;
            bool toe = h.zone == VillageZones.ToeZone;
            int rows = CaveRows(h, hgt, windows);
            int slots = rows > 0 ? Slots(len) : 0;
            for (int k = 0; k < rows; k++)
            {
                ops.Clear();
                float baseRel = hgt - LowH * (k + 1);
                for (int i = 0; i < slots; i++)
                {
                    if (flight && slotT[i] > fa - 0.7f && slotT[i] < fb + 0.7f) continue;
                    Vector3 g = p0 + u * slotT[i] + n * 0.4f;
                    float gy = toe ? ToeWallGround(p0 + u * slotT[i], n) : VKit.Ground(g.x, g.z);
                    if (p0.y + baseRel + VWalls.Sill < gy + 0.6f) continue;
                    if (!VKit.Chance(rng, toe ? ToeCaveShare : 0.72f)) continue;
                    ops.Add(DecoWindow(Window(slotT[i], rng), true));
                }
                VWalls.Band(mb, m, p0 + up * baseRel, u, n, len, LowH, ops, rng);
            }
            float rest = hgt - LowH * rows;
            if (rest < 0.01f) return 0f;
            // a wall standing right at the path gets a cellar door at the walker's level, on a stoop that follows the ground
            Vector3 mid = p0 + u * (len * 0.5f) + n * 0.3f;
            if (windows && !flight && CellarDoors && rest - VillagePlan.FoundationDepth > 3f && VKit.PathGap(mid.x, mid.z) < 4.2f)
            {
                int ns = Slots(len);
                float tc = slotT[rng.Next(ns)];
                Vector3 ga = p0 + u * (tc - 0.55f) + n * 0.3f, gb = p0 + u * (tc + 0.55f) + n * 0.3f;
                float yd = Mathf.Max(VKit.Ground(ga.x, ga.z), VKit.Ground(gb.x, gb.z)) + 0.22f - p0.y;      // threshold above the wall base
                const float bandH = 2.5f;
                if (yd > 0.4f && yd + bandH < rest)
                {
                    VKit.Q(mb, m, p0, p0 + u * len, p0 + u * len + up * yd, p0 + up * yd, n);
                    ops.Clear();
                    float tw = tc + (tc < len * 0.5f ? 1.7f : -1.7f);
                    Vector3 gw = p0 + u * tw + n * 0.3f;
                    bool win = tw > 0.9f && tw < len - 0.9f && p0.y + yd + VWalls.Sill > VKit.Ground(gw.x, gw.z) + 0.4f;
                    if (win && tw < tc) ops.Add(DecoWindow(Window(tw, rng), true));
                    ops.Add(DecoDoor(Door(tc, rng, 0.3f), bandH, true));
                    if (win && tw > tc) ops.Add(DecoWindow(Window(tw, rng), true));
                    VWalls.Band(mb, m, p0 + up * yd, u, n, len, bandH, ops, rng);
                    VKit.Q(mb, m, p0 + up * (yd + bandH), p0 + u * len + up * (yd + bandH), p0 + u * len + up * rest, p0 + up * rest, n);
                    VKit.Box(mb, m, p0 + u * tc + up * (yd - 0.7f) + n * 0.28f, u * 0.75f, up * 0.7f, n * 0.28f, 8 | 32);
                    return 0f;
                }
            }
            if (rest > 6f)
            {
                float cut = rest - 3f;
                VKit.Q(mb, ex.stone, p0, p0 + u * len, p0 + u * len + up * cut, p0 + up * cut, n);
                VKit.Q(mb, m, p0 + up * cut, p0 + u * len + up * cut, p0 + u * len + up * rest, p0 + up * rest, n);
                VKit.Strip(mb, VKit.Trim, p0 + up * (cut - 0.1f), u, n, len, 0.2f);
                return cut;
            }
            VKit.Q(mb, m, p0, p0 + u * len, p0 + u * len + up * rest, p0 + up * rest, n);
            return 0f;
        }

        /// <summary>
        /// Rows of cave-room windows in a retaining wall of height hgt (0 without windows). A very tall wall keeps only two at
        /// the top, so that it reads as a retaining wall and not as a tower; the toe's walls keep three, since the quay sees
        /// only their upper part over the harbour row (frame 45). Never negative: a wall under 1.6 m once gave rest > hgt.
        /// Shared with RuinGen, so the ruin's masonry line agrees with the intact wall.
        /// </summary>
        internal static int CaveRows(HouseDef h, float hgt, bool windows)
        {
            if (!windows) return 0;
            int cap = hgt > 15f && h.zone != VillageZones.ToeZone ? 2 : 3;
            return Mathf.Clamp(Mathf.FloorToInt((hgt - VillagePlan.FoundationDepth - 0.2f) / LowH), 0, cap);
        }

        /// <summary>
        /// Ground under a cave window of a toe wall, as the terrain is drawn (foot: the slot's point on the wall base line).
        /// SeatOnBank takes those walls down to the bank behind the harbour row, which the terrain draws lowered (by up to
        /// ~10 m, Village.QuayGround); the ground function there buried every row but the top one and left 6-9 m of blank
        /// plaster in the quay frames. The bank is steep (up to ~5 m per m), so the wall's own foot counts too: a sill keeps
        /// 0.6 m over the ground 0.4 m out and 0.4 m over the ground at the wall.
        /// </summary>
        static float ToeWallGround(Vector3 foot, Vector3 n)
        {
            Vector3 a = foot + n * 0.4f, b = foot + n * 0.05f;
            return Mathf.Max(TerrainBuilder.Height(a.x, a.z), TerrainBuilder.Height(b.x, b.z) - 0.2f);
        }

        /// <summary>A whitewashed terrace wall with a rounded plaster coping along its length.</summary>
        static void LowWall(MeshBuilder mb, Material m, HouseDef h, float s0, float s1, float f0, float f1, float hgt)
        {
            if (s1 - s0 < 0.05f || f1 - f0 < 0.05f) return;
            VKit.Box(mb, m, h.At((s0 + s1) * 0.5f, hgt * 0.5f, (f0 + f1) * 0.5f), h.S * ((s1 - s0) * 0.5f), Vector3.up * (hgt * 0.5f), h.F * ((f1 - f0) * 0.5f), 8);
            if (s1 - s0 >= f1 - f0) { float fc = (f0 + f1) * 0.5f; VWalls.CopeRun(mb, h.At(s0, hgt, fc), h.At(s1, hgt, fc), h.F, f1 - f0); }
            else { float sc = (s0 + s1) * 0.5f; VWalls.CopeRun(mb, h.At(sc, hgt, f0), h.At(sc, hgt, f1), h.S, s1 - s0); }
        }

        /// <summary>Door steps from the platform edge down to the ground in front. foot = point on the platform edge at floor level.</summary>
        static void Steps(MeshBuilder mb, HouseDef h, Vector3 foot, Vector3 u, Vector3 n)
        {
            float ground = h.zone == 0 ? Config.QuayHeight + 0.03f : VKit.Ground(foot.x + n.x, foot.z + n.z);
            float rise = foot.y - ground;
            if (rise < 0.1f) return;
            int k = Mathf.Clamp(Mathf.CeilToInt(rise / 0.19f), 1, 8);
            rise = Mathf.Min(rise, k * 0.19f);
            float run = k * 0.29f;
            Vector3 start = foot - u * 0.7f + n * run - Vector3.up * rise;
            VWalls.Stair(mb, VKit.WhOld, start, -n, u, 1.4f, run, rise, true);
            if (h.zone != 0)   // a block under the flight so it never hovers over uneven ground
                VKit.Box(mb, VKit.WhOld, foot + n * (run * 0.5f) - Vector3.up * (rise + 0.4f), u * 0.7f, Vector3.up * 0.4f, n * (run * 0.5f), 4 | 8 | 32);
        }

        /// <summary>Fills slotT with evenly spread opening centres for a wall of the given length; returns their number.</summary>
        static int Slots(float len)
        {
            int n = Mathf.Clamp(Mathf.FloorToInt((len - 0.5f) / 2f), 1, 4);
            float pitch = (len - 0.6f) / n;
            for (int i = 0; i < n; i++) slotT[i] = 0.3f + pitch * (i + 0.5f);
            return n;
        }

        static int SideSlots(float len, System.Random rng)
        {
            int n = len >= 4.4f && VKit.Chance(rng, 0.5f) ? 2 : 1;
            float pitch = (len - 0.6f) / n;
            for (int i = 0; i < n; i++) slotT[i] = 0.3f + pitch * (i + 0.5f);
            return n;
        }

        static Opening Window(float tc, System.Random rng)
        {
            double v = rng.NextDouble();
            return new Opening
            {
                t0 = tc - VWalls.WindowW * 0.5f, t1 = tc + VWalls.WindowW * 0.5f, y0 = VWalls.Sill, y1 = VWalls.Sill + VWalls.WindowH,
                kind = 0, shutters = v < 0.7 ? 1 : v < 0.82 ? 2 : 0, glass = VKit.Glass(rng)
            };
        }

        static Opening Door(float tc, System.Random rng, float openChance)
        {
            return new Opening { t0 = tc - VWalls.DoorW * 0.5f, t1 = tc + VWalls.DoorW * 0.5f, y0 = 0f, y1 = VWalls.DoorH, kind = VKit.Chance(rng, openChance) ? 2 : 1 };
        }

        /// <summary>Dressing of a door (dressing stream only): one stone step up, and on some (most cave doors) a round head.</summary>
        static Opening DecoDoor(Opening o, float bandH, bool cave)
        {
            o.y0 = VWalls.DoorStep;
            o.y1 = VWalls.DoorH + 0.1f;
            if (bandH - (VWalls.DoorH + 0.25f) >= 0.2f && VKit.Chance(dr, cave ? 0.85f : 0.3f)) { o.arch = true; o.y1 = VWalls.DoorH + 0.25f; }
            return o;
        }

        /// <summary>Dressing of a window: cave-room windows are often round-headed, house windows rarely.</summary>
        static Opening DecoWindow(Opening o, bool cave)
        {
            if (VKit.Chance(dr, cave ? 0.45f : 0.07f)) { o.arch = true; o.y1 += 0.2f; }
            return o;
        }

        /// <summary>One box of the house: four walls storey by storey, then its roof. cs, cf: centre in the house frame.</summary>
        static void Body(MeshBuilder mb, HouseDef h, System.Random rng, float cs, float cf, float a, float b, int storeys, bool vault, bool main)
        {
            Vector3 c = h.At(cs, 0f, cf);
            Material wall = WallOf(h);
            float H = storeys * h.storeyH;
            int blockedSide = main ? h.wingSide : -h.wingSide;       // the side that faces the other box
            int stair = main ? h.stairSide : 0;
            float otherAhead = h.wingSide == 0 ? -99f : main ? h.wingFront : -h.wingFront;   // how far the other box stands in front of this facade
            for (int w = 0; w < 4; w++)
            {
                Vector3 n = w == 0 ? h.F : w == 1 ? -h.F : w == 2 ? h.S : -h.S;
                Vector3 u = VKit.WallU(n);
                float halfN = w < 2 ? b : a, halfU = w < 2 ? a : b, len = halfU * 2f;
                Vector3 p0 = c + n * halfN - u * halfU;
                int sideSign = w == 2 ? 1 : w == 3 ? -1 : 0;
                bool plain = (w == 1 && !h.backWindows) || (sideSign != 0 && (h.plainSides || sideSign == blockedSide));
                int nSlots = plain ? 0 : w < 2 ? Slots(len) : SideSlots(len, rng);
                int doorSlot = (w == 0 && (main || VKit.Chance(rng, 0.35f)) && !h.noGroundOpenings) ? rng.Next(nSlots) : -1;
                // one balcony per house, on an upper window of the front (dressing stream)
                int balK = -1, balSlot = -1;
                if (w == 0 && ex.balcony && !balconyDone && storeys >= 2 && nSlots > 0 && h.zone != 2) { balK = 1 + dr.Next(storeys - 1); balSlot = dr.Next(nSlots); }
                // a small high window on a blank side wall that stands free (hashes only)
                int wk = WallKey(h, w, main);
                bool open = sideSign != 0 && sideSign != blockedSide && sideSign != stair;
                bool highWin = plain && open && VKit.Hash(wk, 41) < 0.65f && SideFree(h, p0 + u * (len * 0.5f) + n * 0.8f, storeys * h.storeyH - 1.25f);
                bool balBefore = balconyDone;
                groundOps.Clear();
                for (int k = 0; k < storeys; k++)
                {
                    ops.Clear();
                    bool ground = k == 0;
                    bool skipStorey = (ground && h.noGroundOpenings && w == 0) || (ground && sideSign != 0 && sideSign == stair);
                    float bandH = h.storeyH + (k == storeys - 1 && !vault ? VWalls.ParapetH : 0f);
                    Vector3 pk = p0 + Vector3.up * (k * h.storeyH);
                    int balAt = -1;
                    if (!skipStorey)
                        for (int i = 0; i < nSlots; i++)
                        {
                            if (ground && i == doorSlot)
                            {
                                ops.Add(DecoDoor(Door(slotT[i], rng, h.zone == 0 ? 0.45f : 0.3f), bandH, false));
                                if (main && !doorPlaced) { doorPlaced = true; doorS = cs - a + slotT[i]; h.doorS = doorS; }
                                if (h.apron < 0.5f) Steps(mb, h, p0 + u * slotT[i] + n * (h.pF1 - (cf + b)), u, n);
                            }
                            // the toe's side walls face the quay walk at 20 (one draw either way: the stream is unchanged)
                            else if (VKit.Chance(rng, w < 2 ? 0.88f : h.zone == VillageZones.ToeZone ? ToeSideShare : 0.55f))
                            {
                                Opening o = Window(slotT[i], rng);
                                if (k == balK && i == balSlot && BalconyFits(pk, u, n, slotT[i], len, blockedSide, otherAhead))
                                {
                                    o.y0 = 0.07f; o.y1 = VWalls.Sill + VWalls.WindowH + 0.05f;      // a glazed door onto the balcony
                                    balAt = ops.Count;
                                }
                                else o = DecoWindow(o, false);
                                ops.Add(o);
                            }
                        }
                    if (highWin && k == storeys - 1) ops.Add(HighWindow(len, h.storeyH, wk));
                    VWalls.Band(mb, wall, pk, u, n, len, bandH, ops, rng);
                    if (ground) groundOps.AddRange(ops);
                    if (balAt >= 0)
                    {
                        VWalls.Balcony(mb, wall, pk, u, n, (ops[balAt].t0 + ops[balAt].t1) * 0.5f, BalHalfW, BalDepth, ex.ironRail);
                        balconyDone = true;
                    }
                }
                // painted base band on the front and on the free sides (not along the harbour row's party walls)
                if (ex.plinth != null && (w == 0 || (open && !h.plainSides)))
                {
                    bool quay = h.zone == 0 && w == 0 && Mathf.Abs(cf + b - h.pF1) < 0.02f;     // this facade stands on the quay
                    float y0 = quay ? Config.QuayHeight + 0.03f - h.c.y : 0f;
                    // on the quay the band is rendered, proud enough to take in the plaster skirting of the terrace edge
                    PlinthBand(mb, p0, u, n, len, y0, y0 + (quay ? PlinthHQuay : PlinthH), quay ? 0.035f : PlinthOut);
                }
                // small things on the walls the walker passes (a side wall only where no neighbour stands within a metre)
                if (near && w != 1 && (w == 0 || open))
                {
                    Vector3 foot = p0 + u * (len * 0.5f) + n * 0.5f;
                    if (VKit.PathGap(foot.x, foot.z) < ClutterReach && (w == 0 || WallFree(h, p0, u, n, len, 0.3f)))
                        Clutter(mb, h, p0, u, n, len, H, !vault, w == 0, nSlots, highWin, balconyDone != balBefore, wk);
                }
            }
            Vector3 top = c + Vector3.up * H;
            if (!vault) { VWalls.FlatRoof(mb, wall, wall == VKit.WhOld ? VKit.WhWarm : VKit.WhOld, top, h.S, h.F, a, b); return; }
            float rise = main ? ex.vaultRise : ex.wingRise;
            if (rise <= 0.01f) rise = Mathf.Min(Mathf.Min(a, b), 2.6f) * 0.62f;
            if (a >= b) VWalls.VaultRoof(mb, wall, ex.roof, top, h.S, h.F, a, b, rise);
            else VWalls.VaultRoof(mb, wall, ex.roof, top, h.F, h.S, b, a, rise);
            TopY = Mathf.Max(TopY, top.y + rise + 0.09f);
        }

        /// <summary>Room on the facade for a balcony at tc: inside the wall, off the other box's side wall, clear of the path.</summary>
        static bool BalconyFits(Vector3 p0, Vector3 u, Vector3 n, float tc, float len, int blockedSide, float otherAhead)
        {
            if (tc - BalHalfW < 0.12f || tc + BalHalfW > len - 0.12f) return false;
            if (otherAhead > -BalDepth - 0.1f)
            {
                if (blockedSide > 0 && tc + BalHalfW > len - 0.2f) return false;
                if (blockedSide < 0 && tc - BalHalfW < 0.2f) return false;
            }
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 q = p0 + u * (tc + s * BalHalfW) + n * (BalDepth + 0.05f);
                if (VKit.PathGap(q.x, q.z) < 0.5f) return false;
            }
            return true;
        }

        // ---------------- plinths and wall clutter ----------------
        /// <summary>A key for the hashes of one wall of one box.</summary>
        static int WallKey(HouseDef h, int w, bool main) { unchecked { return h.seed * 31 + w * 7 + (main ? 0 : 3); } }

        /// <summary>A small square window high in the top storey, in the middle of a blank wall: no shutters, some lit at night.</summary>
        static Opening HighWindow(float len, float storeyH, int wk)
        {
            float y0 = storeyH - 1.25f;
            return new Opening
            {
                t0 = len * 0.5f - 0.25f, t1 = len * 0.5f + 0.25f, y0 = y0, y1 = y0 + 0.5f, kind = 0, shutters = 0,
                glass = VKit.Hash(wk, 42) < 0.45f ? VKit.WinC : VKit.WinDark
            };
        }

        /// <summary>True when the ground and every other house stay below y0 (above the floor) at q, a point just off a wall.</summary>
        static bool SideFree(HouseDef h, Vector3 q, float y0)
        {
            float y = h.c.y + y0;
            if (Village.QuayGround(q.x, q.z, VKit.Ground(q.x, q.z)) > y - 0.3f) return false;     // the terrain as built (lowered behind the quay)
            List<HouseDef> hs = VillagePlan.Houses;
            for (int i = 0; i < hs.Count; i++)
            {
                HouseDef o = hs[i];
                if (o == h || o.roofTop < y - 0.1f) continue;
                Vector3 d = q - o.c;
                float s = d.x * o.S.x + d.z * o.S.z, f = d.x * o.F.x + d.z * o.F.z;
                if (s > o.pS0 - 0.3f && s < o.pS1 + 0.3f && f > o.pF0 - 0.3f && f < o.pF1 + o.reach + 0.3f) return false;
            }
            return true;
        }

        /// <summary>SideFree at both ends and the middle of a wall (p0 its corner, u along it, n out), 0.8 m out.</summary>
        static bool WallFree(HouseDef h, Vector3 p0, Vector3 u, Vector3 n, float len, float y0)
        {
            for (int i = 0; i < 3; i++)
                if (!SideFree(h, p0 + u * Mathf.Lerp(0.4f, len - 0.4f, i * 0.5f) + n * 0.8f, y0)) return false;
            return true;
        }

        /// <summary>Painted base band from y0 to y1 (above the floor) along a wall, broken at the doors (their surround, step, flight).</summary>
        static void PlinthBand(MeshBuilder mb, Vector3 p0, Vector3 u, Vector3 n, float len, float y0, float y1, float depth)
        {
            float t = 0f;
            for (int i = 0; i <= groundOps.Count; i++)
            {
                float e = len, next = len;
                if (i < groundOps.Count)
                {
                    Opening o = groundOps[i];
                    if (o.kind == 0 && o.y0 > y1) continue;                  // a window above the band
                    e = o.t0 - 0.3f; next = o.t1 + 0.3f;
                }
                if (e - t > 0.12f) VKit.Strip(mb, ex.plinth, p0 + u * t + Vector3.up * y0, u, n, e - t, y1 - y0, depth);
                t = Mathf.Max(t, next);
            }
        }

        /// <summary>
        /// Small things on the blank stretches of a wall the walker passes (hashes of the wall only): an iron lantern beside
        /// the door and a blue number tile on its other side, a lantern on a long blank wall, a meter box in the house's
        /// paint with its conduit and a pair of cables clipped along the wall under the upper floor, a plaster scupper under
        /// the parapet. Nothing reaches into the path corridor.
        /// </summary>
        static void Clutter(MeshBuilder mb, HouseDef h, Vector3 p0, Vector3 u, Vector3 n, float len, float H, bool flat, bool front, int nSlots, bool highWin, bool balcony, int wk)
        {
            Vector3 up = Vector3.up;
            // free stretches of the wall: clear of every window column (leaves, balconies) and of the high window
            spans.Clear();
            float t = 0.3f;
            int nb = nSlots + (highWin ? 1 : 0);
            for (int i = 0; i <= nb; i++)
            {
                float a = len - 0.3f, b = len;
                if (i < nSlots) { a = slotT[i] - 1.05f; b = slotT[i] + 1.05f; }
                else if (i < nb) { a = len * 0.5f - 0.6f; b = len * 0.5f + 0.6f; }
                if (a - t > 0.5f) spans.Add(new Vector2(t, a));
                t = Mathf.Max(t, b);
            }
            int widest = -1; float wl = 0f;
            for (int i = 0; i < spans.Count; i++) if (spans[i].y - spans[i].x > wl) { wl = spans[i].y - spans[i].x; widest = i; }
            float d0 = -1f, d1 = -1f;                                             // the door on this wall
            for (int i = 0; i < groundOps.Count; i++) if (groundOps[i].kind != 0) { d0 = groundOps[i].t0; d1 = groundOps[i].t1; break; }
            bool shaded = front && (h.awning || ex.pergola);                      // awning or pergola at lantern and cable height
            float cableY = Mathf.Min(2.9f, h.storeyH - 0.12f);
            float lampY = 2.72f;                                                   // the bracket; the lantern hangs below it, clear of the door head
            bool lampDone = false;

            if (d0 >= 0f && !shaded && !balcony)
            {
                bool right = VKit.Hash(wk, 44) < 0.5f;
                float tl = right ? d1 + 0.42f : d0 - 0.42f, tt = right ? d0 - 0.3f : d1 + 0.3f;
                if (tl > 0.2f && tl < len - 0.2f && VKit.Hash(wk, 43) < 0.6f) { WallLantern(mb, p0 + u * tl + up * lampY, n); lampDone = true; }
                if (tt > 0.15f && tt < len - 0.15f && VKit.Hash(wk, 45) < 0.55f) NumberTile(mb, p0 + u * tt + up * 1.78f, u, n);
            }
            if (widest >= 0)
            {
                Vector2 sp = spans[widest];
                // a lantern on a long blank wall
                if (!lampDone && !shaded && !balcony && wl > 1.2f && VKit.Hash(wk, 46) < 0.35f)
                {
                    WallLantern(mb, p0 + u * Mathf.Lerp(sp.x, sp.y, 0.3f) + up * lampY, n);
                    sp.x = Mathf.Lerp(sp.x, sp.y, 0.3f) + 0.4f;                     // the meter box goes beyond it
                }
                if (sp.y - sp.x > 0.6f && VKit.Hash(wk, 47) < 0.45f)
                {
                    float tm = (sp.x + sp.y) * 0.5f;
                    Vector3 m = p0 + u * tm + up * 1.5f;
                    VKit.Box(mb, ex.paint, m + n * 0.075f, u * 0.175f, up * 0.25f, n * 0.075f, 32);
                    VKit.Box(mb, VKit.Iron, m + n * 0.152f + u * 0.12f, u * 0.012f, up * 0.03f, n * 0.004f, 32);    // its latch
                    if (!shaded && !balcony && cableY > 2.2f)
                    {
                        Vector3 c0 = m + up * 0.25f + n * 0.03f;
                        mb.Cylinder(VKit.Iron, c0, p0 + u * tm + up * cableY + n * 0.03f, 0.02f, 0.02f, 5, false);   // conduit
                        float te = VKit.Hash(wk, 48) < 0.5f ? 0.05f : len - 0.05f;   // the cables run to a corner of the house
                        for (int k = 0; k < 2; k++) WallCable(mb, p0 + up * (cableY - 0.07f * k) + n * (0.035f + 0.02f * k), u, tm, te);
                    }
                }
            }
            // a plaster scupper under the parapet (flat roofs), near a corner
            if (flat && VKit.Hash(wk, 49) < 0.55f)
            {
                float ts = VKit.Hash(wk, 50) < 0.5f ? 0.45f : len - 0.45f;
                Vector3 s0 = p0 + u * ts + up * (H + 0.07f), tip = s0 + n * 0.36f;
                if (VKit.PathGap(tip.x, tip.z) > 0.3f)
                {
                    VKit.Box(mb, VKit.Trim, s0 + n * 0.17f, u * 0.05f, up * 0.045f, n * 0.17f, 32);
                    VKit.Box(mb, VKit.Ruin, s0 + n * 0.34f + up * 0.005f, u * 0.03f, up * 0.025f, n * 0.002f);   // its dark mouth
                }
            }
        }

        /// <summary>
        /// An iron wall lantern: a scrolled bracket from the wall at anchor, the lantern (glass, cap, floor plate, drop) hanging
        /// from its end. The glass is a street-lamp material, so it lights with the lamps at dusk.
        /// </summary>
        static void WallLantern(MeshBuilder mb, Vector3 anchor, Vector3 n)
        {
            Vector3 up = Vector3.up, tip = anchor + n * 0.3f;
            if (VKit.PathGap(tip.x, tip.z) < 0.3f) return;
            Material glass = lanternGlass != null ? lanternGlass : VKit.WinA;
            VKit.Box(mb, VKit.Iron, anchor + n * 0.01f, Vector3.Cross(up, n) * 0.05f, up * 0.08f, n * 0.01f, 32);   // wall plate
            pts.Clear();
            pts.Add(anchor + n * 0.01f);
            pts.Add(anchor + n * 0.12f + up * 0.05f);
            pts.Add(anchor + n * 0.24f + up * 0.05f);
            pts.Add(anchor + n * 0.3f - up * 0.01f);
            mb.Tube(VKit.Iron, pts, 0.016f, 5);
            Vector3 c = anchor + n * 0.3f - up * 0.2f;                            // centre of the glass
            Quaternion rot = Quaternion.LookRotation(n, up);
            mb.Box(glass, c, new Vector3(0.15f, 0.21f, 0.15f), rot, 4 | 8);
            mb.Box(VKit.Iron, c + up * 0.115f, new Vector3(0.2f, 0.02f, 0.2f), rot);
            mb.Cylinder(VKit.Iron, c + up * 0.125f, c + up * 0.2f, 0.12f, 0.02f, 4, false);
            mb.Box(VKit.Iron, c - up * 0.115f, new Vector3(0.18f, 0.02f, 0.18f), rot);
            mb.Cylinder(VKit.Iron, c - up * 0.125f, c - up * 0.17f, 0.05f, 0.01f, 5, false);
        }

        /// <summary>A blue enamel house number on a white backing, flat on the wall (c = its centre on the wall face).</summary>
        static void NumberTile(MeshBuilder mb, Vector3 c, Vector3 u, Vector3 n)
        {
            VKit.Box(mb, VKit.Trim, c + n * 0.006f, u * 0.1f, Vector3.up * 0.075f, n * 0.006f, 32);
            VKit.Box(mb, VKit.DomeBlue, c + n * 0.014f, u * 0.085f, Vector3.up * 0.06f, n * 0.003f, 32);
        }

        /// <summary>A cable clipped along a wall from t0 to t1 (p0 = the wall's corner at the cable's height, off the wall), sagging between clips.</summary>
        static void WallCable(MeshBuilder mb, Vector3 p0, Vector3 u, float t0, float t1)
        {
            float len = Mathf.Abs(t1 - t0);
            if (len < 0.3f) return;
            int clips = Mathf.Max(1, Mathf.RoundToInt(len / 1.3f));
            pts.Clear();
            for (int i = 0; i < clips; i++)
                for (int k = 0; k < 3; k++)
                {
                    float f = (i + k / 3f) / clips, sag = 0.035f * Mathf.Sin(Mathf.PI * k / 3f);
                    pts.Add(p0 + u * Mathf.Lerp(t0, t1, f) - Vector3.up * sag);
                }
            pts.Add(p0 + u * t1);
            mb.Tube(VKit.Iron, pts, 0.02f, 4);
        }

        /// <summary>Small room on the roof with a door (step up, plaster surround) and a moulded cornice.</summary>
        static void RoofCube(MeshBuilder mb, HouseDef h, System.Random rng, Vector3 c, float a, float b, float hgt)
        {
            for (int w = 0; w < 4; w++)
            {
                Vector3 n = w == 0 ? h.F : w == 1 ? -h.F : w == 2 ? h.S : -h.S;
                Vector3 u = VKit.WallU(n);
                float halfN = w < 2 ? b : a, halfU = w < 2 ? a : b;
                ops.Clear();
                if (w == 0) ops.Add(DecoDoor(Door(halfU, rng, 0.2f), hgt, false));
                VWalls.Band(mb, WallOf(h), c + n * halfN - u * halfU, u, n, halfU * 2f, hgt, ops, rng);
            }
            Vector3 t = c + Vector3.up * hgt;
            VKit.Q(mb, h.wall, t - h.S * a - h.F * b, t + h.S * a - h.F * b, t + h.S * a + h.F * b, t - h.S * a + h.F * b, Vector3.up);   // its roof stays white
            VWalls.Cornice(mb, t, h.S, h.F, a, b);
            roomTop = t; roomBuilt = true;
            TopY = Mathf.Max(TopY, t.y + 0.03f);
        }

        static void Dressing(MeshBuilder mb, HouseDef h, System.Random rng)
        {
            float front = h.hd + 0.28f;
            bool landing = h.apron >= 0.5f || h.pF1 - h.hd > 0.5f;            // room for pots beside the door
            if (doorPlaced && (landing || h.zone == 0))
            {
                float y = landing ? 0f : Config.QuayHeight + 0.03f - h.c.y, f = landing ? front : h.pF1 + 0.3f;
                if (VKit.Chance(rng, 0.75f)) VKit.Pot(mb, h.At(doorS - 1.05f, y, f), VKit.Rnd(rng, 0.9f, 1.4f), rng, VKit.Chance(rng, 0.25f));
                if (VKit.Chance(rng, 0.5f)) VKit.Pot(mb, h.At(doorS + 1.05f, y, f), VKit.Rnd(rng, 0.9f, 1.3f), rng, VKit.Chance(rng, 0.25f));
            }
            bool terrace = h.zone != 0 && h.apron >= 0.9f;
            if (terrace)
            {
                int n = 1 + rng.Next(3);
                for (int i = 0; i < n; i++)
                {
                    float s = Mathf.Lerp(h.pS0 + 1.2f, h.pS1 - 1.2f, (float)rng.NextDouble());
                    if (s > gateA - 0.35f && s < gateB + 0.35f) continue;           // not on the gate post
                    VKit.Pot(mb, h.At(s, 0.9f + VWalls.CopeH - 0.02f, h.pF1 - 0.12f), VKit.Rnd(rng, 0.8f, 1.1f), rng, VKit.Chance(rng, 0.3f));
                }
                if (h.apron >= 1.6f && VKit.Chance(rng, 0.35f) && !ex.pergola)
                    HarbourGen.TableSet(mb, h.At(-doorS * 0.5f + VKit.Rnd(rng, -0.6f, 0.6f), 0f, h.hd + h.apron * 0.5f), Yaw(h) + 90f + VKit.Rnd(rng, -12f, 12f), rng, 2, false);   // chairs along the terrace
            }
            if (h.stairSide != 0 && VKit.Chance(rng, 0.6f))
            {
                // a pot on one of the lower steps
                int n = Mathf.Max(2, Mathf.RoundToInt(h.MainH / VWalls.StepRise)), step = 2 + rng.Next(3);
                float sr = h.MainH / n, sd = h.hd * 2f / n;
                VKit.Pot(mb, h.At(h.stairSide * (h.hw + 0.66f), sr * step, h.hd - sd * (step - 0.5f)), 0.8f, rng);
            }
            if (!h.noGroundOpenings && landing && VKit.Chance(rng, near ? 0.45f : 0.2f) && !ex.pergola)
            {
                float s = (h.hw - 0.5f) * (doorS > 0f ? -1f : 1f);
                float arc = doorPlaced ? Mathf.Abs(doorS - s) + 0.6f : 0f;
                VWalls.Bougainvillea(mb, h.At(s, 0f, h.hd), h.S * (s > 0f ? -1f : 1f), h.F, Mathf.Min(h.MainH * 0.92f, 3.6f), 3.2f + VKit.Hash(h.seed, 131), rng, arc);
                hadBoug = true;
            }
            else if (h.zone == 0 && VKit.Chance(rng, 0.16f))
            {
                float s = (h.hw - 0.5f) * (doorS > 0f ? -1f : 1f);
                float arc = doorPlaced ? Mathf.Abs(doorS - s) + 0.6f : 0f;
                VWalls.Bougainvillea(mb, h.At(s, Config.QuayHeight + 0.03f - h.c.y, h.hd), h.S * (s > 0f ? -1f : 1f), h.F, Mathf.Min(h.MainH * 0.92f, 3.8f), 3.2f + VKit.Hash(h.seed, 131), rng, arc);
                hadBoug = true;
            }
            if (h.awning && !h.noGroundOpenings)
            {
                float reach = h.zone == 0 ? 2.4f : 1.7f;
                float y = h.zone == 0 ? Config.QuayHeight - h.c.y : 0f;                 // harbour posts stand on the quay slab
                if (h.zone == 0 || h.apron <= 0f || h.apron > reach + 0.3f)
                    VWalls.Awning(mb, h.At(-h.hw + 0.25f, y, h.hd), h.S, h.F, h.hw * 2f - 0.5f, 2.72f - y, reach, 0.5f, h.zone != 0 && h.apron <= 0f);
            }
            // ---- dressing stream from here on ----
            if (ex.pergola) Pergola(mb, h);
            if (ex.boug && !hadBoug) ExtraBougainvillea(mb, h);
        }

        /// <summary>
        /// Timber pergola over the terrace in front of the main facade: whitewashed pillars with plaster caps on the terrace edge,
        /// a front beam, a wall plate, rafters and slats (a canvas shade over a cafe), a bougainvillea over one end; on some a table.
        /// </summary>
        static void Pergola(MeshBuilder mb, HouseDef h)
        {
            float fW = h.hd, fP = h.pF1 - 0.42f, yaw = Yaw(h);
            if (fP - fW < 0.95f) return;
            float maxHalf = h.hw - 0.35f;
            float half = ex.cafe ? maxHalf : Mathf.Min(maxHalf, VKit.Rnd(dr, 1.6f, 2.5f));
            if (half < 1.3f) return;
            float sc = ex.cafe ? 0f : Mathf.Clamp(doorPlaced ? doorS : 0f, -maxHalf + half, maxHalf - half);
            float s0 = sc - half, s1 = sc + half, sm = sc;
            Vector3 up = Vector3.up;
            // pillars at both ends and at most 3.2 m apart, moved off the gate and the head of the flight
            int np = Mathf.Max(2, Mathf.CeilToInt((s1 - s0) / 3.2f) + 1);
            for (int i = 0; i < np; i++)
            {
                float s = Mathf.Lerp(s0, s1, i / (float)(np - 1));
                if (gateB > gateA && s > gateA - 0.3f && s < gateB + 0.3f)
                {
                    s = s - gateA < gateB - s ? gateA - 0.3f : gateB + 0.3f;
                    if (s < s0 - 0.01f || s > s1 + 0.01f) continue;
                }
                Vector3 b = h.At(s, 0f, fP);
                VKit.BoxOn(mb, h.wall, b, 0.24f, BeamY, 0.24f, yaw);
                VKit.Box(mb, VKit.Trim, b + up * (BeamY + 0.03f), h.S * 0.15f, up * 0.03f, h.F * 0.15f);
            }
            float hl = half + 0.22f;
            VKit.Box(mb, VKit.Wood, h.At(sm, BeamY + 0.14f, fP), h.S * hl, up * 0.08f, h.F * 0.07f);                         // front beam
            VKit.Box(mb, VKit.WoodDk, h.At(sm, BeamY + 0.14f, fW + 0.05f), h.S * (half + 0.1f), up * 0.08f, h.F * 0.05f, 32);  // wall plate
            float rEnd = fP + 0.3f, rTop = BeamY + 0.22f;
            int nr = Mathf.Max(3, Mathf.RoundToInt((s1 - s0) / 0.55f) + 1);
            for (int i = 0; i < nr; i++)
            {
                float s = Mathf.Lerp(s0 - 0.12f, s1 + 0.12f, i / (float)(nr - 1));
                VKit.Box(mb, VKit.Wood, h.At(s, rTop + 0.055f, (fW + rEnd) * 0.5f), h.S * 0.04f, up * 0.055f, h.F * ((rEnd - fW) * 0.5f), 32);
            }
            float cTop = rTop + 0.11f;
            if (ex.cafe)
                VKit.Q2(mb, VKit.Canvas, h.At(s0 - 0.2f, cTop + 0.02f, fW + 0.05f), h.At(s1 + 0.2f, cTop + 0.02f, fW + 0.05f), h.At(s1 + 0.2f, cTop + 0.02f, rEnd), h.At(s0 - 0.2f, cTop + 0.02f, rEnd));
            else
                for (float f = fW + 0.22f; f < rEnd - 0.05f; f += 0.27f)
                    VKit.Box(mb, VKit.Wood, h.At(sm, cTop + 0.015f, f), h.S * (half + 0.2f), up * 0.015f, h.F * 0.03f);
            // blossom over one end, its stem up the pillar
            float se = VKit.Chance(dr, 0.5f) ? s0 : s1;
            Vector3 stem = h.At(se, 0f, fP) + h.F * 0.16f;
            mb.Cylinder(VKit.WoodDk, stem, stem + up * (BeamY + 0.2f), 0.035f, 0.03f, 5, false);
            int lumps = 2 + dr.Next(2);
            for (int i = 0; i < lumps; i++)
            {
                float r = VKit.Rnd(dr, 0.4f, 0.62f);
                Vector3 p = h.At(se + (se < sm ? 1f : -1f) * VKit.Rnd(dr, 0f, 1.1f), cTop + r * 0.35f, Mathf.Lerp(fW + 0.4f, rEnd, VKit.Rnd(dr, 0.3f, 1f)));
                VKit.Blob(mb, i == 1 ? VKit.Plant : VKit.Boug, p, h.S * r, up * (r * 0.55f), h.F * (r * 0.9f), dr, 0.22f, 6, 3);
            }
            Vector3 hang = h.At(se, BeamY - 0.2f, fP + 0.1f);
            VKit.Blob(mb, VKit.Boug, hang, h.S * 0.35f, up * 0.45f, h.F * 0.25f, dr, 0.22f, 6, 3);
            TopY = Mathf.Max(TopY, h.c.y + cTop + 0.8f);
            // tables under it: a row along a cafe terrace, one on some private terraces (never in front of the door)
            float fm = (fW + fP) * 0.5f;
            if (ex.cafe)
            {
                for (float s = s0 + 1.4f; s <= s1 - 1.4f + 0.01f; s += 2.7f)
                {
                    if (doorPlaced && Mathf.Abs(s - doorS) < 2f) continue;
                    HarbourGen.TableSet(mb, h.At(s, 0f, fm), yaw + 90f + VKit.Rnd(dr, -8f, 8f), dr, 2, false);
                }
                for (int i = 0; i < 3; i++)
                {
                    float s = Mathf.Lerp(h.pS0 + 0.5f, h.pS1 - 0.5f, (i + 0.5f) / 3f);
                    if (s > gateA - 0.35f && s < gateB + 0.35f) continue;
                    VKit.Pot(mb, h.At(s, 0.9f + VWalls.CopeH - 0.02f, h.pF1 - 0.12f), VKit.Rnd(dr, 0.75f, 0.95f), dr, true);
                }
            }
            else if (VKit.Chance(dr, 0.45f) && s1 - s0 >= 2.8f)
            {
                float s = sm;
                if (doorPlaced && Mathf.Abs(s - doorS) < 2f) s = doorS > sm ? s0 + 1.4f : s1 - 1.4f;
                if (!doorPlaced || Mathf.Abs(s - doorS) >= 2f) HarbourGen.TableSet(mb, h.At(s, 0f, fm), yaw + 90f + VKit.Rnd(dr, -8f, 8f), dr, 2, false);
            }
        }

        /// <summary>Bougainvillea spilling over the terrace wall, or over the roof parapet where there is no terrace.</summary>
        static void ExtraBougainvillea(MeshBuilder mb, HouseDef h)
        {
            float len = VKit.Rnd(dr, 1.4f, 2.6f);
            if (h.apron >= 0.9f)
            {
                len = Mathf.Min(len, (h.pS1 - h.pS0) * 0.4f);
                float s = doorS > 0f ? h.pS0 + 0.3f : h.pS1 - 0.3f - len;
                if (gateB > gateA && s + len > gateA - 0.2f && s < gateB + 0.2f) return;
                Vector3 a = h.At(s, 0.9f + VWalls.CopeH * 0.5f, h.pF1), b = h.At(s + len, 0.9f + VWalls.CopeH * 0.5f, h.pF1);
                Vector3 ta = a + h.F * 0.6f, tb = b + h.F * 0.6f;
                if (VKit.PathGap(ta.x, ta.z) > 0.3f && VKit.PathGap(tb.x, tb.z) > 0.3f) VWalls.Drape(mb, a, b, h.F, dr);
            }
            else if (!h.vault && ex.room > 0.6f)
            {
                len = Mathf.Min(len, h.hw * 0.9f);
                float s = doorS > 0f ? -h.hw + 0.2f : h.hw - 0.2f - len;
                float y = h.MainH + VWalls.ParapetH + VWalls.CopeH * 0.5f;
                Vector3 a = h.At(s, y, h.hd), b = h.At(s + len, y, h.hd);
                Vector3 ta = a + h.F * 0.6f, tb = b + h.F * 0.6f;
                if (VKit.PathGap(ta.x, ta.z) > 0.3f && VKit.PathGap(tb.x, tb.z) > 0.3f) VWalls.Drape(mb, a, b, h.F, dr);
            }
        }

        /// <summary>On the main flat roof: a small blue dome, a cistern or a table, pots on the parapet corners.</summary>
        static void RoofDressing(MeshBuilder mb, HouseDef h)
        {
            if (h.vault) return;
            if (ex.dome == 3) { Church(mb, h); return; }
            float H = h.MainH, yaw = Yaw(h);
            const float inner = VWalls.ParapetT + 0.08f;
            float side = chimS < 90f ? (chimS > 0f ? -1f : 1f) : (VKit.Chance(dr, 0.5f) ? 1f : -1f);   // away from the chimney
            if (ex.dome == 2 && roomBuilt)
            {
                SmallDome(mb, h.wall, roomTop, ex.domeR, 0.08f, h.S);
                TopY = Mathf.Max(TopY, roomTop.y + 0.08f + ex.domeR + 0.6f);
            }
            else if (ex.dome == 1)
            {
                float rr = ex.domeR + 0.13f;
                Vector3 b = h.At(side * (h.hw - inner - rr), H, 0f);
                SmallDome(mb, h.wall, b, ex.domeR, 0.45f, h.S);
                TopY = Mathf.Max(TopY, b.y + 0.45f + ex.domeR + 0.6f);
            }
            else if (ex.cistern)
            {
                Vector3 up = Vector3.up;
                Vector3 b = h.At(side * (h.hw - inner - 0.5f), H, -(h.hd - inner - 0.42f));
                VKit.BoxOn(mb, h.wall, b, 0.95f, 0.72f, 0.8f, yaw);
                VWalls.Cornice(mb, b + up * 0.72f, h.S, h.F, 0.475f, 0.4f, 0.7f);
                VKit.BoxOn(mb, VKit.Iron, b + up * 0.72f, 0.4f, 0.035f, 0.34f, yaw);
                Vector3 vent = b + up * 0.72f + h.S * (side * -0.3f) + h.F * 0.22f;
                mb.Cylinder(VKit.Iron, vent, vent + up * 0.26f, 0.03f, 0.03f, 6, true);
                TopY = Mathf.Max(TopY, b.y + 1f);
            }
            else if (ex.roofSeats && near && h.hw >= 1.8f)
            {
                HarbourGen.TableSet(mb, h.At(0f, H, 0.15f), yaw + 90f + VKit.Rnd(dr, -6f, 6f), dr, 2, false);
                TopY = Mathf.Max(TopY, h.c.y + H + 0.95f);
            }
            if (ex.roofPots)
            {
                float y = H + VWalls.ParapetH + VWalls.CopeH - 0.02f;
                for (int k = -1; k <= 1; k += 2)
                    if (VKit.Chance(dr, 0.7f)) VKit.Pot(mb, h.At(k * (h.hw - 0.12f), y, h.hd - 0.12f), 0.7f, dr, VKit.Chance(dr, 0.5f));
            }
        }

        /// <summary>
        /// A rooftop church (a family chapel on the house, the Cycladic way): a whitewashed drum with a ring of lit windows, a
        /// moulded cornice, a big enamel-blue dome with a lantern and a cross at one end of the roof, and a bell gable on the
        /// front parapet at the other end (ChurchLayout keeps them apart and under the roof rule). The hero church (the quay
        /// look) has a bigger dome on a taller drum that stands proud of the facade on a moulded corbel, and a bigger lantern.
        /// </summary>
        static void Church(MeshBuilder mb, HouseDef h)
        {
            Vector3 up = Vector3.up;
            bool hero = ex.hero;
            float H = h.MainH, r = ex.domeR, R = r + 0.05f, drumH = DrumH(hero), lk = hero ? HeroLanternK : 1f;
            int seg = hero ? HeroSeg : near ? 24 : 20;                            // even: the windows sit on every other face
            Vector3 b = h.At(ex.domeS, H, 0f);
            // the hero's drum is wider than the house is deep: a corbel under it, closed below (the quay looks up at it)
            if (hero) mb.Cylinder(VKit.Trim, b - up * 0.24f, b, R - 0.16f, R, seg, true);
            mb.Cylinder(h.wall, b, b + up * drumH, R, R, seg, false);
            if (hero) ChapelGen.DrumWindows(mb, b, seg, R, 0.72f, 1.22f, 0.13f);  // clear of the parapet's coping: the whole ring shows
            else ChapelGen.DrumWindows(mb, b, seg, R, 0.38f, 0.76f, 0.11f);       // above the parapet, lit with the windows
            Vector3 t = b + up * drumH;
            mb.Cylinder(VKit.Trim, t - up * 0.015f, t, R, r + 0.15f, seg, false);                   // underside of the cornice
            mb.Cylinder(VKit.Trim, t, t + up * 0.08f, r + 0.15f, r + 0.15f, seg, false);
            mb.Cylinder(VKit.Trim, t + up * 0.08f, t + up * 0.11f, r + 0.15f, r - 0.02f, seg, false);  // its sloping top
            Vector3 dBase = t + up * 0.08f;
            mb.Dome(VKit.DomeBlue, dBase, r, 1f, seg, hero ? 10 : near ? 8 : 6);
            Vector3 apex = dBase + up * r;
            // lantern: a little white drum, a plaster ring and a blue cap, the cross on top
            int ls = hero ? 12 : 10;
            mb.Cylinder(h.wall, apex - up * (0.08f * lk), apex + up * (0.3f * lk), 0.24f * lk, 0.24f * lk, ls, false);
            mb.Cylinder(VKit.Trim, apex + up * (0.3f * lk), apex + up * (0.34f * lk), 0.29f * lk, 0.29f * lk, ls, true);
            mb.Dome(VKit.DomeBlue, apex + up * (0.34f * lk), 0.24f * lk, 0.8f, ls, 3);
            ChapelGen.Cross(mb, apex + up * LanternH(hero), h.S);
            float top = apex.y + LanternH(hero) + ChapelGen.CrossH;
            if (ex.gableK > 0f)
            {
                Vector3 foot = GableFoot(h, ex.gableS, ex.gableK);                // flush with the facade
                ChapelGen.BellGable(mb, foot, h.F, ex.gableK, h.wall);
                top = Mathf.Max(top, foot.y + ChapelGen.GableTop * ex.gableK + ChapelGen.CrossH);
            }
            TopY = Mathf.Max(TopY, top + 0.05f);
        }

        /// <summary>A small chapel dome on a roof: whitewashed drum, plaster cornice, Santorini-blue dome, lantern stub and cross.</summary>
        static void SmallDome(MeshBuilder mb, Material wall, Vector3 basePos, float r, float drumH, Vector3 arm)
        {
            Vector3 up = Vector3.up;
            int seg = near ? 18 : 14;
            if (drumH > 0.01f) mb.Cylinder(wall, basePos, basePos + up * drumH, r + 0.05f, r + 0.05f, seg, false);
            Vector3 t = basePos + up * drumH;
            mb.Cylinder(VKit.Trim, t - up * 0.012f, t, r + 0.05f, r + 0.13f, seg, false);                 // underside of the cornice
            mb.Cylinder(VKit.Trim, t, t + up * 0.07f, r + 0.13f, r + 0.13f, seg, false);
            mb.Cylinder(VKit.Trim, t + up * 0.07f, t + up * 0.095f, r + 0.13f, r - 0.02f, seg, false);   // its sloping top
            mb.Dome(VKit.DomeBlue, t + up * 0.07f, r, 1f, seg, near ? 6 : 4);
            Vector3 tip = t + up * (0.07f + r - 0.03f);
            mb.Cylinder(wall, tip, tip + up * 0.12f, 0.08f, 0.06f, 6, true);
            Vector3 f = Vector3.Cross(up, arm).normalized;
            VKit.Box(mb, VKit.Trim, tip + up * 0.32f, arm * 0.025f, up * 0.2f, f * 0.025f, 8);
            VKit.Box(mb, VKit.Trim, tip + up * 0.4f, arm * 0.12f, up * 0.025f, f * 0.025f);
        }
    }
}
