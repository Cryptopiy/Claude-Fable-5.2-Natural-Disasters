using UnityEngine;

namespace ZERO
{
    /// <summary>Colour tokens of the game (sRGB hex). One source for code and shaders:
    /// <see cref="PushToShaders"/> publishes every token as a linear global colour named _Pal&lt;Name&gt;.</summary>
    public static class Palette
    {
        public static readonly Color SkyWhite = Hex(0xf2f7ff);
        public static readonly Color SkyBlue = Hex(0xbcd4f2);
        public static readonly Color Haze = Hex(0xdfe8f2);
        public static readonly Color Wheat = Hex(0xd8c48a);
        public static readonly Color FieldGreen = Hex(0x9fb27a);
        public static readonly Color RoadGrey = Hex(0x8e9096);
        public static readonly Color DustTan = Hex(0xc9b089);
        public static readonly Color ShadowBlue = Hex(0x2d3b5c);
        public static readonly Color ShadowDeep = Hex(0x161d33);
        public static readonly Color PlasmaWhite = Hex(0xfff7e6);
        public static readonly Color PlasmaGold = Hex(0xffd27a);
        public static readonly Color PlasmaOrange = Hex(0xff8a3c);
        public static readonly Color FireRed = Hex(0xe0401c);
        public static readonly Color RockDark = Hex(0x2a2420);
        public static readonly Color Molten = Hex(0xffb04a);

        // Derived working tones (still built from the tokens above).
        public static readonly Color WoodPale = Hex(0xb9a888);
        public static readonly Color WoodDark = Hex(0x6f6252);
        public static readonly Color BarnRed = Hex(0xa8483a);
        public static readonly Color MetalPale = Hex(0xd9dde2);
        public static readonly Color TurbineWhite = Hex(0xf0f2f4);
        public static readonly Color Soil = Hex(0x6b5a48);
        public static readonly Color Scorch = Hex(0x231d1b);
        public static readonly Color LeafGreen = Hex(0x7d9a5e);
        public static readonly Color Glass = Hex(0xcfe6f2);

        public static Color Hex(int rgb)
        {
            return new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1f);
        }

        public static Color Mix(Color a, Color b, float t) { return Color.Lerp(a, b, t); }

        /// <summary>Publishes all tokens to shaders (linear).</summary>
        public static void PushToShaders()
        {
            Set("_PalSkyWhite", SkyWhite); Set("_PalSkyBlue", SkyBlue); Set("_PalHaze", Haze);
            Set("_PalWheat", Wheat); Set("_PalFieldGreen", FieldGreen); Set("_PalRoadGrey", RoadGrey);
            Set("_PalDustTan", DustTan); Set("_PalShadowBlue", ShadowBlue); Set("_PalShadowDeep", ShadowDeep);
            Set("_PalPlasmaWhite", PlasmaWhite); Set("_PalPlasmaGold", PlasmaGold); Set("_PalPlasmaOrange", PlasmaOrange);
            Set("_PalFireRed", FireRed); Set("_PalRockDark", RockDark); Set("_PalMolten", Molten);
            Set("_PalSoil", Soil); Set("_PalScorch", Scorch);
        }

        static void Set(string name, Color c) { Shader.SetGlobalColor(name, c.linear); }
    }
}
