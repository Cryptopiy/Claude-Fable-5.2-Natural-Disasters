using System;

namespace MESOCYCLONE.Audio
{
    /// <summary>Wind on the open plain, heard from inside the cab: the broadband roar outside (muffled by the cab),
    /// the low buffeting that shoves the body, two unstable whistles at the door seal and the mirror, and the grit
    /// of the dust wall on the sheet metal. All of it breathes with one shared gust fluctuation.</summary>
    public sealed class WindVoice
    {
        readonly int sr;
        Rng rng;
        Svf roarL, roarR, lowL, lowR, whA, whB, gritHp;
        float buf1, buf2;
        Wander amA, wanderA, wanderB, side;
        float roarPrevL, roarPrevR, bufPrev, whAPrev, whBPrev, gritPrev;
        float gritL, gritR;
        readonly float gritDec, bufA;
        readonly ModalBank door = new ModalBank(6);

        public WindVoice(int sampleRate)
        {
            sr = sampleRate;
            gritDec = MathF.Exp(-1f / (0.0007f * sr)); bufA = Dsp.Pole(55f, sr);
            Rng r = new Rng(); r.Seed(11001u);
            for (int i = 0; i < 6; i++)
            {   // a door skin: loss factor 0.1, 150 Hz .. 3 kHz
                float f = 150f * MathF.Pow(1.82f, i) * (1f + 0.06f * r.Bi());
                door.Set(i, f, MathF.Max(0.004f, 2.2f / (0.1f * f)), r.Range(0.4f, 0.9f), 0.5f * r.Bi(), r.Range(0.3f, 1f), r.Range(0.3f, 1f) * ((i & 1) == 0 ? 1f : -1f), sr);
            }
            gritHp.Set(4200f, 0.7f, sr);
            Reset();
        }

        public void Reset()
        {
            rng.Seed(11002u);
            roarL.Reset(); roarR.Reset(); lowL.Reset(); lowR.Reset(); whA.Reset(); whB.Reset(); gritHp.Reset(); door.Reset();
            buf1 = buf2 = 0f; amA.Reset(); wanderA.Reset(); wanderB.Reset(); side.Reset();
            roarPrevL = roarPrevR = bufPrev = whAPrev = whBPrev = gritPrev = 0f; gritL = gritR = 0f;
        }

