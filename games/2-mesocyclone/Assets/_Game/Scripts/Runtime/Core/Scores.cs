using UnityEngine;

namespace MESOCYCLONE
{
    /// <summary>Wipers as a pure function of story time, shared by the picture (blades, cleared glass) and the
    /// sound (sweep, squeak). Mode keys mirror the "wiper" curve of Story: 0 off, 1 intermittent, 2 normal, 3 fast.</summary>
    public static class WiperModel
    {
        // (time, mode) steps
        static readonly float[] Keys = { 0, 0, 244, 2, 250, 3, 352, 2, 372, 1, 484, 3, 572, 2, 596, 1, 622, 0 };
        static readonly float[] Period = { 0f, 4.2f, 1.45f, 0.82f };     // seconds per full cycle (out and back)
        const float SweepIntermittent = 1.3f;                             // the sweep itself in intermittent mode

        public static int Mode(float T)
        {
            int m = 0;
            for (int i = 0; i < Keys.Length; i += 2) if (T >= Keys[i]) m = (int)Keys[i + 1];
            return m;
        }

        /// <summary>Blade position 0 (parked, bottom) .. 1 (full sweep). Each mode restarts its cycle at its key
        /// time, so the function is stateless and seekable.</summary>
        public static float Position(float T)
        {
            int k = 0;
            for (int i = 0; i < Keys.Length; i += 2) if (T >= Keys[i]) k = i;
            int m = (int)Keys[k + 1];
            if (m == 0) return 0f;
            float ph = Mathf.Repeat(T - Keys[k], Period[m]);
            float dur = m == 1 ? SweepIntermittent : Period[m];
            if (ph >= dur) return 0f;
            float u = ph / dur;                                           // 0..1 over out-and-back
            float tri = u < 0.5f ? u * 2f : 2f - u * 2f;
            return tri * tri * (3f - 2f * tri);
        }

        /// <summary>Phase of the current sweep for the glass shader: seconds since the sweep started, the sweep duration
        /// and the cycle period of the current mode.</summary>
        public static void Sweep(float T, out float since, out float duration, out float cycle)
        {
            int k = 0;
            for (int i = 0; i < Keys.Length; i += 2) if (T >= Keys[i]) k = i;
            int m = (int)Keys[k + 1];
            if (m == 0) { since = 99f; duration = 1f; cycle = 1f; return; }
            cycle = Period[m];
            duration = m == 1 ? SweepIntermittent : Period[m];
            since = Mathf.Repeat(T - Keys[k], cycle);
        }

        /// <summary>Story time at which the wipers last stopped (for the glass refilling when they are off).</summary>
        public static float LastStop(float T)
        {
            float last = -999f;
            for (int i = 2; i < Keys.Length; i += 2)
                if (T >= Keys[i] && (int)Keys[i + 1] == 0) last = Keys[i];
            return last;
        }

        /// <summary>Time since the blade last passed position x (0..1) of the sweep; large when never wiped.</summary>
        public static float SinceWiped(float T, float x)
        {
            // search back through the last cycles (cheap: at most a few evaluations per call on the CPU side;
            // the shader uses its own closed form with the same constants)
            float best = 999f;
            for (float back = 0f; back < 5f; back += 0.04f)
            {
                float a = Position(T - back), b = Position(T - back - 0.04f);
                if ((a - x) * (b - x) <= 0f && (a != b)) { best = back; break; }
            }
            return best;
        }
    }

    /// <summary>The big hail stones: single events (bigger means fewer), deterministic, shared by picture and sound.
    /// Small hail is statistical (rate and size from StoryState); these are the ones that are seen and felt.</summary>
    public static class HailScore
    {
        public const float SlotLen = 0.25f;
        public enum Where { Roof, Hood, Glass, Road }

        static uint Hash(uint x) { x ^= x >> 16; x *= 0x7feb352du; x ^= x >> 15; x *= 0x846ca68bu; x ^= x >> 16; return x; }
        static float H01(uint x) => (Hash(x) & 0xFFFFFF) / 16777216f;

        /// <summary>The big hit of slot k (slot = 0.25 s of story time), if there is one.</summary>
        public static bool BigHit(int k, out float time, out Where where, out float size, out Vector2 pos)
        {
            time = 0f; where = Where.Roof; size = 0f; pos = Vector2.zero;
            float ts = k * SlotLen;
            if (ts < 286f || ts > 344.4f) return false;
            // probability grows with stone size: golf balls a few per second, softballs one every 1-3 s but heavy
            float sz = Mathf.Lerp(0.04f, 0.11f, Mathf.InverseLerp(286f, 322f, ts));
            float perSecond = ts < 300f ? 2.2f : (ts < 318f ? 1.6f : 0.9f);
            uint h = (uint)k * 2246822519u + 0x9e37u;
            if (H01(h) > perSecond * SlotLen) return false;
            time = ts + H01(h + 1u) * SlotLen;
            float w = H01(h + 2u);
            where = w < 0.45f ? Where.Roof : (w < 0.8f ? Where.Hood : (w < 0.9f ? Where.Glass : Where.Road));
            size = sz * Mathf.Lerp(0.75f, 1.1f, H01(h + 3u));
            pos = new Vector2(H01(h + 4u), H01(h + 5u));
            return true;
        }
    }
}
