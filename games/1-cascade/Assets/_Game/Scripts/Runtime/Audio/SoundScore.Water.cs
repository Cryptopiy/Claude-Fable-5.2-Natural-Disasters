using System;

namespace CASCADE
{
    /// <summary>
    /// The flank collapse (late rolling rumble, splash boom), the drawdown (sea sucking back, ropes, hulls, the rising
    /// drone), the wave (90 s of growing rumble, freight train) and the one colossal impact.
    /// </summary>
    public sealed partial class SoundScore
    {
        const float GCol = 1.4f, GColMid = 1.1f, GColRock = 0.035f, GBoom = 0.42f;
        const float GSuck = 0.05f, GHarbour = 0.035f, GTimber = 0.03f, DroneDb0 = -50f, DroneDb1 = -25f;
        const float GWave = 5.4f, GImpact = 1.0f;

        BrownNoise colNz; Biquad colLp1, colLp2, colHp; PinkNoise colMidNzL, colMidNzR; Biquad colMidL, colMidR;
        Dust colDust; Biquad colRockBp; Ramp gCol, gColMid, gColRock; float colDens, colRockGain;
        Osc boomOsc; BrownNoise boomNz; Svf boomLp; Ramp gBoom, gBoomHarm, gBoomNz; double boomInc;

        PinkNoise skNzL, skNzR; Svf skBpL, skBpR, skGurg; Dust skDustL, skDustR; Biquad skPebL, skPebR;
        Ramp gSkL, gSkR, gSkPeb, gSkGurg; float skDens, skPebGain; Rng skRng;

        Creak rope0, rope1; ModalBank timber; double thud2At; float thud2Amp;

        Osc dr1, dr2, dr3; Ramp gDrone; double drInc1, drInc2, drInc3;

        BrownNoise wvNz; Biquad wvLp1, wvLp2, wvHp; PinkNoise wvMidNzL, wvMidNzR; Biquad wvMidL, wvMidR;
        Rng wvHiL, wvHiR; Biquad wvHiHpL, wvHiHpR; Ramp gWvSub, gWvMidL, gWvMidR, gWvHi;

        Osc imOsc, imOscH; BrownNoise imNz; Biquad imNzLp, imSatBp; PinkNoise imCrNzL, imCrNzR; Biquad imCrLpL, imCrLpR;
        Ramp gImBody, gImHarm, gImNz, gImCr; double imInc, imIncH; float imGain;

        void InitWater()
        {
            timber = new ModalBank(5, fs, 81u, 3.0f);
            timber.Define(0, 92f, 0.28f, -0.3f, 1f); timber.Define(1, 141f, 0.22f, -0.3f, 0.8f); timber.Define(2, 214f, 0.16f, 0.2f, 0.5f);
            timber.Define(3, 328f, 0.11f, 0.2f, 0.3f); timber.Define(4, 470f, 0.08f, 0f, 0.15f);
        }

