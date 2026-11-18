using System;

namespace ZERO
{
    /// <summary>Control-rate targets of the continuous soundscape, computed by <see cref="ZeroSynth"/> from the state
    /// the game pushes every frame (never from a timeline of its own). Levels are linear and mean about the RMS
    /// of the source.</summary>
    internal struct CabinTargets
    {
        public float air, rough, dark;          // airflow over the fuselage: level; 0..1 how rough the air is; 0..1 duller (the sky has gone dark)
        public float engine, pitch;             // the two engines: level, pitch multiplier
        public float vent;                      // air conditioning
        public float throb, throbRate;          // the wrongness: a very low throb and its pulse in Hz
        public float shimmer;                   // the wrongness: a thin high shimmer
        public float rumble, rumbleOpen;        // infrasonic rumble through the airframe: level, 0..1 opens its low-pass (45 -> 170 Hz)
        public float buffet;                    // the airframe shuddering
        public float groan;                     // the airframe under stress: a low metallic drone
        public float rattle, rattleGain;        // loose things in the cabin: hits per second, level of one hit
        public float roar, roarCut;             // the wave in the last seconds: level, low-pass in Hz
        public float sky, skyLevel;             // the burning sky above: 0..1 how dense the crackle is, level
        public float bus;                       // overall gain of the layer (trim, cue)
    }

    /// <summary>Everything that sounds continuously inside the cabin: the bed of an ordinary flight (airflow, two
    /// engines beating slowly against each other, air conditioning), the barely perceptible wrongness while the
    /// asteroid nears (a sub-bass throb, a thin shimmer), the rumble of the blast wave coming through the airframe,
    /// the airframe shuddering and groaning, loose things rattling, the roar of the last seconds and the dry crackle
    /// of the burning sky above. Every source is a function of the targets; a source at zero costs nothing.</summary>
    internal sealed class CabinLayer
    {
        // layer bits for ZeroSynth.DebugLayerMask
        public const int LAir = 1, LEngine = 2, LVent = 4, LThrob = 8, LShimmer = 16, LRumble = 32, LBuffet = 64, LGroan = 128,
            LRattle = 256, LRoar = 512, LSky = 1024;

        // With these factors a target of 1 gives an RMS of about 1 (measured offline with the harness).
        const float AirNorm = 5.5f, EngineNorm = 1.14f, EngineNoise = 5.7f, VentNorm = 4.0f, ThrobNorm = 2.0f, ShimmerNorm = 2.1f,
            BuffetNorm = 11.5f, GroanNorm = 3.5f, RoarNorm = 3.5f, SkyNorm = 6.0f;
        static readonly float[] RumbleNorm = { 18.3f, 10.7f, 8.9f };     // at rumbleOpen 0, 0.5, 1

        const int NObj = 8, NH = 4;
        const float EngineHz = 84f, EngineBeat = 0.37f;
        static readonly float[] Harm = { 1f, 0.45f, 0.22f, 0.10f };
        static readonly float[] ObjHz = { 190f, 330f, 520f, 760f, 1100f, 1650f, 2300f, 3100f };
        static readonly float[] ObjTau = { 0.030f, 0.018f, 0.014f, 0.010f, 0.008f, 0.006f, 0.005f, 0.004f };
        static readonly float[] ObjPan = { -0.2f, 0.7f, -0.8f, 0.3f, -0.5f, 0.9f, -0.9f, 0.1f };

        readonly float fs, invFs, k;
        CabinTargets tg;
        readonly float[] tl, tr;

