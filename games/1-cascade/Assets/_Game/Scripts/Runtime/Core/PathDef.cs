using System.Collections.Generic;
using UnityEngine;

namespace CASCADE
{
    public enum Surface { Quay = 0, Stairs = 1, Promenade = 2, Gravel = 3 }

    /// <summary>
    /// The one fixed path, defined in code: quay (flat, heading north) -> zigzag stair-road up the cliff -> narrow stair
    /// between walls -> rim promenade -> headland climb to the lighthouse keeper's door.
    /// Samples are uniform in 3D arc length (chainage, metres). Section ids: 0 quay, 1 stair-road, 2 narrow stair,
    /// 3 promenade, 4 headland.
    /// </summary>
    public static class PathDef
    {
        public const float Step = 0.5f;
        public const float QuayLength = 222f;

        public static Vector3[] P;
        public static byte[] Sec;
        public static float[] HalfW;       // walkable half width, smoothed
        public static int Count;
        public static float Length;
        public static float SQuayEnd, SNarrowStart, SStairEnd, SPromEnd;

        static bool built;
        const float Cell = 8f;
        const float GX0 = -960f, GZ0 = -330f;
        const int GW = 150, GH = 150;
        static int[] cellHead;
        static int[] next;

        static readonly float[] SecHalfWidth = { 3.2f, 2.2f, 1.15f, 2.5f, 3.2f };

        public static void Build()
        {
            if (built) return;
            built = true;
            var raw = new List<Vector3>(8192);
            var sec = new List<byte>(8192);

            // ---- turtle part: quay and zigzag (heading 0 = north, positive turn = right) ----
            Vector3 p = new Vector3(-8f, Config.QuayHeight, -22f - QuayLength);
            float h = 0f;
            raw.Add(p); sec.Add(0);
            Line(raw, sec, ref p, h, QuayLength, 0f, 0);
            Arc(raw, sec, ref p, ref h, 8f, 180f, 0.16f, 1);
            Line(raw, sec, ref p, h, 24f, 0.55f, 1);
            Arc(raw, sec, ref p, ref h, 11f, -180f, 0.36f, 1);
            Line(raw, sec, ref p, h, 24f, 0.55f, 1);
            Arc(raw, sec, ref p, ref h, 11f, 180f, 0.36f, 1);
            Line(raw, sec, ref p, h, 24f, 0.55f, 1);
            Arc(raw, sec, ref p, ref h, 11f, -180f, 0.36f, 1);
            Line(raw, sec, ref p, h, 42f, 0.60f, 2);
            float yTop = p.y;

            // ---- spline part: promenade and headland (centripetal Catmull-Rom through control points) ----
            Vector3[] cp =
            {
                new Vector3(74f, yTop, -30f),            // phantom (keeps the tangent north)
                new Vector3(74f, yTop, -4f),
                new Vector3(75f, 110.4f, 40f),
                new Vector3(77f, 115f, 120f),
                new Vector3(73f, 123f, 235f),
                new Vector3(52f, 132f, 360f),
                new Vector3(8f, 141f, 470f),
                new Vector3(-58f, 150f, 558f),           // promenade ends, headland begins
                new Vector3(-160f, 167f, 628f),
                new Vector3(-285f, 187f, 678f),
                new Vector3(-410f, 208f, 708f),
                new Vector3(-530f, 228f, 723f),
                new Vector3(-640f, 247f, 723f),
                new Vector3(-725f, 265f, 708f),
                new Vector3(-800f, 272f, 690f),          // phantom
            };
            for (int i = 1; i < cp.Length - 2; i++)
            {
                byte id = (byte)(i <= 6 ? 3 : 4);
                float chord = Vector3.Distance(cp[i], cp[i + 1]);
                int n = Mathf.CeilToInt(chord / 0.25f);
                for (int k = 1; k <= n; k++)
                {
                    raw.Add(CatmullRom(cp[i - 1], cp[i], cp[i + 1], cp[i + 2], k / (float)n));
                    sec.Add(id);
                }
            }

            // ---- smooth heights over +-2 m so that slope changes have no kinks ----
            int m = raw.Count;
            var ys = new float[m];
            for (int i = 0; i < m; i++)
            {
                float sum = 0f; int c = 0;
                for (int k = -8; k <= 8; k++) { int j = Mathf.Clamp(i + k, 0, m - 1); sum += raw[j].y; c++; }
                ys[i] = sum / c;
            }
            for (int i = 0; i < m; i++) { Vector3 q = raw[i]; q.y = ys[i]; raw[i] = q; }

            // ---- resample uniformly in 3D arc length ----
            var cum = new float[m];
            for (int i = 1; i < m; i++) cum[i] = cum[i - 1] + Vector3.Distance(raw[i - 1], raw[i]);
            Length = cum[m - 1];
            Count = Mathf.FloorToInt(Length / Step) + 1;
            Length = (Count - 1) * Step;
            P = new Vector3[Count];
            Sec = new byte[Count];
            HalfW = new float[Count];
            int seg = 0;
            for (int i = 0; i < Count; i++)
            {
                float s = i * Step;
                while (seg < m - 2 && cum[seg + 1] < s) seg++;
                float t = Mathf.InverseLerp(cum[seg], cum[seg + 1], s);
                P[i] = Vector3.Lerp(raw[seg], raw[seg + 1], t);
                Sec[i] = sec[seg + 1];
            }
            for (int i = 1; i < Count; i++)
            {
                if (Sec[i] != Sec[i - 1])
                {
                    float s = i * Step;
                    if (Sec[i] == 1) SQuayEnd = s; else if (Sec[i] == 2) SNarrowStart = s;
                    else if (Sec[i] == 3) SStairEnd = s; else if (Sec[i] == 4) SPromEnd = s;
                }
            }
            for (int i = 0; i < Count; i++)
            {
                float sum = 0f; int c = 0;
                for (int k = -6; k <= 6; k++) { int j = Mathf.Clamp(i + k, 0, Count - 1); sum += SecHalfWidth[Sec[j]]; c++; }
                HalfW[i] = sum / c;
            }

            // ---- spatial grid for nearest-point queries ----
            cellHead = new int[GW * GH];
            next = new int[Count];
            for (int i = 0; i < cellHead.Length; i++) cellHead[i] = -1;
            for (int i = 0; i < Count; i++)
            {
                int cx = Mathf.Clamp((int)((P[i].x - GX0) / Cell), 0, GW - 1);
                int cz = Mathf.Clamp((int)((P[i].z - GZ0) / Cell), 0, GH - 1);
                next[i] = cellHead[cz * GW + cx];
                cellHead[cz * GW + cx] = i;
            }
        }

