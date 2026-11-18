using System;

namespace MESOCYCLONE.Audio
{
    /// <summary>The whole procedural sound of the game as one plain C# object: no Unity calls, no allocation after
    /// the constructor. AudioRig drives it from OnAudioFilterRead, OfflineAudio drives it for the WAV check: the
    /// same code path. It renders in sub-blocks of 64 frames; every control value is smoothed and every gain is
    /// ramped per sample, so nothing ever steps.
    /// Buses: cab (crisp) + world (low-passed by the cab) + open (the vent) -> ears -> noise floor -> master.</summary>
    public sealed class Engine
    {
        // voice bits for VoiceMask (debugging and offline balance; -1 = everything)
        public const int VTruck = 1, VCab = 2, VInsects = 4, VRadio = 8, VWind = 16, VFront = 32, VRain = 64, VWiper = 128,
            VHail = 256, VThunder = 512, VSiren = 1024, VPop = 2048, VTornado = 4096, VEar = 8192;

        public readonly int sr;
        public readonly SnapshotBox box = new SnapshotBox();
        public readonly EventQueue queue = new EventQueue();
        public float LastPeak, LastMinGain = 1f;
        public int Resets;                          // times the voices had to be reset after a numeric fault
        public int VoiceMask = -1;

        readonly Ctx c;
        readonly Master master;
        readonly Ear ear;
        readonly Fdn tail;
        readonly Panels panels;
        readonly TruckVoice truck;
        readonly CabVoice cab;
        readonly InsectVoice insects;
        readonly RadioVoice radio;
        readonly WindVoice wind;
        readonly FrontVoice front;
        readonly RainVoice rain;
        readonly WiperVoice wipers;
        readonly HailVoice hail;
        readonly ThunderVoice thunder;
        readonly SirenVoice sirens;
        readonly PopVoice pops;
        readonly TornadoVoice tornado;

        Snapshot snap, cur;
        bool haveSnap, started, firstFadeDone;
        double storyNow;
        int epoch = int.MinValue;
        float fade, floorFade;
        readonly SoundEvent[] pend = new SoundEvent[192];
        int pendCount;
        readonly float[] outL = new float[Ctx.Sub], outR = new float[Ctx.Sub];
        float mL1, mL2, mR1, mR2, fzL, fzR, worldPrev = 1f;
        readonly float muffleA, floorA, floorGain;
        Rng floorRng;

        public Engine(int sampleRate)
        {
            sr = sampleRate < 8000 ? 48000 : sampleRate;
            c = new Ctx(sr); master = new Master(sr); ear = new Ear(sr);
            tail = new Fdn(sr, 4.5f, 1400f);
            panels = new Panels(sr);
            truck = new TruckVoice(sr); cab = new CabVoice(sr); insects = new InsectVoice(sr); radio = new RadioVoice(sr);
            wind = new WindVoice(sr); front = new FrontVoice(sr); rain = new RainVoice(sr); wipers = new WiperVoice(sr);
            hail = new HailVoice(sr); thunder = new ThunderVoice(sr); sirens = new SirenVoice(sr); pops = new PopVoice(sr);
            tornado = new TornadoVoice(sr);
            muffleA = Dsp.Pole(2600f, sr);
            floorA = Dsp.Pole(3000f, sr);
            floorGain = Mix.Floor / MathF.Sqrt(0.3333f * floorA / (2f - floorA));      // so that the floor's RMS is Mix.Floor
            floorRng.Seed(77u);
        }

        void ResetVoices()
        {
            c.Reseed(); ear.Reset(); tail.Reset(); panels.Reset();
            truck.Reset(); cab.Reset(); insects.Reset(); radio.Reset(); wind.Reset(); front.Reset(); rain.Reset(); wipers.Reset();
            hail.Reset(); thunder.Reset(); sirens.Reset(); pops.Reset(); tornado.Reset();
            mL1 = mL2 = mR1 = mR2 = 0f; worldPrev = 1f;
        }

        /// <summary>Adds the game's sound to the first two channels of an interleaved buffer, then runs the master
        /// chain (DC blocker, limiter) over what is there. Audio thread (or the offline renderer).</summary>
        public void Render(float[] buf, int frames, int channels)
        {
            if (box.TryRead(ref snap)) haveSnap = true;
            SoundEvent e;
            while (queue.TryDequeue(out e)) { if (pendCount < pend.Length) pend[pendCount++] = e; }
            if (master.Faulted) { master.Faulted = false; ResetVoices(); Resets++; }

            int done = 0;
            while (done < frames)
            {
                int n = frames - done;
                if (n > Ctx.Sub) n = Ctx.Sub;
                float dt = n / (float)sr;
                bool live = haveSnap && snap.running != 0;
                if (live) started = true;
                bool jumping = live && snap.epoch != epoch;
                float f0 = fade;
                if (jumping && f0 <= 0f) { Resync(); jumping = false; }
                if (!jumping) cur = snap;
                float target = live && snap.paused == 0 && !jumping ? 1f : 0f;
                if (target > fade) fade = Math.Min(1f, fade + dt / (firstFadeDone ? 0.35f : 1f));
                else if (target < fade) fade = Math.Max(0f, fade - dt / 0.04f);
                if (fade >= 1f) firstFadeDone = true;

                if (f0 > 0f || fade > 0f) RenderSub(n);
                else { Array.Clear(outL, 0, n); Array.Clear(outR, 0, n); }

                // fade, the noise floor (the game never goes to digital silence once it runs), mix into the buffer
                float fl0 = floorFade;
                if (started) floorFade = Math.Min(1f, floorFade + dt);
                float g = f0, dg = (fade - f0) / n, fg = fl0 * floorGain, dfg = (floorFade - fl0) * floorGain / n;
                int o = done * channels;
                for (int j = 0; j < n; j++)
                {
                    g += dg; fg += dfg;
                    fzL += floorA * (floorRng.Bi() - fzL); fzR += floorA * (floorRng.Bi() - fzR);
                    float l = outL[j] * g + fzL * fg, r = outR[j] * g + fzR * fg;
                    if (channels >= 2) { buf[o] += l; buf[o + 1] += r; }
                    else buf[o] += 0.5f * (l + r);
                    o += channels;
                }
                done += n;
            }
            master.Process(buf, frames, channels);
            LastPeak = master.Peak; LastMinGain = master.MinGain;
        }

