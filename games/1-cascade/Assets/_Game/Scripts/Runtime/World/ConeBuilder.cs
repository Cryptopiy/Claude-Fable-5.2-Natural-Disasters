using UnityEngine;
using UnityEngine.Rendering;

namespace CASCADE
{
    /// <summary>The volcano: a flat polar grid; the shader lifts it with the shared cone height function.</summary>
    public static class ConeBuilder
    {
        public static GameObject Build(Transform parent)
        {
            const int rings = 168, sectors = 640;
            const float maxR = 3000f;
            Vector3 c = Config.ConeCenter;
            var v = new Vector3[rings * sectors + 1];
            v[0] = new Vector3(c.x, 0f, c.z);
            for (int r = 0; r < rings; r++)
            {
                float rho = maxR * Mathf.Pow((r + 1f) / rings, 1.15f);
                for (int s = 0; s < sectors; s++)
                {
                    float a = s * Mathf.PI * 2f / sectors;
                    v[1 + r * sectors + s] = new Vector3(c.x + Mathf.Cos(a) * rho, 0f, c.z + Mathf.Sin(a) * rho);
                }
            }
            var t = new int[sectors * 3 + (rings - 1) * sectors * 6];
            int k = 0;
            for (int s = 0; s < sectors; s++)
            {
                int a = 1 + s, b = 1 + (s + 1) % sectors;
                t[k++] = 0; t[k++] = b; t[k++] = a;
            }
            for (int r = 0; r < rings - 1; r++)
                for (int s = 0; s < sectors; s++)
                {
                    int a = 1 + r * sectors + s, b = 1 + r * sectors + (s + 1) % sectors, d = a + sectors, e = b + sectors;
                    t[k++] = a; t[k++] = b; t[k++] = d; t[k++] = b; t[k++] = e; t[k++] = d;
                }
            var mesh = new Mesh { name = "Cone", indexFormat = IndexFormat.UInt32 };
            mesh.vertices = v;
            mesh.triangles = t;
            mesh.bounds = new Bounds(c + Vector3.up * 800f, new Vector3(maxR * 2.1f, 2600f, maxR * 2.1f));
            mesh.UploadMeshData(true);
            var go = new GameObject("Cone");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = GameAssets.I.Mat("M_Cone");
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return go;
        }
    }

    /// <summary>Feeds the cone height function and its lava from the collapse and eruption clocks.</summary>
    public class ConeDirector
    {
        static readonly int idSlide = Shader.PropertyToID("_ConeSlide"), idGlow = Shader.PropertyToID("_ConeGlow");
        public const float SlideRun = 2700f;

        // ---- the collapse amphitheatre: CPU mirror of SCAR_* in CascadeCone.hlsl (keep in sync) ----
        public const float ScarFloor = 1000f;                  // floor level at the axis, under the vent (m)
        public const float ScarUc = -60f, ScarR0 = 470f;       // the rounded head: centre on the slide axis, radius (m)
        public const float ScarSplay = 0.36f;                  // half-width growth of the walls per metre downhill
        public const float ScarRun = 2250f;                    // the floor's chute reaches sea level here (m down the slide)
        /// <summary>The vent ends this high above the bare amphitheatre floor: ~40 m above the rubble left on it (~15 m).</summary>
        public const float VentAboveFloor = 55f;

        // ---- the summit crater: CPU mirror of CRATER_* in CascadeCone.hlsl (keep in sync) ----
        public const float CraterRim = 1585f;                  // the cone is cut at this level (soft minimum)...
        public const float CraterK = 45f;                      // ...this soft (m)
        public const float CraterR = 150f;                     // bowl radius = the rim crest (m)
        public const float CraterDepth = 115f;                 // bowl depth below the cut: floor ~1470 m, ~1535 m with the shoulder's +65 m
        /// <summary>Depth of the vent below the old apex (Config.ConeHeight) before the collapse: 1540 m, on the crater floor (the
        /// profile's 1470 m plus the shoulder's 65 m), so from the village the near rim hides it by ~130 m and the jet, the
        /// fountain and the bombs rise out of the crater.</summary>
        public const float VentDepth = 260f;
        /// <summary>The pool under the vent overflows down the scar floor this long after the collapse (s). Shader: SCAR_FLOW_DELAY.</summary>
        public const float ScarFlowDelay = 30f;

        /// <summary>Lava flows: x azimuth from the cone axis (rad, 0 = +x, toward the village), y eruption-clock start (s), z front
        /// speed (m/s); every front starts 300 m from the axis. The cone shader holds the same table (kConeLavaFlows in
        /// ConeSurface.hlsl): keep them in sync. The flank flows sit where the village sees them, clear of the scar's sector
        /// (-0.95..+0.25 rad at mid-slope); the collapse cuts away their upper parts, and the shader crusts over those within
        /// ~0.9-1.4 rad of the slide azimuth (their source went with the summit; they still creep on to the sea, so the laze
        /// timing holds: ConeSurface.hlsl FLOW_STARVE_DAZ). The last one (ScarFlow) runs down the scar
        /// floor along the slide azimuth on the collapse clock (see FlowClock); its y is that start on the nominal eruption clock
        /// (collapse 116 s + ScarFlowDelay).</summary>
        public static readonly Vector3[] LavaFlows =
        {
            new Vector3(0.42f, 40f, 10f), new Vector3(0.85f, 58f, 8f), new Vector3(-1.25f, 82f, 10f),
            new Vector3(1.30f, 120f, 7f), new Vector3(0.62f, 150f, 9f),
            new Vector3(Sim.SlideAzimuth * Mathf.Deg2Rad, 146f, 14f),
        };
        public const int ScarFlow = 5;

