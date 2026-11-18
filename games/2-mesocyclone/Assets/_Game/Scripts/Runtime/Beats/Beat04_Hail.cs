using UnityEngine;

namespace MESOCYCLONE
{
    /// <summary>Beat 4 — Hail (4:30-6:00) [8]. The eye is trapped inside: hood, glass, the cracks.
    /// Cracks at 4:35, 5:10, 5:30; the hail stops as if cut at 5:45 and the silence rings.</summary>
    public static class Beat04_Hail
    {
        public static void Build(Transform root)
        {
            Gaze.Local(270f, 0f, -4f, 76f);
            Gaze.Local(276f, 29f, -11f, 76f, 0.9f);         // the first star crack
            Gaze.Local(284f, 0f, -13f, 77f);               // the hood: stones bouncing
            Gaze.Local(296f, -20f, -24f, 77f);             // dash, wheel
            Gaze.Local(304f, 5f, 13f, 77f);                // the roof line: the drumming
            Gaze.Local(310.6f, -15f, 15f, 77f, 0.8f);        // the second crack, just after it happens
            Gaze.Local(320f, 0f, -11f, 78f);               // softballs on the hood
            Gaze.Local(330.8f, 25f, -2f, 78f, 0.9f);         // the web
            Gaze.Local(338f, 0f, -4f, 77f);
            Gaze.Local(346f, 0f, -2f, 75f, 2.5f);          // silence
            Gaze.Local(352f, -6f, -18f, 74f, 2.2f);

            LightningScore.Add(281f, FlashKind.Sheet, FlashAnchor.Truck, 3000f, 2000f, 3500f, 0.7f, 7000f);
            LightningScore.Add(292f, FlashKind.Sheet, FlashAnchor.Truck, -4000f, 1000f, 3600f, 0.8f, 7000f);
            LightningScore.Add(303f, FlashKind.Sheet, FlashAnchor.Truck, 1000f, 5000f, 3400f, 0.8f, 7000f);
            LightningScore.Add(316f, FlashKind.Bolt, FlashAnchor.Truck, -900f, 1300f, 1400f, 1.1f, 4500f, 0.15f, 0.06f, 0.3f);
            // the cracks are rim-lit from outside as they form (nothing else lights them between flashes):
            // crack 2 finishes growing at 310.5, the peak of this sheet; 4.39 km, so its thunder rolls in at ~323 s
            LightningScore.Add(310.35f, FlashKind.Sheet, FlashAnchor.Truck, -1200f, 2500f, 3400f, 0.8f, 7000f, 0.15f, 0.1f, 0.35f);   // crack 2 caught in the light
            // crack 3 grows 330.0-330.9, the source to the right like the crack. Keep it near 3.1 km: its thunder
            // lands at ~339 s, before the 345 s silence (4.4 km would roll into the ringing)
            LightningScore.Add(330.3f, FlashKind.Sheet, FlashAnchor.Truck, 1500f, 1800f, 2000f, 0.8f, 7000f, 0.15f, 0.1f, 0.35f);    // the web runs in the flash
            LightningScore.Add(327f, FlashKind.Sheet, FlashAnchor.Truck, 2500f, -1500f, 3600f, 0.8f, 7000f);
            LightningScore.Add(338f, FlashKind.Sheet, FlashAnchor.Truck, -1500f, 3500f, 3500f, 0.9f, 7000f);
            LightningScore.Add(350.5f, FlashKind.Sheet, FlashAnchor.Truck, -6000f, 9000f, 3800f, 0.5f, 9000f);
        }
    }
}
