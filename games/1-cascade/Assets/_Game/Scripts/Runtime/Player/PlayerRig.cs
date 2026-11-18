using UnityEngine;
using UnityEngine.InputSystem;

namespace CASCADE
{
    /// <summary>
    /// The observer. Moves along PathDef by chainage with a small lateral drift inside the safety corridor, free look
    /// in manual mode, film-operator gaze in cinematic mode (critically damped look targets from Gaze). Never rolls.
    /// </summary>
    public class PlayerRig : MonoBehaviour
    {
        public Camera cam;
        public float S;                 // chainage, metres
        public float V;                 // speed along the path
        public bool Cinematic;
        public float Yaw, Pitch;        // degrees, world
        public bool Arrived;

        float drift, driftVel;
        float yawVel, pitchVel, fov = Config.BaseFov, fovVel;
        float stepPhase;
        float swayT;
        float manualBlend = 1f;         // eases FOV and sway back after leaving cinematic mode
        const float Sens = 0.075f;      // degrees per mouse count

        public void Place(float s, bool snapGaze)
        {
            S = Mathf.Clamp(s, 0f, PathDef.Length);
            V = Cinematic && s > 0.01f ? Config.BriskSpeed : 0f;
            if (snapGaze)
            {
                Vector3 eye = EyePos();
                Vector3 d; float f, sm, dr;
                Gaze.Eval(Story.Time, S, eye, out d, out f, out sm, out dr);
                MathX.YawPitchFromDir(d, out Yaw, out Pitch);
                if (Harness.Look != null && Harness.Look.Length >= 2) { Yaw = Harness.Look[0]; Pitch = Harness.Look[1]; }
                fov = Harness.Fov > 0f ? Harness.Fov : f; drift = Mathf.Clamp(dr, -PathDef.DriftLimit(S), PathDef.DriftLimit(S));
                yawVel = pitchVel = fovVel = driftVel = 0f;
            }
            Apply(0f);
        }

        public void FaceAlongPath()
        {
            Vector3 d = PathDef.FlatDir(S);
            Yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg; Pitch = 0f;
        }

        public void SetCinematic(bool on)
        {
            if (Cinematic == on) return;
            Cinematic = on;
            yawVel = pitchVel = 0f;
            if (!on) manualBlend = 0f;
        }

        Vector3 EyePos()
        {
            Vector3 p = PathDef.Pos(S);
            Vector3 f = PathDef.FlatDir(S);
            Vector3 right = new Vector3(f.z, 0f, -f.x);
            return p + right * drift + Vector3.up * Config.EyeHeight;
        }

        public void Step(float dt, bool allowInput)
        {
            var kb = Keyboard.current; var mouse = Mouse.current;
            float remaining = PathDef.Length - S;
            float target = 0f, strafe = 0f;
            if (Cinematic) target = Config.BriskSpeed;
            else if (allowInput && kb != null)
            {
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) target = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed ? Config.BriskSpeed : Config.WalkSpeed;
                else if (kb.sKey.isPressed || kb.downArrowKey.isPressed) target = -Config.BackSpeed;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) strafe -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) strafe += 1f;
            }
            if (remaining < 2.2f && target > 0f) target *= Mathf.Clamp01(remaining / 2.2f) * 0.9f + 0.1f * Mathf.Clamp01(remaining * 4f);
            float accel = Config.BriskSpeed / (Cinematic ? 0.5f : Config.Accel);
            V = Mathf.MoveTowards(V, target, accel * dt);
            float minS = Mathf.Max(0f, Story.MaxChainage - Config.BackLimit);
            S = Mathf.Clamp(S + V * dt, minS, PathDef.Length);
            if (S <= minS && V < 0f) V = 0f;
            if (remaining <= 0.05f) { V = 0f; Arrived = true; }

            // lateral drift inside the corridor
            float lim = PathDef.DriftLimit(S);
            Vector3 eye0 = EyePos();
            Vector3 gdir; float gfov, gsmooth, gdrift;
            Gaze.Eval(Story.Time, S, eye0, out gdir, out gfov, out gsmooth, out gdrift);
            if (Cinematic) drift = Mathf.SmoothDamp(drift, Mathf.Clamp(gdrift, -lim, lim), ref driftVel, 2.5f, 10f, dt);
            else drift = Mathf.Clamp(drift + strafe * 1.2f * dt, -lim, lim);
            drift = Mathf.Clamp(drift, -lim, lim);

            // look
            if (Cinematic)
            {
                float ty, tp;
                MathX.YawPitchFromDir(gdir, out ty, out tp);
                Yaw = Mathf.SmoothDampAngle(Yaw, ty, ref yawVel, gsmooth, 60f, dt);
                Pitch = Mathf.SmoothDamp(Pitch, tp, ref pitchVel, gsmooth, 45f, dt);
                fov = Mathf.SmoothDamp(fov, gfov, ref fovVel, 4f, 3f, dt);
            }
            else
            {
                if (allowInput && mouse != null)
                {
                    Vector2 d = mouse.delta.ReadValue();
                    Yaw += d.x * Sens;
                    Pitch = Mathf.Clamp(Pitch + d.y * Sens, -82f, 88f);
                }
                manualBlend = Mathf.MoveTowards(manualBlend, 1f, dt / 0.9f);
                fov = Mathf.SmoothDamp(fov, Config.BaseFov, ref fovVel, 0.3f, 60f, dt);
            }
            if (Harness.Look != null && Harness.Look.Length >= 2 && Harness.Any) { Yaw = Harness.Look[0]; Pitch = Harness.Look[1]; }
            if (Harness.Fov > 0f) fov = Harness.Fov;

            // footsteps
            float stride = PathDef.IsStairs(S) ? 0.62f : 0.78f;
            float before = stepPhase;
            stepPhase += Mathf.Abs(V) * dt / stride;
            if (Mathf.FloorToInt(stepPhase) != Mathf.FloorToInt(before)) { Story.FootSurface = (int)PathDef.SurfaceAt(S); Story.FootstepCount++; }
            Apply(dt);
        }

        void Apply(float dt)
        {
            swayT += dt;
            Vector3 eye = EyePos();
            float speed01 = Mathf.Clamp01(Mathf.Abs(V) / Config.BriskSpeed);
            float bobAmp = PathDef.IsStairs(S) ? 0.022f : 0.013f;
            eye.y += Mathf.Sin(stepPhase * Mathf.PI * 2f) * bobAmp * speed01;
            eye.y += Mathf.Sin(swayT * 1.1f) * 0.004f;                                   // breathing
            // handheld sway (<= 0.3 degrees) and tremor shake (<= 1 degree); yaw and pitch only, the horizon never rolls
            float sway = Cinematic ? 0.2f : 0.08f * manualBlend;
            float trem = Story.TremorLevel();
            float sy = sway * (0.75f * MathX.Noise1(swayT * 0.23f, 11) + 0.25f * MathX.Noise1(swayT * 0.9f, 12))
                     + trem * 0.75f * MathX.Noise1(swayT * 7.3f, 13);
            float sp = sway * (0.75f * MathX.Noise1(swayT * 0.19f, 14) + 0.25f * MathX.Noise1(swayT * 1.1f, 15))
                     + trem * 0.65f * MathX.Noise1(swayT * 8.9f, 16);
            transform.SetPositionAndRotation(eye, Quaternion.Euler(-(Pitch + sp), Yaw + sy, 0f));
            if (cam != null) cam.fieldOfView = fov;
        }
    }
}
