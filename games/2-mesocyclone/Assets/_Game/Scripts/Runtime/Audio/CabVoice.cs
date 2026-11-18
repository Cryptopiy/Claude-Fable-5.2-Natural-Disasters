using System;

namespace MESOCYCLONE.Audio
{
    /// <summary>The cab's own small sounds, kept crisp: a faint rattle (bouncing parts: bursts of clicks whose gaps
    /// shrink), the stick-slip creak of the suspension, the ticking of cooling metal after the engine stops, the
    /// small sounds that return after the hail (a stone rolling off the roof, ticking metal) and the dripping.</summary>
    public sealed class CabVoice
    {
        const int Sites = 5, Blips = 3;
        static readonly float[] ScriptT = { 348.4f, 351.2f, 352.6f, 354.3f, 355.1f, 357.4f, 360.2f };
        readonly int sr;
        Rng rng;
        readonly ModalBank rattle = new ModalBank(6);    // input A: keys, input B: glovebox latch
        readonly ModalBank ticks = new ModalBank(6);     // input A: ticking metal, input B: mirror mount
        readonly ModalBank plink = new ModalBank(6);     // water on sheet metal: input A the roof gutter, input B the sill
        int clicksLeft, clickObj;
        float clickTimer, clickGap, clickAmp;
        float crT = -1f, crDur = 0.3f, crF = 120f, crPhase, crAmp, crPrev;
        Svf crBp;
        float coolTimer = -1f, coolGap = 0.45f, coolAmp = 0.7f;
        float rollT = -1f, rollTap, rollGap;
        float prevSpeed;
        readonly float[] dripTimer = new float[Sites], dripGap = new float[Sites], dripPan = new float[Sites];
        readonly int[] dripKind = new int[Sites];
        readonly Osc[] blOsc = new Osc[Blips];
        readonly float[] blEnv = new float[Blips], blF = new float[Blips], blL = new float[Blips], blR = new float[Blips], blAge = new float[Blips], blClick = new float[Blips];
        int blNext;
        readonly float blDec, clickDec;

        public CabVoice(int sampleRate)
        {
            sr = sampleRate;
            float[] f = { 2400f, 3350f, 4100f, 1250f, 1800f, 2700f };
            float[] t = { 0.030f, 0.022f, 0.018f, 0.040f, 0.030f, 0.020f };
            for (int i = 0; i < 6; i++) rattle.Set(i, f[i], t[i], 0.5f, i < 3 ? 0.35f : -0.2f, i < 3 ? 1f : 0f, i < 3 ? 0f : 1f, sr);
            float[] f2 = { 3200f, 4300f, 5100f, 1500f, 2100f, 3600f };
            float[] t2 = { 0.028f, 0.022f, 0.015f, 0.035f, 0.025f, 0.018f };
            for (int i = 0; i < 6; i++) ticks.Set(i, f2[i], t2[i], 0.5f, i < 3 ? -0.1f : 0.05f, i < 3 ? 1f : 0f, i < 3 ? 0f : 1f, sr);
            float[] f3 = { 640f, 1130f, 1720f, 880f, 1490f, 2350f };
            float[] t3 = { 0.050f, 0.035f, 0.022f, 0.045f, 0.030f, 0.018f };
            for (int i = 0; i < 6; i++) plink.Set(i, f3[i], t3[i], i % 3 == 0 ? 0.6f : 0.35f, i < 3 ? -0.3f : 0.4f, i < 3 ? 1f : 0f, i < 3 ? 0f : 1f, sr);
            blDec = MathF.Exp(-1f / (0.015f * sr)); clickDec = MathF.Exp(-1f / (0.0006f * sr));
            Reset();
        }

