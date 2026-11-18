using UnityEngine;

namespace CASCADE
{
    /// <summary>Colour tokens (sRGB). One source for code and shaders: PushToShaders() publishes linear globals named _Pal*.</summary>
    public static class Palette
    {
        public static readonly Color DaySky = Hex(0x7fb2e5);
        public static readonly Color Whitewash = Hex(0xefe9dc);
        public static readonly Color DoorBlue = Hex(0x2f6fb3);
        public static readonly Color CalderaTeal = Hex(0x1f7f8f);
        public static readonly Color CliffDark = Hex(0x3a2f2a);
        public static readonly Color AshGrey = Hex(0x6b6560);
        public static readonly Color AshBrown = Hex(0x5a4636);
        public static readonly Color SunRed = Hex(0xc8421f);
        public static readonly Color Ember = Hex(0xff6a1f);
        public static readonly Color Lava = Hex(0xff8a2a);
        public static readonly Color Steam = Hex(0xd9d6d0);
        public static readonly Color WindowAmber = Hex(0xffbf6b);

        // Derived working colours (kept here so that nothing else invents colours).
        public static readonly Color DeepWater = Hex(0x0d3b4a);
        public static readonly Color Silt = Hex(0x4a4238);
        public static readonly Color Weed = Hex(0x3d4a2a);
        public static readonly Color CliffOchre = Hex(0x7a6048);
        public static readonly Color CliffRed = Hex(0x6a3a2c);
        public static readonly Color DryGrass = Hex(0x9a8a5a);
        public static readonly Color Foam = Hex(0xf2f4f0);
        public static readonly Color BoatRed = Hex(0xb5412f);
        public static readonly Color BoatYellow = Hex(0xd9a441);
        public static readonly Color Wood = Hex(0x6e5238);
        public static readonly Color Iron = Hex(0x2c2c2e);
        public static readonly Color Charcoal = Hex(0x1c1a19);

        public static Color Hex(int rgb)
        {
            return new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1f);
        }

        public static void PushToShaders()
        {
            Shader.SetGlobalVector("_PalDaySky", DaySky.linear);
            Shader.SetGlobalVector("_PalWhitewash", Whitewash.linear);
            Shader.SetGlobalVector("_PalCalderaTeal", CalderaTeal.linear);
            Shader.SetGlobalVector("_PalDeepWater", DeepWater.linear);
            Shader.SetGlobalVector("_PalCliffDark", CliffDark.linear);
            Shader.SetGlobalVector("_PalCliffOchre", CliffOchre.linear);
            Shader.SetGlobalVector("_PalCliffRed", CliffRed.linear);
            Shader.SetGlobalVector("_PalAshGrey", AshGrey.linear);
            Shader.SetGlobalVector("_PalAshBrown", AshBrown.linear);
            Shader.SetGlobalVector("_PalSunRed", SunRed.linear);
            Shader.SetGlobalVector("_PalEmber", Ember.linear);
            Shader.SetGlobalVector("_PalLava", Lava.linear);
            Shader.SetGlobalVector("_PalSteam", Steam.linear);
            Shader.SetGlobalVector("_PalWindowAmber", WindowAmber.linear);
            Shader.SetGlobalVector("_PalSilt", Silt.linear);
            Shader.SetGlobalVector("_PalWeed", Weed.linear);
            Shader.SetGlobalVector("_PalFoam", Foam.linear);
        }
    }
}
