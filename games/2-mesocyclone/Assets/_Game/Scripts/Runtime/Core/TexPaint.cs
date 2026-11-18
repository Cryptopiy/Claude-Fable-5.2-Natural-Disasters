using UnityEngine;

namespace MESOCYCLONE
{
    /// <summary>A decoded source image: the bytes as stored in the file (no colour-space conversion), bottom row
    /// first like Texture2D.GetPixels32. Only built at edit time, by the asset generator.</summary>
    public sealed class Img
    {
        public readonly int w, h;
        public readonly Color32[] px;

        public Img(int w, int h, Color32[] px) { this.w = w; this.h = h; this.px = px; }

        static int Wrap(int i, int n) { i %= n; return i < 0 ? i + n : i; }

        /// <summary>Bilinear and wrapping; channels 0..1 as stored.</summary>
        public Vector4 Sample(float u, float v)
        {
            float x = u * w - 0.5f, y = v * h - 0.5f;
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float fx = x - xi, fy = y - yi;
            int x0 = Wrap(xi, w), y0 = Wrap(yi, h), x1 = x0 + 1 == w ? 0 : x0 + 1, y1 = y0 + 1 == h ? 0 : y0 + 1;
            Color32 a = px[y0 * w + x0], b = px[y0 * w + x1], c = px[y1 * w + x0], d = px[y1 * w + x1];
            float w00 = (1f - fx) * (1f - fy), w10 = fx * (1f - fy), w01 = (1f - fx) * fy, w11 = fx * fy;
            const float k = 1f / 255f;
            return new Vector4((a.r * w00 + b.r * w10 + c.r * w01 + d.r * w11) * k, (a.g * w00 + b.g * w10 + c.g * w01 + d.g * w11) * k,
                               (a.b * w00 + b.b * w10 + c.b * w01 + d.b * w11) * k, (a.a * w00 + b.a * w10 + c.a * w01 + d.a * w11) * k);
        }

        /// <summary>Bilinear sample of an sRGB colour map, converted to linear before filtering.</summary>
        public Vector3 SampleLin(float u, float v)
        {
            float x = u * w - 0.5f, y = v * h - 0.5f;
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float fx = x - xi, fy = y - yi;
            int x0 = Wrap(xi, w), y0 = Wrap(yi, h), x1 = x0 + 1 == w ? 0 : x0 + 1, y1 = y0 + 1 == h ? 0 : y0 + 1;
            Color32 a = px[y0 * w + x0], b = px[y0 * w + x1], c = px[y1 * w + x0], d = px[y1 * w + x1];
            float w00 = (1f - fx) * (1f - fy), w10 = fx * (1f - fy), w01 = (1f - fx) * fy, w11 = fx * fy;
            return new Vector3(TexPaint.Lin(a.r) * w00 + TexPaint.Lin(b.r) * w10 + TexPaint.Lin(c.r) * w01 + TexPaint.Lin(d.r) * w11,
                               TexPaint.Lin(a.g) * w00 + TexPaint.Lin(b.g) * w10 + TexPaint.Lin(c.g) * w01 + TexPaint.Lin(d.g) * w11,
                               TexPaint.Lin(a.b) * w00 + TexPaint.Lin(b.b) * w10 + TexPaint.Lin(c.b) * w01 + TexPaint.Lin(d.b) * w11);
        }

        /// <summary>Tangent-space normal of an OpenGL-convention (Y up, Unity's) normal map, unpacked to -1..1.</summary>
        public Vector3 SampleNormal(float u, float v)
        {
            Vector4 s = Sample(u, v);
            var n = new Vector3(s.x * 2f - 1f, s.y * 2f - 1f, s.z * 2f - 1f);
            return n.sqrMagnitude > 1e-6f ? n.normalized : Vector3.forward;
        }
    }

