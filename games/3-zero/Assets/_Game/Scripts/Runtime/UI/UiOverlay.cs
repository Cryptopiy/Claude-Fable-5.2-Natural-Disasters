using UnityEngine;
using UnityEngine.UIElements;

namespace ZERO
{
    /// <summary>All text and fades of the game (UI Toolkit, built in code): title card, controls hint,
    /// the "Cinematic mode" line, the pause overlay, the white and black fades and the closing line.</summary>
    public class UiOverlay : MonoBehaviour
    {
        public static UiOverlay I;
        VisualElement root, black, white, pause;
        Label title, hint, cine, closing;
        float titleT = -1f, hintT = -1f, cineT = -1f;
        public float Black = 1f, White = 0f, Closing = 0f;
        public bool HintAllowed = true;
        public System.Action OnResume, OnQuit;

        static readonly Color Ink = new Color(0.086f, 0.114f, 0.2f, 1f);

        void Awake() { I = this; }

        public void Build(PanelSettings panel)
        {
            // the document is enabled only after its panel is set, so the root element exists at once
            var docGo = new GameObject("UIDocument"); docGo.SetActive(false);
            docGo.transform.SetParent(transform, false);
            var doc = docGo.AddComponent<UIDocument>();
            doc.panelSettings = panel;
            docGo.SetActive(true);
            root = doc.rootVisualElement;
            root.style.flexGrow = 1; root.pickingMode = PickingMode.Ignore;
            root.style.position = Position.Absolute; root.style.left = 0; root.style.top = 0; root.style.right = 0; root.style.bottom = 0;

            white = Fill(Color.white); black = Fill(Color.black);
            title = Text("Z E R O", 104, 46, Color.white, TextAnchor.MiddleCenter);
            closing = Text("T + 288.000 s", 30, 10, new Color(0.9f, 0.92f, 0.96f), TextAnchor.MiddleCenter);
            hint = Text("MOUSE  look      W / S  lean to the window      SHIFT  look closer      C  cinematic      ESC  pause", 19, 3, Ink, TextAnchor.LowerCenter);
            hint.style.paddingBottom = 54;
            cine = Text("Cinematic mode", 18, 5, Ink, TextAnchor.LowerLeft);
            cine.style.paddingBottom = 40; cine.style.paddingLeft = 56;

            pause = Fill(new Color(0.05f, 0.07f, 0.12f, 0.72f));
            pause.pickingMode = PickingMode.Position;
            pause.style.alignItems = Align.Center; pause.style.justifyContent = Justify.Center;
            var pt = new Label("PAUSED"); Style(pt, 30, 16, Color.white); pt.style.marginBottom = 42;
            pause.Add(pt);
            pause.Add(Btn("Resume", () => { if (OnResume != null) OnResume(); }));
            pause.Add(Btn("Quit", () => { if (OnQuit != null) OnQuit(); }));
            var ph = new Label("ESC  resume      Q  quit"); Style(ph, 15, 3, new Color(1, 1, 1, 0.55f)); ph.style.marginTop = 36;
            pause.Add(ph);
            pause.style.display = DisplayStyle.None;
            Apply();
        }

        VisualElement Fill(Color c)
        {
            var e = new VisualElement();
            e.style.position = Position.Absolute; e.style.left = 0; e.style.top = 0; e.style.right = 0; e.style.bottom = 0;
            e.style.backgroundColor = c; e.pickingMode = PickingMode.Ignore;
            root.Add(e); return e;
        }

        static void Style(Label l, float size, float spacing, Color c)
        {
            l.style.fontSize = size; l.style.letterSpacing = spacing; l.style.color = c;
            l.style.unityFontStyleAndWeight = FontStyle.Normal;
        }

        Label Text(string s, float size, float spacing, Color c, TextAnchor anchor)
        {
            var l = new Label(s);
            l.style.position = Position.Absolute; l.style.left = 0; l.style.top = 0; l.style.right = 0; l.style.bottom = 0;
            l.style.unityTextAlign = anchor; l.pickingMode = PickingMode.Ignore;
            Style(l, size, spacing, c); l.style.opacity = 0f;
            root.Add(l); return l;
        }

        Button Btn(string s, System.Action a)
        {
            var b = new Button(a) { text = s };
            b.style.fontSize = 24; b.style.letterSpacing = 8; b.style.width = 300; b.style.height = 58; b.style.marginTop = 10;
            b.style.color = Color.white; b.style.backgroundColor = new Color(1, 1, 1, 0.08f);
            b.style.borderTopWidth = b.style.borderBottomWidth = b.style.borderLeftWidth = b.style.borderRightWidth = 1;
            var bc = new Color(1, 1, 1, 0.5f);
            b.style.borderTopColor = bc; b.style.borderBottomColor = bc; b.style.borderLeftColor = bc; b.style.borderRightColor = bc;
            b.style.borderTopLeftRadius = b.style.borderTopRightRadius = b.style.borderBottomLeftRadius = b.style.borderBottomRightRadius = 0;
            return b;
        }

        public void ShowTitle() { titleT = 0f; }
        public void ShowHint() { if (HintAllowed) hintT = 0f; }
        public void HideHint() { if (hintT >= 0f && hintT < 5.2f) hintT = 5.2f; }
        public void ShowCinematic() { cineT = 0f; }
        public void SetPause(bool on) { pause.style.display = on ? DisplayStyle.Flex : DisplayStyle.None; }

        static float Env(float t, float fadeIn, float hold, float fadeOut)
        {
            if (t < 0f) return 0f;
            if (t < fadeIn) return t / fadeIn;
            if (t < fadeIn + hold) return 1f;
            return Mathf.Clamp01(1f - (t - fadeIn - hold) / fadeOut);
        }

        public void Tick(float dt)
        {
            if (titleT >= 0f) { titleT += dt; if (titleT > 6f) titleT = -1f; }
            if (hintT >= 0f) { hintT += dt; if (hintT > 8f) hintT = -1f; }
            if (cineT >= 0f) { cineT += dt; if (cineT > 3f) cineT = -1f; }
            Apply();
        }

        void Apply()
        {
            if (root == null) return;
            black.style.opacity = Mathf.Clamp01(Black);
            white.style.opacity = Mathf.Clamp01(White);
            title.style.opacity = Env(titleT, 0.9f, 1.9f, 1.2f);
            hint.style.opacity = Env(hintT - 0.2f, 0.8f, 4.2f, 1.0f) * 0.8f;
            cine.style.opacity = Env(cineT, 0.3f, 1.2f, 0.5f) * 0.75f;
            closing.style.opacity = Mathf.Clamp01(Closing);
        }
    }
}
