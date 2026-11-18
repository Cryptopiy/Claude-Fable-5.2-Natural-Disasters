using UnityEngine;

namespace MESOCYCLONE
{
    /// <summary>I. What the wedge left on the land between chainage 13250 and 15350: boards, sheet metal, branches,
    /// torn-field patches, two twisted roof sections, a tipped trailer, an overturned tank. About 220 pieces, denser
    /// near chainage 14300. Nothing ever lies on the truck's lane (lateral 0.2 .. 3.6 stays clear, with margin).
    /// Six chunks of 350 m (matte + metal), cross-fading out at about 1 km.</summary>
    public static class DebrisBuilder
    {
        const float ChunkLen = 350f;
        const int Chunks = 6, Pieces = 215;

        public static void Build(Transform root)
        {
            var rng = new Rng(9907);
            var holder = new GameObject("Debris").transform;
            holder.SetParent(root, false);
            var ch = new Chunk[Chunks];
            for (int c = 0; c < Chunks; c++) ch[c] = new Chunk(WorldProps.SwathS0 + (c + 0.5f) * ChunkLen, 0.0, 2);

            for (int i = 0; i < Pieces; i++)
            {
                float u = (rng.Value + rng.Value + rng.Value) / 3f;                       // bell-shaped: dense in the middle
                float s = Mathf.Clamp(WorldProps.SwathMid + (u - 0.5f) * 2300f, WorldProps.SwathS0 + 6f, WorldProps.SwathS1 - 6f);
                Chunk c = At(ch, s);
                MeshBuilder mb = c.mb;
                float v = rng.Value, yaw = rng.Range(0f, 360f);
                if (v < 0.40f)
                {   // a board
                    float len = rng.Range(2.2f, 4.8f), tilt = rng.Chance(0.3f) ? rng.Range(3f, 9f) : 0f;
                    float lat = Lateral(ref rng, len * 0.5f + 0.2f);
                    c.mb.Push(c.P(s, lat, 0.065f + len * 0.5f * Mathf.Sin(tilt * Mathf.Deg2Rad)), c.RoadRot(s, yaw) * Quaternion.Euler(tilt, 0f, 0f));
                    mb.Swatch(rng.Chance(0.7f) ? Sw.DebrisWood : Sw.Wood);
                    mb.Box(Vector3.zero, new Vector3(rng.Range(0.16f, 0.3f), 0.05f, len));
                }
                else if (v < 0.62f)
                {   // a bent piece of sheet metal
                    float len = rng.Range(1.2f, 3f), w = rng.Range(0.9f, 1.3f), bend = rng.Range(20f, 70f);
                    float lat = Lateral(ref rng, len + 0.3f);
                    Quaternion q = c.RoadRot(s, yaw);
                    Vector3 p = c.P(s, lat, 0.06f);
                    mb.SetSub(1);
                    mb.Swatch(Sw.SheetMetal);
                    mb.Push(p, q);
                    mb.Box(new Vector3(0f, 0f, -len * 0.25f), new Vector3(w, 0.04f, len * 0.5f));
                    mb.Push(p, q * Quaternion.Euler(-bend, 0f, 0f));
                    mb.Box(new Vector3(0f, 0.012f, len * 0.25f + 0.02f), new Vector3(w * 0.98f, 0.04f, len * 0.5f));
                    mb.SetSub(0);
                }
                else if (v < 0.82f)
                {   // a branch
                    float len = rng.Range(2f, 5f);
                    float lat = Lateral(ref rng, len * 0.5f + 0.9f);
                    c.At(s, lat, 0f, yaw);
                    mb.Swatch(Sw.Trunk);
                    mb.Cylinder(new Vector3(0f, 0.13f, -len * 0.5f), new Vector3(0f, 0.2f, len * 0.5f), 0.11f, 0.05f, 5, true);
                    mb.Cylinder(new Vector3(0f, 0.16f, 0f), new Vector3(0.75f, 0.5f, 0.6f), 0.06f, 0.04f, 4, false);
                    if (len > 3.4f) mb.Cylinder(new Vector3(0f, 0.18f, len * 0.22f), new Vector3(-0.7f, 0.42f, len * 0.4f), 0.05f, 0.04f, 4, false);
                }
                else
                {   // a torn patch of field: dark soil, 2 cm above the ground
                    float r = rng.Range(5f, 12f);
                    float lat = (rng.Chance(0.5f) ? 1f : -1f) * rng.Range(18f + r * 1.3f, 230f);
                    Vector3 p = c.P(s, lat, 0.02f);
                    mb.Reset();
                    mb.Swatch(Sw.Soil);
                    Vector3 a = p + Corner(yaw, r, ref rng), b = p + Corner(yaw + 90f, r, ref rng);
                    Vector3 d = p + Corner(yaw + 270f, r, ref rng), e = p + Corner(yaw + 180f, r, ref rng);
                    WorldProps.QuadF(mb, a, b, e, d, Vector3.up);
                }
            }

            Specials(ch);
            for (int c = 0; c < Chunks; c++)
            {
                if (ch[c].mb.VertexCount == 0) continue;
                WorldProps.Lod(ch[c].Emit("DebrisChunk", holder, false, WorldProps.Atlas, WorldProps.Metal), 1000f);
            }
        }

