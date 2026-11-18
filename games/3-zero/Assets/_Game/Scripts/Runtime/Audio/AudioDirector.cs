using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZERO
{
    /// <summary>The discrete sounds heard in the cabin. Every event sounds at once when it is emitted (no travel delay:
    /// the director emits it when it is heard); 'distance' only tells how far and dull it is.</summary>
    public enum AudioEventKind
    {
        Chime = 0,      // the cabin chime (seat-belt sign): one soft two-tone ding
        Boom = 1,       // a piece of ejecta re-enters above: a distant double thump through the airframe (distance 5-60 km)
        Creak = 2,      // the airframe groans under a load
        Rattle = 3,     // loose things in the cabin rattle for a moment (bins, trays, panels)
        Alarm = 4,      // a cockpit warning heard faintly through the door: a short repeating tone
    }

    /// <summary>Turns the timetable of the event into sound events and continuous state for the listener in the cabin.
    /// A pure function of the event clock, shared by the game (live) and the offline audio bake.</summary>
    public static class AudioDirector
    {
        struct Ev { public float time; public AudioEventKind kind; public float dist, size, pan; }
        static readonly List<Ev> events = new List<Ev>();
        static int next; static float lastT = -1f;

        public static void Init()
        {
            events.Clear();
            var rng = new Rng(8086u);
            void Add(float t, AudioEventKind k, float dist, float size, float pan) { events.Add(new Ev { time = t, kind = k, dist = dist, size = size, pan = pan }); }
            float c = Schedule.ContactTime;
            Add(24f, AudioEventKind.Chime, 6f, 0.6f, 0.2f);                       // an ordinary flight
            Add(c + 2.6f, AudioEventKind.Chime, 6f, 1f, 0.2f);                    // the seat-belt sign, after the light
            Add(c + 3.5f, AudioEventKind.Chime, 6f, 1f, 0.2f);
            Add(c + 9f, AudioEventKind.Alarm, 12f, 0.5f, 0.6f);
            Add(c + 150f, AudioEventKind.Alarm, 12f, 0.8f, 0.6f);
            Add(Schedule.ArrivalTime - 18f, AudioEventKind.Alarm, 12f, 1f, 0.6f);
            // ejecta coming back down: rare at first, then a drum roll
            float t = c + 92f;
            while (t < Schedule.ArrivalTime - 3f)
            {
                float k = Mathf.InverseLerp(c + 92f, Schedule.ArrivalTime, t);
                float size = rng.Range(0.3f, 0.7f) + 0.3f * k;
                Add(t, AudioEventKind.Boom, rng.Range(6000f, 60000f) * (1.1f - 0.6f * k), Mathf.Clamp01(size), rng.Signed());
                if (rng.Chance(0.35f + 0.4f * k)) Add(t + rng.Range(0.05f, 0.5f), AudioEventKind.Rattle, 2f, Mathf.Clamp01(size * 0.9f), rng.Signed());
                t += Mathf.Lerp(11f, 1.6f, k * k) * rng.Range(0.6f, 1.4f);
            }
            // the airframe starts to complain as the wave nears
            t = c + 170f;
            while (t < Schedule.ArrivalTime - 1f)
            {
                float k = Mathf.InverseLerp(c + 170f, Schedule.ArrivalTime, t);
                Add(t, AudioEventKind.Creak, 3f, Mathf.Clamp01(0.3f + 0.7f * k), rng.Signed());
                t += Mathf.Lerp(14f, 2.2f, k) * rng.Range(0.6f, 1.4f);
            }
            events.Sort((a, b) => a.time.CompareTo(b.time));
            Reset(0f);
        }

        /// <summary>After a jump in time: nothing that is already past will sound.</summary>
        public static void Reset(float T)
        {
            lastT = T; next = 0;
            while (next < events.Count && events[next].time <= T) next++;
        }

        /// <summary>Emits every event whose moment has come. emit(kind, distance in metres, size 0..1, pan -1 left .. +1 right).</summary>
        public static void Tick(float T, Action<AudioEventKind, float, float, float> emit)
        {
            if (T < lastT - 0.01f || T > lastT + 3f) { Reset(T); return; }
            lastT = T;
            while (next < events.Count && events[next].time <= T)
            {
                var e = events[next++];
                emit(e.kind, e.dist, e.size, e.pan);
            }
        }

        /// <summary>The continuous part of the soundscape at event time T.</summary>
        public static void Fill(ref AudioState s, float T)
        {
            s.eventTime = T;
            s.sinceContact = T - Schedule.ContactTime;
            s.frontDist = Schedule.FrontDist(T);
            s.descent = Schedule.Descent(T);
            s.bodyDist = Vector3.Distance(Schedule.AstTip(T), Schedule.PlanePos(T));
            s.fire = Schedule.Reentry(T);
            s.sunVis = 1f - 0.7f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(Schedule.ContactTime + 60f, Schedule.ContactTime + 240f, T));
            s.shake = Schedule.Shake(T);
            s.flash = Schedule.Flash(T);
            s.speed = Schedule.AirSpeed;
            s.worldRate = 1f;
        }
    }
}
