using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace ZERO.EditorTools
{
    /// <summary>Batch entry points: -executeMethod ZERO.EditorTools.BatchTools.RebuildAll | BuildDev | BuildRelease | CompileGate.</summary>
    public static class BatchTools
    {
        [MenuItem("Tools/ZERO/Rebuild All")]
        public static void RebuildAll()
        {
            // materials first (the renderer feature references one), then the pipeline, then the scene
            Directory.CreateDirectory(SceneBuilder.MatDir);
            AssetDatabase.Refresh();
            var tmp = ScriptableObject.CreateInstance<GameAssets>();
            SceneBuilder.BuildMaterials(tmp);
            Object.DestroyImmediate(tmp);
            AssetDatabase.SaveAssets();
            PipelineSetup.Run();
            SceneBuilder.Run();
            AssetDatabase.SaveAssets();
            Debug.Log("ZERO: RebuildAll done");
        }

        public static void CompileGate() { Debug.Log("ZERO: COMPILE OK"); }

        [MenuItem("Tools/ZERO/Build Dev Player")]
        public static void BuildDev() { Build("Builds/Dev/ZERO.exe", BuildOptions.Development); }

        [MenuItem("Tools/ZERO/Build Release Player")]
        public static void BuildRelease() { Build("Builds/ZERO/ZERO.exe", BuildOptions.None); }

        static void Build(string exe, BuildOptions opts)
        {
            RebuildAll();
            var o = new BuildPlayerOptions
            {
                scenes = new[] { SceneBuilder.ScenePath },
                locationPathName = exe,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = opts,
            };
            BuildReport rep = BuildPipeline.BuildPlayer(o);
            var s = rep.summary;
            Debug.Log("ZERO: BUILD " + s.result + " -> " + exe + " size " + (s.totalSize / 1048576f).ToString("0") + " MB, errors " + s.totalErrors + ", time " + s.totalTime);
            if (s.result != BuildResult.Succeeded && Application.isBatchMode) EditorApplication.Exit(1);
        }
    }
}
