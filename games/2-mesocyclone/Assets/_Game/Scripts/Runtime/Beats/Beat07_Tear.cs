using UnityEngine;

namespace MESOCYCLONE
{
    /// <summary>Beat 7 — The tear (9:30-10:30) [6, awe]. The rope thins to a line and is gone; the damage swath;
    /// the clouds part to stars; the whole supercell stands lit from within like a cathedral of cloud.</summary>
    public static class Beat07_Tear
    {
        public static void Build(Transform root)
        {
            Gaze.Tornado(572f, 700f, 72f, 2.2f, 0f, 5f);   // the rope, far left
            Gaze.Tornado(588f, 950f, 72f, 2.4f, 0f, 5f);
            Gaze.Local(596.5f, 2f, -6f, 72f, 2f);          // the damage in the headlights
            Gaze.Local(606f, -4f, 17f, 70f, 2.8f);         // up: the stars
            Gaze.Storm(614.5f, 6500f, 68f, 3f);            // M5: the whole storm, held long

            LightningScore.Add(578f, FlashKind.Sheet, FlashAnchor.Tornado, -1200f, 3000f, 2400f, 0.8f, 8000f, 0.25f, 0.3f, 0.5f);
            LightningScore.Add(584.5f, FlashKind.Sheet, FlashAnchor.Tornado, 900f, 3400f, 2300f, 0.8f, 9000f, 0.5f, 1.3f, 0.9f);
            LightningScore.Add(599.5f, FlashKind.Sheet, FlashAnchor.Tornado, -400f, 3600f, 2200f, 0.55f, 8000f, 0.25f, 0.3f, 0.5f);
            // the cathedral: placed chambers inside the departing storm (Storm anchor: east, north of the updraft),
            // 1.5-3.7 km inside its near face, each lighting its own patch (radius 8-11 km, not the whole storm and
            // the air around it). All silent, at least 0.9 s apart, at most two in any second.
            LightningScore.Add(611.5f, FlashKind.Crawler, FlashAnchor.Storm, -3000f, -6500f, 8500f, 1.1f, 11000f, 0.4f, 0.9f, 0.6f, true);
            LightningScore.Add(613.9f, FlashKind.Sheet, FlashAnchor.Storm, 2500f, -6500f, 7500f, 0.7f, 8000f, 0.3f, 0.4f, 0.5f, true);
            LightningScore.Add(616.6f, FlashKind.Crawler, FlashAnchor.Storm, 5000f, -5500f, 5500f, 1.2f, 11000f, 0.5f, 1.3f, 0.7f, true);
            LightningScore.Add(617.5f, FlashKind.Sheet, FlashAnchor.Storm, -6000f, -10000f, 10500f, 0.9f, 10000f, 0.5f, 0.9f, 0.7f, true);
            LightningScore.Add(620.0f, FlashKind.Sheet, FlashAnchor.Storm, -1500f, -7000f, 9000f, 0.7f, 8000f, 0.3f, 0.4f, 0.5f, true);
            LightningScore.Add(622.4f, FlashKind.Crawler, FlashAnchor.Storm, -5000f, -8000f, 9800f, 1.2f, 11000f, 0.5f, 1.4f, 0.7f, true);
            LightningScore.Add(623.3f, FlashKind.Sheet, FlashAnchor.Storm, 4000f, -8000f, 4200f, 0.9f, 9000f, 0.5f, 0.8f, 0.7f, true);
            LightningScore.Add(625.6f, FlashKind.Sheet, FlashAnchor.Storm, 6000f, -3500f, 8000f, 0.7f, 8000f, 0.3f, 0.4f, 0.5f, true);
            LightningScore.Add(627.6f, FlashKind.Sheet, FlashAnchor.Storm, 1500f, -6500f, 6500f, 0.9f, 10000f, 0.3f, 0.5f, 0.6f, true);
        }
    }
}
