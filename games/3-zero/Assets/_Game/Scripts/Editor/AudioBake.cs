using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace ZERO.EditorTools
{
    /// <summary>Offline check of the procedural audio: renders the whole piece through <see cref="ZeroSynth"/> with the
    /// real <see cref="AudioDirector"/> on a scripted clock (no audio device, no GPU) and writes 16-bit WAV files for
    /// Tools/audio_check.py.
    /// Batch: Unity -batchmode -nographics -quit -executeMethod ZERO.EditorTools.AudioBake.Run
    /// Output (under the project root): QA/audio_full.wav, QA/audio_nosteps.wav (the same flight with EventTrim 0:
    /// no discrete events, no rattling, no ticks of the burning sky, no hit; only the smooth continuous cabin, for
    /// the strict click test; the name is historical) and QA/audio_bake_info.txt (times for the checker).
    /// The clock: T = running time; phase 0 until the breath begins 1.2 s before the wave arrives, then the breath,
    /// the hit, the white (hold and fade to black), two seconds of black.</summary>
    public static class AudioBake
    {
        const int Rate = 48000, Block = 1024;
        const uint Seed = 20261112u;
        // the ending as the game plays it
        const float Breath = 1.2f, Hit = 1.2f, WhiteHold = 1.5f, FadeToBlack = 4f, Black = 2f, FadeIn = 2.5f;

        struct Result
        {
            public long frames, nonFinite;
            public float peak;
            public int nanResets, hits, emitted, dropped;
            public double breathStart, hitStart, whiteStart, blackStart;
        }

        [MenuItem("Tools/ZERO/Bake Audio (QA)")]
        public static void Run()
        {
            RunTo(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "QA")));
        }

        /// <summary>Renders both files and the info file into the folder qa.</summary>
        public static void RunTo(string qa)
        {
            Directory.CreateDirectory(qa);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            Result full = Render(Path.Combine(qa, "audio_full.wav"), 1f);
            Result stem = Render(Path.Combine(qa, "audio_nosteps.wav"), 0f);
            AudioDirector.Reset(0f);                         // leave the director as a fresh game expects it
            watch.Stop();

            CultureInfo inv = CultureInfo.InvariantCulture;
            double dur = full.frames / (double)Rate;
            var sb = new StringBuilder();
            sb.Append("sample_rate=").Append(Rate).Append('\n');
            sb.Append("frames=").Append(full.frames).Append('\n');
            sb.Append("duration_s=").Append(dur.ToString("F4", inv)).Append('\n');
            sb.Append("contact_time_s=").Append(Schedule.ContactTime.ToString("F4", inv)).Append('\n');
            sb.Append("arrival_time_s=").Append(Schedule.ArrivalTime.ToString("F4", inv)).Append('\n');
            sb.Append("breath_start_s=").Append(full.breathStart.ToString("F4", inv)).Append('\n');
            sb.Append("hit_start_s=").Append(full.hitStart.ToString("F4", inv)).Append('\n');
            sb.Append("white_start_s=").Append(full.whiteStart.ToString("F4", inv)).Append('\n');
            sb.Append("black_start_s=").Append(full.blackStart.ToString("F4", inv)).Append('\n');
            sb.Append("peak_full=").Append(full.peak.ToString("F6", inv)).Append('\n');
            sb.Append("peak_stem=").Append(stem.peak.ToString("F6", inv)).Append('\n');
            sb.Append("non_finite_samples=").Append(full.nonFinite + stem.nonFinite).Append('\n');
            sb.Append("nan_resets=").Append(full.nanResets + stem.nanResets).Append('\n');
            sb.Append("hits=").Append(full.hits).Append('\n');
            sb.Append("events_emitted=").Append(full.emitted).Append('\n');
            sb.Append("events_dropped=").Append(full.dropped + stem.dropped).Append('\n');
            sb.Append("stem=audio_nosteps.wav is the smooth continuous cabin only (EventTrim 0: no events, no rattle, no ticks, no hit)\n");
            File.WriteAllText(Path.Combine(qa, "audio_bake_info.txt"), sb.ToString());

            Debug.Log("[AudioBake] duration " + dur.ToString("F2", inv) + " s, peak " + full.peak.ToString("F4", inv) +
                      " (" + Db(full.peak).ToString("F2", inv) + " dBFS), breath at " + full.breathStart.ToString("F2", inv) +
                      " s, hit at " + full.hitStart.ToString("F2", inv) + " s, events " + full.emitted + " (dropped " +
                      (full.dropped + stem.dropped) + "), non-finite samples " + (full.nonFinite + stem.nonFinite) +
                      ", rendered twice in " + watch.Elapsed.TotalSeconds.ToString("F1", inv) + " s -> " + qa);
        }

        static float Db(float lin) { return lin > 1e-9f ? 20f * (float)Math.Log10(lin) : -180f; }

        static Result Render(string path, float eventTrim)
        {
            var synth = new ZeroSynth(Rate, Seed);
            synth.EventTrim = eventTrim;
            var res = new Result { breathStart = -1.0, hitStart = -1.0, whiteStart = -1.0, blackStart = -1.0 };
            int emitted = 0;
            Action<AudioEventKind, float, float, float> emit = (kind, dist, size, pan) => { emitted++; synth.AddEvent(kind, dist, size, pan); };
            AudioDirector.Init();
            var buf = new float[Block * 2];
            var bytes = new byte[Block * 4];
            double dt = Block / (double)Rate;
            double t = 0.0, phaseStart = 0.0;
            double breathAt = Schedule.ArrivalTime - Breath;
            int phase = 0;

            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                fs.Write(new byte[44], 0, 44);           // header is written at the end
                while (true)
                {
                    // the scripted clock: the flight, the breath, the hit, white, black
                    double tp = t - phaseStart;
                    if (phase == 0 && t >= breathAt) { phase = 1; phaseStart = t; res.breathStart = t; }
                    else if (phase == 1 && tp >= Breath) { phase = 2; phaseStart = t; res.hitStart = t; }
                    else if (phase == 2 && tp >= Hit) { phase = 3; phaseStart = t; res.whiteStart = t; }
                    else if (phase == 3 && tp >= WhiteHold + FadeToBlack) { phase = 4; phaseStart = t; res.blackStart = t; }
                    else if (phase == 4 && tp >= Black) break;

                    var s = new AudioState { masterGain = Mathf.Clamp01((float)(t / FadeIn)) };
                    AudioDirector.Fill(ref s, (float)t);
                    s.endPhase = phase;
                    synth.SetState(s);
                    if (phase == 0) AudioDirector.Tick((float)t, emit);
                    synth.Render(buf, Block, 2);

                    for (int i = 0; i < Block * 2; i++)
                    {
                        float x = buf[i];
                        if (float.IsNaN(x) || float.IsInfinity(x)) { res.nonFinite++; x = 0f; }
                        float a = x < 0f ? -x : x;
                        if (a > res.peak) res.peak = a;
                        int v = (int)Math.Round(x * 32767.0);
                        if (v > 32767) v = 32767; else if (v < -32768) v = -32768;
                        bytes[i * 2] = (byte)(v & 0xFF);
                        bytes[i * 2 + 1] = (byte)((v >> 8) & 0xFF);
                    }
                    fs.Write(bytes, 0, bytes.Length);
                    res.frames += Block;
                    t += dt;
                }

                long dataBytes = res.frames * 4;
                fs.Seek(0, SeekOrigin.Begin);
                using (var w = new BinaryWriter(fs, Encoding.ASCII, true))
                {
                    w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write((uint)(36 + dataBytes));
                    w.Write(Encoding.ASCII.GetBytes("WAVE"));
                    w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16u);
                    w.Write((ushort)1); w.Write((ushort)2);                     // PCM, stereo
                    w.Write((uint)Rate); w.Write((uint)(Rate * 4));
                    w.Write((ushort)4); w.Write((ushort)16);
                    w.Write(Encoding.ASCII.GetBytes("data")); w.Write((uint)dataBytes);
                }
            }
            res.nanResets = synth.NanResets; res.hits = synth.Hits; res.emitted = emitted; res.dropped = synth.EventsDropped;
            return res;
        }
    }
}
