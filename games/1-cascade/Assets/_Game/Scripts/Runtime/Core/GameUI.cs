using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace CASCADE
{
    /// <summary>The only text of the game: title, fading controls hint, cinematic-mode note, pause overlay, closing line. UI Toolkit, built in code.</summary>
    public class GameUI : MonoBehaviour
    {
        UIDocument doc;
        VisualElement root, black, pause;
        Label title, hint, cine, line, resume, quit;
        float cineShownAt = -100f;
        bool hintDismissed;
        public bool PauseVisible { get; private set; }
        public float Black = 1f;       // 0..1, set by the game loop

        public void Init(PanelSettings ps)
        {
            gameObject.SetActive(false);
            doc = gameObject.AddComponent<UIDocument>();
            doc.panelSettings = ps;
            gameObject.SetActive(true);
            root = doc.rootVisualElement;
            root.style.position = Position.Absolute;
            root.style.left = 0; root.style.top = 0; root.style.right = 0; root.style.bottom = 0;
            root.pickingMode = PickingMode.Ignore;

            black = Fill(Color.black);
            root.Add(black);
            title = Text(Config.Title, 64f, 30f, 0.92f);
            Center(title, 0f);
            root.Add(title);
            hint = Text(Config.ControlsHint, 20f, 3f, 0.75f);
            Bottom(hint, 70f);
            root.Add(hint);
            cine = Text("Cinematic mode", 18f, 6f, 0.7f);
            Bottom(cine, 40f);
            root.Add(cine);
            line = Text("", 34f, 10f, 0.9f);
            Center(line, 0f);
            root.Add(line);

            pause = Fill(new Color(0f, 0f, 0f, 0.62f));
            pause.style.alignItems = Align.Center;
            pause.style.justifyContent = Justify.Center;
            resume = Text("Resume", 34f, 10f, 0.95f);
            quit = Text("Quit", 34f, 10f, 0.95f);
            resume.style.marginBottom = 34;
            pause.Add(resume);
            pause.Add(quit);
            pause.style.display = DisplayStyle.None;
            root.Add(pause);

            title.style.opacity = 0f; hint.style.opacity = 0f; cine.style.opacity = 0f; line.style.opacity = 0f;
        }

        static VisualElement Fill(Color c)
        {
            var v = new VisualElement();
            v.style.position = Position.Absolute;
            v.style.left = 0; v.style.top = 0; v.style.right = 0; v.style.bottom = 0;
            v.style.backgroundColor = c;
            v.pickingMode = PickingMode.Ignore;
            return v;
        }

        static Label Text(string s, float size, float spacing, float alpha)
        {
            var l = new Label(s);
            l.style.fontSize = size;
            l.style.letterSpacing = spacing;
            l.style.color = new Color(1f, 1f, 1f, alpha);
            l.style.unityTextAlign = TextAnchor.MiddleCenter;
            l.style.unityFontStyleAndWeight = FontStyle.Normal;
            l.style.whiteSpace = WhiteSpace.NoWrap;
            l.pickingMode = PickingMode.Ignore;
            return l;
        }

        static void Center(Label l, float dy)
        {
            l.style.position = Position.Absolute;
            l.style.left = 0; l.style.right = 0; l.style.top = 0; l.style.bottom = 0;
            l.style.translate = new Translate(0, dy);
        }

        static void Bottom(Label l, float fromBottom)
        {
            l.style.position = Position.Absolute;
            l.style.left = 0; l.style.right = 0; l.style.bottom = fromBottom;
        }

        public void NoteCinematicToggled(float realTime) { cineShownAt = realTime; }
        public void DismissHint() { hintDismissed = true; }
        public void SetLine(string s) { line.text = s; }

        /// <param name="sinceStart">real seconds since the walk began</param>
        /// <param name="sinceArrival">seconds since arrival, negative before</param>
        public void Tick(float sinceStart, float sinceArrival, float realTime, bool cinematic, bool harness)
        {
            float fadeIn = 1f - MathX.Smooth(0.2f, 2.7f, sinceStart);
            float fadeOut = sinceArrival > 0f ? MathX.Smooth(1.2f, 1.2f + Config.EndFade, sinceArrival) : 0f;
            float b = Mathf.Max(Black, Mathf.Max(fadeIn, fadeOut));
            black.style.opacity = b;
            black.style.display = b > 0.001f ? DisplayStyle.Flex : DisplayStyle.None;
            title.style.opacity = MathX.Smooth(0.3f, 1.6f, sinceStart) * (1f - MathX.Smooth(3.0f, 4.4f, sinceStart));
            bool showHint = !cinematic && !harness && !hintDismissed;
            hint.style.opacity = showHint ? MathX.Smooth(1.5f, 2.5f, sinceStart) * (1f - MathX.Smooth(6.5f, 7.5f, sinceStart)) : 0f;
            float c = realTime - cineShownAt;
            cine.style.opacity = !harness && c >= 0f && c < 2.2f ? MathX.Smooth(0f, 0.3f, c) * (1f - MathX.Smooth(1.6f, 2.2f, c)) : 0f;
            line.style.opacity = sinceArrival > 0f ? MathX.Smooth(Config.EndLineAt, Config.EndLineAt + 2.5f, sinceArrival) : 0f;
        }

        public void ShowPause(bool on)
        {
            PauseVisible = on;
            pause.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>Pause overlay input by hand (no UI event system needed). Returns 1 = resume, 2 = quit, 0 = nothing.</summary>
        public int PauseInput()
        {
            var kb = Keyboard.current; var mouse = Mouse.current;
            if (kb != null)
            {
                if (kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame) return 1;
                if (kb.qKey.wasPressedThisFrame) return 2;
            }
            if (mouse == null || root.panel == null) return 0;
            Vector2 sp = mouse.position.ReadValue();
            Vector2 pp = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(sp.x, Screen.height - sp.y));
            bool overResume = resume.worldBound.Contains(pp), overQuit = quit.worldBound.Contains(pp);
            resume.style.color = new Color(1f, 1f, 1f, overResume ? 1f : 0.7f);
            quit.style.color = new Color(1f, 1f, 1f, overQuit ? 1f : 0.7f);
            if (mouse.leftButton.wasPressedThisFrame) return overResume ? 1 : overQuit ? 2 : 0;
            return 0;
        }
    }
}
