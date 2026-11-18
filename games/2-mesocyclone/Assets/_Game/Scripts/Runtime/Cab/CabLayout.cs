using UnityEngine;

namespace MESOCYCLONE
{
    /// <summary>Frames and edge functions derived from CabDims, shared by the cab builders.</summary>
    public static class CabLayout
    {
        // windscreen plane: origin at the bottom centre, u = +x, v = up the slope, n = outward (forward-up)
        public static readonly Vector3 GlassOrigin = new Vector3(0f, CabDims.WBL.y, CabDims.WBL.z);
        public static readonly Vector3 GlassUp = new Vector3(0f, CabDims.WTL.y - CabDims.WBL.y, CabDims.WTL.z - CabDims.WBL.z).normalized;
        public static readonly Vector3 GlassOut = Vector3.Cross(Vector3.right, GlassUp).normalized;

        // steering wheel / instrument plane: tilted, top leaning forward; the axis points away from the driver
        static readonly float Tilt = CabDims.WheelTiltDeg * Mathf.Deg2Rad;
        public static readonly Vector3 WheelUp = new Vector3(0f, Mathf.Cos(Tilt), Mathf.Sin(Tilt));
        public static readonly Vector3 WheelAxis = new Vector3(0f, -Mathf.Sin(Tilt), Mathf.Cos(Tilt));

        // centre stack face: through the radio, leaning 8 degrees
        public const float StackTilt = 0.14054f;                       // tan(8 deg)
        public static readonly Vector3 StackUp = new Vector3(0f, 1f, StackTilt).normalized;
        public const float StackHalf = 0.13f;

        // dash sections along x
        public const float DrvX0 = -0.64f, DrvX1 = -0.13f;
        public const float DialY = -0.07f;                             // dial centres below ClusterCentre, on the instrument plane
        public const float DialBigX = 0.075f, DialSmallX = 0.172f, DialBigR = 0.052f, DialSmallR = 0.028f, DialSmallY = -0.085f;

        // interior heights
        public const float FloorY = 0.50f, LinerY = 1.80f, Inner = 0.795f;      // trim surfaces sit 5 mm inside the door plane
        public const float SealW = 0.025f;
        public static readonly Vector3 Mid = new Vector3(0f, 1.25f, -0.15f);    // a point in the middle of the cabin

        /// <summary>Half width of the windscreen at height v (metres up the slope).</summary>
        public static float GlassHalf(float v) { return CabDims.WBR.x - (CabDims.WBR.x - CabDims.WTR.x) * v / CabDims.GlassHeight; }

        /// <summary>Point of the windscreen plane (u right of the centre, v up the slope, d outward).</summary>
        public static Vector3 Glass(float u, float v, float d = 0f) { return GlassOrigin + Vector3.right * u + GlassUp * v + GlassOut * d; }

        /// <summary>Front (slanted) edge of the side glass at height y.</summary>
        public static float SideFront(float y) { return CabDims.SideGlassZ1 - CabDims.SideGlassFrontSlant * (y - CabDims.SideGlassY0); }

        /// <summary>Point on the instrument plane (x right, y up the plane, d away from the driver).</summary>
        public static Vector3 Cluster(float x, float y, float d = 0f) { return CabDims.ClusterCentre + Vector3.right * x + WheelUp * y + WheelAxis * d; }

        /// <summary>z of the centre-stack face at height y.</summary>
        public static float StackZ(float y) { return CabDims.RadioCentre.z + StackTilt * (y - CabDims.RadioCentre.y); }
    }
}
