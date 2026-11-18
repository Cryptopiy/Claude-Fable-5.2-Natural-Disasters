using System.Collections.Generic;
using UnityEngine;

namespace MESOCYCLONE
{
    /// <summary>C. The town (900 x 900 m around chainage 15000, lateral +900) and the grain elevator beside the
    /// road. Town coordinates: e = metres east of the centre, n = metres north, in the road frame of chainage
    /// 15000. Buildings are combined into 18 erase groups by their distance from the north-west corner (plus the
    /// elevator), each with a standing mesh and a ruin mesh; lamps and lit windows are LightDots in 8 blocks. The
    /// elevator's own lamps, floods and red roof beacon are not in any block: they die with its erasure, so the
    /// camera can land on a lit column in front of the wedge and watch it go out when the wedge takes it.</summary>
    public static class TownBuilder
    {
        public const int Groups = 18, Blocks = 8;
        const float Half = 450f, DMax = 1273f, ElevErase = 0.15f;
        static readonly float[] StreetE = { -300f, -100f, 100f, 300f };
        static readonly float[] StreetN = { -280f, 0f, 280f };
        static readonly int[] WallSw = { Sw.HouseBeige, Sw.HouseBlue, Sw.HouseGreen, Sw.HouseWhite, Sw.Cream, Sw.Tan, Sw.Brick };
        static readonly int[] RoofSw = { Sw.RoofDark, Sw.RoofRed, Sw.Grey20 };

        static Chunk[] stand, ruin;
        static List<Vector3>[] lampPos, winPos;
        static List<Vector3> elevLamps;                 // the elevator's lights: they follow its erasure, not a power block
        static Vector3 elevBeacon;
        static MeshBuilder pools;
        static Quaternion Q;
        static float heading;
        static Vector3 centre;

        static Vector3 L(float e, float n, float y = 0f) => Q * new Vector3(e, y, n);
        static float Dist(float e, float n) { float de = e + Half, dn = n - Half; return Mathf.Sqrt(de * de + dn * dn); }
        static int GroupOf(float e, float n) => Mathf.Clamp((int)(Dist(e, n) / DMax * Groups), 0, Groups - 1);
        static int BlockOf(float e, float n) => Mathf.Clamp((int)(Dist(e, n) / DMax * Blocks), 0, Blocks - 1);