        void Resync()
        {
            epoch = snap.epoch; cur = snap; storyNow = snap.T;
            ResetVoices();
            c.SnapTo(cur);
            int k = 0;
            for (int i = 0; i < pendCount; i++) if (pend[i].epoch == epoch) pend[k++] = pend[i];
            pendCount = k;
        }

        void RenderSub(int n)
        {
            c.Begin(n, cur);
            // the story clock follows the published time: never backwards, never far ahead
            double next = storyNow + c.dt * cur.rate;
            double lo = cur.T - 0.10, hi = cur.T + 0.06;
            if (next < lo) next = lo;
            if (next > hi) next = hi > storyNow ? hi : storyNow;
            c.T = storyNow; c.Tnext = next; c.storyDt = (float)(next - storyNow);
            Dispatch(next);
            float t = (float)storyNow;
            int mask = VoiceMask;

            // the inhale before the gust front, the masking under the hail, the hush before a close thunder
            float inh = Dsp.Step(237.7f, 238.7f, t) * (1f - Dsp.Step(240.2f, 240.6f, t));
            float duck = (1f - 0.62f * inh) * (1f - 0.45f * Dsp.Step(0.15f, 0.9f, c.s.hail)) * (1f - thunder.Hush) * (1f - 0.4f * ear.Block);
            c.bedDuck += (duck - c.bedDuck) * c.k50;
            float dull = inh > thunder.Hush * 1.6f ? inh : thunder.Hush * 1.6f;
            c.dull += (dull - c.dull) * c.k50;

            if ((mask & VTruck) != 0) truck.Render(c);
            if ((mask & VCab) != 0) cab.Render(c, panels);
            if ((mask & VInsects) != 0) insects.Render(c);
            if ((mask & VRadio) != 0) radio.Render(c);
            if ((mask & VWind) != 0) wind.Render(c);
            if ((mask & VFront) != 0) front.Render(c);
            if ((mask & VRain) != 0) rain.Render(c, panels);
            if ((mask & VWiper) != 0) wipers.Render(c, panels);
            if ((mask & VHail) != 0) hail.Render(c, panels);
            if ((mask & VThunder) != 0) thunder.Render(c);
            if ((mask & VSiren) != 0) sirens.Render(c);
            if ((mask & VPop) != 0) pops.Render(c);
            if ((mask & VTornado) != 0) tornado.Render(c);
            panels.Render(c);
            tail.Process(c.revL, c.revR, c.worldL, c.worldR, n, Mix.Reverb);

            // the cab muffles the world: two poles at 2.6 kHz and a little leak of the highs through the glass
            // (during the inhale before the gust front the whole outside world steps back, tails included)
            float a = muffleA, worldGain = 1f - 0.55f * c.dull, wg = worldPrev, dwg = (worldGain - worldPrev) / n;
            worldPrev = worldGain;
            for (int j = 0; j < n; j++)
            {
                wg += dwg;
                float wl = c.worldL[j] * wg + Dsp.Tiny, wr = c.worldR[j] * wg + Dsp.Tiny;
                mL1 += a * (wl - mL1); mL2 += a * (mL1 - mL2);
                mR1 += a * (wr - mR1); mR2 += a * (mR1 - mR2);
                outL[j] = c.cabL[j] + c.openL[j] + mL2 + 0.14f * (wl - mL2);
                outR[j] = c.cabR[j] + c.openR[j] + mR2 + 0.14f * (wr - mR2);
            }
            if ((mask & VEar) != 0) ear.Process(c, outL, outR);
            storyNow = next;
        }

        void Dispatch(double next)
        {
            for (int i = pendCount - 1; i >= 0; i--)
            {
                int ep = pend[i].epoch;
                if (ep != epoch)
                {
                    if (ep < epoch) { pend[i] = pend[pendCount - 1]; pendCount--; }
                    continue;
                }
                int kind = pend[i].kind;
                double lead = kind == Ev.Thunder ? ThunderVoice.Lead : (kind == Ev.EarThump ? Ear.Lead : 0.0);
                if (pend[i].time - lead > next) continue;
                SoundEvent e = pend[i];
                pend[i] = pend[pendCount - 1]; pendCount--;
                float late = (float)(next - e.time);
                switch (kind)
                {
                    case Ev.Thunder: thunder.Start(e, late); break;
                    case Ev.Sferic: radio.Sferic(e); tornado.Flash(e); break;
                    case Ev.Pop: pops.Start(e, late); break;
                    case Ev.HailHit: hail.BigHit(e, c, panels); break;
                    case Ev.Crack: hail.Crack(e); break;
                    case Ev.EarThump: ear.Thump(late); break;
                }
            }
        }
    }
}
