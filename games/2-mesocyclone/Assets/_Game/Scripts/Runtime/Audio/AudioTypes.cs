using System.Threading;

namespace MESOCYCLONE.Audio
{
    /// <summary>What the audio thread knows about "now": plain numbers copied from the main thread once per frame.</summary>
    public struct Snapshot
    {
        public int epoch;             // changes on a seek: the engine kills its voices and resyncs
        public int running, paused;   // 0 or 1
        public float T;               // story time
        public float rate;            // 1 while the story clock runs, 0 while it holds
        public float speed;           // m/s
        public float headYaw;         // world bearing of the nose in degrees (truck heading + head yaw)
        public float heading;         // truck heading in degrees
        public float rain, hail, hailSize, wind, windDir, gust, dust, wetness, wiperMode, wiperPos;
        public float insectA, insectB, insectC, radio, pressure, siren, engine, ringing, drips;
        public float torPower, torDist, torBearing, torRadius, torDebris, torWrap;
        public float townDist, townBearing;
        public float wallDist;        // metres to the gust front (dust wall) ahead; negative once it has passed
        public float crack1, crack2, crack3, canopy;
    }

    /// <summary>Event kinds of the ring buffer.</summary>
    public static class Ev
    {
        public const int Thunder = 1;      // a = distance m, b = world bearing deg, c = power, d = flash kind
        public const int Sferic = 2;       // a = power, b = distance m, c = flash kind (radio crash at the flash)
        public const int Pop = 3;          // transformer: a = distance m, b = world bearing deg, c = power
        public const int HailHit = 4;      // a = where (0 roof, 1 hood, 2 glass, 3 road), b = size m, c/d = position 0..1
        public const int Crack = 5;        // a = index 1..3
        public const int EarThump = 6;     // the pressure thump of the tornado's nearest pass
    }

    /// <summary>One scheduled sound. time = story time at which it is heard at the truck.</summary>
    public struct SoundEvent
    {
        public int kind, epoch;
        public uint seed;
        public double time;
        public float a, b, c, d;
    }

    /// <summary>Single-producer (main thread) single-consumer (audio thread) ring buffer, preallocated.</summary>
    public sealed class EventQueue
    {
        const int Size = 512, Mask = Size - 1;
        readonly SoundEvent[] q = new SoundEvent[Size];
        volatile int head, tail;

        public bool TryEnqueue(in SoundEvent e)
        {
            int h = head, n = (h + 1) & Mask;
            if (n == tail) return false;
            q[h] = e; head = n;
            return true;
        }

        public bool TryDequeue(out SoundEvent e)
        {
            int t = tail;
            if (t == head) { e = default(SoundEvent); return false; }
            e = q[t]; tail = (t + 1) & Mask;
            return true;
        }
    }

    /// <summary>The latest snapshot, handed from the main thread to the audio thread without a lock (sequence
    /// counter: the reader retries if a write happened during its copy and otherwise keeps its previous copy).</summary>
    public sealed class SnapshotBox
    {
        Snapshot value;
        int seq;

        public void Publish(in Snapshot s)
        {
            Interlocked.Increment(ref seq);
            value = s;
            Interlocked.Increment(ref seq);
        }

        public bool TryRead(ref Snapshot dst)
        {
            for (int i = 0; i < 4; i++)
            {
                int s0 = Volatile.Read(ref seq);
                if ((s0 & 1) != 0) continue;
                Snapshot copy = value;
                if (Volatile.Read(ref seq) == s0) { dst = copy; return true; }
            }
            return false;
        }
    }

    /// <summary>Bus levels of the mix in dB (linear factors are derived once). One place to balance the game.</summary>
    public static class Mix
    {
        public static readonly float Engine = Dsp.Db(-23f);
        public static readonly float Road = Dsp.Db(-25f);
        public static readonly float Cab = Dsp.Db(-18f);
        public static readonly float Insects = Dsp.Db(-35f);
        public static readonly float Radio = Dsp.Db(-18f);
        public static readonly float Wind = Dsp.Db(-12f);
        public static readonly float Front = Dsp.Db(-11f);
        public static readonly float Rain = Dsp.Db(-16f);
        public static readonly float Wiper = Dsp.Db(-22f);
        public static readonly float Hail = Dsp.Db(-9f);
        public static readonly float Crack = Dsp.Db(-5f);
        public static readonly float Thunder = Dsp.Db(-6f);
        public static readonly float Siren = Dsp.Db(-14.5f);
        public static readonly float Pop = Dsp.Db(-9f);
        public static readonly float Tornado = Dsp.Db(-11f);
        public static readonly float Drips = Dsp.Db(-14f);
        public static readonly float Reverb = Dsp.Db(-9f);
        public static readonly float Tinnitus = Dsp.Db(-34f);
        public static readonly float Floor = Dsp.Db(-57f);
    }
}