    /// <summary>Texture painters for real materials: photo maps from Poly Haven turned into what HDRP Lit wants
    /// (normalised albedo, mask map, detail map, resampled normals), and procedural detail maps. All painters return
    /// n x n pixels, bottom row first. Pure functions of their inputs, so a rebuild gives the same textures.</summary>
    public static class TexPaint
    {
        static readonly float[] lut = BuildLut();

        static float[] BuildLut()
        {
            var t = new float[256];
            for (int i = 0; i < 256; i++) t[i] = Mathf.GammaToLinearSpace(i / 255f);
            return t;
        }

        public static float Lin(byte b) => lut[b];
        public static byte B(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);
        public static byte Srgb(float lin) => B(Mathf.LinearToGammaSpace(Mathf.Clamp01(lin)));
        public static float Lum(Vector3 c) => 0.2126f * c.x + 0.7152f * c.y + 0.0722f * c.z;
        public static float Ss(float e0, float e1, float x) { float t = Mathf.Clamp01((x - e0) / (e1 - e0)); return t * t * (3f - 2f * t); }

        // ------------------------------------------------------------------------------------------ statistics
        /// <summary>Mean linear colour (every 3rd pixel).</summary>
        public static Vector3 MeanLin(Img a)
        {
            double r = 0, g = 0, b = 0; int k = 0;
            for (int i = 0; i < a.px.Length; i += 3) { Color32 c = a.px[i]; r += lut[c.r]; g += lut[c.g]; b += lut[c.b]; k++; }
            return new Vector3((float)(r / k), (float)(g / k), (float)(b / k));
        }

        /// <summary>5th and 95th percentile of the smoothness (1 - roughness, arm.g) and its mean in z. x &lt; 0 when
        /// the map carries no roughness (some sets ship an empty channel).</summary>
        public static Vector3 SmoothStats(Img arm)
        {
            var hist = new int[256];
            double sum = 0; int k = 0;
            for (int i = 0; i < arm.px.Length; i += 3) { int s = 255 - arm.px[i].g; hist[s]++; sum += s; k++; }
            if (sum / k > 250.0) return new Vector3(-1f, -1f, 0.5f);                  // roughness all zero: missing
            int lo = -1, hi = 255, acc = 0;
            for (int i = 0; i < 256; i++)
            {
                acc += hist[i];
                if (lo < 0 && acc >= k * 0.05f) lo = i;
                if (acc >= k * 0.95f) { hi = i; break; }
            }
            return new Vector3(lo / 255f, Mathf.Max(hi, lo + 3) / 255f, (float)(sum / k / 255.0));
        }

        // ------------------------------------------------------------------------------------------ encoders
        /// <summary>Detail-map albedo channel for a wanted brightness ratio. HDRP lerps sqrt(albedo) toward black or
        /// white by speed squared (speed = |2R - 1|): darkening by a ratio is exact, brightening is tuned for the
        /// dark-to-mid albedos of this game.</summary>
        public static float DetailR(float ratio)
        {
            float d;
            if (ratio < 1f) d = -Mathf.Sqrt(1f - Mathf.Sqrt(Mathf.Max(ratio, 0f)));
            else d = Mathf.Sqrt(Mathf.Min((Mathf.Sqrt(ratio) - 1f) * 0.35f, 1f));
            return 0.5f + 0.5f * Mathf.Clamp(d, -1f, 1f);
        }

        /// <summary>Detail-map smoothness channel for a wanted change ds around a base smoothness of about 0.35.</summary>
        public static float DetailB(float ds)
        {
            float f = ds >= 0f ? ds / 0.65f : ds / 0.35f;
            return 0.5f + 0.5f * Mathf.Clamp(f, -1f, 1f);
        }

        public static Color32 DetailPx(float ratio, float ds, float nx, float ny)
        {
            return new Color32(B(DetailR(ratio)), B(ny * 0.5f + 0.5f), B(DetailB(ds)), B(nx * 0.5f + 0.5f));
        }

