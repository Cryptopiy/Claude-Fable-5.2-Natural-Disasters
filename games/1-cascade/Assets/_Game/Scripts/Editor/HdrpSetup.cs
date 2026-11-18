using System.Reflection;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace CASCADE.EditorTools
{
    /// <summary>Creates and assigns the HDRP assets entirely from script (no manual step) and removes the URP template leftovers.</summary>
    public static class HdrpSetup
    {
        public const string AssetPath = "Assets/_Game/Generated/Settings/CascadeHDRP.asset";

        public static void Run()
        {
            // 1) global settings (internal type: reflection first, public route as fallback)
            RenderPipelineGlobalSettings global = null;
            try
            {
                var gsType = typeof(HDRenderPipelineAsset).Assembly.GetType("UnityEngine.Rendering.HighDefinition.HDRenderPipelineGlobalSettings");
                var ensure = gsType.GetMethod("Ensure", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                global = ensure.Invoke(null, new object[] { true }) as RenderPipelineGlobalSettings;
            }
            catch (System.Exception e) { Debug.Log("CASCADE_INFO Ensure via reflection failed: " + e.Message); }
            if (global == null)
            {
                global = AssetDatabase.LoadAssetAtPath<RenderPipelineGlobalSettings>("Assets/HDRPDefaultResources/HDRenderPipelineGlobalSettings.asset");
                if (global != null) EditorGraphicsSettings.SetRenderPipelineGlobalSettingsAsset<HDRenderPipeline>(global);
            }

            // 2) pipeline asset
            CoreUtils.EnsureFolderTreeInAssetFilePath(AssetPath);
            var hdrp = AssetDatabase.LoadAssetAtPath<HDRenderPipelineAsset>(AssetPath);
            if (hdrp == null)
            {
                hdrp = ScriptableObject.CreateInstance<HDRenderPipelineAsset>();
                AssetDatabase.CreateAsset(hdrp, AssetPath);
            }
            var s = hdrp.currentPlatformRenderPipelineSettings;
            s.supportedLitShaderMode = RenderPipelineSettings.SupportedLitShaderMode.DeferredOnly;
            s.colorBufferFormat = RenderPipelineSettings.ColorBufferFormat.R16G16B16A16;   // dark ash gradients band in R11G11B10
            s.supportVolumetrics = true;
            s.supportVolumetricClouds = true;
            s.supportWater = false;
            s.supportCustomPass = true;
            s.supportMotionVectors = true;
            s.supportSSR = false; s.supportSSRTransparent = false; s.supportSSGI = false; s.supportRayTracing = false;
            s.supportSSAO = true;
            s.supportSubsurfaceScattering = false;
            s.supportShadowMask = false;
            s.supportDecals = false;
            s.supportDistortion = false;
            s.supportTransparentBackface = false;
            s.supportTransparentDepthPrepass = false;
            s.supportTransparentDepthPostpass = false;
            s.msaaSampleCount = MSAASamples.None;
            s.lightProbeSystem = RenderPipelineSettings.LightProbeSystem.LegacyLightProbes;
            s.supportDataDrivenLensFlare = true;
            s.supportScreenSpaceLensFlare = false;
            s.lowresTransparentSettings.enabled = true;
            s.hdShadowInitParams.maxDirectionalShadowMapResolution = 4096;
            s.hdShadowInitParams.shadowResolutionDirectional = new IntScalableSetting(new[] { 512, 1024, 2048, 4096 }, ScalableSettingSchemaId.With4Levels);
            s.lightLoopSettings.maxLocalVolumetricFogOnScreen = 16;
            s.postProcessSettings.lutSize = 32;
            hdrp.currentPlatformRenderPipelineSettings = s;
            EditorUtility.SetDirty(hdrp);

            // 3) assign everywhere
            GraphicsSettings.defaultRenderPipeline = hdrp;
            QualitySettings.ForEach(() =>
            {
                QualitySettings.renderPipeline = hdrp;
                QualitySettings.shadows = UnityEngine.ShadowQuality.All;
                QualitySettings.vSyncCount = 1;
                QualitySettings.antiAliasing = 0;
            });
            PlayerSettings.colorSpace = ColorSpace.Linear;

            // 4) stripping settings stay at their defaults (runtime debug shaders are stripped by default)
            if (global != null) EditorUtility.SetDirty(global);
            AssetDatabase.SaveAssets();

            // 5) URP template leftovers (only after the re-assignment)
            foreach (var p in new[]
            {
                "Assets/Settings", "Assets/TutorialInfo", "Assets/Readme.asset", "Assets/Scenes"
            })
                if (AssetDatabase.IsValidFolder(p) || AssetDatabase.LoadMainAssetAtPath(p) != null) AssetDatabase.DeleteAsset(p);
            AssetDatabase.SaveAssets();

            bool ok = GraphicsSettings.defaultRenderPipeline is HDRenderPipelineAsset;
            Debug.Log(ok ? "CASCADE_INFO HDRP active: " + AssetPath : "CASCADE_FAIL HDRP not active");
            if (!ok) throw new System.Exception("HDRP not active");
        }
    }
}
