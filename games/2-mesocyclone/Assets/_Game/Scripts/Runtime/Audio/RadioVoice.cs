using System;

namespace MESOCYCLONE.Audio
{
    /// <summary>The dash radio: a weak station lost in static. Band-limited hiss that breathes, crackle, two slow
    /// heterodyne whistles that come and go, a crash of static exactly at every lightning flash (radio is instant,
    /// thunder is late) and a generic attention tone: three plain pulses of one 620 Hz tone every 27 s. It is
    /// deliberately NOT any real alert signal (no two-tone pair, no 853/960/1050 Hz, no data bursts).
    /// In beat 5 it breaks up (dropouts, the band closing, the tone sagging) and dies.</summary>
    public sealed class RadioVoice
    {
        const float ToneStart = 104.5f, TonePeriod = 27f, ToneHz = 620f;
        readonly int sr;
        Rng rng;
        Svf band;
        float hpZ, gPrev;
        Wander breath, glideA, glideB, fadeA, fadeB;
        Osc whA, whB, tone, tone2, buzz;
        float fA = 900f, fB = 1700f, tonePrev, whAPrev, whBPrev;
        float clickEnv, crash, crashDecay = 0.999f, zap;
        float gate = 1f, gateTarget = 1f, gateTimer, gatePrev = 1f;
        readonly float clickDec, hpA;
        bool finalClick;

        public RadioVoice(int sampleRate)
        {
            sr = sampleRate;
            clickDec = MathF.Exp(-1f / (0.0008f * sr)); hpA = Dsp.Pole(300f, sr);
            Reset();
        }

        public void Reset()
        {
            rng.Seed(10001u); band.Reset(); hpZ = 0f; gPrev = 0f;
            breath.Reset(); glideA.Reset(); glideB.Reset(); fadeA.Reset(); fadeB.Reset();
            whA.Init(0f); whB.Init(0.3f); tone.Init(0f); tone2.Init(0f); buzz.Init(0f);
            fA = 900f; fB = 1700f; tonePrev = 0f; whAPrev = 0f; whBPrev = 0f;
            clickEnv = 0f; crash = 0f; zap = 0f; gate = 1f; gateTarget = 1f; gateTimer = 0f; gatePrev = 1f; finalClick = false;
        }

        /// <summary>A flash happened now. a = power, b = distance in metres, c = flash kind (3 = transformer).</summary>
        public void Sferic(in SoundEvent e)
        {
            float near = 1f / (1f + e.b / 6000f);                        // 1 close .. 0.2 at 25 km
            float amp = Dsp.Clamp01(e.a) * (0.35f + 0.65f * near);
            if ((int)(e.c + 0.5f) == 3) { if (amp * 1.2f > zap) zap = amp * 1.2f; }      // an arc: a buzzing zap
            else if (amp > crash)
            {
                crash = amp;
                crashDecay = MathF.Exp(-1f / (rng.Range(0.05f, 0.16f) * sr));
            }
        }

