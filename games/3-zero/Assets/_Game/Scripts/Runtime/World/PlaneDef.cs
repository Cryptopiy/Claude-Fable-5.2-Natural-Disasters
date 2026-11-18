using UnityEngine;

namespace ZERO
{
    /// <summary>The aircraft the player sits in: dimensions shared by the mesh builder, the camera and the cabin shader.
    /// Plane-local coordinates in metres: +Z = nose, +Y = up, +X = right (starboard). The origin is on the cabin floor,
    /// on the centre line, at the player's seat row. The player sits in the left window seat, ahead of the wing.</summary>
    public static class PlaneDef
    {
        // ---- cabin cross-section ----
        public const float FloorHalfWidth = 1.62f;
        public const float WallX = 1.80f;            // inner wall panel at the window belt (|x|)
        public const float SkinX = 1.94f;            // outer skin at the window belt (|x|): the window tunnel is 0.14 m deep
        public const float CeilingY = 2.18f;         // centre of the ceiling panel
        public const float BinBottomY = 1.66f;       // underside of the overhead bins
        public const float BinInnerX = 1.02f;        // bins reach from the wall to here (|x|)

        // ---- windows (both sides; the wall around them is the cabin lining) ----
        public const float WindowPitch = 0.5334f;
        public const float WindowZ0 = 0.20f;         // centre of the player's window (the others are at WindowZ0 + k * WindowPitch)
        public const float WindowY = 1.10f;          // centre height
        public const float WindowW = 0.36f, WindowH = 0.46f;   // clear opening (generous: the view is the game)
        public const float WindowCorner = 0.13f;     // corner radius of the opening
        public const float BezelW = 0.46f, BezelH = 0.56f;     // the lining flares out to this size on the cabin side

        // ---- seats (3 + 3) ----
        public const float SeatPitch = 0.79f;
        public const float SeatZ0 = -0.12f;          // centre of the seat cushion of the player's row (rows at SeatZ0 + k * SeatPitch)
        public const float SeatWidth = 0.46f;
        public const float AisleHalfWidth = 0.26f;
        public const float SeatBackTopY = 1.14f;
        public static readonly Vector3 Eye = new Vector3(-1.38f, 1.17f, 0f);     // the player's eyes, sitting upright

        // ---- the cabin section that is built ----
        public const float CabinZMin = -5.2f, CabinZMax = 6.4f;

        // ---- the left wing (the player looks back along its leading edge) and its engine ----
        public static readonly Vector3 WingRoot = new Vector3(-1.94f, -0.55f, -1.0f);   // leading edge at the fuselage side, top surface
        public const float WingRootChord = 6.4f, WingTipChord = 1.7f;
        public const float WingSemiSpan = 15.6f;     // from the fuselage side to the tip, along -X
        public const float WingSweepDeg = 25f;       // leading edge sweep (the tip is farther aft)
        public const float WingDihedralDeg = 6.5f;   // includes the flex of cruise flight: the tip rides near the player's eye level
        public const float WingletHeight = 2.4f;
        public static readonly Vector3 NacelleCentre = new Vector3(-5.75f, -1.75f, 0.2f);  // centre of the engine nacelle
        public const float NacelleRadius = 1.12f, NacelleLength = 4.6f;                    // its intake lip is at z = centre.z + length / 2
    }
}
