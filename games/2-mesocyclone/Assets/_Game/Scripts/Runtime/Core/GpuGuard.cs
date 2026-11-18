using System.Globalization;
using System.IO;
using UnityEngine;

namespace MESOCYCLONE
{
    /// <summary>Part 4B item 7. Always on in harness runs: watches real frame times; if a frame takes longer than
    /// 250 ms three times in a row (after a 20 s warm-up) or the 2 s average exceeds 100 ms, it writes
    /// QA/gpu_guard.txt and quits with exit code 7. Frames over 100 ms are counted and logged as bugs.</summary>
    public sealed class GpuGuard : MonoBehaviour
    {
        public static int SlowFrames;            // frames > 100 ms after warm-up
        public static float WorstFrame;          // seconds, after warm-up
        double last, start;
        int longRun;
        readonly float[] ring = new float[240];
        int ringN, ringI;
        double ringSum;
        bool tripped;

        void OnEnable()
        {
            start = last = Time.realtimeSinceStartupAsDouble;
        }

        /// <summary>Call after a deliberate stall (loading, a jump) so it is not counted.</summary>
        public void Forgive()
        {
            last = Time.realtimeSinceStartupAsDouble;
            longRun = 0; ringN = 0; ringI = 0; ringSum = 0;
        }

        void Update()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            float dt = (float)(now - last);
            last = now;
            if (tripped || now - start < Config.GuardWarmup) return;

            if (dt > WorstFrame) WorstFrame = dt;
            if (dt > 0.1f)
            {
                SlowFrames++;
                Debug.LogWarning("[GpuGuard] slow frame " + (dt * 1000f).ToString("F0", CultureInfo.InvariantCulture) + " ms at T=" +
                                 Director.T.ToString("F1", CultureInfo.InvariantCulture));
            }
            longRun = dt > Config.GuardFrameLimit ? longRun + 1 : 0;

            // 2 s running average
            ringSum += dt;
            if (ringN < ring.Length) { ring[ringI] = dt; ringN++; }
            else { ringSum -= ring[ringI]; ring[ringI] = dt; }
            ringI = (ringI + 1) % ring.Length;
            double window = 0; int n = 0;
            for (int k = 1; k <= ringN && window < 2.0; k++) { window += ring[(ringI - k + ring.Length) % ring.Length]; n++; }
            float avg = n > 0 && window >= 2.0 ? (float)(window / n) : 0f;

            if (longRun >= 3 || avg > Config.GuardAvgLimit)
            {
                tripped = true;
                string msg = "GPU GUARD TRIPPED at T=" + Director.T.ToString("F1", CultureInfo.InvariantCulture) +
                             " frame=" + (dt * 1000f).ToString("F0", CultureInfo.InvariantCulture) + " ms, longRun=" + longRun +
                             ", avg2s=" + (avg * 1000f).ToString("F0", CultureInfo.InvariantCulture) + " ms";
                Debug.LogError("[GpuGuard] " + msg);
                try { File.WriteAllText(Path.Combine(Cli.QaDir, "gpu_guard.txt"), msg + "\n"); } catch { }
                Harness.QuitHard(Config.GuardExitCode);
            }
        }
    }
}
