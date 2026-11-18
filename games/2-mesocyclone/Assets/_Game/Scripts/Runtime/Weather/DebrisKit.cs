using UnityEngine;

namespace MESOCYCLONE
{
    /// <summary>Meshes of the things a tornado and a gust front throw: leaves, twigs, a leafy branch, shingles,
    /// insulation, vinyl siding, planks, cornstalks, straw, grit, roofing felt, grass, OSB, and the corrugated roof
    /// sheet. Real sizes in metres, centred on the origin, textured from the debris atlas (WeatherLibrary).</summary>
    public static class DebrisKit
    {
        public const int LeafGreen = 0, LeafDry = 1, Twig = 2, Branch = 3, Shingle = 4, Insulation = 5, Siding = 6, Plank = 7,
            Cornstalk = 8, CornLeaf = 9, Straw = 10, Grit = 11, Felt = 12, Grass = 13, Osb = 14;
        public const int KindCount = 15;

        public static Mesh Build(int kind, uint seed)
        {
            var rng = new Rng(seed);
            var mb = new MeshBuilder();
            string name = "Debris" + kind;
            switch (kind)
            {
                case LeafGreen:
                case LeafDry:
                    Leaf(mb, kind == LeafGreen ? WeatherLibrary.LeafGreen : WeatherLibrary.LeafDry, 0.10f, 0.075f, 0.16f, 0.25f);
                    break;
                case Twig:
                    TwigPath(mb, ref rng, new Vector3(0f, 0f, -0.22f), Vector3.forward, 0.45f, 0.008f, 4, 5);
                    TwigPath(mb, ref rng, new Vector3(0f, 0f, -0.02f), new Vector3(0.7f, 0.2f, 1f), 0.16f, 0.004f, 2, 4);
                    break;
                case Branch:
                {
                    TwigPath(mb, ref rng, new Vector3(0f, 0f, -0.55f), Vector3.forward, 1.1f, 0.018f, 6, 6);
                    for (int i = 0; i < 3; i++)
                    {
                        var at = new Vector3(0f, 0f, -0.3f + i * 0.3f);
                        var dir = new Vector3(rng.Sign * rng.Range(0.5f, 1f), rng.Range(-0.2f, 0.4f), 1f);
                        Vector3 tip = TwigPath(mb, ref rng, at, dir, rng.Range(0.25f, 0.4f), 0.007f, 3, 4);
                        for (int k = 0; k < 4; k++)
                        {
                            mb.Push(tip + new Vector3(rng.Range(-0.06f, 0.06f), rng.Range(-0.04f, 0.04f), rng.Range(-0.12f, 0.02f)),
                                Quaternion.Euler(rng.Range(-60f, 60f), rng.Range(0f, 360f), rng.Range(-40f, 40f)));
                            Leaf(mb, WeatherLibrary.LeafGreen, rng.Range(0.08f, 0.12f), 0.07f, 0.14f, 0.2f);
                            mb.Reset();
                        }
                    }
                    break;
                }
                case Shingle:
                    // a torn three-tab shingle, slightly bent
                    Strip(mb, WeatherLibrary.Shingle, 0.34f, 0.26f, 0.02f, 3);
                    mb.uvRect = WeatherLibrary.Cell(WeatherLibrary.Shingle);
                    mb.Box(new Vector3(0f, -0.002f, 0f), new Vector3(0.34f, 0.003f, 0.26f), 8);
                    break;
                case Insulation:
                {
                    // a torn fragment of a fibreglass batt, about 0.4 m across
                    var s = new IceMesh.Shape { noiseAmp = 0.22f, noiseFreq = 1.6f, oblate = 0.35f, elong = 0.8f, lobes = 3, lobeAmp = 0.2f, lobeSharp = 3f, seed = seed };
                    return IceMesh.Lumpy(s, 2, WeatherLibrary.Cell(WeatherLibrary.Insulation), false, name, 0.4f);
                }
                case Siding:
                {
                    mb.uvRect = WeatherLibrary.Cell(WeatherLibrary.Siding);
                    // two laps of vinyl siding: a saw-tooth profile (z = depth, y = height) swept along x
                    var prof = new[] { new Vector2(0.012f, -0.1f), new Vector2(0f, 0f), new Vector2(0.012f, 0f), new Vector2(0f, 0.1f) };
                    mb.ExtrudeX(prof, -0.6f, 0.6f, false);
                    break;
                }
                case Plank:
                    mb.uvRect = WeatherLibrary.Cell(rng.Chance(0.5f) ? WeatherLibrary.Plank : WeatherLibrary.BarnBoard);
                    mb.Box(Vector3.zero, new Vector3(1.8f, 0.025f, 0.14f));
                    break;
                case Cornstalk:
                {
                    mb.uvRect = WeatherLibrary.Cell(WeatherLibrary.Cornstalk);
                    mb.Cylinder(new Vector3(0f, 0f, -0.8f), new Vector3(0f, 0f, 0.8f), 0.012f, 0.007f, 5, false);
                    for (int i = 0; i < 3; i++)
                    {
                        // a blade growing out of a node, drooping
                        mb.Push(new Vector3(0f, 0f, -0.4f + i * 0.4f), Quaternion.Euler(rng.Range(-30f, 30f), rng.Range(60f, 120f) * rng.Sign, 0f));
                        mb.xf = mb.xf * Matrix4x4.Translate(new Vector3(0f, 0f, 0.32f));
                        Leaf(mb, WeatherLibrary.CornLeaf, 0.65f, 0.07f, 0.1f, -0.3f);
                        mb.Reset();
                    }
                    break;
                }
                case CornLeaf:
                    Leaf(mb, WeatherLibrary.CornLeaf, 0.7f, 0.075f, 0.12f, -0.35f);
                    break;
                case Straw:
                    mb.uvRect = WeatherLibrary.Cell(WeatherLibrary.Straw);
                    for (int i = 0; i < 4; i++)
                    {
                        Vector3 d = new Vector3(rng.Range(-0.3f, 0.3f), rng.Range(-0.2f, 0.2f), 1f).normalized * rng.Range(0.1f, 0.15f);
                        Vector3 o = new Vector3(rng.Range(-0.01f, 0.01f), rng.Range(-0.01f, 0.01f), 0f);
                        mb.Cylinder(o - d, o + d, 0.0025f, 0.002f, 3, false);
                    }
                    break;
                case Grit:
                {
                    var s = new IceMesh.Shape { noiseAmp = 0.2f, noiseFreq = 2.2f, oblate = 0.7f, elong = 1.2f, lobes = 2, lobeAmp = 0.2f, lobeSharp = 4f, seed = seed };
                    return IceMesh.Lumpy(s, 1, WeatherLibrary.Cell(WeatherLibrary.Dirt), false, name);    // diameter 1: scaled per instance
                }
                case Felt:
                    Strip(mb, WeatherLibrary.Felt, 0.45f, 0.3f, 0.08f, 4);
                    break;
                case Grass:
                    mb.uvRect = WeatherLibrary.Cell(WeatherLibrary.Grass);
                    for (int i = 0; i < 3; i++)
                    {
                        float a = i * 60f * Mathf.Deg2Rad;
                        var h = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.13f;
                        mb.QuadUV(new Vector3(-h.x, -0.15f, -h.z), new Vector3(-h.x, 0.15f, -h.z), new Vector3(h.x, 0.15f, h.z), new Vector3(h.x, -0.15f, h.z),
                            new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f));
                    }
                    break;
                default:   // Osb
                    mb.uvRect = WeatherLibrary.Cell(WeatherLibrary.Osb);
                    mb.Box(Vector3.zero, new Vector3(0.36f, 0.011f, 0.26f));
                    break;
            }
            Mesh mesh = mb.ToMesh(name);
            mesh.UploadMeshData(true);
            return mesh;
        }