        public void Reset()
        {
            rng.Seed(8001u); rattle.Reset(); ticks.Reset(); plink.Reset(); crBp.Reset();
            clicksLeft = 0; crT = -1f; crPrev = 0f; coolTimer = -1f; coolGap = 0.45f; coolAmp = 0.7f; rollT = -1f; prevSpeed = 0f;
            for (int i = 0; i < Sites; i++)
            {
                dripGap[i] = rng.Range(0.5f, 2.3f); dripTimer[i] = dripGap[i] * rng.Uni();
                dripPan[i] = rng.Range(-0.8f, 0.8f); dripKind[i] = i % 4;
            }
            for (int i = 0; i < Blips; i++) { blEnv[i] = 0f; blClick[i] = 0f; blOsc[i].Init(0f); }
            blNext = 0;
        }

        void Rattle(float intensity)
        {
            if (clicksLeft > 0) return;
            clicksLeft = 3 + (int)(rng.Uni() * 9f * intensity);
            clickGap = rng.Range(0.025f, 0.05f); clickTimer = 0f;
            clickAmp = (0.25f + 0.75f * intensity) * rng.Range(0.6f, 1f); clickObj = (int)(rng.Uni() * 3f);
        }

        void Creak(float amp, float low)
        {
            if (crT >= 0f) return;
            crT = 0f; crDur = rng.Range(0.15f, 0.4f) * (1f + low); crF = rng.Range(70f, 190f) / (1f + low);
            crAmp = amp; crBp.Set(rng.Range(320f, 880f) / (1f + 0.5f * low), 5f, sr);
        }