        public static Color32 PackNormal(Vector3 n)
        {
            n = n.sqrMagnitude > 1e-8f ? n.normalized : Vector3.forward;
            return new Color32(B(n.x * 0.5f + 0.5f), B(n.y * 0.5f + 0.5f), B(n.z * 0.5f + 0.5f), 255);
        }

        /// <summary>Adds a height-field slope (in tangent units) to a normal: the surface-gradient way of stacking bumps.</summary>
        public static Vector3 AddSlope(Vector3 n, float dhdu, float dhdv)
        {
            return new Vector3(n.x - dhdu * n.z, n.y - dhdv * n.z, n.z).normalized;
        }

        // ------------------------------------------------------------------------------------------ photo maps
        /// <summary>Albedo normalised to a mean linear 0.5 (the material colour sets the real albedo). The luminance
        /// ratio to the mean is kept (scaled by contrast); the colour deviation from the mean hue is kept by 'chroma'
        /// (0 = grey, 1 = as photographed), clamped so that a nearly empty channel cannot add noise.</summary>
        public static Color32[] Albedo(int n, Img d, float chroma, float contrast)
        {
            Vector3 m = MeanLin(d);
            float ml = Mathf.Max(Lum(m), 1e-4f);
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    Vector3 c = d.SampleLin((x + 0.5f) / n, (y + 0.5f) / n);
                    px[y * n + x] = Normalised(c, m, ml, chroma, contrast);
                }
            return px;
        }

        public static Vector3 Ratio(Vector3 c, Vector3 m, float ml, float chroma, float contrast)
        {
            float l = Lum(c);
            float lr = Mathf.Max(0f, 1f + (l / ml - 1f) * contrast);
            float il = 1f / Mathf.Max(l, 1e-4f);
            float dr = Mathf.Clamp(c.x * il / Mathf.Max(m.x / ml, 1e-3f), 0.5f, 2f);
            float dg = Mathf.Clamp(c.y * il / Mathf.Max(m.y / ml, 1e-3f), 0.5f, 2f);
            float db = Mathf.Clamp(c.z * il / Mathf.Max(m.z / ml, 1e-3f), 0.5f, 2f);
            return new Vector3(lr * Mathf.Pow(dr, chroma), lr * Mathf.Pow(dg, chroma), lr * Mathf.Pow(db, chroma));
        }

        static Color32 Normalised(Vector3 c, Vector3 m, float ml, float chroma, float contrast)
        {
            Vector3 r = Ratio(c, m, ml, chroma, contrast);
            return new Color32(Srgb(0.5f * r.x), Srgb(0.5f * r.y), Srgb(0.5f * r.z), 255);
        }

