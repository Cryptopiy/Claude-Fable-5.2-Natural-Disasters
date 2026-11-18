using System;

namespace MESOCYCLONE.Audio
{
    /// <summary>Renders a stretch of the game's sound without the audio device, with exactly the code the game
    /// uses: a simulated main thread steps at 60 Hz (Director.Seek, then the same Conductor), and the same Engine
    /// renders the blocks. Deterministic. Main thread only; it leaves the Director at the end of the stretch.</summary>
    public static class OfflineAudio
    {
        const float PreRoll = 2f;

        /// <summary>Optional mask of Engine.V* bits to render only some voices (balance work); -1 = all.</summary>
        public static int VoiceMask = -1;

        /// <summary>Deepest limiter gain during the last Render (1 = the limiter never worked), for logs.</summary>
        public static float MinLimiterGain = 1f;

        /// <summary>Story time [t0, t1) on the cinematic schedule as interleaved stereo.</summary>
        public static float[] Render(float t0, float t1, int sampleRate)
        {
            if (sampleRate < 8000) sampleRate = 48000;
            if (t0 < 0f) t0 = 0f;
            if (t1 < t0) t1 = t0;
            float start = Math.Max(0f, t0 - PreRoll);
            long total = (long)Math.Round((double)(t1 - t0) * sampleRate);
            long skip = (long)Math.Round((double)(t0 - start) * sampleRate);
            float[] result = new float[total * 2];
            if (total <= 0) return result;

            Engine engine = new Engine(sampleRate);
            engine.VoiceMask = VoiceMask;
            Conductor conductor = new Conductor(engine);
            int frame = sampleRate / 60;
            float[] block = new float[frame * 2];
            bool wasRunning = Director.Running, wasPaused = Director.Paused, wasWarm = Director.Warmup;
            Director.Running = true; Director.Paused = false; Director.Warmup = false;
            MinLimiterGain = 1f;
            try
            {
                long pos = -skip;                       // frames relative to t0
                long step = 0;
                while (pos < total)
                {
                    float T = (float)(start + (double)step * frame / sampleRate);
                    Director.Seek(T);
                    float v = CinematicSpeed(T);        // the last braking, which Seek does not model
                    Director.Speed = v; Director.S.truckSpeed = v;
                    conductor.Tick(1f);
                    Array.Clear(block, 0, block.Length);
                    engine.Render(block, frame, 2);
                    if (pos + frame > 0 && engine.LastMinGain < MinLimiterGain) MinLimiterGain = engine.LastMinGain;
                    long from = pos < 0 ? -pos : 0;
                    for (long i = from; i < frame && pos + i < total; i++)
                    {
                        long o = (pos + i) * 2;
                        result[o] = block[i * 2]; result[o + 1] = block[i * 2 + 1];
                    }
                    pos += frame; step++;
                }
            }
            finally
            {
                Director.Running = wasRunning; Director.Paused = wasPaused; Director.Warmup = wasWarm;
            }
            return result;
        }

        /// <summary>Truck speed on the cinematic schedule: 24 m/s, then 4 m/s2 of braking into the stop.</summary>
        static float CinematicSpeed(float T)
        {
            const float stopAt = 662.6f;
            float v = (stopAt - T) * 4f;
            if (v < 0f) v = 0f;
            return v > Config.SpeedBrisk ? Config.SpeedBrisk : v;
        }
    }
}
