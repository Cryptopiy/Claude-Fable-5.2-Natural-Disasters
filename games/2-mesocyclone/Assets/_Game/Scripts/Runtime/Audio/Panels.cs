using System;

namespace MESOCYCLONE.Audio
{
    /// <summary>The three sounding panels of the cab: roof, hood and windscreen. Every impact (rain, hail, drips, a
    /// rolling stone) excites these shared resonator banks, so hundreds of impacts per second cost nothing extra.
    /// Heavy stones go through a contact filter first: the bigger the stone, the longer the contact, the duller.</summary>
    public sealed class Panels
    {
        public readonly ModalBank roof = new ModalBank(16), hood = new ModalBank(8), glass = new ModalBank(9);
        public float Gain = 1f;                       // the hush before a glass crack (set by the hail voice)
        /// <summary>Direct sound that shares the panels' saturation (the bright ice-on-metal layer of the hail).</summary>
        public readonly float[] inL = new float[Ctx.Sub], inR = new float[Ctx.Sub];
        bool inHot;

        readonly int sr;
        readonly float[] rA = new float[Ctx.Sub], rB = new float[Ctx.Sub], hA = new float[Ctx.Sub], hB = new float[Ctx.Sub];
        readonly float[] tL = new float[Ctx.Sub], tR = new float[Ctx.Sub];
        float rA1, rA2, rB1, rB2, hA1, hA2, hB1, hB2;
        readonly float lowA;
        int lowHot, tailHot;
        Svf cavL, cavR;
        float gPrev;

        public Panels(int sampleRate)
        {
            sr = sampleRate;
            lowA = Dsp.Pole(700f, sr);
            Rng r = new Rng(); r.Seed(7001u);
            for (int i = 0; i < 16; i++)
            {   // roof skin with headliner: loss factor 0.05, boom modes from 85 Hz
                float f = 85f * MathF.Pow(1.33f, i) * (1f + 0.06f * r.Bi());
                float t60 = MathF.Max(0.006f, 2.2f / (0.05f * f));
                float g = MathF.Pow(f / 85f, 0.55f) * r.Range(0.5f, 1f);
                roof.Set(i, f, t60, g, 0.6f * r.Bi(), r.Range(0.3f, 1f) * ((i & 1) == 0 ? 1f : -1f), r.Range(0.3f, 1f), sr);
            }
            for (int i = 0; i < 8; i++)
            {   // hood: rings a little longer, heard through the firewall (highs lost)
                float f = 120f * MathF.Pow(1.5f, i) * (1f + 0.05f * r.Bi());
                float t60 = MathF.Max(0.01f, 2.2f / (0.035f * f));
                float g = MathF.Pow(120f / f, 0.3f) * r.Range(0.5f, 1f) / (1f + (f / 1200f) * (f / 1200f));
                hood.Set(i, f, t60, g, 0.35f * r.Bi(), r.Range(0.3f, 1f), r.Range(0.3f, 1f) * ((i & 1) == 0 ? -1f : 1f), sr);
            }
            for (int i = 0; i < 8; i++)
            {   // laminated glass: dead and bright
                float f = 900f * MathF.Pow(1.39f, i) * (1f + 0.05f * r.Bi());
                float t60 = MathF.Max(0.004f, 2.2f / (0.08f * f));
                glass.Set(i, f, t60, r.Range(0.4f, 0.9f), 0.3f * r.Bi(), r.Range(0.3f, 1f), r.Range(0.3f, 1f) * ((i & 1) == 0 ? 1f : -1f), sr);
            }
            glass.Set(8, 182f, 0.14f, 0.5f, 0f, 0.6f, 0.6f, sr);     // the body of the screen
            cavL.Set(108f, 2f, sr); cavR.Set(112f, 2f, sr);
        }

