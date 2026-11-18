using System.Collections.Generic;
using UnityEngine;

namespace CASCADE
{
    /// <summary>
    /// Section 0: the quay slab (top y = 2), the battered quay wall that the drawdown exposes, the wet band, kerb,
    /// bollards with mooring rings, two flights of narrow steps down the wall face (built against the outside of the wall:
    /// the ground behind the wall is solid), the mole with its beacon post, quay lamps.
    /// </summary>
    internal static class ArchQuay
    {
        public const float Top = 2f;             // quay surface
        public const float WallBottom = -9f;
        public const float Batter = 0.06f;       // metres outwards per metre down
        public const float ZSouth = -300f, ZNorth = -6f, XLand = 0.5f;
        public const float KerbW = 0.35f, KerbH = 0.14f;
        public const float WetW = 1.4f;          // dark wet strip behind the kerb
        public const float WetWall = 1.2f;       // wet upper part of the wall
        public const float DarkBelow = -0.2f;    // the weed-dark part of the wall starts here (normally under water)
        public const float FlightW = 1.25f;      // width of the wall steps
        public const float StepRise = 0.2f, StepRun = 0.3f, StepLanding = 1.2f, TopLanding = 1.1f;
        public const int StepCount = 30;         // 2.0 -> -4.0
        public const float BollardSpacing = 12f;
        public const int QuayLamps = 5;          // lamp posts on the water side (0 = none)
        public const float QuayLampSpacing = 44f;
        public const float MoleZ = -285f, MoleHalf = 2.5f, MoleTop = 2.2f, MoleLength = 70f, MoleBottom = -16f;

        static readonly float[] FlightTop = { -188f, -112f };   // z of the top step (ascending z order)
        static readonly float[] FlightDir = { -1f, 1f };        // direction of descent along z
        public const float SlipTop = -279f, SlipLanding = 1.6f, SlipLen = 22f, SlipBottom = -2.4f, SlipW = 3f;   // slipway along the wall, descending north
        static readonly float[] LadderZ = { -150f, -66f, -30f };
        public const float RingSpacing = 9f;

        static readonly Vector3 Up = ArchGeo.Up;

