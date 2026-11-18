#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System.Collections;
using System.Globalization;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace MESOCYCLONE
{
    /// <summary>Development builds only (<c>--test input</c>): presses the real keys through the Input System so the
    /// manual controls run their real code paths without a person at the keyboard: C hands the camera back,
    /// Shift / W / S change the speed, the mouse turns the view, Esc pauses and resumes. Results go to the log.</summary>
    public sealed class InputSelfTest : MonoBehaviour
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        static void Log(string what)
        {
            Debug.Log("[Test] " + what + ": T=" + Director.T.ToString("F1", Inv) + " speed=" + Director.Speed.ToString("F1", Inv) +
                      " cinematic=" + Director.Cinematic + " paused=" + Director.Paused + " yaw=" + CameraRig.Yaw.ToString("F1", Inv) +
                      " pitch=" + CameraRig.Pitch.ToString("F1", Inv));
        }

        static void Keys(params Key[] down)
        {
            if (Keyboard.current != null) InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(down));
        }

        IEnumerator Tap(Key k)
        {
            Keys(k); yield return null; yield return null;
            Keys(); yield return null;
        }

        IEnumerator Start()
        {
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            while (!Director.Running) yield return null;
            yield return new WaitForSeconds(2f);
            Log("start (keyboard " + (Keyboard.current != null) + ", mouse " + (Mouse.current != null) + ")");

            yield return Tap(Key.C);                                   // cinematic -> manual
            yield return new WaitForSeconds(1.2f);
            Log("after C");
            yield return new WaitForSeconds(3f);
            Log("manual, no keys (default speed)");
            Keys(Key.LeftShift); yield return new WaitForSeconds(2.5f);
            Log("holding Shift");
            Keys(); yield return new WaitForSeconds(2.5f);
            Log("Shift released");
            Keys(Key.S); yield return new WaitForSeconds(1.5f);
            Log("held S 1.5 s");
            Keys(Key.W); yield return new WaitForSeconds(0.7f);
            Log("held W 0.7 s");
            Keys();

            if (Mouse.current != null)
            {
                float yaw0 = CameraRig.Yaw, pitch0 = CameraRig.Pitch;
                Cursor.lockState = CursorLockMode.Locked;
                yield return null;
                for (int i = 0; i < 12; i++)
                {
                    InputSystem.QueueStateEvent(Mouse.current, new MouseState { delta = new Vector2(24f, 8f) });
                    yield return null;
                }
                InputSystem.QueueStateEvent(Mouse.current, new MouseState());
                yield return null;
                Log("mouse look: yaw " + yaw0.ToString("F1", Inv) + " -> " + CameraRig.Yaw.ToString("F1", Inv) + ", pitch " +
                    pitch0.ToString("F1", Inv) + " -> " + CameraRig.Pitch.ToString("F1", Inv) + ", lock " + Cursor.lockState);
                Cursor.lockState = CursorLockMode.None;
            }

            yield return Tap(Key.Escape);                              // pause
            float t0 = Director.T;
            yield return new WaitForSecondsRealtime(1f);
            Log("after Esc, story advanced " + (Director.T - t0).ToString("F2", Inv) + " s in 1 s");
            yield return Tap(Key.Escape);                              // resume
            yield return new WaitForSeconds(0.5f);
            Log("after second Esc");

            yield return Tap(Key.C);                                   // manual -> cinematic
            yield return new WaitForSeconds(2f);
            Log("after second C");
            Debug.Log("[Test] input self-test done");
        }
    }
}
#endif
