using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MESOCYCLONE
{
    /// <summary>The big stones that are seen one by one: each HailScore hit on the hood or the glass has a real, lit,
    /// lumpy ice stone (high-detail mesh, clear-coated ice) that falls in, strikes at the scored instant, throws a
    /// spray of chips (HailField) and glances away, off the windscreen if its bounce takes it there. Stateless in
    /// story time; drawn without GameObjects.</summary>
    [DefaultExecutionOrder(-80)]
    public sealed class HeroHail : MonoBehaviour
    {
        Mesh[] meshes;
        Material mat;
        RenderParams rp;
        bool ready;
        float shutter;

        public static void Materials(List<MatDef> l)
        {
            // fallback ice when the hail_ice registration (WeatherLibrary) is missing: pale, glossy, faintly glowing
            l.Add(new MatDef("hailstone") { color = new Color(0.86f, 0.9f, 0.95f), smoothness = 0.72f, emissive = new Color(0.8f, 0.9f, 1f), emissiveNits = 0.18f });
        }

        public static HeroHail Build(Transform truck)
        {
            var go = new GameObject("HeroHail");
            go.transform.SetParent(truck, false);
            var h = go.AddComponent<HeroHail>();
            h.meshes = new Mesh[IceMesh.ShapeCount];
            for (int i = 0; i < IceMesh.ShapeCount; i++) h.meshes[i] = IceMesh.Stone(i, 3, "HeroStone" + i);     // 1280 triangles: seen up close
            h.shutter = Tune.F("hail.shutter", 0.0015f);
            return h;
        }

        void LateUpdate()
        {
            if (!Director.Running) return;
            if (!ready)
            {
                // HailField builds the shared live materials (their glow follows the light). The hero stones strike the
                // hood and the glass, inside the near band, so they take its brighter beam-scatter glow (IceNear);
                // fall back to the dim ice, then to the asset
                mat = HailField.IceNear != null ? HailField.IceNear
                    : (HailField.IceMaterial != null ? HailField.IceMaterial : WeatherLibrary.Mat(WeatherLibrary.IceMat, "hailstone"));
                if (mat == null) return;
                rp = HailField.MakeParams(mat, true);
                ready = true;
            }
            float T = Director.T;
            if (!(Director.S.hail > 0.3f || (T > 286f && T < 346f))) return;
            Matrix4x4 l2w = transform.parent.localToWorldMatrix;
            Vector3 nOut = WeatherLibrary.GlassOut;
            int k0 = Mathf.FloorToInt((T - 0.9f) / HailScore.SlotLen), k1 = Mathf.FloorToInt((T + 0.45f) / HailScore.SlotLen);
            for (int k = k0; k <= k1; k++)
            {
                if (!HailScore.BigHit(k, out float time, out HailScore.Where where, out float size, out Vector2 pos)) continue;
                if (where != HailScore.Where.Hood && where != HailScore.Where.Glass) continue;
                float tau = T - time;                     // < 0 falling, > 0 bouncing
                if (tau < -0.4f || tau > 0.8f) continue;
                float r = size * 0.5f;
                Vector3 hit, bounce, normal;
                if (where == HailScore.Where.Hood)
                {
                    float x = Mathf.Lerp(-0.78f, 0.78f, pos.x), z = Mathf.Lerp(1.0f, 2.55f, pos.y);
                    hit = new Vector3(x, CabDims.HoodY(x, z) + r, z);
                    normal = Vector3.up;
                    bounce = new Vector3((pos.x - 0.5f) * 3f, 2.6f + 2.5f * pos.y, -3.5f - 4f * pos.x);
                }
                else
                {
                    // outward normal (CabDims.GlassNormalOut points into the cab): the stone sits on the outside of the glass
                    hit = CabDims.GlassPoint(Mathf.Lerp(-0.6f, 0.6f, pos.x), Mathf.Lerp(0.1f, 0.6f, pos.y)) + nOut * (r + 0.01f);
                    normal = nOut;
                    bounce = nOut * 3.5f + new Vector3((pos.x - 0.5f) * 2f, 2.5f, 1.5f);
                }
                Vector3 p, vel;
                if (tau < 0f)
                {
                    vel = new Vector3(0.6f * (pos.x - 0.5f), -31f, -Director.Speed);       // straight in: fast, relative to the truck
                    p = hit + vel * tau;
                }
                else
                {
                    HailField.QueueImpact(hit - normal * r, normal, tau, size, k);
                    if (!IceFlight.Fly(hit, bounce, tau, r, out p, out vel)) continue;
                    if (IceFlight.OverHood(p) && p.y < CabDims.HoodY(p.x, p.z) + r)
                    {   // back on the hood: skid along it until the cowl hides it
                        if (p.z < CabDims.HoodZ0 + 0.05f) continue;
                        p.y = CabDims.HoodY(p.x, p.z) + r; vel.y = 0f;
                    }
                    else if (p.y < 0.6f) continue;
                }
                float sp = vel.magnitude;
                float stretch = 1f + Mathf.Min(sp * shutter / size, 2.5f);
                uint h = WeatherLibrary.Hash((uint)k * 2246822519u + 77u);
                float ang = (tau < 0f ? 6f * tau : 22f * tau) + WeatherLibrary.U(h, 1) * 6.2832f;
                float life = WeatherLibrary.Smooth(-0.4f, -0.36f, tau) * WeatherLibrary.Smooth(0.8f, 0.7f, tau);     // no pop at the window ends
                Matrix4x4 m = l2w * HailField.Local(WeatherLibrary.Dir(h), ang, sp > 0.01f ? vel / sp : Vector3.down, stretch, size * life, p);
                // last frame's place (it moves tens of metres per second): TAA follows the stone instead of smearing it
                Graphics.RenderMesh(rp, meshes[((k % meshes.Length) + meshes.Length) % meshes.Length], 0, m, HailField.Prev(m, l2w, vel));
            }
        }
    }
}
