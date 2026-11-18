using System;

namespace ZERO
{
    /// <summary>Power-of-two ring buffer with integer and fractional (linear) taps.</summary>
    public sealed class DelayLine
    {
        readonly float[] buf;
        readonly int mask, size;
        int w;

        public DelayLine(int maxDelaySamples)
        {
            int n = 16;
            while (n < maxDelaySamples + 8) n <<= 1;
            buf = new float[n]; size = n; mask = n - 1;
        }

        public void Write(float x) { buf[w] = x; w = (w + 1) & mask; }

        /// <summary>Sample written "delay" writes ago (delay >= 1).</summary>
        public float ReadInt(int delay) { return buf[(w - delay) & mask]; }

        /// <summary>Fractional tap, delay in samples (>= 1). Smooth the delay per sample for Doppler.</summary>
        public float Read(float delay)
        {
            float rp = w - delay + size;
            int i = (int)rp;
            float f = rp - i;
            float a = buf[i & mask], b = buf[(i + 1) & mask];
            return a + f * (b - a);
        }

        public void Clear() { Array.Clear(buf, 0, buf.Length); w = 0; }
    }

    /// <summary>Four-line feedback delay network (Householder feedback) with damping, a low cut in the loop
    /// and two slowly modulated lines. Stereo in, stereo out. The wet output is normalised so that noise comes
    /// out at about the level it went in.</summary>
    public sealed class Fdn4
    {
        readonly DelayLine d0, d1, d2, d3;
        readonly float fs;
        readonly float len0, len1, len2, len3;
        readonly int i1, i3;
        float g0, g1, g2, g3, outGain;
        float lp0, lp1, lp2, lp3, damp;
        float hp0, hp1, hp2, hp3, hpc;
        Lfo mod0, mod2;
        readonly float modDepth;

        public Fdn4(int sampleRate, float rt60, float dampHz) : this(sampleRate, rt60, dampHz, 1f) { }

        /// <param name="size">scales the delay lines: 1 = a wide landscape (60-104 ms), 0.2 = a small room such as a cabin</param>
        public Fdn4(int sampleRate, float rt60, float dampHz, float size)
        {
            fs = sampleRate;
            float k = fs / 48000f * Dsp.Clamp(size, 0.05f, 2f);
            len0 = (int)(2903 * k) | 1; len1 = (int)(3571 * k) | 1; len2 = (int)(4219 * k) | 1; len3 = (int)(5003 * k) | 1;
            i1 = (int)len1; i3 = (int)len3;
            modDepth = 24f * k;
            d0 = new DelayLine((int)(len0 + modDepth) + 8);
            d1 = new DelayLine(i1 + 8);
            d2 = new DelayLine((int)(len2 + modDepth) + 8);
            d3 = new DelayLine(i3 + 8);
            damp = Dsp.LpCoef(dampHz, fs);
            hpc = Dsp.LpCoef(28f, fs);
            mod0.Set(0.11f, fs);
            mod2.Set(0.173f, fs); mod2.SetPhase(0.37f);
            SetRt60(rt60);
        }

        public void SetRt60(float rt60)
        {
            g0 = (float)Math.Pow(10.0, -3.0 * len0 / (rt60 * fs));
            g1 = (float)Math.Pow(10.0, -3.0 * len1 / (rt60 * fs));
            g2 = (float)Math.Pow(10.0, -3.0 * len2 / (rt60 * fs));
            g3 = (float)Math.Pow(10.0, -3.0 * len3 / (rt60 * fs));
            float gm = 0.25f * (g0 * g0 + g1 * g1 + g2 * g2 + g3 * g3);
            outGain = (float)Math.Sqrt((1.0 - gm) * 0.5);
        }

        public void Process(float inL, float inR, out float outL, out float outR)
        {
            float x0 = d0.Read(len0 + modDepth * mod0.Sin());
            float x1 = d1.ReadInt(i1);
            float x2 = d2.Read(len2 + modDepth * mod2.Sin());
            float x3 = d3.ReadInt(i3);
            // damping (high cut) and low cut inside the loop
            lp0 += damp * (x0 - lp0); lp1 += damp * (x1 - lp1); lp2 += damp * (x2 - lp2); lp3 += damp * (x3 - lp3);
            hp0 += hpc * (lp0 - hp0); hp1 += hpc * (lp1 - hp1); hp2 += hpc * (lp2 - hp2); hp3 += hpc * (lp3 - hp3);
            x0 = lp0 - hp0; x1 = lp1 - hp1; x2 = lp2 - hp2; x3 = lp3 - hp3;
            float s = 0.5f * (x0 + x1 + x2 + x3);
            const float anti = 1e-18f;                              // keeps the loop out of the denormal range
            d0.Write(inL + g0 * (x0 - s) + anti);
            d1.Write(inR + g1 * (x1 - s) - anti);
            d2.Write(-inR + g2 * (x2 - s) + anti);
            d3.Write(inL + g3 * (x3 - s) - anti);
            outL = (x0 + x2) * outGain;
            outR = (x1 + x3) * outGain;
        }

