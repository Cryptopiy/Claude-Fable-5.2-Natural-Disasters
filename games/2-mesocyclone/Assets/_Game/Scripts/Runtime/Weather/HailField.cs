using UnityEngine;
using UnityEngine.Rendering;

namespace MESOCYCLONE
{
    /// <summary>Beat 4: the hail as real 3D ice. Lumpy lit stones (HDRP/Lit, clear-coated ice) drawn with GPU instancing,
    /// plus the ice chips thrown where they strike. The kinematics are those of the former VFX hail (HailHlsl in
    /// VfxGenWeather), stone for stone: same hashes, periods, fall velocity with wind, impact points on the road and
    /// the hood, bounce and life, size from s.hailSize and count from s.hail, driven by Director.Ambient. The splash
    /// VFX uses the same hashes, so every water crown sits under a stone. Truck-local, no allocation per frame.</summary>
    [DefaultExecutionOrder(-70)]
    public sealed class HailField : MonoBehaviour
    {
        public const int CountTest = 500, CountFinal = 1200;
        public const float FallT = 0.5f;                     // phase of the impact within a stone's cycle (HailHlsl)
        const int Shapes = IceMesh.ShapeCount;

        /// <summary>The shared ice material instance (its emissive "inner glow" follows the ambient light each frame).</summary>
        public static Material IceMaterial;
        /// <summary>A copy of the ice for the stones right in front of the truck (hood, glass, the air ahead; and the
        /// chips and hero stones). They sit in the light the beams scatter off the rain and the hail, yet are mostly
        /// outside the low-beam cone, so with the dim glow of IceMaterial they showed their unlit side as black discs
        /// over the bright pool. Same shader and keywords as IceMaterial, so no new variant.</summary>
        public static Material IceNear;
        /// <summary>Scale on s.hail (tune "hail.density"); the splash VFX uses it too.</summary>
        public static float Density = 1f;

        static readonly int idEmissive = Shader.PropertyToID("_EmissiveColor");

        int n;
        // per-stone constants, from HailHash(i * 7 + 1..6) exactly as the GPU hail did
        float[] period, phase, thr, sizeF, fallVy, omega, kick, giant;
        Vector3[] ip, vbHood, axis;
        float[] vbRoadX, vbRoadY;
        bool[] isHood;

        Mesh[] lod0, lod1, chipMesh;
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        public struct InstData { public Matrix4x4 objectToWorld; public Matrix4x4 prevObjectToWorld; }
        InstData[][] m0, m1, mc, m0n;                    // m0n: near-band stones (IceNear)

        /// <summary>Where an instance stood a frame ago (translation only): the stones move tens of metres per second
        /// across the screen, and without this TAA smeared them into pale soft ovals.</summary>
        public static Matrix4x4 Prev(Matrix4x4 m, Matrix4x4 l2w, Vector3 velTruck)
        {
            Vector3 d = l2w.MultiplyVector(velTruck * Director.FrameDt);
            m.m03 -= d.x; m.m13 -= d.y; m.m23 -= d.z;
            return m;
        }

        static void Put(InstData[] a, ref int n, Matrix4x4 m, Matrix4x4 prev)
        {
            a[n].objectToWorld = m; a[n].prevObjectToWorld = prev; n++;
        }
        int[] n0, n1, nc, n0n;
        int cap, chipCap;
        RenderParams rp, rpNear;

        // tuning, read once
        float shutter, stretchMax, minPx, lodDist, glowK, chipBase, chipDist, glowNear, nearHalfW;
        bool glance;

        // impacts of the hero stones (HeroHail), queued earlier in the same frame
        struct Impact { public Vector3 pos, normal; public float since, size; public int seed; }
        static readonly Impact[] queue = new Impact[16];
        static int queueCount, queueFrame = -1;

        public static HailField Build(Transform truck)
        {
            var go = new GameObject("HailField");
            go.transform.SetParent(truck, false);
            var h = go.AddComponent<HailField>();
            h.Init();
            return h;
        }

