using System.IO;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace ZERO.EditorTools
{
    /// <summary>Generates every asset the game needs (materials, UI panel, volume profile, GameAssets) and the
    /// scene Assets/_Game/Scenes/Main.unity. Everything else is built at load by the runtime.</summary>
    public static class SceneBuilder
    {
        public const string GenDir = "Assets/_Game/Generated";
        public const string MatDir = GenDir + "/Materials";
        public const string ScenePath = "Assets/_Game/Scenes/Main.unity";

        static Material Mat(string name, string shader, System.Action<Material> setup = null)
        {
            string path = MatDir + "/" + name + ".mat";
            var sh = Shader.Find(shader);
            if (sh == null) { Debug.LogError("SceneBuilder: shader not found: " + shader); sh = Shader.Find("Zero/Cabin"); }
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
            else if (m.shader != sh) m.shader = sh;
            m.enableInstancing = false;
            if (setup != null) setup(m);
            EditorUtility.SetDirty(m);
            return m;
        }

        public static void BuildMaterials(GameAssets ga)
        {
            Directory.CreateDirectory(MatDir);
            ga.Sky = Mat("Sky", "Zero/Sky");
            ga.Earth = Mat("Earth", "Zero/Earth");
            ga.Cloud = Mat("Cloud", "Zero/Cloud");
            ga.Cabin = Mat("Cabin", "Zero/Cabin", m => m.SetFloat("_Interior", 1f));
            ga.Glass = Mat("Glass", "Zero/Glass");
            ga.Asteroid = Mat("Asteroid", "Zero/Asteroid", m => { m.SetFloat("_RevealFar", 9e8f); m.SetFloat("_RevealNear", 8e8f); m.renderQueue = -1; });
            ga.Plasma = Mat("Plasma", "Zero/Plasma");
            ga.Trail = Mat("Trail", "Zero/Trail");
            ga.Fireball = Mat("Fireball", "Zero/Fireball");
            ga.Dome = Mat("Dome", "Zero/Dome");
            ga.Smoke = Mat("Smoke", "Zero/Smoke");
            ga.Streak = Mat("Streak", "Zero/Streak");
            ga.Ribbon = Mat("Ribbon", "Zero/Ribbon");
            ga.Glow = Mat("Glow", "Zero/Glow");
            ga.Fullscreen = Mat("Fullscreen", "Hidden/Zero/Fullscreen");
            ga.Beam = Mat("Beam", "Zero/Beam");
        }

        static PanelSettings BuildPanel()
        {
            Directory.CreateDirectory(GenDir + "/UI");
            string tss = GenDir + "/UI/ZeroTheme.tss";
            if (!File.Exists(tss)) { File.WriteAllText(tss, "@import url(\"unity-theme://default\");\nVisualElement {}\n"); AssetDatabase.ImportAsset(tss, ImportAssetOptions.ForceSynchronousImport); }
            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(tss);
            string path = GenDir + "/UI/Panel.asset";
            var ps = AssetDatabase.LoadAssetAtPath<PanelSettings>(path);
            if (ps == null) { ps = ScriptableObject.CreateInstance<PanelSettings>(); AssetDatabase.CreateAsset(ps, path); }
            ps.themeStyleSheet = theme;
            ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            ps.referenceResolution = new Vector2Int(1920, 1080);
            ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            ps.match = 1f;
            ps.sortingOrder = 100;
            EditorUtility.SetDirty(ps);
            if (theme == null) Debug.LogError("SceneBuilder: theme style sheet failed to import");
            return ps;
        }

        static VolumeProfile BuildVolume()
        {
            string path = GenDir + "/ZeroVolume.asset";
            if (AssetDatabase.LoadAssetAtPath<VolumeProfile>(path) != null) AssetDatabase.DeleteAsset(path);
            var p = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(p, path);
            var tm = VolumeProfileFactory.CreateVolumeComponent<Tonemapping>(p, true, false);
            tm.mode.Override(TonemappingMode.Neutral);
            var bl = VolumeProfileFactory.CreateVolumeComponent<Bloom>(p, true, false);
            bl.threshold.Override(1.1f); bl.intensity.Override(0.55f); bl.scatter.Override(0.8f);
            bl.highQualityFiltering.Override(true); bl.tint.Override(new Color(1f, 0.95f, 0.88f));
            var ca = VolumeProfileFactory.CreateVolumeComponent<ColorAdjustments>(p, true, false);
            ca.postExposure.Override(0f); ca.contrast.Override(10f); ca.saturation.Override(6f);
            var lgg = VolumeProfileFactory.CreateVolumeComponent<LiftGammaGain>(p, true, false);
            lgg.lift.Override(new Vector4(0.98f, 1.0f, 1.04f, -0.01f));      // firm blacks, a little cool
            lgg.gamma.Override(new Vector4(1f, 1f, 1f, 0f));
            lgg.gain.Override(new Vector4(1.02f, 1f, 0.97f, 0f));            // warm highlights
            var st = VolumeProfileFactory.CreateVolumeComponent<SplitToning>(p, true, false);
            st.shadows.Override(new Color(0.42f, 0.49f, 0.56f)); st.highlights.Override(new Color(0.56f, 0.51f, 0.45f)); st.balance.Override(10f);
            var vg = VolumeProfileFactory.CreateVolumeComponent<Vignette>(p, true, false);
            vg.intensity.Override(0.2f); vg.smoothness.Override(0.45f); vg.color.Override(new Color(0.02f, 0.02f, 0.03f));
            // a film camera: lens depth of field (driven every frame by CameraFX), grain, a trace of fringing
            var df = VolumeProfileFactory.CreateVolumeComponent<DepthOfField>(p, true, false);
            df.mode.Override(DepthOfFieldMode.Bokeh); df.focusDistance.Override(5000f); df.focalLength.Override(20f); df.aperture.Override(2.2f);
            df.bladeCount.Override(7); df.bladeCurvature.Override(0.85f);
            var fg = VolumeProfileFactory.CreateVolumeComponent<FilmGrain>(p, true, false);
            fg.type.Override(FilmGrainLookup.Thin1); fg.intensity.Override(0f); fg.response.Override(0.8f);
            var cab = VolumeProfileFactory.CreateVolumeComponent<ChromaticAberration>(p, true, false);
            cab.intensity.Override(0.05f);
            EditorUtility.SetDirty(p);
            return p;
        }

        public static void Run()
        {
            Directory.CreateDirectory(GenDir);
            Directory.CreateDirectory("Assets/_Game/Scenes");
            AssetDatabase.Refresh();
            string gaPath = GenDir + "/GameAssets.asset";
            var ga = AssetDatabase.LoadAssetAtPath<GameAssets>(gaPath);
            if (ga == null) { ga = ScriptableObject.CreateInstance<GameAssets>(); AssetDatabase.CreateAsset(ga, gaPath); }
            BuildMaterials(ga);
            ga.Panel = BuildPanel();
            ga.Volume = BuildVolume();
            EditorUtility.SetDirty(ga);
            AssetDatabase.SaveAssets();

            // ---- the scene ----
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox; cam.nearClipPlane = Config.NearPlane; cam.farClipPlane = Config.FarPlane;
            cam.fieldOfView = Config.BaseFov; cam.allowHDR = true; cam.allowMSAA = false;
            camGo.AddComponent<AudioListener>();
            var cd = camGo.AddComponent<UniversalAdditionalCameraData>();
            cd.renderPostProcessing = true; cd.dithering = true; cd.stopNaN = true;
            cd.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            cd.requiresColorOption = CameraOverrideOption.On; cd.requiresDepthOption = CameraOverrideOption.On;

            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional; sun.shadows = LightShadows.None; sun.intensity = 1f; sun.color = Color.white;
            sun.shadowStrength = 1f; sun.shadowBias = 0.05f; sun.shadowNormalBias = 0.4f;
            sunGo.transform.rotation = Quaternion.Euler(36f, 165f, 0f);
            sunGo.AddComponent<UniversalAdditionalLightData>();

            var volGo = new GameObject("Global Volume");
            var vol = volGo.AddComponent<Volume>(); vol.isGlobal = true; vol.priority = 1; vol.sharedProfile = ga.Volume;

            var game = new GameObject("Game");
            var boot = game.AddComponent<Bootstrap>();
            boot.Assets = ga; boot.Cam = cam; boot.Sun = sun;

            RenderSettings.skybox = ga.Sky; RenderSettings.fog = false;
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = Color.black;
            RenderSettings.sun = sun;
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

            // template leftovers
            foreach (var p in new[] { "Assets/Scenes/SampleScene.unity", "Assets/Settings/SampleSceneProfile.asset", "Assets/Scenes" })
                if (File.Exists(p) || Directory.Exists(p)) AssetDatabase.DeleteAsset(p);
            AssetDatabase.SaveAssets();
        }
    }
}