        public void Render(Ctx c, Panels p)
        {
            int n = c.n; float dt = c.dt, t = (float)c.T, shake = c.shake;
            float[] cl = c.cabL, cr = c.cabR;

            // ---------- triggers ----------
            if (rng.Uni() < dt * MathF.Min(4f, 0.05f + 18f * shake * shake)) Rattle(Dsp.Clamp01(0.15f + shake));
            if (rng.Uni() < dt * MathF.Min(1.5f, 0.03f + 5f * shake * shake)) Creak(0.4f + 0.6f * Dsp.Clamp01(shake), 0f);
            float speed = c.s.speed;
            if (prevSpeed >= 0.6f && speed < 0.6f) { Creak(1f, 0.8f); Rattle(0.3f); }     // the truck settles on its springs
            prevSpeed = speed;

            // ---------- rattle: a bouncing part ----------
            if (clicksLeft > 0)
            {
                clickTimer -= dt;
                if (clickTimer <= 0f)
                {
                    float a = clickAmp * rng.Range(0.6f, 1f);
                    if (clickObj == 0) rattle.Hit(0, a, 0f); else if (clickObj == 1) rattle.Hit(0, a, 1f); else ticks.Hit(0, a, 1f);
                    clicksLeft--; clickTimer = clickGap; clickGap *= 0.72f; clickAmp *= 0.86f;
                }
            }

            // ---------- after the hail: a stone rolls off the roof, then metal ticks ----------
            if (c.storyDt > 0f)
            {
                for (int i = 0; i < ScriptT.Length; i++)
                {
                    if (ScriptT[i] >= c.T && ScriptT[i] < c.Tnext)
                    {
                        if (i == 0) { rollT = 0f; rollTap = 0f; rollGap = 0.13f; }
                        else ticks.Hit(0, rng.Range(0.5f, 1f), 0f);
                    }
                }
            }
            if (rollT >= 0f)
            {
                rollT += dt; rollTap -= dt;
                if (rollT < 1f)
                {
                    if (rollTap <= 0f)
                    {
                        p.roof.Hit(0, 0.03f * (0.5f + rollT), 0.2f + 0.7f * rollT);
                        rollGap = rollGap * 0.86f + 0.004f; rollTap = rollGap * rng.Range(0.8f, 1.2f);
                    }
                }
                else if (rollT > 1.3f) { p.HeavyHood(0, 0.25f, 0.9f); rollT = -1f; }
            }

            // ---------- cooling metal after the engine has stopped ----------
            if (c.s.engine < 0.02f && t > Config.EngineOffTime)
            {
                if (coolTimer < 0f) { coolTimer = 0.6f; coolGap = 0.45f; coolAmp = 0.7f; }
                coolTimer -= dt;
                if (coolTimer <= 0f)
                {
                    ticks.Hit(0, coolAmp * rng.Range(0.5f, 1f), 0f);
                    coolGap *= 1.13f; coolAmp *= 0.95f; coolTimer = coolGap * rng.Range(0.6f, 1.5f);
                }
            }
            else coolTimer = -1f;

            // ---------- dripping ----------
            float drips = c.s.drips;
            if (drips > 0.01f)
            {
                bool still = speed < 1f;
                for (int i = 0; i < Sites; i++)
                {
                    dripTimer[i] -= dt * (0.12f + 0.88f * drips);
                    if (dripTimer[i] > 0f) continue;
                    dripTimer[i] = dripGap[i] * rng.Range(0.75f, 1.3f);
                    float a = rng.Range(0.5f, 1f);
                    int kind = dripKind[i];
                    if (kind == 2 && !still) kind = 1;
                    if (kind == 0) plink.Hit(0, a, 0f);
                    else if (kind == 1) { plink.Hit(0, 0.8f * a, 1f); p.HeavyHood(0, 0.15f * a, 0.5f + 0.5f * dripPan[i]); }
                    else if (kind == 3) plink.Hit(0, 0.6f * a, rng.Uni());
                    else
                    {   // a drop into a puddle: a short rising blip (a ringing bubble) with a tiny click
                        int v = blNext; blNext = (blNext + 1) % Blips;
                        blF[v] = rng.Range(1100f, 2200f); blEnv[v] = 0.5f * a; blAge[v] = 0f; blClick[v] = 0.2f * a;
                        blOsc[v].Init(0f); Dsp.Pan(dripPan[i], out blL[v], out blR[v]);
                    }
                }
            }
            float dripGain = Mix.Drips;
            for (int v = 0; v < Blips; v++)
            {
                if (blEnv[v] < 1e-5f) continue;
                blAge[v] += dt;
                blOsc[v].SetFreq(blF[v] * (1f + 9f * blAge[v]), sr); blOsc[v].Norm();
                float e = blEnv[v], ck = blClick[v], l = blL[v] * dripGain, r = blR[v] * dripGain;
                float[] w3 = c.w3;
                for (int j = 0; j < n; j++)
                {
                    float s = blOsc[v].Tick() * e + w3[j] * ck;
                    e *= blDec; ck *= clickDec;
                    cl[j] += l * s; cr[j] += r * s;
                }
                blEnv[v] = e; blClick[v] = ck;
            }

            // ---------- creak: stick-slip ----------
            if (crT >= 0f)
            {
                crT += dt;
                float u = crT / crDur;
                float env = Dsp.Step(0f, 0.12f, u) * (1f - Dsp.Step(0.7f, 1f, u)) * crAmp * Mix.Cab * 0.75f;
                float f = crF * (1f - 0.3f * u) * (1f + 0.04f * rng.Bi());
                float inc = f * c.invSr, g = crPrev, dg = (env - crPrev) / n, k = crBp.k;
                for (int j = 0; j < n; j++)
                {
                    crPhase += inc; if (crPhase >= 1f) crPhase -= 1f;
                    g += dg;
                    float s = g * k * crBp.Bp(2f * crPhase - 1f);
                    cl[j] += 0.8f * s; cr[j] += 0.6f * s;
                }
                crPrev = env;
                if (u >= 1f) { crT = -1f; crPrev = 0f; crBp.Reset(); }
            }

            plink.Render(cl, cr, n, Mix.Drips * 0.6f);
            rattle.Render(cl, cr, n, Mix.Cab * 0.7f);
            ticks.Render(cl, cr, n, Mix.Cab * 0.7f);
        }
    }
}
