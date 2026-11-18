using System.Collections.Generic;
using UnityEngine;

namespace CASCADE
{
    /// <summary>
    /// Section 3: the belvedere at the stair top (a viewpoint terrace with benches, pots and a coin viewer), the paved
    /// rim promenade on its white retaining wall with white-washed edge bands, the coped low wall on the land side with
    /// pots on it, lamp posts on the caldera side (on the land side at the belvedere and where the eye looks back at the
    /// eruption, LandLampFrom..To),
    /// utility poles and wires on the land side, and on the caldera / bay side
    /// (left) a low white parapet carrying the cast-iron railing; on the headland the railing continues on its own posts
    /// to the keeper's forecourt.
    /// </summary>
    internal static class ArchPromenade
    {
        public const float BelvLen = 14f, BelvBulge = 5f;    // the terrace bulges west for the first 14 m
        public const float Margin = 0.5f;                    // paving beyond the walkable width
        public const float RailOffset = 0.15f;               // headland railing line outside the walkable width (lamps are placed from it)
        public const float ParapetOffset = 0.3f;             // promenade parapet / railing line outside the walkable width
        public const float ParapetH = 0.45f, ParapetT = 0.3f; // parapet crown under the railing
        public const float EdgeBand = 0.2f;                  // white-washed band of the paving along both walls
        public const float LampOutside = 0.6f;               // lamp posts stand this far outside the railing line
        public const float LampFirst = 6f;                   // first lamp: this far beyond the belvedere (ruler of M1)
        // One more at the belvedere, on the land side behind the low wall (LandLamp). Inside the caldera railing it stood about
        // 10 degrees right of M1's axis (185) with its lantern level with the summit steam, splitting the cone from the open sky.
        // At 13 m it keeps 4 m off the first utility pole (s0 + 9) and stays clear of the first wall pot (s0 + 16).
        public const float BelvLampAt = 13f;
        public const float LampSpacing = 22f;
        // Lamps whose chainage lies in this window (metres beyond the stair top) stand on the land side instead: the walker
        // passes them at about 190-205 s, when the eye rests on the cone to the left, and a caldera-side post 2-3 m ahead filled
        // the right fifth of the eruption frame (196); on the land side it stands about 130 degrees off the view axis, out of
        // frame. Same chainage, so the night rhythm of the lights is kept.
        public const float LandLampFrom = 30f, LandLampTo = 70f;
        public const float LandLampBehind = 0.25f;          // land-side lamp foot: this far behind the low wall's outer face
        public const float PoleSpacing = 25f, PoleOffset = 1.0f;
        public const float LowWallH = 0.45f, LowWallT = 0.3f;
        public const float RailStopBeforeEnd = 7f;           // the keeper's low wall takes over there

        static readonly Vector3 Up = ArchGeo.Up;

        /// <summary>Extra width of the terrace on the caldera side at chainage s.</summary>
        public static float Bulge(float s)
        {
            float u = s - PathDef.SStairEnd;
            if (u <= 0f || u >= BelvLen) return 0f;
            return BelvBulge * MathX.Smooth(0f, 1.6f, u) * (1f - MathX.Smooth(BelvLen - 2.8f, BelvLen, u));
        }

