using UnityEngine;

namespace ZERO
{
    /// <summary>Every frame: the light, the sky, the haze and the exposure of the moment, as pure functions of the event
    /// clock (smooth: no exposure jumps), plus where the sun, the body and the explosion are for the shaders.</summary>
    public static class ShaderGlobals
    {
        static readonly int idSunDir = Shader.PropertyToID("_ZSunDir"), idSunCol = Shader.PropertyToID("_ZSunColor");
        static readonly int idPlPos = Shader.PropertyToID("_ZPlasmaPos"), idPlCol = Shader.PropertyToID("_ZPlasmaColor"), idPlDir = Shader.PropertyToID("_ZPlasmaDirV");
        static readonly int idAmbS = Shader.PropertyToID("_ZAmbientSky"), idAmbG = Shader.PropertyToID("_ZAmbientGround");
        static readonly int idFog = Shader.PropertyToID("_ZFogParams");
        static readonly int idMid = Shader.PropertyToID("_ZSkyMid"), idBase = Shader.PropertyToID("_ZBaseGlow");
        static readonly int idHor = Shader.PropertyToID("_ZSkyHorizon"), idZen = Shader.PropertyToID("_ZSkyZenith"), idSkyP = Shader.PropertyToID("_ZSkyParams"), idRed = Shader.PropertyToID("_ZSkyRed");
        static readonly int idSite = Shader.PropertyToID("_ZSite"), idFront = Shader.PropertyToID("_ZFront"), idAst = Shader.PropertyToID("_ZAst");
        static readonly int idTip = Shader.PropertyToID("_ZTipPos"), idAstC = Shader.PropertyToID("_ZAstCenter");
        static readonly int idW2L = Shader.PropertyToID("_ZPlaneW2L"), idSunPL = Shader.PropertyToID("_ZSunDirPL"), idPlPL = Shader.PropertyToID("_ZPlasmaDirPL");
        static readonly int idFillL = Shader.PropertyToID("_ZCabinFillL"), idFillR = Shader.PropertyToID("_ZCabinFillR"), idCabAmb = Shader.PropertyToID("_ZCabinAmb");

        public const float SunAzimuthDeg = 162f, SunElevationDeg = 11f;     // low, south-south-east, behind the left shoulder: the rock is gibbous, the clouds are side-lit
        public static Vector3 SunDir;
        public static float ExposureMul = 1f;       // the ending
        public static float Exposure;               // what the shaders got this frame
        public static Vector3 LightAbs;             // the second light on the flat map (the body, then the explosion)
        public static Color LightColour = Color.white;

        public static void InitConstants()
        {
            Palette.PushToShaders();
            float e = SunElevationDeg * Mathf.Deg2Rad, a = SunAzimuthDeg * Mathf.Deg2Rad;
            SunDir = new Vector3(Mathf.Sin(a) * Mathf.Cos(e), Mathf.Sin(e), Mathf.Cos(a) * Mathf.Cos(e)).normalized;
            Shader.SetGlobalVector("_ZCurve", new Vector4(1f / Schedule.EarthRadius, Schedule.EarthRadius, 0f, 1f));
            Shader.SetGlobalVector("_ZWin", new Vector4(PlaneDef.WindowZ0, PlaneDef.WindowPitch, PlaneDef.WindowY, PlaneDef.WallX));
            // the moon by day, a little west of where the body comes from (twice its real size: the same dream scale as the rock)
            float ma = -59f * Mathf.Deg2Rad, me = 15f * Mathf.Deg2Rad;
            Shader.SetGlobalVector("_ZMoonDir", new Vector4(Mathf.Sin(ma) * Mathf.Cos(me), Mathf.Sin(me), Mathf.Cos(ma) * Mathf.Cos(me), 0.0085f));
            Shader.SetGlobalVector("_ZWinSize", new Vector4(PlaneDef.WindowW, PlaneDef.WindowH, PlaneDef.WindowCorner, PlaneDef.SkinX));
        }

        /// <summary>How much the light of the explosion makes the eye (the camera) stop down: 1 = normal day.</summary>
        public static float Adapt(float T)
        {
            // the burning body already makes the eye stop down in its last half minute (continuous through the contact)
            float since = T - Schedule.ContactTime;
            float e = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(Schedule.EntryTime, Schedule.ContactTime, T));
            float body = since < 0f ? e * e : Mathf.Exp(-since / 20f);
            return 1f / (1f + 4.2f * Schedule.Flash(T) + 1.1f * body);
        }

