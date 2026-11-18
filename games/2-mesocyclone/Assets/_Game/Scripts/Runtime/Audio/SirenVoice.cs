using System;

namespace MESOCYCLONE.Audio
{
    /// <summary>Two generic rotating sirens in the town (a near one and a far one, slightly out of tune). A motor
    /// model makes the wail: powered 2.6 s, coasting 2.4 s, about 5 s per cycle. The horn rotates (a swell of
    /// about 15 dB every 17-21 s, a little brighter when it points at the truck, with a stylised Doppler), the wind
    /// tears holes in it, the town answers with three echoes. Generic chopper tone: not a copy of any product.
    /// When the tornado takes the town the far one is cut dead and the near one loses power in the middle of a rise.</summary>
    public sealed class SirenVoice
    {
        const int N = 2;
        const float Start = 412f, PowerLoss = 466.5f;
        static readonly float[] FMax = { 540f, 612f }, Period = { 5.0f, 5.4f }, Offset = { 0.38f, 0.71f }, Rot = { 17f, 21f };
        static readonly float[] DistOff = { -600f, 700f }, BearOff = { -6f, 9f }, Level = { 1f, 0.6f };
        static readonly float[] Turn0 = { 0.79f, 0.57f };     // both horns point at the road when the town goes dark
        readonly int sr;
        readonly float[] phase = new float[N], f = new float[N], rot = new float[N], gL = new float[N], gR = new float[N], alive = new float[N];
        readonly Svf[] lp = new Svf[N];
        readonly Wander[] tear = new Wander[N], flutter = new Wander[N];
        readonly DelayLine echo;
        readonly int tap1, tap2, tap3;
        readonly float echoA;
        float echoZ;
        readonly float[] own = new float[Ctx.Sub];
        bool primed;
        int tailHot;

        public SirenVoice(int sampleRate)
        {
            sr = sampleRate;
            echo = new DelayLine((int)(1.4f * sr));
            tap1 = (int)(0.35f * sr); tap2 = (int)(0.8f * sr); tap3 = (int)(1.3f * sr);
            echoA = Dsp.Pole(800f, sr);
            Reset();
        }

        public void Reset()
        {
            for (int i = 0; i < N; i++)
            {
                phase[i] = 0f; f[i] = 0f; rot[i] = 0.3f + 0.45f * i; gL[i] = gR[i] = 0f; alive[i] = 1f;
                lp[i].Reset(); tear[i].Reset(); flutter[i].Reset();
            }
            echo.Clear(); echoZ = 0f; primed = false; tailHot = 0;
            Array.Clear(own, 0, Ctx.Sub);
        }

        static float Blep(float t, float dt)
        {
            if (t < dt) { t /= dt; return t + t - t * t - 1f; }
            if (t > 1f - dt) { t = (t - 1f) / dt; return t * t + t + t + 1f; }
            return 0f;
        }

