using System.Collections.Generic;
using UnityEngine;

namespace MESOCYCLONE
{
    /// <summary>Swatch indices of the flat-colour atlas (8 x 8). Geometry built with MeshBuilder.Swatch(i) and the
    /// material "atlas" (or "atlas_metal") gets these colours without one material per colour.</summary>
    public static class Sw
    {
        public const int Asphalt = 0, PaintWhite = 1, PaintYellow = 2, Gravel = 3, Wheat = 4, Crop = 5, Soil = 6, Stubble = 7;
        public const int Wood = 8, Galvanized = 9, White = 10, BarnRed = 11, RoofDark = 12, Concrete = 13, HouseBeige = 14, HouseBlue = 15;
        public const int HouseGreen = 16, Tree = 17, CarDark = 18, CarLight = 19, SignGreen = 20, SignYellow = 21, Rust = 22, Verge = 23;
        public const int GlassDark = 24, Tyre = 25, Chrome = 26, CanopyWhite = 27, PumpRed = 28, SteelBlue = 29, Brick = 30, Hay = 31;
        public const int CabPlastic = 32, CabFabric = 33, CabLeather = 34, CabTrim = 35, TruckPaint = 36, Rubber = 37, CabLiner = 38, CabMetal = 39;
        public const int SignWhite = 40, SignRed = 41, Black = 42, Grey20 = 43, Grey40 = 44, Grey60 = 45, HouseWhite = 46, RoofRed = 47;
        public const int Trunk = 48, DebrisWood = 49, SheetMetal = 50, Cream = 51, DarkGreen = 52, Orange = 53, Tan = 54, Navy = 55;
        public const int DashGrey = 56, SeatCloth = 57, Carpet = 58, HeadlinerGrey = 59, PlasticGloss = 60, Aluminium = 61, WarmWhite = 62, DeepRed = 63;

        public static readonly Color[] Colors = Build();

        static Color[] Build()
        {
            var c = new Color[64];
            for (int i = 0; i < 64; i++) c[i] = new Color(0.5f, 0.5f, 0.5f);
            c[Asphalt] = Palette.Asphalt; c[PaintWhite] = Palette.RoadPaintWhite; c[PaintYellow] = Palette.RoadPaintYellow;
            c[Gravel] = Palette.Hex(0x6a6258); c[Wheat] = Palette.FieldWheat; c[Crop] = Palette.FieldGreen; c[Soil] = Palette.FieldSoil;
            c[Stubble] = Palette.FieldStubble; c[Wood] = Palette.Hex(0x3a2a1c); c[Galvanized] = Palette.Hex(0x9aa0a2);
            c[White] = Palette.Hex(0xd9d9d4); c[BarnRed] = Palette.Hex(0x6e2018); c[RoofDark] = Palette.Hex(0x2c2a2a);
            c[Concrete] = Palette.Hex(0xa39f95); c[HouseBeige] = Palette.Hex(0xb9ab8c); c[HouseBlue] = Palette.Hex(0x6f8292);
            c[HouseGreen] = Palette.Hex(0x67775a); c[Tree] = Palette.Hex(0x1f2e17); c[CarDark] = Palette.Hex(0x1c2026);
            c[CarLight] = Palette.Hex(0x9a9c9e); c[SignGreen] = Palette.Hex(0x1f5a3a); c[SignYellow] = Palette.Hex(0xd9b01c);
            c[Rust] = Palette.Hex(0x5a3a26); c[Verge] = Palette.Hex(0x5a6432); c[GlassDark] = Palette.Hex(0x0c1014);
            c[Tyre] = Palette.Hex(0x0e0e0e); c[Chrome] = Palette.Hex(0xc8ccd0); c[CanopyWhite] = Palette.Hex(0xe8e6e0);
            c[PumpRed] = Palette.Hex(0x8a1a16); c[SteelBlue] = Palette.Hex(0x3a4a5a); c[Brick] = Palette.Hex(0x6a3a2c); c[Hay] = Palette.Hex(0xb09a52);
            c[CabPlastic] = Palette.CabPlastic; c[CabFabric] = Palette.CabFabric; c[CabLeather] = Palette.Hex(0x161412);
            c[CabTrim] = Palette.Hex(0x3a3836); c[TruckPaint] = Palette.TruckPaint; c[Rubber] = Palette.Hex(0x0b0b0b);
            c[CabLiner] = Palette.Hex(0x4a463f); c[CabMetal] = Palette.Hex(0x6a6c6e);
            c[SignWhite] = Palette.Hex(0xe6e6e6); c[SignRed] = Palette.Hex(0xa81c14); c[Black] = Palette.Hex(0x020202);
            c[Grey20] = Palette.Hex(0x333333); c[Grey40] = Palette.Hex(0x666666); c[Grey60] = Palette.Hex(0x999999);
            c[HouseWhite] = Palette.Hex(0xcfcabd); c[RoofRed] = Palette.Hex(0x5a2a22);
            c[Trunk] = Palette.Hex(0x2e241a); c[DebrisWood] = Palette.Hex(0x9a8562); c[SheetMetal] = Palette.Hex(0xb4b8ba); c[Cream] = Palette.Hex(0xd8cfb4);
            c[DarkGreen] = Palette.Hex(0x1e3a2a); c[Orange] = Palette.Hex(0xd9641c); c[Tan] = Palette.Hex(0x8a7355); c[Navy] = Palette.Hex(0x1a2433);
            c[DashGrey] = Palette.Hex(0x24231f); c[SeatCloth] = Palette.Hex(0x33302b); c[Carpet] = Palette.Hex(0x15140f); c[HeadlinerGrey] = Palette.Hex(0x56524a);
            c[PlasticGloss] = Palette.Hex(0x0e0e0e); c[Aluminium] = Palette.Hex(0x8e9296); c[WarmWhite] = Palette.Hex(0xefe6d2); c[DeepRed] = Palette.Hex(0x4a0f0c);
            return c;
        }
    }