        public void Render(Ctx c)
        {
            int n = c.n; float dt = c.dt;
            float w = c.wind01;                                       // 1 = 36 m/s, gusts included
            float duck = c.bedDuck;
            if (c.s.gust * w > c.shake) c.shake = Dsp.Clamp01(c.s.gust * w * (0.75f + 0.5f * c.gustMod));

            // the wind comes from windDir + 180: lean the roar to that side of the head
            float from = c.s.windDir + 180f;
            float lean = 0.45f * MathF.Sin((from - c.raw.headYaw) * Dsp.Deg2Rad) + 0.12f * side.Tick(ref c.rng, 0.4f, 1.5f, dt);
            float pl, pr; Dsp.Pan(lean, out pl, out pr);

            // ---------- roar (outside) ----------
            float level = Mix.Wind * w * w * duck;
            float rl = level * pl * 1.414f, rr = level * pr * 1.414f;
            if (rl > 1e-6f || rr > 1e-6f || roarPrevL > 1e-6f || roarPrevR > 1e-6f)
            {
                float fc = 190f + 1300f * Dsp.Clamp(w, 0f, 1.5f);
                roarL.Set(fc, 0.7f, sr); roarR.Set(fc * 1.07f, 0.7f, sr);
                lowL.Set(95f, 0.8f, sr); lowR.Set(105f, 0.8f, sr);
                float gl = roarPrevL, dgl = (rl - roarPrevL) / n, gr = roarPrevR, dgr = (rr - roarPrevR) / n;
                float[] a = c.pinkL, b = c.pinkR, br = c.brown, ol = c.worldL, or_ = c.worldR;
                for (int j = 0; j < n; j++)
                {
                    float xl = a[j] + 0.45f * b[j], xr = b[j] + 0.45f * a[j], lo = br[j];
                    gl += dgl; gr += dgr;
                    ol[j] += gl * (roarL.Lp(xl) + 0.45f * lowL.Lp(lo + 0.3f * xl));
                    or_[j] += gr * (roarR.Lp(xr) + 0.45f * lowR.Lp(lo + 0.3f * xr));
                }
            }
            roarPrevL = rl; roarPrevR = rr;

            // ---------- buffeting on the body (felt more than heard) ----------
            float am = amA.Tick(ref c.rng, 4f, 0.09f, dt);
            float bufGain = Mix.Wind * 0.9f * MathF.Pow(Dsp.Clamp(w, 0f, 1.5f), 2.5f) * (0.35f + 0.65f * c.s.gust) * (0.55f + 0.45f * am) * (0.4f + 0.6f * duck);
            if (bufGain > 1e-6f || bufPrev > 1e-6f)
            {
                float g = bufPrev, dg = (bufGain - bufPrev) / n, a = bufA;
                float[] br = c.brown, cl = c.cabL, cr = c.cabR;
                for (int j = 0; j < n; j++)
                {
                    buf1 += a * (br[j] - buf1); buf2 += a * (buf1 - buf2);
                    g += dg;
                    float s = g * buf2 * 6f;
                    cl[j] += s * (0.72f - 0.2f * lean); cr[j] += s * (0.72f + 0.2f * lean);
                }
            }
            bufPrev = bufGain;

            // ---------- whistles at the gaps (narrow noise, never a steady pitch) ----------
            float ws = Dsp.Step(0.6f, 1.2f, w); ws = ws * ws * ws;
            float ga = Mix.Wind * 0.12f * ws * (0.6f + 0.4f * c.gustMod) * duck, gb = Mix.Wind * 0.09f * ws * (0.6f - 0.4f * c.gustMod) * duck;
            if (ga < 0f) ga = 0f;
            if (gb < 0f) gb = 0f;
            if (ga > 1e-6f || gb > 1e-6f || whAPrev > 1e-6f || whBPrev > 1e-6f)
            {
                whA.Set((520f + 620f * w) * (1f + 0.05f * wanderA.Tick(ref c.rng, 1.5f, 0.3f, dt)), 28f, sr);
                whB.Set((860f + 760f * w) * (1f + 0.05f * wanderB.Tick(ref c.rng, 1.2f, 0.35f, dt)), 34f, sr);
                float g1 = whAPrev, d1 = (ga - whAPrev) / n, g2 = whBPrev, d2 = (gb - whBPrev) / n;
                float[] w0 = c.w0, w1 = c.w1, cl = c.cabL, cr = c.cabR;
                for (int j = 0; j < n; j++)
                {
                    g1 += d1; g2 += d2;
                    float s1 = g1 * 5f * whA.Bp(w0[j]), s2 = g2 * 5f * whB.Bp(w1[j]);
                    cl[j] += 0.85f * s1 + 0.3f * s2; cr[j] += 0.3f * s1 + 0.85f * s2;
                }
            }
            whAPrev = ga; whBPrev = gb;

            // ---------- grit of the dust wall on the body ----------
            float dust = c.s.dust * Dsp.Clamp01(0.3f + w);
            float gritGain = Mix.Wind * 1.25f * (0.5f + 0.5f * duck);
            if (dust > 0.002f || gritL + gritR > 1e-5f)
            {
                float p = dust * (60f + 560f * Dsp.Clamp01(0.7f + 0.6f * c.gustMod)) * c.invSr;
                float[] w2 = c.w2, cl = c.cabL, cr = c.cabR;
                float upwind = 0.5f + 0.5f * Dsp.Clamp(lean * 2f, -1f, 1f);        // 0 left .. 1 right
                for (int j = 0; j < n; j++)
                {
                    if (rng.Uni() < p)
                    {
                        float a = rng.Uni(); a = 0.25f + 0.75f * a * a;
                        float sd = rng.Uni() < upwind ? 1f : 0f;
                        gritL += a * (1f - sd); gritR += a * sd;
                        door.Hit(j, 0.35f * a, sd);
                    }
                    float x = gritHp.Hp(w2[j]) * gritGain;
                    cl[j] += x * gritL; cr[j] += x * gritR;
                    gritL *= gritDec; gritR *= gritDec;
                }
            }
            door.Render(c.cabL, c.cabR, n, gritGain * 0.9f);
        }
    }
}
