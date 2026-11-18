using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace CASCADE
{
    /// <summary>
    /// White villages strung along the far caldera rim, at impostor quality (boxes, a few vaults and blue domes, a
    /// speckle of dark windows), seated on the far ring mesh exactly as it is drawn (TerrainBuilder.FarHeight).
    /// At night their lit windows are points of light: emitters of the lamp-halo particle kind, because a window at
    /// 1.5-9 km is below a pixel and a lit quad would shimmer under TAA, while a halo is always a few pixels wide.
    /// The wave puts out every light it drowns, a few seconds after its front reaches it: a ring of blackouts racing
    /// round the caldera. Everything is a pure function of the clocks.
    /// </summary>
    public static class FarVillages
    {
        // ---- sites: arcs of the far crest (degrees round the cone axis, from +x towards +z), houses, village cores.
        //      Chosen by where the cinematic camera looks (checked offline against Gaze's cues and the terrain):
        //      the south rim, 1.4-4 km (frame 130, 395-476 as the wave sweeps it, 625 at night: the survivors);
        //      the ring's end at the strait, an Oia, 8 km (130, 185, 316-425, and mid-frame in the night glimpses 540/570, 664);
        //      the low north rim right of the cone, 6-9 km (the M1 vista at 185, 316). The north-east arc hides behind the headland.
        static readonly float[] SiteFrom = { 300f, 236f, 86f };
        static readonly float[] SiteTo = { 346f, 256f, 142f };
        // Oia and Fira read as snow on the crest: dense towns of 120-160 houses each, not a sprinkle of dice (all of them fit:
        // checked offline against the far mesh, 1340 houses, ~59k vertices, no shadows)
        static readonly int[] SiteHouses = { 640, 320, 380 };
        static readonly int[] SiteCores = { 4, 2, 3 };
        const int Seed = 7717;
        const int PlaceAttempts = 16;                   // per house: the tight cores clash more often

        const float FaceShare = 0.65f;                  // houses on the inner face; the rest stand on the rim top, on the skyline
        const float FaceDepthMin = 8f, FaceDepthMax = 45f, FaceDepthScale = 16f;   // m below the crest, density ~ exp(-d / 16 m):
                                                                                     // they hug the crest and silhouette against the sky
        const float RimBehindMax = 22f;                 // further behind the crest a house hides behind it from across the caldera
        const float CoreShare = 0.9f;                   // houses that belong to a village core (the rest are scattered farms)
        const float CoreSigmaMin = 60f, CoreSigmaMax = 130f;   // m along the rim: clustered towns
        const float WhiteShare = 0.74f;                 // pure whitewash; the rest warm/old white and a few Oia colours (backlit groups still read pale)
        const float FacingJitter = 12f;                 // degrees off the direction to the cone
        const float SinkBelow = 2f;                     // walls reach this far below the lowest corner of the ground
        const float MaxStep = 14f;                      // sites where the ground falls more than this under one house are skipped

        // ---- night lights (kind 7 particles: the village lamps' halos) ----
        const float LitShare = 0.62f;                   // houses with a lit window at night
        const float LampDelay = 9f;                     // s after Config.LampsOn the far lights begin to come on (over the halo kind's 14 s spread)
        const float LightNits = 32000f;                 // halo radiance at its centre. At 1.5-8 km the shader grows a halo to a 2.5 px quad
                                                        // at alpha 0.4-0.26 whose (1-r)^2.6 profile leaves ~0.4 of the centre on the
                                                        // brightest pixel, and the night haze takes ~0.3 more: ~3000 nits per light, near
                                                        // the village windows' 3200. At 9000 they landed at ~800 nits, a few times the
                                                        // 150-220 nit night sky and haze: grey specks, invisible in 625.
        const float LightSize = 1.4f;                   // m; the particle shader grows it to at least 2.5 px at any distance
        const float LightOut = 2.5f;                    // m in front of the facade, clear of the wall for the soft depth fade
        const float LightFade = 0.5f;                   // m, soft depth fade of the halo against the scene
        const float FloodMargin = 1.15f;                // a light dies if it is below the local crest height x this (lobes vary it by +-40 %)
        const float FloodCeiling = Config.MaxRunup;     // ... capped like the village's run-up; 1e4 follows the sea shader's uncapped far crest
        const float OutDelayMin = 3f, OutDelaySpread = 2f;     // s after the front reaches it
        const float OutBin = 3f;                        // lights that die within the same 3 s share one particle system
        const float BrownOut = 1.4f;                    // s of flicker before a light dies

        internal struct FarHouse
        {
            public int site;
            public Vector3 c;            // footprint centre; y = floor (highest ground under the footprint)
            public float yaw, w, d, bottom, top;
            public int kind;             // 0 flat roof, 1 vault, 2 blue dome
            public int wall;             // 0 white, 1 warm white, 2 old white, 3 ochre, 4 rose, 5 sand
            public int windows;          // per row on the caldera face
            public bool twoRows;
            public bool lit;
            public Vector3 light;        // halo position (in front of a window)
            public float order;          // 0..1 switch-on order
        }

        sealed class Group { public PSystem sys; public int site; public float off; }

        static readonly List<Group> groups = new List<Group>();
        static readonly List<GraphicsBuffer> buffers = new List<GraphicsBuffer>();
        static bool quitHooked;

        public static void Build(Transform root)
        {
            Release();
            List<FarHouse> plan = Plan();
            var a = GameAssets.I;
            Material wh = a.Mat("M_Whitewash"), warm = a.Mat("M_WhitewashWarm"), old = a.Mat("M_WhitewashOld"), dome = a.Mat("M_DomeBlue");
            Material[] walls = { wh, warm, old, Find("M_FarOchre", warm), Find("M_FarRose", warm), Find("M_FarSand", warm) };
            Material win = Find("M_WindowFar", a.Mat("M_WindowDark"));
            int verts = 0;
            for (int s = 0; s < SiteFrom.Length; s++)
            {
                var mb = new MeshBuilder();
                for (int i = 0; i < plan.Count; i++)
                    if (plan[i].site == s) House(mb, plan[i], walls, dome, win);
                verts += mb.VertexCount;
                mb.Build(root, "FarVillage" + s, false);      // 1.4-9 km away: beyond the shadow cascades, cast nothing
            }
            int lights = BuildLights(root, plan);
            Debug.Log("CASCADE_INFO far villages: " + plan.Count + " houses, " + verts + " vertices, " + lights + " night lights in " + groups.Count + " groups");
        }

        public static void Tick(float dt)
        {
            if (groups.Count == 0) return;
            float te = Story.T(Ev.Eruption), tw = Story.T(Ev.Wave), t = Story.Time;
            float clock = te - Config.LampsOn - LampDelay;
            Color amber = Palette.WindowAmber.linear;
            for (int i = 0; i < groups.Count; i++)
            {
                Group g = groups[i];
                PSystem s = g.sys;
                float left = g.off - tw;                                   // seconds until the wave puts this group out
                s.on = clock > -1f && left > 0f;
                if (!s.on) continue;
                float k = LightNits * (0.86f + 0.14f * MathX.Noise1(t * 0.35f, 31 + g.site));     // each village's supply breathes
                if (left < BrownOut) k *= (Mathf.Repeat(t * 5.3f + i * 0.37f, 1f) < 0.55f ? 1f : 0.2f) * (0.3f + 0.7f * left / BrownOut);
                Color c = amber * k;
                s.clock = t;
                s.a = new Vector4(clock, 1f, 0f, 0f);                      // lamps clock (the halo kind staggers by w over 14 s), halo scale
                s.color = new Vector4(c.r, c.g, c.b, 1f);
                s.size = new Vector4(LightSize, LightSize, 0f, LightFade);
            }
        }

        // ---------------------------------------------------------------- plan (pure; no Unity objects)

        /// <summary>The far houses: positions on the far crest, sizes, variants and night lights. Deterministic.</summary>
        internal static List<FarHouse> Plan()
        {
            var rng = new System.Random(Seed);
            var all = new List<FarHouse>(1400);
            var placed = new List<Vector3>(700);                         // xz centre, z = footprint radius
            Vector2 cone = new Vector2(Config.ConeCenter.x, Config.ConeCenter.z);
            for (int s = 0; s < SiteFrom.Length; s++)
            {
                placed.Clear();
                float from = SiteFrom[s], to = SiteTo[s];
                // the crest along the arc (radial scans of the mesh as drawn)
                int n = Mathf.CeilToInt((to - from) / 0.25f) + 1;
                var crestS = new float[n]; var crestY = new float[n];
                for (int k = 0; k < n; k++) CrestAt(Mathf.Lerp(from, to, k / (float)(n - 1)), out crestS[k], out crestY[k]);
                // village cores along the arc
                int nc = SiteCores[s];
                var coreP = new float[nc]; var coreSig = new float[nc];
                for (int c = 0; c < nc; c++)
                {
                    coreP[c] = Mathf.Lerp(from, to, (c + 0.5f + Rnd(rng, -0.3f, 0.3f)) / nc);
                    coreSig[c] = Rnd(rng, CoreSigmaMin, CoreSigmaMax) / (RingRadius(coreP[c]) * Mathf.Deg2Rad);
                }
                for (int i = 0; i < SiteHouses[s]; i++)
                {
                    for (int attempt = 0; attempt < PlaceAttempts; attempt++)
                    {
                        float phi;
                        if (rng.NextDouble() < CoreShare) { int c = rng.Next(nc); phi = coreP[c] + Gauss(rng) * coreSig[c]; }
                        else phi = Rnd(rng, from, to);
                        if (phi < from || phi > to) continue;
                        float fk = (phi - from) / (to - from) * (n - 1);
                        int k0 = Mathf.Min((int)fk, n - 2);
                        float cs = Mathf.Lerp(crestS[k0], crestS[k0 + 1], fk - k0), cy = Mathf.Lerp(crestY[k0], crestY[k0 + 1], fk - k0);
                        if (cy < 25f) continue;                              // the ring is too low here (the strait, a saddle)
                        float sAt;
                        if (rng.NextDouble() < FaceShare)
                        {
                            float depth = FaceDepthMin - FaceDepthScale * Mathf.Log(1f - (float)rng.NextDouble() * (1f - Mathf.Exp(-(FaceDepthMax - FaceDepthMin) / FaceDepthScale)));
                            float target = cy - depth;
                            sAt = cs;
                            while (sAt > cs - 450f && TerrainBuilder.FarHeight(RingX(phi, sAt), RingZ(phi, sAt)) > target) sAt -= 2f;
                            if (sAt <= cs - 450f) continue;
                        }
                        else sAt = cs + Rnd(rng, 1f, RimBehindMax);
                        var h = new FarHouse { site = s, w = Rnd(rng, 5f, 10f), d = Rnd(rng, 4f, 8f) };
                        float x = RingX(phi, sAt), z = RingZ(phi, sAt);
                        float r = 0.5f * Mathf.Max(h.w, h.d);
                        bool clash = false;
                        for (int q = 0; q < placed.Count && !clash; q++)
                        {
                            float dx = placed[q].x - x, dz = placed[q].y - z, rr = (placed[q].z + r) * 0.9f;
                            clash = dx * dx + dz * dz < rr * rr;
                        }
                        if (clash) continue;
                        // face the cone, give or take
                        Vector2 toCone = (cone - new Vector2(x, z)).normalized;
                        h.yaw = Mathf.Atan2(toCone.x, toCone.y) * Mathf.Rad2Deg + Rnd(rng, -FacingJitter, FacingJitter);
                        Vector3 F, S;
                        Frame(h.yaw, out F, out S);
                        float hi = -1e9f, lo = 1e9f;
                        for (int cx = -1; cx <= 1; cx += 2)
                            for (int cz = -1; cz <= 1; cz += 2)
                            {
                                Vector3 p = new Vector3(x, 0f, z) + S * (cx * h.w * 0.5f) + F * (cz * h.d * 0.5f);
                                float g = TerrainBuilder.FarHeight(p.x, p.z);
                                hi = Mathf.Max(hi, g); lo = Mathf.Min(lo, g);
                            }
                        if (lo < 3f || hi - lo > MaxStep) continue;              // the shore, or too steep a spot
                        float height = Rnd(rng, 3.2f, 6.5f);
                        h.c = new Vector3(x, hi, z);
                        h.bottom = lo - SinkBelow;
                        h.top = hi + height;
                        double v = rng.NextDouble();
                        h.kind = v < 0.2 && h.w < 9.5f ? 1 : v < 0.25 ? 2 : 0;
                        double cw = rng.NextDouble();
                        h.wall = cw < WhiteShare ? 0 : cw < 0.83 ? 1 : cw < 0.91 ? 2 : cw < 0.95 ? 3 : cw < 0.98 ? 4 : 5;
                        h.windows = h.w < 6.5f ? 1 : h.w < 8.5f ? 2 : 3;
                        h.twoRows = h.top - lo > 8.5f;                           // a tall front over the slope has a lower storey
                        // at night: one window lit, its halo a little in front of the glass
                        h.lit = rng.NextDouble() < LitShare;
                        int wk = rng.Next(h.windows);
                        float ws = ((wk + 0.5f) / h.windows - 0.5f) * h.w * 0.78f;
                        h.light = new Vector3(x, h.top - 1.7f, z) + S * ws + F * (h.d * 0.5f + LightOut);
                        h.order = (float)rng.NextDouble();
                        placed.Add(new Vector3(x, z, r));
                        all.Add(h);
                        break;
                    }
                }
            }
            return all;
        }

        /// <summary>The crest of the far ring along a radial line at angle phi: offset s from the shoreline ellipse and height.</summary>
        static void CrestAt(float phi, out float bestS, out float bestY)
        {
            bestS = 300f; bestY = -1e9f;
            float shore = 1e9f;
            for (float s = -500f; s <= 1000f; s += 10f)
            {
                float y = TerrainBuilder.FarHeight(RingX(phi, s), RingZ(phi, s));
                if (shore > 1e8f) { if (y > 2f) shore = s; else continue; }
                if (s > shore + 480f) break;                                 // past the crest: the outer slope's bumps do not count
                if (y > bestY) { bestY = y; bestS = s; }
            }
            for (float s = bestS - 10f; s <= bestS + 10f; s += 2f)
            {
                float y = TerrainBuilder.FarHeight(RingX(phi, s), RingZ(phi, s));
                if (y > bestY) { bestY = y; bestS = s; }
            }
        }

        // a point s metres outward from the caldera shoreline ellipse at angle phi (degrees)
        static float RingX(float phi, float s) { return Config.ConeCenter.x + (Config.CalderaA + s) * Mathf.Cos(phi * Mathf.Deg2Rad); }
        static float RingZ(float phi, float s) { return Config.ConeCenter.z + (Config.CalderaB + s) * Mathf.Sin(phi * Mathf.Deg2Rad); }
        static float RingRadius(float phi)
        {
            float a = Config.CalderaA + 300f, b = Config.CalderaB + 300f, r = phi * Mathf.Deg2Rad;
            return Mathf.Sqrt(a * a * Mathf.Sin(r) * Mathf.Sin(r) + b * b * Mathf.Cos(r) * Mathf.Cos(r));
        }

        /// <summary>Facade normal F and side axis S of a house turned by yaw degrees (what Quaternion.Euler(0, yaw, 0) gives).</summary>
        static void Frame(float yaw, out Vector3 F, out Vector3 S)
        {
            float r = yaw * Mathf.Deg2Rad, sn = Mathf.Sin(r), cs = Mathf.Cos(r);
            F = new Vector3(sn, 0f, cs); S = new Vector3(cs, 0f, -sn);
        }

        static float Rnd(System.Random r, float a, float b) { return a + (b - a) * (float)r.NextDouble(); }
        static float Gauss(System.Random r)
        {
            double u1 = 1.0 - r.NextDouble(), u2 = r.NextDouble();
            return (float)(System.Math.Sqrt(-2.0 * System.Math.Log(u1)) * System.Math.Cos(2.0 * System.Math.PI * u2));
        }

        /// <summary>Wave clock at which a light dies (or 1e9 if the wave never climbs to it): the CPU mirror of the sea
        /// shader's crest (WaveHeight x the main-lobe factor) at the moment the front reaches it, capped at the run-up
        /// limit the village obeys (outside the terrain bake the shader does not cap it, so the far crest can top 300 m).</summary>
        internal static float OffTime(Vector3 p, float jitter01)
        {
            Vector2 xz = new Vector2(p.x, p.z);
            float arrive = Sim.WaveArrival(xz);
            Vector2 d = xz - Sim.WaveCentre(arrive);
            float cosT = Vector2.Dot(d.normalized, Sim.WaveAimDir);
            float crest = Sim.WaveHeight(arrive) * Mathf.Lerp(0.3f, 1f, MathX.Smooth(0.2f, 0.86f, cosT));
            return p.y < Mathf.Min(crest, FloodCeiling) * FloodMargin ? arrive + OutDelayMin + OutDelaySpread * jitter01 : 1e9f;
        }

        // ---------------------------------------------------------------- geometry

        static void House(MeshBuilder mb, FarHouse h, Material[] walls, Material dome, Material win)
        {
            Quaternion rot = Quaternion.Euler(0f, h.yaw, 0f);
            Vector3 F, S, up = Vector3.up;
            Frame(h.yaw, out F, out S);
            Material wall = walls[Mathf.Clamp(h.wall, 0, walls.Length - 1)];
            mb.Box(wall, new Vector3(h.c.x, 0.5f * (h.top + h.bottom), h.c.z), new Vector3(h.w, h.top - h.bottom, h.d), rot, 8);   // no bottom face
            Vector3 roof = new Vector3(h.c.x, h.top, h.c.z);
            if (h.kind == 1) Vault(mb, wall, roof, S, F, h.w, h.d);
            else if (h.kind == 2) mb.Dome(dome, roof, Mathf.Min(h.w, h.d) * 0.3f, 1f, 8, 3);
            // dark windows on the caldera face: by day the speckle that makes white boxes read as a village
            Vector3 face = roof + F * (h.d * 0.5f + 0.02f);
            int rows = h.twoRows ? 2 : 1;
            for (int row = 0; row < rows; row++)
                for (int k = 0; k < h.windows; k++)
                {
                    float s = ((k + 0.5f) / h.windows - 0.5f) * h.w * 0.78f;
                    mb.Face(win, face + S * s + up * (-1.7f - 3f * row), S * 0.8f, up * 0.7f);
                }
        }

        /// <summary>Barrel vault over the roof, its axis running front to back (the arch shows on the facade), six facets.</summary>
        static void Vault(MeshBuilder mb, Material m, Vector3 o, Vector3 S, Vector3 F, float w, float d)
        {
            const int seg = 6;
            float hw = w * 0.5f, rise = Mathf.Min(hw, 2.6f) * 0.62f;
            Vector3 f = F * (d * 0.5f), prev = o + S * hw;
            for (int k = 1; k <= seg; k++)
            {
                float ang = Mathf.PI * k / seg;
                Vector3 p = o + S * (Mathf.Cos(ang) * hw) + Vector3.up * (Mathf.Sin(ang) * rise);
                mb.Quad(m, prev - f, p - f, p + f, prev + f);
                mb.Tri(m, o + f, prev + f, p + f);                       // front gable
                mb.Tri(m, o - f, p - f, prev - f);                       // back gable
                prev = p;
            }
        }

        // ---------------------------------------------------------------- night lights

        static int BuildLights(Transform root, List<FarHouse> plan)
        {
            List<PSystem> list = ParticleList(root);
            if (list == null) { Debug.LogWarning("CASCADE_WARN far villages: no particle pass, the far lights are off"); return 0; }
            // group by site and by the moment the wave puts them out (survivors: one group per site)
            var keys = new List<long>();
            var lists = new List<List<Vector4>>();
            var offs = new List<float>();
            var sites = new List<int>();
            int count = 0;
            for (int i = 0; i < plan.Count; i++)
            {
                FarHouse h = plan[i];
                if (!h.lit) continue;
                float off = OffTime(h.light, MathX.Hash(i, 977));
                long bin = off > 1e8f ? -1 : Mathf.FloorToInt(off / OutBin);
                long key = h.site * 100000L + bin;
                int gi = keys.IndexOf(key);
                if (gi < 0)
                {
                    gi = keys.Count; keys.Add(key); lists.Add(new List<Vector4>());
                    offs.Add(off > 1e8f ? 1e9f : (bin + 1) * OutBin); sites.Add(h.site);
                }
                lists[gi].Add(new Vector4(h.light.x, h.light.y, h.light.z, h.order));
                count++;
            }
            var mine = new List<PSystem>(lists.Count);
            for (int g = 0; g < lists.Count; g++)
            {
                Vector4[] data = lists[g].ToArray();
                var buf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, data.Length, 16);
                buf.SetData(data);
                buffers.Add(buf);
                var s = new PSystem { name = "farLights" + g, kind = 7, count = data.Length, emitters = data.Length, bufA = buf, on = false };
                mine.Add(s);
                groups.Add(new Group { sys = s, site = sites[g], off = offs[g] });
            }
            // right after the village halos: additive, drawn before the near ash flakes like them
            int at = list.FindIndex(x => x.name == "halos");
            list.InsertRange(at >= 0 ? at + 1 : list.Count, mine);
            if (!quitHooked) { Application.quitting += Release; quitHooked = true; }
            return count;
        }

        /// <summary>The shared list of systems the particle pass draws (FarVillages adds its own kind-7 systems to it,
        /// so ParticleDirector.SetLamps, which replaces the village halos' buffer, is left alone).</summary>
        static List<PSystem> ParticleList(Transform root)
        {
            foreach (var v in root.GetComponentsInChildren<CustomPassVolume>(true))
                foreach (var p in v.customPasses)
                    if (p is ParticlesPass pp && pp.systems != null) return pp.systems;
            return null;
        }

        static void Release()
        {
            for (int i = 0; i < buffers.Count; i++) buffers[i].Dispose();
            buffers.Clear();
            groups.Clear();
        }

        /// <summary>A material by name without the registry's missing-material error (new materials appear once the
        /// lead regenerates the assets; until then the fallback stands in).</summary>
        static Material Find(string name, Material fallback)
        {
            Material[] ms = GameAssets.I != null ? GameAssets.I.materials : null;
            if (ms != null)
                for (int i = 0; i < ms.Length; i++)
                    if (ms[i] != null && ms[i].name == name) return ms[i];
            return fallback;
        }
    }
}
