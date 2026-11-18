using System;

namespace MESOCYCLONE.Audio
{
    /// <summary>Rain: the hiss outside (muffled by the cab), the patter of drops on the windscreen (crisp, random
    /// left/right) and the dull drumming on the roof through the shared roof resonator. Gusts lash it.</summary>
    public sealed class RainVoice
    {
        readonly int sr;
        Rng rng;
        Svf hissL, hissR, patL, patR;
        float hzL, hzR, hissPrev, tickL, tickR;
        readonly float tickDec, hpA;

        public RainVoice(int sampleRate)
        {
            sr = sampleRate;
            tickDec = MathF.Exp(-1f / (0.0009f * sr)); hpA = Dsp.Pole(1500f, sr);
            hissL.Set(7500f, 0.6f, sr); hissR.Set(8200f, 0.6f, sr); patL.Set(3100f, 0.9f, sr); patR.Set(3600f, 0.9f, sr);
            Reset();
        }

        public void Reset()
        {
            rng.Seed(12001u); hissL.Reset(); hissR.Reset(); patL.Reset(); patR.Reset();
            hzL = hzR = 0f; hissPrev = 0f; tickL = tickR = 0f;
        }

        public void Render(Ctx c, Panels p)
        {
            int n = c.n;
            float rain = c.s.rain * (1f + 0.45f * c.s.gust * c.gustMod);
            if (rain < 0f) rain = 0f;
            float v01 = Dsp.Clamp01(c.s.speed / 24f);

            // ---------- hiss outside ----------
            float hiss = Mix.Rain * 0.9f * MathF.Sqrt(rain) * c.bedDuck;
            if (hiss > 1e-6f || hissPrev > 1e-6f)
            {
                float g = hissPrev, dg = (hiss - hissPrev) / n, a = hpA;
                float[] pl = c.pinkL, pr = c.pinkR, wl = c.worldL, wr = c.worldR;
                for (int j = 0; j < n; j++)
                {
                    float xl = pl[j], xr = pr[j];
                    hzL += a * (xl - hzL); hzR += a * (xr - hzR);
                    g += dg;
                    wl[j] += g * 3f * hissL.Lp(xl - hzL); wr[j] += g * 3f * hissR.Lp(xr - hzR);
                }
            }
            hissPrev = hiss;

            // ---------- drops on the glass and on the roof ----------
            if (rain > 0.002f || tickL + tickR > 1e-5f)
            {
                float rateG = rain > 0.002f ? (30f + 770f * rain * MathF.Sqrt(rain)) * (0.55f + 0.45f * v01) : 0f;
                float rateR = rain > 0.002f ? 60f + 260f * rain : 0f;
                float pG = rateG * c.invSr, pR = pG + rateR * c.invSr;
                float amp = Mix.Rain * (0.5f + 0.5f * rain) * (0.6f + 0.4f * c.bedDuck);
                float[] w0 = c.w0, w1 = c.w1, cl = c.cabL, cr = c.cabR;
                for (int j = 0; j < n; j++)
                {
                    float u = rng.Uni();
                    if (u < pR)
                    {
                        float r = rng.Uni(), side = rng.Uni();
                        float a = 0.15f + 0.85f * r * r;
                        if (u < pG)
                        {
                            tickL += a * (1f - side); tickR += a * side;
                            if (r > 0.6f) p.glass.Hit(j, 0.05f * a * (0.5f + rain), side);
                        }
                        else p.roof.Hit(j, 0.009f * a * (0.4f + rain), side);
                    }
                    cl[j] += amp * patL.Bp(w0[j] * tickL); cr[j] += amp * patR.Bp(w1[j] * tickR);
                    tickL *= tickDec; tickR *= tickDec;
                }
            }
        }
    }

    /// <summary>Wipers, driven by the same blade position as the picture: the swish of each stroke, a faint motor,
    /// the thunk of the reversal, a rubber squeak when the glass is only damp, and after the cracks a scrape each
    /// time the blade crosses one.</summary>
    public sealed class WiperVoice
    {
        static readonly float[] CrackAt = { 0.33f, 0.58f, 0.8f };
        readonly int sr;
        Rng rng;
        Svf swish, squeak, scrapeHp;
        Osc motorA, motorB, chatter;
        readonly ModalBank thunk = new ModalBank(2);
        float prevPos, vel, prevVelSign, swPrev, moPrev, sqPrev, scrape;
        readonly float scrapeDec;

