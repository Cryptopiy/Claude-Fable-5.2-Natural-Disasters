using System.Collections.Generic;
using UnityEngine;

namespace MESOCYCLONE
{
    /// <summary>Procedural ice and blob meshes, built once at load. Hail stones are icospheres displaced by low-frequency
    /// noise and protuberance lobes (big hail is rarely round: oblate, knobby, sometimes spiky aggregates); chips are
    /// faceted shards and flakes. Every mesh is centred and has a mean diameter of 1, so the instance scale is the
    /// diameter in metres.</summary>
    public static class IceMesh
    {
        public const int ShapeCount = 5;

        public struct Shape
        {
            public float noiseAmp, noiseFreq, oblate, elong, lobeAmp, lobeSharp;
            public int lobes;
            public uint seed;
        }

        /// <summary>0 near round (golf ball), 1 oblate and knobby, 2 spiky aggregate (giant hail), 3 egg, 4 bulgy cluster.</summary>
        public static readonly Shape[] Shapes =
        {
            new Shape { noiseAmp = 0.05f, noiseFreq = 2.2f, oblate = 0.93f, elong = 1.00f, lobes = 3, lobeAmp = 0.05f, lobeSharp = 6f, seed = 11 },
            new Shape { noiseAmp = 0.06f, noiseFreq = 3.0f, oblate = 0.72f, elong = 1.05f, lobes = 7, lobeAmp = 0.07f, lobeSharp = 16f, seed = 23 },
            new Shape { noiseAmp = 0.05f, noiseFreq = 2.6f, oblate = 0.86f, elong = 1.00f, lobes = 10, lobeAmp = 0.16f, lobeSharp = 26f, seed = 37 },
            new Shape { noiseAmp = 0.07f, noiseFreq = 2.0f, oblate = 0.88f, elong = 1.28f, lobes = 2, lobeAmp = 0.06f, lobeSharp = 5f, seed = 41 },
            new Shape { noiseAmp = 0.11f, noiseFreq = 1.8f, oblate = 0.80f, elong = 1.10f, lobes = 4, lobeAmp = 0.14f, lobeSharp = 3.5f, seed = 53 },
        };

        /// <summary>A hail stone: spherical UVs over the whole ice texture, seam split so the texture wraps cleanly.</summary>
        public static Mesh Stone(int shape, int subdiv, string name)
        {
            return Lumpy(Shapes[shape % ShapeCount], subdiv, new Rect(0f, 0f, 1f, 1f), true, name);
        }

