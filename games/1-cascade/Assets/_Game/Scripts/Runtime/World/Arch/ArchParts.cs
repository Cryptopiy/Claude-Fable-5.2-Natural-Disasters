using System.Collections.Generic;
using UnityEngine;

namespace CASCADE
{
    /// <summary>The human-scale props of the path: cast-iron railing, lamp post, lantern, utility pole, wire, stone bench, bollard.</summary>
    internal static class ArchParts
    {
        public const float RailHeight = 1.0f;        // top rail above the walking level: the scale ruler of every wide shot
        public const float PostSize = 0.045f;        // square railing post
        public const float TopRailRadius = 0.024f;   // 4.8 cm hand rail
        public const float MidRailRadius = 0.015f;
        public const float PicketRadius = 0.0145f;   // 4-sided: 2 cm across the flats
        public const float PicketGap = 0.24f;
        public const float LampHeight = 4.0f;        // top of the post above the walking level
        public const float LampHeadY = 3.76f;        // lantern centre above the walking level
        public const float LampReach = 0.5f;         // lantern centre towards the path
        public const float PoleHeight = 6.0f;
        public const float WireRadius = 0.022f;

        static readonly Vector3 Up = ArchGeo.Up;
        static readonly List<Vector3> tmp = new List<Vector3>(12);

        /// <summary>Cast-iron railing along a dense ground polyline. Square posts at line points about 'spacing' apart (closer
        /// where the line bends, so every straight span stays within 1.5 cm of the line), a 4.8 cm top rail at 1 m and lower
        /// rails. plinth > 0: the railing stands on a parapet whose crown is that high (base plates, acorn finials, pickets
        /// every ~0.24 m); plinth = 0: the posts are set into the ground or the edging stones (three rails, flat caps).</summary>
        public static void Railing(ArchCtx c, List<Vector3> line, float spacing = 2f, float plinth = 0f)
        {
            int n = line.Count;
            if (n < 2) return;
            var idx = new List<int>(n / 2 + 2) { 0 };
            int last = 0;
            float acc = 0f;
            for (int i = 1; i < n; i++)
            {
                acc += Vector3.Distance(line[i - 1], line[i]);
                bool place = i == n - 1 || acc >= spacing - 0.05f;
                if (!place && Deviation(line, last, i + 1) > 0.015f) place = true;
                if (place) { idx.Add(i); last = i; acc = 0f; }
            }
            bool parapet = plinth > 0f;
            float yLow = parapet ? plinth + 0.09f : 0.3f, yMid = 0.62f;
            for (int k = 0; k < idx.Count; k++)
            {
                Vector3 d = line[idx[Mathf.Min(k + 1, idx.Count - 1)]] - line[idx[Mathf.Max(k - 1, 0)]];
                RailPost(c, line[idx[k]], ArchGeo.Flat(d), plinth);
            }
            for (int k = 1; k < idx.Count; k++)
            {
                Vector3 a = line[idx[k - 1]], b = line[idx[k]];
                MeshBuilder mb = c.F(a.y);
                mb.Cylinder(c.Iron, a + Up * RailHeight, b + Up * RailHeight, TopRailRadius, TopRailRadius, 6, false);
                mb.Cylinder(c.Iron, a + Up * yLow, b + Up * yLow, MidRailRadius, MidRailRadius, 5, false);
                if (!parapet) { mb.Cylinder(c.Iron, a + Up * yMid, b + Up * yMid, MidRailRadius, MidRailRadius, 5, false); continue; }
                int m = Mathf.Max(0, Mathf.RoundToInt(ArchGeo.FlatDist(a, b) / PicketGap) - 1);
                for (int j = 1; j <= m; j++)
                {
                    Vector3 q = Vector3.Lerp(a, b, j / (float)(m + 1));
                    mb.Cylinder(c.Iron, q + Up * (plinth - 0.02f), q + Up * (RailHeight - 0.01f), PicketRadius, PicketRadius, 4, false);
                }
            }
        }

