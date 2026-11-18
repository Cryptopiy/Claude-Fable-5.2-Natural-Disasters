using UnityEngine;

namespace CASCADE
{
    /// <summary>Catastrophe events. Each is triggered when the player passes a chainage and then runs on its own clock.</summary>
    public enum Ev
    {
        Gulls = 0,   // all gulls take off in silence
        Hum,         // the deep hum begins
        Tremor1,     // 4 s, gentle
        Tremor2,     // 8 s, stronger
        Vista,       // stepping onto the rim promenade (M1)
        Eruption,    // summit flash; everything of beat 3 runs on this clock
        Tremor3,     // violent tremor before the collapse
        Collapse,    // flank slide, 20 s, then the splash dome
        Drawdown,    // the sea leaves, 50 s
        Wave,        // the wave is born at the cone; impact at +WaveCross
        AshStorm,    // visibility collapses
        Glimpse1,    // a clearing in the ash
        Glimpse2,    // second clearing: M4
        Silence,     // wind drops, air clears
        Arrival,     // the door of the keeper's house
        Count
    }

    /// <summary>
    /// The timeline. Story.Time is the story clock (equal to the cinematic clock in cinematic mode).
    /// Everything visual and audible is a pure function of Story.Time and of the event clocks Story.T(e),
    /// so the harness can seek to any moment and get the same picture.
    /// </summary>
    public static class Story
    {
        public const int N = (int)Ev.Count;

        // Nominal trigger times on the cinematic clock, seconds. Trigger chainage = time * BriskSpeed.
        public static readonly float[] Nominal =
        {
            76f,    // Gulls
            82f,    // Hum
            110f,   // Tremor1
            150f,   // Tremor2
            181f,   // Vista
            192f,   // Eruption (3:12)
            304f,   // Tremor3
            308f,   // Collapse
            350f,   // Drawdown (5:50)
            370f,   // Wave (white line visible ~6:20, impact 7:40)
            504f,   // AshStorm
            532f,   // Glimpse1
            562f,   // Glimpse2 (M4)
            600f,   // Silence
            660f,   // Arrival (replaced by the real path length in Init)
        };

        public const float Never = 1e9f;
        static readonly float[] start = new float[N];

        public static float Time;        // seconds since the walk began
        public static float Chainage;    // metres along the path
        public static float MaxChainage;
        public static float Speed;       // current speed along the path, m/s
        public static bool Cinematic;
        public static volatile int FootstepCount;   // incremented by the player rig; read by the audio thread
        public static volatile int FootSurface;     // Surface enum of the last footstep

        public static float TriggerChainage(Ev e) { return Nominal[(int)e] * Config.BriskSpeed; }
        /// <summary>Seconds since the event began; a large negative number before it.</summary>
        public static float T(Ev e) { return Time - start[(int)e]; }
        public static bool On(Ev e) { return Time >= start[(int)e]; }
        public static float StartTime(Ev e) { return start[(int)e]; }

        public static void Init(float pathLength)
        {
            Nominal[(int)Ev.Arrival] = (pathLength - 0.6f) / Config.BriskSpeed;
            Time = 0f; Chainage = 0f; MaxChainage = 0f; Speed = 0f;
            for (int i = 0; i < N; i++) start[i] = Never;
        }

        /// <summary>Chainage of the cinematic camera at cinematic time t (0.5 s ease at the start).</summary>
        public static float CinematicChainage(float t)
        {
            const float ease = 0.5f;
            if (t <= 0f) return 0f;
            if (t < ease) return 0.5f * Config.BriskSpeed * t * t / ease;
            return Config.BriskSpeed * (t - 0.5f * ease);
        }

        /// <summary>Jump to cinematic time t with every event clock consistent for that moment.</summary>
        public static void Seek(float t)
        {
            Time = t;
            Chainage = CinematicChainage(t);
            MaxChainage = Chainage;
            for (int i = 0; i < N; i++) start[i] = Nominal[i] <= t ? Nominal[i] : Never;
        }

        /// <summary>Advance the story clock; fire the events whose chainage has been passed.</summary>
        public static void Step(float dt, float chainage, float speed)
        {
            Time += dt;
            Chainage = chainage;
            Speed = speed;
            if (chainage > MaxChainage) MaxChainage = chainage;
            for (int i = 0; i < N; i++)
                if (start[i] >= Never && MaxChainage >= Nominal[i] * Config.BriskSpeed) start[i] = Time;
        }

        // ---- small helpers shared by visuals and sound ----
        public static float Smooth01(float a, float b, float x) { return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, x)); }

        /// <summary>Tremor envelope 0..1 at the current time (all tremors combined), for camera sway and rattles.</summary>
        public static float TremorLevel()
        {
            float a = Bump(T(Ev.Tremor1), 4f) * 0.35f;
            float b = Bump(T(Ev.Tremor2), 8f) * 0.7f;
            float c = Bump(T(Ev.Tremor3), 7f) * 1.0f;
            float d = Bump(T(Ev.Eruption) - Config.ShockDelay, 2.5f) * 0.55f;                 // the late thump
            float e = Bump(T(Ev.Wave) - Config.WaveCross - Config.ImpactSoundDelay, 6f) * 0.9f; // the impact
            return Mathf.Max(Mathf.Max(a, b), Mathf.Max(c, Mathf.Max(d, e)));
        }

        /// <summary>Smooth bump: 0 before 0, rises fast, decays to 0 at dur.</summary>
        public static float Bump(float t, float dur)
        {
            if (t <= 0f || t >= dur) return 0f;
            float x = t / dur;
            float rise = Mathf.Clamp01(x / 0.18f);
            float fall = 1f - Mathf.Clamp01((x - 0.45f) / 0.55f);
            return rise * rise * (3f - 2f * rise) * fall * fall * (3f - 2f * fall);
        }
    }
}