        void ResetWater()
        {
            colNz = new BrownNoise(82u, fs); colLp1.Clear(); colLp2.Clear(); colHp.Clear();
            colLp1.LowPass(fs, 58.0, 0.6); colLp2.LowPass(fs, 58.0, 0.9); colHp.HighPass(fs, 20.0, 0.707);
            colMidNzL = new PinkNoise(83u); colMidNzR = new PinkNoise(84u);
            colMidL.Clear(); colMidR.Clear(); colMidL.BandPass(fs, 105.0, 0.6); colMidR.BandPass(fs, 135.0, 0.6);
            colDust = new Dust(85u); colRockBp.Clear(); colRockBp.BandPass(fs, 520.0, 1.0); colRockGain = ImpulseGain(520f, 1f);
            gCol.Snap(0f); gColMid.Snap(0f); gColRock.Snap(0f); colDens = 0f;
            boomOsc = default(Osc); boomNz = new BrownNoise(86u, fs); boomLp.Clear(); boomLp.Set(fs, 200f, 0.8f);
            gBoom.Snap(0f); gBoomHarm.Snap(0f); gBoomNz.Snap(0f); boomInc = 30.0 / fs;

            skNzL = new PinkNoise(87u); skNzR = new PinkNoise(88u);
            skBpL.Clear(); skBpR.Clear(); skGurg.Clear(); skBpL.Set(fs, 1500f, 0.9f); skBpR.Set(fs, 1700f, 0.9f); skGurg.Set(fs, 450f, 7f);
            skDustL = new Dust(89u); skDustR = new Dust(90u);
            skPebL.Clear(); skPebR.Clear(); skPebL.BandPass(fs, 3100.0, 1.2); skPebR.BandPass(fs, 3600.0, 1.2); skPebGain = ImpulseGain(3300f, 1.2f);
            gSkL.Snap(0f); gSkR.Snap(0f); gSkPeb.Snap(0f); gSkGurg.Snap(0f); skDens = 0f; skRng = new Rng(91u);

            rope0 = default(Creak); rope1 = default(Creak); timber.Reset(); thud2At = 1e12; thud2Amp = 0f;

            dr1 = default(Osc); dr2 = default(Osc); dr3 = default(Osc); gDrone.Snap(0f);
            drInc1 = 28.0 / fs; drInc2 = 28.55 / fs; drInc3 = 56.9 / fs;

            wvNz = new BrownNoise(92u, fs); wvLp1.Clear(); wvLp2.Clear(); wvHp.Clear();
            wvLp1.LowPass(fs, 52.0, 0.6); wvLp2.LowPass(fs, 52.0, 0.9); wvHp.HighPass(fs, 20.0, 0.707);
            wvMidNzL = new PinkNoise(93u); wvMidNzR = new PinkNoise(94u);
            wvMidL.Clear(); wvMidR.Clear(); wvMidL.BandPass(fs, 260.0, 0.5); wvMidR.BandPass(fs, 310.0, 0.5);
            wvHiL = new Rng(95u); wvHiR = new Rng(96u); wvHiHpL.Clear(); wvHiHpR.Clear();
            wvHiHpL.HighPass(fs, 2000.0, 0.707); wvHiHpR.HighPass(fs, 2300.0, 0.707);
            gWvSub.Snap(0f); gWvMidL.Snap(0f); gWvMidR.Snap(0f); gWvHi.Snap(0f);

            imOsc = default(Osc); imOscH = default(Osc); imNz = new BrownNoise(97u, fs);
            imNzLp.Clear(); imSatBp.Clear(); imNzLp.LowPass(fs, 300.0, 0.7); imSatBp.BandPass(fs, 200.0, 0.7);
            imCrNzL = new PinkNoise(98u); imCrNzR = new PinkNoise(99u);
            imCrLpL.Clear(); imCrLpR.Clear(); imCrLpL.LowPass(fs, 2800.0, 0.7); imCrLpR.LowPass(fs, 2500.0, 0.7);
            gImBody.Snap(0f); gImHarm.Snap(0f); gImNz.Snap(0f); gImCr.Snap(0f); imInc = 40.0 / fs; imIncH = 100.0 / fs;
        }

        void KillWaterVoices() { rope0.on = false; rope1.on = false; timber.Reset(); thud2At = 1e12; }

        void StartRope(int id, float amp, float pan)
        {
            float h = Dsp.Hash(id, 401), h2 = Dsp.Hash(id, 402);
            float a = GHarbour * Trim[LHarbour] * amp * (0.6f + 0.4f * h2);
            if ((id & 1) == 0 || rope1.on)
                rope0.Trigger(fs, (uint)(id + 900), 70f + 50f * h, 22f + 15f * h2, 0.5f + 1.3f * h, a, 640f + 200f * h, 1050f + 300f * h2, 1650f + 300f * h, 11f, pan, 0.22f);
            else
                rope1.Trigger(fs, (uint)(id + 900), 60f + 40f * h, 18f + 12f * h2, 0.6f + 1.2f * h2, a, 580f + 150f * h2, 930f + 250f * h, 1480f + 300f * h2, 12f, pan, 0.25f);
        }

        void HullThud(int id, float amp)
        {
            float h = Dsp.Hash(id, 403);
            int m = h < 0.5f ? 0 : 1;
            timber.Excite(m, amp); timber.Excite(m + 1, amp * 0.8f); timber.Excite(m + 2, amp * 0.5f); timber.Excite(m + 3, amp * 0.25f);
            thud2At = T + 0.15 + 0.12 * Dsp.Hash(id, 404); thud2Amp = amp * (0.25f + 0.3f * Dsp.Hash(id, 405));
        }