        void Init()
        {
            n = Cli.Final ? CountFinal : CountTest;
            shutter = Tune.F("hail.shutter", 0.0015f);          // seconds of fake motion blur (stretch along the motion)
            stretchMax = Tune.F("hail.stretchMax", 1.8f);
            minPx = Tune.F("hail.minPx", 0.0012f);              // far stones never shrink below about one pixel
            lodDist = Tune.F("hail.lod", 14f);
            glowK = Tune.F("hail.glow", 0.12f);
            Density = Tune.F("hail.density", 1f);
            chipBase = Tune.F("hail.chips", Cli.Final ? 4f : 2f);
            chipDist = Tune.F("hail.chipDist", Cli.Final ? 32f : 22f);
            glance = Tune.F("hail.glance", 1f) > 0.5f;           // hood stones glance off the windscreen (0: vanish at the cowl as before)
            // near band: the beam-scatter glow of the stones in front of the truck, in nits per nit of road spill
            // (0.6-1.1: lower if they turn into flat glowing coins, higher if dark discs come back over the pool)
            glowNear = Tune.F("hail.glowNear", 0.3f);      // 0.85 turned the near stones into flat glowing coins
            nearHalfW = Tune.F("hail.nearHalfWidth", 5f);        // |x| in metres: the hood, the glass and the air ahead

            period = new float[n]; phase = new float[n]; thr = new float[n]; sizeF = new float[n]; fallVy = new float[n];
            omega = new float[n]; kick = new float[n]; giant = new float[n];
            ip = new Vector3[n]; vbHood = new Vector3[n]; axis = new Vector3[n];
            vbRoadX = new float[n]; vbRoadY = new float[n]; isHood = new bool[n];
            for (int i = 0; i < n; i++)
            {
                uint id = (uint)i;
                float u0 = WeatherLibrary.HailHash(id * 7u + 1u), u1 = WeatherLibrary.HailHash(id * 7u + 2u), u2 = WeatherLibrary.HailHash(id * 7u + 3u);
                float u3 = WeatherLibrary.HailHash(id * 7u + 4u), u4 = WeatherLibrary.HailHash(id * 7u + 5u), u5 = WeatherLibrary.HailHash(id * 7u + 6u);
                period[i] = 0.8f + 0.7f * u0;
                phase[i] = u1;
                thr[i] = u0 * 0.97f + u2 * 0.03f;
                isHood[i] = u2 <= 0.14f;
                float hx = (u3 - 0.5f) * 1.62f, hz = 0.92f + u4 * 1.8f;
                // impact point on the surface (the stone centre is lifted by its radius each frame)
                ip[i] = isHood[i] ? new Vector3(hx, CabDims.HoodY(hx, hz), hz) : new Vector3((u3 - 0.5f) * 20f, 0f, 3.2f + u4 * u4 * 46f);
                fallVy[i] = -27f - 9f * u5;
                vbRoadX[i] = (u1 - 0.5f) * 7f; vbRoadY[i] = 3.5f + 5.5f * u0;
                vbHood[i] = new Vector3((u1 - 0.5f) * 5f, 2.5f + 3.5f * u0, -2f - 6f * u5);
                sizeF[i] = 0.55f + 0.9f * u5 * u5;
                // a few stones well above the size of the moment: the rare giants of the peak
                giant[i] = WeatherLibrary.HailHash(id * 7u + 7u) < 0.035f ? 1.35f : 1f;
                uint h = WeatherLibrary.Hash(id * 2654435761u + 17u);
                axis[i] = WeatherLibrary.Dir(h);
                omega[i] = 3f + 9f * WeatherLibrary.U(h, 3);       // tumbling as they fall, rad/s
                kick[i] = 12f + 22f * WeatherLibrary.U(h, 4);      // extra spin from the strike
            }

            lod0 = new Mesh[Shapes]; lod1 = new Mesh[Shapes];
            for (int s = 0; s < Shapes; s++)
            {
                lod0[s] = IceMesh.Stone(s, 2, "Hail" + s + "_L0");     // 320 triangles
                lod1[s] = IceMesh.Stone(s, 1, "Hail" + s + "_L1");     // 80 triangles, far stones
            }
            chipMesh = new[] { IceMesh.Chip(901u, false, "IceShard"), IceMesh.Chip(902u, true, "IceFlake") };

            cap = n / Shapes + 1;
            chipCap = Cli.Final ? 600 : 220;
            m0 = new InstData[Shapes][]; m1 = new InstData[Shapes][]; m0n = new InstData[Shapes][];
            for (int s = 0; s < Shapes; s++) { m0[s] = new InstData[cap]; m1[s] = new InstData[cap]; m0n[s] = new InstData[cap]; }
            mc = new[] { new InstData[chipCap], new InstData[chipCap] };
            n0 = new int[Shapes]; n1 = new int[Shapes]; nc = new int[2]; n0n = new int[Shapes];

            Material src = WeatherLibrary.Mat(WeatherLibrary.IceMat, "hailstone");
            IceMaterial = src != null ? new Material(src) { name = "hail_ice_live", enableInstancing = true } : null;
            rp = MakeParams(IceMaterial, true);
            // a plain copy of the same material: same keywords (clear coat), only its glow differs
            IceNear = src != null ? new Material(src) { name = "hail_ice_near", enableInstancing = true } : null;
            rpNear = MakeParams(IceNear != null ? IceNear : IceMaterial, true);
        }

