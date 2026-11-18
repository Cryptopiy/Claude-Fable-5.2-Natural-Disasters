using System.Collections.Generic;
using UnityEngine;

namespace CASCADE
{
    /// <summary>
    /// Stair-road furniture: a small landing terrace with a stone bench and a lamp on the outside of each hairpin,
    /// lamp posts at the U-turn and mid-leg on the downhill side, wooden utility poles (uphill side of the legs, inside
    /// of the bends) and their two wires.
    /// </summary>
    internal static class ArchLandings
    {
        public const float TerraceHalf = 2.6f;      // half length along the path
        public const float TerraceDepth = 2.9f;     // outwards from the step edge
        public const float TerraceDrop = 0.12f;     // terrace floor below the path at its upper end
        public const float PoleOffset = 1.3f;       // utility poles: outside the walkable width, uphill side
        public const float LampOffset = 0.65f;      // stair lamps: just behind the parapet
        public const float InnerPoleOffset = 1.7f;  // poles inside the bends

        static readonly Vector3 Up = ArchGeo.Up;
        static readonly List<float> poleS = new List<float>(8);
        static readonly List<Vector3> poleUp = new List<Vector3>(8), poleLo = new List<Vector3>(8);

        public static void Build(ArchCtx c)
        {
            poleS.Clear(); poleUp.Clear(); poleLo.Clear();
            List<ArchBend> bends = ArchStairs.Bends;

            for (int i = 0; i < bends.Count; i++)
            {
                ArchBend b = bends[i];
                if (i == 0) StairLamp(c, ArchStairs.FirstRiser + 3f, b.s1, b.outer);   // foot of the stairs, outer side of the U-turn
                else Terrace(c, b);
                InnerPole(c, b, ArchGeo.Frame(b.apex));
            }

            // legs between the bends: lamp on the downhill side, pole on the uphill side
            for (int i = 0; i + 1 < bends.Count; i++)
            {
                float sm = (bends[i].s1 + bends[i + 1].s0) * 0.5f;
                ArchFrame f = ArchGeo.Frame(sm);
                Vector3 ql = f.At(f.hw + 3.5f), qr = f.At(-(f.hw + 3.5f));
                int down = WorldGen.BaseHeight(ql.x, ql.z, 100f) < WorldGen.BaseHeight(qr.x, qr.z, 100f) ? 1 : -1;
                StairLamp(c, sm, bends[i + 1].s0, down);
                AddPole(c, sm, f.At(-down * (f.hw + PoleOffset)), f.p.y + ArchStairs.RetainHeight(sm));
            }

            // wires from pole to pole in path order
            int n = poleS.Count;
            var order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;
            System.Array.Sort(order, (x, y) => poleS[x].CompareTo(poleS[y]));
            for (int i = 0; i + 1 < n; i++)
            {
                ArchParts.Wire(c, poleUp[order[i]], poleUp[order[i + 1]], 0.5f);
                ArchParts.Wire(c, poleLo[order[i]], poleLo[order[i + 1]], 0.8f);
            }
        }

        /// <summary>Lamp post just behind the parapet on the given side. If the ground stands high there (retaining wall),
        /// it moves up the path to the first place with a parapet; failing that it stands on top of the retaining wall.</summary>
        static void StairLamp(ArchCtx c, float s, float sMax, int side)
        {
            float at = -1f;
            for (float q = s; q <= sMax; q += 1f)
                if (ArchStairs.SideIsLow(q, side)) { at = q; break; }
            if (at >= 0f)
            {
                ArchFrame f = ArchGeo.Frame(at);
                ArchParts.LampPost(c, at, f.At(side * (f.hw + LampOffset)), 0.8f, -side * f.l);
            }
            else
            {
                ArchFrame f = ArchGeo.Frame(s);
                ArchParts.LampPost(c, s, f.At(side * (f.hw + LampOffset), ArchStairs.RetainHeight(s)), 0.3f, -side * f.l);
            }
        }

        static void AddPole(ArchCtx c, float s, Vector3 at, float minGround)
        {
            float g = ArchGeo.Ground(at);
            float top = Mathf.Max(g, minGround);
            Vector3 up, lo;
            ArchParts.Pole(c, ArchGeo.Y(at, top), top - g + 1.2f, out up, out lo);
            poleS.Add(s); poleUp.Add(up); poleLo.Add(lo);
        }

