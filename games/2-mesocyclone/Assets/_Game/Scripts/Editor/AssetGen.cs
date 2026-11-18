using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.UIElements;
using UnityEngine.VFX;

namespace MESOCYCLONE.EditorTools
{
    /// <summary>Generates every asset of the game from code: noise volume, procedural textures (compressed), the
    /// textures painted from the downloaded photo maps (see ExternalTextures; cached between builds), the material
    /// library (so every shader variant is in the build), UI panel settings and the GameAssets hub.</summary>
    public static class AssetGen
    {
        public const string Gen = "Assets/_Game/Generated";
        public const string HubPath = Gen + "/GameAssets.asset";

        public static GameAssets Run()
        {
            foreach (string d in new[] { Gen, Gen + "/Textures", Gen + "/Materials", Gen + "/UI", "Assets/_Game/VFX", "Assets/_Game/Scenes" })
                Directory.CreateDirectory(d);
            AssetDatabase.Refresh();

            var hub = AssetDatabase.LoadAssetAtPath<GameAssets>(HubPath);
            if (hub == null)
            {
                hub = ScriptableObject.CreateInstance<GameAssets>();
                AssetDatabase.CreateAsset(hub, HubPath);
            }

            // ---- noise volume ----
            string noisePath = Gen + "/Textures/Noise3D.asset";
            var noise = AssetDatabase.LoadAssetAtPath<Texture3D>(noisePath);
            if (noise == null)
            {
                noise = NoiseGen.Build3D(64);
                AssetDatabase.CreateAsset(noise, noisePath);
            }
            hub.noise3D = noise;

            // ---- photo maps: import settings first (materials use the normal maps, painters read the files) ----
            ExternalTextures.Configure();

            // ---- textures ----
            var texByName = new Dictionary<string, Texture2D>();
            var texList = new List<Texture2D>();
            Dictionary<string, string> cacheIn = ReadCache(), cacheOut = new Dictionary<string, string>();
            int painted = 0;
            foreach (TexDef def in Library.Textures())
            {
                Texture2D t = BuildTexture(def, cacheIn, cacheOut, ref painted);
                texByName[def.name] = t; texList.Add(t);
            }
            WriteCache(cacheOut);
            ExternalTextures.ClearCache();
            hub.textures = texList.ToArray();
            Debug.Log("[AssetGen] cached textures " + (cacheOut.Count - painted) + ", painted " + painted);

            // ---- materials ----
            var mats = new List<Material>();
            foreach (MatDef def in Library.Materials()) mats.Add(BuildMaterial(def, texByName));
            hub.materials = mats.ToArray();

            // ---- shaders, VFX, compute ----
            hub.shaders = LoadAll<Shader>("t:Shader", "Assets/_Game/Shaders");
            hub.vfx = LoadAll<VisualEffectAsset>("t:VisualEffectAsset", "Assets/_Game/VFX");
            hub.compute = LoadAll<ComputeShader>("t:ComputeShader", "Assets/_Game/Shaders");

            // ---- UI ----
            hub.panelSettings = BuildPanelSettings();

            // ---- pipelines ----
            hub.pipelineTest = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(ProjectSetup.TestPath);
            hub.pipelineFinal = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(ProjectSetup.FinalPath);

            EditorUtility.SetDirty(hub);
            AssetDatabase.SaveAssets();
            Debug.Log("[AssetGen] textures " + hub.textures.Length + ", materials " + hub.materials.Length + ", shaders " + hub.shaders.Length +
                      ", vfx " + hub.vfx.Length);
            return hub;
        }

        /// <summary>Refreshes the list of VFX assets of the hub (they are generated after the textures they use).</summary>
        public static void CollectVfx(GameAssets hub)
        {
            AssetDatabase.Refresh();
            hub.vfx = LoadAll<VisualEffectAsset>("t:VisualEffectAsset", "Assets/_Game/VFX");
            EditorUtility.SetDirty(hub);
            AssetDatabase.SaveAssets();
            Debug.Log("[AssetGen] vfx " + hub.vfx.Length);
        }

