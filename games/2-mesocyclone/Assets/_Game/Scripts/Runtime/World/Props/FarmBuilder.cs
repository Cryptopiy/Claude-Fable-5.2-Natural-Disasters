using System.Collections.Generic;
using UnityEngine;

namespace MESOCYCLONE
{
    /// <summary>D. Six farmsteads: house, gambrel barn (12 m), metal silos or grain bins, machine shed, windbreak,
    /// yard light, gravel yard and driveway, a vehicle. One mesh per farm (matte + metal). Farm frame: origin at the
    /// site, +X away from the road, +Z along it.</summary>
    public static class FarmBuilder
    {
        public static void Build(Transform root, PropLights lights)
        {
            var rng = new Rng(7411);
            var holder = new GameObject("Farms").transform;
            holder.SetParent(root, false);
            var dots = new List<Vector3>(8); var radii = new List<float>(8);
            var pools = new MeshBuilder();
            for (int f = 0; f < WorldProps.FarmS.Length; f++)
            {
                double s = WorldProps.FarmS[f]; float lat = WorldProps.FarmLat[f];
                var ch = new Chunk(s, lat, 2);
                MeshBuilder mb = ch.mb;
                Quaternion R = ch.RoadRot(s, lat < 0f ? 180f : 0f);
                float reach = Mathf.Abs(lat) - 5.2f;

                // ground: yard and driveway
                mb.Push(Vector3.zero, R);
                mb.Swatch(Sw.Gravel);
                WorldProps.QuadF(mb, new Vector3(-34f, 0.025f, -30f), new Vector3(-34f, 0.025f, 30f), new Vector3(34f, 0.025f, 30f), new Vector3(34f, 0.025f, -30f), Vector3.up);
                WorldProps.QuadF(mb, new Vector3(-reach, 0.025f, -2.5f), new Vector3(-reach, 0.025f, 2.5f), new Vector3(-34f, 0.025f, 2.5f), new Vector3(-34f, 0.025f, -2.5f), Vector3.up);

                // house
                int wall = f % 2 == 0 ? Sw.HouseWhite : Sw.Cream;
                mb.Push(R * new Vector3(-14f, 0f, 16f), R * Quaternion.Euler(0f, rng.Chance(0.5f) ? 90f : 0f, 0f));
                WorldProps.House(mb, 9f, 11f, 5.4f, 2.6f, wall, Sw.RoofDark);

                // barn: 12 x 20 m, ridge at 12 m
                mb.Push(R * new Vector3(12f, 0f, -6f), R);
                mb.Swatch(Sw.BarnRed);
                mb.Box(new Vector3(0f, 2.6f, 0f), new Vector3(12f, 5.2f, 20f), 4 | 8);
                WorldProps.Gambrel(mb, 12f, 20f, 5.2f, 6.8f, Sw.RoofDark, Sw.BarnRed);
                mb.Swatch(Sw.Wood);
                WorldProps.QuadF(mb, new Vector3(-2.2f, 0f, -10.03f), new Vector3(-2.2f, 4.2f, -10.03f), new Vector3(2.2f, 4.2f, -10.03f), new Vector3(2.2f, 0f, -10.03f), Vector3.back);

                // silos or grain bins (metal)
                mb.SetSub(1);
                mb.Swatch(Sw.SheetMetal);
                mb.Surface(Surf.CorrugatedH);                                                // grain bins: corrugations run around the bin
                int count = rng.Range(1, 4);
                for (int k = 0; k < count; k++)
                {
                    mb.Push(R * new Vector3(26f, 0f, -17f + k * 8.2f), R);
                    if (rng.Chance(0.55f))
                    {
                        float h = rng.Range(13f, 17f);
                        mb.Lathe(new[] { new Vector2(2.8f, 0f), new Vector2(2.8f, h), new Vector2(2.0f, h + 1.4f), new Vector2(0.2f, h + 2.0f) }, 12);
                    }
                    else
                        mb.Lathe(new[] { new Vector2(3.9f, 0f), new Vector2(3.9f, 6.5f), new Vector2(0.5f, 9.0f), new Vector2(0.5f, 9.5f), new Vector2(0.06f, 9.6f) }, 14);
                }
                // machine shed
                mb.Push(R * new Vector3(-4f, 0f, -22f), R * Quaternion.Euler(0f, 90f, 0f));
                WorldProps.House(mb, 10f, 20f, 4.5f, 1.6f, Sw.SheetMetal, Sw.SheetMetal);       // corrugated walls and roof
                mb.SetSub(0);

                // windbreak
                int trees = rng.Range(6, 13);
                for (int k = 0; k < trees; k++)
                    PropKit.Tree(mb, R * new Vector3(-34f + 68f * (k + 0.5f) / trees + rng.Range(-1f, 1f), 0f, 36f + rng.Range(-1.5f, 1.5f)), rng.Range(7f, 14f), ref rng, false);
                if (rng.Chance(0.5f))
                    for (int k = 0; k < 5; k++)
                        PropKit.Tree(mb, R * new Vector3(40f + rng.Range(-1.5f, 1.5f), 0f, -24f + k * 12f), rng.Range(7f, 12f), ref rng, false);

                // yard light
                mb.Push(R * new Vector3(0f, 0f, 8f), R);
                PropKit.LampPole(mb, 7f, 1.2f);
                Vector3 head = ch.origin + R * new Vector3(1.2f, 6.8f, 8f);
                dots.Add(head); radii.Add(0.24f);
                var c = new Vector3(head.x, 0.06f, head.z);
                WorldProps.QuadF(pools, c + new Vector3(-7f, 0f, -7f), c + new Vector3(-7f, 0f, 7f), c + new Vector3(7f, 0f, 7f), c + new Vector3(7f, 0f, -7f), Vector3.up);

                // a vehicle
                if (f == 1)
                {
                    mb.Push(R * new Vector3(-46f, 0.03f, 0.2f), R * Quaternion.Euler(0f, 90f, 0f));
                    PropKit.Car(mb, Sw.SteelBlue, false);
                }
                else if (f % 2 == 0)
                {
                    mb.Push(R * new Vector3(-6f, 0.03f, -4f), R * Quaternion.Euler(0f, rng.Range(0f, 360f), 0f));
                    PropKit.Tractor(mb);
                }
                else
                {
                    mb.Push(R * new Vector3(-23f, 0.03f, 5f), R * Quaternion.Euler(0f, rng.Range(-20f, 20f), 0f));
                    PropKit.Car(mb, Sw.CarLight, true);
                }
                ch.Emit("Farm" + f, holder, false, WorldProps.Atlas, WorldProps.Metal);
            }

            lights.yardMat = new Material(GameAssets.I.Mat("glow_sodium"));
            lights.yardPoolMat = new Material(GameAssets.I.Mat("glow_pool"));
            lights.yardDots = LightDots.Create("YardLights", root, lights.yardMat, dots, radii, 0.0012f);
            WorldProps.NoShadow(pools.ToObject("YardPools", holder, lights.yardPoolMat));
        }
    }

