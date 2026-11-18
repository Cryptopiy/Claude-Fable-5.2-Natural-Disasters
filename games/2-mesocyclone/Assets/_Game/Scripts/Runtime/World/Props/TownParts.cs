using UnityEngine;

namespace MESOCYCLONE
{
    /// <summary>The larger buildings of the town, each written into the builder's current frame
    /// (origin on the ground at the centre of the footprint).</summary>
    public static class TownParts
    {
        /// <summary>Flat-roofed shop, w wide (local X), 12 m deep; the shop front is the local -Z face.</summary>
        public static void Shop(MeshBuilder mb, float w, float h, int wallSw)
        {
            const float D = 12f;
            mb.Swatch(wallSw);
            if (wallSw != Sw.Brick) mb.Surface(Surf.Concrete);                              // a cream shop front is painted block
            mb.Box(new Vector3(0f, h * 0.5f, 0f), new Vector3(w, h, D), 4 | 8);
            mb.Swatch(Sw.Grey40);
            mb.Box(new Vector3(0f, h + 0.25f, 0f), new Vector3(w + 0.3f, 0.5f, D + 0.3f));
            mb.Swatch(Sw.GlassDark);
            float zf = -D * 0.5f - 0.03f;
            WorldProps.QuadF(mb, new Vector3(-w * 0.5f + 1f, 0.6f, zf), new Vector3(-w * 0.5f + 1f, 2.9f, zf),
                new Vector3(w * 0.5f - 1f, 2.9f, zf), new Vector3(w * 0.5f - 1f, 0.6f, zf), Vector3.back);
            mb.Swatch(Sw.DarkGreen);
            mb.Surface(Surf.Paint);                                                          // awning, not foliage
            mb.Box(new Vector3(0f, 3.3f, -D * 0.5f - 0.7f), new Vector3(w - 1.2f, 0.12f, 1.4f));
        }

        /// <summary>Church: nave along local Z, tower and spire (28 m) at the +Z end.</summary>
        public static void Church(MeshBuilder mb)
        {
            WorldProps.House(mb, 10f, 20f, 6.5f, 4.2f, Sw.HouseWhite, Sw.RoofDark);
            mb.Swatch(Sw.HouseWhite);
            const float tz = 11.9f, th = 16f, hw = 2.2f;
            mb.Box(new Vector3(0f, th * 0.5f, tz), new Vector3(hw * 2f, th, hw * 2f), 4 | 8);
            mb.Swatch(Sw.RoofDark);
            float e = hw + 0.3f;
            var apex = new Vector3(0f, 28f, tz);
            Vector3 a = new Vector3(-e, th, tz - e), b = new Vector3(e, th, tz - e), c = new Vector3(e, th, tz + e), d = new Vector3(-e, th, tz + e);
            WorldProps.TriF(mb, a, b, apex, new Vector3(0f, 0.3f, -1f));
            WorldProps.TriF(mb, b, c, apex, new Vector3(1f, 0.3f, 0f));
            WorldProps.TriF(mb, c, d, apex, new Vector3(0f, 0.3f, 1f));
            WorldProps.TriF(mb, d, a, apex, new Vector3(-1f, 0.3f, 0f));
            WorldProps.QuadF(mb, a, b, c, d, Vector3.down);
        }

        static readonly Vector2[] TankProfile =
        {
            new Vector2(0.8f, 24.5f), new Vector2(5.5f, 27f), new Vector2(6.2f, 29f), new Vector2(6.2f, 34f), new Vector2(5.0f, 36.3f), new Vector2(0.3f, 38f)
        };