        public static void Build(Transform root)
        {
            var rng = new Rng(6307);
            double ts = Beat05_Funnel.TownS, tl = Beat05_Funnel.TownLat;
            heading = (float)PathDef.HeadingDeg(ts);
            Q = Quaternion.Euler(0f, heading, 0f);
            centre = Land.RoadPoint(ts, tl, 0f);
            stand = new Chunk[Groups + 1]; ruin = new Chunk[Groups + 1];
            for (int g = 0; g <= Groups; g++) { stand[g] = new Chunk(ts, tl); ruin[g] = new Chunk(ts, tl); }
            lampPos = new List<Vector3>[Blocks]; winPos = new List<Vector3>[Blocks];
            for (int b = 0; b < Blocks; b++) { lampPos[b] = new List<Vector3>(); winPos[b] = new List<Vector3>(); }
            elevLamps = new List<Vector3>();
            pools = new MeshBuilder();
            pools.SetSub(Blocks - 1);

            var streets = new Chunk(ts, tl);
            foreach (float e in StreetE) Flat(streets, e - 3.5f, e + 3.5f, -420f, 420f, 0.03f, Sw.Asphalt);
            foreach (float n in StreetN) Flat(streets, -420f, 420f, n - 3.5f, n + 3.5f, 0.04f, Sw.Asphalt);
            Flat(streets, -((float)tl - 5.2f), -420f, -3.5f, 3.5f, 0.04f, Sw.Asphalt);          // Main Street runs out to the highway
            streets.Ground(Beat05_Funnel.ElevS - 3.0, Beat05_Funnel.ElevS + 3.0, 5.2, Beat05_Funnel.ElevLat - 12.0, 0.03f, Sw.Gravel);

            Houses(ref rng);
            Shops(ref rng);
            Landmarks(ref rng);
            Lamps();

            // ---- objects ----
            var holder = new GameObject("Town").transform;
            holder.SetParent(root, false);
            Material atlas = WorldProps.Atlas;
            streets.Emit("Streets", holder, false, atlas);
            var drv = holder.gameObject.AddComponent<TownDriver>();
            drv.stand = new GameObject[Groups + 1]; drv.ruin = new GameObject[Groups + 1]; drv.eraseThr = new float[Groups + 1];
            for (int g = 0; g <= Groups; g++)
            {
                drv.eraseThr[g] = g == Groups ? ElevErase : 0.26f + 0.70f * (g + 0.5f) / Groups;
                if (stand[g].mb.VertexCount > 0) drv.stand[g] = stand[g].Emit("Standing" + g, holder, false, atlas);
                if (ruin[g].mb.VertexCount > 0)
                {
                    drv.ruin[g] = ruin[g].Emit("Ruin" + g, holder, false, atlas);
                    drv.ruin[g].SetActive(false);
                }
            }
            drv.poolMats = new Material[Blocks];
            Material poolSrc = GameAssets.I.Mat("glow_pool");
            for (int b = 0; b < Blocks; b++) drv.poolMats[b] = new Material(poolSrc);
            pools.Reset();
            GameObject pg = pools.ToObject("LampPools", holder, drv.poolMats);
            pg.transform.localPosition = centre;
            WorldProps.NoShadow(pg);

            var all = new List<Vector3>(64); var rad = new List<float>(64);
            drv.lampStart = Flatten(lampPos, all, rad, 0.24f);
            // the elevator's lights go after the last block, so lampStart[Blocks] still ends the block lamps and the
            // driver's block loop never touches them; the driver switches this range with the elevator's erasure
            drv.elevLampStart = all.Count;
            for (int i = 0; i < elevLamps.Count; i++) { all.Add(elevLamps[i]); rad.Add(0.3f); }
            drv.elevLampEnd = all.Count;
            drv.lampMat = new Material(GameAssets.I.Mat("glow_sodium"));
            drv.lamps = LightDots.Create("TownLamps", root, drv.lampMat, all, rad, 0.0012f);
            // the beacon has a material of its own so it can be red while the lamps are sodium; it is steady (no
            // pulse), so it can never fall in step with the blinking turbine beacons and read as one of them
            drv.beaconMat = new Material(GameAssets.I.Mat("glow_sodium"));
            drv.beacon = LightDots.Create("ElevatorBeacon", root, drv.beaconMat, new List<Vector3> { elevBeacon }, new List<float> { 0.3f }, 0.0012f);
            all = new List<Vector3>(64); rad = new List<float>(64);
            drv.winStart = Flatten(winPos, all, rad, 0.45f);
            drv.winMat = new Material(GameAssets.I.Mat("glow_window"));
            drv.windows = LightDots.Create("TownWindows", root, drv.winMat, all, rad, 0.0008f);

            stand = null; ruin = null; lampPos = null; winPos = null; pools = null; elevLamps = null;
        }

        static int[] Flatten(List<Vector3>[] blocks, List<Vector3> all, List<float> rad, float r)
        {
            var start = new int[Blocks + 1];
            for (int b = 0; b < Blocks; b++)
            {
                start[b] = all.Count;
                for (int i = 0; i < blocks[b].Count; i++) { all.Add(blocks[b][i]); rad.Add(r); }
            }
            start[Blocks] = all.Count;
            return start;
        }

        static void Flat(Chunk ch, float e0, float e1, float n0, float n1, float y, int sw)
        {
            ch.mb.Reset();
            ch.mb.Swatch(sw);
            WorldProps.QuadF(ch.mb, L(e0, n0, y), L(e0, n1, y), L(e1, n1, y), L(e1, n0, y), Vector3.up);
        }

