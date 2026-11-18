using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZERO
{
    /// <summary>The cloud deck below the aircraft: fair-weather cumulus at 2-4 km. One coverage map (a texture) decides
    /// where clouds are; near the aircraft they are low-poly bodies in tiles that come and go with the flight, beyond
    /// the hand-over distance the land shader draws the same map as a flat layer; both throw the same shadows.</summary>
    public static class CloudBuilder
    {
        public const float Period = 131072f;            // metres covered by the coverage map (mirrored repeat)
        const int Res = 512;
        public const float Threshold = 0.56f;
        public const float BaseHeight = 2200f, TopHeight = 3300f;
        public const float HandOver = 62000f;           // meshes inside, the flat layer outside
        const float Tile = 16384f; const int Cells = 12;
        const float BuildRange = 74000f, DropRange = 92000f;

        static float[] cov;
        static readonly Dictionary<long, GameObject> tiles = new Dictionary<long, GameObject>();
        static readonly List<long> scratch = new List<long>();
        static Transform root;
        static float timer;
        public static int TileCount { get { return tiles.Count; } }

        public static void Build(Transform parent)
        {
            root = new GameObject("Clouds").transform; root.SetParent(parent, false);
            tiles.Clear();
            cov = new float[Res * Res];
            System.Threading.Tasks.Parallel.For(0, Res, y =>
            {
                for (int x = 0; x < Res; x++)
                {
                    float px = (x + 0.5f) / Res * Period, pz = (y + 0.5f) / Res * Period;
                    // cells of cumulus, gathered into streets and broken by clear lanes
                    float cells = Noise.Fbm(px / 5200f + 3.1f, 7.7f, pz / 5200f, 4);
                    float lanes = Noise.Fbm(px / 47000f + 11f, 2.3f, pz / 47000f, 3);
                    cov[y * Res + x] = Mathf.Clamp01(0.5f + 0.95f * cells + 0.55f * lanes);
                }
            });
            var px8 = new byte[Res * Res];
            for (int i = 0; i < px8.Length; i++) px8[i] = (byte)Mathf.RoundToInt(cov[i] * 255f);
            var tex = new Texture2D(Res, Res, TextureFormat.R8, false, true) { name = "CloudCoverage", wrapMode = TextureWrapMode.Mirror, filterMode = FilterMode.Bilinear };
            tex.LoadRawTextureData(px8); tex.Apply(false, true);
            Shader.SetGlobalTexture("_ZCloudTex", tex);
            Shader.SetGlobalVector("_ZCloudParams", new Vector4(1f / Period, Threshold, TopHeight, HandOver));
            BuildAll();
            Tick(Schedule.PlanePos(0f), true);
        }

        /// <summary>The coverage map at a point of the flat map (the same value the shaders sample).</summary>
        public static float Coverage(float x, float z)
        {
            float u = Mirror(x / Period) * Res - 0.5f, v = Mirror(z / Period) * Res - 0.5f;
            int x0 = Mathf.Clamp(Mathf.FloorToInt(u), 0, Res - 1), y0 = Mathf.Clamp(Mathf.FloorToInt(v), 0, Res - 1);
            int x1 = Mathf.Min(x0 + 1, Res - 1), y1 = Mathf.Min(y0 + 1, Res - 1);
            float fx = Mathf.Clamp01(u - x0), fy = Mathf.Clamp01(v - y0);
            return Mathf.Lerp(Mathf.Lerp(cov[y0 * Res + x0], cov[y0 * Res + x1], fx), Mathf.Lerp(cov[y1 * Res + x0], cov[y1 * Res + x1], fx), fy);
        }

        static float Mirror(float t) { float f = t - Mathf.Floor(t); return (Mathf.FloorToInt(t) & 1) == 0 ? f : 1f - f; }

        static long Key(int cx, int cz) { return ((long)cx << 32) | (uint)cz; }

        /// <summary>Keeps the tiles around the aircraft: builds what is missing (one per call unless 'all'), drops what is far behind.</summary>
        /// <summary>Builds every tile the flight will ever see (at load: no generation inside frames).</summary>
        static void BuildAll()
        {
            float z0 = Schedule.PlanePos(-20f).z - BuildRange, z1 = Schedule.PlanePos(Schedule.ArrivalTime + 30f).z + BuildRange;
            int c0x = Mathf.FloorToInt(-BuildRange / Tile), c1x = Mathf.FloorToInt(BuildRange / Tile);
            int c0z = Mathf.FloorToInt(z0 / Tile), c1z = Mathf.FloorToInt(z1 / Tile);
            for (int cz = c0z; cz <= c1z; cz++)
                for (int cx = c0x; cx <= c1x; cx++)
                {
                    var go = BuildTile(cx, cz);
                    go.SetActive(false);
                    tiles[Key(cx, cz)] = go;
                }
        }

        /// <summary>Shows the tiles near the aircraft (a few times per second, or at once after a jump).</summary>
        public static void Tick(Vector3 eyeAbs, bool all)
        {
            if (cov == null) return;
            if (!all) { timer -= Time.unscaledDeltaTime; if (timer > 0f) return; timer = 0.25f; }
            foreach (var kv in tiles)
            {
                int cx = (int)(kv.Key >> 32), cz = (int)(kv.Key & 0xffffffffL);
                float dx = (cx + 0.5f) * Tile - eyeAbs.x, dz = (cz + 0.5f) * Tile - eyeAbs.z;
                bool on = dx * dx + dz * dz < DropRange * DropRange;
                if (kv.Value != null && kv.Value.activeSelf != on) kv.Value.SetActive(on);
            }
        }

        static GameObject BuildTile(int cx, int cz)
        {
            Ico.Get(2, out var sv, out var st);
            Ico.Get(1, out var sv1, out var st1);                    // the small detail puffs need fewer vertices
            var V = new List<Vector3>(16384); var C = new List<Color32>(16384); var U = new List<Vector4>(16384); var U1 = new List<Vector4>(16384); var T = new List<int>(65536);
            var rng = new Rng((uint)(cx * 73856093) ^ (uint)(cz * 19349663) ^ 0x9e3779b9u);
            float cell = Tile / Cells;
            Vector3 origin = new Vector3(cx * Tile, 0f, cz * Tile);
            Vector3 centre = Vector3.zero; float R = 1f, top = 1f;

            // one puff: a squashed, lumpy sphere with a flat base, tagged with its own centre (smooth shading, soft limb)
            void Puff(Vector3 c, float rb, Vector3[] verts, int[] tris, float seed, float rnd)
            {
                int b0 = V.Count;
                for (int s = 0; s < verts.Length; s++)
                {
                    Vector3 d = verts[s];
                    float bump = 1f + 0.2f * Noise.Perlin(d.x * 2.1f + seed, d.y * 2.1f, d.z * 2.1f + seed * 0.7f);
                    Vector3 p = c + new Vector3(d.x * rb * bump, d.y * rb * 0.74f * bump, d.z * rb * bump);
                    if (p.y < BaseHeight) p.y = BaseHeight;          // the flat base of a cumulus
                    float h01 = Mathf.Clamp01((p.y - BaseHeight) / (top - BaseHeight));
                    V.Add(p);
                    C.Add(new Color(0.35f + 0.65f * h01, h01, rnd, 1f));
                    U.Add(new Vector4(centre.x, centre.y, centre.z, R));
                    U1.Add(new Vector4(c.x, c.y, c.z, rb));
                }
                for (int s = 0; s < tris.Length; s++) T.Add(b0 + tris[s]);
            }

            for (int j = 0; j < Cells; j++)
                for (int i = 0; i < Cells; i++)
                {
                    float qx = (i + 0.5f + rng.Signed() * 0.34f) * cell, qz = (j + 0.5f + rng.Signed() * 0.34f) * cell;
                    float cv = Coverage(origin.x + qx, origin.z + qz) + rng.Signed() * 0.03f;
                    if (cv < Threshold + 0.015f) { rng.F(); continue; }
                    float k = Mathf.Clamp01((cv - Threshold) / 0.2f);
                    R = Mathf.Lerp(480f, 1500f, k) * rng.Range(0.85f, 1.2f);
                    centre = new Vector3(qx, BaseHeight + R * 0.42f, qz);
                    top = BaseHeight + R * 1.25f;
                    int blobs = 4 + Mathf.RoundToInt(k * 5f);
                    float stretch = rng.Range(0.8f, 1.35f), turn = rng.F() * 6.2832f;      // clouds drawn out along the wind
                    for (int b = 0; b < blobs; b++)
                    {
                        Vector2 o = b == 0 ? Vector2.zero : rng.InCircle() * R * 0.78f;
                        o = new Vector2(o.x * stretch, o.y / stretch);
                        o = new Vector2(o.x * Mathf.Cos(turn) - o.y * Mathf.Sin(turn), o.x * Mathf.Sin(turn) + o.y * Mathf.Cos(turn));
                        float edge = 1f - 0.4f * Mathf.Min(o.magnitude / R, 1.2f);
                        float u = rng.F(); float rb = R * (b == 0 ? 0.74f : 0.3f + 0.6f * u * u) * edge;     // a few big towers, many small puffs
                        Vector3 c = new Vector3(qx + o.x, BaseHeight + rb * rng.Range(0.35f, 0.7f) * (b == 0 ? 1.25f : 1f), qz + o.y);
                        Puff(c, rb, sv, st, rng.F() * 31f, rng.F());
                    }
                    // cauliflower: towers swelling out of the top, sunk deep into the body (no beads stuck on it)
                    int caps = 1 + Mathf.RoundToInt(k * 2f);
                    for (int d = 0; d < caps; d++)
                    {
                        Vector2 o = rng.InCircle() * R * 0.4f;
                        float rd = R * rng.Range(0.3f, 0.42f);
                        Puff(new Vector3(qx + o.x, BaseHeight + R * rng.Range(0.55f, 0.82f), qz + o.y), rd, sv1, st1, rng.F() * 31f, rng.F());
                    }
                }            var go = new GameObject("CloudTile_" + cx + "_" + cz);
            go.transform.SetParent(root, false); go.transform.localPosition = origin;
            if (V.Count == 0) return go;
            var mesh = new Mesh { name = "CloudTile", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(V); mesh.SetColors(C); mesh.SetUVs(0, U); mesh.SetUVs(1, U1); mesh.SetTriangles(T, 0, false);
            mesh.bounds = new Bounds(new Vector3(Tile * 0.5f, 3500f, Tile * 0.5f), new Vector3(Tile + 12000f, 9000f, Tile + 12000f));
            mesh.UploadMeshData(true);
            Ctx.Renderer(go, mesh, Ctx.A.Cloud, Ctx.LayerWorld);
            return go;
        }
    }
}
