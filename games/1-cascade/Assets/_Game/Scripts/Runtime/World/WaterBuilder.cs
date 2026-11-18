using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CASCADE
{
    /// <summary>
    /// The sea: one tensor-product grid (fine around the bay, 10 m across the caldera, stretching to the horizon),
    /// cut into strips so that no draw call is huge. The shader displaces it with the shared sea height function.
    /// </summary>
    public static class WaterBuilder
    {
        static float[] Axis(float fineFrom, float fineTo, float fineStep, float midFrom, float midTo, float midStep, float far)
        {
            var l = new List<float>(900);
            // outward from the mid range on the low side
            var low = new List<float>();
            float x = midFrom, st = midStep;
            while (x > -far) { st *= 1.28f; x -= st; low.Add(x); }
            for (int i = low.Count - 1; i >= 0; i--) l.Add(low[i]);
            for (x = midFrom; x < fineFrom - 0.01f; x += midStep) l.Add(x);
            for (x = fineFrom; x < fineTo - 0.01f; x += fineStep) l.Add(x);
            for (x = fineTo; x < midTo - 0.01f; x += midStep) l.Add(x);
            l.Add(midTo);
            x = midTo; st = midStep;
            while (x < far) { st *= 1.28f; x += st; l.Add(x); }
            return l.ToArray();
        }

        public static GameObject Build(Transform parent)
        {
            // x: fine 5 m around the bay, 10 m across the caldera to beyond the cone's near shore
            float[] xs = Axis(-1150f, 250f, 5f, -3500f, 420f, 10f, 95000f);
            float[] zs = Axis(-520f, 1120f, 5f, -2700f, 2300f, 10f, 95000f);
            var root = new GameObject("Sea");
            root.transform.SetParent(parent, false);
            Material mat = GameAssets.I.Mat("M_Water");
            int nx = xs.Length, nz = zs.Length;
            const int strip = 96;                      // columns per strip
            for (int s0 = 0; s0 < nx - 1; s0 += strip)
            {
                int s1 = Mathf.Min(s0 + strip, nx - 1);
                int cols = s1 - s0 + 1;
                var v = new Vector3[cols * nz];
                for (int j = 0; j < nz; j++)
                    for (int i = 0; i < cols; i++) v[j * cols + i] = new Vector3(xs[s0 + i], 0f, zs[j]);
                var t = new int[(cols - 1) * (nz - 1) * 6];
                int k = 0;
                for (int j = 0; j < nz - 1; j++)
                    for (int i = 0; i < cols - 1; i++)
                    {
                        int a = j * cols + i, b = a + 1, c = a + cols, d = c + 1;
                        t[k++] = a; t[k++] = c; t[k++] = b; t[k++] = b; t[k++] = c; t[k++] = d;
                    }
                var mesh = new Mesh { name = "SeaStrip", indexFormat = IndexFormat.UInt32 };
                mesh.vertices = v;
                mesh.triangles = t;
                Bounds bd = new Bounds();
                bd.SetMinMax(new Vector3(xs[s0], -60f, zs[0]), new Vector3(xs[s1], 420f, zs[nz - 1]));
                mesh.bounds = bd;
                mesh.UploadMeshData(true);
                var go = new GameObject("SeaStrip");
                go.transform.SetParent(root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.lightProbeUsage = LightProbeUsage.Off;
                mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                mr.motionVectorGenerationMode = MotionVectorGenerationMode.Camera;
            }
            Debug.Log("CASCADE_INFO sea grid " + nx + " x " + nz + " = " + (nx * nz) + " vertices");
            return root;
        }
    }

    /// <summary>Feeds the sea height function from the drawdown and wave clocks.</summary>
    public class WaterDirector
    {
        static readonly int idSplash = Shader.PropertyToID("_SeaSplash"), idAim = Shader.PropertyToID("_SeaAim"),
            idBay = Shader.PropertyToID("_SeaBay"), idMisc = Shader.PropertyToID("_SeaMisc"), idDome = Shader.PropertyToID("_SeaDome"),
            idRing = Shader.PropertyToID("_SeaRing"), idFlow = Shader.PropertyToID("_SeaFlow"), idFlow2 = Shader.PropertyToID("_SeaFlow2");

        public static readonly Vector2 BayCentre = new Vector2(-260f, 160f);

        const float RingHeight = 18f;           // amplitude (m) of the rings the splash sends over the caldera
        // the bared sea floor while it is wet (M_SeaFloor, all four layers): smoothness floor and ceiling, linear albedo factor.
        // Not a black mirror: under the ash sky a 0.93 mirror reflects nothing, so the bed keeps a diffuse read and a broad sheen
        const float WetSmoothMin = 0.6f, WetSmoothMax = 0.82f, WetAlbedo = 0.8f;
        // share of the standing flood (8.5 m) that drains back out through the strait, 70-160 s after the strike: the bay
        // is calm again for the silence and the arrival (the churn, the brown water and the flood foam all follow the level)
        const float FloodSettle = 0.85f, SettleFrom = 70f, SettleTo = 160f;

        static readonly int[] idSmMin = { Shader.PropertyToID("_SmoothnessRemapMin0"), Shader.PropertyToID("_SmoothnessRemapMin1"), Shader.PropertyToID("_SmoothnessRemapMin2"), Shader.PropertyToID("_SmoothnessRemapMin3") };
        static readonly int[] idSmMax = { Shader.PropertyToID("_SmoothnessRemapMax0"), Shader.PropertyToID("_SmoothnessRemapMax1"), Shader.PropertyToID("_SmoothnessRemapMax2"), Shader.PropertyToID("_SmoothnessRemapMax3") };
        static readonly int[] idBase = { Shader.PropertyToID("_BaseColor0"), Shader.PropertyToID("_BaseColor1"), Shader.PropertyToID("_BaseColor2"), Shader.PropertyToID("_BaseColor3") };
        readonly Material floorMat;             // null when the material is missing: the wet look is then only the water film
        readonly float[] floorSmMin = new float[4], floorSmMax = new float[4];
        readonly Color[] floorCol = new Color[4];
        float floorWet = -1f;

        public WaterDirector()
        {
            var a = GameAssets.I;
            Material m = a != null ? a.Mat("M_SeaFloor") : null;
            if (m == null || m.name != "M_SeaFloor") return;          // Mat() falls back to another material when missing
            for (int k = 0; k < 4; k++)
                if (!m.HasProperty(idSmMin[k]) || !m.HasProperty(idSmMax[k]) || !m.HasProperty(idBase[k])) return;
            for (int k = 0; k < 4; k++)
            {
                floorSmMin[k] = m.GetFloat(idSmMin[k]);
                floorSmMax[k] = m.GetFloat(idSmMax[k]);
                floorCol[k] = m.GetColor(idBase[k]);
            }
            floorMat = m;
        }

        public void Tick(float dt)
        {
            float td = Story.T(Ev.Drawdown), tw = Story.T(Ev.Wave), tc = Story.T(Ev.Collapse);
            Vector2 aim = Sim.WaveAimDir, c0 = Sim.WaveCentre(tw);
            Shader.SetGlobalVector(idSplash, new Vector4(c0.x, c0.y, Sim.WaveFrontRadius(tw), Sim.WaveHeight(tw)));
            Shader.SetGlobalVector(idDome, new Vector4(Sim.SplashXZ.x, Sim.SplashXZ.y, 0f, 0f));
            Shader.SetGlobalVector(idAim, new Vector4(aim.x, aim.y, Sim.Drawdown(td, tw), FloodLevel(tw)));
            Shader.SetGlobalVector(idBay, new Vector4(BayCentre.x, BayCentre.y, 760f, 2700f));
            float second = tw > 0f ? 0.34f * MathX.Smooth(30f, 70f, tw) : 0f;
            float swell = Mathf.Lerp(0.55f, 1.6f, MathX.Smooth(0f, 30f, Story.T(Ev.Eruption) - Config.ShockDelay));
            swell *= 1f + 2.2f * Story.TremorLevel();                 // the water shivers with every tremor
            Shader.SetGlobalVector(idMisc, new Vector4(Sim.SplashHeight(tc), Config.MaxRunup, second, swell));

            // the struck sea: rings race out from the splash (326 s) and die out before the wave is far across
            float ts = tc - Config.SplashAt;
            float ring = ts > 0f ? RingHeight * MathX.Smooth(0f, 3f, ts) * (1f - MathX.Smooth(25f, 60f, ts)) : 0f;
            Shader.SetGlobalVector(idRing, new Vector4(Mathf.Max(ts, 0f), ring, 0f, 0f));

            // the flood's mean flow along the wave axis and its time integral (the foam rides it exactly)
            float ti = Mathf.Max(tw, 0f) - Config.WaveCross;
            float sp = FlowSpeed(ti), off = FlowOffset(ti);
            Shader.SetGlobalVector(idFlow, new Vector4(ti, aim.x * sp, aim.y * sp, tw > 0f ? 1f : 0f));
            Shader.SetGlobalVector(idFlow2, new Vector4(aim.x * off, aim.y * off, 0f, 0f));

            // the bared bed glistens from the first seconds of the drawdown until the wall covers it again
            SetFloorWet(MathX.Smooth(0f, 5f, td) * (1f - MathX.Smooth(Config.WaveCross - 9f, Config.WaveCross, tw)));
        }

        void SetFloorWet(float wet)
        {
            if (floorMat == null || Mathf.Abs(wet - floorWet) < 1e-4f) return;
            floorWet = wet;
            for (int k = 0; k < 4; k++)
            {
                floorMat.SetFloat(idSmMin[k], Mathf.Lerp(floorSmMin[k], Mathf.Max(floorSmMin[k], WetSmoothMin), wet));
                floorMat.SetFloat(idSmMax[k], Mathf.Lerp(floorSmMax[k], Mathf.Max(floorSmMax[k], WetSmoothMax), wet));
                Color c = floorCol[k];
                if (wet > 0f)
                {
                    c = (floorCol[k].linear * Mathf.Lerp(1f, WetAlbedo, wet)).gamma;     // the property holds sRGB; darken in linear
                    c.a = floorCol[k].a;
                }
                floorMat.SetColor(idBase[k], c);
            }
        }

        /// <summary>Mean speed (m/s) of the water along the wave axis, + inland, at ti seconds after the strike: the run-up,
        /// the backwash, the second wave (Sim.Flood) and its backwash.</summary>
        static float FlowSpeed(float ti)
        {
            return 9f * (1f - MathX.Smooth(5f, 13f, ti))
                 - 6f * MathX.Smooth(9f, 17f, ti) * (1f - MathX.Smooth(26f, 30f, ti))
                 + 7f * MathX.Smooth(27f, 31f, ti) * (1f - MathX.Smooth(34f, 40f, ti))
                 - 5f * MathX.Smooth(36f, 44f, ti) * (1f - MathX.Smooth(60f, 100f, ti));
        }

        /// <summary>FlowSpeed integrated from the birth of the wave (ti = -WaveCross) to ti, exactly: every ramp pair
        /// above rises before it falls, so each product integrates as the difference of two ramp integrals.</summary>
        static float FlowOffset(float ti)
        {
            float x = Mathf.Max(ti, -Config.WaveCross);
            return 9f * (x + Config.WaveCross) - 9f * RampInt(5f, 13f, x)
                 - 6f * (RampInt(9f, 17f, x) - RampInt(26f, 30f, x))
                 + 7f * (RampInt(27f, 31f, x) - RampInt(34f, 40f, x))
                 - 5f * (RampInt(36f, 44f, x) - RampInt(60f, 100f, x));
        }

        /// <summary>Integral of MathX.Smooth(a, b, t) dt from -infinity to x.</summary>
        static float RampInt(float a, float b, float x)
        {
            if (x <= a) return 0f;
            float w = b - a, t = (x - a) / w;
            return t < 1f ? w * (t * t * t - 0.5f * t * t * t * t) : 0.5f * w + (x - b);
        }

        /// <summary>CPU mirror of the calm part of the sea level at a point (boats, sound): drawdown and flood only.</summary>
        public static float LevelAt(float x, float z)
        {
            float td = Story.T(Ev.Drawdown), tw = Story.T(Ev.Wave);
            float d = Vector2.Distance(new Vector2(x, z), BayCentre);
            float wBay = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(760f, 2700f, d));
            return (FloodLevel(tw) - Sim.Drawdown(td, tw)) * wBay;
        }

        /// <summary>Flood level in the bay (m): Sim.Flood, draining out after the second wave. Sim.Flood's second-wave
        /// term is zero from 58 s after the strike, so this scales only the standing flood.</summary>
        public static float FloodLevel(float tw)
        {
            return Sim.Flood(tw) * (1f - FloodSettle * MathX.Smooth(SettleFrom, SettleTo, tw - Config.WaveCross));
        }
    }
}
