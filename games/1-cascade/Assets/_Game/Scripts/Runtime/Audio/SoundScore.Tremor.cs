using System;

namespace CASCADE
{
    /// <summary>
    /// Tremors: the low rumble swell, rattling glass and crockery, shutters, the chapel bell ringing by itself,
    /// pebbles near the path and the distant rockfall across the bay. The village's answer to the thump lives here too.
    /// </summary>
    public sealed partial class SoundScore
    {
        const float GTrem = 1.4f, GTremMid = 1.0f, GRattle = 0.055f, GShutter = 0.11f, GRock = 0.06f, GPebble = 0.045f;

        BrownNoise trNz; Biquad trLp1, trLp2, trHp; PinkNoise trMidNzL, trMidNzR; Biquad trMidL, trMidR; Ramp gTrem, gTremMid;
        ModalBank rattle, wood; Rng ratRng; float ratTimer0, ratTimer1, ratTimer2;
        Dust rockDustL, rockDustR; Biquad rockBpL, rockBpR, rockLoL, rockLoR; Ramp gRock; float rockDens, rockHi, rockLo;
        Dust pebDust; Svf pebBp; Ramp gPeb; float pebDens, pebGain; Rng pebRng;

        void InitTremor()
        {
            rattle = new ModalBank(9, fs, 41u, 0.6f);
            // glass (0-2, 8), crockery (3-5), cutlery (6-7)
            rattle.Define(0, 3120f, 0.11f, -0.6f, 1f); rattle.Define(1, 4350f, 0.09f, -0.6f, 0.9f); rattle.Define(2, 5680f, 0.07f, -0.5f, 0.8f);
            rattle.Define(3, 1870f, 0.06f, 0.5f, 1f); rattle.Define(4, 2410f, 0.05f, 0.5f, 0.9f); rattle.Define(5, 2960f, 0.045f, 0.6f, 0.8f);
            rattle.Define(6, 3740f, 0.16f, 0.1f, 0.7f); rattle.Define(7, 5120f, 0.13f, 0.15f, 0.6f); rattle.Define(8, 6900f, 0.05f, -0.2f, 0.6f);
            wood = new ModalBank(4, fs, 42u, 2.0f);
            wood.Define(0, 185f, 0.10f, 0.4f, 1f); wood.Define(1, 262f, 0.09f, 0.4f, 0.8f);
            wood.Define(2, 415f, 0.07f, -0.3f, 0.7f); wood.Define(3, 630f, 0.05f, -0.3f, 0.5f);
        }

        void ResetTremor()
        {
            trNz = new BrownNoise(51u, fs);
            trLp1.Clear(); trLp2.Clear(); trHp.Clear();
            trLp1.LowPass(fs, 62.0, 0.6); trLp2.LowPass(fs, 62.0, 0.9); trHp.HighPass(fs, 22.0, 0.707);
            trMidNzL = new PinkNoise(52u); trMidNzR = new PinkNoise(53u);
            trMidL.Clear(); trMidR.Clear(); trMidL.BandPass(fs, 112.0, 0.8); trMidR.BandPass(fs, 124.0, 0.8);
            gTrem.Snap(0f); gTremMid.Snap(0f);
            rattle.Reset(); wood.Reset(); ratRng = new Rng(54u); ratTimer0 = ratTimer1 = ratTimer2 = 0f;
            rockDustL = new Dust(55u); rockDustR = new Dust(56u);
            rockBpL.Clear(); rockBpR.Clear(); rockLoL.Clear(); rockLoR.Clear();
            rockBpL.BandPass(fs, 850.0, 1.3); rockBpR.BandPass(fs, 980.0, 1.3); rockLoL.BandPass(fs, 140.0, 2.5); rockLoR.BandPass(fs, 160.0, 2.5);
            rockHi = ImpulseGain(900f, 1.3f); rockLo = 0.6f * ImpulseGain(150f, 2.5f);
            gRock.Snap(0f); rockDens = 0f;
            pebDust = new Dust(57u); pebBp.Clear(); pebBp.Set(fs, 3200f, 3f); pebGain = ImpulseGain(3200f, 3f);
            gPeb.Snap(0f); pebDens = 0f; pebRng = new Rng(58u);
        }

        void KillTremorVoices() { rattle.Reset(); wood.Reset(); }

        /// <summary>Irregular soft strokes of the chapel bell while the ground moves.</summary>
        void TremorBell(double s0, float dur, float amp, int seed)
        {
            double x = T - s0;
            if (x < 0.0 || x > dur * 1.35 + 1.0) return;
            int k0 = (int)Math.Floor(x / 0.85);
            for (int k = k0 - 1; k <= k0; k++)
            {
                if (k < 0 || Dsp.Hash(k, seed) > 0.72f) continue;
                float at = 0.85f * k + 0.25f + 0.5f * Dsp.Hash(k, seed + 1);
                if (!Fire(s0 + at)) continue;
                float e = Dsp.Swell(at, dur);
                if (e > 0.05f) bell.Strike(amp * e * (0.5f + 0.5f * Dsp.Hash(k, seed + 2)), 0.45f + 0.5f * amp);
            }
        }

        void Knock(int pair, float amp)
        {
            int m = pair * 2;
            wood.Excite(m, amp); wood.Excite(m + 1, amp * 0.7f);
        }

