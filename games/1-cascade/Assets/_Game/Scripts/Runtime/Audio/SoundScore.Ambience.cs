using System;

namespace CASCADE
{
    /// <summary>Morning layers: gulls, water lapping on stone, the chapel bell, wind and wires, the deep hum.</summary>
    public sealed partial class SoundScore
    {
        // levels (linear unless named Db)
        const float GGull = 0.11f, GLap = 0.095f, GBell = 0.15f, GWind = 1.5f, GWire = 1.0f;
        const float HumDb0 = -50f, HumDb1 = -29f;
        // gull cry groups: one slot every 5 s. The seed was searched so that on the nominal timeline the morning opens with a
        // cry at 1.8 s and a group that starts at 75.4 s is cut in the middle of its second note by the take-off at 76 s.
        const double GullSlot = 5.0;
        const int GullSeed = 1791;

        struct GullVoice
        {
            public bool on; public float t, dur, f0, amp, gl, gr;
            public Osc osc; public Lfo vib, rasp; public Biquad f1, f2; public Rng rng; public float raspHz;
        }
        readonly GullVoice[] gull = new GullVoice[4];
        int gullNext;
        Ramp gGull;

        PinkNoise lapNzL, lapNzR; Svf lapBpL, lapBpR; Biquad lapLoL, lapLoR; OnePole lapBedL, lapBedR, lapBedHpL, lapBedHpR;
        Ramp gLapL, gLapR, gLapLoL, gLapLoR, gLapBed;
        Rng lapRng; Blips blips;

        Bell bell; Ramp gBell;

        PinkNoise windNzL, windNzR; Svf windFL, windFR, whisL, whisR; OnePole windHpL, windHpR; Ramp gWindL, gWindR, gWhisL, gWhisR;
        Rng wireRng; Svf wire0, wire1, wire2; Ramp gWire0, gWire1, gWire2;

        Osc hum1, hum2; BrownNoise humNz; Biquad humBp; Ramp gHum; double humInc1, humInc2;

        void InitAmbience()
        {
            blips = new Blips();
            bell = new Bell(fs, 590f, 0.6f, 77u);
        }

        void ResetAmbience()
        {
            for (int i = 0; i < gull.Length; i++) gull[i] = default(GullVoice);
            gullNext = 0; gGull.Snap(0f);
            lapNzL = new PinkNoise(11u); lapNzR = new PinkNoise(12u);
            lapBpL.Clear(); lapBpR.Clear(); lapBpL.Set(fs, 600f, 1.1f); lapBpR.Set(fs, 600f, 1.1f);
            lapLoL.Clear(); lapLoR.Clear(); lapLoL.BandPass(fs, 170.0, 1.4); lapLoR.BandPass(fs, 190.0, 1.4);
            lapBedL = default(OnePole); lapBedR = default(OnePole); lapBedL.SetHz(fs, 900f); lapBedR.SetHz(fs, 900f);
            lapBedHpL = default(OnePole); lapBedHpR = default(OnePole); lapBedHpL.SetHz(fs, 140f); lapBedHpR.SetHz(fs, 140f);
            gLapL.Snap(0f); gLapR.Snap(0f); gLapLoL.Snap(0f); gLapLoR.Snap(0f); gLapBed.Snap(0f);
            lapRng = new Rng(13u); blips.Reset();
            bell.Reset(); gBell.Snap(0f);
            windNzL = new PinkNoise(21u); windNzR = new PinkNoise(22u);
            windFL.Clear(); windFR.Clear(); whisL.Clear(); whisR.Clear();
            windFL.Set(fs, 250f, 0.9f); windFR.Set(fs, 250f, 0.9f); whisL.Set(fs, 900f, 5f); whisR.Set(fs, 1000f, 5f);
            windHpL = default(OnePole); windHpR = default(OnePole); windHpL.SetHz(fs, 80f); windHpR.SetHz(fs, 80f);     // the morning air carries no sub-bass
            gWindL.Snap(0f); gWindR.Snap(0f); gWhisL.Snap(0f); gWhisR.Snap(0f);
            wireRng = new Rng(23u);
            wire0.Clear(); wire1.Clear(); wire2.Clear();
            wire0.Set(fs, 310f, 40f); wire1.Set(fs, 465f, 40f); wire2.Set(fs, 590f, 40f);
            gWire0.Snap(0f); gWire1.Snap(0f); gWire2.Snap(0f);
            hum1 = default(Osc); hum2 = default(Osc); humNz = new BrownNoise(31u, fs);
            humBp.Clear(); humBp.BandPass(fs, 46.0, 5.0);
            gHum.Snap(0f); humInc1 = 46.0 / fs; humInc2 = 46.4 / fs;
        }

