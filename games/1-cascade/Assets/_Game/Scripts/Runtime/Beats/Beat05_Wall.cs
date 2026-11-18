using System.Collections.Generic;
using UnityEngine;

namespace CASCADE
{
    /// <summary>
    /// Beat 5, The wall (6:30-8:30, chainage 1053-1377, tension 10). Ninety seconds of approach; money shot M3 when
    /// the crest, still a kilometre out, rises above the horizon line (7:05-7:15); the headland tip is struck at
    /// about 7:25, the village at 7:40; the water climbs the stairs we climbed; spray on the lens; a lower second wave.
    /// </summary>
    public static class Beat05_Wall
    {
        public const float From = 390f, To = 510f;

        public static void Build(Transform parent) { }

        public static void Cues(List<Gaze.Cue> c)
        {
            Vector3 roofs = new Vector3(40f, 70f, -40f);
            c.Add(Gaze.D(390f, Gaze.WaveLook, 70f, 2.6f, -1.6f));
            c.Add(Gaze.W(413f, Sim.Harbour, 68f, 2.2f, -1.6f));      // one look down: the harbour is still empty
            c.Add(Gaze.D(418.5f, Gaze.WaveLookLeft, 68f, 2.4f, -1.6f));  // back: it has grown (M3 follows)
            c.Add(Gaze.D(427f, Gaze.WaveVillageLook, 68f, 2.8f, -1.6f));   // M3: the wall over the village
            c.Add(Gaze.W(442f, Sim.HeadlandTip + new Vector3(0f, 90f, 0f), 72f, 2.0f, -1.6f));   // the tip is struck first
            c.Add(Gaze.D(449f, Gaze.WaveLook, 72f, 1.8f, -1.6f));
            c.Add(Gaze.W(457f, Sim.Harbour + new Vector3(30f, 80f, 40f), 70f, 2.2f, -1.6f));     // water climbs our stairs; the strike tower rises behind
            c.Add(Gaze.W(471f, roofs + new Vector3(20f, 45f, 20f), 70f, 2.4f, -1.6f));           // over the white roofs
            c.Add(Gaze.D(488f, Gaze.LineLook, 70f, 3.0f));           // the second, lower wave
            c.Add(Gaze.A(498f, -3f, 69f, 2.4f));
        }
    }
}
