using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace MESOCYCLONE
{
    /// <summary>3D grass on both verges and ripe wheat in the wheat fields along the road, near the truck only (the
    /// photo-textured ground carries everything farther away). Instanced HDRP Lit meshes in 8 m tiles that are made
    /// as the truck reaches them (deterministic: the same chainage always grows the same plants) and dropped behind it.
    /// Every plant leans with the story's wind: a steady lean, gust waves that run downwind across the land, and a
    /// small flutter of its own. In the calm before the storm the grass stands still; the gust front lays it flat.
    /// Instances carry this and the previous frame's matrix, so TAA sees them move (the world slides past the truck).</summary>
    [DefaultExecutionOrder(200)]
    public sealed class GrassField : MonoBehaviour
    {
        [StructLayout(LayoutKind.Sequential)]
        struct InstData { public Matrix4x4 objectToWorld; public Matrix4x4 prevObjectToWorld; }

        struct Plant
        {
            public float x, z;          // offset from the tile origin (world axes, metres)
            public float cy, sy;        // yaw of the mesh
            public float width, height; // horizontal and vertical scale
            public float flex;          // how far it gives to the wind (wheat is stiffer than grass)
            public float phase;         // flutter phase
            public byte group;          // draw group (mesh)
            public bool wheat;
        }

        sealed class Tile
        {
            public int index = int.MinValue;
            public double ox, oz;       // world position of the tile origin (road centre at the tile start)
            public float wx, wz;        // the same, as floats relative to the path start (for the gust waves)
            // sized for the densest tile (both verges and wheat on both sides): no growth, no garbage during the ride
            public Plant[] plants = new Plant[1024];
            public Matrix4x4[] prev = new Matrix4x4[1024];
            public bool[] hasPrev = new bool[1024];
            public int count;
        }

        const float TileLen = 8f;
        const int TuftMeshes = 8, WheatNear = 2, WheatFar = 2;
        const int Groups = TuftMeshes + WheatNear + WheatFar;

        Mesh[] meshes;
        InstData[][] buf;
        int[] counts;
        Tile[] tiles;
        RenderParams rp;
        Material mat;
        float ahead, behind, wheatReach, density;
        bool on;

        public static void Build(Transform parent)
        {
            if (Tune.F("veg.on", 1f) < 0.5f) return;
            var go = new GameObject("GrassField");
            go.transform.SetParent(parent, false);
            go.AddComponent<GrassField>().Init();
        }

        void Init()
        {
            Material src = WeatherLibrary.Mat(GrassLibrary.VegMat, "atlas");
            if (src == null) return;
            mat = new Material(src) { name = "veg_live", enableInstancing = true };
            ahead = Tune.F("veg.ahead", Cli.Final ? 150f : 120f);
            behind = Tune.F("veg.behind", 14f);
            wheatReach = Tune.F("veg.wheatReach", Cli.Final ? 46f : 38f);
            density = Tune.F("veg.density", 1f);

            meshes = new Mesh[Groups];
            // tufts: four palettes (lush, mixed, dry verge, wet ditch), two shapes each
            for (int i = 0; i < TuftMeshes; i++)
                meshes[i] = GrassLibrary.Tuft(i % 4, 4001u + (uint)i * 97u, i < 4 ? 22 : 16, i < 4 ? 4 : 3, "Tuft" + i);
            for (int i = 0; i < WheatNear; i++) meshes[TuftMeshes + i] = GrassLibrary.WheatPatch(5001u + (uint)i, true, "WheatNear" + i);
            for (int i = 0; i < WheatFar; i++) meshes[TuftMeshes + WheatNear + i] = GrassLibrary.WheatPatch(5001u + (uint)i, false, "WheatFar" + i);

            int nt = Mathf.CeilToInt((ahead + behind) / TileLen) + 3;
            tiles = new Tile[nt];
            for (int i = 0; i < nt; i++) tiles[i] = new Tile();
            buf = new InstData[Groups][];
            counts = new int[Groups];
            // preallocated for the worst case seen at 1080p final (no resizing, no garbage collection mid-ride)
            for (int g = 0; g < Groups; g++) buf[g] = new InstData[g < TuftMeshes ? 2048 : g < TuftMeshes + WheatNear ? 1536 : 4096];

            rp = new RenderParams(mat);
            rp.layer = 0;
            rp.shadowCastingMode = ShadowCastingMode.Off;
            rp.receiveShadows = true;
            rp.lightProbeUsage = LightProbeUsage.Off;
            rp.motionVectorMode = MotionVectorGenerationMode.Object;
            rp.worldBounds = new Bounds(new Vector3(0f, 1f, 40f), new Vector3(2f * ahead + 120f, 12f, 2f * ahead + 60f));
            on = true;
        }

        // ---------------------------------------------------------------- tiles

        static uint Hash(uint x)
        {
            unchecked { x ^= x >> 16; x *= 0x7feb352du; x ^= x >> 15; x *= 0x846ca68bu; x ^= x >> 16; }
            return x;
        }
        static float U(ref uint h) { h = Hash(h + 0x9E3779B9u); return (h & 0xFFFFFF) / 16777216f; }

        void Add(Tile t, in Plant p)
        {
            if (t.count == t.plants.Length)
            {
                System.Array.Resize(ref t.plants, t.count * 2);
                System.Array.Resize(ref t.prev, t.count * 2);
                System.Array.Resize(ref t.hasPrev, t.count * 2);
            }
            t.hasPrev[t.count] = false;
            t.plants[t.count++] = p;
        }

        void Make(Tile t, int index)
        {
            t.index = index; t.count = 0;
            double s0 = index * (double)TileLen;
            PathDef.RoadToWorld(s0, 0.0, out t.ox, out t.oz);
            t.wx = (float)t.ox; t.wz = (float)t.oz;
            Vector3 fwd = PathDef.Forward(s0), right = PathDef.Right(s0);
            float shoulder = Config.RoadHalfWidth + Config.ShoulderWidth;
            uint h = Hash((uint)(index + 100000) * 2654435761u + 0x5bd1e995u);

            for (int side = -1; side <= 1; side += 2)
            {
                // no grass on the gravel side roads, nor under the gas-station canopy at the goal
                bool skipVerge = false;
                foreach (double cs in PathDef.Crossroads) if (System.Math.Abs(s0 + TileLen * 0.5 - cs) < 10.0) skipVerge = true;
                if (side > 0 && s0 > Director.GoalS - 240.0) skipVerge = true;

                // ---- verge: from the gravel fringe to the field edge (16 m); sparse and short on the dry strip by the
                //      gravel, lush in the ditch, tall toward the field ----
                if (!skipVerge)
                {
                    int n = Mathf.RoundToInt(TileLen * 10.6f * 2.3f * density);
                    for (int i = 0; i < n; i++)
                    {
                        float a = U(ref h) * TileLen;
                        float d = 0.35f + U(ref h) * 10.5f;                  // metres from the shoulder edge
                        float keep = d < 0.8f ? 0.15f : d < 2.6f ? 0.5f : 1f;
                        if (U(ref h) > keep) continue;
                        float lat = side * (shoulder + d);
                        Vector3 o = fwd * a + right * lat;
                        int pal = d < 2.8f ? (U(ref h) < 0.7f ? 2 : 1) : (Mathf.Abs(d - 5.4f) < 1.6f ? (U(ref h) < 0.6f ? 3 : 0) : (U(ref h) < 0.55f ? 0 : 1));
                        float ht = d < 2.8f ? Mathf.Lerp(0.2f, 0.42f, U(ref h)) : d < 7.5f ? Mathf.Lerp(0.35f, 0.7f, U(ref h)) : Mathf.Lerp(0.55f, 1.05f, U(ref h));
                        float yaw = U(ref h) * Mathf.PI * 2f;
                        Add(t, new Plant
                        {
                            x = o.x, z = o.z, cy = Mathf.Cos(yaw), sy = Mathf.Sin(yaw),
                            width = Mathf.Lerp(1.0f, 1.9f, U(ref h)) * (0.75f + 0.5f * ht), height = ht,
                            flex = Mathf.Lerp(0.85f, 1.15f, U(ref h)), phase = U(ref h) * 6.283f,
                            group = (byte)(pal + (U(ref h) < 0.5f ? 0 : 4)), wheat = false
                        });
                    }
                }

                // ---- wheat: jittered 1.4 m grid from the field edge out to wheatReach ----
                int crop = Land.NearCrop(s0 + TileLen * 0.5, side, out float fw);
                if (crop == 0)
                {
                    float lat1 = Mathf.Min(16f + fw, wheatReach);
                    const float cell = 1.35f;
                    for (float a = 0.35f; a < TileLen; a += cell)
                        for (float d = 16.7f; d < lat1; d += cell)
                        {
                            float aa = a + (U(ref h) - 0.5f) * 0.5f, dd = d + (U(ref h) - 0.5f) * 0.5f;
                            double s = s0 + aa;
                            if (Land.NearCrop(s, side, out _) != 0) continue;
                            Vector3 o = fwd * aa + right * (side * dd);
                            float yaw = U(ref h) * Mathf.PI * 2f;
                            Add(t, new Plant
                            {
                                x = o.x, z = o.z, cy = Mathf.Cos(yaw), sy = Mathf.Sin(yaw),
                                width = Mathf.Lerp(0.95f, 1.1f, U(ref h)), height = Mathf.Lerp(0.82f, 1.0f, U(ref h)),
                                flex = Mathf.Lerp(0.7f, 0.9f, U(ref h)), phase = U(ref h) * 6.283f,
                                group = (byte)(U(ref h) < 0.5f ? 0 : 1), wheat = true
                            });
                        }
                }
            }
        }

        // ---------------------------------------------------------------- frame

        void LateUpdate()
        {
            if (!on || !Director.Running) return;
            StoryState s = Director.S;
            double truckS = Director.TruckS;
            int i0 = Mathf.FloorToInt((float)((truckS - behind) / TileLen)), i1 = Mathf.FloorToInt((float)((truckS + ahead) / TileLen));
            for (int g = 0; g < Groups; g++) counts[g] = 0;

            // ---- the wind of this frame ----
            float t = Director.Ambient;
            float wd = s.windDir * Mathf.Deg2Rad;
            float dx = Mathf.Sin(wd), dz = Mathf.Cos(wd);                 // the wind blows TO this direction
            float wind = Mathf.Max(0f, s.wind);
            float lean0 = 1.12f * (1f - Mathf.Exp(-wind / 15f));
            float lambda = Mathf.Lerp(11f, 26f, Mathf.Clamp01(wind / 36f));
            float k = 6.2832f / lambda;
            float omega = k * (3f + wind * 0.55f);                        // the gust waves run downwind
            float gustAmp = 0.35f + 0.5f * Mathf.Clamp01(s.gust);
            float flutterAmp = 0.025f + 0.009f * wind;
            float flutterW = 4f + 0.12f * wind;
            Vector3 fwd = PathDef.Forward(truckS);
            float wheatNear2 = 32f * 32f;

            for (int ti = i0; ti <= i1; ti++)
            {
                Tile tile = tiles[((ti % tiles.Length) + tiles.Length) % tiles.Length];
                if (tile.index != ti) Make(tile, ti);
                float lx = (float)(tile.ox - Director.OriginX), lz = (float)(tile.oz - Director.OriginZ);
                for (int pi = 0; pi < tile.count; pi++)
                {
                    ref Plant p = ref tile.plants[pi];
                    float px = lx + p.x, pz = lz + p.z;
                    if (px * fwd.x + pz * fwd.z < -6f) { tile.hasPrev[pi] = false; continue; }   // behind the cab

                    // lean: steady part + a travelling gust wave (its fronts wander) + the plant's own flutter
                    float wx = tile.wx + p.x, wz = tile.wz + p.z;
                    float across = wx * dz - wz * dx;
                    float wave = 0.5f + 0.5f * Mathf.Sin((wx * dx + wz * dz) * k - t * omega + 1.7f * Mathf.Sin(across * 0.09f + t * 0.31f));
                    float lean = lean0 * (1f - gustAmp + gustAmp * 1.35f * wave) + flutterAmp * Mathf.Sin(t * flutterW + p.phase);
                    lean = Mathf.Clamp(lean * p.flex, -0.3f, 1.3f);
                    float sl = Mathf.Sin(lean), cl = Mathf.Cos(lean);

                    int g = p.group;
                    if (p.wheat) g = TuftMeshes + (px * px + pz * pz < wheatNear2 ? g : WheatNear + g);
                    Matrix4x4 m;
                    m.m00 = p.cy * p.width; m.m10 = 0f; m.m20 = -p.sy * p.width; m.m30 = 0f;
                    m.m01 = sl * dx * p.height; m.m11 = cl * p.height; m.m21 = sl * dz * p.height; m.m31 = 0f;
                    m.m02 = p.sy * p.width; m.m12 = 0f; m.m22 = p.cy * p.width; m.m32 = 0f;
                    m.m03 = px; m.m13 = 0.005f; m.m23 = pz; m.m33 = 1f;

                    int c = counts[g];
                    if (c == buf[g].Length) System.Array.Resize(ref buf[g], c * 2);
                    buf[g][c].objectToWorld = m;
                    buf[g][c].prevObjectToWorld = tile.hasPrev[pi] ? tile.prev[pi] : m;
                    counts[g] = c + 1;
                    tile.prev[pi] = m; tile.hasPrev[pi] = true;
                }
            }

            for (int g = 0; g < Groups; g++)
                if (counts[g] > 0) Graphics.RenderMeshInstanced(rp, meshes[g], 0, buf[g], counts[g]);
        }
    }
}