        /// <summary>Instanced lit drawing near the truck: no shadows, ambient probe from the sky. objectMotion: the
        /// caller passes last frame's matrices (InstData / RenderMesh prev), else camera motion vectors only.</summary>
        public static RenderParams MakeParams(Material m, bool objectMotion = false)
        {
            var p = new RenderParams(m);
            p.layer = 0;
            p.shadowCastingMode = ShadowCastingMode.Off;
            p.receiveShadows = false;
            p.lightProbeUsage = LightProbeUsage.Off;            // "off" = the scene ambient probe (HDRP's sky) is used
            p.motionVectorMode = objectMotion ? MotionVectorGenerationMode.Object : MotionVectorGenerationMode.Camera;
            p.worldBounds = new Bounds(new Vector3(0f, 10f, 20f), new Vector3(180f, 90f, 200f));   // the truck stays at the origin
            return p;
        }

        /// <summary>HeroHail reports its strikes here (same frame, earlier execution order) so they throw chips too.</summary>
        public static void QueueImpact(Vector3 pos, Vector3 normal, float since, float size, int seed)
        {
            if (queueFrame != Time.frameCount) { queueFrame = Time.frameCount; queueCount = 0; }
            if (queueCount >= queue.Length) return;
            queue[queueCount++] = new Impact { pos = pos, normal = normal, since = since, size = size, seed = seed };
        }

