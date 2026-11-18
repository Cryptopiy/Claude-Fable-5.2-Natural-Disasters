using System;
using System.Threading;
using UnityEngine;

namespace CASCADE
{
    /// <summary>
    /// Plays the SoundScore in the game. Lives on the AudioListener's GameObject.
    /// Main thread (Update): copies the Story state into a snapshot (seqlock, never blocks the audio thread).
    /// Audio thread: keeps its own story clock, follows the snapshot (proportional correction, hard resync above 0.25 s)
    /// and fills the output buffer from the score.
    /// Two hooks, one renderer: a clip-less looping AudioSource on a child object ("AudioPump") is the generator; the
    /// filter on the listener object then only guards the final mix. If the source is not being pulled, the listener
    /// filter generates instead. Either way each DSP block is rendered exactly once.
    /// </summary>
    public sealed class AudioDirector : MonoBehaviour
    {
        public static AudioDirector I;

        /// <summary>Extra pause switch (AudioListener.pause is honoured too). Fades out in 50 ms, holds the clock.</summary>
        public bool Paused;
        /// <summary>Master volume 0..1 (applied after the limiter).</summary>
        public float Volume = 1f;

        const double ResyncSeconds = 0.25;      // larger story-time error: jump instead of chasing
        const double Follow = 2.0;              // proportional gain of the clock follower, 1/s
        const float StartFade = 1.5f, PauseDown = 0.05f, PauseUp = 0.25f;

        struct Snapshot { public ScoreClock clock; public float rate; public float volume; public int running; }

        SoundScore score;                       // swapped as a whole when the sample rate changes
        Snapshot pub; int seq;                  // seqlock: main thread writes, audio thread reads
        volatile bool ready;
        volatile int fault;
        AudioSource pumpSource;
        int sampleRate;

        // main-thread state
        float lastStory, rateEst = 1f, healTimer;
        int still = 1000;
        long lastBlocksSeen;
        bool announced, faultLogged;

        // audio-thread state
        Snapshot cur; ScoreClock clk;
        double audioTime; bool haveTime;
        float startGain, pauseGain;
        int pumpCalls, pumpSeen;
        long blocks; int viaSource;

        /// <summary>DSP blocks pulled by Unity so far (0 = no audio callback is running).</summary>
        public long BlocksRendered { get { return Interlocked.Read(ref blocks); } }
        public bool RenderedBySource { get { return viaSource != 0; } }
        public SoundScore Score { get { return score; } }

        public static AudioDirector Create(GameObject listenerObject)
        {
            var d = listenerObject.GetComponent<AudioDirector>();
            return d != null ? d : listenerObject.AddComponent<AudioDirector>();
        }

        void Awake()
        {
            I = this;
            sampleRate = AudioSettings.outputSampleRate;
            if (sampleRate < 8000) sampleRate = 48000;
            score = new SoundScore(sampleRate);
            pub.clock.ClearEvents();

            var go = new GameObject("AudioPump");
            go.transform.SetParent(transform, false);
            pumpSource = go.AddComponent<AudioSource>();
            pumpSource.clip = null; pumpSource.playOnAwake = false; pumpSource.loop = true;
            pumpSource.spatialBlend = 0f; pumpSource.volume = 1f; pumpSource.panStereo = 0f; pumpSource.priority = 0;
            pumpSource.dopplerLevel = 0f; pumpSource.reverbZoneMix = 0f; pumpSource.bypassReverbZones = true;
            pumpSource.ignoreListenerPause = true;          // the pause fade is ours
            go.AddComponent<AudioPump>().owner = this;
            pumpSource.Play();

            AudioSettings.OnAudioConfigurationChanged += OnAudioConfigurationChanged;
            ready = true;
        }

        void OnDestroy()
        {
            ready = false;
            AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigurationChanged;
            if (I == this) I = null;
        }

        void OnAudioConfigurationChanged(bool deviceWasChanged)
        {
            int sr = AudioSettings.outputSampleRate;
            if (sr >= 8000 && sr != sampleRate)
            {
                sampleRate = sr;
                Volatile.Write(ref score, new SoundScore(sr));      // main thread allocates; the audio thread picks it up next block
            }
            if (pumpSource != null) pumpSource.Play();              // sources stop on a device change
        }

