using System;
using System.Threading;

namespace ZERO
{
    /// <summary>The complete procedural sound of ZERO, in real time. Pure C#: no UnityEngine calls, no allocation,
    /// no locks and no logging while rendering.
    /// The listener sits inside an airliner the whole time. Nothing outside is heard directly until the blast wave
    /// arrives: the event is silent at 260 km, and that silence is part of it.
    /// The continuous soundscape is a function of the state the game pushes every frame (<see cref="SetState"/>):
    /// the bed of an ordinary flight (airflow, two engines, air conditioning), a barely perceptible wrongness while
    /// the asteroid nears (bodyDist) and a faint rumble through the airframe while it burns (descent), the hush and
    /// the ringing after the light of the contact (flash, sinceContact), the rumble of the wave as it comes
    /// (frontDist), the airframe shuddering, groaning and rattling (shake), the dry crackle of the burning sky
    /// (fire), the roar of the last seconds, and the ending (endPhase): the breath, the hit, the ring, silence.
    /// Nothing runs on a timeline of its own, so the event clock may run slow, fast or jump.
    /// The discrete sounds arrive as events (<see cref="AddEvent"/>) and sound at once.
    /// Signal flow: cabin + events + cabin reverb -> ears (shut for a few seconds after the light) -> master gain
    /// -> DC blocker -> gate -> look-ahead limiter (-1 dBFS).</summary>
    public sealed class ZeroSynth
    {
        public const int ControlBlock = 32;                  // control rate = sampleRate / 32
        const int EventQueue = 64;                           // power of two
        const float CruiseSpeed = 235f;                      // m/s: the airspeed at which the engines sound as designed
        const float FarBody = 2600000f, NearBody = 260000f;  // metres to the asteroid: the wrongness grows between these
        const float EntryDescent = 240000f;                  // metres left to fall when the body starts to burn (32 s)
        const float RoarFrom = 6000f;                        // metres at which the wave itself begins to be heard (10 s)

        // ---- the mix, in dBFS (about the RMS of each source)
        const float AirDb = -33.5f, EngineDb = -36f, VentDb = -45.5f;                // an ordinary flight
        const float ThrobFarDb = -62f, ThrobNearDb = -38f, ShimmerDb = -52f;         // the wrongness
        const float EntryFromDb = -52f, EntryToDb = -38f;                            // the rumble of the entry
        const float SkyDb = -41f;
        const float ContactRingDb = -34f, BreathRingDb = -44f;
        const float ContactDuckDb = -12f, ContactMuffle = 0.85f;
        const float RevWet = 0.5f;
        const float HitCeiling = 0.80f;                      // the hit is driven into a soft clipper: dense and loud, never over this
        // The blast wave, against the kilometres it still has to come:
        static readonly float[] FrontKm =    { 0f, 6f, 20f, 34f, 58f, 124f, 200f, 260f };
        static readonly float[] RumbleDb =   { -24.5f, -25f, -27f, -30f, -34f, -40f, -45f, -50f };
        static readonly float[] RumbleOpen = { 1f, 0.85f, 0.55f, 0.4f, 0.25f, 0.1f, 0f, 0f };

        public readonly int SampleRate;
        /// <summary>Mix trims (linear, 1 = as designed). Plain fields, picked up at control rate.
        /// World = the continuous cabin, Ring = the ringing tones, Event = the discrete events, everything that ticks
        /// and rattles, and the hit (at 0 only the smooth continuous cabin is left: the stem of the click test).</summary>
        public float WorldTrim = 1f, RingTrim = 1f, EventTrim = 1f;
        /// <summary>Meters, written on the audio thread (read them for debugging only): the highest output sample
        /// so far and the current limiter gain (1 = no reduction).</summary>
        public float MeterPeak, MeterLimiterGain = 1f;
        /// <summary>Number of times a non-finite sample forced a reset (must stay 0).</summary>
        public int NanResets;
        /// <summary>Frames rendered so far.</summary>
        public long FramesRendered;
        /// <summary>How many times the hit (endPhase 2) was played.</summary>
        public int Hits;
        /// <summary>Events lost because the hand-over queue or the voice pool was full (should stay 0).</summary>
        public int EventsDropped { get { return queueDropped + events.Dropped; } }
        /// <summary>Debugging: bit mask of the continuous sources that sound (see CabinLayer.L*); -1 = all.</summary>
        public int DebugLayerMask = -1;

