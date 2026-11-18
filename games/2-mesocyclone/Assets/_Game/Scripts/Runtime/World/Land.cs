using System.Collections.Generic;
using UnityEngine;

namespace MESOCYCLONE
{
    /// <summary>The land: flat plains to the horizon, a quilt of fields along the road, the wet two-lane road with
    /// painted lines, grass verges and the two gravel crossroads. Built once at load, in world coordinates under
    /// the world root (which moves under the truck).
    /// Surfaces are real photo materials (Poly Haven, CC0) at their physical size: UV0 is in metres (road: across x
    /// along a 48 m tile), so a texture tile is metres, not tens of metres. The road, the verge and three field types
    /// are composites painted at edit time from the photos plus the layout (lines, wheel tracks, patches, crack seal,
    /// crop rows, furrows). UV1 carries a slow macro variation (detail map) that breaks the tiling seen at grazing
    /// angles. Nothing is mapped in world space: the world root moves, so world-space mapping would slide.</summary>
    public static class Land
    {
        static Material roadMat, vergeMat, gravelMat;
        static readonly Material[] fieldMats = new Material[4];
        static readonly int idSmoothMin = Shader.PropertyToID("_SmoothnessRemapMin"), idSmoothMax = Shader.PropertyToID("_SmoothnessRemapMax");
        static readonly int idBase = Shader.PropertyToID("_BaseColor"), idNormalScale = Shader.PropertyToID("_NormalScale");
        static readonly int idDetailSmooth = Shader.PropertyToID("_DetailSmoothnessScale"), idDetailNormal = Shader.PropertyToID("_DetailNormalScale");
        static Color[] fieldBase = new Color[4];
        static Color vergeBase, gravelBase;
        static Vector2[] fieldRemap = new Vector2[4];
        static Vector2 vergeRemap, gravelRemap;
        static float lastWet = -1f;

        // the field nearest the road, per 600 m chunk (from s = -1800) and side: crop 0 wheat, 1 young crop, 2 soil,
        // 3 stubble; fields start 16 m from the centreline and run from s0 + 4 to s0 + 596
        public const int NearCropCount = 40;
        static readonly int[] nearCrop = new int[NearCropCount * 2];
        static readonly float[] nearWidth = new float[NearCropCount * 2];

        /// <summary>Crop of the field along the road at chainage s on one side (-1 left, +1 right); -1 in the gaps
        /// between fields. width = how far the field reaches from its inner edge.</summary>
        public static int NearCrop(double s, int side, out float width)
        {
            width = 0f;
            double f = (s + 1800.0) / 600.0;
            int ci = (int)System.Math.Floor(f);
            if (ci < 0 || ci >= NearCropCount) return -1;
            double inChunk = s - (ci * 600.0 - 1800.0);
            if (inChunk < 4.0 || inChunk > 596.0) return -1;
            int k = ci * 2 + (side > 0 ? 1 : 0);
            width = nearWidth[k];
            return nearCrop[k];
        }

        const float RoadTile = 48f;                       // metres of road per texture repeat (four 12 m dash cycles)
        const float W = 2f * (Config.RoadHalfWidth + Config.ShoulderWidth);   // 10.6 m across the road texture
        const float Sa = 3.0f, Sg = 2.0f, Sgr = 2.509f;   // photo tile sizes (m): asphalt_02, gravel_road, grass_ground
        const float VergeW = 12f, VergeL = 3f * Sgr;      // verge texture: 12 m across (clamped), 7.53 m along
        // field composites: tile size (m) and row spacing
        const float CropT = 4f, SoilT = 4f, StubT = 3f, WheatT = 2f, RowP = 0.8f, StubP = 0.25f;

        static float Ss(float e0, float e1, float x) => TexPaint.Ss(e0, e1, x);
        static float Sq(float x) => x * x;

        public static void Textures(List<TexDef> l)
        {
            // the road: albedo with the layout painted into the asphalt, mask (AO, smoothness, detail mask), normal
            l.Add(new TexDef("road_c", 2048, new[] { "asphalt_02/diff", "gravel_road/diff" }, PaintRoadC) { aniso = 16, rev = 2 });
            l.Add(new TexDef("road_m", 1024, new[] { "asphalt_02/diff", "asphalt_02/arm", "gravel_road/arm" }, PaintRoadM) { linear = true, aniso = 16, rev = 2 });
            l.Add(new TexDef("road_n", 2048, new[] { "asphalt_02/diff", "asphalt_02/nor_gl", "gravel_road/nor_gl" }, PaintRoadN) { normal = true, aniso = 16, rev = 2 });
            l.Add(Photo.Detail("asphalt_02", 1024, 1f, 1f));
            // the verge: gravel fringe, sun-dried strip, ditch, taller grass toward the field
            l.Add(new TexDef("verge_c", 2048, new[] { "grass_ground/diff", "gravel_road/diff" }, PaintVergeC) { clampU = true, aniso = 16, rev = 1 });
            l.Add(new TexDef("verge_m", 1024, new[] { "grass_ground/arm" }, PaintVergeM) { linear = true, clampU = true, aniso = 16, rev = 1 });
            l.Add(new TexDef("verge_n", 2048, new[] { "grass_ground/nor_gl", "gravel_road/nor_gl" }, PaintVergeN) { normal = true, clampU = true, aniso = 16, rev = 1 });
            // fields (1k: they start 16 m from the road; 1k over 2-4 m is still finer than the screen there)
            l.Add(Photo.Albedo("withered_grass", 1024, 0.35f, 0.9f));
            l.Add(Photo.Mask("withered_grass", 512));
            l.Add(new TexDef("crop_c", 1024, new[] { "sparse_grass/diff", "farm_soil/diff" }, PaintCropC) { aniso = 16, rev = 1 });
            l.Add(new TexDef("crop_m", 512, new[] { "sparse_grass/arm", "farm_soil/arm" }, PaintCropM) { linear = true, aniso = 16, rev = 1 });
            l.Add(new TexDef("crop_n", 1024, new[] { "sparse_grass/nor_gl", "farm_soil/nor_gl" }, PaintCropN) { normal = true, aniso = 16, rev = 1 });
            l.Add(new TexDef("soil_c", 1024, new[] { "farm_soil/diff" }, PaintSoilC) { aniso = 16, rev = 1 });
            l.Add(new TexDef("soil_m", 512, new[] { "farm_soil/arm" }, PaintSoilM) { linear = true, aniso = 16, rev = 1 });
            l.Add(new TexDef("soil_n", 1024, new[] { "farm_soil/nor_gl" }, PaintSoilN) { normal = true, aniso = 16, rev = 1 });
            l.Add(new TexDef("stubble_c", 1024, new[] { "dry_mud_field_001/diff" }, PaintStubC) { aniso = 16, rev = 1 });
            l.Add(new TexDef("stubble_m", 512, new[] { "dry_mud_field_001/arm" }, PaintStubM) { linear = true, aniso = 16, rev = 1 });
            l.Add(new TexDef("stubble_n", 1024, new[] { "dry_mud_field_001/nor_gl" }, PaintStubN) { normal = true, aniso = 16, rev = 1 });
            // gravel side roads
            l.Add(Photo.Albedo("gravel_road", 1024, 0.6f));
            l.Add(Photo.Mask("gravel_road", 512));
        }