        // airflow
        NoiseGen anL, anR, anC;
        Biquad ahL, ahR;
        Svf alL, alR;
        SlowRandom gust, turb, gustPan;
        float tAirL, tAirR, gAirL, gAirR, airCut = -1f;
        // engines
        double e0, e1;
        double inc0, inc1;
        NoiseGen enN;
        Biquad enLp1, enLp2;
        SlowRandom wob0, wob1, enWalk;
        float gEngine, tEngine, tEngNoise, gEngNoise;
        // air conditioning
        NoiseGen vn;
        Svf vBp, vDuct;
        float gVent;
        // wrongness
        float thPhase, thInc, gThrob;
        Lfo thAm;
        double s1, s2, s3;
        readonly double si1, si2, si3;
        Lfo tr1, tr2, tr3;
        float gShimmer;
        // rumble
        NoiseGen rnA, rnB;
        Biquad rhL, rhR;
        Svf r1L, r1R, r2L, r2R;
        SlowRandom rWalk;
        float tRumble, gRumble, rumbleFc = -1f;
        // buffet and groan
        NoiseGen bnL, bnR;
        Svf bfL, bfR;
        SlowRandom flutter;
        float tBuffet, gBuffet, flut;
        public readonly GroanBank Groan;
        // rattle
        Rng rk;
        NoiseGen rkN;
        Svf rkBp;
        readonly Ping[] obj = new Ping[NObj];
        readonly float[] objL = new float[NObj], objR = new float[NObj];
        float rattleP, rkGrain, rkGl = 0.7f, rkGr = 0.7f;
        readonly float rkDecay;
        bool objOn;
        // roar
        NoiseGen fnL, fnR;
        Svf f1L, f1R, f2L, f2R;
        SlowRandom fTurb;
        float tRoar, gRoar, roarCut = -1f;
        // burning sky
        NoiseGen skL, skR;
        Svf shL, shR;
        float gSky, tSky, skyP, sgL, sgR;
        readonly float skDecay;
        // bus
        float gBus;

        public CabinLayer(int sampleRate, uint seed, int block)
        {
            fs = sampleRate; invFs = 1f / fs;
            k = Dsp.TauCoef(0.03f, fs);
            tl = new float[block]; tr = new float[block];

            anL = new NoiseGen(seed + 1u); anR = new NoiseGen(seed + 2u); anC = new NoiseGen(seed + 3u);
            ahL.SetHighpass(110f, 0.6f, fs); ahR.SetHighpass(110f, 0.6f, fs);
            gust = new SlowRandom(seed + 4u, 3f, 8f); turb = new SlowRandom(seed + 5u, 0.15f, 0.6f);
            gustPan = new SlowRandom(seed + 6u, 5f, 11f);
            SetAir(1500f);

            enN = new NoiseGen(seed + 10u);
            enLp1.SetLowpass(140f, 0.707f, fs); enLp2.SetLowpass(140f, 0.707f, fs);
            wob0 = new SlowRandom(seed + 11u, 0.3f, 1.1f); wob1 = new SlowRandom(seed + 12u, 0.35f, 1.3f);
            enWalk = new SlowRandom(seed + 13u, 2f, 6f);
            inc0 = EngineHz / fs; inc1 = (EngineHz + EngineBeat) / fs;

            vn = new NoiseGen(seed + 20u);
            vBp.Set(3200f, 0.7f, fs); vDuct.Set(1850f, 14f, fs);

            thAm.Set(0.25f, fs); thInc = 31f / fs;
            si1 = 8372.0 / fs; si2 = 8421.0 / fs; si3 = 11180.0 / fs;
            tr1.Set(0.13f, fs); tr2.Set(0.21f, fs); tr2.SetPhase(0.3f); tr3.Set(0.17f, fs); tr3.SetPhase(0.7f);

            rnA = new NoiseGen(seed + 30u); rnB = new NoiseGen(seed + 31u);
            rhL.SetHighpass(18f, 0.707f, fs); rhR.SetHighpass(18f, 0.707f, fs);
            rWalk = new SlowRandom(seed + 32u, 1.5f, 4f);
            SetRumble(0f);

            bnL = new NoiseGen(seed + 40u); bnR = new NoiseGen(seed + 41u);
            bfL.Set(150f, 0.7f, fs); bfR.Set(165f, 0.7f, fs);
            flutter = new SlowRandom(seed + 42u, 0.06f, 0.16f);
            Groan = new GroanBank(sampleRate, seed + 50u, block);

            rk = new Rng(seed + 60u); rkN = new NoiseGen(seed + 61u);
            rkBp.Set(2400f, 0.9f, fs);
            rkDecay = Dsp.Decay(0.0015f, fs);
            for (int i = 0; i < NObj; i++)
            {
                obj[i].Set(ObjHz[i], ObjTau[i], fs);
                Dsp.Pan(ObjPan[i], out objL[i], out objR[i]);
                objL[i] *= 1.4142f; objR[i] *= 1.4142f;
            }

            fnL = new NoiseGen(seed + 70u); fnR = new NoiseGen(seed + 71u);
            fTurb = new SlowRandom(seed + 72u, 0.08f, 0.3f);
            SetRoar(200f);

            skL = new NoiseGen(seed + 80u); skR = new NoiseGen(seed + 81u);
            shL.Set(4500f, 0.8f, fs); shR.Set(5000f, 0.8f, fs);
            skDecay = Dsp.Decay(0.0008f, fs);
        }

