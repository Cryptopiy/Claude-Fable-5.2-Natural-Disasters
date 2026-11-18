using System.IO;
using UnityEditor;
using UnityEngine;

namespace CASCADE.EditorTools
{
    /// <summary>
    /// Offline sound check without a GPU:
    ///   Unity -batchmode -nographics -quit -executeMethod CASCADE.EditorTools.AudioTools.RenderAll
    /// renders the nominal cinematic run to QA/audio/full.wav and b1.wav .. b7.wav (48 kHz, 16-bit, stereo).
    /// Then: python -I Tools/audio_check.py
    /// </summary>
    public static class AudioTools
    {
        [MenuItem("Tools/CASCADE/Render Audio")]
        public static void RenderAll()
        {
            try
            {
                PathDef.Build();
                string root = Directory.GetParent(Application.dataPath).FullName;
                string dir = Path.Combine(root, "QA", "audio");
                string info = ScoreOffline.RenderAll(dir, 48000, PathDef.Length,
                                                     s => (int)PathDef.SurfaceAt(s), s => PathDef.IsStairs(s));
                Debug.Log("CASCADE_INFO " + info);
                Debug.Log("CASCADE_OK AudioRender");
            }
            catch (System.Exception e)
            {
                Debug.LogError("CASCADE_FAIL AudioRender: " + e);
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }
    }
}