        /// <summary>Corrugated roof sheet, 3.2 x 1.4 m, galvanised with rust runs, slightly bent by the wind.</summary>
        public static Mesh RoofSheet()
        {
            var mb = new MeshBuilder { uvRect = WeatherLibrary.Cell(WeatherLibrary.Galvanized) };
            const int pts = 41;
            var prof = new Vector2[pts];
            for (int i = 0; i < pts; i++)
            {
                float z = Mathf.Lerp(0.7f, -0.7f, i / (float)(pts - 1));        // front to back: the top surface faces up
                prof[i] = new Vector2(z, 0.018f * Mathf.Sin(z * Mathf.PI * 2f * 7.1f));
            }
            mb.ExtrudeX(prof, -1.6f, 1.6f, true);
            for (int i = 0; i < mb.verts.Count; i++)
            {
                Vector3 p = mb.verts[i];
                p.y += 0.10f * Mathf.Sin(p.x * 1.1f) + 0.12f * (p.x / 1.6f) * (p.x / 1.6f) * (p.z + 0.7f);   // torn corner curled up
                mb.verts[i] = p;
            }
            Mesh m = mb.ToMesh("RoofSheet");
            m.RecalculateNormals();
            m.RecalculateTangents();
            m.UploadMeshData(true);
            return m;
        }

