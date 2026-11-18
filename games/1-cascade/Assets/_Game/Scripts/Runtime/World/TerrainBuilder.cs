using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

namespace CASCADE
{
    /// <summary>
    /// Ground of the island. The near region (village, bay, headland) is baked once into a 2 m height field
    /// (worker threads) that feeds both the LOD chunk meshes and the texture the water shader reads for depth,
    /// shore foam and run-up. The rest of the island ring is one coarse mesh.
    /// </summary>
    public static class TerrainBuilder
    {
        public const float X0 = -1300f, Z0 = -900f, Cell = 2f;
        public const int W = 851, H = 1201;                 // 1700 m x 2400 m
        const int Chunk = 64;                               // cells per chunk side (128 m)
        public static float[] Ht, Sd, Rim;
        static float[] Arc;                                 // along-shore coordinate of each cell (strata u)
        public static Texture2D GroundTex;

        // the strata mask repeats every ~1.2 km along the shore (T_Strata spans that width), a whole number of times round the ring
        const float StrataRepeatTarget = 1208f;
        static float strataRepeat = StrataRepeatTarget;
        /// <summary>Lerp of two shore arcs the short way round the ring.</summary>
        static float ArcLerp(float a, float b, float t)
        {
            float len = WorldGen.ShoreLength, d = b - a;
            if (d > 0.5f * len) d -= len; else if (d < -0.5f * len) d += len;
            float r = a + d * t;
            return r < 0f ? r + len : r >= len ? r - len : r;
        }

        static void InitStrataRepeat()
        {
            float len = WorldGen.ShoreLength;
            strataRepeat = len > StrataRepeatTarget ? len / Mathf.Round(len / StrataRepeatTarget) : StrataRepeatTarget;
        }

        // the far ring mesh: a 50 m grid, kept so that FarHeight can return the surface that is actually drawn
        public const float FarX0 = -11600f, FarZ0 = -5600f, FarCell = 50f;
        public const int FarW = 305, FarH = 221;
        static float[] farHs;

        static Material[] mats;                             // land (4 rock/earth layers), sea floor (4 layers)
        static List<Vector2> uv1;