        public static void Materials(List<MatDef> l)
        {
            // road: UV0 u = across (0..1 = 10.6 m), v = along / 48 m. Fine asphalt grain as a UV0 detail at 1.5 m.
            l.Add(new MatDef("road_tex")
            {
                color = Color.white, baseMap = "road_c", normalMap = "road_n", maskMap = "road_m", smoothRemap = new Vector2(0f, 1f),
                detailMap = Photo.D("asphalt_02"), detailUV = 0, detailTiling = new Vector2(W / 1.5f, RoadTile / 1.5f), detailAlbedo = 0.5f,
                detailNormal = 0.7f, detailSmooth = 0.6f
            });
            // verge: UV0 u = metres from the road edge / 12, v = metres along / 7.53; macro variation on UV1 (world metres)
            l.Add(new MatDef("verge")
            {
                color = Color.white, baseMap = "verge_c", normalMap = "verge_n", maskMap = "verge_m", smoothRemap = new Vector2(0f, 1f),
                detailMap = CoreLibrary.MacroD, detailUV = 1, detailTiling = new Vector2(1f / 53f, 1f / 47f), detailAlbedo = 0.8f, detailNormal = 0.3f
            });
            // fields: UV0 in metres (random offset per field), tiling = 1 / tile size
            l.Add(Field("field_wheat", Photo.C("withered_grass"), Photo.M("withered_grass"), Photo.N("withered_grass"), WheatT, Palette.FieldWheat, true, new Vector2(0.05f, 0.28f)));
            l.Add(Field("field_crop", "crop_c", "crop_m", "crop_n", CropT, Color.white, false, new Vector2(0.04f, 0.3f)));
            l.Add(Field("field_soil", "soil_c", "soil_m", "soil_n", SoilT, Color.white, false, new Vector2(0.03f, 0.2f)));
            l.Add(Field("field_stubble", "stubble_c", "stubble_m", "stubble_n", StubT, Color.white, false, new Vector2(0.04f, 0.22f)));
            // gravel side roads and yards: base on UV1 (box-projected metres), 2 m tile
            l.Add(new MatDef("land_gravel")
            {
                color = Palette.GravelShoulder, normalizedAlbedo = true, baseMap = Photo.C("gravel_road"), normalMap = Photo.N("gravel_road"),
                maskMap = Photo.M("gravel_road"), smoothRemap = new Vector2(0.08f, 0.22f), baseUV = 1, tiling = new Vector2(1f / Sg, 1f / Sg),
                detailMap = CoreLibrary.MacroD, detailUV = 1, detailTiling = new Vector2(1f / 37f, 1f / 41f), detailAlbedo = 0.7f, detailNormal = 0.2f
            });
        }

        static MatDef Field(string name, string c, string m, string n, float tile, Color color, bool normalized, Vector2 smooth)
        {
            return new MatDef(name)
            {
                color = color, normalizedAlbedo = normalized, baseMap = c, maskMap = m, normalMap = n, smoothRemap = smooth,
                tiling = new Vector2(1f / tile, 1f / tile), detailMap = CoreLibrary.MacroD, detailUV = 1,
                detailTiling = new Vector2(1f / 173f, 1f / 211f), detailAlbedo = 1f, detailNormal = 0.3f
            };
        }

        // ============================================================================================ road painters
        struct RoadPx { public float white, yellow, tar, patch, track, oil, gravel, shoulder; }

        static readonly Vector4[] patches = MakePatches();

        /// <summary>Repair patches of fresher asphalt in the 48 m tile: (lateral min, lateral max, along min, along max).</summary>
        static Vector4[] MakePatches()
        {
            var r = new Rng(1301);
            var p = new Vector4[5];
            for (int i = 0; i < p.Length; i++)
            {
                float side = r.Chance(0.6f) ? 1f : -1f, w = r.Range(0.9f, 2.4f), c = r.Range(0.7f, 2.9f), len = r.Range(1.2f, 5.5f), a0 = r.Range(0f, RoadTile);
                p[i] = new Vector4(side * c - w * 0.5f, side * c + w * 0.5f, a0, a0 + len);
            }
            return p;
        }