    /// <summary>Surface classes of the swatch-coloured props. The colour stays per face (swatch atlas on UV0); the
    /// class adds a real surface through an HDRP detail map on UV1 (box-projected metres written by MeshBuilder):
    /// grain, grime, ribs, mortar, scratches and their roughness. MeshBuilder.ToObject moves the triangles of a
    /// class out of "atlas"/"atlas_metal" into the class material; a builder can override the class of what follows
    /// with MeshBuilder.Surface().</summary>
    public static class Surf
    {
        public const int None = 0, Paint = 1, CarPaint = 2, Concrete = 3, Siding = 4, Brick = 5, Roof = 6, Corrugated = 7, CorrugatedH = 8,
            Galvanized = 9, Wood = 10, Bark = 11, Rust = 12, Rubber = 13, Asphalt = 14, Gravel = 15, Soil = 16, Glass = 17, Foliage = 18,
            Plastic = 19, Count = 20;

        public static readonly string[] MatName =
        {
            "", "surf_paint", "surf_carpaint", "surf_concrete", "surf_siding", "surf_brick", "surf_roof", "surf_corrugated", "surf_corrugated_h",
            "surf_galvanized", "surf_wood", "surf_bark", "surf_rust", "surf_rubber", "surf_asphalt", "surf_gravel", "surf_soil", "surf_glass",
            "surf_foliage", "surf_plastic"
        };

        static readonly byte[] bySwatch = Build();

        /// <summary>Default class of a swatch colour (None = stays on the material it was built with).</summary>
        public static int Of(int swatch) => swatch >= 0 && swatch < bySwatch.Length ? bySwatch[swatch] : None;

        static byte[] Build()
        {
            var t = new byte[64];
            void S(int sw, int c) { t[sw] = (byte)c; }
            S(Sw.Asphalt, Asphalt); S(Sw.PaintWhite, Paint); S(Sw.PaintYellow, Paint); S(Sw.Gravel, Gravel); S(Sw.Soil, Soil);
            S(Sw.Wood, Wood); S(Sw.Galvanized, Galvanized); S(Sw.White, Paint); S(Sw.BarnRed, Siding); S(Sw.RoofDark, Roof);
            S(Sw.Concrete, Concrete); S(Sw.HouseBeige, Siding); S(Sw.HouseBlue, Siding); S(Sw.HouseGreen, Siding); S(Sw.Tree, Foliage);
            S(Sw.CarDark, CarPaint); S(Sw.CarLight, CarPaint); S(Sw.SignGreen, Paint); S(Sw.SignYellow, Paint); S(Sw.Rust, Rust);
            S(Sw.Verge, Foliage); S(Sw.GlassDark, Glass); S(Sw.Tyre, Rubber); S(Sw.CanopyWhite, Paint); S(Sw.PumpRed, Paint);
            S(Sw.SteelBlue, Paint); S(Sw.Brick, Brick); S(Sw.SignWhite, Paint); S(Sw.SignRed, Paint); S(Sw.Black, Paint);
            S(Sw.Grey20, Paint); S(Sw.Grey40, Galvanized); S(Sw.Grey60, Galvanized); S(Sw.HouseWhite, Siding); S(Sw.RoofRed, Roof);
            S(Sw.Trunk, Bark); S(Sw.DebrisWood, Wood); S(Sw.SheetMetal, Corrugated); S(Sw.Cream, Siding); S(Sw.DarkGreen, Foliage);
            S(Sw.Orange, Plastic); S(Sw.Tan, Siding); S(Sw.Navy, CarPaint); S(Sw.Aluminium, Galvanized); S(Sw.WarmWhite, Glass);
            S(Sw.DeepRed, CarPaint);
            return t;
        }
    }