        void LateUpdate()
        {
            for (int s = 0; s < Shapes; s++) { n0[s] = 0; n1[s] = 0; n0n[s] = 0; }
            nc[0] = 0; nc[1] = 0;
            if (!Director.Running || IceMaterial == null) return;

            StoryState st = Director.S;
            Transform truck = transform.parent;
            Matrix4x4 l2w = truck.localToWorldMatrix;
            Vector3 cam = Bootstrap.Cam != null ? truck.InverseTransformPoint(Bootstrap.Cam.transform.position) : CabDims.Eye;
            float t = Director.Ambient, speed = Director.Speed;

            // the same wind as the GPU hail: H1 = (wind.x * 0.5, 0, wind.z * 0.3) in the truck frame
            float wd = st.windDir * Mathf.Deg2Rad;
            Vector3 windLocal = truck.InverseTransformDirection(new Vector3(Mathf.Sin(wd), 0f, Mathf.Cos(wd)) * st.wind);
            float windX = windLocal.x * 0.5f, windZ = windLocal.z * 0.3f;

            float hail = st.hail * Density;
            if (hail > 0.005f && st.hailSize > 0.0005f) Stones(t, speed, windX, windZ, st.hailSize, hail, cam, l2w);
            if (queueFrame == Time.frameCount) for (int q = 0; q < queueCount; q++) HeroChips(queue[q], speed, l2w);
            queueCount = 0;

            int drawn = 0;
            for (int s = 0; s < Shapes; s++) drawn += n0[s] + n1[s] + n0n[s];
            drawn += nc[0] + nc[1];
            if (drawn == 0) return;

            // inner glow: the milky ice scatters the light around it (sky, road spill of the headlights) so a stone is
            // never a black hole against the sky; the real lights (headlights, flash, dash) do the shading on top.
            // Cool white, not the old warm tint: warm ice next to the neutral headlight pool read as cream lumps.
            float day = Mathf.Pow(10f, Mathf.Min(0f, st.sunElev) / 2.6f) * Tune.F("rain.dayNits", 40f) + 0.004f;
            float spill = Tune.F("rain.spillNits", 2.4f) * TruckLights.Level;
            float g = glowK * 1.5f;
            float a = (day * 0.9f + spill) * g;                  // about 0.43 nits at night, as before
            IceMaterial.SetColor(idEmissive, new Color(a * 0.95f, a * 0.98f, a, 1f));
            if (IceNear != null)
            {
                // the near band: the beams' backscatter off the rain and hail wraps the stones in front of the truck,
                // about 2 nits at full headlights (between the rain streaks' 2.4-nit ambient and the old sprite
                // hail's 3.6), so they read as pale ice over the pool instead of black discs
                float an = day * 0.9f * g + spill * glowNear;
                IceNear.SetColor(idEmissive, new Color(an * 0.95f, an * 0.98f, an, 1f));
            }

            for (int s = 0; s < Shapes; s++)
            {
                if (n0[s] > 0) Graphics.RenderMeshInstanced(rp, lod0[s], 0, m0[s], n0[s]);
                if (n0n[s] > 0) Graphics.RenderMeshInstanced(rpNear, lod0[s], 0, m0n[s], n0n[s]);
                if (n1[s] > 0) Graphics.RenderMeshInstanced(rp, lod1[s], 0, m1[s], n1[s]);
            }
            // the chips fly where the stones strike, on the hood and the road ahead: the near glow too
            for (int c = 0; c < 2; c++)
                if (nc[c] > 0) Graphics.RenderMeshInstanced(rpNear, chipMesh[c], 0, mc[c], nc[c]);
        }

