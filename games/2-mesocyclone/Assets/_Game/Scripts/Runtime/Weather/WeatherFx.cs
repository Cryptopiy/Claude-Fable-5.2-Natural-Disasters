using System.Collections.Generic;
using UnityEngine;
using UnityEngine.VFX;

namespace MESOCYCLONE
{
    /// <summary>Rain streaks and splashes around the truck (VFX Graph, stateless, truck-local space). The rain comes at
    /// the windscreen because the truck drives into it; drops land on the road in the headlights and on the hood as
    /// tiny crowns; in heavy rain thin sheets of water run back over the hood; every hail stone of HailField throws a
    /// water crown (or an ice spray on the hood, an ice-dust puff for big stones) at its strike. All lit by the
    /// headlight cones, the lightning and what is left of the daylight. The hail stones themselves are 3D meshes
    /// (HailField); the old sprite hail is kept as an asset and only spawned with the tune "hail.sprites=1".</summary>
    [DefaultExecutionOrder(-90)]
    public sealed class WeatherFx : MonoBehaviour
    {
        VisualEffect rain, hail, splash;
        static readonly int idR0 = Shader.PropertyToID("R0"), idR1 = Shader.PropertyToID("R1"), idR2 = Shader.PropertyToID("R2"),
            idH0 = Shader.PropertyToID("H0"), idH1 = Shader.PropertyToID("H1"), idCam = Shader.PropertyToID("CamPos"),
            idHL = Shader.PropertyToID("HeadL"), idHR = Shader.PropertyToID("HeadR"), idHD = Shader.PropertyToID("HeadDir"),
            idHC = Shader.PropertyToID("HeadCol"), idFlash = Shader.PropertyToID("Flash"), idAmb = Shader.PropertyToID("Amb"),
            idS0 = Shader.PropertyToID("S0"), idS1 = Shader.PropertyToID("S1"), idS2 = Shader.PropertyToID("S2");
        static readonly Vector3 HeadPosL = new Vector3(-0.78f, 0.98f, 2.72f), HeadPosR = new Vector3(0.78f, 0.98f, 2.72f);
        float splashAlpha, splashSheets, splashSize;

        public static void Textures(List<TexDef> l)
        {
            l.Add(new TexDef("streak", 64, PaintStreak) { alpha = true, clamp = true });
            l.Add(new TexDef("stone", 64, PaintStone) { alpha = true, clamp = true });
        }

        static Color32[] PaintStreak(int n)
        {
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n - 0.5f, v = (y + 0.5f) / n;
                    float across = Mathf.Exp(-u * u * 14f);
                    float along = Mathf.Sin(v * Mathf.PI);
                    float a = across * along * along;
                    px[y * n + x] = new Color(a, a, a, a);
                }
            return px;
        }

