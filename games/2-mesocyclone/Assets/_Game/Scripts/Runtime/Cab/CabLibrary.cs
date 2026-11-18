using System.Collections.Generic;
using UnityEngine;

namespace MESOCYCLONE
{
    /// <summary>Materials and textures of the truck cab. Surfaces with a grain (dash, pad, vinyl, cloth, carpet,
    /// liner, wheel) use photo sets (Poly Haven, CC0) at their physical size: UVs are metres, tiling = repeats per
    /// metre (a dashboard grain is a millimetre). Their albedo is normalised and tinted by the designed dark colours,
    /// so the cab keeps its value; the photos bring the grain, the wear and the roughness variation. A dust-and-wear
    /// detail map on UV1 breaks the repeat. Small parts use the swatch atlas with a grain or smudge detail on UV1.
    /// The hood is automotive paint: HDRP clear coat over a slightly rough base with orange peel and settled dust.
    /// The instrument marks are lit pale plastic by day and emissive at night (the emissive colour is animated by CabRig).</summary>
    public static class CabLibrary
    {
        // emissive levels (nits) at full backlight
        public const float GaugeNits = 4.5f, GreenNits = 5f, NeedleNits = 8f, RadioNits = 3.2f;
        public static readonly Color NeedleColor = Palette.Hex(0xff4a1e);

        // photo sets of the cab (1k: at the dash the screen shows about 2 mm per pixel; 1k over 0.15-0.6 m is far finer)
        const string Grain = "leather_white", Vinyl = "fabric_leather_02", Wheel = "brown_leather", Cloth = "poly_wool_herringbone",
            Liner = "terlenka", Carpet = "dirty_carpet";

        public static void Textures(List<TexDef> l)
        {
            l.Add(Photo.Albedo(Grain, 1024, 0.15f, 1.2f)); l.Add(Photo.Mask(Grain, 1024)); l.Add(Photo.Detail(Grain, 1024, 0.8f, 1f));
            l.Add(Photo.Albedo(Vinyl, 1024, 0.3f, 1f)); l.Add(Photo.Mask(Vinyl, 1024));
            l.Add(Photo.Albedo(Wheel, 1024, 0.3f, 1f)); l.Add(Photo.Mask(Wheel, 1024));
            l.Add(Photo.Albedo(Cloth, 1024, 0.4f, 1.2f)); l.Add(Photo.Mask(Cloth, 1024));
            l.Add(Photo.Albedo(Liner, 1024, 0.2f, 1f)); l.Add(Photo.Mask(Liner, 1024));
            l.Add(Photo.Albedo(Carpet, 1024, 0.35f, 1f)); l.Add(Photo.Mask(Carpet, 1024));
        }

        /// <summary>A photo-textured cab surface: UV0 metres, 'tile' metres per repeat, smoothness range from the mask.</summary>
        static MatDef Photo3(string name, string set, Color color, float tile, Vector2 smooth, float normalScale = 1f, float dustTile = 0.8f, float dust = 1f)
        {
            return new MatDef(name)
            {
                color = color, normalizedAlbedo = true, baseMap = Photo.C(set), normalMap = Photo.N(set), maskMap = Photo.M(set),
                smoothRemap = smooth, normalScale = normalScale, tiling = new Vector2(1f / tile, 1f / tile),
                detailMap = CoreLibrary.DustD, detailUV = 1, detailTiling = new Vector2(1f / dustTile, 1f / dustTile),
                detailAlbedo = dust, detailNormal = 0f, detailSmooth = dust
            };
        }

        /// <summary>A swatch-coloured cab part with a detail map on UV1.</summary>
        static MatDef Swatch(string name, float smooth, float metallic, string detail, float tile, float da, float dn, float dsm)
        {
            return new MatDef(name)
            {
                color = Color.white, baseMap = "swatch", smoothness = smooth, metallic = metallic, detailMap = detail, detailUV = 1,
                detailTiling = new Vector2(1f / tile, 1f / tile), detailAlbedo = da, detailNormal = dn, detailSmooth = dsm
            };
        }

