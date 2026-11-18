using UnityEngine;

namespace ZERO
{
    /// <summary>The last seconds: the wall of the blast wave reaches the aircraft. For one breath the light and the sound
    /// are sucked away; then the strike: the airframe is thrown, light floods in, a single ramp to white, silence with
    /// a faint ring, a slow fade to black, the closing line.</summary>
    public static class Ending
    {
        public enum Phase { None, Breath, Hit, White, ToBlack, Line, Done }
        public static Phase State = Phase.None;
        public static float T;            // seconds in the current phase
        public static float Clock;        // seconds since the ending began
        public static int AudioPhase;     // 0 normal, 1 breath, 2 the hit, 3 white, 4 black

        public static void Reset() { State = Phase.None; T = 0f; Clock = 0f; AudioPhase = 0; ShaderGlobals.ExposureMul = 1f; }

        static void Go(Phase p) { State = p; T = 0f; }
        static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

        public static void Tick(float dt, float eventT, SeatRig rig, UiOverlay ui)
        {
            if (State == Phase.None)
            {
                if (eventT >= Schedule.ArrivalTime - Config.EndBreath) { Go(Phase.Breath); AudioPhase = 1; }
                return;
            }
            T += dt; Clock += dt;
            switch (State)
            {
                case Phase.Breath:
                    // the wall stands between the aircraft and every light there is
                    ui.Black = 0.62f * Smooth(T / Config.EndBreath);
                    rig.ExtraShake = 0.25f * Smooth(T / Config.EndBreath);
                    if (T >= Config.EndBreath) { Go(Phase.Hit); AudioPhase = 2; }
                    break;
                case Phase.Hit:
                {
                    // the strike: the left wing is thrown up, light floods in (a ramp, never a flash), then white
                    rig.ExtraShake = 1f;
                    rig.ExtraRoll = 34f * Smooth(T / Config.ResumeDuration);
                    ShaderGlobals.ExposureMul = 1f + 1.6f * Mathf.Clamp01(T / Config.ResumeDuration);
                    float w = Smooth((T - Config.WhiteStart) / Config.WhiteRamp);
                    ui.White = w; ui.Black = 0.62f * (1f - Smooth(T / Config.WhiteStart));
                    if (T >= Config.ResumeDuration) { Go(Phase.White); AudioPhase = 3; ui.White = 1f; ui.Black = 0f; rig.Frozen = true; }
                    break;
                }
                case Phase.White:
                    ui.White = 1f;
                    if (T >= Config.WhiteHold) { Go(Phase.ToBlack); ui.Black = 0f; }
                    break;
                case Phase.ToBlack:
                {
                    float k = Smooth(T / Config.FadeToBlack);
                    ui.Black = k;
                    if (T >= Config.FadeToBlack) { Go(Phase.Line); AudioPhase = 4; ui.Black = 1f; ui.White = 0f; ShaderGlobals.ExposureMul = 1f; }
                    break;
                }
                case Phase.Line:
                    ui.Closing = Mathf.Clamp01((T - 0.8f) / 2.2f);
                    if (T >= 8f) Go(Phase.Done);
                    break;
                case Phase.Done:
                    ui.Closing = 1f;
                    break;
            }
        }
    }
}
