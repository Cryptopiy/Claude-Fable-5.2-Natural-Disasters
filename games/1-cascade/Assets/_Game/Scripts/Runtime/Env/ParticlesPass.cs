using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace CASCADE
{
    /// <summary>One stateless particle system: a draw of count*6 procedural vertices with its own property block.</summary>
    public class PSystem
    {
        public string name;
        public int kind, count;
        public bool on;
        public float clock;
        public Vector4 a, b, c, d, e, color, size;      // d.x is replaced by the pixel size (d.yzw reach the shader)
        public GraphicsBuffer bufA, bufB;
        public int emitters;
        public MaterialPropertyBlock mpb = new MaterialPropertyBlock();
    }

    /// <summary>HDRP custom pass that draws every particle system after the transparents, before post-processing.</summary>
    [System.Serializable]
    public class ParticlesPass : CustomPass
    {
        public Material mat;
        public List<PSystem> systems;
        static readonly int idKind = Shader.PropertyToID("_PKind"), idClock = Shader.PropertyToID("_PClock"), idA = Shader.PropertyToID("_PA"),
            idB = Shader.PropertyToID("_PB"), idC = Shader.PropertyToID("_PC"), idD = Shader.PropertyToID("_PD"), idE = Shader.PropertyToID("_PE"), idColor = Shader.PropertyToID("_PColor"),
            idSize = Shader.PropertyToID("_PSize"), idBufA = Shader.PropertyToID("_PBufA"), idBufB = Shader.PropertyToID("_PBufB"), idEm = Shader.PropertyToID("_PEmitters"),
            idLampA = Shader.PropertyToID("_PLampA"), idLampB = Shader.PropertyToID("_PLampB"), idLampCol = Shader.PropertyToID("_PLampCol");
        public static GraphicsBuffer Dummy;

        protected override void Execute(CustomPassContext ctx)
        {
            if (mat == null || systems == null) return;
            Camera cam = ctx.hdCamera.camera;
            float pix = 2f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(cam.pixelHeight, 1);
            // the pooled lamp lights are placed by VillageDirector, which ticks after the particles: read them at render time
            Vector4 lampA = Vector4.zero, lampB = Vector4.zero, lampCol = Vector4.zero;
            if (ParticleDirector.I != null) ParticleDirector.I.LampLights(out lampA, out lampB, out lampCol);
            CoreUtils.SetRenderTarget(ctx.cmd, ctx.cameraColorBuffer, ctx.cameraDepthBuffer, ClearFlag.None);
            for (int i = 0; i < systems.Count; i++)
            {
                PSystem s = systems[i];
                if (!s.on || s.count <= 0) continue;
                var m = s.mpb;
                m.SetFloat(idKind, s.kind); m.SetFloat(idClock, s.clock);
                m.SetVector(idA, s.a); m.SetVector(idB, s.b); m.SetVector(idC, s.c); m.SetVector(idD, new Vector4(pix, s.d.y, s.d.z, s.d.w)); m.SetVector(idE, s.e);
                m.SetVector(idColor, s.color); m.SetVector(idSize, s.size);
                m.SetBuffer(idBufA, s.bufA ?? Dummy); m.SetBuffer(idBufB, s.bufB ?? Dummy);
                m.SetFloat(idEm, Mathf.Max(s.emitters, 1));
                m.SetVector(idLampA, lampA); m.SetVector(idLampB, lampB); m.SetVector(idLampCol, lampCol);
                ctx.cmd.DrawProcedural(Matrix4x4.identity, mat, 0, MeshTopology.Triangles, s.count * 6, 1, m);
            }
        }
    }

    /// <summary>Owns the particle systems and feeds them from the event clocks.</summary>
    public class ParticleDirector
    {
        readonly List<PSystem> systems = new List<PSystem>(32);
        PSystem ashNear, ashFar, rain, embers, gulls, crest, impact, splash, bombs, fountain, fountainCore, halos, dust1, dust2, dust3, steam,
            laze, thread, bolt, ventBolt, beam, lens, slideDust, flotsam;
        Vector3 beaconPos; bool haveBeacon;
        float scale, lazeStart = 1e9f;
        Transform root;                     // the world root: VillageDirector parents its two pooled lamp lights to it
        Light lampLightA, lampLightB;
        readonly List<GraphicsBuffer> owned = new List<GraphicsBuffer>();
        public static ParticleDirector I;

        // ---- incandescent ejecta (kind 6): particles are trail segments ----
        const int BombSegs = 8, FountainSegs = 3;
        const int BombCount = 400, FountainCount = 800;     // the fountain: many short clots (dots, not rays) around a glowing core
        // white-hot nits of the ejecta (Particles.shader colours them with EjectaRamp and lights the head EJECTA_HEAD x).
        // Day: about the luminance of 1150-1200 C basalt (2-6k nits), so the sunlit ash wake shows; night: lifted to read
        // across the ash-dark exposure (the old 30000 on every trail segment rendered cream-white tracers).
        // By day the onset bombs and clots sit inside the opaque jet (no plume depth to hide them): 6000/3500 put an
        // 18000/10000-nit head on every one, gold sequins in a dark ball (196). Day heads now ~7500/6000 nits on the mean
        // rank: dark ash-wake streaks with dull red heads, only the few big blocks still glow. Night unchanged.
        const float BombNitsDay = 2500f, BombNitsNight = 15000f;
        const float FountainNitsDay = 2000f, FountainNitsNight = 7000f;
        const float BombTrailSeg = 0.08f, FountainTrailSeg = 0.05f;   // seconds per trail segment: comets about 100 m long
        const float BombWindLean = 0.10f, FountainWindLean = 0.06f;   // tilt of the jet downwind (tan), on top of its wander
        // ---- the fountain's glowing body: soft additive glows stacked over the vent (kind 7 in core mode) ----
        const int CoreGlows = 3;
        static readonly float[] CoreHeights = { 40f, 150f, 280f };    // m above the vent: the crater rim hides the lowest
        static readonly Vector4[] CoreShape = { new Vector4(1f, 1f, 0f, 0f), new Vector4(0.85f, 0.7f, 0f, 0f), new Vector4(0.7f, 0.45f, 0f, 0f) };   // size scale, intensity
        const float CoreRadius = 170f;                                // m
        const float CoreNitsDay = 2000f, CoreNitsNight = 4000f;       // peak, before the fog
        readonly Vector4[] coreA = new Vector4[CoreGlows];
        // ---- lightning ----
        const float MainBoltLight = 3.0e10f;        // PlumeDirector.Bolt0.w and _CBolt.w at pulse 1 (lumen-like, as Plume.shader expects)
        const float VentBoltLight = 0.3f * MainBoltLight;
        const float Leader = 0.03f;                 // the stepped leader draws the channel in 30 ms, then the return stroke
        const int MainSegs = 72, ForkSegs = 20, Forks = 3;
        const int VentLanes = 8, VentSegs = 12;     // vent sparks: up to 8 at once, 12 segments each (Particles.shader kind 8)
        readonly Vector4[] ventA = new Vector4[VentLanes], ventB = new Vector4[VentLanes];
        // ---- the lighthouse beam: the plume pass draws it from _CBeam/_CBeam2; the old billboards stay as a fallback ----
        const bool BillboardBeam = false;
        const float BeamHalfAngle = 4f;             // degrees
        const float BeamScale = 1500f;              // _CBeam.w at full strength (the old beam colour scale)
        const float LavaFlowStartRho = 300f;        // lava flows leave the summit about this far from the axis (ConeSurface)
        const float ImpactSpeed = 1.3f;             // strike droplets leave the coast faster than the old mist puffs
        const float ShutterSeconds = 1f / 48f;      // ash, rain and ember motion blur: a 180-degree shutter at 24 fps
        // spray rain after the strike: film-shutter streaks (1/48 s at ~10 m/s is 60-90 px within 3 m), capped on screen.
        // Fully energy-conserving like the ash, a drop at 3 m would be ~4% opaque and vanish under the ash; a 0.5 relief
        // keeps it ~14% (the old 0.05 s uncapped streaks were 50% opaque 150-300 px bars)
        const float RainStreakPx = 44f;
        const float RainRelief = 0.5f;
        const int RainCount = 32000;                // denser than the old 24000: each quad is now ~5x shorter (less fill)
        // the pooled lamp lights on the flakes (Particles.shader, physical Lambert plus a forward lobe): gain per system
        const float AshLampGlint = 1f, RainLampGlint = 0.6f;
        // ...but only a fifth before the storm: walking beside a lamp at 395/425, flakes 2-16 m away glinted over the
        // whole lower frame (warm specks on the wave face and the rock). The 548-600 snow-globe keeps the full gain.
        const float AshGlintPreStorm = 0.2f;
        // laze: flank flows within this of the slide azimuth (rad) are starved (ConeSurface.hlsl FLOW_STARVE_DAZ)
        static readonly Vector2 LazeStarveDAz = new Vector2(0.9f, 1.4f);
        const int LazePerVent = 25;                 // puffs per laze vent (the old 450 over 18 vents)
        const float CrestStretch = 0.8f;            // s: torn crest filaments (was 1.2; the 28 px cap trims the near ones)
        const float ImpactStretch = 0.1f;           // s: strike droplets (was 0.35: 12 m sprites smeared over the lens)
        // the vent below Sim.ConeSummit, as PlumeDirector and ConeDirector.VentDrop (one place to change if the crater deepens)
        const float VentBelowSummit = ConeDirector.VentDepth;
        static readonly int idCBolt = Shader.PropertyToID("_CBolt"), idCBeam = Shader.PropertyToID("_CBeam"), idCBeam2 = Shader.PropertyToID("_CBeam2");

        public static ParticleDirector Create(Transform root, bool final)
        {
            var d = new ParticleDirector { scale = final ? 1f : 0.5f, root = root };
            I = d;
            if (ParticlesPass.Dummy == null)
            {
                ParticlesPass.Dummy = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, 16);
                ParticlesPass.Dummy.SetData(new[] { Vector4.zero });
            }
            var go = new GameObject("ParticlesPass");
            go.transform.SetParent(root, false);
            var v = go.AddComponent<CustomPassVolume>();
            v.isGlobal = true;
            v.injectionPoint = CustomPassInjectionPoint.BeforePostProcess;
            var p = (ParticlesPass)v.AddPassOfType<ParticlesPass>();
            p.mat = GameAssets.I.Mat("M_Particles");
            p.systems = d.systems;
            p.name = "Particles";
            p.clearFlags = ClearFlag.None;
            d.Init();
            return d;
        }

        PSystem Add(string name, int kind, int count)
        {
            var s = new PSystem { name = name, kind = kind, count = Mathf.Max(1, Mathf.RoundToInt(count * (kind == 2 || kind == 7 ? 1f : scale))) };
            systems.Add(s);
            return s;
        }

        GraphicsBuffer Buf(Vector4[] data)
        {
            var b = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Mathf.Max(data.Length, 1), 16);
            if (data.Length > 0) b.SetData(data);
            owned.Add(b);
            return b;
        }

        void Init()
        {
            // far, soft things first; near flakes last
            thread = Add("thread", 5, 120);
            laze = Add("laze", 5, 450);
            slideDust = Add("slideDust", 5, 1500);
            flotsam = Add("flotsam", 11, 3000);
            splash = Add("splash", 4, 1400);
            impact = Add("impact", 4, 5200);
            crest = Add("crest", 3, 5000);
            steam = Add("steam", 4, 1200);
            dust1 = Add("dust1", 5, 160); dust2 = Add("dust2", 5, 420); dust3 = Add("dust3", 5, 420);
            fountainCore = Add("fountainCore", 7, 1); fountainCore.count = CoreGlows;                       // under the clots
            fountainCore.bufA = Buf(coreA); fountainCore.bufB = Buf(CoreShape); fountainCore.emitters = CoreGlows;
            bombs = Add("bombs", 6, BombCount * BombSegs); bombs.count -= bombs.count % BombSegs;            // whole trails only
            fountain = Add("fountain", 6, FountainCount * FountainSegs); fountain.count -= fountain.count % FountainSegs;
            gulls = Add("gulls", 2, 150);
            bolt = Add("bolt", 8, 1); bolt.count = MainSegs + Forks * ForkSegs;
            ventBolt = Add("ventBolt", 8, 1); ventBolt.count = VentLanes * VentSegs;
            ventBolt.bufA = Buf(ventA); ventBolt.bufB = Buf(ventB); ventBolt.emitters = VentLanes;
            beam = Add("beam", 9, 4); beam.count = 2;
            halos = Add("halos", 7, 1);
            embers = Add("embers", 1, 1400);
            ashFar = Add("ashFar", 0, 70000);
            ashNear = Add("ashNear", 0, 30000);
            rain = Add("rain", 0, RainCount);
            lens = Add("lens", 10, 92); lens.count = 46;

            // gull perches along the quay until the village provides better ones
            var perch = new List<Vector4>();
            for (int i = 0; i < 150; i++)
            {
                float z = Mathf.Lerp(-150f, -26f, Mathf.Pow(MathX.Hash(i, 3), 0.7f));
                bool edge = MathX.Hash(i, 5) < 0.6f;
                perch.Add(new Vector4(edge ? -19.6f + MathX.Hash(i, 7) * 0.5f : -22f - MathX.Hash(i, 9) * 30f, edge ? 2.12f : 0.25f, z, 0f));
            }
            SetGullPerches(perch);

            BuildImpactEmitters();
            BuildSplashEmitters();
            BuildDustEmitters();
            BuildSlideDust();
            BuildLazeEmitters();

            // the thread of steam on the summit before the eruption: looping puffs
            var ta = new Vector4[8]; var tb = new Vector4[8];
            for (int i = 0; i < 8; i++)
            {
                ta[i] = new Vector4(Sim.ConeSummit.x + (MathX.Hash(i, 201) - 0.5f) * 60f, Sim.ConeSummit.y - 170f, Sim.ConeSummit.z + (MathX.Hash(i, 203) - 0.5f) * 60f, i * 7f);
                tb[i] = new Vector4(5f, 26f, MathX.Hash(i, 205), 56f);
            }
            thread.bufA = Buf(ta); thread.bufB = Buf(tb); thread.emitters = 8;
        }

        public void SetBeacon(Vector3 p) { beaconPos = p; haveBeacon = true; }

        /// <summary>The two warm lights VillageDirector pools onto the lamps nearest the walker, for the ash and rain glints:
        /// xyz position, w lumen (0 when off or absent), and their linear colour. VillageDirector ticks after this director,
        /// so the pass reads them at render time, when this frame's positions and intensities are set (seekable).</summary>
        public void LampLights(out Vector4 a, out Vector4 b, out Vector4 col)
        {
            a = b = col = Vector4.zero;
            if ((lampLightA == null || lampLightB == null) && root != null)
            {
                Transform ta = root.Find("LampLightA"), tb = root.Find("LampLightB");
                if (ta != null) lampLightA = ta.GetComponent<Light>();
                if (tb != null) lampLightB = tb.GetComponent<Light>();
            }
            if (lampLightA != null) { a = PackLamp(lampLightA); col = lampLightA.color.linear; }
            if (lampLightB != null) { b = PackLamp(lampLightB); if (lampLightA == null) col = lampLightB.color.linear; }
        }

        static Vector4 PackLamp(Light l)
        {
            Vector3 p = l.transform.position;
            return new Vector4(p.x, p.y, p.z, l.isActiveAndEnabled ? Mathf.Max(l.intensity, 0f) : 0f);   // lumen (VillageDirector's unit)
        }

        void BuildSlideDust()
        {
            // puffs born along the track of the sliding flank as its front passes (collapse clock)
            var a = new List<Vector4>(); var b = new List<Vector4>();
            Vector2 sd = Sim.SlideDir, sn = new Vector2(-sd.y, sd.x);
            for (int i = 0; i < 150; i++)
            {
                float u = Mathf.Lerp(150f, 3300f, MathX.Hash(i, 501));
                float v = (MathX.Hash(i, 503) - 0.5f) * 2f * 0.85f * ScarHalfWidth(u);            // between the scar walls
                // time at which the slide front (plus a lead) reaches u
                float tArr = Config.SlideDuration * 1.6f;
                for (float t = 0f; t < Config.SlideDuration * 1.6f; t += 0.25f)
                    if (Sim.Slide(t) * ConeDirector.SlideRun + Mathf.Max(t - Config.SlideDuration, 0f) * 14f + 420f >= u) { tArr = t; break; }
                float x = Config.ConeCenter.x + sd.x * u + sn.x * v, z = Config.ConeCenter.z + sd.y * u + sn.y * v;
                float rho = Mathf.Sqrt(u * u + v * v);
                // the dust rises off the moving mass, which rides about 0.7 of the scar depth below the old surface
                float yOld = ConeDirector.SmoothHeight(new Vector2(x - Config.ConeCenter.x, z - Config.ConeCenter.z));
                float y = Mathf.Max(Mathf.Lerp(yOld, ConeDirector.GroundAfterCollapse(x, z), 0.7f), 0f) + 120f;
                a.Add(new Vector4(x, y, z, tArr + MathX.Hash(i, 505) * 3f));
                b.Add(new Vector4(9f, 64f + 40f * MathX.Hash(i, 507), MathX.Hash(i, 509), 34f));
            }
            slideDust.bufA = Buf(a.ToArray()); slideDust.bufB = Buf(b.ToArray()); slideDust.emitters = a.Count;
        }

        // CPU copy of ConeScarThickness (CascadeCone.hlsl) without its ragged-wall noise: a rounded horseshoe, 650 m deep at
        // the axis, head scarp about 530 m behind the axis, walls that splay downhill, a U-shaped floor.
        const float ScarUc = -60f, ScarR0 = 470f, ScarDeep = 650f;
        static float ScarHalfWidth(float u) { return ScarR0 + 0.36f * Mathf.Max(u - ScarUc, 0f); }
        static float ScarDepth(float u, float v)
        {
            float eu = Mathf.Min(u - ScarUc, 0f) / ScarR0, ev = Mathf.Abs(v) / ScarHalfWidth(u);
            float e = Mathf.Sqrt(eu * eu + ev * ev);
            float lat = 1f - MathX.Smooth(0.62f, 1.04f, e);
            float bowl = Mathf.Lerp(0.55f, 1f, Mathf.Sqrt(Mathf.Clamp01(1f - e * e)));
            return ScarDeep * lat * bowl * (1f - MathX.Smooth(1300f, 2250f, u));
        }

        void BuildLazeEmitters()
        {
            // 'laze': white steam where each lava flow of the cone reaches the sea. Three vents across each entry, looping
            // from the flow's arrival on (flows leave rho ~300 m at their start time and advance at their front speed).
            var a = new List<Vector4>(); var b = new List<Vector4>();
            Vector3[] flows = ConeDirector.LavaFlows;
            for (int f = 0; f < flows.Length; f++)
            {
                float az = flows[f].x, arrive = flows[f].y + (Config.ConeBaseRadius - LavaFlowStartRho) / Mathf.Max(flows[f].z, 0.1f);
                if (f != ConeDirector.ScarFlow)
                {
                    // A flank flow whose source went with the summit crusts over before it reaches the sea (ConeSurface.hlsl
                    // FLOW_STARVE_DAZ 0.9-1.4 rad, all of them arrive after the collapse): a crusted front does not boil
                    // the sea. Flows 0, 2 and 4 were orange flame walls and a campfire at a dark cone's foot (540).
                    float dAz = Mathf.Abs(Mathf.DeltaAngle(az * Mathf.Rad2Deg, Sim.SlideAzimuth)) * Mathf.Deg2Rad;
                    if (1f - MathX.Smooth(LazeStarveDAz.x, LazeStarveDAz.y, dAz) > 0.5f) continue;
                }
                lazeStart = Mathf.Min(lazeStart, arrive);
                float ca = Mathf.Cos(az), sa = Mathf.Sin(az);
                for (int j = 0; j < 3; j++)
                {
                    int key = f * 3 + j;
                    float side = (j - 1) * 55f + (MathX.Hash(key, 1101) - 0.5f) * 30f;
                    float rho = Config.ConeBaseRadius + 30f + MathX.Hash(key, 1103) * 40f;
                    float x = Config.ConeCenter.x + ca * rho - sa * side, z = Config.ConeCenter.z + sa * rho + ca * side;
                    a.Add(new Vector4(x, 2f, z, arrive + j * 4f));
                    b.Add(new Vector4(6f, 0.8f + 0.4f * MathX.Hash(key, 1105), MathX.Hash(key, 1107), 25f));
                }
            }
            laze.bufA = Buf(a.ToArray()); laze.bufB = Buf(b.ToArray()); laze.emitters = a.Count;
            // the shader shares the sprites round the vents (pid % emitters): keep the old density per vent, not per system
            laze.count = Mathf.Max(1, Mathf.RoundToInt(LazePerVent * scale) * Mathf.Max(a.Count, 1));
        }

        public void SetGullPerches(List<Vector4> p)
        {
            gulls.bufA = Buf(p.ToArray());
            gulls.emitters = p.Count;
            gulls.count = Mathf.Min(150, Mathf.Max(p.Count, 1));
        }

        /// <summary>Lamp heads: xyz position, w = switch-on order 0..1.</summary>
        public void SetLamps(List<Vector4> lamps)
        {
            halos.bufA = Buf(lamps.ToArray());
            halos.emitters = lamps.Count;
            halos.count = lamps.Count;
        }

        void BuildImpactEmitters()
        {
            // shore points of the near coast from the baked shore distance, with the time the wall reaches them
            var a = new List<Vector4>(); var b = new List<Vector4>();
            var fa = new List<Vector4>(); var fb = new List<Vector4>();     // flotsam: where the flood picks it up
            Vector2 aim = (Sim.WaveAim - Sim.SplashXZ).normalized;
            for (int j = 6; j < TerrainBuilder.H - 6; j += 6)
                for (int i = 6; i < TerrainBuilder.W - 6; i += 6)
                {
                    int k = j * TerrainBuilder.W + i;
                    float s0 = TerrainBuilder.Sd[k];
                    if (s0 > 0f || s0 < -14f) continue;
                    if (TerrainBuilder.Sd[k + 6] < 0f && TerrainBuilder.Sd[k - 6] < 0f && TerrainBuilder.Sd[k + 6 * TerrainBuilder.W] < 0f && TerrainBuilder.Sd[k - 6 * TerrainBuilder.W] < 0f) continue;
                    float x = TerrainBuilder.X0 + i * TerrainBuilder.Cell, z = TerrainBuilder.Z0 + j * TerrainBuilder.Cell;
                    if (z < -520f || z > 1150f) continue;
                    float arrive = Sim.WaveArrival(new Vector2(x, z));
                    Vector2 d = new Vector2(x, z) - Sim.WaveCentre(arrive);
                    float dist = d.magnitude;
                    float beam = Mathf.Lerp(0.3f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.2f, 0.86f, Vector2.Dot(d / dist, aim))));
                    float h = MathX.Hash(i, j);
                    a.Add(new Vector4(x, 4f, z, arrive - 1.5f));
                    b.Add(new Vector4((48f + 46f * h) * beam * ImpactSpeed, 0.75f + 0.7f * beam, h, 10f + 5f * h));
                    Vector2 toBay = WaterDirector.BayCentre - new Vector2(x, z);
                    if (toBay.sqrMagnitude > 1f && toBay.magnitude < 1800f)
                    {
                        toBay.Normalize();
                        fa.Add(new Vector4(x, 0f, z, arrive));
                        fb.Add(new Vector4(toBay.x, toBay.y, h, 0f));
                    }
                }
            impact.bufA = Buf(a.ToArray()); impact.bufB = Buf(b.ToArray()); impact.emitters = a.Count;
            flotsam.bufA = Buf(fa.ToArray()); flotsam.bufB = Buf(fb.ToArray()); flotsam.emitters = fa.Count;
            Debug.Log("CASCADE_INFO impact emitters " + a.Count + ", flotsam sources " + fa.Count);

            // steam where the pyroclastic currents reach the sea on the near side of the cone
            var sa = new List<Vector4>(); var sb = new List<Vector4>();
            for (int i = 0; i < 46; i++)
            {
                float ang = (Sim.SlideAzimuth + Mathf.Lerp(4f, 150f, i / 45f)) * Mathf.Deg2Rad;
                float r = Config.ConeBaseRadius + 40f + MathX.Hash(i, 71) * 160f;
                sa.Add(new Vector4(Config.ConeCenter.x + Mathf.Cos(ang) * r, 2f, Config.ConeCenter.z + Mathf.Sin(ang) * r, MathX.Hash(i, 73) * 9f));
                sb.Add(new Vector4(34f + 30f * MathX.Hash(i, 75), 2.6f, MathX.Hash(i, 77), 9f));
            }
            steam.bufA = Buf(sa.ToArray()); steam.bufB = Buf(sb.ToArray()); steam.emitters = sa.Count;
        }

        void BuildSplashEmitters()
        {
            var a = new List<Vector4>(); var b = new List<Vector4>();
            for (int i = 0; i < 70; i++)
            {
                float ang = MathX.Hash(i, 31) * Mathf.PI * 2f, r = 80f + 520f * Mathf.Sqrt(MathX.Hash(i, 33));
                a.Add(new Vector4(Sim.SplashXZ.x + Mathf.Cos(ang) * r, 3f, Sim.SplashXZ.y + Mathf.Sin(ang) * r, Config.SplashAt + r / 260f + MathX.Hash(i, 35) * 2.5f));
                b.Add(new Vector4(175f - r * 0.12f, 4.6f, MathX.Hash(i, 37), 24f));
            }
            splash.bufA = Buf(a.ToArray()); splash.bufB = Buf(b.ToArray()); splash.emitters = a.Count;
        }

        void BuildDustEmitters()
        {
            // tremor 1: a little dust from the walls of the stair-road around the walker
            var a1 = new List<Vector4>(); var b1 = new List<Vector4>();
            float s1 = Story.TriggerChainage(Ev.Tremor1);
            for (int i = 0; i < 14; i++)
            {
                float s = s1 + Mathf.Lerp(-6f, 40f, i / 13f);
                Vector3 p = PathDef.Pos(s), f = PathDef.FlatDir(s);
                Vector3 side = new Vector3(f.z, 0f, -f.x) * (i % 2 == 0 ? 1f : -1f) * (PathDef.HalfWidth(s) + 0.6f);
                a1.Add(new Vector4(p.x + side.x, p.y + 1.2f + MathX.Hash(i, 51) * 2f, p.z + side.z, 0.3f + MathX.Hash(i, 53) * 2.5f));
                b1.Add(new Vector4(0.25f, 0.5f, MathX.Hash(i, 55), 4.5f));
            }
            dust1.bufA = Buf(a1.ToArray()); dust1.bufB = Buf(b1.ToArray()); dust1.emitters = a1.Count;

            // tremor 2: wall dust nearby and a rockfall on the far cliff across the bay
            var a2 = new List<Vector4>(); var b2 = new List<Vector4>();
            float s2 = Story.TriggerChainage(Ev.Tremor2);
            for (int i = 0; i < 16; i++)
            {
                float s = s2 + Mathf.Lerp(-4f, 34f, i / 15f);
                Vector3 p = PathDef.Pos(s), f = PathDef.FlatDir(s);
                Vector3 side = new Vector3(f.z, 0f, -f.x) * (i % 2 == 0 ? 1f : -1f) * (PathDef.HalfWidth(s) + 0.6f);
                a2.Add(new Vector4(p.x + side.x, p.y + 1.5f + MathX.Hash(i, 57) * 2.5f, p.z + side.z, 0.4f + MathX.Hash(i, 59) * 5f));
                b2.Add(new Vector4(0.35f, 0.7f, MathX.Hash(i, 61), 5f));
            }
            for (int i = 0; i < 26; i++)
            {
                // the cliff below the headland, 600 to 900 m away: a string of puffs running down the face
                float u = i / 25f;
                float x = -330f - 60f * MathX.Hash(i, 63), z = 545f - 22f * u + 10f * MathX.Hash(i, 65);
                float y = Mathf.Lerp(150f, 12f, u) + 6f * MathX.Hash(i, 67);
                a2.Add(new Vector4(x, y, z, 1.6f + u * 6.5f + MathX.Hash(i, 69) * 0.8f));
                b2.Add(new Vector4(2.2f, 16f, MathX.Hash(i, 71), 13f));
            }
            dust2.bufA = Buf(a2.ToArray()); dust2.bufB = Buf(b2.ToArray()); dust2.emitters = a2.Count;

            // tremor 3 (violent): puffs off the cliffs all around the bay and near the walker
            var a3 = new List<Vector4>(); var b3 = new List<Vector4>();
            float s3 = Story.TriggerChainage(Ev.Tremor3);
            for (int i = 0; i < 12; i++)
            {
                Vector3 p = PathDef.Pos(s3 + Mathf.Lerp(-5f, 30f, i / 11f)), f = PathDef.FlatDir(s3);
                Vector3 side = new Vector3(f.z, 0f, -f.x) * (PathDef.HalfWidth(s3) + 1.5f + MathX.Hash(i, 81) * 3f);
                a3.Add(new Vector4(p.x + side.x, p.y + 0.3f, p.z + side.z, MathX.Hash(i, 83) * 5f));
                b3.Add(new Vector4(0.5f, 1.1f, MathX.Hash(i, 85), 6f));
            }
            for (int i = 0; i < 34; i++)
            {
                float x = Mathf.Lerp(-900f, -60f, MathX.Hash(i, 87)), z = 520f + 30f * MathX.Hash(i, 89);
                if (i % 3 == 0) { x = -24f - 20f * MathX.Hash(i, 91); z = Mathf.Lerp(40f, 360f, MathX.Hash(i, 93)); }
                a3.Add(new Vector4(x, 20f + 110f * MathX.Hash(i, 95), z, MathX.Hash(i, 97) * 6f));
                b3.Add(new Vector4(2.5f, 15f, MathX.Hash(i, 99), 14f));
            }
            dust3.bufA = Buf(a3.ToArray()); dust3.bufB = Buf(b3.ToArray()); dust3.emitters = a3.Count;
        }

        /// <summary>Lightning in the plume as a pure function of the clocks (EnvDirector calls it too, so light and sprite agree).
        /// Returns the light pulse 0..1 (return stroke, restrikes, continuing current) and the channel ends A0 -> B0;
        /// k is the slot index (seed of the channel shape).</summary>
        public static float Lightning(float te, float dark, float storm, out Vector3 A0, out Vector3 B0, out int k)
        {
            return LightningEvent(te, dark, storm, out A0, out B0, out k, out _, out _);
        }

        /// <summary>Lightning plus what the sprite needs: its brightness (the leader glows before the light pulse) and the
        /// leader's reveal 0..1 along the channel.</summary>
        static float LightningEvent(float te, float dark, float storm, out Vector3 A0, out Vector3 B0, out int k, out float sprite, out float reveal)
        {
            const float slot = 1.4f;                                    // at most one bolt per slot: the flash rule allows 3 per second
            float rate = te < 34f || dark < 0.3f ? 0f : Mathf.Lerp(0.42f, 0.7f, storm);   // no lightning in daylight
            float tt = Mathf.Max(te, 0f);
            int k0 = Mathf.FloorToInt(tt / slot);
            float soft = 1f + 1.2f * storm;                             // in the storm the pulses are soft and long (bible B6: 150-300 ms)
            float bestS = 0f, bestP = 0f, bestR = 1f;
            k = k0;
            // a bolt with restrikes can outlast its slot: look at this slot and the one before, keep the brighter
            for (int j = k0; j >= Mathf.Max(k0 - 1, 0); j--)
            {
                if (MathX.Hash(j, 901) >= rate) continue;
                float tIn = tt - j * slot - MathX.Hash(j, 903) * (slot - 0.4f);
                if (tIn < 0f) continue;
                float p = StrokePulse(tIn, j, soft);
                float s = Mathf.Max(p, tIn < Leader ? 0.3f : 0f);
                if (s > bestS) { bestS = s; bestP = p; bestR = Mathf.Clamp01(tIn / Leader); k = j; }
            }
            BoltEnds(k, out A0, out B0);
            sprite = bestS; reveal = bestR;
            return bestP;
        }

        /// <summary>A short stroke: rises in 12 ms, holds, then falls.</summary>
        static float Stroke(float x, float amp, float hold, float fall)
        {
            return amp * MathX.Smooth(0f, 0.012f, x) * (1f - MathX.Smooth(hold, hold + fall, x));
        }

        /// <summary>Light of one flash tIn seconds after its leader starts: the return stroke, 0 to 2 restrikes down the same
        /// channel at about +75 and +170 ms, and a weak continuing current between them. Peak 0.6-1 (the old pulse peaked
        /// at 1 for 60 ms), so the mean-luminance change stays within the old flash budget.</summary>
        static float StrokePulse(float tIn, int j, float soft)
        {
            float x = tIn - Leader;
            float amp = 0.6f + 0.4f * MathX.Hash(j, 917);
            float h = MathX.Hash(j, 919);
            int restrikes = h < 0.3f ? 0 : h < 0.75f ? 1 : 2;
            float p = Stroke(x, 1f, 0.03f * soft, 0.06f * soft);
            float d1 = 0.075f + 0.02f * MathX.Hash(j, 921), d2 = 0.17f + 0.03f * MathX.Hash(j, 923);
            if (restrikes > 0) p = Mathf.Max(p, Stroke(x - d1, 0.65f, 0.025f * soft, 0.05f * soft));
            if (restrikes > 1) p = Mathf.Max(p, Stroke(x - d2, 0.4f, 0.025f * soft, 0.05f * soft));
            float cc = 0.12f * MathX.Smooth(0f, 0.02f, x) * (1f - MathX.Smooth(0.22f * soft, 0.38f * soft, x));
            return amp * Mathf.Min(1f, p + cc);
        }

        /// <summary>Channel ends of bolt k: mostly long discharges inside the column; 30% strike down onto the cone or the sea.</summary>
        static void BoltEnds(int k, out Vector3 A0, out Vector3 B0)
        {
            float ang = MathX.Hash(k, 907) * Mathf.PI * 2f;
            if (MathX.Hash(k, 925) < 0.3f)
            {
                float hgt = 2200f + 3000f * MathX.Hash(k, 905);
                float rr = (PlumeDirector.VentRadius + PlumeDirector.Growth * hgt) * 0.6f * MathX.Hash(k, 909);
                A0 = Sim.ConeSummit + new Vector3(Mathf.Cos(ang) * rr, hgt, Mathf.Sin(ang) * rr);
                float a2 = ang + (MathX.Hash(k, 911) - 0.5f) * 1.6f;
                float rho = 900f + 2600f * MathX.Hash(k, 913);
                float ground = Config.ConeHeight * Mathf.Pow(Mathf.Clamp01(1f - rho / Config.ConeBaseRadius), 1.85f);
                // end inside the slope (or under the sea): the depth test cuts the channel where it meets the real surface
                B0 = new Vector3(Config.ConeCenter.x + Mathf.Cos(a2) * rho, ground - 150f, Config.ConeCenter.z + Mathf.Sin(a2) * rho);
                return;
            }
            float h = 2600f + 9000f * MathX.Hash(k, 905);
            float r = (PlumeDirector.VentRadius + PlumeDirector.Growth * h) * (0.5f + 0.6f * MathX.Hash(k, 909));
            A0 = Sim.ConeSummit + new Vector3(Mathf.Cos(ang) * r, h, Mathf.Sin(ang) * r);
            float a3 = ang + 1.7f + 2.6f * MathX.Hash(k, 911);
            B0 = A0 + new Vector3(Mathf.Cos(a3), -0.45f + 0.9f * MathX.Hash(k, 913), Mathf.Sin(a3)) * (1500f + 2400f * MathX.Hash(k, 915));
        }

        /// <summary>Vent lightning: short sparks in the jet just above the vent, many per second at the onset and when the
        /// flank fails, about one per second while it is dark. Eight lanes, each firing at most once per 0.8 s slot. Fills
        /// the sprite buffers (A: end + brightness, B: end + seed) and returns the brightest spark as a light
        /// (xyz midpoint, w intensity on the Bolt0 scale).</summary>
        static Vector4 VentLightning(float te, float tc, float dark, float calm, Vector3 vent, Vector4[] aOut, Vector4[] bOut)
        {
            const float slot = 0.8f;
            float onset = MathX.Smooth(0.3f, 2f, te) * (1f - MathX.Smooth(38f, 50f, te));
            float fail = MathX.Smooth(0f, 1.5f, tc) * (1f - MathX.Smooth(22f, 32f, tc));
            float steady = MathX.Smooth(0.5f, 0.7f, dark) * (1f - 0.7f * calm);
            float rate = te > 0.3f ? Mathf.Max(5.5f * Mathf.Max(onset, fail), steady) : 0f;      // sparks per second
            float p = Mathf.Clamp01(rate * slot / VentLanes);
            Vector4 best = Vector4.zero;
            for (int i = 0; i < VentLanes; i++)
            {
                float tl = Mathf.Max(te, 0f) + i * 0.31f;                    // lanes out of step
                int m = Mathf.FloorToInt(tl / slot);
                int key = m * VentLanes + i;
                float tIn = tl - m * slot - MathX.Hash(key, 931) * (slot - 0.3f);
                float pulse = 0f;
                if (MathX.Hash(key, 933) < p)
                {
                    pulse = Stroke(tIn, 0.6f + 0.4f * MathX.Hash(key, 935), 0.025f, 0.07f);
                    if (MathX.Hash(key, 937) > 0.55f) pulse = Mathf.Max(pulse, Stroke(tIn - 0.09f, 0.5f, 0.02f, 0.05f));
                }
                float ang = MathX.Hash(key, 939) * Mathf.PI * 2f, r = 300f * Mathf.Sqrt(MathX.Hash(key, 941));
                float hgt = 50f + 850f * Mathf.Pow(MathX.Hash(key, 943), 1.5f);
                Vector3 A = vent + new Vector3(Mathf.Cos(ang) * r, hgt, Mathf.Sin(ang) * r);
                Vector3 d = new Vector3(MathX.Hash(key, 945) - 0.5f, (MathX.Hash(key, 947) - 0.5f) * 1.4f, MathX.Hash(key, 949) - 0.5f);
                d = d.sqrMagnitude > 1e-4f ? d.normalized : Vector3.up;
                Vector3 B = A + d * (80f + 270f * MathX.Hash(key, 951));
                aOut[i] = new Vector4(A.x, A.y, A.z, pulse);
                bOut[i] = new Vector4(B.x, B.y, B.z, key % 97);
                if (pulse * VentBoltLight * EnvDirector.BoltGain > best.w)
                {
                    Vector3 mid = (A + B) * 0.5f;
                    best = new Vector4(mid.x, mid.y, mid.z, pulse * VentBoltLight * EnvDirector.BoltGain);
                }
            }
            return best;
        }

        /// <summary>Azimuth (rad) of the lighthouse beam at story time t: one turn every 15 s.</summary>
        public static float BeamAngle(float t) { return t * (Mathf.PI * 2f / 15f); }

        public void Tick(float dt)
        {
            float t = Story.Time, te = Story.T(Ev.Eruption), tw = Story.T(Ev.Wave), tc = Story.T(Ev.Collapse);
            float storm = EnvDirector.Storm, ashK = Sim.Ashfall(te), night = EnvDirector.Night;
            float tSil = Story.T(Ev.Silence);
            // in the storm the flakes are lit grey snow at about the veil's luminance: dark only against the practicals
            Color ashCol = Palette.AshGrey.linear * Mathf.Lerp(1.9f, 2.6f, storm);

            // ---- ash: slow flakes after the column has spread; a horizontal blizzard in the storm; soft snow at the end ----
            float calm = MathX.Smooth(0f, 18f, tSil);
            Vector3 wind = Vector3.Lerp(new Vector3(0.5f, -1.15f, 0.2f), new Vector3(11f, -2.2f, 4.5f), storm);
            wind = Vector3.Lerp(wind, new Vector3(0.12f, -0.75f, 0.05f), calm);
            float ashLive = Mathf.Clamp01(ashK * 0.32f + storm * 0.9f + calm * 0.4f);
            ashNear.on = ashFar.on = ashLive > 0.001f;
            ashNear.clock = ashFar.clock = t;
            // c.x = 1: a film shutter (Particles.shader): streaks capped on screen, energy conserving, clear of the lens;
            // c.z: the pooled lamp lights glint on the flakes (a snow-globe of amber around each lamp in the storm)
            ashNear.a = new Vector4(16f, 12f, 16f, ashLive);
            ashNear.b = new Vector4(wind.x, wind.y, wind.z, 0.22f + 0.5f * storm);
            ashNear.c = new Vector4(1f, 0f, AshLampGlint * Mathf.Lerp(AshGlintPreStorm, 1f, Mathf.Max(storm, calm)), 0f);
            ashNear.color = new Vector4(ashCol.r, ashCol.g, ashCol.b, Mathf.Lerp(0.75f, 0.5f, storm));
            // Storm: 4-12 mm, so the lamp glints show at 3-10 m. Before it, fine 2.5-7 mm ash: a 12 mm flake 2 m from the
            // lens is a 6 px disc whose 1/48 s fall is shorter than its width, a round dark dot (sensor dust, 334-462)
            ashNear.size = new Vector4(Mathf.Lerp(0.0025f, 0.004f, storm), Mathf.Lerp(0.007f, 0.012f, storm), ShutterSeconds, 0.3f);
            ashFar.a = new Vector4(90f, 55f, 90f, ashLive);
            ashFar.b = new Vector4(wind.x, wind.y, wind.z, 0.9f + 1.4f * storm);
            ashFar.c = ashNear.c;
            ashFar.color = new Vector4(ashCol.r, ashCol.g, ashCol.b, 0.5f);
            ashFar.size = new Vector4(Mathf.Lerp(0.005f, 0.008f, storm), Mathf.Lerp(0.012f, 0.022f, storm), ShutterSeconds, 1.5f);

            // ---- spray rain on the walker after the wall strikes ----
            float ti = tw - Config.WaveCross;
            float rainK = MathX.Smooth(-12f, 3f, ti) * (1f - MathX.Smooth(14f, 34f, ti));
            rain.on = rainK > 0.001f;
            rain.clock = t;
            rain.a = new Vector4(22f, 16f, 22f, rainK);
            rain.b = new Vector4(3.5f, -9.5f, 2f, 0.6f);
            // film-shutter rain: x shutter on, y streak cap (px), z lamp glints, w relief of the energy conservation
            rain.c = new Vector4(1f, RainStreakPx, RainLampGlint, RainRelief);
            // a: 0.5 -> 0.65; the shader now lights the drops with the horizon ring and the strip (silver streaks)
            rain.color = new Vector4(0.82f, 0.84f, 0.84f, 0.65f);
            rain.size = new Vector4(0.004f, 0.010f, ShutterSeconds, 0.4f);

            // ---- embers in the ash night ----
            float emb = Mathf.Clamp01(night * (0.35f + 0.65f * storm)) * (1f - MathX.Smooth(20f, 50f, tSil) * 0.8f);
            embers.on = emb > 0.001f;
            embers.clock = t;
            embers.a = new Vector4(46f, 26f, 46f, emb);
            embers.b = new Vector4(2.5f + 5f * storm, 0.5f, 1.2f + 2f * storm, 1.6f);
            embers.c = new Vector4(1f, 0f, 0f, 0f);                     // film shutter; near the lens a spark is capped at a few pixels
            // the energy-conserving blur spreads a fast storm ember over its streak (about x0.25 per pixel): a little hotter there
            Color ec = Palette.Ember.linear * Mathf.Lerp(9000f, 14000f, storm);
            embers.color = new Vector4(ec.r, ec.g, ec.b, 1f);
            embers.size = new Vector4(0.012f, 0.03f, ShutterSeconds, 0.4f);

            // ---- gulls ----
            float tg = Story.T(Ev.Gulls);
            gulls.on = tg < 75f;
            gulls.clock = t;
            gulls.a = new Vector4(Mathf.Max(tg, -1f), 0f, 0f, 0f);
            gulls.b = new Vector4(-0.72f, 0.16f, 0.67f, 13f);
            gulls.color = new Vector4(0.92f, 0.92f, 0.9f, 1f);
            gulls.size = new Vector4(0.7f, 0.7f, 0f, 0.5f);

            // ---- the wave: spindrift off the crest, fountains where it strikes the coast ----
            Vector2 aim = (Sim.WaveAim - Sim.SplashXZ).normalized;
            float A = Sim.WaveHeight(tw);
            crest.on = tw > 6f && tw < Config.WaveCross + 20f;
            crest.clock = Mathf.Max(tw, 0f);
            Vector2 wc = Sim.WaveCentre(tw);
            float wr = Sim.WaveFrontRadius(tw);
            crest.a = new Vector4(wc.x, wc.y, wr, A);
            crest.b = new Vector4(aim.x, aim.y, Sim.WaveReach / Config.WaveCross, Mathf.Clamp(2700f / Mathf.Max(wr, 1f), 0.2f, 0.62f));
            // the spray curtain is a volume now: the sprites are only the streaks torn off it, short-lived and stretched.
            // Narrow (1-6 m) and long (about 35 m/s x 1.2 s): torn filaments; 5-39 m sprites read as soft balls of lens bokeh.
            crest.color = new Vector4(0.80f, 0.83f, 0.82f, 0.30f * (1f - MathX.Smooth(Config.WaveCross - 2f, Config.WaveCross + 18f, tw)));
            // droplet look (capped at 3 px wide, 28 px long on screen), 3 s life; w = 1: no energy conservation, a torn
            // filament's length is its shape, not motion blur
            // (ARTIFA-1 asked w = 0.5 for thinner near filaments; kept at 1, since its own aim is an unchanged 6-150 m near
            // field, where PlumeFx now fades the crest volume and leaves the droplets to carry it)
            crest.c = new Vector4(0f, 1f, 3f, 1f);
            // 0.4-1.6 m (was 1.2-4.5): the 3 px cap still sets the width inside ~100 m; past it droplets thin out (and fade
            // out by 420 m, Particles.shader DROP_FAR_CREST) instead of standing as solid 6 px dots
            crest.size = new Vector4(0.4f, 1.6f, CrestStretch, 30f);         // z: stretch seconds
            // the strike: droplets in front of the ray-marched spray tower, small and drawn out along their flight. They
            // are true motion blur (w = 0: energy conserving), capped on screen and faded inside 6-16 m (the rain system is
            // the spray that reaches the walker): 12 m sprites stretched 0.35 s were 30-300 px smears over the lens
            impact.on = tw > 40f && tw < Config.WaveCross + 50f;
            impact.clock = tw;
            impact.a = new Vector4(aim.x * 0.35f, 0f, aim.y * 0.35f, 0f);
            impact.c = new Vector4(0f, 1f, 0f, 0f);                     // droplet look
            impact.color = new Vector4(0.80f, 0.82f, 0.80f, 0.9f);     // a: up from 0.75, the conserving blur thins each streak
            impact.size = new Vector4(0.3f, 1.1f, ImpactStretch, 14f);

            // ---- flotsam: wreckage riding the flood after the strike ----
            float tiF = tw - Config.WaveCross;
            flotsam.on = flotsam.emitters > 0 && tiF > -20f && tiF < 85f && storm < 0.9f;
            flotsam.clock = tw;
            flotsam.a = new Vector4(0f, 70f, 0f, 0f);                   // y: seconds after the wall until it has sunk or drifted off
            flotsam.color = new Vector4(1f, 1f, 1f, 0.95f);
            flotsam.size = new Vector4(0.6f, 4f, 0f, 0.4f);                     // half-length of a piece (m): planks to roof sheets

            // ---- collapse: the splash dome ----
            splash.on = false;      // overlapping sprites read as one glowing dome: the splash is the ray-marched jets and surge now
            splash.clock = tc;
            splash.a = Vector4.zero;
            splash.color = new Vector4(0.84f, 0.84f, 0.80f, 0.40f);      // jets in front of the volumetric crown
            splash.size = new Vector4(30f, 120f, 0f, 60f);

            // ---- collapse: the dust that rolls ahead of the sliding flank ----
            slideDust.on = tc > 0.5f && tc < 80f;
            slideDust.clock = tc;
            slideDust.a = new Vector4(Sim.SlideDir.x * 5f, 6.5f, Sim.SlideDir.y * 5f, 0f);
            slideDust.c = Vector4.zero;
            slideDust.color = new Vector4(0.62f, 0.56f, 0.50f, 0.35f);
            slideDust.size = new Vector4(0.9f, 3.4f, 0f, 60f);

            // ---- steam where the hot currents meet the sea: looping fountains during the ash night ----
            // 2.5-5 km away and hundreds of metres wide: inside the storm they would take the fog colour as pale cards
            float tst = Story.T(Ev.AshStorm);
            steam.on = tst > -40f && storm < 0.9f;
            steam.clock = Mathf.Repeat(Mathf.Max(tst + 40f, 0f), 18f);
            steam.a = new Vector4(0.2f, 0f, 0.1f, 0f);
            steam.color = new Vector4(0.86f, 0.80f, 0.74f, 0.85f * MathX.Smooth(-40f, -10f, tst) * (1f - MathX.Smooth(0.4f, 0.85f, storm)));
            steam.size = new Vector4(44f, 235f, 0f, 60f);

            // ---- tremor dust ----
            Color dustCol = Color.Lerp(Palette.Whitewash.linear, Palette.CliffOchre.linear, 0.45f) * 0.8f;
            SetDust(dust1, Story.T(Ev.Tremor1), dustCol, 9f);
            dust1.on = false;      // puffs a metre from the lens read as soft discs; the tremor shows in the camera, the sound and the far rockfalls
            SetDust(dust2, Story.T(Ev.Tremor2), dustCol, 22f);
            SetDust(dust3, Story.T(Ev.Tremor3), dustCol, 24f);

            // ---- incandescent ejecta: bombs with glowing trails that land as spatter, the vent fountain, the blast at the collapse ----
            // Rates are evaluated in the shader at each particle's launch time (offsets of the collapse and silence clocks).
            float tcOff = Story.On(Ev.Collapse) ? tc - te : -1e4f;              // before the event: its clock reads far negative (not -1e9: float precision)
            float silOff = Story.On(Ev.Silence) ? tSil - te : -1e4f;
            Vector3 ventTop = Sim.ConeSummit + new Vector3(0f, -VentBelowSummit, 0f);   // as PlumeDirector: the vent before the flank fails
            float ventSink = ConeDirector.VentDrop(1e3f);                         // fully slid
            float glowAdd = Mathf.Clamp01(2f * EnvDirector.Dark);                 // additive at night; by day a dark ash wake
            Vector3 blastDir = new Vector3(Sim.SlideDir.x, 0.35f, Sim.SlideDir.y).normalized;   // low, over the slide, toward the village
            bool farVisible = storm < 0.9f;                 // deeper in the storm the mean free path is < 50 m: skip what is far away
            bombs.on = te > 0.3f && farVisible;
            bombs.clock = te;
            bombs.a = new Vector4(ventTop.x, ventTop.y, ventTop.z, ventSink);
            bombs.b = new Vector4(blastDir.x, blastDir.y, blastDir.z, 0.35f);   // w: share of bombs between the onset and the blast
            bombs.c = new Vector4(tcOff, 1f, glowAdd, 1f);                       // y: share in the blast, w: share at the onset
            bombs.d = new Vector4(0f, 0f, 9f, 230f);                             // z: cooling time (s), w: top launch speed (m/s)
            Vector2 windLean = new Vector2(0.94f, 0.34f);                         // = PlumeDirector's _PlWind: the ash drifts this way
            bombs.e = new Vector4(silOff, 0.35f, BombWindLean * windLean.x, BombWindLean * windLean.y);   // y: steady share factor once calm
            float bombNits = Mathf.Lerp(BombNitsDay, BombNitsNight, glowAdd);
            bombs.color = new Vector4(bombNits, bombNits, bombNits, 0.85f);     // white-hot nits (the ramp colours it)
            // glow size (m; the shader spreads it as the cube of a rank: mostly 2.5-4 m spray, ~10% blocks of 8-11 m that
            // read as 2-4 px heads with visible arcs at 5 km), seconds per trail segment, soft fade
            bombs.size = new Vector4(2.5f, 11f, BombTrailSeg, 12f);
            fountain.on = te > 0.3f && farVisible;
            fountain.clock = te;
            fountain.a = bombs.a;
            fountain.b = new Vector4(0f, 1f, 0f, 0.6f);
            fountain.c = new Vector4(tcOff, 1f, Mathf.Max(glowAdd, 0.35f), 1f);   // liquid lava: an orange-red glow by day, not white sparks
            fountain.d = new Vector4(0f, 1f, 6f, 115f);
            fountain.e = new Vector4(silOff, 0.6f, FountainWindLean * windLean.x, FountainWindLean * windLean.y);
            float fountainNits = Mathf.Lerp(FountainNitsDay, FountainNitsNight, glowAdd);
            fountain.color = new Vector4(fountainNits, fountainNits, fountainNits, 0.9f);
            fountain.size = new Vector4(4f, 10f, FountainTrailSeg, 12f);

            // the fountain's glowing body: the clots rise out of light instead of radiating from a point
            Vector3 ventNow = ConeDirector.Vent(tc);                              // the vent now (as PlumePass and EnvDirector)
            float coreK = MathX.Smooth(0.3f, 3f, te) * (1f - 0.4f * calm);
            fountainCore.on = fountain.on && coreK > 0.001f;
            if (fountainCore.on)
            {
                for (int i = 0; i < CoreGlows; i++) coreA[i] = new Vector4(ventNow.x, ventNow.y + CoreHeights[i], ventNow.z, 0f);
                fountainCore.bufA.SetData(coreA);
                fountainCore.clock = te;
                fountainCore.a = new Vector4(2f, 1f, 0f, 0f);                   // x: past the lamp switch-on ramp, y: size scale
                fountainCore.c = new Vector4(1f, 0f, 0f, 0f);                   // x: core mode (buffer B: size scale, intensity)
                Color cc = Color.Lerp(Palette.Lava.linear, new Color(1f, 0.6f, 0.24f), 0.3f) * (Mathf.Lerp(CoreNitsDay, CoreNitsNight, glowAdd) * coreK);
                fountainCore.color = new Vector4(cc.r, cc.g, cc.b, 1f);
                fountainCore.size = new Vector4(CoreRadius, CoreRadius, 0f, 60f);
            }

            // ---- laze: steam columns where the lava flows reach the sea ----
            laze.on = te > lazeStart - 1f && farVisible;
            laze.clock = te;
            // A low steam bank, not a stack of flames: it rises 4-8.5 m/s (was 7-12) and drifts with the ash toward the
            // village; bigger, thinner puffs merge into one bank
            laze.a = new Vector4(1.5f, 2.5f, 0.55f, 0f);
            // Nits on the steam at the waterline, falling off over 50 m (was 900 over 120 m: the whole puff orange, blown
            // out by the clearing's exposure). Taken per corner in the shader: an amber foot, pink-grey steam above it.
            Color lazeGlow = Palette.Lava.linear * 300f;
            laze.b = new Vector4(lazeGlow.r, lazeGlow.g, lazeGlow.b, 1f / 50f);
            laze.c = new Vector4(32f, 0f, 1f, 0f);                               // loop period (s), gated by each emitter's start
            laze.color = new Vector4(0.86f, 0.84f, 0.80f, 0.32f);
            laze.size = new Vector4(70f, 240f, 0f, 80f);

            // ---- the steam thread: a peaceful sign of life, erased by the blast ----
            // Each emitter's puffs were all born inside 1.5 s every 56 s: eight clumps 7 s apart, a string of round beads
            // (130) and a rope of balls (185). The gated loop (c.z = 1) spreads their births over the whole period, so the
            // thread is a continuous stream. Period 72 s >= the longest life (70 s): no puff pops at the wrap; the clock
            // offset (> 49 s of emitter starts + 72 s) has every lane running from t = 0.
            thread.on = te < 3f;
            thread.clock = t + 130f;
            thread.a = new Vector4(5.5f, 8.5f, 2.5f, 0f);
            thread.c = new Vector4(72f, 0f, 1f, 0f);
            thread.color = new Vector4(0.92f, 0.92f, 0.9f, 0.42f * (1f - MathX.Smooth(0f, 2.5f, te)));
            thread.size = new Vector4(0.7f, 3.4f, 0f, 30f);
            steam.c = new Vector4(18f, 0f, 0f, 0f);

            // ---- lightning inside the plume: at most one bolt per 1.4 s slot (the flash rule allows 3 per second) ----
            float pulse = LightningEvent(te, EnvDirector.Dark, storm, out Vector3 A0, out Vector3 B0, out int k, out float sprite, out float reveal);
            bolt.on = sprite > 0.002f && farVisible;
            Vector3 mid0 = (A0 + B0) * 0.5f;
            PlumeDirector.Bolt0 = pulse > 0.002f ? new Vector4(mid0.x, mid0.y, mid0.z, pulse * MainBoltLight * EnvDirector.BoltGain) : Vector4.zero;
            if (bolt.on)
            {
                bolt.clock = t;
                bolt.a = new Vector4(A0.x, A0.y, A0.z, 0f);                     // w = 0: the main bolt with forks
                bolt.b = new Vector4(B0.x, B0.y, B0.z, 0f);
                bolt.c = new Vector4(k % 97, sprite, reveal, 0f);
                bolt.color = new Vector4(0.72f, 0.84f, 1f, 1f) * 240000f;
                // half-width of the quad: a core of 1/12 of it (about 8 m, at least a pixel) in a 100 m halo scattered by the
                // ash (60 m with a 0.07 peak read as a bare grey hairline at 540)
                bolt.size = new Vector4(100f, 100f, 0f, 200f);
            }
            // vent sparks in the jet: the brightest one lights the plume as Bolt1
            PlumeDirector.Bolt1 = VentLightning(te, tc, EnvDirector.Dark, calm, ventNow, ventA, ventB);
            ventBolt.on = PlumeDirector.Bolt1.w > 0f && farVisible;
            if (ventBolt.on)
            {
                ventBolt.bufA.SetData(ventA); ventBolt.bufB.SetData(ventB);
                ventBolt.clock = t;
                ventBolt.a = new Vector4(0f, 0f, 0f, 1f);                       // w = 1: ends and brightness from the buffers
                ventBolt.color = new Vector4(0.72f, 0.84f, 1f, 1f) * 150000f;
                ventBolt.size = new Vector4(45f, 45f, 0f, 100f);                // as the main bolt: a ~4 m core in a 45 m halo
            }
            // the world sees the brighter of the two as one light (cone, sea)
            Shader.SetGlobalVector(idCBolt, PlumeDirector.Bolt0.w >= PlumeDirector.Bolt1.w ? PlumeDirector.Bolt0 : PlumeDirector.Bolt1);

            // ---- the lighthouse beam through the falling ash: published for the plume pass (analytic cone) and the ash flakes ----
            float beamOn = Sim.Lamps(te, 8f) * Mathf.Clamp01(MathX.Smooth(0f, 30f, Story.T(Ev.AshStorm)) * (0.45f + 0.55f * Mathf.Max(storm, calm)));
            bool beamLive = haveBeacon && beamOn > 0.001f;
            float beamAng = BeamAngle(t);
            Vector3 beamAxis = new Vector3(Mathf.Cos(beamAng), -0.03f, Mathf.Sin(beamAng)).normalized;
            Shader.SetGlobalVector(idCBeam, beamLive ? new Vector4(beaconPos.x, beaconPos.y, beaconPos.z, BeamScale * beamOn) : Vector4.zero);
            Shader.SetGlobalVector(idCBeam2, new Vector4(beamAxis.x, beamAxis.y, beamAxis.z, Mathf.Cos(BeamHalfAngle * Mathf.Deg2Rad)));
            beam.on = BillboardBeam && haveBeacon && beamOn > 0.01f;
            if (beam.on)
            {
                float ba = BeamAngle(t);
                beam.clock = t;
                beam.a = new Vector4(beaconPos.x, beaconPos.y, beaconPos.z, 420f);
                beam.b = new Vector4(Mathf.Cos(ba), -0.03f, Mathf.Sin(ba), 0f);
                beam.c = new Vector4(beamOn * 0.42f, 0f, 0f, 0f);
                beam.color = new Vector4(1f, 0.93f, 0.78f, 1f) * 1500f;
                beam.size = new Vector4(2.4f, 2.4f, 0f, 3f);
            }

            // ---- drops on the lens after the wall strikes ----
            lens.on = ti > -1.5f && ti < 26f;
            lens.clock = t;
            lens.a = new Vector4(ti + 1.5f, 0f, 0f, 0f);
            lens.color = new Vector4(1f, 1f, 1f, 0.6f);                      // a: faint (they read as grey rings at full strength)
            lens.size = new Vector4(0.02f, 0.02f, 0f, 0.001f);

            // ---- lamp halos (the shader caps each at ~5 degrees: near lamps keep a core, their real light draws the fog) ----
            halos.on = halos.emitters > 0 && te > Config.LampsOn - 2f;
            halos.clock = t;
            halos.a = new Vector4(Mathf.Max(te - Config.LampsOn, 0f), 1f + 2.2f * storm + 0.6f * night, 0f, 0f);
            Color hc = Palette.WindowAmber.linear * ((2600f + 2400f * night) * Mathf.Pow(2f, -0.6f * EnvDirector.LadderEV));   // halos take back most of the exposure ladder
            halos.color = new Vector4(hc.r, hc.g, hc.b, 1f);
            halos.size = new Vector4(0.55f, 0.55f, 0f, 0.25f);
        }

        static void SetDust(PSystem s, float clock, Color col, float lifeWindow)
        {
            s.on = clock > 0f && clock < lifeWindow;
            s.clock = clock;
            s.a = new Vector4(0.15f, 0.25f, 0.08f, 0f);
            s.color = new Vector4(col.r, col.g, col.b, 0.3f);
            s.size = new Vector4(0.5f, 2.4f, 0f, 1.2f);
        }

        public void Dispose()
        {
            for (int i = 0; i < owned.Count; i++) owned[i].Dispose();
            owned.Clear();
        }
    }
}