        void Update()
        {
            // ---- snapshot of the story for the audio thread ----
            float st = Story.Time;
            float dS = st - lastStory;
            lastStory = st;
            float dR = Time.unscaledDeltaTime;
            if (dS > 1e-6f) still = 0; else if (still < 1000) still++;
            if (dS > 0f && dS < 0.5f && dR > 1e-4f) rateEst = Mathf.Lerp(rateEst, Mathf.Clamp(dS / dR, 0f, 30f), 0.08f);
            bool running = still < 3 && !Paused && !AudioListener.pause;

            Snapshot s;
            s.clock = default(ScoreClock);
            s.clock.storyTime = st;
            s.clock.speed = Story.Speed;
            s.clock.footstepCount = Story.FootstepCount;
            s.clock.footSurface = Story.FootSurface;
            s.clock.tGulls = Story.StartTime(Ev.Gulls); s.clock.tHum = Story.StartTime(Ev.Hum);
            s.clock.tTremor1 = Story.StartTime(Ev.Tremor1); s.clock.tTremor2 = Story.StartTime(Ev.Tremor2);
            s.clock.tVista = Story.StartTime(Ev.Vista); s.clock.tEruption = Story.StartTime(Ev.Eruption);
            s.clock.tTremor3 = Story.StartTime(Ev.Tremor3); s.clock.tCollapse = Story.StartTime(Ev.Collapse);
            s.clock.tDrawdown = Story.StartTime(Ev.Drawdown); s.clock.tWave = Story.StartTime(Ev.Wave);
            s.clock.tAshStorm = Story.StartTime(Ev.AshStorm); s.clock.tGlimpse1 = Story.StartTime(Ev.Glimpse1);
            s.clock.tGlimpse2 = Story.StartTime(Ev.Glimpse2); s.clock.tSilence = Story.StartTime(Ev.Silence);
            s.clock.tArrival = Story.StartTime(Ev.Arrival);
            s.rate = rateEst;
            s.volume = Mathf.Clamp01(Volume);
            s.running = running ? 1 : 0;
            Interlocked.Increment(ref seq);         // odd: write in progress
            pub = s;
            Interlocked.Increment(ref seq);         // even: consistent

            // ---- health (main thread only; nothing here is called from the audio thread) ----
            if (fault != 0 && !faultLogged) { faultLogged = true; Debug.LogError("CASCADE audio: exception on the audio thread, output muted"); }
            long b = BlocksRendered;
            if (!announced && b > 20)
            {
                announced = true;
                Debug.Log("CASCADE_INFO audio running via " + (RenderedBySource ? "source" : "listener") + " filter, " + sampleRate + " Hz");
            }
            healTimer += dR;
            if (healTimer > 2f)
            {
                healTimer = 0f;
                if (b == lastBlocksSeen && pumpSource != null) pumpSource.Play();    // nothing pulled audio for 2 s: restart the pump
                lastBlocksSeen = b;
            }
        }

        // ---------------- audio thread ----------------

        /// <summary>Filter on the listener object: guards the final mix, or generates when no source feeds it.</summary>
        void OnAudioFilterRead(float[] data, int channels)
        {
            if (!ready || channels <= 0) return;
            if (pumpCalls != pumpSeen) { pumpSeen = pumpCalls; Guard(data); return; }      // the pump rendered this block
            viaSource = 0;
            Fill(data, channels);
        }

        /// <summary>Called by AudioPump (the clip-less source's filter).</summary>
        internal void PumpRead(float[] data, int channels)
        {
            if (!ready || channels <= 0) return;
            pumpCalls++;
            viaSource = 1;
            Fill(data, channels);
        }

        static void Guard(float[] data)
        {
            const float c = Dsp.Ceiling;
            for (int i = 0; i < data.Length; i++)
            {
                float x = data[i];
                if (x > c) data[i] = c; else if (x < -c) data[i] = -c;
            }
        }

        bool ReadSnapshot()
        {
            for (int i = 0; i < 4; i++)
            {
                int s0 = Volatile.Read(ref seq);
                if ((s0 & 1) != 0) continue;
                Snapshot tmp = pub;
                if (Volatile.Read(ref seq) == s0) { cur = tmp; return true; }
            }
            return false;                           // keep the last good snapshot
        }

        void Fill(float[] data, int channels)
        {
            try
            {
                Interlocked.Increment(ref blocks);
                SoundScore s = Volatile.Read(ref score);
                int frames = data.Length / channels;
                ReadSnapshot();
                bool run = cur.running != 0 && fault == 0;
                if (!run && pauseGain <= 0f) { Array.Clear(data, 0, data.Length); return; }

                double target = cur.clock.storyTime;
                if (!haveTime) { audioTime = target; haveTime = true; }
                double err = target - audioTime;
                if (err > ResyncSeconds || err < -ResyncSeconds) { audioTime = target; err = 0.0; }   // seek or hitch: the score fades across the jump
                double rate = run ? cur.rate + Follow * err : 0.0;
                if (rate < 0.0) rate = 0.0; else if (rate > 40.0) rate = 40.0;

                int fs = s.SampleRate;
                clk = cur.clock;
                clk.storyTime = audioTime;
                clk.dtPerSample = rate / fs;
                s.Render(data, frames, channels, ref clk);
                audioTime = clk.storyTime;

                // master fade: 1.5 s in at the very start, 50 ms out / 250 ms in around a pause
                float upStart = 1f / (StartFade * fs), down = 1f / (PauseDown * fs), up = 1f / (PauseUp * fs), vol = cur.volume;
                int o = 0;
                for (int f = 0; f < frames; f++)
                {
                    if (run)
                    {
                        if (startGain < 1f) { startGain += upStart; if (startGain > 1f) startGain = 1f; }
                        if (pauseGain < 1f) { pauseGain += up; if (pauseGain > 1f) pauseGain = 1f; }
                    }
                    else if (pauseGain > 0f) { pauseGain -= down; if (pauseGain < 0f) pauseGain = 0f; }
                    float g = startGain * startGain * pauseGain * vol;
                    for (int ch = 0; ch < channels; ch++) data[o++] *= g;
                }
            }
            catch
            {
                fault = 1;
                Array.Clear(data, 0, data.Length);
            }
        }
    }
}
