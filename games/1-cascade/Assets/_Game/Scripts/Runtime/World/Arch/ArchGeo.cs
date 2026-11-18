using System.Collections.Generic;
using UnityEngine;

namespace CASCADE
{
    /// <summary>Path frame at a chainage: position, flat heading, left normal and the interpolated walkable half width.</summary>
    internal struct ArchFrame
    {
        public Vector3 p, f, l;
        public float s, hw;

        /// <summary>Point at a lateral offset (+ = left of the walking direction) and a height above the path line.</summary>
        public Vector3 At(float lateral, float dy = 0f)
        {
            return new Vector3(p.x + l.x * lateral, p.y + dy, p.z + l.z * lateral);
        }
    }

    /// <summary>Rectangular recess in a wall face: u along the wall from its start, v = absolute height.</summary>
    internal struct ArchOpening
    {
        public float u0, u1, v0, v1, depth;
        public Material panel;
    }

    /// <summary>Cross-section for ArchGeo.Sweep. Points: x = offset to the left of the swept line, y = height above it
    /// (ArchGeo.Deep = the foundation depth of that station), listed counter-clockwise (x to the right, y up), so the
    /// outward normal of edge k is (dy, -dx). One material per edge (null = no face); caps are convex polygons (index
    /// lists, each with its material) closing the two ends.</summary>
    internal sealed class ArchProfile
    {
        public Vector2[] p;
        public Material[] edge;
        public bool closed;
        public int[][] caps;
        public Material[] capMat;
    }

    /// <summary>Geometry helpers of the path architecture. Every face is oriented by a hint vector, never by vertex order.</summary>
    internal static class ArchGeo
    {
        public static readonly Vector3 Up = new Vector3(0f, 1f, 0f);
        public const float Deep = -1000f;          // profile height marker: 'below' under the station

        /// <summary>Altitude band of the destruction swap (see the world facts): 0 below 12 m ... 7 above 150 m.</summary>
        public static int Band(float y)
        {
            return y < 12f ? 0 : y < 35f ? 1 : y < 60f ? 2 : y < 85f ? 3 : y < 110f ? 4 : y < 130f ? 5 : y < 150f ? 6 : 7;
        }

        /// <summary>Walkable half width, linearly interpolated (PathDef.HalfWidth snaps to samples).</summary>
        public static float Hw(float s)
        {
            float f = Mathf.Clamp(s, 0f, PathDef.Length) / PathDef.Step;
            int i = Mathf.Min((int)f, PathDef.Count - 2);
            return Mathf.Lerp(PathDef.HalfW[i], PathDef.HalfW[i + 1], f - i);
        }

        public static ArchFrame Frame(float s)
        {
            ArchFrame fr;
            fr.s = s;
            fr.p = PathDef.Pos(s);
            fr.f = PathDef.FlatDir(s);
            fr.l = new Vector3(-fr.f.z, 0f, fr.f.x);
            fr.hw = Hw(s);
            return fr;
        }

        public static Vector3 Left(Vector3 f) { return new Vector3(-f.z, 0f, f.x); }
        public static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
        public static Vector3 Y(Vector3 v, float y) { v.y = y; return v; }
        public static float Ground(Vector3 p) { return WorldGen.Height(p.x, p.z); }
        public static float FlatDist(Vector3 a, Vector3 b) { float dx = a.x - b.x, dz = a.z - b.z; return Mathf.Sqrt(dx * dx + dz * dz); }

        /// <summary>How far a wall standing at p (walking level) must reach down to stay in the ground: the ground is
        /// probed at p and up to 'reach' metres further out (the terrain mesh is coarser than the ground function).</summary>
        public static float Depth(Vector3 p, Vector3 outward, float reach, float min, float max)
        {
            float g = Mathf.Min(Ground(p), Mathf.Min(Ground(p + outward * (reach * 0.5f)), Ground(p + outward * reach)));
            return Mathf.Clamp(p.y - g + 1.2f, min, max);
        }

