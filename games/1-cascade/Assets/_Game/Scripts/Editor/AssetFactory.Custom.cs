using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace CASCADE.EditorTools
{
    /// <summary>Materials of the hand-written shaders, and a compile check of every pass (no GPU needed: the shader compiler is a separate process).</summary>
    public static partial class AssetFactory
    {
        static partial void BuildCustomMaterials()
        {
            AssetDatabase.Refresh();
            Custom("M_Plume", "Hidden/Cascade/Plume");
            Custom("M_Particles", "Hidden/Cascade/Particles");
            var blanket = Custom("M_Blanket", "Cascade/Blanket");
            if (blanket != null) blanket.renderQueue = 2015;
            var water = Custom("M_Water", "Cascade/Water");
            if (water != null) water.renderQueue = 2020;
            var cone = Custom("M_Cone", "Cascade/Cone");
            if (cone != null) cone.renderQueue = 2010;
            Lit("M_IsoSea", Palette.DeepWater.linear * 0.6f, 0.82f);
            Lit("M_IsoRock", Palette.CliffDark.linear * 1.2f, 0.15f);

            // the volcano's custom shader samples three photo rock sets (ash scree, lava rock, red scoria)
            if (cone != null)
            {
                cone.SetTexture("_RockA", Photo("rocks_ground_02", "diff")); cone.SetTexture("_RockAN", Photo("rocks_ground_02", "nor_gl"));
                cone.SetTexture("_RockB", Photo("dark_rock_02", "diff")); cone.SetTexture("_RockBN", Photo("dark_rock_02", "nor_gl"));
                cone.SetTexture("_RockC", Photo("rocky_gravel", "diff")); cone.SetTexture("_RockCN", Photo("rocky_gravel", "nor_gl"));
                EditorUtility.SetDirty(cone);
            }
            GenTex("T_OceanSlopes");
            GenTex("T_Foam");

            // terrain: four photo layers blended by vertex colour and texture height (strata on the caldera cliffs).
            // The pale tuff is tiled at 63 m (it was 37 m: a diamond lattice repeated ~20 times across the headland) with a
            // softer normal; the dark rock at 13 m. The 2D mask breaks the beds up along the shore anyway.
            Layered("M_Land", new[]
            {
                new Layer("dark_rock", Palette.CliffDark.linear * 1.75f, 0.35f, 13f, 1.25f, 0.02f, 0.38f, 0.2f),
                new Layer("cliff_side", Color.Lerp(Palette.CliffRed.linear, Palette.CliffDark.linear * 1.7f, 0.45f) * 1.35f, 0.35f, 16f, 1.2f, 0.02f, 0.3f, 0.2f),
                new Layer("rock_face_03", new Color(0.72f, 0.68f, 0.60f) * 0.8f, 0.6f, 63f, 0.8f, 0.02f, 0.3f, 0.2f),
                new Layer("dry_ground_rocks", Palette.DryGrass.linear * 0.66f, 0.45f, 6f, 1.0f, 0.0f, 0.22f, 0.25f),
            }, 0.42f);
            var land = AssetDatabase.LoadAssetAtPath<Material>(Gen + "/Materials/M_Land.mat");
            if (land != null)
            {
                land.SetTexture("_LayerMaskMap", GenTex("T_Strata"));
                land.SetFloat("_UVBlendMask", 0f);                      // UV0: u along the shore (~1.2 km per repeat), v depth below the rim crest (the layers are triplanar)
                HDMaterial.ValidateMaterial(land);
                EditorUtility.SetDirty(land);
            }

            // far-rim villages (FarVillages): untextured, they are a few pixels across at 1.5-9 km.
            // A far window is the average of a recess, glass and shutters: mid-dark and rough. By day a speckle that makes
            // the boxes read as houses without crawling under TAA, and no sun glints (a smooth sub-pixel quad would sparkle).
            // The night lights are halo particles, not emission.
            Lit("M_WindowFar", new Color(0.09f, 0.10f, 0.12f), 0.3f);
            // the Oia palette mixed into the whites, a little darker than the targets so they sit in the whitewash value range
            Lit("M_FarOchre", new Color(0.62f, 0.36f, 0.12f) * 0.85f, 0.25f);
            Lit("M_FarRose", new Color(0.72f, 0.42f, 0.34f) * 0.85f, 0.25f);
            Lit("M_FarSand", new Color(0.78f, 0.64f, 0.40f) * 0.85f, 0.25f);
            Layered("M_SeaFloor", new[]
            {
                new Layer("brown_mud_02", Palette.Silt.linear * 0.94f, 0.5f, 5f, 1.0f, 0.45f, 0.92f, 0.3f),
                new Layer("low_tide_rocks", Palette.Weed.linear * 0.9f, 0.4f, 4f, 1.2f, 0.25f, 0.7f, 0.2f),
                new Layer("damp_beach_sand", Palette.Silt.linear * 1.26f, 0.4f, 4f, 0.9f, 0.4f, 0.92f, 0.3f),
                new Layer("dark_rock_02", Palette.CliffDark.linear * 1.3f, 0.4f, 7f, 1.2f, 0.3f, 0.75f, 0.2f),
            }, 0.25f);
        }

        /// <summary>Compiles every pass of every custom material synchronously and logs compiler messages.</summary>
        public static int CheckShaders()
        {
            int errors = 0;
            foreach (var m in mats)
            {
                if (m == null || m.shader == null) continue;
                string n = m.shader.name;
                if (!n.Contains("Cascade")) continue;
                for (int p = 0; p < m.passCount; p++)
                {
                    try { ShaderUtil.CompilePass(m, p, true); }
                    catch (System.Exception e) { Debug.Log("CASCADE_INFO CompilePass " + n + " pass " + p + ": " + e.Message); }
                }
                foreach (var msg in ShaderUtil.GetShaderMessages(m.shader))
                {
                    bool err = msg.severity == ShaderCompilerMessageSeverity.Error;
                    if (err) errors++;
                    Debug.Log((err ? "CASCADE_SHADER_ERROR " : "CASCADE_SHADER_WARN ") + n + ": " + msg.message + " @ " + msg.file + ":" + msg.line);
                }
            }
            Debug.Log(errors == 0 ? "CASCADE_INFO shaders compile clean" : "CASCADE_FAIL shader errors: " + errors);
            return errors;
        }
    }
}
