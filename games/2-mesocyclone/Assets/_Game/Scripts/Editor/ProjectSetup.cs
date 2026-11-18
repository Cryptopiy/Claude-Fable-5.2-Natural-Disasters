using System.IO;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using RPS = UnityEngine.Rendering.HighDefinition.RenderPipelineSettings;

namespace MESOCYCLONE.EditorTools
{
    /// <summary>Activates and configures HDRP entirely by script (Part 10.0): two HDRP assets (cheap test profile and
    /// final profile) bound to two quality levels, player settings, removal of the URP template leftovers.</summary>
    public static class ProjectSetup
    {
        public const string SettingsDir = "Assets/_Game/Settings";
        public const string TestPath = SettingsDir + "/HDRP_Test.asset";
        public const string FinalPath = SettingsDir + "/HDRP_Final.asset";
        const string GsPath = "Assets/HDRPDefaultResources/HDRenderPipelineGlobalSettings.asset";

        public static void Run()
        {
            Directory.CreateDirectory(SettingsDir);
            AssetDatabase.Refresh();

            // ---- player ----
            PlayerSettings.productName = Config.ProductName;
            PlayerSettings.companyName = "Fable";
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Direct3D11 });
            PlayerSettings.runInBackground = true;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.resizableWindow = false;
            PlayerSettings.visibleInBackground = true;
            PlayerSettings.enableFrameTimingStats = true;
            PlayerSettings.SplashScreen.show = false;            // ignored if the licence forbids it
            PlayerSettings.SplashScreen.showUnityLogo = false;
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);

            // ---- HDRP global settings: verify, repair only if missing ----
            RenderPipelineGlobalSettings gs = GraphicsSettings.GetSettingsForRenderPipeline<HDRenderPipeline>();
            if (gs == null)
            {
                gs = AssetDatabase.LoadAssetAtPath<RenderPipelineGlobalSettings>(GsPath);
                if (gs == null)
                {
                    var t = typeof(HDRenderPipelineAsset).Assembly.GetType("UnityEngine.Rendering.HighDefinition.HDRenderPipelineGlobalSettings");
                    gs = RenderPipelineGlobalSettingsUtils.Create(t, GsPath);
                }
                EditorGraphicsSettings.SetRenderPipelineGlobalSettingsAsset<HDRenderPipeline>(gs);
            }

            // ---- two HDRP assets ----
            HDRenderPipelineAsset test = LoadOrCreate(TestPath);
            HDRenderPipelineAsset fin = LoadOrCreate(FinalPath);
            Configure(test, false);
            Configure(fin, true);
            EditorUtility.SetDirty(test); EditorUtility.SetDirty(fin);

            // ---- quality levels: 0 = Test, 1 = Final ----
            RenameQualityLevels();
            GraphicsSettings.defaultRenderPipeline = fin;
            int count = QualitySettings.count;
            int keep = QualitySettings.GetQualityLevel();
            for (int i = 0; i < count; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = i == 0 ? test : fin;
                QualitySettings.shadows = ShadowQuality.All;
                QualitySettings.vSyncCount = 1;
                QualitySettings.antiAliasing = 0;
                QualitySettings.enableLODCrossFade = true;
                QualitySettings.lodBias = 2f;
                QualitySettings.globalTextureMipmapLimit = 0;
                QualitySettings.anisotropicFiltering = AnisotropicFiltering.Enable;
            }
            QualitySettings.SetQualityLevel(Mathf.Min(1, count - 1), false);

            if (!GraphicsSettings.TryGetRenderPipelineSettings<HDRPDefaultVolumeProfileSettings>(out var dvp) || dvp.volumeProfile == null)
                Debug.LogError("[ProjectSetup] HDRP default volume profile missing");

            // ---- URP template leftovers ----
            foreach (string p in new[] { "Assets/Settings", "Assets/TutorialInfo", "Assets/Readme.asset", "Assets/Scenes/SampleScene.unity" })
                if (AssetDatabase.LoadMainAssetAtPath(p) != null || AssetDatabase.IsValidFolder(p)) AssetDatabase.DeleteAsset(p);
            if (File.Exists("ProjectSettings/URPProjectSettings.asset")) File.Delete("ProjectSettings/URPProjectSettings.asset");

            AssetDatabase.SaveAssets();
            Debug.Log("[ProjectSetup] HDRP active = " + (GraphicsSettings.defaultRenderPipeline is HDRenderPipelineAsset) +
                      ", quality levels = " + string.Join(",", QualitySettings.names));
        }

        static HDRenderPipelineAsset LoadOrCreate(string path)
        {
            var a = AssetDatabase.LoadAssetAtPath<HDRenderPipelineAsset>(path);
            if (a != null) return a;
            a = ScriptableObject.CreateInstance<HDRenderPipelineAsset>();
            AssetDatabase.CreateAsset(a, path);
            return a;
        }

        static void RenameQualityLevels()
        {
            Object[] objs = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset");
            if (objs == null || objs.Length == 0) return;
            var so = new SerializedObject(objs[0]);
            SerializedProperty arr = so.FindProperty("m_QualitySettings");
            if (arr == null || arr.arraySize < 2) return;
            arr.GetArrayElementAtIndex(0).FindPropertyRelative("name").stringValue = "Test";
            arr.GetArrayElementAtIndex(1).FindPropertyRelative("name").stringValue = "Final";
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void Configure(HDRenderPipelineAsset a, bool final)
        {
            RPS s = a.currentPlatformRenderPipelineSettings;

            s.supportedLitShaderMode = RPS.SupportedLitShaderMode.DeferredOnly;
            s.colorBufferFormat = RPS.ColorBufferFormat.R11G11B10;
            s.customBufferFormat = RPS.CustomBufferFormat.R16G16B16A16;
            s.supportCustomPass = true;
            s.supportVolumetrics = true;
            s.supportVolumetricClouds = true;
            s.supportMotionVectors = true;
            s.supportDataDrivenLensFlare = true;
            s.supportScreenSpaceLensFlare = true;

            s.supportSSR = true; s.supportSSRTransparent = false; s.supportSSGI = false;
            s.supportSSAO = true;
            s.supportSubsurfaceScattering = false;
            s.supportRayTracing = false; s.supportVFXRayTracing = false;
            s.supportWater = false;
            s.supportDecals = false; s.supportDecalLayers = false;
            s.supportDistortion = false;
            s.supportTransparentBackface = false;
            s.supportTransparentDepthPrepass = false; s.supportTransparentDepthPostpass = false;
            s.supportShadowMask = false;
            s.supportLightLayers = false; s.renderingLayerMaskBuffer = false;
            s.supportVariableRateShading = false;
            s.supportHighQualityLineRendering = false;
            s.supportComputeThickness = false;
            s.supportRuntimeAOVAPI = false;
            s.supportTerrainHole = false;
            s.lightProbeSystem = RPS.LightProbeSystem.LegacyLightProbes;
            s.msaaSampleCount = MSAASamples.None;
            s.dynamicResolutionSettings.enabled = false;
            s.lowresTransparentSettings.enabled = true;          // large soft particle sheets render at half resolution

            s.lodBias = new FloatScalableSetting(new[] { 2f, 2f, 2f }, ScalableSettingSchemaId.With3Levels);
            s.maximumLODLevel = new IntScalableSetting(new[] { 0, 0, 0 }, ScalableSettingSchemaId.With3Levels);

            s.lightLoopSettings.maxDirectionalLightsOnScreen = 4;
            s.lightLoopSettings.maxPunctualLightsOnScreen = 96;
            s.lightLoopSettings.maxAreaLightsOnScreen = 4;
            s.lightLoopSettings.maxLightsPerClusterCell = 8;
            s.lightLoopSettings.maxLocalVolumetricFogOnScreen = 8;
            s.lightLoopSettings.cookieAtlasSize = CookieAtlasResolution.CookieResolution512;
            s.lightLoopSettings.reflectionProbeTexCacheSize = ReflectionProbeTextureCacheResolution.Resolution1024x1024;
            s.lightLoopSettings.skyReflectionSize = SkyResolution.SkyResolution128;

            int atlas = final ? 2048 : 1024;
            s.hdShadowInitParams.maxShadowRequests = 16;
            s.hdShadowInitParams.punctualLightShadowAtlas.shadowAtlasResolution = atlas;
            s.hdShadowInitParams.areaLightShadowAtlas.shadowAtlasResolution = 512;
            s.hdShadowInitParams.cachedPunctualLightShadowAtlas = 512;
            s.hdShadowInitParams.cachedAreaLightShadowAtlas = 512;
            s.hdShadowInitParams.maxDirectionalShadowMapResolution = atlas;
            s.hdShadowInitParams.maxPunctualShadowMapResolution = atlas / 2;
            s.hdShadowInitParams.shadowResolutionDirectional = new IntScalableSetting(new[] { 512, 1024, atlas, atlas }, ScalableSettingSchemaId.With4Levels);
            s.hdShadowInitParams.shadowResolutionPunctual = new IntScalableSetting(new[] { 256, 512, atlas / 2, atlas / 2 }, ScalableSettingSchemaId.With4Levels);
            s.hdShadowInitParams.punctualShadowFilteringQuality = HDShadowFilteringQuality.Medium;
            s.hdShadowInitParams.directionalShadowFilteringQuality = HDShadowFilteringQuality.Medium;
            s.hdShadowInitParams.supportScreenSpaceShadows = false;
            s.hdShadowInitParams.supportContactShadows = true;

            // quality tiers: index 0 Low (test profile), 1 Medium, 2 High (final profile)
            s.postProcessQualitySettings.BloomRes[0] = BloomResolution.Quarter;
            s.postProcessQualitySettings.BloomRes[2] = BloomResolution.Half;
            s.postProcessQualitySettings.BloomHighQualityFiltering[0] = false;
            s.postProcessQualitySettings.BloomHighQualityFiltering[2] = true;
            s.lightingQualitySettings.Fog_ControlMode[0] = FogControl.Balance;
            s.lightingQualitySettings.Fog_Budget[0] = 0.166f;
            s.lightingQualitySettings.Fog_DepthRatio[0] = 0.666f;
            s.lightingQualitySettings.Fog_ControlMode[2] = FogControl.Balance;
            s.lightingQualitySettings.Fog_Budget[2] = 0.33f;
            s.lightingQualitySettings.Fog_DepthRatio[2] = 0.666f;

            a.currentPlatformRenderPipelineSettings = s;
        }
    }
}
