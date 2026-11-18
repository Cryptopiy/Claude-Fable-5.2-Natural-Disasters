using System;
using System.Collections.Generic;
using UnityEngine;

namespace CASCADE
{
    /// <summary>
    /// Gaze choreography for the cinematic mode: where the operator looks and when (cinematic clock, seconds).
    /// Each beat contributes its cues (Beats/BeatNN_*.cs). A cue stays active until the next one; the rig follows
    /// with a critically damped spring (cue.smooth seconds). The lens (cue.fov, vertical degrees) follows much more slowly
    /// (PlayerRig: a 4 s spring capped at 3 deg/s, so a 15-degree change takes about 8 s); a seek or a QA still snaps to it.
    /// </summary>
    public static class Gaze
    {
        public struct Cue
        {
            public float t; public int kind; public Vector3 a; public float fov, smooth, drift;
            public Func<Vector3, Vector3> dyn;
        }

        const int Ahead = 0, Rel = 1, World = 2, Dyn = 3;
        /// <summary>An Ahead cue looks along the path direction this far ahead of the eye (HouseGen scores its quay frames with it).</summary>
        public const float AheadLead = 9f;
        static Cue[] cues;
        static int last;
        public static Vector3 KeeperWindow, Beacon, Chapel;
        public static bool HaveLandmarks;

        /// <summary>Real positions from the builders (keeper's window, lighthouse lantern, chapel).</summary>
        public static void SetLandmarks(Vector3 window, Vector3 beaconPos, Vector3 chapelPos)
        {
            KeeperWindow = window; Beacon = beaconPos; Chapel = chapelPos; HaveLandmarks = true; cues = null; last = 0;
        }

        // ---- cue constructors ----
        /// <summary>Look along the path (pitch in degrees).</summary>
        public static Cue A(float t, float pitch, float fov, float smooth, float drift = 0f) { return new Cue { t = t, kind = Ahead, a = new Vector3(0f, pitch, AheadLead), fov = fov, smooth = smooth, drift = drift }; }
        /// <summary>Look relative to the walking direction (yaw offset to the right, pitch).</summary>
        public static Cue R(float t, float yawOff, float pitch, float fov, float smooth, float drift = 0f) { return new Cue { t = t, kind = Rel, a = new Vector3(yawOff, pitch, 0f), fov = fov, smooth = smooth, drift = drift }; }
        /// <summary>Look at a fixed world point.</summary>
        public static Cue W(float t, Vector3 p, float fov, float smooth, float drift = 0f) { return new Cue { t = t, kind = World, a = p, fov = fov, smooth = smooth, drift = drift }; }
        /// <summary>Look at a moving target (function of the eye position).</summary>
        public static Cue D(float t, Func<Vector3, Vector3> f, float fov, float smooth, float drift = 0f) { return new Cue { t = t, kind = Dyn, dyn = f, fov = fov, smooth = smooth, drift = drift }; }
        /// <summary>
        /// Look towards a fixed world point (same yaw as W), but never steeper than minPitch (degrees, negative = down): a target
        /// far below the eye would otherwise tip the frame so far down that the sky and the cone leave it.
        /// </summary>
        public static Cue WFloor(float t, Vector3 p, float minPitch, float fov, float smooth, float drift = 0f)
        {
            float k = Mathf.Tan(minPitch * Mathf.Deg2Rad);
            return D(t, eye =>
            {
                Vector3 d = p - eye;
                d.y = Mathf.Max(d.y, Mathf.Sqrt(d.x * d.x + d.z * d.z) * k);
                return eye + d;
            }, fov, smooth, drift);
        }

        // ---- moving targets ----
        public static Vector3 ColumnLook(Vector3 eye)
        {
            // follow the column upward, but never beyond 79 degrees of pitch
            float top = Sim.ColumnTop(Story.T(Ev.Eruption));
            float frac = Mathf.Lerp(0.55f, 0.86f, MathX.Smooth(40f, 75f, Story.T(Ev.Eruption)));
            Vector3 p = new Vector3(Config.ConeCenter.x, Mathf.Lerp(Config.ConeHeight, top, frac), Config.ConeCenter.z);
            float yaw, pitch;
            MathX.YawPitchFromDir((p - eye).normalized, out yaw, out pitch);
            return eye + MathX.DirFromYawPitch(yaw, Mathf.Min(pitch, 79f)) * 1000f;
        }
        public static Vector3 UmbrellaLook(Vector3 eye)
        {
            Vector3 toCone = Config.ConeCenter - eye; toCone.y = 0f; toCone.Normalize();
            float yaw = Mathf.Atan2(toCone.x, toCone.z) * Mathf.Rad2Deg;
            // first the whole stem with the summit at the bottom of the frame, then up until the lid is the frame
            float pitch = Mathf.Lerp(49f, 68f, MathX.Smooth(74f, 96f, Story.T(Ev.Eruption)));
            return eye + MathX.DirFromYawPitch(yaw, pitch) * 1000f;
        }
        public static Vector3 SunLook(Vector3 eye)
        {
            // between the sun and the column, a little above the sun: the disc, the stem and the advancing rim in one frame
            Vector3 toSun = Sim.SunDir(Story.Time);
            Vector3 toCol = Config.ConeCenter + Vector3.up * 9000f - eye;
            float ys, ps, yc, pc;
            MathX.YawPitchFromDir(toSun, out ys, out ps);
            MathX.YawPitchFromDir(toCol.normalized, out yc, out pc);
            return eye + MathX.DirFromYawPitch(Mathf.LerpAngle(ys, yc, 0.42f), ps + 9f) * 1000f;
        }

