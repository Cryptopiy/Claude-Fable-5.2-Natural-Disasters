using System.Collections.Generic;
using UnityEngine;

namespace CASCADE
{
    /// <summary>
    /// Dressing pieces of the path architecture: turned iron (lathe), fluted shafts, slabs, plaster surrounds, louvred
    /// shutters, planting and pots, the timber park bench, the wayside shrine (kandylaki), the coin viewer, scuppers.
    /// World space, build time only; every face is oriented by a hint (ArchGeo.Quad / Tri).
    /// </summary>
    internal static class ArchDetail
    {
        static readonly Vector3 Up = ArchGeo.Up;
        static readonly List<Vector3> tmp = new List<Vector3>(16);
        static readonly List<Vector3> bv = new List<Vector3>(64), bn = new List<Vector3>(64);
        static readonly List<int> bt = new List<int>(256);

        /// <summary>Unit radial direction at angle a: the same basis as MeshBuilder.Cylinder on a vertical axis.</summary>
        public static Vector3 Rad(float a) { return new Vector3(-Mathf.Sin(a), 0f, -Mathf.Cos(a)); }

        /// <summary>Horizontal ring (disc when r0 = 0) at c, facing up or down; vertices line up with Cylinder's.</summary>
        public static void Annulus(MeshBuilder mb, Material m, Vector3 c, float r0, float r1, int sides, bool up)
        {
            Vector3 hint = up ? Up : -Up;
            for (int i = 0; i < sides; i++)
            {
                Vector3 d0 = Rad(i * Mathf.PI * 2f / sides), d1 = Rad((i + 1) * Mathf.PI * 2f / sides);
                if (r0 < 1e-4f) ArchGeo.Tri(mb, m, c, c + d0 * r1, c + d1 * r1, hint);
                else ArchGeo.Quad(mb, m, c + d0 * r0, c + d1 * r0, c + d1 * r1, c + d0 * r1, hint);
            }
        }

        /// <summary>Turned vertical piece standing on b: (height, radius) pairs from the bottom up. Two equal heights make a
        /// horizontal ledge (facing up where the radius shrinks); capTop closes the last radius with a disc.</summary>
        public static void Lathe(MeshBuilder mb, Material m, Vector3 b, float[] hr, int sides, bool capTop = true)
        {
            int n = hr.Length / 2;
            for (int i = 0; i + 1 < n; i++)
            {
                float y0 = hr[2 * i], r0 = hr[2 * i + 1], y1 = hr[2 * i + 2], r1 = hr[2 * i + 3];
                if (y1 - y0 > 1e-4f) mb.Cylinder(m, b + Up * y0, b + Up * y1, r0, r1, sides, false);
                else if (Mathf.Abs(r1 - r0) > 1e-4f) Annulus(mb, m, b + Up * y0, Mathf.Min(r0, r1), Mathf.Max(r0, r1), sides, r1 < r0);
            }
            float rt = hr[hr.Length - 1];
            if (capTop && rt > 1e-4f) Annulus(mb, m, b + Up * hr[hr.Length - 2], 0f, rt, sides, true);
        }

        /// <summary>Vertical fluted shaft: 2 x flutes flat facets alternating between the full and 82 % radius.</summary>
        public static void Fluted(MeshBuilder mb, Material m, Vector3 a, float h, float ra, float rb, int flutes)
        {
            int n = flutes * 2;
            Vector3 b = a + Up * h;
            for (int i = 0; i < n; i++)
            {
                float k0 = (i & 1) == 0 ? 1f : 0.82f, k1 = (i & 1) == 0 ? 0.82f : 1f;
                Vector3 d0 = Rad(i * Mathf.PI * 2f / n), d1 = Rad((i + 1) * Mathf.PI * 2f / n);
                ArchGeo.Quad(mb, m, a + d0 * (ra * k0), a + d1 * (ra * k1), b + d1 * (rb * k1), b + d0 * (rb * k0), d0 + d1);
            }
        }

