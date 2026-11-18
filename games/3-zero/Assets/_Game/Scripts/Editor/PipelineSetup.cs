using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ZERO.EditorTools
{
    /// <summary>Configures URP (PC asset + renderer), quality and player settings from code. No manual step.</summary>
    public static class PipelineSetup
    {
        const string AssetPath = "Assets/Settings/PC_RPAsset.asset";
        const string RendererPath = "Assets/Settings/PC_Renderer.asset";

        public static void Run()
        {
            var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AssetPath);
            var rd = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (urp == null || rd == null) { Debug.LogError("PipelineSetup: PC URP asset or renderer not found"); return; }

            urp.supportsHDR = true;
            urp.supportsCameraOpaqueTexture = true;
            urp.supportsCameraDepthTexture = true;
            urp.msaaSampleCount = 1;
            urp.renderScale = 1f;
            urp.mainLightShadowmapResolution = 4096;
            urp.shadowDistance = 260f;
            urp.shadowCascadeCount = 4;
            urp.cascade4Split = new Vector3(0.07f, 0.2f, 0.47f);
            urp.cascadeBorder = 0.12f;
            urp.shadowDepthBias = 1.0f;
            urp.shadowNormalBias = 0.6f;
            urp.useSRPBatcher = true;
            urp.colorGradingMode = ColorGradingMode.HighDynamicRange;
            urp.colorGradingLutSize = 32;
            var so = new SerializedObject(urp);
            Set(so, "m_MainLightShadowsSupported", true);
            Set(so, "m_SoftShadowsSupported", true);
            Set(so, "m_AdditionalLightShadowsSupported", false);
            SetInt(so, "m_AdditionalLightsRenderingMode", 1);
            SetInt(so, "m_OpaqueDownsampling", 0);
            SetInt(so, "m_SoftShadowQuality", 2);
            SetInt(so, "m_GPUResidentDrawerMode", 0);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(urp);

            rd.renderingMode = RenderingMode.ForwardPlus;
            rd.copyDepthMode = CopyDepthMode.AfterOpaques;
            rd.depthPrimingMode = DepthPrimingMode.Disabled;
            SetupFeatures(rd);
            EditorUtility.SetDirty(rd);

            // quality: the PC level everywhere, with this asset
            string[] names = QualitySettings.names;
            int pc = System.Array.IndexOf(names, "PC"); if (pc < 0) pc = names.Length - 1;
            QualitySettings.SetQualityLevel(pc, true);
            QualitySettings.renderPipeline = urp;
            QualitySettings.vSyncCount = 0;
            QualitySettings.antiAliasing = 0;
            GraphicsSettings.defaultRenderPipeline = urp;
            var qs = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset")[0]);
            var per = qs.FindProperty("m_PerPlatformDefaultQuality");
            if (per != null) for (int i = 0; i < per.arraySize; i++) { var e = per.GetArrayElementAtIndex(i); var v = e.FindPropertyRelative("second"); if (v != null) v.intValue = pc; }
            var cur = qs.FindProperty("m_CurrentQuality"); if (cur != null) cur.intValue = pc;
            qs.ApplyModifiedPropertiesWithoutUndo();

            var strip = GraphicsSettings.GetRenderPipelineSettings<URPShaderStrippingSetting>();
            if (strip != null) strip.stripUnusedPostProcessingVariants = false;
            EditorUtility.SetDirty(AssetDatabase.LoadMainAssetAtPath("Assets/Settings/UniversalRenderPipelineGlobalSettings.asset"));

            PlayerSettings.productName = "ZERO";
            PlayerSettings.companyName = "ZeroWalk";
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1920; PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.visibleInBackground = true;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.SplashScreen.showUnityLogo = false;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { UnityEngine.Rendering.GraphicsDeviceType.Direct3D11 });
            PlayerSettings.forceSingleInstance = false;
            AssetDatabase.SaveAssets();
        }

        static void Set(SerializedObject so, string name, bool v) { var p = so.FindProperty(name); if (p != null) p.boolValue = v; else Debug.LogWarning("PipelineSetup: no property " + name); }
        static void SetInt(SerializedObject so, string name, int v) { var p = so.FindProperty(name); if (p != null) p.intValue = v; else Debug.LogWarning("PipelineSetup: no property " + name); }

        /// <summary>Removes the template's renderer features and installs the game's fullscreen feature.</summary>
        static void SetupFeatures(UniversalRendererData rd)
        {
            var so = new SerializedObject(rd);
            var feats = so.FindProperty("m_RendererFeatures");
            var map = so.FindProperty("m_RendererFeatureMap");
            var old = new List<Object>();
            for (int i = 0; i < feats.arraySize; i++) { var o = feats.GetArrayElementAtIndex(i).objectReferenceValue; if (o != null) old.Add(o); }
            feats.ClearArray(); map.ClearArray();
            so.ApplyModifiedPropertiesWithoutUndo();
            foreach (var o in old) Object.DestroyImmediate(o, true);
            foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(RendererPath))
                if (sub is ScriptableRendererFeature) Object.DestroyImmediate(sub, true);

            var f = ScriptableObject.CreateInstance<ZeroPostFeature>();
            f.name = "ZeroPost"; f.hideFlags |= HideFlags.HideInHierarchy;
            f.material = AssetDatabase.LoadAssetAtPath<Material>(SceneBuilder.MatDir + "/Fullscreen.mat");
            AssetDatabase.AddObjectToAsset(f, rd);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(f, out string _, out long id);
            so = new SerializedObject(rd);
            feats = so.FindProperty("m_RendererFeatures"); map = so.FindProperty("m_RendererFeatureMap");
            feats.arraySize = 1; feats.GetArrayElementAtIndex(0).objectReferenceValue = f;
            map.arraySize = 1; map.GetArrayElementAtIndex(0).longValue = id;
            so.ApplyModifiedPropertiesWithoutUndo();
            rd.SetDirty();
        }
    }
}