        static float Norm3(float[] v, float x)
        {
            x = Dsp.Clamp01(x);
            return x < 0.5f ? Dsp.Lerp(v[0], v[1], x * 2f) : Dsp.Lerp(v[1], v[2], x * 2f - 1f);
        }

        void SetAir(float cut)
        {
            if (Math.Abs(cut - airCut) < 2f) return;
            airCut = cut;
            alL.Set(cut, 0.6f, fs); alR.Set(cut * 1.06f, 0.6f, fs);
        }

        void SetRumble(float open)
        {
            float fc = 45f + 125f * Dsp.Clamp01(open);
            if (Math.Abs(fc - rumbleFc) < 0.5f) return;
            rumbleFc = fc;
            r1L.Set(fc, 0.541f, fs); r1R.Set(fc * 1.05f, 0.541f, fs);      // 4th-order Butterworth in two sections
            r2L.Set(fc, 1.307f, fs); r2R.Set(fc * 1.05f, 1.307f, fs);
        }

        void SetRoar(float cut)
        {
            cut = Dsp.Clamp(cut, 60f, 9000f);
            if (Math.Abs(cut - roarCut) < 0.002f * cut) return;
            roarCut = cut;
            f1L.Set(cut, 0.6f, fs); f1R.Set(cut * 1.06f, 0.6f, fs);
            f2L.Set(cut, 1.15f, fs); f2R.Set(cut * 1.06f, 1.15f, fs);
        }

        /// <summary>Back to silence with clean filter memory (used while the output is gated shut).</summary>
        public void Reset()
        {
            ahL.Reset(); ahR.Reset(); alL.Reset(); alR.Reset(); enLp1.Reset(); enLp2.Reset(); vBp.Reset(); vDuct.Reset();
            rhL.Reset(); rhR.Reset(); r1L.Reset(); r1R.Reset(); r2L.Reset(); r2R.Reset(); bfL.Reset(); bfR.Reset();
            rkBp.Reset(); f1L.Reset(); f1R.Reset(); f2L.Reset(); f2R.Reset(); shL.Reset(); shR.Reset();
            anL.Reset(); anR.Reset(); anC.Reset(); enN.Reset(); rnA.Reset(); rnB.Reset(); bnL.Reset(); bnR.Reset();
            fnL.Reset(); fnR.Reset();
            for (int i = 0; i < NObj; i++) obj[i].Reset();
            objOn = false;
            Groan.Reset();
            gAirL = gAirR = gEngine = gEngNoise = gVent = gThrob = gShimmer = gRumble = gBuffet = gRoar = gSky = gBus = 0f;
            rkGrain = sgL = sgR = 0f;
        }