        /// <summary>Prism over a closed ground-plan loop (4 corners, any order around) from y0 to y1; the bottom is optional.</summary>
        public static void Slab(MeshBuilder mb, Material m, Vector3 a, Vector3 b, Vector3 c, Vector3 d, float y0, float y1, bool bottom)
        {
            Vector3 cen = (a + b + c + d) * 0.25f;
            ArchGeo.Quad(mb, m, ArchGeo.Y(a, y1), ArchGeo.Y(b, y1), ArchGeo.Y(c, y1), ArchGeo.Y(d, y1), Up);
            if (bottom) ArchGeo.Quad(mb, m, ArchGeo.Y(a, y0), ArchGeo.Y(b, y0), ArchGeo.Y(c, y0), ArchGeo.Y(d, y0), -Up);
            Side(mb, m, a, b, y0, y1, cen); Side(mb, m, b, c, y0, y1, cen); Side(mb, m, c, d, y0, y1, cen); Side(mb, m, d, a, y0, y1, cen);
        }

        static void Side(MeshBuilder mb, Material m, Vector3 p, Vector3 q, float y0, float y1, Vector3 cen)
        {
            ArchGeo.VQuad(mb, m, p, q, y0, y1, ArchGeo.Flat((p + q) * 0.5f - cen));
        }

        /// <summary>Raised plaster surround of a wall opening. o = bottom centre of the opening on the wall face, u = along
        /// the wall, n = out of the wall. Jambs reach down to yL / yR (absolute) at the two sides; optional sill.</summary>
        public static void Surround(MeshBuilder mb, Material m, Vector3 o, Vector3 u, Vector3 n, float w, float h, float bw, float proud, bool sill, float yL, float yR)
        {
            // every piece starts at the wall plane (nothing coplanar with the reveals of the recess)
            Quaternion rot = Quaternion.LookRotation(n, Vector3.up);
            Vector3 zc = n * (proud * 0.5f);
            float jx = w * 0.5f + bw * 0.5f, top = o.y + h;
            Vector3 l = o - u * jx, r = o + u * jx;
            mb.Box(m, ArchGeo.Y(l, (yL + top) * 0.5f) + zc, new Vector3(bw, top - yL, proud), rot, 4 | 8 | 32);
            mb.Box(m, ArchGeo.Y(r, (yR + top) * 0.5f) + zc, new Vector3(bw, top - yR, proud), rot, 4 | 8 | 32);
            mb.Box(m, o + Up * (h + bw * 0.5f) + zc, new Vector3(w + 2f * bw, bw, proud), rot, 32);
            mb.Box(m, o + Up * (h + bw + 0.02f) + n * ((proud + 0.025f) * 0.5f), new Vector3(w + 2f * bw + 0.1f, 0.04f, proud + 0.025f), rot, 32);   // drip moulding
            if (sill) mb.Box(m, o - Up * 0.03f + n * ((proud + 0.035f) * 0.5f), new Vector3(w + 2f * bw + 0.08f, 0.06f, proud + 0.035f), rot, 32);
        }

