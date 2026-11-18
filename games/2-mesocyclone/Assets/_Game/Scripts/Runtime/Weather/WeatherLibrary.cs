using System.Collections.Generic;
using UnityEngine;

namespace MESOCYCLONE
{
    /// <summary>Materials and generated textures of the near-truck weather: 3D hail ice (stones and chips) and the
    /// debris the wind throws through the headlights (beat 6 and the gust front). Registered by Library.Materials()
    /// and Library.Textures() (one line each in GameAssets.cs). Also holds small shared helpers (glass normal,
    /// material lookup with fallback, hashes).</summary>
    public static class WeatherLibrary
    {
        public const string IceMat = "hail_ice", DebrisMat = "debris", DebrisMetalMat = "debris_metal";
        public const string IceAlbedo = "hail_alb", IceNormal = "hail_n", DebrisAlbedo = "debris_alb", DebrisNormal = "debris_n";

        // debris atlas: 4 x 4 cells of 256 px (cell 0 bottom left)
        public const int Grid = 4;
        public const int LeafGreen = 0, LeafDry = 1, Bark = 2, Plank = 3, Shingle = 4, Insulation = 5, Siding = 6, Cornstalk = 7,
            Straw = 8, Galvanized = 9, Felt = 10, Dirt = 11, CornLeaf = 12, BarnBoard = 13, Osb = 14, Grass = 15;

        public static void Textures(List<TexDef> l)
        {
            l.Add(new TexDef(IceAlbedo, 256, PaintIceAlbedo));
            l.Add(new TexDef(IceNormal, 256, PaintIceHeight) { linear = true, heightToNormal = true, normalStrength = 2.6f });
            l.Add(new TexDef(DebrisAlbedo, 1024, PaintDebrisAlbedo) { alpha = true });
            l.Add(new TexDef(DebrisNormal, 1024, PaintDebrisHeight) { linear = true, heightToNormal = true, normalStrength = 3.2f });
        }