        void Stones(float t, float speed, float windX, float windZ, float hailSize, float hail, Vector3 cam, Matrix4x4 l2w)
        {
            float lod2 = lodDist * lodDist, chip2 = chipDist * chipDist;
            bool big = hailSize > 0.09f;
            for (int i = 0; i < n; i++)
            {
                if (thr[i] > hail) continue;
                float per = period[i];
                float cyc = t / per + phase[i];
                float ph = cyc - Mathf.Floor(cyc);
                float d = hailSize * sizeF[i] * (big ? giant[i] : 1f);
                float r = d * 0.5f;
                Vector3 ipc = ip[i]; ipc.y += r;
                Vector3 p, vel;
                float spinExtra = 0f;
                if (ph < FallT)
                {
                    float tt = (FallT - ph) * per;
                    vel = new Vector3(windX, fallVy[i], -speed + windZ);
                    p = ipc - vel * tt;
                }
                else
                {
                    float tt = (ph - FallT) * per;
                    // chips first: they fly even when the stone itself is gone
                    if (tt < 0.55f && d >= 0.012f)
                    {
                        float cx = ip[i].x - cam.x, cy = ip[i].y - cam.y, cz = ip[i].z - cam.z;
                        if (cx * cx + cy * cy + cz * cz < chip2) Chips(i, cyc, ip[i], isHood[i], tt, d, speed, windX, windZ, l2w);
                    }
                    if (isHood[i])
                    {
                        if (glance) { if (!IceFlight.Fly(ipc, vbHood[i], tt, r, out p, out vel)) continue; }
                        else
                        {   // the old rule: gone at the cowl line
                            p = ipc + vbHood[i] * tt; p.y -= 4.9f * tt * tt;
                            vel = vbHood[i]; vel.y -= 9.8f * tt;
                            if (p.z < 0.86f) continue;
                        }
                        if (IceFlight.OverHood(p))
                        {   // came down on the hood again: it skids along the paint (until the cowl hides it) rather than popping
                            float hy = CabDims.HoodY(p.x, p.z) + r;
                            if (p.y < hy)
                            {
                                if (p.z < CabDims.HoodZ0 + 0.05f) continue;
                                p.y = hy; vel.y = 0f;
                            }
                        }
                        else if (p.y < 0.6f) continue;                                                      // off the hood, below the wing line
                    }
                    else
                    {
                        Vector3 vb = new Vector3(vbRoadX[i], vbRoadY[i], -speed * 0.8f);
                        p = ipc + vb * tt; p.y -= 4.9f * tt * tt;
                        vel = vb; vel.y -= 9.8f * tt;
                        if (p.z < 3f) continue;                                    // under the hood line
                        if (p.y < ipc.y) { p.y = ipc.y; vel.y = 0f; }              // back on the road: it skids with it, no pop
                    }
                    spinExtra = kick[i] * tt;
                }

                float dx = p.x - cam.x, dy = p.y - cam.y, dz = p.z - cam.z;
                float dist2 = dx * dx + dy * dy + dz * dz;
                float dist = Mathf.Sqrt(dist2);
                float fade = WeatherLibrary.Smooth(0.35f, 0.8f, dist);
                if (fade <= 0.001f) continue;
                float dEff = Mathf.Max(d, dist * minPx) * fade;
                float sp = vel.magnitude;
                float k = 1f + Mathf.Min(sp * shutter / dEff, stretchMax - 1f);
                // the cycle wraps a stone from mid-bounce back to the sky: grow in at the top and shrink out in the
                // last frames instead of popping (the old sprites popped)
                float life = WeatherLibrary.Smooth(0f, 0.05f, ph) * WeatherLibrary.Smooth(1f, 0.9f, ph);
                if (life <= 0.001f) continue;
                Vector3 vdir = sp > 0.01f ? vel / sp : Vector3.down;
                Matrix4x4 m = l2w * Local(axis[i], omega[i] * t + spinExtra, vdir, k, dEff * life, p);
                Matrix4x4 mp = Prev(m, l2w, vel);
                int sh = i % Shapes;
                // close stones in front of the truck (|x| < nearHalfW) get the near glow; the side stones stay dim
                // (only what the headlights light). Stones barely drift sideways, so none crosses the band edge in
                // flight, and at the 14 m LOD edge they are 2-3 px inside the beam: no visible step there.
                if (dist2 < lod2)
                {
                    if (Mathf.Abs(p.x) < nearHalfW) { if (n0n[sh] < cap) Put(m0n[sh], ref n0n[sh], m, mp); }
                    else if (n0[sh] < cap) Put(m0[sh], ref n0[sh], m, mp);
                }
                else if (n1[sh] < cap) Put(m1[sh], ref n1[sh], m, mp);
            }
        }

        /// <summary>Ice chips of stone i's current strike: shards and flakes in a burst; on the road they keep the
        /// road's motion relative to the truck, on the hood the head wind pushes them back.</summary>
        void Chips(int i, float cyc, Vector3 at, bool hood, float tt, float d, float speed, float windX, float windZ, Matrix4x4 l2w)
        {
            float big = Mathf.Clamp01((d - 0.015f) / 0.07f);
            int cnt = Mathf.Min(8, Mathf.RoundToInt(chipBase * (0.4f + big)));
            uint cycle = (uint)Mathf.FloorToInt(cyc);
            for (int j = 0; j < cnt; j++)
            {
                uint h;
                unchecked { h = WeatherLibrary.Hash((uint)i * 0x9E3779B1u ^ cycle * 0x85EBCA77u ^ (uint)j * 0xC2B2AE3Du); }
                float life = 0.3f + 0.25f * WeatherLibrary.U(h, 1);
                if (tt > life) continue;
                float a = WeatherLibrary.U(h, 2) * Mathf.PI * 2f;
                float vh = (0.8f + 2.6f * WeatherLibrary.U(h, 3)) * (0.6f + 0.8f * big);
                float vy = (0.8f + 2.8f * WeatherLibrary.U(h, 4)) * (0.6f + 0.6f * big);
                Vector3 v0 = hood
                    ? new Vector3(Mathf.Cos(a) * vh * 0.7f, vy * 0.7f, Mathf.Sin(a) * vh * 0.7f - 1.5f)
                    : new Vector3(Mathf.Cos(a) * vh + windX, vy, Mathf.Sin(a) * vh - speed + windZ);
                Vector3 p = at + v0 * tt; p.y += 0.01f - 4.9f * tt * tt;
                if (hood)
                {
                    if (IceFlight.OverHood(p) && p.y < CabDims.HoodY(p.x, p.z)) continue;
                    if (p.z < CabDims.HoodZ0) continue;                       // at the cowl: hidden by the dash
                }
                else if (p.y < 0f) continue;
                float size = d * (0.12f + 0.2f * WeatherLibrary.U(h, 5)) * (1f + 0.3f * big);
                float ang = (18f + 22f * WeatherLibrary.U(h, 6)) * tt + WeatherLibrary.U(h, 7) * 6.2832f;
                int c = (int)(h & 1u);
                if (nc[c] < chipCap)
                {
                    Matrix4x4 m = l2w * Local(WeatherLibrary.Dir(h), ang, Vector3.up, 1f, size, p);
                    Put(mc[c], ref nc[c], m, Prev(m, l2w, v0 + new Vector3(0f, -9.8f * tt, 0f)));
                }
            }
        }