        void KillAmbienceVoices()
        {
            for (int i = 0; i < gull.Length; i++) gull[i].on = false;
            blips.Reset(); bell.Reset();
        }

        static float LapEnv(double t, double period, double phase, int seed)
        {
            double u = t / period + phase + 0.12 * Dsp.Noise(t * 0.13, seed);
            float p = (float)(u - Math.Floor(u));
            return p < 0.14f ? Dsp.Smooth(0f, 0.14f, p) : Dsp.Exp(-(p - 0.14f) * 4.2f);
        }

        void CtlAmbience()
        {
            // ---------- gulls: sparse cries; at tGulls they stop dead and never return ----------
            bool gullsHere = eGulls < 0f;
            gGull.Slew(gullsHere ? GGull * Trim[LGulls] : 0f, GGull / 12f, Block);
            if (gullsHere && advancing)
            {
                int k0 = (int)Math.Floor(T / GullSlot);
                for (int k = k0 - 1; k <= k0; k++)
                {
                    if (k < 0 || Dsp.Hash(k, GullSeed) > 0.85f) continue;
                    double st = GullSlot * k + 0.4 + 3.2 * Dsp.Hash(k, GullSeed + 1);
                    int notes = 2 + (int)(Dsp.Hash(k, GullSeed + 2) * 3.99f);
                    float iv = 0.30f + 0.14f * Dsp.Hash(k, GullSeed + 3);
                    for (int j = 0; j < notes; j++)
                        if (Fire(st + j * iv * (1.0 + 0.07 * j))) StartGull(k, j);
                }
            }

            // ---------- lapping: close at the quay, a soft wash from higher up, gone when the sea leaves ----------
            float far01 = Dsp.Smooth(0f, 70f, eHum);
            float lapLvl = GLap * Trim[LLap] * (1f - 0.72f * far01) * (1f - Dsp.Smooth(0f, 6f, eDraw)) * bedDip;
            if (lapLvl > 1e-5f)
            {
                float eL = LapEnv(T, 3.9, 0.0, 111), eR = LapEnv(T, 4.7, 0.37, 112);
                lapBpL.Set(fs, Dsp.Lerp(420f + 950f * eL, 360f + 220f * eL, far01), 1.1f);
                lapBpR.Set(fs, Dsp.Lerp(450f + 900f * eR, 380f + 200f * eR, far01), 1.1f);
                gLapL.To(lapLvl * (0.22f + eL), Block); gLapR.To(lapLvl * (0.22f + eR), Block);
                gLapLoL.To(lapLvl * 0.9f * eL * eL * (1f - far01), Block);
                gLapLoR.To(lapLvl * 0.9f * eR * eR * (1f - far01), Block);
                gLapBed.To(lapLvl * 0.4f, Block);
                if (advancing && far01 < 0.5f && lapRng.Uni() < 2.2f * ctlDt * (0.3f + eL + eR))      // bubbles and drips at the wall
                    blips.Trigger(fs, 700f + 1300f * lapRng.Uni(), 30f + 40f * lapRng.Uni(), lapLvl * (0.04f + 0.08f * lapRng.Uni()),
                                  0.012f + 0.02f * lapRng.Uni(), lapRng.Bi() * 0.8f);
            }
            else { gLapL.To(0f, Block); gLapR.To(0f, Block); gLapLoL.To(0f, Block); gLapLoR.To(0f, Block); gLapBed.To(0f, Block); }

            // ---------- chapel bell: one distant stroke about every 27 to 31 s, only while the morning is whole ----------
            if (gullsHere)
            {
                int k = (int)Math.Floor((T - 10.0) / 27.0);
                if (k >= 0 && Fire(10.0 + 27.0 * k + 4.0 * Dsp.Hash(k, 121))) bell.Strike(0.5f + 0.2f * Dsp.Hash(k, 122), 0.8f);
            }
            gBell.To(GBell * Trim[LBell], Block);

            // ---------- wind: calm morning air, the gust after the thump, the wind ahead of the wave, the ash storm ----------
            float calm = 0.095f + 0.015f * Dsp.Smooth(0f, 60f, eHum) + 0.02f * Dsp.Smooth(0f, 10f, eVista);
            float u = calm * bedDip + 0.45f * Dsp.Win(eThump - 0.5f, 1.2f, 8f, 5f) + 0.10f * Dsp.Smooth(40f, 110f, eEr)
                    + 0.55f * Dsp.Win(eImp + 7f, 6f, 24f, 12f) + 0.8f * storm;
            u *= (1f - 0.8f * hush) * (1f - 0.94f * silence);
            float gust = 0.62f + 0.30f * Dsp.Noise(T * 0.11, 7) + 0.22f * Dsp.Noise(T * 0.37, 8) + 0.10f * Dsp.Noise(T * 1.3, 9);
            gust = Dsp.Lerp(gust, 0.25f + 0.85f * gust * gust, storm);          // the storm: deep lulls, hard gusts
            float uL = u * Math.Max(0.12f, gust + 0.09f * Dsp.Noise(T * 0.9, 21));
            float uR = u * Math.Max(0.12f, gust + 0.09f * Dsp.Noise(T * 0.9, 22));
            uL /= 1f + 0.7f * uL; uR /= 1f + 0.7f * uR;                           // the strongest gusts saturate
            float wl = GWind * Trim[LWind];
            windFL.Set(fs, 150f + 900f * uL, 0.9f); windFR.Set(fs, 150f + 900f * uR, 0.9f);
            gWindL.To(wl * (float)Math.Pow(uL, 1.2), Block); gWindR.To(wl * (float)Math.Pow(uR, 1.2), Block);
            float um = 0.5f * (uL + uR), wh = wl * 1.2f * um * um * um;
            whisL.Set(fs, 800f + 1500f * um + 250f * Dsp.Noise(T * 0.7, 23), 5f);
            whisR.Set(fs, 950f + 1300f * um + 250f * Dsp.Noise(T * 0.6, 24), 5f);
            gWhisL.To(wh, Block); gWhisR.To(wh, Block);
            // wires: faint aeolian tones whose pitch and level follow the wind
            float wg = GWire * Trim[LWind] * Dsp.Sat(um * 2.5f);
            float wp = 0.85f + 0.45f * um;
            wire0.Set(fs, 310f * wp, 40f); wire1.Set(fs, 465f * wp, 40f); wire2.Set(fs, 590f * wp, 40f);
            gWire0.To(wg * (0.6f + 0.4f * Dsp.Noise(T * 0.5, 25)), Block);
            gWire1.To(wg * 0.7f * (0.6f + 0.4f * Dsp.Noise(T * 0.43, 26)), Block);
            gWire2.To(wg * 0.5f * (0.6f + 0.4f * Dsp.Noise(T * 0.61, 27)), Block);

            // ---------- the deep hum: felt, not heard; grows until the mountain speaks ----------
            float humLvl = 0f;
            if (eHum > 0f)
                humLvl = Dsp.Db(HumDb0 + (HumDb1 - HumDb0) * Dsp.Smooth(0f, 95f, eHum)) * Dsp.Smooth(0f, 3f, eHum)
                       * (1f - Dsp.Smooth(0f, 6f, eThump)) * bedDip * Trim[LHum] * (0.86f + 0.14f * Dsp.Noise(T * 0.09, 32));
            gHum.To(humLvl, Block);
            double hz = 46.0 + 2.0 * Dsp.Noise(T * 0.05, 31);
            humInc1 = hz / fs; humInc2 = (hz + 0.37) / fs;
        }

