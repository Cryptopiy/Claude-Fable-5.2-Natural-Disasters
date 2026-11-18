using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace MESOCYCLONE
{
    /// <summary>Headlights: two low beams and a narrow far spot that light the wet road. They come on by themselves at dusk.
    /// The fog and rain see a real low-beam cutoff, the surfaces do not: the three surface lights are kept out of the
    /// volumetric fog, and each has an invisible "volumetric twin" (same cone, lightDimmer 0) that only scatters in the
    /// fog, through a cookie that is dark above the cutoff. A cutoff on the surface lights would black out the beat-6
    /// debris (roof sheet 0.65-4 m high at 27-58 m) and the signs, which are not retroreflective.</summary>
    [DefaultExecutionOrder(-150)]
    public sealed class TruckLights : MonoBehaviour
    {
        // The lamps' downward pitch (Euler x). The cookies are built for exactly these pitches, and WeatherFx's
        // headDir (rain cone) uses the low-beam one, so change them together.
        const float LowPitch = 1.5f, FarPitch = 0.55f;

        // Low-beam cutoff defaults (head.cutoff, head.cutoffSoft, head.stray and the US 15-degree kick-up on the right).
        // VfxGenWeather bakes the same numbers into the rain streaks' RainCone, so the rain and the fog end at one line.
        // A tune.txt override moves only the fog until the new default is baked here and VfxGenWeather is re-run.
        public const float CutoffDeg = -0.6f;        // cutoff elevation left of the kick-up, degrees (- = below horizontal)
        public const float CutoffSoftDeg = 0.3f;     // half width of the soft edge, degrees
        public const float Stray = 0.05f;            // what is left above the cutoff (lens and reflector stray light)
        public const float KickStartDeg = 1.5f;      // the kick-up starts this far right of the beam axis
        public const float KickSlope = 0.2679f;      // tan 15 degrees
        public const float KickMaxDeg = 2.6f;        // capped at cutoff + 2.6 = +2.0 degrees

        Light left, right, far;
        Light volL, volR, volFar;
        float farCandela;
        float baseCandela;
        public static float Level;      // 0..1

        public static void Build(Transform truck)
        {
            var go = new GameObject("Headlights");
            go.transform.SetParent(truck, false);
            var tl = go.AddComponent<TruckLights>();
            tl.left = tl.Make("HeadlightL", new Vector3(-0.78f, 0.98f, 2.72f), -1.2f);
            tl.right = tl.Make("HeadlightR", new Vector3(0.78f, 0.98f, 2.72f), 1.2f);
            tl.far = tl.MakeFar();
            // Two 256 cookies fill half of the 512 cookie atlas (ProjectSetup); a 512 cookie would not fit beside them.
            var lowCk = BuildCookie(58f, LowPitch);
            var farCk = BuildCookie(15f, FarPitch);
            tl.volL = tl.MakeVolTwin(tl.left, lowCk, Tune.F("head.volumetric", 3f));
            tl.volR = tl.MakeVolTwin(tl.right, lowCk, Tune.F("head.volumetric", 3f));
            tl.volFar = tl.MakeVolTwin(tl.far, farCk, Tune.F("head.farVolumetric", 0.5f));
        }

        Light Make(string name, Vector3 pos, float yaw)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(LowPitch, yaw, 0f);   // low beam: the hot spot lands about 40 m ahead
            HDAdditionalLightData hd = go.AddHDLight(LightType.Spot);
            Light l = go.GetComponent<Light>();
            l.spotAngle = 58f; l.innerSpotAngle = 16f; l.range = 260f;
            l.enableSpotReflector = true;
            l.lightUnit = LightUnit.Lumen;
            baseCandela = LightUnitUtils.ConvertIntensity(l, Tune.F("head.lumen", 30000f), LightUnit.Lumen, LightUnit.Candela);
            l.intensity = 0f;
            l.color = new Color(1f, 0.93f, 0.82f);
            hd.affectsVolumetric = false;       // surface only: the fog is lit by the cookied twin (MakeVolTwin)
            hd.EnableShadows(false);
            hd.fadeDistance = 400f; hd.volumetricFadeDistance = 300f;
            return l;
        }

        Light MakeFar()
        {
            var go = new GameObject("HeadlightFar");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 1.0f, 2.74f);
            go.transform.localRotation = Quaternion.Euler(FarPitch, 0f, 0f);
            HDAdditionalLightData hd = go.AddHDLight(LightType.Spot);
            Light l = go.GetComponent<Light>();
            l.spotAngle = 15f; l.innerSpotAngle = 5f; l.range = 420f;
            l.enableSpotReflector = true;
            l.lightUnit = LightUnit.Lumen;
            farCandela = LightUnitUtils.ConvertIntensity(l, Tune.F("head.farLumen", 9000f), LightUnit.Lumen, LightUnit.Candela);
            l.intensity = 0f;
            l.color = new Color(1f, 0.94f, 0.84f);
            hd.affectsVolumetric = false;       // surface only, as the low beams
            hd.EnableShadows(false);
            hd.fadeDistance = 700f; hd.volumetricFadeDistance = 500f;
            return l;
        }

        /// <summary>The fog-only copy of a headlight: same place, cone, range and colour, but lightDimmer 0 and no
        /// diffuse or specular, so it lights nothing solid. HDRP keeps such a light in the volumetric list (it still
        /// contributes through volumetricDimmer) and the surface shading skips it. The cookie puts the cutoff on it.</summary>
        Light MakeVolTwin(Light src, Texture2D cookie, float volDimmer)
        {
            var go = new GameObject(src.name + "Vol");
            go.transform.SetParent(src.transform.parent, false);
            go.transform.localPosition = src.transform.localPosition;
            go.transform.localRotation = src.transform.localRotation;
            HDAdditionalLightData hd = go.AddHDLight(LightType.Spot);
            Light l = go.GetComponent<Light>();
            l.spotAngle = src.spotAngle; l.innerSpotAngle = src.innerSpotAngle; l.range = src.range;
            l.enableSpotReflector = true;
            l.lightUnit = src.lightUnit;
            l.intensity = 0f;                   // follows the source in Update (candela, as the source)
            l.color = src.color;
            hd.lightDimmer = 0f;
            hd.affectDiffuse = false; hd.affectSpecular = false;
            hd.affectsVolumetric = true;
            hd.volumetricDimmer = volDimmer;
            hd.EnableShadows(false);
            HDAdditionalLightData srcHd = src.GetComponent<HDAdditionalLightData>();
            hd.fadeDistance = srcHd.fadeDistance; hd.volumetricFadeDistance = srcHd.volumetricFadeDistance;
            hd.SetCookie(cookie);
            return l;
        }

        /// <summary>A low-beam cutoff mask for a spot of this angle and downward pitch: 1 below the cutoff (the fog under
        /// the beam looks as before), head.stray above it, with the US 15-degree kick-up to the right. HDRP maps a cone
        /// spot's cookie over +-tan(spotAngle/2) in the light's own x (right) and y (up), so each texel is a direction
        /// through the lamp; turning it by the lamp's pitch gives its elevation in the truck frame (the lamp's yaw does
        /// not change elevation). RGBA with r = g = b: an R8 cookie would tint the light red, because the cookie atlas
        /// blit copies channels as they are. Linear, so the mask is not gamma-bent.</summary>
        static Texture2D BuildCookie(float spotAngle, float pitchDeg)
        {
            const int N = 256;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false, true)
            {
                name = "HeadCutoff" + Mathf.RoundToInt(spotAngle), wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
            };
            float tanH = Mathf.Tan(spotAngle * 0.5f * Mathf.Deg2Rad);
            Quaternion q = Quaternion.Euler(pitchDeg, 0f, 0f);
            float cut = Tune.F("head.cutoff", CutoffDeg);
            float soft = Mathf.Max(Tune.F("head.cutoffSoft", CutoffSoftDeg), 0.01f);
            float stray = Mathf.Clamp01(Tune.F("head.stray", Stray));
            // Texel row 0 is the bottom of the cone (v up, as HDRP projects it). If a capture shows the fog lit ABOVE
            // the horizon and dark below, the mask is upside down: head.cookieFlip=1 checks that without a rebuild.
            bool flip = Tune.F("head.cookieFlip", 0f) > 0.5f;
            var px = new Color32[N * N];
            for (int j = 0; j < N; j++)
            {
                float y = ((j + 0.5f) / N * 2f - 1f) * tanH;
                int row = flip ? N - 1 - j : j;
                for (int i = 0; i < N; i++)
                {
                    float x = ((i + 0.5f) / N * 2f - 1f) * tanH;
                    Vector3 d = q * new Vector3(x, y, 1f).normalized;
                    float el = Mathf.Asin(Mathf.Clamp(d.y, -1f, 1f)) * Mathf.Rad2Deg;
                    float az = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;                // + = right of the beam axis
                    float elCut = cut + Mathf.Clamp((az - KickStartDeg) * KickSlope, 0f, KickMaxDeg);
                    float above = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(elCut - soft, elCut + soft, el));
                    float v = Mathf.Lerp(1f, stray, above);
                    byte b = (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);
                    px[row * N + i] = new Color32(b, b, b, 255);
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        void Update()
        {
            if (!Director.Running) return;
            Level = Director.S.headlights;
            float i = baseCandela * Level;
            left.intensity = i; right.intensity = i;
            far.intensity = farCandela * Level;
            // the fog twins carry the same candela; their volumetricDimmer scales the scattering as before
            volL.intensity = i; volR.intensity = i;
            volFar.intensity = far.intensity;
        }
    }

    /// <summary>Hook for systems that are added phase by phase (storm extras, cab, world props, weather, audio).</summary>
    public static partial class Extras
    {
        public static void Build(Bootstrap boot)
        {
            BuildCab(boot);
            BuildWorld(boot);
            BuildWeather(boot);
            BuildAudio(boot);
        }

        static partial void BuildCab(Bootstrap boot);
        static partial void BuildWorld(Bootstrap boot);
        static partial void BuildWeather(Bootstrap boot);
        static partial void BuildAudio(Bootstrap boot);
    }
}
