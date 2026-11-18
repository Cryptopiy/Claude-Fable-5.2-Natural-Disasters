using System.Collections.Generic;
using UnityEngine;

namespace CASCADE
{
    /// <summary>
    /// Beat 2, The climb (1:30-3:00, chainage 243-486, tension 4). Compression between white walls, two tremors, then
    /// release: money shot M1, the whole caldera at 3:00. Stomach-drop: tremor 2, rocks falling far across the bay.
    /// </summary>
    public static class Beat02_Climb
    {
        public const float From = 90f, To = 181f;

        // Lenses (vertical FOV, degrees; full-frame equivalents): one 23 mm lens for the climb and M1, 22 mm between the
        // walls of the narrow stair. Beat 3 opens to 72 for the column (from 190 s; the FOV spring takes about 8 s, so the
        // lens is still opening while the column rises).
        const float ClimbFov = 56f, NarrowFov = 58f;
        /// <summary>M1, the vista: the cone about 1.45x wider than on the old 74-degree lens.</summary>
        public const float VistaFov = 55f;
        /// <summary>
        /// M1 aims this far below the summit: from the promenade (eye about 110 m up, 5.1 km from the axis) the frame centre
        /// sits 8.9 degrees up, which puts the summit on the upper third and the cone's foot on the lower third of a 55-degree frame.
        /// </summary>
        public const float VistaAimBelowSummit = 900f;

        // First flight (92-104 s, leg 1 heading south): the cascade stands up-slope to the LEFT of the treads. The old Ahead cue
        // (pitch 15 + 0.35 x stair slope = 24) already led 17 degrees left into the hairpin at 100 s and still left the houses in
        // the upper-left quadrant over 40% blank treads. A fixed side look 22 degrees left and 30 up puts the cascade on the
        // left third, the lamp near the right third and the treads in the lower-right third (top of the flight at y ~ 740 of 1080).
        // A Rel cue (no slope term, no lead) holds that frame along the whole straight leg, so the 100 s still is the film.
        const float StairLookYaw = -22f, StairLookPitch = 30f;
        // Second landing (129-138 s): the harbour target lies ~210 m out and ~50 m below the eye (pitch -14), which pushed the
        // summit (+18 degrees, 26 off-axis) ~150 px above the 56-degree frame. Floor the pitch: summit at y ~ 83-98, horizon
        // y ~ 478, boats and quay in the lower left; the sun enters near the top centre (fixed exposure, so no pumping).
        const float HarbourPitchMin = -3.5f;

        public static void Build(Transform parent) { }

        public static void Cues(List<Gaze.Cue> c)
        {
            Vector3 cone = Sim.ConeSummit + new Vector3(0f, -350f, 0f);
            Vector3 farCliff = new Vector3(-340f, 110f, 530f);
            c.Add(Gaze.R(92f, StairLookYaw, StairLookPitch, ClimbFov, 1.8f));   // the white cascade beside the stair, not blank treads
            c.Add(Gaze.W(104f, cone + new Vector3(0f, -900f, 300f), ClimbFov, 2.0f));      // first landing; tremor 1 shivers the water
            c.Add(Gaze.A(115f, 12f, ClimbFov, 1.8f));
            c.Add(Gaze.WFloor(129f, Sim.Harbour + new Vector3(-90f, 0f, 20f), HarbourPitchMin, ClimbFov, 2.0f)); // second landing: the harbour below and the whole cone
            c.Add(Gaze.A(138f, 12f, ClimbFov, 1.8f));
            c.Add(Gaze.W(151f, farCliff, ClimbFov, 1.6f));           // tremor 2: the rockfall across the bay
            c.Add(Gaze.A(160f, 13f, NarrowFov, 1.8f));               // the narrow stair: a strip of sky
            c.Add(Gaze.A(170f, 16f, NarrowFov, 2.0f));
            c.Add(Gaze.W(179.5f, Sim.ConeSummit - new Vector3(0f, VistaAimBelowSummit, 0f), VistaFov, 2.6f));   // M1: the vista, nothing happens
            // M1 ends in Beat03_Column: the camera lifts to the summit at 188.5 s on this lens and opens at 190 s
        }
    }
}