        void RattleObject(ref float timer, float rate, int first, int count, int id, float shake)
        {
            timer -= ctlDt;
            if (timer > 0f) return;
            timer = (0.7f + 0.6f * ratRng.Uni()) / rate;
            // every object chatters in bursts; stronger shaking wakes more of them
            if (Dsp.Noise(T * 2.3 + id * 5.7, 131 + id) < 0.3f - shake * 1.1f) return;
            rattle.Excite(first + (int)(ratRng.Uni() * (count - 0.01f)), shake * (0.35f + 0.65f * ratRng.Uni()));
        }

        void CtlTremor()
        {
            // ---------- rumble: 25-70 Hz noise band with a 110 Hz body for small speakers ----------
            float am = 0.72f + 0.28f * Dsp.Noise(T * 1.3, 41) + 0.18f * Dsp.Noise(T * 3.1, 42);
            gTrem.To(GTrem * Trim[LTremor] * tremor * am, Block);
            gTremMid.To(GTremMid * Trim[LTremor] * tremor * (0.7f + 0.3f * Dsp.Noise(T * 2.2, 43)), Block);

            // ---------- rattles: glass, crockery, cutlery; also the village's answer 0.3 s after the thump ----------
            float answer = Dsp.Hit(eThump - 0.3f, 0.02f, 0.6f);
            float shake = Math.Max(tremor, 0.55f * answer);
            if (shake > 0.02f && advancing)
            {
                RattleObject(ref ratTimer0, 31f, 0, 3, 0, shake);
                RattleObject(ref ratTimer1, 22f, 3, 3, 1, shake);
                RattleObject(ref ratTimer2, 17f, 6, 3, 2, shake);
            }
            if (advancing)
            {
                // shutters knock when the pressure wave arrives, and in the violent tremor
                if (Fire(sThump + 0.30)) Knock(0, 1f);
                if (Fire(sThump + 0.44)) Knock(1, 0.6f);
                if (Fire(sThump + 0.63)) Knock(0, 0.4f);
                if (Fire(sThump + 0.95)) Knock(1, 0.22f);
                if (Fire(sT3 + 1.3)) Knock(1, 0.5f);
                if (Fire(sT3 + 2.9)) Knock(0, 0.7f);
                if (Fire(sT3 + 3.4)) Knock(1, 0.4f);
                if (Fire(sT3 + 5.2)) Knock(0, 0.35f);
                // the bell rings by itself
                TremorBell(sT1, 4f, 0.30f, 201);
                TremorBell(sT2, 8f, 0.50f, 211);
                TremorBell(sT3, 7f, 0.80f, 221);
                if (Fire(sThump + 0.36)) bell.Strike(0.42f, 0.7f);
                if (Fire(sThump + 1.15)) bell.Strike(0.2f, 0.5f);
            }

            // ---------- rocks: a distant fall across the bay 1.5 s after tremor 2 begins, a bigger one in tremor 3 ----------
            float rf = Math.Max(Dsp.Win(eT2 - 1.5f, 1.5f, 11f, 6f), 1.2f * Dsp.Win(eT3 - 1f, 1.2f, 12f, 6f));
            rf *= 0.65f + 0.35f * Dsp.Noise(T * 0.9, 51);
            rockDens = rf > 0f ? (40f + 260f * rf) * invFs : 0f;
            gRock.To(GRock * Trim[LRocks] * rf, Block);
            // pebbles skipping down beside the path
            pebDens = 22f * tremor * invFs;
            gPeb.To(GPebble * Trim[LRocks] * Dsp.Sat(tremor * 2.5f), Block);
            if (tremor > 0f)
            {
                float hz = 2000f + 3000f * pebRng.Uni();
                pebBp.Set(fs, hz, 3f); pebGain = ImpulseGain(hz, 3f);
            }
        }

        void RunTremor(int n)
        {
            if (!(gTrem.Off && gTremMid.Off))
                for (int s = 0; s < n; s++)
                {
                    low[s] += trHp.Next(trLp2.Next(trLp1.Next(trNz.Next()))) * gTrem.Next();
                    float m = gTremMid.Next();
                    dryL[s] += trMidL.Next(trMidNzL.Next()) * m; dryR[s] += trMidR.Next(trMidNzR.Next()) * m;
                }

            rattle.Run(nearL, nearR, n, GRattle * Trim[LRattle]);
            wood.Run(nearL, nearR, n, GShutter * Trim[LRattle]);

            if (!gRock.Off)
                for (int s = 0; s < n; s++)
                {
                    float g = gRock.Next();
                    float dl = rockDustL.Next(rockDens), dr = rockDustR.Next(rockDens);
                    float l = (rockBpL.Next(dl) * rockHi + rockLoL.Next(dl) * rockLo) * g;
                    float r = (rockBpR.Next(dr) * rockHi + rockLoR.Next(dr) * rockLo) * g;
                    farL[s] += l; farR[s] += r; echoIn[s] += (l + r) * 0.4f;
                }

            if (!gPeb.Off)
                for (int s = 0; s < n; s++)
                {
                    float y = pebBp.Bp(pebDust.Next(pebDens)) * pebGain * gPeb.Next();
                    nearL[s] += y * 0.8f; nearR[s] += y * 0.55f;
                }
        }
    }
}
