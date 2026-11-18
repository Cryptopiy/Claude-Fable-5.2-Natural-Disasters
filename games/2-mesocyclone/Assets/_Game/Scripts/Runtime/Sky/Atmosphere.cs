using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace MESOCYCLONE
{
    /// <summary>Sky, sun, fog, exposure and grade: one global volume and three directional lights, all driven by the
    /// story state every frame. Fixed exposure (eased by the score), no auto-exposure anywhere.</summary>
    [DefaultExecutionOrder(-400)]
    public sealed class Atmosphere : MonoBehaviour
    {
        public static Atmosphere I;
        public Light sun, flash, fill;
        HDAdditionalLightData sunHd, flashHd;
        VolumeProfile profile;
        Exposure exposure;
        Fog fog;
        GradientSky sky;
        ColorAdjustments color;
        WhiteBalance white;
        ShadowsMidtonesHighlights smh;
        Bloom bloom;
        VolumetricClouds clouds;
        DepthOfField dof;
        bool flashShadows;

        public static Vector3 SunDir;               // direction TO the sun
        public static Color CloudSunColor;          // linear radiance colour of a sunlit cloud face (nits)
        public static float FogExtinction, FogBase, FogScaleHeight;

        public void Build()
        {
            I = this;
            // ---------------- lights ----------------
            sun = MakeDirectional("Sun", out sunHd);
            sunHd.interactsWithSky = true;
            sunHd.EnableShadows(true);
            sunHd.SetShadowResolutionOverride(false);
            sunHd.affectsVolumetric = true; sunHd.volumetricDimmer = 0.6f;

            flash = MakeDirectional("FlashLight", out flashHd);
            flashHd.interactsWithSky = false;
            flashHd.EnableShadows(false);
            flashHd.affectsVolumetric = true; flashHd.volumetricDimmer = 1f;
            // a flash is a lit cloud base kilometres wide, not a sun disc. HDRP ignores maxSmoothness on directional lights
            // and sizes the highlight from angularDiameter (35 deg -> max smoothness 0.23): a broad sheen on the wet road and
            // the hood instead of a mirror-sharp column under the flash
            flashHd.angularDiameter = Tune.F("flash.angDiam", 35f);
            // no crisp shadow shafts in the fog (the cab, pillar and pole volumes read as straight polygons); the shadows on
            // surfaces stay
            flashHd.volumetricShadowDimmer = Tune.F("flash.volShadow", 0f);
            flash.intensity = 0f;

            fill = MakeDirectional("NightFill", out HDAdditionalLightData fillHd);
            fillHd.interactsWithSky = false;
            fillHd.EnableShadows(false);
            fillHd.affectsVolumetric = false;
            fill.transform.rotation = Quaternion.Euler(72f, 20f, 0f);
            fill.color = Palette.StormSlate * 1.6f;
            fill.intensity = 0f;

            // ---------------- volume ----------------
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var env = profile.Add<VisualEnvironment>(false);
            env.skyType.Override((int)SkyType.Gradient);
            env.skyAmbientMode.Override(SkyAmbientMode.Dynamic);

            sky = profile.Add<GradientSky>(false);
            sky.top.Override(Color.black); sky.middle.Override(Color.black); sky.bottom.Override(Color.black);
            sky.gradientDiffusion.Override(1.6f);
            sky.updateMode.Override(EnvironmentUpdateMode.Realtime);
            sky.updatePeriod.Override(1.0f);

            fog = profile.Add<Fog>(false);
            fog.enabled.Override(true);
            fog.colorMode.Override(FogColorMode.SkyColor);
            fog.tint.Override(Color.white);
            fog.albedo.Override(new Color(0.9f, 0.9f, 0.9f));
            fog.meanFreePath.Override(12000f);
            fog.baseHeight.Override(0f);
            fog.maximumHeight.Override(1500f);
            fog.maxFogDistance.Override(60000f);
            fog.enableVolumetricFog.Override(true);
            fog.depthExtent.Override(Tune.F("fog.depthExtent", 500f));
            fog.denoisingMode.Override(FogDenoisingMode.Gaussian);
            fog.anisotropy.Override(0.6f);
            fog.sliceDistributionUniformity.Override(0.6f);
            fog.globalLightProbeDimmer.Override(1f);
            fog.quality.Override(Cli.Final ? 2 : 0);
            // far fog takes the blurred sky at mip (1 - mipFogMaxMip) * (N - 1). 0.5 is HDRP's own default, so this changes
            // nothing as shipped. Fallback if a cyan seam is still on the horizon at 150-180 s: fog.mipMax 0.9. It must go
            // HIGHER: a sharper sky equals the horizon colour, a lower value blurs more of the lower hemisphere into the fog
            fog.mipFogMaxMip.Override(Tune.F("fog.mipMax", 0.5f));

            exposure = profile.Add<Exposure>(false);
            exposure.mode.Override(ExposureMode.Fixed);
            exposure.fixedExposure.Override(12f);

            var tone = profile.Add<Tonemapping>(false);
            tone.mode.Override(TonemappingMode.ACES);

            bloom = profile.Add<Bloom>(false);
            bloom.intensity.Override(Tune.F("bloom.intensity", 0.22f));
            bloom.scatter.Override(0.68f);
            bloom.threshold.Override(0f);
            bloom.quality.Override(Cli.Final ? 2 : 0);

            color = profile.Add<ColorAdjustments>(false);
            color.contrast.Override(8f);
            color.saturation.Override(0f);
            color.colorFilter.Override(Color.white);
            color.postExposure.Override(0f);

            white = profile.Add<WhiteBalance>(false);
            white.temperature.Override(0f);
            white.tint.Override(0f);

            smh = profile.Add<ShadowsMidtonesHighlights>(false);
            smh.shadows.Override(new Vector4(1f, 1f, 1f, 0f));
            smh.midtones.Override(new Vector4(1f, 1f, 1f, 0f));
            smh.highlights.Override(new Vector4(1f, 1f, 1f, 0f));

            var ssr = profile.Add<ScreenSpaceReflection>(false);
            ssr.SetAllOverridesTo(true);
            ssr.enabled.value = true;
            ssr.reflectSky.value = true;
            ssr.minSmoothness = 0.62f;
            ssr.smoothnessFadeStart = 0.7f;
            ssr.screenFadeDistance.value = 0.12f;

            // a photographed look: fine grain and a trace of lens fringing toward the corners
            var grain = profile.Add<FilmGrain>(false);
            grain.type.Override(FilmGrainLookup.Medium2);
            grain.intensity.Override(Tune.F("post.grain", 0.14f));
            grain.response.Override(0.85f);
            var ca = profile.Add<ChromaticAberration>(false);
            ca.intensity.Override(Tune.F("post.fringe", 0.05f));

            var vig = profile.Add<Vignette>(false);
            vig.intensity.Override(0.22f);
            vig.smoothness.Override(0.5f);

            var mb = profile.Add<MotionBlur>(false);
            mb.intensity.Override(0f);

            var sh = profile.Add<HDShadowSettings>(false);
            sh.maxShadowDistance.Override(140f);
            sh.cascadeShadowSplitCount.Override(3);

            var ao = profile.Add<ScreenSpaceAmbientOcclusion>(false);
            ao.intensity.Override(Cli.Final ? 0.6f : 0.35f);
            ao.radius.Override(0.6f);

            var cs = profile.Add<ContactShadows>(false);
            cs.enable.Override(false);

            // ambient deck: sparse fair-weather cumulus in the evening (HDRP Volumetric Clouds), gone before the storm
            clouds = profile.Add<VolumetricClouds>(false);
            clouds.cloudControl.Override(VolumetricClouds.CloudControl.Simple);
            clouds.cloudPreset = VolumetricClouds.CloudPresets.Sparse;
            clouds.SetAllOverridesTo(true);
            clouds.enable.Override(false);
            clouds.bottomAltitude.Override(1700f);
            clouds.altitudeRange.Override(1300f);
            clouds.numPrimarySteps.Override(Cli.Final ? 48 : 32);
            clouds.numLightSteps.Override(4);
            clouds.globalWindSpeed.Override(new WindParameter.WindParamaterValue { mode = WindParameter.WindOverrideMode.Custom, customValue = 86f });
            clouds.orientation.Override(new WindParameter.WindParamaterValue { mode = WindParameter.WindOverrideMode.Custom, customValue = 90f });

            // depth of field: cinematic mode only, a slight softness on the nearest cab parts
            dof = profile.Add<DepthOfField>(false);
            dof.focusMode.Override(DepthOfFieldMode.Off);
            // the glass is 0.45-0.85 m from the eye: anything beyond it (hail striking it, the drops) stays sharp
            dof.nearFocusStart.Override(0.2f);
            dof.nearFocusEnd.Override(Tune.F("dof.nearEnd", 0.42f));
            dof.farFocusStart.Override(1.0e7f);          // beyond the far plane: the distance is never blurred
            dof.farFocusEnd.Override(2.0e7f);
            dof.quality.Override(0);

            var vol = gameObject.AddComponent<Volume>();
            vol.isGlobal = true; vol.priority = 100f; vol.weight = 1f; vol.sharedProfile = profile;
        }

        Light MakeDirectional(string name, out HDAdditionalLightData hd)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            hd = go.AddHDLight(LightType.Directional);
            Light l = go.GetComponent<Light>();
            l.lightUnit = LightUnit.Lux;
            l.useColorTemperature = false;
            return l;
        }

        // sky radiance (nits) by the elevation of the sun: zenith, mid sky, horizon
        static readonly float[] SkyElev = { -16f, -9f, -4.5f, -0.7f, 1.7f, 5.5f };
        static readonly Color[] SkyTop = { new Color(0.004f, 0.006f, 0.012f), new Color(0.06f, 0.10f, 0.24f), new Color(0.9f, 1.3f, 3.0f),
            new Color(14f, 22f, 52f), new Color(34f, 56f, 130f), new Color(120f, 230f, 560f) };
        static readonly Color[] SkyMid = { new Color(0.005f, 0.007f, 0.012f), new Color(0.12f, 0.17f, 0.30f), new Color(1.8f, 2.0f, 3.6f),
            new Color(44f, 32f, 50f), new Color(190f, 120f, 150f), new Color(700f, 760f, 900f) };
        static readonly Color[] SkyBot = { new Color(0.006f, 0.008f, 0.012f), new Color(0.30f, 0.36f, 0.42f), new Color(4.4f, 4.2f, 4.6f),
            new Color(240f, 100f, 60f), new Color(950f, 470f, 180f), new Color(2600f, 1900f, 1100f) };
        public static Color SunGlow;                // radiance of the glow around the sun, drawn by the storm pass on sky pixels

        static Color Key(Color[] k, float elev)
        {
            int n = SkyElev.Length;
            if (elev <= SkyElev[0]) return k[0];
            if (elev >= SkyElev[n - 1]) return k[n - 1];
            for (int i = 1; i < n; i++)
                if (elev <= SkyElev[i])
                {
                    float u = (elev - SkyElev[i - 1]) / (SkyElev[i] - SkyElev[i - 1]);
                    // radiance spans decades: interpolate in log space
                    Color a = k[i - 1], b = k[i];
                    return new Color(Mathf.Exp(Mathf.Lerp(Mathf.Log(a.r), Mathf.Log(b.r), u)), Mathf.Exp(Mathf.Lerp(Mathf.Log(a.g), Mathf.Log(b.g), u)),
                        Mathf.Exp(Mathf.Lerp(Mathf.Log(a.b), Mathf.Log(b.b), u)), 1f);
                }
            return k[n - 1];
        }

        // radiance (nits) of a sunlit cloud face by the sun's elevation at cloud level
        static readonly float[] CnElev = { -1f, 0.1f, 1.2f, 2.8f, 4.7f, 8.5f };
        static readonly float[] CnNits = { 0.001f, 12f, 60f, 190f, 420f, 1500f };
        static float CloudNits(float e)
        {
            if (e <= CnElev[0]) return 0f;
            int n = CnElev.Length;
            if (e >= CnElev[n - 1]) return CnNits[n - 1];
            for (int i = 1; i < n; i++)
                if (e <= CnElev[i])
                    return Mathf.Exp(Mathf.Lerp(Mathf.Log(CnNits[i - 1]), Mathf.Log(CnNits[i]), (e - CnElev[i - 1]) / (CnElev[i] - CnElev[i - 1])));
            return 0f;
        }

        static Color SunTint(float elev)
        {
            // warm white high up, orange at the horizon, deep pink-red just below it
            Color hi = new Color(1f, 0.86f, 0.70f), mid = Palette.SunsetOrange.linear, low = Palette.AnvilPink.linear * new Color(1f, 0.55f, 0.5f);
            if (elev > 5f) return hi;
            if (elev > 1f) return Color.Lerp(mid, hi, (elev - 1f) / 4f);
            return Color.Lerp(low, mid, Mathf.Clamp01((elev + 2f) / 3f));
        }

        void Update()
        {
            if (!Director.Running || profile == null) return;
            StoryState s = Director.S;

            // ---------------- sun ----------------
            float az = s.sunAzim * Mathf.Deg2Rad, el = s.sunElev * Mathf.Deg2Rad;
            SunDir = new Vector3(Mathf.Sin(az) * Mathf.Cos(el), Mathf.Sin(el), Mathf.Cos(az) * Mathf.Cos(el));
            float lightEl = (Mathf.Max(s.sunElev, 0f) + Tune.F("sun.lift", 9f)) * Mathf.Deg2Rad;
            Vector3 lightDir = new Vector3(Mathf.Sin(az) * Mathf.Cos(lightEl), Mathf.Sin(lightEl), Mathf.Cos(az) * Mathf.Cos(lightEl));
            sun.transform.rotation = Quaternion.LookRotation(-lightDir, Vector3.up);
            Color st = SunTint(s.sunElev);
            sun.color = new Color(st.r, st.g, st.b).gamma;
            // direct sunlight at the ground: strong and warm at 5 degrees, red and weak at the horizon, gone below it
            float direct = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.6f, 6f, s.sunElev));
            sun.intensity = s.sunLux * Mathf.Lerp(0.012f, 0.26f, direct) * Mathf.InverseLerp(-0.9f, 0.2f, s.sunElev);
            // the eye adapts: after sunset the sky is carried brighter than a meter would give, so the land,
            // the cab and the storm keep a readable silhouette between flashes
            float skyK = Tune.F("sky.scale", 1f) * Mathf.Lerp(Tune.F("sky.nightScale", 2.5f), 1f, Mathf.InverseLerp(-4.5f, -0.5f, s.sunElev));
            sky.top.value = Key(SkyTop, s.sunElev) * skyK;
            Color greenLow = Color.Lerp(Color.white, new Color(0.5f, 1.3f, 0.72f) * 1.8f, s.greenTint);
            Color greenMid = Color.Lerp(Color.white, new Color(0.80f, 1.08f, 0.90f), s.greenTint);   // was (0.66,1.14,0.82)
            Color midSky = Key(SkyMid, s.sunElev);
            sky.middle.value = midSky * greenMid * skyK;
            // 'bottom' is the hidden lower hemisphere: the land's bounce in the ambient probe and half of the blurred fog
            // colour at the horizon. From the green onset it is a dark ground, never brighter than the horizon (removes the
            // cyan seam and the green light from below on the headliner, the pillars and the turbine towers)
            float gw = Mathf.Clamp01(s.greenTint * 1.6f);
            Color ground = midSky * Tune.F("sky.groundBounce", 0.7f) * Color.Lerp(Color.white, new Color(0.85f, 1.05f, 0.95f), s.greenTint);
            sky.bottom.value = Color.Lerp(Key(SkyBot, s.sunElev) * greenLow, ground, gw) * skyK;
            // the sunset glow is drawn on sky pixels by the storm pass, so it keeps the bright horizon key
            Color sg = Key(SkyBot, s.sunElev);
            float glowK = Tune.F("sky.sunGlow", 5f) * Mathf.InverseLerp(-7f, -0.5f, s.sunElev);
            SunGlow = new Color(sg.r * glowK * 1.15f, sg.g * glowK * 0.8f, sg.b * glowK * 0.45f, Mathf.InverseLerp(-0.6f, 0.4f, s.sunElev));
            bool sunUp = s.sunLux > 1f;
            if (sunUp == flashShadows)
            {   // only one directional light may cast shadows: the sun by day, the lightning by night
                flashShadows = !sunUp;
                sunHd.EnableShadows(sunUp);
                flashHd.EnableShadows(!sunUp);
            }
            // the broad flash highlight also softens the hailstones' and the clear coat's sparkle. Fallback if they stop
            // reading under flashes at 300-345 s: flash.angDiamHail about 10 narrows it there, ramping to the broad value by
            // 365 s, before the night beats. By default both ends are equal, so nothing changes (the setter skips equal values)
            float angDiam = Tune.F("flash.angDiam", 35f);
            flashHd.angularDiameter = Mathf.Lerp(Tune.F("flash.angDiamHail", angDiam), angDiam, Mathf.InverseLerp(345f, 365f, s.T));

            // sunlit cloud faces: clouds at 10 km still see the sun about 3 degrees after the ground lost it
            float cloudElev = s.sunElev + 3f;
            // the light that reaches the tower has crossed the whole sunset air mass: gold, then rose (the classic
            // evening supercell: a burning tower over a blue-black base)
            Color tint = SunTint(cloudElev - Tune.F("cloud.tintShift", 5.0f));
            float nits = Tune.F("cloud.sunScale", 1f) * CloudNits(cloudElev);
            CloudSunColor = new Color(tint.r * nits, tint.g * nits, tint.b * nits, 1f);

            // no unshadowed fill: it would light the inside of the cab through the roof
            fill.intensity = 0f;

            // ---------------- exposure and grade ----------------
            exposure.fixedExposure.value = s.exposureEV + Tune.F("exposure.offset", 0f);
            white.temperature.value = Mathf.Lerp(12f, -22f, s.gradeCold);
            // a hint toward green only: together with the cold temperature a stronger tint turned the whole frame cyan
            white.tint.value = Mathf.Lerp(0f, Tune.F("grade.greenTint", -5f), s.greenTint);
            color.saturation.value = Mathf.Lerp(6f, -12f, s.gradeCold);
            color.contrast.value = Mathf.Lerp(6f, 9f, s.gradeCold);
            // blacks stay deep but are never crushed to zero: video compression needs something to hold on to
            float lift = Mathf.Lerp(0f, Tune.F("grade.nightLift", 0.004f), s.gradeCold);
            // under the green the shadows sink toward the blue-black of the storm core, not toward green
            float g = s.greenTint;
            smh.shadows.value = new Vector4(1f - 0.03f * g, 1f - 0.01f * g, 1f, lift);

            // ---------------- ambient cloud deck, depth of field ----------------
            float deck = Mathf.InverseLerp(120f, 96f, s.T) * Tune.F("deck.density", 0.35f);
            bool deckOn = deck > 0.004f && Tune.F("deck.on", 0f) > 0.5f;
            if (clouds.enable.value != deckOn) clouds.enable.value = deckOn;
            clouds.densityMultiplier.value = Mathf.Clamp01(deck);
            DepthOfFieldMode wantDof = Director.Cinematic && !Harness.BoardLook && !Cli.HasLook ? DepthOfFieldMode.Manual : DepthOfFieldMode.Off;
            if (dof.focusMode.value != wantDof) dof.focusMode.value = wantDof;

            // ---------------- fog ----------------
            bloom.intensity.value = Tune.F("bloom.intensity", 0.22f) + Tune.F("bloom.flash", 0.3f) * LightningDirector.Level;
            float mfp = Mathf.Max(20f, s.fogMfp * Tune.F("fog.mfpScale", 1f));
            fog.meanFreePath.value = mfp;
            float maxH = Mathf.Lerp(900f, 2600f, Mathf.Clamp01(s.rain + s.dust));
            fog.maximumHeight.value = maxH;
            Color dustTint = Color.Lerp(Color.white, new Color(1.25f, 1.0f, 0.72f), s.dust);
            // the fog takes the sky's colour, which is green under the storm: dust is lifted soil, so it undoes that green
            // and reads grey-tan instead of teal
            Color unGreen = new Color(1f / greenMid.r, 1f / greenMid.g, 1f / greenMid.b);
            fog.tint.value = dustTint * Color.Lerp(Color.white, unGreen, s.dust);
            fog.albedo.value = Color.Lerp(new Color(0.9f, 0.9f, 0.9f), new Color(0.75f, 0.62f, 0.46f), s.dust);
            FogExtinction = 1f / mfp; FogBase = 0f;
            FogScaleHeight = Mathf.Max(0.01f, maxH) * 0.144765f;                   // HDRP: ScaleHeightFromLayerDepth
        }

        /// <summary>Called by the lightning director: the whole world lit by the current flashes. veil scales the light in
        /// the volumetric fog (1 = full): a flash hidden behind the vortex lights the near fog only weakly, so the wedge
        /// stays a silhouette instead of a smear. Existing three-argument callers keep the full veil.</summary>
        public void SetFlash(Vector3 fromLocal, Color colorLin, float lux, float veil = 1f)
        {
            if (lux > 0.0005f && fromLocal.sqrMagnitude > 1f)
            {
                Vector3 dir = -fromLocal.normalized;                  // light travels from the flash to the truck
                if (dir.y > -0.12f) { dir.y = -0.12f; dir.Normalize(); }
                flash.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
            }
            flash.color = colorLin.gamma;
            flash.intensity = lux;
            flashHd.volumetricDimmer = veil;                          // the setter skips unchanged values: cheap every frame
        }
    }
}
