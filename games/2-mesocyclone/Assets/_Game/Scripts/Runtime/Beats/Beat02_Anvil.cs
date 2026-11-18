using UnityEngine;

namespace MESOCYCLONE
{
    /// <summary>Beat 2 — The anvil (1:30-3:00) [5]. The structure becomes visible; the silence is wrong.
    /// All lightning here is silent (too far): sheet light deep in the cloud and one crawler across the underside.</summary>
    public static class Beat02_Anvil
    {
        public static void Build(Transform root)
        {
            Gaze.Local(92f, 5f, 15f, 70f, 2.4f);           // up: mammatus catching the last pink
            Gaze.Local(108f, -14f, 17f, 70f, 2.6f);
            Gaze.Local(120f, 0f, 2f, 71f);                 // road; the first chorus stops at 2:05
            Gaze.Storm(128f, 4500f, 69f, 2.4f);            // the dark core ahead
            Gaze.Local(140f, 22f, 14f, 69f, 2.4f);         // sheet lightning inside the anvil
            Gaze.Local(152f, -6f, -20f, 72f);              // the dash: the radio
            Gaze.Local(158f, 6f, 16f, 68f, 2.6f);          // up again: the crawler comes at 2:45
            Gaze.Local(173f, 0f, 3f, 70f, 2f);             // dead calm

            LightningScore.Add(118f, FlashKind.Sheet, FlashAnchor.Storm, 4000f, -30000f, 9500f, 0.5f, 14000f, 0.22f, 0.15f, 0.4f, true);
            LightningScore.Add(133f, FlashKind.Sheet, FlashAnchor.Storm, -9000f, -22000f, 8000f, 0.6f, 14000f, 0.2f, 0.1f, 0.4f, true);
            LightningScore.Add(148f, FlashKind.Crawler, FlashAnchor.Truck, 3000f, 9000f, 9200f, 0.6f, 15000f, 0.35f, 0.6f, 0.5f, true);
            // the stomach-drop: the last insect stops at 2:45.0, then the crawler creeps across the whole underside.
            // Its root sits ahead-left (about yaw -25, 31 deg up), inside the 158 s gaze, so the web sweeps across the glass
            LightningScore.Add(165.8f, FlashKind.Crawler, FlashAnchor.Truck, -6000f, 13000f, 9200f, 0.9f, 22000f, 0.6f, 1.6f, 0.7f, true);
        }
    }
}
