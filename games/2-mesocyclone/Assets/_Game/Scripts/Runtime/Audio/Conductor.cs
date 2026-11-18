using System;
using System.Collections.Generic;
using UnityEngine;

namespace MESOCYCLONE.Audio
{
    /// <summary>The main-thread half of the audio: once per frame it publishes the snapshot and turns the scores into
    /// timed events (thunder with its travel time, radio crashes at the flash, transformer pops, the big hail
    /// stones, the three cracks, the pressure thump). Shared by the real-time rig and the offline renderer, so
    /// both make the same sound. Main thread only (Story.Evaluate and LightningScore.Ambient are not thread safe).</summary>
    public sealed class Conductor
    {
        public const float Lookahead = 0.12f;         // events are queued this much before they are due
        public const float MaxThunderDist = 16500f;   // beyond this lightning is silent ("heat lightning")
        const float BackScan = 80f;                   // after a seek: thunder still in flight or still rolling
        const double TownS = 15000.0, TownLateral = 900.0;

        public int Dropped;                           // events lost to a full queue (for logs)
        public int Sent;

        readonly Engine engine;
        int epoch;
        float scanned = -1f, lastT = -1f;
        int nextSlot;
        bool hasPending, wasRunning, thumpDone;
        Flash pending;
        float prevTorDist;
        Snapshot snap;

        public Conductor(Engine engine) { this.engine = engine; }

        static bool Live { get { return Director.Running && !Director.Warmup; } }

        /// <summary>Call once per frame after the Director. rate = story seconds per real second of this frame.</summary>
        public void Tick(float rate)
        {
            float T = Director.T;
            bool live = Live;
            if (live)
            {
                if (!wasRunning || scanned < 0f || T < lastT - 0.02f || T > lastT + 2f) Resync();
                float hi = T + Lookahead;
                if (hi > scanned) { Scan(scanned, hi); scanned = hi; }
                lastT = T;
            }
            wasRunning = live;
            Publish(rate, live);
        }

        /// <summary>After a seek: new epoch (the engine kills its voices), clocks resynced, thunder that is still on
        /// its way or still rolling is put back in flight.</summary>
        public void Resync()
        {
            float T = Director.T;
            epoch++;
            lastT = T; scanned = T + Lookahead;
            hasPending = false;
            thumpDone = T >= 545f;
            prevTorDist = float.MaxValue;
            float lo = scanned - BackScan;

            IReadOnlyList<Flash> list = LightningScore.Designed;
            int count = list.Count;
            for (int i = 0; i < count; i++)
            {
                Flash f = list[i];
                if (f.t0 > lo && f.t0 <= scanned) EmitPast(f, T);
            }
            int kLast = (int)Math.Floor(scanned / LightningScore.Slot);
            int kFirst = Math.Max(0, (int)Math.Floor(lo / LightningScore.Slot));
            for (int k = kFirst; k <= kLast; k++)
            {
                Flash f;
                if (!LightningScore.Ambient(k, out f)) continue;
                if (f.t0 <= scanned) { if (f.t0 > lo) EmitPast(f, T); }
                else { pending = f; hasPending = true; }
            }
            nextSlot = kLast + 1;
        }

        void Scan(float lo, float hi)
        {
            // designed flashes (the list is short and sorted; a linear pass allocates nothing)
            IReadOnlyList<Flash> list = LightningScore.Designed;
            int count = list.Count;
            for (int i = 0; i < count; i++)
            {
                Flash f = list[i];
                if (f.t0 > hi) break;
                if (f.t0 > lo) EmitFlash(f);
            }
            // ambient flashes: each slot is evaluated once, when it comes into reach
            while (nextSlot * LightningScore.Slot <= hi)
            {
                if (hasPending) { EmitFlash(pending); hasPending = false; }
                hasPending = LightningScore.Ambient(nextSlot, out pending);
                nextSlot++;
            }
            if (hasPending && pending.t0 <= hi) { EmitFlash(pending); hasPending = false; }

            // the big stones
            int k0 = (int)Math.Floor(lo / HailScore.SlotLen), k1 = (int)Math.Floor(hi / HailScore.SlotLen);
            for (int k = k0; k <= k1; k++)
            {
                float time, size; HailScore.Where where; Vector2 pos;
                if (!HailScore.BigHit(k, out time, out where, out size, out pos)) continue;
                if (time <= lo || time > hi) continue;
                SoundEvent e = new SoundEvent { kind = Ev.HailHit, epoch = epoch, time = time, seed = (uint)k, a = (int)where, b = size, c = pos.x, d = pos.y };
                Send(e);
            }
            // the three cracks
            Crack(1, Story.Crack1Time, lo, hi); Crack(2, Story.Crack2Time, lo, hi); Crack(3, Story.Crack3Time, lo, hi);
        }

        void Crack(int index, float tc, float lo, float hi)
        {
            if (tc <= lo || tc > hi) return;
            SoundEvent e = new SoundEvent { kind = Ev.Crack, epoch = epoch, time = tc, a = index };
            Send(e);
        }

        void Send(in SoundEvent e)
        {
            if (engine.queue.TryEnqueue(e)) Sent++; else Dropped++;
        }

