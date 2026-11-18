using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace CASCADE
{
    /// <summary>Command-line flags (user arguments after "--") and QA paths.</summary>
    public static class Harness
    {
        public static bool Cinematic;
        public static float StartAt = -1f;
        public static float EndAt = -1f;
        public static float CaptureEvery = -1f;
        public static bool FinalProfile;
        public static bool QuitAtEnd;
        public static bool Perf;
        public static bool FlashLog;
        public static string Tag = "run";
        public static float[] Shots;          // extra: jump to each time, settle, capture, next ("--shots 80,150,185")
        public static string Iso = "";        // extra: isolation scene for one hero effect ("--iso column")
        public static float[] Look;           // extra: fixed yaw,pitch in degrees for captures
        public static float[] CamPos;         // extra: fixed camera position x,y,z (isolation scenes)
        public static float Fov = -1f;
        public static float TimeScale = 1f;   // extra: story clock multiplier for harness-only previews
        public static bool Any;               // true when any harness flag is present (enables GpuGuard, hides hints)

        static bool parsed;
        static string qaDir;

        public static string QaDir
        {
            get
            {
                if (qaDir == null)
                {
                    // Builds/Dev/CASCADE_Data -> project root is three levels up; fall back to persistentDataPath.
                    string root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));
                    string q = Path.Combine(root, "QA");
                    try { Directory.CreateDirectory(q); Directory.CreateDirectory(Path.Combine(q, "shots")); qaDir = q; }
                    catch { qaDir = Application.persistentDataPath; }
                }
                return qaDir;
            }
        }

        public static void Parse()
        {
            if (parsed) return;
            parsed = true;
            string[] a = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(a, "--");
            if (i < 0) return;
            for (i++; i < a.Length; i++)
            {
                string k = a[i];
                string v = i + 1 < a.Length ? a[i + 1] : "";
                switch (k)
                {
                    case "--cinematic": Cinematic = true; break;
                    case "--start-at": StartAt = F(v); i++; Any = true; break;
                    case "--end-at": EndAt = F(v); i++; Any = true; break;
                    case "--capture-every": CaptureEvery = F(v); i++; Any = true; break;
                    case "--profile": FinalProfile = v == "final"; i++; break;
                    case "--quit-at-end": QuitAtEnd = true; Any = true; break;
                    case "--perf": Perf = true; Any = true; break;
                    case "--flash-log": FlashLog = true; Any = true; break;
                    case "--tag": Tag = v; i++; break;
                    case "--shots": Shots = List(v); i++; Any = true; break;
                    case "--iso": Iso = v; i++; Any = true; break;
                    case "--look": Look = List(v); i++; break;
                    case "--cam": CamPos = List(v); i++; break;
                    case "--fov": Fov = F(v); i++; break;
                    case "--timescale": TimeScale = Mathf.Clamp(F(v), 0.1f, 20f); i++; break;
                }
            }
        }

        static float F(string s)
        {
            float f;
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out f) ? f : 0f;
        }

        static float[] List(string s)
        {
            var l = new List<float>();
            foreach (string p in s.Split(',')) if (p.Length > 0) l.Add(F(p));
            return l.ToArray();
        }
    }
}
