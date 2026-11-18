using System;
using UnityEngine;

namespace MESOCYCLONE
{
    /// <summary>Modelling helper of the cab on top of MeshBuilder: a current frame (origin + axes), an optional
    /// left/right mirror (x -> -x with the winding repaired), automatic orientation of loose quads toward a view
    /// point, metric UVs for the tiling materials and smooth sweeps/lofts with bevelled ends.
    /// Profiles are 2D points (x = frame z, y = frame y), counter-clockwise when drawn with +z to the right and
    /// +y up: a closed profile then faces outward. Sweeps run along the frame's x axis, always from x0 &lt; x1.</summary>
    public sealed class CabMesh
    {
        // material slots (submeshes) of the cab meshes
        public const int Dash = 0, Pad = 1, Leather = 2, Fabric = 3, Liner = 4, Carpet = 5, Gloss = 6, Chrome = 7,
            Paint = 8, Rubber = 9, Face = 10, Glow = 11, Green = 12, Needle = 13, Radio = 14, Plastic = 15, SlotCount = 16;

        public readonly MeshBuilder mb = new MeshBuilder();
        public bool mirror;
        bool metric = true, orient;
        Vector3 toward;
        Vector3 fo = Vector3.zero, fx = Vector3.right, fy = Vector3.up, fz = Vector3.forward;

        // ------------------------------------------------------------------ state
        /// <summary>Material with its own tiling texture or a plain colour: UVs are written in metres.</summary>
        public void Mat(int slot) { mb.SetSub(slot); metric = true; mb.uvRect = new Rect(0f, 0f, 1f, 1f); }

        /// <summary>Swatch-atlas material: every vertex samples the given flat colour.</summary>
        public void Mat(int slot, int swatch) { mb.SetSub(slot); mb.Swatch(swatch); metric = false; }

        public void World() { fo = Vector3.zero; fx = Vector3.right; fy = Vector3.up; fz = Vector3.forward; }

        /// <summary>Frame from an origin, its x axis and an approximate y axis (z = x cross y).</summary>
        public void Frame(Vector3 origin, Vector3 x, Vector3 yHint)
        {
            fo = origin; fx = x.normalized;
            fz = Vector3.Cross(fx, yHint).normalized;
            fy = Vector3.Cross(fz, fx).normalized;
        }

        /// <summary>Loose quads (Q) are turned so that they face this point (world space, unmirrored side).</summary>
        public void Toward(Vector3 worldPoint) { orient = true; toward = worldPoint; }
        public void NoToward() { orient = false; }

        public Vector3 P(Vector3 p) { return fo + fx * p.x + fy * p.y + fz * p.z; }
        public Vector3 P(float x, float y, float z) { return fo + fx * x + fy * y + fz * z; }
        public Vector3 D(Vector3 d) { return fx * d.x + fy * d.y + fz * d.z; }
        Vector3 M(Vector3 p) { return mirror ? new Vector3(-p.x, p.y, p.z) : p; }

        // ------------------------------------------------------------------ raw emitters (world space)
        /// <summary>Unit vector without the 1e-5 cut-off of Vector3.normalized (millimetre-sized faces have cross
        /// products far below it and would get a zero normal).</summary>
        static Vector3 Unit(Vector3 v)
        {
            float m = v.x * v.x + v.y * v.y + v.z * v.z;
            return m > 1e-30f ? v * (1f / Mathf.Sqrt(m)) : Vector3.up;
        }

        /// <summary>Rewrites the flat normal of the n vertices added last from their own positions.</summary>
        void FixFlat(int k, int n)
        {
            Vector3 a = mb.verts[k], b = mb.verts[k + 1], c = mb.verts[k + 2];
            Vector3 nn = Vector3.Cross(b - a, c - a);
            if (nn.sqrMagnitude < 1e-20f && n > 3) nn = Vector3.Cross(c - a, mb.verts[k + 3] - a);
            nn = Unit(nn);
            for (int i = 0; i < n; i++) mb.norms[k + i] = nn;
        }

        void Raw(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            if (metric) mb.uvRect = new Rect(0f, 0f, (d - a).magnitude, (b - a).magnitude);
            int k = mb.verts.Count;
            if (mirror) mb.Quad(M(d), M(c), M(b), M(a)); else mb.Quad(a, b, c, d);
            FixFlat(k, 4);
        }

