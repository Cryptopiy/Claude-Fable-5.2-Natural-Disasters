using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MESOCYCLONE
{
    /// <summary>Many small lights in ONE dynamic mesh and one emissive material. Every light is a tiny octahedron
    /// whose radius grows with its distance from the truck (radius = max(minRadius, distance * perMetre)), so a
    /// far light never becomes smaller than about 1.5 pixels and never shimmers. Lights can be switched off one by
    /// one (they collapse to a point). The object must sit at the local origin of the world root: the centres are
    /// given in the root's space and the root's position is the shift of the world under the truck.</summary>
    public sealed class LightDots : MonoBehaviour
    {
        public float perMetre = 0.0012f;
        Vector3[] centre;
        float[] minR;
        bool[] lit;
        Vector3[] verts;
        Mesh mesh;
        MeshRenderer mr;
        int count;

        static readonly Vector3[] Corner =
        {
            new Vector3(1f, 0f, 0f), new Vector3(-1f, 0f, 0f), new Vector3(0f, 1f, 0f),
            new Vector3(0f, -1f, 0f), new Vector3(0f, 0f, 1f), new Vector3(0f, 0f, -1f)
        };

        public int Count => count;
        public MeshRenderer Mr => mr;

        public static LightDots Create(string name, Transform root, Material mat, List<Vector3> centres, List<float> minRadius, float perMetre = 0.0012f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            var ld = go.AddComponent<LightDots>();
            ld.perMetre = perMetre;
            ld.Init(mat, centres, minRadius);
            return ld;
        }

        void Init(Material mat, List<Vector3> centres, List<float> minRadius)
        {
            count = centres.Count;
            centre = centres.ToArray();
            minR = minRadius.ToArray();
            lit = new bool[count];
            verts = new Vector3[count * 6];
            var normals = new Vector3[count * 6];
            var uv = new Vector2[count * 6];
            var tris = new int[count * 24];
            var bounds = new Bounds(count > 0 ? centre[0] : Vector3.zero, Vector3.zero);
            for (int i = 0; i < count; i++)
            {
                lit[i] = true;
                bounds.Encapsulate(centre[i]);
                for (int k = 0; k < 6; k++)
                {
                    verts[i * 6 + k] = centre[i] + Corner[k] * minR[i];
                    normals[i * 6 + k] = Corner[k];
                    uv[i * 6 + k] = new Vector2(0.5f, 0.5f);
                }
                int t = i * 24;
                for (int sx = 0; sx < 2; sx++)
                    for (int sy = 0; sy < 2; sy++)
                        for (int sz = 0; sz < 2; sz++)
                        {
                            int a = sx, b = 2 + sy, c = 4 + sz;
                            // clockwise seen from outside: the face normal must point away from the centre
                            Vector3 n = Vector3.Cross(Corner[b] - Corner[a], Corner[c] - Corner[a]);
                            if (Vector3.Dot(n, Corner[a] + Corner[b] + Corner[c]) < 0f) { int tmp = b; b = c; c = tmp; }
                            tris[t++] = i * 6 + a; tris[t++] = i * 6 + b; tris[t++] = i * 6 + c;
                        }
            }
            bounds.Expand(120f);
            mesh = new Mesh();
            mesh.name = gameObject.name;
            if (verts.Length > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.MarkDynamic();
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(tris, 0, false);
            mesh.bounds = bounds;
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            mr = gameObject.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        public void Set(int index, bool value) { if (index >= 0 && index < count) lit[index] = value; }

        public void SetRange(int start, int end, bool value)
        {
            if (start < 0) start = 0;
            if (end > count) end = count;
            for (int i = start; i < end; i++) lit[i] = value;
        }

        void LateUpdate()
        {
            if (!Director.Running || count == 0 || !mr.enabled) return;
            Vector3 shift = transform.position;                 // the world root's offset: the truck is at the origin
            float eyeY = Config.EyeY;
            for (int i = 0; i < count; i++)
            {
                Vector3 c = centre[i];
                float r = 0f;
                if (lit[i])
                {
                    float dx = c.x + shift.x, dy = c.y + shift.y - eyeY, dz = c.z + shift.z;
                    r = Mathf.Max(minR[i], Mathf.Sqrt(dx * dx + dy * dy + dz * dz) * perMetre);
                }
                int k = i * 6;
                verts[k] = new Vector3(c.x + r, c.y, c.z);
                verts[k + 1] = new Vector3(c.x - r, c.y, c.z);
                verts[k + 2] = new Vector3(c.x, c.y + r, c.z);
                verts[k + 3] = new Vector3(c.x, c.y - r, c.z);
                verts[k + 4] = new Vector3(c.x, c.y, c.z + r);
                verts[k + 5] = new Vector3(c.x, c.y, c.z - r);
            }
            mesh.SetVertices(verts, 0, verts.Length, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
        }
    }
}