        public void Render(Ctx c)
        {
            int n = c.n; float dt = c.dt, t = (float)c.T;
            float level = c.s.siren;
            bool active = level > 0.0005f;
            if (active) tailHot = (int)(2f / dt);
            if (tailHot <= 0) { primed = false; return; }
            tailHot--;
            if (!primed)
            {   // wound up already when we arrive in the middle; winding up from rest at the very start
                primed = true;
                for (int i = 0; i < N; i++) { f[i] = t < Start + 3f ? 40f : 0.7f * FMax[i]; alive[i] = 1f; }
            }
            float[] wl = c.worldL, wr = c.worldR, sl = c.revL, sr_ = c.revR, w0 = c.w0;
            float vTruck = c.s.speed;
            for (int i = 0; i < N; i++)
            {
                float dist = MathF.Max(600f, c.s.townDist + DistOff[i]);
                float bearing = c.raw.townBearing + BearOff[i];
                // motor: power on for the first 52 % of each cycle
                float cyc = (t - Start) / Period[i] + Offset[i];
                cyc -= MathF.Floor(cyc);
                bool lost = t >= PowerLoss;
                bool powered = cyc < 0.52f && !lost && active;
                if (powered) f[i] += (FMax[i] - f[i]) * Dsp.Ease(1.8f, dt);
                else f[i] *= MathF.Exp(-dt / (lost ? (i == 0 ? 0.9f : 6f) : 6f));
                if (i == 1 && t > 460f && c.raw.siren < 0.86f) alive[i] = MathF.Max(0f, alive[i] - dt / 0.06f);   // cut dead

                // rotation of the horn: swell, brightness and a stylised Doppler
                float turn = (t - Start) / Rot[i] + Turn0[i];                             // a pure function of story time
                rot[i] = turn - MathF.Floor(turn);
                float ang = Dsp.TwoPi * rot[i];
                float beam = 0.5f + 0.5f * MathF.Cos(ang); beam *= beam; beam *= beam;
                float towards = MathF.Cos((bearing - c.raw.heading) * Dsp.Deg2Rad);       // 1 when driving straight at it
                float fq = f[i] * (1f + 0.004f * MathF.Sin(ang) + vTruck * towards / 340f + 0.003f * flutter[i].Tick(ref c.rng, 3f, 0.2f, dt));
                float hole = 0.62f + 0.38f * tear[i].Tick(ref c.rng, 0.5f, 0.9f, dt) * (0.4f + 0.6f * Dsp.Clamp01(c.wind01 * 2f));
                float amp = f[i] / FMax[i]; amp *= amp;
                float g = Mix.Siren * Level[i] * MathF.Pow(Dsp.Clamp01(level), 1.2f) * Dsp.Clamp(3800f / dist, 0.4f, 1.8f)
                          * (0.3f + 0.7f * beam) * hole * amp * alive[i];
                float pan = c.PanOf(bearing), front = 0.75f + 0.25f * c.FrontOf(bearing);
                float tl, tr; Dsp.Pan(pan, out tl, out tr);
                tl *= g * front * 1.414f; tr *= g * front * 1.414f;
                lp[i].Set(MathF.Max(Dsp.AirCutoff(dist) * 2.2f, 500f) * (0.7f + 0.9f * beam), 0.8f, sr);
                float inc = fq * c.invSr, ph = phase[i];
                float l = gL[i], dl = (tl - l) / n, r = gR[i], dr = (tr - r) / n;
                if (inc < 0.0002f) inc = 0.0002f;
                for (int j = 0; j < n; j++)
                {
                    ph += inc; if (ph >= 1f) ph -= 1f;
                    float t2 = ph + 0.58f; if (t2 >= 1f) t2 -= 1f;                        // pulse of 42 % duty
                    float sq = (ph < 0.42f ? 1f : -1f) + Blep(ph, inc) - Blep(t2, inc);
                    float s = lp[i].Lp(sq + 0.05f * w0[j]);
                    l += dl; r += dr;
                    wl[j] += l * s; wr[j] += r * s;
                    sl[j] += 0.3f * l * s; sr_[j] += 0.3f * r * s;
                    own[j] += 0.5f * (l + r) * s;
                }
                phase[i] = ph; gL[i] = tl; gR[i] = tr;
            }
            // the town's echoes (0.35 / 0.8 / 1.3 s): fed from what was just added, read dulled
            for (int j = 0; j < n; j++)
            {
                float e = 0.32f * echo.Tap(tap1) + 0.2f * echo.Tap(tap2) + 0.13f * echo.Tap(tap3);
                echoZ += echoA * (e - echoZ);
                echo.Write(own[j]); own[j] = 0f;
                wl[j] += 0.9f * echoZ; wr[j] += 1.1f * echoZ;
            }
        }
    }

    /// <summary>Transformer power flashes heard across the fields: an arc that buzzes at twice the mains rate for a
    /// third of a second, a bang with a low thump, then a sizzle. The air dulls them: from 3 km only the thump
    /// arrives, late and out of step with the teal light.</summary>
    public sealed class PopVoice
    {
        const int Voices = 3;
        const float BangAt = 0.35f;
        readonly int sr;
        readonly bool[] on = new bool[Voices];
        readonly Rng[] rng = new Rng[Voices];
        readonly float[] age = new float[Voices], gain = new float[Voices], bearing = new float[Voices], airA = new float[Voices];
        readonly float[] a1 = new float[Voices], a2 = new float[Voices], hz = new float[Voices], sizTau = new float[Voices], thF = new float[Voices];
        readonly Svf[] zapBp = new Svf[Voices], sizBp = new Svf[Voices];
        readonly Osc[] buzz = new Osc[Voices], thump = new Osc[Voices];
        readonly float[] pZap = new float[Voices], pBang = new float[Voices], pSiz = new float[Voices], pTh = new float[Voices];
        readonly float hpA;
        int nextVoice;

