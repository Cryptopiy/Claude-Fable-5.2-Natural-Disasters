using System;

namespace MESOCYCLONE.Audio
{
    /// <summary>Thunder: a pool of eight voices. Each is one seeded noise source seen through three windows: the
    /// roll (low-passed by the air: 4 kHz at 300 m, below 120 Hz at 20 km, amplitude wandering like the far ends
    /// of the channel arriving), the boom (below 60 Hz) and, when close, the crack (a rising tear for half a
    /// second, then 2-4 hard bursts). Far thunder is slow, late and only low; near thunder cracks, then booms.
    /// The arrival time (flash + distance / 340) is computed on the main thread; a voice starts half a second
    /// early so the tear and the hush of the beds can prepare the hit.</summary>
    public sealed class ThunderVoice
    {
        public const double Lead = 0.5;
        const int Voices = 8;
        public float Hush;                          // how far the beds should step back right now (0..0.45)

        readonly int sr;
        readonly bool[] on = new bool[Voices], dying = new bool[Voices], hasNext = new bool[Voices];
        readonly SoundEvent[] next = new SoundEvent[Voices];
        readonly float[] nextLate = new float[Voices];
        readonly Rng[] rng = new Rng[Voices];
        readonly float[] age = new float[Voices], dur = new float[Voices], att = new float[Voices], near = new float[Voices];
        readonly float[] gain = new float[Voices], bearing = new float[Voices], boomTau = new float[Voices], boomAmp = new float[Voices];
        readonly float[] lpA = new float[Voices], norm = new float[Voices], airA = new float[Voices], send = new float[Voices];
        readonly float[] r1 = new float[Voices], r2 = new float[Voices], b1 = new float[Voices], b2 = new float[Voices];
        readonly float[] hz = new float[Voices], cz = new float[Voices];
        readonly float[] am = new float[Voices], amTarget = new float[Voices], amHold = new float[Voices];
        readonly float[] crackEnv = new float[Voices], crackDec = new float[Voices], crackNext = new float[Voices];
        readonly int[] cracksLeft = new int[Voices];
        readonly float[] pRoll = new float[Voices], pBoom = new float[Voices], pTear = new float[Voices];
        readonly float[] vf = new float[Voices], pl = new float[Voices], pr = new float[Voices], loud = new float[Voices];
        readonly float boomA, boomNorm, hpA;

        public ThunderVoice(int sampleRate)
        {
            sr = sampleRate;
            boomA = Dsp.Pole(52f, sr); boomNorm = Dsp.Norm2Pole(boomA); hpA = Dsp.Pole(750f, sr);
            Reset();
        }

        public void Reset()
        {
            for (int v = 0; v < Voices; v++) { on[v] = false; dying[v] = false; hasNext[v] = false; vf[v] = 0f; loud[v] = 0f; Clear(v); }
            Hush = 0f;
        }

        void Clear(int v)
        {
            r1[v] = r2[v] = b1[v] = b2[v] = hz[v] = cz[v] = 0f;
            crackEnv[v] = 0f; pRoll[v] = pBoom[v] = pTear[v] = 0f; am[v] = 0.6f; amTarget[v] = 0.6f; amHold[v] = 0f;
        }

        /// <summary>Length of a thunder in seconds: the far ends of a long channel keep arriving.</summary>
        public static float Duration(float dist, float power)
        {
            return Dsp.Clamp(4f + dist * 0.0014f + 3f * power, 5f, 26f);
        }

        static float DistanceGain(float d)
        {
            if (d < 3000f) return 0.4387f * MathF.Pow(3000f / MathF.Max(d, 450f), 0.45f);
            return 0.4387f * MathF.Pow(3000f / d, 1.15f);
        }

