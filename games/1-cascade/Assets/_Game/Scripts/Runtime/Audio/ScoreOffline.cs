using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace CASCADE
{
    /// <summary>
    /// Offline render of the nominal cinematic run to WAV (48 kHz, 16-bit, stereo): QA/audio/full.wav and b1..b7.wav.
    /// Plain C# and System.IO only, so the editor batch tool and any test harness share this exact code path.
    /// Not used during the walk.
    /// </summary>
    public static class ScoreOffline
    {
        public static readonly float[] BeatEdges = { 0f, 90f, 180f, 300f, 390f, 510f, 600f };   // b1..b7 start times; b7 runs to the end
        public const float StrideFlat = 0.78f, StrideStairs = 0.62f;                            // same strides as PlayerRig

        sealed class WavOut : IDisposable
        {
            readonly FileStream f; long bytes; readonly int rate;
            public WavOut(string path, int sampleRate)
            {
                rate = sampleRate;
                f = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 16);
                f.Write(new byte[44], 0, 44);
            }
            public void Write(byte[] pcm, int offset, int count) { f.Write(pcm, offset, count); bytes += count; }
            public void Dispose()
            {
                var h = new MemoryStream(44);
                var w = new BinaryWriter(h, Encoding.ASCII);
                w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write((uint)(36 + bytes)); w.Write(Encoding.ASCII.GetBytes("WAVE"));
                w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write((uint)16); w.Write((ushort)1); w.Write((ushort)2);
                w.Write((uint)rate); w.Write((uint)(rate * 4)); w.Write((ushort)4); w.Write((ushort)16);
                w.Write(Encoding.ASCII.GetBytes("data")); w.Write((uint)bytes);
                w.Flush();
                f.Seek(0, SeekOrigin.Begin);
                f.Write(h.ToArray(), 0, 44);
                f.Dispose();
            }
        }

        /// <summary>
        /// The nominal cinematic run as a ScoreClock: events at Story.Nominal, arrival at pathLength / BriskSpeed,
        /// footsteps from the chainage with the strides of PlayerRig. Call Advance for consecutive blocks, then Render.
        /// </summary>
        public sealed class NominalRun
        {
            public readonly float Arrival;
            public readonly double Duration;
            public ScoreClock Clock;
            readonly float pathLength; readonly Func<float, int> surfaceAt; readonly Func<float, bool> isStairs;
            double stepPhase;

            public NominalRun(int sampleRate, float pathLength, Func<float, int> surfaceAt, Func<float, bool> isStairs)
            {
                this.pathLength = pathLength; this.surfaceAt = surfaceAt; this.isStairs = isStairs;
                Arrival = pathLength / Config.BriskSpeed;
                Duration = Arrival + Config.EndQuitAt;
                Clock = default(ScoreClock);
                Clock.ClearEvents();
                Clock.dtPerSample = 1.0 / sampleRate;
            }

            /// <summary>Chainage of the cinematic camera (same law as Story.CinematicChainage), in double precision.</summary>
            static double Chainage(double t)
            {
                const double ease = 0.5;
                if (t <= 0.0) return 0.0;
                if (t < ease) return 0.5 * Config.BriskSpeed * t * t / ease;
                return Config.BriskSpeed * (t - 0.5 * ease);
            }

            static float At(float nominal, double t) { return t >= nominal ? nominal : Story.Never; }

            /// <summary>Sets Clock for the block [t0, t1): the events known at t0, the speed, the footsteps taken.</summary>
            public void Advance(double t0, double t1)
            {
                float[] nom = Story.Nominal;
                Clock.tGulls = At(nom[(int)Ev.Gulls], t0); Clock.tHum = At(nom[(int)Ev.Hum], t0);
                Clock.tTremor1 = At(nom[(int)Ev.Tremor1], t0); Clock.tTremor2 = At(nom[(int)Ev.Tremor2], t0);
                Clock.tVista = At(nom[(int)Ev.Vista], t0); Clock.tEruption = At(nom[(int)Ev.Eruption], t0);
                Clock.tTremor3 = At(nom[(int)Ev.Tremor3], t0); Clock.tCollapse = At(nom[(int)Ev.Collapse], t0);
                Clock.tDrawdown = At(nom[(int)Ev.Drawdown], t0); Clock.tWave = At(nom[(int)Ev.Wave], t0);
                Clock.tAshStorm = At(nom[(int)Ev.AshStorm], t0); Clock.tGlimpse1 = At(nom[(int)Ev.Glimpse1], t0);
                Clock.tGlimpse2 = At(nom[(int)Ev.Glimpse2], t0); Clock.tSilence = At(nom[(int)Ev.Silence], t0);
                Clock.tArrival = At(Arrival, t0);

                double ch0 = Math.Min(pathLength, Chainage(t0)), ch1 = Math.Min(pathLength, Chainage(t1));
                Clock.speed = t0 < Arrival ? Config.BriskSpeed : 0f;
                double before = stepPhase;
                stepPhase += (ch1 - ch0) / (isStairs((float)ch1) ? StrideStairs : StrideFlat);
                if (Math.Floor(stepPhase) != Math.Floor(before)) { Clock.footSurface = surfaceAt((float)ch1); Clock.footstepCount++; }
                Clock.storyTime = t0;
            }
        }

        /// <summary>
        /// Renders the nominal run (events at Story.Nominal, arrival at pathLength / BriskSpeed, 16 s of ending) into outDir.
        /// surfaceAt(chainage) returns the Surface id, isStairs(chainage) selects the shorter stride. Returns a one-line summary.
        /// </summary>
        public static string RenderAll(string outDir, int sampleRate, float pathLength, Func<float, int> surfaceAt, Func<float, bool> isStairs,
                                       Action<SoundScore> setup = null)
        {
            Directory.CreateDirectory(outDir);
            var score = new SoundScore(sampleRate);
            if (setup != null) setup(score);
            var run = new NominalRun(sampleRate, pathLength, surfaceAt, isStairs);
            float arrival = run.Arrival;
            double duration = run.Duration;
            long total = (long)(duration * sampleRate);
            const int B = 256;
            var buf = new float[B * 2];
            var pcm = new byte[B * 4];
            int beats = BeatEdges.Length;
            var edge = new long[beats + 1];
            for (int i = 0; i < beats; i++) edge[i] = (long)Math.Round((double)BeatEdges[i] * sampleRate);
            edge[beats] = total;
            var peak = new float[beats];
            var sumSq = new double[beats];
            var sw = Stopwatch.StartNew();
            int beat = 0;
            WavOut cur = new WavOut(Path.Combine(outDir, "b1.wav"), sampleRate);
            using (var full = new WavOut(Path.Combine(outDir, "full.wav"), sampleRate))
            {
                for (long pos = 0; pos < total; pos += B)
                {
                    int n = (int)Math.Min(B, total - pos);
                    run.Advance(pos / (double)sampleRate, (pos + n) / (double)sampleRate);
                    score.Render(buf, n, 2, ref run.Clock);

                    for (int i = 0; i < n * 2; i++)
                    {
                        float x = buf[i];
                        long smp = pos + (i >> 1);
                        int bi = beat;
                        if (smp >= edge[beat + 1] && beat < beats - 1) bi = beat + 1;
                        float a = x < 0f ? -x : x;
                        if (a > peak[bi]) peak[bi] = a;
                        sumSq[bi] += (double)x * x;
                        int v = (int)Math.Round(x * 32767.0);
                        if (v > 32767) v = 32767; else if (v < -32768) v = -32768;
                        pcm[i * 2] = (byte)(v & 0xFF); pcm[i * 2 + 1] = (byte)((v >> 8) & 0xFF);
                    }
                    full.Write(pcm, 0, n * 4);
                    // split across a beat boundary if this block straddles one
                    long cut = edge[beat + 1];
                    if (beat < beats - 1 && pos + n > cut)
                    {
                        int head = (int)Math.Max(0, cut - pos);
                        cur.Write(pcm, 0, head * 4);
                        cur.Dispose();
                        beat++;
                        cur = new WavOut(Path.Combine(outDir, "b" + (beat + 1) + ".wav"), sampleRate);
                        cur.Write(pcm, head * 4, (n - head) * 4);
                    }
                    else cur.Write(pcm, 0, n * 4);
                }
            }
            cur.Dispose();
            sw.Stop();

            double speed = duration / Math.Max(1e-3, sw.Elapsed.TotalSeconds);
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine("CASCADE audio render: " + duration.ToString("F1", inv) + " s at " + sampleRate + " Hz, arrival " + arrival.ToString("F1", inv)
                          + " s, rendered in " + sw.Elapsed.TotalSeconds.ToString("F1", inv) + " s (" + speed.ToString("F1", inv) + " x real time)");
            sb.AppendLine("limiter: deepest gain reduction " + (20.0 * Math.Log10(Math.Max(1e-6, score.LimiterMinGain))).ToString("F1", inv)
                          + " dB, largest input sample " + (20.0 * Math.Log10(Math.Max(1e-6, score.PeakIn))).ToString("F1", inv) + " dBFS");
            for (int i = 0; i < beats; i++)
            {
                double len = Math.Max(1, (edge[i + 1] - edge[i]) * 2);
                sb.AppendLine("b" + (i + 1) + " float peak " + (20.0 * Math.Log10(Math.Max(1e-6, peak[i]))).ToString("F2", inv) + " dBFS, rms "
                              + (10.0 * Math.Log10(Math.Max(1e-12, sumSq[i] / len))).ToString("F1", inv) + " dBFS");
            }
            File.WriteAllText(Path.Combine(outDir, "render_info.txt"), sb.ToString());
            return "audio render " + duration.ToString("F1", inv) + " s in " + sw.Elapsed.TotalSeconds.ToString("F1", inv) + " s = "
                   + speed.ToString("F1", inv) + " x real time, files in " + outDir;
        }
    }
}
