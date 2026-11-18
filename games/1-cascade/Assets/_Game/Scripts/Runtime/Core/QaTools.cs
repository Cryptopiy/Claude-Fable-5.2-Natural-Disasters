using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;

namespace CASCADE
{
    /// <summary>
    /// Machine safety inside the game (Part 4B.7): quits with exit code 7 when frames get dangerously long.
    /// Also collects the --perf numbers and the --flash-log luminance trace.
    /// </summary>
    public class QaTools : MonoBehaviour
    {
        public static QaTools I;
        public static bool Tripped;

        const float WarmUp = 20f;
        float t0;
        int longRun;
        float winTime; int winFrames;
        int over100;

        float[] frames; int frameN;
        float worst;
        long vramPeak;

        StringBuilder flash;
        RenderTexture flashFull, flashSmall;
        bool flashBusy;

        void Awake()
        {
            I = this;
            t0 = Time.realtimeSinceStartup;
            if (Harness.Perf) frames = new float[80000];
            if (Harness.FlashLog) { flash = new StringBuilder(1 << 20); flash.Append("time,story,lum\n"); StartCoroutine(FlashLoop()); }
        }

        /// <summary>Called once when the walk actually starts (after loading), so loading frames are not counted.</summary>
        public void MarkStart() { t0 = Mathf.Min(t0, Time.realtimeSinceStartup); frameN = 0; worst = 0f; started = true; startReal = Time.realtimeSinceStartup; }
        bool started; float startReal;

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            float since = Time.realtimeSinceStartup - t0;
            if (started && Harness.Perf && frames != null && Time.realtimeSinceStartup - startReal > 2f)
            {
                if (frameN < frames.Length) frames[frameN++] = dt;
                if (dt > worst) worst = dt;
                long v = Profiler.GetAllocatedMemoryForGraphicsDriver();
                if (v > vramPeak) vramPeak = v;
            }
            if (!Harness.Any || since < WarmUp || !started) return;
            if (dt > 0.1f) { over100++; Debug.Log("CASCADE_INFO long frame " + (dt * 1000f).ToString("F0") + " ms at story " + Story.Time.ToString("F1")); }
            longRun = dt > 0.25f ? longRun + 1 : 0;
            winTime += dt; winFrames++;
            bool avgBad = false;
            if (winTime >= 2f) { avgBad = winTime / winFrames > 0.1f; winTime = 0f; winFrames = 0; }
            if (longRun >= 3 || avgBad)
            {
                Tripped = true;
                try
                {
                    File.AppendAllText(Path.Combine(Harness.QaDir, "gpu_guard.txt"),
                        string.Format(CultureInfo.InvariantCulture, "GPU GUARD: tag {0} story {1:F1}s frame {2:F0} ms, longRun {3}, avgBad {4}\n",
                            Harness.Tag, Story.Time, dt * 1000f, longRun, avgBad));
                }
                catch { }
                Debug.Log("CASCADE_GPU_GUARD tripped, quitting with exit code 7");
                WriteReports();
                Application.Quit(7);
            }
        }