        void RawTri(Vector3 a, Vector3 b, Vector3 c)
        {
            if (metric) mb.uvRect = new Rect(0f, 0f, 0.02f, 0.02f);
            int k = mb.verts.Count;
            if (mirror) mb.Tri(M(a), M(c), M(b)); else mb.Tri(a, b, c);
            FixFlat(k, 3);
        }

        void SetV(int k, Vector3 n, Vector2 uv)
        {
            if (mirror) n.x = -n.x;
            mb.norms[k] = Unit(n);
            if (metric) mb.uvs[k] = uv;
        }

        void RawN(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 na, Vector3 nb, Vector3 nc, Vector3 nd,
            Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
        {
            int k = mb.verts.Count;
            if (mirror)
            {
                mb.Quad(M(d), M(c), M(b), M(a));
                SetV(k, nd, ud); SetV(k + 1, nc, uc); SetV(k + 2, nb, ub); SetV(k + 3, na, ua);
            }
            else
            {
                mb.Quad(a, b, c, d);
                SetV(k, na, ua); SetV(k + 1, nb, ub); SetV(k + 2, nc, uc); SetV(k + 3, nd, ud);
            }
        }

        void Facing(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 view)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (n.sqrMagnitude < 1e-14f) n = Vector3.Cross(c - a, d - a);
            if (Vector3.Dot(n, view - (a + c) * 0.5f) < 0f) Raw(a, d, c, b); else Raw(a, b, c, d);
        }

        void TriFacing(Vector3 a, Vector3 b, Vector3 c, Vector3 view)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (Vector3.Dot(n, view - a) < 0f) RawTri(a, c, b); else RawTri(a, b, c);
        }

        // ------------------------------------------------------------------ loose faces (frame space)
        /// <summary>Quad in frame space. Faces the Toward point when one is set, otherwise clockwise = front.</summary>
        public void Q(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            a = P(a); b = P(b); c = P(c); d = P(d);
            if (orient) Facing(a, b, c, d, toward); else Raw(a, b, c, d);
        }

        public void Tri(Vector3 a, Vector3 b, Vector3 c)
        {
            a = P(a); b = P(b); c = P(c);
            if (orient) TriFacing(a, b, c, toward); else RawTri(a, b, c);
        }

        /// <summary>Box in frame space. Skip mask as MeshBuilder.Box: 1 -X, 2 +X, 4 -Y, 8 +Y, 16 -Z, 32 +Z.</summary>
        public void Box(Vector3 c, Vector3 size, int skip = 0)
        {
            Vector3 h = size * 0.5f;
            Vector3 p000 = P(c + new Vector3(-h.x, -h.y, -h.z)), p100 = P(c + new Vector3(h.x, -h.y, -h.z));
            Vector3 p010 = P(c + new Vector3(-h.x, h.y, -h.z)), p110 = P(c + new Vector3(h.x, h.y, -h.z));
            Vector3 p001 = P(c + new Vector3(-h.x, -h.y, h.z)), p101 = P(c + new Vector3(h.x, -h.y, h.z));
            Vector3 p011 = P(c + new Vector3(-h.x, h.y, h.z)), p111 = P(c + new Vector3(h.x, h.y, h.z));
            if ((skip & 1) == 0) Raw(p001, p011, p010, p000);
            if ((skip & 2) == 0) Raw(p100, p110, p111, p101);
            if ((skip & 4) == 0) Raw(p001, p000, p100, p101);
            if ((skip & 8) == 0) Raw(p010, p011, p111, p110);
            if ((skip & 16) == 0) Raw(p000, p010, p110, p100);
            if ((skip & 32) == 0) Raw(p101, p111, p011, p001);
        }

