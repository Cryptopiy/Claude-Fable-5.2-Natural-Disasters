using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZERO
{
    /// <summary>Ejecta in real time: every arc is a camera-facing ribbon whose vertices evaluate a ballistic trajectory in
    /// the vertex shader ("Zero/Ribbon"): the head flies from its launch point with its launch velocity, the trail is
    /// the path it has just flown. Arcs loop, so the fountain never stops. Vertex: position = launch point,
    /// normal = launch velocity (m/s), uv0 = (side, half width, u along the trail 0 = head, seed),
    /// uv1 = (first launch time, period, flight time, trail seconds).</summary>
    public class ArcBuilder
    {
        readonly List<Vector3> V = new List<Vector3>(8192);
        readonly List<Vector3> Vel = new List<Vector3>(8192);
        readonly List<Color32> C = new List<Color32>(8192);
        readonly List<Vector4> U0 = new List<Vector4>(8192);
        readonly List<Vector4> U1 = new List<Vector4>(8192);
        readonly List<int> T = new List<int>(16384);
        public int Count { get; private set; }

        public void Add(Vector3 launch, Vector3 v0, float t0, float period, float flight, float trailSeconds, float halfWidth, Color32 col, float seed, int points = 12)
        {
            int b = V.Count;
            for (int i = 0; i < points; i++)
            {
                float u = i / (float)(points - 1);
                for (int s = -1; s <= 1; s += 2)
                {
                    V.Add(launch); Vel.Add(v0); C.Add(col);
                    U0.Add(new Vector4(s, halfWidth, u, seed)); U1.Add(new Vector4(t0, period, flight, trailSeconds));
                }
            }
            for (int i = 0; i < points - 1; i++)
            {
                int k = b + i * 2;
                T.Add(k); T.Add(k + 2); T.Add(k + 1); T.Add(k + 1); T.Add(k + 2); T.Add(k + 3);
            }
            Count++;
        }

        public MeshRenderer Build(string name, Transform parent, Material mat)
        {
            if (V.Count == 0) return null;
            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(V); mesh.SetNormals(Vel); mesh.SetColors(C); mesh.SetUVs(0, U0); mesh.SetUVs(1, U1);
            mesh.SetTriangles(T, 0, false);
            mesh.bounds = new Bounds(Schedule.Site, Vector3.one * 4e6f);
            mesh.UploadMeshData(true);
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            V.Clear(); Vel.Clear(); C.Clear(); U0.Clear(); U1.Clear(); T.Clear();
            return r;
        }
    }
}