    /// <summary>Texture names made from one downloaded Poly Haven set (Assets/_Game/Textures/External/&lt;id&gt;) and
    /// their definitions. Several modules may declare the same set: the library keeps the first definition.</summary>
    public static class Photo
    {
        public static string C(string id) => "x_" + id + "_c";      // albedo normalised to a mean of 0.5 (linear)
        public static string M(string id) => "x_" + id + "_m";      // HDRP mask map
        public static string D(string id) => "x_" + id + "_d";      // HDRP detail map
        public static string N(string id) => "ext:" + id;           // the imported normal map (OpenGL convention = Unity's)

        public static TexDef Albedo(string id, int size, float chroma = 0.5f, float contrast = 1f)
            => new TexDef(C(id), size, new[] { id + "/diff" }, (n, s) => TexPaint.Albedo(n, s[0], chroma, contrast)) { aniso = 16 };

        public static TexDef Mask(string id, int size, float ao = 1f)
            => new TexDef(M(id), size, new[] { id + "/arm" }, (n, s) => TexPaint.Mask(n, s[0], ao)) { linear = true, aniso = 16 };

        public static TexDef Detail(string id, int size, float albedo = 1f, float smooth = 1f, bool transpose = false, string name = null)
            => new TexDef(name ?? D(id), size, new[] { id + "/diff", id + "/nor_gl", id + "/arm" },
                (n, s) => TexPaint.Detail(n, s[0], s[1], s[2], albedo, smooth, transpose)) { linear = true, aniso = 16 };

        /// <summary>A procedural detail map (TexPaint.DetailProc kinds).</summary>
        public static TexDef Proc(string name, int size, int kind, int seed)
            => new TexDef(name, size, n => TexPaint.DetailProc(n, kind, seed)) { linear = true, hq = true, rev = 1, aniso = 16 };
    }

    /// <summary>Materials and textures of the core systems and of the land.</summary>
    public static class CoreLibrary
    {
        public const string GrimeD = "grime_d", GalvD = "galv_d", FoliageD = "foliage_d", RubberD = "rubber_d", PaintD = "paint_d",
            DustD = "dust_d", SmudgeD = "smudge_d", MacroD = "macro_d", StainsD = "stains_d";

