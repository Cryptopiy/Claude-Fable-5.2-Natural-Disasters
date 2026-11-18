using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZERO
{
    /// <summary>The camera operator of the cinematic mode: a list of gaze cues over the event clock (where to look, how
    /// close to the glass, how tight the lens). Each cue holds until the next one blends in. Targets are plane-local
    /// angles (yaw 0 = nose, -90 = straight out of the left window) or things in the world (followed live).</summary>
    public static class CinematicDirector
    {
        public struct Cue
        {
            public float t;          // event time at which this cue is fully reached
            public float blend;      // seconds before t over which the camera turns to it
            public float yaw, pitch; // plane-local angles, or offsets when a target is given
            public Func<Vector3> target;   // a point on the flat map (null: use the angles)
            public float fov, lean;
            public float rate;       // max turn rate deg/s
        }

        static readonly List<Cue> cues = new List<Cue>();

        public static void Clear() { cues.Clear(); }

        /// <summary>Look in a direction given in the aircraft's own frame.</summary>
        public static void Rel(float t, float yaw, float pitch, float fov = 62f, float lean = 0.5f, float blend = 4f, float rate = 12f)
        {
            cues.Add(new Cue { t = t, blend = blend, yaw = yaw, pitch = pitch, fov = fov, lean = lean, rate = rate });
        }

        /// <summary>Look at something in the world (with optional angular offsets to frame it off-centre).</summary>
        public static void At(float t, Func<Vector3> target, float fov = 62f, float lean = 0.8f, float blend = 4f, float yawOff = 0f, float pitchOff = 0f, float rate = 12f)
        {
            cues.Add(new Cue { t = t, blend = blend, target = target, yaw = yawOff, pitch = pitchOff, fov = fov, lean = lean, rate = rate });
        }

        public static void Sort() { cues.Sort((a, b) => a.t.CompareTo(b.t)); }

        static void Dir(in Cue c, Vector3 eyeAbs, Quaternion planeRot, out float yaw, out float pitch)
        {
            if (c.target != null)
            {
                Vector3 d = Quaternion.Inverse(planeRot) * Planet.Direction(c.target(), eyeAbs);
                yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg + c.yaw;
                pitch = Mathf.Asin(Mathf.Clamp(d.y, -1f, 1f)) * Mathf.Rad2Deg + c.pitch;
            }
            else { yaw = c.yaw; pitch = c.pitch; }
            pitch = Mathf.Clamp(pitch, -60f, 70f);
        }

        public static void Evaluate(float T, Vector3 eyeAbs, Quaternion planeRot, out float yaw, out float pitch, out float fov, out float lean, out float maxRate)
        {
            if (cues.Count == 0) { yaw = -88f; pitch = -3f; fov = Config.BaseFov; lean = 0.5f; maxRate = 12f; return; }
            int i = 0;
            while (i < cues.Count - 1 && cues[i + 1].t - cues[i + 1].blend <= T) i++;
            Cue cur = cues[i];
            Dir(cur, eyeAbs, planeRot, out yaw, out pitch); fov = cur.fov; lean = cur.lean; maxRate = cur.rate;
            if (i > 0 && T < cur.t)
            {
                Cue prev = cues[i - 1];
                Dir(prev, eyeAbs, planeRot, out float py, out float pp);
                float w = Mathf.Clamp01((T - (cur.t - cur.blend)) / Mathf.Max(cur.blend, 0.01f));
                w = w * w * w * (w * (w * 6f - 15f) + 10f);
                yaw = py + Mathf.DeltaAngle(py, yaw) * w;
                pitch = Mathf.Lerp(pp, pitch, w);
                fov = Mathf.Lerp(prev.fov, cur.fov, w);
                lean = Mathf.Lerp(prev.lean, cur.lean, w);
            }
        }
    }
}
