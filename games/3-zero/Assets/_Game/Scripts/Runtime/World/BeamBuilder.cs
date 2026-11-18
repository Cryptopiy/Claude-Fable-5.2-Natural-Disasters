using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZERO
{
    /// <summary>Shafts of light in the cabin air ("Zero/Beam"): for every window, one ribbon for the sun and one for the
    /// second light (the burning body, then the explosion). The shader casts each ribbon from its window along its
    /// light into the cabin, on the side the light comes from, so the shafts follow the lights by themselves.
    /// uv0 = (window centre z, light 0 = sun / 1 = second light, u along 0..1, side -1..1), uv1.x = seed.</summary>
    public static class BeamBuilder
    {
        const int Segs = 10;

        public static void Build(Transform plane, Material mat)
        {
            var V = new List<Vector3>(); var U0 = new List<Vector4>(); var U1 = new List<Vector4>(); var T = new List<int>();
            int k0 = Mathf.CeilToInt((PlaneDef.CabinZMin + 0.3f - PlaneDef.WindowZ0) / PlaneDef.WindowPitch);
            int k1 = Mathf.FloorToInt((PlaneDef.CabinZMax - 0.3f - PlaneDef.WindowZ0) / PlaneDef.WindowPitch);
            var rng = new Rng(8821u);
            for (int light = 0; light < 2; light++)
                for (int k = k0; k <= k1; k++)
                {
                    if (light == 1 && k == 0) continue;            // the player's own window: the camera sits in that shaft
                    float z = PlaneDef.WindowZ0 + k * PlaneDef.WindowPitch;
                    float seed = rng.F();
                    int b = V.Count;
                    for (int i = 0; i <= Segs; i++)
                    {
                        float u = i / (float)Segs;
                        for (int s = -1; s <= 1; s += 2)
                        {
                            V.Add(new Vector3(0f, PlaneDef.WindowY, z));
                            U0.Add(new Vector4(z, light, u, s)); U1.Add(new Vector4(seed, k, 0f, 0f));
                        }
                    }
                    for (int i = 0; i < Segs; i++)
                    {
                        int q = b + i * 2;
                        T.Add(q); T.Add(q + 2); T.Add(q + 1); T.Add(q + 1); T.Add(q + 2); T.Add(q + 3);
                    }
                }
            var mesh = new Mesh { name = "CabinShafts" };
            mesh.SetVertices(V); mesh.SetUVs(0, U0); mesh.SetUVs(1, U1); mesh.SetTriangles(T, 0, false);
            mesh.bounds = new Bounds(new Vector3(0f, 1.2f, 0.6f), new Vector3(8f, 4f, 16f));
            mesh.UploadMeshData(true);
            var go = Ctx.Child("CabinShafts", plane, Vector3.zero, Quaternion.identity);
            Ctx.Renderer(go, mesh, mat, Ctx.LayerWorld);
        }
    }
}
