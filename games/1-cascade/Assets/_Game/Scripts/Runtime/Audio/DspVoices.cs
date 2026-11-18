using System;

namespace CASCADE
{
    /// <summary>
    /// A bank of two-pole resonators (modal synthesis): glass, crockery, shutters, hulls, masonry, the lighthouse gear.
    /// Excite = short noise burst (natural variation), Strike = one clean impulse (repeatable tick).
    /// </summary>
    public sealed class ModalBank
    {
        readonly int n, fs;
        readonly double[] a1, a2, y1, y2;
        readonly float[] exc, kick, norm, gl, gr;
        readonly int[] live, liveLen;
        readonly float excDecay, excNorm;
        Rng rng; readonly uint seed0;
        int anyLive;

        public ModalBank(int count, int sampleRate, uint seed, float burstMs)
        {
            n = count; fs = sampleRate; seed0 = seed;
            a1 = new double[n]; a2 = new double[n]; y1 = new double[n]; y2 = new double[n];
            exc = new float[n]; kick = new float[n]; norm = new float[n]; gl = new float[n]; gr = new float[n];
            live = new int[n]; liveLen = new int[n];
            excDecay = (float)Math.Exp(-1.0 / (burstMs * 0.001 * fs));
            excNorm = (float)Math.Sqrt(3.0 * (1.0 - excDecay * excDecay));
            rng = new Rng(seed);
        }

        public void Reset()
        {
            rng = new Rng(seed0); anyLive = 0;
            for (int i = 0; i < n; i++) { y1[i] = y2[i] = 0.0; exc[i] = kick[i] = 0f; live[i] = 0; }
        }

        public void Define(int i, float hz, float t60, float pan, float gain)
        {
            double w = Dsp.Tau * hz / fs;
            double r = Math.Pow(10.0, -3.0 / (t60 * fs));
            a1[i] = 2.0 * r * Math.Cos(w); a2[i] = r * r;
            norm[i] = (float)Math.Sin(w) * gain;
            Dsp.Pan(pan, out gl[i], out gr[i]);
            liveLen[i] = (int)(t60 * 1.7f * fs) + 64;
        }

        public void Excite(int i, float amp) { exc[i] += amp * excNorm; Wake(i); }
        public void Strike(int i, float amp) { kick[i] += amp; Wake(i); }
        void Wake(int i) { live[i] = liveLen[i]; if (anyLive < liveLen[i]) anyLive = liveLen[i]; }

        public void Run(float[] outL, float[] outR, int count, float gain)
        {
            if (anyLive <= 0) return;
            anyLive -= count;
            for (int i = 0; i < n; i++)
            {
                if (live[i] <= 0) continue;
                live[i] -= count;
                double c1 = a1[i], c2 = a2[i], s1 = y1[i], s2 = y2[i];
                float e = exc[i], k = kick[i];
                float gL = gl[i] * norm[i] * gain, gR = gr[i] * norm[i] * gain;
                for (int s = 0; s < count; s++)
                {
                    double x = k; k = 0f;
                    if (e > 1e-7f) { x += e * rng.Bi(); e *= excDecay; }
                    double y = x + c1 * s1 - c2 * s2;
                    s2 = s1; s1 = y;
                    float o = (float)y;
                    outL[s] += o * gL; outR[s] += o * gR;
                }
                if (live[i] <= 0) { s1 = 0.0; s2 = 0.0; e = 0f; }
                y1[i] = s1; y2[i] = s2; exc[i] = e; kick[i] = k;
            }
        }
    }