        public static void Materials(List<MatDef> l)
        {
            // layered ice: a milky, frosted body (frost normal map, medium smoothness) under a glassy clear coat that the
            // editor step VfxGenWeather.FinishMaterials switches on (_CoatMask = 1, so the variant is in the build)
            l.Add(new MatDef(IceMat)
            {
                color = new Color(0.84f, 0.89f, 0.95f), baseMap = IceAlbedo, normalMap = IceNormal, normalScale = 0.9f, smoothness = 0.66f
            });
            // storm debris is wet: fairly glossy; alpha-tested for the leaf and grass outlines; double sided (thin sheets)
            l.Add(new MatDef(DebrisMat)
            {
                surface = MatSurface.Cutout, color = Color.white, baseMap = DebrisAlbedo, normalMap = DebrisNormal, smoothness = 0.42f,
                doubleSided = true
            });
            l.Add(new MatDef(DebrisMetalMat)
            {
                color = Color.white, baseMap = DebrisAlbedo, normalMap = DebrisNormal, smoothness = 0.58f, metallic = 0.8f, doubleSided = true
            });
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>A material of the library, or a fallback when the Library registration line is missing (no crash,
        /// one warning).</summary>
        public static Material Mat(string name, string fallback)
        {
            GameAssets g = GameAssets.I;
            if (g == null) return null;
            Material[] all = g.materials;
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && all[i].name == name) return all[i];
            Debug.LogWarning("[WeatherLibrary] material '" + name + "' missing (is WeatherLibrary registered in Library.Materials()?), using '" + fallback + "'");
            return g.Mat(fallback);
        }

        /// <summary>Outward normal of the windscreen (forward and up), from the glass corners. Computed here so it does
        /// not depend on the sign convention of CabDims.GlassNormalOut (which points into the cab).</summary>
        public static readonly Vector3 GlassOut = ComputeGlassOut();

        static Vector3 ComputeGlassOut()
        {
            Vector3 n = Vector3.Cross(Vector3.right, (CabDims.WTL - CabDims.WBL).normalized).normalized;
            return n.z < 0f ? -n : n;
        }

        /// <summary>UV rectangle of an atlas cell, inset so mip levels do not bleed between cells.</summary>
        public static Rect Cell(int i)
        {
            float c = 1f / Grid, m = c * 0.035f;
            return new Rect((i % Grid) * c + m, (i / Grid) * c + m, c - 2f * m, c - 2f * m);
        }

        /// <summary>The integer hash of the VFX hail and splash systems (HailHash / SplashHash in VfxGenWeather):
        /// bit-identical, so CPU stones and GPU crowns agree.</summary>
        public static float HailHash(uint n)
        {
            unchecked
            {
                n = (n ^ 61u) ^ (n >> 16); n *= 9u; n = n ^ (n >> 4); n *= 0x27d4eb2du; n = n ^ (n >> 15);
            }
            return (n & 0x00ffffffu) / 16777216f;
        }

        public static uint Hash(uint x)
        {
            unchecked
            {
                x ^= x >> 16; x *= 0x7feb352du; x ^= x >> 15; x *= 0x846ca68bu; x ^= x >> 16;
            }
            return x;
        }

        /// <summary>k-th uniform number 0..1 derived from one hash.</summary>
        public static float U(uint h, uint k)
        {
            unchecked { return (Hash(h + k * 0x9E3779B9u) & 0xFFFFFF) / 16777216f; }
        }

        public static Vector3 Dir(uint h)
        {
            float z = U(h, 41) * 2f - 1f, a = U(h, 42) * Mathf.PI * 2f, r = Mathf.Sqrt(Mathf.Max(0f, 1f - z * z));
            return new Vector3(r * Mathf.Cos(a), z, r * Mathf.Sin(a));
        }

        public static float Smooth(float e0, float e1, float x)
        {
            float t = Mathf.Clamp01((x - e0) / (e1 - e0));
            return t * t * (3f - 2f * t);
        }

        // ------------------------------------------------------------------ noise for the painters (edit time only)

        static float H(int x, int y, int seed) => CoreLibrary.Hash(x, y, seed);

        /// <summary>Tiling value noise with cx by cy cells over the unit square.</summary>
        static float VN(float u, float v, int cx, int cy, int seed)
        {
            float x = u * cx, y = v * cy;
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float fx = x - xi, fy = y - yi;
            fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
            int x0 = ((xi % cx) + cx) % cx, x1 = (x0 + 1) % cx, y0 = ((yi % cy) + cy) % cy, y1 = (y0 + 1) % cy;
            float a = H(x0, y0, seed), b = H(x1, y0, seed), c = H(x0, y1, seed), d = H(x1, y1, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        static float Fbm(float u, float v, int cx, int cy, int oct, int seed)
        {
            float a = 0.5f, s = 0f, n = 0f;
            for (int o = 0; o < oct; o++) { s += a * VN(u, v, cx << o, cy << o, seed + o * 31); n += a; a *= 0.5f; }
            return s / n;
        }

        /// <summary>Jittered-grid cell noise: value of the nearest feature point's cell, and the distance to it.</summary>
        static float Cells(float u, float v, int g, int seed, out float dist)
        {
            float x = u * g, y = v * g;
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float best = 9f, val = 0f;
            for (int j = -1; j <= 1; j++)
                for (int i = -1; i <= 1; i++)
                {
                    int cx = xi + i, cy = yi + j;
                    float px = cx + H(cx, cy, seed), py = cy + H(cx, cy, seed + 7);
                    float d = (px - x) * (px - x) + (py - y) * (py - y);
                    if (d < best) { best = d; val = H(cx, cy, seed + 13); }
                }
            dist = Mathf.Sqrt(best);
            return val;
        }

        static Color32 Gray(float h)
        {
            byte b = (byte)(Mathf.Clamp01(h) * 255f);
            return new Color32(b, b, b, 255);
        }

        // ------------------------------------------------------------------ hail ice

        static Color32[] PaintIceAlbedo(int n)
        {
            var px = new Color32[n * n];
            var milky = new Color(0.94f, 0.96f, 0.99f);
            var glassy = new Color(0.52f, 0.60f, 0.70f);         // clear ice shows the dark night through it
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                    float clear = Smooth(0.47f, 0.66f, Fbm(u, v, 4, 4, 4, 701));
                    // growth layers: faint bands of milky and clear ice, bent by the lumps
                    float band = 0.5f + 0.5f * Mathf.Sin((v * 7f + Fbm(u, v, 3, 3, 3, 702) * 2.2f) * Mathf.PI * 2f);
                    Color c = Color.Lerp(milky, glassy, clear * (0.55f + 0.45f * band));
                    if (H(x, y, 703) > 0.988f) c = Color.Lerp(c, Color.white, 0.7f);      // trapped air bubbles
                    float grain = 0.93f + 0.07f * Fbm(u, v, 32, 32, 2, 704);
                    px[y * n + x] = new Color(c.r * grain, c.g * grain, c.b * grain, 1f);
                }
            return px;
        }

        static Color32[] PaintIceHeight(int n)
        {
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                    float frost = Fbm(u, v, 16, 16, 4, 711);                 // rime crystals
                    float lumps = Fbm(u, v, 4, 4, 3, 712);                   // soft knobs of the outer shell
                    float h = 0.5f * frost + 0.35f * lumps + 0.15f * H(x, y, 713);
                    if (H(x, y, 714) > 0.992f) h -= 0.25f;                  // tiny pits of burst bubbles
                    px[y * n + x] = Gray(h);
                }
            return px;
        }

        // ------------------------------------------------------------------ debris atlas

        static Color32[] PaintDebrisAlbedo(int n)
        {
            int cp = n / Grid;
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int cell = (y / cp) * Grid + (x / cp);
                    float u = ((x % cp) + 0.5f) / cp, v = ((y % cp) + 0.5f) / cp;
                    px[y * n + x] = DebrisColor(cell, u, v, x, y, cp);
                }
            return px;
        }

