using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace MESOCYCLONE
{
    /// <summary>The only object of the scene. Builds the whole game at start behind a black screen (no generation
    /// work inside frames afterwards), warms everything up, then starts the director.</summary>
    public sealed class Bootstrap : MonoBehaviour
    {
        public GameAssets assets;
        public static Transform WorldRoot, Truck, Cab;
        public static Camera Cam;

        IEnumerator Start()
        {
            Cli.Parse();
            GameAssets.I = assets;
            Application.runInBackground = true;
            QualitySettings.SetQualityLevel(Cli.Final ? 1 : 0, true);
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-screen-width") < 0)
                Screen.SetResolution(1920, 1080, FullScreenMode.Windowed);
            ApplyFrameCap();
            if (Cli.Step > 0f) Time.captureFramerate = Mathf.Max(1, Mathf.RoundToInt(1f / Cli.Step));
            Time.maximumDeltaTime = Cli.Step > 0f ? 2f : 0.1f;
            Palette.PushGlobals();
            double t0 = Time.realtimeSinceStartupAsDouble;

            // ---- black screen first ----
            var ui = new GameObject("UI").AddComponent<UiOverlay>();
            ui.Init(assets.panelSettings);
            yield return null;
            yield return null;

            // ---- rig ----
            WorldRoot = new GameObject("WorldRoot").transform;
            Truck = new GameObject("Truck").transform;
            Cab = new GameObject("Cab").transform;
            Cab.SetParent(Truck, false);
            var eye = new GameObject("EyeAnchor").transform;
            eye.SetParent(Cab, false);
            eye.localPosition = new Vector3(Config.EyeX, Config.EyeY - Config.NeckDrop, Config.EyeZ - Config.NeckBack);

            var director = gameObject.AddComponent<Director>();
            director.worldRoot = WorldRoot; director.truck = Truck;

            var camGo = new GameObject("Camera") { tag = "MainCamera" };
            Cam = camGo.AddComponent<Camera>();
            Cam.nearClipPlane = Config.NearPlane; Cam.farClipPlane = Config.FarPlane;
            Cam.fieldOfView = Config.FovDefault; Cam.allowHDR = true;
            var hd = camGo.AddComponent<HDAdditionalCameraData>();
            hd.antialiasing = HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing;
            hd.TAAQuality = Cli.Final ? HDAdditionalCameraData.TAAQualityLevel.High : HDAdditionalCameraData.TAAQualityLevel.Medium;
            hd.taaSharpenStrength = 0.4f;
            hd.dithering = true; hd.stopNaNs = true;
            hd.customRenderingSettings = true;
            SetFrame(hd, FrameSettingsField.MotionBlur, false);
            SetFrame(hd, FrameSettingsField.SSR, true);
            SetFrame(hd, FrameSettingsField.SSGI, false);
            SetFrame(hd, FrameSettingsField.ReprojectionForVolumetrics, false);
            SetFrame(hd, FrameSettingsField.Distortion, false);
            SetFrame(hd, FrameSettingsField.Refraction, false);
            SetFrame(hd, FrameSettingsField.Decals, false);
            SetFrame(hd, FrameSettingsField.Volumetrics, true);
            SetFrame(hd, FrameSettingsField.VolumetricClouds, true);
            SetFrame(hd, FrameSettingsField.CustomPass, true);
            camGo.AddComponent<AudioListener>();
            var rig = camGo.AddComponent<CameraRig>();
            rig.cam = Cam; rig.eyeAnchor = eye;
            yield return null;

            // ---- sky, light, land ----
            var atmo = new GameObject("Atmosphere").AddComponent<Atmosphere>();
            atmo.Build();
            new GameObject("Lightning").AddComponent<LightningDirector>();
            Land.Build(WorldRoot);
            yield return null;
            Beats.BuildAll(WorldRoot);
            yield return null;
            TruckLights.Build(Truck);
            Extras.Build(this);
            yield return null;

            // ---- the storm volume ----
            var pv = new GameObject("StormPass").AddComponent<CustomPassVolume>();
            pv.isGlobal = true;
            pv.injectionPoint = CustomPassInjectionPoint.BeforePreRefraction;
            var storm = (SupercellPass)pv.AddPassOfType<SupercellPass>();
            storm.targetColorBuffer = CustomPass.TargetBuffer.None;
            storm.targetDepthBuffer = CustomPass.TargetBuffer.None;
            storm.material = new Material(assets.Shader("Hidden/MESOCYCLONE/Supercell"));
            storm.noise = assets.noise3D;
            storm.scale = Cli.Final ? Tune.F("sc.scaleFinal", 0.7f) : 0.5f;     // the final profile can afford a sharper storm
            yield return null;

            // ---- start; warm up behind black so histories, shaders and the sky settle ----
            Director.Cinematic = Cli.Cinematic;
            Director.Warmup = true;
            director.Begin(Cli.StartAt);
            LightningDirector.ResetClock();
            if (Cli.StartAt > 0f) Audio.AudioRig.Jump();
            gameObject.AddComponent<WorldDriver>();
            for (int i = 0; i < 45; i++) yield return null;
            Debug.Log("[Bootstrap] built in " + (Time.realtimeSinceStartupAsDouble - t0).ToString("F1") + " s, profile " + (Cli.Final ? "final" : "test") +
                      ", " + Screen.width + "x" + Screen.height + ", " + SystemInfo.graphicsDeviceName + " " + SystemInfo.graphicsDeviceType);
            if (Cli.Harness)
            {
                gameObject.AddComponent<GpuGuard>();
                gameObject.AddComponent<Harness>();
            }
            ui.SetLoadingBlack(0f);
            UiOverlay.LockCursor(true);
            Director.Warmup = false;
        }

        static void SetFrame(HDAdditionalCameraData hd, FrameSettingsField f, bool on)
        {
            hd.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)f] = true;
            hd.renderingPathCustomFrameSettings.SetEnabled(f, on);
        }

        /// <summary>Never above 60 fps on any monitor (Part 4B): vsync divisor when the refresh rate allows it,
        /// otherwise the software cap.</summary>
        public static void ApplyFrameCap()
        {
            double hz = Screen.currentResolution.refreshRateRatio.value;
            int div = (int)Math.Round(hz / 60.0);
            if (div >= 1 && Math.Abs(hz / div - 60.0) < 2.0) QualitySettings.vSyncCount = div;
            else QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = Config.TargetFrameRate;
            QualitySettings.maxQueuedFrames = 2;
        }
    }

    /// <summary>Small per-frame jobs of the world that have no better home.</summary>
    [DefaultExecutionOrder(-200)]
    public sealed class WorldDriver : MonoBehaviour
    {
        void Update()
        {
            if (!Director.Running) return;
            Land.Tick(Director.S);
        }
    }
}