        readonly float fs, invFs;

        // ---- hand-over of the state from the main thread: a sequence lock, single writer, the reader never blocks
        int seq, inPhase, inValid;
        float inGain, inSpeed = CruiseSpeed, inSince = -360f, inFront = 1e6f, inDescent = 2700000f, inBody = FarBody,
              inFire, inSun = 1f, inShake, inFlash;
        AudioState cur;
        bool hasState;
        // ---- hand-over of the events: a single-producer single-consumer ring
        struct EventMsg { public int kind; public float dist, size, pan; }
        readonly EventMsg[] evq = new EventMsg[EventQueue];
        int evHead, evTail, queueDropped;
        // ---- the optional cue
        int cueSeq, cueSeen;
        float cueSeconds, cueT = -1f, cueHold, cueSm;

        // ---- layers
        readonly CabinLayer world;
        readonly EventBank events;
        readonly WaveHit hit;
        readonly HitRoar rush;
        readonly RingTone ring;
        readonly EndRing endRing;
        readonly Fdn4 reverb;
        readonly Limiter limiter;
        DcBlock dcL, dcR;
        Svf earL, earR;
        readonly float[] mixL, mixR, sendL, sendR, resL, resR, ringL, ringR;

        // ---- control state (audio thread only)
        int ctrlLeft, curPhase = -1, flush;
        bool positioned, mainActive, dead = true;
        Ramp gate, normal, rushMix;
        float master, masterTarget;
        readonly float masterCoef, kUp, kCueDown, kEar, kSlow;
        float ringAmp, muffT, duckT = 1f, muffSm, duckSm = 1f, shakeSm, fireSm, sunSm = 1f;
        CabinTargets ct;

        // A static constructor makes the tables initialise when the first synth is built (main thread),
        // never lazily on the audio thread.
        static ZeroSynth() { }

        /// <summary>Main thread, while loading: runs a throw-away synth through every code path (the flight, the
        /// contact, the wave coming, every kind of event, the whole ending) so that nothing is compiled or
        /// initialised for the first time on the audio thread in the middle of the flight. Takes a few tens of
        /// milliseconds.</summary>
        public static void Warm(int sampleRate)
        {
            var z = new ZeroSynth(sampleRate, 1u);
            var buf = new float[2048];
            var s = new AudioState
            {
                speed = CruiseSpeed, worldRate = 1f, endPhase = 0, masterGain = 1f, eventTime = 340f, sinceContact = -20f,
                frontDist = 1e6f, descent = 150000f, bodyDist = 300000f, fire = 0f, sunVis = 1f, shake = 0.02f, flash = 0f
            };
            z.SetState(s); Run(z, buf, 12);
            s.eventTime = 361f; s.sinceContact = 1f; s.descent = 0f; s.frontDist = 255000f; s.flash = 0.95f;       // the contact
            z.SetState(s);
            for (int k = 0; k < EventVoice.Kinds; k++) z.AddEvent((AudioEventKind)k, k == 1 ? 9000f : 4f, 1f, k * 0.4f - 0.8f);
            z.ThinWorld(0.5f);
            Run(z, buf, 40);
            s.eventTime = 600f; s.sinceContact = 240f; s.frontDist = 27000f; s.fire = 1f; s.sunVis = 0.3f; s.shake = 0.2f; s.flash = 0.22f;
            z.SetState(s); Run(z, buf, 12);
            s.eventTime = 645f; s.sinceContact = 285f; s.frontDist = 1700f; s.shake = 0.8f;                        // the last seconds
            z.SetState(s); Run(z, buf, 12);
            for (int phase = 1; phase <= 4; phase++)
            {
                s.endPhase = phase;
                z.SetState(s); Run(z, buf, 8);
            }
            s.endPhase = 0;
            z.SetState(s);
            z.Render(buf, 1024, 2);
            z.Render(buf, 512, 1);
        }

        static void Run(ZeroSynth z, float[] buf, int blocks) { for (int b = 0; b < blocks; b++) z.Render(buf, 1024, 2); }

