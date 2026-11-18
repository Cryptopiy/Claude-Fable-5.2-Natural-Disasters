using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace MESOCYCLONE
{
    /// <summary>A keyframed scalar: pairs (time, value), eased between keys. No allocation when evaluated.
    /// In development builds a file "tune.txt" next to the executable can override any named curve or constant
    /// (name=t:v,t:v,...  or  name=value) so a look can be tuned without rebuilding the player.</summary>
    public sealed class Curve
    {
        public enum Ease { Smooth, Linear, Step }
        float[] kv;
        readonly Ease ease;
        int hint;

        Curve(Ease ease, string name, float[] keysAndValues)
        {
            this.ease = ease;
            kv = Tune.Keys(name, keysAndValues);
        }

        /// <summary>Smooth (eased) interpolation between keys.</summary>
        public Curve(string name, params float[] keysAndValues) : this(Ease.Smooth, name, keysAndValues) { }
        public static Curve Linear(string name, params float[] keysAndValues) => new Curve(Ease.Linear, name, keysAndValues);
        public static Curve Stepped(string name, params float[] keysAndValues) => new Curve(Ease.Step, name, keysAndValues);

        public float Eval(float t)
        {
            int n = kv.Length / 2;
            if (t <= kv[0]) return kv[1];
            if (t >= kv[(n - 1) * 2]) return kv[(n - 1) * 2 + 1];
            int i = hint;
            if (i < 0 || i >= n - 1 || t < kv[i * 2]) i = 0;
            while (i < n - 2 && t >= kv[(i + 1) * 2]) i++;
            hint = i;
            float t0 = kv[i * 2], t1 = kv[(i + 1) * 2];
            float v0 = kv[i * 2 + 1], v1 = kv[(i + 1) * 2 + 1];
            if (ease == Ease.Step) return v0;
            float u = (t - t0) / (t1 - t0);
            if (ease == Ease.Smooth) u = u * u * (3f - 2f * u);
            return v0 + (v1 - v0) * u;
        }
    }

    public static class Tune
    {
        static Dictionary<string, string> map;

        static void Load()
        {
            map = new Dictionary<string, string>();
            if (!Debug.isDebugBuild) return;
            try
            {
                string path = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "tune.txt");
                if (!File.Exists(path)) return;
                foreach (string raw in File.ReadAllLines(path))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;
                    int eq = line.IndexOf('=');
                    if (eq > 0) map[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                }
                Debug.Log("[Tune] loaded " + map.Count + " overrides from " + path);
            }
            catch (System.Exception e) { Debug.LogWarning("[Tune] " + e.Message); }
        }

        public static float F(string name, float def)
        {
            if (map == null) Load();
            if (map.TryGetValue(name, out string s) &&
                float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v)) return v;
            return def;
        }

        public static float[] Keys(string name, float[] def)
        {
            if (map == null) Load();
            if (!map.TryGetValue(name, out string s)) return def;
            string[] parts = s.Split(',');
            var r = new float[parts.Length * 2];
            for (int i = 0; i < parts.Length; i++)
            {
                string[] p = parts[i].Split(':');
                if (p.Length != 2 ||
                    !float.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out r[i * 2]) ||
                    !float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out r[i * 2 + 1]))
                    return def;
            }
            return r;
        }
    }
}
