using UnityEngine;

namespace CASCADE
{
    /// <summary>
    /// Staging: the scalar state of the catastrophe as pure functions of the event clocks. Shaders receive these values
    /// as globals and do only the spatial shaping, so C# (camera, sound, lighting) and HLSL always agree.
    /// </summary>
    public static class Sim
    {
        // ---------- geography ----------
        public static readonly Vector3 ConeSummit = Config.ConeCenter + new Vector3(0f, Config.ConeHeight, 0f);
        public const float SlideAzimuth = -20f;                    // degrees from +x towards +z: the collapsing flank faces east-south-east
        public static readonly Vector2 SlideDir = new Vector2(Mathf.Cos(SlideAzimuth * Mathf.Deg2Rad), Mathf.Sin(SlideAzimuth * Mathf.Deg2Rad));
        /// <summary>Where the slide enters the water: origin of the wave fronts (x, z).</summary>
        public static readonly Vector2 SplashXZ = new Vector2(Config.ConeCenter.x + Config.ConeBaseRadius * SlideDir.x, Config.ConeCenter.z + Config.ConeBaseRadius * SlideDir.y);
        public static readonly Vector3 Harbour = new Vector3(-55f, 0f, -135f);
        public static readonly Vector3 HeadlandTip = new Vector3(-930f, 40f, 650f);
        public static readonly Vector2 WaveAim = new Vector2(-150f, 150f);     // centre of the wave's main lobe (the bay)
        public static readonly float WaveReach = Vector2.Distance(SplashXZ, new Vector2(-20f, -130f));   // front radius at harbour impact
        public static readonly Vector2 WaveAimDir = (WaveAim - SplashXZ).normalized;

        // ---------- sun ----------
        /// <summary>Direction TO the sun at story time t.</summary>
        public static Vector3 SunDir(float t)
        {
            float elev = Mathf.Lerp(Config.SunElevStart, Config.SunElevEnd, MathX.Smooth(0f, 420f, t)) * Mathf.Deg2Rad;
            // village -> cone is -x (west); the sun stands SunAzimuthFromCone towards the south (-z) of it
            float a = Config.SunAzimuthFromCone * Mathf.Deg2Rad;
            Vector3 flat = new Vector3(-Mathf.Cos(a), 0f, -Mathf.Sin(a));
            return (flat * Mathf.Cos(elev) + Vector3.up * Mathf.Sin(elev)).normalized;
        }

        // ---------- eruption (te = Story.T(Ev.Eruption)) ----------
        static readonly float[] ctX = { 0f, 4f, 12f, 38f, 68f, 98f, 140f, 400f };
        static readonly float[] ctY = { 1800f, 3300f, 5000f, 10500f, 20000f, 35000f, 37000f, 38000f };   // a fast first burst
        public static float ColumnTop(float te) { return te <= 0f ? Config.ConeHeight : MathX.Curve(ctX, ctY, te); }

        static readonly float[] urX = { 55f, 62f, 85f, 120f, 200f, 420f };
        static readonly float[] urY = { 0f, 2500f, 12000f, 30000f, 60000f, 95000f };
        public static float UmbrellaRadius(float te) { return te <= 55f ? 0f : MathX.Curve(urX, urY, te); }
        public const float UmbrellaBase = 17000f;
        public const float UmbrellaTop = 26000f;

        /// <summary>Shock ring radius (m) from the vent; 0 before the blast.</summary>
        public static float ShockRadius(float te) { return te <= 0f ? 0f : te * Config.SoundSpeed; }

        /// <summary>0 = clear day, 1 = the ash umbrella has taken the daylight (drives sun dimming, sky, fog, grade).</summary>
        static readonly float[] dkX = { 40f, 70f, 95f, 118f, 150f, 260f };
        static readonly float[] dkY = { 0f, 0.22f, 0.55f, 0.82f, 0.93f, 1f };
        public static float Darkness(float te) { return te <= 40f ? 0f : MathX.Curve(dkX, dkY, te); }

        /// <summary>Ash fall intensity 0..1 near the camera.</summary>
        public static float Ashfall(float te) { return MathX.Smooth(Config.AshfallStart, Config.AshfallStart + 40f, te); }

        /// <summary>Lamps and windows: 0 off .. 1 on (staggered groups use offsets of this clock).</summary>
        public static float Lamps(float te, float groupDelay) { return MathX.Smooth(Config.LampsOn + groupDelay, Config.LampsOn + groupDelay + 1.2f, te); }

