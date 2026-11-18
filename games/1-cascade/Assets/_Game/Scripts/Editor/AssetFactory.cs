using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.UIElements;

namespace CASCADE.EditorTools
{
    /// <summary>
    /// Generates every asset the game needs (materials, textures, UI settings) and the GameAssets registry.
    /// Material assets are what keeps the shader variants in the player build.
    /// </summary>
    public static partial class AssetFactory
    {
        public const string Gen = "Assets/_Game/Generated";
        static readonly List<Material> mats = new List<Material>();
        static readonly List<Texture> texs = new List<Texture>();
        static readonly List<Shader> shaders = new List<Shader>();

        public static GameAssets BuildAll()
        {
            mats.Clear(); texs.Clear(); shaders.Clear(); ashKeys.Clear(); ashTints.Clear();
            Directory.CreateDirectory(Gen + "/Materials");
            Directory.CreateDirectory(Gen + "/Textures");
            Directory.CreateDirectory(Gen + "/UI");
            AssetDatabase.Refresh();

            BuildTextures();
            BuildLitMaterials();
            BuildCustomMaterials();
            BuildFoliageMaterials();

            var ga = AssetDatabase.LoadAssetAtPath<GameAssets>(Gen + "/GameAssets.asset");
            if (ga == null)
            {
                ga = ScriptableObject.CreateInstance<GameAssets>();
                AssetDatabase.CreateAsset(ga, Gen + "/GameAssets.asset");
            }
            ga.materials = mats.ToArray();
            ga.textures = texs.ToArray();
            ga.shaders = shaders.ToArray();
            ga.panel = BuildPanel();
            ga.ashKeys = ashKeys.ToArray();
            ga.ashTints = ashTints.ToArray();
            EditorUtility.SetDirty(ga);
            AssetDatabase.SaveAssets();
            Debug.Log("CASCADE_INFO assets: " + mats.Count + " materials, " + texs.Count + " textures, " + shaders.Count + " shaders");
            return ga;
        }

        static PanelSettings BuildPanel()
        {
            string tss = Gen + "/UI/Runtime.tss";
            if (!File.Exists(tss))
            {
                File.WriteAllText(tss, "@import url(\"unity-theme://default\");\n");
                AssetDatabase.ImportAsset(tss, ImportAssetOptions.ForceSynchronousImport);
            }
            string path = Gen + "/UI/Panel.asset";
            var ps = AssetDatabase.LoadAssetAtPath<PanelSettings>(path);
            if (ps == null)
            {
                ps = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(ps, path);
            }
            ps.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(tss);
            ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            ps.referenceResolution = new Vector2Int(1920, 1080);
            ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            ps.match = 1f;
            ps.sortingOrder = 100;
            EditorUtility.SetDirty(ps);
            return ps;
        }

        // ---------- helpers ----------
        public static Material Lit(string name, Color baseColor, float smoothness, float metallic = 0f)
        {
            string path = Gen + "/Materials/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            var sh = Shader.Find("HDRP/Lit");
            if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
            else if (m.shader != sh) m.shader = sh;
            m.SetColor("_BaseColor", baseColor.gamma);      // callers pass linear albedo; the property is an sRGB colour
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", metallic);
            m.enableInstancing = true;
            HDMaterial.ValidateMaterial(m);
            EditorUtility.SetDirty(m);
            mats.Add(m);
            return m;
        }

        public static void Emissive(Material m, Color nits)
        {
            m.SetColor("_EmissiveColor", nits);
            m.SetColor("_EmissiveColorLDR", Color.white);
            m.SetFloat("_UseEmissiveIntensity", 0f);
            m.SetFloat("_EmissiveExposureWeight", 1f);
            m.SetFloat("_AlbedoAffectEmissive", 0f);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            HDMaterial.ValidateMaterial(m);
            EditorUtility.SetDirty(m);
        }

        public static void Triplanar(Material m, Texture2D albedo, Texture2D normal, float worldScale, float normalScale = 1f)
        {
            m.SetFloat("_UVBase", 5f);
            m.SetFloat("_TexWorldScale", worldScale);
            if (albedo != null) m.SetTexture("_BaseColorMap", albedo);
            if (normal != null) { m.SetTexture("_NormalMap", normal); m.SetFloat("_NormalScale", normalScale); }
            HDMaterial.ValidateMaterial(m);
            EditorUtility.SetDirty(m);
        }

        public static Material Custom(string name, string shaderName)
        {
            var sh = Shader.Find(shaderName);
            if (sh == null) { Debug.LogError("CASCADE_FAIL shader not found: " + shaderName); return null; }
            if (!shaders.Contains(sh)) shaders.Add(sh);
            string path = Gen + "/Materials/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
            else if (m.shader != sh) m.shader = sh;
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            mats.Add(m);
            return m;
        }

