using System.Collections.Generic;
using UnityEngine;

namespace MESOCYCLONE
{
    /// <summary>The 3D vegetation near the road (GrassField): one material and one painted atlas, and the blade, tuft
    /// and wheat meshes built from code. The atlas has eight vertical bands, one colour family each; a blade maps its
    /// width onto the middle of one band and its length onto v (base dark and shaded, tip lighter and drier).</summary>
    public static class GrassLibrary
    {
        public const string VegMat = "veg", VegAlbedo = "veg_alb";
        public const int Bands = 8;
        public const int Lush = 0, Mixed = 1, Yellow = 2, Dry = 3, Ditch = 4, Straw = 5, StrawGreen = 6, Ear = 7;

        public static void Textures(List<TexDef> l)
        {
            l.Add(new TexDef(VegAlbedo, 512, PaintVeg) { aniso = 8, hq = true });
        }

        public static void Materials(List<MatDef> l)
        {
            // thin double-sided blades: the back face keeps the up-bent normal, so a blade is lit like the meadow it
            // belongs to from both sides; a slight sheen (grass is waxy)
            l.Add(new MatDef(VegMat)
            {
                color = Color.white, baseMap = VegAlbedo, smoothness = 0.26f, doubleSided = true, keepNormalBothSides = true
            });
        }

        // ------------------------------------------------------------------ atlas (edit time)

        static readonly Color[] BandBase =
        {
            Palette.Hex(0x4c6628), Palette.Hex(0x66703a), Palette.Hex(0x86843f), Palette.Hex(0x9a8656),
            Palette.Hex(0x3c5226), Palette.Hex(0xb8964c), Palette.Hex(0x9c9a52), Palette.Hex(0xa98244)
        };
        static readonly Color[] BandTip =
        {
            Palette.Hex(0x8c9c50), Palette.Hex(0xa49c5e), Palette.Hex(0xc0ae68), Palette.Hex(0xcab27c),
            Palette.Hex(0x667a3c), Palette.Hex(0xdcbd76), Palette.Hex(0xcdbf76), Palette.Hex(0xd7b06a)
        };

        static float Ss(float e0, float e1, float x) { float t = Mathf.Clamp01((x - e0) / (e1 - e0)); return t * t * (3f - 2f * t); }

        static Color32[] PaintVeg(int n)
        {
            var px = new Color32[n * n];
            int bw = n / Bands;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int band = x / bw;
                    float u = ((x % bw) + 0.5f) / bw, v = (y + 0.5f) / n;
                    Color b = BandBase[band], t = BandTip[band], c;
                    float streak = 0.9f + 0.2f * CoreLibrary.Hash(x * 3, (int)(v * 48f), 811 + band);
                    if (band == Ear)
                    {
                        // a wheat ear: two staggered columns of plump kernels, darker seams, the awn-side edges shaded
                        float col = u < 0.5f ? 0f : 0.5f;
                        float k = Mathf.Repeat(v * 15f + col, 1f);
                        float kernel = Mathf.Sin(k * Mathf.PI);
                        float side = Mathf.Sin(Mathf.Repeat(u * 2f, 1f) * Mathf.PI);
                        c = Color.Lerp(b * 0.62f, t, kernel * (0.55f + 0.45f * side));
                        c *= 0.82f + 0.18f * Mathf.Sin(u * Mathf.PI);
                        c *= Mathf.Lerp(0.8f, 1f, Ss(0f, 0.25f, v));                 // the base of the ear in the stalk's shade
                    }
                    else if (band == Straw || band == StrawGreen)
                    {
                        // a stalk: green-gold below, straw above, faint nodes, a lit ridge down the middle
                        c = Color.Lerp(b * 0.55f, b, Ss(0f, 0.3f, v));
                        c = Color.Lerp(c, t, Ss(0.35f, 1f, v));
                        float node = Mathf.Exp(-Mathf.Pow((Mathf.Repeat(v * 4f, 1f) - 0.5f) / 0.03f, 2f));
                        c *= 1f - 0.18f * node;
                        c *= 0.85f + 0.2f * Mathf.Sin(u * Mathf.PI);
                    }
                    else
                    {
                        // a grass blade: shaded base (the tuft's own shadow), lighter tip, a pale midrib, fine veins
                        c = Color.Lerp(b * 0.4f, b, Ss(0f, 0.35f, v));
                        c = Color.Lerp(c, t, Ss(0.45f, 1f, v));
                        float rib = Mathf.Exp(-Mathf.Pow((u - 0.5f) / 0.09f, 2f));
                        c *= 1f + 0.12f * rib;
                        if (band == Mixed || band == Yellow) c = Color.Lerp(c, BandTip[Dry], Ss(0.82f, 1f, v) * 0.6f);   // sun-dried tips
                    }
                    c *= streak;
                    c.a = 1f;
                    px[y * n + x] = c;
                }
            return px;
        }

