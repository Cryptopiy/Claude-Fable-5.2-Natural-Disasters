using System.Collections.Generic;
using UnityEngine;

namespace CASCADE
{
    /// <summary>
    /// Beat 4, The slide and the drawdown (5:00-6:30, chainage 810-1053, tension 8, eerie). The flank slides for
    /// twenty seconds behind its dust, the splash dome, the late rumble; then the sea leaves the harbour and the roar
    /// ducks into hush. Stomach-drop: the silence and the exposed sea floor. At 6:20 the thin white line.
    /// </summary>
    public static class Beat04_Drawdown
    {
        public const float From = 300f, To = 390f;

        public static void Build(Transform parent) { }

        public static void Cues(List<Gaze.Cue> c)
        {
            c.Add(Gaze.D(303f, Gaze.SlideLook, 70f, 2.0f));          // on the flank through the whole slide
            c.Add(Gaze.W(331f, new Vector3(Sim.SplashXZ.x, 300f, Sim.SplashXZ.y), 70f, 2.4f));   // the splash crown and the wave being born
            c.Add(Gaze.W(364f, Sim.Harbour, 68f, 2.6f, -1.2f));      // the harbour drains; boats settle
            c.Add(Gaze.D(374f, Gaze.LineLook, 68f, 3.0f, -1.2f));    // the line
        }
    }
}