        public void Control(float dt, ref CabinTargets t)
        {
            tg = t;
            float rough = Dsp.Clamp01(t.rough);

            // ---- airflow: steady in cruise, gusty and brighter when the air gets rough
            float gs = gust.Next(dt), tb = turb.Next(dt), gp = gustPan.Next(dt) * 2f - 1f;
            float lvl = t.air * ((0.90f + 0.10f * gs) * (1f - 0.45f * rough) + rough * (0.25f + 1.25f * tb));
            tAirL = lvl * (1f - 0.12f * gp) * AirNorm; tAirR = lvl * (1f + 0.12f * gp) * AirNorm;
            SetAir((1500f + 120f * gs + 1100f * rough * tb) * (1f - 0.18f * Dsp.Clamp01(t.dark)));

            // ---- engines: the second one a third of a hertz above the first; they waver when the airframe shakes
            float p = t.pitch > 0f ? t.pitch : 1f;
            float w0 = wob0.Next(dt) * 2f - 1f, w1 = wob1.Next(dt) * 2f - 1f;
            inc0 = EngineHz * p * (1f + 0.012f * rough * w0) / fs;
            inc1 = (EngineHz + EngineBeat) * p * (1f + 0.012f * rough * w1) / fs;
            tEngine = t.engine * EngineNorm;
            tEngNoise = t.engine * (0.75f + 0.25f * enWalk.Next(dt)) * EngineNoise;

            // ---- the wrongness
            thAm.Set(Dsp.Clamp(t.throbRate, 0.05f, 3f), fs);

            // ---- the rumble of the wave
            SetRumble(t.rumbleOpen);
            tRumble = t.rumble * (0.6f + 0.4f * rWalk.Next(dt)) * Norm3(RumbleNorm, t.rumbleOpen);

            // ---- the airframe: a shudder at 6-16 Hz, a groan of metal
            flut = flutter.Next(dt);
            tBuffet = t.buffet * (0.15f + 0.85f * flut) * BuffetNorm;
            Groan.Control(dt, 1f, 0f);

            // ---- loose things rattle with the shudder
            rattleP = t.rattle * (0.4f + 1.2f * flut) * invFs;
            if (objOn)
            {
                bool any = false;
                for (int i = 0; i < NObj; i++) { obj[i].Zap(); any |= obj[i].Active; }
                objOn = any;
            }

            // ---- the roar of the last seconds
            SetRoar(t.roarCut);
            float fn = RoarNorm / (float)Math.Sqrt(Math.Log(Math.Max(roarCut, 60f) / 15.0));
            tRoar = t.roar * (0.72f + 0.28f * fTurb.Next(dt)) * fn;

            // ---- the burning sky
            tSky = t.skyLevel * SkyNorm;
            skyP = (20f + 50f * Dsp.Clamp01(t.sky)) * (t.sky > 0f ? 1f : 0f) * invFs;
        }

        void HitObject()
        {
            int i = (int)(rk.F() * rk.F() * 1.6f * NObj);            // the heavy things rattle more often than the small ones
            if (i >= NObj) i = NObj - 1;
            float u = rk.F();
            float a = (0.2f + 0.8f * u * u) * tg.rattleGain;
            obj[i].Hit(a);
            rkGrain += a * 0.9f; rkGl = objL[i]; rkGr = objR[i];
            objOn = true;
        }

