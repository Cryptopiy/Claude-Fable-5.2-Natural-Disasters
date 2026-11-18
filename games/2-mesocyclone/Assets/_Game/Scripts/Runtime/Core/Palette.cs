using UnityEngine;

namespace MESOCYCLONE
{
    /// <summary>Colour tokens (sRGB hex in the spec). One source for code and shaders: PushGlobals() exports them
    /// as linear shader globals named _Pal*.</summary>
    public static class Palette
    {
        public static readonly Color SunsetOrange = Hex(0xff9a4a);
        public static readonly Color AnvilPink = Hex(0xd98a8f);
        public static readonly Color StormGreen = Hex(0x6f9a82);
        public static readonly Color StormSlate = Hex(0x3b4655);
        public static readonly Color CoreBlack = Hex(0x0a0e14);
        public static readonly Color FlashWhite = Hex(0xe8f0ff);
        public static readonly Color FlashBlue = Hex(0x9bbcff);
        public static readonly Color TransformerTeal = Hex(0x6affd8);
        public static readonly Color Sodium = Hex(0xffb347);
        public static readonly Color DashAmber = Hex(0xffb74a);
        public static readonly Color DashGreen = Hex(0x79ff8a);
        public static readonly Color TurbineRed = Hex(0xff2a2a);
        public static readonly Color StarWhite = Hex(0xdfe8ff);

        // derived working colours (not tokens of their own: mixes of the tokens above)
        // about 3300 K, "sodium warmed toward white": the paler #ffe2b0 (4000 K) went neutral under the cold grade.
        // Also warms the mirror's canopy fill in the cab (MirrorFx), on purpose. Fallback if it stacks to orange: #ffd8a8
        public static readonly Color CanopyWarm = Hex(0xffd09a);
        public static readonly Color RoadPaintYellow = Hex(0xd9a520);
        public static readonly Color RoadPaintWhite = Hex(0xd8d8d0);
        public static readonly Color Asphalt = Hex(0x2a2b2e);
        public static readonly Color FieldWheat = Hex(0xa88f4a);
        public static readonly Color FieldGreen = Hex(0x4c6a2c);
        public static readonly Color FieldSoil = Hex(0x4a3a2a);
        public static readonly Color FieldStubble = Hex(0x8a7a52);
        public static readonly Color CabPlastic = Hex(0x1c1b1a);
        public static readonly Color CabFabric = Hex(0x2a2622);
        public static readonly Color TruckPaint = Hex(0x6a1a14);
        // real-material targets (mean albedo of the photo textures, which are normalised and tinted by these)
        public static readonly Color AsphaltAged = Hex(0x3a3b3e);      // weathered two-lane asphalt (dry); the rain darkens it at run time
        public static readonly Color GravelShoulder = Hex(0x6a6258);
        public static readonly Color VergeGrass = Hex(0x5a6432);
        public static readonly Color VergeDry = Hex(0x857a4e);         // sun-dried grass along the gravel
        public static readonly Color DitchGreen = Hex(0x48582c);       // the wetter ditch bottom
        public static readonly Color ConcreteApron = Hex(0x9a968c);

        /// <summary>sRGB hex to a Unity Color (stored gamma; use .linear for light and shader maths).</summary>
        public static Color Hex(int rgb)
        {
            return new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1f);
        }

        public static Color Lin(Color srgb) => srgb.linear;

        public static Vector4 LinV(Color srgb, float scale = 1f)
        {
            Color c = srgb.linear;
            return new Vector4(c.r * scale, c.g * scale, c.b * scale, 1f);
        }

        static readonly int idOrange = Shader.PropertyToID("_PalSunsetOrange");
        static readonly int idPink = Shader.PropertyToID("_PalAnvilPink");
        static readonly int idGreen = Shader.PropertyToID("_PalStormGreen");
        static readonly int idSlate = Shader.PropertyToID("_PalStormSlate");
        static readonly int idBlack = Shader.PropertyToID("_PalCoreBlack");
        static readonly int idFlashW = Shader.PropertyToID("_PalFlashWhite");
        static readonly int idFlashB = Shader.PropertyToID("_PalFlashBlue");
        static readonly int idTeal = Shader.PropertyToID("_PalTransformerTeal");
        static readonly int idSodium = Shader.PropertyToID("_PalSodium");
        static readonly int idStar = Shader.PropertyToID("_PalStarWhite");
        static readonly int idRed = Shader.PropertyToID("_PalTurbineRed");

        public static void PushGlobals()
        {
            Shader.SetGlobalVector(idOrange, LinV(SunsetOrange));
            Shader.SetGlobalVector(idPink, LinV(AnvilPink));
            Shader.SetGlobalVector(idGreen, LinV(StormGreen));
            Shader.SetGlobalVector(idSlate, LinV(StormSlate));
            Shader.SetGlobalVector(idBlack, LinV(CoreBlack));
            Shader.SetGlobalVector(idFlashW, LinV(FlashWhite));
            Shader.SetGlobalVector(idFlashB, LinV(FlashBlue));
            Shader.SetGlobalVector(idTeal, LinV(TransformerTeal));
            Shader.SetGlobalVector(idSodium, LinV(Sodium));
            Shader.SetGlobalVector(idStar, LinV(StarWhite));
            Shader.SetGlobalVector(idRed, LinV(TurbineRed));
        }
    }
}
