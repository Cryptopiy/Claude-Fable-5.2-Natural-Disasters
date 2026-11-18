using UnityEngine;

namespace ZERO
{
    /// <summary>Hosts the procedural synth on an AudioSource without a clip: OnAudioFilterRead is the source.
    /// Put it on its own GameObject (not on the AudioListener's object) or call <see cref="Create"/>.
    /// The game pushes one <see cref="AudioState"/> per frame with <see cref="Push"/> and hands over the discrete
    /// sounds of the catastrophe with <see cref="Event"/> (see <see cref="AudioDirector"/>).
    /// The master limiter is the last stage inside <see cref="ZeroSynth"/>, so no other component is needed.</summary>
    [DisallowMultipleComponent]
    public sealed class AudioEngine : MonoBehaviour
    {
        public static AudioEngine Instance { get; private set; }

        /// <summary>Seed of all audio randomness. The same seed and the same pushed states give the same sound.</summary>
        public uint seed = 20261112u;

        volatile ZeroSynth live;            // swapped whole on a sample-rate change; read by the audio thread
        AudioSource src;
        int sampleRate;
        bool subscribed;

        static AudioState last;
        static bool hasLast;

        // No stale state from an earlier play session (the editor may keep statics when domain reload is off).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Instance = null; last = default(AudioState); hasLast = false; }

        /// <summary>The running synth (meters and trims), or null before Awake.</summary>
        public ZeroSynth Synth { get { return live; } }

        /// <summary>Creates a GameObject "ZERO Audio" with the engine on it (or returns the existing one).</summary>
        public static AudioEngine Create()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("ZERO Audio");
            return go.AddComponent<AudioEngine>();
        }

        /// <summary>Main thread, once per frame: hands the game state to the synth. Lock-free, no allocation.
        /// Safe to call before the engine exists (the last state is kept and applied on Awake).</summary>
        public static void Push(in AudioState s)
        {
            last = s; hasLast = true;
            AudioEngine e = Instance;
            if (e == null) return;
            ZeroSynth z = e.live;
            if (z != null) z.SetState(s);
        }

        /// <summary>Main thread: a discrete sound is heard now (see <see cref="AudioEventKind"/>). 'distance' in metres
        /// only tells how far and dull it is, size 0..1, pan -1 (left) .. +1 (right). Lock-free, no allocation
        /// (a single-producer ring inside the synth). Before the engine exists the event is dropped: a sound that
        /// nobody could hear.</summary>
        public static void Event(AudioEventKind kind, float distance, float size, float pan)
        {
            AudioEngine e = Instance;
            if (e == null) return;
            ZeroSynth z = e.live;
            if (z != null) z.AddEvent(kind, distance, size, pan);
        }

        /// <summary>Optional, main thread: thins the cabin bed by 7 dB for "seconds" (use 2-3 s before a money shot).</summary>
        public static void ThinWorld(float seconds)
        {
            AudioEngine e = Instance;
            if (e == null) return;
            ZeroSynth z = e.live;
            if (z != null) z.ThinWorld(seconds);
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            sampleRate = AudioSettings.outputSampleRate;
            if (sampleRate <= 0) sampleRate = 48000;
            ZeroSynth.Warm(sampleRate);          // compile every path now, not on the audio thread during the walk
            CreateSynth();

            src = GetComponent<AudioSource>();
            if (src == null) src = gameObject.AddComponent<AudioSource>();
            src.clip = null;
            src.playOnAwake = true;
            src.loop = true;
            src.spatialBlend = 0f;
            src.volume = 1f;
            src.pitch = 1f;
            src.priority = 0;
            src.dopplerLevel = 0f;
            src.reverbZoneMix = 0f;
            src.bypassEffects = true;
            src.bypassListenerEffects = true;
            src.bypassReverbZones = true;
            src.ignoreListenerPause = true;      // a pause is a masterGain fade, not a hard stop
            src.Play();

            AudioSettings.OnAudioConfigurationChanged += OnConfigChanged;
            subscribed = true;
        }

        void OnEnable()
        {
            if (src != null && !src.isPlaying) src.Play();
        }

        void OnDestroy()
        {
            if (subscribed) AudioSettings.OnAudioConfigurationChanged -= OnConfigChanged;
            subscribed = false;
            if (Instance == this) Instance = null;
            live = null;
        }

        void CreateSynth()
        {
            var z = new ZeroSynth(sampleRate, seed);
            ZeroSynth old = live;
            if (old != null) { z.WorldTrim = old.WorldTrim; z.RingTrim = old.RingTrim; z.EventTrim = old.EventTrim; }
            if (hasLast) z.SetState(last);
            live = z;
        }

        // The device or its format changed: Unity restarts the audio system, so rebuild at the new rate and play again.
        void OnConfigChanged(bool deviceWasChanged)
        {
            int sr = AudioSettings.outputSampleRate;
            if (sr > 0 && sr != sampleRate)
            {
                sampleRate = sr;
                CreateSynth();
            }
            if (src != null && isActiveAndEnabled && !src.isPlaying) src.Play();
        }

        // Audio thread. With no clip on the AudioSource this filter is the source: overwrite the buffer.
        void OnAudioFilterRead(float[] data, int channels)
        {
            ZeroSynth z = live;
            if (z == null || channels < 1)
            {
                for (int i = 0; i < data.Length; i++) data[i] = 0f;
                return;
            }
            z.Render(data, data.Length / channels, channels);
        }
    }
}