    /// <summary>
    /// Chapel bell: eight inharmonic partials (hum, prime, tierce, quint, nominal, deciem, twelfth, upper octave), each a
    /// beating doublet with its own decay, plus a faint clapper noise. Strikes add energy, so strokes overlap naturally.
    /// </summary>
    public sealed class Bell
    {
        const int P = 16;
        static readonly float[] Ratio = { 0.5f, 1f, 1.2f, 1.5f, 2f, 2.5f, 3f, 4f };
        static readonly float[] Amp = { 0.6f, 0.5f, 0.8f, 0.25f, 1f, 0.3f, 0.35f, 0.2f };
        static readonly float[] T60 = { 14f, 9f, 6f, 3f, 4f, 2f, 1.5f, 1f };
        readonly double[] c = new double[P], s = new double[P], cw = new double[P], sw = new double[P];
        readonly float[] energy = new float[P], amp = new float[P], decay = new float[P], pa = new float[P];
        readonly float atk, clackDecay; readonly int fs, liveLen; readonly uint seed0;
        int live; Rng rng; float clack; Biquad clackBp;

        public Bell(int sampleRate, float primeHz, float size, uint seed)
        {
            fs = sampleRate; seed0 = seed;
            float longest = 0f;
            for (int i = 0; i < P; i++)
            {
                int k = i & 7; bool twin = i >= 8;
                float hz = primeHz * Ratio[k] + (twin ? 0.35f + 1.1f * Dsp.Hash(k, 911) : 0f);
                double w = Dsp.Tau * hz / fs;
                cw[i] = Math.Cos(w); sw[i] = Math.Sin(w);
                float t60 = T60[k] * size * (twin ? 0.85f : 1f);
                if (t60 > longest) longest = t60;
                decay[i] = (float)Math.Pow(10.0, -3.0 / (t60 * fs));
                pa[i] = Amp[k] * (twin ? 0.55f : 1f) * 0.2f;
            }
            atk = 1f - (float)Math.Exp(-1.0 / (0.004 * fs));
            clackDecay = (float)Math.Exp(-1.0 / (0.007 * fs));
            liveLen = (int)(longest * 1.5f * fs);
            clackBp.BandPass(fs, 3100.0, 1.0);
            Reset();
        }

        public void Reset()
        {
            rng = new Rng(seed0); live = 0; clack = 0f; clackBp.Clear();
            for (int i = 0; i < P; i++) { c[i] = 1.0; s[i] = 0.0; energy[i] = 0f; amp[i] = 0f; }
        }

        public bool Live { get { return live > 0; } }

        /// <summary>vel 0..1; bright scales the upper partials and the clapper noise (hard stroke = bright).</summary>
        public void Strike(float vel, float bright)
        {
            for (int i = 0; i < P; i++)
            {
                int k = i & 7;
                energy[i] += vel * pa[i] * (k >= 4 ? bright : 1f);
            }
            clack += vel * bright * 0.25f;
            live = liveLen;
        }

        /// <summary>Adds n mono samples to 'o'.</summary>
        public void Run(float[] o, int n, float gain)
        {
            if (live <= 0) return;
            live -= n;
            for (int i = 0; i < P; i++)
            {
                double ci = c[i], si = s[i], cwi = cw[i], swi = sw[i];
                float e = energy[i], a = amp[i], d = decay[i];
                for (int k = 0; k < n; k++)
                {
                    double nc = ci * cwi - si * swi;
                    si = si * cwi + ci * swi; ci = nc;
                    e *= d; a += atk * (e - a);
                    o[k] += (float)si * a * gain;
                }
                double m = (3.0 - (ci * ci + si * si)) * 0.5;       // keep the rotation on the unit circle
                c[i] = ci * m; s[i] = si * m; energy[i] = e; amp[i] = a;
            }
            if (clack > 1e-5f)
                for (int k = 0; k < n; k++) { o[k] += clackBp.Next(rng.Bi() * clack) * gain; clack *= clackDecay; }
            if (live <= 0) for (int i = 0; i < P; i++) { energy[i] = 0f; amp[i] = 0f; }
        }
    }

    /// <summary>Tiny gliding sine pips: bubbles and drips of water on stone.</summary>
    public sealed class Blips
    {
        const int N = 8;
        readonly double[] ph = new double[N], inc = new double[N], mul = new double[N];
        readonly float[] amp = new float[N], dec = new float[N], at = new float[N], gl = new float[N], gr = new float[N];
        int next;

