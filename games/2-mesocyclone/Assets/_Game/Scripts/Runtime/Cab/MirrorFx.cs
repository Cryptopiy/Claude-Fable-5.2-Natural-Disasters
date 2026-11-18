using System.Collections.Generic;
using UnityEngine;

namespace MESOCYCLONE
{
    /// <summary>The mirrors. A second HDRP camera would cost frame time and VRAM on a GPU shared by three agents, so
    /// the mirrors show two painted views of what lies behind: the sunset (sky, horizon, the road running away) as
    /// the base colour while the sun is up, and the storm deck over the last light as the emissive layer after dusk.
    /// Their brightness and colour follow the evening behind the truck, the flashes behind it (SkyBehind) and the
    /// station canopy: a dim mirror with a believable reflection, never a black slab. Also wires the cab into the
    /// bootstrap.</summary>
    [DefaultExecutionOrder(-70)]
    public sealed class MirrorFx : MonoBehaviour
    {
        Material centre, side;
        Light dash, canopy;
        float dashCandela, canopyCandela, flashGain, canopyGain;
        static readonly int idColor = Shader.PropertyToID("_UnlitColor");
        static readonly int idEmissive = Shader.PropertyToID("_EmissiveColor");
        // display-referred scale of the night painting (exposure weight 0): off by day, the last light behind the
        // truck at dusk, a dim band under the deck through the hail and the funnel, a little more in the clear air
        static readonly Curve nightLum = new Curve("mirror.night", 0, 0f, 95, 0f, 150, 0.5f, 210, 0.5f, 255, 0.22f, 590, 0.22f, 615, 0.30f, 720, 0.30f);

        public static void Textures(List<TexDef> l)
        {
            l.Add(new TexDef("mirror_view", 256, PaintView) { clamp = true });
            // BC7: the dark gradients must not band when a flash scales them up
            l.Add(new TexDef("mirror_night", 256, PaintNight) { clamp = true, hq = true });
        }

        public static void Materials(List<MatDef> l)
        {
            // Unlit on purpose: a Lit, metallic glass would pick up the sky probe and SSR, which the cab avoids
            l.Add(new MatDef("mirror") { kind = MatKind.Unlit, color = Color.black, baseMap = "mirror_view", emissiveMap = "mirror_night", instancing = false });
        }

        // what is behind the truck: the western sky with the low sun to one side, a flat horizon, the road running away
        static Color32[] PaintView(int n)
        {
            var px = new Color32[n * n];
            Color skyLow = new Color(1.0f, 0.62f, 0.30f), skyHigh = new Color(0.50f, 0.42f, 0.55f), land = new Color(0.10f, 0.085f, 0.06f);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                    Color c;
                    if (v > 0.5f)
                    {
                        float h = (v - 0.5f) * 2f;
                        c = Color.Lerp(skyLow, skyHigh, Mathf.Pow(h, 0.6f));
                        float du = u - 0.26f, dv = (v - 0.53f) * 1.6f;
                        float sun = Mathf.Exp(-(du * du + dv * dv) * 26f);
                        c += new Color(1.0f, 0.75f, 0.45f) * sun * 1.2f;
                        c *= 0.92f + 0.16f * CoreLibrary.Fbm(u * 2f, v, 4, 3, 901);
                    }
                    else
                    {
                        float d = (0.5f - v) * 2f;                           // 0 at the horizon, 1 at the bottom edge
                        c = land * (0.7f + 0.6f * CoreLibrary.Fbm(u, v, 12, 3, 903));
                        float half = 0.02f + d * 0.42f;                       // the road widens toward the viewer
                        float ax = Mathf.Abs(u - 0.5f);
                        if (ax < half) c = Color.Lerp(c, new Color(0.055f, 0.055f, 0.06f), 0.9f);
                        if (Mathf.Abs(ax - half * 0.92f) < 0.004f + d * 0.006f) c = Color.Lerp(c, new Color(0.5f, 0.5f, 0.48f), 0.8f);
                        if (ax < 0.003f + d * 0.006f && Mathf.Repeat(Mathf.Log(d + 0.02f) * 3f, 1f) < 0.45f) c = Color.Lerp(c, new Color(0.55f, 0.42f, 0.1f), 0.8f);
                        c = Color.Lerp(c, skyLow * 0.5f, Mathf.Exp(-d * 9f) * 0.5f);     // haze at the horizon
                    }
                    c.a = 1f;
                    px[y * n + x] = c;
                }
            return px;
        }