        static float Patch(float m, float a)
        {
            float best = 0f;
            for (int k = -1; k <= 1; k++)
            {
                float aa = a + k * RoadTile;
                foreach (Vector4 p in patches)
                {
                    float d = Mathf.Min(Mathf.Min(m - p.x, p.y - m), Mathf.Min(aa - p.z, p.w - aa));
                    best = Mathf.Max(best, Ss(0f, 0.03f, d));
                }
            }
            return best;
        }

        /// <summary>Along-road positions (m, in the 48 m tile) of the sealed transverse cracks: irregular on purpose, an
        /// even spacing reads as concrete slab joints.</summary>
        static readonly float[] CrackA = { 2.3f, 9.8f, 13.1f, 24.6f, 31.2f, 40.7f };

        /// <summary>Crack sealant ("tar snakes"): transverse thermal cracks, the joint beside the centre line, the joint
        /// at the lane edge. All periodic over the 48 m tile.</summary>
        static float Tar(float m, float a, float u, float v)
        {
            const float tau = Mathf.PI * 2f;
            float t = 0f;
            for (int i = 0; i < CrackA.Length; i++)
            {
                float ai = CrackA[i];
                float c = ai + 0.22f * Mathf.Sin(m * 0.8f + i * 1.9f) + 0.07f * Mathf.Sin(m * 3.1f + i);
                float d = Mathf.Abs(Mathf.Repeat(a - c + RoadTile * 0.5f, RoadTile) - RoadTile * 0.5f);
                float reach = i % 3 == 0 ? Config.RoadHalfWidth + 0.6f : (i % 3 == 1 ? 3.0f : 1.6f);   // full width, one lane, half a lane
                float side = (i % 2 == 0) ? 1f : -1f;
                float span = i % 3 == 0 ? 1f : 1f - Ss(reach - 0.3f, reach, side * m);
                span *= i % 3 == 0 ? 1f : Ss(-0.4f, 0f, side * m);
                t = Mathf.Max(t, (1f - Ss(0.018f, 0.03f, d)) * span);
            }
            float joint = 0.17f + 0.04f * Mathf.Sin(a * tau * 3f / RoadTile) + 0.015f * Mathf.Sin(a * tau * 11f / RoadTile);
            float gaps = Ss(0.35f, 0.45f, TexPaint.Fbm2(u, v, 4, 16, 2, 91));
            t = Mathf.Max(t, (1f - Ss(0.015f, 0.026f, Mathf.Abs(m - joint))) * gaps);
            float edge = Config.RoadHalfWidth + 0.03f + 0.03f * Mathf.Sin(a * tau * 5f / RoadTile);
            float gaps2 = Ss(0.4f, 0.5f, TexPaint.Fbm2(u, v, 4, 16, 2, 92));
            t = Mathf.Max(t, (1f - Ss(0.015f, 0.026f, Mathf.Abs(Mathf.Abs(m) - edge))) * gaps2);
            return t;
        }

        static RoadPx Layout(float m, float a)
        {
            var r = new RoadPx();
            float am = Mathf.Abs(m), u = m / W + 0.5f, v = a / RoadTile;
            // paint: worn in patches along the line
            float cov = Ss(0.3f, 0.5f, TexPaint.Fbm2(u, v, 48, 216, 3, 75) + 0.18f);
            float edgeC = Config.RoadHalfWidth - 0.18f;
            r.white = (1f - Ss(0.055f, 0.068f, Mathf.Abs(am - edgeC))) * cov;
            float dash = Mathf.Repeat(a, 12f);
            r.yellow = (1f - Ss(0.055f, 0.068f, am)) * Ss(0f, 0.03f, dash) * (1f - Ss(2.97f, 3.0f, dash)) * cov;
            // wheel tracks (polished, darker) and the oil line between them, in both lanes
            float lane = 1f - Ss(Config.RoadHalfWidth - 0.1f, Config.RoadHalfWidth + 0.1f, am);
            r.track = lane * Mathf.Exp(-Sq((Mathf.Abs(am - 1.85f) - 0.85f) / 0.32f));
            r.oil = lane * Mathf.Exp(-Sq((am - 1.85f) / 0.22f)) * (0.5f + 0.5f * TexPaint.Fbm2(u, v, 8, 64, 2, 76));
            r.patch = Patch(m, a);
            r.tar = Tar(m, a, u, v) * (1f - r.patch * 0.7f);
            // paved shoulder, then a ragged gravel edge
            r.shoulder = Ss(Config.RoadHalfWidth - 0.02f, Config.RoadHalfWidth + 0.02f, am);
            float edgeNoise = (TexPaint.Fbm2(u, v, 40, 180, 3, 77) - 0.5f) * 0.4f;
            r.gravel = Ss(4.35f, 4.6f, am + edgeNoise);
            return r;
        }