        /// <summary>Largest distance of the line points strictly between a and b from the chord a-b.</summary>
        static float Deviation(List<Vector3> line, int a, int b)
        {
            if (b >= line.Count) return 0f;
            Vector3 p = line[a], d = line[b] - p;
            float dd = d.sqrMagnitude;
            if (dd < 1e-8f) return 0f;
            float worst = 0f;
            for (int i = a + 1; i < b; i++)
            {
                Vector3 q = line[i] - p;
                worst = Mathf.Max(worst, (q - d * (Vector3.Dot(q, d) / dd)).magnitude);
            }
            return worst;
        }

        static void RailPost(ArchCtx c, Vector3 p, Vector3 dir, float plinth)
        {
            MeshBuilder mb = c.F(p.y);
            if (dir.sqrMagnitude < 1e-8f) dir = Vector3.forward;
            Quaternion rot = Quaternion.LookRotation(dir.normalized, Vector3.up);
            float y0 = plinth > 0f ? plinth - 0.03f : -0.3f, y1 = RailHeight + 0.03f;
            mb.Box(c.Iron, p + Up * ((y0 + y1) * 0.5f), new Vector3(PostSize, y1 - y0, PostSize), rot, 8);
            if (plinth > 0f)
            {
                mb.Box(c.Iron, p + Up * (plinth + 0.006f), new Vector3(0.11f, 0.024f, 0.11f), rot, 8);                // base plate
                mb.Box(c.Iron, p + Up * (plinth + 0.035f), new Vector3(0.07f, 0.035f, 0.07f), rot, 8);                // socket
                ArchDetail.Lathe(mb, c.Iron, p + Up * y1, new[] { 0f, 0f, 0f, 0.033f, 0.028f, 0.033f, 0.06f, 0.036f, 0.1f, 0f }, 6, false);   // acorn finial
            }
            else mb.Box(c.Iron, p + Up * (y1 + 0.012f), new Vector3(0.068f, 0.024f, 0.068f), rot, 0);                 // flat cap
        }

        /// <summary>Lantern around the glowing glass body (0.28 x 0.40 x 0.28, centred on 'centre'): corner posts, floor plate
        /// with a turned drop below, top band, a flared eave and a pyramid roof with a finial ring whose top is 0.37 m above
        /// the centre (the hanger attaches there).</summary>
        public static void LampHead(MeshBuilder mb, ArchCtx c, Vector3 centre, Vector3 fwd, Material glass)
        {
            Quaternion rot = Quaternion.LookRotation(fwd, Vector3.up);
            mb.Box(glass, centre, new Vector3(0.28f, 0.40f, 0.28f), rot, 4 | 8);
            Vector3 r = rot * Vector3.right, f = rot * Vector3.forward;
            for (int i = 0; i < 4; i++)
            {
                float sx = (i & 1) == 0 ? -1f : 1f, sz = (i & 2) == 0 ? -1f : 1f;
                mb.Box(c.Iron, centre + r * (0.14f * sx) + f * (0.14f * sz), new Vector3(0.032f, 0.42f, 0.032f), rot, 4 | 8);
            }
            mb.Box(c.Iron, centre - Up * 0.215f, new Vector3(0.34f, 0.03f, 0.34f), rot, 0);
            ArchDetail.Lathe(mb, c.Iron, centre - Up * 0.31f, new[] { 0f, 0f, 0.025f, 0.035f, 0.08f, 0.11f }, 6, false);
            mb.Box(c.Iron, centre + Up * 0.215f, new Vector3(0.34f, 0.035f, 0.34f), rot, 0);             // top band 0.1975 .. 0.2325
            mb.Box(c.Iron, centre + Up * 0.245f, new Vector3(0.42f, 0.025f, 0.42f), rot, 0);             // eave 0.2325 .. 0.2575
            Vector3 rr = r * 0.2f, ff = f * 0.2f;
            Vector3 b = centre + Up * 0.2575f, apex = centre + Up * 0.335f;
            Vector3 p0 = b - rr - ff, p1 = b + rr - ff, p2 = b + rr + ff, p3 = b - rr + ff;
            ArchGeo.Tri(mb, c.Iron, p0, p1, apex, Up - f * 5f);
            ArchGeo.Tri(mb, c.Iron, p1, p2, apex, Up + r * 5f);
            ArchGeo.Tri(mb, c.Iron, p2, p3, apex, Up + f * 5f);
            ArchGeo.Tri(mb, c.Iron, p3, p0, apex, Up - r * 5f);
            ArchDetail.Lathe(mb, c.Iron, centre + Up * 0.31f, new[] { 0f, 0.028f, 0.025f, 0.034f, 0.045f, 0.016f, 0.06f, 0.016f }, 6, true);
        }