        static T[] LoadAll<T>(string filter, string folder) where T : Object
        {
            var list = new List<T>();
            if (!AssetDatabase.IsValidFolder(folder)) return list.ToArray();
            foreach (string guid in AssetDatabase.FindAssets(filter, new[] { folder }))
            {
                var a = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
                if (a != null && !list.Contains(a)) list.Add(a);
            }
            return list.ToArray();
        }

        // ---- build cache of the painted textures (photo composites take seconds each): name -> fingerprint ----
        const string CachePath = Gen + "/Textures/_painted_cache.txt";
        const int PainterVersion = 1;                     // bump to repaint every cached texture

        static Dictionary<string, string> ReadCache()
        {
            var d = new Dictionary<string, string>();
            if (!File.Exists(CachePath)) return d;
            foreach (string line in File.ReadAllLines(CachePath))
            {
                int k = line.IndexOf('\t');
                if (k > 0) d[line.Substring(0, k)] = line.Substring(k + 1);
            }
            return d;
        }

        static void WriteCache(Dictionary<string, string> d)
        {
            var keys = new List<string>(d.Keys);
            keys.Sort(System.StringComparer.Ordinal);
            var lines = new List<string>(keys.Count);
            foreach (string k in keys) lines.Add(k + "\t" + d[k]);
            File.WriteAllLines(CachePath, lines);
        }

        static string Fingerprint(TexDef def)
        {
            return PainterVersion + "|" + def.rev + "|" + def.size + "|" + (def.linear ? 1 : 0) + (def.normal ? 1 : 0) + (def.hq ? 1 : 0) +
                   (def.mips ? 1 : 0) + (def.clamp ? 1 : 0) + (def.clampU ? 1 : 0) + "|" + def.aniso + "|" + ExternalTextures.Key(def.sources);
        }

        static Texture2D BuildTexture(TexDef def, Dictionary<string, string> cacheIn, Dictionary<string, string> cacheOut, ref int painted)
        {
            string path = Gen + "/Textures/" + def.name + ".asset";
            bool cacheable = def.rev > 0 || def.sources != null;
            string key = cacheable ? Fingerprint(def) : null;
            if (cacheable)
            {
                var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (existing != null && cacheIn.TryGetValue(def.name, out string k) && k == key)
                {
                    cacheOut[def.name] = key;
                    return existing;
                }
                painted++;
            }
            int n = def.size;
            Color32[] px;
            if (def.sources != null && def.paintSrc != null)
            {
                var src = new Img[def.sources.Length];
                for (int i = 0; i < src.Length; i++) src[i] = ExternalTextures.Load(def.sources[i]);
                px = def.paintSrc(n, src);
            }
            else px = def.paint(n);
            if (def.heightToNormal) px = HeightToNormal(px, n, def.normalStrength);
            bool normal = def.heightToNormal || def.normal;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, def.mips, def.linear || normal)
            {
                name = def.name,
                wrapMode = def.clamp ? TextureWrapMode.Clamp : TextureWrapMode.Repeat,
                filterMode = def.point ? FilterMode.Point : FilterMode.Trilinear,
                anisoLevel = def.point ? 0 : def.aniso
            };
            if (def.clampU) { tex.wrapModeU = TextureWrapMode.Clamp; tex.wrapModeV = TextureWrapMode.Repeat; }
            tex.SetPixels32(px);
            tex.Apply(def.mips, false);
            if (!def.point && n >= 128)
            {
                // BC5 for normals (two channels, best quality), BC7 for maps seen close (albedo, masks, detail maps)
                TextureFormat f = normal ? TextureFormat.BC5 : (def.hq ? TextureFormat.BC7 : (def.alpha ? TextureFormat.DXT5 : TextureFormat.DXT1));
                EditorUtility.CompressTexture(tex, f, TextureCompressionQuality.Normal);
            }
            if (cacheable) cacheOut[def.name] = key;
            var old = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (old != null)
            {
                EditorUtility.CopySerialized(tex, old);          // keep the GUID so materials keep their reference
                Object.DestroyImmediate(tex);
                EditorUtility.SetDirty(old);
                return old;
            }
            AssetDatabase.CreateAsset(tex, path);
            return tex;
        }