        public ZeroSynth(int sampleRate, uint seed)
        {
            if (sampleRate < 8000) sampleRate = 48000;
            SampleRate = sampleRate; fs = sampleRate; invFs = 1f / fs;
            world = new CabinLayer(sampleRate, seed + 2000u, ControlBlock);
            hit = new WaveHit(sampleRate, seed + 3000u);
            rush = new HitRoar(sampleRate, seed + 4000u, ControlBlock, world.Groan);
            events = new EventBank(sampleRate, seed + 5000u, ControlBlock);
            ring = new RingTone(sampleRate);
            endRing = new EndRing(sampleRate);
            reverb = new Fdn4(sampleRate, 0.45f, 3500f, 0.2f);       // a small, damped room: the cabin
            limiter = new Limiter(sampleRate);
            dcL.Set(8f, fs); dcR.Set(8f, fs);
            earL.Set(420f, 0.6f, fs); earR.Set(420f, 0.6f, fs);
            mixL = new float[ControlBlock]; mixR = new float[ControlBlock];
            sendL = new float[ControlBlock]; sendR = new float[ControlBlock];
            resL = new float[ControlBlock]; resR = new float[ControlBlock];
            ringL = new float[ControlBlock]; ringR = new float[ControlBlock];
            float dt = ControlBlock * invFs;
            masterCoef = Dsp.TauCoef(0.025f, fs);
            kUp = 1f - (float)Math.Exp(-dt / 0.35f);
            kCueDown = 1f - (float)Math.Exp(-dt / 1.0f);
            kSlow = 1f - (float)Math.Exp(-dt / 0.25f);
            kEar = Dsp.TauCoef(0.02f, fs);
            gate.Jump(0f); normal.Jump(0f); rushMix.Jump(0f);
        }

        /// <summary>Main thread, once per frame. Lock-free; the audio thread picks the state up at its next block.
        /// A state whose event fields are all zero (a game may push one while it is paused) keeps the last event
        /// fields, so a pause can never look like the contact or the wave arriving.</summary>
        public void SetState(in AudioState s)
        {
            bool filled = s.eventTime != 0f || s.frontDist != 0f || s.descent != 0f || s.bodyDist != 0f;
            Interlocked.Increment(ref seq);                  // odd: write in progress
            Volatile.Write(ref inPhase, s.endPhase);
            Volatile.Write(ref inGain, s.masterGain);
            if (filled)
            {
                Volatile.Write(ref inSpeed, s.speed);
                Volatile.Write(ref inSince, s.sinceContact);
                Volatile.Write(ref inFront, s.frontDist);
                Volatile.Write(ref inDescent, s.descent);
                Volatile.Write(ref inBody, s.bodyDist);
                Volatile.Write(ref inFire, s.fire);
                Volatile.Write(ref inSun, s.sunVis);
                Volatile.Write(ref inShake, s.shake);
                Volatile.Write(ref inFlash, s.flash);
            }
            Volatile.Write(ref inValid, 1);
            Interlocked.Increment(ref seq);                  // even: stable
        }

        /// <summary>Main thread: a discrete sound is heard now (see <see cref="AudioEventKind"/>); 'distance' in metres
        /// only tells how far and dull it is, size 0..1, pan -1 (left) .. +1 (right). Lock-free, no allocation: a
        /// single-producer ring that the audio thread drains within a millisecond. Matches the emit callback of
        /// AudioDirector.Tick.</summary>
        public void AddEvent(AudioEventKind kind, float distance, float size, float pan)
        {
            int h = evHead;                                  // written by this thread only
            int next = (h + 1) & (EventQueue - 1);
            if (next == Volatile.Read(ref evTail)) { queueDropped++; return; }     // full: the audio thread is not running
            evq[h].kind = (int)kind; evq[h].dist = distance; evq[h].size = size; evq[h].pan = pan;
            Volatile.Write(ref evHead, next);                // publish
        }

        /// <summary>Optional, main thread: thins the continuous soundscape by 7 dB for "seconds" (0.5..10), easing in
        /// over about a second and back out over three. Use it 2-3 s before a money shot: silence is a layer.</summary>
        public void ThinWorld(float seconds)
        {
            Volatile.Write(ref cueSeconds, seconds);
            Interlocked.Increment(ref cueSeq);
        }

        static bool Ok(float x) { return !float.IsNaN(x) && !float.IsInfinity(x); }