        /// <summary>Cast-iron lamp post: stepped octagonal plinth, bell base and moulding, fluted tapering shaft with a
        /// collar, capital, slim stem with a ball finial, a scrolled swan-neck arm with a brace, and the lantern hanging
        /// from it. foot = where the post meets the walking level; bury = how far the base continues below it.
        /// The lantern centre (foot + toPath x LampReach + up x LampHeadY) is recorded and returned.</summary>
        public static Vector3 LampPost(ArchCtx c, float chainage, Vector3 foot, float bury, Vector3 toPath)
        {
            MeshBuilder mb = c.F(foot.y);
            Vector3 tp = ArchGeo.Flat(toPath).normalized;
            ArchDetail.Lathe(mb, c.Iron, foot, new[]
            {
                -bury, 0.17f, 0.07f, 0.17f, 0.07f, 0.14f, 0.15f, 0.14f, 0.15f, 0.11f,
                0.22f, 0.105f, 0.52f, 0.078f, 0.56f, 0.078f, 0.56f, 0.092f, 0.63f, 0.092f
            }, 8, true);
            ArchDetail.Fluted(mb, c.Iron, foot + Up * 0.63f, 2.92f, 0.064f, 0.047f, 8);
            ArchDetail.Lathe(mb, c.Iron, foot + Up * 2.25f, new[] { 0f, 0.04f, 0f, 0.074f, 0.06f, 0.074f, 0.06f, 0.04f }, 8, false);    // collar
            ArchDetail.Lathe(mb, c.Iron, foot + Up * 3.55f, new[] { 0f, 0.035f, 0f, 0.07f, 0.03f, 0.082f, 0.07f, 0.082f, 0.1f, 0.05f, 0.1f, 0.036f }, 8, false);   // capital
            ArchDetail.Lathe(mb, c.Iron, foot + Up * 3.65f, new[] { 0f, 0.036f, 0.33f, 0.03f, 0.33f, 0.04f, 0.36f, 0.046f, 0.40f, 0.04f, 0.43f, 0f }, 6, false);   // stem and ball

            tmp.Clear();                                                       // swan-neck arm ending in a small scroll
            tmp.Add(foot + Up * 3.8f);
            tmp.Add(foot + tp * 0.10f + Up * 3.93f);
            tmp.Add(foot + tp * 0.22f + Up * 4.08f);
            tmp.Add(foot + tp * 0.36f + Up * 4.19f);
            tmp.Add(foot + tp * 0.50f + Up * 4.23f);
            tmp.Add(foot + tp * 0.60f + Up * 4.21f);
            tmp.Add(foot + tp * 0.66f + Up * 4.15f);
            tmp.Add(foot + tp * 0.63f + Up * 4.10f);
            mb.Tube(c.Iron, tmp, 0.022f, 5);
            tmp.Clear();                                                       // brace from the capital up to the arm
            tmp.Add(foot + Up * 3.62f);
            tmp.Add(foot + tp * 0.07f + Up * 3.68f);
            tmp.Add(foot + tp * 0.16f + Up * 3.8f);
            tmp.Add(foot + tp * 0.24f + Up * 3.95f);
            tmp.Add(foot + tp * 0.29f + Up * 4.13f);
            mb.Tube(c.Iron, tmp, 0.015f, 5);

            Vector3 head = foot + toPath * LampReach + Up * LampHeadY;
            mb.Cylinder(c.Iron, foot + tp * LampReach + Up * 4.245f, head + Up * 0.355f, 0.016f, 0.016f, 5, false);   // hanger
            LampHead(mb, c, head, toPath, c.NextGlass());
            c.AddLamp(chainage, head);
            return head;
        }

