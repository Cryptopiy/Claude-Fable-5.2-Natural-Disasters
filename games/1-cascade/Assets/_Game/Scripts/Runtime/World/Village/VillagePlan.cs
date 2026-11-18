using System.Collections.Generic;
using UnityEngine;

namespace CASCADE
{
    /// <summary>One house of the seeded plan. The intact and the ruined generator both read this, so they share footprints.</summary>
    internal sealed class HouseDef
    {
        public int id, seed;
        public int zone;                   // 0 harbour row, 1 cliff terraces, 2 narrow-stair flank, 3 rim village, 4 below the rim, 5 farm
        public Vector3 c;                  // centre of the main box, y = floor level
        public Vector3 F, S;               // facade normal and side axis (S = up x F), unit and horizontal
        public float hw, hd;               // half width along S, half depth along F
        public int storeys = 1;
        public float storeyH = 3f;
        public bool vault;
        public int wingSide;               // 0 none, +1/-1: a second box on that side (along S)
        public float wingHw, wingHd, wingFront;   // wing half sizes; wingFront = how far its facade stands in front (+) of the main facade
        public int wingStoreys = 1;
        public bool wingVault;
        public bool roofBox;               // a small cube on the main roof
        public float apron;                // terrace in front of the facade, metres (0 = none)
        public int stairSide;              // 0 none, +1/-1: exterior stair to the roof along that side
        public int frontStair;             // 0 none, +1/-1: steps down the face of the foundation, descending towards that side
        public float reach;                // how far such a flight stands in front of the platform (kept clear like the platform)
        public bool lowerWindows;          // windows in a tall foundation wall
        public bool backWindows;           // the back wall is seen too (it hangs over the cliff): windows there as well
        public bool cafe, awning, noGroundOpenings, plainSides, noChimney;
        public float baseY;                // bottom of the foundation
        public float roofTop;              // highest point of the house
        public float doorS = 99f;          // position of the front door along S (set by the generator; 99 = none)
        public int band;
        public Material wall;
        // platform (foundation) rectangle in the house frame, relative to c
        public float pS0, pS1, pF0, pF1;

        public float MainH { get { return storeys * storeyH; } }
        public float WingH { get { return wingStoreys * storeyH; } }
        public Vector3 At(float s, float y, float f) { return c + S * s + Vector3.up * y + F * f; }
        public float WingS { get { return wingSide * (hw + wingHw); } }                 // wing centre along S
        public float WingF { get { return hd + wingFront - wingHd; } }                  // wing centre along F
    }

    /// <summary>Placement rules shared by all zones: footprints, foundations, path clearance, roof heights, overlap.</summary>
    internal static class VillagePlan
    {
        public const float HouseClear = 1.5f;      // houses keep HalfWidth + this from the path centre
        public const float RoofBelowStair = 1.2f;  // roofs on the downhill side of the stair-road stay this far below the path
        public const float RoofBelowProm = 1.5f;   // and this far below the promenade
        public const float RoofRuleRadius = 10f;   // the roof rule applies to houses within this distance of the path
        public const float RoofRuleAbeam = 0.6f;   // ... and compares the roof with the path points this close to the nearest one
        public const float LandwardCos = 0f;       // ... unless they lie on its land side (raise towards 1 to exempt only houses straight inland)
        public const float FoundationDepth = 1.4f; // retaining walls go this far below the lowest ground sample (covers a coarse terrain mesh)
        public const float MaxFoundation = 11f;    // reject sites that would need a taller retaining wall
        public const float IntactAbove = 145.5f;   // floors above this are never flooded (Config.MaxRunup)

        public static readonly List<HouseDef> Houses = new List<HouseDef>(160);
        public static Vector3 ChapelC, ChapelAxis;  // nave centre (floor level) and the unit axis pointing from the door into the nave

        public static int BandOf(float y)
        {
            return y < 12f ? 0 : y < 35f ? 1 : y < 60f ? 2 : y < 85f ? 3 : y < 110f ? 4 : y < 130f ? 5 : y < 150f ? 6 : 7;
        }

        /// <summary>Computes the platform rectangle (main box, wing, apron, stair strip) in the house frame.</summary>
        public static void Platform(HouseDef h)
        {
            h.pS0 = -h.hw; h.pS1 = h.hw; h.pF0 = -h.hd; h.pF1 = h.hd;
            if (h.wingSide != 0)
            {
                float s = h.WingS, f = h.WingF;
                h.pS0 = Mathf.Min(h.pS0, s - h.wingHw); h.pS1 = Mathf.Max(h.pS1, s + h.wingHw);
                h.pF0 = Mathf.Min(h.pF0, f - h.wingHd); h.pF1 = Mathf.Max(h.pF1, f + h.wingHd);
            }
            h.pF1 += h.apron;
            if (h.stairSide > 0) h.pS1 = Mathf.Max(h.pS1, h.hw + 1.0f);
            if (h.stairSide < 0) h.pS0 = Mathf.Min(h.pS0, -h.hw - 1.0f);
        }

