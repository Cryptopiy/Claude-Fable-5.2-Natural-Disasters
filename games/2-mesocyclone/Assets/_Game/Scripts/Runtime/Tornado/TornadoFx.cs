using System.Collections.Generic;
using UnityEngine;
using UnityEngine.VFX;

namespace MESOCYCLONE
{
    /// <summary>The particle layers of the tornado (VFX Graph): soft condensation sheets hugging the funnel, the
    /// dust skirt, and the orbiting debris. The solid body of the funnel lives in the storm volume; these layers
    /// share its shape, its rotation clocks and its lightning. Stateless: seekable and deterministic.</summary>
    [DefaultExecutionOrder(-100)]
    public sealed class TornadoFx : MonoBehaviour
    {
        VisualEffect sheets, debris;
        static readonly int idTor0 = Shader.PropertyToID("Tor0"), idTor1 = Shader.PropertyToID("Tor1"), idTor2 = Shader.PropertyToID("Tor2"),
            idCam = Shader.PropertyToID("CamRel"), idF0P = Shader.PropertyToID("F0Pos"), idF0C = Shader.PropertyToID("F0Col"),
            idF1P = Shader.PropertyToID("F1Pos"), idF1C = Shader.PropertyToID("F1Col"), idGP = Shader.PropertyToID("GPos"),
            idGC = Shader.PropertyToID("GCol"), idAmb = Shader.PropertyToID("Amb");

        public static void Textures(List<TexDef> l)
        {
            l.Add(new TexDef("puff", 256, PaintPuff) { alpha = true });
            l.Add(new TexDef("chip", 64, PaintChip) { alpha = true, mips = true });
        }

        static Color32[] PaintPuff(int n)
        {
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                    float dx = u - 0.5f, dy = v - 0.5f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
                    float nz = CoreLibrary.Fbm(u, v, 5, 4, 301);
                    float a = Mathf.Clamp01(1f - r - (nz - 0.5f) * 0.7f);
                    a = a * a * (3f - 2f * a);
                    px[y * n + x] = new Color(1f, 1f, 1f, a * Mathf.Clamp01((1f - r) * 4f));
                }
            return px;
        }

        static Color32[] PaintChip(int n)
        {
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                    float dx = Mathf.Abs(u - 0.5f) * 2f, dy = Mathf.Abs(v - 0.5f) * 2f;
                    float edge = Mathf.Max(dx * 0.8f + dy * 0.45f, dy) + (CoreLibrary.Fbm(u, v, 4, 2, 311) - 0.5f) * 0.5f;
                    float a = Mathf.Clamp01((0.8f - edge) * 6f);
                    px[y * n + x] = new Color(1f, 1f, 1f, a);
                }
            return px;
        }

        public static TornadoFx Build(Transform parent)
        {
            var go = new GameObject("Tornado");
            go.transform.SetParent(parent, false);
            var fx = go.AddComponent<TornadoFx>();
            string suffix = Cli.Final ? "_Final" : "_Test";
            fx.sheets = Make(go.transform, "TornadoSheets" + suffix);
            fx.debris = Make(go.transform, "TornadoDebris" + suffix);
            return fx;
        }

        static VisualEffect Make(Transform parent, string asset)
        {
            VisualEffectAsset a = GameAssets.I.Vfx(asset);
            if (a == null) return null;
            var go = new GameObject(asset);
            go.transform.SetParent(parent, false);
            var v = go.AddComponent<VisualEffect>();
            v.resetSeedOnPlay = false;
            v.startSeed = 7;
            v.visualEffectAsset = a;
            return v;
        }

        void LateUpdate()
        {
            if (!Director.Running) return;
            StoryState s = Director.S;
            bool on = s.torDescend > 0.001f;
            if (sheets != null && sheets.enabled != on) sheets.enabled = on;
            if (debris != null && debris.enabled != on) debris.enabled = on;
            if (!on) return;

            Vector3 c = Director.TornadoLocal();
            transform.position = c;
            Vector3 cam = Bootstrap.Cam != null ? Bootstrap.Cam.transform.position - c : -c;
            // w carries how far the dust skirt is squashed (1 - domeScale), the same hold-down the storm shader puts on
            // the debris dome in beat 6; stored as a squash so a VFX that never receives it keeps its full dome
            Vector4 camRel = new Vector4(cam.x, cam.y, cam.z, 1f - Mathf.Clamp01(s.domeScale));
            Vector4 tor0 = new Vector4(s.torRadius, s.torDescend, s.torMorph, s.torRope);
            // the funnel's top follows the wall cloud, as in the storm shader, so the sheets stay on the funnel
            Vector4 tor1 = new Vector4(s.torDebris, FunnelShape.Top(s), SupercellPass.ShellAngle, SupercellPass.InnerAngle);
            float amb = Tune.F("tor.ambNits", 0.02f);
            Vector4 ambient = new Vector4(amb * 0.8f, amb * 0.9f, amb, 1f);
            Vector4 f0p = Rel(LightningDirector.Pos[0], c), f1p = Rel(LightningDirector.Pos[1], c), gp = Rel(LightningDirector.GroundPos, c);
            float vis = Tune.F("tor.alpha", 1f);
            Apply(sheets, tor0, tor1, new Vector4(Director.Ambient, vis, 1f, 0f), camRel, f0p, f1p, gp, ambient);
            Apply(debris, tor0, tor1, new Vector4(Director.Ambient, vis, 1f, 1f), camRel, f0p, f1p, gp, ambient);
        }

        static Vector4 Rel(Vector4 p, Vector3 c) => new Vector4(p.x - c.x, p.y - c.y, p.z - c.z, p.w);

        static void Apply(VisualEffect v, Vector4 tor0, Vector4 tor1, Vector4 tor2, Vector4 camRel, Vector4 f0p, Vector4 f1p, Vector4 gp, Vector4 amb)
        {
            if (v == null) return;
            v.SetVector4(idTor0, tor0); v.SetVector4(idTor1, tor1); v.SetVector4(idTor2, tor2);
            v.SetVector4(idCam, camRel);
            v.SetVector4(idF0P, f0p); v.SetVector4(idF0C, LightningDirector.Col[0]);
            v.SetVector4(idF1P, f1p); v.SetVector4(idF1C, LightningDirector.Col[1]);
            v.SetVector4(idGP, gp); v.SetVector4(idGC, LightningDirector.GroundCol);
            v.SetVector4(idAmb, amb);
        }
    }
}
