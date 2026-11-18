using System;

namespace MESOCYCLONE.Audio
{
    /// <summary>The truck itself: a V8 at low load (one engine cycle as a table of eight uneven firing pulses, so the
    /// burble comes for free), its mechanical hiss, the road hum with the tyre cavity tone and tread, wet-road
    /// spray, and the double thump of expansion joints. The engine runs down naturally when StoryState.engine falls.</summary>
    public sealed class TruckVoice
    {
        const int N = 2048;
        readonly int sr;
        readonly float[] table = new float[N + 1];
        float phase, rpm = 1650f;
        Wander jitter, load, surface;
        Rng rng;
        Svf body, boom, mechL, mechR;
        Svf roadL, roadR, cavity, tread, wetL, wetR;
        float engPrev, roadPrev, wetPrev;
        readonly ModalBank thump = new ModalBank(2);
        float jointDist = 60f, rearTimer = -1f, rearAmp;

        public TruckVoice(int sampleRate)
        {
            sr = sampleRate;
            float[] amp = { 1f, 0.82f, 0.95f, 0.78f, 1f, 0.88f, 0.84f, 0.93f };
            float[] off = { 0f, 0.012f, -0.008f, 0.015f, 0f, -0.011f, 0.009f, -0.006f };
            float mean = 0f;
            for (int i = 0; i < N; i++)
            {
                float phi = i / (float)N, v = 0f;
                for (int j = 0; j < 8; j++)
                {
                    float d = phi - (j / 8f + off[j]);
                    float cs = MathF.Cos(Dsp.TwoPi * d);
                    if (cs > 0f) { float c2 = cs * cs; v += amp[j] * c2 * c2 * c2; }
                }
                table[i] = v; mean += v;
            }
            mean /= N;
            float peak = 1e-6f;
            for (int i = 0; i < N; i++) { table[i] -= mean; float a = MathF.Abs(table[i]); if (a > peak) peak = a; }
            for (int i = 0; i < N; i++) table[i] /= peak;
            table[N] = table[0];
            thump.Set(0, 61f, 0.10f, 1f, 0f, 1f, 0.6f, sr);
            thump.Set(1, 97f, 0.06f, 0.6f, 0f, 0.5f, 1f, sr);
            Reset();
        }

        public void Reset()
        {
            rng.Seed(3001u); phase = 0f; rpm = 1650f;
            jitter.Reset(); load.Reset(); surface.Reset();
            body.Reset(); boom.Reset(); mechL.Reset(); mechR.Reset();
            roadL.Reset(); roadR.Reset(); cavity.Reset(); tread.Reset(); wetL.Reset(); wetR.Reset();
            engPrev = 0f; roadPrev = 0f; wetPrev = 0f; thump.Reset();
            jointDist = 60f; rearTimer = -1f;
        }

