using System.Collections.Generic;
using UnityEngine;

namespace CASCADE
{
    /// <summary>A bend of the stair-road (the U-turn off the quay and the three hairpins). outer: +1 = left side, -1 = right side.</summary>
    internal struct ArchBend
    {
        public float s0, s1, apex;
        public float terrace;       // centre chainage of the landing terrace (hairpins only)
        public int outer;
    }

    /// <summary>
    /// Sections 1 and 2: one continuous flight of real steps from the quay (y = 2) to the rim, and for the stair-road
    /// the side walls: a rounded white parapet where the ground falls away, a retaining wall with a terrace top where
    /// it rises. Hairpin landings, lamps and poles are in ArchLandings; the walls of the narrow stair in ArchNarrow.
    /// </summary>
    internal static class ArchStairs
    {
        public const float Rise = 0.18f;            // nominal riser
        public const float StepExt = 0.12f;         // steps continue this far under the side walls
        public const float NoseDepth = 0.07f;       // white-washed front strip of every tread
        public const float ParapetH = 1.0f, ParapetT = 0.3f;
        public const float WallGap = 0.05f;         // side walls start this far outside the walkable width
        public const float RetainMin = 2.2f, RetainMax = 3.2f;
        public const float RetainReach = 7f;        // how far the terrace top may run back to meet the ground
        public const float RetainDepthBend = 2.6f;  // fixed terrace depth on the inside of a bend (offset lines converge there)
        public const float RetainIf = 1.3f;         // natural ground this much above the path (3.5 m outside) -> retaining wall
        public const float RiserSkirt = 0.05f;      // every riser runs this far below the tread beneath it (no crack under a nose)
        public const float EdgeBand = 0.16f;        // white-washed band along both edges of every tread (inside the walkable width)
        public const float DrainSpacing = 4.5f;     // scuppers in the parapets
        public const float CopeOver = 0.035f, CopeDepth = 0.12f;   // plaster coping of the retaining walls

        public static readonly List<ArchBend> Bends = new List<ArchBend>(4);
        public static float FirstRiser;             // chainage of the first riser
        static float[] treadS;                      // chainage of riser k (1..n), treadS[n + 1] = end of the flight
        static float treadY0, treadRise;
        static int treadN;

        static readonly Vector3 Up = ArchGeo.Up;

        public static void Build(ArchCtx c)
        {
            FindBends();
            Steps(c);
            SideWalls(c);
            ArchLandings.Build(c);
        }

        /// <summary>Finds the curved stretches of the stair-road from the path itself (no hard-coded chainages).</summary>
        static void FindBends()
        {
            Bends.Clear();
            float sB = PathDef.SNarrowStart + 4f;
            bool inBend = false;
            float start = 0f, turn = 0f;
            for (float s = PathDef.SQuayEnd - 4f; s <= sB; s += 0.5f)
            {
                Vector3 f0 = PathDef.FlatDir(s - 1f), f1 = PathDef.FlatDir(s + 1f);
                float cr = f0.z * f1.x - f0.x * f1.z;          // > 0: turning right
                bool curved = Mathf.Abs(cr) > 0.08f;
                if (curved && !inBend) { inBend = true; start = s; turn = 0f; }
                if (curved) turn += cr;
                if (inBend && (!curved || s + 0.5f > sB))
                {
                    inBend = false;
                    var b = new ArchBend { s0 = start, s1 = s, apex = (start + s) * 0.5f, outer = turn > 0f ? 1 : -1 };
                    b.terrace = FindTerrace(b);
                    Bends.Add(b);
                }
            }
        }

        /// <summary>The landing terrace sits on the outside of the bend as close to the apex as the ground allows: the path
        /// climbs out of fill into a cutting around the apex, and the terrace must stay above the ground.</summary>
        static float FindTerrace(ArchBend b)
        {
            float half = ArchLandings.TerraceHalf;
            for (float sc = b.apex; sc >= b.s0 + half; sc -= 0.5f)
            {
                float floorY = PathDef.Pos(sc + half).y - ArchLandings.TerraceDrop;
                bool ok = true;
                for (int j = 0; j <= 4 && ok; j++)
                {
                    ArchFrame f = ArchGeo.Frame(Mathf.Lerp(sc - half, sc + half, j / 4f));
                    for (int k = 1; k <= 3 && ok; k++)
                        if (ArchGeo.Ground(f.At(b.outer * (f.hw + StepExt + ArchLandings.TerraceDepth * k / 3f))) > floorY - 0.05f) ok = false;
                }
                if (ok) return sc;
            }
            return b.s0 + half;
        }

