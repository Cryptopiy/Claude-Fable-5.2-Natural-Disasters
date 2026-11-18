using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CASCADE
{
    /// <summary>
    /// Collects procedural geometry per material and bakes it into a few big static meshes (one MeshRenderer per
    /// material and builder). Positions are world space; the produced GameObjects sit at the origin.
    /// Usage: var mb = new MeshBuilder(); mb.Box(mat, centre, size, rot); ... mb.Build(parent, "Houses", true);
    /// </summary>
    public class MeshBuilder
    {
        class Bucket
        {
            public Material mat;
            public List<Vector3> v = new List<Vector3>(4096);
            public List<Vector3> n = new List<Vector3>(4096);
            public List<Vector2> uv = new List<Vector2>(4096);
            public List<int> t = new List<int>(8192);
        }

        readonly List<Bucket> buckets = new List<Bucket>();
        Bucket last;

        Bucket B(Material m)
        {
            if (last != null && last.mat == m) return last;
            for (int i = 0; i < buckets.Count; i++) if (buckets[i].mat == m) { last = buckets[i]; return last; }
            last = new Bucket { mat = m };
            buckets.Add(last);
            return last;
        }

        public int VertexCount { get { int c = 0; for (int i = 0; i < buckets.Count; i++) c += buckets[i].v.Count; return c; } }

        /// <summary>Quad a,b,c,d in CLOCKWISE order seen from the front (Unity front face). Normal = (b-a) x (d-a).</summary>
        public void Quad(Material m, Vector3 a, Vector3 b, Vector3 c, Vector3 d, float uvScale = 1f)
        {
            Bucket k = B(m);
            int i = k.v.Count;
            Vector3 nn = Vector3.Cross(b - a, d - a).normalized;
            k.v.Add(a); k.v.Add(b); k.v.Add(c); k.v.Add(d);
            k.n.Add(nn); k.n.Add(nn); k.n.Add(nn); k.n.Add(nn);
            float w = (b - a).magnitude * uvScale, h = (d - a).magnitude * uvScale;
            k.uv.Add(new Vector2(0, 0)); k.uv.Add(new Vector2(w, 0)); k.uv.Add(new Vector2(w, h)); k.uv.Add(new Vector2(0, h));
            k.t.Add(i); k.t.Add(i + 1); k.t.Add(i + 2); k.t.Add(i); k.t.Add(i + 2); k.t.Add(i + 3);
        }

        /// <summary>Triangle a,b,c clockwise seen from the front. Normal = (b-a) x (c-a).</summary>
        public void Tri(Material m, Vector3 a, Vector3 b, Vector3 c)
        {
            Bucket k = B(m);
            int i = k.v.Count;
            Vector3 nn = Vector3.Cross(b - a, c - a).normalized;
            k.v.Add(a); k.v.Add(b); k.v.Add(c);
            k.n.Add(nn); k.n.Add(nn); k.n.Add(nn);
            k.uv.Add(Vector2.zero); k.uv.Add(Vector2.right); k.uv.Add(Vector2.up);
            k.t.Add(i); k.t.Add(i + 1); k.t.Add(i + 2);
        }

        /// <summary>Rectangle centred at p spanned by half-vectors u and v; it faces u x v.</summary>
        public void Face(Material m, Vector3 p, Vector3 u, Vector3 v)
        {
            Quad(m, p - u - v, p + u - v, p + u + v, p - u + v);
        }

        /// <summary>Box with centre, full size and rotation. Faces can be skipped with a mask (bit 0 +x, 1 -x, 2 +y, 3 -y, 4 +z, 5 -z).</summary>
        public void Box(Material m, Vector3 centre, Vector3 size, Quaternion rot, int skipMask = 0)
        {
            Vector3 h = size * 0.5f;
            Vector3 x = rot * Vector3.right * h.x, y = rot * Vector3.up * h.y, z = rot * Vector3.forward * h.z;
            Vector3 c = centre;
            if ((skipMask & 1) == 0) Face(m, c + x, y, z);
            if ((skipMask & 2) == 0) Face(m, c - x, z, y);
            if ((skipMask & 4) == 0) Face(m, c + y, z, x);
            if ((skipMask & 8) == 0) Face(m, c - y, x, z);
            if ((skipMask & 16) == 0) Face(m, c + z, x, y);
            if ((skipMask & 32) == 0) Face(m, c - z, y, x);
        }

        public void Box(Material m, Vector3 centre, Vector3 size, float yawDeg = 0f, int skipMask = 0)
        {
            Box(m, centre, size, Quaternion.Euler(0f, yawDeg, 0f), skipMask);
        }

        /// <summary>Cylinder or cone frustum between two points with smooth normals. sides >= 3. Caps optional.</summary>
        public void Cylinder(Material m, Vector3 a, Vector3 b, float ra, float rb, int sides = 8, bool caps = false)
        {
            Bucket k = B(m);
            Vector3 axis = (b - a).normalized;
            Vector3 u = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.95f ? Vector3.up : Vector3.right).normalized;
            Vector3 w = Vector3.Cross(axis, u);
            int i0 = k.v.Count;
            float len = (b - a).magnitude;
            for (int i = 0; i <= sides; i++)
            {
                float ang = i * Mathf.PI * 2f / sides;
                Vector3 d = u * Mathf.Cos(ang) + w * Mathf.Sin(ang);
                Vector3 nn = (d + axis * ((ra - rb) / Mathf.Max(len, 1e-4f))).normalized;
                k.v.Add(a + d * ra); k.n.Add(nn); k.uv.Add(new Vector2(i / (float)sides, 0f));
                k.v.Add(b + d * rb); k.n.Add(nn); k.uv.Add(new Vector2(i / (float)sides, len));
            }
            for (int i = 0; i < sides; i++)
            {
                int p = i0 + i * 2;
                k.t.Add(p); k.t.Add(p + 3); k.t.Add(p + 1); k.t.Add(p); k.t.Add(p + 2); k.t.Add(p + 3);
            }
            if (caps)
            {
                for (int i = 0; i < sides; i++)
                {
                    float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
                    Vector3 d0 = u * Mathf.Cos(a0) + w * Mathf.Sin(a0), d1 = u * Mathf.Cos(a1) + w * Mathf.Sin(a1);
                    if (rb > 1e-4f) Tri(m, b, b + d0 * rb, b + d1 * rb);
                    if (ra > 1e-4f) Tri(m, a, a + d1 * ra, a + d0 * ra);
                }
            }
        }

        /// <summary>Upper hemisphere (dome) with smooth normals; squash scales the height.</summary>
        public void Dome(Material m, Vector3 centre, float radius, float squash = 1f, int seg = 16, int rings = 6)
        {
            Bucket k = B(m);
            int i0 = k.v.Count;
            for (int r = 0; r <= rings; r++)
            {
                float el = r * Mathf.PI * 0.5f / rings;
                float cy = Mathf.Sin(el), cr = Mathf.Cos(el);
                for (int s = 0; s <= seg; s++)
                {
                    float az = s * Mathf.PI * 2f / seg;
                    Vector3 d = new Vector3(Mathf.Cos(az) * cr, cy, Mathf.Sin(az) * cr);
                    k.v.Add(centre + new Vector3(d.x * radius, d.y * radius * squash, d.z * radius));
                    k.n.Add(new Vector3(d.x, d.y / Mathf.Max(squash, 0.01f), d.z).normalized);
                    k.uv.Add(new Vector2(s / (float)seg, r / (float)rings));
                }
            }
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < seg; s++)
                {
                    int p = i0 + r * (seg + 1) + s, q = p + seg + 1;
                    k.t.Add(p); k.t.Add(q); k.t.Add(q + 1); k.t.Add(p); k.t.Add(q + 1); k.t.Add(p + 1);
                }
        }

        /// <summary>Tube along a polyline (rails, ropes, wires). Radius at least 0.02 m (thin lines shimmer).</summary>
        public void Tube(Material m, IList<Vector3> pts, float radius, int sides = 5)
        {
            for (int i = 0; i < pts.Count - 1; i++) Cylinder(m, pts[i], pts[i + 1], radius, radius, sides, false);
        }

        /// <summary>Raw indexed geometry (for swept shapes). Normals must be supplied.</summary>
        public void Raw(Material m, IList<Vector3> verts, IList<Vector3> normals, IList<Vector2> uvs, IList<int> tris)
        {
            Bucket k = B(m);
            int i0 = k.v.Count;
            for (int i = 0; i < verts.Count; i++) { k.v.Add(verts[i]); k.n.Add(normals[i]); k.uv.Add(uvs != null ? uvs[i] : Vector2.zero); }
            for (int i = 0; i < tris.Count; i++) k.t.Add(i0 + tris[i]);
        }

        /// <summary>Bake everything into GameObjects under parent (one per material). Returns the group root.</summary>
        public GameObject Build(Transform parent, string name, bool castShadows = true, bool receiveLayerDefault = true)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            for (int i = 0; i < buckets.Count; i++)
            {
                Bucket k = buckets[i];
                if (k.v.Count == 0) continue;
                var mesh = new Mesh { name = name + "_" + (k.mat != null ? k.mat.name : "null") };
                mesh.indexFormat = k.v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                mesh.SetVertices(k.v); mesh.SetNormals(k.n); mesh.SetUVs(0, k.uv); mesh.SetTriangles(k.t, 0, true);
                mesh.UploadMeshData(true);
                var go = new GameObject(mesh.name);
                go.transform.SetParent(root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = k.mat;
                mr.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
                mr.lightProbeUsage = LightProbeUsage.Off;
                mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                mr.motionVectorGenerationMode = MotionVectorGenerationMode.Camera;
            }
            buckets.Clear(); last = null;
            return root;
        }
    }
}
