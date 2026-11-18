using System;

namespace MESOCYCLONE.Audio
{
    /// <summary>Hail on the truck. Three statistical layers (pea: hundreds per second, golf ball: 10-30, baseball:
    /// 2-6; as the stones grow the hits get fewer and heavier) drive the shared panel resonators and a direct
    /// "ice on metal" layer; the softballs are single designed events (HailScore.BigHit): heavy thumps with
    /// quiet around them. Also owns the three glass cracks and the hush that prepares each of them.</summary>
    public sealed class HailVoice
    {
        const int Thuds = 4;
        readonly int sr;
        Rng rng;
        // direct layer: shot-noise envelopes (left/right) for small, medium and big stones
        float eSL, eSR, eML, eMR, eBL, eBR;
        readonly float dS, dM, dB;
        readonly int soft;                              // frames of a heavy stone's contact with a hard panel
        Svf fSL, fSR, fML, fMR, fBL, fBR, gndL, gndR;
        float gndPrev;
        // softball thuds
        readonly Osc[] thOsc = new Osc[Thuds];
        readonly float[] thEnv = new float[Thuds], thDec = new float[Thuds], thF = new float[Thuds], thL = new float[Thuds], thR = new float[Thuds];
        readonly float[] thN = new float[Thuds], thNd = new float[Thuds];
        readonly bool[] thWorld = new bool[Thuds];
        readonly Svf[] thLp = new Svf[Thuds];
        int thNext;
        public readonly CrackVoice cracks;

        public HailVoice(int sampleRate)
        {
            sr = sampleRate;
            dS = MathF.Exp(-1f / (0.0035f * sr)); dM = MathF.Exp(-1f / (0.011f * sr)); dB = MathF.Exp(-1f / (0.024f * sr));
            cracks = new CrackVoice(sr);
            soft = Math.Max(3, (int)(0.0003f * sr));
            fSL.Set(4200f, 0.6f, sr); fSR.Set(4700f, 0.6f, sr);
            fML.Set(1500f, 0.7f, sr); fMR.Set(1700f, 0.7f, sr);
            fBL.Set(520f, 0.8f, sr); fBR.Set(560f, 0.8f, sr);
            gndL.Set(1900f, 0.6f, sr); gndR.Set(2200f, 0.6f, sr);
            for (int i = 0; i < Thuds; i++) thLp[i].Set(650f, 0.7f, sr);
            Reset();
        }

        public void Reset()
        {
            rng.Seed(7201u);
            eSL = eSR = eML = eMR = eBL = eBR = 0f; gndPrev = 0f;
            fSL.Reset(); fSR.Reset(); fML.Reset(); fMR.Reset(); fBL.Reset(); fBR.Reset(); gndL.Reset(); gndR.Reset();
            for (int i = 0; i < Thuds; i++) { thEnv[i] = 0f; thN[i] = 0f; thOsc[i].Init(0f); thLp[i].Reset(); }
            thNext = 0; cracks.Reset();
        }

        public void Crack(in SoundEvent e) { cracks.Start((int)(e.a + 0.5f)); }

