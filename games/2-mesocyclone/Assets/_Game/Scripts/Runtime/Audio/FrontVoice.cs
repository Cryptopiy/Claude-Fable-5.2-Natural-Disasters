using System;

namespace MESOCYCLONE.Audio
{
    /// <summary>Beat 3, before the hit: the gust front's roar rising out of the distance ahead (driven by the real
    /// distance to the dust wall), and the wind farm beside the road: turbines over-speeding with a rising whine
    /// and a blade-pass whoosh, then braking with a shuddering groan as the wall takes them.
    /// Audio-side script of the turbines: spin-up 196-232 s, brake 233-239.6 s.</summary>
    public sealed class FrontVoice
    {
        const float SpinUp0 = 196f, SpinUp1 = 232f, Brake0 = 233f, Brake1 = 239.6f;
        readonly int sr;
        Svf roarL, roarR, lowL, lowR;
        float roarPrevL, roarPrevR;
        Wander swell;
        // two turbines, one on each side of the road
        Osc whineA, whineB;
        Svf whooshA, whooshB, sqA, sqB;
        float bladeA, bladeB = 0.4f, passA = 0.1f, passB = 0.55f;
        float gAL, gAR, gBL, gBR, sqPrev;
        Wander drift;

        public FrontVoice(int sampleRate) { sr = sampleRate; Reset(); }

        public void Reset()
        {
            roarL.Reset(); roarR.Reset(); lowL.Reset(); lowR.Reset(); roarPrevL = roarPrevR = 0f; swell.Reset();
            whineA.Init(0f); whineB.Init(0.4f); whooshA.Reset(); whooshB.Reset(); sqA.Reset(); sqB.Reset();
            bladeA = 0f; bladeB = 0.4f; passA = 0.1f; passB = 0.55f; gAL = gAR = gBL = gBR = 0f; sqPrev = 0f; drift.Reset();
        }