        static Color32[] PaintRoadC(int n, Img[] s)
        {
            Img asp = s[0], grv = s[1];
            Vector3 ma = TexPaint.MeanLin(asp), mg = TexPaint.MeanLin(grv);
            float mla = TexPaint.Lum(ma), mlg = TexPaint.Lum(mg);
            Vector3 tA = TexPaint.LinV(Palette.AsphaltAged), tG = TexPaint.LinV(Palette.GravelShoulder);
            Vector3 white = TexPaint.LinV(Palette.RoadPaintWhite) * 0.8f, yellow = TexPaint.LinV(Palette.RoadPaintYellow) * 0.85f;
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n, v = (y + 0.5f) / n, m = (u - 0.5f) * W, a = v * RoadTile;
                    RoadPx p = Layout(m, a);
                    Vector3 ra = TexPaint.Ratio(asp.SampleLin(m / Sa, a / Sa), ma, mla, 0.35f, 1.15f);
                    Vector3 c = TexPaint.Mul(tA, ra);
                    float age = TexPaint.Fbm2(u, v, 6, 27, 3, 80);
                    c *= (0.9f + 0.2f * age) * (1f + 0.1f * p.shoulder);
                    c *= (1f - 0.16f * p.track) * (1f - 0.12f * p.oil) * (1f - 0.38f * p.patch);
                    c = Vector3.Lerp(c, tA * 0.3f, p.tar);
                    float through = Mathf.Clamp01((TexPaint.Lum(ra) - 1f) * 0.7f) * 0.6f;   // aggregate peaks wear through the paint
                    c = Vector3.Lerp(c, white * (0.9f + 0.1f * age), p.white * (1f - through));
                    c = Vector3.Lerp(c, yellow * (0.9f + 0.1f * age), p.yellow * (1f - through));
                    if (p.gravel > 0.001f)
                    {
                        Vector3 rg = TexPaint.Ratio(grv.SampleLin(m / Sg, a / Sg), mg, mlg, 0.6f, 1.1f);
                        c = Vector3.Lerp(c, TexPaint.Mul(tG, rg), p.gravel);
                    }
                    px[y * n + x] = TexPaint.SrgbPx(c);
                }
            return px;
        }

