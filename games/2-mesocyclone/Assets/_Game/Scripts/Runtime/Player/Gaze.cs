using System.Collections.Generic;
using UnityEngine;

namespace MESOCYCLONE
{
    /// <summary>Gaze cues of the cinematic mode: where the camera operator looks, and when. Each beat registers
    /// its cues (story time, target, FOV, how lazily to get there). Targets that move are tracked every frame.</summary>
    public static class Gaze
    {
        public enum Kind { Local, RoadPoint, Tornado, Storm, Shelf }

        struct Cue
        {
            public float t; public Kind kind;
            public float a, b, c;            // Local: yaw, pitch.  RoadPoint: chainage, lateral, height.  Tornado/Storm/Shelf: height
            public float yawOff, pitchOff, fov, smooth;
        }

        static readonly List<Cue> cues = new List<Cue>(128);
        static bool sorted;

        public static void Clear() { cues.Clear(); sorted = false; }

        /// <summary>Look in a fixed direction relative to the truck (yaw right +, pitch up +).</summary>
        public static void Local(float t, float yaw, float pitch, float fov = Config.FovDefault, float smooth = 1.6f)
        { Add(new Cue { t = t, kind = Kind.Local, a = yaw, b = pitch, fov = fov, smooth = smooth }); }

        /// <summary>Look at a fixed point of the land given in road coordinates.</summary>
        public static void Road(float t, float chainage, float lateral, float height, float fov = Config.FovDefault, float smooth = 1.6f,
            float yawOff = 0f, float pitchOff = 0f)
        { Add(new Cue { t = t, kind = Kind.RoadPoint, a = chainage, b = lateral, c = height, fov = fov, smooth = smooth, yawOff = yawOff, pitchOff = pitchOff }); }

        public static void Tornado(float t, float height, float fov = Config.FovDefault, float smooth = 1.6f, float yawOff = 0f, float pitchOff = 0f)
        { Add(new Cue { t = t, kind = Kind.Tornado, c = height, fov = fov, smooth = smooth, yawOff = yawOff, pitchOff = pitchOff }); }

        public static void Storm(float t, float height, float fov = Config.FovDefault, float smooth = 2.2f, float yawOff = 0f, float pitchOff = 0f)
        { Add(new Cue { t = t, kind = Kind.Storm, c = height, fov = fov, smooth = smooth, yawOff = yawOff, pitchOff = pitchOff }); }

        /// <summary>Look at the shelf cloud's leading edge straight up the road.</summary>
        public static void Shelf(float t, float height, float fov = Config.FovDefault, float smooth = 2.2f, float yawOff = 0f, float pitchOff = 0f)
        { Add(new Cue { t = t, kind = Kind.Shelf, c = height, fov = fov, smooth = smooth, yawOff = yawOff, pitchOff = pitchOff }); }

        static void Add(Cue c) { cues.Add(c); sorted = false; }

        /// <summary>Desired truck-relative yaw and pitch (degrees) for story time T.</summary>
        public static bool Evaluate(float T, Vector3 eyeLocal, out float yaw, out float pitch, out float fov, out float smooth)
        {
            yaw = 0f; pitch = 0f; fov = Config.FovDefault; smooth = 1.6f;
            if (cues.Count == 0) return false;
            if (!sorted) { cues.Sort((x, y) => x.t.CompareTo(y.t)); sorted = true; }
            int idx = 0;
            for (int i = 0; i < cues.Count; i++) { if (cues[i].t <= T) idx = i; else break; }
            Cue c = cues[idx];
            fov = c.fov; smooth = c.smooth;
            if (c.kind == Kind.Local) { yaw = c.a; pitch = c.b; return true; }

            Vector3 p;
            switch (c.kind)
            {
                case Kind.RoadPoint: p = Director.RoadToLocal(c.a, c.b, c.c); break;
                case Kind.Tornado: p = Director.TornadoLocal(c.c); break;
                case Kind.Storm: p = Director.StormLocal(c.c); break;
                default:
                    {
                        // the point of the shelf's leading edge that lies on the road line
                        double sShelf = Director.S.shelfZ;            // z is chainage to within metres on this road
                        p = Director.RoadToLocal(System.Math.Max(sShelf, Director.TruckS + 400.0), 0.0, c.c); break;
                    }
            }
            Vector3 d = p - eyeLocal;
            float worldYaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            yaw = Mathf.DeltaAngle(Director.HeadingDeg, worldYaw) + c.yawOff;
            pitch = Mathf.Atan2(d.y, Mathf.Sqrt(d.x * d.x + d.z * d.z)) * Mathf.Rad2Deg + c.pitchOff;
            yaw = Mathf.Clamp(yaw, -Config.YawLimit, Config.YawLimit);
            pitch = Mathf.Clamp(pitch, Config.PitchMin, Config.PitchMax);
            return true;
        }
    }
}