        /// <summary>Louvred timber shutter leaf (w x h): a 3.5 cm board, a frame and horizontal slats, all on the face plane
        /// through c (centre of the back of the leaf) facing n.</summary>
        public static void Shutter(MeshBuilder mb, Material m, Vector3 c, Vector3 n, float w, float h)
        {
            Quaternion rot = Quaternion.LookRotation(n, Vector3.up);
            Vector3 u = rot * Vector3.right;
            mb.Box(m, c + n * 0.0175f, new Vector3(w, h, 0.035f), rot, 32);
            const float fw = 0.055f, fd = 0.024f;
            Vector3 z = n * (0.035f + fd * 0.5f);
            mb.Box(m, c + z + u * (w * 0.5f - fw * 0.5f), new Vector3(fw, h, fd), rot, 32);
            mb.Box(m, c + z - u * (w * 0.5f - fw * 0.5f), new Vector3(fw, h, fd), rot, 32);
            mb.Box(m, c + z + Up * (h * 0.5f - fw * 0.5f), new Vector3(w - 2f * fw, fw, fd), rot, 32 | 1 | 2);
            mb.Box(m, c + z - Up * (h * 0.5f - fw * 0.5f), new Vector3(w - 2f * fw, fw, fd), rot, 32 | 1 | 2);
            float span = h - 2f * fw;
            int k = Mathf.Max(2, Mathf.FloorToInt(span / 0.075f));
            for (int i = 0; i < k; i++)
            {
                float y = -span * 0.5f + span * (i + 0.5f) / k;
                mb.Box(m, c + n * (0.035f + 0.011f) + Up * y, new Vector3(w - 2f * fw, 0.03f, 0.022f), rot * Quaternion.Euler(-14f, 0f, 0f), 32 | 1 | 2);
            }
        }

        /// <summary>Leafy mass (low-poly ellipsoid with a little vertex noise, smooth normals), world-axis radii.</summary>
        public static void Blob(MeshBuilder mb, Material m, Vector3 c, float rx, float ry, float rz, System.Random rng, float noise = 0.18f, int seg = 7, int rings = 4)
        {
            Blob(mb, m, c, new Vector3(rx, 0f, 0f), new Vector3(0f, ry, 0f), new Vector3(0f, 0f, rz), rng, noise, seg, rings);
        }

        /// <summary>
        /// Leafy mass spanned by three half-axis vectors (hy should point up): a cluster of lumpy lobes (a shrub silhouette,
        /// not one smooth gem). Its own seeded random, so the caller's stream is not disturbed. Within VKit.CardRange of the
        /// path the lobes get shells of leaf cards over smaller, darker solid cores (VKit.LeafMix; cards from a third stream).
        /// </summary>
        public static void Blob(MeshBuilder mb, Material m, Vector3 c, Vector3 hx, Vector3 hy, Vector3 hz, System.Random rng, float noise = 0.18f, int seg = 6, int rings = 4)
        {
            unchecked
            {
                int seed = (int)(c.x * 73.13f) * 92821 ^ (int)(c.y * 41.7f) * 68917 ^ (int)(c.z * 59.3f) * 12289;
                var lr = new System.Random(seed);
                int mix = VKit.LeafMix(m, c);
                Material solid = mix == VKit.MixNone ? m : VKit.CoreOf(mix);
                float k0 = mix == VKit.MixNone ? 1f : VKit.CoreShrink;
                System.Random cr = mix == VKit.MixNone ? null : new System.Random(seed ^ 0x5EED1EAF);
                BlobOne(mb, solid, c, hx * (0.72f * k0), hy * (0.72f * k0), hz * (0.72f * k0), lr, Mathf.Max(noise, 0.28f), Mathf.Max(seg, 8), Mathf.Max(rings, 5));
                if (cr != null) VKit.LeafCards(mb, mix, c, hx * 0.72f, hy * 0.72f, hz * 0.72f, c, hx, hy, hz, cr);
                int n = 4 + lr.Next(4);
                for (int i = 0; i < n; i++)
                {
                    float a = (float)lr.NextDouble() * Mathf.PI * 2f;
                    float ry = -0.25f + (float)lr.NextDouble() * 0.95f;
                    Vector3 off = hx * (Mathf.Cos(a) * 0.48f) + hz * (Mathf.Sin(a) * 0.48f) + hy * (ry * 0.5f);
                    float k = 0.36f + (float)lr.NextDouble() * 0.22f;
                    BlobOne(mb, solid, c + off, hx * (k * k0), hy * (k * k0), hz * (k * k0), lr, 0.3f, 7, 4);
                    if (cr != null) VKit.LeafCards(mb, mix, c + off, hx * k, hy * k, hz * k, c, hx, hy, hz, cr);
                }
            }
        }

