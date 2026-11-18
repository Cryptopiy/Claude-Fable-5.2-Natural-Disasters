using System;

namespace CASCADE
{
    /// <summary>Power-of-two ring buffer delay. All memory is allocated in the constructor.</summary>
    public sealed class DelayLine
    {
        readonly float[] buf; readonly int mask; int w;
        public DelayLine(int maxDelay)
        {
            int n = 16;
            while (n < maxDelay + 4) n <<= 1;
            buf = new float[n]; mask = n - 1;
        }
        public void Clear() { Array.Clear(buf, 0, buf.Length); w = 0; }
        public void Write(float x) { buf[w] = x; w = (w + 1) & mask; }
        /// <summary>Sample written 'delay' Write calls ago (delay >= 1).</summary>
        public float Read(int delay) { return buf[(w - delay) & mask]; }
        public float ReadFrac(float delay)
        {
            int d = (int)delay; float f = delay - d;
            float a = buf[(w - d) & mask], b = buf[(w - d - 1) & mask];
            return a + (b - a) * f;
        }
        /// <summary>Schroeder all-pass around this line (diffusion).</summary>
        public float AllPass(float x, int len, float g)
        {
            float d = Read(len);
            float y = d - g * x;
            Write(x + g * y);
            return y;
        }
    }

    /// <summary>
    /// Small stereo feedback-delay-network reverb: two diffusion all-passes per input channel, eight delay lines mixed by a
    /// Householder matrix, one-pole damping in every loop. T60 and damping can change while it runs.
    /// </summary>
    public sealed class Reverb
    {
        const int N = 8;
        static readonly int[] Base = { 1423, 1789, 2137, 2473, 2857, 3181, 3571, 3989 };   // samples at 48 kHz, primes
        readonly DelayLine[] line = new DelayLine[N];
        readonly int[] len = new int[N];
        readonly float[] gain = new float[N], lp = new float[N], v = new float[N];
        readonly DelayLine apL1, apL2, apR1, apR2;
        readonly int a1, a2, a3, a4, fs;
        float damp = 0.4f, t60Set = -1f, dampSet = -1f;

        public Reverb(int sampleRate)
        {
            fs = sampleRate;
            double k = fs / 48000.0;
            for (int i = 0; i < N; i++) { len[i] = (int)(Base[i] * k) | 1; line[i] = new DelayLine(len[i]); }
            a1 = (int)(149 * k) | 1; a2 = (int)(379 * k) | 1; a3 = (int)(113 * k) | 1; a4 = (int)(277 * k) | 1;
            apL1 = new DelayLine(a1); apL2 = new DelayLine(a2); apR1 = new DelayLine(a3); apR2 = new DelayLine(a4);
            Set(2.5f, 4000f);
        }

        public void Clear()
        {
            for (int i = 0; i < N; i++) { line[i].Clear(); lp[i] = 0f; }
            apL1.Clear(); apL2.Clear(); apR1.Clear(); apR2.Clear();
        }

        /// <summary>Decay time in seconds and loop damping corner in Hz. Cheap to call; recomputes only on a real change.</summary>
        public void Set(float t60, float dampHz)
        {
            if (Math.Abs(t60 - t60Set) < 0.01f * t60 && Math.Abs(dampHz - dampSet) < 0.01f * dampHz) return;
            t60Set = t60; dampSet = dampHz;
            for (int i = 0; i < N; i++) gain[i] = (float)Math.Pow(10.0, -3.0 * len[i] / (t60 * (double)fs));
            damp = 1f - (float)Math.Exp(-Dsp.Tau * dampHz / fs);
        }

        /// <summary>Adds the wet signal of n input samples to outL / outR.</summary>
        public void Process(float[] inL, float[] inR, float[] outL, float[] outR, int n, float wet)
        {
            for (int s = 0; s < n; s++)
            {
                float xl = apL2.AllPass(apL1.AllPass(inL[s], a1, 0.6f), a2, 0.6f);
                float xr = apR2.AllPass(apR1.AllPass(inR[s], a3, 0.6f), a4, 0.6f);
                float sum = 0f;
                for (int i = 0; i < N; i++) { v[i] = line[i].Read(len[i]); sum += v[i]; }
                sum *= 0.25f;                                           // Householder: x - (2/N) sum
                for (int i = 0; i < N; i++)
                {
                    float y = (v[i] - sum) * gain[i];
                    lp[i] += damp * (y - lp[i]) + 1e-20f;
                    line[i].Write(lp[i] + ((i & 1) == 0 ? xl : xr));
                }
                outL[s] += (v[0] - v[2] + v[4] - v[6]) * wet;
                outR[s] += (v[1] - v[3] + v[5] - v[7]) * wet;
            }
        }
    }

