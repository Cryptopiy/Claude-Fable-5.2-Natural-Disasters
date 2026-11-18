using UnityEngine;
using UnityEngine.InputSystem;

namespace ZERO
{
    /// <summary>The passenger: a head in the left window seat of the aircraft. Mouse look; the head moves so that the eye
    /// stays on a line through the window (W / S lean to and from the glass); Shift or the right mouse button looks
    /// hard at one thing (zoom). In cinematic mode the director moves head, eyes and zoom. The rig also flies the
    /// aircraft: position from the timetable, attitude from cruise motion, turbulence and the blast.</summary>
    public class SeatRig : MonoBehaviour
    {
        public static SeatRig I;
        public Camera Cam;
        public Transform Plane;                 // the aircraft (unity space), parent of the cabin and the wing
        public Transform Wing;                  // flexes and shakes
        public float Yaw = -84f, Pitch = -3f;   // plane-local look: yaw 0 = nose, -90 = straight out of the left window
        public float Lean = 0.45f;              // 0 = sitting back, 1 = at the glass
        public bool Cinematic, Frozen, InputEnabled = true, FovLock, HadInput;
        public Vector3 EyeAbs;                  // the eye on the flat map
        public float ExtraRoll, ExtraShake;     // set by the ending (the strike)
        public const float MouseSens = 0.085f;

        Vector3 head = PlaneDef.Eye, headVel;
        float yawVel, pitchVel, fovVel, leanVel, leanTarget = 0.45f, wantFov = Config.BaseFov;
        Quaternion wingRest = Quaternion.identity;

        void Awake() { I = this; }

        public void SetCinematic(bool on)
        {
            Cinematic = on;
            if (!on) leanTarget = Lean;
        }

        public void SetWing(Transform w) { Wing = w; if (w != null) wingRest = w.localRotation; }

        /// <summary>Harness: an exact view (no smoothing).</summary>
        public void SetView(float yaw, float pitch, float fov, float lean)
        {
            Yaw = yaw; Pitch = pitch; Lean = leanTarget = lean; wantFov = fov; Cam.fieldOfView = fov;
            yawVel = pitchVel = fovVel = leanVel = 0f; headVel = Vector3.zero;
            head = HeadTarget();
        }

        public static Vector3 LookDir(float yaw, float pitch) { return Quaternion.Euler(-pitch, yaw, 0f) * Vector3.forward; }

        // the eye sits on the line through the centre of the window along the look direction
        Vector3 HeadTarget()
        {
            Vector3 dir = LookDir(Yaw, Pitch);
            float outK = Mathf.Clamp01((-dir.x - 0.12f) / 0.4f);
            var wc = new Vector3(-(PlaneDef.WallX + 0.07f), PlaneDef.WindowY, PlaneDef.WindowZ0);
            float r = Mathf.Lerp(0.56f, 0.07f, Lean * Lean);
            Vector3 want = wc - dir * r;
            want.x = Mathf.Clamp(want.x, -1.70f, -1.24f); want.y = Mathf.Clamp(want.y, 1.02f, 1.32f); want.z = Mathf.Clamp(want.z, -0.42f, 0.48f);
            return Vector3.Lerp(PlaneDef.Eye, want, outK);
        }

        static float N(float t, float seed) { return Mathf.PerlinNoise(t, seed) * 2f - 1f; }

        /// <summary>Attitude of the aircraft at event time T (degrees: pitch, yaw, roll) and its height offset.</summary>
        public static Vector3 Attitude(float T, float extraShake, out float bump)
        {
            float s = Mathf.Clamp01(Schedule.Shake(T) + extraShake);
            float roll = 0.7f * Mathf.Sin(T * 0.47f) + 0.35f * Mathf.Sin(T * 1.13f + 1f);
            float pitch = -1.8f + 0.25f * Mathf.Sin(T * 0.31f);                      // nose a little up in cruise
            float yaw = 0.2f * Mathf.Sin(T * 0.23f);
            roll += s * (7f * N(T * 2.1f, 3.3f) + 3.5f * N(T * 6.7f, 9.1f));
            pitch += s * (3.2f * N(T * 1.7f, 17.7f) + 1.8f * N(T * 7.9f, 5.5f));
            yaw += s * 2.4f * N(T * 2.9f, 29.3f);
            bump = s * (26f * N(T * 1.3f, 41.1f) + 5f * N(T * 9.3f, 57.9f));
            return new Vector3(pitch, yaw, roll);
        }

