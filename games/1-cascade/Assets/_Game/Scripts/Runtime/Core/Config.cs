using UnityEngine;

namespace CASCADE
{
    /// <summary>All constants, in metres and seconds. Sea level is y = 0. +x east, +z north. The caldera lies west of the village.</summary>
    public static class Config
    {
        // ---- player ----
        public const float WalkSpeed = 1.6f;
        public const float BriskSpeed = 2.7f;          // Shift speed == cinematic speed
        public const float BackSpeed = 0.9f;
        public const float BackLimit = 20f;            // how far behind the furthest progress the player may step back
        public const float Accel = 0.3f;               // seconds to reach speed
        public const float EyeHeight = 1.65f;
        public const float BaseFov = 60f;              // vertical; free walk (about 91 degrees across at 16:9, a 21 mm lens)
        public const float NearClip = 0.12f;
        public const float FarClip = 160000f;

        // ---- world ----
        public static readonly Vector3 ConeCenter = new Vector3(-5000f, 0f, -140f);   // caldera centre and volcano axis
        public const float ConeHeight = 1800f;
        public const float ConeBaseRadius = 2300f;     // shoreline radius of the cone
        public const float CalderaA = 4980f;           // inner shoreline ellipse semi-axis east-west
        public const float CalderaB = 3500f;           // inner shoreline ellipse semi-axis north-south
        public const float QuayHeight = 2f;
        public const float QuayEdgeX = -20f;           // quay wall (shoreline) in the village sector
        public const float MaxRunup = 146f;            // the water never rises above this anywhere (the path is above 150 m by then)

        // ---- sun (fictional compass: the sun stands left of the cone as seen from the village) ----
        public const float SunAzimuthFromCone = 38f;   // degrees towards the south (left) of the village->cone direction
        public const float SunElevStart = 17f;
        public const float SunElevEnd = 31f;

        // ---- eruption (clock: Story.T(Ev.Eruption)) ----
        public const float SoundSpeed = 340f;
        public const float ShockDelay = 14.7f;         // 5000 m / 340 m/s: the thump arrives this late
        public const float ColumnT10 = 38f;            // column top at 10 km
        public const float ColumnT20 = 68f;            // 20 km
        public const float ColumnT35 = 98f;            // 35 km
        public const float UmbrellaStart = 55f;        // umbrella begins to spread
        public const float AshfallStart = 68f;
        public const float LampsOn = 66f;              // lamps and windows switch on (staggered over ~15 s)
        public const float SunGone = 118f;             // the sun disc has vanished

        // ---- collapse (clock: Ev.Collapse) ----
        public const float SlideDuration = 20f;
        public const float SplashAt = 18f;             // splash dome begins
        public const float CollapseSoundDelay = 14.5f;

        // ---- drawdown (clock: Ev.Drawdown) and wave (clock: Ev.Wave) ----
        public const float DrawdownDuration = 50f;
        public const float DrawdownDepth = 36f;        // metres at the harbour
        public const float WaveCross = 90f;            // seconds from birth at the cone shore to impact on the village cliffs
        public const float WaveLineVisible = 10f;      // the white line becomes visible
        public const float WavePeakHeight = 255f;      // crest height about 1 km out
        public const float WaveSecondDelay = 36f;      // second, lower wave after the impact
        public const float ImpactSoundDelay = 1.6f;

        // ---- ash storm and ending ----
        public const float StormCloseIn = 14f;         // seconds for visibility to collapse
        public const float GlimpseDuration = 16f;      // a clearing in the ash
        public const float SilenceClear = 16f;         // air clears at the start of beat 7
        public const float EndFade = 6f;
        public const float EndLineAt = 6.5f;           // closing line appears (after arrival)
        public const float EndQuitAt = 14f;            // --quit-at-end leaves this long after arrival

        // ---- UI ----
        public const string Title = "C A S C A D E";
        public const string ControlsHint = "Mouse  look      W / S  walk      Shift  brisk      C  cinematic      Esc  pause";
    }
}
