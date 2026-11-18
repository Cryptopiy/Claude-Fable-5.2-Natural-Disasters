using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace CASCADE.EditorTools
{
    public static partial class AssetFactory
    {
        /// <summary>The keeper's pane: the lamp inside makes a hot spot low and right of centre that falls to 0.45 at the
        /// edges, so the ending's subject reads as a lit room, not a flat emissive card. The pane quad's UVs are metres
        /// (ArchGeo.FaceWithOpenings with the facade normal -Fw winds it so x runs up the 1.0 m pane and y across the
        /// 0.8 m pane, left to right as seen from the path); the tiling maps them to 0..1. A material asset with the map
        /// keeps the _EMISSIVE_COLOR_MAP variant in the player (it is a shader_feature: setting it at runtime would not).</summary>
        static void KeeperGlow(Material m)
        {
            if (m == null) return;
            const int N = 32;
            const float PaneH = 1.0f, PaneW = 0.8f;          // ArchLighthouse.WinH, WinW (internal)
            string path = Gen + "/Textures/T_KeeperGlow.asset";
            var old = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            var px = new Color32[N * N];
            for (int row = 0; row < N; row++)                 // texture v: across the pane
                for (int col = 0; col < N; col++)             // texture u: up the pane
                {
                    float across = (row + 0.5f) / N, up = (col + 0.5f) / N;
                    float g = Mathf.Lerp(0.45f, 1f, Mathf.Exp(-6f * (across - 0.55f) * (across - 0.55f) - 5f * (up - 0.38f) * (up - 0.38f)));
                    byte b = (byte)Mathf.RoundToInt(g * 255f);
                    px[row * N + col] = new Color32(b, b, b, 255);
                }
            var t = new Texture2D(N, N, TextureFormat.RGBA32, false, true) { name = "T_KeeperGlow", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            t.SetPixels32(px);
            t.Apply(false, false);
            if (old != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(t, path);
            m.SetTexture("_EmissiveColorMap", t);
            m.SetTextureScale("_EmissiveColorMap", new Vector2(1f / PaneH, 1f / PaneW));
            m.SetTextureOffset("_EmissiveColorMap", Vector2.zero);
            HDMaterial.ValidateMaterial(m);                   // turns on _EMISSIVE_COLOR_MAP because the map is set
            EditorUtility.SetDirty(m);
            AddTexture(t);
        }
    }
}
