using System;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace MESOCYCLONE
{
    /// <summary>Harness flags: user arguments after "--" on the command line (Part 8).</summary>
    public static class Cli
    {
        public static bool Cinematic;
        public static float StartAt = -1f;        // story seconds; < 0 = from the beginning
        public static float EndAt = -1f;          // story seconds; < 0 = play to the end
        public static float CaptureEvery = -1f;   // story seconds between PNG captures; < 0 = none
        public static bool Final;                 // --profile final (test is the default)
        public static bool QuitAtEnd;
        public static bool Perf;
        public static bool FlashLog;
        public static float Step = -1f;           // harness only: fixed story seconds per rendered frame (fast look checks)
        public static string Test = "";           // harness only: isolate one hero effect ("storm", "tornado", "glass", ...)
        public static bool HasLook;               // harness only: fixed gaze "--look yaw,pitch[,fov]"
        public static float LookYaw, LookPitch, LookFov;
        public static string ShotPrefix = "s";
        public static bool Mute;
        public static float[][] Board;            // harness only: storyboard of frozen shots "T,yaw,pitch[,fov];..."
        public static bool Harness;               // any harness flag present

        static bool parsed;

        public static void Parse()
        {
            if (parsed) return;
            parsed = true;
            string[] a = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(a, "--");
            if (i < 0) return;
            for (i++; i < a.Length; i++)
            {
                switch (a[i])
                {
                    case "--cinematic": Cinematic = true; break;
                    case "--start-at": StartAt = F(a, ++i); Harness = true; break;
                    case "--end-at": EndAt = F(a, ++i); Harness = true; break;
                    case "--capture-every": CaptureEvery = F(a, ++i); Harness = true; break;
                    case "--profile": Final = S(a, ++i) == "final"; break;
                    case "--quit-at-end": QuitAtEnd = true; Harness = true; break;
                    case "--perf": Perf = true; Harness = true; break;
                    case "--flash-log": FlashLog = true; Harness = true; break;
                    case "--step": Step = F(a, ++i); Harness = true; break;
                    case "--test": Test = S(a, ++i); Harness = true; break;
                    case "--prefix": ShotPrefix = S(a, ++i); break;
                    case "--mute": Mute = true; break;
                    case "--board":
                        {
                            string[] shots = S(a, ++i).Split(';');
                            var list = new System.Collections.Generic.List<float[]>();
                            foreach (string sh in shots)
                            {
                                string[] p = sh.Split(',');
                                if (p.Length < 3) continue;
                                list.Add(new[] { P(p[0]), P(p[1]), P(p[2]), p.Length > 3 ? P(p[3]) : Config.FovDefault });
                            }
                            if (list.Count > 0) { Board = list.ToArray(); Harness = true; Mute = true; }
                            break;
                        }
                    case "--look":
                        {
                            string[] p = S(a, ++i).Split(',');
                            HasLook = p.Length >= 2;
                            if (HasLook)
                            {
                                LookYaw = P(p[0]); LookPitch = P(p[1]);
                                LookFov = p.Length > 2 ? P(p[2]) : Config.FovDefault;
                            }
                            break;
                        }
                    default: Debug.LogWarning("[Cli] unknown flag " + a[i]); break;
                }
            }
            Debug.Log("[Cli] cinematic=" + Cinematic + " start=" + StartAt + " end=" + EndAt + " capture=" + CaptureEvery +
                      " profile=" + (Final ? "final" : "test") + " perf=" + Perf + " flash=" + FlashLog + " step=" + Step + " test=" + Test);
        }

        static string S(string[] a, int i) => i < a.Length ? a[i] : "";
        static float F(string[] a, int i) => P(S(a, i));
        static float P(string s) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 0f;

        static string qaDir;

        /// <summary>The QA folder: project root /QA when the player runs from Builds/&lt;x&gt;/, else next to the exe.</summary>
        public static string QaDir
        {
            get
            {
                if (qaDir != null) return qaDir;
                string exeDir = Path.GetDirectoryName(Application.dataPath) ?? ".";
                string root = Path.GetFullPath(Path.Combine(exeDir, "..", ".."));
                qaDir = Directory.Exists(Path.Combine(root, "ProjectSettings")) ? Path.Combine(root, "QA") : Path.Combine(exeDir, "QA");
                Directory.CreateDirectory(qaDir);
                return qaDir;
            }
        }
    }
}