        /// <summary>Sets the floor to the highest ground under the boxes (the uphill edge) and the foundation below the lowest ground sample.</summary>
        public static void Seat(HouseDef h, bool floorFromGround)
        {
            Platform(h);
            float hi = -1e9f, lo = 1e9f;
            for (int i = 0; i <= 4; i++)
                for (int j = 0; j <= 4; j++)
                {
                    float f = Mathf.Lerp(h.pF0, h.pF1 + h.reach, j / 4f);
                    Vector3 p = h.At(Mathf.Lerp(h.pS0, h.pS1, i / 4f), 0f, f);
                    float g = VKit.Ground(p.x, p.z);
                    if (g < lo) lo = g;
                    // the apron may hang over lower ground; the floor follows the ground under the boxes only
                    if (f <= h.pF1 - h.apron + 0.01f && g > hi) hi = g;
                }
            if (floorFromGround) h.c.y = hi + 0.06f;
            h.baseY = Mathf.Min(lo, h.c.y) - FoundationDepth;
            h.band = BandOf(h.c.y);
            // roof line: parapet, vault crown or roof room (chimneys are left off houses that touch the roof rule)
            float top = h.MainH + (h.vault ? Mathf.Min(Mathf.Min(h.hd, h.hw), 2.6f) * 0.62f : VWalls.ParapetH) + (h.roofBox ? 2.3f - VWalls.ParapetH : 0f);
            if (h.wingSide != 0) top = Mathf.Max(top, h.WingH + (h.wingVault ? Mathf.Min(Mathf.Min(h.wingHd, h.wingHw), 2.6f) * 0.62f : VWalls.ParapetH));
            h.roofTop = h.c.y + top;
        }

        /// <summary>Smallest gap between the platform outline (and its front steps) and the path edge.</summary>
        public static float MinPathGap(HouseDef h, float grow)
        {
            float best = 99f;
            float s0 = h.pS0 - grow, s1 = h.pS1 + grow, f0 = h.pF0 - grow, f1 = h.pF1 + h.reach + grow;
            int ns = Mathf.CeilToInt((s1 - s0) / 0.8f), nf = Mathf.CeilToInt((f1 - f0) / 0.8f);
            for (int i = 0; i <= ns; i++)
            {
                float s = Mathf.Lerp(s0, s1, i / (float)ns);
                Vector3 a = h.At(s, 0f, f0), b = h.At(s, 0f, f1);
                best = Mathf.Min(best, Mathf.Min(VKit.PathGap(a.x, a.z), VKit.PathGap(b.x, b.z)));
            }
            for (int j = 0; j <= nf; j++)
            {
                float f = Mathf.Lerp(f0, f1, j / (float)nf);
                Vector3 a = h.At(s0, 0f, f), b = h.At(s1, 0f, f);
                best = Mathf.Min(best, Mathf.Min(VKit.PathGap(a.x, a.z), VKit.PathGap(b.x, b.z)));
            }
            Vector3 m = h.At((s0 + s1) * 0.5f, 0f, (f0 + f1) * 0.5f);
            return Mathf.Min(best, VKit.PathGap(m.x, m.z));
        }

        /// <summary>
        /// How far the roof sticks up into the view of a walker on the piece of path beside the house (positive = too high).
        /// Only the path points abeam of the house count (its nearest ones, within RoofRuleRadius), and only houses that are
        /// not clearly on the land side (east of the stair-road, right of the promenade): a house north or south of a
        /// hairpin counts, so the landings keep their view down to the harbour.
        /// </summary>
        public static float RoofExcess(HouseDef h)
        {
            Vector3 m = h.At((h.pS0 + h.pS1) * 0.5f, 0f, (h.pF0 + h.pF1) * 0.5f);
            float reach = RoofRuleRadius + (h.pS1 - h.pS0) + (h.pF1 - h.pF0);
            float worst = -99f, nearest = 1e9f;
            for (int pass = 0; pass < 2 && (pass == 0 || nearest <= RoofRuleRadius); pass++)
                for (int i = 0; i < PathDef.Count; i += 2)
                {
                    Vector3 p = PathDef.P[i];
                    float dx = p.x - m.x, dz = p.z - m.z;
                    float d2 = dx * dx + dz * dz;
                    if (d2 > reach * reach) continue;
                    int sec = PathDef.Sec[i];
                    if (sec == 2 || sec == 0) continue;                      // the narrow stair is walled in; the quay has the harbour row
                    float landward;                                          // > 0: the house stands on the land side of this path point
                    if (sec < 3) landward = -dx;
                    else { Vector3 d = PathDef.FlatDir(i * PathDef.Step); landward = -dx * d.z + dz * d.x; }
                    if (landward > LandwardCos * Mathf.Sqrt(d2)) continue;
                    // horizontal distance from the path point to the platform rectangle
                    Vector3 rel = new Vector3(p.x - h.c.x, 0f, p.z - h.c.z);
                    float s = Vector3.Dot(rel, h.S), f = Vector3.Dot(rel, h.F);
                    float ds = Mathf.Max(h.pS0 - s, 0f, s - h.pS1), df = Mathf.Max(h.pF0 - f, 0f, f - h.pF1);
                    float dist = Mathf.Sqrt(ds * ds + df * df);
                    if (pass == 0) { if (dist < nearest) nearest = dist; continue; }
                    if (dist > nearest + RoofRuleAbeam) continue;
                    float margin = sec >= 3 ? RoofBelowProm : RoofBelowStair;
                    worst = Mathf.Max(worst, h.roofTop - (p.y - margin));
                }
            return worst;
        }

