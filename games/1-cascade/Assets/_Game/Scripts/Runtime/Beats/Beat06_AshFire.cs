using System.Collections.Generic;
using UnityEngine;

namespace CASCADE
{
    /// <summary>
    /// Beat 6, Ash and fire (8:30-10:00, chainage 1377-1620, tension 8 falling to 6). The ash storm closes the world
    /// to tens of metres; two gusts open it: a glow and a silhouette (8:52), then money shot M4 (9:22): a glowing
    /// pyroclastic current pouring off the cone into the sea, and the hole closes before it finishes.
    /// </summary>
    public static class Beat06_AshFire
    {
        public const float From = 510f, To = 600f;

        /// <summary>Where the pyroclastic current of the near flank reaches the sea.</summary>
        public static Vector3 PdcShore
        {
            get
            {
                float az = (Sim.SlideAzimuth + 6f) * Mathf.Deg2Rad;
                return Config.ConeCenter + new Vector3(Mathf.Cos(az) * 2150f, 90f, Mathf.Sin(az) * 2150f);
            }
        }

        public static void Build(Transform parent) { }

        public static void Cues(List<Gaze.Cue> c)
        {
            Vector3 pdcShore = PdcShore;
            c.Add(Gaze.A(505f, -5f, 68f, 2.4f));                     // lamp to lamp
            c.Add(Gaze.W(534f, pdcShore + new Vector3(0f, 600f, 0f), 70f, 2.4f));
            c.Add(Gaze.A(549f, -4f, 68f, 2.4f));
            c.Add(Gaze.W(564f, pdcShore + new Vector3(0f, 300f, 0f), 66f, 2.4f));   // M4
            c.Add(Gaze.A(581f, -3f, 68f, 2.6f));
        }
    }
}