        /// <summary>Quad through a,b,c,d (loop order, either direction); it faces the hint.</summary>
        public static void Quad(MeshBuilder mb, Material m, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 hint)
        {
            Vector3 n = Vector3.Cross(b - a, d - a);
            if (n.sqrMagnitude < 1e-10f)
            {
                // degenerate first corner: start the loop at the opposite corner
                Vector3 t = a; a = c; c = t; t = b; b = d; d = t;
                n = Vector3.Cross(b - a, d - a);
                if (n.sqrMagnitude < 1e-10f) return;
            }
            if (Vector3.Dot(n, hint) < 0f) mb.Quad(m, a, d, c, b); else mb.Quad(m, a, b, c, d);
        }

        public static void Tri(MeshBuilder mb, Material m, Vector3 a, Vector3 b, Vector3 c, Vector3 hint)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (n.sqrMagnitude < 1e-10f) return;
            if (Vector3.Dot(n, hint) < 0f) mb.Tri(m, a, c, b); else mb.Tri(m, a, b, c);
        }

        /// <summary>Vertical quad above the ground-plan segment a-b between two heights.</summary>
        public static void VQuad(MeshBuilder mb, Material m, Vector3 a, Vector3 b, float y0, float y1, Vector3 hint)
        {
            Quad(mb, m, Y(a, y0), Y(b, y0), Y(b, y1), Y(a, y1), hint);
        }

        /// <summary>Horizontal rectangle (axis aligned) facing up.</summary>
        public static void TopQuad(MeshBuilder mb, Material m, float x0, float x1, float z0, float z1, float y)
        {
            Quad(mb, m, new Vector3(x0, y, z0), new Vector3(x1, y, z0), new Vector3(x1, y, z1), new Vector3(x0, y, z1), Up);
        }

        /// <summary>Skin between two cross-section rings with the same point count. Faces point away from the ring centre.
        /// mats (one per profile segment, null = skip) or a single material.</summary>
        public static void Loft(MeshBuilder mb, Vector3[] a, Vector3[] b, Material[] mats, Material one, bool closed)
        {
            int n = a.Length;
            Vector3 c = Vector3.zero;
            for (int i = 0; i < n; i++) c += a[i] + b[i];
            c /= 2f * n;
            int segs = closed ? n : n - 1;
            for (int k = 0; k < segs; k++)
            {
                int k1 = (k + 1) % n;
                Material m = mats != null ? mats[k] : one;
                if (m == null) continue;
                Vector3 mid = (a[k] + a[k1] + b[k] + b[k1]) * 0.25f;
                Quad(mb, m, a[k], a[k1], b[k1], b[k], mid - c);
            }
        }

        /// <summary>End cap of a convex ring (triangle fan).</summary>
        public static void Cap(MeshBuilder mb, Material m, Vector3[] ring, Vector3 facing)
        {
            for (int i = 1; i < ring.Length - 1; i++) Tri(mb, m, ring[0], ring[i], ring[i + 1], facing);
        }

        static readonly float[] RoundO = { -1f, -1f, -0.67f, 0f, 0.67f, 1f, 1f };
        static readonly float[] RoundH = { 0f, 0.14f, 0.04f, 0f, 0.04f, 0.14f, 0f };   // metres below the crown

        /// <summary>Wall along an open polyline (centreline points, y = walking level at each vertex): mitred corners,
        /// rounded or flat top, no bottom face. Each segment goes to the band of its own altitude. belowPer overrides the
        /// foundation depth per vertex.</summary>
        public static void PolyWall(ArchCtx c, bool fragile, Material m, IList<Vector3> pts, float height, float thick, float below, bool rounded, IList<float> belowPer = null)
        {
            int n = pts.Count;
            if (n < 2) return;
            float h = thick * 0.5f;
            Vector3[] prev = null;
            Vector3 lastDir = Vector3.forward;
            for (int i = 0; i < n; i++)
            {
                Vector3 dIn = i > 0 ? Flat(pts[i] - pts[i - 1]).normalized : Vector3.zero;
                Vector3 dOut = i < n - 1 ? Flat(pts[i + 1] - pts[i]).normalized : Vector3.zero;
                Vector3 refDir = i < n - 1 ? dOut : dIn;
                Vector3 t = dIn + dOut;
                if (t.sqrMagnitude < 1e-6f) t = refDir;
                t.Normalize();
                Vector3 nrm = Left(t);
                float k = 1f / Mathf.Max(0.5f, Vector3.Dot(nrm, Left(refDir)));
                Vector3 o = nrm * (h * k);
                Vector3 p = pts[i];
                if (belowPer != null) below = belowPer[i];
                Vector3[] ring;
                if (rounded)
                {
                    ring = new Vector3[7];
                    for (int j = 0; j < 7; j++)
                    {
                        float y = (j == 0 || j == 6) ? p.y - below : p.y + height - RoundH[j];
                        ring[j] = new Vector3(p.x + o.x * RoundO[j], y, p.z + o.z * RoundO[j]);
                    }
                }
                else
                {
                    ring = new[]
                    {
                        new Vector3(p.x - o.x, p.y - below, p.z - o.z), new Vector3(p.x - o.x, p.y + height, p.z - o.z),
                        new Vector3(p.x + o.x, p.y + height, p.z + o.z), new Vector3(p.x + o.x, p.y - below, p.z + o.z)
                    };
                }
                MeshBuilder mb = fragile ? c.F(p.y) : c.S(p.y);
                if (prev != null) Loft(mb, prev, ring, null, m, false);
                else Cap(mb, m, ring, -refDir);
                prev = ring;
                lastDir = refDir;
                if (i == n - 1) Cap(mb, m, ring, lastDir);
            }
        }

