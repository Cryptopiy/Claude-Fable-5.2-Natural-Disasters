using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace ZERO
{
    /// <summary>Machine safety inside the game (MASTER_PROMPT Part 4B.7): watches frame times and quits with exit
    /// code 7 before a stalled GPU job can turn into a driver reset.</summary>
    public class GpuGuard : MonoBehaviour
    {
        const float WarmUp = 20f;
        float t0; int slowStreak; float winTime; int winFrames; bool tripped;
        public static float WorstMs; public static int Over100;

        void OnEnable() { t0 = Time.realtimeSinceStartup; }

        void Update()
        {
            if (tripped || !Bootstrap.I.Walking) { t0 = Mathf.Max(t0, Time.realtimeSinceStartup - WarmUp + 3f); return; }
            float dt = Time.unscaledDeltaTime;
            if (HarnessRunner.CapturedThisFrame) return;          // a PNG capture stalls the CPU, not the GPU
            float ms = dt * 1000f;
            if (Time.realtimeSinceStartup - t0 > 3f) { if (ms > WorstMs) WorstMs = ms; if (ms > 100f) Over100++; }
            if (Time.realtimeSinceStartup - t0 < WarmUp) return;
            slowStreak = ms > 250f ? slowStreak + 1 : 0;
            winTime += dt; winFrames++;
            bool avgBad = false;
            if (winTime >= 2f) { avgBad = (winTime / winFrames) > 0.1f; winTime = 0f; winFrames = 0; }
            if (slowStreak >= 3 || avgBad) Trip(ms, avgBad);
        }

        void Trip(float ms, bool avg)
        {
            tripped = true;
            try
            {
                Directory.CreateDirectory(Harness.QaDir);
                File.WriteAllText(Path.Combine(Harness.QaDir, "gpu_guard.txt"), string.Format(CultureInfo.InvariantCulture,
                    "GPU GUARD TRIPPED at t={0:0.0}s event_time={1:0} reason={2} last_frame_ms={3:0} resolution={4}x{5} profile={6}\n",
                    WorldTime.Real, WorldTime.Now, avg ? "2s average over 100 ms" : "3 frames over 250 ms", ms, Screen.width, Screen.height, Harness.Profile));
            }
            catch { }
            Debug.LogError("GpuGuard: frame times out of bounds, quitting with exit code 7");
            Application.Quit(7);
        }
    }

    /// <summary>One switch for the two quality profiles (test = cheap development runs, final = recording).</summary>
    public static class QualityProfile
    {
        public static float Density = 0.5f;        // instance / particle multiplier (1 = final caps)

        public static void Apply(Camera cam)
        {
            bool final = Harness.Final;
            // the engine limiter is the cap: vsync alone did not hold 60 fps in a small window, and uncapped is forbidden
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
            Application.runInBackground = true;
            Density = final ? 1f : 0.5f;
            var urp = UniversalRenderPipeline.asset;
            if (urp != null)
            {
                urp.msaaSampleCount = 1;
                urp.renderScale = 1f;
            }
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = final ? AntialiasingQuality.High : AntialiasingQuality.Medium;
            data.dithering = true; data.stopNaN = true;
        }
    }
}
