namespace MESOCYCLONE
{
    /// <summary>All constants of the game, in metres and seconds. One source of truth.</summary>
    public static class Config
    {
        public const string ProductName = "MESOCYCLONE";

        // ---- path and speeds -------------------------------------------------------------------------
        public const double PathLength = 15840.0;       // 24 m/s x 660 s
        public const float SpeedMin = 8f;               // S held
        public const float SpeedDefault = 15f;          // manual default (W / idle)
        public const float SpeedBrisk = 24f;            // Shift, and exactly the cinematic speed
        public const float SpeedAccelTime = 0.3f;       // smooth acceleration
        public const float CinematicEaseIn = 0.5f;      // short ease at the very start
        public const float FinalBrakeTime = 6f;         // the only speed change of the game
        public const float LaneOffset = 1.85f;          // truck drives in the right lane
        public const float RoadHalfWidth = 3.7f;        // two 3.7 m lanes
        public const float ShoulderWidth = 1.6f;
        public const float DriftRadius = 0.25f;         // free drift inside the lane (safety corridor = the road)

        // ---- beats: story time at the cinematic speed --------------------------------------------------
        public const int BeatCount = 8;
        public static readonly float[] BeatStart = { 0f, 90f, 180f, 270f, 360f, 480f, 570f, 630f };
        public static readonly float[] BeatEnd = { 90f, 180f, 270f, 360f, 480f, 570f, 630f, 720f };
        public const float TriggerLead = 30f;           // beats trigger 30 m early so the story clock never stutters
        public const float GoalTime = 663f;             // truck stands under the canopy
        public const float EngineOffTime = 665.5f;
        public const float ClosingLineTime = 668f;
        public const float FadeOutStart = 676f;
        public const float FadeOutEnd = 682f;           // black; Esc available
        public const float StartFadeIn = 2.5f;
        public const float TitleShowTime = 4f;
        public const float HintShowTime = 6f;

        // ---- camera ------------------------------------------------------------------------------------
        public const float FovDefault = 74f;
        public const float FovMin = 68f;
        public const float FovMax = 78f;
        public const float YawLimit = 150f;
        public const float PitchMin = -50f;             // looking down
        public const float PitchMax = 60f;              // looking up
        public const float ShakeMaxDeg = 0.6f;
        public const float SwayMaxDeg = 0.3f;
        public const float NearPlane = 0.06f;
        public const float FarPlane = 90000f;
        public const float MouseSensitivity = 0.085f;   // degrees per mouse count

        // ---- cab frame: origin on the ground under the driver's eye plane, x right, y up, z forward -----
        public const float EyeX = -0.38f;
        public const float EyeY = 1.52f;
        public const float EyeZ = 0f;
        public const float NeckDrop = 0.10f;            // eye sits above and ahead of the neck pivot
        public const float NeckBack = 0.09f;

        // ---- storm scale (impossible scale, on purpose) --------------------------------------------------
        public const float AnvilBase = 9000f;
        public const float AnvilTop = 13000f;
        public const float OvershootTop = 15500f;
        public const float CloudBase = 2800f;           // rain-free base
        public const float WallCloudBase = 1500f;
        public const float ShelfEdgeBase = 700f;
        public const float MesoRadius = 10000f;         // mesocyclone 20 km wide
        public const float TornadoMaxRadius = 1500f;    // wedge 3 km wide at the ground
        public const float SpeedOfSound = 340f;

        // ---- world ---------------------------------------------------------------------------------------
        public const float PoleSpacing = 60f;
        public const float PoleHeight = 11f;
        public const float WireThickness = 0.05f;
        public const float TurbineHub = 100f;
        public const float TurbineBlade = 52f;
        public const float TurbineBlinkPeriod = 2.0f;   // synchronised aviation lights
        public const float TurbineBlinkOn = 0.8f;

        // ---- particles (final profile; the test profile halves them) -------------------------------------
        public const int TornadoParticlesFinal = 300000;
        public const int RainParticlesFinal = 24000;
        public const int HailParticlesFinal = 1600;
        public const int DebrisParticlesFinal = 4000;

        // ---- harness / safety ------------------------------------------------------------------------------
        public const int TargetFrameRate = 60;
        public const float GuardWarmup = 20f;
        public const float GuardFrameLimit = 0.25f;     // three such frames in a row -> quit with code 7
        public const float GuardAvgLimit = 0.10f;       // 2 s average
        public const int GuardExitCode = 7;

        public static int BeatAt(float t)
        {
            for (int i = BeatCount - 1; i >= 0; i--) if (t >= BeatStart[i]) return i;
            return 0;
        }

        public static double BeatTriggerChainage(int beat) => BeatStart[beat] * SpeedBrisk - TriggerLead;
    }
}