        static void Houses(ref Rng rng)
        {
            int idx = 0;
            for (int si = 0; si < StreetE.Length; si++)
                for (int side = -1; side <= 1; side += 2)
                    for (float n = -400f; n <= 400f; n += 36f)
                    {
                        idx++;
                        if (Mathf.Abs(n) < 46f || Mathf.Abs(Mathf.Abs(n) - 280f) < 22f) continue;
                        if (!rng.Chance(0.44f)) continue;
                        float e = StreetE[si] + side * rng.Range(15f, 19f), nn = n + rng.Range(-3f, 3f);
                        float w = rng.Range(7f, 10f), d = rng.Range(9f, 13f), rise = rng.Range(1.8f, 3f);
                        float wallH = rng.Chance(0.25f) ? rng.Range(5f, 5.6f) : rng.Range(2.8f, 3.4f);
                        bool turned = rng.Chance(0.4f);
                        int wall = WallSw[rng.Range(0, WallSw.Length)], roof = RoofSw[rng.Range(0, RoofSw.Length)];
                        int g = GroupOf(e, nn);
                        MeshBuilder mb = stand[g].mb;
                        float yaw = turned ? 90f : 0f;
                        mb.Push(L(e, nn), Q * Quaternion.Euler(0f, yaw, 0f));
                        WorldProps.House(mb, w, d, wallH, rise, wall, roof);
                        float fe = turned ? d : w, fn = turned ? w : d;                 // footprint along e and n
                        if (rng.Chance(0.4f))
                        {   // porch on the street side
                            mb.Push(L(e, nn), Q);
                            float px = -side * (fe * 0.5f + 1.1f);
                            mb.Swatch(roof);
                            mb.Box(new Vector3(px, 2.5f, 0f), new Vector3(2.3f, 0.14f, fn * 0.6f));
                            mb.Swatch(Sw.HouseWhite);
                            mb.Box(new Vector3(px - side * 0.95f, 1.21f, fn * 0.27f), new Vector3(0.14f, 2.42f, 0.14f), 4);
                            mb.Box(new Vector3(px - side * 0.95f, 1.21f, -fn * 0.27f), new Vector3(0.14f, 2.42f, 0.14f), 4);
                        }
                        if (rng.Chance(0.25f))
                        {   // garage beside the house
                            mb.Push(L(e + side * 1f, nn + fn * 0.5f + 2.6f), Q * Quaternion.Euler(0f, 90f, 0f));
                            WorldProps.House(mb, 4.2f, 6.5f, 2.5f, 0.9f, wall, roof);
                        }
                        if (rng.Chance(0.55f))
                            winPos[BlockOf(e, nn)].Add(centre + L(e - side * (fe * 0.5f + 0.3f), nn + rng.Range(-2f, 2f), 1.8f));
                        PropKit.Ruin(ruin[g].mb, L(e, nn), heading + yaw, w, d, rng.Range(5, 9), wall, roof, ref rng);
                        if (rng.Chance(0.35f))
                        {
                            Vector3 tp = L(e + side * rng.Range(11f, 17f), nn + rng.Range(-8f, 8f));
                            float th = rng.Range(7f, 13f);
                            var r2 = new Rng((uint)(7100 + idx)); var r3 = new Rng((uint)(7100 + idx));
                            PropKit.Tree(mb, tp, th, ref r2, false);
                            PropKit.Tree(ruin[g].mb, tp, th, ref r3, true);
                        }
                    }
        }