        void StartGull(int k, int j)
        {
            int bird = (int)(Dsp.Hash(k, 105) * 2.99f);
            int id = k * 16 + j;
            ref GullVoice v = ref gull[gullNext];
            gullNext = (gullNext + 1) & 3;
            v.on = true; v.t = 0f;
            v.dur = (j == 0 ? 0.34f : 0.26f) + 0.16f * Dsp.Hash(id, 107);
            float baseHz = bird == 0 ? 820f : bird == 1 ? 700f : 930f;
            v.f0 = baseHz * (1f + 0.06f * (Dsp.Hash(id, 106) - 0.5f)) * (1f - 0.025f * j);
            float dist = bird == 0 ? 1f : bird == 1 ? 0.55f : 0.36f;
            v.amp = dist * (0.7f + 0.3f * Dsp.Hash(id, 108));
            Dsp.Pan(bird == 0 ? -0.55f : bird == 1 ? 0.3f : 0.75f, out v.gl, out v.gr);
            v.f1.Clear(); v.f2.Clear();
            v.f1.BandPass(fs, 2350.0 + 500.0 * Dsp.Hash(k, 109), 3.0);
            v.f2.BandPass(fs, 3700.0 + 500.0 * Dsp.Hash(k, 110), 3.5);
            v.rng = new Rng((uint)(id * 31 + 7));
            v.raspHz = 62f + 30f * Dsp.Hash(id, 113);
        }

