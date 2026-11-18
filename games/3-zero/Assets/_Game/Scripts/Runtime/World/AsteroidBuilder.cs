using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZERO
{
    /// <summary>The asteroid: a dense cube-sphere displaced into a fractured, cratered, ridged body, modelled at
    /// 2 km x 1.4 km x 1.2 km and scaled by Schedule.AstScale (26 km long in the world).
    /// Vertex colour: r = leading-face heat, g = cavity, b = tone.</summary>
    public static class AsteroidBuilder
    {
        public static Mesh Mesh;
        public static Mesh ShellMesh;            // the leading part only, for the plasma shells
        public static Transform Body, Rig;
        public static Vector3 TipLocal;          // the point that touches the ground first, in the rig's local space
        public static Quaternion Rot;            // local +Z = back up the trajectory
        public static readonly Vector3 Radii = new Vector3(700f, 600f, 1000f);
        const int N = 164;                        // grid per cube face (about 163k vertices)

        struct Crater { public Vector3 c; public float rad, depth; }
        static Crater[] craters; static Vector4[] planes; static Vector3 leadDir, downDir;

        static void InitShape()
        {
            var rng = new Rng(7741u);
            craters = new Crater[46];
            for (int i = 0; i < craters.Length; i++)
            {
                float big = rng.F();
                craters[i] = new Crater { c = rng.OnSphere(), rad = Mathf.Lerp(0.045f, 0.34f, big * big * big), depth = 0f };
                craters[i].depth = craters[i].rad * Mathf.Lerp(0.16f, 0.30f, rng.F());
            }
            planes = new Vector4[9];
            for (int i = 0; i < planes.Length; i++)
            {
                Vector3 n = rng.OnSphere();
                planes[i] = new Vector4(n.x, n.y, n.z, Mathf.Lerp(0.58f, 0.86f, rng.F()));
            }
            leadDir = Vector3.back;                                           // travel direction in object space (-Z)
            Rot = Quaternion.LookRotation(Schedule.AstAxis, Vector3.up);
            downDir = Quaternion.Inverse(Rot) * Vector3.down;                 // world down in object space
        }

        /// <summary>Radius multiplier of the unit sphere in direction d, plus cavity (0 deep .. 1 open).</summary>
        static float Shape(Vector3 d, out float cavity)
        {
            // domain warp, big lobes
            Vector3 w = d * 1.15f + new Vector3(Noise.Perlin(d.x * 1.3f + 4f, d.y * 1.3f, d.z * 1.3f), Noise.Perlin(d.x * 1.3f, d.y * 1.3f + 9f, d.z * 1.3f), Noise.Perlin(d.x * 1.3f, d.y * 1.3f, d.z * 1.3f + 17f)) * 0.55f;
            float r = 1.06f + 0.30f * Noise.Fbm(w.x, w.y, w.z, 3) + 0.09f * Noise.Fbm(w.x * 2.7f + 3f, w.y * 2.7f, w.z * 2.7f, 2);
            cavity = 1f;
            // fracture planes: flat broken faces
            for (int i = 0; i < planes.Length; i++)
            {
                float nd = d.x * planes[i].x + d.y * planes[i].y + d.z * planes[i].z;
                if (nd > 0.05f)
                {
                    float lim = planes[i].w / nd * (1f + 0.035f * Noise.Perlin(d.x * 9f + i, d.y * 9f, d.z * 9f));
                    if (lim < r) { r = Mathf.Lerp(lim, r, 0.12f); }
                }
            }
            // ridges
            float rid = Noise.Ridged(d.x * 3.1f + 2f, d.y * 3.1f, d.z * 3.1f + 5f, 4);
            r += 0.055f * (rid - 0.45f);
            // craters with raised rims
            for (int i = 0; i < craters.Length; i++)
            {
                float dot = d.x * craters[i].c.x + d.y * craters[i].c.y + d.z * craters[i].c.z;
                if (dot < 0.6f) continue;
                float ang = Mathf.Acos(Mathf.Clamp(dot, -1f, 1f)) / craters[i].rad;
                if (ang > 1.5f) continue;
                if (ang < 1f) { float bowl = 1f - ang * ang; r -= craters[i].depth * bowl; cavity = Mathf.Min(cavity, 1f - 0.75f * bowl); }
                float rim = (ang - 1f) / 0.22f;
                r += craters[i].depth * 0.28f * Mathf.Exp(-rim * rim);
            }
            // medium and fine relief
            r += 0.018f * Noise.Fbm(d.x * 11f, d.y * 11f, d.z * 11f, 3) + 0.006f * Noise.Fbm(d.x * 37f, d.y * 37f, d.z * 37f, 2);
            cavity *= Mathf.Clamp01(0.55f + 1.6f * rid);
            return r;
        }

        static Vector3 Point(Vector3 d, out float cavity)
        {
            float r = Shape(d, out cavity);
            Vector3 e = Radii;
            return new Vector3(d.x * e.x, d.y * e.y, d.z * e.z) * r;
        }

        static Vector3 CubeDir(int face, float u, float v)
        {
            // tangent warp for a more uniform spacing
            u = Mathf.Tan(u * Mathf.PI * 0.25f); v = Mathf.Tan(v * Mathf.PI * 0.25f);
            Vector3 p;
            switch (face)
            {
                case 0: p = new Vector3(1, v, -u); break;
                case 1: p = new Vector3(-1, v, u); break;
                case 2: p = new Vector3(u, 1, -v); break;
                case 3: p = new Vector3(u, -1, v); break;
                case 4: p = new Vector3(u, v, 1); break;
                default: p = new Vector3(-u, v, -1); break;
            }
            return p.normalized;
        }

        public static void Build(Transform parent)
        {
            InitShape();
            int side = N + 1, perFace = side * side;
            var verts = new Vector3[perFace * 6]; var norms = new Vector3[perFace * 6]; var cols = new Color32[perFace * 6];
            var tris = new int[N * N * 6 * 6];
            Parallel.For(0, 6 * side, new ParallelOptions { MaxDegreeOfParallelism = Mathf.Max(2, System.Environment.ProcessorCount / 2) }, job =>
            {
                int face = job / side, j = job % side;
                float v = j / (float)N * 2f - 1f, e = 2f / N * 0.5f;
                for (int i = 0; i < side; i++)
                {
                    float u = i / (float)N * 2f - 1f;
                    Vector3 d = CubeDir(face, u, v);
                    Vector3 p = Point(d, out float cav);
                    Vector3 pu = Point(CubeDir(face, u + e, v), out float _) - Point(CubeDir(face, u - e, v), out float _);
                    Vector3 pv = Point(CubeDir(face, u, v + e), out float _) - Point(CubeDir(face, u, v - e), out float _);
                    Vector3 n = Vector3.Cross(pv, pu).normalized;
                    if (Vector3.Dot(n, d) < 0f) n = -n;
                    int idx = face * perFace + j * side + i;
                    verts[idx] = p; norms[idx] = n;
                    // heat: the leading face (travel direction) and the underside near the tip
                    float lead = Mathf.Clamp01(1.25f * (Vector3.Dot(n, (leadDir * 0.65f + downDir * 0.55f).normalized) - 0.12f));
                    float axial = Mathf.Clamp01((-p.z / Radii.z + 0.55f) / 1.3f);
                    float heat = Mathf.Clamp01(lead * (0.35f + 0.9f * axial));
                    float tone = Mathf.Clamp01(0.5f + 0.9f * Noise.Fbm(d.x * 2.2f + 8f, d.y * 2.2f, d.z * 2.2f, 3));
                    cols[idx] = new Color(heat, cav, tone, 1f);
                }
            });
            int t = 0;
            for (int face = 0; face < 6; face++)
                for (int j = 0; j < N; j++)
                    for (int i = 0; i < N; i++)
                    {
                        int a = face * perFace + j * side + i, b = a + 1, c = a + side, d = c + 1;
                        // orient by the stored normals
                        Vector3 fn = Vector3.Cross(verts[c] - verts[a], verts[b] - verts[a]);
                        if (Vector3.Dot(fn, norms[a]) >= 0f) { tris[t++] = a; tris[t++] = c; tris[t++] = b; tris[t++] = b; tris[t++] = c; tris[t++] = d; }
                        else { tris[t++] = a; tris[t++] = b; tris[t++] = c; tris[t++] = b; tris[t++] = d; tris[t++] = c; }
                    }

            // the lowest vertex of the real mesh is the point that touches the ground first
            Quaternion rot = Rot;
            float lowY = float.MaxValue; Vector3 lowW = Vector3.zero;
            for (int i = 0; i < verts.Length; i++) { Vector3 w = rot * verts[i]; if (w.y < lowY) { lowY = w.y; lowW = w; } }
            TipLocal = Quaternion.Inverse(rot) * lowW;

            Mesh = new Mesh { name = "Asteroid", indexFormat = IndexFormat.UInt32 };
            Mesh.SetVertices(verts); Mesh.SetNormals(norms); Mesh.SetColors(cols); Mesh.SetTriangles(tris, 0, true);
            Mesh.UploadMeshData(false);
            // the rig moves down the trajectory; the rock, its plasma and its trail ride on it
            var rig = new GameObject("AsteroidRig");
            rig.transform.SetParent(parent, false);
            rig.transform.localRotation = rot; rig.transform.localScale = Vector3.one * Schedule.AstScale;
            Rig = rig.transform;
            var go = new GameObject("Asteroid");
            go.transform.SetParent(Rig, false); go.layer = Ctx.LayerSky;
            go.AddComponent<MeshFilter>().sharedMesh = Mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = Ctx.A.Asteroid; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            Body = go.transform;

            // the leading part as its own mesh for the plasma shells
            var keep = new System.Collections.Generic.List<int>(tris.Length / 2);
            for (int i = 0; i < tris.Length; i += 3)
                if (cols[tris[i]].r > 6 || cols[tris[i + 1]].r > 6 || cols[tris[i + 2]].r > 6) { keep.Add(tris[i]); keep.Add(tris[i + 1]); keep.Add(tris[i + 2]); }
            ShellMesh = new Mesh { name = "AsteroidShell", indexFormat = IndexFormat.UInt32 };
            ShellMesh.SetVertices(verts); ShellMesh.SetNormals(norms); ShellMesh.SetColors(cols); ShellMesh.SetTriangles(keep, 0, true);
            var bb = ShellMesh.bounds; bb.Expand(400f); ShellMesh.bounds = bb;
            ShellMesh.UploadMeshData(true);
        }
    }
}