        static void Shops(ref Rng rng)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                float e = side > 0 ? -62f : -34f, end = side > 0 ? 62f : 44f;
                while (e < end)
                {
                    float w = rng.Range(13f, 21f), h = rng.Range(4.5f, 7.5f);
                    float ce = e + w * 0.5f, cn = side * 12f;
                    int g = GroupOf(ce, cn);
                    stand[g].mb.Push(L(ce, cn), Q * Quaternion.Euler(0f, side > 0 ? 0f : 180f, 0f));
                    TownParts.Shop(stand[g].mb, w, h, rng.Chance(0.5f) ? Sw.Brick : Sw.Cream);
                    if (rng.Chance(0.6f)) winPos[BlockOf(ce, cn)].Add(centre + L(ce, cn - side * 6.6f, 1.8f));
                    PropKit.Ruin(ruin[g].mb, L(ce, cn), heading, w, 12f, rng.Range(6, 10), Sw.Brick, Sw.Grey40, ref rng);
                    e += w + 0.5f;
                }
            }
        }

        static void Landmarks(ref Rng rng)
        {
            float ce = 190f, cn = 150f;
            int g = GroupOf(ce, cn);
            stand[g].mb.Push(L(ce, cn), Q * Quaternion.Euler(0f, 90f, 0f));
            TownParts.Church(stand[g].mb);
            PropKit.Ruin(ruin[g].mb, L(ce, cn), heading + 90f, 10f, 24f, 9, Sw.HouseWhite, Sw.RoofDark, ref rng);

            ce = -190f; cn = -160f; g = GroupOf(ce, cn);
            stand[g].mb.Push(L(ce, cn), Q);
            TownParts.WaterTower(stand[g].mb);
            PropKit.Ruin(ruin[g].mb, L(ce, cn), heading, 12f, 12f, 7, Sw.White, Sw.Grey60, ref rng);

            // three empty cars
            Car(-97.6f, 64f, 0f, Sw.CarLight, false);
            Car(102.4f, -150f, 180f, Sw.Navy, true);
            Car(24f, -2.4f, 90f, Sw.CarDark, false);

            // the grain elevator, in road coordinates, with lamps of its own that die with it. Their ground pools sit
            // in block 5, which goes dark at 461.12, before the mesh is erased (about 462.4), so no glowing patch is
            // ever left lying over the ruin
            Chunk se = stand[Groups], re = ruin[Groups];
            double es = Beat05_Funnel.ElevS, el = Beat05_Funnel.ElevLat;
            se.At(es, el);
            TownParts.Elevator(se.mb);
            TownParts.ElevatorRuin(re.mb, re.P(es, el), (float)PathDef.HeadingDeg(es), ref rng);
            for (int k = -1; k <= 1; k += 2)
            {
                double ls = es + k * 16.0, ll = el - 15.0;
                se.At(ls, ll);
                PropKit.LampPole(se.mb, 8f, 1.6f);
                Vector3 head = se.P(ls, ll) + se.RoadRot(ls) * new Vector3(1.6f, 7.8f, 0f);
                elevLamps.Add(centre + head);
                Pool(5, new Vector3(head.x, 0.07f, head.z));
            }

            // Floods and a roof beacon in the elevator's own frame (the frame se.At(es, el) uses: +x away from the
            // road, +z up-road, y up). At 3.76 km the structure is only 7-10 px of dark on a dark horizon; lit, it
            // reads as a column of sodium dots with a red top standing in front of the wedge. Nothing goes on the
            // headhouse below its gallery roof (49.9 m): from the truck's bearing the 45 m silos hide it.
            Vector3 eo = centre + se.P(es, el);
            Quaternion er = se.RoadRot(es);
            elevLamps.Add(eo + er * new Vector3(-2.5f, 30f, -18.9f));      // near end face of the silo bank (silos end at z -18.3)
            elevLamps.Add(eo + er * new Vector3(0f, 47.2f, -18.4f));       // end of the gallery (box y 45-48.8, z +-18)
            elevLamps.Add(eo + er * new Vector3(-4.9f, 52.4f, 21.4f));     // road side of the headhouse, above the gallery roof
            elevBeacon = eo + er * new Vector3(0f, 56.1f, 21.4f);          // on the headhouse roof (top 55.6 m)
        }

        static void Car(float e, float n, float yaw, int paint, bool pickup)
        {
            MeshBuilder mb = stand[GroupOf(e, n)].mb;
            mb.Push(L(e, n, 0.04f), Q * Quaternion.Euler(0f, yaw, 0f));
            PropKit.Car(mb, paint, pickup);
        }

        static void Lamps()
        {
            for (int si = 0; si < StreetE.Length; si++)
                for (float n = -390f; n <= 411f; n += 100f) Lamp(StreetE[si] + 4.6f, n, 180f);
            Lamp(-200f, 4f, 90f); Lamp(0f, 4f, 90f); Lamp(200f, 4f, 90f);
            Lamp(-520f, 4f, 90f); Lamp(-680f, 4f, 90f);
        }

        /// <summary>A street lamp at town coordinates; its arm points along local +X turned by yaw.</summary>
        static void Lamp(float e, float n, float yaw)
        {
            MeshBuilder mb = stand[GroupOf(e, n)].mb;
            Quaternion q = Q * Quaternion.Euler(0f, yaw, 0f);
            mb.Push(L(e, n), q);
            PropKit.LampPole(mb, 8f, 1.6f);
            Vector3 head = L(e, n) + q * new Vector3(1.6f, 7.8f, 0f);
            lampPos[BlockOf(e, n)].Add(centre + head);
            Pool(BlockOf(e, n), new Vector3(head.x, 0.07f, head.z));
        }

        static void Pool(int block, Vector3 c)
        {
            const float R = 6f;
            pools.SetSub(block);
            pools.Reset();
            WorldProps.QuadF(pools, c + new Vector3(-R, 0f, -R), c + new Vector3(-R, 0f, R), c + new Vector3(R, 0f, R), c + new Vector3(R, 0f, -R), Vector3.up);
        }
    }
}