        void RunGull(ref GullVoice v, int n)
        {
            for (int s = 0; s < n; s++)
            {
                v.t += invFs;
                float x = v.t / v.dur;
                if (x >= 1f) { v.on = false; return; }
                float arch = (float)Math.Sin(Math.PI * Math.Min(1f, x * 1.3f));
                float hz = v.f0 * (1f + 0.36f * arch) * (1f - 0.42f * x * x)
                         * (1f + 0.025f * v.vib.Next(24.0 * invFs) + 0.02f * v.rng.Bi());
                float src = v.osc.Saw(hz * invFs) * 0.8f + v.rng.Bi() * 0.12f;
                src *= 1f + 0.35f * v.rasp.Next(v.raspHz * invFs);                 // the rasp of a gull's throat
                float y = v.f1.Next(src) + 0.6f * v.f2.Next(src) + 0.1f * src;
                y *= Dsp.Smooth(0f, 0.1f, x) * (1f - Dsp.Smooth(0.5f, 1f, x)) * v.amp;
                tA[s] += y * v.gl; tB[s] += y * v.gr;
            }
        }

        void RunAmbience(int n)
        {
            if (!gGull.Off)
            {
                for (int s = 0; s < n; s++) { tA[s] = 0f; tB[s] = 0f; }
                for (int i = 0; i < gull.Length; i++) if (gull[i].on) RunGull(ref gull[i], n);
                for (int s = 0; s < n; s++) { float g = gGull.Next(); farL[s] += tA[s] * g; farR[s] += tB[s] * g; }
            }

            if (!(gLapL.Off && gLapR.Off && gLapBed.Off))
                for (int s = 0; s < n; s++)
                {
                    float pl = lapNzL.Next(), pr = lapNzR.Next(), bed = gLapBed.Next();
                    farL[s] += lapBpL.Bp(pl) * gLapL.Next() + lapLoL.Next(pl) * gLapLoL.Next() + lapBedHpL.Hp(lapBedL.Lp(pl)) * bed;
                    farR[s] += lapBpR.Bp(pr) * gLapR.Next() + lapLoR.Next(pr) * gLapLoR.Next() + lapBedHpR.Hp(lapBedR.Lp(pr)) * bed;
                }
            blips.Run(nearL, nearR, n);

            if (bell.Live)
            {
                for (int s = 0; s < n; s++) tC[s] = 0f;
                bell.Run(tC, n, 1f);
                for (int s = 0; s < n; s++)
                {
                    float y = tC[s] * gBell.Next();
                    farL[s] += y * 0.62f; farR[s] += y * 0.88f; echoIn[s] += y * 0.25f;        // the chapel stands up the slope, to the right
                }
            }
            else gBell.Snap(gBell.v + gBell.d * n);

            for (int s = 0; s < n; s++)
            {
                float pl = windNzL.Next(), pr = windNzR.Next();
                windFL.Tick(pl); windFR.Tick(pr);
                dryL[s] += windHpL.Hp(windFL.low + 0.35f * windFL.band) * gWindL.Next() + whisL.Bp(pl) * gWhisL.Next();
                dryR[s] += windHpR.Hp(windFR.low + 0.35f * windFR.band) * gWindR.Next() + whisR.Bp(pr) * gWhisR.Next();
            }
            if (!(gWire0.Off && gWire1.Off && gWire2.Off))
                for (int s = 0; s < n; s++)
                {
                    float w = wireRng.Bi();
                    float a = wire0.Bp(w) * gWire0.Next(), b = wire1.Bp(w) * gWire1.Next(), c = wire2.Bp(w) * gWire2.Next();
                    nearL[s] += a * 0.85f + b * 0.3f + c * 0.55f;
                    nearR[s] += a * 0.3f + b * 0.85f + c * 0.6f;
                }

            if (!gHum.Off)
                for (int s = 0; s < n; s++)
                {
                    float y = 0.85f * hum1.Sine(humInc1) + 0.15f * hum2.Sine(humInc2) + 0.24f * hum1.Harm(2) + 0.09f * hum1.Harm(3)
                            + humBp.Next(humNz.Next()) * 1.2f;
                    low[s] += y * gHum.Next();
                }
        }
    }
}