        void ReadState()
        {
            for (int tries = 0; tries < 8; tries++)
            {
                int a = Volatile.Read(ref seq);
                if ((a & 1) != 0) continue;
                float g = Volatile.Read(ref inGain), sp = Volatile.Read(ref inSpeed), si = Volatile.Read(ref inSince);
                float fr = Volatile.Read(ref inFront), de = Volatile.Read(ref inDescent), bo = Volatile.Read(ref inBody);
                float fi = Volatile.Read(ref inFire), su = Volatile.Read(ref inSun), sh = Volatile.Read(ref inShake), fl = Volatile.Read(ref inFlash);
                int ph = Volatile.Read(ref inPhase), valid = Volatile.Read(ref inValid);
                if (a != Volatile.Read(ref seq)) continue;
                if (valid == 0) return;
                if (Ok(g)) cur.masterGain = g;               // NaN and Infinity never get in
                if (Ok(sp)) cur.speed = sp;
                if (Ok(si)) cur.sinceContact = si;
                if (Ok(fr)) cur.frontDist = fr;
                if (Ok(de)) cur.descent = de;
                if (Ok(bo)) cur.bodyDist = bo;
                if (Ok(fi)) cur.fire = fi;
                if (Ok(su)) cur.sunVis = su;
                if (Ok(sh)) cur.shake = sh;
                if (Ok(fl)) cur.flash = fl;
                cur.endPhase = ph;
                hasState = true;
                return;
            }
        }

        void ReadEvents()
        {
            int t = evTail;                                  // written by this thread only
            int h = Volatile.Read(ref evHead);
            if (t == h) return;
            bool take = mainActive && curPhase == 0;         // nothing sounds into a closed gate
            while (t != h)
            {
                if (take) events.Trigger(evq[t].kind, evq[t].dist, evq[t].size, evq[t].pan);
                t = (t + 1) & (EventQueue - 1);
            }
            Volatile.Write(ref evTail, t);
        }

        /// <summary>Audio thread. Overwrites interleaved[0 .. frames*channels) with the next frames.
        /// Stereo expected; mono gets the sum, extra channels get silence.</summary>
        public void Render(float[] interleaved, int frames, int channels)
        {
            if (interleaved == null || channels < 1) return;
            int max = interleaved.Length / channels;
            if (frames > max) frames = max;
            ReadState();
            int done = 0;
            while (done < frames)
            {
                if (ctrlLeft <= 0) { Control(); ReadEvents(); ctrlLeft = ControlBlock; }
                int n = frames - done;
                if (n > ctrlLeft) n = ctrlLeft;
                RenderChunk(n);
                int o = done * channels;
                if (channels == 2)
                    for (int i = 0; i < n; i++) { interleaved[o++] = mixL[i]; interleaved[o++] = mixR[i]; }
                else if (channels == 1)
                    for (int i = 0; i < n; i++) interleaved[o++] = 0.5f * (mixL[i] + mixR[i]);
                else
                    for (int i = 0; i < n; i++)
                    {
                        interleaved[o] = mixL[i]; interleaved[o + 1] = mixR[i];
                        for (int c = 2; c < channels; c++) interleaved[o + c] = 0f;
                        o += channels;
                    }
                ctrlLeft -= n; done += n;
            }
            FramesRendered += frames;
        }

        int Ms(float ms) { return Math.Max(1, (int)(ms * 0.001f * fs)); }

        /// <summary>Soft clipper of the hit: linear gain 1.5 for small signals, never above HitCeiling.</summary>
        static float Saturate(float x)
        {
            x *= 1f / HitCeiling;
            if (x > 1f) x = 1f; else if (x < -1f) x = -1f;
            return HitCeiling * (1.5f * x - 0.5f * x * x * x);
        }