        /// <summary>Sweeps a cross-section along an open polyline (y = base level at each vertex) with mitred corners, like
        /// PolyWall. Each segment goes to the band of its own altitude. belowPer / below: foundation depth (ArchGeo.Deep points).</summary>
        public static void Sweep(ArchCtx c, bool fragile, IList<Vector3> pts, ArchProfile pr, IList<float> belowPer = null, float below = 0.5f)
        {
            int n = pts.Count, m = pr.p.Length;
            if (n < 2) return;
            int segs = pr.closed ? m : m - 1;
            Vector3[] prev = null;
            for (int i = 0; i < n; i++)
            {
                Vector3 dIn = i > 0 ? Flat(pts[i] - pts[i - 1]).normalized : Vector3.zero;
                Vector3 dOut = i < n - 1 ? Flat(pts[i + 1] - pts[i]).normalized : Vector3.zero;
                Vector3 refDir = i < n - 1 ? dOut : dIn;
                Vector3 t = dIn + dOut;
                if (t.sqrMagnitude < 1e-6f) t = refDir;
                t.Normalize();
                Vector3 nrm = Left(t);
                float k = 1f / Mathf.Max(0.5f, Vector3.Dot(nrm, Left(refDir)));
                Vector3 p = pts[i];
                float bl = belowPer != null ? belowPer[i] : below;
                var ring = new Vector3[m];
                for (int j = 0; j < m; j++)
                {
                    Vector2 q = pr.p[j];
                    float y = q.y <= Deep * 0.5f ? -bl : q.y;
                    ring[j] = new Vector3(p.x + nrm.x * (q.x * k), p.y + y, p.z + nrm.z * (q.x * k));
                }
                MeshBuilder mb = fragile ? c.F(p.y) : c.S(p.y);
                if (prev != null)
                {
                    Vector3 sl = Left(dIn);
                    for (int e = 0; e < segs; e++)
                    {
                        Material mat = pr.edge[e];
                        if (mat == null) continue;
                        int e1 = (e + 1) % m;
                        float dx = pr.p[e1].x - pr.p[e].x, dy = Mathf.Clamp(pr.p[e1].y - pr.p[e].y, -10f, 10f);
                        Quad(mb, mat, prev[e], prev[e1], ring[e1], ring[e], sl * dy - Up * dx);
                    }
                }
                if (pr.caps != null && (i == 0 || i == n - 1))
                {
                    Vector3 face = i == 0 ? -refDir : refDir;
                    for (int q = 0; q < pr.caps.Length; q++)
                    {
                        int[] poly = pr.caps[q];
                        for (int j = 1; j + 1 < poly.Length; j++) Tri(mb, pr.capMat[q], ring[poly[0]], ring[poly[j]], ring[poly[j + 1]], face);
                    }
                }
                prev = ring;
            }
        }

