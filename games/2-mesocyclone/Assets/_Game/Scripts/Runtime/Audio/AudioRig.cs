using System;
using UnityEngine;

namespace MESOCYCLONE.Audio
{
    /// <summary>The one audio component of the game. It sits on the AudioListener's object (added after the
    /// listener, so its filter is the last thing before the device), adds the synthesized mix to whatever arrives
    /// and runs the master limiter. Update() feeds the engine from the main thread; OnAudioFilterRead() renders
    /// on the audio thread without allocating. Silent when Cli.Mute is set or the harness runs with --step.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(200)]
    public sealed class AudioRig : MonoBehaviour
    {
        /// <summary>Peak of the last output block (linear), for logs.</summary>
        public static float LastPeak;
        /// <summary>Deepest limiter gain of the last block (1 = the limiter is idle), for logs.</summary>
        public static float LastLimiterGain = 1f;
        /// <summary>Audio callbacks so far. Still 0 after a few seconds of play = the filter is not being pumped.</summary>
        public static int Callbacks;

        static AudioRig instance;
        Engine engine;
        Conductor conductor;
        volatile bool ready, faulted, silent;
        int rebuilt;
        float liveSince = -1f;
        bool pumpTried;

        /// <summary>Adds the rig to the object that carries the AudioListener (adds a listener if there is none).
        /// The output starts silent and fades in over one second once the Director runs.</summary>
        public static AudioRig Create(GameObject listenerObject)
        {
            if (listenerObject == null) return null;
            if (listenerObject.GetComponent<AudioListener>() == null) listenerObject.AddComponent<AudioListener>();
            AudioRig rig = listenerObject.GetComponent<AudioRig>();
            if (rig == null) rig = listenerObject.AddComponent<AudioRig>();
            return rig;
        }

        /// <summary>After a seek (harness --start-at): tails and voices are killed behind a short fade, the clocks
        /// are resynced, thunder still in flight is restored. No click.</summary>
        public static void Jump()
        {
            AudioRig rig = instance;
            if (rig != null && rig.ready && rig.conductor != null && Director.Running) rig.conductor.Resync();
        }

        void Awake()
        {
            instance = this;
            Build();
        }

        void Build()
        {
            ready = false;
            int sampleRate = AudioSettings.outputSampleRate;
            if (sampleRate < 8000) sampleRate = 48000;
            engine = new Engine(sampleRate);
            conductor = new Conductor(engine);
            faulted = false;
            ready = true;
        }

        void OnEnable() { AudioSettings.OnAudioConfigurationChanged += OnAudioConfigurationChanged; }

        void OnDisable() { AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigurationChanged; }

        void OnDestroy()
        {
            ready = false;
            if (instance == this) instance = null;
        }

        void OnAudioConfigurationChanged(bool deviceWasChanged)
        {
            // a new device or sample rate: rebuild everything for the new rate (main thread)
            Build();
        }

        void Update()
        {
            if (!ready) return;
            silent = Cli.Mute || Cli.Step > 0f;
            if (silent) return;
            if (faulted && rebuilt < 3)
            {
                rebuilt++;
                Debug.LogError("[AudioRig] the audio engine threw on the audio thread; rebuilding it (" + rebuilt + "/3)");
                Build();
            }
            float rate = 0f;
            if (!Director.Paused && Director.FrameDt > 1e-5f) rate = Mathf.Clamp01(Director.Dt / Director.FrameDt);
            conductor.Tick(rate);

            // A filter on the listener's object is pumped by the audio system itself. Should it not be (no callback
            // within three seconds of play), a clip-less looping AudioSource on the same object pumps it instead.
            if (!pumpTried && Director.Running && !Director.Warmup)
            {
                if (liveSince < 0f) liveSince = Time.unscaledTime;
                else if (Callbacks > 0) pumpTried = true;
                else if (Time.unscaledTime - liveSince > 3f)
                {
                    pumpTried = true;
                    Debug.LogWarning("[AudioRig] no audio callback after 3 s: adding a clip-less AudioSource to pump the filter");
                    AudioSource pump = gameObject.AddComponent<AudioSource>();
                    pump.playOnAwake = false; pump.loop = true; pump.spatialBlend = 0f;
                    pump.Play();
                }
            }
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            Callbacks++;
            if (!ready || faulted || silent || channels < 1) return;
            Engine e = engine;
            if (e == null) return;
            try
            {
                e.Render(data, data.Length / channels, channels);
                LastPeak = e.LastPeak; LastLimiterGain = e.LastMinGain;
            }
            catch (Exception)
            {
                faulted = true;
                Array.Clear(data, 0, data.Length);
            }
        }
    }
}