        /// <summary>HDRP mask map from a Poly Haven ARM map (R ambient occlusion, G roughness, B metal): R = 1 (the
        /// material's metallic sets the value), G = AO (lerped toward 1 by 1 - ao), B = 1 (full detail), A = the
        /// smoothness stretched from its 5th..95th percentile to 0..1 (the material's remap sets the real range).</summary>
        public static Color32[] Mask(int n, Img arm, float ao)
        {
            Vector3 st = SmoothStats(arm);
            bool has = st.x >= 0f;
            float inv = has ? 1f / (st.y - st.x) : 0f;
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    Vector4 a = arm.Sample((x + 0.5f) / n, (y + 0.5f) / n);
                    float s = has ? Mathf.Clamp01((1f - a.y - st.x) * inv) : 0.5f;
                    px[y * n + x] = new Color32(255, B(Mathf.Lerp(1f, a.x, ao)), 255, B(s));
                }
            return px;
        }

        /// <summary>HDRP detail map from a photo set: R = the brightness ratio to the mean (scaled by albedoGain),
        /// G/A = the normal's Y/X, B = the smoothness deviation from the mean (scaled by smoothGain).
        /// transpose = swap u and v (corrugations that must run the other way).</summary>
        public static Color32[] Detail(int n, Img d, Img nor, Img arm, float albedoGain, float smoothGain, bool transpose)
        {
            Vector3 m = MeanLin(d);
            float ml = Mathf.Max(Lum(m), 1e-4f);
            Vector3 st = SmoothStats(arm);
            bool has = st.x >= 0f;
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                    if (transpose) { float t = u; u = v; v = t; }
                    float ratio = 1f + (Lum(d.SampleLin(u, v)) / ml - 1f) * albedoGain;
                    Vector4 nn = nor.Sample(u, v);
                    float nx = nn.x * 2f - 1f, ny = nn.y * 2f - 1f;
                    if (transpose) { float t = nx; nx = ny; ny = t; }
                    float ds = has ? ((1f - arm.Sample(u, v).y) - st.z) * smoothGain : 0f;
                    px[y * n + x] = DetailPx(ratio, ds, nx, ny);
                }
            return px;
        }

        /// <summary>A normal map resampled to n x n (strength 1 = as photographed).</summary>
        public static Color32[] Normal(int n, Img nor, float strength)
        {
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    Vector3 v = nor.SampleNormal((x + 0.5f) / n, (y + 0.5f) / n);
                    px[y * n + x] = PackNormal(new Vector3(v.x * strength, v.y * strength, v.z));
                }
            return px;
        }

        // ------------------------------------------------------------------------------------------ noise
        /// <summary>Tiling value noise with cu cells across u and cv cells along v (for textures whose two axes
        /// cover different lengths, so the noise stays round in metres).</summary>
        public static float Value2(float u, float v, int cu, int cv, int seed)
        {
            float x = u * cu, y = v * cv;
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float fx = x - xi, fy = y - yi;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            int x0 = ((xi % cu) + cu) % cu, x1 = (x0 + 1) % cu, y0 = ((yi % cv) + cv) % cv, y1 = (y0 + 1) % cv;
            return Mathf.Lerp(Mathf.Lerp(CoreLibrary.Hash(x0, y0, seed), CoreLibrary.Hash(x1, y0, seed), fx),
                              Mathf.Lerp(CoreLibrary.Hash(x0, y1, seed), CoreLibrary.Hash(x1, y1, seed), fx), fy);
        }

        public static float Fbm2(float u, float v, int cu, int cv, int octaves, int seed)
        {
            float a = 0.5f, s = 0f, norm = 0f;
            for (int o = 0; o < octaves; o++) { s += a * Value2(u, v, cu << o, cv << o, seed + o * 17); norm += a; a *= 0.5f; }
            return s / norm;
        }

        public static Vector3 Mul(Vector3 a, Vector3 b) => new Vector3(a.x * b.x, a.y * b.y, a.z * b.z);
        public static Vector3 LinV(Color srgb) { Color c = srgb.linear; return new Vector3(c.r, c.g, c.b); }
        public static Color32 SrgbPx(Vector3 lin) => new Color32(Srgb(lin.x), Srgb(lin.y), Srgb(lin.z), 255);

        /// <summary>Scales the tilt of a unit normal (0 = flat, 1 = unchanged).</summary>
        public static Vector3 Strength(Vector3 n, float k) => new Vector3(n.x * k, n.y * k, n.z).normalized;

        // ------------------------------------------------------------------------------------------ procedural
        public const int Grime = 0, Galvanized = 1, Foliage = 2, Rubber = 3, OrangePeel = 4, Dust = 5, Smudge = 6, Macro = 7, Stains = 8;

        /// <summary>Tiling cellular noise: F1 and F2 in cell units and the id of the nearest cell.</summary>
        public static float Cell(float u, float v, int cells, int seed, out float f2, out int id)
        {
            float x = u * cells, y = v * cells;
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float f1 = 9f; f2 = 9f; id = 0;
            for (int j = -1; j <= 1; j++)
                for (int i = -1; i <= 1; i++)
                {
                    int cx = xi + i, cy = yi + j;
                    int wx = ((cx % cells) + cells) % cells, wy = ((cy % cells) + cells) % cells;
                    float px = cx + CoreLibrary.Hash(wx, wy, seed), py = cy + CoreLibrary.Hash(wx, wy, seed + 7);
                    float dd = Mathf.Sqrt((px - x) * (px - x) + (py - y) * (py - y));
                    if (dd < f1) { f2 = f1; f1 = dd; id = wy * cells + wx; }
                    else if (dd < f2) f2 = dd;
                }
            return f1;
        }

        /// <summary>Procedural detail maps (format as Detail). Each kind is one weathering or micro-surface look.</summary>
        public static Color32[] DetailProc(int n, int kind, int seed)
        {
            var h = new float[n * n];
            var ratio = new float[n * n];
            var ds = new float[n * n];
            float strength = 1f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                    int i = y * n + x;
                    float r = 1f, s = 0f, hh = 0f;
                    switch (kind)
                    {
                        case Grime:
                        {   // weathered paint: soft grime patches, fine speckle, faint brush/roll texture
                            float g = Ss(0.5f, 0.82f, CoreLibrary.Fbm(u, v, 3, 4, seed));
                            r = 1f - 0.2f * g + 0.08f * (CoreLibrary.Hash(x, y, seed + 1) - 0.5f) + 0.06f * (CoreLibrary.Fbm(u, v, 12, 3, seed + 2) - 0.5f);
                            s = -0.14f * g + 0.06f * (CoreLibrary.Fbm(u, v, 24, 2, seed + 3) - 0.5f);
                            hh = CoreLibrary.Fbm(u, v, 32, 3, seed + 4) * 0.7f + CoreLibrary.Hash(x, y, seed + 5) * 0.3f;
                            strength = 0.5f;
                            break;
                        }
                        case Galvanized:
                        {   // hot-dip spangle: crystal patches of slightly different sheen, a few white-rust specks
                            float f1 = Cell(u, v, 18, seed, out float f2, out int id);
                            float a = CoreLibrary.Hash(id, 3, seed), b = CoreLibrary.Hash(id, 5, seed);
                            float speck = Ss(0.72f, 0.9f, CoreLibrary.Fbm(u, v, 20, 3, seed + 9));
                            r = 0.93f + 0.14f * a + 0.25f * speck - 0.1f * Ss(0.55f, 0.85f, CoreLibrary.Fbm(u, v, 4, 3, seed + 2));
                            s = (b - 0.5f) * 0.2f - 0.25f * speck;
                            hh = Ss(0f, 0.06f, f2 - f1) * 0.15f + a * 0.2f;
                            strength = 0.4f;
                            break;
                        }
                        case Foliage:
                        {   // clumps of leaves: domes that shade their own crevices
                            float f1 = Cell(u, v, 9, seed, out float f2, out int id);
                            float g1 = Cell(u, v, 36, seed + 1, out float g2, out int lid);
                            float dome = 1f - Ss(0f, 0.85f, f1), leaf = 1f - Ss(0f, 0.7f, g1);
                            hh = dome * 0.65f + leaf * 0.35f;
                            r = (0.45f + 0.8f * hh) * (0.85f + 0.3f * CoreLibrary.Hash(lid, 1, seed));
                            s = (hh - 0.5f) * 0.12f;
                            strength = 3f;
                            break;
                        }
                        case Rubber:
                        {
                            hh = CoreLibrary.Hash(x, y, seed) * 0.5f + CoreLibrary.Fbm(u, v, 48, 2, seed + 1) * 0.5f;
                            r = 1f + 0.06f * (CoreLibrary.Fbm(u, v, 8, 3, seed + 2) - 0.5f);
                            s = 0.08f * (CoreLibrary.Fbm(u, v, 6, 3, seed + 3) - 0.5f);
                            strength = 0.8f;
                            break;
                        }
                        case OrangePeel:
                        {   // automotive paint: soft orange-peel ripple, dust settled in patches (rougher, paler)
                            hh = CoreLibrary.Fbm(u, v, 96, 2, seed) * 0.8f + CoreLibrary.Fbm(u, v, 24, 2, seed + 1) * 0.2f;
                            float dust = Ss(0.56f, 0.86f, CoreLibrary.Fbm(u, v, 3, 4, seed + 2));
                            r = 1f + 0.22f * dust + 0.03f * (CoreLibrary.Hash(x, y, seed + 3) - 0.5f);
                            s = -0.22f * dust - 0.04f * Ss(0.6f, 0.9f, CoreLibrary.Fbm(u, v, 40, 2, seed + 4));
                            strength = 0.3f;
                            break;
                        }
                        case Dust:
                        {   // cab interior: dust in patches, polished wear between them
                            float d = Ss(0.5f, 0.85f, CoreLibrary.Fbm(u, v, 3, 4, seed));
                            float wear = Ss(0.62f, 0.9f, CoreLibrary.Fbm(u, v, 5, 3, seed + 1));
                            r = 1f + 0.16f * d - 0.05f * wear;
                            s = -0.16f * d + 0.12f * wear;
                            hh = 0.5f;
                            break;
                        }
                        case Smudge:
                        {   // chrome and gloss trim: finger smudges and haze
                            float sm = Ss(0.5f, 0.8f, CoreLibrary.Fbm(u, v, 5, 4, seed));
                            r = 1f - 0.05f * sm;
                            s = -0.18f * sm - 0.04f * (CoreLibrary.Fbm(u, v, 30, 2, seed + 1) - 0.5f);
                            hh = 0.5f;
                            break;
                        }
                        case Macro:
                        {   // ground at the scale of a field: moisture and soil patches, faint undulation
                            float a = CoreLibrary.Fbm(u, v, 4, 4, seed);
                            float blot = Ss(0.6f, 0.82f, CoreLibrary.Fbm(u, v, 7, 3, seed + 5));
                            r = (1f + (a - 0.5f) * 0.5f) * (1f - 0.16f * blot);
                            s = (a - 0.5f) * 0.06f + 0.05f * blot;
                            hh = CoreLibrary.Fbm(u, v, 8, 3, seed + 7);
                            strength = 0.25f;
                            break;
                        }
                        case Stains:
                        {   // a gas-station apron: oil drips under where cars stand, tyre grime, faint patches
                            float f1 = Cell(u, v, 10, seed, out float f2, out int id);
                            float on = CoreLibrary.Hash(id, 11, seed) > 0.5f ? 1f : 0f;
                            float spot = on * (1f - Ss(0.08f, 0.5f, f1 + 0.25f * CoreLibrary.Fbm(u, v, 30, 2, seed + 1)));
                            float patch = Ss(0.5f, 0.8f, CoreLibrary.Fbm(u, v, 3, 3, seed + 2));
                            r = 1f - 0.5f * spot - 0.12f * patch;
                            s = 0.3f * spot - 0.04f * patch;
                            hh = 0.5f;
                            break;
                        }
                    }
                    h[i] = hh; ratio[i] = r; ds[i] = s;
                }
            var px = new Color32[n * n];
            float k = strength * n / 256f;               // slope per texel, independent of the resolution
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float l = h[y * n + (x + n - 1) % n], rr = h[y * n + (x + 1) % n];
                    float dd = h[((y + n - 1) % n) * n + x], uu = h[((y + 1) % n) * n + x];
                    var nn = new Vector3((l - rr) * k, (dd - uu) * k, 1f).normalized;
                    int i = y * n + x;
                    px[i] = DetailPx(ratio[i], ds[i], nn.x, nn.y);
                }
            return px;
        }
    }
}