        public void Clear()
        {
            d0.Clear(); d1.Clear(); d2.Clear(); d3.Clear();
            lp0 = lp1 = lp2 = lp3 = 0f; hp0 = hp1 = hp2 = hp3 = 0f;
        }
    }

    /// <summary>Stereo-linked look-ahead soft limiter, the last stage of the game.
    /// Gain computer with a soft knee between Threshold and the internal ceiling, sliding minimum over the
    /// look-ahead window, 200 ms release, then a moving average as long as the window, so the gain is always
    /// down before the peak leaves the delay: no overshoot. A final hard clamp at -1 dBFS is only a safety net.
    /// The detector also estimates the inter-sample peak half-way between samples.</summary>
    public sealed class Limiter
    {
        public const float Threshold = 0.70f;
        public const float SoftCeiling = 0.84f;      // -1.5 dBFS: leaves room for inter-sample peaks

        readonly int look, delay, minWin, cap, capMask;
        readonly float[] dl, dr, box, qVal;
        readonly int[] qIdx;
        int dpos, bpos, qHead, qCount, n;
        double boxSum;
        float rel, relCoef;
        // detector history for the half-sample estimate
        float l1, l2, l3, l4, l5, r1, r2, r3, r4, r5;

        /// <summary>Current gain (1 = no reduction). Written on the audio thread; read it for meters only.</summary>
        public float Gain = 1f;
        /// <summary>Latency in samples (5 ms plus two samples).</summary>
        public int Latency { get { return delay; } }

        public Limiter(int sampleRate)
        {
            look = Math.Max(8, (int)(0.005f * sampleRate));
            delay = look + 2;
            minWin = look + 3;
            int c = 16;
            while (c < minWin + 2) c <<= 1;
            cap = c; capMask = c - 1;
            dl = new float[delay]; dr = new float[delay];
            box = new float[look];
            qVal = new float[cap]; qIdx = new int[cap];
            relCoef = Dsp.TauCoef(0.2f, sampleRate);
            Reset();
        }

        public void Reset()
        {
            Array.Clear(dl, 0, dl.Length); Array.Clear(dr, 0, dr.Length);
            for (int i = 0; i < box.Length; i++) box[i] = 1f;
            boxSum = box.Length; rel = 1f; Gain = 1f;
            dpos = 0; bpos = 0; qHead = 0; qCount = 0; n = 0;
            l1 = l2 = l3 = l4 = l5 = 0f; r1 = r2 = r3 = r4 = r5 = 0f;
        }

        public void Process(ref float l, ref float r)
        {
            // --- detector: sample peak and the half-sample point between x[n-3] and x[n-2] (6-point Lagrange)
            float ml = (3f * (l + l5) - 25f * (l1 + l4) + 150f * (l2 + l3)) * 0.00390625f;
            float mr = (3f * (r + r5) - 25f * (r1 + r4) + 150f * (r2 + r3)) * 0.00390625f;
            l5 = l4; l4 = l3; l3 = l2; l2 = l1; l1 = l;
            r5 = r4; r4 = r3; r3 = r2; r2 = r1; r1 = r;
            float p = l < 0f ? -l : l;
            float q = r < 0f ? -r : r; if (q > p) p = q;
            q = ml < 0f ? -ml : ml; if (q > p) p = q;
            q = mr < 0f ? -mr : mr; if (q > p) p = q;

            // --- gain computer (soft knee)
            float gt = 1f;
            if (p > Threshold)
            {
                const float span = SoftCeiling - Threshold;
                float o = Threshold + span * (1f - (float)Math.Exp(-(p - Threshold) / span));
                gt = o / p;
            }

            // --- sliding minimum over minWin samples (monotonic queue)
            unchecked
            {
                while (qCount > 0 && qVal[(qHead + qCount - 1) & capMask] >= gt) qCount--;
                int t = (qHead + qCount) & capMask;
                qVal[t] = gt; qIdx[t] = n; qCount++;
                if (n - qIdx[qHead] >= minWin) { qHead = (qHead + 1) & capMask; qCount--; }
                n++;
            }
            float m = qVal[qHead];

            // --- instant attack, slow release, then the moving average
            if (m < rel) rel = m; else rel += relCoef * (m - rel);
            boxSum += rel - box[bpos];
            box[bpos] = rel;
            if (++bpos >= look) bpos = 0;
            float g = (float)(boxSum / look);
            if (g > 1f) g = 1f;
            Gain = g;

            // --- delayed audio times gain, safety clamp
            float ol = dl[dpos] * g, or = dr[dpos] * g;
            dl[dpos] = l; dr[dpos] = r;
            if (++dpos >= delay) dpos = 0;
            if (ol > Dsp.Ceiling) ol = Dsp.Ceiling; else if (ol < -Dsp.Ceiling) ol = -Dsp.Ceiling;
            if (or > Dsp.Ceiling) or = Dsp.Ceiling; else if (or < -Dsp.Ceiling) or = -Dsp.Ceiling;
            l = ol; r = or;
        }
    }
}