        public WiperVoice(int sampleRate)
        {
            sr = sampleRate;
            thunk.Set(0, 96f, 0.07f, 1f, 0f, 1f, 1f, sr); thunk.Set(1, 1600f, 0.005f, 0.25f, 0f, 1f, 1f, sr);
            scrapeDec = MathF.Exp(-1f / (0.012f * sr)); scrapeHp.Set(2600f, 1.2f, sr);
            Reset();
        }

        public void Reset()
        {
            rng.Seed(12101u); swish.Reset(); squeak.Reset(); scrapeHp.Reset(); thunk.Reset();
            motorA.Init(0f); motorB.Init(0.2f); chatter.Init(0f);
            prevPos = -1f; vel = 0f; prevVelSign = 0f; swPrev = moPrev = sqPrev = 0f; scrape = 0f;
        }

        public void Render(Ctx c, Panels p)
        {
            int n = c.n; float dt = c.dt;
            float pos = c.s.wiperPos;
            if (prevPos < 0f) prevPos = pos;
            float v = (pos - prevPos) / dt;
            vel += (v - vel) * Dsp.Ease(0.012f, dt);
            float sN = Dsp.Clamp(MathF.Abs(vel) / 3f, 0f, 1.3f);
            bool moving = sN > 0.015f;

            // reversal thunk and the park stop
            float sign = vel > 0.08f ? 1f : (vel < -0.08f ? -1f : 0f);
            if (sign != prevVelSign)
            {
                if (prevVelSign != 0f) thunk.Hit(0, 0.5f + 0.3f * rng.Uni(), 0.5f);
                prevVelSign = sign;
            }
            // scrape across each crack
            for (int i = 0; i < 3; i++)
            {
                float grown = i == 0 ? c.raw.crack1 : (i == 1 ? c.raw.crack2 : c.raw.crack3);
                if (grown > 0.5f && (prevPos - CrackAt[i]) * (pos - CrackAt[i]) < 0f)
                {
                    scrape = 0.6f + 0.4f * rng.Uni();
                    p.glass.Hit(0, 0.25f, rng.Uni());
                }
            }
            prevPos = pos;

            float wet = c.s.wetness, rain = c.s.rain;
            float swGain = Mix.Wiper * sN * MathF.Sqrt(sN) * (0.35f + 0.65f * wet);
            float moGain = Mix.Wiper * 0.05f * (moving ? 1f : 0f);
            float damp = wet * (1f - Dsp.Step(0.12f, 0.45f, rain));                    // damp glass squeaks, wet glass does not
            float sqGain = Mix.Wiper * 0.5f * damp * (vel < 0f ? 1f : 0.35f) * Dsp.Step(0.25f, 0.8f, sN);
            float pan = (0.5f - pos) * 0.8f;
            if (swGain < 1e-6f && swPrev < 1e-6f && moPrev < 1e-6f && moGain < 1e-6f && scrape < 1e-5f && sqPrev < 1e-6f && !thunk.Hot)
            {
                return;
            }
            swish.Set(900f + 1800f * sN, 1.5f, sr);
            squeak.Set(1350f + 900f * sN, 14f, sr);
            motorA.SetFreq(190f, sr); motorB.SetFreq(382f, sr); chatter.SetFreq(35f, sr);
            motorA.Norm(); motorB.Norm(); chatter.Norm();
            float gl, gr; Dsp.Pan(pan, out gl, out gr);
            float g1 = swPrev, d1 = (swGain - swPrev) / n, g2 = moPrev, d2 = (moGain - moPrev) / n, g3 = sqPrev, d3 = (sqGain - sqPrev) / n;
            float[] w1 = c.w1, w3 = c.w3, cl = c.cabL, cr = c.cabR;
            float sc = scrape, scGain = Mix.Wiper * 1.3f;
            for (int j = 0; j < n; j++)
            {
                g1 += d1; g2 += d2; g3 += d3;
                float x = w1[j];
                float s = g1 * swish.Bp(x) + g3 * squeak.Bp(x) * (0.3f + 0.7f * (0.5f + 0.5f * chatter.Tick()))
                        + scGain * sc * scrapeHp.Hp(w3[j]);
                float m = g2 * (motorA.Tick() + 0.5f * motorB.Tick());
                sc *= scrapeDec;
                cl[j] += gl * s + 0.7f * m; cr[j] += gr * s + 0.7f * m;
            }
            scrape = sc < 1e-6f ? 0f : sc;
            swPrev = swGain; moPrev = moGain; sqPrev = sqGain;
            thunk.Render(cl, cr, n, Mix.Wiper * 0.9f);
        }
    }
}
