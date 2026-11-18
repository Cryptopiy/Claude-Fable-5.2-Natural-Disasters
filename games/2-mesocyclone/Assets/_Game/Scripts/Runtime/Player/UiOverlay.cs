using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace MESOCYCLONE
{
    /// <summary>The only text of the game: title card, fading controls hint, pause overlay, closing line, and the
    /// 2 s "Cinematic mode" notice. Also the black fade. UI Toolkit, built in code.</summary>
    [DefaultExecutionOrder(800)]
    public sealed class UiOverlay : MonoBehaviour
    {
        public static UiOverlay I;
        public const string ClosingLine = "The storm never knew you were there.";
        const string HintLine = "MOUSE  look      W / S  speed      SHIFT  faster      C  cinematic      ESC  pause";

        UIDocument doc;
        VisualElement black, pause;
        Label title, hint, closing, notice;
        bool hintDismissed;
        float loadingBlack = 1f;

        public void Init(PanelSettings settings)
        {
            I = this;
            gameObject.SetActive(false);
            doc = gameObject.AddComponent<UIDocument>();
            doc.panelSettings = settings;
            gameObject.SetActive(true);
            VisualElement root = doc.rootVisualElement;
            root.style.flexGrow = 1;
            root.pickingMode = PickingMode.Ignore;

            black = Fill(root, Color.black);
            title = Text(root, Config.ProductName, 54, 26f, 46f);
            hint = Text(root, HintLine, 15, 3f, 88f);
            // the closing line sits low (72 %), on the dark hood below the storm's base, not across the storm; the soft
            // shadow keeps it legible over the soffit's reflection in the glossy red paint
            closing = Text(root, ClosingLine, 34, 7f, 72f);
            closing.style.textShadow = new StyleTextShadow(new TextShadow { offset = new Vector2(0f, 1.5f), blurRadius = 6f, color = new Color(0f, 0f, 0f, 0.55f) });
            notice = Text(root, "Cinematic mode", 14, 5f, 92f);

            pause = Fill(root, new Color(0f, 0f, 0f, 0.72f));
            pause.pickingMode = PickingMode.Position;
            pause.style.alignItems = Align.Center; pause.style.justifyContent = Justify.Center;
            var pl = new Label("PAUSED"); Style(pl, 30, 14f); pl.style.marginBottom = 36; pause.Add(pl);
            pause.Add(Btn("Resume", () => SetPaused(false)));
            pause.Add(Btn("Quit", () => Application.Quit(0)));
            pause.style.display = DisplayStyle.None;
            Apply(1f, 0f, 0f, 0f, 0f);
        }

        static VisualElement Fill(VisualElement root, Color c)
        {
            var e = new VisualElement();
            e.style.position = Position.Absolute; e.style.left = 0; e.style.right = 0; e.style.top = 0; e.style.bottom = 0;
            e.style.backgroundColor = c; e.pickingMode = PickingMode.Ignore;
            root.Add(e);
            return e;
        }

        static void Style(Label l, int size, float spacing)
        {
            l.style.fontSize = size; l.style.letterSpacing = spacing;
            l.style.color = new Color(0.86f, 0.9f, 0.96f, 1f);
            l.style.unityTextAlign = TextAnchor.MiddleCenter;
            l.style.unityFontStyleAndWeight = FontStyle.Normal;
            l.style.whiteSpace = WhiteSpace.NoWrap;
        }

        static Label Text(VisualElement root, string s, int size, float spacing, float topPercent)
        {
            var l = new Label(s);
            Style(l, size, spacing);
            l.style.position = Position.Absolute; l.style.left = 0; l.style.right = 0;
            l.style.top = Length.Percent(topPercent);
            l.pickingMode = PickingMode.Ignore;
            root.Add(l);
            return l;
        }

        static Button Btn(string s, System.Action onClick)
        {
            var b = new Button(onClick) { text = s };
            b.style.fontSize = 20; b.style.letterSpacing = 6f; b.style.width = 260; b.style.height = 46; b.style.marginTop = 10;
            b.style.color = new Color(0.86f, 0.9f, 0.96f); b.style.backgroundColor = new Color(1f, 1f, 1f, 0.06f);
            b.style.borderTopWidth = b.style.borderBottomWidth = b.style.borderLeftWidth = b.style.borderRightWidth = 1;
            Color bc = new Color(1f, 1f, 1f, 0.25f);
            b.style.borderTopColor = b.style.borderBottomColor = b.style.borderLeftColor = b.style.borderRightColor = bc;
            return b;
        }

        public void SetLoadingBlack(float v) { loadingBlack = v; }

        public void SetPaused(bool p)
        {
            Director.Paused = p;
            Time.timeScale = p ? 0f : 1f;
            AudioListener.pause = p;
            pause.style.display = p ? DisplayStyle.Flex : DisplayStyle.None;
            LockCursor(!p);
        }

        public static void LockCursor(bool locked)
        {
            if (Cli.Harness) return;
            UnityEngine.Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            UnityEngine.Cursor.visible = !locked;
        }

        void Apply(float b, float t, float h, float c, float n)
        {
            black.style.opacity = b; title.style.opacity = t; hint.style.opacity = h; closing.style.opacity = c; notice.style.opacity = n;
        }

        void Update()
        {
            if (doc == null) return;
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.escapeKey.wasPressedThisFrame && Director.Running) SetPaused(!Director.Paused);
                if (kb.f11Key.wasPressedThisFrame)
                {
                    bool full = !Screen.fullScreen;
                    if (full) Screen.SetResolution(Screen.currentResolution.width, Screen.currentResolution.height, FullScreenMode.FullScreenWindow);
                    else Screen.SetResolution(1920, 1080, FullScreenMode.Windowed);
                }
                if (Director.Running && !hintDismissed && Director.T > 5f && !Director.Cinematic &&
                    (kb.anyKey.wasPressedThisFrame || (Mouse.current != null && Mouse.current.delta.ReadValue().sqrMagnitude > 4f)))
                    hintDismissed = true;
            }
            if (!Director.Running) { Apply(loadingBlack, 0f, 0f, 0f, 0f); return; }

            StoryState s = Director.S;
            float h = (hintDismissed || Director.Cinematic || Cli.Harness) ? 0f : s.hint;
            float since = Time.unscaledTime - CameraRig.NoticeTime;
            float n = Cli.Harness ? 0f : Mathf.Clamp01(since / 0.25f) * (1f - Mathf.Clamp01((since - 1.5f) / 0.5f));
            notice.text = Director.Cinematic ? "Cinematic mode" : "Cinematic mode off";
            Apply(Mathf.Max(s.black, loadingBlack), s.title, h, s.closing, n * 0.8f);
        }
    }
}
