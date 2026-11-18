using System.Collections.Generic;
using UnityEngine;

namespace CASCADE
{
    internal sealed class BoatSpec
    {
        public float length, beam, draft, freeboard;
        public int type;                      // 0 fishing boat with a mast, 1 fishing boat with a wheelhouse, 2 rowing boat
        public Material hull, stripe;
    }

    /// <summary>Small wooden boats: a hull lofted from cross-sections (smooth outside, wooden inside), thwarts, mast or wheelhouse.</summary>
    internal static class BoatGen
    {
        const int NS = 8, NP = 5;             // stations stern->bow, points keel->gunwale
        static readonly float[] xprof = { 0f, 0.55f, 0.9f, 0.985f, 1f };
        static readonly float[] yprof = { 0f, 0.13f, 0.48f, 0.8f, 1f };
        static readonly Vector3[,,] P = new Vector3[2, NS, NP];      // world positions per side
        static readonly Vector3[,,] N = new Vector3[2, NS, NP];      // outward normals
        static readonly List<Vector3> lv = new List<Vector3>(64), ln = new List<Vector3>(64);
        static readonly List<int> lt = new List<int>(256);
        static readonly List<Opening> ops = new List<Opening>(2);

        static float HalfBeam(BoatSpec s, float t)
        {
            float k = t < 0.4f ? 0.6f + 0.4f * Mathf.Sin(Mathf.PI * 0.5f * t / 0.4f) : 1f - Mathf.Pow((t - 0.4f) / 0.6f, 2.1f);
            return s.beam * 0.5f * k;
        }
        static float KeelY(BoatSpec s, float t)
        {
            float bow = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1f, t));
            float stern = t < 0.2f ? 0.72f + 0.28f * t / 0.2f : 1f;
            return -s.draft * stern * (1f - bow * bow);
        }
        static float Sheer(BoatSpec s, float t)
        {
            float d = t > 0.35f ? 0.75f * Mathf.Pow((t - 0.35f) / 0.65f, 2f) : 0.3f * Mathf.Pow((0.35f - t) / 0.35f, 2f);
            return s.freeboard * (1f + d);
        }

        /// <summary>
        /// Emits one boat. o = keel midpoint on the waterline, r/u/f = its right, up and bow axes (orthonormal).
        /// Returns the highest perch (mast top, antenna or stem head) in the same space.
        /// </summary>
        public static Vector3 Emit(MeshBuilder mb, Vector3 o, Vector3 r, Vector3 u, Vector3 f, BoatSpec s, System.Random rng)
        {
            float L = s.length;
            // ---- hull grid ----
            for (int side = 0; side < 2; side++)
            {
                float sg = side == 0 ? -1f : 1f;
                for (int i = 0; i < NS; i++)
                {
                    float t = i / (float)(NS - 1), hb = HalfBeam(s, t), ky = KeelY(s, t), sh = Sheer(s, t);
                    for (int j = 0; j < NP; j++)
                    {
                        P[side, i, j] = o + r * (sg * hb * xprof[j]) + u * (ky + (sh - ky) * yprof[j]) + f * ((t - 0.5f) * L);
                        N[side, i, j] = Vector3.zero;
                    }
                }
                for (int i = 0; i < NS - 1; i++)
                    for (int j = 0; j < NP - 1; j++)
                    {
                        Vector3 fn = Vector3.Cross(P[side, i + 1, j] - P[side, i, j], P[side, i, j + 1] - P[side, i, j]);
                        if (fn.sqrMagnitude < 1e-10f) fn = Vector3.Cross(P[side, i + 1, j + 1] - P[side, i, j], P[side, i, j + 1] - P[side, i, j]);
                        if (Vector3.Dot(fn, r * sg - u * 0.35f) < 0f) fn = -fn;
                        fn = fn.normalized;
                        N[side, i, j] += fn; N[side, i + 1, j] += fn; N[side, i, j + 1] += fn; N[side, i + 1, j + 1] += fn;
                    }
            }
            for (int i = 0; i < NS; i++)
            {
                Vector3 k = (N[0, i, 0] + N[1, i, 0]).normalized;          // the keel line is shared by both sides
                N[0, i, 0] = k; N[1, i, 0] = k;
                for (int side = 0; side < 2; side++)
                    for (int j = 1; j < NP; j++) N[side, i, j] = N[side, i, j].normalized;
            }
            for (int side = 0; side < 2; side++)
            {
                Skin(mb, s.hull, side, 0, 3, false);
                Skin(mb, s.stripe, side, 3, NP - 1, false);
                Skin(mb, VKit.Wood, side, 1, NP - 1, true);
            }
            // ---- transom (outside and inside) ----
            Vector3 K = P[0, 0, 0];
            for (int j = 1; j < NP - 1; j++)
                for (int side = 0; side < 2; side++)
                {
                    VKit.T(mb, s.hull, K, P[side, 0, j], P[side, 0, j + 1], -f);
                    VKit.T(mb, VKit.Wood, K, P[side, 0, j], P[side, 0, j + 1], f);
                }
            VKit.T(mb, s.hull, K, P[0, 0, NP - 1], P[1, 0, NP - 1], -f); VKit.T(mb, VKit.Wood, K, P[0, 0, NP - 1], P[1, 0, NP - 1], f);
            // ---- floor boards, gunwale rails ----
            for (int i = 0; i < NS - 1; i++) VKit.Q(mb, VKit.Wood, P[0, i, 1], P[1, i, 1], P[1, i + 1, 1], P[0, i + 1, 1], u);
            for (int side = 0; side < 2; side++)
            {
                lv.Clear();
                for (int i = 0; i < NS; i++) lv.Add(P[side, i, NP - 1]);
                mb.Tube(VKit.WoodDk, lv, 0.03f, 4);
            }
            // ---- dressing from the boat's own stream (the fleet's stream, moorings and perches stay as they were) ----
            var lr = new System.Random(Mathf.RoundToInt(s.length * 997f) * 31 + Mathf.RoundToInt(s.beam * 113f) + s.type * 7);
            Material strake = Third(s, lr.Next(4));
            for (int side = 0; side < 2; side++)
            {
                // a painted rubbing strake along the topsides
                lv.Clear();
                for (int i = 0; i < NS; i++) lv.Add(P[side, i, 3] + N[side, i, 3] * 0.022f);
                mb.Tube(strake, lv, 0.032f, 5);
            }
            if (s.type != 2)
            {
                Material fender = s.hull == VKit.BoatWhite ? VKit.BoatBlue : VKit.BoatWhite;
                for (int side = 0; side < 2; side++)
                    for (int k = 0; k < 2; k++)
                    {
                        float fi = (k == 0 ? 0.3f : 0.58f) * (NS - 1) + (float)lr.NextDouble() * 0.4f;
                        int i0 = Mathf.Min((int)fi, NS - 2); float ft = fi - i0;
                        Vector3 g = Vector3.Lerp(P[side, i0, NP - 1], P[side, i0 + 1, NP - 1], ft);
                        Vector3 nrm = Vector3.Lerp(N[side, i0, 3], N[side, i0 + 1, 3], ft);
                        nrm -= u * Vector3.Dot(nrm, u); nrm.Normalize();
                        Vector3 top = g + nrm * 0.11f - u * 0.13f, bot = top - u * 0.32f;
                        mb.Cylinder(fender, top, bot, 0.075f, 0.075f, 8, true);
                        mb.Cylinder(VKit.Rope, g + u * 0.02f, top, 0.02f, 0.02f, 4, false);
                    }
            }
            // ---- fittings ----
            Vector3 perch = o + u * Sheer(s, 1f) + f * (L * 0.5f);
            float[] th = s.type == 2 ? new[] { 0.24f, 0.5f, 0.72f } : new[] { 0.5f, 0.74f };
            for (int i = 0; i < th.Length; i++)
            {
                float t = th[i];
                VKit.Box(mb, VKit.Wood, o + u * (Sheer(s, t) * 0.72f) + f * ((t - 0.5f) * L), r * (HalfBeam(s, t) * 0.95f), u * 0.02f, f * 0.11f);
            }
            if (s.type != 2)
            {
                // foredeck
                for (int i = NS - 3; i < NS - 1; i++)
                    VKit.Q(mb, VKit.Wood, P[0, i, NP - 1] - u * 0.05f, P[1, i, NP - 1] - u * 0.05f, P[1, i + 1, NP - 1] - u * 0.05f, P[0, i + 1, NP - 1] - u * 0.05f, u);
            }
            float floorY = KeelY(s, 0.4f) + (Sheer(s, 0.4f) - KeelY(s, 0.4f)) * yprof[1];
            if (s.type == 0)
            {
                float zm = 0.12f * L, mh = L * VKit.Rnd(rng, 0.42f, 0.5f);
                Vector3 foot = o + u * floorY + f * zm, top = o + u * mh + f * zm;
                mb.Cylinder(VKit.WoodDk, foot, top, 0.055f, 0.035f, 6, true);
                Vector3 b0 = o + u * (s.freeboard + 0.55f) + f * (zm - 0.12f), b1 = o + u * (s.freeboard + 0.95f) + f * (zm - L * 0.36f);
                mb.Cylinder(VKit.WoodDk, b0, b1 + (b1 - b0).normalized * 0.15f, 0.03f, 0.03f, 5, true);
                mb.Cylinder(VKit.Canvas, Vector3.Lerp(b0, b1, 0.08f) + u * 0.08f, b1 + u * 0.08f, 0.085f, 0.06f, 6, true);   // furled sail
                VKit.Box(mb, VKit.WoodDk, o + u * (floorY + 0.2f) + f * (-0.2f * L), r * 0.32f, u * 0.2f, f * 0.42f, 8);          // engine box
                if (s.length > 6f) MastLamp(mb, o + u * (mh * 0.78f) + f * zm, r, u);
                perch = top;
            }
            else if (s.type == 1)
            {
                float zc = -0.2f * L, hw = Mathf.Min(HalfBeam(s, 0.3f) * 0.58f, 0.66f), hl = 0.68f;
                float y0 = floorY, y1 = s.freeboard + 0.42f, y2 = y1 + 0.95f;
                Vector3 c = o + f * zc;
                VKit.Box(mb, VKit.BoatWhite, c + u * ((y0 + y1) * 0.5f), r * hw, u * ((y1 - y0) * 0.5f), f * hl, 4 | 8);
                for (int w = 0; w < 4; w++)
                {
                    Vector3 n = w == 0 ? f : w == 1 ? -f : w == 2 ? r : -r;
                    Vector3 uu = Vector3.Cross(u, n);
                    float halfN = w < 2 ? hl : hw, halfU = w < 2 ? hw : hl;
                    ops.Clear();
                    if (w != 1) ops.Add(new Opening { t0 = 0.14f, t1 = halfU * 2f - 0.14f, y0 = 0.2f, y1 = 0.72f, kind = 0, shutters = 0, glass = VKit.WinDark });
                    VWalls.Band(mb, VKit.BoatWhite, c + u * y1 + n * halfN - uu * halfU, uu, n, halfU * 2f, y2 - y1, ops, rng);
                }
                VKit.Box(mb, s.hull, c + u * (y2 + 0.025f), r * (hw + 0.09f), u * 0.025f, f * (hl + 0.12f));
                Vector3 a0 = c + u * (y2 + 0.05f) - f * 0.3f, a1 = a0 + u * 0.85f;
                mb.Cylinder(VKit.Iron, a0, a1, 0.03f, 0.025f, 5, true);
                perch = a1;
                // a light mast on the foredeck with a lamp for night fishing
                float zf = 0.26f * L, my = s.freeboard + 2.5f;
                Vector3 mf = o + u * floorY + f * zf, mt = o + u * my + f * zf;
                mb.Cylinder(VKit.WoodDk, mf, mt, 0.045f, 0.03f, 6, true);
                MastLamp(mb, o + u * (my - 0.35f) + f * zf, r, u);
            }
            else
            {
                for (int k = -1; k <= 1; k += 2)
                {
                    Vector3 a = o + r * (k * 0.24f) + u * (s.freeboard * 0.78f + 0.03f) - f * (L * 0.3f), b = a + f * (L * 0.58f) + u * 0.03f;
                    mb.Cylinder(VKit.Wood, a, b, 0.025f, 0.025f, 5, true);
                    VKit.Box(mb, VKit.Wood, b + f * 0.24f, r * 0.07f, u * 0.012f, f * 0.26f);
                }
            }
            return perch;
        }

        /// <summary>A crossbar on a mast at p (r = athwartships) with a lamp hanging under one end: lit glass under an iron hood.</summary>
        static void MastLamp(MeshBuilder mb, Vector3 p, Vector3 r, Vector3 u)
        {
            mb.Cylinder(VKit.Iron, p - r * 0.32f, p + r * 0.32f, 0.022f, 0.022f, 5, true);
            Vector3 hang = p + r * 0.28f;
            mb.Cylinder(VKit.Iron, hang, hang - u * 0.12f, 0.02f, 0.02f, 4, false);
            Vector3 lt = hang - u * 0.12f;
            mb.Cylinder(VKit.Iron, lt, lt - u * 0.07f, 0.03f, 0.13f, 7, true);                    // hood
            mb.Cylinder(VKit.WinA, lt - u * 0.07f, lt - u * 0.27f, 0.085f, 0.085f, 7, true);      // glass
            mb.Cylinder(VKit.Iron, lt - u * 0.27f, lt - u * 0.3f, 0.09f, 0.06f, 7, true);
        }

        /// <summary>A boat colour that is neither the hull's nor the stripe's (for the rubbing strake).</summary>
        static Material Third(BoatSpec s, int pick)
        {
            for (int k = 0; k < 4; k++)
            {
                int c = (pick + k) & 3;
                Material m = c == 0 ? VKit.BoatYellow : c == 1 ? VKit.BoatBlue : c == 2 ? VKit.BoatRed : VKit.BoatWhite;
                if (m != s.hull && m != s.stripe) return m;
            }
            return VKit.BoatWhite;
        }

        /// <summary>Rows j0..j1 of one side of the hull grid as a smooth surface; inside = the same surface seen from within.</summary>
        static void Skin(MeshBuilder mb, Material m, int side, int j0, int j1, bool inside)
        {
            lv.Clear(); ln.Clear(); lt.Clear();
            int rows = j1 - j0 + 1;
            for (int i = 0; i < NS; i++)
                for (int j = j0; j <= j1; j++) { lv.Add(P[side, i, j]); ln.Add(inside ? -N[side, i, j] : N[side, i, j]); }
            for (int i = 0; i < NS - 1; i++)
                for (int j = 0; j < rows - 1; j++)
                {
                    int a = i * rows + j, b = a + rows, c = b + 1, d = a + 1;
                    Vector3 fn = Vector3.Cross(lv[b] - lv[a], lv[d] - lv[a]);
                    if (fn.sqrMagnitude < 1e-10f) fn = Vector3.Cross(lv[c] - lv[a], lv[d] - lv[a]);
                    bool ok = Vector3.Dot(fn, ln[a] + ln[b] + ln[c] + ln[d]) >= 0f;
                    if (ok) { lt.Add(a); lt.Add(b); lt.Add(c); lt.Add(a); lt.Add(c); lt.Add(d); }
                    else { lt.Add(a); lt.Add(c); lt.Add(b); lt.Add(a); lt.Add(d); lt.Add(c); }
                }
            mb.Raw(m, lv, ln, null, lt);
        }

        static BoatSpec Spec(System.Random rng, int type, int colour)
        {
            var s = new BoatSpec { type = type };
            if (type == 2) { s.length = VKit.Rnd(rng, 3.4f, 4.2f); s.beam = VKit.Rnd(rng, 1.3f, 1.5f); s.draft = 0.4f; s.freeboard = VKit.Rnd(rng, 0.38f, 0.45f); }
            else { s.length = VKit.Rnd(rng, 5f, 8f); s.beam = s.length * VKit.Rnd(rng, 0.31f, 0.36f); s.draft = Mathf.Lerp(0.45f, 0.7f, (s.length - 5f) / 3f); s.freeboard = Mathf.Lerp(0.5f, 0.75f, (s.length - 5f) / 3f); }
            int c = colour & 3;
            s.hull = c == 0 ? VKit.BoatRed : c == 1 ? VKit.BoatWhite : c == 2 ? VKit.BoatBlue : VKit.BoatYellow;
            s.stripe = c == 1 ? (VKit.Chance(rng, 0.5f) ? VKit.BoatBlue : VKit.WoodDk) : (VKit.Chance(rng, 0.75f) ? VKit.BoatWhite : VKit.WoodDk);
            return s;
        }

        /// <summary>The 14 moored boats, each its own GameObject (origin = keel midpoint on the waterline, +z = bow).</summary>
        public static Village.Boat[] BuildFleet(Transform parent, System.Random rng, List<Vector3> perches)
        {
            // x, z, yaw, type : near row stern-to the quay, boats at anchor, two along the mole, four rowing boats
            float mz = Village.MoleZ + 6f * Village.MoleInnerSide;
            float[] d =
            {
                -30.5f, -252f, -90f, 0,   -29.5f, -224f, -84f, 1,   -31f, -193f, -96f, 0,   -29f, -158f, -88f, 1,   -30f, -124f, -93f, 0,
                -45f, -208f, -62f, 1,     -55f, -146f, -110f, 0,    -47f, -82f, -74f, 0,
                -37f, mz, 88f, 1,         -52f, mz - 0.6f * Village.MoleInnerSide, 96f, 0,
                -25.2f, -238f, 4f, 2,     -24.8f, -174f, 172f, 2,   -25.5f, -99f, -8f, 2,   -24.6f, -50f, 185f, 2
            };
            int n = d.Length / 4;
            var boats = new Village.Boat[n];
            var mb = new MeshBuilder();
            for (int i = 0; i < n; i++)
            {
                int type = (int)d[i * 4 + 3];
                BoatSpec s = Spec(rng, type, i + (i / 4));
                float yaw = d[i * 4 + 2] + VKit.Rnd(rng, -7f, 7f);
                Vector3 moor = new Vector3(d[i * 4] + VKit.Rnd(rng, -1.2f, 1.2f), 0f, d[i * 4 + 1] + VKit.Rnd(rng, -2f, 2f));
                Vector3 perch = Emit(mb, Vector3.zero, Vector3.right, Vector3.up, Vector3.forward, s, rng);
                GameObject go = mb.Build(parent, "Boat_" + i, true);
                go.transform.localPosition = moor;
                go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
                for (int k = 0; k < go.transform.childCount; k++)
                {
                    var mr = go.transform.GetChild(k).GetComponent<MeshRenderer>();
                    if (mr != null) mr.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
                }
                boats[i] = new Village.Boat { t = go.transform, mooring = moor, length = s.length, draft = s.draft, yaw = yaw };
                if (perches != null && type != 2)
                {
                    Vector3 fw = VKit.Dir(yaw), rt = Vector3.Cross(Vector3.up, fw);
                    perches.Add(moor + rt * perch.x + Vector3.up * perch.y + fw * perch.z);
                }
            }
            return boats;
        }

        /// <summary>A boat thrown onto land by the wave, lying on its side (goes into a ruined band).</summary>
        public static void Wreck(MeshBuilder mb, Vector3 pos, float yawDeg, System.Random rng, int colour)
        {
            BoatSpec s = Spec(rng, VKit.Chance(rng, 0.5f) ? 0 : 2, colour);
            if (s.length > 6.4f) s.length = 6.4f;
            Vector3 r, u, f;
            VKit.Axes(yawDeg + VKit.Rnd(rng, -12f, 12f), VKit.Rnd(rng, -9f, 9f), VKit.Rnd(rng, 38f, 62f) * VKit.Sgn(rng), out r, out u, out f);
            Emit(mb, pos + Vector3.up * (s.beam * 0.36f), r, u, f, s, rng);
        }
    }
}