        /// <summary>0..1: dust and smoke in the high air after the impact (the day goes out).</summary>
        public static float Dust(float T) { return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(Schedule.ContactTime + 50f, Schedule.ContactTime + 250f, T)); }

        /// <summary>Called once per frame after the camera was placed.</summary>
        public static void Tick(float T, Vector3 eyeAbs, Transform plane)
        {
            float since = T - Schedule.ContactTime;
            float flash = Schedule.Flash(T), dust = Dust(T), reentry = Schedule.Reentry(T), burn = Schedule.BodyGlow(T);
            Vector3 eyeU = FloatingOrigin.ToUnity(eyeAbs);

            // ---- the sun: it stays where it is, but the sky between fills with dust ----
            float sunK = 1f - 0.8f * dust;
            Shader.SetGlobalVector(idSunDir, new Vector4(SunDir.x, SunDir.y, SunDir.z, sunK));
            Shader.SetGlobalVector(idSunCol, new Color(1.0f, 0.76f, 0.52f) * 1.85f);

            // ---- the second light: the body while it falls, the explosion afterwards ----
            Vector3 tip = Schedule.AstTip(T);
            float fr = Schedule.FireballRadius(T);
            Vector3 ball = Schedule.Site + Vector3.up * (fr * 0.35f + Mathf.Min(since, 200f) * 320f);
            LightAbs = since < 0f ? tip : Vector3.Lerp(tip, ball, Mathf.Clamp01(since / 2f));
            Color hot = Color.Lerp(Palette.PlasmaGold.linear, Palette.PlasmaWhite.linear, 0.4f);
            Color cooled = Color.Lerp(Palette.PlasmaOrange.linear, Palette.FireRed.linear, 0.25f);
            Color pc = since < 0f ? hot : Color.Lerp(hot, cooled, Mathf.SmoothStep(0f, 1f, since / 50f));
            LightColour = pc;
            // intensity: a star that barely counts, a torch from the entry on, the explosion, then its long red afterglow
            float entry = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(Schedule.EntryTime - 4f, Schedule.ContactTime, T));
            float pI = since < 0f ? 0.02f * burn + 1.1f * entry * entry : Mathf.Lerp(1.1f, 5f, Mathf.Clamp01(since / 0.9f)) * (0.1f + 0.9f * Mathf.Exp(-since / 30f)) + 0.35f;
            Shader.SetGlobalVector(idPlPos, FloatingOrigin.ToUnity(LightAbs));
            Shader.SetGlobalVector(idPlCol, new Vector4(pc.r, pc.g, pc.b, pI));
            Vector3 toL = Planet.Direction(LightAbs, eyeAbs);
            float veil = since < 0f ? 0.25f * burn + 0.9f * entry : 1.2f * (0.25f + 0.75f * Mathf.Exp(-since / 40f));
            Shader.SetGlobalVector(idPlDir, new Vector4(toL.x, toL.y, toL.z, veil));

            // ---- sky and air ----
            Color horizon = Color.Lerp(new Color(0.88f, 0.80f, 0.74f), Palette.DustTan.linear * 0.75f, dust * 0.85f);
            Color zenith = Color.Lerp(new Color(0.045f, 0.13f, 0.46f), new Color(0.10f, 0.09f, 0.12f), dust * 0.9f);
            Color mid = Color.Lerp(new Color(0.34f, 0.50f, 0.86f), new Color(0.36f, 0.26f, 0.20f), dust * 0.9f);
            Shader.SetGlobalVector(idHor, horizon);
            Shader.SetGlobalVector(idZen, zenith);
            Shader.SetGlobalVector(idMid, mid);
            Color red = new Color(1.0f, 0.34f, 0.10f) * 1.25f;
            Shader.SetGlobalVector(idRed, new Vector4(red.r, red.g, red.b, 0.78f * reentry));
            Color aS = Color.Lerp(new Color(0.24f, 0.32f, 0.52f), new Color(0.42f, 0.20f, 0.12f), Mathf.Max(dust * 0.7f, reentry * 0.9f)) * (1f - 0.35f * dust);
            Color aG = Color.Lerp(new Color(0.22f, 0.24f, 0.28f), new Color(0.20f, 0.12f, 0.09f), dust);
            Shader.SetGlobalVector(idAmbS, aS);
            Shader.SetGlobalVector(idAmbG, aG);
            Shader.SetGlobalVector(idFog, new Vector4(1.5e-5f * (1f + 1.8f * dust), 1f / 8200f, 0f, 1f));
            Exposure = 1.04f * Adapt(T) * ExposureMul * CameraFX.Metering;
            float horizonGlow = since < 0f ? 0.35f * entry : Mathf.Clamp01(since / 1.5f) * (0.6f + 0.4f * Mathf.Exp(-since / 60f)) * (1f + 1.2f * dust);
            Shader.SetGlobalVector(idSkyP, new Vector4(1f - 0.35f * dust, horizonGlow, Exposure, 0f));

            // ---- the event for the shaders ----
            Vector3 siteU = FloatingOrigin.ToUnity(Schedule.Site);
            Shader.SetGlobalVector(idSite, new Vector4(siteU.x, siteU.y, siteU.z, since));
            Shader.SetGlobalVector(idFront, new Vector4(Schedule.FrontRadius(T), fr, flash, reentry));
            // the haze around the foot of the explosion glows with its fire (as wide as the base surge seen from here)
            if (since > 0f)
            {
                float dSite = Mathf.Max(Schedule.SiteDistance(T), 1000f);
                float wide = Mathf.Atan((fr * 1.35f + 85f * since) / dSite);
                float g = 1.4f * Mathf.SmoothStep(0f, 1f, since / 4f) * (0.45f + 0.55f * Mathf.Exp(-since / 100f)) * (1f - 0.4f * dust);
                Shader.SetGlobalVector(idBase, new Vector4(g, Mathf.Max(wide, 0.02f), 0.022f, 0f));
            }
            else Shader.SetGlobalVector(idBase, Vector4.zero);
            Vector3 tipU = FloatingOrigin.ToUnity(tip);
            Shader.SetGlobalVector(idAst, new Vector4(tipU.x, tipU.y, tipU.z, burn));
            Shader.SetGlobalVector(idTip, tipU);
            Shader.SetGlobalVector(idAstC, FloatingOrigin.ToUnity(SkyObjects.CentreAbs));

            // ---- the cabin: light comes in through the windows ----
            if (plane != null)
            {
                Shader.SetGlobalMatrix(idW2L, plane.worldToLocalMatrix);
                Quaternion inv = Quaternion.Inverse(plane.rotation);
                Vector3 sPL = inv * SunDir, pPL = inv * toL;
                Shader.SetGlobalVector(idSunPL, new Vector4(sPL.x, sPL.y, sPL.z, 1.25f * sunK));
                // the eye has stopped down for the light outside; the cabin still has to read as lit by the fire
                // (only while the eye is on the cabin: looking out, the window frame stays a dark vignette around the glow)
                float onCabin = Mathf.InverseLerp(1f, 1.9f, CameraFX.Metering);
                float fireBoost = since > 0f ? Mathf.Lerp(1f, Mathf.Min(1.6f / Mathf.Sqrt(Adapt(T)), 3f), Mathf.Clamp01(since / 3f) * onCabin) : 1f;
                float cabinP = since < 0f ? 0.9f * entry * entry : Mathf.Min(pI, 6f) * 1.2f * fireBoost;
                Shader.SetGlobalVector(idPlPL, new Vector4(pPL.x, pPL.y, pPL.z, cabinP));
                Color skyMid = Color.Lerp(horizon, zenith, 0.35f) * (1f - 0.35f * dust) + red * (0.5f * reentry);
                Color fillL = skyMid * 0.34f + new Color(1f, 0.84f, 0.64f) * (0.12f * sunK) + new Color(pc.r, pc.g, pc.b) * (Mathf.Min(pI, 6f) * 0.4f * fireBoost);
                Color fillR = skyMid * 0.30f;
                Shader.SetGlobalVector(idFillL, fillL);
                Shader.SetGlobalVector(idFillR, fillR);
                float fireAmb = since > 0f ? Mathf.Min(pI, 6f) * 0.045f * fireBoost : 0f;
                // the ceiling strip light dims to an emergency glow as the wave comes (its emission scale is the w component)
                float strip = Mathf.Lerp(1f, 0.08f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(Schedule.ContactTime + 150f, Schedule.ContactTime + 230f, T)));
                Shader.SetGlobalVector(idCabAmb, new Vector4(0.045f + pc.r * fireAmb, 0.047f + pc.g * fireAmb, 0.055f + pc.b * fireAmb, strip));
            }
        }
    }
}