        public void Render(Ctx c)
        {
            int n = c.n; float dt = c.dt, t = (float)c.T;
            float level = c.s.radio;
            if (level < 0.0005f && gPrev < 1e-6f)
            {
                if (!finalClick && t > 468.5f && t < 470f) { finalClick = true; clickEnv = 0.5f; gPrev = Mix.Radio * 0.3f; }
                else { crash = 0f; zap = 0f; return; }
            }
            float dying = Dsp.Step(455f, 468f, t);                        // 0 healthy .. 1 dead (beat 5)

            // dropouts while it dies
            gateTimer -= dt;
            if (gateTimer <= 0f)
            {
                gateTimer = rng.Range(0.03f, 0.3f);
                gateTarget = rng.Uni() < 0.8f * dying ? 0.05f : 1f;
            }
            gate += (gateTarget - gate) * Dsp.Ease(0.003f, dt);

            float br = breath.Tick(ref c.rng, 0.3f, 1.8f, dt);
            float gain = Mix.Radio * level * (0.75f + 0.25f * br) * (0.5f + 0.5f * c.bedDuck);
            band.Set(Dsp.Lerp(3400f, 420f, Dsp.Step(463f, 468.5f, t)), 0.75f, sr);

            // two whistles gliding slowly, each present only now and then
            fA *= 1f + 0.18f * dt * glideA.Tick(ref c.rng, 0.2f, 2.5f, dt);
            fB *= 1f + 0.22f * dt * glideB.Tick(ref c.rng, 0.16f, 3f, dt);
            fA = Dsp.Clamp(fA, 420f, 2500f); fB = Dsp.Clamp(fB, 500f, 2600f);
            float aA = fadeA.Tick(ref c.rng, 0.12f, 3f, dt) - 0.15f, aB = fadeB.Tick(ref c.rng, 0.1f, 3.5f, dt) - 0.3f;
            float whAGain = aA > 0f ? aA * 0.2f : 0f, whBGain = aB > 0f ? aB * 0.16f : 0f;
            float sag = 1f - 0.08f * dying;
            whA.SetFreq(fA * sag, sr); whB.SetFreq(fB * sag, sr); whA.Norm(); whB.Norm();

            // the attention tone: three pulses, 0.5 s on, 0.25 s off, 20 ms raised-cosine edges
            float toneAmp = 0f;
            if (t >= ToneStart && t < 469f && !(t > 344f && t < 362f))
            {
                float ph = (t - ToneStart) % TonePeriod;
                if (ph < 2.02f)
                {
                    float u = ph % 0.75f;
                    if (ph < 2f || u < 0.52f) toneAmp = Dsp.Step(0f, 0.02f, u) * (1f - Dsp.Step(0.5f, 0.52f, u));
                }
            }
            toneAmp *= 0.5f;
            tone.SetFreq(ToneHz * sag, sr); tone2.SetFreq(2f * ToneHz * sag, sr); tone.Norm(); tone2.Norm();
            buzz.SetFreq(120f, sr); buzz.Norm();

            // crackle: Poisson clicks, more of them in rain and near the storm
            float pClick = (3f + 9f * c.s.rain + 10f * dying) * c.invSr;

            float g = gPrev, dg = (gain - gPrev) / n;
            float gt = gatePrev, dgt = (gate - gatePrev) / n;
            float ta = tonePrev, dta = (toneAmp - tonePrev) / n;
            float wa = whAPrev, dwa = (whAGain - whAPrev) / n, wb = whBPrev, dwb = (whBGain - whBPrev) / n;
            float[] w3 = c.w3, cl = c.cabL, cr = c.cabR;
            float cd = crashDecay, zd = 1f - 3.5f * c.invSr;
            for (int j = 0; j < n; j++)
            {
                if (rng.Uni() < pClick) clickEnv = rng.Range(0.6f, 2.2f);
                float w = w3[j];
                float bz = buzz.Tick(); bz = bz > 0.55f ? 1f : 0.12f;                // a 120 Hz gated buzz for arcs
                float x = w * (1f + 3.2f * crash + 3f * zap * bz) + w * clickEnv * 2.5f;
                clickEnv *= clickDec; crash *= cd; zap *= zd;
                ta += dta; wa += dwa; wb += dwb;
                x += ta * (tone.Tick() + 0.1f * tone2.Tick()) + wa * whA.Tick() + wb * whB.Tick();
                hpZ += hpA * (x - hpZ);
                float y = band.Lp(x - hpZ);
                g += dg; gt += dgt;
                y *= g * gt;
                cl[j] += 0.62f * y; cr[j] += 0.78f * y;
            }
            gPrev = gain; gatePrev = gate; tonePrev = toneAmp; whAPrev = whAGain; whBPrev = whBGain;
            if (crash < 1e-6f) crash = 0f;
            if (zap < 1e-6f) zap = 0f;
        }
    }
}