        /// <summary>A single heavy stone. a = where (0 roof, 1 hood, 2 glass, 3 road), b = diameter in metres.</summary>
        public void BigHit(in SoundEvent e, Ctx c, Panels p)
        {
            int where = (int)(e.a + 0.5f);
            float amp = MathF.Pow(Dsp.Clamp(e.b / 0.1f, 0.25f, 1.3f), 1.5f);
            float px = e.c, py = e.d;
            int v = thNext; thNext = (thNext + 1) % Thuds;
            thOsc[v].Init(0f); thWorld[v] = false;
            float pan;
            if (where == 0)
            {   // roof: the big drum right above the head
                p.HeavyRoof(0, 1.1f * amp, py); p.roof.HitSoft(0, 0.5f * amp, px, soft);
                thF[v] = 84f + 20f * py; thEnv[v] = 0.9f * amp; thDec[v] = MathF.Exp(-1f / (0.045f * sr)); thN[v] = 0.9f * amp;
                pan = (px - 0.5f) * 1.2f;
                if (c.shake < 1f) c.shake = 1f;
            }
            else if (where == 1)
            {   // hood: a tonk in front
                p.HeavyHood(0, 1.1f * amp, px); p.hood.HitSoft(0, 0.45f * amp, py, soft);
                thF[v] = 118f + 25f * py; thEnv[v] = 0.6f * amp; thDec[v] = MathF.Exp(-1f / (0.03f * sr)); thN[v] = 0.6f * amp;
                pan = (px - 0.5f) * 0.9f;
                if (c.shake < 0.7f) c.shake = 0.7f;
            }
            else if (where == 2)
            {   // windscreen: the worst sound of the three
                p.glass.HitSoft(0, 2.5f * amp, px, soft); p.HeavyHood(0, 0.5f * amp, py);
                thF[v] = 176f; thEnv[v] = 0.45f * amp; thDec[v] = MathF.Exp(-1f / (0.022f * sr)); thN[v] = 1.1f * amp;
                pan = (px - 0.5f) * 0.6f;
                if (c.shake < 0.9f) c.shake = 0.9f;
            }
            else
            {   // the road beside the truck: outside, dull
                thF[v] = 70f; thEnv[v] = 0.5f * amp; thDec[v] = MathF.Exp(-1f / (0.05f * sr)); thN[v] = 0.7f * amp;
                pan = px < 0.5f ? -0.8f : 0.8f; thWorld[v] = true;
            }
            thNd[v] = MathF.Exp(-1f / (0.012f * sr));
            Dsp.Pan(pan, out thL[v], out thR[v]);
        }

