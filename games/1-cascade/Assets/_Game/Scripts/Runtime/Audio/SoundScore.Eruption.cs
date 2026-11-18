using System;

namespace CASCADE
{
    /// <summary>
    /// The eruption as heard 5 km away: nothing for ShockDelay, then the pressure thump, the sustained jet roar,
    /// volcanic thunder that never matches a flash, and the fine hiss of falling ash.
    /// </summary>
    public sealed partial class SoundScore
    {
        const float GThump = 0.6f, GRoar = 0.44f, GRoarSub = 0.53f, GThunder = 0.55f, GAsh = 0.03f;

        Osc thOsc; BrownNoise thNz; Svf thLp; PinkNoise thAir; Biquad thAirLp; Ramp gThBody, gThHarm, gThWhump, gThAir; double thInc;

        PinkNoise roNzL, roNzR; Biquad roLpL, roLpR, roPkL, roPkR, roHpL, roHpR, roHiL, roHiR;
        BrownNoise roSubNz; Biquad roSubBp; Ramp gRoL, gRoR, gRoHi, gRoSub; float roLpSet;

        BrownNoise tuNzL, tuNzR; Svf tuLpL, tuLpR; Rng tuRng; Biquad tuBp, tuHp; Ramp gTuRollL, gTuRollR, gTuCrL, gTuCrR;

        Rng ashRngL, ashRngR; Biquad ashHpL, ashHpR; Dust ashDustL, ashDustR; Biquad ashTickL, ashTickR;
        Ramp gAshL, gAshR, gAshTick; float ashDens;

        void InitEruption() { }

        void ResetEruption()
        {
            thOsc = default(Osc); thNz = new BrownNoise(61u, fs); thLp.Clear(); thLp.Set(fs, 200f, 0.8f);
            thAir = new PinkNoise(62u); thAirLp.Clear(); thAirLp.LowPass(fs, 500.0, 0.7);
            gThBody.Snap(0f); gThHarm.Snap(0f); gThWhump.Snap(0f); gThAir.Snap(0f); thInc = 40.0 / fs;

            roNzL = new PinkNoise(63u); roNzR = new PinkNoise(64u);
            roLpL.Clear(); roLpR.Clear(); roPkL.Clear(); roPkR.Clear(); roHpL.Clear(); roHpR.Clear(); roHiL.Clear(); roHiR.Clear();
            roLpSet = 420f; roLpL.LowPass(fs, 420.0, 0.6); roLpR.LowPass(fs, 420.0, 0.6);
            roPkL.Peak(fs, 105.0, 0.8, 7.0); roPkR.Peak(fs, 118.0, 0.8, 7.0);
            roHpL.HighPass(fs, 38.0, 0.707); roHpR.HighPass(fs, 38.0, 0.707);
            roHiL.BandPass(fs, 1250.0, 0.7); roHiR.BandPass(fs, 1400.0, 0.7);
            roSubNz = new BrownNoise(65u, fs); roSubBp.Clear(); roSubBp.BandPass(fs, 40.0, 1.4);
            gRoL.Snap(0f); gRoR.Snap(0f); gRoHi.Snap(0f); gRoSub.Snap(0f);

            tuNzL = new BrownNoise(66u, fs); tuNzR = new BrownNoise(67u, fs);
            tuLpL.Clear(); tuLpR.Clear(); tuLpL.Set(fs, 200f, 0.75f); tuLpR.Set(fs, 200f, 0.75f);
            tuRng = new Rng(68u); tuBp.Clear(); tuHp.Clear(); tuBp.BandPass(fs, 1100.0, 0.6); tuHp.HighPass(fs, 300.0, 0.707);
            gTuRollL.Snap(0f); gTuRollR.Snap(0f); gTuCrL.Snap(0f); gTuCrR.Snap(0f);

            ashRngL = new Rng(69u); ashRngR = new Rng(70u);
            ashHpL.Clear(); ashHpR.Clear(); ashHpL.HighPass(fs, 5200.0, 0.707); ashHpR.HighPass(fs, 5600.0, 0.707);
            ashDustL = new Dust(71u); ashDustR = new Dust(72u);
            ashTickL.Clear(); ashTickR.Clear(); ashTickL.BandPass(fs, 5600.0, 1.5); ashTickR.BandPass(fs, 6400.0, 1.5);
            gAshL.Snap(0f); gAshR.Snap(0f); gAshTick.Snap(0f); ashDens = 0f;
        }