        static Color32[] PaintDebrisHeight(int n)
        {
            int cp = n / Grid;
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int cell = (y / cp) * Grid + (x / cp);
                    float u = ((x % cp) + 0.5f) / cp, v = ((y % cp) + 0.5f) / cp;
                    px[y * n + x] = Gray(DebrisHeight(cell, u, v, x, y, cp));
                }
            return px;
        }

        /// <summary>Leaf outline along v (stalk at 0, tip at 1): coverage 0..1, anti-aliased over a pixel.</summary>
        static float LeafMask(float u, float v, float cp, float lobes, out float t, out float d)
        {
            t = (v - 0.07f) / 0.86f;
            d = Mathf.Abs(u - 0.5f);
            float stalk = (v > 0.005f && v < 0.1f && d < 0.012f) ? 1f : 0f;
            if (t <= 0f || t >= 1f) return stalk;
            float hw = 0.40f * Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Pow(t, 0.75f)), 0.85f);
            hw *= 1f + lobes * Mathf.Sin(t * 38f);
            return Mathf.Max(stalk, Mathf.Clamp01((hw - d) * cp + 0.5f));
        }

        static float GrassMask(float u, float v, float cp, out float shade)
        {
            float best = 0f; shade = 0.5f;
            for (int b = 0; b < 9; b++)
            {
                float bx = 0.12f + 0.76f * CoreLibrary.Hash(b, 1, 951), lean = (CoreLibrary.Hash(b, 2, 951) - 0.5f) * 0.5f;
                float top = 0.6f + 0.38f * CoreLibrary.Hash(b, 3, 951), w = 0.018f + 0.016f * CoreLibrary.Hash(b, 4, 951);
                if (v > top) continue;
                float c = bx + lean * v * v, hw = w * (1f - v / top);
                float m = Mathf.Clamp01((hw - Mathf.Abs(u - c)) * cp + 0.5f);
                if (m > best) { best = m; shade = CoreLibrary.Hash(b, 5, 951); }
            }
            return best;
        }

        static Color Mul(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, c.a);

        static Color32 DebrisColor(int cell, float u, float v, int x, int y, int cp)
        {
            float g = H(x, y, 977);
            switch (cell)
            {
                case LeafGreen:
                case LeafDry:
                {
                    bool dry = cell == LeafDry;
                    float a = LeafMask(u, v, cp, dry ? 0.06f : 0.025f, out float t, out float d);
                    Color c = dry ? new Color(0.42f, 0.29f, 0.13f) : new Color(0.16f, 0.29f, 0.08f);
                    c = Mul(c, 0.8f + 0.4f * Fbm(u, v, 6, 6, 3, dry ? 801 : 802));
                    if (!dry && Fbm(u, v, 5, 5, 3, 803) > 0.66f) c = Color.Lerp(c, new Color(0.40f, 0.42f, 0.12f), 0.6f);   // yellowing
                    if (dry && Fbm(u, v, 7, 7, 3, 804) > 0.64f) c = Mul(c, 0.55f);                                           // rot spots
                    float vein = d < 0.012f ? 1f : (Mathf.Repeat(t * 7f - d * 3.2f, 1f) < 0.06f ? 0.5f : 0f);
                    c = Color.Lerp(c, Mul(c, 1.6f), vein * 0.6f);
                    if (dry && Fbm(u, v, 9, 9, 2, 805) > 0.78f) a = 0f;                                                     // holes
                    c.a = a;
                    return c;
                }
                case Bark:
                {
                    float f = Fbm(u, v, 14, 3, 3, 811);
                    float groove = Smooth(0.42f, 0.28f, f);
                    Color c = Mul(new Color(0.26f, 0.20f, 0.15f), 0.85f + 0.3f * Fbm(u, v, 6, 6, 3, 812));
                    c = Color.Lerp(c, Mul(c, 0.45f), groove);
                    if (Fbm(u, v, 5, 5, 3, 813) > 0.68f) c = Color.Lerp(c, new Color(0.38f, 0.40f, 0.33f), 0.5f);         // lichen
                    return c;
                }
                case Plank:
                {
                    float grain = 0.5f + 0.5f * Mathf.Sin((v * 26f + Fbm(u, v, 2, 6, 3, 821) * 5f) * Mathf.PI);
                    Color c = Color.Lerp(new Color(0.50f, 0.44f, 0.36f), new Color(0.34f, 0.28f, 0.21f), grain * 0.55f);
                    float kd = new Vector2((u - 0.32f) * 2.5f, v - 0.58f).magnitude;                                        // a knot
                    if (kd < 0.06f) c = Mul(c, 0.55f + 0.45f * Mathf.Sin(kd * 120f) * 0.5f + 0.2f);
                    return Mul(c, 0.85f + 0.25f * Fbm(u, v, 4, 4, 3, 822));
                }
                case Shingle:
                {
                    float gr = g < 0.08f ? 1.9f : (0.6f + 0.8f * H(x, y, 831));                                            // mineral granules
                    Color c = Mul(new Color(0.16f, 0.15f, 0.14f), gr);
                    if (v < 0.48f && (Mathf.Abs(u - 0.3333f) < 0.007f || Mathf.Abs(u - 0.6667f) < 0.007f)) c = new Color(0.03f, 0.03f, 0.03f);
                    if (v > 0.52f && v < 0.58f) c = Mul(c, 0.6f);                                                           // sealant strip
                    if (v > 0.6f) c = Color.Lerp(c, new Color(0.22f, 0.18f, 0.12f), 0.35f);                                 // asphalt mat showing
                    return c;
                }
                case Insulation:
                {
                    float fluff = Fbm(u, v, 8, 8, 4, 841);
                    Color c = Color.Lerp(new Color(0.62f, 0.36f, 0.38f), new Color(0.92f, 0.62f, 0.64f), fluff);
                    if (H(x, y, 842) > 0.97f) c = Mul(c, 1.25f);                                                          // glass fibres
                    if (v < 0.14f) c = Mul(new Color(0.60f, 0.47f, 0.30f), 0.9f + 0.2f * fluff);                         // kraft facing
                    return c;
                }
                case Siding:
                {
                    float lap = Mathf.Repeat(v * 4f, 1f);
                    float shade = 0.7f + 0.3f * Smooth(0f, 0.12f, lap);
                    Color c = Mul(new Color(0.86f, 0.85f, 0.80f), shade * (0.96f + 0.04f * Mathf.Sin(u * 160f + Fbm(u, v, 3, 12, 2, 851) * 8f)));
                    float grime = Fbm(u, v, 3, 6, 3, 852);
                    return Color.Lerp(c, new Color(0.45f, 0.43f, 0.36f), Smooth(0.55f, 0.8f, grime) * 0.6f);
                }
                case Cornstalk:
                case Straw:
                {
                    bool straw = cell == Straw;
                    Color c = straw ? new Color(0.82f, 0.69f, 0.38f) : new Color(0.70f, 0.60f, 0.41f);
                    c = Mul(c, 0.9f + 0.12f * Mathf.Sin(u * 90f) + 0.15f * (Fbm(u, v, 3, 8, 3, 861) - 0.5f));
                    if (!straw && Mathf.Repeat(v * 4f, 1f) < 0.04f) c = new Color(0.44f, 0.35f, 0.21f);                   // nodes
                    if (Fbm(u, v, 4, 6, 3, 862) > 0.7f) c = Mul(c, 0.7f);                                                // mildew
                    return c;
                }
                case Galvanized:
                {
                    float spangle = Cells(u, v, 7, 871, out float dist);
                    Color c = Mul(new Color(0.60f, 0.62f, 0.63f), 0.86f + 0.22f * spangle + 0.06f * (Fbm(u, v, 16, 16, 2, 872) - 0.5f));
                    float rust = Fbm(u, v, 6, 2, 4, 873) + 0.25f * (1f - v);                                             // runs downward
                    c = Color.Lerp(c, new Color(0.42f, 0.23f, 0.11f), Smooth(0.62f, 0.85f, rust));
                    if (dist < 0.06f) c = Mul(c, 0.94f);
                    return c;
                }
                case Felt:
                {
                    Color c = Mul(new Color(0.075f, 0.075f, 0.07f), 0.8f + 0.5f * Fbm(u, v, 12, 12, 3, 881) + 0.2f * g);
                    if (Mathf.Abs(v - 0.2f) < 0.006f || Mathf.Abs(v - 0.8f) < 0.006f) c = new Color(0.55f, 0.55f, 0.52f);  // printed lap line
                    return c;
                }
                case Dirt:
                {
                    float peb = Cells(u, v, 10, 891, out float dist);
                    Color c = Mul(new Color(0.30f, 0.24f, 0.17f), 0.75f + 0.4f * Fbm(u, v, 6, 6, 4, 892));
                    if (dist < 0.32f) c = Color.Lerp(c, Mul(new Color(0.45f, 0.41f, 0.36f), 0.7f + 0.6f * peb), 0.8f);
                    return c;
                }
                case CornLeaf:
                {
                    float t = (v - 0.03f) / 0.94f, d = Mathf.Abs(u - 0.5f);
                    float hw = t > 0f && t < 1f ? 0.46f * (1f - t * t * t) : 0f;
                    float a = Mathf.Clamp01((hw - d) * cp + 0.5f);
                    Color c = Color.Lerp(new Color(0.40f, 0.45f, 0.20f), new Color(0.62f, 0.55f, 0.32f), Smooth(0.25f, 0.46f, d) * 0.8f + 0.3f * Fbm(u, v, 3, 8, 3, 901));
                    c = Mul(c, 0.92f + 0.08f * Mathf.Sin(u * 220f));
                    if (d < 0.02f) c = Mul(c, 1.25f);
                    c.a = a;
                    return c;
                }
                case BarnBoard:
                {
                    float flake = Fbm(u, v, 6, 6, 4, 911);
                    float grain = 0.5f + 0.5f * Mathf.Sin((v * 30f + Fbm(u, v, 2, 6, 3, 912) * 5f) * Mathf.PI);
                    Color wood = Color.Lerp(new Color(0.46f, 0.43f, 0.39f), new Color(0.32f, 0.29f, 0.26f), grain * 0.5f);
                    Color paint = Mul(new Color(0.46f, 0.11f, 0.08f), 0.9f + 0.2f * Fbm(u, v, 8, 8, 2, 913));
                    return Color.Lerp(paint, wood, Smooth(0.56f, 0.64f, flake));
                }
                case Osb:
                {
                    float a1 = VN(u, v, 22, 8, 921), a2 = VN(u, v, 9, 20, 922), s = Mathf.Max(a1, a2);
                    Color c = new Color(0.66f, 0.53f, 0.33f);
                    return Mul(c, 0.7f + 0.5f * s + 0.08f * g);
                }
                default:  // Grass
                {
                    float a = GrassMask(u, v, cp, out float shade);
                    Color c = Color.Lerp(new Color(0.20f, 0.30f, 0.10f), new Color(0.52f, 0.48f, 0.24f), shade * 0.8f);
                    c = Mul(c, 0.7f + 0.5f * v);
                    c.a = a;
                    return c;
                }
            }
        }

        static float DebrisHeight(int cell, float u, float v, int x, int y, int cp)
        {
            float g = H(x, y, 977);
            switch (cell)
            {
                case LeafGreen:
                case LeafDry:
                {
                    LeafMask(u, v, cp, 0.03f, out float t, out float d);
                    float vein = d < 0.012f ? 1f : (Mathf.Repeat(t * 7f - d * 3.2f, 1f) < 0.06f ? 0.6f : 0f);
                    return 0.45f + 0.3f * vein + 0.15f * Fbm(u, v, 12, 12, 2, 1801);
                }
                case Bark: return 0.2f + 0.7f * Smooth(0.28f, 0.42f, Fbm(u, v, 14, 3, 3, 811)) + 0.1f * g;
                case Plank: return 0.5f + 0.3f * Mathf.Sin((v * 26f + Fbm(u, v, 2, 6, 3, 821) * 5f) * Mathf.PI) * 0.5f + 0.1f * g;
                case Shingle: return 0.3f + 0.6f * H(x, y, 831) - ((v < 0.48f && (Mathf.Abs(u - 0.3333f) < 0.007f || Mathf.Abs(u - 0.6667f) < 0.007f)) ? 0.3f : 0f);
                case Insulation: return 0.2f + 0.7f * Fbm(u, v, 8, 8, 4, 841) + 0.1f * g;
                case Siding: return 0.3f + 0.5f * Smooth(0f, 0.85f, Mathf.Repeat(v * 4f, 1f)) + 0.03f * Mathf.Sin(u * 160f);
                case Cornstalk:
                case Straw: return 0.5f + 0.25f * Mathf.Sin(u * 90f) - (cell == Cornstalk && Mathf.Repeat(v * 4f, 1f) < 0.04f ? 0.2f : 0f);
                case Galvanized: return 0.5f + 0.15f * Cells(u, v, 7, 871, out float _) + 0.2f * Smooth(0.62f, 0.85f, Fbm(u, v, 6, 2, 4, 873));
                case Felt: return 0.4f + 0.4f * Fbm(u, v, 12, 12, 3, 881) + 0.2f * g;
                case Dirt:
                {
                    Cells(u, v, 10, 891, out float dist);
                    return 0.3f + 0.4f * Smooth(0.4f, 0.1f, dist) + 0.3f * Fbm(u, v, 6, 6, 4, 892);
                }
                case CornLeaf: return 0.5f + 0.2f * Mathf.Sin(u * 220f) + (Mathf.Abs(u - 0.5f) < 0.02f ? 0.25f : 0f);
                case BarnBoard: return 0.4f + 0.3f * Smooth(0.64f, 0.56f, Fbm(u, v, 6, 6, 4, 911)) + 0.2f * Mathf.Sin((v * 30f) * Mathf.PI) * 0.5f;
                case Osb: return 0.3f + 0.6f * Mathf.Max(VN(u, v, 22, 8, 921), VN(u, v, 9, 20, 922));
                default: return 0.5f;
            }
        }
    }
}