        public void Render(Ctx c)
        {
            int n = c.n; float dt = c.dt;
            float speed = c.s.speed, run = Dsp.Clamp01(c.s.engine);
            float v01 = Dsp.Clamp01(speed / 24f);

            // ---------- engine ----------
            float j = jitter.Tick(ref c.rng, 3f, 0.12f, dt), ld = load.Tick(ref c.rng, 0.35f, 1.4f, dt);
            float target = (700f + 39.6f * speed) * (1f + 0.004f * j + 0.012f * ld + 0.01f * c.s.gust * c.gustMod);
            if (run < 0.999f) target *= 0.2f + 0.8f * MathF.Pow(run, 0.7f);
            rpm += (target - rpm) * Dsp.Ease(0.22f, dt);
            float inc = rpm / 120f * c.invSr;                       // engine cycles per sample
            float f0 = rpm / 120f;
            body.Set(190f + 6f * f0, 0.8f, sr);
            boom.Set(Dsp.Clamp(f0 * 4f, 28f, 70f), 2.2f, sr);       // the half-firing order: the burble of a V8
            mechL.Set(1500f + 0.25f * rpm, 0.8f, sr); mechR.Set(1750f + 0.25f * rpm, 0.8f, sr);
            float engGain = Mix.Engine * (0.82f + 0.18f * ld + 0.25f * (1f - v01)) * Dsp.Step(0f, 0.3f, run) * (0.45f + 0.55f * c.bedDuck);
            float g = engPrev, dg = (engGain - engPrev) / n;
            float[] cl = c.cabL, cr = c.cabR, w0 = c.w0, w1 = c.w1;
            float boomK = boom.k;
            if (engGain > 1e-6f || engPrev > 1e-6f)
            {
                for (int i = 0; i < n; i++)
                {
                    phase += inc; if (phase >= 1f) phase -= 1f;
                    float x = phase * N; int k = (int)x;
                    float e = table[k] + (x - k) * (table[k + 1] - table[k]);
                    float lp = body.Lp(e) + 0.5f * boomK * boom.Bp(e) + 0.25f * e;
                    float am = 0.5f + 0.5f * (e < 0f ? -e : e);
                    g += dg;
                    cl[i] += g * (lp + 0.07f * am * mechL.Bp(w0[i]));
                    cr[i] += g * (lp + 0.07f * am * mechR.Bp(w1[i]));
                }
            }
            engPrev = engGain;

            // ---------- road ----------
            float sf = surface.Tick(ref c.rng, 0.5f, 1.2f, dt);
            float roadGain = Mix.Road * MathF.Pow(v01, 1.6f) * (1f + 0.22f * sf) * c.bedDuck;
            float wetGain = Mix.Road * 0.5f * c.s.wetness * v01 * v01 * (0.55f + 0.45f * c.s.rain) * c.bedDuck;
            roadL.Set(135f + 20f * sf, 0.7f, sr); roadR.Set(150f - 20f * sf, 0.7f, sr);
            cavity.Set(214f + 6f * v01, 12f, sr); tread.Set(520f + 260f * v01, 0.9f, sr);
            wetL.Set(3300f, 0.7f, sr); wetR.Set(3700f, 0.7f, sr);
            float rg = roadPrev, drg = (roadGain - roadPrev) / n, wg = wetPrev, dwg = (wetGain - wetPrev) / n;
            if (roadGain > 1e-6f || roadPrev > 1e-6f || wetGain > 1e-6f || wetPrev > 1e-6f)
            {
                float[] br = c.brown, pl = c.pinkL, pr = c.pinkR, w2 = c.w2, w3 = c.w3;
                float cavK = cavity.k;
                for (int i = 0; i < n; i++)
                {
                    float b = br[i];
                    float mid = 0.09f * cavK * cavity.Bp(w2[i]) + 0.07f * tread.Bp(w3[i]);
                    rg += drg; wg += dwg;
                    cl[i] += rg * (2.2f * roadL.Lp(b + 0.6f * pl[i]) + mid) + wg * wetL.Bp(pl[i]);
                    cr[i] += rg * (2.2f * roadR.Lp(b + 0.6f * pr[i]) + mid) + wg * wetR.Bp(pr[i]);
                }
            }
            roadPrev = roadGain; wetPrev = wetGain;

            // ---------- expansion joints: front axle, then the rear one wheelbase later ----------
            jointDist -= speed * dt;
            if (jointDist <= 0f && speed > 2f)
            {
                jointDist = rng.Range(50f, 230f);
                float a = rng.Range(0.35f, 1f);
                thump.Hit(0, a, rng.Uni());
                rearTimer = 3.7f / speed; rearAmp = a * rng.Range(0.6f, 0.9f);
                if (c.shake < 0.5f * a) c.shake = 0.5f * a;
            }
            if (rearTimer >= 0f)
            {
                rearTimer -= dt;
                if (rearTimer < 0f) thump.Hit(0, rearAmp, rng.Uni());
            }
            thump.Render(cl, cr, n, Mix.Road * 0.5f * (0.4f + 0.6f * c.bedDuck));
        }
    }
}