        public static void Build(ArchCtx c)
        {
            MeshBuilder mb = c.solid[0];
            float xw = Config.QuayEdgeX;
            int nf = FlightTop.Length;
            var lo = new float[nf + 1]; var hi = new float[nf + 1];     // coping gaps at the top landings (slipway first: southmost)
            lo[0] = SlipTop - SlipLanding; hi[0] = SlipTop;
            for (int i = 0; i < nf; i++)
            {
                float a = FlightTop[i], b = FlightTop[i] - FlightDir[i] * TopLanding;
                lo[i + 1] = Mathf.Min(a, b); hi[i + 1] = Mathf.Max(a, b);
            }
            int ng = nf + 1;

            // edge zone: coping (open at the landings) and the wall face
            for (int i = 0; i <= ng; i++)
            {
                EdgePiece(c, mb, xw, i == 0 ? ZSouth : hi[i - 1], i == ng ? ZNorth : lo[i], true);
                if (i < ng) EdgePiece(c, mb, xw, lo[i], hi[i], false);
            }
            for (int i = 0; i < nf; i++) Flight(c, mb, xw, FlightTop[i], FlightDir[i]);
            Slipway(c, mb, xw);
            for (int i = 0; i < LadderZ.Length; i++) Ladder(c, xw, LadderZ[i]);
            for (float z = -268f; z < ZNorth - 3f; z += RingSpacing)
            {
                bool clear = true;
                for (int i = 0; i < ng; i++) if (z > lo[i] - 1f && z < hi[i] + 1f) clear = false;
                for (int i = 0; i < nf; i++) if (Mathf.Abs(z - (FlightTop[i] + FlightDir[i] * StepCount * StepRun * 0.5f)) < StepCount * StepRun * 0.5f + StepLanding + 1f) clear = false;
                for (int i = 0; i < LadderZ.Length; i++) if (Mathf.Abs(z - LadderZ[i]) < 1.5f) clear = false;
                if (z < SlipTop + SlipLen + 1f) clear = false;
                if (clear) WallRing(c, xw, z);
            }

            // the top: wet strip behind the kerb, then dry stone to the land side
            ArchGeo.TopQuad(mb, c.StoneWet, xw + KerbW, xw + KerbW + WetW, ZSouth, ZNorth, Top);
            ArchGeo.TopQuad(mb, c.Stone, xw + KerbW + WetW, XLand, ZSouth, ZNorth, Top);
            Zoned(c, mb, P(xw, ZNorth), P(XLand, ZNorth), WallBottom, Top, Vector3.forward, Vector3.left, Vector3.zero);
            Zoned(c, mb, P(xw, ZSouth), P(XLand, ZSouth), WallBottom, Top, Vector3.back, Vector3.left, Vector3.zero);

            Mole(c, mb, xw);

            // bollards along the edge (not at the stair landings)
            for (float z = -276f; z < ZNorth - 4f; z += BollardSpacing)
            {
                bool atLanding = false;
                for (int i = 1; i < ng; i++) if (z > lo[i] - 0.8f && z < hi[i] + 0.8f) atLanding = true;
                if (!atLanding) ArchParts.Bollard(c, mb, new Vector3(xw + 0.85f, Top, z), Vector3.forward);
            }
            for (float x = xw - 12f; x > xw - MoleLength + 4f; x -= BollardSpacing)
                ArchParts.Bollard(c, mb, new Vector3(x, MoleTop, MoleZ + MoleHalf - 0.75f), Vector3.left);

            // lamp posts on the water side: rulers in front of the cone during the first glance
            for (int i = 0; i < QuayLamps; i++)
            {
                float z = -222f + i * QuayLampSpacing;
                ArchParts.LampPost(c, z - PathDef.P[0].z, new Vector3(xw + 2.3f, Top, z), 0.4f, Vector3.right);
            }
        }

        static Vector3 P(float x, float z) { return new Vector3(x, 0f, z); }

        static Material ZoneMat(ArchCtx c, float y) { return y > Top - WetWall ? c.StoneWet : y > DarkBelow ? c.Stone : c.Rock; }

        static Vector3 Bat(Vector3 p, Vector3 dir, float y)
        {
            float k = Batter * (Top - y);
            return new Vector3(p.x + dir.x * k, y, p.z + dir.z * k);
        }

        /// <summary>Vertical (battered) face over the plan segment a-b, split into wet band / stone / weed-dark zones.
        /// batA and batB are the directions in which the two ends lean out with depth.</summary>
        static void Zoned(ArchCtx c, MeshBuilder mb, Vector3 a, Vector3 b, float yLo, float yHi, Vector3 hint, Vector3 batA, Vector3 batB)
        {
            float top = yHi;
            for (int z = 0; z < 3; z++)
            {
                float bot = z == 0 ? Mathf.Max(Top - WetWall, yLo) : z == 1 ? Mathf.Max(DarkBelow, yLo) : yLo;
                if (bot >= top) continue;
                Material m = z == 0 ? c.StoneWet : z == 1 ? c.Stone : c.Rock;
                ArchGeo.Quad(mb, m, Bat(a, batA, bot), Bat(b, batB, bot), Bat(b, batB, top), Bat(a, batA, top), hint);
                top = bot;
            }
        }

        /// <summary>Pumice coping (or, at a landing, plain wet stone) and the wall face for one stretch of the quay edge.</summary>
        static void EdgePiece(ArchCtx c, MeshBuilder mb, float xw, float za, float zb, bool kerb)
        {
            if (zb - za < 0.05f) return;
            if (kerb) Coping(c, mb, xw, za, zb);
            else ArchGeo.TopQuad(mb, c.StoneWet, xw, xw + KerbW, za, zb, Top);
            Zoned(c, mb, P(xw, za), P(xw, zb), WallBottom, Top, Vector3.left, Vector3.left, Vector3.left);
        }

