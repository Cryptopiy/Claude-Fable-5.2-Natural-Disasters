using System.Collections.Generic;
using UnityEngine;

namespace CASCADE
{
    /// <summary>
    /// Beat 3, The column (3:00-5:00, chainage 486-810, tension 7-8). Flash at 3:12, fifteen seconds of picture
    /// alone, shock ring, the late thump, the column to 35 km, the umbrella takes the sun. Money shot M2: looking
    /// almost straight up (4:35-4:50). Breath at 4:50: the roar ducks, ash falls past a lit lamp.
    /// </summary>
    public static class Beat03_Column
    {
        public const float From = 181f, To = 300f;

        public static void Build(Transform parent) { }

        public static void Cues(List<Gaze.Cue> c)
        {
            // M1 (Beat02_Climb) framed the summit on the upper third; lift to it on M1's lens first, so the camera is nearly at
            // rest on the summit when it flashes, then open the lens for the column (the spring reaches 72 at about 200 s)
            c.Add(Gaze.W(188.5f, Sim.ConeSummit, Beat02_Climb.VistaFov, 2.6f));
            c.Add(Gaze.W(190f, Sim.ConeSummit, 72f, 2.2f));          // already resting on the cone when it flashes
            c.Add(Gaze.D(197f, Gaze.ColumnLook, 72f, 2.6f));
            c.Add(Gaze.D(246f, Gaze.UmbrellaLook, 74f, 4.0f));       // M2
            c.Add(Gaze.D(277f, Gaze.SunLook, 72f, 3.2f));            // the sun turns into a dim red disc and goes out
            c.Add(Gaze.A(290.5f, -4f, 70f, 2.4f));                   // down to the path and a lit lamp: the breath
            c.Add(Gaze.W(298f, Sim.ConeSummit + Vector3.up * 3500f, 72f, 3.0f));   // root to lid in one frame (straight up the axis it read as a disc)
        }
    }
}
