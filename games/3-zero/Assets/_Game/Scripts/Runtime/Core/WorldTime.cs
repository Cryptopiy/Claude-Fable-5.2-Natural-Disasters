using UnityEngine;

namespace ZERO
{
    /// <summary>The event clock: seconds since the flight began. It runs in real time, always.
    /// Every world animation and shader (_WorldTime) reads this clock.</summary>
    public static class WorldTime
    {
        public static double Now;          // event clock T (seconds)
        public static double Real;         // the same clock (kept for the harness: time since the walk began in older tools)
        public static bool Running;

        static readonly int idWorld = Shader.PropertyToID("_WorldTime");
        static readonly int idReal = Shader.PropertyToID("_RealTime");

        public static void Reset(double t) { Real = t; Now = t; Push(); }

        public static void Advance(float dt)
        {
            if (Running) { Real += dt; Now += dt; }
            Push();
        }

        static void Push()
        {
            Shader.SetGlobalFloat(idWorld, (float)Now);
            Shader.SetGlobalFloat(idReal, (float)(Real % 3600.0));
        }
    }

    /// <summary>Floating origin: the outside world lives under one root that is shifted so the camera stays near (0,0,0)
    /// in x and z. absolute flat map position = unity + Offset. Shaders get _OriginOffset.</summary>
    public static class FloatingOrigin
    {
        public static Vector3 Offset;
        public static Transform Root;
        public static int Shifts;
        static readonly int id = Shader.PropertyToID("_OriginOffset");

        public static void Init(Transform root) { Root = root; Offset = Vector3.zero; Shifts = 0; Apply(); }

        public static Vector3 ToUnity(Vector3 abs) { return abs - Offset; }
        public static Vector3 ToAbs(Vector3 unity) { return unity + Offset; }

        /// <summary>Re-centres when the aircraft is farther than the shift distance from the origin. Returns true on a shift.</summary>
        public static bool Maybe(Vector3 abs)
        {
            Vector3 u = abs - Offset;
            if (u.x * u.x + u.z * u.z < Config.OriginShiftDistance * Config.OriginShiftDistance) return false;
            Set(abs); Shifts++;
            return true;
        }

        public static void Set(Vector3 abs)
        {
            // whole 64 m steps keep world-space noise stable across a shift
            Offset = new Vector3(Mathf.Round(abs.x / 64f) * 64f, 0f, Mathf.Round(abs.z / 64f) * 64f);
            Apply();
        }

        static void Apply()
        {
            if (Root != null) Root.position = -Offset;
            Shader.SetGlobalVector(id, new Vector4(Offset.x, Offset.y, Offset.z, 0f));
        }
    }

    /// <summary>The planet: where a point of the flat map is seen from a camera (the same mapping as ZCurve in the shaders).</summary>
    public static class Planet
    {
        public static Vector3 Apparent(Vector3 p, Vector3 cam)
        {
            float dx = p.x - cam.x, dz = p.z - cam.z;
            float d = Mathf.Sqrt(dx * dx + dz * dz);
            if (d < 1f) return p;
            double R = Schedule.EarthRadius, th = d / R;
            double s = System.Math.Sin(th), c = System.Math.Cos(th), sh = System.Math.Sin(th * 0.5);
            double hr = (R + p.y) * s;
            float y = (float)(p.y * c - 2.0 * R * sh * sh);
            return new Vector3(cam.x + dx / d * (float)hr, y, cam.z + dz / d * (float)hr);
        }

        /// <summary>Unit direction in which a flat-map point is seen from the camera.</summary>
        public static Vector3 Direction(Vector3 p, Vector3 cam) { return (Apparent(p, cam) - cam).normalized; }
    }

    /// <summary>Piecewise curve: pairs (t, value), smooth between keys.</summary>
    public static class Curve
    {
        public static float Eval(float s, float[] k)
        {
            int n = k.Length / 2;
            if (s <= k[0]) return k[1];
            if (s >= k[(n - 1) * 2]) return k[(n - 1) * 2 + 1];
            int i = 0;
            while (i < n - 2 && k[(i + 1) * 2] < s) i++;
            float t = Mathf.InverseLerp(k[i * 2], k[(i + 1) * 2], s);
            t = t * t * (3f - 2f * t);
            return Mathf.Lerp(k[i * 2 + 1], k[(i + 1) * 2 + 1], t);
        }

        public static Color Eval(float s, float[] ks, Color[] cs)
        {
            int n = ks.Length;
            if (s <= ks[0]) return cs[0];
            if (s >= ks[n - 1]) return cs[n - 1];
            int i = 0;
            while (i < n - 2 && ks[i + 1] < s) i++;
            float t = Mathf.InverseLerp(ks[i], ks[i + 1], s);
            t = t * t * (3f - 2f * t);
            return Color.Lerp(cs[i], cs[i + 1], t);
        }
    }
}