        void CtlWater()
        {
            // ---------- collapse: the rumble arrives CollapseSoundDelay late and rolls for half a minute ----------
            float ce = Dsp.Smooth(0f, 5f, eColSnd) * (1f - Dsp.Smooth(19f, 33f, eColSnd)) * (1f - 0.9f * Dsp.Smooth(2f, 8f, eDraw));
            if (ce > 0f)
            {
                float roll = 0.62f + 0.38f * Dsp.Noise(T * 0.8, 91) + 0.22f * Dsp.Noise(T * 2.1, 92);
                float g = Trim[LCollapse] * ce;
                gCol.To(GCol * g * roll, Block);
                gColMid.To(GColMid * g * (0.6f + 0.4f * Dsp.Noise(T * 1.1, 93)), Block);
                gColRock.To(GColRock * g, Block);
                colDens = 200f * ce * invFs;
            }
            else { gCol.To(0f, Block); gColMid.To(0f, Block); gColRock.To(0f, Block); colDens = 0f; }

            // the splash: a deep boom, swelling in over 0.3 s out of the rumble
            if (eBoom > -0.2f && eBoom < 14f)
            {
                float bp = Math.Max(0f, eBoom), g = GBoom * Trim[LCollapse];
                boomInc = (27f + 20f * Dsp.Exp(-bp / 0.35f)) / fs;
                gBoom.To(g * Dsp.Hit(eBoom, 0.3f, 1.5f), Block);
                gBoomHarm.To(g * Dsp.Hit(eBoom, 0.3f, 0.9f), Block);
                gBoomNz.To(g * 1.8f * Dsp.Hit(eBoom, 0.3f, 2.2f), Block);
                boomLp.Set(fs, 70f + 200f * Dsp.Exp(-bp / 0.8f), 0.8f);
            }
            else { gBoom.To(0f, Block); gBoomHarm.To(0f, Block); gBoomNz.To(0f, Block); }

            // ---------- the sea sucking back over shingle: long hissing draws, pebble clicks, a gurgle, drips ----------
            float sa = Dsp.Smooth(1f, 9f, eDraw) * (1f - Dsp.Smooth(84f, 91f, eWave));
            if (sa > 0f)
            {
                double cyc = eDraw / 6.3 + 0.1 * Dsp.Noise(eDraw * 0.21, 95);
                float p = (float)(cyc - Math.Floor(cyc));
                float h = p < 0.08f ? Dsp.Smooth(0f, 0.08f, p) : Dsp.Exp(-(p - 0.08f) * 2.6f);
                float fc = 600f + 2400f * Dsp.Exp(-p * 3f);
                skBpL.Set(fs, fc, 0.9f); skBpR.Set(fs, fc * 1.13f, 0.9f);
                float g = GSuck * Trim[LSuck] * sa;
                gSkL.To(g * (0.18f + h), Block);
                gSkR.To(g * (0.18f + h * (0.8f + 0.2f * Dsp.Noise(T * 0.7, 97))), Block);
                skDens = (40f + 480f * Dsp.Exp(-p * 3.5f)) * invFs;
                gSkPeb.To(g * 0.35f, Block);
                skGurg.Set(fs, 450f + 250f * Dsp.Noise(T * 5.5, 96), 7f);
                gSkGurg.To(g * 0.22f * (0.3f + 0.7f * h), Block);
                if (advancing && skRng.Uni() < 3f * ctlDt)
                    blips.Trigger(fs, 900f + 1500f * skRng.Uni(), 30f + 40f * skRng.Uni(), g * (0.15f + 0.3f * skRng.Uni()),
                                  0.012f + 0.02f * skRng.Uni(), skRng.Bi() * 0.9f);
            }
            else { gSkL.To(0f, Block); gSkR.To(0f, Block); gSkPeb.To(0f, Block); gSkGurg.To(0f, Block); skDens = 0f; }

            // ---------- ropes and hulls: beside the quay in the morning, far below during the drawdown ----------
            if (advancing)
            {
                if (eHum < 12f)
                {
                    int k = (int)Math.Floor(T / 9.5);
                    if (Dsp.Hash(k, 141) < 0.7f && Fire(9.5 * k + 1.0 + 6.5 * Dsp.Hash(k, 142))) StartRope(k, 0.55f, Dsp.Hash(k, 143) * 1.4f - 0.9f);
                    int q = (int)Math.Floor(T / 8.0);
                    if (Dsp.Hash(q, 144) < 0.6f && Fire(8.0 * q + 2.0 + 5.0 * Dsp.Hash(q, 145))) HullThud(q, 0.45f);
                }
                double xd = T - sDraw;
                if (xd > 3.0 && eWave < 78f)
                {
                    int k = (int)Math.Floor(xd / 5.5);
                    if (Dsp.Hash(k, 146) < 0.8f && Fire(sDraw + 5.5 * k + 0.5 + 4.0 * Dsp.Hash(k, 147))) StartRope(k + 50, 1f, Dsp.Hash(k, 150) * 1.6f - 0.8f);
                    int q = (int)Math.Floor(xd / 7.0);
                    if (q >= 1 && Dsp.Hash(q, 148) < 0.85f && Fire(sDraw + 7.0 * q + 0.8 + 5.0 * Dsp.Hash(q, 149))) HullThud(q + 50, 1f);
                }
                if (Fire(thud2At)) { timber.Excite(1, thud2Amp); timber.Excite(2, thud2Amp * 0.6f); thud2At = 1e12; }
            }

            // ---------- the drone: 28 -> 40 Hz with a slow beat and a quiet octave, growing through the wait ----------
            float dl = 0f;
            if (eDraw > 2f)
            {
                float uu = Dsp.Sat(eDraw / 100f);
                dl = Dsp.Db(DroneDb0 + (DroneDb1 - DroneDb0) * (float)Math.Pow(uu, 1.2)) * Dsp.Smooth(2f, 10f, eDraw)
                   * (1f - Dsp.Smooth(0f, 3f, eImp)) * impDip * Trim[LDrone];
                float f = 28f + 12f * Dsp.Smooth(0f, 105f, eDraw);
                drInc1 = f / fs; drInc2 = (f + 0.55f) / fs; drInc3 = (2f * f + 0.9f) / fs;
            }
            gDrone.To(dl, Block);

            // ---------- the wave: inaudible at birth, +0.38 dB/s, a freight train in the last 25 s ----------
            if (eWave > 0f && eImp < 60f)
            {
                float sub, mid, hiss;
                if (eImp < 0f)
                {
                    float w = eWave;
                    sub = Dsp.Db(w <= 65f ? -54f + 0.38f * w : -29.3f + 0.62f * (w - 65f)) * Dsp.Smooth(0f, 6f, w);
                    mid = w > 28f ? Dsp.Db(w <= 65f ? -62f + 0.65f * (w - 28f) : -37.95f + 0.769f * (w - 65f)) * Dsp.Smooth(28f, 34f, w) : 0f;
                    hiss = w > 66f ? Dsp.Db(-72f + 1.5f * (w - 66f)) * Dsp.Smooth(66f, 70f, w) : 0f;
                }
                else
                {
                    // the train has arrived: its energy is in the impact now, the rest sinks away
                    sub = Dsp.Db(-12.8f) * Dsp.Exp(-eImp / 6f) * (1f - Dsp.Smooth(40f, 60f, eImp));
                    mid = Dsp.Db(-17.5f) * Dsp.Exp(-eImp / 3f);
                    hiss = Dsp.Db(-33.6f) * Dsp.Exp(-eImp / 2f);
                }
                float g = GWave * Trim[LWave] * (eImp < 0f ? impDip : 0.3f);
                float prox = Dsp.Smooth(40f, 88f, eWave);
                gWvSub.To(g * sub * (0.85f + 0.15f * Dsp.Noise(T * 0.6, 113)), Block);
                gWvMidL.To(g * mid * (1f - 0.45f * prox * Math.Abs(Dsp.Noise(T * 7.3, 111))), Block);       // the chug
                gWvMidR.To(g * mid * (1f - 0.45f * prox * Math.Abs(Dsp.Noise(T * 8.9, 112))), Block);
                gWvHi.To(g * hiss, Block);
            }
            else { gWvSub.To(0f, Block); gWvMidL.To(0f, Block); gWvMidR.To(0f, Block); gWvHi.To(0f, Block); }

            // ---------- the impact (60 -> 25 Hz chirp, 140 -> 92 Hz partial, broadband burst) and the smaller second wave ----------
            float aB = Dsp.Hit(eImp, 0.01f, 1.2f) + 0.2f * Dsp.Hit(eImp2, 0.08f, 1.0f);
            float aC = Dsp.Hit(eImp, 0.02f, 1.6f) + 0.4f * Dsp.Hit(eImp2, 0.1f, 1.8f);
            if (aB + aC > 1e-5f)
            {
                bool second = eImp2 > -0.2f;
                float y = Math.Max(0f, second ? eImp2 : eImp), g = GImpact * Trim[LImpact];
                imInc = (second ? 24f + 24f * Dsp.Exp(-y / 0.4f) : 25f + 35f * Dsp.Exp(-y / 0.35f)) / fs;
                imIncH = (second ? 85f + 35f * Dsp.Exp(-y / 0.5f) : 92f + 48f * Dsp.Exp(-y / 0.5f)) / fs;
                imGain = g;
                gImBody.To(aB, Block);                                         // drives the saturator: the body holds its level for a second
                gImHarm.To(g * 0.45f * (Dsp.Hit(eImp, 0.01f, 1.2f) + 0.3f * Dsp.Hit(eImp2, 0.08f, 0.8f)), Block);
                gImNz.To(g * 0.4f * (Dsp.Hit(eImp, 0.012f, 0.6f) + 0.35f * Dsp.Hit(eImp2, 0.08f, 0.7f)), Block);
                gImCr.To(g * 1.6f * aC, Block);
            }
            else { gImBody.To(0f, Block); gImHarm.To(0f, Block); gImNz.To(0f, Block); gImCr.To(0f, Block); }
        }

