using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MESOCYCLONE
{
    /// <summary>B. The wind farm: twelve rows parallel to the road, hub 100 m, blade 52 m (tip 152 m). Towers and
    /// nacelles are combined per 2.1 km of road; every rotor is its own object sharing one mesh; all aviation
    /// lights live in one LightDots mesh with one material. Twenty turbines stand in the tornado's path: each has
    /// an intact and a wrecked version, swapped by the driver when the wedge has swallowed it.</summary>
    public static class TurbineBuilder
    {
        static readonly float[] Rows = { 350f, 900f, 1500f, 2200f, 3000f, 3900f, -500f, -1100f, -1800f, -2600f, -3500f, -4500f };
        static readonly float[] SwathRows = { 2200f, -1100f, -1800f, -2600f, -3500f };
        static readonly float[] SwathS = { 13520f, 14080f, 14640f, 15200f };
        const float Spacing = 560f, FaceDeg = 200f, HubOut = 6.4f, ChunkLen = 2100f;
        static readonly Vector2[] TowerProfile = { new Vector2(2.3f, 0f), new Vector2(2.0f, 50f), new Vector2(1.55f, 98.2f) };

        struct Site { public double s; public float lat, yaw; public bool wreck; }

        public static void Build(Transform root)
        {
            var rng = new Rng(5203);
            var sites = new List<Site>(320);
            for (int r = 0; r < Rows.Length; r++)
            {
                // the far rows thin out: the farm reads as rows receding to the horizon, not a forest of white sticks
                float far = Mathf.Abs(Rows[r]);
                float sp = far > 3200f ? Spacing * 2.4f : far > 1900f ? Spacing * 1.6f : Spacing;
                float stagger = (r % 2 == 1) ? sp * 0.5f : 0f;
                for (float s = 400f + stagger; s <= 13000f; s += sp)
                {
                    double ss = s + rng.Range(-40f, 40f);
                    float lat = Rows[r] + rng.Range(-25f, 25f), yaw = FaceDeg + rng.Range(-4f, 4f);
                    if (Allowed(ss, lat)) sites.Add(new Site { s = ss, lat = lat, yaw = yaw });
                }
            }
            int normal = sites.Count;
            for (int r = 0; r < SwathRows.Length; r++)
                for (int k = 0; k < SwathS.Length; k++)
                    sites.Add(new Site { s = SwathS[k] + rng.Range(-60f, 60f) + (r % 2) * 180f - 90f, lat = SwathRows[r] + rng.Range(-25f, 25f),
                        yaw = FaceDeg + rng.Range(-4f, 4f), wreck = true });
            int n = sites.Count, wrecks = n - normal;

            var holder = new GameObject("WindFarm").transform;
            holder.SetParent(root, false);
            Material atlas = WorldProps.Atlas;
            // rotors and swath towers are single-material parts (no routing): give them the painted-steel class directly
            Material paint = GameAssets.I.Mat(Surf.MatName[Surf.Paint]);

            // ---- towers and nacelles of the ordinary turbines, combined ----
            int chunks = Mathf.CeilToInt((13000f - 400f + 80f) / ChunkLen);
            for (int c = 0; c < chunks; c++)
            {
                float s0 = 360f + c * ChunkLen;
                var ch = new Chunk(s0 + ChunkLen * 0.5f, 0.0);
                bool any = false;
                for (int i = 0; i < normal; i++)
                {
                    if (sites[i].s < s0 || sites[i].s >= s0 + ChunkLen) continue;
                    ch.mb.Push(ch.P(sites[i].s, sites[i].lat), Quaternion.Euler(0f, sites[i].yaw, 0f));
                    TowerGeo(ch.mb);
                    any = true;
                }
                if (any) ch.Emit("Towers", holder, false, atlas);
            }

            // ---- shared meshes ----
            var mbR = new MeshBuilder();
            RotorGeo(mbR);
            Mesh rotorMesh = mbR.ToMesh("Rotor");
            var mbT = new MeshBuilder();
            TowerGeo(mbT);
            Mesh towerMesh = mbT.ToMesh("TurbineTower");
            var wreckMesh = new Mesh[4];
            for (int v = 0; v < 4; v++)
            {
                var mbW = new MeshBuilder();
                WreckGeo(mbW, v);
                wreckMesh[v] = mbW.ToMesh("TurbineWreck" + v);
            }

            // ---- rotors, lights, the wrecks of the swath ----
            var drv = holder.gameObject.AddComponent<TurbineDriver>();
            drv.Alloc(n, wrecks);
            var centres = new List<Vector3>(n);
            var radii = new List<float>(n);
            for (int i = 0; i < n; i++)
            {
                Site t = sites[i];
                Vector3 p = Land.RoadPoint(t.s, t.lat, 0f);
                Quaternion q = Quaternion.Euler(0f, t.yaw, 0f);
                GameObject rotor = Part("Rotor", holder, rotorMesh, paint, p + new Vector3(0f, Config.TurbineHub, 0f) + q * new Vector3(0f, 0f, HubOut), q);
                drv.rotor[i] = rotor.transform;
                drv.baseRot[i] = q;
                drv.angle[i] = rng.Range(0f, 120f);
                drv.rpm0[i] = rng.Range(7f, 9f);
                drv.calm[i] = rng.Chance(0.6f) ? rng.Range(0f, 0.25f) : rng.Range(0.3f, 1f);
                drv.coast0[i] = rng.Range(88f, 100f);
                drv.coast1[i] = rng.Range(118f, 130f);
                drv.rpmMax[i] = rng.Range(16.5f, 20f);
                drv.hitLag[i] = rng.Range(-1.5f, 1.5f);
                drv.hold[i] = rng.Range(9f, 11.5f);
                drv.feather[i] = rng.Range(10f, 20f);
                drv.idle[i] = rng.Chance(0.12f) ? rng.Range(0.8f, 1.1f) : 0f;
                drv.worldZ[i] = p.z;
                drv.killT[i] = float.MaxValue;
                centres.Add(p + new Vector3(0f, Config.TurbineHub + 2.7f, 0f) + q * new Vector3(0f, 0f, -4.5f));
                radii.Add(0.5f);
                if (!t.wreck) continue;
                int w = i - normal;
                drv.killT[i] = KillTime(p);
                drv.wreckSite[w] = i;
                drv.intact[w] = Part("TowerIntact", holder, towerMesh, paint, p, q);
                drv.wreck[w] = Part("TowerWreck", holder, wreckMesh[w % 4], paint, p, Quaternion.Euler(0f, rng.Range(40f, 150f), 0f));
                drv.wreck[w].SetActive(false);
            }
            drv.lightMat = new Material(GameAssets.I.Mat("glow_red"));
            drv.dots = LightDots.Create("TurbineLights", root, drv.lightMat, centres, radii, 0.0012f);
        }

        static GameObject Part(string name, Transform parent, Mesh mesh, Material mat, Vector3 pos, Quaternion rot)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            return go;
        }

        static bool Allowed(double s, float lat)
        {
            for (int i = 0; i < WorldProps.FarmS.Length; i++)
            {
                double ds = s - WorldProps.FarmS[i]; float dl = lat - WorldProps.FarmLat[i];
                if (ds * ds + dl * dl < 280.0 * 280.0) return false;
            }
            foreach (double cs in PathDef.Crossroads)
                if (System.Math.Abs(s - cs) < 70.0 && Mathf.Abs(lat) < 3300f) return false;
            return true;
        }

        /// <summary>Story time at which the wedge has swallowed a point (world x, z): pure function of the score.</summary>
        static float KillTime(Vector3 p)
        {
            var st = new StoryState();
            for (float t = 398f; t < 606f; t += 0.5f)
            {
                Story.Evaluate(t, ref st);
                if (st.torDescend < 0.9f) continue;
                PathDef.RoadToWorld(st.torS, st.torLat, out double x, out double z);
                double dx = p.x - x, dz = p.z - z;
                float rr = st.torRadius * 0.85f;
                if (dx * dx + dz * dz < rr * rr) return t + 1.5f;
            }
            return 478f;
        }

        /// <summary>Tower and nacelle in the current frame (local +Z = the direction the rotor faces).</summary>
        static void TowerGeo(MeshBuilder mb)
        {
            mb.Swatch(Sw.White);
            mb.Lathe(TowerProfile, 12);
            mb.Box(new Vector3(0f, Config.TurbineHub, -1.5f), new Vector3(4.2f, 4.0f, 12f));
        }

        /// <summary>Hub and three blades in the XY plane, axis = local Z, blade length Config.TurbineBlade.</summary>
        static void RotorGeo(MeshBuilder mb)
        {
            mb.Swatch(Sw.White);
            mb.Sphere(new Vector3(0f, 0f, 0.2f), 2.1f, 8, 5);
            for (int b = 0; b < 3; b++)
            {
                mb.Push(Vector3.zero, Quaternion.Euler(0f, 0f, b * 120f));
                Blade(mb, Config.TurbineBlade);
            }
            mb.Reset();
        }

        // radius along the blade (fraction), chord, thickness, chord offset
        static readonly float[] BladeSec = { 0.023f, 2.0f, 1.9f, 0f, 0.17f, 4.2f, 0.9f, -0.7f, 0.54f, 2.8f, 0.45f, -0.35f, 1f, 0.95f, 0.16f, -0.1f };

        static void Blade(MeshBuilder mb, float len)
        {
            for (int i = 0; i < 3; i++)
            {
                int a = i * 4, b = a + 4;
                float y0 = BladeSec[a] * len, y1 = BladeSec[b] * len;
                float c0 = BladeSec[a + 1] * 0.5f, c1 = BladeSec[b + 1] * 0.5f, t0 = BladeSec[a + 2] * 0.5f, t1 = BladeSec[b + 2] * 0.5f;
                float o0 = BladeSec[a + 3], o1 = BladeSec[b + 3];
                Vector3 a00 = new Vector3(o0 - c0, y0, -t0), a10 = new Vector3(o0 + c0, y0, -t0), a11 = new Vector3(o0 + c0, y0, t0), a01 = new Vector3(o0 - c0, y0, t0);
                Vector3 b00 = new Vector3(o1 - c1, y1, -t1), b10 = new Vector3(o1 + c1, y1, -t1), b11 = new Vector3(o1 + c1, y1, t1), b01 = new Vector3(o1 - c1, y1, t1);
                WorldProps.QuadF(mb, a01, a11, b11, b01, Vector3.forward);
                WorldProps.QuadF(mb, a00, a10, b10, b00, Vector3.back);
                WorldProps.QuadF(mb, a10, a11, b11, b10, Vector3.right);
                WorldProps.QuadF(mb, a00, a01, b01, b00, Vector3.left);
                if (i == 2) WorldProps.QuadF(mb, b00, b10, b11, b01, Vector3.up);
            }
        }

        /// <summary>Four wrecks (local frame at the tower base, +Z = the direction the top fell).</summary>
        static void WreckGeo(MeshBuilder mb, int variant)
        {
            mb.Swatch(Sw.White);
            if (variant == 0 || variant == 2)
            {
                // snapped: a stub with a torn rim, the upper tower on the ground, the nacelle beyond it
                float cut = variant == 0 ? 34f : 57f, r = Mathf.Lerp(2.3f, 1.55f, cut / 98.2f);
                mb.Lathe(new[] { new Vector2(2.3f, 0f), new Vector2(r, cut), new Vector2(r * 0.55f, cut + 2.5f), new Vector2(0.05f, cut - 3f) }, 12);
                float rest = 98f - cut;
                var a = new Vector3(1.5f, r, 5f);
                Vector3 b = a + new Vector3(0.12f, 0f, 0.99f).normalized * rest;
                mb.Cylinder(a, new Vector3(b.x, 1.6f, b.z), r, 1.55f, 12, true);
                mb.Push(new Vector3(b.x + 3f, 1.9f, b.z + 7f), Quaternion.Euler(8f, 24f, 78f));
                mb.Box(Vector3.zero, new Vector3(4.2f, 4.0f, 12f));
                mb.Push(new Vector3(b.x - 14f, 0.5f, b.z - 6f), Quaternion.Euler(0f, 62f, 4f));
                mb.Box(new Vector3(0f, 0f, 22f), new Vector3(3.2f, 0.7f, 46f));
            }
            else
            {
                // the tower stands, the rotor is torn: one or two blades left, hanging
                mb.Lathe(TowerProfile, 12);
                mb.Push(new Vector3(0f, Config.TurbineHub, 0f), Quaternion.Euler(0f, variant == 1 ? 0f : 35f, variant == 1 ? 0f : 6f));
                mb.Box(new Vector3(0f, 0f, -1.5f), new Vector3(4.2f, 4.0f, 12f));
                mb.Sphere(new Vector3(0f, 0f, HubOut + 0.2f), 2.1f, 8, 5);
                mb.Push(new Vector3(0f, Config.TurbineHub, 0f), Quaternion.Euler(0f, variant == 1 ? 0f : 35f, 0f) * Quaternion.Euler(0f, 0f, 172f));
                mb.Box(new Vector3(-0.4f, variant == 1 ? 14f : 25f, HubOut), new Vector3(3.4f, variant == 1 ? 26f : 48f, 0.8f));
                if (variant == 3)
                {
                    mb.Push(new Vector3(0f, Config.TurbineHub, 0f), Quaternion.Euler(0f, 35f, 0f) * Quaternion.Euler(0f, 0f, 58f));
                    mb.Box(new Vector3(-0.4f, 9f, HubOut), new Vector3(3.2f, 16f, 0.8f));
                }
            }
            mb.Reset();
        }
    }
}
