using UnityEngine;
using UnityEngine.InputSystem;

namespace MESOCYCLONE
{
    /// <summary>The driver's head. Manual mode: raw mouse look (no smoothing lag). Cinematic mode: a film-camera
    /// operator with critically damped look targets that follow the gaze cues, eased FOV and a tiny handheld sway.
    /// The horizon never rolls: the cab may roll around the head, the camera does not.</summary>
    [DefaultExecutionOrder(-500)]
    public sealed class CameraRig : MonoBehaviour
    {
        public static CameraRig I;
        public Camera cam;
        public Transform eyeAnchor;          // neck pivot inside the cab (moves with the cab body)
        public static float Yaw, Pitch;      // truck-relative, degrees (pitch up +)
        public static float Fov = Config.FovDefault;
        public static float NoticeTime = -10f;   // real time of the last cinematic toggle (for the 2 s notice)

        float yawVel, pitchVel, fovVel;
        float handBack;                      // 1 -> 0 during the second after C hands control back
        float seed;

        void Awake() { I = this; seed = 17.3f; }

        public void ToggleCinematic()
        {
            Director.Cinematic = !Director.Cinematic;
            NoticeTime = Time.unscaledTime;
            yawVel = pitchVel = 0f;
            if (!Director.Cinematic) handBack = 1f;
        }

        void Update()
        {
            if (!Director.Running || cam == null) return;
            var kb = Keyboard.current;
            if (kb != null && kb.cKey.wasPressedThisFrame && !Director.Paused) ToggleCinematic();
            float dt = Director.FrameDt;
            Vector3 neck = eyeAnchor != null ? eyeAnchor.position : new Vector3(Config.EyeX, Config.EyeY - Config.NeckDrop, Config.EyeZ - Config.NeckBack);

            if (Harness.BoardLook)
            {
                Yaw = Harness.BoardYaw; Pitch = Harness.BoardPitch; Fov = Harness.BoardFov;
            }
            else if (Cli.HasLook)
            {
                Yaw = Cli.LookYaw; Pitch = Cli.LookPitch; Fov = Cli.LookFov;
            }
            else if (Director.Cinematic)
            {
                if (Gaze.Evaluate(Director.T, neck, out float ty, out float tp, out float tf, out float smooth))
                {
                    // long steps (fast-forward harness) must not blow up the spring
                    float sdt = Mathf.Min(dt, 0.05f);
                    int sub = Mathf.Clamp(Mathf.CeilToInt(dt / 0.05f), 1, 40);
                    for (int i = 0; i < sub; i++)
                    {
                        Yaw = Mathf.SmoothDamp(Yaw, ty, ref yawVel, smooth, 60f, sdt);
                        Pitch = Mathf.SmoothDamp(Pitch, tp, ref pitchVel, smooth, 40f, sdt);
                        Fov = Mathf.SmoothDamp(Fov, tf, ref fovVel, 3.5f, 3f, sdt);
                    }
                }
            }
            else
            {
                var mouse = Mouse.current;
                if (mouse != null && !Director.Paused && Cursor.lockState == CursorLockMode.Locked)
                {
                    Vector2 d = mouse.delta.ReadValue();
                    Yaw += d.x * Config.MouseSensitivity;
                    Pitch += d.y * Config.MouseSensitivity;          // mouse up = look up (not inverted)
                }
                var pad = Gamepad.current;
                if (pad != null)
                {
                    Vector2 r = pad.rightStick.ReadValue();
                    Yaw += r.x * 110f * dt; Pitch += r.y * 80f * dt;
                }
                // FOV returns to the default within the second after the hand-back
                handBack = Mathf.Max(0f, handBack - dt);
                Fov = Mathf.SmoothDamp(Fov, Config.FovDefault, ref fovVel, 0.3f, 100f, Mathf.Max(dt, 1e-4f));
            }
            Yaw = Mathf.Clamp(Yaw, -Config.YawLimit, Config.YawLimit);
            Pitch = Mathf.Clamp(Pitch, Config.PitchMin, Config.PitchMax);
            if (!Harness.BoardLook) Fov = Mathf.Clamp(Fov, Config.FovMin, Config.FovMax);

            // breathing / handheld sway and gust shake: yaw and pitch only, never roll
            float a = Director.Ambient;
            float sway = Director.Cinematic ? Config.SwayMaxDeg : Config.SwayMaxDeg * 0.5f;
            float sy = (Mathf.PerlinNoise(a * 0.23f, seed) - 0.5f) * 2f * sway;
            float sp = (Mathf.PerlinNoise(seed, a * 0.19f) - 0.5f) * 2f * sway * 0.7f;
            float shakeAmp = Director.S.shake * Config.ShakeMaxDeg * (Director.Cinematic ? 0.5f : 1f);
            float ky = (Mathf.PerlinNoise(a * 7.3f, seed + 9f) - 0.5f) * 2f * shakeAmp;
            float kp = (Mathf.PerlinNoise(seed + 5f, a * 8.9f) - 0.5f) * 2f * shakeAmp;
            if (Cli.HasLook || Harness.BoardLook) { sy = sp = ky = kp = 0f; }

            Quaternion rot = Quaternion.Euler(-(Pitch + sp + kp), Director.HeadingDeg + Yaw + sy + ky, 0f);
            Transform t = cam.transform;
            t.SetPositionAndRotation(neck + rot * new Vector3(0f, Config.NeckDrop, Config.NeckBack), rot);
            cam.fieldOfView = Fov;
        }
    }
}