        /// <summary>Coping blocks along the quay edge: top at Top + KerbH over the same strip as the old kerb (gull perches
        /// stand on it), a rounded nose oversailing the wall face by 5 cm, the underside closing onto the wall top.</summary>
        static void Coping(ArchCtx c, MeshBuilder mb, float xw, float za, float zb)
        {
            float kt = Top + KerbH, xk = xw + KerbW;
            var prof = new[]
            {
                new Vector2(xk, Top), new Vector2(xk, kt), new Vector2(xw + 0.03f, kt), new Vector2(xw - 0.035f, kt - 0.025f),
                new Vector2(xw - 0.05f, kt - 0.07f), new Vector2(xw - 0.05f, kt - 0.105f), new Vector2(xw - 0.03f, Top)
            };
            var ra = new Vector3[prof.Length]; var rb = new Vector3[prof.Length];
            for (int j = 0; j < prof.Length; j++) { ra[j] = new Vector3(prof[j].x, prof[j].y, za); rb[j] = new Vector3(prof[j].x, prof[j].y, zb); }
            ArchGeo.Loft(mb, ra, rb, null, c.Pumice, true);
            ArchGeo.Cap(mb, c.Pumice, ra, Vector3.back);
            ArchGeo.Cap(mb, c.Pumice, rb, Vector3.forward);
        }

        /// <summary>Iron mooring ring hanging from a staple in the wall face below the coping.</summary>
        static void WallRing(ArchCtx c, float xw, float z)
        {
            MeshBuilder mb = c.fragile[0];
            float y = Top - 0.3f, face = xw - Batter * (Top - y);
            mb.Box(c.Iron, new Vector3(face - 0.01f, y, z), new Vector3(0.06f, 0.08f, 0.07f), 0f, 0);
            ArchGeo.RingTube(mb, c.Iron, new Vector3(face - 0.06f, y - 0.12f, z), Vector3.forward, Vector3.up, 0.1f, 0.02f, 10, 5);
        }

        /// <summary>Iron ladder on the battered wall face from below the water up over the coping, with a grab hoop per stile.</summary>
        static void Ladder(ArchCtx c, float xw, float z)
        {
            MeshBuilder mb = c.fragile[0];
            const float Off = 0.13f, Half = 0.22f, Low = -3.2f;
            float kt = Top + KerbH;
            var pts = new List<Vector3>(8);
            for (int e = -1; e <= 1; e += 2)
            {
                float zs = z + e * Half;
                pts.Clear();
                pts.Add(new Vector3(xw - Batter * (Top - Low) - Off, Low, zs));
                pts.Add(new Vector3(xw - Off, Top, zs));
                pts.Add(new Vector3(xw - Off, kt + 0.72f, zs));
                pts.Add(new Vector3(xw - 0.07f, kt + 0.86f, zs));
                pts.Add(new Vector3(xw + 0.05f, kt + 0.88f, zs));
                pts.Add(new Vector3(xw + 0.14f, kt + 0.74f, zs));
                pts.Add(new Vector3(xw + 0.16f, kt - 0.01f, zs));
                mb.Tube(c.Iron, pts, 0.022f, 6);
                for (float y = Low + 0.6f; y < Top - 0.4f; y += 1.6f)            // stand-off brackets into the wall
                {
                    float f = xw - Batter * (Top - y);
                    mb.Cylinder(c.Iron, new Vector3(f + 0.04f, y, zs), new Vector3(f - Off, y, zs), 0.016f, 0.016f, 5, false);
                }
            }
            for (float y = Low + 0.2f; y < kt + 0.1f; y += 0.3f)                  // rungs
            {
                float x = xw - Batter * Mathf.Max(Top - y, 0f) - Off;
                mb.Cylinder(c.Iron, new Vector3(x, y, z - Half), new Vector3(x, y, z + Half), 0.017f, 0.017f, 5, false);
            }
        }