        void RunWater(int n)
        {
            if (!(gCol.Off && gColMid.Off))
                for (int s = 0; s < n; s++)
                {
                    low[s] += colHp.Next(colLp2.Next(colLp1.Next(colNz.Next()))) * gCol.Next();
                    float m = gColMid.Next();
                    float rk = colRockBp.Next(colDust.Next(colDens)) * colRockGain * gColRock.Next();
                    farL[s] += colMidL.Next(colMidNzL.Next()) * m + rk * 0.9f;
                    farR[s] += colMidR.Next(colMidNzR.Next()) * m + rk * 0.6f;
                    echoIn[s] += rk * 0.5f;
                }

            if (!(gBoom.Off && gBoomNz.Off))
                for (int s = 0; s < n; s++)
                {
                    float y = boomOsc.Sine(boomInc) * gBoom.Next() + (0.4f * boomOsc.Harm(2) + 0.16f * boomOsc.Harm(3)) * gBoomHarm.Next()
                            + boomLp.Lp(boomNz.Next()) * gBoomNz.Next();
                    low[s] += y; echoIn[s] += y * 0.5f;
                }

            if (!(gSkL.Off && gSkR.Off))
                for (int s = 0; s < n; s++)
                {
                    float pl = skNzL.Next(), pr = skNzR.Next(), pg = gSkPeb.Next();
                    float gu = skGurg.Lp(pl + pr) * gSkGurg.Next();
                    farL[s] += skBpL.Bp(pl) * gSkL.Next() + skPebL.Next(skDustL.Next(skDens)) * skPebGain * pg + gu;
                    farR[s] += skBpR.Bp(pr) * gSkR.Next() + skPebR.Next(skDustR.Next(skDens)) * skPebGain * pg + gu;
                }

            rope0.Run(farL, farR, n); rope1.Run(farL, farR, n);
            timber.Run(farL, farR, n, GTimber * Trim[LHarbour]);

            if (!gDrone.Off)
                for (int s = 0; s < n; s++)
                {
                    float y = 0.5f * (dr1.Sine(drInc1) + dr2.Sine(drInc2)) + 0.2f * (dr1.Harm(2) + dr3.Sine(drInc3)) + 0.07f * dr1.Harm(3) + 0.05f * dr1.Harm(4);
                    low[s] += y * gDrone.Next();
                }

            if (!(gWvSub.Off && gWvMidL.Off && gWvMidR.Off && gWvHi.Off))
                for (int s = 0; s < n; s++)
                {
                    low[s] += wvHp.Next(wvLp2.Next(wvLp1.Next(wvNz.Next()))) * gWvSub.Next();
                    float hi = gWvHi.Next();
                    farL[s] += wvMidL.Next(wvMidNzL.Next()) * gWvMidL.Next() + wvHiHpL.Next(wvHiL.Bi()) * hi;
                    farR[s] += wvMidR.Next(wvMidNzR.Next()) * gWvMidR.Next() + wvHiHpR.Next(wvHiR.Bi()) * hi;
                }

            if (!(gImBody.Off && gImHarm.Off && gImNz.Off && gImCr.Off))
                for (int s = 0; s < n; s++)
                {
                    float raw = imOsc.Sine(imInc), gb = gImBody.Next();
                    float lowSum = (Dsp.Tanh(2.6f * raw * gb) * 0.84f + imSatBp.Next(Dsp.Tanh(2.5f * raw)) * gb * 0.2f) * imGain
                                 + (imOscH.Sine(imIncH) + 0.4f * imOscH.Harm(2)) * gImHarm.Next() + imNzLp.Next(imNz.Next()) * gImNz.Next();
                    float gc = gImCr.Next();
                    float cl = imCrLpL.Next(imCrNzL.Next()) * gc, cr = imCrLpR.Next(imCrNzR.Next()) * gc;
                    low[s] += lowSum; farL[s] += cl; farR[s] += cr;
                    echoIn[s] += lowSum * 0.3f + (cl + cr) * 0.3f;
                }
        }
    }
}
