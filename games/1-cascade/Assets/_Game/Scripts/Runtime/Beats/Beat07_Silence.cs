using System.Collections.Generic;
using UnityEngine;

namespace CASCADE
{
    /// <summary>
    /// Beat 7, Silence (10:00-11:00, chainage 1620-end, tension 3, awe). The wind drops, ash settles like snow, the air
    /// clears on a brown, still caldera, the broken cone and the drowned village. Money shot M5: one amber window at
    /// the keeper house and the lighthouse beam in the ash. The walker stops at the door; fade; one line.
    /// </summary>
    public static class Beat07_Silence
    {
        public const float From = 600f, To = 680f;

        public static void Build(Transform parent) { }

        public static void Cues(List<Gaze.Cue> c)
        {
            Vector3 window = Gaze.HaveLandmarks ? Gaze.KeeperWindow : PathDef.Pos(PathDef.Length) + new Vector3(-9f, 2.2f, 2f);
            c.Add(Gaze.A(600f, 0f, 69f, 3.0f));
            c.Add(Gaze.D(609f, Gaze.ColumnLook, 72f, 4.0f));         // the plume still towers
            c.Add(Gaze.W(622f, new Vector3(-20f, 40f, -60f), 71f, 3.6f));   // the drowned village, the brown caldera
            if (Gaze.HaveLandmarks)
            {
                c.Add(Gaze.D(638f, Gaze.WindowLook, 68f, 3.4f));     // M5: one warm light
                c.Add(Gaze.D(654f, Gaze.WindowLook, 66f, 3.0f));
            }
            else
            {
                c.Add(Gaze.W(638f, window, 68f, 3.4f));
                c.Add(Gaze.W(654f, window + new Vector3(0f, -0.6f, 0f), 66f, 3.0f));
            }
        }
    }
}