        public static void Build(ArchCtx c)
        {
            float s0 = PathDef.SStairEnd, s1 = PathDef.SPromEnd;

            // ---- paving on its retaining wall (dense stations around the belvedere), white bands along both edges ----
            var st = new List<float>(460);
            float s = s0;
            for (; s < s0 + BelvLen + 1f; s += 0.4f) st.Add(s);
            for (; s < s1 - 0.6f; s += 1.5f) st.Add(s);
            st.Add(s1);
            var mats = new[] { c.WhiteOld, c.White, c.Paving, c.White, c.Paving };
            Vector3[] prev = null;
            for (int i = 0; i < st.Count; i++)
            {
                ArchFrame f = ArchGeo.Frame(st[i]);
                float b = Bulge(f.s);
                Vector3 lt = f.At(f.hw + Margin + b), rt = f.At(-(f.hw + Margin));
                Vector3 lb = f.At(f.hw + b - EdgeBand), rb = f.At(-(f.hw - EdgeBand));
                float yb = f.p.y - ArchGeo.Depth(lt, f.l, 1.6f, 0.6f, 30f);     // white retaining wall down the cliff edge
                var ring = new[] { ArchGeo.Y(lt, yb), lt, lb, rb, rt, ArchGeo.Y(rt, f.p.y - 0.6f) };
                MeshBuilder mb = c.S(f.p.y);
                if (prev != null) ArchGeo.Loft(mb, prev, ring, mats, null, false);
                if (i == 0) ArchGeo.Cap(mb, c.WhiteOld, ring, -f.f);
                if (i == st.Count - 1) ArchGeo.Cap(mb, c.Paving, ring, f.f);
                prev = ring;
            }

            // ---- caldera side: coped parapet carrying the railing (promenade), railing on its own posts (headland) ----
            var wall = new List<Vector3>(500);
            int belvN = 0;
            for (int i = 0; s0 - 0.1f + 0.2f * i < s0 + BelvLen + 1f; i++) { wall.Add(ParapetLine(s0 - 0.1f + 0.2f * i)); belvN++; }
            for (s = s0 + BelvLen + 1.5f; s < s1 - 0.8f; s += 1.5f) wall.Add(ParapetLine(s));
            float sPier = s1 - 0.25f;
            wall.Add(ParapetLine(sPier));
            ArchGeo.CopedWall(c, wall, ParapetH, ParapetT, 0.3f);
            ArchFrame fq = ArchGeo.Frame(sPier + 0.05f);
            ArchDetail.Pier(c.S(fq.p.y), c, fq.At(fq.hw + ParapetOffset), fq.f, 0.38f, 0.75f, 0.6f);   // outer face 1 cm inside the paving edge

            // railing on the parapet crown: around the belvedere it uses the parapet's own stations (the bulge corners are tight)
            var line = new List<Vector3>(800);
            for (int i = 0; i < belvN; i++) if (0.2f * i - 0.1f > 0.05f) line.Add(wall[i]);   // from s0 + 0.1 on
            for (s = s0 + BelvLen + 1.5f; s < sPier - 0.3f; s += 1f) line.Add(ParapetLine(s));
            line.Add(ParapetLine(sPier - 0.3f));
            ArchParts.Railing(c, line, 1.8f, ParapetH);

            line.Clear();
            float sEnd = PathDef.Length - RailStopBeforeEnd;
            for (s = sPier + 0.35f; s < sEnd; s += 1f)
            {
                ArchFrame f = ArchGeo.Frame(s);
                line.Add(f.At(f.hw + RailOffset));
            }
            ArchFrame fe = ArchGeo.Frame(sEnd);
            line.Add(fe.At(fe.hw + RailOffset));
            ArchParts.Railing(c, line, 2f, 0f);

            // ---- coped low white wall on the land side, with pots standing on it ----
            var pts = new List<Vector3>(420);
            for (s = s0; s < s1 - 0.6f; s += 1.5f)
            {
                ArchFrame f = ArchGeo.Frame(s);
                pts.Add(f.At(-(f.hw + 0.12f + LowWallT * 0.5f)));
            }
            ArchFrame f1 = ArchGeo.Frame(s1);
            pts.Add(f1.At(-(f1.hw + 0.12f + LowWallT * 0.5f)));
            ArchGeo.CopedWall(c, pts, LowWallH, LowWallT, 0.5f);
            var rng = new System.Random(3307);
            for (s = s0 + 16f; s < s1 - 6f; s += 6f + 7f * (float)rng.NextDouble())
            {
                ArchFrame f = ArchGeo.Frame(s);
                Vector3 p = f.At(-(f.hw + 0.12f + LowWallT * 0.5f), LowWallH - 0.012f);
                ArchDetail.Pot(c.F(f.p.y), c, p, 0.75f + 0.25f * (float)rng.NextDouble(), rng, rng.NextDouble() < 0.55);
            }

            // ---- lamp posts on the caldera side (the first, M1's ruler, included), on the land side where the walker passes
            //      them while the eye rests on the cone and the eruption to the left (the belvedere one, 180-205 s) ----
            LandLamp(c, s0 + BelvLampAt);
            for (s = s0 + BelvLen + LampFirst; s < s1 - 4f; s += LampSpacing)
            {
                if (s > s0 + LandLampFrom && s < s0 + LandLampTo) LandLamp(c, s);
                else Lamp(c, s);
            }

            Belvedere(c, s0);

            // ---- utility poles and two wires on the land side ----
            bool have = false;
            Vector3 pu = Vector3.zero, pl = Vector3.zero;
            for (s = s0 + 9f; s < s1 - 3f; s += PoleSpacing)
            {
                ArchFrame f = ArchGeo.Frame(s);
                Vector3 at = f.At(-(f.hw + PoleOffset));
                Vector3 up, lo;
                ArchParts.Pole(c, at, 1.2f + Mathf.Max(0f, at.y - ArchGeo.Ground(at)), out up, out lo);
                if (have) { ArchParts.Wire(c, pu, up, 0.5f); ArchParts.Wire(c, pl, lo, 0.8f); }
                pu = up; pl = lo; have = true;
            }
        }

