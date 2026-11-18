using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MESOCYCLONE
{
    /// <summary>Small procedural mesh helper. Every primitive is transformed by <see cref="xf"/> and tagged with the
    /// current <see cref="uvRect"/> (UVs are mapped into that rectangle; use Swatch() to paint flat colours from the
    /// swatch atlas). Flat-shaded primitives duplicate vertices; smooth ones share them.
    /// UV1 always holds metres in the primitive's own frame (box-projected per face for flat faces, unrolled for round
    /// primitives): the detail maps of the real materials tile on it at their physical size, whatever UV0 does.
    /// Each vertex also records its surface class (Surf), used by ToObject to give swatch geometry its material.</summary>
    public sealed class MeshBuilder
    {
        public readonly List<Vector3> verts = new List<Vector3>(4096);
        public readonly List<Vector3> norms = new List<Vector3>(4096);
        public readonly List<Vector2> uvs = new List<Vector2>(4096);
        public readonly List<Vector2> uv1 = new List<Vector2>(4096);
        readonly List<byte> surfs = new List<byte>(4096);
        readonly List<List<int>> subs = new List<List<int>> { new List<int>(8192) };
        public Matrix4x4 xf = Matrix4x4.identity;
        public Rect uvRect = new Rect(0, 0, 1, 1);
        public int sub;
        int surf;                                          // class of the geometry that follows (Surf.None = keep its material)

        public const int SwatchGrid = 8;                  // swatch atlas is 8 x 8 flat colours

        /// <summary>Set false (or Tune "world.surfaces" = 0) to keep every swatch triangle on the material it was built with.</summary>
        public static bool AutoSurfaces = true;

        /// <summary>All following geometry samples the centre of swatch i (0..63) of the swatch atlas, and takes the
        /// surface class of that colour.</summary>
        public void Swatch(int i)
        {
            float c = 1f / SwatchGrid;
            uvRect = new Rect((i % SwatchGrid + 0.5f) * c, (i / SwatchGrid + 0.5f) * c, 0f, 0f);
            surf = Surf.Of(i);
        }

        /// <summary>Overrides the surface class of the following geometry until the next Swatch() call (for colours
        /// that serve several surfaces: a dark green tractor is paint, not foliage).</summary>
        public void Surface(int s) { surf = s; }

        public void SetSub(int s) { while (subs.Count <= s) subs.Add(new List<int>(1024)); sub = s; }
        public void Push(Vector3 pos, Quaternion rot) { xf = Matrix4x4.TRS(pos, rot, Vector3.one); }
        public void Push(Vector3 pos, Quaternion rot, Vector3 scale) { xf = Matrix4x4.TRS(pos, rot, scale); }
        public void Reset() { xf = Matrix4x4.identity; }

        // ------------------------------------------------------------------ UV1 (metres)
        Vector3 Scale() => new Vector3(((Vector3)xf.GetColumn(0)).magnitude, ((Vector3)xf.GetColumn(1)).magnitude, ((Vector3)xf.GetColumn(2)).magnitude);

        /// <summary>Planar projection of a flat face: near-horizontal faces use (x, z); the others run u along the face
        /// horizontally and v up its slope (siding stays level, roof courses follow the ridge, corrugations run down).</summary>
        static Vector2 Planar(Vector3 p, Vector3 n)
        {
            if (n.y > 0.97f || n.y < -0.97f) return new Vector2(p.x, p.z);
            var t = new Vector3(n.z, 0f, -n.x);                     // up x n
            float tl = t.magnitude;
            if (tl < 1e-6f) return new Vector2(p.x, p.z);
            t /= tl;
            Vector3 b = Vector3.Cross(n, t);
            return new Vector2(Vector3.Dot(p, t), Vector3.Dot(p, b));
        }

        Vector2 FaceUV1(Vector3 p, Vector3 n)
        {
            Vector3 s = Scale();
            var ns = new Vector3(n.x / Mathf.Max(s.x, 1e-6f), n.y / Mathf.Max(s.y, 1e-6f), n.z / Mathf.Max(s.z, 1e-6f));
            return Planar(Vector3.Scale(p, s), ns.sqrMagnitude > 1e-12f ? ns.normalized : n);
        }

        int V(Vector3 p, Vector3 n, float u, float v, Vector2 w)
        {
            verts.Add(xf.MultiplyPoint3x4(p));
            norms.Add(xf.MultiplyVector(n).normalized);
            uvs.Add(new Vector2(uvRect.x + uvRect.width * u, uvRect.y + uvRect.height * v));
            uv1.Add(w);
            surfs.Add((byte)surf);
            return verts.Count - 1;
        }

        int V(Vector3 p, Vector3 faceN, float u, float v) => V(p, faceN, u, v, FaceUV1(p, faceN));

        public void Tri(Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 n = Vector3.Cross(b - a, c - a).normalized;
            int i = V(a, n, 0, 0); V(b, n, 1, 0); V(c, n, 0.5f, 1);
            subs[sub].Add(i); subs[sub].Add(i + 1); subs[sub].Add(i + 2);
        }

        /// <summary>Quad a-b-c-d (clockwise seen from the front), flat normal, UV (0,0)-(1,1).</summary>
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            Vector3 n = Vector3.Cross(b - a, c - a).normalized;
            if (n.sqrMagnitude < 0.5f) n = Vector3.Cross(c - a, d - a).normalized;
            int i = V(a, n, 0, 0); V(b, n, 0, 1); V(c, n, 1, 1); V(d, n, 1, 0);
            List<int> t = subs[sub];
            t.Add(i); t.Add(i + 1); t.Add(i + 2); t.Add(i); t.Add(i + 2); t.Add(i + 3);
        }

        public void QuadUV(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
        {
            Vector3 n = Vector3.Cross(b - a, c - a).normalized;
            int i = V(a, n, ua.x, ua.y); V(b, n, ub.x, ub.y); V(c, n, uc.x, uc.y); V(d, n, ud.x, ud.y);
            List<int> t = subs[sub];
            t.Add(i); t.Add(i + 1); t.Add(i + 2); t.Add(i); t.Add(i + 2); t.Add(i + 3);
        }

        /// <summary>Axis-aligned box (in the current transform). Faces can be skipped with a mask: 1 -X, 2 +X, 4 -Y, 8 +Y, 16 -Z, 32 +Z.</summary>
        public void Box(Vector3 c, Vector3 size, int skip = 0)
        {
            Vector3 h = size * 0.5f;
            Vector3 p000 = c + new Vector3(-h.x, -h.y, -h.z), p100 = c + new Vector3(h.x, -h.y, -h.z);
            Vector3 p010 = c + new Vector3(-h.x, h.y, -h.z), p110 = c + new Vector3(h.x, h.y, -h.z);
            Vector3 p001 = c + new Vector3(-h.x, -h.y, h.z), p101 = c + new Vector3(h.x, -h.y, h.z);
            Vector3 p011 = c + new Vector3(-h.x, h.y, h.z), p111 = c + new Vector3(h.x, h.y, h.z);
            if ((skip & 1) == 0) Quad(p001, p011, p010, p000);
            if ((skip & 2) == 0) Quad(p100, p110, p111, p101);
            if ((skip & 4) == 0) Quad(p001, p000, p100, p101);
            if ((skip & 8) == 0) Quad(p010, p011, p111, p110);
            if ((skip & 16) == 0) Quad(p000, p010, p110, p100);
            if ((skip & 32) == 0) Quad(p101, p111, p011, p001);
        }

        /// <summary>Cone frustum from p0 (radius r0) to p1 (radius r1), smooth sides, optional caps.
        /// UV1: u around the circumference, v along the axis (metres).</summary>
        public void Cylinder(Vector3 p0, Vector3 p1, float r0, float r1, int seg = 12, bool caps = true)
        {
            Vector3 axis = (p1 - p0).normalized;
            Vector3 t = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 b = Vector3.Cross(axis, t);
            float len = (p1 - p0).magnitude, slope = (r0 - r1) / Mathf.Max(len, 1e-5f);
            Vector3 s = Scale();
            float lenW = xf.MultiplyVector(p1 - p0).magnitude, rs = (s.x + s.y + s.z) / 3f, rMid = (r0 + r1) * 0.5f * rs;
            int i0 = verts.Count;
            for (int i = 0; i <= seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                Vector3 d = t * Mathf.Cos(a) + b * Mathf.Sin(a);
                Vector3 n = (d + axis * slope).normalized;
                V(p0 + d * r0, n, (float)i / seg, 0, new Vector2(a * rMid, 0f));
                V(p1 + d * r1, n, (float)i / seg, 1, new Vector2(a * rMid, lenW));
            }
            List<int> tr = subs[sub];
            for (int i = 0; i < seg; i++)
            {
                int k = i0 + i * 2;
                tr.Add(k); tr.Add(k + 3); tr.Add(k + 1); tr.Add(k); tr.Add(k + 2); tr.Add(k + 3);
            }
            if (!caps) return;
            for (int cap = 0; cap < 2; cap++)
            {
                float r = cap == 0 ? r0 : r1; if (r < 1e-4f) continue;
                Vector3 pc = cap == 0 ? p0 : p1, n = cap == 0 ? -axis : axis;
                int c0 = V(pc, n, 0.5f, 0.5f, Vector2.zero);
                for (int i = 0; i <= seg; i++)
                {
                    float a = i * Mathf.PI * 2f / seg;
                    V(pc + (t * Mathf.Cos(a) + b * Mathf.Sin(a)) * r, n, 0.5f + 0.5f * Mathf.Cos(a), 0.5f + 0.5f * Mathf.Sin(a),
                        new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (r * rs));
                }
                for (int i = 0; i < seg; i++)
                {
                    if (cap == 0) { tr.Add(c0); tr.Add(c0 + 2 + i); tr.Add(c0 + 1 + i); }
                    else { tr.Add(c0); tr.Add(c0 + 1 + i); tr.Add(c0 + 2 + i); }
                }
            }
        }

        public void Sphere(Vector3 c, float r, int seg = 12, int rings = 8, float yScale = 1f)
        {
            int i0 = verts.Count;
            Vector3 s = Scale();
            float rs = r * (s.x + s.y + s.z) / 3f;
            for (int y = 0; y <= rings; y++)
            {
                float phi = Mathf.PI * y / rings;
                for (int x = 0; x <= seg; x++)
                {
                    float th = Mathf.PI * 2f * x / seg;
                    Vector3 n = new Vector3(Mathf.Sin(phi) * Mathf.Cos(th), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(th));
                    V(c + new Vector3(n.x * r, n.y * r * yScale, n.z * r), n, (float)x / seg, 1f - (float)y / rings,
                        new Vector2(th * rs, -phi * rs * yScale));
                }
            }
            List<int> tr = subs[sub];
            for (int y = 0; y < rings; y++)
                for (int x = 0; x < seg; x++)
                {
                    int k = i0 + y * (seg + 1) + x;
                    tr.Add(k); tr.Add(k + 1); tr.Add(k + seg + 2); tr.Add(k); tr.Add(k + seg + 2); tr.Add(k + seg + 1);
                }
        }

        /// <summary>A lumpy ellipsoid (a mass of leaves): the radius is pushed out and in by a few random bumps, the
        /// underside is flattened, and the normals follow the lumps (so each lump has its lit and its shaded side).</summary>
        public void LumpySphere(Vector3 c, float r, int seg, int rings, float yScale, uint seed, float amp)
        {
            int i0 = verts.Count;
            Vector3 s = Scale();
            float rs = r * (s.x + s.y + s.z) / 3f;
            const int K = 8;
            var bd = new Vector3[K]; var bw = new float[K]; var ba = new float[K];
            uint h = seed * 2654435761u + 0x68e31da4u;
            for (int k = 0; k < K; k++)
            {
                h ^= h << 13; h ^= h >> 17; h ^= h << 5; float a = (h & 0xFFFF) / 65535f * Mathf.PI * 2f;
                h ^= h << 13; h ^= h >> 17; h ^= h << 5; float z = (h & 0xFFFF) / 65535f * 1.6f - 0.6f;
                h ^= h << 13; h ^= h >> 17; h ^= h << 5; bw[k] = 3f + 5f * (h & 0xFFFF) / 65535f;
                h ^= h << 13; h ^= h >> 17; h ^= h << 5; ba[k] = amp * ((h & 0xFFFF) / 65535f * 1.5f - 0.45f);
                float q = Mathf.Sqrt(Mathf.Max(0f, 1f - z * z));
                bd[k] = new Vector3(q * Mathf.Cos(a), z, q * Mathf.Sin(a));
            }
            int w = seg + 1;
            var pos = new Vector3[(rings + 1) * w];
            for (int y = 0; y <= rings; y++)
            {
                float phi = Mathf.PI * y / rings;
                for (int x = 0; x <= seg; x++)
                {
                    float th = Mathf.PI * 2f * (x % seg) / seg;
                    Vector3 n = new Vector3(Mathf.Sin(phi) * Mathf.Cos(th), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(th));
                    float f = 1f;
                    for (int k = 0; k < K; k++) f += ba[k] * Mathf.Exp(bw[k] * (Vector3.Dot(n, bd[k]) - 1f));
                    float ny = n.y < -0.25f ? Mathf.Lerp(1f, 0.55f, (-n.y - 0.25f) / 0.75f) : 1f;     // flat-ish underside
                    pos[y * w + x] = c + new Vector3(n.x * r * f, n.y * r * yScale * f * ny, n.z * r * f);
                }
            }
            for (int y = 0; y <= rings; y++)
            {
                float phi = Mathf.PI * y / rings;
                for (int x = 0; x <= seg; x++)
                {
                    Vector3 p = pos[y * w + x];
                    Vector3 nrm;
                    if (y == 0 || y == rings) nrm = (p - c).normalized;
                    else
                    {
                        Vector3 du = pos[y * w + (x + 1) % seg] - pos[y * w + (x + seg - 1) % seg];
                        Vector3 dv = pos[(y + 1) * w + x] - pos[(y - 1) * w + x];
                        nrm = Vector3.Cross(du, dv).normalized;
                        if (Vector3.Dot(nrm, p - c) < 0f) nrm = -nrm;
                    }
                    float th = Mathf.PI * 2f * x / seg;
                    V(p, nrm, (float)x / seg, 1f - (float)y / rings, new Vector2(th * rs, -phi * rs * yScale));
                }
            }
            List<int> tr = subs[sub];
            for (int y = 0; y < rings; y++)
                for (int x = 0; x < seg; x++)
                {
                    int k = i0 + y * w + x;
                    tr.Add(k); tr.Add(k + 1); tr.Add(k + seg + 2); tr.Add(k); tr.Add(k + seg + 2); tr.Add(k + seg + 1);
                }
        }

        /// <summary>Torus around the local Z axis at centre c (major radius R, tube radius r).</summary>
        public void Torus(Vector3 c, float R, float r, int segU = 32, int segV = 8)
        {
            int i0 = verts.Count;
            Vector3 s = Scale();
            float sc = (s.x + s.y + s.z) / 3f;
            for (int u = 0; u <= segU; u++)
            {
                float a = Mathf.PI * 2f * u / segU;
                Vector3 dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                for (int v = 0; v <= segV; v++)
                {
                    float b = Mathf.PI * 2f * v / segV;
                    Vector3 n = dir * Mathf.Cos(b) + Vector3.forward * Mathf.Sin(b);
                    V(c + dir * R + n * r, n, (float)u / segU, (float)v / segV, new Vector2(a * R * sc, b * r * sc));
                }
            }
            List<int> tr = subs[sub];
            for (int u = 0; u < segU; u++)
                for (int v = 0; v < segV; v++)
                {
                    int k = i0 + u * (segV + 1) + v;
                    tr.Add(k); tr.Add(k + segV + 1); tr.Add(k + segV + 2); tr.Add(k); tr.Add(k + segV + 2); tr.Add(k + 1);
                }
        }

        /// <summary>Sweeps a 2D profile (points in the local ZY plane: x = z, y = y) along local X from x0 to x1.
        /// Smooth normals along the profile when smooth is true; UV: u along X, v along the profile.
        /// The surface faces the RIGHT-hand side of the walking direction when the profile is drawn with +z to the
        /// right and +y up (a top surface must therefore run from front, high z, to back, low z). flip reverses it.</summary>
        public void ExtrudeX(Vector2[] profile, float x0, float x1, bool smooth = true, bool closed = false, bool flip = false)
        {
            int n = profile.Length;
            int segs = closed ? n : n - 1;
            float total = 0f;
            for (int i = 0; i < segs; i++) total += (profile[(i + 1) % n] - profile[i]).magnitude;
            Vector3 s = Scale();
            float run = 0f;
            for (int i = 0; i < segs; i++)
            {
                Vector2 a = profile[i], b = profile[(i + 1) % n];
                float l = (b - a).magnitude;
                Vector2 e = (b - a).normalized;
                Vector2 nSeg = new Vector2(e.y, -e.x);
                Vector2 na = nSeg, nb = nSeg;
                if (smooth)
                {
                    if (i > 0 || closed) { Vector2 p = (a - profile[(i - 1 + n) % n]).normalized; na = (nSeg + new Vector2(p.y, -p.x)).normalized; }
                    if (i < segs - 1 || closed) { Vector2 q = (profile[(i + 2) % n] - b).normalized; nb = (nSeg + new Vector2(q.y, -q.x)).normalized; }
                }
                if (flip) { na = -na; nb = -nb; }
                float v0 = run / total, v1 = (run + l) / total;
                float w0 = run * (s.y + s.z) * 0.5f, w1 = (run + l) * (s.y + s.z) * 0.5f;
                run += l;
                int k = verts.Count;
                V(new Vector3(x0, a.y, a.x), new Vector3(0, na.y, na.x), 0, v0, new Vector2(x0 * s.x, w0));
                V(new Vector3(x1, a.y, a.x), new Vector3(0, na.y, na.x), 1, v0, new Vector2(x1 * s.x, w0));
                V(new Vector3(x1, b.y, b.x), new Vector3(0, nb.y, nb.x), 1, v1, new Vector2(x1 * s.x, w1));
                V(new Vector3(x0, b.y, b.x), new Vector3(0, nb.y, nb.x), 0, v1, new Vector2(x0 * s.x, w1));
                List<int> tr = subs[sub];
                if (!flip) { tr.Add(k); tr.Add(k + 1); tr.Add(k + 2); tr.Add(k); tr.Add(k + 2); tr.Add(k + 3); }
                else { tr.Add(k); tr.Add(k + 2); tr.Add(k + 1); tr.Add(k); tr.Add(k + 3); tr.Add(k + 2); }
            }
        }

        /// <summary>Surface of revolution around local Y: profile points are (radius, y), bottom to top.
        /// UV1: u around (at each ring's own radius), v along the profile (metres).</summary>
        public void Lathe(Vector2[] profile, int seg = 16)
        {
            int n = profile.Length, i0 = verts.Count;
            Vector3 sc = Scale();
            float rs = (sc.x + sc.z) * 0.5f;
            float run = 0f;
            for (int i = 0; i < n; i++)
            {
                Vector2 p = profile[i];
                if (i > 0) run += new Vector2((p.x - profile[i - 1].x) * rs, (p.y - profile[i - 1].y) * sc.y).magnitude;
                Vector2 e = (profile[Mathf.Min(i + 1, n - 1)] - profile[Mathf.Max(i - 1, 0)]).normalized;
                Vector2 nn = new Vector2(e.y, -e.x);
                for (int s = 0; s <= seg; s++)
                {
                    float a = Mathf.PI * 2f * s / seg;
                    V(new Vector3(Mathf.Cos(a) * p.x, p.y, Mathf.Sin(a) * p.x), new Vector3(Mathf.Cos(a) * nn.x, nn.y, Mathf.Sin(a) * nn.x),
                        (float)s / seg, (float)i / (n - 1), new Vector2(a * Mathf.Max(p.x, 0.05f) * rs, run));
                }
            }
            List<int> tr = subs[sub];
            for (int i = 0; i < n - 1; i++)
                for (int s = 0; s < seg; s++)
                {
                    int k = i0 + i * (seg + 1) + s;
                    tr.Add(k); tr.Add(k + seg + 1); tr.Add(k + seg + 2); tr.Add(k); tr.Add(k + seg + 2); tr.Add(k + 1);
                }
        }

        public int VertexCount => verts.Count;

        public Mesh ToMesh(string name)
        {
            var m = new Mesh { name = name };
            if (verts.Count > 65000) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(verts); m.SetNormals(norms); m.SetUVs(0, uvs); m.SetUVs(1, uv1);
            m.subMeshCount = subs.Count;
            for (int i = 0; i < subs.Count; i++) m.SetTriangles(subs[i], i, false);
            m.RecalculateBounds();
            m.RecalculateTangents();
            return m;
        }

        /// <summary>Creates a child GameObject with a MeshFilter and MeshRenderer for this mesh. Triangles of a surface
        /// class on the "atlas" or "atlas_metal" submeshes move to extra submeshes appended after the given ones (so
        /// the caller's material slots keep their indices), each with its class material.</summary>
        public GameObject ToObject(string name, Transform parent, params Material[] mats)
        {
            Material[] final = Route(mats);
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = ToMesh(name);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = final;
            return go;
        }

        Material[] Route(Material[] mats)
        {
            GameAssets g = GameAssets.I;
            if (!AutoSurfaces || g == null || mats == null || Tune.F("world.surfaces", 1f) < 0.5f) return mats;
            Material atlas = g.Mat("atlas"), metal = g.Mat("atlas_metal");
            int original = subs.Count, baseCount = Mathf.Min(mats.Length, original);
            int[] slot = null;
            var extra = new List<Material>();
            for (int i = 0; i < baseCount; i++)
            {
                if (mats[i] == null || (mats[i] != atlas && mats[i] != metal)) continue;
                List<int> src = subs[i];
                var keep = new List<int>(src.Count);
                for (int k = 0; k + 2 < src.Count; k += 3)
                {
                    int s = surfs[src[k]];
                    if (s <= Surf.None || s >= Surf.Count) { keep.Add(src[k]); keep.Add(src[k + 1]); keep.Add(src[k + 2]); continue; }
                    if (slot == null) slot = new int[Surf.Count];
                    if (slot[s] == 0)
                    {
                        subs.Add(new List<int>(1024));
                        slot[s] = subs.Count - 1;
                        extra.Add(g.Mat(Surf.MatName[s]));
                    }
                    List<int> dst = subs[slot[s]];
                    dst.Add(src[k]); dst.Add(src[k + 1]); dst.Add(src[k + 2]);
                }
                subs[i] = keep;
            }
            if (extra.Count == 0) return mats;
            // submeshes the caller gave no material were never drawn: empty them and pad their slots so the appended
            // class slots line up with their submeshes
            var all = new Material[subs.Count];
            for (int i = 0; i < original; i++)
            {
                if (i < mats.Length) { all[i] = mats[i]; continue; }
                subs[i].Clear();
                all[i] = mats.Length > 0 ? mats[0] : null;
            }
            for (int i = 0; i < extra.Count; i++) all[original + i] = extra[i];
            return all;
        }
    }

    /// <summary>Deterministic random numbers (xorshift). The same seed gives the same world every run.</summary>
    public struct Rng
    {
        uint s;
        public Rng(uint seed) { s = seed == 0 ? 0x9E3779B9u : seed; Next(); Next(); }
        public uint Next() { s ^= s << 13; s ^= s >> 17; s ^= s << 5; return s; }
        public float Value => (Next() & 0xFFFFFF) / 16777216f;
        public float Range(float a, float b) => a + (b - a) * Value;
        public int Range(int a, int b) => a + (int)(Next() % (uint)Mathf.Max(1, b - a));
        public bool Chance(float p) => Value < p;
        public float Sign => Value < 0.5f ? -1f : 1f;
    }
}