        /// <summary>Treads and risers. Tread k sits at quay level + k * rise and covers the chainage where the smooth path
        /// line is within half a riser of it, so the camera line never leaves the step surface by more than 0.09 m.</summary>
        static void Steps(ArchCtx c)
        {
            float sA = PathDef.SQuayEnd, sB = PathDef.SStairEnd;
            float yA = Config.QuayHeight, yB = PathDef.Pos(sB).y;
            int n = Mathf.Max(1, Mathf.RoundToInt((yB - yA) / Rise));
            float rise = (yB - yA) / n;
            var rs = new float[n + 2];
            float s = sA - 3f, yPrev = PathDef.Pos(s).y;
            int k = 1;
            while (k <= n && s < sB)
            {
                float s2 = s + 0.05f, y2 = PathDef.Pos(s2).y;
                float target = yA + (k - 0.5f) * rise;
                while (k <= n && y2 >= target)
                {
                    rs[k] = y2 > yPrev ? Mathf.Lerp(s, s2, Mathf.Clamp01((target - yPrev) / (y2 - yPrev))) : s2;
                    k++;
                    target = yA + (k - 0.5f) * rise;
                }
                s = s2; yPrev = y2;
            }
            for (; k <= n; k++) rs[k] = sB;
            rs[n + 1] = sB;
            FirstRiser = rs[1];
            treadS = rs; treadY0 = yA; treadRise = rise; treadN = n;

            for (k = 1; k <= n; k++)
            {
                float s0 = rs[k], s1 = rs[k + 1];
                float y = yA + k * rise;
                ArchFrame a = ArchGeo.Frame(s0);
                float wa = a.hw + StepExt, ia = Mathf.Max(a.hw - EdgeBand, 0.3f);
                Vector3 aL = ArchGeo.Y(a.At(wa), y), aR = ArchGeo.Y(a.At(-wa), y);
                Vector3 aLi = ArchGeo.Y(a.At(ia), y), aRi = ArchGeo.Y(a.At(-ia), y);
                MeshBuilder mb = c.S(y);
                // riser: always built (also under a vanishing tread) and carried below the tread beneath it, so the
                // joint under every nose is backed by the riser face and can never open onto the void under the flight
                float yr = y - rise - RiserSkirt;
                ArchGeo.Quad(mb, c.White, ArchGeo.Y(aL, yr), ArchGeo.Y(aLi, yr), aLi, aL, -a.f);
                ArchGeo.Quad(mb, c.White, ArchGeo.Y(aLi, yr), ArchGeo.Y(aRi, yr), aRi, aLi, -a.f);
                ArchGeo.Quad(mb, c.White, ArchGeo.Y(aRi, yr), ArchGeo.Y(aR, yr), aR, aRi, -a.f);
                if (s1 - s0 < 0.01f) continue;

                ArchFrame b = ArchGeo.Frame(s1);
                float wb = b.hw + StepExt, ib = Mathf.Max(b.hw - EdgeBand, 0.3f);
                Vector3 bL = ArchGeo.Y(b.At(wb), y), bR = ArchGeo.Y(b.At(-wb), y);
                Vector3 bLi = ArchGeo.Y(b.At(ib), y), bRi = ArchGeo.Y(b.At(-ib), y);
                float len = Mathf.Max(ArchGeo.FlatDist(a.p, b.p), 0.05f);
                float fn = Mathf.Min(NoseDepth / len, 0.35f);
                Vector3 nL = Vector3.Lerp(aL, bL, fn), nR = Vector3.Lerp(aR, bR, fn);
                Vector3 nLi = Vector3.Lerp(aLi, bLi, fn), nRi = Vector3.Lerp(aRi, bRi, fn);
                ArchGeo.Quad(mb, c.White, aL, aLi, nLi, nL, Up);                 // white-washed nose (three pieces: shared edges)
                ArchGeo.Quad(mb, c.White, aLi, aRi, nRi, nLi, Up);
                ArchGeo.Quad(mb, c.White, aRi, aR, nR, nRi, Up);
                ArchGeo.Quad(mb, c.Stone, nLi, nRi, bRi, bLi, Up);               // stone tread
                ArchGeo.Quad(mb, c.White, nL, nLi, bLi, bL, Up);                 // white edge bands, running on under the walls
                ArchGeo.Quad(mb, c.White, nRi, nR, bR, bRi, Up);
            }
        }

        /// <summary>Height of the tread under chainage s (the path line elsewhere).</summary>
        public static float TreadY(float s)
        {
            int k = TreadIndex(s);
            return k < 0 ? PathDef.Pos(s).y : treadY0 + k * treadRise;
        }

        /// <summary>Middle chainage of the tread under s (s itself off the flight).</summary>
        public static float TreadMid(float s)
        {
            int k = TreadIndex(s);
            if (k < 1) return s;
            return (treadS[k] + treadS[Mathf.Min(k + 1, treadN + 1)]) * 0.5f;
        }