        /// <summary>Wooden utility pole, 6 m above 'ground'. Returns the two wire attachment points (upper, lower).</summary>
        public static void Pole(ArchCtx c, Vector3 ground, float bury, out Vector3 upper, out Vector3 lower)
        {
            MeshBuilder mb = c.F(ground.y);
            mb.Cylinder(c.WoodDark, ground - Up * bury, ground + Up * PoleHeight, 0.11f, 0.075f, 6, true);
            upper = ground + Up * (PoleHeight - 0.15f);
            lower = ground + Up * (PoleHeight - 0.60f);
            mb.Cylinder(c.Iron, upper - Up * 0.06f, upper + Up * 0.06f, 0.105f, 0.105f, 6, true);
            mb.Cylinder(c.Iron, lower - Up * 0.06f, lower + Up * 0.06f, 0.11f, 0.11f, 6, true);
        }

        /// <summary>Sagging wire between two points (parabola, 8 spans, radius 0.022).</summary>
        public static void Wire(ArchCtx c, Vector3 a, Vector3 b, float sag)
        {
            const int N = 8;
            MeshBuilder mb = c.W(Mathf.Min(a.y, b.y) - PoleHeight);
            Vector3 prev = a;
            for (int i = 1; i <= N; i++)
            {
                float t = i / (float)N;
                Vector3 p = Vector3.Lerp(a, b, t) - Up * (sag * 4f * t * (1f - t));
                mb.Cylinder(c.Iron, prev, p, WireRadius, WireRadius, 5, false);
                prev = p;
            }
        }

        /// <summary>Built-in masonry bench: a 1.66 m pumice slab (seat at 0.48 m) on two whitewashed legs joined by a
        /// recessed back panel, with plaster plinths. 'along' = long axis.</summary>
        public static void Bench(ArchCtx c, Vector3 ground, Vector3 along)
        {
            MeshBuilder mb = c.F(ground.y);
            Quaternion rot = Quaternion.LookRotation(along, Vector3.up);
            Vector3 across = rot * Vector3.right;
            mb.Box(c.Pumice, ground + Up * 0.445f, new Vector3(0.46f, 0.08f, 1.66f), rot, 0);                     // seat 0.405 .. 0.485
            for (int e = -1; e <= 1; e += 2)
            {
                mb.Box(c.White, ground + along * (e * 0.56f) + Up * 0.07f, new Vector3(0.36f, 0.67f, 0.28f), rot, 4 | 8);   // -0.265 .. 0.405
                mb.Box(c.Trim, ground + along * (e * 0.56f) + Up * 0.03f, new Vector3(0.40f, 0.1f, 0.32f), rot, 8);         // plinth band
            }
            mb.Box(c.White, ground - across * 0.12f + Up * 0.12f, new Vector3(0.08f, 0.57f, 0.86f), rot, 4 | 8 | 16 | 32);  // back panel between the legs
        }

        /// <summary>Stone mooring bollard (0.45 m) with an iron ring lying beside it.</summary>
        public static void Bollard(ArchCtx c, MeshBuilder mb, Vector3 p, Vector3 ringDir)
        {
            mb.Cylinder(c.Stone, p - Up * 0.05f, p + Up * 0.36f, 0.17f, 0.14f, 8, false);
            mb.Cylinder(c.Stone, p + Up * 0.36f, p + Up * 0.45f, 0.20f, 0.17f, 8, true);
            Vector3 side = ArchGeo.Left(ringDir);
            ArchGeo.RingTube(mb, c.Iron, p + ringDir * 0.33f + Up * 0.03f, ringDir, side, 0.12f, 0.022f, 8, 5);
            mb.Box(c.Iron, p + ringDir * 0.21f + Up * 0.03f, new Vector3(0.05f, 0.06f, 0.06f), Quaternion.LookRotation(ringDir, Vector3.up), 8);
        }
    }
}