        public static Vector3 SlideLook(Vector3 eye)
        {
            float k = Sim.Slide(Story.T(Ev.Collapse));
            Vector2 f = Sim.SlideDir;
            Vector3 top = Config.ConeCenter + new Vector3(f.x * 500f, 1150f, f.y * 500f);
            Vector3 foot = new Vector3(Sim.SplashXZ.x, 160f, Sim.SplashXZ.y);
            return Vector3.Lerp(top, foot, k);
        }
        public static Vector3 GullLook(Vector3 eye)
        {
            // the flock: up from the quay and out towards the cone, shrinking
            float tg = Mathf.Max(Story.T(Ev.Gulls), 0f);
            return new Vector3(-24f, 3f, -70f) + new Vector3(-0.72f, 0f, 0.67f) * (13f * tg) + Vector3.up * (4f + 22f * (1f - Mathf.Exp(-tg * 0.4f)) + 1.4f * tg);
        }
        public static Vector3 WaveLook(Vector3 eye) { return Sim.WaveCrestPoint(Story.T(Ev.Wave), eye); }
        /// <summary>The crest, 6 degrees to the left of centre (a lamp post on the right third, not across the cone).</summary>
        public static Vector3 WaveLookLeft(Vector3 eye)
        {
            float y, p;
            MathX.YawPitchFromDir((Sim.WaveCrestPoint(Story.T(Ev.Wave), eye) - eye).normalized, out y, out p);
            return eye + MathX.DirFromYawPitch(y + 6f, p) * 1000f;
        }
        /// <summary>M3: the harbour and the lower village with the wall behind them, in one frame.</summary>
        public static Vector3 WaveVillageLook(Vector3 eye)
        {
            Vector3 w = Sim.WaveCrestPoint(Story.T(Ev.Wave), eye);
            Vector3 v = Sim.Harbour + new Vector3(40f, 45f, 70f);
            float yw, pw, yv, pv;
            MathX.YawPitchFromDir((w - eye).normalized, out yw, out pw);
            MathX.YawPitchFromDir((v - eye).normalized, out yv, out pv);
            float yaw = yv + Mathf.Clamp(Mathf.DeltaAngle(yv, yw), -25f, 25f);
            return eye + MathX.DirFromYawPitch(yaw, Mathf.Lerp(pv, pw, 0.6f)) * 1000f;
        }
        /// <summary>M5: the lit window on a third of the frame, the caldera and the plume on the other side.</summary>
        public static Vector3 WindowLook(Vector3 eye)
        {
            float yw, pw, yc, pc;
            MathX.YawPitchFromDir((KeeperWindow - eye).normalized, out yw, out pw);
            MathX.YawPitchFromDir((Config.ConeCenter - eye).normalized, out yc, out pc);
            float side = Mathf.Sign(Mathf.DeltaAngle(yw, yc));
            return eye + MathX.DirFromYawPitch(yw + side * 13f, pw + 2f) * 100f;
        }
        public static Vector3 LineLook(Vector3 eye) { return new Vector3(Sim.SplashXZ.x + 500f, 10f, Sim.SplashXZ.y + 200f); }

        static void Build()
        {
            var l = new List<Cue>(72);
            Beat01_Harbour.Cues(l);
            Beat02_Climb.Cues(l);
            Beat03_Column.Cues(l);
            Beat04_Drawdown.Cues(l);
            Beat05_Wall.Cues(l);
            Beat06_AshFire.Cues(l);
            Beat07_Silence.Cues(l);
            l.Sort((x, y) => x.t.CompareTo(y.t));
            cues = l.ToArray();
            last = 0;
        }

        public static void Eval(float t, float s, Vector3 eye, out Vector3 dir, out float fov, out float smooth, out float drift)
        {
            if (cues == null) Build();
            if (last >= cues.Length || cues[last].t > t) last = 0;
            while (last < cues.Length - 1 && cues[last + 1].t <= t) last++;
            Cue c = cues[last];
            fov = c.fov; smooth = c.smooth; drift = c.drift;
            Vector3 f = PathDef.FlatDir(s);
            float baseYaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
            switch (c.kind)
            {
                case Ahead:
                {
                    Vector3 fa = PathDef.FlatDir(s + c.a.z);
                    float yaw = Mathf.Atan2(fa.x, fa.z) * Mathf.Rad2Deg;
                    dir = MathX.DirFromYawPitch(yaw, c.a.y + Mathf.Atan(PathDef.Slope(s + 4f)) * Mathf.Rad2Deg * 0.35f);
                    break;
                }
                case Rel: dir = MathX.DirFromYawPitch(baseYaw + c.a.x, c.a.y); break;
                case World: dir = (c.a - eye).normalized; break;
                default: dir = (c.dyn(eye) - eye).normalized; break;
            }
        }
    }
}