        /// <summary>A flash that happens now (within the look-ahead): radio crash at once, the sound later.</summary>
        void EmitFlash(in Flash f)
        {
            if (f.channelOnly) return;               // a drawn channel inside a sheet: the sheet makes the sound
            Vector3 p = LightningScore.LocalPos(f, Director.S);
            float dist = Mathf.Sqrt(p.x * p.x + p.y * p.y + p.z * p.z);
            float bearing = Mathf.Atan2(p.x, p.z) * Mathf.Rad2Deg;
            SoundEvent s = new SoundEvent { kind = Ev.Sferic, epoch = epoch, time = f.t0, seed = f.seed, a = f.power, b = dist, c = (int)f.kind };
            Send(s);
            if (f.silent) return;
            QueueSound(f, dist, bearing);
        }

        void QueueSound(in Flash f, float dist, float bearing)
        {
            double arrival = f.t0 + (double)dist / Config.SpeedOfSound;
            if (f.kind == FlashKind.Transformer)
            {
                SoundEvent e = new SoundEvent { kind = Ev.Pop, epoch = epoch, time = arrival, seed = f.seed, a = dist, b = bearing, c = f.power };
                Send(e);
            }
            else if (dist < MaxThunderDist)
            {
                SoundEvent e = new SoundEvent { kind = Ev.Thunder, epoch = epoch, time = arrival, seed = f.seed, a = dist, b = bearing, c = f.power, d = (int)f.kind };
                Send(e);
            }
        }

        /// <summary>A flash of the past (seek): only its sound matters, and only if it has not finished yet.
        /// Its position is taken relative to where the truck was at the flash (cinematic schedule).</summary>
        void EmitPast(in Flash f, float now)
        {
            if (f.channelOnly) return;
            if (f.silent) return;
            StoryState st = new StoryState();
            Story.Evaluate(f.t0, ref st);
            double s = Math.Min(f.t0 * (double)Config.SpeedBrisk, Director.GoalS);
            double ox, oz, wx, wz;
            PathDef.RoadToWorld(s, Director.LaneOffset(s), out ox, out oz);
            switch (f.anchor)
            {
                case FlashAnchor.World: wx = f.a; wz = f.b; break;
                case FlashAnchor.Road: PathDef.RoadToWorld(f.a, f.b, out wx, out wz); break;
                case FlashAnchor.Storm: wx = st.stormX + f.a; wz = st.stormZ + f.b; break;
                case FlashAnchor.Tornado: PathDef.RoadToWorld(st.torS, st.torLat, out wx, out wz); wx += f.a; wz += f.b; break;
                default: PathDef.RoadToWorld(s, Config.LaneOffset, out wx, out wz); wx += f.a; wz += f.b; break;
            }
            float x = (float)(wx - ox), z = (float)(wz - oz);
            float dist = Mathf.Sqrt(x * x + f.y * f.y + z * z);
            double arrival = f.t0 + (double)dist / Config.SpeedOfSound;
            float life = f.kind == FlashKind.Transformer ? 4f : ThunderVoice.Duration(dist, f.power) * 1.3f;
            if (arrival + life <= now) return;
            QueueSound(f, dist, Mathf.Atan2(x, z) * Mathf.Rad2Deg);
        }

        void Publish(float rate, bool live)
        {
            StoryState S = Director.S;
            Vector3 tor = Director.TornadoLocal(0f);
            Vector3 town = Director.RoadToLocal(TownS, TownLateral, 0f);
            float torDist = Mathf.Sqrt(tor.x * tor.x + tor.z * tor.z);

            // the pressure thump: once, just after the nearest pass of the wedge (beat 6)
            if (live && !thumpDone && S.T >= 535f && S.T < 556f && S.torPower > 0.6f && (torDist > prevTorDist + 0.01f || S.T >= 545f))
            {
                thumpDone = true;
                SoundEvent e = new SoundEvent { kind = Ev.EarThump, epoch = epoch, time = S.T + 0.75 };
                Send(e);
            }
            prevTorDist = torDist;

            snap.epoch = epoch;
            snap.running = live ? 1 : 0;
            snap.paused = Director.Paused ? 1 : 0;
            snap.T = S.T; snap.rate = rate;
            snap.speed = Director.Speed;
            snap.heading = Director.HeadingDeg; snap.headYaw = Director.HeadingDeg + CameraRig.Yaw;
            snap.rain = S.rain; snap.hail = S.hail; snap.hailSize = S.hailSize;
            snap.wind = S.wind; snap.windDir = S.windDir; snap.gust = S.gust; snap.dust = S.dust; snap.wetness = S.wetness;
            snap.wiperMode = WiperModel.Mode(S.T); snap.wiperPos = WiperModel.Position(S.T);
            snap.insectA = S.insectA; snap.insectB = S.insectB; snap.insectC = S.insectC;
            snap.radio = S.radio; snap.pressure = S.pressure; snap.siren = S.siren; snap.engine = S.engine;
            snap.ringing = S.ringing; snap.drips = S.drips;
            snap.torPower = S.torPower; snap.torDist = torDist; snap.torBearing = Mathf.Atan2(tor.x, tor.z) * Mathf.Rad2Deg;
            snap.torRadius = S.torRadius; snap.torDebris = S.torDebris; snap.torWrap = S.torWrap;
            snap.townDist = Mathf.Sqrt(town.x * town.x + town.z * town.z);
            snap.townBearing = Mathf.Atan2(town.x, town.z) * Mathf.Rad2Deg;
            snap.wallDist = (float)(S.dustWallZ - Director.OriginZ);
            snap.crack1 = S.crack1; snap.crack2 = S.crack2; snap.crack3 = S.crack3; snap.canopy = S.canopy;
            engine.box.Publish(snap);
        }
    }
}