        static int TreadIndex(float s)
        {
            if (treadS == null || s < treadS[1] - 3f || s > treadS[treadN + 1]) return -1;
            int lo = 1, hi = treadN, k = 0;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                if (treadS[mid] <= s) { k = mid; lo = mid + 1; } else hi = mid - 1;
            }
            return k;
        }

        /// <summary>True inside the landing terrace of a hairpin on that side (the terrace replaces the side wall there).</summary>
        public static bool InTerrace(float s, int side)
        {
            for (int i = 1; i < Bends.Count; i++)
                if (Bends[i].outer == side && s > Bends[i].terrace - ArchLandings.TerraceHalf && s < Bends[i].terrace + ArchLandings.TerraceHalf) return true;
            return false;
        }

        static void SideWalls(ArchCtx c)
        {
            float sStart = FirstRiser - 0.15f, sEnd = PathDef.SNarrowStart;
            // stations: about 1 m apart, with the terrace ends as exact break points
            var brk = new List<float> { sStart };
            for (int i = 1; i < Bends.Count; i++) { brk.Add(Bends[i].terrace - ArchLandings.TerraceHalf); brk.Add(Bends[i].terrace + ArchLandings.TerraceHalf); }
            brk.Add(sEnd);
            brk.Sort();
            var st = new List<float>(320);
            for (int i = 0; i < brk.Count - 1; i++)
            {
                float a = Mathf.Clamp(brk[i], sStart, sEnd), b = Mathf.Clamp(brk[i + 1], sStart, sEnd);
                if (b - a < 0.05f) continue;
                int m = Mathf.Max(1, Mathf.CeilToInt((b - a) / 1.0f));
                for (int j = 0; j < m; j++) st.Add(Mathf.Lerp(a, b, j / (float)m));
            }
            st.Add(sEnd);
            int n = st.Count;

            for (int side = 1; side >= -1; side -= 2)
            {
                // classify: does the natural ground stand above the path on this side?
                var ret = new bool[n];
                for (int i = 0; i < n; i++)
                {
                    ArchFrame f = ArchGeo.Frame(st[i]);
                    ret[i] = GroundIsHigh(f, side);
                }
                for (int pass = 0; pass < 2; pass++)           // majority filter: no walls shorter than a few metres
                {
                    var sm = new bool[n];
                    for (int i = 0; i < n; i++)
                    {
                        int cnt = 0, tot = 0;
                        for (int j = -3; j <= 3; j++) { int q = i + j; if (q < 0 || q >= n) continue; tot++; if (ret[q]) cnt++; }
                        sm[i] = cnt * 2 > tot;
                    }
                    ret = sm;
                }
                // runs of equal kind, broken by the terraces
                int i0 = 0;
                while (i0 < n - 1)
                {
                    if (InTerrace((st[i0] + st[i0 + 1]) * 0.5f, side)) { i0++; continue; }
                    int i1 = i0;
                    while (i1 < n - 1 && ret[i1] == ret[i0] && !InTerrace((st[i1] + st[i1 + 1]) * 0.5f, side)) i1++;
                    if (ret[i0]) Retaining(c, side, st, i0, i1); else Parapet(c, side, st, i0, i1);
                    i0 = i1;
                }
            }
        }

        /// <summary>Does the natural ground (path not carved in) stand above the path on that side, 3.5 m outside the walkable width?</summary>
        public static bool GroundIsHigh(ArchFrame f, int side)
        {
            Vector3 q = f.At(side * (f.hw + 3.5f));
            return WorldGen.BaseHeight(q.x, q.z, 100f) - f.p.y > RetainIf;
        }

        /// <summary>True where that side of the stair-road gets the low parapet (with a safety margin along the path).</summary>
        public static bool SideIsLow(float s, int side)
        {
            for (float d = -2.5f; d <= 2.5f; d += 2.5f)
            {
                ArchFrame f = ArchGeo.Frame(s + d);
                Vector3 q = f.At(side * (f.hw + 3.5f));
                if (WorldGen.BaseHeight(q.x, q.z, 100f) - f.p.y > RetainIf - 0.5f) return false;
            }
            return !InTerrace(s, side);
        }

        /// <summary>True on the inside of a bend, where offset lines converge on the bend centre.</summary>
        static bool InsideBend(float s, int side)
        {
            for (int i = 0; i < Bends.Count; i++)
                if (Bends[i].outer == -side && s > Bends[i].s0 - 1.5f && s < Bends[i].s1 + 1.5f) return true;
            return false;
        }