        static Color32[] PaintStone(int n)
        {
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = ((x + 0.5f) / n - 0.5f) * 2f, v = ((y + 0.5f) / n - 0.5f) * 2f;
                    float r2 = u * u + v * v;
                    float a = Mathf.Clamp01((1f - Mathf.Sqrt(r2)) * 8f);
                    float z = Mathf.Sqrt(Mathf.Max(0f, 1f - r2));
                    float shade = 0.55f + 0.45f * Mathf.Clamp01(z * 0.6f + v * 0.5f);          // lit from above, milky
                    px[y * n + x] = new Color(shade, shade, shade, a);
                }
            return px;
        }

        public static WeatherFx Build(Transform truck)
        {
            var go = new GameObject("WeatherFx");
            go.transform.SetParent(truck, false);
            var fx = go.AddComponent<WeatherFx>();
            string suffix = Cli.Final ? "_Final" : "_Test";
            fx.rain = Make(go.transform, "Rain" + suffix);
            if (Tune.F("hail.sprites", 0f) > 0.5f) fx.hail = Make(go.transform, "Hail" + suffix);
            fx.splash = Make(go.transform, "Splash" + suffix);
            fx.splashAlpha = Tune.F("splash.alpha", 1f);
            fx.splashSheets = Tune.F("splash.sheets", 1f);
            fx.splashSize = Tune.F("splash.size", 1f);
            return fx;
        }

        static VisualEffect Make(Transform parent, string asset)
        {
            VisualEffectAsset a = GameAssets.I.Vfx(asset);
            if (a == null) return null;
            var go = new GameObject(asset);
            go.transform.SetParent(parent, false);
            var v = go.AddComponent<VisualEffect>();
            v.resetSeedOnPlay = false; v.startSeed = 11;
            v.visualEffectAsset = a;
            return v;
        }

        void LateUpdate()
        {
            if (!Director.Running) return;
            StoryState s = Director.S;
            bool rainOn = s.rain > 0.01f, hailOn = s.hail > 0.005f, splashOn = rainOn || hailOn;
            if (rain != null && rain.enabled != rainOn) rain.enabled = rainOn;
            if (hail != null && hail.enabled != hailOn) hail.enabled = hailOn;
            if (splash != null && splash.enabled != splashOn) splash.enabled = splashOn;
            if (!splashOn) return;

            Transform truck = transform.parent;
            // wind in the truck's frame; the truck's own speed adds a head wind
            float wd = s.windDir * Mathf.Deg2Rad;
            Vector3 windWorld = new Vector3(Mathf.Sin(wd), 0f, Mathf.Cos(wd)) * s.wind;
            Vector3 windLocal = truck.InverseTransformDirection(windWorld);
            Vector3 camLocal = Bootstrap.Cam != null ? truck.InverseTransformPoint(Bootstrap.Cam.transform.position) : CabDims.Eye;
            Vector3 headDir = Quaternion.Euler(1.5f, 0f, 0f) * Vector3.forward;
            float headNits = Tune.F("rain.headNits", 240f) * TruckLights.Level;
            Vector4 headCol = new Vector4(headNits, headNits * 0.93f, headNits * 0.8f, 0f);
            Color fc = LightningDirector.LevelColor * (LightningDirector.Level * Tune.F("rain.flashNits", 320f));
            Vector4 flash = new Vector4(fc.r, fc.g, fc.b, 0f);
            float day = Mathf.Pow(10f, Mathf.Min(0f, s.sunElev) / 2.6f) * Tune.F("rain.dayNits", 40f) + 0.004f;
            float spill = Tune.F("rain.spillNits", 2.4f) * TruckLights.Level;          // light bounced off the lit road
            Vector4 amb = new Vector4(day * 0.8f + spill, day * 0.9f + spill * 0.95f, day + spill * 0.85f, 0f);

            if (rainOn && rain != null)
            {
                Vector3 vel = windLocal * 0.85f + new Vector3(0f, -9f, -Director.Speed);
                rain.SetVector4(idR0, new Vector4(vel.x, vel.y, vel.z, Director.Ambient));
                rain.SetVector4(idR1, new Vector4(Mathf.Clamp01(s.rain * 1.15f), 0.022f, Tune.F("rain.alpha", 1.0f), 0f));
                rain.SetVector4(idR2, new Vector4(44f, 14f, 64f, 0f));
                Common(rain, camLocal, headDir, headCol, flash, amb);
            }
            if (hailOn && hail != null)
            {
                hail.SetVector4(idH0, new Vector4(Director.Ambient, s.hail, s.hailSize, Director.Speed));
                hail.SetVector4(idH1, new Vector4(windLocal.x * 0.5f, 0f, windLocal.z * 0.3f, 0f));
                Common(hail, camLocal, headDir, headCol, flash, amb * 1.5f);
            }
            if (splash != null)
            {
                // S0: clock, rain rate, hail rate (same scale as HailField), truck speed
                // S1: hail size, the hail's wind (H1.x, H1.z), road wetness
                // S2: alpha, hood sheets amount (heavy rain only), droplet size scale, head wind over the hood (m/s)
                float sheets = Mathf.Clamp01((s.rain - 0.45f) / 0.4f) * s.wetness * splashSheets;
                splash.SetVector4(idS0, new Vector4(Director.Ambient, rainOn ? Mathf.Clamp01(s.rain * 1.1f) : 0f, hailOn ? s.hail * HailField.Density : 0f, Director.Speed));
                splash.SetVector4(idS1, new Vector4(s.hailSize, windLocal.x * 0.5f, windLocal.z * 0.3f, s.wetness));
                splash.SetVector4(idS2, new Vector4(splashAlpha, sheets, splashSize, 0.3f + 0.04f * Director.Speed));
                Common(splash, camLocal, headDir, headCol, flash, amb);
            }
        }

        static void Common(VisualEffect v, Vector3 cam, Vector3 headDir, Vector4 headCol, Vector4 flash, Vector4 amb)
        {
            v.SetVector4(idCam, new Vector4(cam.x, cam.y, cam.z, 0f));
            v.SetVector4(idHL, new Vector4(HeadPosL.x, HeadPosL.y, HeadPosL.z, 0f));
            v.SetVector4(idHR, new Vector4(HeadPosR.x, HeadPosR.y, HeadPosR.z, 0f));
            v.SetVector4(idHD, new Vector4(headDir.x, headDir.y, headDir.z, 0f));
            v.SetVector4(idHC, headCol);
            v.SetVector4(idFlash, flash);
            v.SetVector4(idAmb, amb);
        }
    }
}