        // the same view after dusk: the storm deck behind the truck, lit from beneath by the last light at the horizon,
        // the wet road holding that light and the faint red wash of the tail lamps; same layout as PaintView
        static Color32[] PaintNight(int n)
        {
            var px = new Color32[n * n];
            Color horizon = new Color(0.42f, 0.46f, 0.55f), deck = new Color(0.20f, 0.22f, 0.27f), land = new Color(0.05f, 0.05f, 0.055f);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                    Color c;
                    if (v > 0.5f)
                    {
                        float h = (v - 0.5f) * 2f;
                        float k = TexPaint.Ss(0f, 0.55f, h);
                        c = Color.Lerp(horizon, deck, k);
                        c *= Mathf.Lerp(1f, 0.7f + 0.6f * CoreLibrary.Fbm(u * 3f, v * 1.2f, 5, 4, 905), k);   // the lumpy underside of the deck
                        c += new Color(0.10f, 0.10f, 0.12f) * Mathf.Exp(-h * 14f);                        // the pale gap under the deck edge
                    }
                    else
                    {
                        float d = (0.5f - v) * 2f;                           // 0 at the horizon, 1 at the bottom edge
                        c = land * (0.7f + 0.6f * CoreLibrary.Fbm(u, v, 12, 3, 903));
                        float half = 0.02f + d * 0.42f;                       // the road widens toward the viewer
                        float ax = Mathf.Abs(u - 0.5f);
                        // wet asphalt holding the sky: brightest far away, where it mirrors the horizon at a grazing angle
                        if (ax < half) c = Color.Lerp(new Color(0.07f, 0.075f, 0.085f), horizon * 0.6f, Mathf.Exp(-d * 5f) * 0.8f);
                        if (Mathf.Abs(ax - half * 0.92f) < 0.004f + d * 0.006f) c = Color.Lerp(c, new Color(0.30f, 0.30f, 0.30f), 0.8f);
                        if (ax < 0.003f + d * 0.006f && Mathf.Repeat(Mathf.Log(d + 0.02f) * 3f, 1f) < 0.45f) c = Color.Lerp(c, new Color(0.30f, 0.24f, 0.08f), 0.8f);
                        // the tail lamps (on with the headlights, when this layer fades in) redden the road just behind
                        float tu = (u - 0.5f) / 0.28f;
                        float tail = Mathf.Exp(-tu * tu) * TexPaint.Ss(0.35f, 1f, d);
                        c += new Color(0.45f, 0.03f, 0.03f) * (0.6f * tail);
                        c = Color.Lerp(c, horizon * 0.7f, Mathf.Exp(-d * 9f) * 0.5f);     // haze at the horizon
                    }
                    c = new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), 1f);
                    px[y * n + x] = c;
                }
            return px;
        }

        public static void Attach(CabRig rig)
        {
            if (rig == null) return;
            var fx = rig.gameObject.AddComponent<MirrorFx>();
            Material src = GameAssets.I.Mat("mirror");
            fx.centre = new Material(src);
            fx.side = new Material(src);
            // the night painting is the emissive layer; exposure weight 0 keeps it display-referred like _UnlitColor,
            // so nightLum sets what the eye sees however far the night exposure opens up
            fx.centre.SetFloat("_EmissiveExposureWeight", 0f);
            fx.side.SetFloat("_EmissiveExposureWeight", 0f);
            if (!src.IsKeywordEnabled("_EMISSIVE_COLOR_MAP")) Debug.LogError("[MirrorFx] mirror has no night map: rebuild assets");
            fx.flashGain = Tune.F("mirror.flash", 4f);
            fx.canopyGain = Tune.F("mirror.canopy", 0.6f);
            if (rig.mirrorFace != null) rig.mirrorFace.sharedMaterial = fx.centre;
            if (rig.sideMirrorFaceL != null) rig.sideMirrorFaceL.sharedMaterial = fx.side;
            if (rig.sideMirrorFaceR != null) rig.sideMirrorFaceR.sharedMaterial = fx.side;
            var lg = new GameObject("DashGlow");
            lg.transform.SetParent(rig.transform, false);
            // just in front of the gauges and below the dash top: with its shadow on, the cluster hood and the dash top
            // hide the windscreen from it (it used to light the hail and the drops outside the glass amber)
            lg.transform.localPosition = CabDims.ClusterCentre + new Vector3(0.04f, -0.05f, -0.11f);
            UnityEngine.Rendering.HighDefinition.HDAdditionalLightData hd = UnityEngine.Rendering.HighDefinition.GameObjectExtension.AddHDLight(lg, LightType.Point);
            fx.dash = lg.GetComponent<Light>();
            // short and dim: at 130 lm and 1.9 m it put about 1000 lux on the binnacle wall (a lightbox brighter than
            // the gauges) and an amber oval on the A-pillar. 14 lm leaves the wall a soft amber surround under the
            // marks and needles, and a 0.5 m reach lights only the binnacle, the gauges and the wheel rim
            fx.dash.range = Tune.F("cab.dashRange", 0.5f);
            fx.dash.color = Palette.DashAmber;
            fx.dash.lightUnit = UnityEngine.Rendering.LightUnit.Lumen;
            fx.dashCandela = UnityEngine.Rendering.LightUnitUtils.ConvertIntensity(fx.dash, Tune.F("cab.dashLumen", 14f), UnityEngine.Rendering.LightUnit.Lumen, UnityEngine.Rendering.LightUnit.Candela);
            fx.dash.intensity = 0f;
            // shadowed (a small map): without it the instrument glow shone through the dashboard onto the hail and
            // the drops on the glass, which then read as warm lit lumps. The shadow alone did not cover the stones
            // above the binnacle lip (steep rays pass behind it: cream eggs at the top of the glass); the 0.5 m range
            // ends before them, and the glass at 0.37 m is still inside it, so the shadow stays
            hd.EnableShadows(true);
            hd.SetShadowResolutionOverride(true);
            hd.SetShadowResolution(256);
            hd.shadowNearPlane = 0.02f;
            hd.affectsVolumetric = false;
            // under the canopy its warm light comes in through the glass and fills the cab
            var cg = new GameObject("CanopyFill");
            cg.transform.SetParent(rig.transform, false);
            cg.transform.localPosition = new Vector3(0.05f, 1.74f, 0.62f);
            UnityEngine.Rendering.HighDefinition.HDAdditionalLightData ch = UnityEngine.Rendering.HighDefinition.GameObjectExtension.AddHDLight(cg, LightType.Point);
            fx.canopy = cg.GetComponent<Light>();
            fx.canopy.range = 3.2f;
            fx.canopy.color = Palette.CanopyWarm;
            fx.canopy.lightUnit = UnityEngine.Rendering.LightUnit.Lumen;
            fx.canopyCandela = UnityEngine.Rendering.LightUnitUtils.ConvertIntensity(fx.canopy, Tune.F("cab.canopyLumen", 700f), UnityEngine.Rendering.LightUnit.Lumen, UnityEngine.Rendering.LightUnit.Candela);
            fx.canopy.intensity = 0f;
            ch.EnableShadows(false);
            ch.affectsVolumetric = false;
        }

        void LateUpdate()
        {
            if (!Director.Running || centre == null) return;
            StoryState s = Director.S;
            // the evening behind the truck: bright while the sun is up, a last band of colour, then night
            float day = Mathf.Clamp01(Mathf.Pow(10f, Mathf.Min(0f, s.sunElev - 1.5f) / 3.0f));
            float warm = Mathf.InverseLerp(-4f, 3f, s.sunElev);
            float rainDim = 1f - 0.5f * s.rain;
            // day layer (base colour): the painted sunset, gone with the sun. No floor any more: a 0.006 floor read as
            // pure black after ACES, and the night layer below carries the mirror from dusk on
            Color c = Color.Lerp(new Color(0.55f, 0.6f, 0.9f), new Color(1f, 0.96f, 0.9f), warm) * (0.95f * day * rainDim);
            c.a = 1f;
            float under = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp((float)Director.GoalS - 55f, (float)Director.GoalS - 6f, (float)Director.TruckS));
            // night layer (emissive): the deck over the last light, warm while the afterglow lasts and cold at night;
            // the flashes behind the truck (SkyBehind: the vortex shadow cuts Level for the silhouette flashes ahead,
            // but not the sky behind us), and the station canopy at the end
            Color nightTint = Color.Lerp(new Color(1.0f, 0.70f, 0.50f), new Color(0.85f, 0.92f, 1.0f), Mathf.InverseLerp(-2f, -8f, s.sunElev));
            Color e = nightTint * (nightLum.Eval(s.T) * rainDim)
                    + LightningDirector.LevelColor * (flashGain * LightningDirector.SkyBehind)
                    + Palette.CanopyWarm.linear * (canopyGain * under * s.canopy);
            e.a = 1f;
            if (dash != null) dash.intensity = dashCandela * CabRig.GaugeLight;
            if (canopy != null) canopy.intensity = canopyCandela * under * s.canopy;
            WindscreenPass.CabGlow = CabRig.GaugeLight;
            centre.SetColor(idColor, c);
            centre.SetColor(idEmissive, e);
            side.SetColor(idColor, c * 0.85f);
            side.SetColor(idEmissive, e * 0.85f);
        }

        void OnDestroy()
        {
            if (centre != null) Destroy(centre);
            if (side != null) Destroy(side);
        }
    }

    public static partial class Extras
    {
        static partial void BuildCab(Bootstrap boot)
        {
            CabRig rig = CabRig.Build(Bootstrap.Cab);
            MirrorFx.Attach(rig);
        }
    }
}
