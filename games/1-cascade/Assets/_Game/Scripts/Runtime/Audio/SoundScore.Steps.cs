using System;

namespace CASCADE
{
    /// <summary>
    /// Footsteps: one per increment of the clock's footstep counter. Heel and toe, three noise components per step
    /// (low thump, mid click, gritty top), slightly different every time. Ash slowly turns every surface into a soft thud.
    /// "Silence is never zero": nothing in the score ducks this layer.
    /// </summary>
    public sealed partial class SoundScore
    {
        const float GSteps = 0.062f;
        const int StepCols = 12;
        // lpHz thAmp thTauMs | bpHz bpQ clAmp clTauMs | hpHz grAmp grTauMs grainsPerSec | slapback
        static readonly float[] StepTable =
        {
            240f, 1.00f, 22f,  2300f, 2.5f, 0.50f,  5f,  3000f, 0.14f, 35f,  0f, 0.00f,     // 0 quay stone
            190f, 1.25f, 28f,  1900f, 2.5f, 0.55f,  6f,  3000f, 0.09f, 30f,  0f, 0.30f,     // 1 stairs between walls
            280f, 0.85f, 18f,  2800f, 2.8f, 0.42f,  4f,  3500f, 0.06f, 25f,  0f, 0.00f,     // 2 promenade paving
            200f, 0.60f, 25f,  3200f, 1.5f, 0.12f,  6f,  1600f, 0.60f, 95f, 70f, 0.00f,     // 3 gravel
            150f, 0.75f, 45f,   650f, 0.8f, 0.30f, 60f,  2500f, 0.05f, 80f, 40f, 0.00f,     // 4 ash
        };

        struct StepVoice
        {
            public bool on, toeDone;
            public float t, toeAt, toeScale, atk;
            public float eTh, eCl, eGr, dTh, dCl, dGr, aTh, aCl, aGr;
            public float grainP, grainE, grainD, gl, gr, slap;
            public Svf lp, bp, hp; public Rng rng;
        }
        readonly StepVoice[] step = new StepVoice[3];
        int stepNext, lastStep, stepIdle;
        float ashMix;
        DelayLine slapLine; int slapLen;
        Ramp gSteps;

        void InitSteps()
        {
            slapLen = (int)(0.031f * fs);
            slapLine = new DelayLine(slapLen + 8);
        }

        void ResetSteps()
        {
            for (int i = 0; i < step.Length; i++) step[i] = default(StepVoice);
            stepNext = 0; lastStep = 0; stepIdle = 1 << 20; ashMix = 0f;
            slapLine.Clear(); gSteps.Snap(0f);
        }

        void KillStepVoices() { for (int i = 0; i < step.Length; i++) step[i].on = false; }

        void CtlSteps(ref ScoreClock c)
        {
            // a dusting of ash softens the steps from the ashfall on; 20 s into the storm everything is ash
            ashMix = Math.Max(0.3f * Dsp.Smooth(95f, 200f, eEr), Dsp.Smooth(6f, 22f, eStorm));
            gSteps.To(GSteps * Trim[LSteps], Block);
            if (seekPending) lastStep = c.footstepCount;
            if (c.footstepCount != lastStep)
            {
                lastStep = c.footstepCount;
                if (advancing) TriggerStep(c.footSurface, c.footstepCount, c.speed);
            }
        }

        static float StepParam(int surface, int col, float ash)
        {
            float a = StepTable[surface * StepCols + col], b = StepTable[4 * StepCols + col];
            return a + (b - a) * ash;
        }