        public void Render(Ctx c, Panels p)
        {
            int n = c.n;
            float t = (float)c.T;
            float h = Dsp.Clamp01(c.s.hail), d = c.s.hailSize * 100f;       // diameter in cm

            // the hush before each crack (0.45 s) so that the tick is prepared and heard
            float hush = Hush(t, Story.Crack1Time) + Hush(t, Story.Crack2Time) + Hush(t, Story.Crack3Time);
            // the build: pea hail is a bright patter, softballs are deafening (about 7 dB between the two)
            float stage = Dsp.Lerp(1f, Dsp.Lerp(0.42f, 1f, Dsp.Step(1.5f, 9f, d)), Dsp.Step(0f, 0.05f, h));
            float bus = (1f - 0.5f * Dsp.Clamp01(hush)) * stage;
            p.Gain = bus;

            bool active = h > 0.0005f;
            float sq = MathF.Sqrt(h);
            float rateS = sq * 640f * (1f - 0.78f * Dsp.Step(1.5f, 6f, d));
            float rateM = sq * 26f * Dsp.Step(1.8f, 4f, d) * (1f - 0.6f * Dsp.Step(5.5f, 8.5f, d));
            float rateB = sq * 5.5f * Dsp.Step(5f, 7.5f, d);
            float pS = rateS * c.invSr, pM = pS + rateM * c.invSr, pB = pM + rateB * c.invSr;
            float aS = 0.085f * (1f + 0.6f * Dsp.Step(1f, 4f, d));
            float aM = 0.55f * MathF.Pow(Dsp.Clamp(d, 2f, 5.5f) * 0.25f, 1.6f);
            float aB = 0.5f * MathF.Pow(Dsp.Clamp(d, 5f, 8f) * 0.25f, 1.6f);
            if (active && c.shake < 0.35f * h) c.shake = 0.35f * h;

            float[] w0 = c.w0, w1 = c.w1, cl = p.inL, cr = p.inR;
            bool tails = eSL + eSR + eML + eMR + eBL + eBR > 1e-6f;
            if (active || tails)
            {
                for (int j = 0; j < n; j++)
                {
                    if (active)
                    {
                        float u = rng.Uni();
                        if (u < pB)
                        {
                            float r = rng.Uni(), side = rng.Uni(), q = rng.Uni(), a;      // size, left/right, which panel
                            if (u < pS)
                            {   // pea and ice fragments: bright ticks all over the cab
                                a = aS * (0.25f + 0.75f * r * r);
                                eSL += a * (1f - side); eSR += a * side;
                                if (q < 0.62f) p.roof.Hit(j, a * 1.6f, side);
                                else if (q < 0.87f) p.hood.Hit(j, a * 1.3f, side);
                                else p.glass.Hit(j, a * 1.4f, side);
                            }
                            else if (u < pM)
                            {   // golf balls: each one is heard
                                a = aM * (0.4f + 0.6f * r);
                                eML += 0.28f * a * (1f - side); eMR += 0.28f * a * side;
                                if (q < 0.6f) p.HeavyRoof(j, a * 0.5f, side); else if (q < 0.9f) p.HeavyHood(j, a * 0.5f, side); else p.glass.HitSoft(j, a * 0.9f, side, soft / 2);
                                p.roof.HitSoft(j, a * 0.5f, side, soft / 2);
                            }
                            else
                            {   // baseballs: thumps
                                a = aB * (0.5f + 0.5f * r);
                                eBL += 0.3f * a * (1f - side); eBR += 0.3f * a * side;
                                if (q < 0.62f) p.HeavyRoof(j, a * 0.55f, side); else p.HeavyHood(j, a * 0.55f, side);
                                p.roof.HitSoft(j, a * 0.35f, side, soft);
                                if (c.shake < 0.6f) c.shake = 0.6f;
                            }
                        }
                    }
                    float x = w0[j], y = w1[j];
                    cl[j] += 24f * fSL.Bp(x * eSL) + 36f * fML.Bp(x * eML) + 2.2f * fBL.Lp(y * eBL);
                    cr[j] += 24f * fSR.Bp(y * eSR) + 36f * fMR.Bp(y * eMR) + 2.2f * fBR.Lp(x * eBR);
                    eSL *= dS; eSR *= dS; eML *= dM; eMR *= dM; eBL *= dB; eBR *= dB;
                }
                p.TouchInput();
            }

            // hail hitting the road and the fields all around: a roar outside the cab
            float gnd = Mix.Hail * 0.35f * sq * (0.6f + 0.4f * Dsp.Step(1f, 5f, d)) * bus;
            if (gnd > 1e-6f || gndPrev > 1e-6f)
            {
                float gg = gndPrev, dgg = (gnd - gndPrev) / n;
                float[] pl = c.pinkL, pr = c.pinkR, wl = c.worldL, wr = c.worldR;
                for (int j = 0; j < n; j++)
                {
                    gg += dgg;
                    wl[j] += gg * gndL.Lp(pl[j]); wr[j] += gg * gndR.Lp(pr[j]);
                }
            }
            gndPrev = gnd;

            // softball thuds
            float thGain = Mix.Hail * stage;
            for (int v = 0; v < Thuds; v++)
            {
                if (thEnv[v] < 1e-5f && thN[v] < 1e-5f) continue;
                thF[v] *= 1f - 2.2f * c.dt;                          // the pitch sags as the dent springs back
                thOsc[v].SetFreq(thF[v], sr); thOsc[v].Norm();
                float e = thEnv[v], de = thDec[v], ne = thN[v], nd = thNd[v], l = thL[v] * thGain, r = thR[v] * thGain;
                float[] bl = thWorld[v] ? c.worldL : c.cabL, br = thWorld[v] ? c.worldR : c.cabR;
                float[] w2 = c.w2;
                for (int j = 0; j < n; j++)
                {
                    float s = thOsc[v].Tick() * e + thLp[v].Lp(w2[j] * ne + Dsp.Tiny) * 1.4f;
                    e *= de; ne *= nd;
                    bl[j] += l * s; br[j] += r * s;
                }
                thEnv[v] = e; thN[v] = ne;
            }
            cracks.Render(c, p);
        }

        static float Hush(float t, float tc)
        {
            if (t < tc - 0.6f || t > tc + 1f) return 0f;
            return Dsp.Step(tc - 0.5f, tc - 0.12f, t) * (1f - Dsp.Step(tc + 0.12f, tc + 0.8f, t));
        }
    }
}