        public static void AddTexture(Texture t) { if (t != null && !texs.Contains(t)) texs.Add(t); }

        // ---------- content ----------
        static void BuildLitMaterials()
        {
            // ---- the material vocabulary of the world (names are a contract with the builders) ----
            Color ww = Palette.Whitewash.linear;
            // photo-textured surfaces (CC0, Poly Haven; Tools/fetch_textures.py), world-space triplanar, tinted to the palette
            PbrLit("M_Whitewash", new Layer("painted_plaster_wall", ww * 0.78f, 0.55f, 2.6f, 0.9f, 0.04f, 0.32f));
            PbrLit("M_WhitewashWarm", new Layer("painted_plaster_wall", new Color(ww.r * 0.9f, ww.g * 0.85f, ww.b * 0.74f), 0.8f, 2.6f, 0.9f, 0.04f, 0.32f));
            PbrLit("M_WhitewashOld", new Layer("white_plaster_02", ww * 0.70f, 0.45f, 3.2f, 1.0f, 0.02f, 0.28f));
            PbrLit("M_WhitewashWet", new Layer("white_plaster_02", ww * 0.36f, 0.5f, 3.2f, 1.0f, 0.45f, 0.82f));
            PbrLit("M_PlasterTrim", new Layer("painted_plaster_wall", new Color(0.80f, 0.76f, 0.68f), 0.8f, 1.8f, 0.7f, 0.08f, 0.40f));
            // domes: a deep enamel blue (sRGB #1E5AA8), glossy enough to carry a sky highlight, the plaster grain softened by the paint
            PbrLit("M_DomeBlue", new Layer("painted_plaster_wall", new Color(0x1E / 255f, 0x5A / 255f, 0xA8 / 255f).linear, 1f, 2.2f, 0.55f, 0.35f, 0.70f));
            // the Oia palette: pigmented lime-wash on the whitewash's plaster (HouseGen colours a share of the houses by zone).
            // Linear targets scaled by PaintValue so the colours sit in the whitewash's value range; ash coat via PbrLit's AddAsh.
            const float PaintValue = 0.85f, PaintHue = 0.92f;
            PbrLit("M_WallOchre", new Layer("painted_plaster_wall", new Color(0.62f, 0.36f, 0.12f) * PaintValue, PaintHue, 2.6f, 0.9f, 0.04f, 0.32f));
            PbrLit("M_WallSand", new Layer("painted_plaster_wall", new Color(0.78f, 0.64f, 0.40f) * PaintValue, PaintHue, 2.6f, 0.9f, 0.04f, 0.32f));
            PbrLit("M_WallRose", new Layer("painted_plaster_wall", new Color(0.72f, 0.42f, 0.34f) * PaintValue, PaintHue, 2.6f, 0.9f, 0.04f, 0.32f));
            PbrLit("M_WallTerracotta", new Layer("painted_plaster_wall", new Color(0.45f, 0.13f, 0.06f) * PaintValue, PaintHue, 2.6f, 0.9f, 0.04f, 0.32f));
            PbrLit("M_WallSky", new Layer("painted_plaster_wall", new Color(0.50f, 0.62f, 0.70f) * PaintValue, PaintHue, 2.6f, 0.9f, 0.04f, 0.32f));
            PbrLit("M_Plinth", new Layer("white_plaster_02", new Color(0.32f, 0.31f, 0.30f), 0.8f, 3.2f, 1.0f, 0.10f, 0.35f, 0.28f));   // painted grey base band
            PbrLit("M_Ruin", new Layer("old_stone_wall", new Color(0.20f, 0.17f, 0.15f), 0.6f, 3f, 1.2f, 0.05f, 0.35f));
            PbrLit("M_Mud", new Layer("brown_mud_02", Palette.Silt.linear * 0.8f, 0.6f, 4f, 1.0f, 0.35f, 0.75f));
            PbrLit("M_DoorBlue", new Layer("wood_planks_grey", Palette.DoorBlue.linear, 1f, 1.6f, 0.8f, 0.15f, 0.5f));
            PbrLit("M_ShutterGreen", new Layer("wood_planks_grey", new Color(0.05f, 0.20f, 0.11f), 1f, 1.6f, 0.8f, 0.15f, 0.5f));
            PbrLit("M_ShutterRed", new Layer("wood_planks_grey", new Color(0.28f, 0.045f, 0.035f), 1f, 1.6f, 0.8f, 0.15f, 0.5f));
            PbrLit("M_Stone", new Layer("grey_stone_path", new Color(0.30f, 0.28f, 0.27f), 0.5f, 3.4f, 1.1f, 0.05f, 0.40f));
            PbrLit("M_StoneWet", new Layer("grey_stone_path", new Color(0.12f, 0.115f, 0.11f), 0.5f, 3.4f, 1.1f, 0.55f, 0.9f));
            PbrLit("M_Paving", new Layer("grey_stone_path", new Color(0.52f, 0.49f, 0.44f), 0.45f, 2.6f, 1.0f, 0.05f, 0.42f));
            PbrLit("M_Gravel", new Layer("gravel_floor_02", new Color(0.40f, 0.34f, 0.27f), 0.6f, 1.6f, 1.0f, 0.0f, 0.25f));
            PbrLit("M_RockWall", new Layer("old_stone_wall", Palette.CliffDark.linear * 1.6f, 0.55f, 2.8f, 1.3f, 0.02f, 0.3f));
            PbrLit("M_Pumice", new Layer("old_stone_wall", new Color(0.30f, 0.28f, 0.27f), 0.8f, 2.2f, 0.9f, 0.04f, 0.32f, 0.35f));
            PbrLit("M_Wood", new Layer("wood_planks_grey", Palette.Wood.linear, 0.8f, 1.8f, 0.8f, 0.1f, 0.4f));
            PbrLit("M_WoodDark", new Layer("wood_planks_grey", Palette.Wood.linear * 0.45f, 0.8f, 1.8f, 0.8f, 0.1f, 0.45f));
            Lit("M_Iron", Palette.Iron.linear, 0.42f, 0.85f);                        // railings, lamp posts
            Lit("M_Rope", new Color(0.42f, 0.34f, 0.22f), 0.1f);
            Lit("M_Net", new Color(0.10f, 0.13f, 0.12f), 0.1f);
            Lit("M_Canvas", new Color(0.78f, 0.72f, 0.58f), 0.1f);                   // awnings, sails, table cloths
            Lit("M_Terracotta", new Color(0.52f, 0.24f, 0.13f), 0.2f);               // pots
            Lit("M_Bronze", new Color(0.45f, 0.30f, 0.12f), 0.55f, 0.9f);            // the bell
            // boats move: their painted planks use the mesh UVs (metres), not world-space projection
            PbrLit("M_BoatRed", new Layer("wood_planks_grey", Palette.BoatRed.linear, 1f, 1.4f, 0.7f, 0.25f, 0.55f), false);
            PbrLit("M_BoatYellow", new Layer("wood_planks_grey", Palette.BoatYellow.linear, 1f, 1.4f, 0.7f, 0.25f, 0.55f), false);
            PbrLit("M_BoatWhite", new Layer("wood_planks_grey", ww * 0.80f, 0.9f, 1.4f, 0.7f, 0.25f, 0.55f), false);
            PbrLit("M_BoatBlue", new Layer("wood_planks_grey", Palette.DoorBlue.linear * 0.8f, 1f, 1.4f, 0.7f, 0.25f, 0.55f), false);
            // foliage: a fine, speckled photo texture tinted leaf-green and bract-magenta gives the lobes a leafy grain
            PbrLit("M_Plant", new Layer("low_tide_rocks", new Color(0.10f, 0.19f, 0.05f), 1f, 0.35f, 1.6f, 0.05f, 0.35f, 0.3f));
            PbrLit("M_Bougainvillea", new Layer("low_tide_rocks", new Color(0.45f, 0.05f, 0.20f), 1f, 0.3f, 1.6f, 0.05f, 0.3f, 0.3f));
            // windows: three groups that switch on at different moments, one that never lights, and the keeper's window
            Color glass = new Color(0.035f, 0.045f, 0.06f);
            foreach (string n in new[] { "M_WindowA", "M_WindowB", "M_WindowC", "M_WindowDark", "M_WindowKeeper" })
                Emissive(Lit(n, glass, 0.88f), Color.black);
            KeeperGlow(AssetDatabase.LoadAssetAtPath<Material>(Gen + "/Materials/M_WindowKeeper.mat"));   // the lit room's hot spot
            foreach (string n in new[] { "M_LampA", "M_LampB" })
                Emissive(Lit(n, new Color(0.75f, 0.72f, 0.62f), 0.7f), Color.black);
            Emissive(Lit("M_BeaconGlass", new Color(0.7f, 0.7f, 0.65f), 0.9f), Color.black);   // the lighthouse lantern
        }

        static partial void BuildTextures();
        static partial void BuildCustomMaterials();
        static partial void BuildFoliageMaterials();
    }
}