        /// <summary>
        /// Thunder as a closed form of clock time x on a grid of slots: a rolling body (brown noise, falling cutoff)
        /// and a few crackles at its head. Pure function of x, so a seek lands inside a roll correctly.
        /// </summary>
        void ThunderSlots(float x, float slotLen, float prob, int seed, float crackScale,
                          ref float rollL, ref float rollR, ref float crL, ref float crR, ref float fcSum, ref float fcW)
        {
            if (x <= 0f) return;
            int k0 = (int)(x / slotLen);
            for (int k = k0 - 2; k <= k0; k++)
            {
                if (k < 0 || Dsp.Hash(k, seed) > prob) continue;
                float y = x - (slotLen * k + Dsp.Hash(k, seed + 1) * slotLen * 0.6f);
                if (y <= 0f) continue;
                float dur = 3.5f + 4f * Dsp.Hash(k, seed + 2);
                if (y >= dur * 1.4f) continue;
                float amp = 0.35f + 0.65f * Dsp.Hash(k, seed + 3);
                float roll = amp * Dsp.Smooth(0f, 0.45f, y) * Dsp.Exp(-y / (dur * 0.42f)) * (1f - Dsp.Smooth(dur * 1.1f, dur * 1.4f, y))
                           * (0.55f + 0.45f * Math.Abs(Dsp.Noise(y * 2.3 + k * 7.13, seed + 5)));
                float cr = 0f;
                if (y < 1.6f)
                    for (int j = 0; j < 5; j++)
                    {
                        int id = k * 8 + j;
                        float bj = j == 0 ? 0.05f : 0.05f + 0.75f * Dsp.Hash(id, seed + 6);
                        cr += (0.4f + 0.6f * Dsp.Hash(id, seed + 7)) * Dsp.Hit(y - bj, 0.004f, 0.025f + 0.06f * Dsp.Hash(id, seed + 8));
                    }
                cr *= amp * crackScale;
                float pl, pr;
                Dsp.Pan(Dsp.Hash(k, seed + 4) * 1.2f - 0.6f, out pl, out pr);
                rollL += roll * (0.6f + 0.4f * pl); rollR += roll * (0.6f + 0.4f * pr);
                crL += cr * pl; crR += cr * pr;
                fcW += roll; fcSum += roll * (130f + 300f * Dsp.Exp(-y / (dur * 0.35f)));
            }
        }

        void CtlEruption()
        {
            // ---------- the thump: a pressure event (chirp 53 -> 33 Hz, harmonics 66-160 Hz, 2.5 s tail) ----------
            float x = eThump;
            if (x > -0.2f && x < 9f)
            {
                float xp = Math.Max(0f, x), g = GThump * Trim[LThump];
                thInc = (33f + 20f * Dsp.Exp(-xp / 0.28f)) / fs;
                gThBody.To(g * Dsp.Hit(x, 0.03f, 0.75f), Block);
                gThHarm.To(g * Dsp.Hit(x, 0.03f, 0.42f), Block);
                gThWhump.To(g * 1.3f * Dsp.Hit(x, 0.04f, 0.9f), Block);
                gThAir.To(g * 1.4f * Dsp.Hit(x, 0.05f, 0.35f), Block);
                thLp.Set(fs, 60f + 170f * Dsp.Exp(-xp / 0.5f), 0.8f);
            }
            else { gThBody.To(0f, Block); gThHarm.To(0f, Block); gThWhump.To(0f, Block); gThAir.To(0f, Block); }

            // ---------- the roar: sustained jet, slow swell; breath at eruption + 96 s; ducks for the drawdown ----------
            float breath = 1f - 0.992f * Dsp.Win(eEr - 95f, 1.6f, 10f, 2.2f);
            float lvl = 0f;
            float on = Dsp.Smooth(0f, 1.5f, eThump);
            if (on > 0f)
            {
                float grow = 0.5f + 0.5f * Dsp.Smooth(4f, 70f, eThump);
                float duck = 1f - 0.994f * Dsp.Smooth(0f, 8f, eDraw);
                float back = 0.42f * Dsp.Smooth(6f, 40f, eImp);                 // returns behind the ash as the far pyroclastic roar
                float far = Math.Max(duck, back) * (1f + 0.8f * glimpse * storm) * (1f - 0.95f * silence);
                lvl = on * grow * breath * far * impDip * Trim[LRoar];
            }
            float sw = 1f + 0.28f * Dsp.Noise(T * 0.11, 61) + 0.16f * Dsp.Noise(T * 0.29, 62);
            gRoL.To(GRoar * lvl * (sw + 0.07f * Dsp.Noise(T * 1.7, 63)), Block);
            gRoR.To(GRoar * lvl * (sw + 0.07f * Dsp.Noise(T * 1.7, 64)), Block);
            gRoSub.To(GRoarSub * lvl * (1f + 0.3f * Dsp.Noise(T * 0.19, 65)), Block);
            gRoHi.To(GRoar * 0.12f * lvl * (0.5f + glimpse), Block);
            float lp = 420f * (1f - 0.38f * storm) * (1f + 1.5f * glimpse * storm);   // dull inside the ash, open in a clearing
            if (Math.Abs(lp - roLpSet) > 0.01f * roLpSet)
            {
                roLpSet = lp; roLpL.LowPass(fs, lp, 0.6); roLpR.LowPass(fs, lp, 0.6);
            }

            // ---------- thunder: far and soft before the storm, closer inside it; silent in the breath and the hush ----------
            float rollL = 0f, rollR = 0f, crL = 0f, crR = 0f, fcSum = 0f, fcW = 0f;
            float pre = breath * (1f - Dsp.Smooth(0f, 6f, eDraw));
            if (pre > 0.001f)
            {
                ThunderSlots(eThump - 10f, 8.5f, 0.72f, 301, 0.5f, ref rollL, ref rollR, ref crL, ref crR, ref fcSum, ref fcW);
                rollL *= pre; rollR *= pre; crL *= pre; crR *= pre;
            }
            float st = 1f - Dsp.Smooth(0f, 8f, eSil);
            if (st > 0.001f && eStorm > 3f)
            {
                float a = 0f, b = 0f, c = 0f, d = 0f;
                ThunderSlots(eStorm - 3f, 6.5f, 0.8f, 321, 0.9f, ref a, ref b, ref c, ref d, ref fcSum, ref fcW);
                rollL += 1.25f * st * a; rollR += 1.25f * st * b; crL += st * c; crR += st * d;
            }
            float tg = GThunder * Trim[LThunder];
            gTuRollL.To(tg * rollL, Block); gTuRollR.To(tg * rollR, Block);
            gTuCrL.To(tg * crL, Block); gTuCrR.To(tg * crR, Block);
            if (fcW > 1e-4f) { float fc = fcSum / fcW; tuLpL.Set(fs, fc, 0.75f); tuLpR.Set(fs, fc * 1.07f, 0.75f); }

            // ---------- falling ash: fine hiss from eruption + 68 s, dense in the storm, a whisper at the end ----------
            float ash = 0.3f * Dsp.Smooth(Config.AshfallStart, Config.AshfallStart + 40f, eEr) + 1.0f * storm;
            ash *= 1f - 0.8f * Dsp.Smooth(0f, 20f, eSil);
            float al = GAsh * Trim[LAsh] * ash;
            gAshL.To(al * (0.8f + 0.2f * Dsp.Noise(T * 0.4, 81)), Block);
            gAshR.To(al * (0.8f + 0.2f * Dsp.Noise(T * 0.4, 82)), Block);
            gAshTick.To(al * 2.5f, Block);
            ashDens = (200f + 1100f * storm) * ash * invFs;
        }

