using System;

namespace CASCADE
{
    /// <summary>
    /// After the impact (spray rain, pouring water, distant breaking masonry), the ash storm's steam bursts, and the ending:
    /// one distant metallic creak and the slow lighthouse mechanism. Wind, ash hiss, thunder and the far roar are driven
    /// by the same staging values in the other layer files.
    /// </summary>
    public sealed partial class SoundScore
    {
        const float GSpray = 0.075f, GPour = 0.55f, GMasonry = 0.5f, GStone = 0.09f, GSteam = 0.16f, GCreak = 0.02f, GMech = 0.035f, GWhirr = 0.2f;

        Rng spRngL, spRngR; Biquad spHpL, spHpR; Dust spDust; Svf spDrop; Ramp gSpL, gSpR, gSpDrop; float spDens, spDropGain; Rng spPick;
        PinkNoise poNzL, poNzR; Svf poBpL, poBpR; Ramp gPoL, gPoR;
        ModalBank stone; PinkNoise rubNz; Biquad rubBp; Ramp gRub;
        Rng stRng; Svf stBpL, stBpR; BrownNoise stWhNz; Biquad stWhBp; Ramp gStL, gStR, gStWh;
        Creak metal; ModalBank mech; PinkNoise whNz; Svf whBp; Ramp gWhirr;

        void InitStorm()
        {
            stone = new ModalBank(5, fs, 111u, 4.0f);
            stone.Define(0, 58f, 0.45f, -0.4f, 1f); stone.Define(1, 83f, 0.40f, 0.3f, 0.9f); stone.Define(2, 121f, 0.30f, -0.2f, 0.7f);
            stone.Define(3, 168f, 0.22f, 0.5f, 0.5f); stone.Define(4, 236f, 0.15f, 0f, 0.35f);
            mech = new ModalBank(4, fs, 112u, 1.0f);
            mech.Define(0, 900f, 0.05f, 0.3f, 1f); mech.Define(1, 2200f, 0.035f, 0.3f, 0.6f);
            mech.Define(2, 310f, 0.12f, 0.25f, 0.8f); mech.Define(3, 1450f, 0.04f, 0.35f, 0.5f);
        }

        void ResetStorm()
        {
            spRngL = new Rng(113u); spRngR = new Rng(114u); spHpL.Clear(); spHpR.Clear();
            spHpL.HighPass(fs, 3200.0, 0.707); spHpR.HighPass(fs, 3600.0, 0.707);
            spDust = new Dust(115u); spDrop.Clear(); spDrop.Set(fs, 3500f, 2f); spDropGain = ImpulseGain(3500f, 2f); spPick = new Rng(116u);
            gSpL.Snap(0f); gSpR.Snap(0f); gSpDrop.Snap(0f); spDens = 0f;
            poNzL = new PinkNoise(117u); poNzR = new PinkNoise(118u); poBpL.Clear(); poBpR.Clear();
            poBpL.Set(fs, 520f, 0.8f); poBpR.Set(fs, 600f, 0.8f); gPoL.Snap(0f); gPoR.Snap(0f);
            stone.Reset(); rubNz = new PinkNoise(119u); rubBp.Clear(); rubBp.BandPass(fs, 420.0, 0.8); gRub.Snap(0f);
            stRng = new Rng(120u); stBpL.Clear(); stBpR.Clear(); stBpL.Set(fs, 2000f, 0.9f); stBpR.Set(fs, 2200f, 0.9f);
            stWhNz = new BrownNoise(121u, fs); stWhBp.Clear(); stWhBp.BandPass(fs, 70.0, 1.2);
            gStL.Snap(0f); gStR.Snap(0f); gStWh.Snap(0f);
            metal = default(Creak); mech.Reset();
            whNz = new PinkNoise(122u); whBp.Clear(); whBp.Set(fs, 300f, 3f); gWhirr.Snap(0f);
        }

        void KillStormVoices() { stone.Reset(); mech.Reset(); metal.on = false; }

