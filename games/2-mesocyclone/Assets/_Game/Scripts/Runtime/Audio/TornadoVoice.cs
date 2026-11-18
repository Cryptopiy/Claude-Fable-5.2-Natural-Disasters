using System;

namespace MESOCYCLONE.Audio
{
    /// <summary>The tornado, four voices mixed at the source: the sub pulse (20-60 Hz, amplitude-modulated by the
    /// slow outer rotation, with its octave so that small speakers still read "huge"), the low roar, the whipping
    /// hiss (200-2000 Hz with two wandering formants) and far debris impacts. The mix travels through a 20 s
    /// fractional delay line (distance / 340 m/s: the delay shrinks as it nears, the pitch of everything rises a
    /// little), then the air takes the highs (2 kHz at 1 km, 400 Hz at 4 km) and the sub outlives the roar.
    /// In the dark between two designed flashes the roar slowly grows; each flash takes it back a step.</summary>
    public sealed class TornadoVoice
    {
        readonly int sr;
        Rng rng;
        readonly DelayLine line, widen;
        readonly int widenTap;
        // source
        Svf subHp, subLp, sub2, roarLp, hissHp, hissLp, f1, f2, debLp;
        float roarHz, rotPhase, chugPhase;
        Wander amNoise, form1, form2, whip;
        float pSub, pRoar, pHiss;
        float debEnv, debDec = 0.999f, debTimer = 1f, wobT = -1f, wobF = 120f, wobPhase, wobAmp;
        Osc wob;
        // listener
        double dl1, dl2;
        Svf split;
        float a1, a2, gSubPrev, gRoarPrev, panL = 0.7f, panR = 0.7f, widthPrev;
        float swell, swellSm, warm;
        int tailHot;
        readonly float roarHpA;

        public TornadoVoice(int sampleRate)
        {
            sr = sampleRate;
            line = new DelayLine(sr * 20);
            widen = new DelayLine((int)(0.02f * sr));
            widenTap = (int)(0.011f * sr);
            roarHpA = Dsp.Pole(55f, sr);
            Reset();
        }

        public void Reset()
        {
            rng.Seed(13001u); line.Clear(); widen.Clear();
            subHp.Reset(); subLp.Reset(); sub2.Reset(); roarLp.Reset(); hissHp.Reset(); hissLp.Reset(); f1.Reset(); f2.Reset(); debLp.Reset();
            roarHz = 0f; rotPhase = 0f; chugPhase = 0f; amNoise.Reset(); form1.Reset(); form2.Reset(); whip.Reset();
            pSub = pRoar = pHiss = 0f; debEnv = 0f; debTimer = 1f; wobT = -1f; wob.Init(0f);
            dl1 = dl2 = -1.0; split.Reset(); a1 = a2 = 0f; gSubPrev = gRoarPrev = 0f; widthPrev = 0f;
            swell = 0f; swellSm = 0f; warm = 0f; tailHot = 0;
        }

        /// <summary>A flash: a strong designed one takes the roar back a step; it then grows again in the dark.</summary>
        public void Flash(in SoundEvent e) { if (e.a > 0.6f) swell = -1f; }

