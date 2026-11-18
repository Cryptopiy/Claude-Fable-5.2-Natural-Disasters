using System;

namespace MESOCYCLONE.Audio
{
    /// <summary>The summer-evening chorus in three sections. A: the far bed (thousands of crickets, a meadow buzz, a
    /// ground trill) that collapses in three steps. B: individuals (tree crickets, katydids) that each finish
    /// their chirp and then stay silent. C: one lone field cricket, the last to go, cut off in the middle of a chirp.
    /// They come back one by one in beat 8. No fades: insects stop, they do not fade.</summary>
    public sealed class InsectVoice
    {
        const int Tree = 4, Katy = 2;
        readonly int sr;
        Rng rng;

        // section A
        Svf bedL, bedR, buzzL, buzzR;
        Osc bedAmL, bedAmR, buzzAm, trill, trillAm;
        float bedG, buzzG, trillG, aPrevBed, aPrevBuzz, aPrevTrill;
        Wander swell;

        // section B: tree crickets
        readonly Osc[] trOsc = new Osc[Tree];
        readonly float[] trEnv = new float[Tree], trChirp = new float[Tree], trPulse = new float[Tree], trPeriod = new float[Tree];
        readonly float[] trPass = new float[Tree], trPassRate = new float[Tree], trSide = new float[Tree], trF = new float[Tree];
        readonly int[] trLeft = new int[Tree], trAtt = new int[Tree];
        // section B: katydids
        readonly Svf[] kaBp = new Svf[Katy];
        readonly Osc[] kaAm = new Osc[Katy];
        readonly float[] kaEnv = new float[Katy], kaTarget = new float[Katy], kaPhrase = new float[Katy], kaPulse = new float[Katy], kaHold = new float[Katy];
        readonly int[] kaLeft = new int[Katy];

        // section C
        Osc cOsc;
        float cEnv, cChirp, cPulse, cGate, cGatePrev;
        int cLeft, cAtt;
        bool lastChirpDone;

        readonly float attInc, trDec, cDec, kaK;
        readonly int attLen;

        static readonly float[] TreeStop = { 0.8f, 0.62f, 0.44f, 0.26f }, TreeBack = { 0.05f, 0.14f, 0.23f, 0.32f };
        static readonly float[] KatyStop = { 0.7f, 0.1f }, KatyBack = { 0.2f, 0.4f };

        public InsectVoice(int sampleRate)
        {
            sr = sampleRate;
            attLen = Math.Max(8, (int)(0.003f * sr)); attInc = 1f / attLen;
            trDec = MathF.Exp(-1f / (0.0042f * sr)); cDec = MathF.Exp(-1f / (0.0065f * sr));
            kaK = 1f - MathF.Exp(-1f / (0.006f * sr));
            Reset();
        }

        public void Reset()
        {
            rng.Seed(9001u);
            bedL.Reset(); bedR.Reset(); buzzL.Reset(); buzzR.Reset();
            bedAmL.Init(0f); bedAmR.Init(0.37f); buzzAm.Init(0f); trill.Init(0f); trillAm.Init(0.2f);
            bedG = buzzG = trillG = 0f; aPrevBed = aPrevBuzz = aPrevTrill = 0f; swell.Reset();
            for (int i = 0; i < Tree; i++)
            {
                trOsc[i].Init(0f); trEnv[i] = 0f; trLeft[i] = 0; trAtt[i] = 0;
                trPeriod[i] = rng.Range(0.34f, 0.41f); trChirp[i] = trPeriod[i] * rng.Uni(); trPulse[i] = 0f;
                trPass[i] = rng.Uni(); trPassRate[i] = rng.Range(0.2f, 0.34f); trSide[i] = (i & 1) == 0 ? -1f : 1f;
                trF[i] = rng.Range(2700f, 3100f);
            }
            for (int i = 0; i < Katy; i++)
            {
                kaBp[i].Reset(); kaAm[i].Init(0.3f * i); kaEnv[i] = 0f; kaTarget[i] = 0f; kaLeft[i] = 0;
                kaPhrase[i] = 0.3f + 0.5f * i; kaPulse[i] = 0f; kaHold[i] = 0f;
            }
            cOsc.Init(0f); cEnv = 0f; cChirp = 0.2f; cPulse = 0f; cLeft = 0; cAtt = 0; cGate = 0f; cGatePrev = 0f;
            lastChirpDone = false;
        }

        public void Render(Ctx c)
        {
            float a = c.raw.insectA, b = c.raw.insectB, cc = c.raw.insectC;
            bool early = c.T < 300.0;
            if (a > 0.001f || bedG + buzzG + trillG > 1e-4f) SectionA(c, a, early);
            if (b > 0.001f || Busy()) SectionB(c, b, early);
            if (cc > 0.001f || cGate > 1e-4f || cEnv > 1e-5f) SectionC(c, cc, early);
        }

