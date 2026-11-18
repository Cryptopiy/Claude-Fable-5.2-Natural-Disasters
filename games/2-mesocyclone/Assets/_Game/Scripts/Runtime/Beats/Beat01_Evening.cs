using UnityEngine;

namespace MESOCYCLONE
{
    /// <summary>Beat 1 — Sultry evening (0:00-1:30, chainage 0-2160) [3]. Beauty with a splinter of threat.</summary>
    public static class Beat01_Evening
    {
        public static void Build(Transform root)
        {
            Gaze.Local(0f, 0f, -2f, 74f, 1.2f);            // the road
            Gaze.Local(9f, 38f, 3f, 73f);                  // the wind farm
            Gaze.Storm(22f, 7000f, 72f, 2.4f);             // first glance at the anvil: far, small
            Gaze.Local(34f, -10f, -24f, 74f);              // the dash
            Gaze.Local(42f, 0f, -1f, 74f);                 // back to the road
            Gaze.Local(52f, -72f, 2f, 73f, 2f);            // the low sun on the fields, left window
            Gaze.Local(61f, 14f, 13f, 70f, 2.6f);          // M1: sunset light under the anvil, turbines tiny against it
            Gaze.Local(84f, 0f, 0f, 72f, 2f);
        }
    }
}