        public static bool Overlaps(HouseDef h, float margin)
        {
            Vector2 c0 = Centre(h), a0 = new Vector2(h.S.x, h.S.z);
            float ha0 = (h.pS1 - h.pS0) * 0.5f + margin, hb0 = (h.pF1 + h.reach - h.pF0) * 0.5f + margin;
            for (int i = 0; i < Houses.Count; i++)
            {
                HouseDef o = Houses[i];
                if (VKit.RectsOverlap(c0, a0, ha0, hb0, Centre(o), new Vector2(o.S.x, o.S.z), (o.pS1 - o.pS0) * 0.5f, (o.pF1 + o.reach - o.pF0) * 0.5f)) return true;
            }
            // the chapel and its forecourt
            Vector2 cc = new Vector2(ChapelC.x, ChapelC.z), ca = new Vector2(ChapelAxis.x, ChapelAxis.z);
            return VKit.RectsOverlap(c0, a0, ha0, hb0, cc - ca * 6f, ca, 15f, 9.5f);
        }

        static Vector2 Centre(HouseDef h)
        {
            Vector3 m = h.At((h.pS0 + h.pS1) * 0.5f, 0f, (h.pF0 + h.pF1 + h.reach) * 0.5f);
            return new Vector2(m.x, m.z);
        }

        /// <summary>True when the point lies inside any platform (grown by margin) or the chapel block.</summary>
        public static bool Occupied(float x, float z, float margin)
        {
            for (int i = 0; i < Houses.Count; i++)
            {
                HouseDef o = Houses[i];
                Vector3 d = new Vector3(x, 0f, z) - new Vector3(o.c.x, 0f, o.c.z);
                float s = Vector3.Dot(d, o.S), f = Vector3.Dot(d, o.F);
                if (s > o.pS0 - margin && s < o.pS1 + margin && f > o.pF0 - margin && f < o.pF1 + o.reach + margin) return true;
            }
            Vector3 dc = new Vector3(x - ChapelC.x, 0f, z - ChapelC.z);
            float ca = Vector3.Dot(dc, ChapelAxis), cb = Vector3.Dot(dc, Vector3.Cross(Vector3.up, ChapelAxis));
            return ca > -9f - margin && ca < 6f + margin && Mathf.Abs(cb) < 7.5f + margin;
        }

        /// <summary>Full site test for a seated house. Tries to cure a roof that is too high by removing storeys.</summary>
        public static bool Accept(HouseDef h, bool floorFromGround, bool roofRule, float overlapMargin = 0.7f, float maxFoundation = MaxFoundation, float clear = HouseClear)
        {
            Seat(h, floorFromGround);
            if (MinPathGap(h, 0f) < clear) return false;
            if (h.c.y - h.baseY > maxFoundation) return false;
            if (roofRule)
            {
                for (int guard = 0; guard < 3 && RoofExcess(h) > 0f; guard++)
                {
                    if (h.roofBox) h.roofBox = false;
                    else if (h.storeys > 1) h.storeys--;
                    else if (h.wingSide != 0 && h.wingStoreys > 1) h.wingStoreys--;
                    else return false;
                    if (h.wingStoreys > h.storeys) h.wingStoreys = h.storeys;
                    Seat(h, floorFromGround);
                }
                float ex = RoofExcess(h);
                if (ex > 0f) return false;
                h.noChimney = ex > -1.5f;
            }
            if (Overlaps(h, overlapMargin)) return false;
            return true;
        }

        public static HouseDef New(System.Random rng, int zone, Vector3 centre, Vector3 facing, float width, float depth)
        {
            var h = new HouseDef { zone = zone, seed = rng.Next(1, int.MaxValue), c = centre, hw = width * 0.5f, hd = depth * 0.5f };
            facing.y = 0f;
            h.F = facing.normalized; h.S = Vector3.Cross(Vector3.up, h.F);
            h.storeyH = VKit.Rnd(rng, 2.85f, 3.2f);
            double w = rng.NextDouble();
            h.wall = w < 0.72 ? VKit.Wh : w < 0.87 ? VKit.WhWarm : VKit.WhOld;
            return h;
        }

        public static void AddWing(HouseDef h, System.Random rng, float width, float depth, int storeys, int side = 0)
        {
            h.wingSide = side != 0 ? side : (VKit.Chance(rng, 0.5f) ? 1 : -1);
            h.wingHw = width * 0.5f; h.wingHd = depth * 0.5f;
            h.wingFront = VKit.Rnd(rng, -0.9f, 0.5f);
            h.wingStoreys = storeys;
        }

        public static void Commit(HouseDef h)
        {
            h.id = Houses.Count;
            Houses.Add(h);
        }
    }
}