        public static void Materials(List<MatDef> l)
        {
            // textured surfaces: UVs are metres
            l.Add(Photo3("cab_dash", Grain, Palette.Hex(0x2b2a26), 0.15f, new Vector2(0.16f, 0.42f), 0.55f, 0.9f));
            l.Add(Photo3("cab_pad", Grain, Palette.Hex(0x3a3833), 0.22f, new Vector2(0.1f, 0.3f), 0.8f, 0.9f));
            l.Add(Photo3("cab_leather", Vinyl, Palette.Hex(0x1f1c19), 0.5f, new Vector2(0.22f, 0.58f), 1f, 0.8f));
            l.Add(Photo3("cab_wheel", Wheel, Palette.Hex(0x1f1c19), 0.4f, new Vector2(0.3f, 0.62f), 1f, 0.5f));      // worn shiny where it is held
            l.Add(Photo3("cab_fabric", Cloth, Palette.Hex(0x3d3933), 0.27f, new Vector2(0.02f, 0.14f), 1.2f, 0.8f, 0.6f));
            l.Add(Photo3("cab_liner", Liner, Palette.Hex(0x5c584f), 0.265f, new Vector2(0.02f, 0.1f), 1f, 1.1f, 0.6f));
            l.Add(Photo3("cab_carpet", Carpet, Palette.Hex(0x1f1e19), 0.6f, new Vector2(0f, 0.08f), 1.2f, 0.9f, 0.8f));
            // swatch-atlas materials for small parts
            l.Add(Swatch("cab_trim_gloss", 0.78f, 0f, CoreLibrary.SmudgeD, 0.35f, 1f, 0f, 1f));
            l.Add(Swatch("cab_chrome", 0.9f, 1f, CoreLibrary.SmudgeD, 0.3f, 1f, 0f, 1f));
            l.Add(Swatch("cab_plastic", 0.3f, 0f, Photo.D(Grain), 0.15f, 0.5f, 0.6f, 0.8f));
            // automotive paint: clear coat, orange peel and dust in the base (detail on UV1 metres)
            MatDef paint = MatDef.Lit("cab_paint", Palette.TruckPaint, 0.5f, 0.12f);
            paint.coatMask = 1f; paint.detailMap = CoreLibrary.PaintD; paint.detailUV = 1; paint.detailTiling = new Vector2(1f / 0.35f, 1f / 0.35f);
            l.Add(paint);
            MatDef rubber = MatDef.Lit("cab_rubber", Palette.Hex(0x0b0b0b), 0.28f);
            rubber.detailMap = CoreLibrary.RubberD; rubber.detailUV = 1; rubber.detailTiling = new Vector2(1f / 0.08f, 1f / 0.08f);
            l.Add(rubber);
            l.Add(MatDef.Lit("cab_gauge_face", Palette.Hex(0x050505), 0.35f));
            l.Add(MatDef.Lit("cab_mirror_base", Palette.Hex(0x020202), 0.95f));
            // instrument marks: lit by day, glowing at night (CabRig animates _EmissiveColor on its own instances)
            l.Add(new MatDef("cab_gauge_glow") { color = Palette.Hex(0xcfc8b4), smoothness = 0.2f, emissive = Palette.DashAmber, emissiveNits = GaugeNits });
            l.Add(new MatDef("cab_tell_green") { color = Palette.Hex(0x0c1c10), smoothness = 0.5f, emissive = Palette.DashGreen, emissiveNits = GreenNits });
            l.Add(new MatDef("cab_needle") { color = Palette.Hex(0xc8421c), smoothness = 0.4f, emissive = NeedleColor, emissiveNits = NeedleNits });
            l.Add(new MatDef("cab_radio_glow") { color = Palette.Hex(0xb8b09a), smoothness = 0.2f, emissive = Palette.DashAmber, emissiveNits = RadioNits });
        }
    }
}