        void CtlStorm()
        {
            // ---------- spray: ten seconds of hiss raining on the camera after the impact, a little again after the second wave ----------
            float sp = Dsp.Smooth(0.25f, 2.2f, eImp) * (1f - Dsp.Smooth(3.5f, 11.5f, eImp)) + 0.35f * Dsp.Win(eImp2 - 0.3f, 2f, 9f, 6f);
            float sg = GSpray * Trim[LAfter] * sp;
            gSpL.To(sg * (0.75f + 0.25f * Dsp.Noise(T * 1.9, 151)), Block);
            gSpR.To(sg * (0.75f + 0.25f * Dsp.Noise(T * 1.9, 152)), Block);
            gSpDrop.To(sg * 1.6f, Block);
            spDens = (60f + 220f * sp) * sp * invFs;
            if (sp > 0f)
            {
                float hz = 2200f + 3500f * spPick.Uni();
                spDrop.Set(fs, hz, 2f); spDropGain = ImpulseGain(hz, 2f);
            }

            // ---------- water pouring off roofs and stairs; it swells again before the second impact ----------
            float po = Dsp.Smooth(1f, 6f, eImp) * (1f - 0.85f * Dsp.Smooth(8f, 46f, eImp)) + 0.7f * Dsp.Win(eImp - 27f, 9f, 24f, 12f);
            po *= 1f - Dsp.Smooth(52f, 80f, eImp);
            if (po > 0f)
            {
                poBpL.Set(fs, 520f + 260f * Dsp.Noise(T * 0.6, 153), 0.8f);
                poBpR.Set(fs, 600f + 280f * Dsp.Noise(T * 0.5, 154), 0.8f);
            }
            float pg = GPour * Trim[LAfter] * po;
            gPoL.To(pg * (0.8f + 0.2f * Dsp.Noise(T * 1.3, 155)), Block);
            gPoR.To(pg * (0.8f + 0.2f * Dsp.Noise(T * 1.3, 156)), Block);

            // ---------- distant masonry: sparse low cracks with a tail of sliding rubble ----------
            float rub = 0f;
            if (eImp > 1.5f && eImp < 58f)
            {
                int k0 = (int)(eImp / 3.7f);
                for (int k = k0 - 1; k <= k0; k++)
                {
                    if (k < 0 || Dsp.Hash(k, 161) > 0.75f) continue;
                    float st = 3.7f * k + 1.5f + 2.5f * Dsp.Hash(k, 162);
                    if (st > 50f) continue;
                    float amp = (0.4f + 0.6f * Dsp.Hash(k, 163)) * (1f - 0.5f * Dsp.Smooth(20f, 50f, st));
                    int m = (int)(Dsp.Hash(k, 166) * 2.99f);
                    if (Fire(sImp + st)) { stone.Excite(m, amp); stone.Excite(m + 1, amp * 0.7f); stone.Excite(m + 2, amp * 0.4f); }
                    if (Fire(sImp + st + 0.22 + 0.3 * Dsp.Hash(k, 167))) { stone.Excite(m + 1, amp * 0.5f); stone.Excite(m + 2, amp * 0.35f); }
                    rub += amp * Dsp.Hit(eImp - st, 0.03f, 0.35f + 0.7f * Dsp.Hash(k, 164))
                         * (0.55f + 0.45f * Math.Abs(Dsp.Noise(T * 23.0, 165 + (k & 3))));
                }
            }
            gRub.To(GMasonry * Trim[LAfter] * rub, Block);

            // ---------- steam bursts where the currents meet the sea: white-noise swells with a low whump ----------
            float stG = 0f, stFc = 0f, whump = 0f, sx = eStorm - 6f;
            if (sx > 0f && eSil < 10f)
            {
                int k0 = (int)(sx / 7.5f);
                for (int k = k0 - 1; k <= k0; k++)
                {
                    if (k < 0 || Dsp.Hash(k, 171) > 0.65f) continue;
                    float y = sx - (7.5f * k + 4.5f * Dsp.Hash(k, 172));
                    float dur = 1.6f + 2.6f * Dsp.Hash(k, 173);
                    if (y <= 0f || y >= dur) continue;
                    float amp = 0.4f + 0.6f * Dsp.Hash(k, 174);
                    float e = amp * Dsp.Smooth(0f, 0.3f, y) * (1f - Dsp.Smooth(dur * 0.35f, dur, y));
                    stG += e; stFc += e * (2800f - 1500f * y / dur);
                    whump += amp * Dsp.Hit(y, 0.15f, 0.5f);
                }
            }
            float sgain = GSteam * Trim[LSteam] * (1f - Dsp.Smooth(0f, 10f, eSil)) * (1f + 0.6f * glimpse);
            if (stG > 1e-4f) { float fc = stFc / stG; stBpL.Set(fs, fc, 0.9f); stBpR.Set(fs, fc * 1.1f, 0.9f); }
            float am = 0.6f + 0.4f * Dsp.Noise(T * 37.0, 175);
            gStL.To(sgain * stG * am * 0.75f, Block); gStR.To(sgain * stG * (0.6f + 0.4f * Dsp.Noise(T * 41.0, 176)), Block);
            gStWh.To(sgain * 4f * whump, Block);

            // ---------- ending: one distant metallic creak; the lighthouse mechanism every 4 s, growing as we approach ----------
            if (advancing)
            {
                if (Fire(sSil + 21.0))
                    metal.Trigger(fs, 501u, 31f, 13f, 2.6f, GCreak * Trim[LEnding], 430f, 790f, 1210f, 22f, -0.5f, 0.12f);
                double xs = T - sSil - 6.0;
                if (xs > -1.0 && xs < 4000.0)
                {
                    float grow = eArr > 0f ? 1f : Dsp.Smooth(4f, 58f, eSil);
                    float a = Dsp.Db(-24f * (1f - grow));
                    double k = Math.Floor(xs / 4.0);
                    if (k >= 0.0 && Fire(sSil + 6.0 + 4.0 * k)) { mech.Strike(0, a); mech.Strike(1, a * 0.6f); mech.Strike(2, a * 0.8f); }
                    if (k >= 0.0 && Fire(sSil + 6.42 + 4.0 * k)) { mech.Strike(2, a * 0.5f); mech.Strike(3, a * 0.45f); }
                }
            }
            float wh = 0f;
            if (eSil > 5f)
            {
                float ph = (eSil - 6f) - 4f * (float)Math.Floor((eSil - 6f) / 4f);          // 0..4, the tick is at 0
                float y = ph >= 3.3f ? ph - 3.3f : ph + 0.7f;                             // the whirr leads the tick by 0.7 s
                float grow = eArr > 0f ? 1f : Dsp.Smooth(4f, 58f, eSil);
                wh = Dsp.Win(y, 0.45f, 1.0f, 0.2f) * Dsp.Db(-24f * (1f - grow));
                if (wh > 0f) whBp.Set(fs, 270f + 150f * y, 3f);
            }
            gWhirr.To(GWhirr * Trim[LEnding] * wh, Block);
        }