        void EnterPhase(int phase)
        {
            int prev = curPhase;
            curPhase = phase;
            bool silent = gate.Value <= 0f;
            switch (phase)
            {
                case 0:                                      // the flight
                    cueT = -1f; cueSm = 0f;
                    hit.Stop();
                    if (silent) { normal.Jump(1f); rushMix.Jump(0f); }
                    else { normal.To(1f, Ms(60f)); rushMix.To(0f, Ms(30f)); }
                    gate.To(1f, Ms(60f));
                    endRing.Release();
                    break;
                case 1:                                      // the breath: the wall is on the aircraft, everything is sucked away
                    gate.To(0f, Ms(120f));
                    break;
                case 2:                                      // the hit: the slam at once, the roar with its own 0.35 s ramp
                    if (silent) normal.Jump(0f); else normal.To(0f, Ms(40f));
                    rushMix.Jump(1f);
                    rush.Start();
                    hit.Trigger();
                    Hits++;
                    gate.To(1f, Ms(5f));
                    break;
                case 3:                                      // white: hard stop within 25 ms, then only the ring
                    gate.To(0f, Ms(25f));
                    if (prev != 4) endRing.Start();
                    break;
                default:                                     // black
                    gate.To(0f, Ms(30f));
                    endRing.Release();
                    break;
            }
        }

        void ResetMain()
        {
            world.Reset(); events.Reset(); hit.Stop(); rush.Stop();
            reverb.Clear(); earL.Reset(); earR.Reset();
            normal.Jump(0f); rushMix.Jump(0f);
            muffSm = 0f; duckSm = 1f; muffT = 0f; duckT = 1f;
        }