        /// <summary>late = seconds since the arrival (negative while the voice is still in its lead-in).</summary>
        public void Start(in SoundEvent e, float late)
        {
            float d = MathF.Max(e.a, 60f), p = Dsp.Clamp(e.c, 0.05f, 1.6f);
            float g = Mix.Thunder * MathF.Pow(p, 0.6f) * DistanceGain(d);
            int pick = -1;
            for (int v = 0; v < Voices; v++) if (!on[v]) { pick = v; break; }
            if (pick < 0)
            {   // all busy: the quietest one gives way, unless the newcomer is quieter still
                float lowest = g;
                for (int v = 0; v < Voices; v++) if (!dying[v] && loud[v] < lowest) { lowest = loud[v]; pick = v; }
                if (pick < 0) return;
                dying[pick] = true; hasNext[pick] = true; next[pick] = e; nextLate[pick] = late;
                return;
            }
            Begin(pick, e, late);
        }

        void Begin(int v, in SoundEvent e, float late)
        {
            float d = MathF.Max(e.a, 60f), p = Dsp.Clamp(e.c, 0.05f, 1.6f);
            int kind = (int)(e.d + 0.5f);                          // 0 sheet, 1 bolt, 2 crawler
            rng[v].Seed(e.seed ^ 0x7F4A7C15u);
            float nf = Dsp.Clamp01(1f - (d - 350f) / 2400f);
            if (kind == 1) nf = Dsp.Clamp01(nf * 1.25f + (nf > 0f ? 0.1f : 0f)); else nf *= 0.6f;
            near[v] = nf; bearing[v] = e.b;
            gain[v] = Mix.Thunder * MathF.Pow(p, 0.6f) * DistanceGain(d);
            float fc = MathF.Min(Dsp.AirCutoff(d) * (0.8f + 0.4f * nf), 700f);
            lpA[v] = Dsp.Pole(fc, sr); norm[v] = Dsp.Norm2Pole(lpA[v]);
            airA[v] = Dsp.Pole(MathF.Min(Dsp.AirCutoff(d) * 2.5f, 9000f), sr);
            dur[v] = Duration(d, p) * (kind == 2 ? 1.3f : 1f);
            att[v] = 0.12f + 1.9f * Dsp.Clamp01((d - 1500f) / 9000f) + (kind == 2 ? 0.5f : 0f);
            boomTau[v] = 0.7f + 1.3f * (1f - nf) + 0.4f * rng[v].Uni();
            boomAmp[v] = 0.55f + 0.75f * nf;
            send[v] = 0.42f - 0.2f * nf;
            cracksLeft[v] = nf > 0.15f ? 2 + (int)(rng[v].Uni() * 2.99f) : 0;
            crackNext[v] = 0f; crackDec[v] = MathF.Exp(-1f / (rng[v].Range(0.045f, 0.08f) * sr));
            Clear(v);
            age[v] = late; on[v] = true; dying[v] = false; hasNext[v] = false; vf[v] = 0f; loud[v] = gain[v];
            Dsp.Pan(0f, out pl[v], out pr[v]);
        }

