namespace ZERO
{
    /// <summary>Constants of the game that are not part of the timetable (see Schedule) or of the aircraft (see PlaneDef).</summary>
    public static class Config
    {
        // --- camera ---
        public const float FarPlane = 4.6e6f;         // the asteroid is first seen 2 600 km away
        public const float NearPlane = 0.04f;         // the eye can be 15 cm from the window frame
        public const float BaseFov = 62f;
        public const float ZoomFov = 9f;              // Shift / right mouse: looking hard at one thing (a long lens)
        public const float OriginShiftDistance = 1000f;

        // --- the ending (the blast wave takes the aircraft) ---
        public const float EndBreath = 1.2f;          // the wall is on the aircraft: light and sound are sucked away
        public const float ResumeDuration = 1.2f;     // the strike, from the first blow to full white
        public const float WhiteStart = 0.75f;        // seconds into the strike
        public const float WhiteRamp = 0.3f;          // >= 0.15 s (photosensitivity)
        public const float WhiteHold = 1.5f;
        public const float FadeToBlack = 4f;

        // --- budgets (final profile) ---
        public const int ArcCount = 420;
        public const int ReentryStreaks = 6500;
    }
}