        static void BlobOne(MeshBuilder mb, Material m, Vector3 c, Vector3 hx, Vector3 hy, Vector3 hz, System.Random rng, float noise = 0.18f, int seg = 7, int rings = 4)
        {
            bv.Clear(); bn.Clear(); bt.Clear();
            Vector3 nx = hx.normalized, ny = hy.normalized, nz = hz.normalized;
            float lx = Mathf.Max(hx.magnitude, 1e-4f), ly = Mathf.Max(hy.magnitude, 1e-4f), lz = Mathf.Max(hz.magnitude, 1e-4f);
            for (int r = 0; r <= rings; r++)
            {
                float el = -Mathf.PI * 0.5f + Mathf.PI * r / rings;
                float cy = Mathf.Sin(el), cr = Mathf.Cos(el);
                for (int s = 0; s <= seg; s++)
                {
                    float az = s * Mathf.PI * 2f / seg;
                    float k = (r == 0 || r == rings || s == seg) ? 1f : 1f + noise * (2f * (float)rng.NextDouble() - 1f);
                    float dx = Mathf.Cos(az) * cr, dz = Mathf.Sin(az) * cr;
                    bv.Add(c + (hx * dx + hy * cy + hz * dz) * k);
                    bn.Add((nx * (dx / lx) + ny * (cy / ly) + nz * (dz / lz)).normalized);
                }
            }
            for (int r = 0; r <= rings; r++) bv[r * (seg + 1) + seg] = bv[r * (seg + 1)];
            bool flip = Vector3.Dot(Vector3.Cross(hz, hx), hy) < 0f;
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < seg; s++)
                {
                    int p = r * (seg + 1) + s, q = p + seg + 1;
                    if (flip) { bt.Add(p); bt.Add(q + 1); bt.Add(q); bt.Add(p); bt.Add(p + 1); bt.Add(q + 1); }
                    else { bt.Add(p); bt.Add(q); bt.Add(q + 1); bt.Add(p); bt.Add(q + 1); bt.Add(p + 1); }
                }
            mb.Raw(m, bv, bn, null, bt);
        }

        /// <summary>Terracotta pot (rim at 0.32 x scale) with a plant, optionally flowering bougainvillea, standing on p.</summary>
        public static void Pot(MeshBuilder mb, ArchCtx c, Vector3 p, float scale, System.Random rng, bool flower)
        {
            float s = scale;
            Lathe(mb, c.Terracotta, p, new[] { 0f, 0.11f * s, 0.27f * s, 0.165f * s, 0.27f * s, 0.182f * s, 0.32f * s, 0.182f * s, 0.32f * s, 0.15f * s }, 8, false);
            float pr = (0.2f + 0.1f * (float)rng.NextDouble()) * s, ph = (0.2f + 0.18f * (float)rng.NextDouble()) * s;
            Blob(mb, c.Plant, p + Up * (0.3f * s + ph * 0.55f), pr, ph, pr, rng, 0.22f, 7, 4);
            if (flower)
            {
                float a = (float)rng.NextDouble() * 6.28f;
                Vector3 off = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * pr * 0.35f;
                Blob(mb, c.Boug, p + off + Up * (0.3f * s + ph * 1.05f), pr * 0.75f, ph * 0.55f, pr * 0.75f, rng, 0.28f, 6, 3);
            }
        }

        /// <summary>Square whitewashed masonry planter (0.7 m) with a plaster rim, shrubs and bougainvillea, standing on g.</summary>
        public static void Planter(MeshBuilder mb, ArchCtx c, Vector3 g, Vector3 fwd, System.Random rng)
        {
            Quaternion rot = Quaternion.LookRotation(ArchGeo.Flat(fwd).normalized, Vector3.up);
            mb.Box(c.White, g + Up * 0.2f, new Vector3(0.7f, 0.6f, 0.7f), rot, 8);              // -0.1 .. 0.5
            mb.Box(c.Trim, g + Up * 0.53f, new Vector3(0.78f, 0.06f, 0.78f), rot, 0);            // rim 0.5 .. 0.56
            Blob(mb, c.Plant, g + Up * 0.78f, 0.36f, 0.32f, 0.36f, rng, 0.22f, 8, 4);
            Blob(mb, c.Boug, g + rot * new Vector3(0.12f, 0f, -0.08f) + Up * 1.02f, 0.26f, 0.2f, 0.26f, rng, 0.3f, 7, 3);
        }