        public PopVoice(int sampleRate) { sr = sampleRate; hpA = Dsp.Pole(100f, sr); Reset(); }

        public void Reset()
        {
            for (int v = 0; v < Voices; v++) on[v] = false;
            nextVoice = 0;
        }

        public void Start(in SoundEvent e, float late)
        {
            int v = -1;
            for (int i = 0; i < Voices; i++) if (!on[i]) { v = i; break; }
            if (v < 0) { v = nextVoice; nextVoice = (nextVoice + 1) % Voices; }
            float d = MathF.Max(e.a, 250f);
            rng[v].Seed(e.seed ^ 0x51ED270Bu);
            on[v] = true; age[v] = late < 0f ? 0f : late; bearing[v] = e.b;
            gain[v] = Mix.Pop * Dsp.Clamp(e.c, 0.1f, 1.6f) * Dsp.Clamp(1500f / d, 0.15f, 1.4f);
            airA[v] = Dsp.Pole(Dsp.AirCutoff(d) * 1.3f, sr);
            a1[v] = a2[v] = hz[v] = 0f; pZap[v] = pBang[v] = pSiz[v] = pTh[v] = 0f;
            zapBp[v].Reset(); sizBp[v].Reset(); zapBp[v].Set(2400f, 0.7f, sr); sizBp[v].Set(5000f, 0.8f, sr);
            buzz[v].Init(0f); buzz[v].SetFreq(120f, sr); thump[v].Init(0f); thF[v] = 66f;
            sizTau[v] = rng[v].Range(0.3f, 0.9f);
        }

        public void Render(Ctx c)
        {
            int n = c.n; float dt = c.dt;
            float[] wl = c.worldL, wr = c.worldR, sl = c.revL, sr_ = c.revR;
            for (int v = 0; v < Voices; v++)
            {
                if (!on[v]) continue;
                age[v] += dt;
                float a = age[v], g = gain[v];
                float zap = a < BangAt ? (a / BangAt) * (a / BangAt) * 0.5f : 0.5f * MathF.Exp(-(a - BangAt) / 0.05f);
                float b = a - BangAt;
                float bang = b > 0f ? MathF.Exp(-b / 0.045f) : 0f;
                float th = b > 0f ? MathF.Exp(-b / 0.085f) : 0f;
                float siz = b > 0f ? 0.35f * MathF.Exp(-b / sizTau[v]) : 0f;
                if (b > 0f) thF[v] += (45f - thF[v]) * Dsp.Ease(0.12f, dt);
                thump[v].SetFreq(thF[v], sr); thump[v].Norm(); buzz[v].Norm();
                float tZap = g * zap * 1.5f, tBang = g * bang * 1.3f, tSiz = g * siz * 1.5f, tTh = g * th * 1.1f;
                float gz = pZap[v], dz = (tZap - gz) / n, gb = pBang[v], db = (tBang - gb) / n;
                float gs = pSiz[v], ds = (tSiz - gs) / n, gt = pTh[v], dth = (tTh - gt) / n;
                float tl, tr; Dsp.Pan(c.PanOf(bearing[v]), out tl, out tr);
                tl *= 1.414f; tr *= 1.414f;
                float x1 = a1[v], x2 = a2[v], hp = hz[v], aa = airA[v];
                Rng rg = rng[v];
                for (int j = 0; j < n; j++)
                {
                    float w = rg.Bi();
                    float bz = buzz[v].Tick(); bz = bz > 0.4f ? 1f : 0.15f;
                    hp += hpA * (w - hp);
                    gz += dz; gb += db; gs += ds; gt += dth;
                    float s = gz * bz * zapBp[v].Bp(w) + gb * (w - hp) + gs * bz * sizBp[v].Bp(w);
                    x1 += aa * (s - x1); x2 += aa * (x1 - x2);
                    float o = x2 + gt * thump[v].Tick();
                    wl[j] += tl * o; wr[j] += tr * o; sl[j] += 0.3f * tl * o; sr_[j] += 0.3f * tr * o;
                }
                rng[v] = rg; a1[v] = x1; a2[v] = x2; hz[v] = hp;
                pZap[v] = tZap; pBang[v] = tBang; pSiz[v] = tSiz; pTh[v] = tTh;
                if (a > BangAt + 3.5f) on[v] = false;
            }
        }
    }
}