        /// <summary>Centre line of the caldera-side parapet (and its railing) at chainage s, on the walking level.</summary>
        static Vector3 ParapetLine(float s)
        {
            ArchFrame f = ArchGeo.Frame(s);
            return f.At(f.hw + ParapetOffset + Bulge(s));
        }

        /// <summary>The viewpoint terrace: two park benches looking out over the caldera, pots with bougainvillea and a coin viewer.</summary>
        static void Belvedere(ArchCtx c, float s0)
        {
            var rng = new System.Random(5813);
            float[] benchAt = { 4.6f, 8.7f };
            for (int i = 0; i < benchAt.Length; i++)
            {
                ArchFrame f = ArchGeo.Frame(s0 + benchAt[i]);
                Vector3 g = f.At(f.hw + Bulge(f.s) - 0.62f);
                ArchDetail.ParkBench(c.F(f.p.y), c, g, f.l);
            }
            float[] potAt = { 6.65f, 6.95f, 10.75f, 3.35f };
            float[] potIn = { 0.48f, 1.05f, 0.5f, 0.55f };
            float[] potScale = { 1.6f, 1.0f, 1.5f, 1.2f };
            for (int i = 0; i < potAt.Length; i++)
            {
                ArchFrame f = ArchGeo.Frame(s0 + potAt[i]);
                if (Bulge(f.s) < 2f) continue;
                Vector3 g = f.At(f.hw + Bulge(f.s) - potIn[i]);
                ArchDetail.Pot(c.F(f.p.y), c, g, potScale[i], rng, i != 1);
            }
            ArchFrame fv = ArchGeo.Frame(s0 + 11.6f);
            if (Bulge(fv.s) > 2f) ArchDetail.Viewer(c.F(fv.p.y), c, fv.At(fv.hw + Bulge(fv.s) - 0.45f), fv.l - fv.f * 0.3f);
        }

        /// <summary>Lamp post on the caldera / bay side at chainage s (also used by the headland).</summary>
        public static void Lamp(ArchCtx c, float s)
        {
            ArchFrame f = ArchGeo.Frame(s);
            Vector3 foot = f.At(f.hw + RailOffset + LampOutside + Bulge(s));
            float bury = ArchGeo.Depth(foot, f.l, 1.0f, 1.5f, 9f);
            ArchParts.LampPost(c, s, foot, bury, -f.l);
        }

        /// <summary>Lamp post on the land side at chainage s: just behind the coped low wall, the lantern reaching back over the
        /// path edge. The ground there is the path bed (WorldGen carves 0.7 m beyond the walkable width), so the base is
        /// sunk by Depth like the caldera-side posts rather than by a fixed amount.</summary>
        static void LandLamp(ArchCtx c, float s)
        {
            ArchFrame f = ArchGeo.Frame(s);
            Vector3 foot = f.At(-(f.hw + 0.12f + LowWallT + LandLampBehind));
            ArchParts.LampPost(c, s, foot, ArchGeo.Depth(foot, -f.l, 1.0f, 0.3f, 3f), f.l);
        }
    }
}
