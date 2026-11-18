using UnityEngine;

namespace MESOCYCLONE
{
    /// <summary>Beat 8 — The light (10:30-11:00) [2]. A lone gas-station canopy glows; the truck slows in the last
    /// six seconds and stops under it; the engine dies; the storm still flickers, uninterested.</summary>
    public static class Beat08_Light
    {
        public static void Build(Transform root)
        {
            Gaze.Local(630.5f, 0f, -1f, 70f, 2.4f);        // one warm point on the road
            Gaze.Road(640f, (float)Director.GoalS, 10.5f, 5f, 70f, 2.6f);
            Gaze.Local(647f, -7f, 5f, 70f, 2.8f);          // M6: canopy glow, black land, the storm behind
            Gaze.Road(656.5f, (float)Director.GoalS + 2f, 10.5f, 4.6f, 72f, 2.4f);
            Gaze.Local(662f, 0f, 5f, 72f, 2.6f);

            LightningScore.Add(636f, FlashKind.Crawler, FlashAnchor.Storm, 4000f, -3000f, 9000f, 0.8f, 14000f, 0.4f, 1.0f, 0.7f, true);
            LightningScore.Add(645f, FlashKind.Sheet, FlashAnchor.Storm, -5000f, -1000f, 5500f, 0.7f, 11000f, 0.3f, 0.5f, 0.6f, true);
            LightningScore.Add(652.5f, FlashKind.Crawler, FlashAnchor.Storm, 0f, -5000f, 10000f, 0.9f, 15000f, 0.5f, 1.3f, 0.7f, true);
            LightningScore.Add(661f, FlashKind.Sheet, FlashAnchor.Storm, 3000f, 2000f, 6500f, 0.6f, 11000f, 0.3f, 0.5f, 0.6f, true);
            // the storm lit inside the canopy opening as the closing line comes up (the 652.5 crawler's twin); the
            // deck shades the frame, so only about 2.5 lux of it reaches the truck
            LightningScore.Add(669.4f, FlashKind.Crawler, FlashAnchor.Storm, -1500f, -5000f, 9500f, 0.75f, 14000f, 0.45f, 1.2f, 0.7f, true);
            // the second pulse during the line (was 672: moved off the crawler, 4.4 s after it)
            LightningScore.Add(673.8f, FlashKind.Sheet, FlashAnchor.Storm, -3000f, -2000f, 8000f, 0.6f, 12000f, 0.3f, 0.6f, 0.6f, true);
        }
    }
}