        public void Reset()
        {
            roof.Reset(); hood.Reset(); glass.Reset();
            Array.Clear(rA, 0, Ctx.Sub); Array.Clear(rB, 0, Ctx.Sub); Array.Clear(hA, 0, Ctx.Sub); Array.Clear(hB, 0, Ctx.Sub);
            rA1 = rA2 = rB1 = rB2 = hA1 = hA2 = hB1 = hB2 = 0f;
            lowHot = 0; tailHot = 0; cavL.Reset(); cavR.Reset(); gPrev = 0f; Gain = 1f;
            Array.Clear(inL, 0, Ctx.Sub); Array.Clear(inR, 0, Ctx.Sub); inHot = false;
        }

        /// <summary>A heavy stone on the roof (through the contact filter). pos 0..1 picks the spot.</summary>
        public void HeavyRoof(int frame, float amp, float pos)
        {
            if (frame < 0) frame = 0; else if (frame >= Ctx.Sub) frame = Ctx.Sub - 1;
            rA[frame] += amp * (1f - pos); rB[frame] += amp * pos; lowHot = 80;
        }

        public void HeavyHood(int frame, float amp, float pos)
        {
            if (frame < 0) frame = 0; else if (frame >= Ctx.Sub) frame = Ctx.Sub - 1;
            hA[frame] += amp * (1f - pos); hB[frame] += amp * pos; lowHot = 80;
        }

        /// <summary>Tells the panels that inL/inR were written in this sub-block.</summary>
        public void TouchInput() { inHot = true; }

        public void Render(Ctx c)
        {
            int n = c.n;
            if (lowHot > 0)
            {
                lowHot--;
                float a = lowA;
                float[] ea = roof.excA, eb = roof.excB, fa = hood.excA, fb = hood.excB;
                for (int j = 0; j < n; j++)
                {
                    rA1 += a * (rA[j] - rA1); rA2 += a * (rA1 - rA2); ea[j] += 0.8f * rA2;
                    rB1 += a * (rB[j] - rB1); rB2 += a * (rB1 - rB2); eb[j] += 0.8f * rB2;
                    hA1 += a * (hA[j] - hA1); hA2 += a * (hA1 - hA2); fa[j] += hA2;
                    hB1 += a * (hB[j] - hB1); hB2 += a * (hB1 - hB2); fb[j] += hB2;
                    rA[j] = 0f; rB[j] = 0f; hA[j] = 0f; hB[j] = 0f;
                }
                roof.Touch(); hood.Touch();
                if (lowHot == 0) { rA1 = rA2 = rB1 = rB2 = hA1 = hA2 = hB1 = hB2 = 0f; }
            }
            if (roof.Hot || hood.Hot || glass.Hot || inHot) tailHot = 200;
            if (tailHot <= 0) { gPrev = Gain * Mix.Hail; return; }
            tailHot--;

            if (inHot)
            {
                for (int j = 0; j < n; j++) { tL[j] = inL[j]; tR[j] = inR[j]; inL[j] = 0f; inR[j] = 0f; }
                inHot = false;
            }
            else { Array.Clear(tL, 0, n); Array.Clear(tR, 0, n); }
            roof.Render(tL, tR, n, 1f); hood.Render(tL, tR, n, 1f); glass.Render(tL, tR, n, 0.7f);

            // cab cavity (+3 dB near 110 Hz), then a soft saturation: density instead of level
            float target = Gain * Mix.Hail, g = gPrev, dg = (target - gPrev) / n;
            float kL = cavL.k, kR = cavR.k;
            float[] cl = c.cabL, cr = c.cabR;
            for (int j = 0; j < n; j++)
            {
                float l = tL[j] + Dsp.Tiny, r = tR[j] + Dsp.Tiny;
                l += 0.4f * kL * cavL.Bp(l); r += 0.4f * kR * cavR.Bp(r);
                g += dg;
                cl[j] += g * 1.6f * Dsp.Soft(l * 0.625f);
                cr[j] += g * 1.6f * Dsp.Soft(r * 0.625f);
            }
            gPrev = target;
            if (tailHot == 0) { cavL.Reset(); cavR.Reset(); }
        }
    }

