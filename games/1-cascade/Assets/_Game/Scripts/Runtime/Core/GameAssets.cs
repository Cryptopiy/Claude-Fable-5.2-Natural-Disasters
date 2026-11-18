using UnityEngine;
using UnityEngine.UIElements;

namespace CASCADE
{
    /// <summary>
    /// Registry of the generated assets (materials, textures, shaders). It is created by the editor build script and
    /// referenced from the scene, so that every shader variant the game uses is part of the player build.
    /// Look-up is by asset name ("M_Whitewash", "T_PlumeNoise" ...).
    /// </summary>
    public class GameAssets : ScriptableObject
    {
        public Material[] materials;
        public Texture[] textures;
        public Shader[] shaders;
        public PanelSettings panel;
        /// <summary>"material/property" -> the colour that property must take for the surface to look ash-covered
        /// (photo-textured materials store a tint relative to their texture, so the ash target differs per material).</summary>
        public string[] ashKeys;
        public Color[] ashTints;

        public static GameAssets I;

        public Material Mat(string name)
        {
            if (materials != null)
                for (int i = 0; i < materials.Length; i++)
                    if (materials[i] != null && materials[i].name == name) return materials[i];
            Debug.LogError("CASCADE_MISSING material " + name);
            return materials != null && materials.Length > 0 ? materials[0] : null;
        }

        public bool AshTint(string key, out Color c)
        {
            if (ashKeys != null)
                for (int i = 0; i < ashKeys.Length; i++)
                    if (ashKeys[i] == key) { c = ashTints[i]; return true; }
            c = Color.white;
            return false;
        }

        public Texture Tex(string name)
        {
            if (textures != null)
                for (int i = 0; i < textures.Length; i++)
                    if (textures[i] != null && textures[i].name == name) return textures[i];
            Debug.LogError("CASCADE_MISSING texture " + name);
            return null;
        }

        public Shader Sh(string name)
        {
            if (shaders != null)
                for (int i = 0; i < shaders.Length; i++)
                    if (shaders[i] != null && shaders[i].name == name) return shaders[i];
            Debug.LogError("CASCADE_MISSING shader " + name);
            return null;
        }
    }
}
