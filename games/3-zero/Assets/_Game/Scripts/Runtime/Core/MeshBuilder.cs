using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZERO
{
    /// <summary>Builds vertex-coloured static meshes for the shader "Zero/Lit".
    /// Vertex format: position, normal, colour (rgb = albedo, a = emission strength 0..1),
    /// uv0 = (sway weight, phase seed, spare, spare). A matrix stack places primitives.</summary>
    public class MeshBuilder
    {
        public readonly List<Vector3> V = new List<Vector3>(4096);
        public readonly List<Vector3> Nrm = new List<Vector3>(4096);
        public readonly List<Color32> C = new List<Color32>(4096);
        public readonly List<Vector4> U = new List<Vector4>(4096);
        public readonly List<int> T = new List<int>(8192);
        public Matrix4x4 M = Matrix4x4.identity;
        public Vector4 Uv = Vector4.zero;          // uv0 written to every new vertex
        readonly Stack<Matrix4x4> stack = new Stack<Matrix4x4>();

        public int VertexCount { get { return V.Count; } }

        public void Push(Matrix4x4 local) { stack.Push(M); M = M * local; }
        public void Push(Vector3 pos, Quaternion rot, Vector3 scale) { Push(Matrix4x4.TRS(pos, rot, scale)); }
        public void Push(Vector3 pos, Quaternion rot) { Push(Matrix4x4.TRS(pos, rot, Vector3.one)); }
        public void Pop() { M = stack.Pop(); }

        public int Vert(Vector3 p, Vector3 n, Color32 c)
        {
            V.Add(M.MultiplyPoint3x4(p));
            Nrm.Add(M.MultiplyVector(n).normalized);
            C.Add(c); U.Add(Uv);
            return V.Count - 1;
        }

        public void Tri(int a, int b, int c) { T.Add(a); T.Add(b); T.Add(c); }

        /// <summary>Flat quad a-b-c-d; the front (normal) side is cross(b-a, d-a).</summary>
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color32 col, bool doubleSided = false)
        {
            Vector3 n = Vector3.Cross(b - a, d - a).normalized;
            int i = Vert(a, n, col); Vert(b, n, col); Vert(c, n, col); Vert(d, n, col);
            Tri(i, i + 1, i + 2); Tri(i, i + 2, i + 3);
            if (doubleSided)
            {
                int j = Vert(a, -n, col); Vert(b, -n, col); Vert(c, -n, col); Vert(d, -n, col);
                Tri(j, j + 2, j + 1); Tri(j, j + 3, j + 2);
            }
        }

        public void Triangle(Vector3 a, Vector3 b, Vector3 c, Color32 col, bool doubleSided = false)
        {
            Vector3 n = Vector3.Cross(b - a, c - a).normalized;
            int i = Vert(a, n, col); Vert(b, n, col); Vert(c, n, col);
            Tri(i, i + 1, i + 2);
            if (doubleSided) { int j = Vert(a, -n, col); Vert(b, -n, col); Vert(c, -n, col); Tri(j, j + 2, j + 1); }
        }

        /// <summary>Axis-aligned box in the current matrix (hard edges).</summary>
        public void Box(Vector3 center, Vector3 size, Color32 col)
        {
            Vector3 h = size * 0.5f;
            Vector3 p000 = center + new Vector3(-h.x, -h.y, -h.z), p100 = center + new Vector3(h.x, -h.y, -h.z);
            Vector3 p010 = center + new Vector3(-h.x, h.y, -h.z), p110 = center + new Vector3(h.x, h.y, -h.z);
            Vector3 p001 = center + new Vector3(-h.x, -h.y, h.z), p101 = center + new Vector3(h.x, -h.y, h.z);
            Vector3 p011 = center + new Vector3(-h.x, h.y, h.z), p111 = center + new Vector3(h.x, h.y, h.z);
            Quad(p000, p010, p110, p100, col);   // -z
            Quad(p101, p111, p011, p001, col);   // +z
            Quad(p001, p011, p010, p000, col);   // -x
            Quad(p100, p110, p111, p101, col);   // +x
            Quad(p010, p011, p111, p110, col);   // +y
            Quad(p001, p000, p100, p101, col);   // -y
        }

        public void Box(Vector3 center, Vector3 size, Quaternion rot, Color32 col)
        {
            Push(center, rot); Box(Vector3.zero, size, col); Pop();
        }

        /// <summary>Cylinder or cone between two points with smooth sides; r1 = 0 gives a cone.</summary>
        public void Cylinder(Vector3 p0, Vector3 p1, float r0, float r1, int sides, Color32 col, bool caps = true)
        {
            Vector3 axis = p1 - p0; float len = axis.magnitude; if (len < 1e-5f) return;
            Vector3 up = axis / len;
            Vector3 a = Mathf.Abs(up.y) < 0.99f ? Vector3.Cross(Vector3.up, up).normalized : Vector3.right;
            Vector3 b = Vector3.Cross(up, a);
            float slope = (r0 - r1) / len;
            int baseIdx = V.Count;
            for (int i = 0; i <= sides; i++)
            {
                float ang = i * Mathf.PI * 2f / sides;
                Vector3 d = a * Mathf.Cos(ang) + b * Mathf.Sin(ang);
                Vector3 n = (d + up * slope).normalized;
                Vert(p0 + d * r0, n, col); Vert(p1 + d * r1, n, col);
            }
            for (int i = 0; i < sides; i++)
            {
                int k = baseIdx + i * 2;
                Tri(k, k + 3, k + 1); Tri(k, k + 2, k + 3);
            }
            if (!caps) return;
            if (r0 > 1e-4f) Disc(p0, -up, a, b, r0, sides, col);
            if (r1 > 1e-4f) Disc(p1, up, a, b, r1, sides, col);
        }

        void Disc(Vector3 c, Vector3 n, Vector3 a, Vector3 b, float r, int sides, Color32 col)
        {
            int ci = Vert(c, n, col);
            for (int i = 0; i <= sides; i++)
            {
                float ang = i * Mathf.PI * 2f / sides;
                Vert(c + (a * Mathf.Cos(ang) + b * Mathf.Sin(ang)) * r, n, col);
            }
            bool flip = Vector3.Dot(Vector3.Cross(a, b), n) < 0f;
            for (int i = 0; i < sides; i++)
            {
                if (flip) Tri(ci, ci + 2 + i, ci + 1 + i); else Tri(ci, ci + 1 + i, ci + 2 + i);
            }
        }

        /// <summary>Smooth tube along a polyline (wires, cables, trunks). Radius per point optional.</summary>
        public void Tube(IList<Vector3> pts, float radius, int sides, Color32 col, IList<float> radii = null)
        {
            int n = pts.Count; if (n < 2) return;
            int baseIdx = V.Count;
            Vector3 prevA = Vector3.zero;
            for (int k = 0; k < n; k++)
            {
                Vector3 dir = (k == 0 ? pts[1] - pts[0] : k == n - 1 ? pts[n - 1] - pts[n - 2] : pts[k + 1] - pts[k - 1]).normalized;
                Vector3 a = k == 0 ? (Mathf.Abs(dir.y) < 0.99f ? Vector3.Cross(Vector3.up, dir).normalized : Vector3.right)
                                   : (prevA - dir * Vector3.Dot(prevA, dir)).normalized;
                prevA = a;
                Vector3 b = Vector3.Cross(dir, a);
                float r = radii != null ? radii[k] : radius;
                for (int i = 0; i <= sides; i++)
                {
                    float ang = i * Mathf.PI * 2f / sides;
                    Vector3 d = a * Mathf.Cos(ang) + b * Mathf.Sin(ang);
                    Vert(pts[k] + d * r, d, col);
                }
            }
            int ring = sides + 1;
            for (int k = 0; k < n - 1; k++)
                for (int i = 0; i < sides; i++)
                {
                    int p = baseIdx + k * ring + i;
                    Tri(p, p + ring + 1, p + ring); Tri(p, p + 1, p + ring + 1);
                }
        }

        /// <summary>Surface of revolution around +Y: profile points are (radius, height).</summary>
        public void Lathe(IList<Vector2> profile, int sides, Color32 col)
        {
            int n = profile.Count; int baseIdx = V.Count;
            for (int k = 0; k < n; k++)
            {
                Vector2 d2 = (k == 0 ? profile[1] - profile[0] : k == n - 1 ? profile[n - 1] - profile[n - 2] : profile[k + 1] - profile[k - 1]).normalized;
                Vector2 n2 = new Vector2(d2.y, -d2.x);
                for (int i = 0; i <= sides; i++)
                {
                    float ang = i * Mathf.PI * 2f / sides; float c = Mathf.Cos(ang), s = Mathf.Sin(ang);
                    Vert(new Vector3(profile[k].x * c, profile[k].y, profile[k].x * s), new Vector3(n2.x * c, n2.y, n2.x * s), col);
                }
            }
            int ring = sides + 1;
            for (int k = 0; k < n - 1; k++)
                for (int i = 0; i < sides; i++)
                {
                    int p = baseIdx + k * ring + i;
                    Tri(p, p + ring, p + ring + 1); Tri(p, p + ring + 1, p + 1);
                }
        }

        /// <summary>Low-poly sphere / ellipsoid (smooth normals).</summary>
        public void Sphere(Vector3 center, Vector3 radii, int segs, int rings, Color32 col)
        {
            int baseIdx = V.Count;
            for (int j = 0; j <= rings; j++)
            {
                float v = j / (float)rings, phi = v * Mathf.PI;
                for (int i = 0; i <= segs; i++)
                {
                    float th = i * Mathf.PI * 2f / segs;
                    Vector3 d = new Vector3(Mathf.Sin(phi) * Mathf.Cos(th), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(th));
                    Vector3 nn = new Vector3(d.x / radii.x, d.y / radii.y, d.z / radii.z).normalized;
                    Vert(center + Vector3.Scale(d, radii), nn, col);
                }
            }
            int ring = segs + 1;
            for (int j = 0; j < rings; j++)
                for (int i = 0; i < segs; i++)
                {
                    int p = baseIdx + j * ring + i;
                    Tri(p, p + 1, p + ring + 1); Tri(p, p + ring + 1, p + ring);
                }
        }

        public void Append(MeshBuilder other, Matrix4x4 m)
        {
            int b = V.Count;
            for (int i = 0; i < other.V.Count; i++)
            {
                V.Add(m.MultiplyPoint3x4(other.V[i])); Nrm.Add(m.MultiplyVector(other.Nrm[i]).normalized);
                C.Add(other.C[i]); U.Add(other.U[i]);
            }
            for (int i = 0; i < other.T.Count; i++) T.Add(other.T[i] + b);
        }

        public Mesh ToMesh(string name)
        {
            var m = new Mesh { name = name };
            if (V.Count > 65000) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(V); m.SetNormals(Nrm); m.SetColors(C); m.SetUVs(0, U);
            m.SetTriangles(T, 0, true);
            m.UploadMeshData(false);
            return m;
        }

        /// <summary>Creates a GameObject with this mesh under a parent (local space = builder space).</summary>
        public GameObject ToObject(string name, Transform parent, Material mat, bool castShadows = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = ToMesh(name);
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            return go;
        }

        public void Clear() { V.Clear(); Nrm.Clear(); C.Clear(); U.Clear(); T.Clear(); M = Matrix4x4.identity; stack.Clear(); Uv = Vector4.zero; }

        public static Color32 Shade(Color c, float mul, float emission = 0f)
        {
            return new Color(c.r * mul, c.g * mul, c.b * mul, emission);
        }
    }
}
