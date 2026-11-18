using System;

namespace MESOCYCLONE.Audio
{
    /// <summary>Everything the voices share during one sub-block (64 frames): the buses they add into, the shared
    /// noise generators, the smoothed controls and the story time.</summary>
    public sealed class Ctx
    {
        public const int Sub = 64;

        public readonly int sr;
        public readonly float invSr;
        public int n;                    // frames in this sub-block
        public float dt;                 // its length in seconds
        public double T;                 // story time at its start
        public double Tnext;             // story time at its end (the start of the next one, exactly)
        public float storyDt;            // story seconds it covers (0 while the story clock holds)
        public float k10, k50, k300;     // control-rate smoothing coefficients for 10, 50 and 300 ms
        public Snapshot raw;             // as published
        public Snapshot s;               // smoothed (never steps)

        // buses (the voices ADD)
        public readonly float[] cabL = new float[Sub], cabR = new float[Sub];       // the cab's own sounds: crisp
        public readonly float[] worldL = new float[Sub], worldR = new float[Sub];   // outside: muffled by the cab
        public readonly float[] openL = new float[Sub], openR = new float[Sub];     // outside, through the open vent
        public readonly float[] revL = new float[Sub], revR = new float[Sub];       // send to the open-air tail

        // shared noise, refilled every sub-block
        public readonly float[] w0 = new float[Sub], w1 = new float[Sub], w2 = new float[Sub], w3 = new float[Sub];
        public readonly float[] pinkL = new float[Sub], pinkR = new float[Sub], brown = new float[Sub];

        public float bedDuck = 1f;       // gain of the beds: the inhale before a hit, masking under the hail
        public float dull;               // 0..1 dulling of the highs during an inhale
        public float shake;              // 0..1 how hard the cab is being shaken (rattles, creaks)
        public float gustMod;            // -1..1 slow gust fluctuation shared by wind, rain and grit
        public float wind01;             // effective wind strength, 1 = 36 m/s, gusts included
        public Rng rng;

        Rng nA, nB, nC, nD, nE, nF, nG;
        Pink pL, pR;
        float brownZ;
        Wander gustSlow, gustFast;
        int lastN;

        public Ctx(int sampleRate)
        {
            sr = sampleRate; invSr = 1f / sampleRate;
            Reseed();
        }

        public void Reseed()
        {
            rng.Seed(11u); nA.Seed(101u); nB.Seed(202u); nC.Seed(303u); nD.Seed(404u); nE.Seed(505u); nF.Seed(606u); nG.Seed(707u);
            pL.Reset(); pR.Reset(); brownZ = 0f; gustSlow.Reset(); gustFast.Reset();
            bedDuck = 1f; dull = 0f; shake = 0f; gustMod = 0f; wind01 = 0f;
        }

        /// <summary>Copies the snapshot into the smoothed controls without smoothing (start, seek).</summary>
        public void SnapTo(in Snapshot snapshot) { raw = snapshot; s = snapshot; }

        /// <summary>Pan (-1..1) of a world bearing for the current head direction.</summary>
        public float PanOf(float worldBearingDeg)
        {
            return MathF.Sin((worldBearingDeg - raw.headYaw) * Dsp.Deg2Rad) * 0.85f;
        }

        /// <summary>1 in front of the nose, 0 behind: a small level cue for what the pan law cannot tell.</summary>
        public float FrontOf(float worldBearingDeg)
        {
            return 0.5f + 0.5f * MathF.Cos((worldBearingDeg - raw.headYaw) * Dsp.Deg2Rad);
        }

        public void Begin(int frames, in Snapshot snapshot)
        {
            n = frames; dt = frames * invSr; raw = snapshot;
            if (frames != lastN)
            {
                lastN = frames;
                k10 = Dsp.Ease(0.010f, dt); k50 = Dsp.Ease(0.050f, dt); k300 = Dsp.Ease(0.300f, dt);
            }
            float k = k50;
            s.T = raw.T; s.epoch = raw.epoch; s.running = raw.running; s.paused = raw.paused; s.rate = raw.rate;
            s.headYaw = raw.headYaw; s.heading = raw.heading;
            s.torBearing = raw.torBearing; s.townBearing = raw.townBearing; s.wiperMode = raw.wiperMode;
            s.speed += (raw.speed - s.speed) * k300;
            s.rain += (raw.rain - s.rain) * k; s.hail += (raw.hail - s.hail) * k; s.hailSize += (raw.hailSize - s.hailSize) * k;
            s.wind += (raw.wind - s.wind) * k; s.windDir += (raw.windDir - s.windDir) * k; s.gust += (raw.gust - s.gust) * k;
            s.dust += (raw.dust - s.dust) * k; s.wetness += (raw.wetness - s.wetness) * k;
            s.wiperPos += (raw.wiperPos - s.wiperPos) * Dsp.Ease(0.022f, dt);
            s.insectA += (raw.insectA - s.insectA) * k; s.insectB += (raw.insectB - s.insectB) * k; s.insectC += (raw.insectC - s.insectC) * k;
            s.radio += (raw.radio - s.radio) * k; s.pressure += (raw.pressure - s.pressure) * k; s.siren += (raw.siren - s.siren) * k;
            s.engine += (raw.engine - s.engine) * k; s.ringing += (raw.ringing - s.ringing) * k; s.drips += (raw.drips - s.drips) * k;
            s.torPower += (raw.torPower - s.torPower) * k; s.torDist += (raw.torDist - s.torDist) * k;
            s.torRadius += (raw.torRadius - s.torRadius) * k; s.torDebris += (raw.torDebris - s.torDebris) * k;
            s.torWrap += (raw.torWrap - s.torWrap) * k; s.townDist += (raw.townDist - s.townDist) * k;
            s.wallDist += (raw.wallDist - s.wallDist) * k;
            s.crack1 += (raw.crack1 - s.crack1) * k; s.crack2 += (raw.crack2 - s.crack2) * k; s.crack3 += (raw.crack3 - s.crack3) * k;
            s.canopy += (raw.canopy - s.canopy) * k;

            // gusts: one slow and one quick wander, shared so that wind, rain and grit breathe together
            float a = gustSlow.Tick(ref rng, 0.8f, 0.7f, dt), b = gustFast.Tick(ref rng, 3.2f, 0.16f, dt);
            gustMod = 0.65f * a + 0.35f * b;
            float w = s.wind * (1f / 36f) * (1f + s.gust * 0.45f * gustMod);
            wind01 = w < 0f ? 0f : w;
            shake -= shake * Dsp.Ease(0.35f, dt);

            for (int j = 0; j < frames; j++)
            {
                float a0 = nA.Bi(), a1 = nB.Bi();
                w0[j] = a0; w1[j] = a1; w2[j] = nC.Bi(); w3[j] = nD.Bi();
                pinkL[j] = pL.Tick(nE.Bi()); pinkR[j] = pR.Tick(nF.Bi());
                brownZ = (brownZ + 0.02f * nG.Bi()) * 0.98039216f;
                brown[j] = 3.5f * brownZ;
                cabL[j] = 0f; cabR[j] = 0f; worldL[j] = 0f; worldR[j] = 0f;
                openL[j] = 0f; openR[j] = 0f; revL[j] = 0f; revR[j] = 0f;
            }
        }
    }
}
