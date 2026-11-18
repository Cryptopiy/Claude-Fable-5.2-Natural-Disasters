using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace CASCADE.EditorTools
{
    /// <summary>Import settings for the photo textures (Tools/fetch_textures.py) and the generated ones (Tools/gen_textures.py).</summary>
    public class CascadeTexturePostprocessor : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            string p = assetPath.Replace('\\', '/');
            bool photo = p.StartsWith(AssetFactory.PhotoDir + "/");
            bool gen = p.StartsWith(AssetFactory.GenTexDir + "/");
            if (!photo && !gen) return;
            var imp = (TextureImporter)assetImporter;
            string f = Path.GetFileNameWithoutExtension(p);
            imp.mipmapEnabled = true;
            imp.wrapMode = TextureWrapMode.Repeat;
            imp.filterMode = FilterMode.Trilinear;
            imp.anisoLevel = 8;
            imp.textureCompression = TextureImporterCompression.CompressedHQ;
            imp.isReadable = false;
            if (gen)
            {
                imp.textureType = TextureImporterType.Default;
                imp.sRGBTexture = false;
                imp.maxTextureSize = 1024;
                imp.alphaSource = TextureImporterAlphaSource.FromInput;
                imp.textureCompression = TextureImporterCompression.Uncompressed;     // slopes need their precision
                return;
            }
            imp.maxTextureSize = f.EndsWith("_disp") ? 1024 : 2048;
            if (f.EndsWith("_nor_gl")) { imp.textureType = TextureImporterType.NormalMap; return; }
            imp.textureType = TextureImporterType.Default;
            imp.sRGBTexture = f.EndsWith("_diff");
            imp.alphaSource = f.EndsWith("_mask") ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
        }
    }

    /// <summary>Physically based materials built from CC0 photo textures (Poly Haven), tinted to the palette.</summary>
    public static partial class AssetFactory
    {
        public const string PhotoDir = "Assets/_Game/Textures/PolyHaven";
        public const string GenTexDir = "Assets/_Game/Textures/Generated";
        static Dictionary<string, Vector3> albedoMean;
        public static readonly Color AshAlbedo = new Color(0.34f, 0.32f, 0.30f);      // linear: pale volcanic ash
        static readonly List<string> ashKeys = new List<string>();
        static readonly List<Color> ashTints = new List<Color>();
        static void AddAsh(string key, Color c) { int i = ashKeys.IndexOf(key); if (i >= 0) ashTints[i] = c; else { ashKeys.Add(key); ashTints.Add(c); } }

        public struct Layer
        {
            public string id; public Color target; public float hueK, tileM, normalScale, smoothMin, smoothMax, aoMin;
            public Layer(string id, Color target, float hueK, float tileM, float normalScale = 1f, float smoothMin = 0f, float smoothMax = 0.6f, float aoMin = 0.15f)
            { this.id = id; this.target = target; this.hueK = hueK; this.tileM = tileM; this.normalScale = normalScale; this.smoothMin = smoothMin; this.smoothMax = smoothMax; this.aoMin = aoMin; }
        }

        public static Texture2D Photo(string id, string map)
        {
            string path = PhotoDir + "/" + id + "/" + id + "_" + map + (map == "mask" ? ".png" : ".jpg");
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (t == null) Debug.LogError("CASCADE_FAIL missing texture " + path);
            else AddTexture(t);
            return t;
        }

        public static Texture2D GenTex(string name)
        {
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(GenTexDir + "/" + name + ".png");
            if (t == null) Debug.LogError("CASCADE_FAIL missing texture " + name);
            else AddTexture(t);
            return t;
        }

        static Vector3 Mean(string id)
        {
            if (albedoMean == null)
            {
                albedoMean = new Dictionary<string, Vector3>();
                string js = File.Exists(PhotoDir + "/stats.json") ? File.ReadAllText(PhotoDir + "/stats.json") : "";
                // tiny parser for {"id": [r, g, b], ...}
                int i = 0;
                while ((i = js.IndexOf('"', i)) >= 0)
                {
                    int j = js.IndexOf('"', i + 1); if (j < 0) break;
                    string key = js.Substring(i + 1, j - i - 1);
                    int a = js.IndexOf('[', j), b = js.IndexOf(']', a);
                    string[] v = js.Substring(a + 1, b - a - 1).Split(',');
                    albedoMean[key] = new Vector3(float.Parse(v[0], System.Globalization.CultureInfo.InvariantCulture),
                        float.Parse(v[1], System.Globalization.CultureInfo.InvariantCulture), float.Parse(v[2], System.Globalization.CultureInfo.InvariantCulture));
                    i = b + 1;
                }
            }
            Vector3 m;
            return albedoMean.TryGetValue(id, out m) ? m : new Vector3(0.3f, 0.3f, 0.3f);
        }

        static float Lum(Vector3 c) { return 0.2126f * c.x + 0.7152f * c.y + 0.0722f * c.z; }

        /// <summary>Linear tint that brings the photo's mean albedo to the target: hueK 1 = exact target colour, 0 = keep the photo's hue.</summary>
        public static Color Tint(string id, Color target, float hueK)
        {
            Vector3 mean = Mean(id);
            Vector3 t = new Vector3(target.r, target.g, target.b);
            float lm = Mathf.Max(Lum(mean), 1e-4f), lt = Lum(t);
            Vector3 hue = new Vector3(
                Mathf.Lerp(1f, (t.x / Mathf.Max(lt, 1e-4f)) / Mathf.Max(mean.x / lm, 1e-3f), hueK),
                Mathf.Lerp(1f, (t.y / Mathf.Max(lt, 1e-4f)) / Mathf.Max(mean.y / lm, 1e-3f), hueK),
                Mathf.Lerp(1f, (t.z / Mathf.Max(lt, 1e-4f)) / Mathf.Max(mean.z / lm, 1e-3f), hueK));
            float s = lt / lm;
            return new Color(Mathf.Min(hue.x * s, 4f), Mathf.Min(hue.y * s, 4f), Mathf.Min(hue.z * s, 4f), 1f);
        }

        /// <summary>HDRP/Lit with a photo texture set, world-space triplanar (or UV0 in metres when triplanar is false).</summary>
        public static Material PbrLit(string name, Layer l, bool triplanar = true)
        {
            var m = Lit(name, Color.white, 0.5f);
            m.SetColor("_BaseColor", Tint(l.id, l.target, l.hueK).gamma);
            AddAsh(name + "/_BaseColor", Tint(l.id, AshAlbedo, 0.85f).gamma);
            m.SetTexture("_BaseColorMap", Photo(l.id, "diff"));
            m.SetTexture("_NormalMap", Photo(l.id, "nor_gl"));
            m.SetFloat("_NormalScale", l.normalScale);
            m.SetTexture("_MaskMap", Photo(l.id, "mask"));
            m.SetFloat("_MetallicRemapMin", 0f); m.SetFloat("_MetallicRemapMax", 0f);
            m.SetFloat("_SmoothnessRemapMin", l.smoothMin); m.SetFloat("_SmoothnessRemapMax", l.smoothMax);
            m.SetFloat("_AORemapMin", l.aoMin); m.SetFloat("_AORemapMax", 1f);
            if (triplanar)
            {
                m.SetFloat("_UVBase", 5f);
                m.SetFloat("_TexWorldScale", 1f / l.tileM);
            }
            else
            {
                m.SetFloat("_UVBase", 0f);
                m.SetTextureScale("_BaseColorMap", new Vector2(1f / l.tileM, 1f / l.tileM));
            }
            HDMaterial.ValidateMaterial(m);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>HDRP/LayeredLit: up to four photo layers, blended by vertex colour (R layer 1, G layer 2, B layer 3) and texture height.</summary>
        public static Material Layered(string name, Layer[] ls, float heightTransition)
        {
            string path = Gen + "/Materials/" + name + ".mat";
            var sh = Shader.Find("HDRP/LayeredLit");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
            else if (m.shader != sh) m.shader = sh;
            m.SetFloat("_LayerCount", ls.Length);
            m.SetFloat("_VertexColorMode", 1f);
            m.SetFloat("_UseHeightBasedBlend", 1f);
            m.SetFloat("_HeightTransition", heightTransition);
            m.SetFloat("_UseMainLayerInfluence", 0f);
            for (int i = 0; i < ls.Length; i++)
            {
                Layer l = ls[i];
                string k = i.ToString();
                m.SetColor("_BaseColor" + k, Tint(l.id, l.target, l.hueK).gamma);
                AddAsh(name + "/_BaseColor" + k, Tint(l.id, AshAlbedo, 0.85f).gamma);
                m.SetTexture("_BaseColorMap" + k, Photo(l.id, "diff"));
                m.SetTexture("_NormalMap" + k, Photo(l.id, "nor_gl"));
                m.SetFloat("_NormalScale" + k, l.normalScale);
                m.SetTexture("_MaskMap" + k, Photo(l.id, "mask"));
                m.SetTexture("_HeightMap" + k, Photo(l.id, "disp"));
                m.SetFloat("_HeightMin" + k, -1f); m.SetFloat("_HeightMax" + k, 1f);
                m.SetFloat("_MetallicRemapMin" + k, 0f); m.SetFloat("_MetallicRemapMax" + k, 0f);
                m.SetFloat("_SmoothnessRemapMin" + k, l.smoothMin); m.SetFloat("_SmoothnessRemapMax" + k, l.smoothMax);
                m.SetFloat("_AORemapMin" + k, l.aoMin); m.SetFloat("_AORemapMax" + k, 1f);
                m.SetFloat("_UVBase" + k, 5f);
                m.SetFloat("_TexWorldScale" + k, 1f / l.tileM);
            }
            m.enableInstancing = true;
            HDMaterial.ValidateMaterial(m);
            EditorUtility.SetDirty(m);
            mats.Add(m);
            return m;
        }
    }
}
