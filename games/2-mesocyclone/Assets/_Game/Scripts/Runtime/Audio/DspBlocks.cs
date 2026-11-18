using System;

namespace MESOCYCLONE.Audio
{
    /// <summary>A bank of decaying resonators (rotating phasors) shared by every impact on one panel: the cost does
    /// not depend on the number of impacts. Two excitation inputs with different mode weights stand for two
    /// positions on the panel, so that hits do not all sound alike.</summary>
    public sealed class ModalBank
    {
        public readonly int count;
        public readonly float[] excA = new float[Ctx.Sub];
        public readonly float[] excB = new float[Ctx.Sub];
        readonly float[] re, im, cr, sn, wa, wb, gl, gr;
        bool hot;
        int idle;

        public ModalBank(int count)
        {
            this.count = count;
            re = new float[count]; im = new float[count]; cr = new float[count]; sn = new float[count];
            wa = new float[count]; wb = new float[count]; gl = new float[count]; gr = new float[count];
        }

        /// <summary>Defines mode i. t60 in seconds; weightA/B are the excitation weights of the two inputs.</summary>
        public void Set(int i, float freq, float t60, float gain, float pan, float weightA, float weightB, float sr)
        {
            if (freq > 0.45f * sr) freq = 0.45f * sr;
            float w = Dsp.TwoPi * freq / sr;
            float r = MathF.Exp(-6.908f / (MathF.Max(t60, 0.002f) * sr));
            cr[i] = MathF.Cos(w) * r; sn[i] = MathF.Sin(w) * r;
            wa[i] = weightA; wb[i] = weightB;
            float l, rr; Dsp.Pan(pan, out l, out rr);
            gl[i] = gain * l * 1.4142f; gr[i] = gain * rr * 1.4142f;
        }

        /// <summary>A hard impact at a frame of the current sub-block; pos 0..1 blends the two excitation inputs.
        /// The impulse is two frames wide (a zero at Nyquist): nothing real is infinitely sharp, and the output
        /// then never jumps by its full height within one sample.</summary>
        public void Hit(int frame, float amp, float pos)
        {
            if (frame < 0) frame = 0; else if (frame > Ctx.Sub - 2) frame = Ctx.Sub - 2;
            float a = 0.5f * amp * (1f - pos), b = 0.5f * amp * pos;
            excA[frame] += a; excB[frame] += b; excA[frame + 1] += a; excB[frame + 1] += b;
            hot = true; idle = 0;
        }

        /// <summary>A soft impact: a raised-cosine pulse of len frames instead of a single-sample impulse (the
        /// contact time of something heavy or wet), so that the onset is a thud and not a click.</summary>
        public void HitSoft(int frame, float amp, float pos, int len)
        {
            if (len < 2) { Hit(frame, amp, pos); return; }
            if (len > Ctx.Sub) len = Ctx.Sub;
            if (frame < 0) frame = 0;
            if (frame + len > Ctx.Sub) frame = Ctx.Sub - len;
            float a = amp * (1f - pos) / len, b = amp * pos / len, w = Dsp.TwoPi / len;
            for (int k = 0; k < len; k++)
            {
                float h = 1f - MathF.Cos(w * (k + 0.5f));
                excA[frame + k] += a * h; excB[frame + k] += b * h;
            }
            hot = true; idle = 0;
        }

        /// <summary>Marks the bank as excited after a voice wrote into excA/excB directly.</summary>
        public void Touch() { hot = true; idle = 0; }

        /// <summary>True while the bank is ringing or has been excited in this sub-block.</summary>
        public bool Hot { get { return hot; } }

        /// <summary>Adds the ringing of the bank to L/R and consumes the excitation of this sub-block.</summary>
        public void Render(float[] L, float[] R, int n, float gain)
        {
            if (!hot) return;
            float energy = 0f;
            for (int i = 0; i < count; i++)
            {
                float x = re[i], y = im[i], c = cr[i], s = sn[i], a = wa[i], b = wb[i];
                float l = gl[i] * gain, r = gr[i] * gain;
                for (int j = 0; j < n; j++)
                {
                    float nx = c * x - s * y + a * excA[j] + b * excB[j];
                    y = s * x + c * y; x = nx;
                    L[j] += l * y; R[j] += r * y;
                }
                re[i] = x; im[i] = y;
                energy += x * x + y * y;
            }
            Array.Clear(excA, 0, Ctx.Sub); Array.Clear(excB, 0, Ctx.Sub);
            if (energy < 1e-12f)
            {
                if (++idle > 8) { Reset(); }
            }
            else idle = 0;
        }