        /// <summary>Raised stone platform on the outside of the hairpin (see ArchStairs.FindTerrace), level with the path at
        /// its upper end, with a parapet on three sides, a bench and a lamp post.</summary>
        static void Terrace(ArchCtx c, ArchBend b)
        {
            const int N = 8;
            int side = b.outer;
            float s0 = b.terrace - TerraceHalf, s1 = b.terrace + TerraceHalf;
            float floorY = PathDef.Pos(s1).y - TerraceDrop;
            float yb = floorY - 2.8f;                   // foot of the outer walls: below the ground all round
            var inner = new Vector3[N + 1]; var kerb = new Vector3[N + 1]; var outer = new Vector3[N + 1];
            var lat = new Vector3[N + 1];
            for (int j = 0; j <= N; j++)
            {
                ArchFrame f = ArchGeo.Frame(Mathf.Lerp(s0, s1, j / (float)N));
                float oi = f.hw + ArchStairs.StepExt;
                inner[j] = ArchGeo.Y(f.At(side * oi), floorY);
                kerb[j] = ArchGeo.Y(f.At(side * (oi + 0.14f)), floorY);
                outer[j] = ArchGeo.Y(f.At(side * (oi + TerraceDepth)), floorY);
                lat[j] = f.l * side;
                yb = Mathf.Min(yb, floorY - ArchGeo.Depth(outer[j], lat[j], 1.6f, 2.8f, 14f));
            }
            MeshBuilder mb = c.S(floorY);
            const float kerbH = 0.14f;
            for (int j = 0; j < N; j++)
            {
                ArchGeo.Quad(mb, c.Stone, kerb[j], outer[j], outer[j + 1], kerb[j + 1], Up);                       // floor
                ArchGeo.Quad(mb, c.Pumice, inner[j] + Up * kerbH, kerb[j] + Up * kerbH, kerb[j + 1] + Up * kerbH, inner[j + 1] + Up * kerbH, Up);
                ArchGeo.VQuad(mb, c.Pumice, kerb[j], kerb[j + 1], floorY, floorY + kerbH, lat[j]);
                ArchGeo.VQuad(mb, c.WhiteOld, inner[j], inner[j + 1], floorY - 2.8f, floorY + kerbH, -lat[j]);     // face towards the path
                ArchGeo.VQuad(mb, c.WhiteOld, outer[j], outer[j + 1], yb, floorY, lat[j]);
            }
            Vector3 fwd0 = PathDef.FlatDir(s0), fwd1 = PathDef.FlatDir(s1);
            ArchGeo.VQuad(mb, c.WhiteOld, inner[0], outer[0], yb, floorY, -fwd0);
            ArchGeo.VQuad(mb, c.WhiteOld, inner[N], outer[N], yb, floorY, fwd1);
            ArchGeo.VQuad(mb, c.Pumice, inner[0], kerb[0], floorY, floorY + kerbH, -fwd0);
            ArchGeo.VQuad(mb, c.Pumice, inner[N], kerb[N], floorY, floorY + kerbH, fwd1);

            // parapet around the three outer sides
            var pts = new List<Vector3>(N + 3);
            float e0 = s0 + 0.17f, e1 = s1 - 0.17f;
            ArchFrame fa = ArchGeo.Frame(e0), fb = ArchGeo.Frame(e1);
            pts.Add(ArchGeo.Y(fa.At(side * (fa.hw + ArchStairs.StepExt + 0.3f)), floorY));
            for (int j = 0; j <= N; j++)
            {
                ArchFrame f = ArchGeo.Frame(Mathf.Lerp(e0, e1, j / (float)N));
                pts.Add(ArchGeo.Y(f.At(side * (f.hw + ArchStairs.StepExt + TerraceDepth - 0.17f)), floorY));
            }
            pts.Add(ArchGeo.Y(fb.At(side * (fb.hw + ArchStairs.StepExt + 0.3f)), floorY));
            ArchGeo.CopedWall(c, pts, ArchStairs.ParapetH, ArchStairs.ParapetT, 0.2f);

            // bench against the outer parapet, lamp in the lower outer corner
            ArchFrame fm = ArchGeo.Frame(b.terrace);
            float om = fm.hw + ArchStairs.StepExt + TerraceDepth;
            ArchParts.Bench(c, ArchGeo.Y(fm.At(side * (om - 0.75f)), floorY), fm.f);
            ArchFrame fl = ArchGeo.Frame(s0 + 0.8f);
            ArchParts.LampPost(c, fl.s, ArchGeo.Y(fl.At(side * (fl.hw + ArchStairs.StepExt + TerraceDepth - 0.65f)), floorY), 0.3f, -side * fl.l);

            // wayside shrine (kandylaki) in the upper outer corner, facing the path; pots along the parapet foot
            var rng = new System.Random(6100 + (int)b.terrace);
            MeshBuilder fm2 = c.F(floorY);
            ArchFrame fs = ArchGeo.Frame(s1 - 0.78f);
            float os = fs.hw + ArchStairs.StepExt + TerraceDepth;
            ArchDetail.Shrine(fm2, c, ArchGeo.Y(fs.At(side * (os - 0.6f)), floorY), -side * fs.l - fs.f * 0.35f);
            ArchFrame fp = ArchGeo.Frame(b.terrace + 1.25f);
            ArchDetail.Pot(fm2, c, ArchGeo.Y(fp.At(side * (fp.hw + ArchStairs.StepExt + TerraceDepth - 0.58f)), floorY), 1.35f, rng, true);
            fp = ArchGeo.Frame(b.terrace + 1.6f);
            ArchDetail.Pot(fm2, c, ArchGeo.Y(fp.At(side * (fp.hw + ArchStairs.StepExt + TerraceDepth - 0.95f)), floorY), 0.9f, rng, false);
            fp = ArchGeo.Frame(s0 + 1.45f);
            ArchDetail.Pot(fm2, c, ArchGeo.Y(fp.At(side * (fp.hw + ArchStairs.StepExt + TerraceDepth - 0.55f)), floorY), 1.15f, rng, true);
        }

        /// <summary>Utility pole on the inside of a bend, so that no wire crosses the path at low height.</summary>
        static void InnerPole(ArchCtx c, ArchBend b, ArchFrame apex)
        {
            int inner = -b.outer;
            float ground = apex.p.y + (ArchStairs.GroundIsHigh(apex, inner) ? ArchStairs.RetainHeight(apex.s) : 0f);
            AddPole(c, b.apex, apex.At(inner * (apex.hw + InnerPoleOffset)), ground);
        }
    }
}
