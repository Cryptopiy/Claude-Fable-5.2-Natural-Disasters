using UnityEngine;

namespace MESOCYCLONE
{
    /// <summary>Beat 5 — The funnel (6:00-8:00) [9]. Wall cloud, rope, cone, wedge; the lights of the town die in
    /// teal power flashes; the grain elevator is erased; the radio dies. Never the biggest first.</summary>
    public static class Beat05_Funnel
    {
        public const float TownS = 15000f, TownLat = 900f;       // town centre (road coordinates)
        public const float ElevS = 14750f, ElevLat = 250f;       // the grain elevator, nearest the road

        public static void Build(Transform root)
        {
            // far away, all of it with clear sky beside it: the funnel track runs nearer the road (Story torLat), and the
            // yaw offsets look a little right of it, so the funnel sits left of centre in clear glass and the pillar,
            // door and side glass shrink from about half the frame to a third. 451 stays centred (the wedge's full width).
            Gaze.Tornado(361f, 1700f, 72f, 2.4f, 4f);      // the wall cloud lowering
            Gaze.Tornado(374f, 1000f, 71f, 2.4f, 9f);      // the rope descending
            Gaze.Tornado(398f, 350f, 70f, 2.4f, 9f);       // touchdown
            Gaze.Road(414f, TownS, TownLat, 30f, 70f, 2.2f);    // the town: sirens
            Gaze.Tornado(426f, 500f, 70f, 2.4f, 6f);
            Gaze.Road(440f, 15150f, -300f, 300f, 70f, 2.6f);    // wedge and town in one frame
            Gaze.Tornado(451f, 600f, 70f, 2.4f);           // its full width
            // the elevator, a lit column in front of the wedge: the move starts once the 456.0 flash has faded,
            // so the camera has settled before its lights go out (461-462.4)
            Gaze.Road(456.7f, ElevS, ElevLat, 60f, 70f, 1.8f);
            Gaze.Tornado(465.5f, 650f, 70f, 2.4f, 12f, 3f); // M3: the wedge in the cracked glass, red lights in front
            Gaze.Local(475.5f, -8f, -18f, 72f, 1.6f);      // the radio dies
            Gaze.Local(479f, 4f, 0f, 72f, 2f);

            // light from behind and above the vortex: silhouette (Tornado anchor: east, north of the vortex)
            LightningScore.Add(360.6f, FlashKind.Sheet, FlashAnchor.Tornado, 500f, 2500f, 3300f, 0.8f, 9000f, 0.25f, 0.3f, 0.45f);
            LightningScore.Add(368f, FlashKind.Sheet, FlashAnchor.Tornado, -2000f, 3500f, 3400f, 0.6f, 9000f);
            LightningScore.Add(379f, FlashKind.Sheet, FlashAnchor.Tornado, 1200f, 3000f, 1200f, 0.9f, 8000f, 0.25f, 0.3f, 0.45f);
            LightningScore.Add(391f, FlashKind.Sheet, FlashAnchor.Tornado, -800f, 4000f, 3000f, 0.7f, 9000f);
            LightningScore.Add(404f, FlashKind.Sheet, FlashAnchor.Tornado, 2500f, 3200f, 1100f, 0.9f, 9000f, 0.25f, 0.25f, 0.45f);
            LightningScore.Add(418f, FlashKind.Sheet, FlashAnchor.Tornado, -1500f, 4500f, 3200f, 0.7f, 9000f);
            LightningScore.Add(430f, FlashKind.Sheet, FlashAnchor.Tornado, 800f, 4200f, 1000f, 1.0f, 10000f, 0.25f, 0.3f, 0.5f);
            // a ground strike beside the cone inside that sheet (channel only: same instant and envelope, one swell)
            LightningScore.AddChannel(430f, FlashAnchor.Tornado, 2200f, 2500f, 1900f, 0.25f, 0.3f, 0.5f);
            LightningScore.Add(437f, FlashKind.Sheet, FlashAnchor.Tornado, 3200f, 3000f, 3000f, 0.6f, 8000f);
            // power flashes marching through the town ahead of the wedge (Road anchor)
            float[] t = { 441f, 446f, 449.5f, 453f, 456f, 458.5f, 461f, 463.2f, 466f, 470f, 474f };
            float[] s = { 15350f, 15250f, 15150f, 15100f, 14950f, 14900f, 14800f, 14760f, 14700f, 14950f, 15200f };
            float[] l = { 300f, 500f, 420f, 700f, 520f, 900f, 300f, 240f, 600f, 1100f, 1300f };
            float[] p = { 0.5f, 0.6f, 0.5f, 0.7f, 0.6f, 0.8f, 1.1f, 0.9f, 0.7f, 0.6f, 0.5f };
            for (int i = 0; i < t.Length; i++)
                LightningScore.Add(t[i], FlashKind.Transformer, FlashAnchor.Road, s[i], l[i], 14f, p[i], 2600f, 0.15f, 0.12f, 0.22f);
            LightningScore.Add(467.6f, FlashKind.Sheet, FlashAnchor.Tornado, -500f, 3800f, 900f, 1.1f, 10000f, 0.3f, 0.8f, 0.6f);   // M3 backlight
            LightningScore.Add(467.6f, FlashKind.Sheet, FlashAnchor.Tornado, -3500f, 3000f, 300f, 0.5f, 9000f, 0.3f, 0.8f, 0.6f, true);  // a pale slot along the horizon: the foot of the wedge
            // a strike in the lit slot right of the wedge (LightningVisuals keeps it clear of the limb)
            LightningScore.AddChannel(467.6f, FlashAnchor.Tornado, 5500f, 1500f, 1900f, 0.3f, 0.8f, 0.6f);
        }
    }
}