        public void Reset()
        {
            Array.Clear(re, 0, count); Array.Clear(im, 0, count);
            Array.Clear(excA, 0, Ctx.Sub); Array.Clear(excB, 0, Ctx.Sub);
            hot = false; idle = 0;
        }
    }

    /// <summary>Power-of-two delay line with linear interpolation. The delay is given as integer + fraction so that a
    /// very long line (the tornado: up to 20 s) keeps its sub-sample precision.</summary>
    public sealed class DelayLine
    {
        readonly float[] buf;
        readonly int mask;
        int w;
        bool dirty;

        public DelayLine(int minLength)
        {
            int n = 64;
            while (n < minLength) n <<= 1;
            buf = new float[n]; mask = n - 1;
        }

        public int Length { get { return buf.Length; } }

        public void Write(float x) { buf[w] = x; w = (w + 1) & mask; dirty = true; }

        /// <summary>Sample written (whole + frac) samples before the next write position; whole must be at least 1.</summary>
        public float Read(int whole, float frac)
        {
            float a = buf[(w - whole) & mask], b = buf[(w - whole - 1) & mask];
            return a + frac * (b - a);
        }

        public float Tap(int whole) { return buf[(w - whole) & mask]; }

        public void Clear()
        {
            if (!dirty) return;
            Array.Clear(buf, 0, buf.Length); dirty = false;
        }
    }

    /// <summary>Four-line feedback delay network (Hadamard): the open-air tail behind thunder, sirens and far
    /// impacts. Stereo in, stereo out, mono compatible; damped in the loop so the tail darkens as it dies.</summary>
    public sealed class Fdn
    {
        readonly float[] d0, d1, d2, d3;
        readonly int n0, n1, n2, n3;
        int i0, i1, i2, i3;
        readonly float g0, g1, g2, g3, damp;
        float l0, l1, l2, l3;
        bool hot;
        int idle;

        public Fdn(int sr, float t60, float dampHz)
        {
            float k = sr / 48000f;
            n0 = (int)(3089 * k); n1 = (int)(4127 * k); n2 = (int)(5231 * k); n3 = (int)(6449 * k);
            d0 = new float[n0]; d1 = new float[n1]; d2 = new float[n2]; d3 = new float[n3];
            g0 = MathF.Pow(10f, -3f * (n0 / (float)sr) / t60); g1 = MathF.Pow(10f, -3f * (n1 / (float)sr) / t60);
            g2 = MathF.Pow(10f, -3f * (n2 / (float)sr) / t60); g3 = MathF.Pow(10f, -3f * (n3 / (float)sr) / t60);
            damp = Dsp.Pole(dampHz, sr);
        }

        /// <summary>Feeds inL/inR, adds the wet signal times gain to outL/outR.</summary>
        public void Process(float[] inL, float[] inR, float[] outL, float[] outR, int n, float gain)
        {
            float inEnergy = 0f;
            for (int j = 0; j < n; j++) inEnergy += inL[j] * inL[j] + inR[j] * inR[j];
            if (inEnergy > 1e-14f) { hot = true; idle = 0; }
            if (!hot) return;
            float outEnergy = 0f;
            for (int j = 0; j < n; j++)
            {
                l0 += damp * (d0[i0] - l0); l1 += damp * (d1[i1] - l1);
                l2 += damp * (d2[i2] - l2); l3 += damp * (d3[i3] - l3);
                float a = l0 + l1, b = l0 - l1, c = l2 + l3, e = l2 - l3;
                float xl = inL[j], xr = inR[j];
                d0[i0] = xl + g0 * 0.5f * (a + c);
                d1[i1] = xr + g1 * 0.5f * (b + e);
                d2[i2] = 0.7f * xr + g2 * 0.5f * (a - c);
                d3[i3] = 0.7f * xl + g3 * 0.5f * (b - e);
                if (++i0 >= n0) i0 = 0;
                if (++i1 >= n1) i1 = 0;
                if (++i2 >= n2) i2 = 0;
                if (++i3 >= n3) i3 = 0;
                float wl = l0 + l2, wr = l1 + l3;
                outL[j] += gain * wl; outR[j] += gain * wr;
                outEnergy += wl * wl + wr * wr;
            }
            if (outEnergy < 1e-13f && inEnergy <= 1e-14f)
            {
                if (++idle > 400) Reset();      // about half a second of nothing: put the tail to sleep
            }
            else idle = 0;
        }

        public void Reset()
        {
            Array.Clear(d0, 0, n0); Array.Clear(d1, 0, n1); Array.Clear(d2, 0, n2); Array.Clear(d3, 0, n3);
            l0 = l1 = l2 = l3 = 0f; hot = false; idle = 0;
        }
    }
}
