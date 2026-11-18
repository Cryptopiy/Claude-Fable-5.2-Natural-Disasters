using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZERO
{
    /// <summary>The window panes: one quad per window, set in the tunnel a few centimetres in from the outer skin
    /// (the shader "Zero/Glass" cuts the rounded opening and draws frost, the breather hole and the glare).</summary>
    public static class GlassBuilder
    {
        public static void Build(Transform plane, Material mat)
        {
            var V = new List<Vector3>(); var T = new List<int>();
            float x = PlaneDef.SkinX - 0.035f;
            float hw = PlaneDef.WindowW * 0.5f + 0.01f, hh = PlaneDef.WindowH * 0.5f + 0.01f;
            for (int side = -1; side <= 1; side += 2)
            {
                int k0 = Mathf.CeilToInt((PlaneDef.CabinZMin + 0.3f - PlaneDef.WindowZ0) / PlaneDef.WindowPitch);
                int k1 = Mathf.FloorToInt((PlaneDef.CabinZMax - 0.3f - PlaneDef.WindowZ0) / PlaneDef.WindowPitch);
                for (int k = k0; k <= k1; k++)
                {
                    float z = PlaneDef.WindowZ0 + k * PlaneDef.WindowPitch;
                    int b = V.Count;
                    V.Add(new Vector3(side * x, PlaneDef.WindowY - hh, z - hw)); V.Add(new Vector3(side * x, PlaneDef.WindowY - hh, z + hw));
                    V.Add(new Vector3(side * x, PlaneDef.WindowY + hh, z + hw)); V.Add(new Vector3(side * x, PlaneDef.WindowY + hh, z - hw));
                    T.Add(b); T.Add(b + 1); T.Add(b + 2); T.Add(b); T.Add(b + 2); T.Add(b + 3);
                }
            }
            var mesh = new Mesh { name = "WindowPanes" };
            mesh.SetVertices(V); mesh.SetTriangles(T, 0, true);
            mesh.UploadMeshData(true);
            var go = Ctx.Child("WindowPanes", plane, Vector3.zero, Quaternion.identity);
            Ctx.Renderer(go, mesh, mat, Ctx.LayerWorld);
        }
    }
}