        void RunStorm(int n)
        {
            if (!(gSpL.Off && gSpR.Off))
                for (int s = 0; s < n; s++)
                {
                    float d = spDrop.Bp(spDust.Next(spDens)) * spDropGain * gSpDrop.Next();
                    dryL[s] += spHpL.Next(spRngL.Bi()) * gSpL.Next(); dryR[s] += spHpR.Next(spRngR.Bi()) * gSpR.Next();
                    nearL[s] += d * 0.7f; nearR[s] += d * 0.9f;
                }

            if (!(gPoL.Off && gPoR.Off))
                for (int s = 0; s < n; s++)
                {
                    farL[s] += poBpL.Bp(poNzL.Next()) * gPoL.Next(); farR[s] += poBpR.Bp(poNzR.Next()) * gPoR.Next();
                }

            stone.Run(farL, farR, n, GStone * Trim[LAfter]);
            if (!gRub.Off)
                for (int s = 0; s < n; s++)
                {
                    float y = rubBp.Next(rubNz.Next()) * gRub.Next();
                    farL[s] += y * 0.8f; farR[s] += y * 0.65f; echoIn[s] += y * 0.5f;
                }

            if (!(gStL.Off && gStR.Off && gStWh.Off))
                for (int s = 0; s < n; s++)
                {
                    float w = stRng.Bi();
                    farL[s] += stBpL.Bp(w) * gStL.Next(); farR[s] += stBpR.Bp(w) * gStR.Next();
                    low[s] += stWhBp.Next(stWhNz.Next()) * gStWh.Next();
                }

            if (metal.on)
            {
                for (int s = 0; s < n; s++) { tA[s] = 0f; tB[s] = 0f; }
                metal.Run(tA, tB, n);
                for (int s = 0; s < n; s++) { farL[s] += tA[s]; farR[s] += tB[s]; echoIn[s] += (tA[s] + tB[s]) * 0.35f; }
            }
            mech.Run(nearL, nearR, n, GMech * Trim[LEnding]);
            if (!gWhirr.Off)
                for (int s = 0; s < n; s++)
                {
                    float y = whBp.Bp(whNz.Next()) * gWhirr.Next();
                    nearL[s] += y * 0.75f; nearR[s] += y;
                }
        }
    }
}
