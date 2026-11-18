using System;

namespace ZERO
{
    /// <summary>White, pink and brown noise from the seeded xorshift generator (<see cref="Rng"/>).</summary>
    public struct NoiseGen
    {
        Rng rng;
        float b0, b1, b2, brown;

        public NoiseGen(uint seed) { rng = new Rng(seed); b0 = 0f; b1 = 0f; b2 = 0f; brown = 0f; }

        /// <summary>Uniform white noise in [-1, 1).</summary>
        public float White() { return rng.Signed(); }
        /// <summary>Uniform [0, 1).</summary>
        public float Uniform() { return rng.F(); }

        /// <summary>Pink noise (Kellet, economy version), about -3 dB per octave. RMS about 0.2.</summary>
        public float Pink()
        {
            float w = rng.Signed();
            b0 = 0.99765f * b0 + w * 0.0990460f;
            b1 = 0.96300f * b1 + w * 0.2965164f;
            b2 = 0.57000f * b2 + w * 1.0526913f;
            return (b0 + b1 + b2 + w * 0.1848f) * 0.2f;
        }

        /// <summary>Brown noise (leaky integrator), -6 dB per octave above about 150 Hz.</summary>
        public float Brown()
        {
            float w = rng.Signed();
            brown = (brown + 0.02f * w) / 1.02f;
            return brown * 3.5f;
        }

        public void Reset() { b0 = 0f; b1 = 0f; b2 = 0f; brown = 0f; }
    }

    /// <summary>Low-frequency oscillator with a double phase (accurate at fractions of a hertz).</summary>
    public struct Lfo
    {
        double phase, inc;

        public void Set(float hz, float fs) { inc = hz / fs; }
        public void SetPhase(float turns) { phase = turns - Math.Floor(turns); }
        public float Phase { get { return (float)phase; } }

        void Step()
        {
            phase += inc;
            if (phase >= 1.0) phase -= 1.0;
        }

        /// <summary>Sine, -1..1.</summary>
        public float Sin() { float v = Dsp.Sin01(phase); Step(); return v; }
        /// <summary>Raised cosine, 0..1, zero at phase 0.</summary>
        public float RaisedCos() { float v = 0.5f - 0.5f * Dsp.Sin01(phase + 0.25); Step(); return v; }
    }

    /// <summary>One-shot envelope: raised-cosine rise, exponential decay, and a closing fade that reaches exactly zero.</summary>
    public struct Env
    {
        float t, dt, attack, total, fadeStart, dec, level;
        bool on;

        public bool Active { get { return on; } }
        public float Time { get { return t; } }

        /// <param name="attackS">rise time in seconds</param>
        /// <param name="decayTau">time constant of the decay after the rise</param>
        /// <param name="totalS">total length; the last 25 % fades to zero</param>
        public void Trigger(float attackS, float decayTau, float totalS, float fs)
        {
            t = 0f; dt = 1f / fs; attack = attackS; total = totalS; fadeStart = totalS * 0.75f;
            dec = Dsp.Decay(decayTau, fs); level = 1f; on = true;
        }

        public void Stop() { on = false; }

        public float Tick()
        {
            if (!on) return 0f;
            float v;
            if (t < attack) v = 0.5f - 0.5f * Dsp.Sin01(t / attack * 0.5f + 0.25f);
            else { v = level; level *= dec; }
            if (t > fadeStart) v *= 1f - Dsp.Smooth01((t - fadeStart) / (total - fadeStart));
            t += dt;
            if (t >= total) on = false;
            return v;
        }
    }

    /// <summary>Smooth random walk for control-rate modulation: eases between random values in 0..1.</summary>
    public struct SlowRandom
    {
        Rng rng;
        float a, b, t, dur, minDur, maxDur;

        public SlowRandom(uint seed, float minSeconds, float maxSeconds)
        {
            rng = new Rng(seed);
            minDur = minSeconds; maxDur = maxSeconds;
            a = rng.F(); b = rng.F(); t = 0f;
            dur = rng.Range(minSeconds, maxSeconds);
        }

        /// <summary>Advance by dt seconds and return the value in 0..1.</summary>
        public float Next(float dt)
        {
            t += dt;
            if (t >= dur)
            {
                t -= dur; a = b; b = rng.F();
                dur = rng.Range(minDur, maxDur);
                if (t > dur) t = 0f;
            }
            float x = t / dur;
            x = x * x * (3f - 2f * x);
            return a + (b - a) * x;
        }
    }

    /// <summary>A decaying sine (one resonant mode) as a rotating phasor. Hit() adds energy without a click:
    /// the new partial starts at a zero crossing. Used for knocks, clicks, tinkles and creak pulses.</summary>
    public struct Ping
    {
        float re, im, c, s;
        public bool Active;

        public void Set(float freq, float decayTau, float fs)
        {
            if (freq > fs * 0.45f) freq = fs * 0.45f;
            double w = 2.0 * Math.PI * freq / fs;
            double r = Math.Exp(-1.0 / (decayTau * fs));
            c = (float)(r * Math.Cos(w)); s = (float)(r * Math.Sin(w));
        }

        public void Hit(float amp) { im -= amp; Active = true; }

        public float Tick()
        {
            float nr = re * c - im * s;
            float ni = re * s + im * c;
            re = nr; im = ni;
            return re;
        }

        /// <summary>Call at control rate: stops the mode when it has decayed below -130 dB.</summary>
        public void Zap()
        {
            if (re * re + im * im < 1e-13f) { re = 0f; im = 0f; Active = false; }
        }

        public void Reset() { re = 0f; im = 0f; Active = false; }
    }
}