        /// <summary>Slipway against the outside of the wall: a landing level with the quay (the coping is open there), then a
        /// 1:5 ramp north into the water with two timber skids.</summary>
        static void Slipway(ArchCtx c, MeshBuilder mb, float xw)
        {
            float xo = xw - SlipW, xi = xw, zL = SlipTop - SlipLanding, z0 = SlipTop, z1 = SlipTop + SlipLen;
            ArchGeo.Quad(mb, c.StoneWet, new Vector3(xo, Top, zL), new Vector3(xi, Top, zL), new Vector3(xi, Top, z0), new Vector3(xo, Top, z0), Up);
            Zoned(c, mb, P(xo, zL), P(xo, z0), WallBottom, Top, Vector3.left, Vector3.zero, Vector3.zero);
            Zoned(c, mb, P(xo, zL), P(xi, zL), WallBottom, Top, Vector3.back, Vector3.zero, Vector3.zero);
            const int N = 11;
            for (int k = 0; k < N; k++)
            {
                float za = Mathf.Lerp(z0, z1, k / (float)N), zb = Mathf.Lerp(z0, z1, (k + 1) / (float)N);
                float ya = Mathf.Lerp(Top, SlipBottom, k / (float)N), yb = Mathf.Lerp(Top, SlipBottom, (k + 1) / (float)N);
                Material m = ZoneMat(c, (ya + yb) * 0.5f);
                ArchGeo.Quad(mb, m, new Vector3(xo, ya, za), new Vector3(xi, ya, za), new Vector3(xi, yb, zb), new Vector3(xo, yb, zb), Up + Vector3.forward);
                Zoned(c, mb, P(xo, za), P(xo, zb), WallBottom, yb, Vector3.left, Vector3.zero, Vector3.zero);
                ArchGeo.Tri(mb, m, new Vector3(xo, yb, za), new Vector3(xo, ya, za), new Vector3(xo, yb, zb), Vector3.left);
            }
            Zoned(c, mb, P(xo, z1), P(xi, z1), WallBottom, SlipBottom, Vector3.forward, Vector3.zero, Vector3.zero);
            // two timber skids down the ramp
            Vector3 a = new Vector3(0f, Top, z0), b = new Vector3(0f, SlipBottom, z1);
            Vector3 d = (b - a).normalized;
            Quaternion rot = Quaternion.LookRotation(d, Vector3.up);
            Vector3 n = rot * Vector3.up;
            for (int e = 0; e < 2; e++)
            {
                float x = e == 0 ? xw - 0.85f : xw - 2.15f;
                Vector3 mid = (a + b) * 0.5f + new Vector3(x, 0f, 0f) + n * 0.03f;
                mb.Box(c.WoodDark, mid, new Vector3(0.14f, 0.06f, (b - a).magnitude - 0.1f), rot, 8);
            }
        }