        /// <summary>White parapet with a rounded plaster coping along stations i0..i1, with a scupper every few metres
        /// (iron grille at the foot on the path side, stone spout on the drop side).</summary>
        static void Parapet(ArchCtx c, int side, List<float> st, int i0, int i1)
        {
            var pts = new List<Vector3>(i1 - i0 + 1);
            var below = new List<float>(i1 - i0 + 1);
            for (int i = i0; i <= i1; i++)
            {
                ArchFrame f = ArchGeo.Frame(st[i]);
                Vector3 p = f.At(side * (f.hw + WallGap + ParapetT * 0.5f));
                pts.Add(p);
                below.Add(ArchGeo.Depth(p, f.l * side, 2.0f, 1.0f, 9f));   // the upper half of every leg runs on fill
            }
            ArchGeo.Sweep(c, false, pts, ArchGeo.Coped(c.White, c.Trim, ParapetT, ParapetH), below);

            float a = st[i0] + 1.6f, b = st[i1] - 1.2f;
            int nd = Mathf.FloorToInt((b - a) / DrainSpacing);
            for (int j = 0; j <= nd; j++)
            {
                float sc = TreadMid(a + (b - a) * (nd > 0 ? j / (float)nd : 0.5f));
                if (InTerrace(sc, side) || InsideBend(sc, side)) continue;
                ArchFrame f = ArchGeo.Frame(sc);
                float y = TreadY(sc);
                Vector3 inner = ArchGeo.Y(f.At(side * (f.hw + WallGap)), y), outer = ArchGeo.Y(f.At(side * (f.hw + WallGap + ParapetT)), y);
                ArchDetail.Drain(c.S(y), c, inner, outer, f.l * side);
            }
        }

        /// <summary>Height of the retaining wall above the path: wanders slowly between 2.2 and 3.2 m.</summary>
        public static float RetainHeight(float s)
        {
            return Mathf.Lerp(RetainMin, RetainMax, 0.5f + 0.5f * MathX.Noise1(s * 0.11f, 77));
        }

        /// <summary>Weathered retaining wall; its top runs back as a terrace until it meets the rising ground.</summary>
        static void Retaining(ArchCtx c, int side, List<float> st, int i0, int i1)
        {
            Vector3[] prev = null;
            for (int i = i0; i <= i1; i++)
            {
                ArchFrame f = ArchGeo.Frame(st[i]);
                float hr = RetainHeight(f.s);
                float o0 = f.hw + WallGap;
                float depth = 0.6f;
                if (InsideBend(f.s, side)) depth = RetainDepthBend;
                else
                    for (float a = 0.6f; a <= RetainReach; a += 0.4f)
                    {
                        Vector3 q = f.At(side * (o0 + a));
                        if (WorldGen.Height(q.x, q.z) >= f.p.y + hr - 0.1f) { depth = a + 0.6f; break; }
                    }
                // (outward, height) profile, clockwise: pumice face, plaster coping proud of it, white terrace top, pumice back
                float yc = hr - CopeDepth;
                RetainUV[0] = new Vector2(o0, -1f); RetainUV[1] = new Vector2(o0, yc); RetainUV[2] = new Vector2(o0 - CopeOver, yc);
                RetainUV[3] = new Vector2(o0 - CopeOver, hr - 0.035f); RetainUV[4] = new Vector2(o0 + 0.03f, hr);
                RetainUV[5] = new Vector2(o0 + depth, hr); RetainUV[6] = new Vector2(o0 + depth, -1f);
                var ring = new Vector3[RetainUV.Length];
                for (int j = 0; j < ring.Length; j++) ring[j] = f.At(side * RetainUV[j].x, RetainUV[j].y);
                MeshBuilder mb = c.S(f.p.y);
                if (prev != null)
                    for (int e = 0; e < RetainMats(c).Length; e++)
                    {
                        Vector2 d = RetainUV[e + 1] - RetainUV[e];
                        ArchGeo.Quad(mb, RetainMats(c)[e], prev[e], prev[e + 1], ring[e + 1], ring[e], f.l * (side * -d.y) + Up * d.x);
                    }
                if (i == i0 || i == i1)
                {
                    Vector3 face = i == i0 ? -f.f : f.f;
                    for (int j = 0; j < ring.Length; j++)                 // fan from the back top corner (the profile is star-shaped around it)
                    {
                        int j1 = (j + 1) % ring.Length;
                        if (j == 5 || j1 == 5) continue;
                        ArchGeo.Tri(mb, j >= 1 && j <= 3 ? c.Trim : c.Pumice, ring[5], ring[j], ring[j1], face);
                    }
                }
                prev = ring;
            }
        }

        static readonly Vector2[] RetainUV = new Vector2[7];
        static Material[] retainMats;
        static Material[] RetainMats(ArchCtx c)
        {
            if (retainMats == null || retainMats[0] != c.Pumice) retainMats = new[] { c.Pumice, c.Trim, c.Trim, c.Trim, c.WhiteOld, c.Pumice };
            return retainMats;
        }
    }
}