        public void Render(Ctx c)
        {
            int n = c.n; float dt = c.dt, t = (float)c.T;

            // ---------- the roar of the gust front, approaching ----------
            float d = c.s.wallDist;
            float near = 0f;
            if (t > 205f && t < 246f && d < 1800f)
            {
                near = Dsp.Clamp01(1f - d / 1800f);
                near = near * near * (1f - Dsp.Step(240.4f, 243.5f, t));
            }
            float sw = swell.Tick(ref c.rng, 0.5f, 1.2f, dt);
            float level = Mix.Front * near * (1f + 0.25f * sw) * c.bedDuck;
            float pan = c.PanOf(c.raw.heading) * 0.5f;
            float pl, pr; Dsp.Pan(pan, out pl, out pr);
            float rl = level * pl * 1.414f, rr = level * pr * 1.414f;
            if (rl > 1e-6f || rr > 1e-6f || roarPrevL > 1e-6f || roarPrevR > 1e-6f)
            {
                float fc = 220f + 700f * near;
                roarL.Set(fc, 0.7f, sr); roarR.Set(fc * 1.08f, 0.7f, sr); lowL.Set(70f, 0.8f, sr); lowR.Set(78f, 0.8f, sr);
                float gl = roarPrevL, dgl = (rl - roarPrevL) / n, gr = roarPrevR, dgr = (rr - roarPrevR) / n;
                float[] a = c.pinkL, b = c.pinkR, br = c.brown, ol = c.worldL, or_ = c.worldR;
                for (int j = 0; j < n; j++)
                {
                    gl += dgl; gr += dgr;
                    ol[j] += gl * (roarL.Lp(a[j] + 0.4f * b[j]) + 1.6f * lowL.Lp(br[j]));
                    or_[j] += gr * (roarR.Lp(b[j] + 0.4f * a[j]) + 1.6f * lowR.Lp(br[j]));
                }
            }
            roarPrevL = rl; roarPrevR = rr;

            // ---------- turbines ----------
            if (t < SpinUp0 || t > Brake1 + 1.5f)
            {
                if (gAL + gAR + gBL + gBR + sqPrev < 1e-6f) return;
            }
            float spin = t < Brake0 ? 0.3f + 1.0f * Dsp.Step(SpinUp0, SpinUp1, t) : 1.3f * (1f - Dsp.Step(Brake0, Brake1, t));
            float braking = Dsp.Step(Brake0, Brake0 + 0.6f, t) * (1f - Dsp.Step(Brake1 - 0.8f, Brake1 + 0.3f, t));
            float present = Dsp.Step(SpinUp0 + 2f, SpinUp0 + 14f, t) * (1f - Dsp.Step(Brake1 - 0.3f, Brake1 + 0.6f, t));
            float move = Dsp.Clamp01(c.s.speed / 24f);
            passA += dt * 0.105f * move; if (passA >= 1f) passA -= 1f;
            passB += dt * 0.087f * move; if (passB >= 1f) passB -= 1f;
            bladeA += dt * spin * 0.92f; if (bladeA >= 1f) bladeA -= 1f;          // three blades at up to 24 rpm
            bladeB += dt * spin * 0.81f; if (bladeB >= 1f) bladeB -= 1f;
            float dr = drift.Tick(ref c.rng, 0.6f, 0.8f, dt);
            float nearA = 0.5f - 0.5f * MathF.Cos(Dsp.TwoPi * passA), nearB = 0.5f - 0.5f * MathF.Cos(Dsp.TwoPi * passB);
            float amA = (0.3f + 0.7f * nearA * nearA) * (0.55f + 0.45f * MathF.Sin(Dsp.TwoPi * bladeA));
            float amB = (0.3f + 0.7f * nearB * nearB) * (0.55f + 0.45f * MathF.Sin(Dsp.TwoPi * bladeB));
            float gain = Mix.Front * 0.6f * present * (0.3f + 0.7f * Dsp.Clamp01(spin)) * (0.5f + 0.5f * c.bedDuck);
            float head = c.raw.headYaw - c.raw.heading;                           // looking right moves both to the left
            float lA, rA, lB, rB;
            Dsp.Pan(Dsp.Clamp(0.25f + 0.6f * passA - head / 90f, -1f, 1f), out lA, out rA);
            Dsp.Pan(Dsp.Clamp(-0.25f - 0.6f * passB - head / 90f, -1f, 1f), out lB, out rB);
            float tAL = gain * amA * lA, tAR = gain * amA * rA, tBL = gain * amB * lB, tBR = gain * amB * rB;
            float fA = (170f + 400f * spin) * (1f + 0.012f * dr), fB = (150f + 365f * spin) * (1f - 0.015f * dr);
            whineA.SetFreq(fA, sr); whineB.SetFreq(fB, sr); whineA.Norm(); whineB.Norm();
            whooshA.Set(380f + 500f * spin, 1.1f, sr); whooshB.Set(340f + 450f * spin, 1.1f, sr);
            float shudder = 0.6f + 0.4f * MathF.Sin(Dsp.TwoPi * bladeA * 11f);
            float sqGain = gain * 1.5f * braking * shudder;
            sqA.Set(1050f + 500f * spin, 22f, sr); sqB.Set(1580f + 700f * spin, 26f, sr);
            float d1 = (tAL - gAL) / n, d2 = (tAR - gAR) / n, d3 = (tBL - gBL) / n, d4 = (tBR - gBR) / n, d5 = (sqGain - sqPrev) / n;
            float[] w0 = c.w0, w1 = c.w1, p0 = c.pinkL, p1 = c.pinkR, wl = c.worldL, wr = c.worldR;
            for (int j = 0; j < n; j++)
            {
                gAL += d1; gAR += d2; gBL += d3; gBR += d4; sqPrev += d5;
                float sa = 0.5f * whineA.Tick() + 2.2f * whooshA.Bp(p0[j]);
                float sb = 0.5f * whineB.Tick() + 2.2f * whooshB.Bp(p1[j]);
                float sq = sqPrev * (sqA.Bp(w0[j]) + 0.7f * sqB.Bp(w1[j]));
                wl[j] += gAL * sa + gBL * sb + 0.5f * sq;
                wr[j] += gAR * sa + gBR * sb + 0.7f * sq;
            }
            gAL = tAL; gAR = tAR; gBL = tBL; gBR = tBR; sqPrev = sqGain;
        }
    }
}