        public void Render(Ctx c)
        {
            int n = c.n; float dt = c.dt;
            float power = c.s.torPower;
            warm += dt;
            if (power > 0.0005f) tailHot = (int)(21f / dt);
            if (tailHot <= 0) { line.Clear(); return; }
            tailHot--;

            // ---------- source (at the vortex) ----------
            rotPhase += dt * 0.33f; if (rotPhase >= 1f) rotPhase -= 1f;        // sub-vortices passing: the slow rotation
            chugPhase += dt * 5.3f; if (chugPhase >= 1f) chugPhase -= 1f;      // the freight-train chug
            float am = 1f + 0.5f * MathF.Sin(Dsp.TwoPi * rotPhase) + 0.3f * amNoise.Tick(ref c.rng, 5f, 0.07f, dt)
                       + 0.18f * MathF.Sin(Dsp.TwoPi * chugPhase);
            float wh = 0.62f + 0.38f * whip.Tick(ref c.rng, 3f, 0.14f, dt);
            float tSub = power * am * 0.55f, tRoar = power * (0.8f + 0.2f * am), tHiss = power * wh;
            subHp.Set(22f, 0.7f, sr); subLp.Set(58f, 0.8f, sr); sub2.Set(76f, 0.9f, sr);
            roarLp.Set(330f, 0.7f, sr); hissHp.Set(210f, 0.7f, sr); hissLp.Set(2000f, 0.7f, sr);
            f1.Set(440f * (1f + 0.3f * form1.Tick(ref c.rng, 0.25f, 2f, dt)), 2.5f, sr);
            f2.Set(1120f * (1f + 0.22f * form2.Tick(ref c.rng, 0.2f, 2.5f, dt)), 2.5f, sr);

            // debris: far crashes, and now and then a sheet of metal wobbling through the air
            float debris = c.s.torDebris * power;
            debTimer -= dt * (0.5f + 3.5f * debris);
            if (debTimer <= 0f && debris > 0.02f)
            {
                debTimer = rng.Range(0.5f, 1.6f);
                debEnv = rng.Range(0.4f, 1f) * debris; debDec = MathF.Exp(-1f / (rng.Range(0.05f, 0.3f) * sr));
                debLp.Set(rng.Range(400f, 1200f), 0.8f, sr);
                if (wobT < 0f && rng.Uni() < 0.12f) { wobT = 0f; wobF = rng.Range(80f, 200f); wobAmp = 0.35f * debris; }
            }
            float wobGain = 0f;
            if (wobT >= 0f)
            {
                wobT += dt; wobPhase += dt * 9f;
                wob.SetFreq(wobF * (1f + 0.25f * MathF.Sin(Dsp.TwoPi * wobPhase)), sr); wob.Norm();
                wobGain = wobAmp * Dsp.Step(0f, 0.08f, wobT) * (1f - Dsp.Step(0.3f, 0.6f, wobT));
                if (wobT > 0.6f) wobT = -1f;
            }

            // ---------- listener ----------
            float dA = MathF.Max(250f, c.s.torDist - 0.6f * c.s.torRadius);       // to the near wall of the wedge
            double want = Math.Min(dA / Config.SpeedOfSound * (double)sr, line.Length - 16.0);
            if (dl1 < 0.0) { dl1 = want; dl2 = want; }
            dl1 += (want - dl1) * c.k300; double dlNext = dl2 + (dl1 - dl2) * c.k300;
            double dlStep = (dlNext - dl2) / n, dly = dl2;
            dl2 = dlNext;
            float delaySec = (float)(dlNext / sr);
            float fresh = 1f - Dsp.Step(delaySec, delaySec + 0.6f, warm);         // after a seek: no wait for the line to fill
            float fc = Dsp.Clamp(2000f * MathF.Pow(1000f / dA, 1.16f), 150f, 3500f) * (1f - 0.2f * c.s.torWrap);
            float la = Dsp.Pole(fc, sr);
            split.Set(90f, 0.7f, sr);
            swell = MathF.Min(0.6f, swell + dt * 0.22f);
            swellSm += (swell - swellSm) * Dsp.Ease(0.25f, dt);
            float grow = MathF.Pow(10f, 0.15f * swellSm);                         // +/- 3 dB per unit
            float tgSub = Mix.Tornado * 1.1f * MathF.Min(1f, MathF.Pow(1500f / dA, 0.6f));
            float tgRoar = Mix.Tornado * MathF.Min(1.6f, MathF.Pow(1200f / dA, 1.5f)) * grow;
            float width = Dsp.Clamp01(c.s.torRadius / dA) * 0.5f;
            float tl, tr; Dsp.Pan(c.PanOf(c.raw.torBearing) * (1f - 1.2f * width), out tl, out tr);
            panL += (tl - panL) * c.k50; panR += (tr - panR) * c.k50;
            float pL = panL * 1.414f, pR = panR * 1.414f;

            float gs = pSub, dgs = (tSub - pSub) / n, gr = pRoar, dgr = (tRoar - pRoar) / n, gh = pHiss, dgh = (tHiss - pHiss) / n;
            float ls = gSubPrev, dls = (tgSub - gSubPrev) / n, lr = gRoarPrev, dlr = (tgRoar - gRoarPrev) / n;
            float wd = widthPrev, dwd = (width - widthPrev) / n;
            float[] br = c.brown, pk = c.pinkL, w2 = c.w2, w3 = c.w3, wl = c.worldL, wr = c.worldR, sl = c.revL, sr_ = c.revR;
            float k1 = f1.k, k2 = f2.k;
            for (int j = 0; j < n; j++)
            {
                float b = br[j], p = pk[j];
                gs += dgs; gr += dgr; gh += dgh;
                float sub = subLp.Lp(subHp.Hp(b)) * 5f + sub2.Bp(b) * 1.1f;
                float lo = roarLp.Lp(p + 0.5f * b); roarHz += roarHpA * (lo - roarHz);
                float hs = hissLp.Lp(hissHp.Hp(w3[j])) * 1.3f;
                hs += 0.9f * (k1 * f1.Bp(hs) + k2 * f2.Bp(hs));
                float deb = debLp.Lp(w2[j] * debEnv) * 2.5f + wobGain * wob.Tick();
                debEnv *= debDec;
                float src = gs * sub + gr * (lo - roarHz) * 1.2f + gh * hs * 1.6f + deb;
                line.Write(src);

                dly += dlStep;
                int whole = (int)dly;
                float y = line.Read(whole + 1, (float)(dly - whole));
                y += fresh * (src - y);
                float low = split.Lp(y), high = y - low;
                a1 += la * (high - a1); a2 += la * (a1 - a2);
                ls += dls; lr += dlr; wd += dwd;
                float mid = lr * a2, side = wd * widen.Tap(widenTap);
                widen.Write(mid);
                float l = pL * mid + side + 0.75f * ls * low, r = pR * mid - side + 0.75f * ls * low;
                wl[j] += l; wr[j] += r; sl[j] += 0.08f * l; sr_[j] += 0.08f * r;
            }
            pSub = tSub; pRoar = tRoar; pHiss = tHiss; gSubPrev = tgSub; gRoarPrev = tgRoar; widthPrev = width;
            if (debEnv < 1e-7f) debEnv = 0f;
        }
    }
}
