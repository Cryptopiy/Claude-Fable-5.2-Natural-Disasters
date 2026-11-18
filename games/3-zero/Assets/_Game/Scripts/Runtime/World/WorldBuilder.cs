using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZERO
{
    /// <summary>Shared state while the world is being built.</summary>
    public static class Ctx
    {
        public const int LayerWorld = 0;
        public const int LayerSky = 8;
        public static Transform Root;       // the outside world (flat map coordinates)
        public static GameAssets A { get { return GameAssets.I; } }
        public static float Density { get { return QualityProfile.Density; } }
        public static int N(int finalCount) { return Mathf.Max(1, Mathf.RoundToInt(finalCount * QualityProfile.Density)); }

        /// <summary>A renderer for a generated mesh: no shadows, no probes.</summary>
        public static MeshRenderer Renderer(GameObject go, Mesh mesh, Material mat, int layer = LayerSky)
        {
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            go.layer = layer;
            return r;
        }

        public static GameObject Child(string name, Transform parent, Vector3 pos, Quaternion rot)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false); go.transform.localPosition = pos; go.transform.localRotation = rot;
            return go;
        }
    }

    /// <summary>Icosphere: a unit sphere of nearly equal triangles (level 0 = 12 vertices, each level quadruples the faces).</summary>
    public static class Ico
    {
        static readonly Dictionary<int, KeyValuePair<Vector3[], int[]>> cache = new Dictionary<int, KeyValuePair<Vector3[], int[]>>();

        public static void Get(int level, out Vector3[] verts, out int[] tris)
        {
            if (!cache.TryGetValue(level, out var kv))
            {
                const float A = 0.5257311f, B = 0.8506508f;
                var v = new List<Vector3>
                {
                    new Vector3(-A, B, 0), new Vector3(A, B, 0), new Vector3(-A, -B, 0), new Vector3(A, -B, 0),
                    new Vector3(0, -A, B), new Vector3(0, A, B), new Vector3(0, -A, -B), new Vector3(0, A, -B),
                    new Vector3(B, 0, -A), new Vector3(B, 0, A), new Vector3(-B, 0, -A), new Vector3(-B, 0, A),
                };
                var t = new List<int>
                {
                    0, 5, 11, 0, 1, 5, 0, 7, 1, 0, 10, 7, 0, 11, 10, 1, 9, 5, 5, 4, 11, 11, 2, 10, 10, 6, 7, 7, 8, 1,
                    3, 4, 9, 3, 2, 4, 3, 6, 2, 3, 8, 6, 3, 9, 8, 4, 5, 9, 2, 11, 4, 6, 10, 2, 8, 7, 6, 9, 1, 8,
                };
                for (int l = 0; l < level; l++)
                {
                    var mid = new Dictionary<long, int>(); var nt = new List<int>(t.Count * 4);
                    int Mid(int a, int b)
                    {
                        long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                        if (mid.TryGetValue(key, out int m)) return m;
                        v.Add(((v[a] + v[b]) * 0.5f).normalized); mid[key] = v.Count - 1; return v.Count - 1;
                    }
                    for (int i = 0; i < t.Count; i += 3)
                    {
                        int a = t[i], b = t[i + 1], c = t[i + 2];
                        int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                        nt.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                    }
                    t = nt;
                }
                // make every face look outward
                for (int i = 0; i < t.Count; i += 3)
                {
                    Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                    if (Vector3.Dot(Vector3.Cross(b - a, c - a), a + b + c) < 0f) { int s = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = s; }
                }
                kv = new KeyValuePair<Vector3[], int[]>(v.ToArray(), t.ToArray());
                cache[level] = kv;
            }
            verts = kv.Key; tris = kv.Value;
        }

        public static Mesh UnitSphere(string name, int level)
        {
            Get(level, out var v, out var t);
            var m = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            m.SetVertices(v); m.SetNormals(v); m.SetTriangles(t, 0, false);
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 3f);
            m.UploadMeshData(true);
            return m;
        }
    }

    /// <summary>The list of build steps (one per frame behind the black screen) and the per-frame upkeep of the world.</summary>
    public static class WorldBuilder
    {
        static Bootstrap boot;

        public static List<KeyValuePair<string, Action>> Steps(Bootstrap b)
        {
            boot = b;
            Ctx.Root = b.WorldRoot;
            var l = new List<KeyValuePair<string, Action>>();
            void Add(string k, Action a) { l.Add(new KeyValuePair<string, Action>(k, a)); }
            Add("plane", () =>
            {
                var hull = new Material(Ctx.A.Cabin) { name = "Hull" }; hull.SetFloat("_Interior", 0f);
                GlassBuilder.Build(b.PlaneRoot, Ctx.A.Glass);
                if (Ctx.A.Beam != null) BeamBuilder.Build(b.PlaneRoot, Ctx.A.Beam);
                var res = PlaneBuilder.Build(b.PlaneRoot, Ctx.A.Cabin, hull);
                SeatRig.I.SetWing(res.Wing);
            });
            Add("earth", () => EarthBuilder.Build(b.WorldRoot));
            Add("clouds", () => CloudBuilder.Build(b.WorldRoot));
            Add("asteroid", () => AsteroidBuilder.Build(b.WorldRoot));
            Add("sky", () => SkyObjects.Build(b.WorldRoot));
            Add("impact", () => ImpactFX.Build(b.WorldRoot));
            Add("audio", () => { new GameObject("Audio").AddComponent<AudioEngine>(); AudioDirector.Init(); });
            Add("cues", () => GazeCues.Build());
            return l;
        }

        public static void Finish(Bootstrap b)
        {
            b.ArcCount = ImpactFX.ArcCount; b.SpriteCount = ImpactFX.SpriteCount;
        }

        static readonly List<GameObject> warm = new List<GameObject>();

        /// <summary>One small copy of a real mesh per material, held in front of the lens during the loading stations:
        /// every shader is compiled before the flight begins.</summary>
        public static void BeginWarmup(Camera cam)
        {
            var seen = new HashSet<Material>();
            var all = new List<MeshRenderer>(boot.WorldRoot.GetComponentsInChildren<MeshRenderer>(true));
            all.AddRange(boot.PlaneRoot.GetComponentsInChildren<MeshRenderer>(true));
            foreach (var r in all)
            {
                var m = r.sharedMaterial; var mf = r.GetComponent<MeshFilter>();
                if (m == null || mf == null || mf.sharedMesh == null || !seen.Add(m)) continue;
                var b = mf.sharedMesh.bounds;
                float sc = 1.2f / Mathf.Clamp(Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)), 0.01f, 1e7f);
                var go = new GameObject("Warm");
                go.transform.SetParent(cam.transform, false);
                go.transform.localScale = Vector3.one * sc;
                go.transform.localPosition = new Vector3(0f, 0f, 5f) - b.center * sc;
                Ctx.Renderer(go, mf.sharedMesh, m, Ctx.LayerWorld);
                warm.Add(go);
            }
        }

        public static void EndWarmup() { foreach (var g in warm) UnityEngine.Object.Destroy(g); warm.Clear(); }

        /// <summary>Per-frame upkeep: the land and the clouds follow the aircraft, the body comes down, the explosion grows.</summary>
        public static void Tick(Bootstrap b, float T, Vector3 eyeAbs, float dt, bool snap)
        {
            EarthBuilder.Tick(eyeAbs);
            CloudBuilder.Tick(eyeAbs, snap);
            SkyObjects.Tick(T);
            ImpactFX.Tick(T);
            // the blast wave seen from inside it: the picture itself bends in the second it passes
            float d = Schedule.FrontDist(T);
            float k = T > Schedule.ContactTime ? Mathf.Exp(-(d * d) / (700f * 700f)) : 0f;
            ZeroPostFeature.Strength = k > 0.004f ? k : 0f;
            Shader.SetGlobalVector("_ZPostParams", new Vector4(k, Mathf.Clamp(-d / 1500f, -1f, 1f), (float)(WorldTime.Real % 1000.0), 0f));
        }
    }

    /// <summary>The land: one disc of triangles that follows the aircraft (the pattern is drawn from absolute coordinates
    /// by the shader "Zero/Earth", the curvature of the planet is applied there too). Dense under the aircraft, coarse
    /// toward the horizon 380 km away.</summary>
    public static class EarthBuilder
    {
        static Transform tf;
        public const float Radius = 450000f;

        public static void Build(Transform root)
        {
            const int rings = 150, sectors = 256;
            var v = new Vector3[(rings + 1) * sectors + 1]; var t = new List<int>(rings * sectors * 6);
            v[0] = Vector3.zero;
            for (int i = 0; i <= rings; i++)
            {
                float r = Radius * Mathf.Pow((i + 1) / (float)(rings + 1), 2.4f);
                for (int j = 0; j < sectors; j++)
                {
                    float a = j * Mathf.PI * 2f / sectors;
                    v[1 + i * sectors + j] = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                }
            }
            for (int j = 0; j < sectors; j++) { t.Add(0); t.Add(1 + (j + 1) % sectors); t.Add(1 + j); }
            for (int i = 0; i < rings; i++)
                for (int j = 0; j < sectors; j++)
                {
                    int a = 1 + i * sectors + j, b = 1 + i * sectors + (j + 1) % sectors, c = a + sectors, d = b + sectors;
                    t.Add(a); t.Add(b); t.Add(c); t.Add(b); t.Add(d); t.Add(c);
                }
            var mesh = new Mesh { name = "Earth", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(v); mesh.SetTriangles(t, 0, false);
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(Radius * 2.2f, 80000f, Radius * 2.2f));
            mesh.UploadMeshData(true);
            var go = new GameObject("Earth"); go.transform.SetParent(root, false);
            Ctx.Renderer(go, mesh, Ctx.A.Earth, Ctx.LayerWorld);
            tf = go.transform;
        }

        public static void Tick(Vector3 eyeAbs) { if (tf != null) tf.localPosition = new Vector3(eyeAbs.x, 0f, eyeAbs.z); }
    }
}