        bool Busy()
        {
            for (int i = 0; i < Tree; i++) if (trLeft[i] > 0 || trEnv[i] > 1e-5f) return true;
            for (int i = 0; i < Katy; i++) if (kaLeft[i] > 0 || kaEnv[i] > 1e-5f) return true;
            return false;
        }

        void SectionA(Ctx c, float level, bool early)
        {
            int n = c.n;
            float k = Dsp.Ease(0.02f, c.dt);
            bedG += ((level > (early ? 0.85f : 0.38f) ? 1f : 0f) - bedG) * k;
            buzzG += ((level > (early ? 0.5f : 0.22f) ? 1f : 0f) - buzzG) * k;
            trillG += ((level > (early ? 0.15f : 0.08f) ? 1f : 0f) - trillG) * k;
            float sw = swell.Tick(ref c.rng, 0.25f, 2.5f, c.dt);
            float amp = Mix.Insects * Dsp.Clamp01(level / 0.6f + 0.25f);
            float gBed = bedG * amp * 1.5f * (1f + 0.2f * sw), gBuzz = buzzG * amp * 0.8f * (1f - 0.25f * sw), gTrill = trillG * amp * 0.22f;
            bedL.Set(4550f, 9f, sr); bedR.Set(4720f, 9f, sr); buzzL.Set(7400f, 2.5f, sr); buzzR.Set(7900f, 2.5f, sr);
            bedAmL.SetFreq(29f, sr); bedAmR.SetFreq(31.5f, sr); buzzAm.SetFreq(104f, sr); trill.SetFreq(3440f, sr); trillAm.SetFreq(43f, sr);
            bedAmL.Norm(); bedAmR.Norm(); buzzAm.Norm(); trill.Norm(); trillAm.Norm();
            float g1 = aPrevBed, d1 = (gBed - aPrevBed) / n, g2 = aPrevBuzz, d2 = (gBuzz - aPrevBuzz) / n, g3 = aPrevTrill, d3 = (gTrill - aPrevTrill) / n;
            float[] ol = c.openL, or_ = c.openR, w0 = c.w0, w1 = c.w1, w2 = c.w2, w3 = c.w3;
            for (int j = 0; j < n; j++)
            {
                g1 += d1; g2 += d2; g3 += d3;
                float bz = 0.5f + 0.5f * buzzAm.Tick(); bz *= bz;
                float tr = trill.Tick() * (0.55f + 0.45f * trillAm.Tick()) * g3;
                ol[j] += g1 * bedL.Bp(w2[j]) * (0.6f + 0.4f * bedAmL.Tick()) + g2 * bz * buzzL.Bp(w0[j]) + 0.8f * tr;
                or_[j] += g1 * bedR.Bp(w3[j]) * (0.6f + 0.4f * bedAmR.Tick()) + g2 * bz * buzzR.Bp(w1[j]) + 0.5f * tr;
            }
            aPrevBed = gBed; aPrevBuzz = gBuzz; aPrevTrill = gTrill;
        }

