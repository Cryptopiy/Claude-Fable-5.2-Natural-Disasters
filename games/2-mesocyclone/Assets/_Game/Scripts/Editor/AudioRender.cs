using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using MESOCYCLONE.Audio;
using UnityEngine;

namespace MESOCYCLONE.EditorTools
{
    /// <summary>Offline sound check: renders every beat with the game's own audio engine (no audio device, no
    /// GPU) to 16-bit stereo WAV files in QA/audio, for Tools/audio_check.py.
    /// Unity.exe -batchmode -nographics -quit -executeMethod MESOCYCLONE.EditorTools.AudioRender.RenderAll</summary>
    public static class AudioRender
    {
        const int SampleRate = 48000;
        const float LastBeatEnd = 684f;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static void RenderAll()
        {
            string dir;
            try
            {
                dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "QA", "audio");
                Directory.CreateDirectory(dir);
            }
            catch (Exception e)
            {
                Debug.LogError("[AudioRender] cannot create the output folder: " + e.Message);
                return;
            }
            EnsureScore();
            int ok = 0;
            for (int beat = 0; beat < Config.BeatCount; beat++)
            {
                try
                {
                    float t0 = Config.BeatStart[beat];
                    float t1 = beat == Config.BeatCount - 1 ? LastBeatEnd : Config.BeatEnd[beat];
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    float[] pcm = OfflineAudio.Render(t0, t1, SampleRate);
                    watch.Stop();
                    string path = Path.Combine(dir, "beat" + (beat + 1).ToString(Inv) + ".wav");
                    WriteWav(path, pcm, SampleRate, 2);
                    double peak = 0.0, sum = 0.0;
                    for (int i = 0; i < pcm.Length; i++)
                    {
                        double v = pcm[i];
                        if (v < 0.0) v = -v;
                        if (v > peak) peak = v;
                        sum += v * v;
                    }
                    double rms = pcm.Length > 0 ? Math.Sqrt(sum / pcm.Length) : 0.0;
                    double seconds = pcm.Length / 2.0 / SampleRate;
                    Debug.Log("[AudioRender] beat" + (beat + 1).ToString(Inv) + ".wav  " + seconds.ToString("F1", Inv) + " s  peak " +
                              Db(peak).ToString("F2", Inv) + " dBFS  rms " + Db(rms).ToString("F2", Inv) + " dBFS  limiter max reduction " +
                              (-Db(OfflineAudio.MinLimiterGain)).ToString("F1", Inv) + " dB  (rendered in " +
                              watch.Elapsed.TotalSeconds.ToString("F1", Inv) + " s = " +
                              (100.0 * watch.Elapsed.TotalSeconds / Math.Max(0.001, seconds)).ToString("F1", Inv) + " % of real time)");
                    ok++;
                }
                catch (Exception e)
                {
                    Debug.LogError("[AudioRender] beat " + (beat + 1).ToString(Inv) + " failed: " + e);
                }
            }
            Debug.Log("[AudioRender] done: " + ok.ToString(Inv) + " of " + Config.BeatCount.ToString(Inv) + " files in " + dir);
        }

        static double Db(double linear) { return 20.0 * Math.Log10(Math.Max(linear, 1e-9)); }

        /// <summary>The designed flashes (thunder, transformer pops) are registered by the beats when the game is
        /// built. In a bare editor batch run nobody has done that yet, so do it here (by name, so that this tool
        /// never breaks the build if the beats change).</summary>
        static void EnsureScore()
        {
            int before = 0;
            try { before = LightningScore.Designed.Count; } catch (Exception) { }
            if (before == 0)
            {
                GameObject temp = null;
                try
                {
                    Type beats = Type.GetType("MESOCYCLONE.Beats, Game.Runtime");
                    MethodInfo build = beats != null ? beats.GetMethod("BuildAll", BindingFlags.Public | BindingFlags.Static) : null;
                    if (build != null)
                    {
                        temp = new GameObject("AudioRenderTemp");
                        build.Invoke(null, new object[] { temp.transform });
                    }
                    else Debug.LogWarning("[AudioRender] MESOCYCLONE.Beats.BuildAll(Transform) not found: designed lightning is missing from the render");
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[AudioRender] could not register the designed lightning: " + e.Message);
                }
                finally
                {
                    if (temp != null) UnityEngine.Object.DestroyImmediate(temp);
                }
            }
            int count = 0;
            try { count = LightningScore.Designed.Count; } catch (Exception) { }
            Debug.Log("[AudioRender] designed flashes in the score: " + count.ToString(Inv));
        }

        /// <summary>16-bit PCM RIFF/WAVE, interleaved.</summary>
        static void WriteWav(string path, float[] samples, int sampleRate, int channels)
        {
            int dataBytes = samples.Length * 2;
            byte[] bytes = new byte[44 + dataBytes];
            WriteAscii(bytes, 0, "RIFF"); WriteInt(bytes, 4, 36 + dataBytes); WriteAscii(bytes, 8, "WAVE");
            WriteAscii(bytes, 12, "fmt "); WriteInt(bytes, 16, 16);
            WriteShort(bytes, 20, 1); WriteShort(bytes, 22, (short)channels);
            WriteInt(bytes, 24, sampleRate); WriteInt(bytes, 28, sampleRate * channels * 2);
            WriteShort(bytes, 32, (short)(channels * 2)); WriteShort(bytes, 34, 16);
            WriteAscii(bytes, 36, "data"); WriteInt(bytes, 40, dataBytes);
            int o = 44;
            for (int i = 0; i < samples.Length; i++)
            {
                double v = Math.Round(samples[i] * 32767.0);
                if (v > 32767.0) v = 32767.0; else if (v < -32768.0) v = -32768.0;
                short s = (short)v;
                bytes[o++] = (byte)(s & 0xFF); bytes[o++] = (byte)((s >> 8) & 0xFF);
            }
            File.WriteAllBytes(path, bytes);
        }

        static void WriteAscii(byte[] b, int o, string s) { for (int i = 0; i < s.Length; i++) b[o + i] = (byte)s[i]; }
        static void WriteInt(byte[] b, int o, int v) { b[o] = (byte)v; b[o + 1] = (byte)(v >> 8); b[o + 2] = (byte)(v >> 16); b[o + 3] = (byte)(v >> 24); }
        static void WriteShort(byte[] b, int o, short v) { b[o] = (byte)v; b[o + 1] = (byte)(v >> 8); }
    }
}