        // ------------------------------------------------------------------ parts

        /// <summary>A leaf: a strip folded along its midrib (V section) and curled along its length, lying along z.
        /// The atlas alpha cuts the outline.</summary>
        public static void Leaf(MeshBuilder mb, int cell, float len, float width, float fold, float curl)
        {
            mb.uvRect = WeatherLibrary.Cell(cell);
            const int seg = 4;
            for (int k = 0; k < seg; k++)
            {
                float v0 = (float)k / seg, v1 = (float)(k + 1) / seg;
                Vector3 l0 = LeafPt(-1f, v0, len, width, fold, curl), m0 = LeafPt(0f, v0, len, width, fold, curl), r0 = LeafPt(1f, v0, len, width, fold, curl);
                Vector3 l1 = LeafPt(-1f, v1, len, width, fold, curl), m1 = LeafPt(0f, v1, len, width, fold, curl), r1 = LeafPt(1f, v1, len, width, fold, curl);
                mb.QuadUV(l0, l1, m1, m0, new Vector2(0f, v0), new Vector2(0f, v1), new Vector2(0.5f, v1), new Vector2(0.5f, v0));
                mb.QuadUV(m0, m1, r1, r0, new Vector2(0.5f, v0), new Vector2(0.5f, v1), new Vector2(1f, v1), new Vector2(1f, v0));
            }
        }

        static Vector3 LeafPt(float side, float v, float len, float width, float fold, float curl)
        {
            float z = (v - 0.5f) * len;
            float y = Mathf.Abs(side) * fold * width + curl * len * (v - 0.5f) * (v - 0.5f);
            return new Vector3(side * width * 0.5f, y, z);
        }

        /// <summary>A flat sheet (felt, shingle) with a gentle bend across x.</summary>
        static void Strip(MeshBuilder mb, int cell, float lx, float lz, float bend, int seg)
        {
            mb.uvRect = WeatherLibrary.Cell(cell);
            for (int k = 0; k < seg; k++)
            {
                float u0 = (float)k / seg, u1 = (float)(k + 1) / seg;
                float x0 = (u0 - 0.5f) * lx, x1 = (u1 - 0.5f) * lx;
                float y0 = bend * (u0 - 0.5f) * (u0 - 0.5f) * 4f, y1 = bend * (u1 - 0.5f) * (u1 - 0.5f) * 4f;
                mb.QuadUV(new Vector3(x0, y0, -lz * 0.5f), new Vector3(x0, y0, lz * 0.5f), new Vector3(x1, y1, lz * 0.5f), new Vector3(x1, y1, -lz * 0.5f),
                    new Vector2(u0, 0f), new Vector2(u0, 1f), new Vector2(u1, 1f), new Vector2(u1, 0f));
            }
        }

        /// <summary>A bent, tapering twig from start along dir; returns the tip.</summary>
        static Vector3 TwigPath(MeshBuilder mb, ref Rng rng, Vector3 start, Vector3 dir, float len, float r0, int segs, int sides)
        {
            mb.uvRect = WeatherLibrary.Cell(WeatherLibrary.Bark);
            Vector3 p = start, d = dir.normalized;
            float segLen = len / segs;
            for (int k = 0; k < segs; k++)
            {
                Vector3 nd = (d + new Vector3(rng.Range(-0.25f, 0.25f), rng.Range(-0.25f, 0.25f), rng.Range(-0.1f, 0.1f))).normalized;
                Vector3 q = p + nd * segLen;
                float ra = Mathf.Lerp(r0, r0 * 0.35f, (float)k / segs), rb = Mathf.Lerp(r0, r0 * 0.35f, (float)(k + 1) / segs);
                mb.Cylinder(p, q, ra, rb, sides, k == segs - 1);
                p = q; d = nd;
            }
            return p;
        }
    }
}