        static void Line(List<Vector3> pts, List<byte> sec, ref Vector3 p, float heading, float len, float slope, byte id)
        {
            int n = Mathf.CeilToInt(len / 0.25f);
            Vector3 d = new Vector3(Mathf.Sin(heading), 0f, Mathf.Cos(heading));
            Vector3 p0 = p;
            for (int i = 1; i <= n; i++)
            {
                float l = len * i / n;
                Vector3 q = p0 + d * l;
                q.y = p0.y + slope * l;
                pts.Add(q); sec.Add(id);
                p = q;
            }
        }

        static void Arc(List<Vector3> pts, List<byte> sec, ref Vector3 p, ref float heading, float radius, float turnDeg, float slope, byte id)
        {
            float turn = turnDeg * Mathf.Deg2Rad;
            float arcLen = Mathf.Abs(turn) * radius;
            int n = Mathf.CeilToInt(arcLen / 0.25f);
            float sgn = Mathf.Sign(turn);
            Vector3 right = new Vector3(Mathf.Cos(heading), 0f, -Mathf.Sin(heading));
            Vector3 c = p + right * (radius * sgn);
            float y0 = p.y;
            for (int i = 1; i <= n; i++)
            {
                float hh = heading + turn * i / n;
                Vector3 rh = new Vector3(Mathf.Cos(hh), 0f, -Mathf.Sin(hh));
                Vector3 q = c - rh * (radius * sgn);
                q.y = y0 + slope * arcLen * i / n;
                pts.Add(q); sec.Add(id);
                p = q;
            }
            heading += turn;
        }

        static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            // centripetal parameterisation (alpha 0.5), Barry-Goldman
            float t0 = 0f;
            float t1 = t0 + Mathf.Sqrt(Vector3.Distance(p0, p1));
            float t2 = t1 + Mathf.Sqrt(Vector3.Distance(p1, p2));
            float t3 = t2 + Mathf.Sqrt(Vector3.Distance(p2, p3));
            float u = Mathf.Lerp(t1, t2, t);
            Vector3 a1 = (t1 - u) / (t1 - t0) * p0 + (u - t0) / (t1 - t0) * p1;
            Vector3 a2 = (t2 - u) / (t2 - t1) * p1 + (u - t1) / (t2 - t1) * p2;
            Vector3 a3 = (t3 - u) / (t3 - t2) * p2 + (u - t2) / (t3 - t2) * p3;
            Vector3 b1 = (t2 - u) / (t2 - t0) * a1 + (u - t0) / (t2 - t0) * a2;
            Vector3 b2 = (t3 - u) / (t3 - t1) * a2 + (u - t1) / (t3 - t1) * a3;
            return (t2 - u) / (t2 - t1) * b1 + (u - t1) / (t2 - t1) * b2;
        }