        // ------------------------------------------------------------------ flat details on the frame's XY plane
        // They face the viewer who looks along the frame's +z (x to the right, y up).
        public void Flat(float x0, float y0, float x1, float y1, float z)
        {
            Facing(P(x0, y0, z), P(x0, y1, z), P(x1, y1, z), P(x1, y0, z), P((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, z - 1f));
        }

        /// <summary>Radial mark: angle in degrees clockwise from up, from radius r0 to r1, width w.</summary>
        public void Tick(float cx, float cy, float deg, float r0, float r1, float w, float z, float wTip = -1f)
        {
            float a = deg * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(Mathf.Sin(a), Mathf.Cos(a), 0f), side = new Vector3(Mathf.Cos(a), -Mathf.Sin(a), 0f);
            Vector3 c = new Vector3(cx, cy, z);
            float w1 = wTip < 0f ? w : wTip;
            Facing(P(c + dir * r0 - side * (w * 0.5f)), P(c + dir * r1 - side * (w1 * 0.5f)),
                P(c + dir * r1 + side * (w1 * 0.5f)), P(c + dir * r0 + side * (w * 0.5f)), P(cx, cy, z - 1f));
        }

        public void Disc(float cx, float cy, float r, float z, int seg = 16)
        {
            Vector3 c = P(cx, cy, z), view = P(cx, cy, z - 1f);
            for (int i = 0; i < seg; i++)
            {
                float a0 = i * Mathf.PI * 2f / seg, a1 = (i + 1) * Mathf.PI * 2f / seg;
                TriFacing(c, P(cx + Mathf.Cos(a1) * r, cy + Mathf.Sin(a1) * r, z), P(cx + Mathf.Cos(a0) * r, cy + Mathf.Sin(a0) * r, z), view);
            }
        }

        public void RingFlat(float cx, float cy, float r0, float r1, float z, int seg = 24)
        {
            Vector3 view = P(cx, cy, z - 1f);
            for (int i = 0; i < seg; i++)
            {
                float a0 = i * Mathf.PI * 2f / seg, a1 = (i + 1) * Mathf.PI * 2f / seg;
                float c0 = Mathf.Cos(a0), s0 = Mathf.Sin(a0), c1 = Mathf.Cos(a1), s1 = Mathf.Sin(a1);
                Facing(P(cx + c0 * r0, cy + s0 * r0, z), P(cx + c1 * r0, cy + s1 * r0, z),
                    P(cx + c1 * r1, cy + s1 * r1, z), P(cx + c0 * r1, cy + s0 * r1, z), view);
            }
        }

        // ------------------------------------------------------------------ round primitives
        public void Cyl(Vector3 p0, Vector3 p1, float r0, float r1, int seg = 12, bool caps = true)
        {
            Vector3 a = M(P(p0)), b = M(P(p1));
            if (metric) mb.uvRect = new Rect(0f, 0f, Mathf.PI * (r0 + r1), (b - a).magnitude);
            mb.Cylinder(a, b, r0, r1, seg, caps);
        }

        public void Ball(Vector3 c, float r, int seg = 12, int rings = 8, float yScale = 1f)
        {
            if (metric) mb.uvRect = new Rect(0f, 0f, Mathf.PI * 2f * r, Mathf.PI * r);
            mb.Sphere(M(P(c)), r, seg, rings, yScale);
        }

        Matrix4x4 AxisMatrix(Vector3 origin, Vector3 axis, bool axisIsZ)
        {
            Vector3 o = M(P(origin)), k = D(axis).normalized;
            if (mirror) k.x = -k.x;
            Vector3 t = Vector3.Cross(k, Mathf.Abs(k.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 x, y, z;
            if (axisIsZ) { z = k; x = t; y = Vector3.Cross(z, x); }
            else { y = k; x = t; z = Vector3.Cross(x, y); }
            Matrix4x4 m = Matrix4x4.identity;
            m.SetColumn(0, new Vector4(x.x, x.y, x.z, 0f));
            m.SetColumn(1, new Vector4(y.x, y.y, y.z, 0f));
            m.SetColumn(2, new Vector4(z.x, z.y, z.z, 0f));
            m.SetColumn(3, new Vector4(o.x, o.y, o.z, 1f));
            return m;
        }

        /// <summary>Torus at c (frame space) around the given axis (frame space).</summary>
        public void Torus(Vector3 c, Vector3 axis, float R, float r, int segU = 32, int segV = 8)
        {
            mb.xf = AxisMatrix(c, axis, true);
            if (metric) mb.uvRect = new Rect(0f, 0f, Mathf.PI * 2f * R, Mathf.PI * 2f * r);
            mb.Torus(Vector3.zero, R, r, segU, segV);
            mb.Reset();
        }

        /// <summary>Surface of revolution: profile (radius, height along the axis), from the base outward to the tip.</summary>
        public void Lathe(Vector2[] profile, Vector3 origin, Vector3 axis, int seg = 16)
        {
            mb.xf = AxisMatrix(origin, axis, false);
            if (metric) mb.uvRect = new Rect(0f, 0f, 0.1f, 0.05f);
            mb.Lathe(profile, seg);
            mb.Reset();
        }

        // ------------------------------------------------------------------ profiles
        static Vector2 Perp(Vector2 e) { return new Vector2(e.y, -e.x); }

        /// <summary>Rounded rectangle, counter-clockwise, optionally rotated (degrees, counter-clockwise).</summary>
        public static Vector2[] RR(float zc, float yc, float w, float h, float r, int n = 4, float rotDeg = 0f)
        {
            r = Mathf.Min(r, Mathf.Min(w, h) * 0.5f - 0.0005f);
            var p = new Vector2[4 * (n + 1)];
            float cs = Mathf.Cos(rotDeg * Mathf.Deg2Rad), sn = Mathf.Sin(rotDeg * Mathf.Deg2Rad);
            int k = 0;
            for (int c = 0; c < 4; c++)
            {
                float sx = (c == 0 || c == 3) ? 1f : -1f, sy = c < 2 ? 1f : -1f;
                float ox = sx * (w * 0.5f - r), oy = sy * (h * 0.5f - r);
                for (int i = 0; i <= n; i++)
                {
                    float a = (c + (float)i / n) * Mathf.PI * 0.5f;
                    float x = ox + Mathf.Cos(a) * r, y = oy + Mathf.Sin(a) * r;
                    p[k++] = new Vector2(zc + x * cs - y * sn, yc + x * sn + y * cs);
                }
            }
            return p;
        }

        /// <summary>Closed profile moved inward by d along its vertex normals.</summary>
        public static Vector2[] Inset(Vector2[] p, float d)
        {
            int n = p.Length;
            var q = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                Vector2 e0 = p[i] - p[(i - 1 + n) % n], e1 = p[(i + 1) % n] - p[i];
                if (e0.sqrMagnitude < 1e-12f) e0 = p[i] - p[(i - 2 + n) % n];
                if (e1.sqrMagnitude < 1e-12f) e1 = p[(i + 2) % n] - p[i];
                Vector2 nn = Perp(e0.normalized) + Perp(e1.normalized);
                q[i] = nn.sqrMagnitude < 1e-8f ? p[i] : p[i] - nn.normalized * d;
            }
            return q;
        }

        // ------------------------------------------------------------------ sweeps
        /// <summary>Sweeps a profile along the frame's x axis. Corners sharper than 55 degrees stay creased.</summary>
        public void Sweep(Vector2[] p, float x0, float x1, bool closed = false, bool smooth = true)
        {
            int n = p.Length, segs = closed ? n : n - 1;
            var sn = new Vector2[segs];
            Vector2 last = Vector2.up;
            for (int i = 0; i < segs; i++)
            {
                Vector2 e = p[(i + 1) % n] - p[i];
                if (e.sqrMagnitude > 1e-12f) last = Perp(e.normalized);
                sn[i] = last;
            }
            float run = 0f;
            for (int i = 0; i < segs; i++)
            {
                Vector2 a = p[i], b = p[(i + 1) % n];
                float l = (b - a).magnitude;
                if (l < 1e-6f) continue;
                Vector2 na = sn[i], nb = sn[i];
                if (smooth)
                {
                    if (i > 0 || closed) { Vector2 q = sn[(i - 1 + segs) % segs]; if (Vector2.Dot(q, sn[i]) > 0.57f) na = (q + sn[i]).normalized; }
                    if (i < segs - 1 || closed) { Vector2 q = sn[(i + 1) % segs]; if (Vector2.Dot(q, sn[i]) > 0.57f) nb = (q + sn[i]).normalized; }
                }
                Vector3 Na = D(new Vector3(0f, na.y, na.x)), Nb = D(new Vector3(0f, nb.y, nb.x));
                RawN(P(x0, a.y, a.x), P(x1, a.y, a.x), P(x1, b.y, b.x), P(x0, b.y, b.x), Na, Na, Nb, Nb,
                    new Vector2(x0, run), new Vector2(x1, run), new Vector2(x1, run + l), new Vector2(x0, run + l));
                run += l;
            }
        }

        /// <summary>Flat-shaded band between two closed profiles with the same point count (xa &lt; xb).</summary>
        public void Loft(Vector2[] pa, float xa, Vector2[] pb, float xb)
        {
            int n = pa.Length;
            for (int i = 0; i < n; i++)
            {
                Vector2 a0 = pa[i], a1 = pa[(i + 1) % n], b0 = pb[i], b1 = pb[(i + 1) % n];
                if ((a1 - a0).sqrMagnitude < 1e-12f && (b1 - b0).sqrMagnitude < 1e-12f) continue;
                Raw(P(xa, a0.y, a0.x), P(xb, b0.y, b0.x), P(xb, b1.y, b1.x), P(xa, a1.y, a1.x));
            }
        }

        /// <summary>Closes a profile at x with a fan around the pivot (the profile must be star-shaped from it).</summary>
        public void Cap(Vector2[] p, float x, bool plusX, Vector2 pivot, bool closed = true)
        {
            int n = p.Length, segs = closed ? n : n - 1;
            Vector3 c = P(x, pivot.y, pivot.x);
            for (int i = 0; i < segs; i++)
            {
                Vector2 a = p[i], b = p[(i + 1) % n];
                if ((b - a).sqrMagnitude < 1e-12f) continue;
                if (plusX) RawTri(c, P(x, b.y, b.x), P(x, a.y, a.x)); else RawTri(c, P(x, a.y, a.x), P(x, b.y, b.x));
            }
        }

        public static Vector2 Centre(Vector2[] p)
        {
            Vector2 c = Vector2.zero;
            for (int i = 0; i < p.Length; i++) c += p[i];
            return c / p.Length;
        }

        /// <summary>A closed solid: the profile swept from x0 to x1 with bevelled (chamfer ch) and capped ends.</summary>
        public void Pill(Vector2[] p, float x0, float x1, float ch)
        {
            Vector2 c = Centre(p);
            if (ch <= 0f) { Sweep(p, x0, x1, true); Cap(p, x0, false, c); Cap(p, x1, true, c); return; }
            Vector2[] q = Inset(p, ch);
            Sweep(p, x0 + ch, x1 - ch, true);
            Loft(q, x0, p, x0 + ch);
            Loft(p, x1 - ch, q, x1);
            Cap(q, x0, false, c); Cap(q, x1, true, c);
        }

        /// <summary>Smooth surface from a function of (u, v) in 0..1 returning frame-space points. It faces the Toward
        /// point when one is set; otherwise its normal is dP/dv cross dP/du.</summary>
        public void Grid(int nu, int nv, Func<float, float, Vector3> f, float uvU = 1f, float uvV = 1f)
        {
            int w = nu + 1, h = nv + 1;
            var pt = new Vector3[w * h];
            var nm = new Vector3[w * h];
            for (int j = 0; j < h; j++)
                for (int i = 0; i < w; i++) pt[j * w + i] = P(f((float)i / nu, (float)j / nv));
            for (int j = 0; j < h; j++)
                for (int i = 0; i < w; i++)
                {
                    Vector3 du = pt[j * w + Mathf.Min(i + 1, nu)] - pt[j * w + Mathf.Max(i - 1, 0)];
                    Vector3 dv = pt[Mathf.Min(j + 1, nv) * w + i] - pt[Mathf.Max(j - 1, 0) * w + i];
                    Vector3 n = Vector3.Cross(dv, du);
                    nm[j * w + i] = Unit(n);
                }
            bool flip = false;
            if (orient)
            {
                int m = (nv / 2) * w + nu / 2;
                flip = Vector3.Dot(nm[m], toward - pt[m]) < 0f;
            }
            for (int j = 0; j < nv; j++)
                for (int i = 0; i < nu; i++)
                {
                    int a = j * w + i, b = (j + 1) * w + i, c = (j + 1) * w + i + 1, d = j * w + i + 1;
                    Vector2 ua = new Vector2(uvU * i / nu, uvV * j / nv), ub = new Vector2(uvU * i / nu, uvV * (j + 1) / nv);
                    Vector2 uc = new Vector2(uvU * (i + 1) / nu, uvV * (j + 1) / nv), ud = new Vector2(uvU * (i + 1) / nu, uvV * j / nv);
                    if (flip) RawN(pt[a], pt[d], pt[c], pt[b], -nm[a], -nm[d], -nm[c], -nm[b], ua, ud, uc, ub);
                    else RawN(pt[a], pt[b], pt[c], pt[d], nm[a], nm[b], nm[c], nm[d], ua, ub, uc, ud);
                }
        }

        public int VertexCount { get { return mb.VertexCount; } }

        public GameObject ToObject(string name, Transform parent, params Material[] mats)
        {
            return mb.ToObject(name, parent, mats);
        }
    }
}
