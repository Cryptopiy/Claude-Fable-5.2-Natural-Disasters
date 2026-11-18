using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using UnityEngine.VFX;

namespace MESOCYCLONE
{
    public enum MatKind { Lit, Unlit }
    public enum MatSurface { Opaque, Cutout, Transparent, Additive }

    /// <summary>Definition of one material of the library. The editor build script turns every definition into a
    /// material asset (so its shader variant is in the build); runtime code fetches it by name.</summary>
    [Serializable]
    public sealed class MatDef
    {
        public string name;
        public MatKind kind = MatKind.Lit;
        public MatSurface surface = MatSurface.Opaque;
        public Color color = Color.gray;          // sRGB base colour (alpha used by Transparent)
        public float metallic;
        public float smoothness = 0.3f;
        public Color emissive = Color.black;      // sRGB emissive colour
        public float emissiveNits;                // 0 = not emissive
        public string baseMap = "";               // name of a TexDef, "" = none
        public string normalMap = "";             // name of a TexDef (height-painted or a normal map), or "ext:<asset id>" = an imported Poly Haven normal map
        public string emissiveMap = "";
        public float normalScale = 1f;
        public Vector2 tiling = Vector2.one;      // base, normal and mask maps share it (HDRP Lit)
        public Vector2 offset = Vector2.zero;
        public bool doubleSided;
        public bool keepNormalBothSides;          // double sided with HDRP normal mode None: thin blades keep their
                                                  // (up-bent) vertex normal on the back face instead of flipping it
        public bool instancing = true;

        // ---- real-material inputs (HDRP Lit) ----
        public int baseUV;                        // 0 = UV0, 1 = UV1 (MeshBuilder writes box-projected metres into UV1)
        public bool normalizedAlbedo;             // the base map is normalised to a mean linear albedo of 0.5: color is the
                                                  // wanted mean albedo and is doubled (linear) so the palette stays in charge
        public string maskMap = "";               // HDRP mask: R metal weight, G AO, B detail mask, A smoothness
        public Vector2 smoothRemap = new Vector2(0f, 1f);   // with a mask map: smoothness = lerp(x, y, mask.a)
        public Vector2 aoRemap = new Vector2(0f, 1f);
        public string detailMap = "";             // HDRP detail: R albedo overlay (0.5 neutral), G normal Y, B smoothness overlay, A normal X
        public int detailUV;                      // 0 = UV0, 1 = UV1
        public Vector2 detailTiling = Vector2.one;   // absolute (not linked to the base tiling)
        public Vector2 detailOffset = Vector2.zero;
        public float detailAlbedo = 1f, detailNormal = 1f, detailSmooth = 1f;
        public float coatMask;                    // > 0: clear coat (car paint)

        public MatDef(string name) { this.name = name; }
        public static MatDef Lit(string name, Color color, float smoothness = 0.3f, float metallic = 0f)
            => new MatDef(name) { color = color, smoothness = smoothness, metallic = metallic };
        public static MatDef Glow(string name, Color color, float nits, MatSurface surface = MatSurface.Opaque)
            => new MatDef(name) { kind = MatKind.Unlit, surface = surface, color = Color.black, emissive = color, emissiveNits = nits };
    }

    /// <summary>Definition of one procedural texture: painted by code at edit time, saved compressed.</summary>
    public sealed class TexDef
    {
        public string name;
        public int size = 256;
        public bool linear;                       // data (height, masks) instead of colour
        public bool heightToNormal;               // the painter returns a height field in the red channel
        public float normalStrength = 2f;
        public bool mips = true;
        public bool point;                        // point filtering (swatch atlas)
        public bool alpha;                        // keep the alpha channel (DXT5 instead of DXT1)
        public bool clamp;                        // clamp instead of repeat
        public bool clampU;                       // clamp across (u) only, repeat along (v): the verge strip
        public Func<int, Color32[]> paint;        // size -> pixels (row-major, bottom row first)

        // ---- textures made from downloaded photo maps (Assets/_Game/Textures/External/<asset id>) ----
        public string[] sources;                  // "<asset id>/<map>" with map = diff, nor_gl or arm, decoded at edit time
        public Func<int, Img[], Color32[]> paintSrc;   // size, decoded sources (same order) -> pixels
        public bool normal;                       // the painter returns a tangent-space normal (xyz * 0.5 + 0.5): linear, BC5
        public bool hq;                           // BC7 instead of DXT1/DXT5 (maps seen close, masks, detail maps)
        public int rev;                           // > 0: cached between builds; bump it when the painter changes
        public int aniso = 8;

