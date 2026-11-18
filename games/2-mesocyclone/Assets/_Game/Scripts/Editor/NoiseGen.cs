using UnityEngine;

namespace MESOCYCLONE.EditorTools
{
    /// <summary>Tiling 3D noise for the storm volume, generated on the CPU at edit time (committed as an asset).
    /// R: Perlin-Worley (billows), G: inverted Worley, 5 cells (lobes), B: inverted Worley fbm 10/20 cells (detail),
    /// A: Perlin fbm (smooth). All channels 0..1, linear.</summary>
    public static class NoiseGen
    {
        static uint Hash(int x, int y, int z, uint seed)
        {
            uint h = (uint)x * 0x8da6b343u ^ (uint)y * 0xd8163841u ^ (uint)z * 0xcb1ab31fu ^ seed * 0x9e3779b9u;
            h ^= h >> 13; h *= 0x5bd1e995u; h ^= h >> 15; h *= 0x27d4eb2du; h ^= h >> 13;
            return h;
        }

        static float H01(uint h) => (h & 0xFFFFFF) / 16777216f;
        static int Mod(int a, int n) { int r = a % n; return r < 0 ? r + n : r; }

        static float Grad(uint h, float x, float y, float z)
        {
            switch (h & 15)
            {
                case 0: return x + y; case 1: return -x + y; case 2: return x - y; case 3: return -x - y;
                case 4: return x + z; case 5: return -x + z; case 6: return x - z; case 7: return -x - z;
                case 8: return y + z; case 9: return -y + z; case 10: return y - z; case 11: return -y - z;
                case 12: return y + x; case 13: return -y + z; case 14: return y - x; default: return -y - z;
            }
        }

        /// <summary>Tiling gradient noise, period = freq cells, input 0..1, output about -1..1.</summary>
        static float Perlin(float u, float v, float w, int freq, uint seed)
        {
            float x = u * freq, y = v * freq, z = w * freq;
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y), zi = Mathf.FloorToInt(z);
            float xf = x - xi, yf = y - yi, zf = z - zi;
            float sx = xf * xf * xf * (xf * (xf * 6 - 15) + 10), sy = yf * yf * yf * (yf * (yf * 6 - 15) + 10), sz = zf * zf * zf * (zf * (zf * 6 - 15) + 10);
            float r = 0f;
            for (int k = 0; k < 8; k++)
            {
                int dx = k & 1, dy = (k >> 1) & 1, dz = (k >> 2) & 1;
                uint h = Hash(Mod(xi + dx, freq), Mod(yi + dy, freq), Mod(zi + dz, freq), seed);
                float g = Grad(h, xf - dx, yf - dy, zf - dz);
                r += g * (dx == 1 ? sx : 1 - sx) * (dy == 1 ? sy : 1 - sy) * (dz == 1 ? sz : 1 - sz);
            }
            return r;
        }

        /// <summary>Tiling Worley F1 (distance to the nearest feature point), in cell units, about 0..1.</summary>
        static float Worley(float u, float v, float w, int freq, uint seed)
        {
            float x = u * freq, y = v * freq, z = w * freq;
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y), zi = Mathf.FloorToInt(z);
            float best = 9f;
            for (int dz = -1; dz <= 1; dz++)
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int cx = xi + dx, cy = yi + dy, cz = zi + dz;
                        uint h = Hash(Mod(cx, freq), Mod(cy, freq), Mod(cz, freq), seed);
                        float px = cx + H01(h), py = cy + H01(h * 0x2545F491u + 1u), pz = cz + H01(h * 0x9E3779B1u + 7u);
                        float d = (px - x) * (px - x) + (py - y) * (py - y) + (pz - z) * (pz - z);
                        if (d < best) best = d;
                    }
            return Mathf.Min(1f, Mathf.Sqrt(best));
        }

        static float Remap(float v, float a, float b, float c, float d) => c + (v - a) / (b - a) * (d - c);

        public static Texture3D Build3D(int n)
        {
            var tex = new Texture3D(n, n, n, TextureFormat.RGBA32, false, true)
            { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, name = "Noise3D" };
            var px = new Color32[n * n * n];
            float inv = 1f / n;
            for (int z = 0; z < n; z++)
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float u = (x + 0.5f) * inv, v = (y + 0.5f) * inv, w = (z + 0.5f) * inv;
                        float perlin = 0.5f + 0.5f * (Perlin(u, v, w, 4, 11) * 0.55f + Perlin(u, v, w, 8, 12) * 0.28f + Perlin(u, v, w, 16, 13) * 0.17f) * 1.5f;
                        perlin = Mathf.Clamp01(perlin);
                        float w5 = 1f - Worley(u, v, w, 5, 21);
                        float w10 = 1f - Worley(u, v, w, 10, 22);
                        float w20 = 1f - Worley(u, v, w, 20, 23);
                        float wfbm = w5 * 0.625f + w10 * 0.25f + w20 * 0.125f;
                        float pw = Mathf.Clamp01(Remap(perlin, 0f, 1f, wfbm * 0.9f, 1f));        // Perlin-Worley: billowy
                        float detail = Mathf.Clamp01(w10 * 0.6f + w20 * 0.4f);
                        float smooth = Mathf.Clamp01(0.5f + 0.5f * (Perlin(u, v, w, 5, 31) * 0.6f + Perlin(u, v, w, 10, 32) * 0.4f) * 1.6f);
                        px[(z * n + y) * n + x] = new Color32((byte)(pw * 255f), (byte)(Mathf.Clamp01(w5) * 255f), (byte)(detail * 255f), (byte)(smooth * 255f));
                    }
            tex.SetPixels32(px);
            tex.Apply(false, false);
            return tex;
        }
    }
}