        // ------------------------------------------------------------------ meshes (run time)

        /// <summary>u range (inset) of a band of the atlas.</summary>
        static Vector2 BandU(int band, float inset = 0.22f)
        {
            float w = 1f / Bands;
            return new Vector2(band * w + w * inset, (band + 1) * w - w * inset);
        }

        sealed class Geo
        {
            public readonly List<Vector3> v = new List<Vector3>(4096);
            public readonly List<Vector3> n = new List<Vector3>(4096);
            public readonly List<Vector2> uv = new List<Vector2>(4096);
            public readonly List<int> t = new List<int>(8192);

            public Mesh ToMesh(string name)
            {
                var m = new Mesh { name = name };
                m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0);
                m.RecalculateBounds();
                m.UploadMeshData(true);
                return m;
            }
        }

        /// <summary>One curved, tapering strip from base to tip: segments quads and a pointed tip. dir = horizontal
        /// direction the blade leans and curls toward; width across is perpendicular to it.</summary>
        static void Blade(Geo g, Vector3 basePos, Vector2 dir, float height, float tilt, float curl, float width, int segs, int band, float taper = 0.8f, float vTop = 1f)
        {
            Vector3 d = new Vector3(dir.x, 0f, dir.y), side = new Vector3(-dir.y, 0f, dir.x);
            Vector2 bu = BandU(band);
            int first = g.v.Count;
            for (int i = 0; i <= segs; i++)
            {
                float t = (float)i / segs;
                float off = (tilt * t + curl * t * t) * height;
                float y = height * t * (1f - 0.5f * curl * curl * t * t - 0.25f * tilt * tilt * t);
                Vector3 c = basePos + d * off + Vector3.up * y;
                // the slope of the blade here: the normal leans away from it, bent up (meadow light), the same for both faces
                Vector3 tangent = (d * (tilt + 2f * curl * t) * height + Vector3.up * height).normalized;
                Vector3 face = Vector3.Cross(side, tangent);
                if (face.y < 0f) face = -face;
                Vector3 nrm = (face * 0.35f + Vector3.up * 0.75f).normalized;
                float vv = t * vTop;
                if (i == segs)
                {
                    g.v.Add(c); g.n.Add(nrm); g.uv.Add(new Vector2((bu.x + bu.y) * 0.5f, vv));
                }
                else
                {
                    float w = width * Mathf.Pow(1f - t, taper) * 0.5f;
                    g.v.Add(c - side * w); g.n.Add(nrm); g.uv.Add(new Vector2(bu.x, vv));
                    g.v.Add(c + side * w); g.n.Add(nrm); g.uv.Add(new Vector2(bu.y, vv));
                }
            }
            for (int i = 0; i < segs - 1; i++)
            {
                int a = first + i * 2;
                g.t.Add(a); g.t.Add(a + 2); g.t.Add(a + 1);
                g.t.Add(a + 1); g.t.Add(a + 2); g.t.Add(a + 3);
            }
            int last = first + (segs - 1) * 2;
            g.t.Add(last); g.t.Add(last + 2); g.t.Add(last + 1);
        }

        /// <summary>A tuft of grass, unit height (the instance scales y): blades fan out from a small crown.
        /// palette: which bands the blades take (lush meadow, mixed, dry verge, wet ditch).</summary>
        public static Mesh Tuft(int palette, uint seed, int blades, int segs, string name)
        {
            var g = new Geo();
            int[][] pal =
            {
                new[] { Lush, Lush, Mixed, Lush, Ditch },
                new[] { Mixed, Lush, Yellow, Mixed, Dry },
                new[] { Yellow, Dry, Dry, Mixed, Yellow },
                new[] { Ditch, Lush, Ditch, Mixed, Lush }
            };
            int[] p = pal[palette % pal.Length];
            for (int b = 0; b < blades; b++)
            {
                uint h = WeatherLibrary.Hash(seed * 7919u + (uint)b * 104729u + 17u);
                float r0 = 0.075f * Mathf.Sqrt(WeatherLibrary.U(h, 1)), a0 = WeatherLibrary.U(h, 2) * Mathf.PI * 2f;
                Vector3 bp = new Vector3(Mathf.Cos(a0) * r0, 0f, Mathf.Sin(a0) * r0);
                float ad = a0 + (WeatherLibrary.U(h, 3) - 0.5f) * 1.4f;
                Vector2 dir = new Vector2(Mathf.Cos(ad), Mathf.Sin(ad));
                float hh = Mathf.Lerp(0.5f, 1f, WeatherLibrary.U(h, 4));
                float tilt = Mathf.Lerp(0.06f, 0.5f, WeatherLibrary.U(h, 5));
                float curl = Mathf.Lerp(0.04f, 0.42f, WeatherLibrary.U(h, 6) * WeatherLibrary.U(h, 6));
                float w = Mathf.Lerp(0.0075f, 0.014f, WeatherLibrary.U(h, 7));
                int band = p[(int)(WeatherLibrary.U(h, 8) * p.Length) % p.Length];
                Blade(g, bp, dir, hh, tilt, curl, w, segs, band);
            }
            return g.ToMesh(name);
        }

        /// <summary>A square patch of ripe wheat, about 1.4 m across, unit height: straight stalks with a slight
        /// random lean and nodding ears (two crossed strips each). near = more stalks and segments.</summary>
        public static Mesh WheatPatch(uint seed, bool near, string name)
        {
            var g = new Geo();
            int grid = near ? 8 : 6;                         // 64 or 36 stalks on a jittered grid
            float cell = 1.4f / grid;
            Vector2 ear = BandU(Ear, 0.12f);
            for (int iy = 0; iy < grid; iy++)
                for (int ix = 0; ix < grid; ix++)
                {
                    uint h = WeatherLibrary.Hash(seed * 31337u + (uint)(iy * grid + ix) * 2654435761u + 5u);
                    Vector3 bp = new Vector3(-0.7f + (ix + WeatherLibrary.U(h, 1)) * cell, 0f, -0.7f + (iy + WeatherLibrary.U(h, 2)) * cell);
                    float a = WeatherLibrary.U(h, 3) * Mathf.PI * 2f;
                    Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    float hh = Mathf.Lerp(0.8f, 1f, WeatherLibrary.U(h, 4));
                    float tilt = Mathf.Lerp(0f, 0.12f, WeatherLibrary.U(h, 5));
                    int band = WeatherLibrary.U(h, 6) < 0.75f ? Straw : StrawGreen;
                    Blade(g, bp, dir, hh, tilt, 0.02f, near ? 0.009f : 0.012f, near ? 3 : 1, band, 0.25f, 0.85f);
                    // the ear: nodding a little away from the lean, two crossed strips
                    float len = Mathf.Lerp(0.09f, 0.12f, WeatherLibrary.U(h, 7)), wid = near ? 0.017f : 0.022f;
                    float off = (tilt + 0.02f) * hh;
                    Vector3 top = bp + new Vector3(dir.x, 0f, dir.y) * off + Vector3.up * (hh * (1f - 0.25f * tilt * tilt));
                    float nod = Mathf.Lerp(0.1f, 0.45f, WeatherLibrary.U(h, 8));
                    Vector3 axis = (Vector3.up + new Vector3(dir.x, 0f, dir.y) * nod).normalized;
                    Vector3 s1 = Vector3.Cross(axis, new Vector3(dir.y, 0f, -dir.x)).normalized;
                    if (s1.sqrMagnitude < 1e-4f) s1 = Vector3.right;
                    Vector3 s2 = Vector3.Cross(axis, s1).normalized;
                    for (int k = 0; k < (near ? 2 : 1); k++)
                    {
                        Vector3 sd = (k == 0 ? s1 : s2) * (wid * 0.5f);
                        Vector3 b0 = top - axis * (len * 0.15f), b1 = top + axis * (len * 0.85f);
                        Vector3 nrm = (Vector3.up * 0.8f + (k == 0 ? s2 : s1) * 0.3f).normalized;
                        int f = g.v.Count;
                        g.v.Add(b0 - sd); g.v.Add(b0 + sd); g.v.Add(b1 - sd * 0.55f); g.v.Add(b1 + sd * 0.55f);
                        for (int q = 0; q < 4; q++) g.n.Add(nrm);
                        g.uv.Add(new Vector2(ear.x, 0f)); g.uv.Add(new Vector2(ear.y, 0f)); g.uv.Add(new Vector2(ear.x, 1f)); g.uv.Add(new Vector2(ear.y, 1f));
                        g.t.Add(f); g.t.Add(f + 2); g.t.Add(f + 1);
                        g.t.Add(f + 1); g.t.Add(f + 2); g.t.Add(f + 3);
                    }
                }
            return g.ToMesh(name);
        }
    }
}