        void SectionB(Ctx c, float level, bool early)
        {
            int n = c.n; float dt = c.dt;
            float amp = Mix.Insects * 2f * (0.6f + 0.4f * Dsp.Clamp01(level / 0.5f));
            float move = Dsp.Clamp01(c.s.speed / 24f);
            float[] ol = c.openL, or_ = c.openR;
            for (int i = 0; i < Tree; i++)
            {
                // the truck passes them: each voice swells and falls, and a new individual takes its place
                trPass[i] += dt * trPassRate[i] * move;
                if (trPass[i] >= 1f) { trPass[i] -= 1f; trF[i] = rng.Range(2700f, 3100f); trPeriod[i] = rng.Range(0.34f, 0.41f); }
                float near = 0.5f - 0.5f * MathF.Cos(Dsp.TwoPi * trPass[i]);
                float gain = amp * (0.25f + 0.75f * near * near);
                float gl, gr; Dsp.Pan(trSide[i] * (0.25f + 0.65f * trPass[i]), out gl, out gr);
                trChirp[i] -= dt;
                if (trChirp[i] <= 0f)
                {
                    trChirp[i] += trPeriod[i] * rng.Range(0.95f, 1.05f);
                    if (level > (early ? TreeStop[i] : TreeBack[i])) { trLeft[i] = 8; trPulse[i] = 0f; }
                }
                if (trLeft[i] > 0)
                {
                    trPulse[i] -= dt;
                    if (trPulse[i] <= 0f) { trPulse[i] += 0.02f; trLeft[i]--; trAtt[i] = attLen; }
                }
                if (trEnv[i] < 1e-5f && trAtt[i] == 0) continue;
                trOsc[i].SetFreq(trF[i], sr); trOsc[i].Norm();
                float e = trEnv[i]; int att = trAtt[i];
                gl *= gain; gr *= gain;
                for (int j = 0; j < n; j++)
                {
                    if (att > 0) { e += attInc * (1f - e) * 3f; att--; } else e *= trDec;
                    float s = trOsc[i].Tick() * e;
                    ol[j] += gl * s; or_[j] += gr * s;
                }
                trEnv[i] = e; trAtt[i] = att;
            }
            for (int i = 0; i < Katy; i++)
            {
                // "ka-ty-did": three raspy pulses of 45 ms, 130 ms apart, one phrase per second, two alternating
                kaPhrase[i] -= dt;
                if (kaPhrase[i] <= 0f)
                {
                    kaPhrase[i] += rng.Range(0.95f, 1.1f);
                    if (level > (early ? KatyStop[i] : KatyBack[i])) { kaLeft[i] = rng.Uni() < 0.3f ? 2 : 3; kaPulse[i] = 0f; }
                }
                if (kaLeft[i] > 0)
                {
                    kaPulse[i] -= dt;
                    if (kaPulse[i] <= 0f) { kaPulse[i] += 0.13f; kaLeft[i]--; kaHold[i] = 0.04f; }
                }
                kaHold[i] -= dt;
                kaTarget[i] = kaHold[i] > 0f ? 1f : 0f;
                if (kaEnv[i] < 1e-5f && kaTarget[i] <= 0f) continue;
                kaBp[i].Set(i == 0 ? 3900f : 4300f, 3f, sr); kaAm[i].SetFreq(i == 0 ? 62f : 69f, sr); kaAm[i].Norm();
                float gl, gr; Dsp.Pan(i == 0 ? -0.55f : 0.6f, out gl, out gr);
                float e = kaEnv[i], tg = kaTarget[i], g = amp * 1.3f;
                float[] w = i == 0 ? c.w1 : c.w2;
                gl *= g; gr *= g;
                for (int j = 0; j < n; j++)
                {
                    e += (tg - e) * kaK;
                    float am = kaAm[i].Tick(); if (am < 0f) am = -am;
                    float s = kaBp[i].Bp(w[j]) * e * am;
                    ol[j] += gl * s; or_[j] += gr * s;
                }
                kaEnv[i] = e;
            }
        }

        void SectionC(Ctx c, float level, bool early)
        {
            int n = c.n; float dt = c.dt, t = (float)c.T;
            // slows down before it stops (beat 2); hesitant when it returns (beat 8)
            float slow = early ? 1f + 1.3f * Dsp.Step(150f, 164f, t) : 1f + 0.8f * (1f - Dsp.Step(640f, 652f, t));
            bool open = level > (early ? 0.35f : 0.03f);
            cGate += ((open ? 1f : 0f) - cGate) * Dsp.Ease(0.002f, dt);
            cChirp -= dt;
            // the last chirp: started so that the cut at 165 lands in its middle
            if (early && !lastChirpDone && c.T <= 164.83 && c.Tnext > 164.83) { lastChirpDone = true; cChirp = 0f; }
            if (cChirp <= 0f)
            {
                cChirp = 0.45f * slow * rng.Range(0.93f, 1.07f);
                if (open) { cLeft = rng.Uni() < 0.4f ? 3 : 4; cPulse = 0f; }
            }
            if (cLeft > 0)
            {
                cPulse -= dt;
                if (cPulse <= 0f) { cPulse += 0.036f; cLeft--; cAtt = attLen; }
            }
            if (cEnv < 1e-5f && cAtt == 0 && cGate < 1e-4f) { cGatePrev = cGate; return; }
            cOsc.SetFreq(4700f, sr); cOsc.Norm();
            float gain = Mix.Insects * 4.5f * Dsp.Clamp01(0.35f + level);
            float g = cGatePrev * gain, dg = (cGate * gain - g) / n, e = cEnv; int att = cAtt;
            float[] ol = c.openL, or_ = c.openR;
            for (int j = 0; j < n; j++)
            {
                if (att > 0) { e += attInc * (1f - e) * 3f; att--; } else e *= cDec;
                g += dg;
                float s = cOsc.Tick() * e * g;
                ol[j] += 0.55f * s; or_[j] += 0.83f * s;
            }
            cEnv = e; cAtt = att; cGatePrev = cGate;
        }
    }
}