        public static void Textures(List<TexDef> l)
        {
            l.Add(new TexDef("swatch", 64, PaintSwatch) { mips = false, point = true });
            // procedural detail maps (weathering and micro-surface)
            l.Add(Photo.Proc(GrimeD, 512, TexPaint.Grime, 501));
            l.Add(Photo.Proc(GalvD, 512, TexPaint.Galvanized, 502));
            l.Add(Photo.Proc(FoliageD, 512, TexPaint.Foliage, 503));
            l.Add(Photo.Proc(RubberD, 256, TexPaint.Rubber, 504));
            l.Add(Photo.Proc(PaintD, 1024, TexPaint.OrangePeel, 505));
            l.Add(Photo.Proc(DustD, 512, TexPaint.Dust, 506));
            l.Add(Photo.Proc(SmudgeD, 512, TexPaint.Smudge, 507));
            l.Add(Photo.Proc(MacroD, 512, TexPaint.Macro, 508));
            l.Add(Photo.Proc(StainsD, 512, TexPaint.Stains, 509));
            // photo detail maps of the surface classes (1k: they tile at 1.5..3 m on props seen from 5 m and beyond)
            l.Add(Photo.Detail("blue_metal_plate", 1024, 1.2f, 1f));
            l.Add(Photo.Detail("concrete_floor_worn_001", 1024, 1.6f, 1f));
            l.Add(Photo.Detail("weathered_plank_siding", 1024, 0.9f, 1f));
            l.Add(Photo.Detail("factory_brick", 1024, 0.9f, 0.8f));
            l.Add(Photo.Detail("roof_slates_03", 1024, 0.9f, 0.8f));
            l.Add(Photo.Detail("corrugated_iron_03", 1024, 1.2f, 1f));
            l.Add(Photo.Detail("corrugated_iron_03", 1024, 1.2f, 1f, true, "x_corrugated_iron_03_dt"));
            l.Add(Photo.Detail("old_planks_02", 1024, 1.1f, 1f));
            l.Add(Photo.Detail("bark_brown_02", 1024, 1f, 0.6f));
            l.Add(Photo.Detail("rusty_metal_04", 1024, 0.8f, 1f));
            l.Add(Photo.Detail("asphalt_02", 1024, 1f, 1f));
            l.Add(Photo.Detail("gravel_road", 1024, 1.2f, 1f));
            l.Add(Photo.Detail("farm_soil", 1024, 1f, 1f));
        }

        /// <summary>A swatch-coloured material with a detail map on UV1 tiling every 'tile' metres.</summary>
        static MatDef Class(string name, string detail, float tile, float smooth, float metallic = 0f, float da = 1f, float dn = 1f, float dsm = 1f)
            => new MatDef(name)
            {
                color = Color.white, baseMap = "swatch", smoothness = smooth, metallic = metallic, detailMap = detail, detailUV = 1,
                detailTiling = new Vector2(1f / tile, 1f / tile), detailAlbedo = da, detailNormal = dn, detailSmooth = dsm
            };

        public static void Materials(List<MatDef> l)
        {
            // the two base materials keep their names and roles; they now carry generic weathering
            l.Add(Class("atlas", GrimeD, 2.2f, 0.22f));
            l.Add(Class("atlas_metal", GalvD, 0.8f, 0.62f, 0.85f));
            l.Add(Class("ground", MacroD, 140f, 0.12f));
            l.Add(Class("road", Photo.D("asphalt_02"), 3f, 0.3f));
            // surface classes (see Surf)
            l.Add(Class(Surf.MatName[Surf.Paint], Photo.D("blue_metal_plate"), 2.5f, 0.34f, 0f, 1f, 0.8f));
            MatDef car = Class(Surf.MatName[Surf.CarPaint], PaintD, 0.5f, 0.55f, 0.1f, 1f, 1f);
            car.coatMask = 1f;
            l.Add(car);
            l.Add(Class(Surf.MatName[Surf.Concrete], Photo.D("concrete_floor_worn_001"), 3f, 0.24f, 0f, 1f, 1.2f));
            l.Add(Class(Surf.MatName[Surf.Siding], Photo.D("weathered_plank_siding"), 1.57f, 0.2f, 0f, 1f, 1.3f));
            l.Add(Class(Surf.MatName[Surf.Brick], Photo.D("factory_brick"), 1.5f, 0.16f, 0f, 1f, 1.2f));
            l.Add(Class(Surf.MatName[Surf.Roof], Photo.D("roof_slates_03"), 3f, 0.26f, 0f, 1f, 1.2f));
            l.Add(Class(Surf.MatName[Surf.Corrugated], Photo.D("corrugated_iron_03"), 2f, 0.42f, 0.7f, 1f, 1.4f));
            l.Add(Class(Surf.MatName[Surf.CorrugatedH], "x_corrugated_iron_03_dt", 2f, 0.42f, 0.7f, 1f, 1.4f));
            l.Add(Class(Surf.MatName[Surf.Galvanized], GalvD, 0.8f, 0.5f, 0.8f));
            l.Add(Class(Surf.MatName[Surf.Wood], Photo.D("old_planks_02"), 2f, 0.16f, 0f, 1f, 1.3f));
            l.Add(Class(Surf.MatName[Surf.Bark], Photo.D("bark_brown_02"), 1f, 0.1f, 0f, 1f, 1.6f));
            l.Add(Class(Surf.MatName[Surf.Rust], Photo.D("rusty_metal_04"), 2f, 0.3f, 0.25f));
            l.Add(Class(Surf.MatName[Surf.Rubber], RubberD, 0.15f, 0.22f));
            l.Add(Class(Surf.MatName[Surf.Asphalt], Photo.D("asphalt_02"), 3f, 0.3f));
            l.Add(Class(Surf.MatName[Surf.Gravel], Photo.D("gravel_road"), 2f, 0.14f, 0f, 1f, 1.3f));
            l.Add(Class(Surf.MatName[Surf.Soil], Photo.D("farm_soil"), 2f, 0.1f, 0f, 1f, 1.3f));
            l.Add(Class(Surf.MatName[Surf.Glass], SmudgeD, 0.8f, 0.92f, 0f, 0.5f, 0f, 0.6f));
            l.Add(Class(Surf.MatName[Surf.Foliage], FoliageD, 2.5f, 0.12f));
            l.Add(Class(Surf.MatName[Surf.Plastic], GrimeD, 1f, 0.42f));
            l.Add(MatDef.Glow("glow_red", Palette.TurbineRed, 9000f));
            l.Add(MatDef.Glow("glow_sodium", Palette.Sodium, 2600f));
            l.Add(MatDef.Glow("glow_window", Palette.Hex(0xffd9a0), 260f));
            l.Add(MatDef.Glow("glow_canopy", Palette.CanopyWarm, 5200f));
            l.Add(MatDef.Glow("glow_teal", Palette.TransformerTeal, 60000f, MatSurface.Additive));
            l.Add(MatDef.Glow("glow_bolt", Palette.FlashWhite, 400000f, MatSurface.Additive));
            l.Add(MatDef.Glow("glow_amber", Palette.DashAmber, 180f));
        }