    /// <summary>
    /// Cliff echoes for the big far events: one long line, three low-passed taps (2 x distance / 340 for walls
    /// 160 to 540 m away) panned apart, a little feedback from the last tap. Mono in, stereo out.
    /// </summary>
    public sealed class EchoTaps
    {
        readonly DelayLine line;
        readonly int d1, d2, d3;
        OnePole lp1, lp2, lp3;
        public EchoTaps(int fs)
        {
            d1 = (int)(0.94 * fs); d2 = (int)(1.93 * fs); d3 = (int)(3.17 * fs);
            line = new DelayLine(d3 + 8);
            lp1.SetHz(fs, 2400f); lp2.SetHz(fs, 1500f); lp3.SetHz(fs, 900f);
        }
        public void Clear() { line.Clear(); lp1.y = lp2.y = lp3.y = 0f; }
        public void Process(float[] input, float[] outL, float[] outR, int n, float wet)
        {
            for (int s = 0; s < n; s++)
            {
                float t1 = lp1.Lp(line.Read(d1)) * 0.40f;       // -8 dB
                float t2 = lp2.Lp(line.Read(d2)) * 0.28f;       // -11 dB
                float t3 = lp3.Lp(line.Read(d3)) * 0.18f;       // -15 dB
                line.Write(input[s] + t3 * 0.5f);
                outL[s] += (t1 * 0.85f + t2 * 0.35f + t3 * 0.7f) * wet;
                outR[s] += (t1 * 0.40f + t2 * 0.90f + t3 * 0.6f) * wet;
            }
        }
    }

    /// <summary>
    /// Look-ahead brick-wall limiter, stereo linked. The gain needed by each sample is known 5 ms before that sample leaves
    /// the delay, held by a sliding minimum, released smoothly and averaged over the look-ahead, so the output never
    /// exceeds the ceiling and the gain never jumps. A final clamp is the last safety net.
    /// </summary>
    public sealed class Limiter
    {
        readonly int L, cap, cmask;
        readonly float[] dl, dr, box, qv;
        readonly long[] qi;
        readonly float ceiling, rel;
        int w, head, tail, bw;
        long n;
        float gRel = 1f;
        double boxSum;
        public float MinGain = 1f;           // deepest gain reduction since the last reset (diagnostics)

        public Limiter(int fs, float ceilingLin)
        {
            ceiling = ceilingLin;
            L = Math.Max(16, (int)(0.005 * fs));
            dl = new float[L]; dr = new float[L]; box = new float[L];
            int c = 16; while (c < L + 4) c <<= 1;
            cap = c; cmask = c - 1;
            qv = new float[cap]; qi = new long[cap];
            rel = 1f - (float)Math.Exp(-1.0 / (0.22 * fs));
            Clear();
        }

        public int Latency { get { return L; } }

        public void Clear()
        {
            Array.Clear(dl, 0, L); Array.Clear(dr, 0, L);
            for (int i = 0; i < L; i++) box[i] = 1f;
            boxSum = L; w = 0; bw = 0; head = 0; tail = 0; n = 0; gRel = 1f; MinGain = 1f;
        }

        public void Process(ref float l, ref float r)
        {
            float al = l < 0f ? -l : l, ar = r < 0f ? -r : r;
            float m = al > ar ? al : ar;
            float g = m > ceiling ? ceiling / m : 1f;

            // sliding minimum of g over the last L + 1 samples (monotonic queue)
            while (head != tail && qv[(tail - 1) & cmask] >= g) tail = (tail - 1) & cmask;
            qv[tail] = g; qi[tail] = n; tail = (tail + 1) & cmask;
            while (qi[head] < n - L) head = (head + 1) & cmask;
            float gMin = qv[head];
            n++;

            float up = gRel + (1f - gRel) * rel;
            gRel = gMin < up ? gMin : up;
            if (gRel < MinGain) MinGain = gRel;

            boxSum += gRel - box[bw];
            box[bw] = gRel; bw++; if (bw >= L) { bw = 0; ReSum(); }
            float gOut = (float)(boxSum / L);

            float yl = dl[w] * gOut, yr = dr[w] * gOut;
            dl[w] = l; dr[w] = r; w++; if (w >= L) w = 0;
            l = yl > ceiling ? ceiling : yl < -ceiling ? -ceiling : yl;
            r = yr > ceiling ? ceiling : yr < -ceiling ? -ceiling : yr;
        }

        void ReSum()        // removes the rounding drift of the running sum once per window
        {
            double s = 0.0;
            for (int i = 0; i < L; i++) s += box[i];
            boxSum = s;
        }
    }
}