        static Color32[] PaintRoadM(int n, Img[] s)
        {
            Img asp = s[0], arm = s[1], garm = s[2];
            Vector3 ma = TexPaint.MeanLin(asp);
            float mla = TexPaint.Lum(ma);
            Vector3 st = TexPaint.SmoothStats(arm);
            float inv = st.x >= 0f ? 1f / (st.y - st.x) : 0f;
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n, v = (y + 0.5f) / n, m = (u - 0.5f) * W, a = v * RoadTile;
                    RoadPx p = Layout(m, a);
                    Vector4 ar = arm.Sample(m / Sa, a / Sa);
                    float sn = st.x >= 0f ? Mathf.Clamp01((1f - ar.y - st.x) * inv) : 0.5f;
                    float paint = Mathf.Max(p.white, p.yellow);
                    float through = Mathf.Clamp01((TexPaint.Lum(asp.SampleLin(m / Sa, a / Sa)) / mla - 1f) * 0.7f) * 0.6f;
                    float sm = Mathf.Lerp(0.2f, 0.36f, sn) + 0.07f * p.track + 0.04f * p.oil + 0.05f * p.patch;
                    sm = Mathf.Lerp(sm, 0.55f, p.tar);
                    sm = Mathf.Lerp(sm, 0.42f, paint * (1f - through));
                    float ao = Mathf.Lerp(1f, ar.x, 0.8f);
                    if (p.gravel > 0.001f)
                    {
                        Vector4 g = garm.Sample(m / Sg, a / Sg);
                        sm = Mathf.Lerp(sm, 0.12f, p.gravel);
                        ao = Mathf.Lerp(ao, Mathf.Lerp(1f, g.x, 0.9f), p.gravel);
                    }
                    float detail = 1f - 0.65f * paint - 0.8f * p.tar;
                    px[y * n + x] = new Color32(255, TexPaint.B(ao), TexPaint.B(detail), TexPaint.B(sm));
                }
            return px;
        }

        static Color32[] PaintRoadN(int n, Img[] s)
        {
            Img asp = s[0], nor = s[1], gnor = s[2];
            Vector3 ma = TexPaint.MeanLin(asp);
            float mla = TexPaint.Lum(ma);
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n, v = (y + 0.5f) / n, m = (u - 0.5f) * W, a = v * RoadTile;
                    RoadPx p = Layout(m, a);
                    float paint = Mathf.Max(p.white, p.yellow);
                    float through = Mathf.Clamp01((TexPaint.Lum(asp.SampleLin(m / Sa, a / Sa)) / mla - 1f) * 0.7f) * 0.6f;
                    // paint and fresh patches fill the texture, tar is a smooth strip
                    float k = 1.1f * (1f - 0.45f * p.patch) * (1f - 0.65f * paint * (1f - through)) * (1f - 0.8f * p.tar) * (1f - 0.25f * p.track);
                    Vector3 nn = TexPaint.Strength(nor.SampleNormal(m / Sa, a / Sa), k);
                    if (p.gravel > 0.001f) nn = Vector3.Slerp(nn, TexPaint.Strength(gnor.SampleNormal(m / Sg, a / Sg), 1.2f), p.gravel);
                    px[y * n + x] = TexPaint.PackNormal(nn);
                }
            return px;
        }

        // ============================================================================================ verge painters
        struct VergePx { public float gravel, dry, ditch, tall; }

        static VergePx VLayout(float d, float u, float v)
        {
            var r = new VergePx();
            float ragged = TexPaint.Fbm2(u, v, 24, 15, 3, 120);
            float slow = TexPaint.Fbm2(u, v, 3, 4, 3, 123) - 0.5f;                       // the zones wander along the road
            r.gravel = 1f - Ss(0.12f, 0.45f + 0.6f * ragged, d);
            r.dry = (1f - Ss(1.8f + 0.8f * ragged + 2f * slow, 3.4f + 0.6f * ragged + 2f * slow, d)) * 0.55f
                    + 0.35f * Ss(0.55f, 0.75f, TexPaint.Fbm2(u, v, 8, 5, 3, 122));      // dried patches anywhere
            r.ditch = Mathf.Exp(-Sq((d - 5.4f - 0.5f * (ragged - 0.5f) - 1.5f * slow) / 1.5f)) * (0.6f + 0.8f * Mathf.Max(0f, slow + 0.25f));
            r.tall = Ss(7f, 9f, d);
            return r;
        }

        static Color32[] PaintVergeC(int n, Img[] s)
        {
            Img gr = s[0], gv = s[1];
            Vector3 mgr = TexPaint.MeanLin(gr), mgv = TexPaint.MeanLin(gv);
            float lgr = TexPaint.Lum(mgr), lgv = TexPaint.Lum(mgv);
            Vector3 grass = TexPaint.LinV(Palette.VergeGrass), dry = TexPaint.LinV(Palette.VergeDry), ditch = TexPaint.LinV(Palette.DitchGreen);
            Vector3 gravel = TexPaint.LinV(Palette.GravelShoulder);
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n, v = (y + 0.5f) / n, d = u * VergeW, a = v * VergeL;
                    VergePx p = VLayout(d, u, v);
                    Vector3 tint = Vector3.Lerp(grass, dry, Mathf.Clamp01(p.dry));
                    tint = Vector3.Lerp(tint, ditch, Mathf.Clamp01(p.ditch) * 0.45f) * (1f - 0.08f * p.tall);
                    tint *= 0.85f + 0.3f * TexPaint.Fbm2(u, v, 12, 8, 3, 121);         // clumps of greener and browner grass
                    Vector3 c = TexPaint.Mul(tint, TexPaint.Ratio(gr.SampleLin(d / Sgr, a / Sgr), mgr, lgr, 0.45f, 1.2f));
                    if (p.gravel > 0.001f)
                        c = Vector3.Lerp(c, TexPaint.Mul(gravel, TexPaint.Ratio(gv.SampleLin(d / Sg + 0.37f, a / Sg), mgv, lgv, 0.6f, 1.1f)), p.gravel);
                    px[y * n + x] = TexPaint.SrgbPx(c);
                }
            return px;
        }

        static Color32[] PaintVergeM(int n, Img[] s)
        {
            Img arm = s[0];
            Vector3 st = TexPaint.SmoothStats(arm);
            float inv = st.x >= 0f ? 1f / (st.y - st.x) : 0f;
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n, v = (y + 0.5f) / n, d = u * VergeW, a = v * VergeL;
                    VergePx p = VLayout(d, u, v);
                    Vector4 ar = arm.Sample(d / Sgr, a / Sgr);
                    float sn = st.x >= 0f ? Mathf.Clamp01((1f - ar.y - st.x) * inv) : 0.5f;
                    float sm = Mathf.Lerp(0.06f, 0.2f, sn) + 0.06f * p.ditch;
                    sm = Mathf.Lerp(sm, 0.12f, p.gravel);
                    float ao = Mathf.Lerp(1f, ar.x, 0.9f) * (1f - 0.1f * p.tall);
                    px[y * n + x] = new Color32(255, TexPaint.B(ao), 255, TexPaint.B(sm));
                }
            return px;
        }

        static Color32[] PaintVergeN(int n, Img[] s)
        {
            Img gn = s[0], vn = s[1];
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n, v = (y + 0.5f) / n, d = u * VergeW, a = v * VergeL;
                    VergePx p = VLayout(d, u, v);
                    Vector3 nn = TexPaint.Strength(gn.SampleNormal(d / Sgr, a / Sgr), 1.1f + 0.3f * p.tall);
                    if (p.gravel > 0.001f) nn = Vector3.Slerp(nn, TexPaint.Strength(vn.SampleNormal(d / Sg + 0.37f, a / Sg), 1.2f), p.gravel);
                    px[y * n + x] = TexPaint.PackNormal(nn);
                }
            return px;
        }

        // ============================================================================================ field painters
        /// <summary>Distance (m) from the nearest row centre; rows run along v, spaced p metres across u.</summary>
        static float RowDist(float u, float tile, float p)
        {
            float x = u * tile / p;
            return Mathf.Abs(x - Mathf.Floor(x) - 0.5f) * p;
        }

        /// <summary>Young crop: plants in rows 0.8 m apart, soil between them.</summary>
        static float CropCover(float u, float v)
        {
            float jitter = (TexPaint.Fbm2(u, v, 20, 20, 3, 140) - 0.5f) * 0.12f;
            float clump = Ss(0.25f, 0.55f, TexPaint.Fbm2(u, v, 5, 40, 3, 141));        // gaps along the row
            return (1f - Ss(0.13f, 0.27f, RowDist(u, CropT, RowP) + jitter)) * (0.35f + 0.65f * clump);
        }

        static Color32[] PaintCropC(int n, Img[] s)
        {
            Img pl = s[0], so = s[1];
            Vector3 mp = TexPaint.MeanLin(pl), ms = TexPaint.MeanLin(so);
            float lp = TexPaint.Lum(mp), ls = TexPaint.Lum(ms);
            Vector3 green = TexPaint.LinV(Palette.FieldGreen) * 1.15f, soil = TexPaint.LinV(Palette.FieldSoil) * 1.1f;
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n, v = (y + 0.5f) / n, k = CropT / 2f;            // 2 m photos, 4 m tile
                    float cover = CropCover(u, v);
                    Vector3 cs = TexPaint.Mul(soil, TexPaint.Ratio(so.SampleLin(u * k, v * k), ms, ls, 0.5f, 1f)) * (1f - 0.25f * cover);
                    Vector3 cp = TexPaint.Mul(green, TexPaint.Ratio(pl.SampleLin(u * k + 0.31f, v * k), mp, lp, 0.6f, 1.2f));
                    px[y * n + x] = TexPaint.SrgbPx(Vector3.Lerp(cs, cp, cover));
                }
            return px;
        }

        static Color32[] PaintCropM(int n, Img[] s)
        {
            Img pa = s[0], sa = s[1];
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n, v = (y + 0.5f) / n, k = CropT / 2f;
                    float cover = CropCover(u, v);
                    Vector4 a = pa.Sample(u * k + 0.31f, v * k), b = sa.Sample(u * k, v * k);
                    float ao = Mathf.Lerp(Mathf.Lerp(1f, b.x, 0.8f) * (1f - 0.2f * cover), Mathf.Lerp(1f, a.x, 0.9f), cover);
                    float sm = Mathf.Lerp(0.15f, 0.75f, cover);                 // leaves have a sheen, dry soil none (remapped)
                    px[y * n + x] = new Color32(255, TexPaint.B(ao), 255, TexPaint.B(sm));
                }
            return px;
        }

        static Color32[] PaintCropN(int n, Img[] s)
        {
            Img pn = s[0], sn = s[1];
            var px = new Color32[n * n];
            float tau = Mathf.PI * 2f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n, v = (y + 0.5f) / n, k = CropT / 2f;
                    float cover = CropCover(u, v);
                    // the rows are planted on low ridges (3 cm)
                    float ph = u * CropT / RowP;
                    float slope = 0.03f * tau / RowP * Mathf.Sin(ph * tau);
                    Vector3 ns = TexPaint.AddSlope(sn.SampleNormal(u * k, v * k), slope, 0f);
                    Vector3 np = TexPaint.Strength(pn.SampleNormal(u * k + 0.31f, v * k), 1.3f);
                    px[y * n + x] = TexPaint.PackNormal(Vector3.Slerp(ns, np, cover));
                }
            return px;
        }

        /// <summary>Plowed soil: ridges 0.8 m apart, drier and paler on top, darker and moister in the furrows.</summary>
        static float Ridge(float u) { return 0.5f + 0.5f * Mathf.Cos(u * SoilT / RowP * Mathf.PI * 2f); }

        static Color32[] PaintSoilC(int n, Img[] s)
        {
            Img so = s[0];
            Vector3 ms = TexPaint.MeanLin(so);
            float ls = TexPaint.Lum(ms);
            Vector3 soil = TexPaint.LinV(Palette.FieldSoil);
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n, v = (y + 0.5f) / n, k = SoilT / 2f;
                    float r = Ridge(u + (TexPaint.Fbm2(u, v, 16, 16, 2, 150) - 0.5f) * 0.03f);
                    Vector3 c = TexPaint.Mul(soil, TexPaint.Ratio(so.SampleLin(u * k, v * k), ms, ls, 0.5f, 1.1f));
                    px[y * n + x] = TexPaint.SrgbPx(c * (0.78f + 0.36f * r));
                }
            return px;
        }

        static Color32[] PaintSoilM(int n, Img[] s)
        {
            Img arm = s[0];
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n, v = (y + 0.5f) / n, k = SoilT / 2f;
                    float r = Ridge(u);
                    Vector4 a = arm.Sample(u * k, v * k);
                    px[y * n + x] = new Color32(255, TexPaint.B(Mathf.Lerp(1f, a.x, 0.9f) * (0.8f + 0.2f * r)), 255, TexPaint.B(0.6f - 0.5f * r));
                }
            return px;
        }

        static Color32[] PaintSoilN(int n, Img[] s)
        {
            Img sn = s[0];
            var px = new Color32[n * n];
            float tau = Mathf.PI * 2f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n, v = (y + 0.5f) / n, k = SoilT / 2f;
                    float slope = -0.06f * tau / RowP * Mathf.Sin(u * SoilT / RowP * tau) * 0.5f;     // 6 cm ridges
                    px[y * n + x] = TexPaint.PackNormal(TexPaint.AddSlope(TexPaint.Strength(sn.SampleNormal(u * k, v * k), 1.2f), slope, 0f));
                }
            return px;
        }

        /// <summary>Wheat stubble: drill rows of cut straw 25 cm apart over dry ground, loose straw between them.</summary>
        static float Straw(float u, float v)
        {
            float row = 1f - Ss(0.03f, 0.07f, RowDist(u, StubT, StubP));
            float stalks = Ss(0.35f, 0.7f, TexPaint.Fbm2(u, v, 96, 96, 2, 160));
            float loose = Ss(0.55f, 0.8f, TexPaint.Fbm2(u, v, 24, 24, 3, 161));
            return Mathf.Clamp01(row * (0.4f + 0.6f * stalks) + 0.5f * loose);
        }

        static Color32[] PaintStubC(int n, Img[] s)
        {
            Img gd = s[0];
            Vector3 mg = TexPaint.MeanLin(gd);
            float lg = TexPaint.Lum(mg);
            Vector3 straw = TexPaint.LinV(Palette.FieldStubble) * 1.2f, ground = TexPaint.LinV(Palette.FieldSoil) * 1.25f;
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                    Vector3 r = TexPaint.Ratio(gd.SampleLin(u, v), mg, lg, 0.5f, 1f);
                    float st = Straw(u, v);
                    Vector3 c = Vector3.Lerp(TexPaint.Mul(ground, r), TexPaint.Mul(straw, r) * (0.9f + 0.25f * CoreLibrary.Hash(x, y, 162)), st);
                    px[y * n + x] = TexPaint.SrgbPx(c);
                }
            return px;
        }

        static Color32[] PaintStubM(int n, Img[] s)
        {
            Img arm = s[0];
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                    float st = Straw(u, v);
                    Vector4 a = arm.Sample(u, v);
                    px[y * n + x] = new Color32(255, TexPaint.B(Mathf.Lerp(1f, a.x, 0.8f) * (1f - 0.15f * st)), 255, TexPaint.B(0.2f + 0.6f * st));
                }
            return px;
        }

        static Color32[] PaintStubN(int n, Img[] s)
        {
            Img gn = s[0];
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                    float st = Straw(u, v);
                    // straw stalks: fine bumps across the rows
                    float h0 = Straw(u - 1f / n, v), h1 = Straw(u + 1f / n, v);
                    Vector3 nn = TexPaint.AddSlope(gn.SampleNormal(u, v), (h1 - h0) * 0.4f * n / 512f, 0f);
                    px[y * n + x] = TexPaint.PackNormal(TexPaint.Strength(nn, 1f + 0.4f * st));
                }
            return px;
        }

        // ============================================================================================ build
        public static void Build(Transform root)
        {
            GameAssets g = GameAssets.I;
            var land = new GameObject("Land").transform;
            land.SetParent(root, false);
            lastWet = -1f;
            extraWet.Clear();

            // ---- the plain to the horizon (below everything else) ----
            {
                var mb = new MeshBuilder();
                mb.Swatch(Sw.Stubble);
                float h = 70000f; double cz = Config.PathLength * 0.5;
                mb.Quad(new Vector3(-h, -0.35f, (float)cz - h), new Vector3(-h, -0.35f, (float)cz + h), new Vector3(h, -0.35f, (float)cz + h), new Vector3(h, -0.35f, (float)cz - h));
                Shadowless(mb.ToObject("Plain", land, g.Mat("atlas")));
            }

            // ---- fields: a quilt on both sides of the road, in 600 m chunks; UV0 in metres, a random offset per field
            //      and rows either along or across the road ----
            string[] crops = { "field_wheat", "field_crop", "field_soil", "field_stubble" };
            for (int i = 0; i < 4; i++)
            {
                fieldMats[i] = new Material(g.Mat(crops[i]));
                fieldBase[i] = fieldMats[i].GetColor(idBase);
                fieldRemap[i] = new Vector2(fieldMats[i].GetFloat(idSmoothMin), fieldMats[i].GetFloat(idSmoothMax));
            }
            var rng = new Rng(9001);
            var off = new Rng(9002);
            for (double s0 = -1800; s0 < Config.PathLength + 4200; s0 += 600)
            {
                var mbs = new MeshBuilder();
                for (int i = 1; i < 4; i++) mbs.SetSub(i);
                for (int side = -1; side <= 1; side += 2)
                {
                    float lat = 16f;
                    while (lat < 5200f)
                    {
                        float w = rng.Range(380f, 900f);
                        float len = 600f - 8f;
                        int crop = rng.Range(0, 4);
                        bool rot = rng.Chance(0.5f);
                        mbs.SetSub(crop);
                        Vector3 a = RoadPoint(s0 + 4, side * lat), b = RoadPoint(s0 + 4 + len, side * lat);
                        Vector3 c = RoadPoint(s0 + 4 + len, side * (lat + w)), d = RoadPoint(s0 + 4, side * (lat + w));
                        float ox = off.Range(0f, 64f), oy = off.Range(0f, 64f);
                        Vector2 ua = new Vector2(ox, oy), ub = new Vector2(ox, oy + len), uc = new Vector2(ox + w, oy + len), ud = new Vector2(ox + w, oy);
                        if (rot) { ua = new Vector2(ua.y, ua.x); ub = new Vector2(ub.y, ub.x); uc = new Vector2(uc.y, uc.x); ud = new Vector2(ud.y, ud.x); }
                        if (side > 0) mbs.QuadUV(a, b, c, d, ua, ub, uc, ud);
                        else mbs.QuadUV(d, c, b, a, ud, uc, ub, ua);
                        if (lat == 16f)
                        {   // the field along the road: the 3D crop near the truck follows it (GrassField)
                            int ci = (int)((s0 + 1800.0) / 600.0);
                            if (ci >= 0 && ci < NearCropCount) { nearCrop[ci * 2 + (side > 0 ? 1 : 0)] = crop; nearWidth[ci * 2 + (side > 0 ? 1 : 0)] = w; }
                        }
                        lat += w + 6f;
                    }
                }
                Shadowless(mbs.ToObject("Fields", land, fieldMats[0], fieldMats[1], fieldMats[2], fieldMats[3]));
            }

            // ---- road and verges, in 480 m chunks ----
            roadMat = new Material(g.Mat("road_tex"));
            vergeMat = new Material(g.Mat("verge"));
            vergeBase = vergeMat.GetColor(idBase);
            vergeRemap = new Vector2(vergeMat.GetFloat(idSmoothMin), vergeMat.GetFloat(idSmoothMax));
            float half = Config.RoadHalfWidth + Config.ShoulderWidth;
            const double Seg = 8.0;
            var chunkRng = new Rng(7919);
            for (double s0 = -1900; s0 < Config.PathLength + 4000; s0 += 480)
            {
                // a whole number of 12 m dash cycles of phase per chunk (s0 steps by 480 = 40 cycles, so the dashes stay
                // in step across chunks) changes which patches and cracks pass; the verge gets a free offset
                float phase = chunkRng.Range(0, 4) * 0.25f, vergeOff = chunkRng.Range(0f, 1f);
                var mb = new MeshBuilder(); mb.SetSub(1);
                for (double s = s0; s < s0 + 480 - 0.01; s += Seg)
                {
                    float v0 = (float)((s - s0) / RoadTile) + phase, v1 = (float)((s + Seg - s0) / RoadTile) + phase;
                    mb.SetSub(0);
                    mb.QuadUV(RoadPoint(s, -half, 0.02f), RoadPoint(s + Seg, -half, 0.02f), RoadPoint(s + Seg, half, 0.02f), RoadPoint(s, half, 0.02f),
                        new Vector2(0, v0), new Vector2(0, v1), new Vector2(1, v1), new Vector2(1, v0));
                    mb.SetSub(1);
                    float w0 = (float)((s - s0) / VergeL) + vergeOff, w1 = (float)((s + Seg - s0) / VergeL) + vergeOff, uo = (16.5f - half) / VergeW;
                    for (int side = -1; side <= 1; side += 2)
                    {
                        Vector3 a = RoadPoint(s, side * half, 0.01f), b = RoadPoint(s + Seg, side * half, 0.01f);
                        Vector3 c = RoadPoint(s + Seg, side * 16.5f, 0f), d = RoadPoint(s, side * 16.5f, 0f);
                        Vector2 ua = new Vector2(0, w0), ub = new Vector2(0, w1), uc = new Vector2(uo, w1), ud = new Vector2(uo, w0);
                        if (side > 0) mb.QuadUV(a, b, c, d, ua, ub, uc, ud); else mb.QuadUV(d, c, b, a, ud, uc, ub, ua);
                    }
                }
                Shadowless(mb.ToObject("Road", land, roadMat, vergeMat));
            }

            // ---- crossroads: gravel side roads (photo gravel on UV1 metres) ----
            {
                gravelMat = new Material(g.Mat("land_gravel"));
                gravelBase = gravelMat.GetColor(idBase);
                gravelRemap = new Vector2(gravelMat.GetFloat(idSmoothMin), gravelMat.GetFloat(idSmoothMax));
                var mb = new MeshBuilder();
                mb.Swatch(Sw.Gravel);
                foreach (double cs in PathDef.Crossroads)
                    for (int side = -1; side <= 1; side += 2)
                    {
                        Vector3 a = RoadPoint(cs - 3.2, side * (half - 0.2f), 0.012f), b = RoadPoint(cs + 3.2, side * (half - 0.2f), 0.012f);
                        Vector3 c = RoadPoint(cs + 3.2, side * 3200f, 0.012f), d = RoadPoint(cs - 3.2, side * 3200f, 0.012f);
                        if (side > 0) mb.Quad(a, b, c, d); else mb.Quad(d, c, b, a);
                    }
                Shadowless(mb.ToObject("Crossroads", land, gravelMat));
            }
        }

        static void Shadowless(GameObject go)
        {
            var mr = go.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>World-space point (as seen from the world root) at chainage s and lateral offset.</summary>
        public static Vector3 RoadPoint(double s, double lateral, float y = 0f)
        {
            PathDef.RoadToWorld(s, lateral, out double x, out double z);
            return new Vector3((float)x, y, (float)z);
        }

        /// <summary>Wet land, a pure function of the story's wetness (so seeking works); called every frame by the
        /// bootstrap's world driver, writes only when the wetness changed. Wet asphalt is darker and nearly a mirror
        /// (water fills the texture: its normals and roughness detail fade); wet grass, soil and gravel darken and gain
        /// a little sheen.</summary>
        public static void Tick(in StoryState s)
        {
            if (roadMat == null) return;
            float w = Mathf.Clamp01(s.wetness);
            if (Mathf.Abs(w - lastWet) < 0.002f) return;
            lastWet = w;
            roadMat.SetFloat(idSmoothMin, Mathf.Lerp(0f, Tune.F("land.wetSmoothMin", 0.68f), w));
            roadMat.SetFloat(idSmoothMax, Mathf.Lerp(1f, 0.97f, w));
            float dark = Mathf.Lerp(1f, Tune.F("land.wetDarken", 0.55f), w);
            roadMat.SetColor(idBase, new Color(dark, dark, dark, 1f).gamma);
            roadMat.SetFloat(idNormalScale, Mathf.Lerp(1f, 0.45f, w));
            roadMat.SetFloat(idDetailSmooth, Mathf.Lerp(0.6f, 0.15f, w));
            roadMat.SetFloat(idDetailNormal, Mathf.Lerp(0.7f, 0.3f, w));
            Wet(vergeMat, vergeBase, vergeRemap, w, 0.78f, 0.22f);
            Wet(gravelMat, gravelBase, gravelRemap, w, 0.7f, 0.35f);
            for (int i = 0; i < 4; i++) Wet(fieldMats[i], fieldBase[i], fieldRemap[i], w, 0.8f, 0.15f);
            foreach (WetEntry e in extraWet) Wet(e.m, e.baseGamma, e.remap, w, e.darken, e.sheen);
        }

        struct WetEntry { public Material m; public Color baseGamma; public Vector2 remap; public float darken, sheen; }
        static readonly List<WetEntry> extraWet = new List<WetEntry>();

        /// <summary>Lets a prop's ground material (a material instance with a mask map) get wet with the land:
        /// darker by 'darken' and smoother by 'sheen' at full wetness.</summary>
        public static void RegisterWet(Material m, float darken, float sheen)
        {
            if (m == null) return;
            extraWet.Add(new WetEntry
            {
                m = m, baseGamma = m.GetColor(idBase), remap = new Vector2(m.GetFloat(idSmoothMin), m.GetFloat(idSmoothMax)), darken = darken, sheen = sheen
            });
            lastWet = -1f;
        }

        static void Wet(Material m, Color baseGamma, Vector2 remap, float w, float darken, float sheen)
        {
            if (m == null) return;
            float k = Mathf.Lerp(1f, darken, w);
            Color c = baseGamma.linear * k;
            m.SetColor(idBase, new Color(c.r, c.g, c.b, 1f).gamma);
            m.SetFloat(idSmoothMin, remap.x + sheen * w);
            m.SetFloat(idSmoothMax, Mathf.Min(1f, remap.y + sheen * w));
        }
    }
}