        IEnumerator FlashLoop()
        {
            var eof = new WaitForEndOfFrame();
            while (true)
            {
                yield return eof;
                if (!started || flashBusy) continue;
                if (flashFull == null || flashFull.width != Screen.width || flashFull.height != Screen.height)
                {
                    if (flashFull != null) flashFull.Release();
                    flashFull = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32) { useMipMap = true, autoGenerateMips = false, filterMode = FilterMode.Trilinear };
                    flashSmall = new RenderTexture(32, 18, 0, RenderTextureFormat.ARGB32);
                }
                ScreenCapture.CaptureScreenshotIntoRenderTexture(flashFull);
                flashFull.GenerateMips();
                Graphics.Blit(flashFull, flashSmall);
                float real = Time.realtimeSinceStartup - startReal, story = Story.Time;
                flashBusy = true;
                AsyncGPUReadback.Request(flashSmall, 0, TextureFormat.RGBA32, req =>
                {
                    flashBusy = false;
                    if (req.hasError) return;
                    NativeArray<byte> d = req.GetData<byte>();
                    long sum = 0; int n = d.Length / 4;
                    for (int i = 0; i < d.Length; i += 4) sum += d[i] * 2126 + d[i + 1] * 7152 + d[i + 2] * 722;
                    float lum = sum / (n * 10000f * 255f);
                    flash.Append(real.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                         .Append(story.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                         .Append(lum.ToString("F4", CultureInfo.InvariantCulture)).Append('\n');
                });
            }
        }

        public void WriteReports()
        {
            try
            {
                if (Harness.Perf && frameN > 10)
                {
                    float sum = 0f;
                    for (int i = 0; i < frameN; i++) sum += frames[i];
                    var sorted = new float[frameN];
                    System.Array.Copy(frames, sorted, frameN);
                    System.Array.Sort(sorted);
                    int k = Mathf.Max(1, frameN / 100);
                    float low = 0f;
                    for (int i = frameN - k; i < frameN; i++) low += sorted[i];
                    low /= k;
                    int hitch50 = 0;
                    for (int i = 0; i < frameN; i++) if (frames[i] > 0.05f) hitch50++;
                    string s = string.Format(CultureInfo.InvariantCulture,
                        "tag {0} profile {1} {2}x{3}\nframes {4} duration {5:F1} s\navg fps {6:F1}\n1% low fps {7:F1}\nworst frame {8:F1} ms\nframes over 50 ms {9}\nframes over 100 ms (after warm-up) {10}\ngraphics driver memory peak {11} MB (Profiler; 0 in non-development builds)\ngraphics card memory {12} MB\ndevice {13} / {14}\n\n",
                        Harness.Tag, Harness.FinalProfile ? "final" : "test", Screen.width, Screen.height, frameN, sum,
                        frameN / sum, 1f / low, worst * 1000f, hitch50, over100, vramPeak / (1024 * 1024), SystemInfo.graphicsMemorySize,
                        SystemInfo.graphicsDeviceName, SystemInfo.graphicsDeviceType);
                    File.AppendAllText(Path.Combine(Harness.QaDir, "perf.txt"), s);
                }
                if (flash != null) File.WriteAllText(Path.Combine(Harness.QaDir, "flash.csv"), flash.ToString());
            }
            catch (System.Exception e) { Debug.Log("CASCADE_QA write failed: " + e.Message); }
        }
    }

    /// <summary>Harness captures: --capture-every (interval of story time) and --shots (jump, settle, capture).</summary>
    public class CaptureTool : MonoBehaviour
    {
        float nextAt = -1f;
        int shotIndex; int settle; bool waitingWrite;
        public bool ShotsDone { get; private set; }
        public bool UsingShots { get { return Harness.Shots != null && Harness.Shots.Length > 0; } }

        public void Begin()
        {
            if (Harness.CaptureEvery > 0f) nextAt = Story.Time;
            if (UsingShots) { shotIndex = 0; settle = 0; GameLoop.I.SeekTo(Harness.Shots[0]); }
            else ShotsDone = true;
        }

        void Shoot(float t)
        {
            string name = string.Format(CultureInfo.InvariantCulture, "shot_{0}_{1:0000.0}.png", Harness.Tag, t);
            ScreenCapture.CaptureScreenshot(Path.Combine(Harness.QaDir, "shots", name));
        }

        public void Tick()
        {
            if (UsingShots && !ShotsDone)
            {
                settle++;
                if (settle == 70) Shoot(Harness.Shots[shotIndex]);
                if (settle >= 74)
                {
                    shotIndex++; settle = 0;
                    if (shotIndex >= Harness.Shots.Length) ShotsDone = true;
                    else GameLoop.I.SeekTo(Harness.Shots[shotIndex]);
                }
                return;
            }
            if (nextAt >= 0f && Story.Time >= nextAt)
            {
                Shoot(Story.Time);
                nextAt += Harness.CaptureEvery;
            }
        }
    }
}
