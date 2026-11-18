using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MESOCYCLONE
{
    /// <summary>Event director. Owns the truck's chainage, the story clock and the world shift.
    /// The truck (and the camera) stay at the world origin; the world root moves, so nothing near the eye ever
    /// suffers float jitter. Story time advances in real time and waits at a beat boundary until the truck has
    /// passed that beat's trigger chainage: events keep their designed duration at any driving speed.</summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class Director : MonoBehaviour
    {
        public static Director I;
        public static float T;                       // story time
        public static float Dt;                      // story delta of this frame (0 while holding or paused)
        public static float FrameDt;                 // game delta of this frame
        public static float Ambient;                 // ambient clock: always runs (rotation, noise, blinking)
        public static StoryState S;
        public static double TruckS;                 // chainage
        public static float Speed;                   // m/s
        public static double OriginX, OriginZ;       // world position of the truck (double precision)
        public static float HeadingDeg;
        public static bool Cinematic;
        public static bool Running;                  // false while loading
        public static bool Arrived;
        public static bool Paused;
        public static bool Freeze;                   // harness storyboard: story time stands still, ambient clocks run
        public static bool Warmup;                   // loading: everything evaluates, nothing advances
        public static float LaneNow;

        public Transform worldRoot;                  // everything that belongs to the land
        public Transform truck;                      // stays at the origin; rotates with the road

        float setSpeed = Config.SpeedDefault;
        float runClock;
        int beatReached;
        public const double GoalS = 15810.0;
        const float BrakeDecel = 4f;                 // 24 m/s -> 0 in 6 s over 72 m

        void Awake() { I = this; }

        /// <summary>Lateral position of the truck: right lane, pulling off under the canopy at the very end.</summary>
        public static float LaneOffset(double s)
        {
            float u = Mathf.Clamp01((float)((s - (GoalS - 170.0)) / 150.0));
            u = u * u * (3f - 2f * u);
            return Mathf.Lerp(Config.LaneOffset, 10.5f, u);
        }

        public void Begin(float startAt)
        {
            T = Mathf.Max(0f, startAt);
            TruckS = Math.Min(T * Config.SpeedBrisk, GoalS);
            Speed = startAt > 0f ? Config.SpeedBrisk : 0f;
            runClock = startAt > 0f ? 10f : 0f;
            beatReached = Config.BeatAt(T);
            Arrived = TruckS >= GoalS - 0.01;
            Ambient = T;
            Running = true;
            Step(0f);
        }

        void Update()
        {
            if (!Running) return;
            FrameDt = (Paused || Warmup) ? 0f : Time.deltaTime;
            Step(FrameDt);
        }

        /// <summary>Harness: jump to story time t on the cinematic schedule.</summary>
        public void Jump(float t)
        {
            T = Mathf.Max(0f, t);
            TruckS = Math.Min(T * Config.SpeedBrisk, GoalS);
            Speed = Config.SpeedBrisk;
            runClock = 10f;
            beatReached = Config.BeatAt(T);
            Arrived = TruckS >= GoalS - 0.01;
            Step(0f);
        }

        void Step(float dt)
        {
            Ambient += dt;
            if (Freeze) dt = 0f;
            runClock += dt;

            // ---- speed ----
            float target;
            if (Cinematic) target = Config.SpeedBrisk;
            else
            {
                var kb = Keyboard.current;
                if (kb != null)
                {
                    if (kb.wKey.isPressed || kb.upArrowKey.isPressed) setSpeed += 6f * dt;
                    if (kb.sKey.isPressed || kb.downArrowKey.isPressed) setSpeed -= 6f * dt;
                    setSpeed = Mathf.Clamp(setSpeed, Config.SpeedMin, Config.SpeedDefault);
                    target = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed ? Config.SpeedBrisk : setSpeed;
                }
                else target = setSpeed;
            }
            float remaining = (float)(GoalS - TruckS);
            float brakeCap = Mathf.Sqrt(Mathf.Max(0f, 2f * BrakeDecel * remaining));
            if (remaining < 3f) brakeCap = Mathf.Max(brakeCap * 0.6f, remaining * 1.2f);    // soft landing
            float tau = runClock < 1.5f ? Config.CinematicEaseIn * 0.4f : Config.SpeedAccelTime;
            Speed = Mathf.Lerp(Speed, target, 1f - Mathf.Exp(-dt / Mathf.Max(0.01f, tau)));
            if (Speed > brakeCap) Speed = brakeCap;
            if (remaining <= 0.02f) { Speed = 0f; Arrived = true; }
            TruckS = Math.Min(GoalS, TruckS + Speed * dt);

            // ---- story clock ----
            while (beatReached < Config.BeatCount - 1 && TruckS >= Config.BeatTriggerChainage(beatReached + 1)) beatReached++;
            float limit = beatReached < Config.BeatCount - 1 ? Config.BeatEnd[beatReached] : (Arrived ? float.MaxValue : Config.GoalTime - 0.5f);
            float newT = Mathf.Min(T + dt, Mathf.Max(T, limit));
            Dt = newT - T;
            T = newT;

            // ---- state ----
            Story.Evaluate(T, ref S);
            S.truckS = TruckS; S.truckSpeed = Speed;

            // ---- world shift ----
            LaneNow = LaneOffset(TruckS);
            PathDef.RoadToWorld(TruckS, LaneNow, out OriginX, out OriginZ);
            double hd = PathDef.HeadingDeg(TruckS);
            // the pull-off under the canopy turns the truck slightly
            double ds = 2.0;
            hd += Math.Atan2(LaneOffset(TruckS + ds) - LaneOffset(TruckS - ds), 2.0 * ds) * 180.0 / Math.PI;
            HeadingDeg = (float)hd;
            if (worldRoot != null) worldRoot.position = new Vector3((float)-OriginX, 0f, (float)-OriginZ);
            if (truck != null) truck.rotation = Quaternion.Euler(0f, HeadingDeg, 0f);
        }

        /// <summary>Sets the whole state for story time T on the cinematic schedule, without a scene (offline audio
        /// render, tools). The truck is where the cinematic mode would have it.</summary>
        public static void Seek(float t)
        {
            T = Mathf.Max(0f, t); Dt = 0f; FrameDt = 0f; Ambient = T;
            TruckS = Math.Min(T * Config.SpeedBrisk, GoalS);
            Speed = T < Config.GoalTime ? Config.SpeedBrisk : 0f;
            Story.Evaluate(T, ref S);
            S.truckS = TruckS; S.truckSpeed = Speed;
            LaneNow = LaneOffset(TruckS);
            PathDef.RoadToWorld(TruckS, LaneNow, out OriginX, out OriginZ);
            HeadingDeg = (float)PathDef.HeadingDeg(TruckS);
        }

        /// <summary>World (double) to the moving local frame used by all rendering.</summary>
        public static Vector3 ToLocal(double x, float y, double z)
        {
            return new Vector3((float)(x - OriginX), y, (float)(z - OriginZ));
        }

        public static Vector3 RoadToLocal(double s, double lateral, float y)
        {
            PathDef.RoadToWorld(s, lateral, out double x, out double z);
            return new Vector3((float)(x - OriginX), y, (float)(z - OriginZ));
        }

        public static Vector3 TornadoLocal(float y = 0f)
        {
            return RoadToLocal(S.torS, S.torLat, y);
        }

        public static Vector3 StormLocal(float y = 0f)
        {
            return ToLocal(S.stormX, y, S.stormZ);
        }
    }
}