        void HeroChips(in Impact im, float speed, Matrix4x4 l2w)
        {
            if (im.since > 0.6f) return;
            Vector3 nrm = im.normal;
            Vector3 t1 = Vector3.Cross(nrm, Vector3.right).sqrMagnitude > 0.01f ? Vector3.Cross(nrm, Vector3.right).normalized : Vector3.forward;
            Vector3 t2 = Vector3.Cross(nrm, t1);
            float tt = im.since;
            for (int j = 0; j < 9; j++)
            {
                uint h;
                unchecked { h = WeatherLibrary.Hash((uint)im.seed * 0x9E3779B1u + (uint)j * 0x632BE5ABu); }
                float life = 0.35f + 0.25f * WeatherLibrary.U(h, 1);
                if (tt > life) continue;
                float a = WeatherLibrary.U(h, 2) * Mathf.PI * 2f;
                float vt = 1.2f + 2.8f * WeatherLibrary.U(h, 3), vn = 1.2f + 2.6f * WeatherLibrary.U(h, 4);
                Vector3 v0 = (t1 * Mathf.Cos(a) + t2 * Mathf.Sin(a)) * vt + nrm * vn + new Vector3(0f, 0f, -1.5f);
                Vector3 p = im.pos + nrm * 0.01f + v0 * tt; p.y -= 4.9f * tt * tt;
                if (IceFlight.OverHood(p) && p.y < CabDims.HoodY(p.x, p.z)) continue;
                if (Vector3.Dot(p - IceFlight.GlassOrigin, WeatherLibrary.GlassOut) < 0f) continue;
                float size = im.size * (0.12f + 0.22f * WeatherLibrary.U(h, 5));
                float ang = (16f + 20f * WeatherLibrary.U(h, 6)) * tt + WeatherLibrary.U(h, 7) * 6.2832f;
                int c = (int)(h & 1u);
                if (nc[c] < chipCap)
                {
                    Matrix4x4 m = l2w * Local(WeatherLibrary.Dir(h), ang, Vector3.up, 1f, size, p);
                    Put(mc[c], ref nc[c], m, Prev(m, l2w, v0 + new Vector3(0f, -9.8f * tt, 0f)));
                }
            }
        }

