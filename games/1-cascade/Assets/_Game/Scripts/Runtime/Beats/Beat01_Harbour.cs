using System.Collections.Generic;
using UnityEngine;

namespace CASCADE
{
    /// <summary>
    /// Beat 1, Harbour morning (0:00-1:30, chainage 0-243, tension 2). Beauty, calm, human scale, something is wrong.
    /// Stomach-drop: every gull takes off at once in silence (1:16). World pieces used: quay, harbour row, boats,
    /// gulls (ParticleDirector), steam thread on the cone, morning mist (EnvRig), the dark wet band on the quay, the
    /// rooftop church on the cliff that the walker looks up at (HouseGen).
    /// </summary>
    public static class Beat01_Harbour
    {
        public const float From = 0f, To = 90f;

        // Lenses (vertical FOV, degrees; full-frame equivalents). The walk is a 22 mm lens instead of the old 17 mm: the cone,
        // the domes and the cascade read bigger and the frame edges stop stretching. Every cue of the beat uses the walk lens
        // unless it has a subject of its own, so the slow FOV spring (Gaze) never breathes between cues.
        /// <summary>The quay walk (Ahead and side glances); HouseGen scores its walk churches in this frame.</summary>
        public const float WalkFov = 58f;
        /// <summary>The look up at the hero church (t 40-46 s); HouseGen puts its dome on the upper third of this frame.</summary>
        public const float ChurchFov = 46f;
        const float ConeFov = 60f;      // the first glance at the cone and the gulls that lead the eye back to it

        /// <summary>Beat-specific objects. The harbour itself comes from the shared village and architecture builders.</summary>
        public static void Build(Transform parent) { }

        public static void Cues(List<Gaze.Cue> c)
        {
            Vector3 cone = Sim.ConeSummit + new Vector3(0f, -350f, 0f);
            // the Ahead looks are lifted a few degrees: the white cascade and its domes fill the upper right, not the paving
            // (HouseGen scores its church candidates with these pitches and WalkFov: keep its QuayEyePitch in step)
            c.Add(Gaze.A(0f, 6f, WalkFov, 2.0f));
            c.Add(Gaze.R(7f, -48f, -5f, WalkFov, 2.2f));             // boats and nets on the water side
            c.Add(Gaze.A(18f, 7f, WalkFov, 2.0f));
            c.Add(Gaze.W(28f, cone, ConeFov, 2.4f));                 // the cone, first glance: small and peaceful
            c.Add(Gaze.R(34.5f, 30f, -3f, WalkFov, 2.2f));           // cafe chairs, open doors
            // up at the hero church: a big blue dome and its bell gable on the cliff (HouseGen picks the church and aims below it,
            // so the dome rides on the upper third with the white houses under it). The lens closes in during the look
            // (about 50 degrees at 44 s in a continuous run; a still snaps to 46).
            if (HouseGen.HaveQuayChurch) c.Add(Gaze.W(40f, HouseGen.QuayChurchLook, ChurchFov, 2.8f));
            else c.Add(Gaze.R(40f, 32f, 20f, WalkFov, 2.4f));
            c.Add(Gaze.A(46f, 6f, WalkFov, 2.2f));
            c.Add(Gaze.R(57f, -40f, -19f, WalkFov, 2.0f));           // the dark wet band, taut ropes
            c.Add(Gaze.A(66f, 4f, WalkFov, 2.0f));
            c.Add(Gaze.D(75.6f, Gaze.GullLook, ConeFov, 1.3f));      // the flock leads the eye to the cone a second time
            c.Add(Gaze.A(86f, 6f, WalkFov, 2.2f));
        }
    }
}