        /// <summary>Seconds since lava flow i left its vent (negative before), from the eruption and collapse clocks.</summary>
        public static float FlowClock(int i, float te, float tc) { return i == ScarFlow ? tc - ScarFlowDelay : te - LavaFlows[i].y; }
        /// <summary>Distance of flow i's front from the cone axis (m); it reaches the sea at Config.ConeBaseRadius.</summary>
        public static float FlowFront(int i, float te, float tc) { return 300f + LavaFlows[i].z * Mathf.Max(FlowClock(i, te, tc), 0f); }

        /// <summary>How far the summit vent has dropped because the flank is gone (m): from the crater floor (VentDepth under the
        /// old apex) to VentAboveFloor over the amphitheatre floor (485 m in all).</summary>
        public static float VentDrop(float tc) { return (Config.ConeHeight - VentDepth - ScarFloor - VentAboveFloor) * Sim.Slide(tc); }

        /// <summary>The vent (world position) on the collapse clock: in the summit crater, then sinking with the slide.</summary>
        public static Vector3 Vent(float tc) { return Sim.ConeSummit + new Vector3(0f, -VentDepth - VentDrop(tc), 0f); }

        // ---- CPU mirror of the cone's smooth surface and of the scar (no noise), for placing effects ----
        public static float Profile(float rho)
        {
            float t = Mathf.Clamp01(1f - rho / Config.ConeBaseRadius);
            float h = Config.ConeHeight * Mathf.Pow(t, 1.85f);
            // the summit crater: the cone cut at the rim (soft minimum), a bowl sunk into the cut, a low lip round it
            float hh = Mathf.Max(CraterK - Mathf.Abs(h - CraterRim), 0f) / CraterK;
            h = Mathf.Min(h, CraterRim) - hh * hh * CraterK * 0.25f;
            h -= CraterDepth * Mathf.Clamp01(1f - rho * rho / (CraterR * CraterR));
            float rr = (rho - (CraterR + 15f)) / 30f;
            h += 16f * Mathf.Exp(-rr * rr);
            h -= Mathf.Max(rho - Config.ConeBaseRadius, 0f) * 0.45f;
            return h;
        }

        /// <summary>The smooth old surface at offset d (x, z) from the cone axis: profile, shoulder and parasitic cone.</summary>
        public static float SmoothHeight(Vector2 d)
        {
            float h = Profile(d.magnitude);
            Vector2 sh = d - new Vector2(-620f, 540f);
            h += 320f * Mathf.Exp(-sh.sqrMagnitude / (650f * 650f));
            Vector2 pc = d - new Vector2(950f, -820f);
            h += 140f * Mathf.Exp(-pc.sqrMagnitude / (260f * 260f));
            return h;
        }

        /// <summary>Amphitheatre floor along the slide axis, u metres down the slide from the cone axis.</summary>
        public static float ScarFloorAt(float u)
        {
            float ramp = 0.5f * (u + Mathf.Sqrt(u * u + 300f * 300f)) - 150f;
            float f = 1f - Mathf.Min(ramp, ScarRun) / ScarRun;
            return ScarFloor * f * f;
        }

        /// <summary>Ground height after the collapse at world (x, z): the smooth old surface minus the scar, i.e. the shader's
        /// ConeScar without its ragged walls and erosion relief (good to ~50 m). For placing effects in the amphitheatre.</summary>
        public static float GroundAfterCollapse(float x, float z)
        {
            Vector2 d = new Vector2(x - Config.ConeCenter.x, z - Config.ConeCenter.z);
            Vector2 sd = Sim.SlideDir;
            float u = Vector2.Dot(d, sd), v = d.x * sd.y - d.y * sd.x;       // the shader's slide frame (ConeSlideUV)
            float halfW = ScarR0 + ScarSplay * Mathf.Max(u - ScarUc, 0f);
            float e = new Vector2(Mathf.Min(u - ScarUc, 0f) / ScarR0, v / halfW).magnitude;
            float lat = 1f - MathX.Smooth(0.68f, 1.0f, e);
            float h = SmoothHeight(d);
            float above = Mathf.Max(h - ScarFloorAt(u), 0f);
            return h - above * lat * (1f - 0.4f * e * e);
        }

        public void Tick(float dt)
        {
            float tc = Story.T(Ev.Collapse), te = Story.T(Ev.Eruption);
            float k = Sim.Slide(tc);
            float thin = Mathf.Lerp(1f, 0.16f, Mathf.Pow(k, 0.8f));
            Shader.SetGlobalVector(idSlide, new Vector4(k * SlideRun, thin, Sim.Scar(tc), Sim.SlideAzimuth * Mathf.Deg2Rad));
            // the flows brighten as the daylight goes (Sim.Darkness 0.22 at te 70, 0.82 at 118): from then on they are the
            // hero light of the dark half and of the ash night
            float lava = MathX.Smooth(25f, 110f, te);
            float crater = MathX.Smooth(0f, 2.5f, te);
            Shader.SetGlobalVector(idGlow, new Vector4(lava, crater, Sim.Ashfall(te) * 0.85f, 0f));
        }
    }
}
