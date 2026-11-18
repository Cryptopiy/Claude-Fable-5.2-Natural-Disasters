using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ZERO
{
    /// <summary>Entry point. Builds the whole world behind a black screen, warms it up, then runs the flight:
    /// input, the event clock, the passenger, the ending, the per-frame globals, the harness.</summary>
    [DefaultExecutionOrder(-100)]
    public class Bootstrap : MonoBehaviour
    {
        public static Bootstrap I;
        public GameAssets Assets;
        public Camera Cam;
        public Light Sun;

        public bool Walking;                 // the flight has begun (the clock runs)
        public bool Paused;
        public float VramBeforeLoad = -1f;
        public int ArcCount, SpriteCount;
        public Transform WorldRoot, PlaneRoot;

        SeatRig rig; UiOverlay ui; HarnessRunner harness;
        float fadeIn = -1f;
        static readonly System.Action<AudioEventKind, float, float, float> audioEmit = AudioEngine.Event;

        void Awake()
        {
            I = this; GameAssets.I = Assets;
            Harness.Parse();
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            QualitySettings.vSyncCount = 0; Application.targetFrameRate = 60; Application.runInBackground = true;
        }

        IEnumerator Start()
        {
            if (Harness.Perf) VramBeforeLoad = HarnessRunner.QueryVram();
            var uiGo = new GameObject("UI"); ui = uiGo.AddComponent<UiOverlay>(); ui.Build(Assets.Panel);
            ui.Black = 1f;
            ui.OnResume = () => SetPaused(false); ui.OnQuit = () => Application.Quit(0);
            yield return null;

            WorldRoot = new GameObject("WorldRoot").transform;
            PlaneRoot = new GameObject("PlaneRoot").transform;
            FloatingOrigin.Init(WorldRoot);
            ShaderGlobals.InitConstants();
            RenderSettings.skybox = Assets.Sky; RenderSettings.fog = false;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight = Color.black;
            if (Sun != null) { Sun.shadows = LightShadows.None; Sun.transform.rotation = Quaternion.LookRotation(-ShaderGlobals.SunDir, Vector3.up); }

            rig = Cam.gameObject.AddComponent<SeatRig>(); rig.Cam = Cam; rig.Plane = PlaneRoot;
            Cam.nearClipPlane = Config.NearPlane; Cam.farClipPlane = Config.FarPlane; Cam.fieldOfView = Config.BaseFov;
            Cam.allowHDR = true;
            QualityProfile.Apply(Cam);
            CameraFX.Init(Assets.Volume);
            WorldTime.Reset(0); rig.Place(0f);
            ShaderGlobals.Tick(0f, rig.EyeAbs, PlaneRoot);
            yield return null;

            // ---- build the world, one system per frame, behind black ----
            float t0 = Time.realtimeSinceStartup;
            foreach (var step in WorldBuilder.Steps(this))
            {
                if (!Harness.Builds(step.Key)) continue;
                float ts = Time.realtimeSinceStartup;
                step.Value();
                Debug.Log("[build] " + step.Key + " " + ((Time.realtimeSinceStartup - ts) * 1000f).ToString("0") + " ms");
                yield return null;
            }
            WorldBuilder.Finish(this);
            Debug.Log("[build] world total " + (Time.realtimeSinceStartup - t0).ToString("0.0") + " s, ejecta arcs " + ArcCount + ", sprites " + SpriteCount);

            // ---- warm-up: visit moments of the event so every shader variant and mesh is on the GPU ----
            bool shots = Harness.ShotList.Count > 0;
            if (!shots)
            {
                WorldBuilder.BeginWarmup(Cam);
                float[] moments = { 0f, 200f, 335f, 358f, 361f, 364f, 380f, 420f, 500f, 600f, 646f };
                for (int i = 0; i < moments.Length; i++)
                {
                    WorldTime.Reset(moments[i]);
                    rig.SetView(-88f + (i % 3) * 30f - 30f, -6f + (i % 2) * 30f, Config.BaseFov, 0.8f);
                    rig.Place(moments[i]);
                    Snap(); FrameGlobals(0.016f);
                    ZeroPostFeature.Strength = 0.02f;       // the fullscreen pass is compiled and bound now, not at the strike
                    yield return null; yield return null;
                }
                WorldBuilder.EndWarmup();
            }

            // ---- start ----
            float startT = Mathf.Max(0f, Harness.StartAt);
            WorldTime.Reset(startT);
            Ending.Reset();
            AudioDirector.Reset(startT);
            rig.SetView(-84f, -3f, Config.BaseFov, 0.45f);
            rig.Place(startT);
            rig.Cinematic = false; if (Harness.Cinematic) rig.SetCinematic(true);
            Snap(); FrameGlobals(0.016f);
            yield return null; yield return null;

            harness = gameObject.AddComponent<HarnessRunner>();
            gameObject.AddComponent<GpuGuard>();
            Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
            WorldTime.Running = true; Walking = true;
            if (shots || Harness.StartAt > 0f) { ui.Black = 0f; fadeIn = -1f; }
            else { fadeIn = 0f; ui.ShowTitle(); ui.HintAllowed = !Harness.Cinematic && !Harness.Any; }
            if (Harness.Any) harness.Begin();
        }

        void SetPaused(bool p)
        {
            Paused = p; ui.SetPause(p);
            Cursor.lockState = p ? CursorLockMode.None : CursorLockMode.Locked; Cursor.visible = p;
        }

        void Update()
        {
            if (!Walking) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.escapeKey.wasPressedThisFrame) SetPaused(!Paused);
                if (Paused && kb.qKey.wasPressedThisFrame) Application.Quit(0);
                if (kb.f11Key.wasPressedThisFrame) ToggleFullscreen();
                if (!Paused && kb.cKey.wasPressedThisFrame && Ending.State == Ending.Phase.None)
                {
                    rig.SetCinematic(!rig.Cinematic);
                    if (!Harness.Any) ui.ShowCinematic();
                    ui.HideHint();
                }
            }
            if (Paused)
            {
                var ps = new AudioState { endPhase = Ending.AudioPhase, masterGain = 0f };
                AudioDirector.Fill(ref ps, (float)WorldTime.Now);
                AudioEngine.Push(ps);
                return;
            }

            WorldTime.Advance(dt);
            float T = (float)WorldTime.Now;
            rig.Tick(dt, T);
            Ending.Tick(dt, T, rig, ui);
            FrameGlobals(dt);

            if (fadeIn >= 0f)
            {
                fadeIn += dt;
                float k = Mathf.Clamp01((fadeIn - 0.6f) / 2.5f); ui.Black = 1f - k * k * (3f - 2f * k);
                if (fadeIn > 3.6f && fadeIn - dt <= 3.6f) ui.ShowHint();
                if (fadeIn > 12f) fadeIn = -1f;
            }
            if (rig.HadInput || rig.Cinematic) ui.HideHint();
            ui.Tick(dt);

            float master = Mathf.Clamp01(T / 2.5f);
            if (Harness.StartAt > 0f) master = Mathf.Clamp01((T - Harness.StartAt) / 0.5f);
            var au = new AudioState { endPhase = Ending.AudioPhase, masterGain = master };
            AudioDirector.Fill(ref au, T);
            AudioEngine.Push(au);
            if (Ending.State == Ending.Phase.None) AudioDirector.Tick(T, audioEmit);

            if (Ending.State == Ending.Phase.Done && Harness.QuitAtEnd && harness != null) harness.Finish();
        }

        bool snap;
        /// <summary>After a jump in time: the next frame converges every smoothed value at once.</summary>
        public void Snap() { snap = true; }

        void FrameGlobals(float dt)
        {
            float T = (float)WorldTime.Now;
            ShaderGlobals.Tick(T, rig.EyeAbs, PlaneRoot);
            WorldBuilder.Tick(this, T, rig.EyeAbs, dt, snap);
            CameraFX.Tick(Cam, PlaneRoot, T, dt, snap);
            snap = false;
        }

        void ToggleFullscreen()
        {
            if (Screen.fullScreen) Screen.SetResolution(1920, 1080, FullScreenMode.Windowed);
            else { var r = Screen.currentResolution; Screen.SetResolution(r.width, r.height, FullScreenMode.FullScreenWindow); }
        }
    }
}