        void Control()
        {
            float dt = ControlBlock * invFs;
            if (!hasState) { masterTarget = 0f; return; }
            AudioState s = cur;
            int phase = s.endPhase < 0 ? 0 : (s.endPhase > 4 ? 4 : s.endPhase);
            masterTarget = Dsp.Clamp01(s.masterGain);
            int mask = DebugLayerMask;

            if (!positioned || phase != curPhase) { positioned = true; EnterPhase(phase); }

            // ---- the thin ringing: after the light of the contact, and alone in the breath
            float t = s.sinceContact;
            float lit = Dsp.Smooth01(Dsp.Clamp01(s.flash) / 0.6f);              // the light has come up
            ringAmp = 0f;
            if (phase == 1) ringAmp = Dsp.DbToLin(BreathRingDb) * RingTrim;
            else if (phase == 0 && t > 0f && t < 40f)
                ringAmp = Dsp.DbToLin(ContactRingDb) * lit * Dsp.Smooth01(t / 1.5f) * (float)Math.Exp(-Math.Max(0f, t - 1.5f) / 3.2f) * RingTrim;
            if (ringAmp < 1e-6f) ringAmp = 0f;

            bool active = gate.Value > 0f || gate.Target > 0f;
            if (!active && mainActive) ResetMain();
            mainActive = active;
            if (!active) return;

            if (rush.Active)
            {
                if (rushMix.Value <= 0f && rushMix.Done) { rush.Stop(); hit.Stop(); }
                else { rush.Control(dt); if (hit.Active) hit.Control(dt); }
            }
            if (!(normal.Value > 0f || normal.Target > 0f)) return;

            // ---- the pushed state
            float fd = s.frontDist;
            bool wave = fd < 500000f;                                           // the blast wave is on its way
            float fdKm = fd * 0.001f;
            float near = Dsp.Clamp01((FarBody - s.bodyDist) / (FarBody - NearBody));        // 0 at 2 600 km .. 1 at the contact
            float after = t > 0f ? (float)Math.Exp(-t / 4f) : 1f;               // what belonged to the approach dies after the contact
            shakeSm += (Dsp.Clamp01(s.shake) - shakeSm) * kSlow;
            fireSm += (Dsp.Clamp01(s.fire) - fireSm) * kSlow;
            sunSm += (Dsp.Clamp01(s.sunVis) - sunSm) * kSlow;
            Dsp.Zap(ref shakeSm); Dsp.Zap(ref fireSm);
            float a = Dsp.Clamp01((shakeSm - 0.02f) / 0.22f);                   // 0..1 while the wave nears
            float b = Dsp.Clamp01((shakeSm - 0.24f) / 0.76f);                   // 0..1 in the last ten seconds
            float stress = Dsp.Smooth01(a * 5f);

            // ---- the ears: they shut for a few seconds when the light comes (no bang: it is 260 km away)
            float closed = t > 0f && t < 40f ? lit * (t < 2f ? 1f : (float)Math.Exp(-(t - 2f) / 2.5f)) : 0f;
            muffT = ContactMuffle * closed;
            duckT = Dsp.DbToLin(ContactDuckDb * closed);

            // ---- an optional cue from the game: thin the cabin before a money shot
            int cue = Volatile.Read(ref cueSeq);
            if (cue != cueSeen) { cueSeen = cue; cueT = 0f; cueHold = Dsp.Clamp(Volatile.Read(ref cueSeconds), 0.5f, 10f); }
            float cueTarget = 0f;
            if (cueT >= 0f) { cueT += dt; if (cueT < cueHold) cueTarget = 1f; else cueT = -1f; }
            cueSm += (cueTarget - cueSm) * (cueTarget > cueSm ? kUp : kCueDown);
            if (cueSm < 1e-6f && cueTarget <= 0f) cueSm = 0f;

            // ---- an ordinary flight; the air gets rough and the engines waver when the airframe shakes
            ct.air = Dsp.DbToLin(AirDb);
            ct.rough = 0.5f * a + 0.5f * b;
            ct.dark = 1f - sunSm;
            ct.engine = Dsp.DbToLin(EngineDb);
            ct.pitch = s.speed > 1f ? Dsp.Clamp(s.speed / CruiseSpeed, 0.8f, 1.2f) : 1f;
            ct.vent = Dsp.DbToLin(VentDb);

            // ---- the wrongness while the asteroid nears: a very low throb, a thin high shimmer
            ct.throb = near > 0f ? Dsp.DbToLin(Dsp.Lerp(ThrobFarDb, ThrobNearDb, near)) * Dsp.Smooth01(near * 6f) * after : 0f;
            ct.throbRate = 0.22f + 0.2f * near;
            ct.shimmer = Dsp.DbToLin(ShimmerDb) * near * near * after;

            // ---- the rumble through the airframe: faint while the body burns, then the wave: slowly at first, steeply at the end
            float entry = 0f;
            if (s.descent < EntryDescent)
            {
                float e = 1f - Math.Max(s.descent, 0f) / EntryDescent;
                entry = Dsp.DbToLin(Dsp.Lerp(EntryFromDb, EntryToDb, e)) * Dsp.Smooth01(e * 6f) * (t > 0f ? (float)Math.Exp(-t / 3f) : 1f);
            }
            float coming = wave ? Dsp.DbToLin(Dsp.Curve(fdKm, FrontKm, RumbleDb)) : 0f;
            ct.rumble = coming > entry ? coming : entry;
            ct.rumbleOpen = wave ? Dsp.Curve(fdKm, FrontKm, RumbleOpen) : 0f;

            // ---- the airframe under the load
            ct.buffet = stress > 0f ? Dsp.DbToLin(-46f + 14f * a + 6f * b) * stress : 0f;
            ct.groan = stress > 0f ? Dsp.DbToLin(-48f + 12f * a + 8f * b) * stress : 0f;
            ct.rattle = 1.5f * a * a + 10f * a * stress + 26f * b;
            ct.rattleGain = Dsp.DbToLin(-41f + 8f * a + 8f * b) * EventTrim;

            // ---- the wave itself in the last ten seconds
            if (wave && fd < RoarFrom)
            {
                float x = Dsp.Clamp01(1f - fd / RoarFrom);
                ct.roar = Dsp.DbToLin(-43f + 26f * (float)Math.Pow(x, 0.9)) * Dsp.Smooth01(x * 6f);
                ct.roarCut = 140f * (float)Math.Pow(27.0, x);
            }
            else { ct.roar = 0f; ct.roarCut = 140f; }

            // ---- the burning sky above
            ct.sky = EventTrim > 0f ? fireSm : 0f;                              // the ticks belong to the events, the hiss to the cabin
            ct.skyLevel = fireSm * Dsp.DbToLin(SkyDb);

            ct.bus = Dsp.DbToLin(-7f * cueSm) * WorldTrim;

            if (mask != -1)
            {
                if ((mask & CabinLayer.LAir) == 0) ct.air = 0f;
                if ((mask & CabinLayer.LEngine) == 0) ct.engine = 0f;
                if ((mask & CabinLayer.LVent) == 0) ct.vent = 0f;
                if ((mask & CabinLayer.LThrob) == 0) ct.throb = 0f;
                if ((mask & CabinLayer.LShimmer) == 0) ct.shimmer = 0f;
                if ((mask & CabinLayer.LRumble) == 0) ct.rumble = 0f;
                if ((mask & CabinLayer.LBuffet) == 0) ct.buffet = 0f;
                if ((mask & CabinLayer.LGroan) == 0) ct.groan = 0f;
                if ((mask & CabinLayer.LRattle) == 0) ct.rattle = 0f;
                if ((mask & CabinLayer.LRoar) == 0) ct.roar = 0f;
                if ((mask & CabinLayer.LSky) == 0) { ct.sky = 0f; ct.skyLevel = 0f; }
            }
            world.Control(dt, ref ct);
        }

