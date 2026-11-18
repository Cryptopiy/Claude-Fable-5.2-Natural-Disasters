namespace ZERO
{
    /// <summary>Everything the procedural audio needs from the game, pushed once per frame from the main thread
    /// (<see cref="AudioEngine.Push"/>). The listener sits in an airliner at 11.5 km; the asteroid lands 260 km away.
    /// The continuous soundscape is derived from these numbers; discrete sounds arrive as events
    /// (<see cref="AudioEngine.Event"/>, see <see cref="AudioDirector"/>).</summary>
    public struct AudioState
    {
        /// <summary>Unused (kept for compatibility): always 0.</summary>
        public float chainage;
        /// <summary>Airspeed in m/s (about 235, constant). The cabin bed (airflow, engines) may follow it.</summary>
        public float speed;
        /// <summary>World time rate: always 1.</summary>
        public float worldRate;
        /// <summary>0 normal, 1 breath (the wall is on the aircraft: the sound is sucked away for a moment), 2 the hit
        /// (the wave strikes the airframe), 3 white (hard stop, faint ring), 4 black (silence).</summary>
        public int endPhase;
        /// <summary>0..1 master gain (fade in at the start, pause).</summary>
        public float masterGain;

        /// <summary>Seconds since the flight began (0..about 665).</summary>
        public float eventTime;
        /// <summary>Seconds since the asteroid touched the ground (negative before; the contact is at eventTime 360).</summary>
        public float sinceContact;
        /// <summary>Metres from the aircraft to the blast wave: 1e6 before the contact, about 286 000 at the contact,
        /// 0 when it strikes (288 s later). It closes at 600-1500 m/s.</summary>
        public float frontDist;
        /// <summary>Metres the asteroid still has to travel: 2 700 000 at the start, 0 at the contact.</summary>
        public float descent;
        /// <summary>Metres from the aircraft to the asteroid (2 600 000 .. 260 000).</summary>
        public float bodyDist;
        /// <summary>0..1 ejecta re-entering all over the sky (from about 70 s after the contact): the air above is full of fire.</summary>
        public float fire;
        /// <summary>1 = cabin in sunlight, lower when the sky has gone dark.</summary>
        public float sunVis;
        /// <summary>0..1 how hard the airframe shakes (0.02 in cruise, rising as the wave nears, 1 at the strike).</summary>
        public float shake;
        /// <summary>0..1 light of the explosion in the cabin (a ramp at the contact, then a long decay).</summary>
        public float flash;
    }
}
