using System;
using UnityEngine;

namespace ZERO
{
    /// <summary>The gaze choreography of the cinematic mode over the event clock (docs/CINEMATIC_BIBLE.md).
    /// Angles are in the aircraft's frame: yaw 0 = nose, -90 = straight out of the left window, pitch + = up.
    /// lean 0 = sitting back (the window frame and the cabin are in the picture), 1 = at the glass.</summary>
    public static class GazeCues
    {
        public static void Build()
        {
            CinematicDirector.Clear();
            float c = Schedule.ContactTime, e = Schedule.EntryTime, a = Schedule.ArrivalTime;
            Func<Vector3> body = () => Schedule.AstTip((float)WorldTime.Now);
            Func<Vector3> site = () => Schedule.Site;
            Func<Vector3> ball = () => ShaderGlobals.LightAbs;
            Func<Vector3> plume = () => Schedule.Site + Vector3.up * (Schedule.PlumeTop((float)WorldTime.Now) * 0.62f);
            Func<Vector3> mushroom = () => Schedule.Site + Vector3.up * (Schedule.PlumeTop((float)WorldTime.Now) * 0.3f);
            // the foot of the blast wave on the line from the impact to the aircraft: the wall that is coming
            Func<Vector3> front = () =>
            {
                float T = (float)WorldTime.Now;
                Vector3 p = Schedule.PlanePos(T); p.y = 0f;
                Vector3 d = (p - Schedule.Site); d.y = 0f;
                return Schedule.Site + d.normalized * Schedule.FrontRadius(T) + Vector3.up * 9000f;
            };

            // ---- beat 1: an ordinary flight (0:00-1:30) ----
            CinematicDirector.Rel(0f, -84f, -3f, 62f, 0.3f, 0.1f);
            CinematicDirector.Rel(16f, -112f, -10f, 62f, 0.45f, 9f);                    // back along the wing, the clouds sliding under it
            CinematicDirector.Rel(34f, -78f, -16f, 62f, 0.8f, 9f);                      // down at the fields
            CinematicDirector.At(56f, body, 62f, 0.8f, 10f, 0f, -7f);                   // a star by day
            CinematicDirector.At(70f, body, 3.4f, 0.95f, 7f);                            // it has a tail
            CinematicDirector.At(84f, body, 3.4f, 0.95f, 0.1f);

            // ---- beat 2: the line (1:30-3:00) ----
            CinematicDirector.Rel(98f, 24f, -4f, 62f, 0.0f, 9f, 30f);                   // the empty cabin; sunlight on the seats
            CinematicDirector.Rel(112f, 24f, -4f, 62f, 0.0f, 0.1f, 30f);
            CinematicDirector.Rel(126f, -98f, -8f, 62f, 0.55f, 9f, 30f);
            CinematicDirector.At(146f, body, 62f, 0.85f, 9f, 0f, -6f);
            CinematicDirector.At(160f, body, 3.4f, 0.95f, 7f);                          // not a star: a rock, and it does not drift across the window
            CinematicDirector.At(178f, body, 3.4f, 0.95f, 0.1f);

            // ---- beat 3: a second moon (3:00-4:30) ----
            CinematicDirector.At(194f, body, 62f, 0.72f, 10f, -16f, -9f);              // the window and the cloud deck as the ruler: it hangs above them
            CinematicDirector.At(214f, body, 62f, 0.9f, 9f, 8f, -5f);
            CinematicDirector.At(236f, body, 30f, 0.95f, 8f, 0f, -9f);         // with the horizon and the moon in frame
            CinematicDirector.Rel(252f, -80f, -20f, 62f, 0.85f, 9f);                    // the world it is coming to: fields, a river, towns
            CinematicDirector.At(270f, body, 62f, 0.9f, 9f, 0f, -4f);

            // ---- beat 4: the fall (4:30-6:30) ----
            CinematicDirector.At(292f, body, 34f, 0.95f, 8f, 0f, -11f);                           // the size of it
            CinematicDirector.At(310f, body, 62f, 0.95f, 8f, 0f, -3f);
            CinematicDirector.At(e + 1f, body, 34f, 0.95f, 6f);                         // it reaches the air and catches fire
            CinematicDirector.At(c - 14f, body, 62f, 0.95f, 8f, 0f, -2f, 20f);          // the last seconds: wide, the horizon in frame
            CinematicDirector.At(c - 0.5f, site, 62f, 0.95f, 5f, 0f, 3f, 20f);          // the contact
            CinematicDirector.At(c + 9f, site, 62f, 0.95f, 0.1f, 0f, 3f);
            CinematicDirector.At(c + 24f, ball, 62f, 0.95f, 12f, 0f, 2f);

            // ---- beat 5: fire and earth (6:30-8:30) ----
            CinematicDirector.Rel(c + 40f, 26f, -3f, 62f, 0.0f, 8f, 35f);               // the cabin in the light of it
            CinematicDirector.Rel(c + 50f, 26f, -3f, 62f, 0.0f, 0.1f, 35f);
            CinematicDirector.At(c + 62f, ball, 62f, 0.95f, 8f, 0f, 6f, 35f);
            CinematicDirector.At(c + 74f, site, 50f, 0.95f, 10f, 0f, 12f);              // the tilt up the plume: from the cloud deck...
            CinematicDirector.At(c + 96f, plume, 28f, 0.95f, 18f, 0f, -10f);              // ...to the cap, cut off by the top of the window: it leaves the air
            CinematicDirector.At(c + 100f, plume, 28f, 0.95f, 0.1f, 0f, -10f);
            CinematicDirector.At(c + 110f, mushroom, 58f, 0.95f, 8f, 0f, 3f);          // pull back: head, rolling rim, stem, base surge, the cloud deck
            CinematicDirector.At(c + 116f, mushroom, 58f, 0.95f, 0.1f, 0f, 3f);
            CinematicDirector.At(c + 130f, front, 50f, 0.95f, 12f, 0f, 3f);             // the ring racing over the clouds
            CinematicDirector.At(c + 140f, front, 50f, 0.95f, 0.1f, 0f, 3f);

            // ---- beat 6: the wall is coming (8:30-10:00) ----
            CinematicDirector.Rel(c + 156f, -96f, 27f, 54f, 1f, 11f);                   // up at the glass: the sky rains fire
            CinematicDirector.Rel(c + 170f, -96f, 27f, 54f, 1f, 0.1f);
            CinematicDirector.Rel(c + 186f, -104f, -2f, 62f, 0.88f, 10f);                // the wing, small against the wall
            CinematicDirector.Rel(c + 200f, -104f, -2f, 62f, 0.88f, 0.1f);
            CinematicDirector.At(c + 216f, front, 52f, 0.95f, 10f, 0f, 20f);            // closer: up the wall to its crest, clouds torn away under it
            CinematicDirector.At(c + 232f, front, 52f, 0.95f, 0.1f, 0f, 20f);

            // ---- beat 7: it takes the aircraft (10:00-10:48) ----
            CinematicDirector.Rel(c + 246f, 22f, -2f, 62f, 0.0f, 8f, 35f);              // the cabin, red
            CinematicDirector.Rel(c + 254f, 22f, -2f, 62f, 0.0f, 0.1f, 35f);
            CinematicDirector.At(c + 266f, front, 62f, 0.92f, 9f, 0f, 32f, 35f);        // the last look: up the wall that fills the window
            CinematicDirector.At(a + 2f, front, 62f, 0.9f, 0.1f, 0f, 32f);
            CinematicDirector.Sort();
        }
    }
}