        /// <summary>Whitewashed wall with a rounded plaster coping (crown = height of the flat crown above the line, the
        /// coping overhangs both faces by ov and is ch deep). Foundation from 'below' (Sweep).</summary>
        public static ArchProfile Coped(Material body, Material trim, float thick, float crown, float ov = 0.03f, float ch = 0.08f)
        {
            float h = thick * 0.5f, w = h + ov, y0 = crown - ch;
            return new ArchProfile
            {
                p = new[]
                {
                    new Vector2(h, Deep), new Vector2(h, y0), new Vector2(w, y0), new Vector2(w, y0 + 0.035f),
                    new Vector2(w * 0.55f, crown), new Vector2(-w * 0.55f, crown), new Vector2(-w, y0 + 0.035f),
                    new Vector2(-w, y0), new Vector2(-h, y0), new Vector2(-h, Deep)
                },
                edge = new[] { body, trim, trim, trim, trim, trim, trim, trim, body },
                closed = false,
                caps = new[] { new[] { 0, 1, 8, 9 }, new[] { 2, 3, 4, 5, 6, 7 } },
                capMat = new[] { body, trim }
            };
        }

        /// <summary>Coped wall along pts (see Coped / Sweep): the standard whitewashed wall of the path.</summary>
        public static void CopedWall(ArchCtx c, IList<Vector3> pts, float crown, float thick, float below, IList<float> belowPer = null, Material body = null)
        {
            Sweep(c, false, pts, Coped(body ?? c.White, c.Trim, thick, crown), belowPer, below);
        }

        /// <summary>Vertical wall face from ground-plan point a to b between two heights, facing 'normal', with rectangular
        /// recesses (doors, windows). Openings must not overlap along u and are given in ascending u.</summary>
        public static void FaceWithOpenings(MeshBuilder mb, Material wall, Vector3 a, Vector3 b, float yLo, float yHi, Vector3 normal, IList<ArchOpening> ops)
        {
            Vector3 u = Flat(b - a);
            float w = u.magnitude;
            if (w < 1e-4f) return;
            u /= w;
            float uPrev = 0f;
            int count = ops != null ? ops.Count : 0;
            for (int i = 0; i < count; i++)
            {
                ArchOpening o = ops[i];
                if (o.u0 > uPrev) Quad(mb, wall, FP(a, u, uPrev, yLo), FP(a, u, o.u0, yLo), FP(a, u, o.u0, yHi), FP(a, u, uPrev, yHi), normal);
                Quad(mb, wall, FP(a, u, o.u0, yLo), FP(a, u, o.u1, yLo), FP(a, u, o.u1, o.v0), FP(a, u, o.u0, o.v0), normal);
                Quad(mb, wall, FP(a, u, o.u0, o.v1), FP(a, u, o.u1, o.v1), FP(a, u, o.u1, yHi), FP(a, u, o.u0, yHi), normal);
                Vector3 back = -normal * o.depth;
                Vector3 p00 = FP(a, u, o.u0, o.v0), p10 = FP(a, u, o.u1, o.v0), p11 = FP(a, u, o.u1, o.v1), p01 = FP(a, u, o.u0, o.v1);
                Quad(mb, wall, p00, p00 + back, p01 + back, p01, u);          // reveals
                Quad(mb, wall, p10, p10 + back, p11 + back, p11, -u);
                Quad(mb, wall, p01, p01 + back, p11 + back, p11, -Up);
                Quad(mb, wall, p00, p00 + back, p10 + back, p10, Up);
                Quad(mb, o.panel, p00 + back, p10 + back, p11 + back, p01 + back, normal);
                uPrev = o.u1;
            }
            if (uPrev < w) Quad(mb, wall, FP(a, u, uPrev, yLo), FP(a, u, w, yLo), FP(a, u, w, yHi), FP(a, u, uPrev, yHi), normal);
        }

        static Vector3 FP(Vector3 a, Vector3 u, float d, float y) { return new Vector3(a.x + u.x * d, y, a.z + u.z * d); }

        /// <summary>Closed ring of tube segments (mooring rings, gallery rails) in the plane spanned by u and v.</summary>
        public static void RingTube(MeshBuilder mb, Material m, Vector3 centre, Vector3 u, Vector3 v, float radius, float tube, int seg, int sides = 5)
        {
            Vector3 prev = centre + u * radius;
            for (int i = 1; i <= seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                Vector3 p = centre + (u * Mathf.Cos(a) + v * Mathf.Sin(a)) * radius;
                mb.Cylinder(m, prev, p, tube, tube, sides, false);
                prev = p;
            }
        }
    }
}