        /// <summary>Water tower, 38 m: a tank on six legs with a riser pipe.</summary>
        public static void WaterTower(MeshBuilder mb)
        {
            mb.Swatch(Sw.White);
            mb.Lathe(TankProfile, 16);
            mb.Swatch(Sw.Grey60);
            mb.Cylinder(Vector3.zero, new Vector3(0f, 25.5f, 0f), 0.8f, 0.8f, 8, false);
            Vector3 prevLow = Vector3.zero, prevMid = Vector3.zero;
            for (int i = 0; i <= 6; i++)
            {
                float a = i * Mathf.PI / 3f;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 foot = dir * 5.4f, top = dir * 4.5f + new Vector3(0f, 26.6f, 0f);
                Vector3 low = Vector3.Lerp(foot, top, 0.3f), mid = Vector3.Lerp(foot, top, 0.62f);
                if (i < 6) mb.Cylinder(foot, top, 0.32f, 0.28f, 6, false);
                if (i > 0)
                {
                    mb.Cylinder(prevLow, low, 0.12f, 0.12f, 4, false);
                    mb.Cylinder(prevMid, mid, 0.12f, 0.12f, 4, false);
                }
                prevLow = low; prevMid = mid;
            }
        }

        /// <summary>Grain elevator: eight concrete silos (45 m) in two rows along local Z, a gallery on top and the
        /// head house (55 m) at the +Z end. Footprint about 20 x 46 m.</summary>
        public static void Elevator(MeshBuilder mb)
        {
            mb.Swatch(Sw.Concrete);
            for (int row = -1; row <= 1; row += 2)
                for (int k = 0; k < 4; k++)
                {
                    float x = row * 4.6f, z = -13.8f + k * 9.2f;
                    mb.Cylinder(new Vector3(x, 0f, z), new Vector3(x, 45f, z), 4.5f, 4.5f, 12, true);
                }
            mb.Box(new Vector3(0f, 46.9f, 0f), new Vector3(5f, 3.8f, 36f), 4 | 8);
            WorldProps.Gable(mb, 5f, 36f, 48.8f, 1.1f, 0.25f, Sw.Grey40, Sw.Concrete);
            mb.Swatch(Sw.Concrete);
            mb.Box(new Vector3(0f, 27.5f, 21.4f), new Vector3(9f, 55f, 8f), 4 | 8);
            mb.Swatch(Sw.Grey40);
            mb.Box(new Vector3(0f, 55.3f, 21.4f), new Vector3(9.5f, 0.6f, 8.5f));
            mb.Swatch(Sw.Grey60);
            mb.Cylinder(new Vector3(4.2f, 41f, 21.4f), new Vector3(17f, 9f, 21.4f), 0.5f, 0.5f, 6, true);
            mb.Swatch(Sw.SheetMetal);
            mb.Box(new Vector3(13.5f, 3.2f, 21.4f), new Vector3(9f, 6.4f, 10f), 4 | 8);
            mb.Swatch(Sw.Grey40);
            mb.Box(new Vector3(13.5f, 6.55f, 21.4f), new Vector3(9.4f, 0.3f, 10.4f));
        }

        /// <summary>What the wedge leaves of the elevator: slabs, and the stumps of three silos (below 1.5 m).</summary>
        public static void ElevatorRuin(MeshBuilder mb, Vector3 p, float yaw, ref Rng r)
        {
            PropKit.Ruin(mb, p, yaw, 20f, 46f, 22, Sw.Concrete, Sw.Grey40, ref r);
            mb.Push(p, Quaternion.Euler(0f, yaw, 0f));
            mb.Swatch(Sw.Concrete);
            mb.Cylinder(new Vector3(-4.6f, 0f, -13.8f), new Vector3(-4.6f, 1.4f, -13.8f), 4.5f, 4.4f, 12, true);
            mb.Cylinder(new Vector3(4.6f, 0f, 4.6f), new Vector3(4.6f, 1.1f, 4.6f), 4.5f, 4.4f, 12, true);
            mb.Cylinder(new Vector3(-4.6f, 0f, 13.8f), new Vector3(-4.6f, 0.8f, 13.8f), 4.5f, 4.4f, 12, true);
            mb.Reset();
        }
    }
}