        public void Render(Ctx c)
        {
            int n = c.n; float dt = c.dt;
            float hush = 0f;
            float[] wl = c.worldL, wr = c.worldR, sl = c.revL, sr_ = c.revR;
            for (int v = 0; v < Voices; v++)
            {
                if (!on[v]) continue;
                float a0 = age[v];
                age[v] = a0 + dt;
                float a = age[v], nf = near[v], g = gain[v], D = dur[v];

                // voice fade: in over 60 ms, out over 25 ms when it is stolen or finished
                float vf0 = vf[v];
                if (dying[v]) vf[v] = MathF.Max(0f, vf0 - dt / 0.025f); else vf[v] = MathF.Min(1f, vf0 + dt / 0.06f);
                if (a > D) dying[v] = true;

                // envelopes at the end of this sub-block
                float roll = 0f, boom = 0f, tear = 0f;
                if (a > 0f)
                {
                    roll = Dsp.Step(0f, att[v], a) * MathF.Exp(-MathF.Max(0f, a - att[v]) / (0.4f * D)) * (1f - Dsp.Step(0.72f * D, D, a));
                    boom = Dsp.Step(0f, 0.05f + 0.12f * (1f - nf), a) * MathF.Exp(-a / boomTau[v]) * boomAmp[v];
                    tear = nf * 0.1f * MathF.Exp(-a / 0.04f);
                }
                else
                {
                    float u = Dsp.Clamp01(1f + a / (float)Lead);
                    tear = nf * 0.1f * u * u;
                }
                // the roll wanders: far parts of the channel arriving
                amHold[v] -= dt;
                if (amHold[v] <= 0f) { amHold[v] = rng[v].Range(0.12f, 0.55f); float q = rng[v].Uni(); amTarget[v] = 0.3f + 0.7f * q * q + 0.25f * q; }
                am[v] += (amTarget[v] - am[v]) * Dsp.Ease(0.11f, dt);
                // crack bursts
                if (cracksLeft[v] > 0 && a >= crackNext[v])
                {
                    cracksLeft[v]--; crackNext[v] = a + rng[v].Range(0.05f, 0.17f);
                    float amp = nf * rng[v].Range(0.6f, 1f);
                    if (amp > crackEnv[v]) crackEnv[v] = amp;
                }
                float h = nf * MathF.Min(1f, g * 1.5f) * 0.45f * Dsp.Step(-(float)Lead, -0.12f, a) * (1f - Dsp.Step(0.25f, 1.3f, a));
                if (h > hush) hush = h;

                float tRoll = g * 1.25f * roll * am[v] * norm[v], tBoom = g * 1.1f * boom * boomNorm, tTear = g * tear * 2f, tCrack = g * 1.5f;
                float gR = pRoll[v], dR = (tRoll - gR) / n, gB = pBoom[v], dB = (tBoom - gB) / n, gT = pTear[v], dT = (tTear - gT) / n;
                float fv = vf0, dfv = (vf[v] - vf0) / n;
                float tp, tq; Dsp.Pan(c.PanOf(bearing[v]) * (0.3f + 0.7f * nf), out tp, out tq);
                pl[v] += (tp - pl[v]) * c.k50; pr[v] += (tq - pr[v]) * c.k50;
                float gl = pl[v] * 1.414f, gr = pr[v] * 1.414f, sd = send[v];
                float x1 = r1[v], x2 = r2[v], y1 = b1[v], y2 = b2[v], hp = hz[v], cp = cz[v], ce = crackEnv[v];
                float la = lpA[v], ba = boomA, ha = hpA, aa = airA[v], cd = crackDec[v];
                Rng rg = rng[v];
                for (int j = 0; j < n; j++)
                {
                    float w = rg.Bi();
                    x1 += la * (w - x1); x2 += la * (x1 - x2);
                    y1 += ba * (w - y1); y2 += ba * (y1 - y2);
                    hp += ha * (w - hp); cp += aa * (w - hp - cp);
                    gR += dR; gB += dB; gT += dT; fv += dfv;
                    float s = (gR * x2 + gB * y2 + (gT + ce * tCrack) * cp) * fv;
                    ce *= cd;
                    float l = gl * s, r = gr * s;
                    wl[j] += l; wr[j] += r; sl[j] += sd * l; sr_[j] += sd * r;
                }
                rng[v] = rg; r1[v] = x1; r2[v] = x2; b1[v] = y1; b2[v] = y2; hz[v] = hp; cz[v] = cp; crackEnv[v] = ce < 1e-7f ? 0f : ce;
                pRoll[v] = tRoll; pBoom[v] = tBoom; pTear[v] = tTear;
                loud[v] = g * (roll * 0.8f + boom + (a < 0f ? 1f : 0f));

                if (dying[v] && vf[v] <= 0f)
                {
                    on[v] = false; dying[v] = false;
                    if (hasNext[v]) { SoundEvent e = next[v]; Begin(v, e, nextLate[v] + 0.03f); }
                }
            }
            Hush += (hush - Hush) * c.k50;
        }
    }
}