        void TriggerStep(int surface, int index, float speed)
        {
            if (surface < 0) surface = 0; else if (surface > 3) surface = 3;
            float ash = ashMix;
            ref StepVoice v = ref step[stepNext];
            stepNext = (stepNext + 1) % step.Length;
            float h1 = Dsp.Hash(index, 601), h2 = Dsp.Hash(index, 602), h3 = Dsp.Hash(index, 603), h4 = Dsp.Hash(index, 604);
            float effort = 0.62f + 0.38f * Dsp.Sat(Math.Abs(speed) / Config.BriskSpeed);
            float vary = 0.85f + 0.3f * h1;
            v.on = true; v.toeDone = false; v.t = 0f; v.atk = 0f;
            v.toeAt = Dsp.Clamp(0.16f / Math.Max(1f, Math.Abs(speed)), 0.05f, 0.12f) * (0.9f + 0.2f * h2);
            v.toeScale = 0.45f + 0.3f * h3;
            v.aTh = StepParam(surface, 1, ash) * effort * vary;
            v.aCl = StepParam(surface, 5, ash) * effort * (0.8f + 0.4f * h2);
            v.aGr = StepParam(surface, 8, ash) * effort * (0.7f + 0.6f * h4);
            v.eTh = v.aTh; v.eCl = v.aCl; v.eGr = v.aGr;
            v.dTh = Dsp.Exp(-1000f * invFs / StepParam(surface, 2, ash));
            v.dCl = Dsp.Exp(-1000f * invFs / StepParam(surface, 6, ash));
            v.dGr = Dsp.Exp(-1000f * invFs / StepParam(surface, 9, ash));
            v.lp.Clear(); v.bp.Clear(); v.hp.Clear();
            v.lp.Set(fs, StepParam(surface, 0, ash) * (0.9f + 0.2f * h3), 0.7f);
            v.bp.Set(fs, StepParam(surface, 3, ash) * (0.88f + 0.24f * h4), StepParam(surface, 4, ash));
            v.hp.Set(fs, StepParam(surface, 7, ash) * (0.9f + 0.2f * h1), 0.7f);
            float grains = StepParam(surface, 10, ash);
            v.grainP = grains > 1f ? grains * invFs : 0f;
            v.grainE = 0.6f; v.grainD = Dsp.Exp(-invFs / 0.004f);
            Dsp.Pan(((index & 1) == 0 ? -0.14f : 0.14f) + 0.06f * (h2 - 0.5f), out v.gl, out v.gr);
            v.slap = StepParam(surface, 11, ash);
            v.rng = new Rng((uint)(index * 7919 + 13));
            stepIdle = 0;
        }

        void RunStep(ref StepVoice v, int n)
        {
            for (int s = 0; s < n; s++)
            {
                v.t += invFs;
                if (!v.toeDone && v.t >= v.toeAt)
                {
                    v.toeDone = true;
                    v.eTh += v.aTh * v.toeScale * 0.45f; v.eCl += v.aCl * v.toeScale; v.eGr += v.aGr * v.toeScale;
                }
                v.atk += (1f - v.atk) * 0.02f;                       // about 1 ms: no hard edge
                float w = v.rng.Bi(), gm = 1f;
                if (v.grainP > 0f)
                {
                    if (v.rng.Uni() < v.grainP) v.grainE = 0.35f + 0.65f * v.rng.Uni();
                    v.grainE *= v.grainD; gm = v.grainE;
                }
                v.hp.Tick(w);
                float y = (v.lp.Lp(w) * v.eTh * 8f + v.bp.Bp(w) * v.eCl * 4f + v.hp.high * v.eGr * gm) * v.atk;
                v.eTh *= v.dTh; v.eCl *= v.dCl; v.eGr *= v.dGr;
                tA[s] += y * v.gl; tB[s] += y * v.gr; tC[s] += y * v.slap;
            }
            if (v.t > 0.6f || (v.toeDone && v.eTh + v.eCl + v.eGr < 2e-4f)) v.on = false;
        }

        void RunSteps(int n)
        {
            bool any = false;
            for (int i = 0; i < step.Length; i++) any |= step[i].on;
            if (!any)
            {
                gSteps.Snap(gSteps.v + gSteps.d * n);
                if (stepIdle > 4096) return;                         // the slapback has left the line
                stepIdle += n;
            }
            for (int s = 0; s < n; s++) { tA[s] = 0f; tB[s] = 0f; tC[s] = 0f; }
            for (int i = 0; i < step.Length; i++) if (step[i].on) RunStep(ref step[i], n);
            for (int s = 0; s < n; s++)
            {
                float g = any ? gSteps.Next() : gSteps.v;
                float d = slapLine.Read(slapLen);
                slapLine.Write(tC[s] * g);
                nearL[s] += tA[s] * g + d * 0.7f; nearR[s] += tB[s] * g + d;
            }
        }
    }
}