        /// <summary>Adds the cabin to l/r and the share of the rattling things for the cabin reverb to sendL/sendR.</summary>
        public void Process(float[] l, float[] r, float[] sendL, float[] sendR, int n)
        {
            for (int i = 0; i < n; i++) { tl[i] = 0f; tr[i] = 0f; }
            float kk = k;

            // airflow over the fuselage: a muffled hiss, 110 Hz .. about 1.5 kHz
            if (tAirL + tAirR > 0f || gAirL + gAirR > 1e-7f)
            {
                for (int i = 0; i < n; i++)
                {
                    gAirL += kk * (tAirL - gAirL); gAirR += kk * (tAirR - gAirR);
                    float c = anC.Pink() * 0.5f;
                    tl[i] += alL.Lp(ahL.Tick(anL.Pink() + c)) * gAirL;
                    tr[i] += alR.Lp(ahR.Tick(anR.Pink() + c)) * gAirR;
                }
                Dsp.Zap(ref gAirL); Dsp.Zap(ref gAirR);
            }

            // two engines: four harmonics each, the nearer one louder on the window side; a low broadband rumble under them
            if (tEngine + tEngNoise > 0f || gEngine + gEngNoise > 1e-7f)
            {
                double a0 = e0, a1 = e1, i0 = inc0, i1 = inc1;
                for (int i = 0; i < n; i++)
                {
                    gEngine += kk * (tEngine - gEngine); gEngNoise += kk * (tEngNoise - gEngNoise);
                    float x = 0f, y = 0f;
                    for (int h = 0; h < NH; h++)
                    {
                        x += Harm[h] * Dsp.Sin01(a0 * (h + 1));
                        y += Harm[h] * Dsp.Sin01(a1 * (h + 1));
                    }
                    a0 += i0; if (a0 >= 1.0) a0 -= 1.0;
                    a1 += i1; if (a1 >= 1.0) a1 -= 1.0;
                    float nz = enLp2.Tick(enLp1.Tick(enN.Brown())) * gEngNoise;
                    tl[i] += (0.80f * x + 0.40f * y) * gEngine + nz;
                    tr[i] += (0.45f * x + 0.60f * y) * gEngine + nz;
                }
                e0 = a0; e1 = a1;
                Dsp.Zap(ref gEngine); Dsp.Zap(ref gEngNoise);
            }

            // air conditioning: a soft hiss from above and a faint whistle in the duct
            if (tg.vent > 0f || gVent > 1e-7f)
            {
                float target = tg.vent * VentNorm;
                for (int i = 0; i < n; i++)
                {
                    gVent += kk * (target - gVent);
                    float w = vn.White();
                    float v = (vBp.Bp(w) + 0.35f * vDuct.Bp(w)) * gVent;
                    tl[i] += 0.8f * v; tr[i] += v;
                }
                Dsp.Zap(ref gVent);
            }

            // the wrongness: a 31 Hz throb that pulses very slowly
            if (tg.throb > 0f || gThrob > 1e-7f)
            {
                float target = tg.throb * ThrobNorm;
                for (int i = 0; i < n; i++)
                {
                    gThrob += kk * (target - gThrob);
                    thPhase += thInc;
                    if (thPhase >= 1f) thPhase -= 1f;
                    float am = 0.25f + 0.75f * thAm.RaisedCos();
                    float th = (Dsp.Sin01(thPhase) + 0.30f * Dsp.Sin01(thPhase * 2f) + 0.12f * Dsp.Sin01(thPhase * 3f)) * am * gThrob;
                    tl[i] += th; tr[i] += th;
                }
                Dsp.Zap(ref gThrob);
            }

            // the wrongness: three very high tones, each fading in and out on its own, one to each side
            if (tg.shimmer > 0f || gShimmer > 1e-7f)
            {
                float target = tg.shimmer * ShimmerNorm;
                for (int i = 0; i < n; i++)
                {
                    gShimmer += kk * (target - gShimmer);
                    float a = Dsp.Sin01(s1) * tr1.RaisedCos(), b = Dsp.Sin01(s2) * tr2.RaisedCos(), c = Dsp.Sin01(s3) * tr3.RaisedCos();
                    s1 += si1; if (s1 >= 1.0) s1 -= 1.0;
                    s2 += si2; if (s2 >= 1.0) s2 -= 1.0;
                    s3 += si3; if (s3 >= 1.0) s3 -= 1.0;
                    tl[i] += (0.9f * a + 0.5f * b + 0.2f * c) * gShimmer;
                    tr[i] += (0.3f * a + 0.5f * b + 0.9f * c) * gShimmer;
                }
                Dsp.Zap(ref gShimmer);
            }

            // the rumble of the wave: 18 Hz up to the moving low-pass, mostly the same in both ears
            if (tRumble > 0f || gRumble > 1e-7f)
            {
                for (int i = 0; i < n; i++)
                {
                    gRumble += kk * (tRumble - gRumble);
                    float a = rnA.Brown(), b = rnB.Brown();
                    tl[i] += r2L.Lp(r1L.Lp(rhL.Tick(a + 0.5f * b))) * gRumble;
                    tr[i] += r2R.Lp(r1R.Lp(rhR.Tick(b + 0.5f * a))) * gRumble;
                }
                Dsp.Zap(ref gRumble);
            }

            // the airframe shudders
            if (tBuffet > 0f || gBuffet > 1e-7f)
            {
                for (int i = 0; i < n; i++)
                {
                    gBuffet += kk * (tBuffet - gBuffet);
                    tl[i] += bfL.Bp(bnL.Pink()) * gBuffet;
                    tr[i] += bfR.Bp(bnR.Pink()) * gBuffet;
                }
                Dsp.Zap(ref gBuffet);
            }

            // and groans
            Groan.Process(tl, tr, n, tg.groan * GroanNorm);

            // bins, trays and panels rattle
            if (rattleP > 0f || objOn || rkGrain > 0f)
            {
                float p = rattleP;
                for (int i = 0; i < n; i++)
                {
                    if (p > 0f && rk.F() < p) HitObject();
                    rkGrain *= rkDecay;
                    if (rkGrain < 1e-7f) rkGrain = 0f;
                    float g = rkBp.Bp(rkN.White() * (rkGrain + 1e-9f));
                    float x = g * rkGl, y = g * rkGr;
                    for (int q = 0; q < NObj; q++)
                        if (obj[q].Active) { float v = obj[q].Tick(); x += v * objL[q]; y += v * objR[q]; }
                    tl[i] += x; tr[i] += y;
                    sendL[i] += 0.3f * x; sendR[i] += 0.3f * y;
                }
                rkBp.Zap();
            }

            // the wave itself in the last seconds: a roar that opens into a hiss, from the side of the window first
            if (tRoar > 0f || gRoar > 1e-7f)
            {
                for (int i = 0; i < n; i++)
                {
                    gRoar += kk * (tRoar - gRoar);
                    float a = fnL.Pink() * 2.4f + fnL.White() * 0.25f;
                    float b = fnR.Pink() * 2.4f + fnR.White() * 0.25f;
                    tl[i] += f2L.Lp(f1L.Lp(a)) * gRoar * 1.15f;
                    tr[i] += f2R.Lp(f1R.Lp(b)) * gRoar * 0.85f;
                }
                Dsp.Zap(ref gRoar);
            }

            // the burning sky above: a thin dry hiss, sparse ticks
            if (tSky > 0f || gSky > 1e-7f)
            {
                float p = skyP;
                for (int i = 0; i < n; i++)
                {
                    gSky += kk * (tSky - gSky);
                    if (skL.Uniform() < p) { float u = skL.Uniform(); sgL += u * u * u; }
                    if (skR.Uniform() < p) { float u = skR.Uniform(); sgR += u * u * u; }
                    sgL *= skDecay; sgR *= skDecay;
                    tl[i] += shL.Hp(skL.White()) * (0.22f + sgL) * gSky;
                    tr[i] += shR.Hp(skR.White()) * (0.22f + sgR) * gSky;
                }
                Dsp.Zap(ref gSky); Dsp.Zap(ref sgL); Dsp.Zap(ref sgR);
            }

            float bus = tg.bus;
            for (int i = 0; i < n; i++)
            {
                gBus += kk * (bus - gBus);
                l[i] += tl[i] * gBus; r[i] += tr[i] * gBus;
            }
            Dsp.Zap(ref gBus);
        }
    }
}
