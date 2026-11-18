using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZERO
{
    /// <summary>Command-line flags after "--" (see README): the test harness and the recording options.</summary>
    public static class Harness
    {
        public static bool Cinematic, QuitAtEnd, Perf, FlashLog, Any;
        public static float StartAt = -1f, EndAt = -1f, CaptureEvery = -1f;
        public static string Profile = "test";
        public static string Shots, Only, Tag = "";
        public static string QaDir;

        public struct Shot { public float t, yaw, pitch, fov, lean; public bool cine; }
        public static readonly List<Shot> ShotList = new List<Shot>();

        public static void Parse()
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            // in a player dataPath = <build>/ZERO_Data ; QA lives in the project root (two levels above Builds/<x>/)
            string proj = Application.isEditor ? root : Path.GetFullPath(Path.Combine(root, "..", ".."));
            QaDir = Directory.Exists(Path.Combine(proj, "Assets")) || Directory.Exists(Path.Combine(proj, "QA")) ? Path.Combine(proj, "QA") : Path.Combine(root, "QA");
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "--");
            if (i < 0) return;
            for (i++; i < args.Length; i++)
            {
                string a = args[i]; string next = i + 1 < args.Length ? args[i + 1] : null;
                switch (a)
                {
                    case "--cinematic": Cinematic = true; break;
                    case "--quit-at-end": QuitAtEnd = true; Any = true; break;
                    case "--perf": Perf = true; Any = true; break;
                    case "--flash-log": FlashLog = true; Any = true; break;
                    case "--start-at": StartAt = F(next); i++; Any = true; break;
                    case "--end-at": EndAt = F(next); i++; Any = true; break;
                    case "--capture-every": CaptureEvery = F(next); i++; Any = true; break;
                    case "--profile": Profile = next; i++; break;
                    case "--shots": Shots = next; i++; Any = true; break;
                    case "--only": Only = next; i++; break;
                    case "--tag": Tag = next; i++; break;
                }
            }
            if (!string.IsNullOrEmpty(Shots))
            {
                // "T,yaw,pitch[,fov[,lean]];..."  T = event time in seconds; yaw is in the aircraft's frame (0 = nose,
                // -90 = straight out of the left window); "T,c" = the framing of the cinematic camera at that moment
                foreach (var part in Shots.Split(';'))
                {
                    var f = part.Split(',');
                    if (f.Length < 2) continue;
                    var sh = new Shot { t = F(f[0]), fov = Config.BaseFov, lean = 0.9f };
                    if (f[1].Trim() == "c") sh.cine = true;
                    else
                    {
                        if (f.Length < 3) continue;
                        sh.yaw = F(f[1]); sh.pitch = F(f[2]);
                        if (f.Length > 3) sh.fov = F(f[3]);
                        if (f.Length > 4) sh.lean = F(f[4]);
                    }
                    ShotList.Add(sh);
                }
            }
        }

        static float F(string s) { return float.Parse(s, CultureInfo.InvariantCulture); }
        public static bool Builds(string system) { return string.IsNullOrEmpty(Only) || ("," + Only + ",").Contains("," + system + ","); }
        public static bool Final { get { return Profile == "final"; } }
    }

    /// <summary>Runs the harness: captures, perf statistics, the flash log, the end of the run.</summary>
    public class HarnessRunner : MonoBehaviour
    {
        public static HarnessRunner I;
        public static bool CapturedThisFrame;
        float nextCapture;
        readonly List<float> frameMs = new List<float>(60000);
        StringBuilder flash; RenderTexture flashFull, flashSmall;
        bool running, finishing;
        float vramEnd = -1f;
        int perfFrames; readonly StringBuilder hitches = new StringBuilder();
        public static bool PerfWindowOpen;

        void Awake() { I = this; }

        public void Begin()
        {
            running = true;
            Directory.CreateDirectory(Path.Combine(Harness.QaDir, "shots"));
            nextCapture = Harness.CaptureEvery > 0f ? (float)WorldTime.Real + 0.75f : float.MaxValue;
            if (Harness.FlashLog) { flash = new StringBuilder(1 << 20); flash.Append("t,luma\n"); }
            if (Harness.Perf) PerfWindowOpen = true;
            if (Harness.ShotList.Count > 0) StartCoroutine(RunShots());
            StartCoroutine(EndOfFrameLoop());
        }

        IEnumerator RunShots()
        {
            var rig = SeatRig.I;
            rig.InputEnabled = false; rig.Cinematic = false; rig.FovLock = true;
            WorldTime.Running = false;
            for (int i = 0; i < Harness.ShotList.Count; i++)
            {
                var sh = Harness.ShotList[i];
                WorldTime.Reset(sh.t);
                rig.Place(sh.t);
                if (sh.cine)
                {
                    CinematicDirector.Evaluate(sh.t, rig.EyeAbs, rig.Plane.rotation, out float cy, out float cp, out float cf, out float cl, out float _);
                    rig.SetView(cy, cp, cf, cl);
                }
                else rig.SetView(sh.yaw, sh.pitch, sh.fov, sh.lean);
                rig.Place(sh.t);
                Bootstrap.I.Snap();
                for (int k = 0; k < 6; k++) yield return null;
                yield return new WaitForEndOfFrame();
                Capture(string.Format(CultureInfo.InvariantCulture, "shot_{0:00}_t{1:0000}{2}.png", i, sh.t, Harness.Tag));
            }
            yield return null;
            Finish();
        }

        IEnumerator EndOfFrameLoop()
        {
            var eof = new WaitForEndOfFrame();
            while (true)
            {
                yield return eof;
                CapturedThisFrame = false;
                if (!running) continue;
                if (Harness.ShotList.Count == 0 && WorldTime.Real >= nextCapture && Bootstrap.I.Walking)
                {
                    int sec = Mathf.RoundToInt((float)WorldTime.Real);
                    Capture(string.Format("t{0:0000}_{1:00}m{2:00}s{3}.png", sec, sec / 60, sec % 60, Harness.Tag));
                    nextCapture += Harness.CaptureEvery;
                }
                if (flash != null && Bootstrap.I.Walking) FlashSample();
            }
        }

        void Capture(string file)
        {
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(Harness.QaDir, "shots", file), tex.EncodeToPNG());
            Destroy(tex);
            CapturedThisFrame = true;
        }

        void FlashSample()
        {
            if (flashFull == null || flashFull.width != Screen.width || flashFull.height != Screen.height)
            {
                if (flashFull != null) flashFull.Release();
                flashFull = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32);
                flashSmall = new RenderTexture(64, 64, 0, RenderTextureFormat.ARGB32) { useMipMap = true, autoGenerateMips = false };
            }
            ScreenCapture.CaptureScreenshotIntoRenderTexture(flashFull);
            Graphics.Blit(flashFull, flashSmall); flashSmall.GenerateMips();
            float t = (float)WorldTime.Real;
            AsyncGPUReadback.Request(flashSmall, flashSmall.mipmapCount - 1, TextureFormat.RGBA32, r =>
            {
                if (r.hasError || flash == null) return;
                var c = r.GetData<Color32>()[0];
                // display-referred luminance 0..1
                float l = (0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b) / 255f;
                flash.Append(t.ToString("0.0000", CultureInfo.InvariantCulture)).Append(',').Append(l.ToString("0.0000", CultureInfo.InvariantCulture)).Append('\n');
            });
        }

        void Update()
        {
            if (!running || finishing) return;
            if (Harness.Perf && PerfWindowOpen && Bootstrap.I.Walking && !CapturedThisFrame && perfFrames++ > 30)
            {
                float ms = Time.unscaledDeltaTime * 1000f;
                frameMs.Add(ms);
                if (ms > 50f && hitches.Length < 1600) hitches.AppendFormat(CultureInfo.InvariantCulture, " T={0:0.00}s/{1:0}ms", WorldTime.Real, ms);
            }
            if (Harness.ShotList.Count == 0 && Harness.EndAt > 0f && WorldTime.Real >= Harness.EndAt) Finish();
        }

        /// <summary>Called by the harness at --end-at, or by the ending when the closing line has been shown.</summary>
        public void Finish()
        {
            if (finishing) return;
            finishing = true;
            StartCoroutine(FinishRoutine());
        }

        IEnumerator FinishRoutine()
        {
            if (Harness.Perf) vramEnd = QueryVram();
            AsyncGPUReadback.WaitAllRequests();
            yield return null;
            if (flash != null) File.WriteAllText(Path.Combine(Harness.QaDir, "flash" + Harness.Tag + ".csv"), flash.ToString());
            if (Harness.Perf) WritePerf();
            if (Harness.QuitAtEnd || Harness.ShotList.Count > 0) { yield return null; Application.Quit(0); }
            running = false;
        }

        void WritePerf()
        {
            if (frameMs.Count < 10) return;
            var sorted = new List<float>(frameMs); sorted.Sort();
            double sum = 0; foreach (var f in frameMs) sum += f;
            float avg = (float)(sum / frameMs.Count);
            int n1 = Mathf.Max(1, frameMs.Count / 100);
            double slow = 0; for (int i = 0; i < n1; i++) slow += sorted[sorted.Count - 1 - i];
            float low1 = 1000f / (float)(slow / n1);
            float worst = sorted[sorted.Count - 1];
            int over50 = 0, over100 = 0; foreach (var f in frameMs) { if (f > 50f) over50++; if (f > 100f) over100++; }
            var sb = new StringBuilder();
            sb.AppendLine("ZERO perf  " + DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "profile={0} resolution={1}x{2} frames={3} window={4:0.0}s..{5:0.0}s", Harness.Profile, Screen.width, Screen.height, frameMs.Count, Harness.StartAt < 0 ? 0 : Harness.StartAt, WorldTime.Real));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "avg_fps={0:0.0} low1pct_fps={1:0.0} worst_frame_ms={2:0.0} frames_over_50ms={3} frames_over_100ms={4}", 1000f / avg, low1, worst, over50, over100));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "gpu={0} adapter_vram_mb={1} driver_alloc_mb={2:0}", SystemInfo.graphicsDeviceName, SystemInfo.graphicsMemorySize, UnityEngine.Profiling.Profiler.GetAllocatedMemoryForGraphicsDriver() / 1048576f));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "nvidia_smi_vram_used_mb_before_load={0:0} at_end={1:0} (whole machine, includes the other agents and the desktop); game_delta_mb={2:0}", Bootstrap.I.VramBeforeLoad, vramEnd, vramEnd - Bootstrap.I.VramBeforeLoad));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "cloud_tiles={0} ejecta_arcs={1} sprites={2} origin_shifts={3}", CloudBuilder.TileCount, Bootstrap.I.ArcCount, Bootstrap.I.SpriteCount, FloatingOrigin.Shifts));
            sb.AppendLine("hitches_over_50ms:" + (hitches.Length == 0 ? " none" : hitches.ToString()));
            File.WriteAllText(Path.Combine(Harness.QaDir, "perf" + Harness.Tag + ".txt"), sb.ToString());
        }

        /// <summary>Total VRAM in use on the machine in MB (nvidia-smi), or -1.</summary>
        public static float QueryVram()
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("nvidia-smi", "--query-gpu=memory.used --format=csv,noheader,nounits") { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
                using (var p = System.Diagnostics.Process.Start(psi))
                {
                    string o = p.StandardOutput.ReadLine();
                    p.WaitForExit(3000);
                    return float.Parse(o.Trim(), CultureInfo.InvariantCulture);
                }
            }
            catch { return -1f; }
        }
    }
}
