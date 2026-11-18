using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace CASCADE.EditorTools
{
    public static partial class AssetFactory
    {
        /// <summary>
        /// Leaf-card foliage and cloth materials (flags, laundry). The cards are alpha-tested and double sided with normal
        /// mode None, so the back faces keep the bent canopy normals the builders give them (no transmission: HDRP has
        /// subsurface scattering off). Names are a contract with VKit, ScrubGen and ClothDirector.
        /// </summary>
        static partial void BuildFoliageMaterials()
        {
            Texture2D atlas = folLeaf != null ? folLeaf : AssetDatabase.LoadAssetAtPath<Texture2D>(Gen + "/Textures/T_LeafAtlas.asset");
            Texture2D flag = folFlag != null ? folFlag : AssetDatabase.LoadAssetAtPath<Texture2D>(Gen + "/Textures/T_FlagGR.asset");
            Texture2D laundry = folLaundry != null ? folLaundry : AssetDatabase.LoadAssetAtPath<Texture2D>(Gen + "/Textures/T_Laundry.asset");

            // the atlas carries the final albedo; the tint stays white (a knob for the grade), bracts a little glossier than leaves
            FolCard(Lit("M_LeafCard", Color.white, 0.32f), atlas);
            FolCard(Lit("M_BougCard", Color.white, 0.38f), atlas);
            // the solid inside of a carded plant: dark enough to read as the shadowed depth between the cards, never as a lit
            // green egg or a magenta lump (the sprig cards are mostly air, so much of the core shows through them); the
            // bougainvillea heart is a deep wine with more of its green leaves in it, the bracts on the cards carry the colour
            PbrLit("M_PlantCore", new Layer("low_tide_rocks", new Color(0.10f, 0.19f, 0.05f) * 0.40f, 1f, 0.35f, 1.6f, 0.05f, 0.3f, 0.3f));
            PbrLit("M_BougCore", new Layer("low_tide_rocks", new Color(0.42f, 0.06f, 0.19f) * 0.28f, 1f, 0.3f, 1.6f, 0.05f, 0.25f, 0.3f));
            // prickly pear pads and agave blades (grey-green, waxy), tree bark (grey, fissured)
            PbrLit("M_Cactus", new Layer("low_tide_rocks", new Color(0.15f, 0.22f, 0.13f), 1f, 0.8f, 1.0f, 0.12f, 0.42f, 0.4f));
            PbrLit("M_Bark", new Layer("dark_rock_02", new Color(0.17f, 0.15f, 0.13f), 0.7f, 0.9f, 1.3f, 0.02f, 0.25f, 0.3f));
            // cloth: double sided, the back face flips its normal
            FolCloth(Lit("M_Flag", Color.white, 0.2f), flag);
            FolCloth(Lit("M_Laundry", Color.white, 0.12f), laundry);
        }

        static void FolCard(Material m, Texture2D atlas)
        {
            if (atlas != null) m.SetTexture("_BaseColorMap", atlas);
            m.SetFloat("_SurfaceType", 0f);
            m.SetFloat("_AlphaCutoffEnable", 1f);
            m.SetFloat("_AlphaCutoff", FolCutoff);
            m.SetFloat("_AlphaCutoffShadow", FolCutoff);
            m.SetFloat("_DoubleSidedEnable", 1f);
            m.SetFloat("_DoubleSidedNormalMode", 2f);          // None: both faces use the vertex (canopy) normal
            HDMaterial.ValidateMaterial(m);
            EditorUtility.SetDirty(m);
        }

        static void FolCloth(Material m, Texture2D tex)
        {
            if (tex != null) m.SetTexture("_BaseColorMap", tex);
            m.SetFloat("_DoubleSidedEnable", 1f);
            m.SetFloat("_DoubleSidedNormalMode", 0f);          // Flip: the back of the cloth is lit as its own side
            HDMaterial.ValidateMaterial(m);
            EditorUtility.SetDirty(m);
        }
    }
}
