using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MESOCYCLONE.EditorTools
{
    /// <summary>The downloaded photo maps (Poly Haven, CC0; see docs/CREDITS.md) under Assets/_Game/Textures/External,
    /// one folder per asset id: &lt;id&gt;_diff_&lt;res&gt;.jpg (or _albedo_), &lt;id&gt;_nor_gl_&lt;res&gt;.jpg, &lt;id&gt;_arm_&lt;res&gt;.jpg
    /// (R ambient occlusion, G roughness, B metal).
    /// Only the normal maps are used directly by materials: they are imported as normal maps (OpenGL convention, which
    /// is Unity's; BC5, mipmaps, anisotropic). The colour and ARM maps are only read raw (file bytes) by the texture
    /// painters, which bake normalised albedo, HDRP mask and detail maps into Assets/_Game/Generated/Textures, so those
    /// imports are kept tiny and never reach the build. Batch-safe; settings are written only when they differ.</summary>
    public static class ExternalTextures
    {
        public const string Root = "Assets/_Game/Textures/External";

        /// <summary>Normal maps of surfaces seen close and large keep 2048; the rest are imported at 1024 (fields start
        /// 16 m away, the cab sets tile at 0.15-0.6 m: 1k is already finer than the screen there).</summary>
        static readonly HashSet<string> Close = new HashSet<string> { "asphalt_02", "grass_ground", "concrete_floor_worn_001" };
        /// <summary>Seen at grazing angles: maximum anisotropic filtering.</summary>
        static readonly HashSet<string> Ground = new HashSet<string>
        { "asphalt_02", "grass_ground", "concrete_floor_worn_001", "gravel_road", "withered_grass", "sparse_grass", "farm_soil", "dry_mud_field_001" };

        // ------------------------------------------------------------------ import settings
        public static void Configure()
        {
            if (!AssetDatabase.IsValidFolder(Root))
            {
                Debug.LogWarning("[ExternalTextures] " + Root + " is missing: the photo materials fall back to neutral maps");
                return;
            }
            int changed = 0, total = 0;
            var paths = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Root })) paths.Add(AssetDatabase.GUIDToAssetPath(guid));
            paths.Sort(System.StringComparer.Ordinal);
            AssetDatabase.StartAssetEditing();                                               // one batched re-import
            try
            {
                foreach (string path in paths)
                {
                    var ti = AssetImporter.GetAtPath(path) as TextureImporter;
                    if (ti == null) continue;
                    total++;
                    if (!Apply(ti, path)) continue;
                    ti.SaveAndReimport();
                    changed++;
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            Debug.Log("[ExternalTextures] " + total + " photo maps, " + changed + " re-imported");
        }

        static bool Apply(TextureImporter ti, string path)
        {
            string file = Path.GetFileNameWithoutExtension(path);
            string id = Path.GetFileName(Path.GetDirectoryName(path));
            bool normal = file.Contains("_nor_gl_");
            bool colour = file.Contains("_diff_") || file.Contains("_albedo_");
            bool dirty = false;
            TextureImporterType type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            int max = normal ? (Close.Contains(id) ? 2048 : 1024) : 256;
            int aniso = normal ? (Ground.Contains(id) ? 16 : 8) : 1;
            TextureImporterCompression comp = normal ? TextureImporterCompression.CompressedHQ : TextureImporterCompression.Compressed;
            if (ti.textureType != type) { ti.textureType = type; dirty = true; }
            if (!normal && ti.sRGBTexture != colour) { ti.sRGBTexture = colour; dirty = true; }
            if (ti.maxTextureSize != max) { ti.maxTextureSize = max; dirty = true; }
            if (ti.textureCompression != comp) { ti.textureCompression = comp; dirty = true; }
            if (ti.anisoLevel != aniso) { ti.anisoLevel = aniso; dirty = true; }
            if (ti.mipmapEnabled != normal) { ti.mipmapEnabled = normal; dirty = true; }
            if (ti.wrapMode != TextureWrapMode.Repeat) { ti.wrapMode = TextureWrapMode.Repeat; dirty = true; }
            if (ti.filterMode != FilterMode.Trilinear) { ti.filterMode = FilterMode.Trilinear; dirty = true; }
            if (ti.npotScale != TextureImporterNPOTScale.ToNearest) { ti.npotScale = TextureImporterNPOTScale.ToNearest; dirty = true; }
            if (ti.isReadable) { ti.isReadable = false; dirty = true; }
            if (normal && ti.convertToNormalmap) { ti.convertToNormalmap = false; dirty = true; }
            if (normal && ti.flipGreenChannel) { ti.flipGreenChannel = false; dirty = true; }    // nor_gl is already Y-up
            return dirty;
        }

        // ------------------------------------------------------------------ lookups
        static string FindFile(string id, string map)
        {
            string folder = Path.Combine(Root, id);
            if (!Directory.Exists(folder)) return null;
            string[] files = Directory.GetFiles(folder, "*.jpg");
            System.Array.Sort(files, System.StringComparer.Ordinal);
            string[] keys = map == "diff" ? new[] { "_diff_", "_albedo_" } : new[] { "_" + map + "_" };
            foreach (string k in keys)
                foreach (string f in files)
                    if (Path.GetFileName(f).Contains(k)) return f.Replace('\\', '/');
            return null;
        }

        /// <summary>The imported normal map of a set ("ext:&lt;id&gt;" in a MatDef), or null when it was not downloaded.</summary>
        public static Texture2D Normal(string id)
        {
            string f = FindFile(id, "nor_gl");
            Texture2D t = f != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(f) : null;
            if (t == null) Debug.LogWarning("[ExternalTextures] no normal map for '" + id + "'");
            return t;
        }

        /// <summary>Fingerprint of the source files of a painted texture (names and sizes), for the build cache.</summary>
        public static string Key(string[] sources)
        {
            if (sources == null) return "";
            var sb = new System.Text.StringBuilder();
            foreach (string s in sources)
            {
                Split(s, out string id, out string map);
                string f = FindFile(id, map);
                sb.Append(s).Append(':').Append(f != null ? new FileInfo(f).Length.ToString() : "missing").Append(';');
            }
            return sb.ToString();
        }

        static void Split(string source, out string id, out string map)
        {
            int k = source.IndexOf('/');
            id = k > 0 ? source.Substring(0, k) : source;
            map = k > 0 ? source.Substring(k + 1) : "diff";
        }

        // a few decoded images stay in memory: the albedo, mask and detail of one set are painted one after the other
        static readonly Dictionary<string, Img> cache = new Dictionary<string, Img>();
        static readonly List<string> order = new List<string>();
        const int CacheSize = 8;

        /// <summary>Decodes a source map ("&lt;id&gt;/&lt;map&gt;") from its file bytes. A missing file gives a small
        /// neutral map (mid grey, flat normal, AO 1 / roughness 0.5), so a build never fails for a missing download.</summary>
        public static Img Load(string source)
        {
            if (cache.TryGetValue(source, out Img hit)) return hit;
            Split(source, out string id, out string map);
            string f = FindFile(id, map);
            Img img = null;
            if (f != null)
            {
                var t = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
                if (t.LoadImage(File.ReadAllBytes(f), false)) img = new Img(t.width, t.height, t.GetPixels32());
                Object.DestroyImmediate(t);
            }
            if (img == null)
            {
                Debug.LogError("[ExternalTextures] missing or unreadable source '" + source + "' (expected under " + Root + "/" + id + ")");
                Color32 c = map == "nor_gl" ? new Color32(128, 128, 255, 255) : (map == "arm" ? new Color32(255, 128, 0, 255) : new Color32(128, 128, 128, 255));
                var px = new Color32[16];
                for (int i = 0; i < px.Length; i++) px[i] = c;
                img = new Img(4, 4, px);
            }
            cache[source] = img;
            order.Add(source);
            while (order.Count > CacheSize) { cache.Remove(order[0]); order.RemoveAt(0); }
            return img;
        }

        public static void ClearCache() { cache.Clear(); order.Clear(); }
    }
}