        public TexDef(string name, int size, Func<int, Color32[]> paint) { this.name = name; this.size = size; this.paint = paint; }

        public TexDef(string name, int size, string[] sources, Func<int, Img[], Color32[]> paintSrc)
        {
            this.name = name; this.size = size; this.sources = sources; this.paintSrc = paintSrc; hq = true; rev = 1;
        }
    }

    /// <summary>Everything the runtime needs that must exist as an asset in the build. Created by the editor
    /// build script (Tools/MESOCYCLONE/Rebuild All) and referenced by the Bootstrap object of the scene.</summary>
    public sealed class GameAssets : ScriptableObject
    {
        public static GameAssets I;

        public Texture3D noise3D;
        public PanelSettings panelSettings;
        public VolumeProfile profile;
        public RenderPipelineAsset pipelineTest, pipelineFinal;
        public Shader[] shaders = new Shader[0];
        public Material[] materials = new Material[0];
        public Texture2D[] textures = new Texture2D[0];
        public VisualEffectAsset[] vfx = new VisualEffectAsset[0];
        public ComputeShader[] compute = new ComputeShader[0];

        Dictionary<string, Material> matMap;
        Dictionary<string, Texture2D> texMap;

        public Material Mat(string name)
        {
            if (matMap == null)
            {
                matMap = new Dictionary<string, Material>();
                foreach (Material m in materials) if (m != null) matMap[m.name] = m;
            }
            if (matMap.TryGetValue(name, out Material r)) return r;
            Debug.LogError("[GameAssets] missing material '" + name + "'");
            return materials.Length > 0 ? materials[0] : null;
        }

        public Texture2D Tex(string name)
        {
            if (texMap == null)
            {
                texMap = new Dictionary<string, Texture2D>();
                foreach (Texture2D t in textures) if (t != null) texMap[t.name] = t;
            }
            if (texMap.TryGetValue(name, out Texture2D r)) return r;
            Debug.LogError("[GameAssets] missing texture '" + name + "'");
            return Texture2D.grayTexture;
        }

        public Shader Shader(string name)
        {
            foreach (Shader s in shaders) if (s != null && s.name == name) return s;
            Debug.LogError("[GameAssets] missing shader '" + name + "'");
            return null;
        }

        public VisualEffectAsset Vfx(string name)
        {
            foreach (VisualEffectAsset v in vfx) if (v != null && v.name == name) return v;
            Debug.LogError("[GameAssets] missing vfx '" + name + "'");
            return null;
        }
    }

    /// <summary>The library: every module registers its materials and textures here (one static method each).</summary>
    public static class Library
    {
        const float CabDarken = 0.42f;
        public static List<MatDef> Materials()
        {
            var l = new List<MatDef>();
            CoreLibrary.Materials(l);
            Land.Materials(l);
            LightningVisuals.Materials(l);
            WorldLibrary.Materials(l);
            HeroHail.Materials(l);
            WeatherLibrary.Materials(l);
            GrassLibrary.Materials(l);
            CabLibrary.Materials(l);
            MirrorFx.Materials(l);
            // the cab has no ambient occlusion from the sky probe: its matte surfaces are darkened instead
            foreach (MatDef d in l)
                if (d.name.StartsWith("cab_") && d.kind == MatKind.Lit && d.emissiveNits <= 0f && d.name != "cab_paint" && d.name != "cab_chrome")
                    d.color = new Color(d.color.r * CabDarken, d.color.g * CabDarken, d.color.b * CabDarken, d.color.a);
            return l;
        }

        public static List<TexDef> Textures()
        {
            var l = new List<TexDef>();
            CoreLibrary.Textures(l);
            Land.Textures(l);
            TornadoFx.Textures(l);
            WeatherFx.Textures(l);
            WeatherLibrary.Textures(l);
            GrassLibrary.Textures(l);
            LightningVisuals.Textures(l);
            WorldLibrary.Textures(l);
            CabLibrary.Textures(l);
            MirrorFx.Textures(l);
            // shared photo-map textures are requested by several modules: keep the first definition of each name
            var seen = new HashSet<string>();
            l.RemoveAll(d => !seen.Add(d.name));
            return l;
        }
    }
}
