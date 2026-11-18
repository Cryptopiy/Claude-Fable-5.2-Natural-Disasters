using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace CASCADE
{
    /// <summary>
    /// Light, sky, fog and grade as pure functions of the story clocks. The camera EV is fixed; the change of the day comes
    /// from light and fog, plus a slow post-exposure ladder (the eye adapting to ash, storm and night) that only follows
    /// smooth clocks, so it never pumps. Also publishes the globals the hand-written shaders use, so they stay consistent with HDRP.
    /// </summary>
    public class EnvDirector
    {
        public EnvRig rig;

        // state shared with other systems (read after Apply)
        public static Vector3 SunDir;          // to the sun (the custom shaders always shade with this one)
        public static Vector3 KeyDir;          // to the HDRP key light: the sun by day, the burning flank in the ash night
        public static Vector3 FireDir;         // to the glow of the burning flank, as seen from the camera
        public static Color SunColorLux;       // surface sun colour * lux (linear), as the custom shaders receive it
        public static Color SkyZenith, SkyHorizon;   // nits, linear: what custom shaders use for ambient and reflections
        public static Color FogColor;          // nits
        public static Color StormFog;          // nits, the storm veil of the plume pass
        public static Color Strip;             // nits of the daylight strip under the umbrella edge (black when it does not apply)
        public static float Dark, Storm, Night;
        public static float AshGrey;           // 0 -> 1 over the 35 s after the collapse: bronze haze becomes neutral ash
        public static float BoltFlash;         // 0..1 how much the current lightning bolt lights the world (shape, strength and BoltGain included)
        public static float BoltGain;          // 0..1 exposure compensation of lightning: other systems' bolt lights multiply by it (already in BoltFlash)
        public static float LadderEV;          // stops of post exposure this frame (the exposure ladder): practicals take part of it back

        static readonly int idSunDir = Shader.PropertyToID("_CSunDir");
        static readonly int idSunCol = Shader.PropertyToID("_CSunColor");
        static readonly int idZen = Shader.PropertyToID("_CSkyZenith");
        static readonly int idHor = Shader.PropertyToID("_CSkyHorizon");
        static readonly int idFog = Shader.PropertyToID("_CFogColor");
        static readonly int idTime = Shader.PropertyToID("_CTime");
        static readonly int idFrame = Shader.PropertyToID("_CFrameIdx");
        static readonly int idClocksA = Shader.PropertyToID("_CClocksA");   // eruption, collapse, drawdown, wave
        static readonly int idClocksB = Shader.PropertyToID("_CClocksB");   // storm, silence, arrival, tremor level
        static readonly int idState = Shader.PropertyToID("_CState");       // dark, storm, night, ashfall
        static readonly int idCone = Shader.PropertyToID("_CCone");         // cone centre xyz, height
        static readonly int idSunRaw = Shader.PropertyToID("_CSunRaw");
        static readonly int idPlFog = Shader.PropertyToID("_PlFog");
        static readonly int idStrip = Shader.PropertyToID("_CStrip");
        static readonly int idStrip2 = Shader.PropertyToID("_CStrip2");

        // ---- sun colours: a gold low morning sun on the whitewash against the cool sky fill, then bronze and brown ----
        static readonly Color DaySun = new Color(1f, 0.87f, 0.71f);
        static readonly Color HighSun = new Color(1f, 0.94f, 0.85f);
        static readonly Color BronzeSun = new Color(1f, 0.66f, 0.36f);
        static readonly Color BrownSun = new Color(0.9f, 0.42f, 0.2f);

        // ---- night key: the burning flank replaces the dead sun as the shadow-casting light (HDRP only) ----
        const float FireElevDeg = 13f;         // apparent height of the glowing flank above the horizon
        const float KeySwing = 25f;            // s after the storm begins: the swing ends before the first clearing (28 s)
        const float KeyLuxStorm = 3500f;       // fire key in the storm ...
        const float KeyLuxCalm = 5000f;        // ... and once the air clears
        const float KeyFogStorm = 0.45f;       // volumetric dimmer of the key inside the storm, so the fog does not glow orange end to end
        // the old ember glow, still what the custom shaders get through _CSunColor (cone, water, plume, flakes keep their tuning)
        const float GlowLuxStorm = 1600f, GlowLuxCalm = 2800f;

        // ---- lightning: one relative pulse for HDRP sky/ambient, the fog and the custom globals ----
        // Photosensitivity: Tools/flash_check.py, largest mean-luminance change within 150 ms must stay <= 0.085.
        // A relative boost k of all light gives about 0.1 * ((1 + k)^0.9 - 1) of display change at the dark beats' mean 0.1.
        // The exposure ladder raises that display step too (x1.25 for +0.6 EV in B5, x1.9 for +1.3 EV in the storm, measured
        // on the stills through ACES), so the relative flash shrinks by 2^-(BoltExpComp * lift): the step stays near the
        // 0.042 worst case measured before the ladder (QA/flash.csv, t 327).
        const float BoltAmbient = 0.55f;       // HDRP gradient sky: ambient probe of surfaces and the volumetric fog's in-scatter
        const float BoltFog = 0.45f;           // far fog colour and the storm veil
        const float BoltSky = 0.45f;           // sky globals: plume ambient, water reflections, ash flakes
        const float BoltDirLux = 1800f;        // the unshadowed 'Lightning' directional (rims and a fog glow toward the bolt)
        const float BoltFloor = 0.25f;         // the weakest bolt relative to the strongest (hash per bolt)
        const float BoltExpComp = 0.5f;        // stops of flash taken back per stop of ladder lift (0 = none, 0.9 = full)
        static readonly Color BoltTint = new Color(0.80f, 0.86f, 1.0f);

        // ---- daylight strip under the umbrella edge (rendered by the plume composite and CSky) ----
        static readonly Color StripDay = new Color(1.0f, 0.84f, 0.60f) * 4200f;
        // the red seam of the night, the promise of dawn: SunRed alone has almost no green, so the night grade lifted its blue
        // to its green and the seam read rose-pink (625: 84,49,51). Half Ember gives it the green of a sun under the ash edge
        // (G > B); the nits drop 3000 -> 2400 so its red stays within ~10% of before and it reads as a seam, not a neon line
        const float StripNightNits = 2400f;
        const float StripNightEmber = 0.5f;    // share of Palette.Ember in the seam colour (the rest Palette.SunRed), mixed in linear
        const float StripScaleH = 2500f;       // m, scale height of the sunlit air by day ...
        const float StripScaleHNight = 1200f;  // ... and of the thin red band at night: a seam, not a smear
        const float StripRadius = 0.95f;       // shadow footprint radius / umbrella radius (the rim is a thin veil)
        const float StripFadeD0 = 3000f, StripFadeD1 = 12000f;   // m of sunlit-air exit distance over which the strip fades in (t ~316-334)

        // ---- grade ----
        // Exposure ladder: post exposure in stops over the fixed daylight EV. The stills of the dark half sat 50-75% under
        // sRGB 20; these lifts put B5 at a mean of about 35-50, the storm about 35, the night about 38 (inverse-ACES gain
        // test on QA/review4). Each term rides a smooth clock or the storm without its clearings: no pumping, seekable.
        const float DarkLiftEV = 0.45f;        // with the darkness itself (B3 on)
        const float AshLiftEV = 0.6f;          // the ash-dark beats before the storm (300 to 504): wave, cliffs, stairs out of the toe
        const float AshLiftD0 = 0.5f, AshLiftD1 = 0.92f;   // Dark window of that lift: starts ~t 284, full by ~t 330 (M2 at 262 untouched)
        const float StormLiftEV = 1.3f;        // the body of the storm; its clearings keep it (they read brighter, as they should)
        const float NightLiftEV = 1.2f;        // once the storm has gone (B7): treads and the keeper house about 1 stop under mid grey
        const float BloomThrAsh = 0.6f;        // linear, exposed bloom threshold under the ash: lamps and lava bloom, the sky does not
        const float BloomThrNight = 0.4f;      // linear, at night: the keeper window and the lamp heads bloom
        const float ProbeDimStorm = 0.6f;      // fog in-scatter of the ambient probe in the storm: the practicals make halos
        // lum-vs-sat: darks and mids keep most of their colour now (x0.8 / x0.84, was x0.5 / x0.6: the ash half went monochrome)
        const float CurveDark = 0.10f, CurveLow = 0.08f, CurveHigh = 0.1f;
        const float TealBoost = 0.3f;          // hue-vs-sat: the teal of the wave survives the grey grade
        const float ContrastDark = 10f;        // contrast the bronze stop relaxes to as the light dies (22 crushed t 300 to a mean of 16)
        const float ContrastStorm = 6f;        // pivots at mid grey, so a high value sinks the storm's few-stops-under content
        const float SatStorm = -10f;           // global saturation in the storm (cool slate ash against the fire, not grey)
        const float TempStormNight = -6f;      // white balance in the storm and the night: slate air, the fire stays warm
        const float TempAshWave = -10f;        // white balance under the ash once the wave runs (protects the teal)
        // Split-toning shadows of the dark beats: teal (G close to B), not blue. Soft-lit onto a fire-lit dark red, a blue tone
        // lifts B over G and turns it rose, then violet (395-664 column 324-343 deg, 540 lilac); a cyan-teal tone takes R down
        // against G and B together, so the same darks stay ember-brown and charcoal while the slate sky and sea stay slate-teal.
        // Checked with a port of HDRP's LUT (grading in ACEScg, approx ACES) on the v4 stills: violet share of the dark pixels
        // 0.04-0.52 -> 0.00, the 625 sky 40,44,52 -> 40,46,49, the 425 wave face keeps its teal, mean L within +1.
        static readonly Color SplitShadowDark = new Color(0.40f, 0.48f, 0.505f);
        const float SplitShadowK = 0.85f;      // strength of that tone at full darkness
        const float TintStormNight = -3f;      // white balance tint in the storm and the night: a touch of green against the magenta drift
        const float CloudDensityDay = 0.26f;   // the 2.1 km cumulus layer holds more cloud than the old 1.1 km slab
        // B3's bronze is sunlight in the ash: it dies with the sun (Dark 0.7 -> 0.9, t ~300-325), not 35 s later with the
        // collapse clock. Without this 320 kept a +16 bronze balance and a warm haze, and the orange column sat on beige.
        const float SunGoneD0 = 0.7f, SunGoneD1 = 0.9f;
        const float BronzeSunWB = 0.55f;       // share of the bronze white balance that goes with the sun (t 320: 16 -> 7.5)
        const float HazeSunK = 0.4f;           // share of the warm ash haze (sky, fog) that turns slate with the sun (t 320: 0.27 -> 0.55)
        // cumulus drift on the story clock (EnvRig zeroes HDRP's accumulated wind): clouds and their shadows are seekable
        static readonly Vector2 CloudDrift = new Vector2(0.94f, 0.34f) * 3.3f;   // m/s: the old 12 km/h breeze, along the plume's wind (_PlWind)
        static readonly Vector2 CloudPhase = Vector2.zero;                        // m: slides the whole field, e.g. a shadow off a hero frame

        float curveK = -1f;                    // cache of the quantised curve strength last written into the TextureCurves

        public void Apply()
        {
            float t = Story.Time;
            float te = Story.T(Ev.Eruption);
            Dark = Sim.Darkness(te);
            Storm = Sim.StormDensity(Story.T(Ev.AshStorm), Story.T(Ev.Glimpse1), Story.T(Ev.Glimpse2), Story.T(Ev.Silence));
            Night = MathX.Smooth(0f, 40f, Story.T(Ev.AshStorm));
            float ash = Sim.Ashfall(te);
            float calmK = MathX.Smooth(0f, 25f, Story.T(Ev.Silence));
            AshGrey = MathX.Smooth(0f, 35f, Story.T(Ev.Collapse));
            float ashG = AshGrey;
            // the body of the storm without its clearings: what the exposure follows (a clearing must not pump the exposure)
            float stormBody = MathX.Smooth(0f, Config.StormCloseIn, Story.T(Ev.AshStorm)) * (1f - MathX.Smooth(0f, Config.SilenceClear, Story.T(Ev.Silence)));
            Vector3 eye = rig.cam != null ? rig.cam.transform.position : PathDef.Pos(Story.Chainage) + Vector3.up * Config.EyeHeight;

            // ---- exposure ladder (stops of post exposure; see the constants) ----
            // continuous through every hand-over: ~1.05 EV at 504, 1.57 in the storm and its clearings, 1.65 after the silence
            float darkK = Mathf.Max(Dark, Night);
            float baseEV = DarkLiftEV * darkK * (1f - 0.4f * stormBody);       // stormBody, not Storm: the clearings must not pump it
            float ashLift = AshLiftEV * MathX.Smooth(AshLiftD0, AshLiftD1, Dark) * (1f - Night);
            float liftEV = ashLift * (1f - stormBody) + StormLiftEV * stormBody + NightLiftEV * Night * (1f - stormBody);
            LadderEV = baseEV + liftEV;
            BoltGain = Mathf.Pow(2f, -BoltExpComp * liftEV);
            // the sun's share of B3's bronze (sky, fog, white balance), gone by Dark 0.9; t 300 is untouched (Dark 0.72)
            float sunGone = MathX.Smooth(SunGoneD0, SunGoneD1, Dark);
            float hazeG = 1f - (1f - ashG) * (1f - HazeSunK * sunGone);

            // ---- sun, and the key light of the night ----
            SunDir = Sim.SunDir(t);
            // the key swings from the dead sun to the burning flank as the storm closes in: contre-jour rims, long shadows
            float swing = MathX.Smooth(0f, KeySwing, Story.T(Ev.AshStorm));
            Vector3 toFire = Beat06_AshFire.PdcShore - eye; toFire.y = 0f;
            float fe = FireElevDeg * Mathf.Deg2Rad;
            FireDir = toFire.sqrMagnitude > 1f ? toFire.normalized * Mathf.Cos(fe) + Vector3.up * Mathf.Sin(fe) : SunDir;
            KeyDir = swing > 0f ? Vector3.Slerp(SunDir, FireDir, swing).normalized : SunDir;
            rig.sun.transform.rotation = Quaternion.LookRotation(-KeyDir, Vector3.up);
            float elev01 = Mathf.InverseLerp(Config.SunElevStart, Config.SunElevEnd, Mathf.Asin(SunDir.y) * Mathf.Rad2Deg);
            Color sunCol = Color.Lerp(DaySun, HighSun, elev01);
            sunCol = Color.Lerp(sunCol, BronzeSun, MathX.Smooth(0.05f, 0.5f, Dark));
            sunCol = Color.Lerp(sunCol, BrownSun, MathX.Smooth(0.5f, 0.9f, Dark));
            // the ash never takes all of the light: a dim brown "ashlight" remains so that the world stays readable
            float sunLux = EnvRig.SunLux * 0.95f * Mathf.Exp(-4.0f * Mathf.Pow(Dark, 1.3f)) * (1f - 0.6f * Night);
            Color emberMix = Color.Lerp(Palette.Ember.linear, new Color(0.75f, 0.6f, 0.5f), 0.6f);
            // HDRP key: the fire, warming from ember to a firelit amber once the storm has passed
            float keyLux = Night * Mathf.Lerp(KeyLuxStorm, KeyLuxCalm, calmK);
            Color keyCol = Color.Lerp(emberMix, new Color(0.95f, 0.70f, 0.52f), calmK);
            Color surf = sunCol * sunLux + keyCol * keyLux;
            float lux = Mathf.Max(surf.maxColorComponent, 1e-3f);
            rig.sun.color = new Color(surf.r / lux, surf.g / lux, surf.b / lux).gamma;      // Light.color is sRGB; our maths is linear
            rig.sun.intensity = lux;
            rig.sunData.shadowDimmer = Mathf.Lerp(Mathf.Lerp(1f, 0.3f, Mathf.Max(Dark, Night)), 0.85f, swing);
            rig.sunData.volumetricDimmer = Mathf.Lerp(1f, KeyFogStorm, stormBody * swing);
            // custom shaders keep the old ember glow from the sun's direction: their tuning is unchanged
            float glow = Night * Mathf.Lerp(GlowLuxStorm, GlowLuxCalm, calmK);
            Color glowCol = Color.Lerp(emberMix, new Color(0.80f, 0.74f, 0.68f), calmK);
            SunColorLux = sunCol * sunLux + glowCol * glow;

            // ---- sky colours for custom shaders (nits) ----
            Color zenDay = new Color(0.22f, 0.40f, 0.78f) * 4600f;
            Color horDay = new Color(0.60f, 0.76f, 0.96f) * 11500f;
            // after the collapse the bronze haze turns to slate ash: the warmth is left to what glows (colour separation)
            Color horAshWarm = new Color(0.95f, 0.60f, 0.33f) * 3200f;
            // cool slate (same luminance as the old neutral tokens): complementary to lava, lamps and the keeper window.
            // hazeG: the collapse clock, led by the dying sun (the bronze is sunlit ash; without the sun it is grey)
            Color zenAsh = Color.Lerp(Palette.AshBrown.linear * 3000f, new Color(0.27f, 0.29f, 0.33f) * 2290f, hazeG);
            Color horAsh = Color.Lerp(horAshWarm, new Color(0.55f, 0.55f, 0.56f) * 2480f, hazeG);
            Color zenNight = new Color(0.10f, 0.085f, 0.075f) * 1500f;
            Color horNight = new Color(0.42f, 0.33f, 0.27f) * 1000f;      // neutral: the red lives in the horizon strip, not in a brown wash
            // day -> bronze haze -> ash, as the umbrella actually covers the sky: the M2 column (t 262, Dark 0.22) still stands
            // dark against blue, the dome turns bronze over ~254-290
            float skyK = MathX.Smooth(0.15f, 0.6f, Dark);
            Color zenBr = new Color(0.40f, 0.34f, 0.28f) * 2600f, horBr = new Color(0.95f, 0.70f, 0.42f) * 6200f;
            Color zenA = skyK < 0.5f ? Color.Lerp(zenDay, zenBr, skyK * 2f) : Color.Lerp(zenBr, zenAsh, skyK * 2f - 1f);
            Color horA = skyK < 0.5f ? Color.Lerp(horDay, horBr, skyK * 2f) : Color.Lerp(horBr, horAsh, skyK * 2f - 1f);
            SkyZenith = Color.Lerp(zenA, zenNight, Night);
            SkyHorizon = Color.Lerp(horA, horNight, Night);

            // ---- lightning (same pure function as the bolt sprite and the plume's bolt light) ----
            float pulse = ParticleDirector.Lightning(te, Dark, Storm, out Vector3 boltA, out Vector3 boltB, out int boltK);
            float hb = MathX.Hash(boltK, 931);
            // no world flash while daylight remains; a quieter sky once the storm has passed
            BoltFlash = pulse * (BoltFloor + (1f - BoltFloor) * hb * hb) * MathX.Smooth(0.5f, 0.9f, Dark) * (1f - 0.6f * calmK) * BoltGain;

            // the zenith of the HDRP sky is hidden by the ash umbrella once it has spread: there it only feeds the ambient
            // probe, so it carries a shadowless fill that keeps the dark beats readable; cool at night against the fire key
            Color fillTint = Color.Lerp(new Color(0.34f, 0.31f, 0.29f), new Color(0.27f, 0.30f, 0.36f), Night);
            Color fill = fillTint * Mathf.Lerp(1500f, 2600f, Night) * (1f + 0.45f * calmK) * Mathf.Lerp(1f, 0.6f, Night);
            Color gTop = Color.Lerp(SkyZenith, fill, MathX.Smooth(0.55f, 0.95f, Mathf.Max(Dark, Night)));
            // lower hemisphere: sea and horizon, plus by day the bounce of the sunlit island and sea; it is also what lights
            // the cloud bases from below (HDRP clouds lerp the probe bottom -> top with height), so cumulus are not charcoal
            Color gB0 = Color.Lerp(SkyHorizon * 0.32f, Palette.DeepWater.linear * 2600f * (1f - Dark), 0.5f);
            Color gBottom = Color.Lerp(gB0, new Color(0.56f, 0.53f, 0.48f) * 4200f, 0.45f * (1f - MathX.Smooth(0.05f, 0.4f, Dark)));
            rig.gsky.top.value = Flash(gTop, BoltAmbient * BoltFlash);
            rig.gsky.middle.value = Flash(SkyHorizon, BoltAmbient * BoltFlash);
            rig.gsky.bottom.value = Flash(gBottom, BoltAmbient * BoltFlash);

            // ---- fog (its colour is the horizon colour, so far scenery dissolves into the backdrop) ----
            Color fogDay = new Color(0.66f, 0.74f, 0.86f) * 10700f;
            Color fogAsh = Color.Lerp(horAshWarm * 0.8f, new Color(0.45f, 0.48f, 0.52f) * 2370f, hazeG);
            Color fogStorm = new Color(0.28f, 0.29f, 0.32f) * 3050f;      // slate, not brown: the fire key and the lamps carry the warmth
            Color fogNight = new Color(0.22f, 0.22f, 0.24f) * 1000f;
            Color fogBr = horBr * 0.88f;
            Color fc = skyK < 0.5f ? Color.Lerp(fogDay, fogBr, skyK * 2f) : Color.Lerp(fogBr, fogAsh, skyK * 2f - 1f);
            fc = Color.Lerp(fc, fogStorm, Storm);
            fc = Color.Lerp(fc, fogNight, Night * (1f - Storm));
            fc = Flash(fc, BoltFog * BoltFlash);
            StormFog = Flash(fogStorm, BoltFog * BoltFlash);
            FogColor = fc;
            rig.fog.color.value = fc;
            float mfp = Mathf.Lerp(6500f, 6000f, Dark);
            mfp = Mathf.Lerp(mfp, 2600f, ash * 0.6f);
            // inside a clearing of the storm the air is cleaner than the ash haze around it (the veil of the plume pass keeps the rest closed)
            mfp = Mathf.Lerp(mfp, 10000f, Mathf.Max(Sim.GlimpseWindow(Story.T(Ev.Glimpse1)), Sim.GlimpseWindow(Story.T(Ev.Glimpse2))));
            mfp = Mathf.Exp(Mathf.Lerp(Mathf.Log(mfp), Mathf.Log(26f), Storm));
            rig.fog.meanFreePath.value = mfp;
            rig.fog.maximumHeight.value = Mathf.Lerp(1300f, 2600f, MathX.Smooth(0.1f, 0.5f, Dark));   // the haze thins with height: the summit is crisper than the base
            rig.fog.anisotropy.value = Mathf.Lerp(0.55f, 0.3f, Storm);
            rig.fog.globalLightProbeDimmer.value = Mathf.Lerp(1f, ProbeDimStorm, Storm);

            rig.indirect.indirectDiffuseLightingMultiplier.value = Mathf.Lerp(1f, 2.0f, Dark) * (1f - 0.4f * Night);

            // ---- the lightning light: from the bolt to the eye, unshadowed; off between bolts ----
            float boltLux = BoltDirLux * BoltFlash * (1f - 0.6f * Storm);
            bool boltOn = boltLux > 1f;
            if (rig.bolt != null)
            {
                if (rig.bolt.enabled != boltOn) rig.bolt.enabled = boltOn;
                if (boltOn)
                {
                    Vector3 fromBolt = eye - (boltA + boltB) * 0.5f;
                    if (fromBolt.sqrMagnitude > 1f) rig.bolt.transform.rotation = Quaternion.LookRotation(fromBolt.normalized, Vector3.up);
                    rig.bolt.intensity = boltLux;
                }
            }

            // ---- clouds fade with the day, on the same window as the sky's turn to bronze ----
            rig.clouds.densityMultiplier.value = Mathf.Lerp(CloudDensityDay, 0f, MathX.Smooth(0.15f, 0.6f, Dark));
            rig.clouds.enable.value = Dark < 0.6f;
            // HDRP shape-noise coordinates are metres * shapeScale / 1e5; the offset is subtracted, so the field moves along +drift
            Vector2 drift = CloudPhase + CloudDrift * t;
            float noisePerM = rig.clouds.shapeScale.value * 1e-5f;
            rig.clouds.shapeOffset.value = new Vector3(drift.x * noisePerM, 0f, drift.y * noisePerM);

            // ---- grade ----
            float bronze = MathX.Smooth(0.05f, 0.6f, Dark) * (1f - Storm) * (1f - Night);
            float calmG = calmK;
            // B3 keeps its bronze stop; after the collapse the saturation is rationed by luminance (curves) instead of globally
            float satBronze = Mathf.Lerp(-22f, -10f, ashG), conBronze = Mathf.Lerp(22f, 14f, ashG);
            // the bronze stop's punch suits the dimming daylight (B3), not a frame 3-4 stops under mid grey: relax it as the light dies
            conBronze = Mathf.Lerp(conBronze, ContrastDark, MathX.Smooth(0.55f, 0.9f, Dark));
            rig.color.saturation.value = Mathf.Lerp(Mathf.Lerp(4f, satBronze, bronze), SatStorm, Storm) + Night * (1f - Storm) * Mathf.Lerp(-8f, -20f, calmG);
            // night: lower contrast (it pivots at mid grey and crushed the dark beat), the SMH black level keeps silhouettes black
            rig.color.contrast.value = Mathf.Lerp(Mathf.Lerp(6f, conBronze, bronze), ContrastStorm, Storm) - 2f * Night * (1f - Storm);
            // the dark beats: the exposure ladder and a teal-and-ember split, so the darkness has depth and colour
            rig.color.postExposure.value = LadderEV;
            rig.split.shadows.value = Color.Lerp(new Color(0.5f, 0.5f, 0.5f), SplitShadowDark, darkK * SplitShadowK);
            rig.split.highlights.value = Color.Lerp(new Color(0.5f, 0.5f, 0.5f), new Color(0.72f, 0.54f, 0.36f), darkK * 0.8f);
            rig.split.balance.value = -10f * darkK;
            // white balance: bronze in B3, back to 0 in B4, cool in B5 (protects the teal), slate in the storm and the night.
            // Chained so each hand-over is monotonic (blending through the bronze stop swung warm halfway into the storm's close-in);
            // the bronze stop shrinks with the sun before the collapse clock takes the rest (both fall toward tempAsh <= 0)
            float tempAsh = Mathf.Lerp(0f, TempAshWave, MathX.Smooth(0f, 30f, Story.T(Ev.Wave)));
            float tempBronze = Mathf.Lerp(5f, 22f, MathX.Smooth(0.05f, 0.6f, Dark)) * (1f - BronzeSunWB * sunGone);
            float tempDay = Mathf.Lerp(tempBronze, tempAsh, ashG);
            rig.wb.temperature.value = Mathf.Lerp(tempDay, TempStormNight, Mathf.Max(Storm, Night));
            // HDRP's tint: negative moves the balance toward green (ColorBalanceToLMSCoeffs). Small: the teal split does the work
            rig.wb.tint.value = TintStormNight * Mathf.Max(Storm, Night);
            // shadows / highlights: cool shadows and warm highlights by day; under the ash a black level (a multiplier on the
            // shadows, so a light touch: -0.07 sank the wave face) and brighter practicals
            rig.smh.shadows.value = new Vector4(Mathf.Lerp(0.98f, 0.96f, darkK), 0.99f, Mathf.Lerp(1.02f, 1.05f, darkK), -0.03f * ashG);   // a milder cool shadow by day keeps the cone volcanic
            rig.smh.highlights.value = new Vector4(1.06f, 1.0f, 0.92f, 0.04f * ashG);
            ApplyCurves(ashG * (1f - 0.3f * Night));
            rig.vignette.intensity.value = Mathf.Lerp(0.16f, 0.26f, Mathf.Max(Dark, Storm));
            // bloom: HDRP's threshold is gamma encoded; ours are linear exposed values. It rises with half the ladder's lift, so
            // the lamps, lava and keeper window bloom about as before and the brighter ash sky still does not
            float thr = Mathf.Lerp(Mathf.Lerp(1f, BloomThrAsh, Dark), BloomThrNight, Night) * Mathf.Pow(2f, 0.5f * liftEV);
            rig.bloom.threshold.value = Mathf.LinearToGammaSpace(thr);
            rig.bloom.intensity.value = Mathf.Lerp(0.16f, 0.42f, Night);
            rig.bloom.scatter.value = Mathf.Lerp(0.68f, 0.8f, Night);

            // ---- lens flare only while the sun is clean; harbour mist only before the blast ----
            rig.flare.intensity = 0.08f * (1f - MathX.Smooth(0.02f, 0.2f, Dark)) * (te > 0f ? 1f - MathX.Smooth(20f, 45f, te) : 1f);
            bool mistOn = te < 25f;
            if (rig.mist.gameObject.activeSelf != mistOn) rig.mist.gameObject.SetActive(mistOn);

            // ---- no depth of field (EnvRig leaves it off). The last shot's far softness blurred the lighthouse beam and the
            // column at full radius: the plume pass draws both on sky pixels (depth 0), which HDRP's far blur takes as
            // infinitely far (664: a 50-70 px grey smear and a ghost funnel, against a 17 px beam at 570 without it) ----

            // ---- the daylight strip: sunlit air beyond the edge of the umbrella's shadow, seen from inside it ----
            Vector4 strip = Vector4.zero, strip2 = Vector4.zero;
            Strip = Color.black;
            float R = StripRadius * Sim.UmbrellaRadius(te);
            float stripK = MathX.Smooth(0.35f, 0.75f, Dark) * (1f - Storm);
            if (R > 100f && stripK > 0f)
            {
                // the shadow of the lid (sunlight passes under its base; with the plume's wind lean) projected along the sun
                Vector3 vent = ConeDirector.Vent(Story.T(Ev.Collapse));
                float hS = Sim.UmbrellaBase;
                float zl = hS - vent.y;
                Vector2 lean = new Vector2(0.94f, 0.34f) * (2.2e-6f * zl * zl);      // = PlumeDirector's _PlWind
                float proj = hS / Mathf.Max(SunDir.y, 0.05f);
                Vector2 c = new Vector2(vent.x + lean.x - SunDir.x * proj, vent.z + lean.y - SunDir.z * proj);
                Vector2 o = new Vector2(eye.x, eye.z) - c;
                Vector2 toCone = new Vector2(Config.ConeCenter.x - eye.x, Config.ConeCenter.z - eye.z);
                float cc = o.sqrMagnitude - R * R;
                if (cc < 0f && toCone.sqrMagnitude > 1f)
                {
                    Vector2 dir = toCone.normalized;
                    float b = Vector2.Dot(o, dir);
                    float exitD = -b + Mathf.Sqrt(Mathf.Max(b * b - cc, 0f));          // where the view toward the cone leaves the shadow
                    float inside = MathX.Saturate(-cc / (0.2f * R * R));               // fades in as the shadow edge passes over the camera
                    // while the edge is still close (B4 onset) the sunlit air would fill half the sky of a beat that must be dark:
                    // the strip grows in as a seam once the edge has moved out
                    inside *= MathX.Smooth(StripFadeD0, StripFadeD1, exitD);
                    Color stripNight = Color.Lerp(Palette.SunRed.linear, Palette.Ember.linear, StripNightEmber) * StripNightNits;
                    Strip = Color.Lerp(StripDay, stripNight, Night) * (stripK * inside);
                    if (Strip.maxColorComponent > 1e-3f)
                    {
                        strip = new Vector4(Strip.r, Strip.g, Strip.b, 1f / Mathf.Lerp(StripScaleH, StripScaleHNight, Night));
                        strip2 = new Vector4(c.x, c.y, R, exitD);
                    }
                    else Strip = Color.black;
                }
            }

            // ---- globals for the hand-written shaders ----
            // the custom sky flashes with the bolt too: water reflections, the umbrella's ambient and the ash flakes
            SkyZenith = Flash(SkyZenith, BoltSky * BoltFlash);
            SkyHorizon = Flash(SkyHorizon, BoltSky * BoltFlash);
            Shader.SetGlobalVector(idSunDir, SunDir);
            Shader.SetGlobalVector(idSunCol, SunColorLux);
            Shader.SetGlobalVector(idSunRaw, Color.Lerp(DaySun, HighSun, elev01) * (EnvRig.SunLux * 0.95f));
            float fogBase = rig.fog.baseHeight.value, fogH = Mathf.Max(1f, 0.144765f * (rig.fog.maximumHeight.value - fogBase));
            Shader.SetGlobalVector(idPlFog, new Vector4(1f / mfp, fogBase, 1f / fogH, 1f / Mathf.Lerp(Mathf.Lerp(90000f, 21000f, Dark), 11000f, Night)));
            Shader.SetGlobalVector(idZen, SkyZenith);
            Shader.SetGlobalVector(idHor, SkyHorizon);
            Shader.SetGlobalVector(idFog, FogColor);
            Shader.SetGlobalVector(idStrip, strip);
            Shader.SetGlobalVector(idStrip2, strip2);
            Shader.SetGlobalFloat(idTime, t);
            Shader.SetGlobalFloat(idFrame, Time.frameCount % 64);      // march jitter changes every frame, so TAA can resolve it
            Shader.SetGlobalVector(idClocksA, new Vector4(C(te), C(Story.T(Ev.Collapse)), C(Story.T(Ev.Drawdown)), C(Story.T(Ev.Wave))));
            Shader.SetGlobalVector(idClocksB, new Vector4(C(Story.T(Ev.AshStorm)), C(Story.T(Ev.Silence)), C(Story.T(Ev.Arrival)), Story.TremorLevel()));
            Shader.SetGlobalVector(idState, new Vector4(Dark, Storm, Night, ash));
            Shader.SetGlobalVector(idCone, new Vector4(Config.ConeCenter.x, Config.ConeCenter.y, Config.ConeCenter.z, Config.ConeHeight));
        }

        /// <summary>Colour rationing under the ash: darks and mids lose colour, lamps, lava and the strip keep it, teal is boosted.
        /// Brown and ember share a hue, so luminance separates them, not hue. Keys only move when the quantised strength changes.</summary>
        void ApplyCurves(float k)
        {
            if (rig.curves == null) return;
            float kq = Mathf.Round(MathX.Saturate(k) * 64f) / 64f;           // quantised: a pure function of the clock, few texture rebuilds
            bool on = kq > 0f;
            if (rig.curves.lumVsSat.overrideState != on) rig.curves.lumVsSat.overrideState = on;
            if (rig.curves.hueVsSat.overrideState != on) rig.curves.hueVsSat.overrideState = on;
            if (!on || kq == curveK) return;
            curveK = kq;
            TextureCurve lum = rig.curves.lumVsSat.value;
            lum.MoveKey(0, new Keyframe(0f, 0.5f - CurveDark * kq));
            lum.MoveKey(1, new Keyframe(0.25f, 0.5f - CurveLow * kq));
            lum.MoveKey(2, new Keyframe(0.55f, 0.5f));
            lum.MoveKey(3, new Keyframe(0.8f, 0.5f + CurveHigh * kq));
            TextureCurve hue = rig.curves.hueVsSat.value;
            hue.MoveKey(0, new Keyframe(0.02f, 0.5f));
            hue.MoveKey(1, new Keyframe(0.065f, 0.5f + 0.22f * kq));                // ember orange keeps its colour in the dark grade
            hue.MoveKey(2, new Keyframe(0.11f, 0.5f));
            hue.MoveKey(3, new Keyframe(0.47f, 0.5f));
            hue.MoveKey(4, new Keyframe(0.52f, 0.5f + TealBoost * kq));
            hue.MoveKey(5, new Keyframe(0.58f, 0.5f));
        }

        static float Lum(Color c) { return 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b; }

        /// <summary>Adds bolt-white light proportional to the colour's own luminance: the relative flash is the same in every beat.</summary>
        static Color Flash(Color c, float k) { return k > 0f ? c + BoltTint * (Lum(c) * k) : c; }

        static float C(float clock) { return Mathf.Max(clock, -1f); }
    }
}