    /// <summary>E. Shelterbelts: long rows of trees on field edges, along or across the road direction.
    /// Inside the damage swath the trees are stripped trunks and some are gone.</summary>
    public static class Shelterbelts
    {
        // chainage, lateral, length, across (1) or along (0) the road
        static readonly float[] Rows =
        {
            700f, 140f, 420f, 0f,      1900f, -260f, 520f, 0f,     2600f, 480f, 300f, 1f,     4700f, -120f, 360f, 0f,
            5300f, 1150f, 600f, 0f,    6800f, -620f, 400f, 1f,     8200f, 160f, 500f, 0f,     8900f, -1500f, 560f, 0f,
            10500f, 420f, 380f, 1f,    10900f, -150f, 440f, 0f,    12300f, 1900f, 600f, 0f,   13600f, -170f, 420f, 0f,
            14050f, 130f, 320f, 0f,    16300f, -240f, 500f, 0f
        };

        public static void Build(Transform root)
        {
            var rng = new Rng(8111);
            var holder = new GameObject("Shelterbelts").transform;
            holder.SetParent(root, false);
            for (int i = 0; i + 3 < Rows.Length; i += 4)
            {
                float s0 = Rows[i], lat0 = Rows[i + 1], len = Rows[i + 2];
                bool across = Rows[i + 3] > 0.5f;
                float dir = lat0 >= 0f ? 1f : -1f;
                var ch = across ? new Chunk(s0, lat0 + dir * len * 0.5f) : new Chunk(s0 + len * 0.5f, lat0);
                for (float t = 0f; t < len; t += rng.Range(5.5f, 8.5f))
                {
                    double s = across ? s0 + rng.Range(-1.5f, 1.5f) : s0 + t;
                    double lat = across ? lat0 + dir * t : lat0 + rng.Range(-1.5f, 1.5f);
                    bool stripped = WorldProps.InSwath(s);
                    if (stripped && rng.Chance(0.25f)) continue;
                    PropKit.Tree(ch.mb, ch.P(s, lat), rng.Range(7f, 14f), ref rng, stripped);
                }
                ch.Emit("Shelterbelt", holder, false, WorldProps.Atlas);
            }
        }
    }
}