    /// <summary>The three glass cracks: a dry tick (a 3 ms burst above 3 kHz), a short high modal ring, the thock of
    /// the screen's body, then a few creeping ticks while the crack runs. The second is the loudest.</summary>
    public sealed class CrackVoice
    {
        static readonly float[] Level = { 0.7f, 1f, 0.85f };
        static readonly float[] Span = { 0.35f, 0.5f, 0.9f };
        static readonly float[] PanOf = { -0.25f, 0.2f, 0f };
        static readonly int[] Creeps = { 5, 7, 10 };

        readonly int sr;
        readonly ModalBank ring = new ModalBank(5);
        readonly float[] tL = new float[Ctx.Sub], tR = new float[Ctx.Sub];
        Rng rng;
        Svf hp;
        float env, peak, gl = 0.7f, gr = 0.7f, creepTimer, creepAmp, creepGap;
        int rise, creepLeft, tailHot;
        readonly float decay;
        readonly int riseLen;
        bool pending;
        float pendingAmp;

        public CrackVoice(int sampleRate)
        {
            sr = sampleRate;
            decay = MathF.Exp(-1f / (0.0009f * sr));
            riseLen = Math.Max(4, (int)(0.0004f * sr));
            Rng r = new Rng(); r.Seed(7101u);
            float[] f = { 1900f, 3100f, 4450f, 6300f, 8400f };
            float[] t = { 0.07f, 0.11f, 0.09f, 0.06f, 0.04f };
            for (int i = 0; i < 5; i++)
                ring.Set(i, f[i] * (1f + 0.03f * r.Bi()), t[i], i == 0 ? 0.35f : 0.6f, 0.4f * r.Bi(), r.Range(0.4f, 1f), r.Range(0.4f, 1f) * ((i & 1) == 0 ? 1f : -1f), sr);
            hp.Set(3000f, 0.8f, sr);
            Reset();
        }

        public void Reset()
        {
            rng.Seed(7102u); ring.Reset(); hp.Reset();
            env = 0f; peak = 0f; rise = 0; creepLeft = 0; creepTimer = 0f; tailHot = 0; pending = false;
        }

        public void Start(int index)
        {
            int i = index < 1 ? 0 : (index > 3 ? 2 : index - 1);
            Dsp.Pan(PanOf[i], out gl, out gr);
            pending = true; pendingAmp = Level[i];
            creepLeft = Creeps[i]; creepGap = Span[i] / (Creeps[i] + 1); creepTimer = creepGap * rng.Range(0.5f, 1.2f);
            creepAmp = Level[i] * 0.25f;
        }

        void Tick(float amp, Panels p)
        {
            peak = amp; rise = riseLen; env = 0f;
            ring.Hit(0, amp * 0.5f, rng.Uni());
            p.glass.Hit(0, amp * 2.2f, rng.Uni());
            tailHot = 400;
        }

        public void Render(Ctx c, Panels p)
        {
            int n = c.n;
            if (pending) { pending = false; Tick(pendingAmp, p); if (c.shake < 0.6f) c.shake = 0.6f; }
            if (creepLeft > 0)
            {
                creepTimer -= c.dt;
                if (creepTimer <= 0f)
                {
                    creepLeft--;
                    creepTimer = creepGap * rng.Range(0.4f, 1.6f);
                    Tick(creepAmp * rng.Range(0.6f, 1.2f), p);
                }
            }
            if (tailHot <= 0) return;
            tailHot--;
            Array.Clear(tL, 0, n); Array.Clear(tR, 0, n);
            ring.Render(tL, tR, n, 1f);
            float[] w = c.w2, cl = c.cabL, cr = c.cabR;
            float g = Mix.Crack, up = peak / riseLen;
            for (int j = 0; j < n; j++)
            {
                if (rise > 0) { env += up; rise--; } else env *= decay;
                float tick = hp.Hp(w[j] * env) * 1.6f;
                cl[j] += g * (gl * tick + 0.5f * tL[j]);
                cr[j] += g * (gr * tick + 0.5f * tR[j]);
            }
            if (env < 1e-9f) env = 0f;
        }
    }
}