        /// <summary>A flight of narrow stone steps against the outside of the quay wall, parallel to it: a landing level
        /// with the quay (the kerb is open there), then down to y = -4 and a lower landing.</summary>
        static void Flight(ArchCtx c, MeshBuilder mb, float xw, float zTop, float dir)
        {
            float xo = xw - FlightW, xi = xw;
            Vector3 along = new Vector3(0f, 0f, dir);
            // top landing
            float zl = zTop - dir * TopLanding;
            ArchGeo.Quad(mb, c.StoneWet, new Vector3(xo, Top, zl), new Vector3(xi, Top, zl), new Vector3(xi, Top, zTop), new Vector3(xo, Top, zTop), Up);
            Zoned(c, mb, P(xo, zl), P(xo, zTop), WallBottom, Top, Vector3.left, Vector3.zero, Vector3.zero);
            Zoned(c, mb, P(xo, zl), P(xi, zl), WallBottom, Top, -along, Vector3.zero, Vector3.zero);
            float y = Top, za = zTop, zb = zTop;
            for (int k = 1; k <= StepCount + 1; k++)
            {
                bool landing = k > StepCount;
                if (!landing) y = Top - StepRise * k;
                za = zb;
                zb = za + dir * (landing ? StepLanding : StepRun);
                Material m = ZoneMat(c, y);
                ArchGeo.Quad(mb, m, new Vector3(xo, y, za), new Vector3(xi, y, za), new Vector3(xi, y, zb), new Vector3(xo, y, zb), Up);
                if (!landing)
                    ArchGeo.Quad(mb, m, new Vector3(xo, y, za), new Vector3(xi, y, za), new Vector3(xi, y + StepRise, za), new Vector3(xo, y + StepRise, za), along);
                Zoned(c, mb, P(xo, za), P(xo, zb), WallBottom, y, Vector3.left, Vector3.zero, Vector3.zero);     // outer face below the step
            }
            Zoned(c, mb, P(xo, zb), P(xi, zb), WallBottom, y, along, Vector3.zero, Vector3.zero);                // low end
        }

        /// <summary>Breakwater projecting west from the south end of the quay, with a small unlit beacon post: a ruler for the wave.</summary>
        static void Mole(ArchCtx c, MeshBuilder mb, float xw)
        {
            float x0 = xw, x1 = xw - MoleLength;
            float zn = MoleZ + MoleHalf, zs = MoleZ - MoleHalf;
            Vector3 N = Vector3.forward, S = Vector3.back, W = Vector3.left;
            ArchGeo.TopQuad(mb, c.Stone, x1, x0, zs + 0.3f, zn - 0.3f, MoleTop);
            mb.Box(c.Pumice, new Vector3((x0 + x1) * 0.5f, MoleTop + KerbH * 0.5f, zn - 0.15f), new Vector3(MoleLength, KerbH, 0.3f), 0f, 8);
            mb.Box(c.Pumice, new Vector3((x0 + x1) * 0.5f, MoleTop + KerbH * 0.5f, zs + 0.15f), new Vector3(MoleLength, KerbH, 0.3f), 0f, 8);
            ArchGeo.VQuad(mb, c.Stone, P(x0, zs), P(x0, zn), Top, MoleTop, Vector3.right);      // the step up from the quay onto the mole
            ArchGeo.TopQuad(mb, c.Stone, x1, x0, zn - 0.3f, zn, MoleTop);     // under the kerbs (seen at the open ends)
            ArchGeo.TopQuad(mb, c.Stone, x1, x0, zs, zs + 0.3f, MoleTop);
            Zoned(c, mb, P(x0, zn), P(x1, zn), MoleBottom, MoleTop, N, N, N + W);
            Zoned(c, mb, P(x0, zs), P(x1, zs), MoleBottom, MoleTop, S, S, S + W);
            Zoned(c, mb, P(x1, zs), P(x1, zn), MoleBottom, MoleTop, W, S + W, N + W);

            // beacon post at the tip (unlit)
            MeshBuilder fb = c.fragile[0];
            Vector3 b = new Vector3(x1 + 1.8f, MoleTop, MoleZ);
            fb.Cylinder(c.WhiteOld, b, b + Up * 1.1f, 0.55f, 0.45f, 8, true);
            fb.Cylinder(c.Iron, b + Up * 1.1f, b + Up * 4.2f, 0.08f, 0.06f, 6, false);
            fb.Cylinder(c.Iron, b + Up * 4.16f, b + Up * 4.22f, 0.30f, 0.30f, 6, true);
            fb.Cylinder(c.WinDark, b + Up * 4.22f, b + Up * 4.66f, 0.2f, 0.2f, 6, false);
            fb.Cylinder(c.Iron, b + Up * 4.66f, b + Up * 4.98f, 0.27f, 0.02f, 6, true);
        }
    }
}