        // ---------- collapse (tc = Story.T(Ev.Collapse)) ----------
        /// <summary>Slide progress 0..1 with a slow, heavy start (ease in) and a long run-out.</summary>
        public static float Slide(float tc)
        {
            float x = MathX.Saturate(tc / Config.SlideDuration);
            return x * x * (3f - 2f * x) * 0.6f + x * x * x * 0.4f;
        }
        public static float Scar(float tc) { return MathX.Smooth(-1.5f, 5f, tc); }
        /// <summary>Splash dome height (m) at the entry point.</summary>
        public static float SplashHeight(float tc)
        {
            float t = tc - Config.SplashAt;
            if (t <= 0f) return 0f;
            return 620f * MathX.Smooth(0f, 9f, t) * (1f - MathX.Smooth(14f, 40f, t));
        }

        // ---------- drawdown (td = Story.T(Ev.Drawdown)) and wave (tw = Story.T(Ev.Wave)) ----------
        /// <summary>How far the sea has dropped at the harbour, metres (positive = lower).</summary>
        public static float Drawdown(float td, float tw)
        {
            float down = Config.DrawdownDepth * MathX.Smooth(0f, Config.DrawdownDuration, td);
            float refill = MathX.Smooth(Config.WaveCross - 9f, Config.WaveCross + 3f, tw);     // the wall arrives
            return down * (1f - refill);
        }

        /// <summary>Radius of the leading crest from the splash origin, metres.</summary>
        public static float WaveRadius(float tw) { return tw <= 0f ? 0f : WaveReach * tw / Config.WaveCross; }

        /// <summary>The front flattens as it crosses the caldera: its centre of curvature retreats behind the cone (metres).</summary>
        public static float WaveFlatten(float tw) { return 6500f * MathX.Smooth(4f, 70f, tw); }
        public static Vector2 WaveCentre(float tw) { return SplashXZ - WaveAimDir * WaveFlatten(tw); }
        public static float WaveFrontRadius(float tw) { return WaveRadius(tw) + WaveFlatten(tw); }
        /// <summary>Wave clock at which the leading crest reaches a point (x, z).</summary>
        public static float WaveArrival(Vector2 p)
        {
            float lo = 0f, hi = 400f;
            for (int i = 0; i < 26; i++)
            {
                float m = 0.5f * (lo + hi);
                if (WaveFrontRadius(m) - Vector2.Distance(p, WaveCentre(m)) >= 0f) hi = m; else lo = m;
            }
            return hi;
        }

        static readonly float[] waX = { 0f, 8f, 20f, 40f, 58f, 72f, 82f, 90f, 100f, 130f };
        static readonly float[] waY = { 0f, 12f, 95f, 205f, 310f, 320f, 250f, 175f, 120f, 50f };
        /// <summary>Crest height of the leading wave above the undisturbed sea, metres (open water, main lobe).</summary>
        public static float WaveHeight(float tw) { return tw <= 0f ? 0f : MathX.Curve(waX, waY, tw); }

        /// <summary>Residual flood level in the bay after the impact (metres above sea level), sloshing down.</summary>
        public static float Flood(float tw)
        {
            float t = tw - Config.WaveCross;
            if (t <= 0f) return 0f;
            float second = 26f * Story.Bump(t - Config.WaveSecondDelay + 8f, 30f);
            return 34f * MathX.Smooth(0f, 8f, t) * (1f - 0.75f * MathX.Smooth(12f, 70f, t)) + second;
        }

        /// <summary>Point on the leading crest closest to the eye (for the gaze and for sound panning).</summary>
        public static Vector3 WaveCrestPoint(float tw, Vector3 eye)
        {
            float r = WaveFrontRadius(tw);
            Vector2 c0 = WaveCentre(tw);
            Vector2 d = new Vector2(eye.x - c0.x, eye.z - c0.y);
            float len = d.magnitude;
            if (len < 1f) return new Vector3(SplashXZ.x, 0f, SplashXZ.y);
            Vector2 p = c0 + d / len * Mathf.Min(r, len - 40f);
            return new Vector3(p.x, WaveHeight(tw) * 0.75f, p.y);
        }

        // ---------- ash storm and ending ----------
        /// <summary>Visibility in metres (mean free path of the ash fog near the ground).</summary>
        public static float StormDensity(float tStorm, float tG1, float tG2, float tSilence)
        {
            float storm = MathX.Smooth(0f, Config.StormCloseIn, tStorm);
            float g = Mathf.Max(GlimpseWindow(tG1), GlimpseWindow(tG2));
            float clear = MathX.Smooth(0f, Config.SilenceClear, tSilence);
            return storm * (1f - 0.985f * g) * (1f - clear);
        }
        public static float GlimpseWindow(float t)
        {
            if (t <= 0f || t >= Config.GlimpseDuration) return 0f;
            return MathX.Smooth(0f, 4.5f, t) * (1f - MathX.Smooth(Config.GlimpseDuration - 5f, Config.GlimpseDuration, t));
        }
    }
}