        static Color32[] PaintSwatch(int n)
        {
            var px = new Color32[n * n];
            int cell = n / MeshBuilder.SwatchGrid;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    px[y * n + x] = Sw.Colors[(y / cell) * MeshBuilder.SwatchGrid + (x / cell)];
            return px;
        }

        public static float Hash(int x, int y, int seed)
        {
            uint h = (uint)x * 374761393u + (uint)y * 668265263u + (uint)seed * 2246822519u;
            h = (h ^ (h >> 13)) * 1274126177u; h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777216f;
        }

        /// <summary>Tiling value noise 0..1 with the given number of cells per side.</summary>
        public static float Value(float u, float v, int cells, int seed)
        {
            float x = u * cells, y = v * cells;
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float fx = x - xi, fy = y - yi;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            int x0 = ((xi % cells) + cells) % cells, x1 = (x0 + 1) % cells, y0 = ((yi % cells) + cells) % cells, y1 = (y0 + 1) % cells;
            return Mathf.Lerp(Mathf.Lerp(Hash(x0, y0, seed), Hash(x1, y0, seed), fx), Mathf.Lerp(Hash(x0, y1, seed), Hash(x1, y1, seed), fx), fy);
        }

        public static float Fbm(float u, float v, int cells, int octaves, int seed)
        {
            float a = 0.5f, s = 0f, norm = 0f;
            for (int o = 0; o < octaves; o++) { s += a * Value(u, v, cells << o, seed + o * 17); norm += a; a *= 0.5f; }
            return s / norm;
        }

        /// <summary>A flat colour with fine grain (tiling).</summary>
        public static Color32[] PaintGrain(int n, int hex, float amount, int seed)
        {
            Color c = Palette.Hex(hex);
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float g = 1f + (Fbm((x + 0.5f) / n, (y + 0.5f) / n, 32, 4, seed) - 0.5f) * 2f * amount + (Hash(x, y, seed) - 0.5f) * amount;
                    px[y * n + x] = new Color(c.r * g, c.g * g, c.b * g, 1f);
                }
            return px;
        }

        /// <summary>A tiling height field in the red channel (for normal maps).</summary>
        public static Color32[] PaintHeight(int n, int seed, int cells)
        {
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float h = Fbm((x + 0.5f) / n, (y + 0.5f) / n, cells * 4, 5, seed) * 0.7f + Hash(x, y, seed) * 0.3f;
                    byte b = (byte)(Mathf.Clamp01(h) * 255f);
                    px[y * n + x] = new Color32(b, b, b, 255);
                }
            return px;
        }
    }
}
