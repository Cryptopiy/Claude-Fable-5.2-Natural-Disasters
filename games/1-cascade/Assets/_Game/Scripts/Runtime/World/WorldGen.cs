using System.Collections.Generic;
using UnityEngine;

namespace CASCADE
{
    /// <summary>
    /// The island as pure functions. The island is a ring around an elliptical flooded caldera; the village cliff, the bay
    /// and the headland are an explicit piece of the inner shoreline. Height(x,z) is the ground (terrain and sea floor)
    /// with the path carved in. The volcano cone is NOT part of this function (it has its own mesh and shader).
    /// </summary>
    public static class WorldGen
    {
        static Vector2[] inner;          // inner shoreline polygon: caldera water inside
        static int innerN;
        static float[] innerArc;         // arc length along the shoreline at each polygon vertex, counted from the strait
        /// <summary>Length of the whole inner shoreline (m).</summary>
        public static float ShoreLength;
        static float[] spX, spZ, spH, spW;   // rim spine: position, crest height, distance from shore to crest
        static int spN;
        static bool built;

        public const float RingThickness = 1900f;
        public const float GapFrom = 262f, GapTo = 292f;   // strait in the ring, degrees (lets the open-sea horizon show under the sun)

        public static void Build()
        {
            if (built) return;
            built = true;
            PathDef.Build();
            Vector3 O = Config.ConeCenter;

            var pts = new List<Vector2>(160);
            float[] near =
            {
                -84f, -700f,  -46f, -500f,  -24f, -320f,  -20f, -262f,  -20f, -10f,  -22f, 120f,  -30f, 240f,  -58f, 372f,
                -135f, 468f,  -290f, 518f,  -480f, 538f,  -680f, 528f,  -860f, 556f,  -952f, 640f,  -940f, 770f,
                -830f, 890f,  -650f, 985f,  -505f, 1130f,  -440f, 1260f
            };
            for (int i = 0; i < near.Length; i += 2) pts.Add(new Vector2(near[i], near[i + 1]));
            for (float phi = 26f; phi <= 349.01f; phi += 3f)
            {
                float w = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(26f, 52f, phi)) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(322f, 349f, phi)));
                float r = phi * Mathf.Deg2Rad;
                float k = 1f + w * (0.035f * Mathf.Sin(3f * r + 1.3f) + 0.02f * Mathf.Sin(7f * r + 0.4f) + 0.012f * Mathf.Sin(13f * r + 2.1f));
                pts.Add(new Vector2(O.x + Config.CalderaA * k * Mathf.Cos(r), O.z + Config.CalderaB * k * Mathf.Sin(r)));
            }
            inner = pts.ToArray();
            innerN = inner.Length;
            // arc length along the shore, origin in the strait of the ring (no land there, so the wrap-around is never seen)
            innerArc = new float[innerN];
            float acc = 0f, strait = 0f;
            for (int i = 0; i < innerN; i++)
            {
                if (i > 0) acc += Vector2.Distance(inner[i - 1], inner[i]);
                innerArc[i] = acc;
                Vector2 rel = inner[i] - new Vector2(O.x, O.z);
                float phi = Mathf.Repeat(Mathf.Atan2(rel.y / Config.CalderaB, rel.x / Config.CalderaA) * Mathf.Rad2Deg, 360f);
                if (i > 0 && phi <= (GapFrom + GapTo) * 0.5f) strait = acc;
            }
            ShoreLength = acc + Vector2.Distance(inner[innerN - 1], inner[0]);
            for (int i = 0; i < innerN; i++) innerArc[i] = Mathf.Repeat(innerArc[i] - strait, ShoreLength);

            // ---- rim spine: where the crest is and how high ----
            var sx = new List<float>(); var sz = new List<float>(); var sh = new List<float>(); var sw = new List<float>();
            float[] south = { 80f, -262f, 108f,  78f, -120f, 110f,  76f, -50f, 110f,  72f, -420f, 104f,  40f, -600f, 98f,  -12f, -770f, 96f };
            for (int i = 0; i < south.Length; i += 3) { sx.Add(south[i]); sz.Add(south[i + 1]); sh.Add(south[i + 2]); sw.Add(-1f); }
            for (float s = PathDef.SStairEnd; s <= PathDef.Length; s += 10f)
            {
                Vector3 p = PathDef.Pos(s);
                sx.Add(p.x); sz.Add(p.z); sh.Add(p.y + 0.8f); sw.Add(-1f);
            }
            for (float phi = 30f; phi <= 346f; phi += 4f)
            {
                float r = phi * Mathf.Deg2Rad;
                float h = 170f + 95f * Mathf.Sin(2.3f * r + 0.7f) + 55f * Mathf.Sin(5.1f * r + 2.0f) + 30f * Mathf.Sin(11f * r + 0.3f);
                h = Mathf.Max(h, 70f);
                float gap = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(GapFrom - 9f, GapFrom, phi)) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(GapTo, GapTo + 9f, phi)));
                h = Mathf.Lerp(h, -30f, gap);
                float ka = 1f + 300f / Config.CalderaA, kb = 1f + 300f / Config.CalderaB;
                sx.Add(O.x + Config.CalderaA * ka * Mathf.Cos(r)); sz.Add(O.z + Config.CalderaB * kb * Mathf.Sin(r)); sh.Add(h); sw.Add(260f);
            }
            spN = sx.Count;
            spX = sx.ToArray(); spZ = sz.ToArray(); spH = sh.ToArray(); spW = sw.ToArray();
            for (int i = 0; i < spN; i++) if (spW[i] < 0f) spW[i] = Mathf.Max(40f, SdInner(spX[i], spZ[i]) + 4f);
        }

        /// <summary>Signed distance to the inner (caldera) shoreline: positive on land (and beyond), negative in the caldera water.</summary>
        public static float SdInner(float x, float z)
        {
            float best = 1e18f; bool inside = false;
            Vector2 a = inner[innerN - 1];
            for (int i = 0; i < innerN; i++)
            {
                Vector2 b = inner[i];
                float ex = b.x - a.x, ez = b.y - a.y;
                float wx = x - a.x, wz = z - a.y;
                float t = Mathf.Clamp01((wx * ex + wz * ez) / (ex * ex + ez * ez));
                float dx = wx - ex * t, dz = wz - ez * t;
                float d = dx * dx + dz * dz;
                if (d < best) best = d;
                if ((a.y > z) != (b.y > z) && x < a.x + ex * (z - a.y) / ez) inside = !inside;
                a = b;
            }
            float dist = Mathf.Sqrt(best);
            return inside ? -dist : dist;
        }

        const float ArcBlend = 12f;                 // m: near a medial axis (a ridge, the bisector of a headland corner) the arcs of the
                                                    // competing shore segments are blended over about this distance, so no hard seam
        [System.ThreadStatic] static float[] segDist, segArc;

        /// <summary>
        /// Along-shore coordinate (m, 0..ShoreLength): arc length of the inner shoreline point nearest to (x, z), softly
        /// blended between shore segments that are almost equally near. The cliff strata vary along it, so faces that
        /// point at the cone (the headland) get the same detail as the rest. Thread-safe after Build.
        /// </summary>
        public static float ShoreArc(float x, float z)
        {
            float[] sd = segDist, sa = segArc;                              // thread-static: read once, not per segment
            if (sd == null || sd.Length < innerN) { segDist = sd = new float[innerN]; segArc = sa = new float[innerN]; }
            float best = 1e18f, bestArc = 0f;
            int ia = innerN - 1;
            for (int i = 0; i < innerN; i++)
            {
                Vector2 a = inner[ia], b = inner[i];
                float ex = b.x - a.x, ez = b.y - a.y;
                float t = Mathf.Clamp01(((x - a.x) * ex + (z - a.y) * ez) / (ex * ex + ez * ez));
                float dx = x - a.x - ex * t, dz = z - a.y - ez * t;
                float d = Mathf.Sqrt(dx * dx + dz * dz);
                float a0 = innerArc[ia], a1 = innerArc[i];
                if (a1 < a0) a1 += ShoreLength;                             // the segment that crosses the origin
                sd[i] = d; sa[i] = a0 + (a1 - a0) * t;
                if (d < best) { best = d; bestArc = sa[i]; }
                ia = i;
            }
            // soft minimum: offsets from the nearest arc (wrapped to +-half the shore), weighted by how much farther each segment is
            const float Cut = 0.0183f;                                      // exp(-4): weights fade to exactly zero there, no step
            float sw = 0f, so = 0f;
            for (int i = 0; i < innerN; i++)
            {
                float extra = sd[i] - best;
                if (extra >= 4f * ArcBlend) continue;
                float w = Mathf.Exp(-extra / ArcBlend) - Cut;
                if (w <= 0f) continue;
                float off = Mathf.Repeat(sa[i] - bestArc + 0.5f * ShoreLength, ShoreLength) - 0.5f * ShoreLength;
                sw += w; so += w * off;
            }
            return Mathf.Repeat(bestArc + so / sw, ShoreLength);
        }

        static void RimInfo(float x, float z, out float H, out float W)
        {
            float sw = 0f, h = 0f, w = 0f;
            for (int i = 0; i < spN; i++)
            {
                float dx = spX[i] - x, dz = spZ[i] - z;
                float d2 = dx * dx + dz * dz + 30f;
                float k = 1f / (d2 * d2);
                sw += k; h += k * spH[i]; w += k * spW[i];
            }
            H = h / sw; W = w / sw;
        }

        static float VNoise(float x, float z)
        {
            int ix = Mathf.FloorToInt(x), iz = Mathf.FloorToInt(z);
            float fx = x - ix, fz = z - iz;
            fx = fx * fx * (3f - 2f * fx); fz = fz * fz * (3f - 2f * fz);
            float a = MathX.Hash(ix, iz), b = MathX.Hash(ix + 1, iz), c = MathX.Hash(ix, iz + 1), d = MathX.Hash(ix + 1, iz + 1);
            return a + (b - a) * fx + (c - a) * fz + (a - b - c + d) * fx * fz;
        }

        /// <summary>Thread-safe fractal value noise, 0..1.</summary>
        public static float Fbm(float x, float z, int oct)
        {
            float a = 0.5f, s = 0f, n = 0f;
            for (int i = 0; i < oct; i++)
            {
                s += a * VNoise(x + i * 37.1f, z + i * 91.7f);
                n += a; a *= 0.5f; x *= 2.03f; z *= 2.03f;
            }
            return s / n;
        }

        /// <summary>1 in the village quay sector (where a built quay replaces the natural shore), 0 elsewhere.</summary>
        public static float QuaySector(float z)
        {
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-292f, -268f, z)) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-8f, 14f, z)));
        }

        static float Seabed(float d, float x, float z)
        {
            float quay = QuaySector(z) * (x > -400f ? 1f : 0f);
            float h0 = Mathf.Lerp(0.5f, 3.2f, quay);
            float h = d < 280f ? -(h0 + 0.13f * d) : -(h0 + 36.4f) - (d - 280f) * 0.55f;
            if (h < -320f) h = -320f;
            if (d < 330f) h += (Fbm(x / 9f, z / 9f, 3) - 0.5f) * 2.2f * Mathf.Clamp01(d / 8f) + (Fbm(x / 47f, z / 47f, 2) - 0.5f) * 5f * Mathf.Clamp01(d / 40f);
            return h;
        }

        /// <summary>Ground height without the path carved in.</summary>
        /// <summary>Height of the caldera rim crest nearest to (x, z) (the strata are counted down from it).</summary>
        public static float RimHeight(float x, float z) { float H, W; RimInfo(x, z, out H, out W); return H; }

        public static float BaseHeight(float x, float z, float pathDist) { float sd; return BaseHeight(x, z, pathDist, out sd); }

        public static float BaseHeight(float x, float z, float pathDist, out float sd)
        {
            sd = SdInner(x, z);
            if (sd <= 0f) return Seabed(-sd, x, z);
            float H, W;
            RimInfo(x, z, out H, out W);
            float quay = QuaySector(z) * (sd < 60f ? 1f : 0f);
            float s0 = 12f * quay, h0 = 1.6f * quay;
            float mask = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(5f, 24f, pathDist));
            float h;
            if (sd < W)
            {
                float t = Mathf.Clamp01((sd - s0) / Mathf.Max(W - s0, 1f));
                h = h0 + (H - h0) * Mathf.Pow(t, 1.25f);
                if (H > 0f)
                {
                    float cliff = 4f * t * (1f - t) * mask;
                    float n = Fbm(x / 41f, z / 41f, 4);
                    h += (n - 0.5f) * 16f * cliff;
                    h += Mathf.Sin(h * 0.33f + n * 9f) * 2.2f * cliff;      // strata ledges
                    h += (Fbm(x / 7f, z / 7f, 2) - 0.5f) * 1.6f * cliff;
                }
            }
            else
            {
                float b = sd - W;
                float fall = Mathf.Clamp01(b / RingThickness);
                h = H * Mathf.Pow(1f - fall, 1.3f);
                h += (Fbm(x / 230f, z / 230f, 3) - 0.5f) * 26f * Mathf.Clamp01(b / 120f) * (1f - fall) * mask;
                h += (Fbm(x / 31f, z / 31f, 2) - 0.5f) * 3f * Mathf.Clamp01(b / 30f) * mask;
                if (b > RingThickness) h = -Mathf.Min(60f, (b - RingThickness) * 0.06f) - 0.5f;
            }
            return h;
        }

        /// <summary>Ground height (terrain and sea floor) with the path bed carved in.</summary>
        public static float Height(float x, float z) { float sd; return Sample(x, z, out sd); }

        /// <summary>Ground height and signed shore distance in one evaluation (thread-safe after Build).</summary>
        public static float Sample(float x, float z, out float sd)
        {
            float s, d;
            bool nearPath = PathDef.Nearest(x, z, out s, out d);
            float h = BaseHeight(x, z, nearPath ? d : 100f, out sd);
            if (nearPath)
            {
                float hw = PathDef.HalfWidth(s) + 0.7f;
                float w = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(hw, hw + 7.5f, d));
                float bed = PathDef.Pos(s).y - (PathDef.IsStairs(s) ? 0.32f : 0.07f);
                h = Mathf.Lerp(h, bed, w);
            }
            return h;
        }

        public static Vector3 Normal(float x, float z, float e)
        {
            float hx = Height(x + e, z) - Height(x - e, z);
            float hz = Height(x, z + e) - Height(x, z - e);
            return new Vector3(-hx, 2f * e, -hz).normalized;
        }
    }
}
