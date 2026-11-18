using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace MESOCYCLONE
{
    /// <summary>Test harness (Part 8): PNG captures at story-time intervals, perf statistics, the per-frame
    /// luminance log for the flash analysis, and the end of a harness run.</summary>
    [DefaultExecutionOrder(900)]
    public sealed class Harness : MonoBehaviour
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        float nextCapture;
        bool ended;
        double lastReal;
        float settle = 3f;                               // seconds ignored by perf after start
        readonly List<float> frames = new List<float>(65536);
        RenderTexture lumRT, lumMips;
        string lastShot;
        StringBuilder flash;
        int flashPending;
        double flashT0;
        long peakDriverMem;
        public static bool BoardLook;
        public static float BoardYaw, BoardPitch, BoardFov;
        int boardIndex = -1;
        double boardUntil;
        bool boardShot;
        double gpuSum; int gpuN; double gpuMax;
        readonly StringBuilder hitches = new StringBuilder(512);
        int hitchCount;
        System.Threading.Thread utilThread;
        volatile bool utilRun;
        static int utilSum, utilN, utilMax, vramMax;
        readonly FrameTiming[] ft = new FrameTiming[1];

        void Start()
        {
            nextCapture = Cli.StartAt > 0f ? Cli.StartAt : 0f;
            lastReal = Time.realtimeSinceStartupAsDouble;
            if (Cli.FlashLog)
            {
                flash = new StringBuilder(1 << 20);
                flash.Append("game_time_s,real_time_s,mean_luminance,linear_luminance\n");
                flashT0 = Time.realtimeSinceStartupAsDouble;
            }
            if (Cli.CaptureEvery > 0f) Directory.CreateDirectory(Path.Combine(Cli.QaDir, "shots"));
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (Cli.Test == "input") gameObject.AddComponent<InputSelfTest>();
#endif
        }

        void Update()
        {
            if (!Director.Running || ended) return;
            double now = Time.realtimeSinceStartupAsDouble;
            if (Cli.Board != null) { RunBoard(now); return; }
            float real = (float)(now - lastReal);
            lastReal = now;

            if (Cli.Perf)
            {
                if (settle > 0f) settle -= real;
                else
                {
                    frames.Add(real);
                    if (real > 0.045f && hitchCount < 40)
                    {
                        hitchCount++;
                        hitches.Append(Director.T.ToString("F1", Inv)).Append("s:").Append((real * 1000f).ToString("F0", Inv)).Append("ms ");
                    }
                }
                if (utilThread == null) StartUtilSampler();
                FrameTimingManager.CaptureFrameTimings();
                if (settle <= 0f && FrameTimingManager.GetLatestTimings(1, ft) > 0 && ft[0].gpuFrameTime > 0.0)
                { gpuSum += ft[0].gpuFrameTime; gpuN++; if (ft[0].gpuFrameTime > gpuMax) gpuMax = ft[0].gpuFrameTime; }
                long m = Profiler.GetAllocatedMemoryForGraphicsDriver();
                if (m > peakDriverMem) peakDriverMem = m;
            }

            if (Cli.CaptureEvery > 0f && Director.T >= nextCapture && Director.S.black < 0.999f)
            {
                string name = Cli.ShotPrefix + "_" + Director.T.ToString("0000.0", Inv).Replace('.', '_') + ".png";
                lastShot = Path.Combine(Cli.QaDir, "shots", name);
                ScreenCapture.CaptureScreenshot(lastShot);
                nextCapture += Cli.CaptureEvery;
            }

            if (Cli.FlashLog && Time.frameCount > 4) StartCoroutine(SampleLuminance());

            bool end = Cli.EndAt > 0f ? Director.T >= Cli.EndAt : Director.T >= Config.FadeOutEnd + 0.5f;
            if (end) StartCoroutine(Finish());
        }

        /// <summary>Storyboard: for each shot jump to its story time, freeze, look where told, let the temporal
        /// histories settle, capture. Six arbitrary frames of the game in one short GPU run.</summary>
        void RunBoard(double now)
        {
            if (boardIndex < 0 || (boardShot && now >= boardUntil))
            {
                boardIndex++;
                if (boardIndex >= Cli.Board.Length) { StartCoroutine(Finish()); return; }
                float[] b = Cli.Board[boardIndex];
                Director.Freeze = true;
                Director.I.Jump(b[0]);
                Audio.AudioRig.Jump();
                LightningDirector.ResetClock();
                BoardLook = true; BoardYaw = b[1]; BoardPitch = b[2]; BoardFov = b[3];
                boardUntil = now + 1.1;
                boardShot = false;
                return;
            }
            if (!boardShot && now >= boardUntil)
            {
                float[] b = Cli.Board[boardIndex];
                string name = Cli.ShotPrefix + "_" + boardIndex.ToString("00") + "_t" + b[0].ToString("0000.0", Inv).Replace('.', '_') + ".png";
                lastShot = Path.Combine(Cli.QaDir, "shots", name);
                Directory.CreateDirectory(Path.Combine(Cli.QaDir, "shots"));
                ScreenCapture.CaptureScreenshot(lastShot);
                Debug.Log("[Board] " + name + " flashLevel=" + LightningDirector.Level.ToString("F3", Inv) + " slot0=" + LightningDirector.Pos[0] + " col0=" + LightningDirector.Col[0] +
                          " tor=" + Director.TornadoLocal() + " storm=" + Director.StormLocal());
                boardShot = true;
                boardUntil = now + 0.12;
            }
        }

        System.Collections.IEnumerator SampleLuminance()
        {
            yield return new WaitForEndOfFrame();
            int w = Screen.width, h = Screen.height;
            if (lumRT == null || lumRT.width != w || lumRT.height != h)
            {
                if (lumRT != null) { lumRT.Release(); lumMips.Release(); }
                lumRT = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                lumMips = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { useMipMap = true, autoGenerateMips = true };
                lumRT.Create(); lumMips.Create();
            }
            ScreenCapture.CaptureScreenshotIntoRenderTexture(lumRT);
            Graphics.Blit(lumRT, lumMips);                                  // mips = linear-light box averages
            int mip = Mathf.Max(0, lumMips.mipmapCount - 5);                // about 16 x 9 texels
            float gt = Director.T;
            float rt = (float)(Time.realtimeSinceStartupAsDouble - flashT0);
            flashPending++;
            AsyncGPUReadback.Request(lumMips, mip, TextureFormat.RGBA32, req =>
            {
                flashPending--;
                if (req.hasError || flash == null) return;
                NativeArray<byte> d = req.GetData<byte>();
                double sumG = 0, sumL = 0; int n = d.Length / 4;
                for (int i = 0; i < n; i++)
                {
                    float r = d[i * 4] / 255f, g = d[i * 4 + 1] / 255f, b = d[i * 4 + 2] / 255f;
                    sumG += 0.2126f * r + 0.7152f * g + 0.0722f * b;
                    sumL += 0.2126f * Mathf.GammaToLinearSpace(r) + 0.7152f * Mathf.GammaToLinearSpace(g) + 0.0722f * Mathf.GammaToLinearSpace(b);
                }
                flash.Append(gt.ToString("F3", Inv)).Append(',').Append(rt.ToString("F3", Inv)).Append(',')
                     .Append((sumG / n).ToString("F4", Inv)).Append(',').Append((sumL / n).ToString("F4", Inv)).Append('\n');
            });
        }

        System.Collections.IEnumerator Finish()
        {
            ended = true;
            // let the last capture land on disk and the readbacks return
            for (int i = 0; i < 90; i++)
            {
                yield return null;
                bool shotOk = lastShot == null || File.Exists(lastShot);
                if (i >= 3 && flashPending <= 0 && shotOk) break;
            }
            if (Cli.Perf) WritePerf();
            if (flash != null) File.WriteAllText(Path.Combine(Cli.QaDir, "flash.csv"), flash.ToString());
            utilRun = false;
            Debug.Log("[Harness] audio callbacks " + Audio.AudioRig.Callbacks + ", last peak " + Audio.AudioRig.LastPeak.ToString("F3", Inv));
            Debug.Log("[Harness] end at T=" + Director.T.ToString("F1", Inv) + " slowFrames=" + GpuGuard.SlowFrames +
                      " worst=" + (GpuGuard.WorstFrame * 1000f).ToString("F0", Inv) + " ms");
            if (Cli.QuitAtEnd) QuitHard(0);
        }

        /// <summary>GPU utilisation and VRAM of the whole card, sampled every 3 s on a background thread (no hitch).</summary>
        void StartUtilSampler()
        {
            utilRun = true;
            utilThread = new System.Threading.Thread(() =>
            {
                while (utilRun)
                {
                    try
                    {
                        var psi = new ProcessStartInfo("nvidia-smi", "--query-gpu=utilization.gpu,memory.used --format=csv,noheader,nounits")
                        { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
                        using (var p = Process.Start(psi))
                        {
                            string o = p.StandardOutput.ReadToEnd();
                            p.WaitForExit(2000);
                            string[] q = o.Trim().Split(',');
                            if (q.Length >= 2 && int.TryParse(q[0].Trim(), out int u) && int.TryParse(q[1].Trim(), out int m))
                            { utilSum += u; utilN++; if (u > utilMax) utilMax = u; if (m > vramMax) vramMax = m; }
                        }
                    }
                    catch { }
                    System.Threading.Thread.Sleep(3000);
                }
            }) { IsBackground = true };
            utilThread.Start();
        }

        /// <summary>Quit with an exit code; if Unity's teardown hangs, a watchdog thread ends the process.</summary>
        public static void QuitHard(int code)
        {
            Debug.Log("EXIT " + code);
            var t = new System.Threading.Thread(() => { System.Threading.Thread.Sleep(6000); System.Environment.Exit(code); }) { IsBackground = true };
            t.Start();
            Application.Quit(code);
        }

        void WritePerf()
        {
            var sb = new StringBuilder();
            int n = frames.Count;
            if (n > 0)
            {
                double sum = 0; float worst = 0f;
                for (int i = 0; i < n; i++) { sum += frames[i]; if (frames[i] > worst) worst = frames[i]; }
                var sorted = new List<float>(frames); sorted.Sort();
                float p99 = sorted[Mathf.Min(n - 1, (int)(n * 0.99f))];
                int over50 = 0; for (int i = 0; i < n; i++) if (frames[i] > 0.05f) over50++;
                sb.AppendLine("profile: " + (Cli.Final ? "final" : "test") + "  resolution: " + Screen.width + "x" + Screen.height +
                              "  story " + (Cli.StartAt > 0 ? Cli.StartAt : 0).ToString("F0", Inv) + " .. " + Director.T.ToString("F0", Inv) + " s");
                sb.AppendLine("frames: " + n + "  real seconds: " + sum.ToString("F1", Inv));
                sb.AppendLine("average fps: " + (n / sum).ToString("F1", Inv));
                sb.AppendLine("1% low fps: " + (1f / p99).ToString("F1", Inv));
                sb.AppendLine("worst frame ms: " + (worst * 1000f).ToString("F1", Inv));
                if (gpuN > 0) sb.AppendLine("GPU frame ms (FrameTimingManager): average " + (gpuSum / gpuN).ToString("F2", Inv) + ", max " + gpuMax.ToString("F2", Inv));
                if (utilN > 0) sb.AppendLine("GPU utilisation % (nvidia-smi, whole card, " + utilN + " samples): average " + (utilSum / utilN) + ", max " + utilMax +
                    "   VRAM used MB (whole card): max " + vramMax);
                if (hitchCount > 0) sb.AppendLine("frames over 45 ms (story time: ms): " + hitches);
                sb.AppendLine("frames over 50 ms: " + over50 + "   frames over 100 ms (guard): " + GpuGuard.SlowFrames);
            }
            sb.AppendLine("graphics device: " + SystemInfo.graphicsDeviceName + " (" + SystemInfo.graphicsDeviceType + "), card memory MB: " + SystemInfo.graphicsMemorySize);
            sb.AppendLine("driver-allocated graphics memory MB (peak, dev builds only): " + (peakDriverMem / (1024 * 1024)));
            sb.AppendLine("nvidia-smi memory.used MB (whole card, all processes): " + NvidiaUsed());
            File.WriteAllText(Path.Combine(Cli.QaDir, "perf.txt"), sb.ToString());
            Debug.Log("[Harness] perf written\n" + sb);
        }

        static string NvidiaUsed()
        {
            try
            {
                var psi = new ProcessStartInfo("nvidia-smi", "--query-gpu=memory.used,temperature.gpu --format=csv,noheader,nounits")
                { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
                using (var p = Process.Start(psi))
                {
                    string o = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(3000);
                    return o.Trim() + "  (MB, C)";
                }
            }
            catch (System.Exception e) { return "n/a (" + e.Message + ")"; }
        }
    }
}