        /// <summary>Cast-iron park bench with timber slats and a back, centred on g; the sitter looks along 'face'.</summary>
        public static void ParkBench(MeshBuilder mb, ArchCtx c, Vector3 g, Vector3 face)
        {
            face = ArchGeo.Flat(face).normalized;
            Vector3 along = ArchGeo.Left(face);
            Quaternion rot = Quaternion.LookRotation(face, Vector3.up);
            for (int e = -1; e <= 1; e += 2)
            {
                Vector3 b = g + along * (e * 0.74f);
                mb.Box(c.Iron, b + face * 0.17f + Up * 0.21f, new Vector3(0.045f, 0.46f, 0.05f), rot, 0);           // front leg
                mb.Cylinder(c.Iron, b - face * 0.2f - Up * 0.03f, b - face * 0.25f + Up * 0.88f, 0.024f, 0.021f, 6, true);  // back leg and back post
                mb.Box(c.Iron, b + Up * 0.42f, new Vector3(0.045f, 0.04f, 0.46f), rot, 0);                           // seat bearer
                tmp.Clear();
                tmp.Add(b + face * 0.19f + Up * 0.44f); tmp.Add(b + face * 0.2f + Up * 0.6f);
                tmp.Add(b + face * 0.1f + Up * 0.66f); tmp.Add(b - face * 0.23f + Up * 0.66f);
                mb.Tube(c.Iron, tmp, 0.02f, 5);                                                                       // arm rest
            }
            for (int i = 0; i < 4; i++)
                mb.Box(c.Wood, g + face * (0.15f - i * 0.11f) + Up * 0.457f, new Vector3(1.72f, 0.03f, 0.085f), rot, 0);
            Quaternion back = rot * Quaternion.Euler(-12f, 0f, 0f);
            for (int i = 0; i < 3; i++)
            {
                float y = 0.56f + i * 0.11f;
                mb.Box(c.Wood, g - face * (0.205f + (y - 0.5f) * 0.12f) + Up * y, new Vector3(1.72f, 0.085f, 0.03f), back, 0);
            }
        }

        /// <summary>Kandylaki: a small whitewashed wayside shrine on a pedestal with a blue domed roof, a cross and a lit
        /// votive window, standing on g and facing 'face'.</summary>
        public static void Shrine(MeshBuilder mb, ArchCtx c, Vector3 g, Vector3 face)
        {
            face = ArchGeo.Flat(face).normalized;
            Quaternion rot = Quaternion.LookRotation(face, Vector3.up);
            mb.Box(c.Trim, g + Up * 0.04f, new Vector3(0.36f, 0.16f, 0.36f), rot, 8);              // footing -0.04 .. 0.12
            mb.Box(c.White, g + Up * 0.56f, new Vector3(0.24f, 0.88f, 0.24f), rot, 4 | 8);         // pedestal 0.12 .. 1.0
            mb.Box(c.Trim, g + Up * 1.02f, new Vector3(0.34f, 0.04f, 0.34f), rot, 0);              // cap 1.0 .. 1.04
            mb.Box(c.White, g + Up * 1.25f, new Vector3(0.40f, 0.42f, 0.30f), rot, 8);             // house 1.04 .. 1.46
            mb.Box(c.Trim, g + face * 0.15f + Up * 1.24f, new Vector3(0.26f, 0.30f, 0.05f), rot, 32);
            mb.Box(c.LampB, g + face * 0.16f + Up * 1.24f, new Vector3(0.17f, 0.21f, 0.07f), rot, 32);   // votive glass, 2 cm proud of the frame
            mb.Box(c.Trim, g + Up * 1.48f, new Vector3(0.46f, 0.04f, 0.36f), rot, 0);              // cornice 1.46 .. 1.50
            mb.Dome(c.DomeBlue, g + Up * 1.5f, 0.155f, 0.95f, 10, 3);
            mb.Box(c.Iron, g + Up * 1.71f, new Vector3(0.022f, 0.17f, 0.022f), rot, 8);
            mb.Box(c.Iron, g + Up * 1.74f, new Vector3(0.1f, 0.022f, 0.022f), rot, 0);
        }

