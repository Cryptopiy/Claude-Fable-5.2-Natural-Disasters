using UnityEngine;

namespace MESOCYCLONE
{
    /// <summary>Hard points of the pickup cab, shared by the cab geometry, the glass and weather systems and the
    /// camera. Cab frame: origin on the ground under the driver's eye plane, x right, y up, z forward, metres.
    /// Left-hand drive: the driver sits on the left.</summary>
    public static class CabDims
    {
        public static readonly Vector3 Eye = new Vector3(Config.EyeX, Config.EyeY, Config.EyeZ);

        // ---- windscreen: a flat raked quad (about 36 degrees from vertical) ----
        public static readonly Vector3 WBL = new Vector3(-0.74f, 1.18f, 0.78f);   // bottom left (driver side)
        public static readonly Vector3 WBR = new Vector3(0.74f, 1.18f, 0.78f);
        public static readonly Vector3 WTL = new Vector3(-0.66f, 1.80f, 0.33f);
        public static readonly Vector3 WTR = new Vector3(0.66f, 1.80f, 0.33f);
        public const float GlassWidth = 1.48f;                                     // metres along the bottom edge
        public const float GlassHeight = 0.766f;                                   // metres along the slope

        /// <summary>Point on the windscreen plane from glass coordinates: u metres to the right of the bottom centre,
        /// v metres up the slope from the bottom edge.</summary>
        public static Vector3 GlassPoint(float u, float v)
        {
            Vector3 up = (new Vector3(0f, WTL.y - WBL.y, WTL.z - WBL.z)).normalized;
            return new Vector3(u, WBL.y, WBL.z) + up * v;
        }

        public static Vector3 GlassNormalOut => Vector3.Cross(Vector3.right, (WTL - WBL).normalized).normalized * -1f;   // points outward (forward-up)

        // ---- wipers: tandem, parked pointing to the passenger side, sweeping up toward the driver's side ----
        public static readonly Vector3 WiperPivotL = new Vector3(-0.42f, 1.165f, 0.80f);
        public static readonly Vector3 WiperPivotR = new Vector3(0.22f, 1.165f, 0.80f);
        public const float WiperArm = 0.50f;                                       // pivot to blade tip
        public const float WiperBladeFrom = 0.14f;                                 // blade starts this far from the pivot
        public const float WiperParkDeg = 4f;                                      // angle in the glass plane from +x
        public const float WiperSweepDeg = 96f;                                    // parked + sweep = full stroke

        /// <summary>Wiper angle in degrees (in the glass plane, from +x toward the top) at story time T.</summary>
        public static float WiperAngle(float T) => WiperParkDeg + WiperSweepDeg * WiperModel.Position(T);

        // ---- interior ----
        public static readonly Vector3 WheelCentre = new Vector3(-0.38f, 1.02f, 0.36f);
        public const float WheelRadius = 0.19f;
        public const float WheelTiltDeg = 25f;                                     // top leaning forward
        public static readonly Vector3 ClusterCentre = new Vector3(-0.38f, 1.13f, 0.50f);   // gauges, facing the driver
        public static readonly Vector3 RadioCentre = new Vector3(0.0f, 0.98f, 0.40f);       // amber dial
        public static readonly Vector3 MirrorCentre = new Vector3(0.0f, 1.70f, 0.36f);      // rear-view mirror 0.24 x 0.07
        public const float DashTopY = 1.17f;                                       // dash top at the base of the glass
        public const float DashBackZ = 0.42f;                                      // dash edge nearest the driver
        public const float RoofY = 1.84f;                                          // outer roof; liner at 1.80
        public const float CabHalfWidth = 0.80f;                                   // inner door surface at x = +-0.80
        public const float RearWallZ = -1.10f;
        public const float BPillarZ = -0.46f;

        // ---- side and rear glass (flat rectangles) ----
        public const float SideGlassZ0 = -0.42f, SideGlassZ1 = 0.66f, SideGlassY0 = 1.12f, SideGlassY1 = 1.74f;
        public const float SideGlassFrontSlant = 0.58f;                            // the front edge leans back with the A-pillar: z_front(y) = Z1 - slant * (y - Y0)
        public const float RearGlassX = 0.62f, RearGlassY0 = 1.20f, RearGlassY1 = 1.72f;

        // ---- outside ----
        public const float HoodZ0 = 0.84f, HoodZ1 = 2.78f;                         // cowl to nose
        public const float HoodY0 = 1.15f, HoodY1 = 1.06f;                         // height at the cowl and at the nose
        public const float HoodHalfWidth = 0.86f;
        public static readonly Vector3 SideMirrorL = new Vector3(-0.99f, 1.22f, 0.55f);
        public static readonly Vector3 SideMirrorR = new Vector3(0.99f, 1.22f, 0.55f);
        public const float BedZ0 = -1.18f, BedZ1 = -3.05f, BedTopY = 1.30f;        // pickup bed behind the cab

        /// <summary>Height of the hood surface at a point (cab frame); the hail bounces on it.</summary>
        public static float HoodY(float x, float z)
        {
            float t = Mathf.InverseLerp(HoodZ0, HoodZ1, z);
            float crown = 0.035f * (1f - (x / HoodHalfWidth) * (x / HoodHalfWidth));
            return Mathf.Lerp(HoodY0, HoodY1, t * t * 0.6f + t * 0.4f) + crown;
        }
    }
}