        static Color32[] HeightToNormal(Color32[] h, int n, float strength)
        {
            var o = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float l = h[y * n + (x + n - 1) % n].r, r = h[y * n + (x + 1) % n].r;
                    float d = h[((y + n - 1) % n) * n + x].r, u = h[((y + 1) % n) * n + x].r;
                    var v = new Vector3((l - r) / 255f * strength, (d - u) / 255f * strength, 1f).normalized;
                    o[y * n + x] = new Color32((byte)((v.x * 0.5f + 0.5f) * 255f), (byte)((v.y * 0.5f + 0.5f) * 255f), (byte)((v.z * 0.5f + 0.5f) * 255f), 255);
                }
            return o;
        }

        static Material BuildMaterial(MatDef def, Dictionary<string, Texture2D> tex)
        {
            string path = Gen + "/Materials/" + def.name + ".mat";
            Shader sh = Shader.Find(def.kind == MatKind.Lit ? "HDRP/Lit" : "HDRP/Unlit");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(sh) { name = def.name }; AssetDatabase.CreateAsset(m, path); }
            else if (m.shader != sh) m.shader = sh;

            bool transparent = def.surface == MatSurface.Transparent || def.surface == MatSurface.Additive;
            m.SetFloat("_SurfaceType", transparent ? 1f : 0f);
            m.SetFloat("_BlendMode", def.surface == MatSurface.Additive ? 1f : 0f);
            m.SetFloat("_AlphaCutoffEnable", def.surface == MatSurface.Cutout ? 1f : 0f);
            m.SetFloat("_DoubleSidedEnable", def.doubleSided ? 1f : 0f);
            if (def.doubleSided) m.SetFloat("_DoubleSidedNormalMode", def.keepNormalBothSides ? 2f : 1f);   // 1 Mirror (HDRP default), 2 None
            m.SetFloat("_EnableFogOnTransparent", 1f);
            if (def.kind == MatKind.Lit)
            {
                Color col = def.color;
                if (def.normalizedAlbedo)
                {
                    // the base map has a mean linear albedo of 0.5: double the wanted albedo (in linear) so the mean lands on it
                    Color lin = def.color.linear;
                    col = new Color(Mathf.Min(lin.r * 2f, 1f), Mathf.Min(lin.g * 2f, 1f), Mathf.Min(lin.b * 2f, 1f), 1f).gamma;
                    col.a = def.color.a;
                }
                m.SetColor("_BaseColor", col);
                m.SetFloat("_Metallic", def.metallic);
                m.SetFloat("_Smoothness", def.smoothness);
                m.SetTexture("_BaseColorMap", Find(tex, def.baseMap));
                m.SetTextureScale("_BaseColorMap", def.tiling);
                m.SetTextureOffset("_BaseColorMap", def.offset);
                m.SetTexture("_NormalMap", Find(tex, def.normalMap));
                m.SetFloat("_NormalScale", def.normalScale);
                m.SetFloat("_NormalMapSpace", 0f);
                // base UV set (UV0 or UV1); HDRP's inspector derives these hidden values, so they are written here
                bool uv1 = def.baseUV == 1;
                m.SetFloat("_UVBase", uv1 ? 1f : 0f);
                m.SetColor("_UVMappingMask", uv1 ? new Color(0f, 1f, 0f, 0f) : new Color(1f, 0f, 0f, 0f));
                m.SetFloat("_TexWorldScale", 1f);
                m.SetFloat("_InvTilingScale", 2f / Mathf.Max(1e-4f, Mathf.Abs(def.tiling.x) + Mathf.Abs(def.tiling.y)));
                // mask map: R metal weight (metallic = remap max), G AO, B detail mask, A smoothness (remapped)
                m.SetTexture("_MaskMap", Find(tex, def.maskMap));
                m.SetFloat("_MetallicRemapMin", 0f);
                m.SetFloat("_MetallicRemapMax", def.metallic);
                m.SetFloat("_SmoothnessRemapMin", def.smoothRemap.x);
                m.SetFloat("_SmoothnessRemapMax", def.smoothRemap.y);
                m.SetFloat("_AORemapMin", def.aoRemap.x);
                m.SetFloat("_AORemapMax", def.aoRemap.y);
                // detail map with its own UV set and absolute tiling (not linked to the base tiling)
                bool d1 = def.detailUV == 1;
                m.SetTexture("_DetailMap", Find(tex, def.detailMap));
                m.SetTextureScale("_DetailMap", def.detailTiling);
                m.SetTextureOffset("_DetailMap", def.detailOffset);
                m.SetFloat("_UVDetail", d1 ? 1f : 0f);
                m.SetColor("_UVDetailsMappingMask", d1 ? new Color(0f, 1f, 0f, 0f) : new Color(1f, 0f, 0f, 0f));
                m.SetFloat("_LinkDetailsWithBase", 0f);
                m.SetFloat("_DetailAlbedoScale", def.detailAlbedo);
                m.SetFloat("_DetailNormalScale", def.detailNormal);
                m.SetFloat("_DetailSmoothnessScale", def.detailSmooth);
                // clear coat (keyword _MATERIAL_FEATURE_CLEAR_COAT is set by ValidateMaterial when > 0)
                m.SetFloat("_CoatMask", def.coatMask);
            }
            else
            {
                m.SetColor("_UnlitColor", def.color);
                m.SetTexture("_UnlitColorMap", Find(tex, def.baseMap));
                m.SetTextureScale("_UnlitColorMap", def.tiling);
            }
            Color e = def.emissive.linear * def.emissiveNits;
            m.SetColor("_EmissiveColor", new Color(e.r, e.g, e.b, 1f));
            m.SetTexture("_EmissiveColorMap", Find(tex, def.emissiveMap));
            m.SetFloat("_EmissiveExposureWeight", 1f);
            m.SetFloat("_UseEmissiveIntensity", 0f);
            m.enableInstancing = def.instancing;
            HDMaterial.ValidateMaterial(m);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Texture2D Find(Dictionary<string, Texture2D> tex, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (name.StartsWith("ext:")) return ExternalTextures.Normal(name.Substring(4));
            if (tex.TryGetValue(name, out Texture2D t)) return t;
            Debug.LogError("[AssetGen] texture '" + name + "' is not defined");
            return null;
        }

        static PanelSettings BuildPanelSettings()
        {
            string tssPath = Gen + "/UI/Runtime.tss";
            if (!File.Exists(tssPath))
            {
                File.WriteAllText(tssPath, "@import url(\"unity-theme://default\");\nVisualElement {}\n");
                AssetDatabase.ImportAsset(tssPath, ImportAssetOptions.ForceSynchronousImport);
            }
            string path = Gen + "/UI/Panel.asset";
            var ps = AssetDatabase.LoadAssetAtPath<PanelSettings>(path);
            if (ps == null) { ps = ScriptableObject.CreateInstance<PanelSettings>(); AssetDatabase.CreateAsset(ps, path); }
            ps.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(tssPath);
            ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            ps.referenceResolution = new Vector2Int(1920, 1080);
            ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            ps.match = 1f;
            ps.sortingOrder = 100;
            if (ps.themeStyleSheet == null) Debug.LogError("[AssetGen] runtime theme style sheet did not import");
            EditorUtility.SetDirty(ps);
            return ps;
        }
    }
}