        /// <summary>Coin-operated viewer on a turned iron pedestal at g, looking along 'face'.</summary>
        public static void Viewer(MeshBuilder mb, ArchCtx c, Vector3 g, Vector3 face)
        {
            face = ArchGeo.Flat(face).normalized;
            Lathe(mb, c.Iron, g, new[] { -0.05f, 0.16f, 0.05f, 0.16f, 0.05f, 0.1f, 0.12f, 0.07f, 1.0f, 0.045f, 1.0f, 0.07f, 1.06f, 0.07f }, 8, true);
            Quaternion rot = Quaternion.LookRotation(face, Vector3.up) * Quaternion.Euler(-6f, 0f, 0f);
            Vector3 body = g + Up * 1.2f;
            mb.Box(c.Iron, body, new Vector3(0.36f, 0.24f, 0.3f), rot, 0);
            mb.Box(c.Iron, g + Up * 1.09f, new Vector3(0.1f, 0.06f, 0.1f), rot, 0);
            Vector3 r = rot * Vector3.right, f = rot * Vector3.forward;
            for (int e = -1; e <= 1; e += 2)
            {
                mb.Cylinder(c.Iron, body + r * (e * 0.09f) + f * 0.14f, body + r * (e * 0.09f) + f * 0.26f, 0.055f, 0.06f, 8, true);     // objectives
                mb.Cylinder(c.Iron, body + r * (e * 0.06f) - f * 0.14f, body + r * (e * 0.06f) - f * 0.22f, 0.035f, 0.04f, 6, true);     // eyepieces
            }
        }

        /// <summary>Scupper of a wall on a drop: a small iron grille on the inner face at the foot (3 cm proud) and a stone
        /// spout projecting from the outer face. Feet at walking level; outward points from the path through the wall.</summary>
        public static void Drain(MeshBuilder mb, ArchCtx c, Vector3 innerFoot, Vector3 outerFoot, Vector3 outward)
        {
            Quaternion rot = Quaternion.LookRotation(outward, Vector3.up);
            mb.Box(c.Iron, innerFoot + Up * 0.065f - outward * 0.005f, new Vector3(0.22f, 0.11f, 0.05f), rot, 8);
            mb.Box(c.Pumice, outerFoot + Up * 0.035f + outward * 0.1f, new Vector3(0.13f, 0.08f, 0.34f), rot, 0);
        }

        /// <summary>Square pier with a plaster cap where a coped wall ends (g = foot on the walking level).</summary>
        public static void Pier(MeshBuilder mb, ArchCtx c, Vector3 g, Vector3 fwd, float size, float h, float below)
        {
            Quaternion rot = Quaternion.LookRotation(ArchGeo.Flat(fwd).normalized, Vector3.up);
            mb.Box(c.White, g + Up * ((h - below) * 0.5f), new Vector3(size, h + below, size), rot, 4 | 8);
            mb.Box(c.Trim, g + Up * (h + 0.035f), new Vector3(size + 0.07f, 0.07f, size + 0.07f), rot, 0);
            mb.Box(c.Trim, g + Up * (h + 0.1f), new Vector3(size * 0.62f, 0.06f, size * 0.62f), rot, 8);
        }
    }
}
