using System.Collections.Generic;
using UnityEngine;

namespace CASCADE
{
    /// <summary>
    /// Section 4: the gravel path up the headland with rough stone edging, long stone-fronted steps where the slope
    /// exceeds 0.14, lamp posts on the bay side, three stone benches; the last 25 m before the keeper's house are paved.
    /// (The bay-side railing is one run with the promenade railing, see ArchPromenade.)
    /// </summary>
    internal static class ArchHeadland
    {
        public const float StepOn = 0.14f, StepOff = 0.125f;   // slope hysteresis for steps
        public const float Rise = 0.16f;                       // riser of the long steps (camera stays within 0.1 m)
        public const float Lift = 0.02f;                       // treads sit this much above the mean path line
        public const float RampCell = 1.5f;
        public const float NoseDepth = 0.22f;                  // stone front strip of each step
        public const float EdgeW = 0.2f;
        public const float PavedEnd = 25f;
        public const float LampSpacing = 32f;
        public const float LampStopBeforeEnd = 45f;            // keep the keeper's window the only light at the end
        static readonly float[] LampFixed = { 1118f, 1150f, 1180f, 1210f, 1240f };   // rulers of the wave shot (1150 and 1240 are required)
        static readonly float[] BenchAt = { 140f, 330f, 520f };                       // metres after the promenade end

        static readonly Vector3 Up = ArchGeo.Up;

        public static void Build(ArchCtx c)
        {
            float s0 = PathDef.SPromEnd, L = PathDef.Length;

            // ---- surface: ramps and long steps ----
            float s = s0, prevEnd = PathDef.Pos(s0).y;
            bool stepping = false;
            while (s < L - 1e-3f)
            {
                float slope = PathDef.Slope(Mathf.Min(s + 0.5f, L - 1f));
                stepping = stepping ? slope > StepOff : slope > StepOn;
                float ds = stepping ? Mathf.Clamp(Rise / Mathf.Max(slope, 0.05f), 0.6f, 1.6f) : RampCell;
                float s1 = Mathf.Min(s + ds, L);
                if (L - s1 < 0.35f) s1 = L;
                ArchFrame a = ArchGeo.Frame(s), b = ArchGeo.Frame(s1);
                float y0, y1;
                if (stepping) { y0 = y1 = PathDef.Pos((s + s1) * 0.5f).y + Lift; }
                else { y0 = a.p.y; y1 = b.p.y; }
                Material top = s1 > L - PavedEnd ? c.Paving : c.Gravel;
                MeshBuilder mb = c.S(a.p.y);
                Vector3 aL = ArchGeo.Y(a.At(a.hw), y0), aR = ArchGeo.Y(a.At(-a.hw), y0);
                Vector3 bL = ArchGeo.Y(b.At(b.hw), y1), bR = ArchGeo.Y(b.At(-b.hw), y1);
                if (y0 - prevEnd > 0.02f)
                {
                    // stone-fronted riser with a stone nose on the tread
                    float fn = Mathf.Min(NoseDepth / Mathf.Max(ArchGeo.FlatDist(a.p, b.p), 0.3f), 0.5f);
                    Vector3 nL = Vector3.Lerp(aL, bL, fn), nR = Vector3.Lerp(aR, bR, fn);
                    ArchGeo.Quad(mb, c.Rock, ArchGeo.Y(aL, prevEnd - 0.1f), ArchGeo.Y(aR, prevEnd - 0.1f), aR, aL, -a.f);
                    ArchGeo.Quad(mb, c.Rock, aL, aR, nR, nL, Up);
                    ArchGeo.Quad(mb, top, nL, nR, bR, bL, Up);
                }
                else ArchGeo.Quad(mb, top, aL, aR, bR, bL, Up);
                prevEnd = y1;
                s = s1;
            }

            // ---- rough stone edging on both sides ----
            var rng = new System.Random(9131);
            for (int side = 1; side >= -1; side -= 2)
            {
                Vector3[] prev = null;
                int n = Mathf.CeilToInt((L - s0) / 1.1f);
                for (int i = 0; i <= n; i++)
                {
                    ArchFrame f = ArchGeo.Frame(Mathf.Lerp(s0, L, i / (float)n));
                    float h1 = 0.12f + 0.10f * (float)rng.NextDouble(), h2 = 0.07f + 0.10f * (float)rng.NextDouble();
                    float w = EdgeW + 0.08f * (float)rng.NextDouble();
                    float o = f.hw - 0.02f;
                    // on the bay side the outer face continues down the fill as a dry-stone retaining wall
                    float foot = side > 0 ? -ArchGeo.Depth(f.At(o + w), f.l, 1.6f, 0.6f, 12f) : -0.6f;
                    var ring = new[]
                    {
                        f.At(side * o, -0.6f), f.At(side * o, h1), f.At(side * (o + w * 0.6f), Mathf.Max(h1, h2) + 0.03f),
                        f.At(side * (o + w), h2), f.At(side * (o + w + 0.05f), foot)
                    };
                    MeshBuilder mb = c.S(f.p.y);
                    if (prev != null) ArchGeo.Loft(mb, prev, ring, null, c.Rock, false);
                    if (i == 0) ArchGeo.Cap(mb, c.Rock, ring, -f.f);
                    if (i == n) ArchGeo.Cap(mb, c.Rock, ring, f.f);
                    prev = ring;
                }
            }

            // ---- lamp posts on the bay side ----
            float last = s0;
            for (int i = 0; i < LampFixed.Length; i++)
            {
                if (LampFixed[i] < s0 + 6f || LampFixed[i] > L - LampStopBeforeEnd) continue;
                ArchPromenade.Lamp(c, LampFixed[i]);
                last = LampFixed[i];
            }
            for (s = last + LampSpacing; s < L - LampStopBeforeEnd; s += LampSpacing) ArchPromenade.Lamp(c, s);

            // ---- three stone benches on the land side, looking over the bay ----
            for (int i = 0; i < BenchAt.Length; i++)
            {
                float sb = s0 + BenchAt[i];
                if (sb > L - 30f) continue;
                ArchFrame f = ArchGeo.Frame(sb);
                Vector3 at = f.At(-(f.hw + 1.0f));
                at.y = Mathf.Max(ArchGeo.Ground(at), f.p.y - 0.1f);
                ArchParts.Bench(c, at, f.f);
            }
        }
    }
}