        public void Tick(float dt, float T)
        {
            var kb = Keyboard.current; var mouse = Mouse.current;
            if (Cinematic)
            {
                CinematicDirector.Evaluate(T, EyeAbs, Plane.rotation, out float ty, out float tp, out float tf, out float tl, out float rate);
                Yaw = Mathf.SmoothDampAngle(Yaw, ty, ref yawVel, 0.8f, rate, dt);
                Pitch = Mathf.SmoothDampAngle(Pitch, tp, ref pitchVel, 0.8f, rate, dt);
                wantFov = tf; leanTarget = tl;
            }
            else if (InputEnabled && !Frozen)
            {
                if (kb != null)
                {
                    float f = (kb.wKey.isPressed || kb.upArrowKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed || kb.downArrowKey.isPressed ? 1f : 0f);
                    if (f != 0f) { leanTarget = Mathf.Clamp01(leanTarget + f * 0.9f * dt); HadInput = true; }
                }
                bool zoom = (kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed)) || (mouse != null && mouse.rightButton.isPressed);
                if (!FovLock) wantFov = zoom ? Config.ZoomFov : Config.BaseFov;
                if (mouse != null && Cursor.lockState == CursorLockMode.Locked)
                {
                    Vector2 d = mouse.delta.ReadValue();
                    if (d.sqrMagnitude > 0f) HadInput = true;
                    float k = MouseSens * Cam.fieldOfView / Config.BaseFov;
                    Yaw = Mathf.Clamp(Yaw + d.x * k, -176f, 120f); Pitch = Mathf.Clamp(Pitch + d.y * k, -70f, 70f);
                }
            }
            Lean = Mathf.SmoothDamp(Lean, leanTarget, ref leanVel, 0.45f, 2f, dt);
            if (!FovLock || Cinematic) Cam.fieldOfView = Mathf.SmoothDamp(Cam.fieldOfView, wantFov, ref fovVel, Cinematic ? 1.6f : 0.35f, 60f, dt);
            head = Vector3.SmoothDamp(head, HeadTarget(), ref headVel, 0.3f, 3f, dt);
            Place(T);
        }

        /// <summary>Puts the aircraft and the camera where they are at event time T.</summary>
        public void Place(float T)
        {
            Vector3 att = Attitude(T, ExtraShake, out float bump);
            Vector3 planeAbs = Schedule.PlanePos(T) + Vector3.up * bump;
            FloatingOrigin.Maybe(planeAbs);
            Plane.SetPositionAndRotation(FloatingOrigin.ToUnity(planeAbs), Quaternion.Euler(att.x, att.y, att.z + ExtraRoll));
            if (Wing != null)
            {
                // the wing flexes with the load: slow breathing in cruise, hard when the air is rough
                float s = Mathf.Clamp01(Schedule.Shake(T) + ExtraShake);
                float flex = 0.5f * Mathf.Sin(T * 0.9f) + s * (4.5f * N(T * 3.3f, 71.3f) + 2f * N(T * 9.1f, 83.7f));
                Wing.localRotation = wingRest * Quaternion.Euler(0f, 0f, -flex);
            }
            // the head is not bolted to the seat: it lags the airframe a little
            float sk = Mathf.Clamp01(Schedule.Shake(T) + ExtraShake);
            Vector3 jitter = new Vector3(N(T * 5.1f, 11f), N(T * 6.3f, 23f), N(T * 4.7f, 37f)) * (0.004f + 0.035f * sk);
            Quaternion look = Quaternion.Euler(-Pitch + sk * 1.4f * N(T * 7.7f, 47f), Yaw + sk * 1.1f * N(T * 6.1f, 59f), sk * 2.2f * N(T * 5.3f, 67f));
            Cam.transform.SetPositionAndRotation(Plane.TransformPoint(head + jitter), Plane.rotation * look);
            // the planet is bent in the vertex shaders: far things are drawn up to hundreds of kilometres lower than
            // their flat positions, so they are culled against a wide frustum (there are few objects outside)
            Cam.cullingMatrix = Matrix4x4.Perspective(Mathf.Max(Cam.fieldOfView * 2f, 120f), Cam.aspect, Cam.nearClipPlane, Cam.farClipPlane) * Cam.worldToCameraMatrix;
            EyeAbs = FloatingOrigin.ToAbs(Cam.transform.position);
        }
    }
}