        public void Reset() { for (int i = 0; i < N; i++) { amp[i] = 0f; at[i] = 0f; ph[i] = 0.0; } next = 0; }

        public void Trigger(int fs, float hz, float octavesPerSecond, float a, float tau, float pan)
        {
            int i = next; next = (next + 1) % N;
            ph[i] = 0.0; inc[i] = hz / fs;
            mul[i] = Math.Pow(2.0, octavesPerSecond / fs);
            amp[i] = a; at[i] = 0f;
            dec[i] = (float)Math.Exp(-1.0 / (tau * fs));
            Dsp.Pan(pan, out gl[i], out gr[i]);
        }

        public void Run(float[] outL, float[] outR, int n)
        {
            for (int i = 0; i < N; i++)
            {
                if (amp[i] < 1e-5f) continue;
                double p = ph[i], d = inc[i], m = mul[i];
                float a = amp[i], k = dec[i], t = at[i], l = gl[i], r = gr[i];
                for (int s = 0; s < n; s++)
                {
                    p += d; if (p >= 1.0) p -= 1.0;
                    d *= m; a *= k; t += (1f - t) * 0.03f;
                    float o = (float)Math.Sin(p * Dsp.Tau) * a * t;
                    outL[s] += o * l; outR[s] += o * r;
                }
                ph[i] = p; inc[i] = d; amp[i] = a; at[i] = t;
            }
        }
    }

    /// <summary>
    /// Stick-slip creak: a jittered, decelerating pulse train through three resonances, under a slow swell.
    /// Rope = 600 to 1800 Hz, timber = 90 to 350 Hz, steel = sharp high resonances.
    /// </summary>
    public struct Creak
    {
        public bool on;
        Rng r; double ph; float t, dur, rate0, rate1, amp, jit, jitNow, gl, gr, invFs, g1, g2, g3;
        Svf f1, f2, f3;

        public void Trigger(int fs, uint seed, float rateStart, float rateEnd, float seconds, float a,
                            float hz1, float hz2, float hz3, float q, float pan, float jitter)
        {
            r = new Rng(seed); ph = 0.9; t = 0f; dur = seconds; rate0 = rateStart; rate1 = rateEnd; amp = a; jit = jitter; jitNow = 1f;
            invFs = 1f / fs;
            f1.Clear(); f2.Clear(); f3.Clear();
            f1.Set(fs, hz1, q); f2.Set(fs, hz2, q); f3.Set(fs, hz3, q);
            float k = q * fs / (float)Dsp.Tau;                    // makes one impulse ring at about unit amplitude
            g1 = k / hz1; g2 = 0.7f * k / hz2; g3 = 0.5f * k / hz3;
            Dsp.Pan(pan, out gl, out gr);
            on = true;
        }

        public void Run(float[] outL, float[] outR, int n)
        {
            if (!on) return;
            for (int s = 0; s < n; s++)
            {
                t += invFs;
                float x = t / dur;
                float imp = 0f, env = 0f;
                if (x < 1f)
                {
                    float rate = rate0 + (rate1 - rate0) * x;
                    ph += rate * invFs * jitNow;
                    if (ph >= 1.0) { ph -= 1.0; imp = 0.45f + 0.55f * r.Uni(); jitNow = 1f + jit * r.Bi(); }
                    env = Dsp.Smooth(0f, 0.3f, x) * (1f - Dsp.Smooth(0.55f, 1f, x));
                }
                else if (x > 1f + 0.25f / dur) { on = false; return; }
                float e = imp * env * amp;
                float y = f1.Bp(e) * g1 + f2.Bp(e) * g2 + f3.Bp(e) * g3;
                outL[s] += y * gl; outR[s] += y * gr;
            }
        }
    }
}
