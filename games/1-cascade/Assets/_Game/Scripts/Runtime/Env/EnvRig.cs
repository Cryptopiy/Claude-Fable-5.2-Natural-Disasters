using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace CASCADE
{
    /// <summary>Camera, the key light and the lightning light, and the global volume with every override the game animates. Created at runtime.</summary>
    public class EnvRig
    {
        public Camera cam; public HDAdditionalCameraData hdCam;
        public Light sun; public HDAdditionalLightData sunData;         // the shadowed directional key (sun, at night the burning flank)
        public Light bolt; public HDAdditionalLightData boltData;       // unshadowed directional that flashes with each lightning bolt
        public Volume volume; public VolumeProfile profile;
        public VisualEnvironment env; public GradientSky gsky; public Fog fog; public VolumetricClouds clouds;
        public Exposure exposure; public Bloom bloom; public Vignette vignette; public Tonemapping tone;
        public ColorAdjustments color; public WhiteBalance wb; public ShadowsMidtonesHighlights smh; public SplitToning split;
        public ColorCurves curves;                                       // colour rationing by luminance under the ash (EnvDirector)
        public HDShadowSettings shadows; public IndirectLightingController indirect; public ContactShadows contact;
        public ScreenSpaceAmbientOcclusion ssao; public DepthOfField dof; public FilmGrain grain;
        public LensFlareComponentSRP flare; public LocalVolumetricFog mist;

        public const float ExposureEV = 13.1f;
        public const float SunLux = 100000f;

        public static EnvRig Create(Transform root, bool final)
        {
            var r = new EnvRig();

            // ---- camera ----
            var cgo = new GameObject("Camera");
            cgo.transform.SetParent(root, false);
            cgo.tag = "MainCamera";
            r.cam = cgo.AddComponent<Camera>();
            r.cam.nearClipPlane = Config.NearClip;
            r.cam.farClipPlane = Config.FarClip;
            r.cam.fieldOfView = Config.BaseFov;
            r.cam.allowHDR = true;
            r.hdCam = cgo.AddComponent<HDAdditionalCameraData>();
            r.hdCam.antialiasing = HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing;
            r.hdCam.TAAQuality = final ? HDAdditionalCameraData.TAAQualityLevel.High : HDAdditionalCameraData.TAAQualityLevel.Low;
            r.hdCam.taaSharpenStrength = 0.35f;
            r.hdCam.taaAntiFlicker = 0.6f;
            r.hdCam.dithering = true;
            r.hdCam.stopNaNs = true;
            r.hdCam.clearColorMode = HDAdditionalCameraData.ClearColorMode.Sky;
            r.hdCam.customRenderingSettings = true;
            SetFrame(r.hdCam, FrameSettingsField.SSR, false);
            SetFrame(r.hdCam, FrameSettingsField.SSGI, false);
            SetFrame(r.hdCam, FrameSettingsField.FullResolutionCloudsForSky, false);
            SetFrame(r.hdCam, FrameSettingsField.ContactShadows, final);
            cgo.AddComponent<AudioListener>();

            // ---- suns ----
            var sgo = new GameObject("Sun");
            sgo.transform.SetParent(root, false);
            r.sunData = sgo.AddHDLight(LightType.Directional);
            r.sun = sgo.GetComponent<Light>();
            r.sun.lightUnit = LightUnit.Lux;
            r.sun.intensity = SunLux;
            r.sun.color = Color.white;
            r.sun.shadows = LightShadows.Soft;
            r.sunData.SetShadowResolutionOverride(true);
            r.sunData.SetShadowResolution(final ? 4096 : 2048);
            r.sunData.affectsVolumetric = true;
            r.sunData.volumetricDimmer = 1f;
            r.sunData.angularDiameter = 0.53f;

            // ---- lightning: a second directional aimed from the bolt to the eye, no shadows (one shadowed directional only),
            // off between bolts so it costs nothing; it lights rims and makes the fog glow toward the bolt ----
            var lgo = new GameObject("Lightning");
            lgo.transform.SetParent(root, false);
            r.boltData = lgo.AddHDLight(LightType.Directional);
            r.bolt = lgo.GetComponent<Light>();
            r.bolt.lightUnit = LightUnit.Lux;
            r.bolt.intensity = 0f;
            r.bolt.color = new Color(0.80f, 0.86f, 1.0f).gamma;            // Light.color is sRGB
            r.bolt.shadows = LightShadows.None;
            r.boltData.affectsVolumetric = true;
            r.boltData.volumetricDimmer = 1f;
            r.boltData.interactsWithSky = false;                             // the sun stays the clouds' main light
            r.boltData.angularDiameter = 3f;                                 // a long channel, not a disc: broad soft highlights
            r.bolt.enabled = false;

            // ---- a restrained lens flare for the low morning sun (generated elements, no textures) ----
            var fd = ScriptableObject.CreateInstance<LensFlareDataSRP>();
            fd.elements = new[]
            {
                Ghost(0.75f, 0.16f, new Color(0.7f, 0.9f, 1f), 0.06f),
                Ghost(-0.3f, 0.12f, new Color(0.8f, 1f, 0.9f), 0.04f),
            };
            r.flare = sgo.AddComponent<LensFlareComponentSRP>();
            r.flare.lensFlareData = fd;
            r.flare.intensity = 1f;
            r.flare.useOcclusion = true;
            r.flare.occlusionRadius = 0.05f;
            r.flare.attenuationByLightShape = false;
            r.flare.allowOffScreen = false;

            // ---- morning mist on the harbour water (local volumetric fog), burnt off by the eruption ----
            var mgo = new GameObject("HarbourMist");
            mgo.transform.SetParent(root, false);
            mgo.transform.position = new Vector3(-120f, 7f, -140f);
            r.mist = mgo.AddComponent<LocalVolumetricFog>();
            r.mist.parameters.size = new Vector3(210f, 14f, 300f);
            r.mist.parameters.meanFreePath = 520f;
            r.mist.parameters.albedo = new Color(0.92f, 0.96f, 1f);
            r.mist.parameters.positiveFade = new Vector3(0.25f, 0.9f, 0.2f);
            r.mist.parameters.negativeFade = new Vector3(0.15f, 0.05f, 0.2f);
            r.mist.parameters.distanceFadeStart = 230f;
            r.mist.parameters.distanceFadeEnd = 300f;

            // ---- global volume ----
            var vgo = new GameObject("GlobalVolume");
            vgo.transform.SetParent(root, false);
            r.volume = vgo.AddComponent<Volume>();
            r.volume.isGlobal = true;
            r.volume.priority = 10f;
            r.profile = ScriptableObject.CreateInstance<VolumeProfile>();
            r.volume.sharedProfile = r.profile;
            var p = r.profile;

            // Sky: HDRP gradient sky driven by the colour script (the physically based sky renders black in the built
            // player of this Unity version, see DEVELOPMENT.md). Sun disc and glow are drawn by the plume pass.
            r.env = p.Add<VisualEnvironment>(true);
            r.env.skyType.value = (int)SkyType.Gradient;
            r.env.skyAmbientMode.value = SkyAmbientMode.Dynamic;
            r.gsky = p.Add<GradientSky>(true);
            r.gsky.gradientDiffusion.value = 2.6f;

            r.fog = p.Add<Fog>(true);
            r.fog.enabled.value = true;
            r.fog.colorMode.value = FogColorMode.ConstantColor;
            r.fog.color.value = new Color(0.55f, 0.68f, 0.85f) * 6000f;
            r.fog.tint.value = Color.white;
            r.fog.maxFogDistance.value = 150000f;
            r.fog.baseHeight.value = 0f;
            r.fog.maximumHeight.value = 2600f;
            r.fog.meanFreePath.value = 26000f;
            r.fog.enableVolumetricFog.value = true;
            r.fog.albedo.value = Color.white;
            r.fog.anisotropy.value = 0.55f;
            r.fog.depthExtent.value = final ? 420f : 260f;
            r.fog.sliceDistributionUniformity.value = 0.6f;
            r.fog.denoisingMode.value = FogDenoisingMode.Reprojection;
            r.fog.globalLightProbeDimmer.value = 1f;
            r.fog.quality.levelAndOverride = (0, true);
            r.fog.fogControlMode = FogControl.Balance;
            r.fog.volumetricFogBudget = final ? 0.42f : 0.2f;
            r.fog.resolutionDepthRatio = 0.6f;

            r.clouds = p.Add<VolumetricClouds>(true);
            r.clouds.enable.value = true;
            r.clouds.cloudControl.value = VolumetricClouds.CloudControl.Simple;
            // Fair-weather cumulus with our own height profile. The Sparse preset's density curve is a flat slab (1 from 5% to
            // 75% of the layer) and, in a 1.1 km layer, drew flat lens 'pancakes' with charcoal bellies in rows to the horizon.
            r.clouds.cloudPreset = VolumetricClouds.CloudPresets.Custom;
            r.clouds.shapeFactor.value = 0.9f;                               // lower = more cover (Sparse 0.95, Cloudy 0.9): the taller layer shows more cloud
            r.clouds.shapeScale.value = 5f;                                  // the presets' cell size: the lower base already makes them bigger overhead
            r.clouds.erosionFactor.value = 0.8f;                             // cauliflower edges
            r.clouds.erosionScale.value = 107f;
            r.clouds.densityMultiplier.value = 0.26f;                        // EnvDirector fades it with the darkness
            r.clouds.bottomAltitude.value = 1900f;                           // flat condensation-level bases just over the 1.8 km summit
            r.clouds.altitudeRange.value = 2100f;                            // towers with height, not lenses
            // density by height: a sharp flat base, then a taper, so each cloud narrows upward into rounded tops
            r.clouds.densityCurve.value = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.07f, 1f), new Keyframe(0.35f, 0.8f), new Keyframe(0.75f, 0.35f), new Keyframe(1f, 0f));
            r.clouds.erosionCurve.value = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.1f, 0.9f), new Keyframe(1f, 1f));
            // baked ambient occlusion low in the cloud: some shade in the bellies, not the presets' half (charcoal cores)
            r.clouds.ambientOcclusionCurve.value = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.25f, 0.3f), new Keyframe(1f, 0f));
            // lighting: more multiple scattering brightens the dense cores (the dark bellies were single scattering through
            // a km of cloud), a little powder for definition away from the sun, less erosion darkening of the ambient
            r.clouds.multiScattering.value = 0.8f;
            r.clouds.powderEffectIntensity.value = 0.3f;
            r.clouds.erosionOcclusion.value = 0.04f;
            // HDRP ramps density from 0 at fadeInStart to 1 at 2 * fadeInStart + fadeInDistance: full by 2.1 km, so the clouds
            // overhead are solid, not half-transparent grey (the old 3.2/2.5 km emptied every view above ~25 deg; the camera
            // never climbs into the 1.9 km base)
            r.clouds.fadeInMode.value = VolumetricClouds.CloudFadeInMode.Manual;
            r.clouds.fadeInStart.value = 600f;
            r.clouds.fadeInDistance.value = 900f;
            r.clouds.numPrimarySteps.value = final ? 64 : 32;
            r.clouds.numLightSteps.value = final ? 6 : 3;
            // cloud shadows: dapples drifting over the village and the caldera walls (HDRP Lit only; the custom cone and sea
            // shaders do not sample them). The trace uses the shape noise only (no erosion), a field of 0.5-2 km blobs, so the
            // per-frame refit of the map to the frustum (about 100 m texels over a ~25 km region at 256) cannot shimmer; no
            // jitter, no history. ~0.05-0.1 ms with the lit and fog taps; gone with the clouds at Dark 0.6.
            r.clouds.shadows.value = true;
            r.clouds.shadowResolution.value = VolumetricClouds.CloudShadowResolution.Medium256;
            r.clouds.shadowDistance.value = 12000f;                          // the sun at 17-31 deg throws a 1.9-4 km layer's shadow 3-13 km
            r.clouds.shadowOpacity.value = 0.6f;                             // 40% of the sun stays in a cloud shadow: dapples, not blackouts
            r.clouds.shadowOpacityFallback.value = 0f;
            // no HDRP wind: it accumulates Time.time (not seekable). EnvDirector drives shapeOffset from the story clock instead
            r.clouds.globalWindSpeed.value = new WindSpeedParameter.WindParamaterValue { mode = WindParameter.WindOverrideMode.Custom, customValue = 0f };

            r.exposure = p.Add<Exposure>(true);
            r.exposure.mode.value = ExposureMode.Fixed;
            r.exposure.fixedExposure.value = ExposureEV;

            r.tone = p.Add<Tonemapping>(true);
            r.tone.mode.value = TonemappingMode.ACES;

            r.bloom = p.Add<Bloom>(true);
            r.bloom.quality.levelAndOverride = (final ? 2 : 0, false);
            r.bloom.threshold.value = 1.0f;
            r.bloom.intensity.value = 0.18f;
            r.bloom.scatter.value = 0.68f;

            r.vignette = p.Add<Vignette>(true);
            r.vignette.intensity.value = 0.16f;
            r.vignette.smoothness.value = 0.45f;
            r.vignette.color.value = Color.black;

            r.color = p.Add<ColorAdjustments>(true);
            r.color.postExposure.value = 0f;
            r.color.contrast.value = 4f;
            r.color.saturation.value = 2f;
            r.color.colorFilter.value = Color.white;

            r.wb = p.Add<WhiteBalance>(true);
            r.wb.temperature.value = 4f;
            r.wb.tint.value = 0f;

            r.smh = p.Add<ShadowsMidtonesHighlights>(true);
            r.smh.shadows.value = new Vector4(0.96f, 0.99f, 1.05f, 0f);         // a filmic split: cool shadows, warm highlights
            r.smh.highlights.value = new Vector4(1.06f, 1.0f, 0.92f, 0f);

            r.split = p.Add<SplitToning>(true);
            r.split.shadows.value = new Color(0.5f, 0.5f, 0.5f);
            r.split.highlights.value = new Color(0.5f, 0.5f, 0.5f);

            // Curves: neutral keys (0.5 = no change) that EnvDirector bends after the collapse. Lum-vs-sat reads exposed scene
            // linear luminance (0.18 = mid grey); hue-vs-sat loops, 0.52 is the caldera teal. Off (not overridden) by day, so
            // HDRP can skip the grading LUT rebuild when nothing else changes.
            r.curves = p.Add<ColorCurves>(false);
            r.curves.lumVsSat.Override(new TextureCurve(new[] { new Keyframe(0f, 0.5f), new Keyframe(0.25f, 0.5f), new Keyframe(0.55f, 0.5f), new Keyframe(0.8f, 0.5f) },
                0.5f, false, new Vector2(0f, 1f)));
            r.curves.hueVsSat.Override(new TextureCurve(new[] { new Keyframe(0.02f, 0.5f), new Keyframe(0.065f, 0.5f), new Keyframe(0.11f, 0.5f), new Keyframe(0.47f, 0.5f), new Keyframe(0.52f, 0.5f), new Keyframe(0.58f, 0.5f) },
                0.5f, true, new Vector2(0f, 1f)));
            r.curves.lumVsSat.overrideState = false;
            r.curves.hueVsSat.overrideState = false;

            var ca = p.Add<ChromaticAberration>(true);
            ca.intensity.value = 0f;                // a rainbow fringe on the cone silhouette read as a defect

            r.shadows = p.Add<HDShadowSettings>(true);
            r.shadows.maxShadowDistance.value = final ? 520f : 380f;
            r.shadows.cascadeShadowSplitCount.value = final ? 4 : 3;
            r.shadows.cascadeShadowSplit0.value = 0.06f;
            r.shadows.cascadeShadowSplit1.value = 0.2f;
            r.shadows.cascadeShadowSplit2.value = 0.5f;

            r.indirect = p.Add<IndirectLightingController>(true);
            r.indirect.indirectDiffuseLightingMultiplier.value = 1f;
            r.indirect.reflectionLightingMultiplier.value = 1f;

            r.ssao = p.Add<ScreenSpaceAmbientOcclusion>(true);
            r.ssao.quality.levelAndOverride = (final ? 2 : 0, false);
            r.ssao.intensity.value = final ? 1.3f : 1.0f;
            r.ssao.radius.value = 0.9f;
            r.ssao.directLightingStrength.value = 0.3f;

            r.contact = p.Add<ContactShadows>(true);
            r.contact.enable.value = final;

            r.dof = p.Add<DepthOfField>(true);
            r.dof.focusMode.value = DepthOfFieldMode.Off;

            r.grain = p.Add<FilmGrain>(true);
            r.grain.type.value = FilmGrainLookup.Thin1;
            r.grain.intensity.value = 0.16f;
            r.grain.response.value = 0.8f;
            return r;
        }

        static LensFlareDataElementSRP Ghost(float position, float scale, Color tint, float intensity)
        {
            var e = new LensFlareDataElementSRP();
            e.flareType = SRPLensFlareType.Circle;
            e.position = position;
            e.uniformScale = scale;
            e.tint = tint;
            e.localIntensity = intensity;
            e.blendMode = SRPLensFlareBlendMode.Additive;
            e.count = 1;
            return e;
        }

        static void SetFrame(HDAdditionalCameraData hd, FrameSettingsField f, bool on)
        {
            hd.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)f] = true;
            hd.renderingPathCustomFrameSettings.SetEnabled(f, on);
        }
    }
}
