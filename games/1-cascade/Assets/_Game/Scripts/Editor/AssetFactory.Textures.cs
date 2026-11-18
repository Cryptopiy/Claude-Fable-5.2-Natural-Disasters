using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace CASCADE.EditorTools
{
    /// <summary>Procedural textures, generated in code and saved as assets (regenerated only when the version changes).</summary>
    public static partial class AssetFactory
    {
        const string TexVersion = "v5";

        static partial void BuildTextures()
        {
            string stamp = Gen + "/Textures/version.txt";
            bool fresh = !File.Exists(stamp) || File.ReadAllText(stamp) != TexVersion;
            AddTexture(Noise3D("T_PlumeNoise", 96, fresh));
            AddTexture(Noise2D("T_Noise2D", 256, fresh));
            AddTexture(Rock("T_RockAlbedo", 512, false, fresh));
            AddTexture(Rock("T_RockNormal", 512, true, fresh));
            AddTexture(WaterNormal("T_WaterNormal", 512, fresh));
            File.WriteAllText(stamp, TexVersion);
            FolTextures();
        }

        // ---------- hashing and tileable noise ----------
        static uint H(uint x) { x ^= x >> 16; x *= 0x7feb352du; x ^= x >> 15; x *= 0x846ca68bu; x ^= x >> 16; return x; }
        static float H01(int x, int y, int z, int s)
        {
            unchecked { return (H((uint)x * 73856093u ^ (uint)y * 19349663u ^ (uint)z * 83492791u ^ (uint)s * 2654435761u) & 0xFFFFFF) / 16777216f; }
        }
        static int Wrap(int a, int n) { int r = a % n; return r < 0 ? r + n : r; }

        /// <summary>Tileable Worley F1 distance, p in 0..1, freq cells per tile.</summary>
        static float Worley(float px, float py, float pz, int freq, int seed)
        {
            px *= freq; py *= freq; pz *= freq;
            int ix = Mathf.FloorToInt(px), iy = Mathf.FloorToInt(py), iz = Mathf.FloorToInt(pz);
            float best = 9f;
            for (int dz = -1; dz <= 1; dz++)
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int cx = ix + dx, cy = iy + dy, cz = iz + dz;
                        int wx = Wrap(cx, freq), wy = Wrap(cy, freq), wz = Wrap(cz, freq);
                        float fx = cx + H01(wx, wy, wz, seed) - px;
                        float fy = cy + H01(wx, wy, wz, seed + 17) - py;
                        float fz = cz + H01(wx, wy, wz, seed + 31) - pz;
                        float d = fx * fx + fy * fy + fz * fz;
                        if (d < best) best = d;
                    }
            return Mathf.Min(Mathf.Sqrt(best), 1f);
        }

        static float Value3(float px, float py, float pz, int freq, int seed)
        {
            px *= freq; py *= freq; pz *= freq;
            int ix = Mathf.FloorToInt(px), iy = Mathf.FloorToInt(py), iz = Mathf.FloorToInt(pz);
            float fx = px - ix, fy = py - iy, fz = pz - iz;
            fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy); fz = fz * fz * (3f - 2f * fz);
            float v = 0f;
            for (int k = 0; k < 8; k++)
            {
                int dx = k & 1, dy = (k >> 1) & 1, dz = (k >> 2) & 1;
                float w = (dx == 1 ? fx : 1f - fx) * (dy == 1 ? fy : 1f - fy) * (dz == 1 ? fz : 1f - fz);
                v += w * H01(Wrap(ix + dx, freq), Wrap(iy + dy, freq), Wrap(iz + dz, freq), seed);
            }
            return v;
        }

        static float Billow(float x, float y, float z, int f0, int seed)
        {
            // inverted Worley fbm: round lumps with sharp creases between them
            float a = 1f - Worley(x, y, z, f0, seed);
            float b = 1f - Worley(x, y, z, f0 * 2, seed + 101);
            float c = 1f - Worley(x, y, z, f0 * 4, seed + 202);
            return Mathf.Clamp01(a * 0.58f + b * 0.28f + c * 0.14f);
        }

        static Texture3D Noise3D(string name, int n, bool fresh)
        {
            string path = Gen + "/Textures/" + name + ".asset";
            var old = AssetDatabase.LoadAssetAtPath<Texture3D>(path);
            if (old != null && !fresh) return old;
            var px = new Color32[n * n * n];
            Parallel.For(0, n, z =>
            {
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float u = (x + 0.5f) / n, v = (y + 0.5f) / n, w = (z + 0.5f) / n;
                        float r = Billow(u, v, w, 3, 1);
                        float g = Value3(u, v, w, 4, 5) * 0.5f + Value3(u, v, w, 8, 6) * 0.27f + Value3(u, v, w, 16, 7) * 0.15f + Value3(u, v, w, 32, 8) * 0.08f;
                        float b = 1f - Worley(u, v, w, 9, 9);
                        float a = Billow(u, v, w, 5, 11);
                        px[(z * n + y) * n + x] = new Color32((byte)(r * 255f), (byte)(g * 255f), (byte)(b * 255f), (byte)(a * 255f));
                    }
            });
            var t = new Texture3D(n, n, n, TextureFormat.RGBA32, true) { name = name, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear };
            t.SetPixels32(px);
            t.Apply(true, false);
            if (old != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(t, path);
            return t;
        }

        static float Value2(float px, float py, int freq, int seed) { return Value3(px, py, 0.37f, freq, seed); }

        static Texture2D Noise2D(string name, int n, bool fresh)
        {
            string path = Gen + "/Textures/" + name + ".asset";
            var old = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (old != null && !fresh) return old;
            var px = new Color32[n * n];
            Parallel.For(0, n, y =>
            {
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                    float r = 0f, g = 0f, a = 0f, amp = 0.5f; int f = 4;
                    for (int o = 0; o < 5; o++) { r += amp * Value2(u, v, f, 21 + o); g += amp * Value2(u, v, f * 2, 41 + o); a += amp * Value2(u, v, f * 4, 61 + o); amp *= 0.5f; f *= 2; }
                    float b = 1f - Worley(u, v, 0.5f / 6f, 6, 77);       // 2D-ish cellular (thin slab of the 3D function)
                    px[y * n + x] = new Color32((byte)(Mathf.Clamp01(r / 0.97f) * 255f), (byte)(Mathf.Clamp01(g / 0.97f) * 255f), (byte)(Mathf.Clamp01(b) * 255f), (byte)(Mathf.Clamp01(a / 0.97f) * 255f));
                }
            });
            var t = new Texture2D(n, n, TextureFormat.RGBA32, true, true) { name = name, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            t.SetPixels32(px);
            t.Apply(true, false);
            if (old != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(t, path);
            return t;
        }

        /// <summary>Tiling wavelet slopes for the sea (RG = slope x,z around 0.5): mip-mapping fades them without aliasing.</summary>
        static Texture2D WaterNormal(string name, int n, bool fresh)
        {
            string path = Gen + "/Textures/" + name + ".asset";
            var old = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (old != null && !fresh) return old;
            var h = new float[n * n];
            Parallel.For(0, n, y =>
            {
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                    float f = 0f, amp = 0.5f; int fr = 5;
                    for (int o = 0; o < 6; o++)
                    {
                        float a = Value2(u, v, fr, 300 + o);
                        f += amp * (1f - Mathf.Abs(a * 2f - 1f));          // ridged: sharp little crests
                        amp *= 0.56f; fr *= 2;
                    }
                    h[y * n + x] = f;
                }
            });
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float hx = h[y * n + (x + 1) % n] - h[y * n + (x + n - 1) % n];
                    float hy = h[((y + 1) % n) * n + x] - h[((y + n - 1) % n) * n + x];
                    px[y * n + x] = new Color(Mathf.Clamp01(0.5f + hx * 4f), Mathf.Clamp01(0.5f + hy * 4f), Mathf.Clamp01((h[y * n + x] - 0.42f) / 0.56f), 1f);
                }
            var t = new Texture2D(n, n, TextureFormat.RGBA32, true, true) { name = name, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
            t.SetPixels32(px);
            t.Apply(true, false);
            if (old != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(t, path);
            return t;
        }

        /// <summary>Tiling rock: a neutral albedo detail (multiplied by the material colour) or its normal map (xy in RG, A = 1).</summary>
        static Texture2D Rock(string name, int n, bool normal, bool fresh)
        {
            string path = Gen + "/Textures/" + name + ".asset";
            var old = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (old != null && !fresh) return old;
            var h = new float[n * n];
            Parallel.For(0, n, y =>
            {
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                    float f = 0f, amp = 0.5f; int fr = 4;
                    for (int o = 0; o < 6; o++) { f += amp * Mathf.Abs(Value2(u, v, fr, 90 + o) * 2f - 1f); amp *= 0.5f; fr *= 2; }
                    float cells = Worley(u, v, 0.31f, 7, 55);
                    float strata = 0.5f + 0.5f * Mathf.Sin((v + 0.06f * Value2(u, v, 5, 3)) * Mathf.PI * 2f * 9f);
                    h[y * n + x] = Mathf.Clamp01(0.55f * (1f - f) + 0.3f * cells + 0.15f * strata);
                }
            });
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float c = h[y * n + x];
                    if (!normal)
                    {
                        float tone = 0.55f + 0.45f * c;
                        float warm = Value2((x + 0.5f) / n, (y + 0.5f) / n, 3, 123);
                        px[y * n + x] = new Color((tone * (0.95f + 0.1f * warm)), tone, tone * (1.02f - 0.12f * warm), 1f);
                    }
                    else
                    {
                        float hx = h[y * n + (x + 1) % n] - h[y * n + (x + n - 1) % n];
                        float hy = h[((y + 1) % n) * n + x] - h[((y + n - 1) % n) * n + x];
                        Vector3 nn = new Vector3(-hx * 9f, -hy * 9f, 1f).normalized;
                        px[y * n + x] = new Color(nn.x * 0.5f + 0.5f, nn.y * 0.5f + 0.5f, nn.z * 0.5f + 0.5f, 1f);
                    }
                }
            var t = new Texture2D(n, n, TextureFormat.RGBA32, true, normal) { name = name, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
            t.SetPixels32(px);
            t.Apply(true, false);
            if (old != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(t, path);
            return t;
        }

        // ---------- foliage and cloth (own stamp: the big noise volumes above are not rebuilt for them) ----------
        const string FolVersion = "f1";
        /// <summary>Alpha-test threshold of the leaf cards (M_LeafCard, M_BougCard); the atlas mips keep their coverage at it.</summary>
        public const float FolCutoff = 0.45f;
        static Texture2D folLeaf, folFlag, folLaundry;

        static void FolTextures()
        {
            string stamp = Gen + "/Textures/foliage_version.txt";
            bool fresh = !File.Exists(stamp) || File.ReadAllText(stamp) != FolVersion;
            folLeaf = FolLeafAtlas("T_LeafAtlas", 1024, fresh);
            folFlag = FolFlag("T_FlagGR", 384, 256, fresh);
            folLaundry = FolLaundry("T_Laundry", 256, fresh);
            AddTexture(folLeaf); AddTexture(folFlag); AddTexture(folLaundry);
            File.WriteAllText(stamp, FolVersion);
        }

        /// <summary>Float canvas for painting foliage: premultiplied linear colour and coverage, each stroke laid over the last.</summary>
        sealed class FolSheet
        {
            public readonly int n;
            public readonly float[] r, g, b, a;
            public FolSheet(int size) { n = size; r = new float[n * n]; g = new float[n * n]; b = new float[n * n]; a = new float[n * n]; }

            public void Over(int x, int y, Color c, float k)
            {
                if (k <= 0f) return;
                if (k > 1f) k = 1f;
                int i = y * n + x;
                float t = 1f - k;
                r[i] = c.r * k + r[i] * t; g[i] = c.g * k + g[i] * t; b[i] = c.b * k + b[i] * t; a[i] = k + a[i] * t;
            }
        }

        static float FolR(System.Random r, float a, float b) { return a + (b - a) * (float)r.NextDouble(); }

        /// <summary>
        /// One leaf (or bract) from its base (px, py) along angle ang, length len, half width wid (pixels), clipped to its cell
        /// (x0, y0, size cs). round < 1 makes it fuller. Darker towards the stalk, a lighter midrib, and an optional paler rim
        /// rimW pixels wide (bougainvillea bracts).
        /// </summary>
        static void FolLeaf(FolSheet s, int x0, int y0, int cs, float px, float py, float ang, float len, float wid, float round, Color col, Color rimCol, float rimW)
        {
            float ca = Mathf.Cos(ang), sa = Mathf.Sin(ang), ext = len + 2f;
            int xa = Mathf.Max(x0 + 1, Mathf.FloorToInt(px - ext)), xb = Mathf.Min(x0 + cs - 2, Mathf.CeilToInt(px + ext));
            int ya = Mathf.Max(y0 + 1, Mathf.FloorToInt(py - ext)), yb = Mathf.Min(y0 + cs - 2, Mathf.CeilToInt(py + ext));
            for (int y = ya; y <= yb; y++)
                for (int x = xa; x <= xb; x++)
                {
                    float dx = x + 0.5f - px, dy = y + 0.5f - py;
                    float u = dx * ca + dy * sa, v = -dx * sa + dy * ca;
                    float t = u / len;
                    if (t <= 0f || t >= 1f) continue;
                    float w = wid * Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Pow(t, 0.85f)), round);
                    float d = Mathf.Abs(v) - w;
                    float k = Mathf.Clamp01(0.5f - d);                                           // one pixel of antialiasing
                    if (k <= 0f) continue;
                    float shade = Mathf.Lerp(0.7f, 1.06f, Mathf.Sqrt(t)) * (1f + 0.14f * Mathf.Clamp01(1f - Mathf.Abs(v) / 1.3f) * (1f - t));
                    Color c = col * shade;
                    if (rimW > 0f) c = Color.Lerp(c, rimCol, 0.65f * Mathf.Clamp01(1f + d / rimW));
                    c.a = 1f;
                    s.Over(x, y, c, k);
                }
        }

        /// <summary>A round disc (petal, floret centre); rim colour towards its edge over rimW pixels.</summary>
        static void FolDisc(FolSheet s, int x0, int y0, int cs, float cx, float cy, float rad, Color col, Color rimCol, float rimW)
        {
            int xa = Mathf.Max(x0 + 1, Mathf.FloorToInt(cx - rad - 2f)), xb = Mathf.Min(x0 + cs - 2, Mathf.CeilToInt(cx + rad + 2f));
            int ya = Mathf.Max(y0 + 1, Mathf.FloorToInt(cy - rad - 2f)), yb = Mathf.Min(y0 + cs - 2, Mathf.CeilToInt(cy + rad + 2f));
            for (int y = ya; y <= yb; y++)
                for (int x = xa; x <= xb; x++)
                {
                    float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) - rad;
                    float k = Mathf.Clamp01(0.5f - d);
                    if (k <= 0f) continue;
                    Color c = rimW > 0f ? Color.Lerp(col, rimCol, Mathf.Clamp01(1f + d / rimW)) : col;
                    c.a = 1f;
                    s.Over(x, y, c, k);
                }
        }

        /// <summary>Geranium leaf: a round, scalloped blade with the darker horseshoe zone of the zonal pelargonium.</summary>
        static void FolRound(FolSheet s, int x0, int y0, int cs, float cx, float cy, float rad, float ph, Color col, Color zone)
        {
            float ext = rad * 1.1f + 2f;
            int xa = Mathf.Max(x0 + 1, Mathf.FloorToInt(cx - ext)), xb = Mathf.Min(x0 + cs - 2, Mathf.CeilToInt(cx + ext));
            int ya = Mathf.Max(y0 + 1, Mathf.FloorToInt(cy - ext)), yb = Mathf.Min(y0 + cs - 2, Mathf.CeilToInt(cy + ext));
            for (int y = ya; y <= yb; y++)
                for (int x = xa; x <= xb; x++)
                {
                    float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy), th = Mathf.Atan2(dy, dx);
                    float edge = rad * (1f + 0.06f * Mathf.Cos(9f * (th + ph)));
                    float k = Mathf.Clamp01(0.5f - (dist - edge));
                    if (k <= 0f) continue;
                    float rr = dist / rad;
                    float z = 0.65f * Mathf.Clamp01(1f - Mathf.Abs(rr - 0.56f) / 0.1f);
                    Color c = Color.Lerp(col, zone, z) * Mathf.Lerp(0.8f, 1.05f, Mathf.Clamp01(rr));
                    c.a = 1f;
                    s.Over(x, y, c, k);
                }
        }

        /// <summary>A tapering stroke along a polyline (twigs, grass blades): width and colour run from w0/c0 to w1/c1.</summary>
        static void FolStroke(FolSheet s, int x0, int y0, int cs, Vector2[] p, int np, float w0, float w1, Color c0, Color c1)
        {
            var cum = new float[np];
            for (int k = 1; k < np; k++) cum[k] = cum[k - 1] + Vector2.Distance(p[k - 1], p[k]);
            float L = cum[np - 1];
            if (L < 1e-3f) return;
            float pad = Mathf.Max(w0, w1) + 1.5f;
            float mnx = 1e9f, mny = 1e9f, mxx = -1e9f, mxy = -1e9f;
            for (int k = 0; k < np; k++) { mnx = Mathf.Min(mnx, p[k].x); mny = Mathf.Min(mny, p[k].y); mxx = Mathf.Max(mxx, p[k].x); mxy = Mathf.Max(mxy, p[k].y); }
            int xa = Mathf.Max(x0 + 1, Mathf.FloorToInt(mnx - pad)), xb = Mathf.Min(x0 + cs - 2, Mathf.CeilToInt(mxx + pad));
            int ya = Mathf.Max(y0 + 1, Mathf.FloorToInt(mny - pad)), yb = Mathf.Min(y0 + cs - 2, Mathf.CeilToInt(mxy + pad));
            for (int y = ya; y <= yb; y++)
                for (int x = xa; x <= xb; x++)
                {
                    Vector2 q = new Vector2(x + 0.5f, y + 0.5f);
                    float best = 1e9f, bt = 0f;
                    for (int k = 0; k < np - 1; k++)
                    {
                        Vector2 ab = p[k + 1] - p[k];
                        float l2 = ab.sqrMagnitude;
                        float tt = l2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(q - p[k], ab) / l2) : 0f;
                        float gt = (cum[k] + tt * (cum[k + 1] - cum[k])) / L;
                        float dd = Vector2.Distance(p[k] + ab * tt, q) - Mathf.Lerp(w0, w1, gt);
                        if (dd < best) { best = dd; bt = gt; }
                    }
                    float cov = Mathf.Clamp01(0.5f - best);
                    if (cov <= 0f) continue;
                    Color c = Color.Lerp(c0, c1, bt);
                    c.a = 1f;
                    s.Over(x, y, c, cov);
                }
        }

        /// <summary>Twigs radiating from the middle of a clump (drawn first, behind the leaves).</summary>
        static void FolTwigs(FolSheet s, int x0, int y0, int cs, System.Random r, int count, float reach)
        {
            float cx = x0 + cs * 0.5f, cy = y0 + cs * 0.5f;
            var pts = new Vector2[4];
            for (int i = 0; i < count; i++)
            {
                float a = (i + FolR(r, -0.3f, 0.3f)) / count * Mathf.PI * 2f, len = reach * FolR(r, 0.55f, 0.9f), bend = FolR(r, -0.35f, 0.35f);
                for (int k = 0; k < 4; k++)
                {
                    float t = k / 3f, aa = a + bend * t;
                    pts[k] = new Vector2(cx + Mathf.Cos(aa) * len * t, cy + Mathf.Sin(aa) * len * t);
                }
                FolStroke(s, x0, y0, cs, pts, 4, cs * 0.006f, cs * 0.0022f, new Color(0.055f, 0.045f, 0.025f), new Color(0.07f, 0.085f, 0.03f));
            }
        }

        /// <summary>Base point of a clump leaf: in the disc of radius R about the cell centre (denser inside), pointing roughly
        /// outwards, its tip kept inside the disc. Returns false when there is no room for it.</summary>
        static bool FolPlace(System.Random r, int x0, int y0, int cs, float R, ref float len, out float bx, out float by, out float ang, out float depth)
        {
            float cx = x0 + cs * 0.5f, cy = y0 + cs * 0.5f;
            float rr = R * Mathf.Sqrt(FolR(r, 0.01f, 0.8f)), th = FolR(r, 0f, Mathf.PI * 2f);
            bx = cx + Mathf.Cos(th) * rr; by = cy + Mathf.Sin(th) * rr;
            ang = th + FolR(r, -0.9f, 0.9f);
            depth = Mathf.Lerp(0.6f, 1f, rr / R);                 // inner leaves sit deeper in the clump: darker
            float tx = bx + Mathf.Cos(ang) * len - cx, ty = by + Mathf.Sin(ang) * len - cy;
            if (tx * tx + ty * ty > R * R)
            {
                ang = th;                                            // straight outwards, shortened to the rim
                len = Mathf.Min(len, R - rr);
            }
            return len > cs * 0.04f;
        }

        /// <summary>Cell 0: a sprig clump of 64 elliptic leaves, dark to mid green, darker towards the middle.</summary>
        static void FolSprigs(FolSheet s, int x0, int y0, int cs, System.Random r)
        {
            float R = cs * 0.44f;
            FolTwigs(s, x0, y0, cs, r, 7, R);
            for (int i = 0; i < 64; i++)
            {
                float len = cs * FolR(r, 0.1f, 0.15f), bx, by, ang, depth;
                if (!FolPlace(r, x0, y0, cs, R, ref len, out bx, out by, out ang, out depth)) continue;
                Color col = Color.Lerp(new Color(0.05f, 0.13f, 0.03f), new Color(0.12f, 0.22f, 0.06f), (float)r.NextDouble()) * (FolR(r, 0.8f, 1.2f) * depth);
                if (r.NextDouble() < 0.06) col = new Color(0.17f, 0.2f, 0.05f) * depth;     // an older, yellowing leaf
                FolLeaf(s, x0, y0, cs, bx, by, ang, len, len * FolR(r, 0.17f, 0.24f), 0.9f, col, col, 0f);
            }
        }

        /// <summary>Cell 1: bougainvillea, triads of magenta bracts with paler rims and cream flowers, a quarter green leaves behind.</summary>
        static void FolBracts(FolSheet s, int x0, int y0, int cs, System.Random r)
        {
            float R = cs * 0.44f, cx = x0 + cs * 0.5f, cy = y0 + cs * 0.5f;
            FolTwigs(s, x0, y0, cs, r, 5, R);
            for (int i = 0; i < 22; i++)
            {
                float len = cs * FolR(r, 0.11f, 0.15f), bx, by, ang, depth;
                if (!FolPlace(r, x0, y0, cs, R, ref len, out bx, out by, out ang, out depth)) continue;
                Color col = Color.Lerp(new Color(0.06f, 0.15f, 0.04f), new Color(0.1f, 0.2f, 0.05f), (float)r.NextDouble()) * depth;
                FolLeaf(s, x0, y0, cs, bx, by, ang, len, len * FolR(r, 0.25f, 0.32f), 0.8f, col, col, 0f);
            }
            for (int i = 0; i < 56; i++)
            {
                float bl = cs * FolR(r, 0.055f, 0.08f);
                float rr = (R - bl) * Mathf.Sqrt(FolR(r, 0f, 1f)), th = FolR(r, 0f, Mathf.PI * 2f);
                float tx = cx + Mathf.Cos(th) * rr, ty = cy + Mathf.Sin(th) * rr, a0 = FolR(r, 0f, Mathf.PI * 2f);
                float depth = Mathf.Lerp(0.7f, 1f, rr / R);
                Color col = Color.Lerp(new Color(0.55f, 0.03f, 0.23f), new Color(0.8f, 0.16f, 0.42f), (float)r.NextDouble()) * (FolR(r, 0.85f, 1.12f) * depth);
                Color rim = Color.Lerp(col, new Color(0.92f, 0.5f, 0.66f), 0.55f);
                for (int k = 0; k < 3; k++)
                    FolLeaf(s, x0, y0, cs, tx, ty, a0 + k * 2.094f + FolR(r, -0.2f, 0.2f), bl * FolR(r, 0.9f, 1.1f), bl * FolR(r, 0.34f, 0.42f), 0.6f, col, rim, 2.5f);
                Color cream = new Color(0.85f, 0.8f, 0.55f);
                for (int k = 0; k < 3; k++)
                    FolDisc(s, x0, y0, cs, tx + Mathf.Cos(a0 + 1.05f + k * 2.094f) * 2.5f, ty + Mathf.Sin(a0 + 1.05f + k * 2.094f) * 2.5f, 1.6f, cream, cream, 0f);
            }
        }

        /// <summary>Cell 2: geraniums, round zoned leaves under five scarlet (one coral) heads of five-petalled florets.</summary>
        static void FolGeraniums(FolSheet s, int x0, int y0, int cs, System.Random r)
        {
            float R = cs * 0.44f, cx = x0 + cs * 0.5f, cy = y0 + cs * 0.5f;
            for (int i = 0; i < 17; i++)
            {
                float rad = cs * FolR(r, 0.055f, 0.08f);
                float rr = (R - rad) * Mathf.Sqrt(FolR(r, 0f, 1f)), th = FolR(r, 0f, Mathf.PI * 2f);
                Color col = Color.Lerp(new Color(0.06f, 0.16f, 0.035f), new Color(0.1f, 0.22f, 0.05f), (float)r.NextDouble()) * Mathf.Lerp(0.7f, 1f, rr / R);
                FolRound(s, x0, y0, cs, cx + Mathf.Cos(th) * rr, cy + Mathf.Sin(th) * rr, rad, FolR(r, 0f, 1f), col, new Color(0.05f, 0.07f, 0.02f));
            }
            for (int h = 0; h < 5; h++)
            {
                float hr = cs * FolR(r, 0.055f, 0.075f);
                float rr = (R * 0.75f - hr) * Mathf.Sqrt(FolR(r, 0f, 1f)), th = FolR(r, 0f, Mathf.PI * 2f);
                float hx = cx + Mathf.Cos(th) * rr, hy = cy + Mathf.Sin(th) * rr;
                Color baseCol = h == 4 ? new Color(0.85f, 0.25f, 0.22f) : Color.Lerp(new Color(0.72f, 0.02f, 0.015f), new Color(0.86f, 0.1f, 0.025f), (float)r.NextDouble());
                for (int f = 0; f < 16; f++)
                {
                    float fr = hr * Mathf.Sqrt(FolR(r, 0f, 1f)), ft = FolR(r, 0f, Mathf.PI * 2f);
                    float fx = hx + Mathf.Cos(ft) * fr, fy = hy + Mathf.Sin(ft) * fr, pr = cs * FolR(r, 0.011f, 0.015f), a0 = FolR(r, 0f, 6.28f);
                    Color pc = baseCol * FolR(r, 0.85f, 1.1f) * Mathf.Lerp(0.75f, 1f, fr / hr);
                    for (int k = 0; k < 5; k++)
                        FolDisc(s, x0, y0, cs, fx + Mathf.Cos(a0 + k * 1.2566f) * pr * 0.85f, fy + Mathf.Sin(a0 + k * 1.2566f) * pr * 0.85f, pr, pc, pc * 0.8f, 1.5f);
                    FolDisc(s, x0, y0, cs, fx, fy, pr * 0.3f, new Color(0.9f, 0.7f, 0.5f), pc, 0f);
                }
            }
        }

        /// <summary>Cell 3: a tuft of dry grass and phrygana blades rising from the bottom edge (the card's foot), straw to olive.</summary>
        static void FolGrass(FolSheet s, int x0, int y0, int cs, System.Random r)
        {
            Color[] pal = { new Color(0.42f, 0.33f, 0.16f), new Color(0.3f, 0.22f, 0.1f), new Color(0.16f, 0.16f, 0.06f), new Color(0.08f, 0.14f, 0.035f), new Color(0.2f, 0.22f, 0.14f) };
            float[] wgt = { 0.3f, 0.25f, 0.25f, 0.1f, 0.1f };
            var pts = new Vector2[10];
            for (int i = 0; i < 72; i++)
            {
                float pick = (float)r.NextDouble(), acc = 0f; int ci = 0;
                for (int k = 0; k < pal.Length; k++) { acc += wgt[k]; if (pick <= acc) { ci = k; break; } }
                Color col = pal[ci] * FolR(r, 0.85f, 1.15f);
                float bx = x0 + cs * (0.5f + FolR(r, -0.1f, 0.1f)), by = y0 + cs * 0.02f;
                float lean = (FolR(r, -1f, 1f) + FolR(r, -1f, 1f)) * 0.32f, bend = lean * FolR(r, 0.3f, 0.9f);
                float len = cs * FolR(r, 0.42f, 0.92f);
                for (int tries = 0; tries < 8; tries++)
                {
                    bool fits = true;
                    Vector2 q = new Vector2(bx, by);
                    for (int k = 0; k < pts.Length; k++)
                    {
                        float t = k / (float)(pts.Length - 1);
                        if (k > 0)
                        {
                            float a = lean + bend * t * t;
                            q += new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * (len / (pts.Length - 1));
                        }
                        pts[k] = q;
                        if (q.x < x0 + 6f || q.x > x0 + cs - 6f || q.y > y0 + cs - 6f) fits = false;
                    }
                    if (fits) break;
                    len *= 0.85f;
                }
                FolStroke(s, x0, y0, cs, pts, pts.Length, cs * FolR(r, 0.006f, 0.01f), 0.45f, col * 0.5f, col * 1.12f);
                if (r.NextDouble() < 0.2)
                {
                    // a seed head at the tip
                    Vector2 d = pts[pts.Length - 1] - pts[pts.Length - 2];
                    Color st = new Color(0.5f, 0.42f, 0.24f) * FolR(r, 0.85f, 1.1f);
                    FolLeaf(s, x0, y0, cs, pts[pts.Length - 1].x, pts[pts.Length - 1].y, Mathf.Atan2(d.y, d.x), cs * 0.05f, cs * 0.012f, 1f, st, st, 0f);
                }
            }
        }

        /// <summary>
        /// The 2x2 leaf-card atlas (cell 0 shrub, 1 bougainvillea, 2 geranium, 3 dry grass): sRGB albedo, A = coverage.
        /// Transparent texels take their cell's mean leaf colour (filtering never pulls in black), and every mip level is
        /// built here with its alpha scaled until the share of texels above FolCutoff matches the full-size cell, so the
        /// alpha-tested cards do not thin out with distance.
        /// </summary>
        static Texture2D FolLeafAtlas(string name, int n, bool fresh)
        {
            string path = Gen + "/Textures/" + name + ".asset";
            var old = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (old != null && !fresh) return old;
            var s = new FolSheet(n);
            int c = n / 2;
            FolSprigs(s, 0, 0, c, new System.Random(101));
            FolBracts(s, c, 0, c, new System.Random(202));
            FolGeraniums(s, 0, c, c, new System.Random(303));
            FolGrass(s, c, c, c, new System.Random(404));

            // straight colour; the empty space carries the cell's mean leaf colour
            var R = new float[n * n]; var G = new float[n * n]; var B = new float[n * n]; var A = (float[])s.a.Clone();
            for (int cell = 0; cell < 4; cell++)
            {
                int cx0 = (cell & 1) * c, cy0 = (cell >> 1) * c;
                double mr = 0, mg = 0, mb = 0; int cnt = 0;
                for (int y = cy0; y < cy0 + c; y++)
                    for (int x = cx0; x < cx0 + c; x++)
                    {
                        int i = y * n + x;
                        if (s.a[i] < 0.5f) continue;
                        mr += s.r[i] / s.a[i]; mg += s.g[i] / s.a[i]; mb += s.b[i] / s.a[i]; cnt++;
                    }
                float fr = cnt > 0 ? (float)(mr / cnt) : 0.1f, fg = cnt > 0 ? (float)(mg / cnt) : 0.15f, fb = cnt > 0 ? (float)(mb / cnt) : 0.05f;
                for (int y = cy0; y < cy0 + c; y++)
                    for (int x = cx0; x < cx0 + c; x++)
                    {
                        int i = y * n + x;
                        float a = s.a[i], k = Mathf.Clamp01(a * 3f);
                        float sr = a > 1e-4f ? s.r[i] / a : fr, sg = a > 1e-4f ? s.g[i] / a : fg, sb = a > 1e-4f ? s.b[i] / a : fb;
                        R[i] = Mathf.Lerp(fr, sr, k); G[i] = Mathf.Lerp(fg, sg, k); B[i] = Mathf.Lerp(fb, sb, k);
                    }
            }

            var t = new Texture2D(n, n, TextureFormat.RGBA32, true, false) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            var target = new float[4];
            for (int cell = 0; cell < 4; cell++) target[cell] = FolCoverage(A, n, (cell & 1) * c, (cell >> 1) * c, c, 1f);
            int size = n;
            for (int lv = 0; lv < t.mipmapCount; lv++)
            {
                if (lv > 0)
                {
                    int ns = Mathf.Max(1, size >> 1);
                    var R2 = new float[ns * ns]; var G2 = new float[ns * ns]; var B2 = new float[ns * ns]; var A2 = new float[ns * ns];
                    for (int y = 0; y < ns; y++)
                        for (int x = 0; x < ns; x++)
                        {
                            int x2 = Mathf.Min(x * 2, size - 1), y2 = Mathf.Min(y * 2, size - 1), x3 = Mathf.Min(x2 + 1, size - 1), y3 = Mathf.Min(y2 + 1, size - 1);
                            int i0 = y2 * size + x2, i1 = y2 * size + x3, i2 = y3 * size + x2, i3 = y3 * size + x3;
                            float a0 = A[i0], a1 = A[i1], a2 = A[i2], a3 = A[i3], wa = a0 + a1 + a2 + a3;
                            int o = y * ns + x;
                            A2[o] = wa * 0.25f;
                            if (wa > 1e-5f)
                            {
                                R2[o] = (R[i0] * a0 + R[i1] * a1 + R[i2] * a2 + R[i3] * a3) / wa;
                                G2[o] = (G[i0] * a0 + G[i1] * a1 + G[i2] * a2 + G[i3] * a3) / wa;
                                B2[o] = (B[i0] * a0 + B[i1] * a1 + B[i2] * a2 + B[i3] * a3) / wa;
                            }
                            else
                            {
                                R2[o] = (R[i0] + R[i1] + R[i2] + R[i3]) * 0.25f; G2[o] = (G[i0] + G[i1] + G[i2] + G[i3]) * 0.25f; B2[o] = (B[i0] + B[i1] + B[i2] + B[i3]) * 0.25f;
                            }
                        }
                    R = R2; G = G2; B = B2; A = A2; size = ns;
                    int csz = size / 2;
                    if (csz >= 2)
                        for (int cell = 0; cell < 4; cell++)
                        {
                            int cx0 = (cell & 1) * csz, cy0 = (cell >> 1) * csz;
                            float lo = 0.3f, hi = 12f;
                            for (int it = 0; it < 18; it++)
                            {
                                float mid = 0.5f * (lo + hi);
                                if (FolCoverage(A, size, cx0, cy0, csz, mid) < target[cell]) lo = mid; else hi = mid;
                            }
                            float kk = 0.5f * (lo + hi);
                            for (int y = cy0; y < cy0 + csz; y++)
                                for (int x = cx0; x < cx0 + csz; x++) A[y * size + x] = Mathf.Min(1f, A[y * size + x] * kk);
                        }
                }
                var px = new Color32[size * size];
                for (int i = 0; i < px.Length; i++)
                    px[i] = new Color32(FolByte(Mathf.LinearToGammaSpace(Mathf.Clamp01(R[i]))), FolByte(Mathf.LinearToGammaSpace(Mathf.Clamp01(G[i]))),
                                        FolByte(Mathf.LinearToGammaSpace(Mathf.Clamp01(B[i]))), FolByte(Mathf.Clamp01(A[i])));
                t.SetPixels32(px, lv);
            }
            t.Apply(false, false);
            if (old != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(t, path);
            return t;
        }

        static byte FolByte(float v) { return (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255); }

        /// <summary>Share of the texels of one cell whose alpha, scaled by k, passes the alpha test.</summary>
        static float FolCoverage(float[] A, int size, int cx0, int cy0, int csz, float k)
        {
            int pass = 0;
            for (int y = cy0; y < cy0 + csz; y++)
                for (int x = cx0; x < cx0 + csz; x++)
                    if (A[y * size + x] * k > FolCutoff) pass++;
            return pass / (float)(csz * csz);
        }

        /// <summary>The Greek flag (3:2): nine blue and white stripes, the white cross on the blue canton; a faint weave and the
        /// hoist sleeve. u runs from the hoist (0) to the fly, v from the bottom to the top.</summary>
        static Texture2D FolFlag(string name, int w, int h, bool fresh)
        {
            string path = Gen + "/Textures/" + name + ".asset";
            var old = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (old != null && !fresh) return old;
            Color blue = new Color(13f / 255f, 94f / 255f, 175f / 255f).linear, white = new Color(0.95f, 0.95f, 0.93f).linear;
            float stripe = h / 9f, canton = stripe * 5f;
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float fb = 0f;
                    for (int sy = 0; sy < 3; sy++)
                        for (int sx = 0; sx < 3; sx++)
                        {
                            float fx = x + (sx + 0.5f) / 3f, top = h - (y + (sy + 0.5f) / 3f);
                            bool isBlue;
                            if (fx < canton && top < canton)
                                isBlue = !(Mathf.Abs(fx - canton * 0.5f) < stripe * 0.5f || Mathf.Abs(top - canton * 0.5f) < stripe * 0.5f);
                            else isBlue = ((int)(top / stripe) & 1) == 0;
                            if (isBlue) fb += 1f / 9f;
                        }
                    Color col = Color.Lerp(white, blue, fb);
                    float weave = 1f + 0.035f * (H01(x, y, 0, 909) - 0.5f) + 0.02f * (((x + y) & 1) == 0 ? 1f : -1f);
                    if (x < w * 0.025f) col = white * 0.82f;                     // the sleeve round the halyard
                    col *= weave;
                    px[y * w + x] = new Color32(FolByte(Mathf.LinearToGammaSpace(Mathf.Clamp01(col.r))), FolByte(Mathf.LinearToGammaSpace(Mathf.Clamp01(col.g))),
                                                FolByte(Mathf.LinearToGammaSpace(Mathf.Clamp01(col.b))), 255);
                }
            var t = new Texture2D(w, h, TextureFormat.RGBA32, true, false) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            t.SetPixels32(px);
            t.Apply(true, false);
            if (old != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(t, path);
            return t;
        }

        /// <summary>Laundry, 2x2 cells: a white towel with two blue bands, a pale blue pin-striped shirt cloth, a rose and an
        /// ochre cloth; darker hems, a faint weave. u along the line, v from the hem (0) up to the pegs.</summary>
        static Texture2D FolLaundry(string name, int n, bool fresh)
        {
            string path = Gen + "/Textures/" + name + ".asset";
            var old = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (old != null && !fresh) return old;
            Color[] cloth = { new Color(0.8f, 0.8f, 0.78f), new Color(0.42f, 0.56f, 0.72f), new Color(0.72f, 0.36f, 0.38f), new Color(0.74f, 0.5f, 0.15f) };
            Color band = new Color(0.1f, 0.22f, 0.52f);
            int c = n / 2;
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int cell = (x / c) + 2 * (y / c), lx = x % c, ly = y % c;
                    Color col = cloth[cell];
                    float v = ly / (float)c;
                    if (cell == 0 && ((v > 0.12f && v < 0.19f) || (v > 0.23f && v < 0.26f))) col = band;          // towel bands near the hem
                    if (cell == 1 && lx % 8 == 0) col = Color.Lerp(col, Color.white, 0.4f);                         // pin stripes
                    int edge = Mathf.Min(Mathf.Min(lx, c - 1 - lx), Mathf.Min(ly, c - 1 - ly));
                    if (edge < 4) col *= 0.82f;                                                                      // hem
                    col *= 1f + 0.05f * (H01(x, y, 1, 717) - 0.5f) + 0.025f * ((x & 1) == 0 ? 1f : -1f);
                    px[y * n + x] = new Color32(FolByte(Mathf.LinearToGammaSpace(Mathf.Clamp01(col.r))), FolByte(Mathf.LinearToGammaSpace(Mathf.Clamp01(col.g))),
                                                FolByte(Mathf.LinearToGammaSpace(Mathf.Clamp01(col.b))), 255);
                }
            var t = new Texture2D(n, n, TextureFormat.RGBA32, true, false) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            t.SetPixels32(px);
            t.Apply(true, false);
            if (old != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(t, path);
            return t;
        }
    }
}