        void RunEruption(int n)
        {
            if (!(gThBody.Off && gThHarm.Off && gThWhump.Off && gThAir.Off))
                for (int s = 0; s < n; s++)
                {
                    float body = thOsc.Sine(thInc) * gThBody.Next() + (0.6f * thOsc.Harm(2) + 0.3f * thOsc.Harm(3)) * gThHarm.Next();
                    float wh = thLp.Lp(thNz.Next()) * gThWhump.Next();
                    float air = thAirLp.Next(thAir.Next()) * gThAir.Next();
                    low[s] += body + wh;
                    dryL[s] += air; dryR[s] += air;
                    echoIn[s] += body * 0.35f + wh * 0.6f + air * 0.5f;
                }

            if (!(gRoL.Off && gRoR.Off && gRoSub.Off))
                for (int s = 0; s < n; s++)
                {
                    float pl = roNzL.Next(), pr = roNzR.Next(), hi = gRoHi.Next();
                    farL[s] += roHpL.Next(roPkL.Next(roLpL.Next(pl))) * gRoL.Next() + roHiL.Next(pl) * hi;
                    farR[s] += roHpR.Next(roPkR.Next(roLpR.Next(pr))) * gRoR.Next() + roHiR.Next(pr) * hi;
                    low[s] += roSubBp.Next(roSubNz.Next()) * gRoSub.Next();
                }

            if (!(gTuRollL.Off && gTuRollR.Off && gTuCrL.Off && gTuCrR.Off))
                for (int s = 0; s < n; s++)
                {
                    float l = tuLpL.Lp(tuNzL.Next()) * gTuRollL.Next(), r = tuLpR.Lp(tuNzR.Next()) * gTuRollR.Next();
                    float c = tuHp.Next(tuBp.Next(tuRng.Bi()));
                    float cl = c * gTuCrL.Next(), cr = c * gTuCrR.Next();
                    farL[s] += l * 0.7f + cl; farR[s] += r * 0.7f + cr;
                    low[s] += (l + r) * 0.3f;
                    echoIn[s] += (cl + cr) * 0.5f + (l + r) * 0.2f;
                }

            if (!(gAshL.Off && gAshR.Off))
                for (int s = 0; s < n; s++)
                {
                    float tk = gAshTick.Next();
                    dryL[s] += ashHpL.Next(ashRngL.Bi()) * gAshL.Next() + ashTickL.Next(ashDustL.Next(ashDens)) * tk;
                    dryR[s] += ashHpR.Next(ashRngR.Bi()) * gAshR.Next() + ashTickR.Next(ashDustR.Next(ashDens)) * tk;
                }
        }
    }
}