        public static Vector3 Pos(float s)
        {
            float f = Mathf.Clamp(s, 0f, Length) / Step;
            int i = Mathf.Min((int)f, Count - 2);
            return Vector3.LerpUnclamped(P[i], P[i + 1], f - i);
        }

        public static Vector3 Tan(float s)
        {
            Vector3 d = Pos(s + 0.75f) - Pos(s - 0.75f);
            return d.sqrMagnitude > 1e-8f ? d.normalized : Vector3.forward;
        }

        /// <summary>Horizontal walking direction.</summary>
        public static Vector3 FlatDir(float s)
        {
            Vector3 d = Pos(s + 1.5f) - Pos(s - 1.5f);
            d.y = 0f;
            return d.sqrMagnitude > 1e-8f ? d.normalized : Vector3.forward;
        }

        public static int Section(float s) { return Sec[Mathf.Clamp(Mathf.RoundToInt(s / Step), 0, Count - 1)]; }

        public static Surface SurfaceAt(float s)
        {
            int k = Section(s);
            return k == 0 ? Surface.Quay : k <= 2 ? Surface.Stairs : k == 3 ? Surface.Promenade : Surface.Gravel;
        }

        public static float HalfWidth(float s) { return HalfW[Mathf.Clamp(Mathf.RoundToInt(s / Step), 0, Count - 1)]; }

        /// <summary>Slope (rise per metre of chainage) around s.</summary>
        public static float Slope(float s) { return (Pos(s + 1f).y - Pos(s - 1f).y) / 2f; }

        /// <summary>True where step geometry is built (stair-road and narrow stair).</summary>
        public static bool IsStairs(float s) { int k = Section(s); return k == 1 || k == 2; }

        /// <summary>Lateral drift allowed for the player: the safety corridor (3 m, 6 m on the headland) limited by the walkable width.</summary>
        public static float DriftLimit(float s)
        {
            float corridor = Section(s) == 4 ? 6f : 3f;
            return Mathf.Max(0.2f, Mathf.Min(corridor, HalfWidth(s) - 0.5f));
        }

        /// <summary>Nearest path point within about 16 m in plan. Returns false when the path is farther away.</summary>
        public static bool Nearest(float x, float z, out float s, out float dist)
        {
            s = 0f; dist = 1e9f;
            int cx = (int)((x - GX0) / Cell), cz = (int)((z - GZ0) / Cell);
            if (cx < -2 || cz < -2 || cx > GW + 1 || cz > GH + 1) return false;
            int best = -1; float bd = 1e18f;
            for (int j = cz - 2; j <= cz + 2; j++)
            {
                if (j < 0 || j >= GH) continue;
                for (int i = cx - 2; i <= cx + 2; i++)
                {
                    if (i < 0 || i >= GW) continue;
                    for (int k = cellHead[j * GW + i]; k >= 0; k = next[k])
                    {
                        float dx = P[k].x - x, dz = P[k].z - z;
                        float d = dx * dx + dz * dz;
                        if (d < bd) { bd = d; best = k; }
                    }
                }
            }
            if (best < 0) return false;
            // refine on the two neighbouring segments
            float bs = best * Step; float bdist = Mathf.Sqrt(bd);
            for (int k = Mathf.Max(best - 1, 0); k <= Mathf.Min(best, Count - 2); k++)
            {
                float ax = P[k].x, az = P[k].z, bx = P[k + 1].x - ax, bz = P[k + 1].z - az;
                float t = Mathf.Clamp01(((x - ax) * bx + (z - az) * bz) / (bx * bx + bz * bz + 1e-9f));
                float px = ax + bx * t - x, pz = az + bz * t - z;
                float d = Mathf.Sqrt(px * px + pz * pz);
                if (d <= bdist) { bdist = d; bs = (k + t) * Step; }
            }
            s = bs; dist = bdist;
            return dist < 17f;
        }
    }
}
