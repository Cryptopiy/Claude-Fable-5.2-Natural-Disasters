using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MESOCYCLONE
{
    /// <summary>Extra materials and textures of the props (everything else uses the atlas, its surface classes and
    /// the glows). The gas-station ground is the one prop surface seen large and close under bright light: it gets
    /// full photo materials (asphalt apron, concrete slabs with saw-cut joints) on UV1 metres, with oil stains.</summary>
    public static class WorldLibrary
    {
        const float SlabT = 3f;                           // concrete_floor_worn_001 is 3 m: one slab per tile, joints on its edges

        public static void Textures(List<TexDef> l)
        {
            l.Add(new TexDef("pool_glow", 64, PaintPool));
            l.Add(Photo.Albedo("asphalt_02", 2048, 0.3f, 1.1f));
            l.Add(Photo.Mask("asphalt_02", 1024));
            l.Add(new TexDef("slab_c", 2048, new[] { "concrete_floor_worn_001/diff" }, PaintSlabC) { aniso = 16, rev = 1 });
            l.Add(new TexDef("slab_m", 1024, new[] { "concrete_floor_worn_001/arm" }, PaintSlabM) { linear = true, aniso = 16, rev = 1 });
            l.Add(new TexDef("slab_n", 2048, new[] { "concrete_floor_worn_001/nor_gl" }, PaintSlabN) { normal = true, aniso = 16, rev = 1 });
            // the canopy soffit's glow (a level, not a colour): BC7 so the soft halos do not band when seen close overhead
            l.Add(new TexDef("soffit_glow", 512, PaintSoffit) { linear = true, hq = true, clamp = true });
        }

        public static void Materials(List<MatDef> l)
        {
            // the pool of light under a lamp: an additive quad with a soft radial falloff
            MatDef pool = MatDef.Glow("glow_pool", Palette.Sodium, 3f, MatSurface.Additive);
            pool.emissiveMap = "pool_glow";
            l.Add(pool);
            l.Add(new MatDef("gs_asphalt")
            {
                color = Palette.AsphaltAged, normalizedAlbedo = true, baseMap = Photo.C("asphalt_02"), normalMap = Photo.N("asphalt_02"),
                maskMap = Photo.M("asphalt_02"), smoothRemap = new Vector2(0.16f, 0.38f), baseUV = 1, tiling = new Vector2(1f / 3f, 1f / 3f),
                detailMap = CoreLibrary.StainsD, detailUV = 1, detailTiling = new Vector2(1f / 9f, 1f / 9f)
            });
            l.Add(new MatDef("gs_concrete")
            {
                color = Palette.ConcreteApron, normalizedAlbedo = true, baseMap = "slab_c", normalMap = "slab_n", maskMap = "slab_m",
                smoothRemap = new Vector2(0.18f, 0.42f), baseUV = 1, tiling = new Vector2(1f / SlabT, 1f / SlabT),
                detailMap = CoreLibrary.StainsD, detailUV = 1, detailTiling = new Vector2(1f / 7.3f, 1f / 7.3f), detailAlbedo = 0.8f
            });
            // the canopy's underside: nothing can light it from below (the spots hang under it and point down, the flash
            // only comes from above, there is no GI), so it carries its own light, the panels' spill painted into its
            // glow map. Lit, not a glow: it stays a painted surface with its grime detail and the sheen of painted metal
            // (do not count on lightning to show on it: the flash is clamped to come from above, and this faces down).
            // PropLights scales the emissive colour with the canopy each time it changes; the 20 nits here are only the
            // value the asset is built with.
            l.Add(new MatDef("gs_soffit")
            {
                color = Palette.Hex(0xe8e6e0), smoothness = 0.3f,
                emissive = Color.Lerp(Palette.CanopyWarm, Color.white, 0.35f), emissiveNits = 20f, emissiveMap = "soffit_glow",
                detailMap = CoreLibrary.GrimeD, detailUV = 1, detailTiling = new Vector2(1f / 2.2f, 1f / 2.2f)
            });
            // the pump screens: a backlit LCD, a small cool accent in the warm space (about 1.3 before the tone mapper
            // at the canopy's exposure)
            l.Add(MatDef.Glow("glow_pump", Palette.Hex(0xdfeaf2), 22f));
        }

        /// <summary>The soffit's glow level over the whole canopy (u across its width, v along its length, the station
        /// frame of GasStation): a broad level that falls toward the open edges, a soft halo round each of the six
        /// light panels (the light they throw on the white metal next to them), the seams of the linear-metal strips
        /// and a few faint stains. About 1.0 at a panel's edge, 0.55 between panels, 0.19 at the rim.</summary>
        static Color32[] PaintSoffit(int n)
        {
            const float W = GasStation.CanopyW, L = GasStation.CanopyL;
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                    float pX = u * W - W * 0.5f, pZ = v * L - L * 0.5f;
                    // distance (m) to the nearest panel rectangle, 0 on and under a panel
                    float d = float.MaxValue;
                    for (int sx = -1; sx <= 1; sx += 2)
                        for (int k = -1; k <= 1; k++)
                        {
                            float dx = Mathf.Max(Mathf.Abs(pX - sx * GasStation.PanelX) - GasStation.PanelW * 0.5f, 0f);
                            float dz = Mathf.Max(Mathf.Abs(pZ - k * GasStation.PanelZ) - GasStation.PanelL * 0.5f, 0f);
                            d = Mathf.Min(d, Mathf.Sqrt(dx * dx + dz * dz));
                        }
                    float halo = Mathf.Exp(-d / 0.6f);
                    float edge = Mathf.Min(W * 0.5f - Mathf.Abs(pX), L * 0.5f - Mathf.Abs(pZ));
                    float g = 0.18f + 0.24f * TexPaint.Ss(0f, 2.5f, edge) + 0.6f * halo;
                    if (x % 10 == 0) g *= 0.82f;                                              // strip seams along the length, one every ~19.5 cm
                    g *= 0.94f + 0.06f * CoreLibrary.Fbm(u, v * 1.4f, 8, 3, 611);
                    byte b = (byte)(Mathf.Clamp01(g) * 255f);
                    px[y * n + x] = new Color32(b, b, b, 255);
                }
            return px;
        }

        /// <summary>Distance (m) to the nearest saw-cut joint at the tile edges.</summary>
        static float Joint(float u, float v)
        {
            float du = Mathf.Min(u, 1f - u), dv = Mathf.Min(v, 1f - v);
            return Mathf.Min(du, dv) * SlabT;
        }

        static Color32[] PaintSlabC(int n, Img[] s)
        {
            Img d = s[0];
            Vector3 m = TexPaint.MeanLin(d);
            float ml = TexPaint.Lum(m);
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                    Vector3 r = TexPaint.Ratio(d.SampleLin(u, v), m, ml, 0.3f, 1.5f);
                    float j = Joint(u, v);
                    float groove = 1f - TexPaint.Ss(0.004f, 0.009f, j), lip = 1f - TexPaint.Ss(0.009f, 0.05f, j);   // dirt collects along the joint
                    r *= (1f - 0.65f * groove) * (1f - 0.12f * lip) * (0.94f + 0.12f * TexPaint.Fbm2(u, v, 3, 3, 4, 171));
                    px[y * n + x] = TexPaint.SrgbPx(r * 0.5f);
                }
            return px;
        }

        static Color32[] PaintSlabM(int n, Img[] s)
        {
            Img arm = s[0];
            Vector3 st = TexPaint.SmoothStats(arm);
            float inv = st.x >= 0f ? 1f / (st.y - st.x) : 0f;
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                    Vector4 a = arm.Sample(u, v);
                    float sn = st.x >= 0f ? Mathf.Clamp01((1f - a.y - st.x) * inv) : 0.5f;
                    float groove = 1f - TexPaint.Ss(0.004f, 0.009f, Joint(u, v));
                    px[y * n + x] = new Color32(255, TexPaint.B(Mathf.Lerp(1f, a.x, 0.8f) * (1f - 0.5f * groove)), 255, TexPaint.B(sn * (1f - 0.8f * groove)));
                }
            return px;
        }

        static Color32[] PaintSlabN(int n, Img[] s)
        {
            Img nor = s[0];
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                    Vector3 nn = TexPaint.Strength(nor.SampleNormal(u, v), 1.2f);
                    // the joint's walls: a 7 mm wide cut; the height rises away from the cut (dh/du > 0 just after u = 0)
                    float du = Mathf.Min(u, 1f - u) * SlabT, dv = Mathf.Min(v, 1f - v) * SlabT;
                    float gu = du < 0.007f ? (u < 0.5f ? 1f : -1f) * (1f - du / 0.007f) * 0.8f : 0f;
                    float gv = dv < 0.007f ? (v < 0.5f ? 1f : -1f) * (1f - dv / 0.007f) * 0.8f : 0f;
                    px[y * n + x] = TexPaint.PackNormal(TexPaint.AddSlope(nn, gu, gv));
                }
            return px;
        }

        static Color32[] PaintPool(int n)
        {
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    float r = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                    float v = r * r * (3f - 2f * r);
                    byte b = (byte)(v * v * 255f);
                    px[y * n + x] = new Color32(b, b, b, 255);
                }
            return px;
        }
    }

    /// <summary>Everything man-made and every tree on the land. Built once at load under the world root, in road
    /// coordinates (chainage, lateral). Static geometry is combined into chunks; small things fade with LOD groups.</summary>
    public static class WorldProps
    {
        public const float SwathS0 = 13250f, SwathS1 = 15350f, SwathMid = 14300f;   // the tornado's damage swath
        public const float PowerOutT = 462f;                                        // the grid south of the town dies
        public static readonly float[] FarmS = { 1250f, 3950f, 6100f, 7400f, 9350f, 11900f };
        public static readonly float[] FarmLat = { -170f, 230f, -300f, 200f, -240f, 260f };
        public static readonly int IdEmissive = Shader.PropertyToID("_EmissiveColor");

        public static Material Atlas => GameAssets.I.Mat("atlas");
        public static Material Metal => GameAssets.I.Mat("atlas_metal");
        public static bool InSwath(double s) => s >= SwathS0 && s <= SwathS1;

        public static void Build(Transform worldRoot)
        {
            var root = new GameObject("Props").transform;
            root.SetParent(worldRoot, false);
            PropLights lights = root.gameObject.AddComponent<PropLights>();
            PolesBuilder.Build(root);
            TurbineBuilder.Build(root);
            TownBuilder.Build(root);
            FarmBuilder.Build(root, lights);
            Shelterbelts.Build(root);
            RoadFurniture.Build(root, lights);
            GasStation.Build(root, lights);
            DebrisBuilder.Build(root);
        }

        /// <summary>Emissive level (nits) of a light dot: the designed level, but never more than "white" times the
        /// exposed white point, so the 2-pixel dots do not turn into bloom balls at night exposure.</summary>
        public static float DotNits(float baseNits, float white)
        {
            return Mathf.Min(baseNits, white * 1.2f * Mathf.Pow(2f, Director.S.exposureEV));
        }

        /// <summary>0 by day .. 1 at dusk and night: lamps come on by themselves.</summary>
        public static float Dusk => Mathf.Clamp01(Mathf.InverseLerp(11.6f, 10.4f, Director.S.exposureEV));

        public static Color Hdr(Color srgb, float nits)
        {
            Color c = srgb.linear;
            return new Color(c.r * nits, c.g * nits, c.b * nits, 1f);
        }

        /// <summary>One LOD that cross-fades to nothing at about cullDistance metres (from the centre of the object).</summary>
        public static void Lod(GameObject go, float cullDistance)
        {
            var mr = go.GetComponent<MeshRenderer>();
            var mf = go.GetComponent<MeshFilter>();
            if (mr == null) return;
            var lg = go.AddComponent<LODGroup>();
            lg.fadeMode = LODFadeMode.CrossFade;
            lg.animateCrossFading = true;
            var rs = new Renderer[] { mr };
            lg.SetLODs(new LOD[] { new LOD(0.5f, rs) });
            lg.RecalculateBounds();
            float size = lg.size;
            if (size < 0.01f && mf != null && mf.sharedMesh != null)
            {
                Bounds b = mf.sharedMesh.bounds;
                size = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
                lg.localReferencePoint = b.center;
                lg.size = size;
            }
            float bias = Mathf.Max(0.05f, Tune.F("world.lodBias", 1f));
            float h = size * bias / (2f * cullDistance * Mathf.Tan(Config.FovDefault * 0.5f * Mathf.Deg2Rad));
            lg.SetLODs(new LOD[] { new LOD(Mathf.Clamp(h, 0.0005f, 0.9f), rs) });
        }

        public static void NoShadow(GameObject go)
        {
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null) mr.shadowCastingMode = ShadowCastingMode.Off;
        }

        // ---------------------------------------------------------------- winding-safe faces

        /// <summary>Quad whose front faces "outward" (any vector on the visible side), whatever the corner order.</summary>
        public static void QuadF(MeshBuilder mb, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) >= 0f) mb.Quad(a, b, c, d); else mb.Quad(d, c, b, a);
        }

        public static void TriF(MeshBuilder mb, Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) >= 0f) mb.Tri(a, b, c); else mb.Tri(a, c, b);
        }

        /// <summary>A thin sheet seen from both sides.</summary>
        public static void Quad2(MeshBuilder mb, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            mb.Quad(a, b, c, d); mb.Quad(d, c, b, a);
        }

        /// <summary>Gable roof on a w x d footprint centred on the local origin, ridge along local Z, eaves at y0.</summary>
        public static void Gable(MeshBuilder mb, float w, float d, float y0, float rise, float eave, int roofSw, int wallSw)
        {
            float hw = w * 0.5f, hd = d * 0.5f, k = rise / hw;
            mb.Swatch(wallSw);
            TriF(mb, new Vector3(-hw, y0, hd), new Vector3(hw, y0, hd), new Vector3(0f, y0 + rise, hd), Vector3.forward);
            TriF(mb, new Vector3(-hw, y0, -hd), new Vector3(hw, y0, -hd), new Vector3(0f, y0 + rise, -hd), Vector3.back);
            mb.Swatch(roofSw);
            float ex = hw + eave, ey = y0 - eave * k + 0.03f, ez = hd + eave, top = y0 + rise + 0.03f;
            for (int side = -1; side <= 1; side += 2)
                Quad2(mb, new Vector3(side * ex, ey, -ez), new Vector3(side * ex, ey, ez), new Vector3(0f, top, ez), new Vector3(0f, top, -ez));
        }

        /// <summary>Gambrel (barn) roof, ridge along local Z, eaves at y0, ridge at y0 + rise.</summary>
        public static void Gambrel(MeshBuilder mb, float w, float d, float y0, float rise, int roofSw, int wallSw)
        {
            float hw = w * 0.5f, hd = d * 0.5f, kx = hw * 0.58f, ky = y0 + rise * 0.68f, top = y0 + rise;
            mb.Swatch(wallSw);
            for (int e = -1; e <= 1; e += 2)
            {
                var o = new Vector3(0f, 0f, e);
                QuadF(mb, new Vector3(-hw, y0, e * hd), new Vector3(hw, y0, e * hd), new Vector3(kx, ky, e * hd), new Vector3(-kx, ky, e * hd), o);
                TriF(mb, new Vector3(-kx, ky, e * hd), new Vector3(kx, ky, e * hd), new Vector3(0f, top, e * hd), o);
            }
            mb.Swatch(roofSw);
            float ez = hd + 0.5f;
            for (int s = -1; s <= 1; s += 2)
            {
                Quad2(mb, new Vector3(s * (hw + 0.04f), y0, -ez), new Vector3(s * (hw + 0.04f), y0, ez), new Vector3(s * kx, ky + 0.03f, ez), new Vector3(s * kx, ky + 0.03f, -ez));
                Quad2(mb, new Vector3(s * kx, ky + 0.03f, -ez), new Vector3(s * kx, ky + 0.03f, ez), new Vector3(0f, top + 0.03f, ez), new Vector3(0f, top + 0.03f, -ez));
            }
        }

        /// <summary>A small house in the current frame: walls w x d, gable ridge along local Z, dark windows.</summary>
        public static void House(MeshBuilder mb, float w, float d, float wallH, float rise, int wallSw, int roofSw)
        {
            mb.Swatch(wallSw);
            mb.Box(new Vector3(0f, wallH * 0.5f, 0f), new Vector3(w, wallH, d), 4 | 8);
            Gable(mb, w, d, wallH, rise, 0.45f, roofSw, wallSw);
            mb.Swatch(Sw.GlassDark);
            float hw = w * 0.5f + 0.03f, wy = Mathf.Min(1.9f, wallH * 0.55f);
            for (int side = -1; side <= 1; side += 2)
                for (int k = -1; k <= 1; k += 2)
                {
                    float z = k * d * 0.24f;
                    QuadF(mb, new Vector3(side * hw, wy - 0.6f, z - 0.55f), new Vector3(side * hw, wy + 0.6f, z - 0.55f),
                        new Vector3(side * hw, wy + 0.6f, z + 0.55f), new Vector3(side * hw, wy - 0.6f, z + 0.55f), new Vector3(side, 0f, 0f));
                }
        }
    }

    /// <summary>One combined mesh in the making. Its object sits at "origin" (a point of the road frame), so the
    /// vertices stay small numbers; P() and At() convert road coordinates into the chunk's local space.</summary>
    public sealed class Chunk
    {
        public readonly MeshBuilder mb = new MeshBuilder();
        public readonly Vector3 origin;

        public Chunk(double s, double lateral, int subMeshes = 1)
        {
            origin = Land.RoadPoint(s, lateral, 0f);
            if (subMeshes > 1) { mb.SetSub(subMeshes - 1); mb.SetSub(0); }
        }

        /// <summary>Road coordinates to the chunk's local space.</summary>
        public Vector3 P(double s, double lateral, float y = 0f) => Land.RoadPoint(s, lateral, y) - origin;

        public Quaternion RoadRot(double s, float yawDeg = 0f) => Quaternion.Euler(0f, (float)PathDef.HeadingDeg(s) + yawDeg, 0f);

        /// <summary>Sets the builder's frame: origin at the road point, +Z along the road turned by yaw, +X to the right.</summary>
        public void At(double s, double lateral, float y = 0f, float yawDeg = 0f)
        {
            mb.Push(P(s, lateral, y), RoadRot(s, yawDeg));
        }

        /// <summary>A flat patch on the ground between two chainages and two lateral offsets.</summary>
        public void Ground(double s0, double s1, double lat0, double lat1, float y, int swatch)
        {
            mb.Reset();
            mb.Swatch(swatch);
            WorldProps.QuadF(mb, P(s0, lat0, y), P(s1, lat0, y), P(s1, lat1, y), P(s0, lat1, y), Vector3.up);
        }

        public GameObject Emit(string name, Transform parent, bool shadows, params Material[] mats)
        {
            mb.Reset();
            GameObject go = mb.ToObject(name, parent, mats);
            go.transform.localPosition = origin;
            if (!shadows) WorldProps.NoShadow(go);
            return go;
        }
    }
}