        void RenderChunk(int n)
        {
            bool ringOn = ringAmp > 0f || ring.Sounding;
            if (mainActive || endRing.Active || ringOn) { flush = limiter.Latency + 8; dead = false; }
            else if (flush > 0) flush -= n;
            else
            {
                if (!dead) { dead = true; limiter.Reset(); dcL.Reset(); dcR.Reset(); master = masterTarget; }
                for (int i = 0; i < n; i++) { mixL[i] = 0f; mixR[i] = 0f; }      // total digital silence
                return;
            }

            for (int i = 0; i < n; i++)
            {
                mixL[i] = 0f; mixR[i] = 0f; sendL[i] = 0f; sendR[i] = 0f;
                resL[i] = 0f; resR[i] = 0f; ringL[i] = 0f; ringR[i] = 0f;
            }
            if (mainActive)
            {
                if (normal.Value > 0f || normal.Target > 0f)
                {
                    world.Process(mixL, mixR, sendL, sendR, n);
                    events.Process(mixL, mixR, sendL, sendR, n, EventTrim);
                    // the cabin answers; then the ears (shut and ducked for a few seconds after the light)
                    float mt = muffT, dk = duckT, ke = kEar;
                    for (int i = 0; i < n; i++)
                    {
                        reverb.Process(sendL[i], sendR[i], out float vl, out float vr);
                        muffSm += ke * (mt - muffSm); duckSm += ke * (dk - duckSm);
                        float a = mixL[i] + vl * RevWet, b = mixR[i] + vr * RevWet;
                        a += muffSm * (earL.Lp(a) - a);
                        b += muffSm * (earR.Lp(b) - b);
                        mixL[i] = a * duckSm;
                        mixR[i] = b * duckSm;
                    }
                    Dsp.Zap(ref muffSm);
                    earL.Zap(); earR.Zap();
                }
                if (rush.Active) rush.Process(resL, resR, n);
                if (hit.Active) hit.Process(resL, resR, n, 1f);
            }
            if (ringOn) ring.Process(ringL, ringR, n, ringAmp);

            float mtg = masterTarget, et = EventTrim, pk = MeterPeak;
            for (int i = 0; i < n; i++)
            {
                float ng = normal.Next(); ng = ng * ng * (3f - 2f * ng);
                float rg = rushMix.Next() * et;
                master += masterCoef * (mtg - master);
                float l = dcL.Tick((mixL[i] * ng + Saturate(resL[i] * rg)) * master);
                float r = dcR.Tick((mixR[i] * ng + Saturate(resR[i] * rg)) * master);
                float g = gate.Next(); g = g * g * (3f - 2f * g);
                l = l * g + ringL[i] * master; r = r * g + ringR[i] * master;
                if (endRing.Active) { float e = endRing.Tick() * master * RingTrim; l += e; r += e; }
                limiter.Process(ref l, ref r);
                mixL[i] = l; mixR[i] = r;
                float a = l < 0f ? -l : l; if (a > pk) pk = a;
                a = r < 0f ? -r : r; if (a > pk) pk = a;
            }
            if (mtg <= 0f && master < 1e-6f) master = 0f;
            dcL.Zap(); dcR.Zap();
            MeterPeak = pk; MeterLimiterGain = limiter.Gain;

            float last = mixL[n - 1] + mixR[n - 1];
            if (float.IsNaN(last) || float.IsInfinity(last))                     // cannot happen; never let it stick
            {
                NanResets++;
                ResetMain(); limiter.Reset(); dcL.Reset(); dcR.Reset(); endRing.Stop(); ring.Reset();
                master = 0f; MeterPeak = 0f;
                positioned = false;                                              // re-enter the current phase cleanly
                for (int i = 0; i < n; i++) { mixL[i] = 0f; mixR[i] = 0f; }
            }
        }
    }
}
