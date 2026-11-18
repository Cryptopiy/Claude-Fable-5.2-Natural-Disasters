using UnityEngine;

namespace MESOCYCLONE
{
    /// <summary>Beat 3 — The wall (3:00-4:30) [6 to 7]. A shelf cloud wider than the world, lit from inside;
    /// the dust wall takes the turbine rows one by one; the gust front hits at 4:01.</summary>
    public static class Beat03_Wall
    {
        public static void Build(Transform root)
        {
            Gaze.Shelf(181f, 1500f, 68f, 2.4f);            // the wall far away
            Gaze.Local(190f, -28f, 7f, 68f, 2.8f);         // along its length: left...
            Gaze.Local(198f, 10f, 8f, 68f, 2.8f);
            Gaze.Local(206f, 52f, 7f, 68f, 2.8f);          // ...to the right: the width is the point (M2)
            Gaze.Local(215f, 34f, 3f, 70f, 2.2f);          // the turbines spinning up
            Gaze.Shelf(223f, 500f, 72f, 2.2f);             // the dust arriving
            Gaze.Local(232f, 0f, 15f, 74f, 2.2f);          // the black lip passing overhead
            Gaze.Local(240f, 0f, 0f, 74f, 1.2f);           // impact
            Gaze.Local(250f, 8f, -6f, 75f);
            Gaze.Local(262f, 0f, -2f, 75f);

            // inside the shelf, along its length (Road anchor: chainage, lateral)
            LightningScore.Add(186f, FlashKind.Sheet, FlashAnchor.Road, 12600f, -7000f, 2200f, 0.7f, 9000f, 0.2f, 0.12f, 0.35f);
            LightningScore.Add(196.5f, FlashKind.Sheet, FlashAnchor.Road, 11600f, 9000f, 2400f, 0.7f, 9000f, 0.2f, 0.1f, 0.35f);
            LightningScore.Add(203.5f, FlashKind.Sheet, FlashAnchor.Road, 10800f, -11000f, 2000f, 0.9f, 10000f, 0.25f, 0.2f, 0.4f);
            LightningScore.Add(208f, FlashKind.Sheet, FlashAnchor.Road, 10300f, 1500f, 2300f, 1.0f, 11000f, 0.25f, 0.35f, 0.45f);    // M2
            LightningScore.Add(212.6f, FlashKind.Sheet, FlashAnchor.Road, 9900f, 12000f, 2300f, 0.9f, 10000f, 0.2f, 0.2f, 0.4f);
            LightningScore.Add(219f, FlashKind.Sheet, FlashAnchor.Road, 9300f, -4000f, 1900f, 0.8f, 8000f, 0.2f, 0.1f, 0.35f);
            LightningScore.Add(227f, FlashKind.Bolt, FlashAnchor.Road, 8900f, -2600f, 1400f, 1.0f, 6000f, 0.15f, 0.06f, 0.3f);
            // behind the dust wall as it closes: its leading edge stands black against the lit band under the lip
            LightningScore.Add(230.6f, FlashKind.Sheet, FlashAnchor.Road, 8000f, -1800f, 1500f, 0.85f, 6000f, 0.25f, 0.3f, 0.45f);
            LightningScore.Add(233.8f, FlashKind.Sheet, FlashAnchor.Road, 7100f, 1300f, 1100f, 0.9f, 5000f, 0.25f, 0.35f, 0.45f);
            LightningScore.Add(236.4f, FlashKind.Bolt, FlashAnchor.Road, 7300f, 1500f, 1200f, 1.1f, 5000f, 0.15f, 0.06f, 0.3f);
            LightningScore.Add(252f, FlashKind.Sheet, FlashAnchor.Truck, -2500f, 3000f, 3200f, 0.7f, 7000f);
        }
    }
}