        public static void Bake()
        {
            Ht = new float[W * H];
            Sd = new float[W * H];
            Rim = new float[W * H];
            Arc = new float[W * H];
            InitStrataRepeat();
            Parallel.For(0, H, j =>
            {
                float z = Z0 + j * Cell;
                for (int i = 0; i < W; i++)
                {
                    float sd;
                    Ht[j * W + i] = Village.QuayGround(X0 + i * Cell, z, WorldGen.Sample(X0 + i * Cell, z, out sd));
                    Sd[j * W + i] = sd;
                    Rim[j * W + i] = (i % 4 == 0 && j % 4 == 0) ? WorldGen.RimHeight(X0 + i * Cell, z) : 0f;
                    // the shore arc is smooth (soft-blended across ridges): every other cell, the rest interpolated
                    Arc[j * W + i] = (i % 2 == 0 && j % 2 == 0) ? WorldGen.ShoreArc(X0 + i * Cell, z) : 0f;
                }
            });
            Parallel.For(0, H, j =>
            {
                int j0 = (j / 4) * 4, j1 = Mathf.Min(j0 + 4, H - 1); j1 -= j1 % 4; if (j1 < j0) j1 = j0;
                float v = j1 > j0 ? (j - j0) / (float)(j1 - j0) : 0f;
                int k0 = j & ~1, k1 = Mathf.Min(k0 + 2, H - 1);                // W and H are odd: the even grid reaches both edges
                float va = k1 > k0 ? (j - k0) * 0.5f : 0f;
                for (int i = 0; i < W; i++)
                {
                    if (i % 2 != 0 || j % 2 != 0)
                    {
                        int m0 = i & ~1, m1 = Mathf.Min(m0 + 2, W - 1);
                        float ua = m1 > m0 ? (i - m0) * 0.5f : 0f;
                        Arc[j * W + i] = ArcLerp(ArcLerp(Arc[k0 * W + m0], Arc[k0 * W + m1], ua), ArcLerp(Arc[k1 * W + m0], Arc[k1 * W + m1], ua), va);
                    }
                    if (i % 4 == 0 && j % 4 == 0) continue;
                    int i0 = (i / 4) * 4, i1 = Mathf.Min(i0 + 4, W - 1); i1 -= i1 % 4; if (i1 < i0) i1 = i0;
                    float u = i1 > i0 ? (i - i0) / (float)(i1 - i0) : 0f;
                    Rim[j * W + i] = Mathf.Lerp(Mathf.Lerp(Rim[j0 * W + i0], Rim[j0 * W + i1], u), Mathf.Lerp(Rim[j1 * W + i0], Rim[j1 * W + i1], u), v);
                }
            });
            var data = new float[W * H * 2];
            for (int k = 0; k < W * H; k++) { data[k * 2] = Ht[k]; data[k * 2 + 1] = Mathf.Clamp(Sd[k], -2000f, 2000f); }
            GroundTex = new Texture2D(W, H, TextureFormat.RGFloat, false, true) { name = "GroundBake", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            GroundTex.SetPixelData(data, 0);
            GroundTex.Apply(false, true);
            Shader.SetGlobalTexture("_CTerrainTex", GroundTex);
            BakeSkyline();
            Shader.SetGlobalVector("_CTerrainRect", new Vector4(X0 - Cell * 0.5f, Z0 - Cell * 0.5f, 1f / (W * Cell), 1f / (H * Cell)));
        }

        /// <summary>The skyline around the bay: for 512 azimuths seen from the bay centre, the highest elevation of the
        /// cliffs or the cone (A = sine of it), its distance in km (G) and its albedo (R). The sea reflects it.</summary>
        static void BakeSkyline()
        {
            const int N = 512;
            var px = new Color[N];
            Vector2 c = new Vector2(-150f, 150f);
            Parallel.For(0, N, k =>
            {
                float az = (k + 0.5f) / N * Mathf.PI * 2f - Mathf.PI;
                Vector2 d = new Vector2(Mathf.Cos(az), Mathf.Sin(az));
                float best = -1f, bestDist = 12f, bestAlb = 0.08f;
                for (float t = 60f; t < 14000f; t *= 1.02f)
                {
                    Vector2 q = c + d * t;
                    float h = WorldGen.Height(q.x, q.y);
                    float rc = Vector2.Distance(q, new Vector2(Config.ConeCenter.x, Config.ConeCenter.z));
                    float hc = Config.ConeHeight * Mathf.Pow(Mathf.Clamp01(1f - rc / Config.ConeBaseRadius), 1.85f);
                    bool cone = hc > h;
                    h = Mathf.Max(h, hc);
                    float sinE = h / Mathf.Sqrt(h * h + t * t);
                    if (sinE > best) { best = sinE; bestDist = t / 1000f; bestAlb = cone ? 0.07f : 0.10f; }
                }
                px[k] = new Color(bestAlb, bestDist, 0f, Mathf.Max(best, 0f));
            });
            var tex = new Texture2D(N, 1, TextureFormat.RGBAHalf, false, true) { name = "Skyline", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            tex.SetPixels(px);
            tex.Apply(false, true);
            Shader.SetGlobalTexture("_CSkyline", tex);
        }

        /// <summary>Bilinear ground height from the bake; falls back to the exact function outside the baked rectangle.</summary>
        public static float Height(float x, float z)
        {
            float fx = (x - X0) / Cell, fz = (z - Z0) / Cell;
            if (Ht == null || fx < 0f || fz < 0f || fx >= W - 1 || fz >= H - 1) return WorldGen.Height(x, z);
            int i = (int)fx, j = (int)fz;
            float u = fx - i, v = fz - j;
            int k = j * W + i;
            return Mathf.Lerp(Mathf.Lerp(Ht[k], Ht[k + 1], u), Mathf.Lerp(Ht[k + W], Ht[k + W + 1], u), v);
        }

        static Vector3 NormalAt(int i, int j, int step)
        {
            int i0 = Mathf.Max(i - step, 0), i1 = Mathf.Min(i + step, W - 1), j0 = Mathf.Max(j - step, 0), j1 = Mathf.Min(j + step, H - 1);
            float dx = (Ht[j * W + i1] - Ht[j * W + i0]) / ((i1 - i0) * Cell);
            float dz = (Ht[j1 * W + i] - Ht[j0 * W + i]) / ((j1 - j0) * Cell);
            return new Vector3(-dx, 1f, -dz).normalized;
        }

        /// <summary>True where the ground belongs to the sea-floor material (below the waterline, not a cliff face).</summary>
        static bool IsSeaFloor(float y, float ny, float sd) { return y < 0.6f && sd < 3f && ny >= 0.6f; }

        static float Pulse(float x, float a, float b, float w)
        {
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a - w, a + w, x)) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(b - w, b + w, x)));
        }

        /// <summary>Layer weights as vertex colour: land R red scoria strata, G pale tuff, B earth (the rest is dark lava rock);
        /// sea floor R weedy rocks, G sand, B rock outcrops (the rest is silt). Smooth per vertex: no stepped edges.
        /// farK (0..1) blends in the far ring's cliff rule (FarNyEarth..FarApron1; BuildFar ramps it in away from the near terrain).</summary>
        static Color32 Weights(float x, float y, float z, float ny, float sd, float farK = 0f)
        {
            float n = WorldGen.Fbm(x / 60f, z / 60f, 2);
            if (IsSeaFloor(y, ny, sd))
            {
                float rocks = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.42f, 0.55f, n)) * Pulse(y, -30f, -1.5f, 3f);
                float sand = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-9f, -2f, y)) * (1f - rocks);
                float outcrop = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.86f, 0.70f, ny));
                return new Color32((byte)(rocks * 255f), (byte)(sand * 255f), (byte)(outcrop * 255f), 255);
            }
            // strata live in the layer mask (UV1 = depth below the rim, per pixel); the vertex colour only says where
            // the ground is a cliff face (R = G = 1: the strata show) and where it is earth and scree (B)
            float steep = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.72f, 0.52f, ny));
            if (farK > 0f)
            {
                // The far walls rise 70-320 m over 260 m (16-50 degrees), and the 50 m grid's normals average 100 m, so the
                // near rule (2 m cells, beds only past ~44 degrees) left every far wall earth: a beige dune with the towns lost
                // on it. Here faces of 33 degrees and more show their beds, 16-33 degrees blend into scree aprons, and the
                // beach below 6 m (dark rock) and the gentle outer slopes keep their own look.
                float far = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(FarNyEarth, FarNyCliff, ny)) * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(FarApron0, FarApron1, y));
                steep = Mathf.Lerp(steep, far, farK);
            }
            float earthK = (1f - steep) * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 6f, y));
            return new Color32((byte)(steep * 255f), (byte)(steep * 255f), (byte)(earthK * 255f), 255);