        /// <summary>A lumpy blob of mean diameter <paramref name="size"/>. spherical = true: spherical UVs with a split seam
        /// (whole texture); false: planar UVs from above into uvRect (an atlas cell; no seam, slight stretch on the sides
        /// of noisy textures).</summary>
        public static Mesh Lumpy(Shape s, int subdiv, Rect uvRect, bool spherical, string name, float size = 1f)
        {
            var dirs = new List<Vector3>(); var tris = new List<int>();
            Icosahedron(dirs, tris);
            var cache = new Dictionary<long, int>();
            for (int k = 0; k < subdiv; k++) Subdivide(dirs, tris, cache);

            var rng = new Rng(s.seed);
            var lobeDir = new Vector3[s.lobes]; var lobeA = new float[s.lobes]; var lobeK = new float[s.lobes];
            for (int i = 0; i < s.lobes; i++)
            {
                lobeDir[i] = RandomDir(ref rng);
                lobeA[i] = s.lobeAmp * rng.Range(0.5f, 1.2f);
                lobeK[i] = s.lobeSharp * rng.Range(0.7f, 1.3f);
            }
            var off = new Vector3(rng.Range(0f, 40f), rng.Range(0f, 40f), rng.Range(0f, 40f));

            int nv = dirs.Count;
            var pos = new Vector3[nv];
            float mean = 0f;
            for (int i = 0; i < nv; i++)
            {
                Vector3 d = dirs[i];
                float r = 1f + s.noiseAmp * (Noise3(d * s.noiseFreq + off) * 2f - 1f)
                             + 0.4f * s.noiseAmp * (Noise3(d * s.noiseFreq * 2.3f + off * 1.7f) * 2f - 1f);
                for (int j = 0; j < s.lobes; j++)
                {
                    float c = Vector3.Dot(d, lobeDir[j]);
                    if (c > 0f) r += lobeA[j] * Mathf.Pow(c, lobeK[j]);
                }
                Vector3 p = d * r;
                p.y *= s.oblate; p.z *= s.elong;
                pos[i] = p;
                mean += p.magnitude;
            }
            float scale = 0.5f / (mean / nv);
            for (int i = 0; i < nv; i++) pos[i] *= scale;      // unit size here: planar atlas UVs are taken before the final scale

            // winding: outward for every triangle (the shapes are star-shaped around the centre)
            for (int t = 0; t < tris.Count; t += 3)
            {
                Vector3 a = pos[tris[t]], b = pos[tris[t + 1]], c = pos[tris[t + 2]];
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), a + b + c) < 0f) { int tmp = tris[t + 1]; tris[t + 1] = tris[t + 2]; tris[t + 2] = tmp; }
            }

            // smooth normals (area weighted)
            var nrm = new Vector3[nv];
            for (int t = 0; t < tris.Count; t += 3)
            {
                Vector3 a = pos[tris[t]], b = pos[tris[t + 1]], c = pos[tris[t + 2]];
                Vector3 fn = Vector3.Cross(b - a, c - a);
                nrm[tris[t]] += fn; nrm[tris[t + 1]] += fn; nrm[tris[t + 2]] += fn;
            }

            var vList = new List<Vector3>(nv + 64); var nList = new List<Vector3>(nv + 64); var uvList = new List<Vector2>(nv + 64);
            for (int i = 0; i < nv; i++)
            {
                vList.Add(pos[i] * size); nList.Add(nrm[i].normalized);
                Vector2 uv;
                if (spherical)
                {
                    Vector3 d = dirs[i];
                    uv = new Vector2(0.5f + Mathf.Atan2(d.z, d.x) / (2f * Mathf.PI), 0.5f + Mathf.Asin(Mathf.Clamp(d.y, -1f, 1f)) / Mathf.PI);
                }
                else uv = new Vector2(Mathf.Clamp01(pos[i].x + 0.5f), Mathf.Clamp01(pos[i].z + 0.5f));
                uvList.Add(new Vector2(uvRect.x + uvRect.width * uv.x, uvRect.y + uvRect.height * uv.y));
            }

            if (spherical)
            {
                // split the seam: a triangle that spans more than half the texture gets copies of its low-u vertices at u + 1
                var dup = new Dictionary<int, int>();
                for (int t = 0; t < tris.Count; t += 3)
                {
                    float u0 = uvList[tris[t]].x, u1 = uvList[tris[t + 1]].x, u2 = uvList[tris[t + 2]].x;
                    if (Mathf.Max(u0, Mathf.Max(u1, u2)) - Mathf.Min(u0, Mathf.Min(u1, u2)) <= 0.5f) continue;
                    for (int e = 0; e < 3; e++)
                    {
                        int vi = tris[t + e];
                        if (uvList[vi].x >= 0.5f) continue;
                        if (!dup.TryGetValue(vi, out int copy))
                        {
                            copy = vList.Count;
                            vList.Add(vList[vi]); nList.Add(nList[vi]); uvList.Add(new Vector2(uvList[vi].x + 1f, uvList[vi].y));
                            dup[vi] = copy;
                        }
                        tris[t + e] = copy;
                    }
                }
            }

            var m = new Mesh { name = name };
            m.SetVertices(vList); m.SetNormals(nList); m.SetUVs(0, uvList);
            m.SetTriangles(tris, 0, true);
            m.RecalculateTangents();
            m.UploadMeshData(true);          // no CPU copy needed after load
            return m;
        }

        /// <summary>An ice chip: a jittered octahedron, flat shaded so each facet catches the headlights on its own.
        /// flake = a thin plate, else an elongated shard.</summary>
        public static Mesh Chip(uint seed, bool flake, string name)
        {
            var rng = new Rng(seed);
            var v = new Vector3[6];
            Vector3[] axes = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            for (int i = 0; i < 6; i++)
            {
                Vector3 p = axes[i] * rng.Range(0.6f, 1.25f) + new Vector3(rng.Range(-0.18f, 0.18f), rng.Range(-0.18f, 0.18f), rng.Range(-0.18f, 0.18f));
                if (flake) p.y *= 0.28f; else p.z *= 1.7f;
                v[i] = p * 0.5f;
            }
            int[] f = { 2, 4, 0, 2, 0, 5, 2, 5, 1, 2, 1, 4, 3, 0, 4, 3, 5, 0, 3, 1, 5, 3, 4, 1 };
            var mb = new MeshBuilder();
            for (int i = 0; i < f.Length; i += 3)
            {
                Vector3 a = v[f[i]], b = v[f[i + 1]], c = v[f[i + 2]];
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), a + b + c) < 0f) { Vector3 tmp = b; b = c; c = tmp; }
                mb.Tri(a, b, c);
            }
            Mesh m = mb.ToMesh(name);
            m.UploadMeshData(true);
            return m;
        }

        // ------------------------------------------------------------------ geometry helpers

        static void Icosahedron(List<Vector3> v, List<int> t)
        {
            float p = (1f + Mathf.Sqrt(5f)) * 0.5f;
            Vector3[] raw =
            {
                new Vector3(-1, p, 0), new Vector3(1, p, 0), new Vector3(-1, -p, 0), new Vector3(1, -p, 0),
                new Vector3(0, -1, p), new Vector3(0, 1, p), new Vector3(0, -1, -p), new Vector3(0, 1, -p),
                new Vector3(p, 0, -1), new Vector3(p, 0, 1), new Vector3(-p, 0, -1), new Vector3(-p, 0, 1)
            };
            // a slight tilt keeps vertices off the poles (spherical UVs are undefined there)
            Quaternion tilt = Quaternion.Euler(13f, 7f, 5f);
            foreach (Vector3 r in raw) v.Add(tilt * r.normalized);
            int[] f =
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1
            };
            t.AddRange(f);
        }

        static void Subdivide(List<Vector3> v, List<int> t, Dictionary<long, int> cache)
        {
            cache.Clear();
            var nt = new List<int>(t.Count * 4);
            for (int i = 0; i < t.Count; i += 3)
            {
                int a = t[i], b = t[i + 1], c = t[i + 2];
                int ab = Mid(v, cache, a, b), bc = Mid(v, cache, b, c), ca = Mid(v, cache, c, a);
                nt.Add(a); nt.Add(ab); nt.Add(ca);
                nt.Add(b); nt.Add(bc); nt.Add(ab);
                nt.Add(c); nt.Add(ca); nt.Add(bc);
                nt.Add(ab); nt.Add(bc); nt.Add(ca);
            }
            t.Clear(); t.AddRange(nt);
        }

        static int Mid(List<Vector3> v, Dictionary<long, int> cache, int a, int b)
        {
            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            if (cache.TryGetValue(key, out int i)) return i;
            v.Add(((v[a] + v[b]) * 0.5f).normalized);
            cache[key] = v.Count - 1;
            return v.Count - 1;
        }

        static Vector3 RandomDir(ref Rng r)
        {
            float z = r.Range(-1f, 1f), a = r.Range(0f, Mathf.PI * 2f), s = Mathf.Sqrt(Mathf.Max(0f, 1f - z * z));
            return new Vector3(s * Mathf.Cos(a), z, s * Mathf.Sin(a));
        }

        static float L(int x, int y, int z) => CoreLibrary.Hash(x + z * 157, y - z * 61, 9001);

        /// <summary>3D value noise 0..1 (smooth trilinear).</summary>
        public static float Noise3(Vector3 p)
        {
            int xi = Mathf.FloorToInt(p.x), yi = Mathf.FloorToInt(p.y), zi = Mathf.FloorToInt(p.z);
            float fx = p.x - xi, fy = p.y - yi, fz = p.z - zi;
            fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy); fz = fz * fz * (3f - 2f * fz);
            float a = Mathf.Lerp(L(xi, yi, zi), L(xi + 1, yi, zi), fx), b = Mathf.Lerp(L(xi, yi + 1, zi), L(xi + 1, yi + 1, zi), fx);
            float c = Mathf.Lerp(L(xi, yi, zi + 1), L(xi + 1, yi, zi + 1), fx), d = Mathf.Lerp(L(xi, yi + 1, zi + 1), L(xi + 1, yi + 1, zi + 1), fx);
            return Mathf.Lerp(Mathf.Lerp(a, b, fy), Mathf.Lerp(c, d, fy), fz);
        }
    }
}
