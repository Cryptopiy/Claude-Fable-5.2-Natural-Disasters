using UnityEngine;

namespace ZERO
{
    /// <summary>The timetable of the event, seen from an aircraft. T = seconds since the flight began; the world runs in
    /// real time. Everything here is a pure function of T, so any moment can be jumped to and replayed.
    /// Coordinates are "flat map" metres: x east, z north, y height above the ground (the curvature of the Earth is
    /// applied when drawing). The aircraft flies north (+z), level; the asteroid lands to its left.</summary>
    public static class Schedule
    {
        // ---- the flight ----
        public const float Altitude = 11500f;
        public const float AirSpeed = 235f;
        public const float EarthRadius = 6371000f;

        // ---- the asteroid ----
        public const float ContactTime = 360f;            // 6:00 it touches the ground
        public const float AstSpeed = 7500f;              // m/s along its trajectory (constant: nothing slows it)
        public const float AstAzimuthDeg = -62f;          // seen from the impact site it comes from the west-north-west (out of the left window)...
        public const float AstElevationDeg = 30f;         // ...down a 30 degree slope
        public const float AstScale = 34f;                // the body is 68 x 48 x 41 km (dream scale: it must dwarf the moon in the sky)
        public const float EntryAltitude = 120000f;       // below this it burns
        public static readonly Vector3 Site = new Vector3(-256000f, 0f, 45000f);
        public static readonly Vector3 AstAxis = Axis();  // unit, back up the trajectory

        static Vector3 Axis()
        {
            float az = AstAzimuthDeg * Mathf.Deg2Rad, el = AstElevationDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(az) * Mathf.Cos(el), Mathf.Sin(el), Mathf.Cos(az) * Mathf.Cos(el));
        }

        /// <summary>Event time at which the leading point of the body is at a given height.</summary>
        public static float TimeAtHeight(float h) { return ContactTime - h / (AstAxis.y * AstSpeed); }
        public static float EntryTime { get { return TimeAtHeight(EntryAltitude); } }      // 5:28

        public static Vector3 PlanePos(float T) { return new Vector3(0f, Altitude, AirSpeed * (T - ContactTime)); }

        /// <summary>Metres the leading point of the body still has to travel (0 from the contact on).</summary>
        public static float Descent(float T) { return Mathf.Max(0f, (ContactTime - T) * AstSpeed); }

        /// <summary>Metres the body has driven into the ground since the contact (it is 52 km long and does not stop at once).</summary>
        public static float Burial(float T) { float t = Mathf.Max(0f, T - ContactTime); return Mathf.Min(t * AstSpeed * (1f + 0.5f * t), 70000f); }     // it vanishes into its own fireball within about four seconds

        /// <summary>The leading point of the body (flat map coordinates).</summary>
        public static Vector3 AstTip(float T) { return Site + AstAxis * (Descent(T) - Burial(T)); }

        public static float SiteDistance(float T)
        {
            Vector3 p = PlanePos(T);
            return new Vector2(p.x - Site.x, p.z - Site.z).magnitude;
        }

        // ---- the blast wave in the air: fast at first, about 550 m/s when it reaches the aircraft ----
        public const float ArrivalDelay = 288f;
        public const float ArrivalTime = ContactTime + ArrivalDelay;      // 10:48 the wave reaches the aircraft
        public const float BowLead = 6f;                  // seconds before the contact when the air crushed ahead of the body reaches the ground
        public const float BowRadius = 26000f;            // it clears the clouds over the place where the body will land
        public static float FrontRadius(float T)
        {
            float t = T - ContactTime;
            if (t <= -BowLead) return 0f;
            float bow = BowRadius * Mathf.SmoothStep(0f, 1f, (t + BowLead) / BowLead);
            if (t <= 0f) return bow;
            return Mathf.Max(bow, SiteDistance(ArrivalTime) * Mathf.Pow(t / ArrivalDelay, 0.62f));
        }
        /// <summary>Signed distance from the aircraft to the blast wave (positive while it is still coming).</summary>
        public static float FrontDist(float T) { return T < ContactTime ? 1e6f : SiteDistance(T) - FrontRadius(T); }

        // ---- the explosion ----
        /// <summary>Radius of the incandescent fireball.</summary>
        public static float FireballRadius(float T)
        {
            float t = T - ContactTime;
            return t <= 0f ? 0f : 62000f * (1f - Mathf.Exp(-t / 7f)) + 160f * t;
        }
        /// <summary>Height of the top of the plume that leaves the atmosphere.</summary>
        public static float PlumeTop(float T)
        {
            float t = T - ContactTime;
            return t <= 0f ? 0f : 340000f * (1f - Mathf.Exp(-t / 70f));
        }
        /// <summary>0..1 light of the explosion as the aircraft sees it: a ramp at the contact (never a flash), a long decay.</summary>
        public static float Flash(float T)
        {
            float t = T - ContactTime;
            if (t <= 0f) return 0f;
            float ramp = Mathf.SmoothStep(0f, 1f, t / 0.9f);
            return ramp * (0.22f + 0.78f * Mathf.Exp(-t / 28f));
        }
        /// <summary>0..1 ejecta falling back through the upper air all over the sky.</summary>
        public static float Reentry(float T) { return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(ContactTime + 70f, ContactTime + 210f, T)); }
        /// <summary>0..1 how hard the airframe shakes.</summary>
        public static float Shake(float T)
        {
            float d = FrontDist(T);
            float pre = T < ContactTime ? 0f : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(70000f, 6000f, d));
            float hit = T < ContactTime ? 0f : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(6000f, 0f, d));
            return Mathf.Clamp01(0.02f + 0.22f * pre + 0.76f * hit);
        }
        /// <summary>0..1 the light of the body itself before the contact (it burns from the entry on; before that it only shines).</summary>
        public static float BodyGlow(float T)
        {
            if (T >= ContactTime) return 1f;
            float far = Mathf.InverseLerp(0f, EntryTime, T);                     // a star that grows
            float burn = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(EntryTime - 6f, EntryTime + 10f, T));
            return Mathf.Clamp01(0.12f + 0.2f * far * far + 0.68f * burn);
        }
    }
}
