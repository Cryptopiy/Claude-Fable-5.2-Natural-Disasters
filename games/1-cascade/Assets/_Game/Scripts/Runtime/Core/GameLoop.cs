using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CASCADE
{
    /// <summary>
    /// Owns the order of a frame: input, player, story clock, environment, effects, UI, captures.
    /// Everything is built at start behind a black screen; nothing is generated inside the walk.
    /// </summary>
    public class GameLoop : MonoBehaviour
    {
        public static GameLoop I;
        public PlayerRig rig;
        public EnvRig env;
        public EnvDirector director;
        public GameUI ui;
        public World world;
        QaTools qa; CaptureTool capture;

        bool lineSet;
        static readonly string[] Numbers = { "Zero", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine", "Ten", "Eleven", "Twelve", "Thirteen", "Fourteen", "Fifteen",
            "Sixteen", "Seventeen", "Eighteen", "Nineteen", "Twenty", "Twenty-one", "Twenty-two", "Twenty-three", "Twenty-four", "Twenty-five", "Twenty-six", "Twenty-seven", "Twenty-eight", "Twenty-nine", "Thirty", "Many" };
        enum State { Loading, Running, Done }
        State state = State.Loading;
        bool paused, frozen;
        float startReal;
        bool quitting;

        void Awake()
        {
            I = this;
            Harness.Parse();
            QualitySettings.vSyncCount = Mathf.Clamp(Mathf.RoundToInt((float)Screen.currentResolution.refreshRateRatio.value / 60f), 1, 4);
            Application.targetFrameRate = 60;
            Application.runInBackground = true;
            Palette.PushToShaders();
            Shader.SetGlobalTexture("_CNoise3D", GameAssets.I.Tex("T_PlumeNoise"));
            Shader.SetGlobalTexture("_CNoise2D", GameAssets.I.Tex("T_Noise2D"));
            Shader.SetGlobalTexture("_CWaterNormal", GameAssets.I.Tex("T_WaterNormal"));
            Shader.SetGlobalTexture("_COcean", GameAssets.I.Tex("T_OceanSlopes"));
            Shader.SetGlobalTexture("_CFoam", GameAssets.I.Tex("T_Foam"));
            PathDef.Build();
            WorldGen.Build();
            Story.Init(PathDef.Length);
            Story.Cinematic = Harness.Cinematic;
        }

        IEnumerator Start()
        {
            env = EnvRig.Create(transform, Harness.FinalProfile);
            director = new EnvDirector { rig = env };
            var uigo = new GameObject("UI");
            uigo.transform.SetParent(transform, false);
            ui = uigo.AddComponent<GameUI>();
            ui.Init(GameAssets.I.panel);
            ui.Black = 1f;
            ui.Tick(0f, -1f, 0f, false, true);
            qa = gameObject.AddComponent<QaTools>();
            capture = gameObject.AddComponent<CaptureTool>();

            AudioDirector.Create(env.cam.gameObject);
            rig = env.cam.gameObject.AddComponent<PlayerRig>();
            rig.cam = env.cam;
            rig.Cinematic = Harness.Cinematic;
            rig.S = 0f;
            rig.FaceAlongPath();
            rig.Place(0f, Harness.Cinematic);
            director.Apply();
            yield return null;

            float t0 = Time.realtimeSinceStartup;
            world = new World();
            IEnumerator build = world.Build(transform, Harness.Iso, Harness.FinalProfile);
            while (build.MoveNext()) yield return null;
            Debug.Log("CASCADE_INFO world built in " + (Time.realtimeSinceStartup - t0).ToString("F1") + " s, path " + PathDef.Length.ToString("F1") +
                      " m (quay " + PathDef.SQuayEnd.ToString("F0") + ", narrow " + PathDef.SNarrowStart.ToString("F0") + ", rim " + PathDef.SStairEnd.ToString("F0") + ", headland " + PathDef.SPromEnd.ToString("F0") + ")");

            // warm-up behind black: let every material, variant and temporal history settle
            world.Tick(0f);
            for (int i = 0; i < 24; i++) { director.Apply(); world.Tick(0f); yield return null; }

            if (Harness.StartAt > 0f) SeekTo(Harness.StartAt);
            startReal = Time.realtimeSinceStartup;
            if (Harness.StartAt > 0f || capture.UsingShots || Harness.Iso.Length > 0) startReal -= 10f;   // no fade-in when jumping into the middle
            ui.Black = 0f;
            qa.MarkStart();
            capture.Begin();
            if (!Harness.Any) Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = Harness.Any;
            state = State.Running;
            Debug.Log("CASCADE_INFO walk started");
        }

        public void SeekTo(float t)
        {
            Story.Seek(t);
            rig.Cinematic = true;
            rig.Place(Story.Chainage, true);
            frozen = capture != null && capture.UsingShots;
        }

        void Update()
        {
            if (state == State.Loading) return;
            float real = Time.realtimeSinceStartup;
            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            var kb = Keyboard.current;

            if (kb != null && !quitting)
            {
                if (kb.f11Key.wasPressedThisFrame) ToggleFullscreen();
                if (kb.escapeKey.wasPressedThisFrame) SetPaused(!paused);
                if (!paused && kb.cKey.wasPressedThisFrame)
                {
                    rig.SetCinematic(!rig.Cinematic);
                    Story.Cinematic = rig.Cinematic;
                    ui.NoteCinematicToggled(real);
                    ui.DismissHint();
                }
                if (!paused && (kb.wKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame)) ui.DismissHint();
            }
            if (paused)
            {
                int a = ui.PauseInput();
                if (a == 1) SetPaused(false);
                else if (a == 2) Finish(0);
            }

            if (!paused && !frozen && state == State.Running)
            {
                float sdt = dt * Harness.TimeScale;
                rig.Step(sdt, true);
                Story.Step(sdt, rig.S, rig.V);
            }
            director.Apply();
            world.Tick(paused || frozen ? 0f : dt);
            float sinceArrival = Story.On(Ev.Arrival) ? Story.T(Ev.Arrival) : -1f;
            if (sinceArrival >= 0f && !lineSet)
            {
                // the closing line: the minutes since the first tremor
                lineSet = true;
                float t1 = Story.StartTime(Ev.Tremor1);
                int minutes = Mathf.Clamp(Mathf.RoundToInt((Story.Time - (t1 < Story.Never ? t1 : 0f)) / 60f), 1, 60);
                ui.SetLine(Numbers[Mathf.Min(minutes, Numbers.Length - 1)] + (minutes == 1 ? " minute" : " minutes") + " since the first tremor.");
            }
            ui.Tick(real - startReal, sinceArrival, real, rig.Cinematic, Harness.Any);
            capture.Tick();

            if (quitting) return;
            if (capture.UsingShots && capture.ShotsDone) Finish(0);
            else if (Harness.EndAt > 0f && Story.Time >= Harness.EndAt) { if (Harness.QuitAtEnd) Finish(0); else frozen = true; }
            else if (Harness.QuitAtEnd && sinceArrival > Config.EndQuitAt) Finish(0);
        }

        void SetPaused(bool p)
        {
            if (Story.On(Ev.Arrival) && Story.T(Ev.Arrival) > Config.EndLineAt + 3f && !paused && p == true) { /* still allow the pause overlay at the end */ }
            paused = p;
            ui.ShowPause(p);
            AudioListener.pause = p;
            if (!Harness.Any) { Cursor.lockState = p ? CursorLockMode.None : CursorLockMode.Locked; Cursor.visible = p; }
        }

        void ToggleFullscreen()
        {
            if (Screen.fullScreen) Screen.SetResolution(1920, 1080, FullScreenMode.Windowed);
            else { var r = Screen.currentResolution; Screen.SetResolution(r.width, r.height, FullScreenMode.FullScreenWindow); }
        }

        void OnDestroy() { if (ParticleDirector.I != null) ParticleDirector.I.Dispose(); }

        void Finish(int code)
        {
            if (quitting) return;
            quitting = true;
            StartCoroutine(QuitSoon(code));
        }

        IEnumerator QuitSoon(int code)
        {
            for (int i = 0; i < 6; i++) yield return null;      // let the last screenshot reach the disk
            qa.WriteReports();
            Debug.Log("CASCADE_INFO quitting, story time " + Story.Time.ToString("F1"));
            Application.Quit(code);
        }
    }
}
