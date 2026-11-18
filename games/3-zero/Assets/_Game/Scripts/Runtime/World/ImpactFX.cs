using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZERO
{
    /// <summary>The explosion, at the scale of the planet: the fireball, the stem and the cap of the plume that leaves
    /// the atmosphere, the light around it, the dome of the blast wave and the wall of dust it pushes along the
    /// ground, the curtain of ejecta on ballistic arcs, and the rain of fire when the ejecta come back down all over
    /// the sky. Everything is a pure function of the event clock (see Schedule).</summary>
    public static class ImpactFX
    {
        public static int ArcCount, SpriteCount;
        static Transform site, dome, wall, glowRig;
        static Material headMat, ringMat, stemMat, skirtMat, domeMat, wallMat, glowCore, glowHalo, rampartMat, wakeMat, footMat;

        // the cloud of the explosion: billows (displaced spheres) laid out every frame from the timetable
        struct Billow { public Transform tf; public Vector3 dir; public float size, phase; }
        static Billow[] head, ring, stem, skirt, rampart, towers, tail, foot;
        static readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();
        static GameObject arcsGo, rainGo;

        public static void Build(Transform root)
        {
            site = Ctx.Child("Site", root, Schedule.Site, Quaternion.identity).transform;

            // ---- the cloud: fireball and head, the ring of the mushroom, the stem, the base surge ----
            Mesh sphere = Ico.UnitSphere("Billow", 4);
            headMat = Fire("Head", 2.6f, 0.05f, 14f, 0.3f, 2990); headMat.SetFloat("_Disp", 0.34f);
            ringMat = Fire("Ring", 2.2f, 0.03f, 18f, 4.1f, 2990); ringMat.SetFloat("_Disp", 0.3f);
            stemMat = Fire("Stem", 2.8f, 0.11f, 40f, 1.7f, 2990); stemMat.SetFloat("_Cracks", 1f); stemMat.SetFloat("_Disp", 0.3f);
            skirtMat = Fire("Skirt", 2.4f, 0.02f, 12f, 6.3f, 2990); skirtMat.SetFloat("_Disp", 0.5f); skirtMat.SetFloat("_Amb", 1.6f); skirtMat.SetFloat("_FootFade", 7000f); skirtMat.SetFloat("_Dust", 0.55f); skirtMat.SetFloat("_Rim", 0f);
            var rng = new Rng(6113u);
            head = Billows("Head", sphere, headMat, 11, ref rng, true);
            ring = Billows("Ring", sphere, ringMat, 12, ref rng, false);
            stem = Billows("Stem", sphere, stemMat, 9, ref rng, false);
            skirt = Billows("Skirt", sphere, skirtMat, 14, ref rng, false);
            rampartMat = Fire("Rampart", 3.0f, 0.03f, 8f, 9.1f, 2990); rampartMat.SetFloat("_Disp", 0.42f); rampartMat.SetFloat("_Heat", 0f); rampartMat.SetFloat("_Haze", 0.12f); rampartMat.SetFloat("_Amb", 0.45f); rampartMat.SetFloat("_Dust", 0.75f); rampartMat.SetFloat("_Rim", 2.4f);
            Mesh fine = Ico.UnitSphere("BillowFine", 5);          // the wall is seen from close by at the end: finer relief
            rampart = Billows("Rampart", fine, rampartMat, 34, ref rng, false);
            towers = Billows("Tower", fine, rampartMat, 14, ref rng, false);

            // ---- the burning body: the wake of fire and smoke it drags through the air ----
            wakeMat = Fire("Wake", 2.4f, 0.05f, 9f, 12.7f, 2985);
            wakeMat.SetFloat("_Disp", 0.38f); wakeMat.SetFloat("_HeatFall", 85000f); wakeMat.SetFloat("_Rim", 1.6f); wakeMat.SetFloat("_Haze", 0.15f);
            tail = Billows("Tail", sphere, wakeMat, 22, ref rng, false);
            // the wake is drawn out along the path: every billow is stretched along the axis of the fall
            for (int i = 0; i < tail.Length; i++) tail[i].tf.localRotation = Quaternion.FromToRotation(Vector3.up, Schedule.AstAxis) * Quaternion.Euler(0f, tail[i].phase * Mathf.Rad2Deg, 0f);
            // ---- the dust the base surge drags over the ground: wide flat sheets that tie the explosion to the land ----
            footMat = Fire("Foot", 2.0f, 0.01f, 6f, 3.9f, 2988);
            footMat.SetFloat("_Disp", 0.25f); footMat.SetFloat("_Dust", 1f); footMat.SetFloat("_FootFade", 500f); footMat.SetFloat("_Rim", 0.2f); footMat.SetFloat("_Amb", 1.8f); footMat.SetFloat("_Heat", 0.05f);
            foot = Billows("Foot", sphere, footMat, 10, ref rng, false);
            for (int i = 0; i < foot.Length; i++) foot[i].tf.localRotation = Quaternion.Euler(0f, foot[i].phase * Mathf.Rad2Deg, 0f);     // flat on the ground


            // ---- the light around it ----
            glowRig = Ctx.Child("FlashGlow", site, Vector3.zero, Quaternion.identity).transform;
            Color white = Palette.PlasmaWhite.linear, gold = Palette.PlasmaGold.linear;
            glowCore = GlowSprite("GlowCore", new Color(white.r, white.g, white.b, 0.9f), 0.55f);
            glowHalo = GlowSprite("GlowHalo", new Color(gold.r, gold.g, gold.b, 0.5f), 0.05f);

            // ---- the blast wave ----
            domeMat = new Material(Ctx.A.Dome) { name = "ShockDome" }; domeMat.SetFloat("_Kind", 0f);
            wallMat = new Material(Ctx.A.Dome) { name = "DustWall" }; wallMat.SetFloat("_Kind", 1f); wallMat.renderQueue = 2995;
            dome = Part("ShockDome", DomeMesh(), domeMat);
            wall = Part("DustWall", WallMesh(), wallMat);

            BuildEjecta(root);
            BuildRain(root);
            BuildTrailSmoke(root);
            Tick(0f);
        }

        static Material Fire(string name, float scale, float rise, float power, float seed, int queue)
        {
            var m = new Material(Ctx.A.Fireball) { name = name };
            m.SetFloat("_Scale", scale); m.SetFloat("_Rise", rise); m.SetFloat("_Power", power); m.SetFloat("_Seed", seed);
            m.renderQueue = queue;
            return m;
        }

        static Transform Part(string name, Mesh mesh, Material mat)
        {
            var go = Ctx.Child(name, site, Vector3.zero, Quaternion.identity);
            Ctx.Renderer(go, mesh, mat);
            go.SetActive(false);
            return go.transform;
        }

        static Billow[] Billows(string name, Mesh mesh, Material mat, int n, ref Rng rng, bool core)
        {
            var b = new Billow[n];
            for (int i = 0; i < n; i++)
            {
                Vector3 d = core && i == 0 ? Vector3.zero : rng.OnSphere();
                b[i] = new Billow { tf = Part(name + i, mesh, mat), dir = d, size = rng.Range(0.75f, 1.15f), phase = rng.F() * 6.2832f };
                var r = b[i].tf.GetComponent<MeshRenderer>();
                mpb.Clear(); mpb.SetFloat("_Seed", rng.Range(0f, 40f)); r.SetPropertyBlock(mpb);
                b[i].tf.localRotation = rng.Rotation();
            }
            return b;
        }

        static void Place(Billow[] set, bool on, System.Func<int, Billow, Vector4> pose, float squash = 1f)
        {
            for (int i = 0; i < set.Length; i++)
            {
                Show(set[i].tf, on);
                if (!on) continue;
                Vector4 p = pose(i, set[i]);           // xyz = centre (site-local), w = radius
                set[i].tf.localPosition = new Vector3(p.x, p.y, p.z);
                set[i].tf.localScale = new Vector3(p.w, p.w * squash, p.w);
            }
        }

        static Material GlowSprite(string name, Color col, float sharp)
        {
            var sb = new SpriteBuilder(name, 1e7f);
            sb.Add(Vector3.zero, 1000f, 1000f, col, 0.5f, sharp);
            var m = new Material(Ctx.A.Glow) { name = name };
            foreach (var r in sb.Build(glowRig, m, 0f)) { r.gameObject.layer = Ctx.LayerSky; r.GetComponent<MeshFilter>().sharedMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 4e6f); }
            return m;
        }

        // unit hemisphere, uv = (around, up)
        static Mesh DomeMesh()
        {
            const int sides = 128, rows = 48;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var u = new List<Vector2>(); var t = new List<int>();
            for (int j = 0; j <= rows; j++)
            {
                float el = j / (float)rows * Mathf.PI * 0.5f;
                for (int i = 0; i <= sides; i++)
                {
                    float a = i * Mathf.PI * 2f / sides;
                    Vector3 d = new Vector3(Mathf.Cos(a) * Mathf.Cos(el), Mathf.Sin(el), Mathf.Sin(a) * Mathf.Cos(el));
                    v.Add(d); n.Add(d); u.Add(new Vector2(i / (float)sides, j / (float)rows));
                }
            }
            int ring = sides + 1;
            for (int j = 0; j < rows; j++)
                for (int i = 0; i < sides; i++)
                {
                    int p = j * ring + i;
                    t.Add(p); t.Add(p + ring); t.Add(p + 1); t.Add(p + 1); t.Add(p + ring); t.Add(p + ring + 1);
                }
            var m = new Mesh { name = "Dome", indexFormat = IndexFormat.UInt32 };
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, u); m.SetTriangles(t, 0, false);
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 3f);
            m.UploadMeshData(true);
            return m;
        }

        // unit cylinder wall: radius 1, height 1, uv = (around, up)
        static Mesh WallMesh()
        {
            const int sides = 1024, rows = 28;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var u = new List<Vector2>(); var t = new List<int>();
            for (int j = 0; j <= rows; j++)
                for (int i = 0; i <= sides; i++)
                {
                    float a = i * Mathf.PI * 2f / sides;
                    Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    v.Add(d + Vector3.up * (j / (float)rows)); n.Add(d); u.Add(new Vector2(i / (float)sides, j / (float)rows));
                }
            int ring = sides + 1;
            for (int j = 0; j < rows; j++)
                for (int i = 0; i < sides; i++)
                {
                    int p = j * ring + i;
                    t.Add(p); t.Add(p + ring); t.Add(p + 1); t.Add(p + 1); t.Add(p + ring); t.Add(p + ring + 1);
                }
            var m = new Mesh { name = "Wall", indexFormat = IndexFormat.UInt32 };
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, u); m.SetTriangles(t, 0, false);
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 3f);
            m.UploadMeshData(true);
            return m;
        }

        /// <summary>The curtain of ejecta: thousands of pieces thrown out of the crater on real ballistic arcs (real
        /// gravity: the big ones climb for minutes and leave the atmosphere), gathered into rays.</summary>
        static void BuildEjecta(Transform root)
        {
            var rng = new Rng(3301u);
            var arcs = new ArcBuilder();
            const int rays = 26;
            var rayAz = new float[rays]; var rayEl = new float[rays];
            for (int i = 0; i < rays; i++) { rayAz[i] = rng.Range(-Mathf.PI, Mathf.PI); rayEl[i] = rng.Range(44f, 68f); }
            int n = Ctx.N(Config.ArcCount);
            for (int i = 0; i < n; i++)
            {
                bool inRay = rng.Chance(0.7f);
                int k = rng.Int(0, rays);
                float az = inRay ? rayAz[k] + rng.Gauss() * 0.05f : rng.Range(-Mathf.PI, Mathf.PI);
                float el = (inRay ? rayEl[k] + rng.Gauss() * 4f : rng.Range(38f, 74f)) * Mathf.Deg2Rad;
                float big = rng.F(); big *= big;
                float sp = rng.F(); float v0 = Mathf.Lerp(650f, 3300f, sp * sp) * (inRay ? 1f : 0.8f);
                Vector3 dirH = new Vector3(Mathf.Sin(az), 0f, Mathf.Cos(az));
                Vector3 launch = Schedule.Site + dirH * rng.Range(6000f, 38000f) + Vector3.up * 200f;
                Vector3 vel = dirH * (v0 * Mathf.Cos(el)) + Vector3.up * (v0 * Mathf.Sin(el));
                float flight = 2f * v0 * Mathf.Sin(el) / 9.8f;
                float t0 = Schedule.ContactTime + 0.6f + rng.F() * rng.F() * 34f;
                float hw = Mathf.Lerp(260f, 950f, big);
                float lum = rng.F(); byte a8 = (byte)(35f + 160f * lum * lum * lum);     // a few bright pieces, many faint ones (never a hairbrush)
                arcs.Add(launch, vel, t0, 1e6f, flight, rng.Range(18f, 70f), hw, new Color32(255, 255, 255, a8), rng.F(), 18);
            }
            ArcCount = arcs.Count;
            var r = arcs.Build("EjectaArcs", root, Ctx.A.Ribbon);
            if (r != null) { r.gameObject.layer = Ctx.LayerSky; arcsGo = r.gameObject; }
        }

        /// <summary>The rain of fire: ejecta falling back through the high air. It starts over the impact and spreads
        /// outward until it is over the aircraft and beyond: every streak loops, each one born when the rain reaches it.</summary>
        static void BuildRain(Transform root)
        {
            var rng = new Rng(4409u);
            var sb = new SpriteBuilder("Reentry", 1e7f);
            int n = Ctx.N(Config.ReentryStreaks);
            Vector3 mid = Vector3.Lerp(Schedule.Site, Schedule.PlanePos(Schedule.ArrivalTime), 0.72f); mid.y = 0f;
            for (int i = 0; i < n; i++)
            {
                Vector2 c = rng.InCircle() * 420000f;
                Vector3 p = new Vector3(mid.x + c.x, rng.Range(55000f, 115000f), mid.z + c.y);
                Vector3 radial = new Vector3(p.x - Schedule.Site.x, 0f, p.z - Schedule.Site.z);
                float dist = radial.magnitude; radial /= Mathf.Max(dist, 1f);
                float vy = rng.Range(2600f, 4600f), vh = rng.Range(500f, 1500f);
                Vector3 vel = radial * vh + Vector3.down * vy + new Vector3(rng.Signed(), 0f, rng.Signed()) * 300f;
                float period = (p.y - rng.Range(19000f, 44000f)) / vy;  // each burns out at its own height (well above the aircraft): no common hem across the sky
                bool hero = rng.Chance(0.06f);                           // a few big pieces: thick, white-hot heads
                float len = rng.Range(6000f, 18000f) * (hero ? 1.6f : 1f);
                float born = Schedule.ContactTime + 62f + dist / 2300f + rng.F() * 45f;
                float heat = hero ? rng.Range(0.55f, 0.85f) : rng.Range(0.15f, 0.7f);
                sb.Add(p, len * (hero ? 0.012f : 0.006f), len, new Color(1f, 1f, 1f, hero ? 1f : rng.Range(0.3f, 0.85f)), rng.F(), heat, period * rng.Range(1f, 1.12f), vel, 4f, Mathf.Round(born * 2f) * 0.5f);
            }
            SpriteCount = sb.Count;
            var m = new Material(Ctx.A.Streak) { name = "Reentry" }; m.SetFloat("_Power", 2.8f);
            foreach (var r in sb.Build(root, m, 0f)) { r.gameObject.layer = Ctx.LayerSky; r.GetComponent<MeshFilter>().sharedMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2.4e6f); rainGo = r.gameObject; }
        }

        /// <summary>The smoke the body leaves in the air on its way down: born where it passes, it swells for minutes,
        /// the winds at each height bend it, and after the contact it hangs across the sky lit by the fire below.</summary>
        static void BuildTrailSmoke(Transform root)
        {
            var rng = new Rng(5507u);
            var sb = new SpriteBuilder("TrailSmoke", 1e7f);
            int n = Ctx.N(420);
            for (int i = 0; i < n; i++)
            {
                float u = (i + rng.F()) / n;
                float h = Mathf.Lerp(1500f, Schedule.EntryAltitude * 0.92f, u * u);
                Vector3 p = Schedule.Site + Schedule.AstAxis * (h / Schedule.AstAxis.y);
                float born = Schedule.TimeAtHeight(h);
                float thin = Mathf.Exp(-h / 30000f);                                     // thick low in the air, a thread high up
                float size = Mathf.Lerp(7000f, 15000f, thin) * rng.Range(0.7f, 1.3f);     // as wide as the wake it continues: never a thin tube
                p += new Vector3(rng.Signed(), rng.Signed() * 0.5f, rng.Signed()) * size * 0.5f;
                // the wind at this height: it turns with height, so the straight line kinks and twists over the minutes
                float wa = h / 9000f + 1.3f; float ws = Mathf.Lerp(8f, 45f, Mathf.Clamp01(h / 40000f));
                Vector3 vel = new Vector3(Mathf.Cos(wa), 0f, Mathf.Sin(wa)) * ws + Vector3.up * rng.Range(0f, 6f);
                float dark = Mathf.Lerp(0.35f, 0.75f, thin) * rng.Range(0.8f, 1.1f);
                sb.Add(p, size, size * rng.Range(0.6f, 0.9f), new Color(0.82f, 0.8f, 0.78f, Mathf.Lerp(0.45f, 0.75f, thin) * rng.Range(0.6f, 1f)), rng.F(), Mathf.Min(1f, dark + 0.15f), 0f, vel, 0f, Mathf.Round(born * 4f) * 0.25f);
            }
            var m = new Material(Ctx.A.Smoke) { name = "TrailSmoke" };
            m.SetVector("_Life", new Vector4(0.01f, 150f, 3f, 0f)); m.SetFloat("_Grow", 0.1f);     // it hangs across the sky for minutes, frayed by the winds
            m.renderQueue = 2980;                                                                      // behind the wake and the fireball, never drawn over them
            foreach (var r in sb.Build(root, m, 0f)) { r.gameObject.layer = Ctx.LayerSky; r.GetComponent<MeshFilter>().sharedMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2.4e6f); }
            SpriteCount += sb.Count;
        }

        static void Show(Transform t, bool on) { if (t.gameObject.activeSelf != on) t.gameObject.SetActive(on); }

        public static void Tick(float T)
        {
            if (site == null) return;
            float t = T - Schedule.ContactTime;
            bool on = t > 0f;
            Show(dome, false); Show(wall, false);
            if (arcsGo != null && arcsGo.activeSelf != on) arcsGo.SetActive(on);
            if (rainGo != null) { bool r = t > 55f; if (rainGo.activeSelf != r) rainGo.SetActive(r); }
            glowCore.SetFloat("_Power", 0f); glowHalo.SetFloat("_Power", 0f);
            TickWake(T, t);
            if (!on) { Place(head, false, null); Place(ring, false, null); Place(stem, false, null); Place(skirt, false, null); Place(rampart, false, null); Place(towers, false, null); Place(foot, false, null); return; }

            ft = t; rf = Schedule.FireballRadius(T); top = Schedule.PlumeTop(T); rw = Schedule.FrontRadius(T);
            float under = 0.25f + 1.4f * Mathf.Exp(-t / 35f);

            // the head: first the fireball on the ground, then it rises on its column and spreads into a mushroom
            hy = rf * 0.3f + 0.55f * top;
            spread = 1f + t / 110f; flat = 1f / (1f + t / 200f);
            churn = t * 0.0035f;
            Place(head, true, HeadPose, 0.88f);
            headMat.SetFloat("_Heat", 0.2f + 0.55f * Mathf.Exp(-t / 50f) + 0.75f * Mathf.Exp(-t / 6f));
            headMat.SetFloat("_Alpha", Mathf.Clamp01(t / 0.7f));
            headMat.SetFloat("_Under", under * 1.3f + 0.25f);         // the fire below lights the underside of the cap
            // the rim of the mushroom: a ring of billows rolling over under the head, from about 30 s
            ringR = rf * 0.62f * spread;
            Place(ring, t > 24f, RingPose, 0.7f);
            ringMat.SetFloat("_Heat", 0.1f + 0.45f * Mathf.Exp(-t / 60f));
            ringMat.SetFloat("_Alpha", 0.95f * Mathf.Clamp01((t - 24f) / 20f));
            ringMat.SetFloat("_Under", under * 0.8f);
            // the stem: a column of billows from the ground into the head, swaying
            stemTop = Mathf.Max(hy - rf * 0.35f, rf * 0.2f);
            Place(stem, t > 3f, StemPose, 1.15f);
            stemMat.SetFloat("_Heat", 0.3f + 0.9f * Mathf.Exp(-t / 45f));         // the column glows at its foot, through the gaps in its smoke
            stemMat.SetFloat("_GlowTop", Mathf.Max(stemTop * 0.45f, 1000f));
            stemMat.SetFloat("_Alpha", Mathf.Clamp01((t - 3f) / 8f));
            stemMat.SetFloat("_Under", under);
            // the base surge rolls out along the ground faster than the fireball grows, low and dark, its foot glowing
            sk = rf * 1.05f + 85f * t;
            Place(skirt, true, SkirtPose, 0.62f * flat);
            skirtMat.SetFloat("_Heat", 0.06f + 0.6f * Mathf.Exp(-t / 9f) + 0.12f * Mathf.Exp(-t / 120f));
            skirtMat.SetFloat("_Alpha", Mathf.Clamp01(t / 1.2f) * Mathf.Lerp(1f, 0.55f, Mathf.Clamp01((t - 90f) / 120f)));
            skirtMat.SetFloat("_Under", under * 0.5f);                 // no fire seams between the rolls of the surge
            Place(foot, t > 1.5f, FootPose, 0.2f);
            footMat.SetFloat("_Alpha", Mathf.Clamp01((t - 1.5f) / 3f) * 0.55f);
            footMat.SetFloat("_Under", under * 0.4f);
            // light
            float flash = Schedule.Flash(T);
            glowRig.localPosition = new Vector3(0f, hy, 0f);
            glowCore.SetFloat("_SizeMul", Mathf.Max(rf * 2.4f, 9000f) / 1000f);
            glowCore.SetFloat("_Power", 3.2f * flash);
            glowHalo.SetFloat("_SizeMul", Mathf.Max(rf * 6f, 60000f) / 1000f);
            glowHalo.SetFloat("_Power", 1.5f * flash);
            // the rampart: the part of the blast wave that comes at the aircraft, built of billows that roll as it advances
            wh = 6000f + 22000f * Mathf.Clamp01(t / 140f);
            Vector3 pl = Schedule.PlanePos(T) - Schedule.Site; pl.y = 0f;
            phi0 = Mathf.Atan2(pl.z, pl.x);
            grow = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 30f) / 60f));
            Place(rampart, t > 30f, RampartPose, 0.9f);
            Place(towers, t > 30f, TowerPose, 1.1f);
            rampartMat.SetFloat("_Alpha", grow);
            rampartMat.SetFloat("_Under", 0.25f + under * 0.5f);
        }

        // ---- the layout of the billows for this frame (static: no allocation per frame) ----
        static float ft, rf, top, rw, hy, spread, flat, churn, ringR, stemTop, sk, wh, phi0, grow;
        static readonly System.Func<int, Billow, Vector4> HeadPose = (i, b) =>
        {
            float a = b.phase + churn * (i % 2 == 0 ? 1f : -1f);
            Vector3 d = Quaternion.Euler(0f, a * Mathf.Rad2Deg, 0f) * b.dir;
            Vector3 c = new Vector3(d.x * spread, d.y * flat * 0.8f + 0.12f, d.z * spread) * rf * 0.46f;
            return new Vector4(c.x, hy + c.y, c.z, rf * (i == 0 ? 0.72f : 0.5f * b.size) * Mathf.Lerp(1f, 1.15f, Mathf.Clamp01(ft / 120f)));
        };
        static readonly System.Func<int, Billow, Vector4> RingPose = (i, b) =>
        {
            float a = i * 6.2832f / ring.Length + b.phase * 0.08f + churn * 0.6f;
            return new Vector4(Mathf.Cos(a) * ringR, hy - rf * 0.22f * flat + b.dir.y * rf * 0.05f, Mathf.Sin(a) * ringR, rf * 0.34f * b.size * Mathf.Clamp01((ft - 24f) / 30f + 0.2f));
        };
        static readonly System.Func<int, Billow, Vector4> StemPose = (i, b) =>
        {
            float k = Mathf.Clamp01(i / (float)(stem.Length - 1) + b.dir.y * 0.05f);   // uneven spacing: never a stack of rings
            float y = Mathf.Lerp(rf * 0.12f, stemTop, k);
            float w = rf * Mathf.Lerp(0.42f, 0.27f, Mathf.Sin(k * 3.1416f) * 0.8f + k * 0.2f) * (1f + ft / 500f);
            Vector3 sway = new Vector3(Mathf.Sin(b.phase + ft * 0.01f + k * 2f), 0f, Mathf.Cos(b.phase * 1.3f + ft * 0.008f)) * w * 0.25f;
            return new Vector4(sway.x, y, sway.z, w * b.size);
        };
        static readonly System.Func<int, Billow, Vector4> SkirtPose = (i, b) =>
        {
            float a = i * 6.2832f / skirt.Length + b.phase * 0.1f;
            float pw = Mathf.Pow(b.phase / 6.2832f, 1.5f);                          // big rolls and small ones, about 4:1
            float rr = sk * (0.5f + 0.32f * b.size + 0.25f * b.dir.z);              // spread in depth: never two tidy rows
            return new Vector4(Mathf.Cos(a) * rr, rf * (0.02f + 0.07f * pw + 0.09f * b.dir.y) * flat, Mathf.Sin(a) * rr, (rf * 0.34f + 12f * ft) * (0.45f + 1.4f * pw) * b.size);
        };
        static readonly System.Func<int, Billow, Vector4> FootPose = (i, b) =>
        {
            float a = i * 6.2832f / foot.Length + b.phase * 0.15f;
            float rr = sk * (0.8f + 0.3f * b.size);
            return new Vector4(Mathf.Cos(a) * rr, 0f, Mathf.Sin(a) * rr, sk * 0.42f * b.size);
        };
        static readonly System.Func<int, Billow, Vector4> RampartPose = (i, b) =>
        {
            float k = i / (float)(rampart.Length - 1) - 0.5f;
            float phi = phi0 + k * 0.95f + (b.dir.x * 0.012f);
            float pw = b.phase / 6.2832f; pw *= pw * pw;                          // a few huge lobes, many small ones
            float rr = wh * (0.3f + 0.5f * pw + 0.08f * b.size) * grow;
            float rad = rw - rr * 1.0f + b.dir.z * rr * 0.12f;
            return new Vector4(Mathf.Cos(phi) * rad, rr * (0.75f + 0.25f * b.dir.y), Mathf.Sin(phi) * rad, rr);
        };
        static readonly System.Func<int, Billow, Vector4> TowerPose = (i, b) =>
        {
            float k = (i + 0.5f) / towers.Length - 0.5f;
            float phi = phi0 + k * 0.9f + b.dir.x * 0.02f;
            float rr = wh * (0.26f + 0.12f * b.size) * grow;
            float rad = rw - rr * 1.05f;
            return new Vector4(Mathf.Cos(phi) * rad, wh * (0.95f + 0.25f * b.dir.y) * grow, Mathf.Sin(phi) * rad, rr);
        };

        // ---- the burning body (from the entry into the air until the fireball swallows it) ----
        static float wt, wGrow, wAvail;
        static Vector3 wTip, wE1, wE2;

        static void TickWake(float T, float t)
        {
            float entryK = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(Schedule.EntryTime - 3f, Schedule.EntryTime + 14f, T));
            bool on = entryK > 0.001f && t < 45f;
            if (!on) { Place(tail, false, null); return; }
            float close = Mathf.Clamp01(Mathf.InverseLerp(Schedule.EntryTime, Schedule.ContactTime, T));
            Vector3 ax = Schedule.AstAxis;
            wE1 = Vector3.Cross(ax, Vector3.up).normalized; wE2 = Vector3.Cross(wE1, ax);
            wTip = Schedule.AstTip(Mathf.Min(T, Schedule.ContactTime)) - Schedule.Site;      // frozen at the contact: the wake stays in the air
            wt = T; wGrow = 0.5f + 0.5f * close;                                                   // it swells as the air thickens
            wAvail = Mathf.Clamp((Mathf.Min(T, Schedule.ContactTime) - Schedule.EntryTime) * Schedule.AstSpeed, 0f, 260000f);
            float after = t > 0f ? Mathf.Exp(-t / 2.5f) : 1f;
            wakeMat.SetFloat("_Heat", 0.15f + (0.65f + 0.6f * close) * after);
            wakeMat.SetFloat("_Alpha", entryK * (t > 0f ? 1f - Mathf.SmoothStep(0f, 1f, t / 45f) : 1f));     // the column hangs over the fireball, then thins away
            wakeMat.SetFloat("_Under", 0.5f);
            Place(tail, true, TailPose, 1.9f);
        }

        static readonly System.Func<int, Billow, Vector4> TailPose = (i, b) =>
        {
            float k = i / (float)(tail.Length - 1);
            float s = Mathf.Lerp(38000f, Mathf.Max(60000f, wAvail + 30000f), Mathf.Pow(k, 1.15f));
            float wob = 3000f + 9000f * k;                                         // irregular, never a neat stack of rings
            Vector3 lat = (wE1 * Mathf.Sin(b.phase + k * 5f + wt * 0.03f) + wE2 * Mathf.Cos(b.phase * 1.3f + k * 4f)) * wob;
            Vector3 c = wTip + Schedule.AstAxis * (s + b.dir.x * 9000f) + lat;
            return new Vector4(c.x, c.y, c.z, (12000f + 24000f * k) * b.size * wGrow);       // a narrow neck at the body, widening behind
        };
    }
}