#pragma warning disable CS0162
            float earth = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.64f, 0.82f, ny)) * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 6f, y));
            float nl = WorldGen.Fbm(x / 420f, z / 420f, 2);                       // the beds undulate gently over hundreds of metres
            float yw = y + 9f * Mathf.Sin(y * 0.061f + nl * 3f) + 6f * Mathf.Sin(y * 0.137f + 1.3f);   // beds of unequal thickness
            float band = Mathf.Repeat(yw * 0.024f + nl * 0.45f + n * 0.06f, 1f);  // thick strata: dark lava, rust scoria, pale tuff
            float red = Pulse(band, 0.42f, 0.70f, 0.05f);
            float pale = Pulse(band, 0.80f, 0.93f, 0.04f);
            float band2 = Mathf.Repeat(y * 0.083f + nl * 0.9f + 0.3f, 1f);         // thin seams inside the lava
            pale = Mathf.Max(pale, 0.55f * Pulse(band2, 0.45f, 0.53f, 0.03f));
            red = Mathf.Max(red, 0.5f * Pulse(band2, 0.78f, 0.88f, 0.04f));
            float rock = 1f - earth;
            return new Color32((byte)(red * rock * 255f), (byte)(pale * rock * 255f), (byte)(earth * 255f), 255);
#pragma warning restore CS0162
        }

        /// <summary>UV1 of the land: u = along-shore distance in strata repeats (T_Strata spans ~1.2 km: faults, pinching beds,
        /// pumice gullies), v = depth below the rim crest (0..600 m -> 0..1), tilted and warped a little so the beds dip.
        /// u is the arc length of the nearest shore point rather than the azimuth round the cone: the headland points at
        /// the cone, and an azimuth would barely change along its bay face (stretching every gully five-fold there).</summary>
        static Vector2 StrataUV(float x, float y, float z, float rim, float arc)
        {
            float d = rim - y + 0.04f * x - 0.03f * z + 18f * WorldGen.Fbm(x / 300f, z / 300f, 2);
            return new Vector2(arc / strataRepeat, d / 600f);
        }

        // ---- the far ring's cliffs (BuildFar). Both blends are 0 at the near terrain's edge, so the beds meet it without a seam ----
        const float FarNyEarth = 0.96f, FarNyCliff = 0.84f;    // grid-normal y: earth above 0.96 (16 deg), full beds below 0.84 (33 deg)
        const float FarApron0 = 6f, FarApron1 = 25f;           // m: the beds start above this scree apron at the wall's foot
        const float FarRule0 = 50f, FarRule1 = 450f;           // m outside the near rectangle over which the far cliff rule takes over
        const float FarWarp0 = 150f, FarWarp1 = 1600f;         // ... and over which the period breaker fades in (a gentle ramp: no squeeze)
        const float FarWarpCell = 3000f, FarWarpU = 0.7f;      // along-shore warp of u: noise cell (m of shore) and amplitude (repeats);
                                                               // its slope stays under ~60 % of du/ds, so the mask never folds back
        const float FarDipCell = 1900f, FarDipM = 80f;         // along-shore dip of the beds: noise cell and peak-to-peak depth shift (m)

        /// <summary>Distance (m) of a point outside the near terrain's rectangle (0 inside).</summary>
        static float OutsideNear(float x, float z)
        {
            float nearX1 = X0 + (W - 1) * Cell, nearZ1 = Z0 + (H - 1) * Cell;
            float dx = Mathf.Max(Mathf.Max(X0 - x, x - nearX1), 0f), dz = Mathf.Max(Mathf.Max(Z0 - z, z - nearZ1), 0f);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>
        /// UV offset that breaks the strata mask's 1.2 km period on the far walls, where 3-6 repeats share one view: u is
        /// warped slowly along the shore (neighbouring repeats drift by up to ~400 m) and the beds dip gently along it
        /// (+-40 m over a few km, a few degrees), so the same fault, fan or dyke never comes back at the same spacing and height.
        /// A function of the shore arc only, sampled on a circle so it closes round the ring: features stay upright.
        /// </summary>
        static Vector2 FarStrataWarp(float arc)
        {
            float len = Mathf.Max(WorldGen.ShoreLength, 1f), a = arc / len * 2f * Mathf.PI, ca = Mathf.Cos(a), sa = Mathf.Sin(a);
            float ru = len / (2f * Mathf.PI * FarWarpCell), rv = len / (2f * Mathf.PI * FarDipCell);
            float wu = (WorldGen.Fbm(ru * ca + 5.1f, ru * sa + 2.7f, 2) - 0.5f) * FarWarpU;
            float wv = (WorldGen.Fbm(rv * ca + 17.3f, rv * sa + 9.1f, 2) - 0.5f) * FarDipM / 600f;
            return new Vector2(wu, wv);
        }

        public static IEnumerator Build(Transform parent)
        {
            var a = GameAssets.I;
            mats = new[] { a.Mat("M_Land"), a.Mat("M_SeaFloor") };
            var root = new GameObject("Terrain");
            root.transform.SetParent(parent, false);

            // distance from each chunk to the path decides its resolution
            var pathPts = new List<Vector2>();
            for (float s = 0f; s <= PathDef.Length; s += 8f) { Vector3 p = PathDef.Pos(s); pathPts.Add(new Vector2(p.x, p.z)); }
            int nx = (W - 1 + Chunk - 1) / Chunk, nz = (H - 1 + Chunk - 1) / Chunk;
            var verts = new List<Vector3>(6000); var norms = new List<Vector3>(6000); var cols = new List<Color32>(6000);
            uv1 = new List<Vector2>(6000);
            var tris = new List<int>[2];
            for (int m = 0; m < 2; m++) tris[m] = new List<int>(8000);
            int built = 0;
            for (int cz = 0; cz < nz; cz++)
            {
                for (int cx = 0; cx < nx; cx++)
                {
                    int i0 = cx * Chunk, j0 = cz * Chunk, i1 = Mathf.Min(i0 + Chunk, W - 1), j1 = Mathf.Min(j0 + Chunk, H - 1);
                    float maxH = -1e9f;
                    for (int j = j0; j <= j1; j += 4) for (int i = i0; i <= i1; i += 4) maxH = Mathf.Max(maxH, Ht[j * W + i]);
                    if (maxH < -44f) continue;                                   // never exposed, always under opaque water
                    Vector2 c = new Vector2(X0 + (i0 + i1) * 0.5f * Cell, Z0 + (j0 + j1) * 0.5f * Cell);
                    float dPath = 1e9f;
                    for (int k = 0; k < pathPts.Count; k++) dPath = Mathf.Min(dPath, (pathPts[k] - c).sqrMagnitude);
                    dPath = Mathf.Sqrt(dPath);
                    int step = dPath < 210f ? 1 : dPath < 520f ? 2 : 4;
                    BuildChunk(root.transform, i0, j0, i1, j1, step, dPath < 700f, verts, norms, cols, tris);
                    built++;
                }
                if ((cz & 3) == 3) yield return null;
            }
            yield return null;
            BuildFar(root.transform);
            Debug.Log("CASCADE_INFO terrain chunks " + built);
        }

        static void BuildChunk(Transform parent, int i0, int j0, int i1, int j1, int step, bool shadows, List<Vector3> verts, List<Vector3> norms, List<Color32> cols, List<int>[] tris)
        {
            verts.Clear(); norms.Clear(); cols.Clear(); uv1.Clear();
            for (int m = 0; m < 2; m++) tris[m].Clear();
            int nc = (i1 - i0) / step + 1, rows = (j1 - j0) / step + 1;
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < nc; c++)
                {
                    int i = i0 + c * step, j = j0 + r * step;
                    float x = X0 + i * Cell, z = Z0 + j * Cell, y = Ht[j * W + i];
                    verts.Add(new Vector3(x, y, z));
                    norms.Add(NormalAt(i, j, step));
                    cols.Add(Weights(x, y, z, NormalAt(i, j, 3).y, Sd[j * W + i]));
                    uv1.Add(StrataUV(x, y, z, Rim[j * W + i], Arc[j * W + i]));
                }
            for (int r = 0; r < rows - 1; r++)
                for (int c = 0; c < nc - 1; c++)
                {
                    int v0 = r * nc + c, v1 = v0 + 1, v2 = v0 + nc, v3 = v2 + 1;
                    Vector3 p = (verts[v0] + verts[v3]) * 0.5f;
                    int i = i0 + c * step, j = j0 + r * step;
                    float ny = NormalAt(i, j, 6).y;
                    var t = tris[IsSeaFloor(p.y, ny, Sd[j * W + i]) ? 1 : 0];
                    t.Add(v0); t.Add(v2); t.Add(v1); t.Add(v1); t.Add(v2); t.Add(v3);
                }
            // skirts hide the cracks between chunks of different resolution
            float drop = 3f + 3f * step;
            Skirt(verts, norms, cols, tris[0], nc, rows, 0, 1, nc, drop, false);                    // south edge (r = 0)
            Skirt(verts, norms, cols, tris[0], nc, rows, (rows - 1) * nc, 1, nc, drop, true);       // north edge
            Skirt(verts, norms, cols, tris[0], nc, rows, 0, nc, rows, drop, true);                  // west edge (c = 0)
            Skirt(verts, norms, cols, tris[0], nc, rows, nc - 1, nc, rows, drop, false);            // east edge

            var mesh = new Mesh { name = "TerrainChunk" };
            mesh.SetVertices(verts); mesh.SetNormals(norms); mesh.SetColors(cols); mesh.SetUVs(0, uv1);
            var used = new List<Material>(2);
            int sub = 0;
            for (int m = 0; m < 2; m++) if (tris[m].Count > 0) sub++;
            mesh.subMeshCount = sub;
            sub = 0;
            for (int m = 0; m < 2; m++)
            {
                if (tris[m].Count == 0) continue;
                mesh.SetTriangles(tris[m], sub++, false);
                used.Add(mats[m]);
            }
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            var go = new GameObject("T");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = used.ToArray();
            mr.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        static void Skirt(List<Vector3> verts, List<Vector3> norms, List<Color32> cols, List<int> tris, int nc, int rows, int start, int stride, int count, float drop, bool flip)
        {
            int baseIndex = verts.Count;
            for (int k = 0; k < count; k++)
            {
                int v = start + k * stride;
                verts.Add(verts[v] + Vector3.down * drop);
                norms.Add(norms[v]);
                cols.Add(cols[v]);
                uv1.Add(uv1[v]);
            }
            for (int k = 0; k < count - 1; k++)
            {
                int a = start + k * stride, b = start + (k + 1) * stride, c = baseIndex + k, d = baseIndex + k + 1;
                if (flip) { tris.Add(a); tris.Add(b); tris.Add(c); tris.Add(b); tris.Add(d); tris.Add(c); }
                else { tris.Add(a); tris.Add(c); tris.Add(b); tris.Add(b); tris.Add(c); tris.Add(d); }
            }
        }

        /// <summary>
        /// Height of the far ring mesh exactly as drawn: the 50 m grid split along the same diagonal as its triangles
        /// (v1-v2, i.e. from (i+1, j) to (i, j+1)). Things seated on the far ring (the far villages) use it, so they
        /// neither float nor sink where the coarse mesh departs from WorldGen.Height. Outside the grid, or before
        /// the far mesh is baked, it falls back to the exact function.
        /// </summary>
        public static float FarHeight(float x, float z)
        {
            float fx = (x - FarX0) / FarCell, fz = (z - FarZ0) / FarCell;
            if (farHs == null || fx < 0f || fz < 0f || fx >= FarW - 1 || fz >= FarH - 1) return WorldGen.Height(x, z);
            int i = (int)fx, j = (int)fz;
            float u = fx - i, v = fz - j;
            int k = j * FarW + i;
            float h0 = farHs[k], h1 = farHs[k + 1], h2 = farHs[k + FarW], h3 = farHs[k + FarW + 1];
            return u + v <= 1f ? h0 + (h1 - h0) * u + (h2 - h0) * v : h3 + (h2 - h3) * (1f - u) + (h1 - h3) * (1f - v);
        }

        /// <summary>Fills the far height grid (and the rim heights and shore arcs its vertices need). Pure, thread-parallel.</summary>
        public static void BakeFar(float[] rims, float[] arcs)
        {
            float nearX1 = X0 + (W - 1) * Cell, nearZ1 = Z0 + (H - 1) * Cell;
            var hs = new float[FarW * FarH];
            InitStrataRepeat();
            Parallel.For(0, FarH, j =>
            {
                float z = FarZ0 + j * FarCell;
                for (int i = 0; i < FarW; i++)
                {
                    float x = FarX0 + i * FarCell;
                    float h = WorldGen.Height(x, z);
                    float inset = Mathf.Min(Mathf.Min(x - X0, nearX1 - x), Mathf.Min(z - Z0, nearZ1 - z));
                    if (inset > 0f) h -= 14f * Mathf.SmoothStep(0f, 1f, inset / 70f);       // dive under the detailed near terrain
                    hs[j * FarW + i] = h;
                    if (rims != null) rims[j * FarW + i] = WorldGen.RimHeight(x, z);
                    if (arcs != null) arcs[j * FarW + i] = WorldGen.ShoreArc(x, z);
                }
            });
            farHs = hs;
        }

        /// <summary>The rest of the island ring at 50 m: far cliffs, far rim, the outer slopes.</summary>
        static void BuildFar(Transform parent)
        {
            const float fx0 = FarX0, fz0 = FarZ0, fc = FarCell;
            const int fw = FarW, fh = FarH;
            float nearX1 = X0 + (W - 1) * Cell, nearZ1 = Z0 + (H - 1) * Cell;
            var rims = new float[fw * fh];
            var arcs = new float[fw * fh];
            BakeFar(rims, arcs);
            float[] hs = farHs;
            var verts = new List<Vector3>(fw * fh); var norms = new List<Vector3>(fw * fh); var cols = new List<Color32>(fw * fh);
            var fuv = new List<Vector2>(fw * fh);
            for (int j = 0; j < fh; j++)
                for (int i = 0; i < fw; i++)
                {
                    float x = fx0 + i * fc, z = fz0 + j * fc, y = hs[j * fw + i];
                    verts.Add(new Vector3(x, y, z));
                    int ia = Mathf.Max(i - 1, 0), ib = Mathf.Min(i + 1, fw - 1), ja = Mathf.Max(j - 1, 0), jb = Mathf.Min(j + 1, fh - 1);
                    Vector3 nn = new Vector3(-(hs[j * fw + ib] - hs[j * fw + ia]) / ((ib - ia) * fc), 1f, -(hs[jb * fw + i] - hs[ja * fw + i]) / ((jb - ja) * fc)).normalized;
                    norms.Add(nn);
                    float outside = OutsideNear(x, z);
                    float ruleK = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(FarRule0, FarRule1, outside));
                    float warpK = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(FarWarp0, FarWarp1, outside));
                    cols.Add(Weights(x, Mathf.Max(y, 1f), z, nn.y, 100f, ruleK));
                    Vector2 uv = StrataUV(x, y, z, rims[j * fw + i], arcs[j * fw + i]);
                    if (warpK > 0f) uv += FarStrataWarp(arcs[j * fw + i]) * warpK;
                    fuv.Add(uv);
                }
            var tris = new List<int>(120000);
            for (int j = 0; j < fh - 1; j++)
                for (int i = 0; i < fw - 1; i++)
                {
                    int v0 = j * fw + i, v1 = v0 + 1, v2 = v0 + fw, v3 = v2 + 1;
                    float top = Mathf.Max(Mathf.Max(hs[v0], hs[v1]), Mathf.Max(hs[v2], hs[v3]));
                    if (top < -3f) continue;                                         // under the sea
                    Vector3 p = (verts[v0] + verts[v3]) * 0.5f;
                    float inset = Mathf.Min(Mathf.Min(p.x - X0, nearX1 - p.x), Mathf.Min(p.z - Z0, nearZ1 - p.z));
                    if (inset > 110f) continue;                                      // covered by the near terrain
                    tris.Add(v0); tris.Add(v2); tris.Add(v1); tris.Add(v1); tris.Add(v2); tris.Add(v3);
                }
            var mesh = new Mesh { name = "TerrainFar", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(verts); mesh.SetNormals(norms); mesh.SetColors(cols); mesh.SetUVs(0, fuv);
            mesh.SetTriangles(tris, 0, false);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            var go = new GameObject("TerrainFar");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mats[0];
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }
    }
}
