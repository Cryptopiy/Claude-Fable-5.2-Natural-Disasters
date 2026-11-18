using System;

namespace MESOCYCLONE.Audio
{
    /// <summary>The end of the chain: DC blocker, then a look-ahead soft-knee limiter whose side-chain also sees the
    /// peaks between the samples (4x interpolation). Internal ceiling -1.3 dBFS, so the true peak stays below
    /// -1 dBFS. The gain is the box average of a sliding minimum: it can never step, so it can never click.</summary>
    public sealed class Master
    {
        public float Peak;             // peak of the last block after limiting (linear)
        public float MinGain = 1f;     // deepest gain reduction of the last block
        public bool Faulted;           // a NaN or runaway sample was seen (the engine resets its voices)

        const float CeilDb = -1.3f, KneeDb = 6f, Safety = 1.03f;   // the interpolated peaks are read 3 % high
        readonly float ceil, kneeLo;
        readonly int L, D, cap;
        readonly float[] dl, dr, gb, dqV;
        readonly long[] dqI;
        int w, wg, dqH, dqT, resum;
        long idx;
        double sum;
        float rel = 1f;
        readonly float relC, dcR;
        float dxl, dyl, dxr, dyr;
        const int Taps = 32, TapMask = Taps - 1;
        readonly float[] hl = new float[Taps], hr = new float[Taps];
        readonly float[] fir = new float[3 * Taps];
        int hp, firHold;
        readonly float firGate;

        public Master(int sr)
        {
            ceil = Dsp.Db(CeilDb); kneeLo = Dsp.Db(CeilDb - KneeDb * 0.5f);
            firGate = kneeLo / 2.9f;                    // below this no interpolated peak can reach the knee
            L = Math.Max(32, (int)(sr * 0.005f)); D = L + Taps / 2; cap = L + 2;
            dl = new float[D]; dr = new float[D]; gb = new float[L]; dqV = new float[cap]; dqI = new long[cap];
            for (int i = 0; i < L; i++) gb[i] = 1f;
            sum = L;
            relC = 1f - MathF.Exp(-1f / (0.16f * sr));
            dcR = 1f - Dsp.TwoPi * 10f / sr;
            // three interpolation phases (1/4, 1/2, 3/4 between the two middle samples of a 32-sample window)
            int half = Taps / 2;
            for (int p = 0; p < 3; p++)
            {
                float frac = 0.25f * (p + 1), total = 0f;
                for (int k = 0; k < Taps; k++)
                {
                    float t = (k - (half - 1)) - frac, x = MathF.PI * t;
                    float sinc = MathF.Abs(t) < 1e-6f ? 1f : MathF.Sin(x) / x;
                    float h = sinc * (0.5f + 0.5f * MathF.Cos(MathF.PI * t / half));
                    fir[p * Taps + k] = h; total += h;
                }
                for (int k = 0; k < Taps; k++) fir[p * Taps + k] *= Safety / total;
            }
        }

        /// <summary>Processes the first two channels of an interleaved buffer in place (delay about 5 ms).</summary>
        public void Process(float[] d, int frames, int ch)
        {
            float peak = 0f, minG = 1f;
            for (int i = 0; i < frames; i++)
            {
                int o = i * ch;
                float l = d[o], r = ch > 1 ? d[o + 1] : l;
                if (!(l > -8f && l < 8f) || !(r > -8f && r < 8f)) { l = 0f; r = 0f; Faulted = true; }

                // DC blocker (10 Hz)
                float yl = l - dxl + dcR * dyl; dxl = l; dyl = yl;
                float yr = r - dxr + dcR * dyr; dxr = r; dyr = yr;

                // side-chain: the sample half a window back and the three interpolated points after it
                hl[hp] = yl; hr[hp] = yr; hp = (hp + 1) & TapMask;
                float pk = Math.Abs(hl[(hp + Taps / 2 - 1) & TapMask]), q = Math.Abs(hr[(hp + Taps / 2 - 1) & TapMask]);
                if (q > pk) pk = q;
                float newest = yl < 0f ? -yl : yl, newestR = yr < 0f ? -yr : yr;
                if (newest > firGate || newestR > firGate) firHold = Taps + 1;
                if (firHold > 0) firHold--;
                for (int p = 0; firHold > 0 && p < 3 * Taps; p += Taps)
                {
                    float sl = 0f, sr = 0f;
                    for (int k = 0; k < Taps; k++)
                    {
                        int h = (hp + k) & TapMask; float c = fir[p + k];
                        sl += c * hl[h]; sr += c * hr[h];
                    }
                    if (sl < 0f) sl = -sl;
                    if (sr < 0f) sr = -sr;
                    if (sl > pk) pk = sl;
                    if (sr > pk) pk = sr;
                }

                // static curve: soft knee into a brick wall
                float tgt = 1f;
                if (pk > kneeLo)
                {
                    float x = 20f * MathF.Log10(pk), y;
                    float over = x - CeilDb + KneeDb * 0.5f;
                    if (x <= CeilDb + KneeDb * 0.5f) y = x - over * over / (2f * KneeDb); else y = CeilDb;
                    tgt = MathF.Pow(10f, (y - x) * 0.05f);
                }
                float up = rel + (1f - rel) * relC;
                rel = tgt < up ? tgt : up;

                // sliding minimum over the look-ahead (monotonic queue), then its box average
                while (dqH != dqT)
                {
                    int last = dqT == 0 ? cap - 1 : dqT - 1;
                    if (dqV[last] >= rel) dqT = last; else break;
                }
                dqV[dqT] = rel; dqI[dqT] = idx; dqT = dqT == cap - 1 ? 0 : dqT + 1;
                if (dqI[dqH] <= idx - L) dqH = dqH == cap - 1 ? 0 : dqH + 1;
                float m = dqV[dqH];
                sum += m - gb[wg]; gb[wg] = m;
                if (++wg == L) wg = 0;
                float g = (float)(sum / L);
                if (g > 1f) g = 1f;
                if (g < minG) minG = g;

                dl[w] = yl; dr[w] = yr;
                if (++w == D) w = 0;
                float ol = dl[w] * g, orr = dr[w] * g;
                if (ol > ceil) ol = ceil; else if (ol < -ceil) ol = -ceil;
                if (orr > ceil) orr = ceil; else if (orr < -ceil) orr = -ceil;
                d[o] = ol;
                if (ch > 1) d[o + 1] = orr;
                idx++;
                float a = ol < 0f ? -ol : ol, b = orr < 0f ? -orr : orr;
                if (a > peak) peak = a;
                if (b > peak) peak = b;
            }
            Peak = peak; MinGain = minG;
            resum += frames;
            if (resum > 65536)
            {   // re-anchor the running sum so that rounding can never drift
                double s = 0.0;
                for (int i = 0; i < L; i++) s += gb[i];
                sum = s; resum = 0;
            }
        }
    }
}