        /// <summary>Object-to-truck matrix: rotation by angle about axis a (Rodrigues), then a stretch by k along the
        /// unit motion direction v (the shutter streak), uniform scale, translation p.</summary>
        public static Matrix4x4 Local(Vector3 a, float ang, Vector3 v, float k, float scale, Vector3 p)
        {
            float c = Mathf.Cos(ang), s = Mathf.Sin(ang), C = 1f - c;
            float r00 = c + a.x * a.x * C, r01 = a.x * a.y * C - a.z * s, r02 = a.x * a.z * C + a.y * s;
            float r10 = a.y * a.x * C + a.z * s, r11 = c + a.y * a.y * C, r12 = a.y * a.z * C - a.x * s;
            float r20 = a.z * a.x * C - a.y * s, r21 = a.z * a.y * C + a.x * s, r22 = c + a.z * a.z * C;
            float e = k - 1f;
            // M = (I + e v vT) R: w = vT R
            float w0 = v.x * r00 + v.y * r10 + v.z * r20, w1 = v.x * r01 + v.y * r11 + v.z * r21, w2 = v.x * r02 + v.y * r12 + v.z * r22;
            Matrix4x4 m;
            m.m00 = (r00 + e * v.x * w0) * scale; m.m01 = (r01 + e * v.x * w1) * scale; m.m02 = (r02 + e * v.x * w2) * scale; m.m03 = p.x;
            m.m10 = (r10 + e * v.y * w0) * scale; m.m11 = (r11 + e * v.y * w1) * scale; m.m12 = (r12 + e * v.y * w2) * scale; m.m13 = p.y;
            m.m20 = (r20 + e * v.z * w0) * scale; m.m21 = (r21 + e * v.z * w1) * scale; m.m22 = (r22 + e * v.z * w2) * scale; m.m23 = p.z;
            m.m30 = 0f; m.m31 = 0f; m.m32 = 0f; m.m33 = 1f;
            return m;
        }

        void OnDestroy()
        {
            if (IceMaterial != null) Destroy(IceMaterial);
            IceMaterial = null;
            if (IceNear != null) Destroy(IceNear);
            IceNear = null;
        }
    }

    /// <summary>Ballistic flight of an ice stone near the cab (truck frame), with one glancing bounce off the windscreen.
    /// The old sprites vanished at the cowl line in mid-air; real stones glance off the glass and fly over the roof.</summary>
    public static class IceFlight
    {
        public static readonly Vector3 GlassOrigin = new Vector3(0f, CabDims.WBL.y, CabDims.WBL.z);
        const float Restitution = 0.3f, Slide = 0.75f;

        public static bool OverHood(Vector3 p)
        {
            return p.z > CabDims.HoodZ0 && p.z < CabDims.HoodZ1 && Mathf.Abs(p.x) < CabDims.HoodHalfWidth;
        }

        /// <summary>Position and velocity t seconds after leaving p0 with v0, for a stone of radius r. False when the stone
        /// is hidden for good: it would strike below the glass (cowl, behind the dash), above it (the roof hides it),
        /// went over the roof after the glance, or fell back against the glass.</summary>
        public static bool Fly(Vector3 p0, Vector3 v0, float t, float r, out Vector3 p, out Vector3 v)
        {
            Vector3 n = WeatherLibrary.GlassOut;
            p = p0 + v0 * t; p.y -= 4.9f * t * t;
            v = v0; v.y -= 9.8f * t;
            float g0 = Vector3.Dot(p0 - GlassOrigin, n) - r;
            if (g0 <= 0f) return false;
            // contact when dot(p(t) - G, n) = r: a quadratic with a < 0 and a positive constant: one positive root
            float a = -4.9f * n.y, b = Vector3.Dot(v0, n);
            float tc = (-b - Mathf.Sqrt(b * b - 4f * a * g0)) / (2f * a);
            if (tc <= 0f || t <= tc) return true;
            Vector3 pc = p0 + v0 * tc; pc.y -= 4.9f * tc * tc;
            if (pc.y < CabDims.WBL.y + 0.02f || pc.y > CabDims.WTL.y) return false;
            Vector3 vc = v0; vc.y -= 9.8f * tc;
            float vn = Vector3.Dot(vc, n);
            Vector3 v2 = (vc - n * vn) * Slide - n * (vn * Restitution);
            float dt = t - tc;
            p = pc + v2 * dt; p.y -= 4.9f * dt * dt;
            v = v2; v.y -= 9.8f * dt;
            if (p.z < 0.3f && p.y > 1.7f) return false;
            if (Vector3.Dot(p - GlassOrigin, n) < r * 0.5f) return false;
            return true;
        }
    }
}