        static Chunk At(Chunk[] ch, float s)
        {
            return ch[Mathf.Clamp((int)((s - WorldProps.SwathS0) / ChunkLen), 0, Chunks - 1)];
        }

        static Vector3 Corner(float yawDeg, float r, ref Rng rng)
        {
            float a = (yawDeg + rng.Range(-22f, 22f)) * Mathf.Deg2Rad, rr = r * rng.Range(0.6f, 1.3f);
            return new Vector3(Mathf.Sin(a) * rr, 0f, Mathf.Cos(a) * rr);
        }

        /// <summary>A lateral offset for a piece whose largest half-extent is e: shoulders and verges, the left lane,
        /// or the fields; never touching lateral 0.2 .. 3.6 (the truck's lane).</summary>
        static float Lateral(ref Rng rng, float e)
        {
            if (rng.Chance(0.45f))
                return (rng.Chance(0.5f) ? 1f : -1f) * rng.Range(17f + e, 150f);          // fields
            if (rng.Chance(0.5f))
                return rng.Range(4.1f + e, 16f + e);                                       // right shoulder and verge
            return -rng.Range(0.4f + e, 16f + e);                                          // left lane, shoulder, verge
        }

        static void Specials(Chunk[] ch)
        {
            // two twisted roof sections
            for (int k = 0; k < 2; k++)
            {
                float s = k == 0 ? 14180f : 14660f, lat = k == 0 ? -10.5f : 12.5f;
                Chunk c = At(ch, s);
                MeshBuilder mb = c.mb;
                c.At(s, lat, 0f, k == 0 ? 25f : -40f);
                mb.Swatch(Sw.RoofDark);
                Vector3 a0 = new Vector3(-3.8f, 0.08f, -2.5f), a1 = new Vector3(-3.8f, 0.08f, 2.5f);
                Vector3 b0 = new Vector3(-0.6f, 1.25f, -2.3f), b1 = new Vector3(-0.4f, 1.1f, 2.6f);
                Vector3 c0 = new Vector3(2.2f, 0.3f, -2.0f), c1 = new Vector3(2.6f, 0.9f, 2.4f);
                Vector3 d0 = new Vector3(4.1f, 1.4f, -1.2f), d1 = new Vector3(4.0f, 0.12f, 2.9f);
                WorldProps.Quad2(mb, a0, a1, b1, b0);
                WorldProps.Quad2(mb, b0, b1, c1, c0);
                WorldProps.Quad2(mb, c0, c1, d1, d0);
                mb.Swatch(Sw.DebrisWood);
                mb.Box(new Vector3(-2.2f, 0.32f, 0f), new Vector3(0.12f, 0.2f, 4.6f));
                mb.Box(new Vector3(1.0f, 0.3f, 0.2f), new Vector3(0.12f, 0.2f, 4.2f));
            }
            {   // a tipped trailer on the right verge, lying on its side
                const float s = 14420f;
                Chunk c = At(ch, s);
                MeshBuilder mb = c.mb;
                c.At(s, 13.2f, 0f, 8f);
                mb.Swatch(Sw.White);
                mb.Box(new Vector3(0f, 1.32f, 0f), new Vector3(2.7f, 2.6f, 12f), 4);
                mb.Swatch(Sw.Grey20);
                mb.Box(new Vector3(1.47f, 1.32f, -1f), new Vector3(0.24f, 0.3f, 9f));                 // the frame, now on the side
                mb.Swatch(Sw.Tyre);
                for (int w = 0; w < 4; w++)
                {
                    float y = w < 2 ? 0.25f : 2.05f, z = w % 2 == 0 ? -3.9f : -5.1f;
                    mb.Cylinder(new Vector3(1.95f, y, z), new Vector3(1.95f, y + 0.32f, z), 0.5f, 0.5f, 10, true);
                }
            }
            {   // an overturned tank on the left verge, legs in the air
                const float s = 13980f;
                Chunk c = At(ch, s);
                MeshBuilder mb = c.mb;
                c.At(s, -13.8f, 0f, 63f);
                mb.Swatch(Sw.Rust);
                mb.Cylinder(new Vector3(0f, 1.3f, -2.6f), new Vector3(0f, 1.3f, 2.6f), 1.3f, 1.3f, 12, true);
                mb.Swatch(Sw.Grey40);
                for (int sx = -1; sx <= 1; sx += 2)
                    for (int sz = -1; sz <= 1; sz += 2)
                        mb.Box(new Vector3(sx * 0.7f, 2.75f, sz * 1.8f), new Vector3(0.12f, 0.9f, 0.12f));
            }
        }
    }
}
