using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MESOCYCLONE.EditorTools
{
    /// <summary>Batch entry points (no manual editor step is ever needed):
    /// Unity.exe -batchmode -nographics -quit -executeMethod MESOCYCLONE.EditorTools.BatchTools.&lt;Method&gt;</summary>
    public static class BatchTools
    {
        public const string ScenePath = "Assets/_Game/Scenes/Main.unity";

        [MenuItem("Tools/MESOCYCLONE/Rebuild All")]
        public static void RebuildAll()
        {
            ProjectSetup.Run();
            GameAssets hub = AssetGen.Run();
            VfxGen.Run();
            VfxGenWeather.Run();
            AssetGen.CollectVfx(hub);
            BuildScene(hub);
            AssetDatabase.SaveAssets();
            Debug.Log("[BatchTools] RebuildAll done");
        }

        /// <summary>Compile gate: reaching this line means all scripts compiled.</summary>
        public static void CompileGate()
        {
            Debug.Log("[BatchTools] COMPILE OK");
        }

        public static void Setup()
        {
            ProjectSetup.Run();
            Debug.Log("[BatchTools] Setup done");
        }

        static void BuildScene(GameAssets hub)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("Bootstrap");
            var boot = go.AddComponent<Bootstrap>();
            boot.assets = hub;
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        [MenuItem("Tools/MESOCYCLONE/Build Dev")]
        public static void BuildDev()
        {
            Build("Builds/Dev/" + Config.ProductName + ".exe", BuildOptions.Development);
        }

        [MenuItem("Tools/MESOCYCLONE/Build Release")]
        public static void BuildRelease()
        {
            Build("Builds/" + Config.ProductName + "/" + Config.ProductName + ".exe", BuildOptions.None);
        }

        /// <summary>Regenerates everything, then builds the development player (one Unity start instead of two).</summary>
        public static void RebuildAndBuildDev()
        {
            RebuildAll();
            BuildDev();
        }

        public static void RebuildAndBuildRelease()
        {
            RebuildAll();
            BuildRelease();
        }

        static void Build(string exePath, BuildOptions options)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(exePath));
            var opt = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = exePath,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = options
            };
            BuildReport report = BuildPipeline.BuildPlayer(opt);
            BuildSummary s = report.summary;
            Debug.Log("[BatchTools] BUILD " + s.result + " -> " + exePath + " (" + (s.totalSize / (1024 * 1024)) + " MB, " +
                      s.totalTime.TotalSeconds.ToString("F0") + " s, errors " + s.totalErrors + ")");
            if (s.result != BuildResult.Succeeded && Application.isBatchMode) EditorApplication.Exit(2);
        }
    }
}
