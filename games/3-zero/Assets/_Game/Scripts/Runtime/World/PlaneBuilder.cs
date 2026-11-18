using System.Collections.Generic;
using UnityEngine;

namespace ZERO
{
    /// <summary>Procedural airliner around the player's window seat: a cabin section (lining with real window openings,
    /// bins, ceiling, seats), the left wing with its engine, and a stub of outer skin around the wing root.
    /// Everything is plane-local (see <see cref="PlaneDef"/>), vertex-coloured and single-sided:
    /// interior faces look into the cabin, wing / nacelle / skin faces look outward.</summary>
    public static class PlaneBuilder
    {
        public struct Result { public Transform Root, Cabin, Wing, Nacelle; }

        /// <summary>Builds the aircraft under 'parent' in plane-local coordinates.
        /// cabinMat: interior; hullMat: everything outside (wing, engine, outer skin of the window tunnels).</summary>
        public static Result Build(Transform parent, Material cabinMat, Material hullMat)
        {
            var rootGo = new GameObject("Plane");
            Transform root = rootGo.transform;
            root.SetParent(parent, false);
            root.localPosition = Vector3.zero; root.localRotation = Quaternion.identity; root.localScale = Vector3.one;

            var shell = new MeshBuilder(); var hull = new MeshBuilder();
            BuildShell(shell, hull);
            var seats = new MeshBuilder();
            BuildSeats(seats);
            BuildFuselageStub(hull);
            var wing = new MeshBuilder();
            BuildWing(wing);
            var nacelle = new MeshBuilder();
            BuildNacelle(nacelle);

            GameObject cabinGo = shell.ToObject("Cabin", root, cabinMat, false);
            seats.ToObject("Seats", cabinGo.transform, cabinMat, false);
            hull.ToObject("Hull", root, hullMat, false);
            GameObject wingGo = wing.ToObject("Wing", root, hullMat, false);
            wingGo.transform.localPosition = PlaneDef.WingRoot;                               // the wing flexes about its root
            GameObject nacGo = nacelle.ToObject("Nacelle", wingGo.transform, hullMat, false);
            nacGo.transform.localPosition = PlaneDef.NacelleCentre - PlaneDef.WingRoot;
            return new Result { Root = root, Cabin = cabinGo.transform, Wing = wingGo.transform, Nacelle = nacGo.transform };
        }

        // =====================================================================================================
        // colours
        // =====================================================================================================
        static readonly Color LiningCol = Palette.Hex(0xd8d4cb);        // warm light grey
        static readonly Color BezelCol = Palette.Hex(0xe5e2db);
        static readonly Color TunnelCol = Palette.Hex(0x9c9a95);
        static readonly Color GasketCol = Palette.Hex(0x4b4d52);
        static readonly Color DadoCol = Palette.Hex(0x8c8f95);
        static readonly Color CarpetCol = Palette.Hex(0x343c4d);
        static readonly Color AisleCol = Palette.Hex(0x4b566c);
        static readonly Color FabricCol = Palette.Hex(0x4a6288);        // muted blue
        static readonly Color PipingCol = Palette.ShadowBlue;
        static readonly Color CoverCol = Palette.Hex(0xe6e2d8);         // headrest covers
        static readonly Color PlasticCol = Palette.Hex(0xc3c6ca);
        static readonly Color PlasticDarkCol = Palette.Hex(0x5b606a);
        static readonly Color ArmPadCol = Palette.Hex(0x39414f);
        static readonly Color SeatMetalCol = Palette.Hex(0x70747b);
        static readonly Color BinCol = Palette.Hex(0xe3e3e1);
        static readonly Color CeilingCol = Palette.Hex(0xeae9e5);
        static readonly Color LightCol = Palette.Hex(0xfff4dc);
        static readonly Color PsuCol = Palette.Hex(0xcfd0d0);
        static readonly Color CurtainCol = Palette.Hex(0x3d5278);
        static readonly Color ScreenCol = Palette.Hex(0x23262c);
        static readonly Color GuardCol = Palette.Hex(0x2a2c30);         // unseen backing between lining and skin (shows only if something is open)
        static readonly Color PaintCol = Palette.TurbineWhite;          // wing, nacelle, skin
        static readonly Color SlatCol = Palette.MetalPale;
        static readonly Color LipCol = Palette.Mix(Palette.MetalPale, Palette.SkyWhite, 0.6f);
        static readonly Color LinerCol = Palette.Hex(0x33363d);
        static readonly Color FanCol = Palette.Hex(0x23262b);
        static readonly Color HotMetalCol = Palette.Hex(0x8f8a84);
        const float LineTone = 0.64f;                                   // panel lines on the white parts

        static Color32 Sh(Color c, float mul = 1f, float emission = 0f) { return MeshBuilder.Shade(c, mul, emission); }

        // =====================================================================================================
        // emit helpers (every face is wound so that its front side agrees with the normals given)
        // =====================================================================================================
        static Vector3 Norm(Vector3 v)
        {
            float m = Mathf.Sqrt(v.x * v.x + v.y * v.y + v.z * v.z);
            return m > 1e-18f ? new Vector3(v.x / m, v.y / m, v.z / m) : Vector3.up;
        }

        static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

        /// <summary>Piecewise-linear lookup; kv = x0, y0, x1, y1 ... with ascending x.</summary>
        static float Key(float x, float[] kv)
        {
            int n = kv.Length / 2;
            if (x <= kv[0]) return kv[1];
            for (int i = 1; i < n; i++)
                if (x <= kv[i * 2])
                {
                    float t = (x - kv[i * 2 - 2]) / (kv[i * 2] - kv[i * 2 - 2]);
                    return kv[i * 2 - 1] + (kv[i * 2 + 1] - kv[i * 2 - 1]) * t;
                }
            return kv[n * 2 - 1];
        }

        static float Hash01(int n)
        {
            unchecked
            {
                uint h = (uint)n * 747796405u + 2891336453u;
                h = ((h >> (int)((h >> 28) + 4u)) ^ h) * 277803737u;
                h ^= h >> 22;
                return (h & 0xFFFFFF) / 16777216f;
            }
        }

        /// <summary>Triangle between existing vertices; the winding is chosen so the front side agrees with the vertex normals.</summary>
        static void AddTri(MeshBuilder mb, int i0, int i1, int i2)
        {
            Vector3 a = mb.V[i0], b = mb.V[i1], c = mb.V[i2];
            Vector3 g = Vector3.Cross(b - a, c - a);
            if (g.x * g.x + g.y * g.y + g.z * g.z < 1e-20f) return;                           // degenerate
            if (Vector3.Dot(g, mb.Nrm[i0] + mb.Nrm[i1] + mb.Nrm[i2]) >= 0f) mb.Tri(i0, i1, i2); else mb.Tri(i0, i2, i1);
        }

        static void Quad4(MeshBuilder mb, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 na, Vector3 nb, Vector3 nc, Vector3 nd, Color32 col)
        {
            int i = mb.Vert(a, na, col); mb.Vert(b, nb, col); mb.Vert(c, nc, col); mb.Vert(d, nd, col);
            AddTri(mb, i, i + 1, i + 2); AddTri(mb, i, i + 2, i + 3);
        }

        /// <summary>Flat quad a-b-c-d (perimeter order) with the given unit normal.</summary>
        static void Face(MeshBuilder mb, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, Color32 col) { Quad4(mb, a, b, c, d, n, n, n, n, col); }

        /// <summary>Flat quad a-b-c-d (perimeter order) whose front side looks toward 'facing'.</summary>
        static void QuadF(MeshBuilder mb, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color32 col, Vector3 facing)
        {
            Vector3 n = Vector3.Cross(c - a, d - b);
            if (n.x * n.x + n.y * n.y + n.z * n.z < 1e-20f) n = Vector3.Cross(b - a, c - a);
            if (n.x * n.x + n.y * n.y + n.z * n.z < 1e-20f) return;
            n = Norm(n); if (Vector3.Dot(n, facing) < 0f) n = -n;
            Quad4(mb, a, b, c, d, n, n, n, n, col);
        }

        static void TriF(MeshBuilder mb, Vector3 a, Vector3 b, Vector3 c, Color32 col, Vector3 facing)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (n.x * n.x + n.y * n.y + n.z * n.z < 1e-20f) return;
            n = Norm(n); if (Vector3.Dot(n, facing) < 0f) n = -n;
            int i = mb.Vert(a, n, col); mb.Vert(b, n, col); mb.Vert(c, n, col);
            AddTri(mb, i, i + 1, i + 2);
        }

        /// <summary>Axis-aligned box (outward faces, exact normals even for very small boxes).</summary>
        static void Bx(MeshBuilder mb, Vector3 c, Vector3 size, Color32 col)
        {
            Vector3 h = size * 0.5f;
            Vector3 p000 = c + new Vector3(-h.x, -h.y, -h.z), p100 = c + new Vector3(h.x, -h.y, -h.z), p010 = c + new Vector3(-h.x, h.y, -h.z), p110 = c + new Vector3(h.x, h.y, -h.z);
            Vector3 p001 = c + new Vector3(-h.x, -h.y, h.z), p101 = c + new Vector3(h.x, -h.y, h.z), p011 = c + new Vector3(-h.x, h.y, h.z), p111 = c + new Vector3(h.x, h.y, h.z);
            Face(mb, p000, p010, p110, p100, Vector3.back, col);
            Face(mb, p101, p111, p011, p001, Vector3.forward, col);
            Face(mb, p001, p011, p010, p000, Vector3.left, col);
            Face(mb, p100, p110, p111, p101, Vector3.right, col);
            Face(mb, p010, p011, p111, p110, Vector3.up, col);
            Face(mb, p001, p000, p100, p101, Vector3.down, col);
        }

        /// <summary>Six-faced solid: c0..c3 one face in perimeter order, c4..c7 the opposite face in the same order. Outward faces.</summary>
        static void Hexa(MeshBuilder mb, Vector3 c0, Vector3 c1, Vector3 c2, Vector3 c3, Vector3 c4, Vector3 c5, Vector3 c6, Vector3 c7, Color32 col)
        {
            Vector3 m = (c0 + c1 + c2 + c3 + c4 + c5 + c6 + c7) * 0.125f;
            QuadF(mb, c0, c1, c2, c3, col, (c0 + c1 + c2 + c3) * 0.25f - m);
            QuadF(mb, c4, c5, c6, c7, col, (c4 + c5 + c6 + c7) * 0.25f - m);
            QuadF(mb, c0, c1, c5, c4, col, (c0 + c1 + c5 + c4) * 0.25f - m);
            QuadF(mb, c1, c2, c6, c5, col, (c1 + c2 + c6 + c5) * 0.25f - m);
            QuadF(mb, c2, c3, c7, c6, col, (c2 + c3 + c7 + c6) * 0.25f - m);
            QuadF(mb, c3, c0, c4, c7, col, (c3 + c0 + c4 + c7) * 0.25f - m);
        }

        /// <summary>Box of cross-section w x h along the segment p0-p1 (h is measured in the vertical plane of the segment).</summary>
        static void Beam(MeshBuilder mb, Vector3 p0, Vector3 p1, float w, float h, Color32 col)
        {
            Vector3 dir = Norm(p1 - p0);
            Vector3 sd = Mathf.Abs(dir.y) < 0.99f ? Norm(Vector3.Cross(Vector3.up, dir)) : Vector3.right;
            Vector3 up = Vector3.Cross(dir, sd);
            Vector3 a = sd * (w * 0.5f), b = up * (h * 0.5f);
            Hexa(mb, p0 - a - b, p0 + a - b, p0 + a + b, p0 - a + b, p1 - a - b, p1 + a - b, p1 + a + b, p1 - a + b, col);
        }

        /// <summary>Smooth surface through a grid of points with explicit normals. One colour per cell (hard-edged panels and lines),
        /// or, when nodeCol is given, one colour per node (soft gradients). wrapJ closes the second index.</summary>
        static void GridN(MeshBuilder mb, Vector3[,] P, Vector3[,] N, bool wrapJ, System.Func<int, int, Color32> cellCol, System.Func<int, int, Color32> nodeCol = null)
        {
            int ni = P.GetLength(0), nj = P.GetLength(1), cj = wrapJ ? nj : nj - 1;
            for (int i = 0; i < ni - 1; i++)
                for (int j = 0; j < cj; j++)
                {
                    int jn = (j + 1) % nj;
                    if (nodeCol == null) { Quad4(mb, P[i, j], P[i + 1, j], P[i + 1, jn], P[i, jn], N[i, j], N[i + 1, j], N[i + 1, jn], N[i, jn], cellCol(i, j)); continue; }
                    int k = mb.Vert(P[i, j], N[i, j], nodeCol(i, j)); mb.Vert(P[i + 1, j], N[i + 1, j], nodeCol(i + 1, j));
                    mb.Vert(P[i + 1, jn], N[i + 1, jn], nodeCol(i + 1, jn)); mb.Vert(P[i, jn], N[i, jn], nodeCol(i, jn));
                    AddTri(mb, k, k + 1, k + 2); AddTri(mb, k, k + 2, k + 3);
                }
        }

        /// <summary>As <see cref="GridN"/>, with normals taken from the grid itself and turned toward the hint of each node.</summary>
        static void Grid(MeshBuilder mb, Vector3[,] P, bool wrapJ, System.Func<int, int, Vector3> hint, System.Func<int, int, Color32> cellCol, System.Func<int, int, Color32> nodeCol = null)
        {
            int ni = P.GetLength(0), nj = P.GetLength(1);
            var N = new Vector3[ni, nj];
            for (int i = 0; i < ni; i++)
                for (int j = 0; j < nj; j++)
                {
                    int ia = Mathf.Max(i - 1, 0), ib = Mathf.Min(i + 1, ni - 1);
                    int ja = wrapJ ? (j + nj - 1) % nj : Mathf.Max(j - 1, 0), jb = wrapJ ? (j + 1) % nj : Mathf.Min(j + 1, nj - 1);
                    Vector3 n = Vector3.Cross(P[ib, j] - P[ia, j], P[i, jb] - P[i, ja]);
                    Vector3 h = hint(i, j);
                    if (n.x * n.x + n.y * n.y + n.z * n.z < 1e-16f) n = h; else if (Vector3.Dot(n, h) < 0f) n = -n;
                    N[i, j] = Norm(n);
                }
            GridN(mb, P, N, wrapJ, cellCol, nodeCol);
        }

        // =====================================================================================================
        // 1. cabin shell
        // =====================================================================================================
        const float ZF = PlaneDef.CabinZMax, ZR = PlaneDef.CabinZMin;
        const float EndInset = 0.03f;                                    // the two end walls stand this far inside the section, so everything else runs through them
        const float GlassInset = 0.035f;                                 // the pane (GlassBuilder) sits this far in from the outer skin: the seal ring is drawn round it
        const float XOpen = PlaneDef.WallX + 0.035f;                     // inner end of the window tunnel: the bezel flares down to here
        const float BayHalfH = 0.44f;                                    // half height of the tessellated panel around a window
        const float SeamHalf = 0.0025f;                                  // half width of the joint between two sidewall panels
        const float BayHalfW = PlaneDef.WindowPitch * 0.5f - SeamHalf;
        const float BackHalfH = 0.50f;                                   // half height of the backing ring behind each bay
        const float RimW = 0.045f;                                       // outer skin rim around each opening
        const float DadoTop = 0.14f, TrimH = 0.015f, SlotY = 1.60f, SlotH = 0.012f, LiningTopY = 1.69f;
        const float WallApexY = 0.95f;                                   // height at which the lining is widest
        // the lining is one circular arc through the floor edge and the window belt
        static readonly float WallCx = (PlaneDef.WallX * PlaneDef.WallX + (PlaneDef.WindowY - WallApexY) * (PlaneDef.WindowY - WallApexY)
                                        - PlaneDef.FloorHalfWidth * PlaneDef.FloorHalfWidth - WallApexY * WallApexY) / (2f * (PlaneDef.WallX - PlaneDef.FloorHalfWidth));
        static readonly float WallR = Mathf.Sqrt((PlaneDef.WallX - WallCx) * (PlaneDef.WallX - WallCx) + (PlaneDef.WindowY - WallApexY) * (PlaneDef.WindowY - WallApexY));
        static readonly int FirstWindow = Mathf.CeilToInt((ZR + PlaneDef.WindowPitch * 0.5f - PlaneDef.WindowZ0) / PlaneDef.WindowPitch);
        static readonly int LastWindow = Mathf.FloorToInt((ZF - PlaneDef.WindowPitch * 0.5f - PlaneDef.WindowZ0) / PlaneDef.WindowPitch);
        static readonly float[] YLow = { -0.012f, DadoTop, DadoTop + TrimH, 0.40f, PlaneDef.WindowY + (-BayHalfH) };
        static readonly float[] YHigh = { PlaneDef.WindowY + BayHalfH, SlotY, SlotY + SlotH, LiningTopY };

        /// <summary>|x| of the cabin lining at height y.</summary>
        static float WallXAt(float y) { float d = y - WallApexY; return WallCx + Mathf.Sqrt(WallR * WallR - d * d); }

        /// <summary>Unit normal of the lining (pointing into the cabin) at height y on the given side.</summary>
        static Vector3 WallNormal(float y, float side)
        {
            float x = WallXAt(y);
            return new Vector3(-side * (x - WallCx) / WallR, -(y - WallApexY) / WallR, 0f);
        }

        static Color32 WallTone(float y, float shade, bool seam)
        {
            if (y < DadoTop) return Sh(DadoCol, seam ? 0.92f : 1f);
            if (y < DadoTop + TrimH) return Sh(DadoCol, 0.72f);
            if (y > SlotY && y < SlotY + SlotH) return Sh(GasketCol, 1.1f);
            if (seam) return Sh(LiningCol, 0.80f);
            return Sh(LiningCol, (y > SlotY ? 0.965f : y < PlaneDef.WindowY - BayHalfH ? 0.985f : 1f) * shade);
        }

        static List<float> PlainRows()
        {
            var l = new List<float>(YLow);
            for (int i = 1; i < 6; i++) l.Add(YLow[YLow.Length - 1] + (YHigh[0] - YLow[YLow.Length - 1]) * i / 6f);
            l.AddRange(YHigh);
            return l;
        }

        /// <summary>One point of a rounded rectangle (half size hw x hh, corner radius r; x = along the cabin, y = up) and the matching point
        /// on an outer rectangle (a x b). q runs 0..nTop+nArc+nSide over a quarter: top centre, along the top edge, round the corner, down to the side centre.</summary>
        static void QuarterPoint(int q, int nTop, int nArc, int nSide, float hw, float hh, float r, float a, float b, out Vector2 p, out Vector2 n, out Vector2 o)
        {
            float sx = hw - r, sy = hh - r;
            if (q <= nTop)
            {
                float t = q / (float)nTop;
                p = new Vector2(sx * t, hh); n = new Vector2(0f, 1f); o = new Vector2(sx * t, b);
            }
            else if (q < nTop + nArc)
            {
                float t = (q - nTop) / (float)nArc, ang = (1f - t) * Mathf.PI * 0.5f;
                n = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)); p = new Vector2(sx + n.x * r, sy + n.y * r);
                if (t < 0.5f) o = new Vector2(sx + (a - sx) * (t * 2f), b);
                else if (t == 0.5f) o = new Vector2(a, b);
                else o = new Vector2(a, b + (sy - b) * ((t - 0.5f) * 2f));
            }
            else
            {
                float t = (q - nTop - nArc) / (float)nSide;
                p = new Vector2(hw, sy * (1f - t)); n = new Vector2(1f, 0f); o = new Vector2(a, sy * (1f - t));
            }
        }

        /// <summary>Point i of the closed loop (4 quarters): starts at the top centre and runs toward +z, down, back along the bottom and up again.</summary>
        static void LoopPoint(int i, int nTop, int nArc, int nSide, float hw, float hh, float r, float a, float b, out Vector2 p, out Vector2 n, out Vector2 o)
        {
            int nq = nTop + nArc + nSide, quad = i / nq, m = i % nq;
            float sz = quad >= 2 ? -1f : 1f, sy = (quad == 1 || quad == 2) ? -1f : 1f;
            int q = (quad == 0 || quad == 2) ? m : nq - m;
            QuarterPoint(q, nTop, nArc, nSide, hw, hh, r, a, b, out p, out n, out o);
            p = new Vector2(p.x * sz, p.y * sy); n = new Vector2(n.x * sz, n.y * sy); o = new Vector2(o.x * sz, o.y * sy);
        }

        static void BuildShell(MeshBuilder mb, MeshBuilder hull)
        {
            var rng = new Rng(7301u);
            float P = PlaneDef.WindowPitch;
            for (int si = 0; si < 2; si++)
            {
                float side = si == 0 ? -1f : 1f;
                List<float> prevYs = PlainRows(); float prevZ = ZR, prevShade = 1f;
                for (int k = FirstWindow; k <= LastWindow; k++)
                {
                    int ak = Mathf.Abs(k);
                    int lod = side < 0f ? (ak <= 1 ? 2 : ak <= 4 ? 1 : 0) : (ak <= 2 ? 1 : 0);          // the player's bay and its neighbours are the finest
                    float zc = PlaneDef.WindowZ0 + k * P, shade = 1f + rng.Signed() * 0.018f;
                    List<float> ys = Bay(mb, hull, side, zc, lod, shade);
                    if (k == FirstWindow) { float sh = shade; ZipWall(mb, side, prevZ, prevYs, zc - BayHalfW, ys, y => WallTone(y, sh, false)); }
                    else ZipWall(mb, side, prevZ, prevYs, zc - BayHalfW, ys, y => WallTone(y, 1f, true));    // the joint between two panels
                    prevYs = ys; prevZ = zc + BayHalfW; prevShade = shade;
                }
                { float sh = prevShade; ZipWall(mb, side, prevZ, prevYs, ZF, PlainRows(), y => WallTone(y, sh, false)); }

                BuildBins(mb, side);
            }
            BuildGuard(mb);
            BuildFloor(mb);
            BuildCeiling(mb);
            BuildPsus(mb);
            BuildFrontBulkhead(mb);
            BuildGalleyWall(mb);
        }

        /// <summary>Dark backing behind the interior: the inside of the skin below, above and beyond the window bays, under the floor,
        /// over the ceiling and behind the two end walls. It is never seen unless something in front of it is open
        /// (then it shows instead of the bright sky). It stays inside the outline of the fuselage.</summary>
        static void BuildGuard(MeshBuilder mb)
        {
            Color32 guard = Sh(GuardCol);
            float P = PlaneDef.WindowPitch, wy = PlaneDef.WindowY;
            float zFirst = PlaneDef.WindowZ0 + FirstWindow * P + (-P * 0.5f), zLast = PlaneDef.WindowZ0 + LastWindow * P + P * 0.5f;
            float[] o = { 0f, -0.05f, 1.72f, -0.05f, PlaneDef.SkinX, wy - BackHalfH, PlaneDef.SkinX, wy + BackHalfH, 1.55f, 2.00f, 1.00f, 2.40f, 0f, 2.50f };   // outline (x, y), right half
            Vector3 mid = new Vector3(0f, 1.1f, 0f);
            for (int si = 0; si < 2; si++)
            {
                float side = si == 0 ? -1f : 1f;
                for (int i = 0; i + 3 < o.Length; i += 2)
                {
                    Vector2 a = new Vector2(side * o[i], o[i + 1]), b = new Vector2(side * o[i + 2], o[i + 3]);
                    Vector3 facing = mid - new Vector3((a.x + b.x) * 0.5f, (a.y + b.y) * 0.5f, 0f);
                    if (i == 4)        // the window belt: the bays bring their own backing rings, only the two ends are closed here
                    {
                        QuadF(mb, new Vector3(a.x, a.y, ZR), new Vector3(b.x, b.y, ZR), new Vector3(b.x, b.y, zFirst), new Vector3(a.x, a.y, zFirst), guard, facing);
                        QuadF(mb, new Vector3(a.x, a.y, zLast), new Vector3(b.x, b.y, zLast), new Vector3(b.x, b.y, ZF), new Vector3(a.x, a.y, ZF), guard, facing);
                    }
                    else QuadF(mb, new Vector3(a.x, a.y, ZR), new Vector3(b.x, b.y, ZR), new Vector3(b.x, b.y, ZF), new Vector3(a.x, a.y, ZF), guard, facing);
                    TriF(mb, new Vector3(0f, 1.1f, ZR), new Vector3(a.x, a.y, ZR), new Vector3(b.x, b.y, ZR), guard, Vector3.forward);
                    TriF(mb, new Vector3(0f, 1.1f, ZF), new Vector3(a.x, a.y, ZF), new Vector3(b.x, b.y, ZF), guard, Vector3.back);
                }
            }
        }

        /// <summary>One window bay of the sidewall: the lining as rings of quads around a real opening, the soft bezel, the tunnel to the
        /// outer skin, the backing ring, the strips of wall below and above, and (in the hull mesh) the rim of outer skin.
        /// Returns the heights of the vertices along the bay's fore / aft edge (for the joint to the next panel).</summary>
        static List<float> Bay(MeshBuilder mb, MeshBuilder hull, float side, float zc, int lod, float shade)
        {
            // nArc must be even: the middle of each corner arc runs out to the corner of the panel
            int nTop = lod == 2 ? 2 : 1, nArc = lod == 2 ? 16 : lod == 1 ? 10 : 6, nSide = lod == 2 ? 3 : lod == 1 ? 2 : 1;
            int nRad = lod == 2 ? 4 : lod == 1 ? 3 : 2, nFl = lod == 2 ? 8 : lod == 1 ? 5 : 3;
            int n = 4 * (nTop + nArc + nSide);
            float a = BayHalfW, b = BayHalfH, wy = PlaneDef.WindowY;
            float insW = (PlaneDef.BezelW - PlaneDef.WindowW) * 0.5f, insH = (PlaneDef.BezelH - PlaneDef.WindowH) * 0.5f, ins = Mathf.Min(insW, insH);
            float hwB = PlaneDef.BezelW * 0.5f, hhB = PlaneDef.BezelH * 0.5f, rB = PlaneDef.WindowCorner + ins;
            float hwO = PlaneDef.WindowW * 0.5f, hhO = PlaneDef.WindowH * 0.5f, rO = PlaneDef.WindowCorner;
            Vector2 p, nn, o;

            // lining: rings of quads from the bezel edge out to the panel rectangle
            var L = new Vector3[nRad + 1, n]; var LN = new Vector3[nRad + 1, n];
            var edgeY = new List<float>(); var edgeZ = new List<float>();
            for (int j = 0; j < n; j++)
            {
                LoopPoint(j, nTop, nArc, nSide, hwB, hhB, rB, a, b, out p, out nn, out o);
                if (o.x == a) edgeY.Add(wy + o.y);
                if (o.y == -b) edgeZ.Add(o.x);
                for (int i = 0; i <= nRad; i++)
                {
                    float f = i / (float)nRad; f *= 0.55f + 0.45f * f;
                    Vector2 q = i == nRad ? o : p + (o - p) * f;
                    float y = wy + q.y;
                    L[i, j] = new Vector3(side * WallXAt(y), y, zc + q.x); LN[i, j] = WallNormal(y, side);
                }
            }
            Color32 lining = Sh(LiningCol, shade);
            GridN(mb, L, LN, true, (i, j) => lining);

            // bezel: a rounded shoulder that rolls from the lining into the opening
            const float tauMax = 1.15f;
            var F = new Vector3[nFl + 1, n]; var Fh = new Vector3[n];
            for (int j = 0; j < n; j++)
            {
                LoopPoint(j, nTop, nArc, nSide, hwB, hhB, rB, a, b, out p, out nn, out o);
                float x0 = WallXAt(wy + p.y);
                Fh[j] = new Vector3(-side * 0.7f, -nn.y, -nn.x);
                for (int i = 0; i <= nFl; i++)
                {
                    if (i == nFl)
                    {
                        LoopPoint(j, nTop, nArc, nSide, hwO, hhO, rO, a, b, out p, out nn, out o);
                        F[i, j] = new Vector3(side * XOpen, wy + p.y, zc + p.x);
                        continue;
                    }
                    float tau = tauMax * i / nFl, g = Mathf.Sin(tau) / Mathf.Sin(tauMax), d = (1f - Mathf.Cos(tau)) / (1f - Mathf.Cos(tauMax));
                    LoopPoint(j, nTop, nArc, nSide, hwB - insW * g, hhB - insH * g, rB - ins * g, a, b, out p, out nn, out o);
                    F[i, j] = new Vector3(side * (x0 + (XOpen - x0) * d), wy + p.y, zc + p.x);
                }
            }
            Grid(mb, F, true, (i, j) => Fh[j], null, (i, j) => Sh(BezelCol, Mathf.Lerp(1.0f, 0.89f, i / (float)nFl) * shade));     // a little darker into the recess

            // tunnel through to the outer skin, backing ring (inside of the skin), rim of outer skin
            float xg = PlaneDef.SkinX - GlassInset;
            float[] tx = { XOpen, XOpen + 0.012f, xg - 0.009f, xg + 0.009f, PlaneDef.SkinX };
            int nt = tx.Length;
            var T = new Vector3[nt, n]; var TN = new Vector3[nt, n];
            var Bk = new Vector3[2, n]; var BkN = new Vector3[2, n]; var Rm = new Vector3[2, n]; var RmN = new Vector3[2, n];
            for (int j = 0; j < n; j++)
            {
                LoopPoint(j, nTop, nArc, nSide, hwO, hhO, rO, PlaneDef.WindowPitch * 0.5f, BackHalfH, out p, out nn, out o);
                for (int i = 0; i < nt; i++) { T[i, j] = new Vector3(side * tx[i], wy + p.y, zc + p.x); TN[i, j] = new Vector3(0f, -nn.y, -nn.x); }
                Bk[0, j] = T[nt - 1, j]; Bk[1, j] = new Vector3(side * PlaneDef.SkinX, wy + o.y, zc + o.x);
                BkN[0, j] = BkN[1, j] = new Vector3(-side, 0f, 0f);
                Rm[0, j] = T[nt - 1, j]; RmN[0, j] = RmN[1, j] = new Vector3(side, 0f, 0f);
                LoopPoint(j, nTop, nArc, nSide, hwO + RimW, hhO + RimW, rO + RimW, a, b, out p, out nn, out o);
                Rm[1, j] = new Vector3(side * PlaneDef.SkinX, wy + p.y, zc + p.x);
            }
            Color32 frame = Sh(BezelCol, 0.83f * shade), gasket = Sh(GasketCol), tunnel = Sh(TunnelCol, shade), outer = Sh(TunnelCol, 0.80f * shade), guard = Sh(GuardCol), rim = Sh(PaintCol, 0.80f);
            GridN(mb, T, TN, true, (i, j) => i == 0 ? frame : i == 1 ? tunnel : i == 2 ? gasket : outer);
            GridN(mb, Bk, BkN, true, (i, j) => guard);
            GridN(hull, Rm, RmN, true, (i, j) => rim);

            // wall below and above the bay, in columns that share the bay's edge vertices
            edgeZ.Sort();
            for (int part = 0; part < 2; part++)
            {
                float[] rows = part == 0 ? YLow : YHigh;
                var W = new Vector3[rows.Length, edgeZ.Count]; var WN = new Vector3[rows.Length, edgeZ.Count];
                for (int r = 0; r < rows.Length; r++)
                    for (int c = 0; c < edgeZ.Count; c++) { W[r, c] = new Vector3(side * WallXAt(rows[r]), rows[r], zc + edgeZ[c]); WN[r, c] = WallNormal(rows[r], side); }
                GridN(mb, W, WN, false, (r, c) => WallTone((rows[r] + rows[r + 1]) * 0.5f, shade, false));
            }

            var ys = new List<float>(YLow); ys.AddRange(edgeY); ys.AddRange(YHigh); ys.Sort();
            for (int i = ys.Count - 1; i > 0; i--) if (ys[i] == ys[i - 1]) ys.RemoveAt(i);
            return ys;
        }

        /// <summary>Strip of lining between two vertical vertex columns (heights ascending, same first and last value).</summary>
        static void ZipWall(MeshBuilder mb, float side, float zA, List<float> yA, float zB, List<float> yB, System.Func<float, Color32> tone)
        {
            int i = 0, j = 0;
            while (i < yA.Count - 1 || j < yB.Count - 1)
            {
                bool advA = j >= yB.Count - 1 || (i < yA.Count - 1 && yA[i + 1] <= yB[j + 1]);
                float y0 = yA[i], y1 = yB[j], y2 = advA ? yA[i + 1] : yB[j + 1], z2 = advA ? zA : zB;
                Color32 col = tone((y0 + y1 + y2) / 3f);
                int k = mb.Vert(new Vector3(side * WallXAt(y0), y0, zA), WallNormal(y0, side), col);
                mb.Vert(new Vector3(side * WallXAt(y1), y1, zB), WallNormal(y1, side), col);
                mb.Vert(new Vector3(side * WallXAt(y2), y2, z2), WallNormal(y2, side), col);
                AddTri(mb, k, k + 1, k + 2);
                if (advA) i++; else j++;
            }
        }

        static void BuildFloor(MeshBuilder mb)
        {
            var rng = new Rng(911u);
            const float FloorEdge = PlaneDef.FloorHalfWidth + 0.08f;        // runs on under the lining
            float z0 = ZR, z1 = ZF, ah = PlaneDef.AisleHalfWidth;
            int rows = Mathf.CeilToInt((z1 - z0) / PlaneDef.SeatPitch);
            for (int r = 0; r < rows; r++)                                                          // carpet in row-long patches, lighter aisle runner
            {
                float za = z0 + r * PlaneDef.SeatPitch, zb = r == rows - 1 ? z1 : z0 + (r + 1) * PlaneDef.SeatPitch;
                FloorQuad(mb, -FloorEdge, -ah, za, zb, 0f, Sh(CarpetCol, 1f + rng.Signed() * 0.035f));
                FloorQuad(mb, -ah, ah, za, zb, 0f, Sh(AisleCol, 1f + rng.Signed() * 0.03f));
                FloorQuad(mb, ah, FloorEdge, za, zb, 0f, Sh(CarpetCol, 1f + rng.Signed() * 0.035f));
            }
            for (int s = -1; s <= 1; s += 2)
            {
                FloorQuad(mb, s * ah, s * (ah + 0.026f), z0, z1, 0.004f, Sh(LightCol, 0.80f, 0.10f));  // floor-path strips along the aisle
                for (int i = 1; i <= 2; i++)                                                         // seat rails
                    Bx(mb, new Vector3(s * (SeatX0 - SeatDX * 0.5f + i * SeatDX), 0.004f, (ZR + ZF) * 0.5f), new Vector3(0.034f, 0.008f, ZF - ZR - 0.1f), Sh(SeatMetalCol, 0.9f));
            }
        }

        static void FloorQuad(MeshBuilder mb, float xa, float xb, float za, float zb, float y, Color32 col)
        {
            Face(mb, new Vector3(xa, y, za), new Vector3(xa, y, zb), new Vector3(xb, y, zb), new Vector3(xb, y, za), Vector3.up, col);
        }

        // ---- overhead bins: one profile from the wall to the ceiling cove, swept along the cabin -------------
        // (dx from BinInnerX, dy from BinBottomY)
        static readonly float[] BinProf = {
            0.710f, -0.005f,   0.580f, 0f,   0.120f, 0f,   0.055f, 0.003f,   0.030f, 0.012f,   0.010f, 0.030f,   0f, 0.060f,
            -0.008f, 0.130f,   -0.020f, 0.210f,   -0.035f, 0.290f,   -0.060f, 0.350f,   -0.095f, 0.390f,   -0.115f, 0.402f,   -0.140f, 0.412f };
        const float CeilHalfW = PlaneDef.BinInnerX - 0.175f, CeilEdgeDrop = 0.075f;

        static void BuildBins(MeshBuilder mb, float side)
        {
            var rng = new Rng(side < 0f ? 5011u : 5012u);
            float P = PlaneDef.WindowPitch;
            int np = BinProf.Length / 2;
            var zs = new List<float>(); var cellDoor = new List<int>(); var doorTone = new List<float>();
            zs.Add(ZR); int door = 0;
            for (int k = FirstWindow; k <= LastWindow; k += 2)                                        // door joints every second window
            {
                float e = PlaneDef.WindowZ0 + (k - 0.5f) * P;
                cellDoor.Add(door++); zs.Add(e - 0.003f); cellDoor.Add(-1); zs.Add(e + 0.003f);
            }
            cellDoor.Add(door); zs.Add(ZF);
            for (int d = 0; d <= door; d++) doorTone.Add(1f + rng.Signed() * 0.02f);

            var G = new Vector3[np, zs.Count];
            for (int i = 0; i < np; i++)
                for (int j = 0; j < zs.Count; j++)
                    G[i, j] = new Vector3(side * (PlaneDef.BinInnerX + BinProf[i * 2]), PlaneDef.BinBottomY + BinProf[i * 2 + 1], zs[j]);
            Vector3 hint = new Vector3(-side * 0.7f, -0.7f, 0f);
            Grid(mb, G, false, (i, j) => hint, (i, j) =>
            {
                int d = cellDoor[j];
                if (i == 0) return Sh(BinCol, 0.90f);                     // strip along the wall
                if (i == 1) return Sh(BinCol, 0.86f);                     // service channel
                if (i == 2) return Sh(BinCol, 0.93f);
                if (i == 3) return Sh(BinCol, 0.55f);                     // gap under the door lip
                if (i == 11) return Sh(BinCol, 0.60f);                    // hinge line
                if (i == 12) return Sh(BinCol, 0.94f);
                return d < 0 ? Sh(BinCol, 0.58f) : Sh(BinCol, doorTone[d]);
            });

            // latches
            for (int j = 0; j < cellDoor.Count; j++)
            {
                if (cellDoor[j] < 0 || zs[j + 1] - zs[j] < 0.5f) continue;
                float zm = (zs[j] + zs[j + 1]) * 0.5f;
                Bx(mb, new Vector3(side * (PlaneDef.BinInnerX - 0.008f), PlaneDef.BinBottomY + 0.095f, zm), new Vector3(0.020f, 0.036f, 0.115f), Sh(PlasticDarkCol, 1.05f));
                Bx(mb, new Vector3(side * (PlaneDef.BinInnerX - 0.013f), PlaneDef.BinBottomY + 0.095f, zm), new Vector3(0.016f, 0.016f, 0.085f), Sh(PlasticCol, 0.95f));
            }

            // light strip in the cove between the bins and the ceiling panel (reaches a little behind both)
            float xa = PlaneDef.BinInnerX - 0.125f, ya = PlaneDef.BinBottomY + 0.400f, xb = CeilHalfW - 0.015f, yb = PlaneDef.CeilingY - CeilEdgeDrop + 0.013f;
            QuadF(mb, new Vector3(side * xa, ya, ZR), new Vector3(side * xb, yb, ZR), new Vector3(side * xb, yb, ZF), new Vector3(side * xa, ya, ZF),
                  Sh(LightCol, 1f, 0.85f), new Vector3(-side * 0.6f, -0.8f, 0f));
        }

        static void BuildCeiling(MeshBuilder mb)
        {
            var rng = new Rng(6133u);
            float P = PlaneDef.WindowPitch;
            const int nx = 10;
            var zs = new List<float>(); var seam = new List<bool>();
            zs.Add(ZR);
            for (int k = FirstWindow; k <= LastWindow; k += 2)
            {
                float e = PlaneDef.WindowZ0 + (k - 0.5f) * P;
                seam.Add(false); zs.Add(e - 0.003f); seam.Add(true); zs.Add(e + 0.003f);
            }
            seam.Add(false); zs.Add(ZF);
            var tone = new float[zs.Count];
            for (int j = 0; j < tone.Length; j++) tone[j] = 1f + rng.Signed() * 0.015f;
            var G = new Vector3[nx + 1, zs.Count];
            for (int i = 0; i <= nx; i++)
            {
                float x = -CeilHalfW + 2f * CeilHalfW * i / nx, t = x / CeilHalfW;
                for (int j = 0; j < zs.Count; j++) G[i, j] = new Vector3(x, PlaneDef.CeilingY - CeilEdgeDrop * t * t, zs[j]);
            }
            Grid(mb, G, false, (i, j) => Vector3.down, (i, j) => seam[j] ? Sh(CeilingCol, 0.80f) : Sh(CeilingCol, tone[j] * (i == 4 || i == 5 ? 1f : 0.985f)));
        }

        static void BuildPsus(MeshBuilder mb)
        {
            float yb = PlaneDef.BinBottomY;
            for (int k = RowMin; k <= RowMax; k++)
            {
                float z = PlaneDef.SeatZ0 + k * PlaneDef.SeatPitch + 0.13f;
                int sides = (k >= -1 && k <= 2) ? 12 : 6;
                for (int s = -1; s <= 1; s += 2)
                {
                    float xc = s * (PlaneDef.BinInnerX + 0.35f);
                    Bx(mb, new Vector3(xc, yb - 0.006f, z), new Vector3(0.40f, 0.014f, 0.30f), Sh(PsuCol, 1f));
                    Bx(mb, new Vector3(xc, yb - 0.008f, z), new Vector3(0.36f, 0.013f, 0.26f), Sh(PsuCol, 1.06f));
                    for (int i = -1; i <= 1; i++)
                    {
                        float x = xc + i * 0.115f;
                        // reading light: bezel ring and a faintly glowing lens
                        mb.Cylinder(new Vector3(x, yb - 0.012f, z - 0.07f), new Vector3(x, yb - 0.019f, z - 0.07f), 0.026f, 0.023f, sides, Sh(PlasticDarkCol, 1.2f));
                        mb.Cylinder(new Vector3(x, yb - 0.018f, z - 0.07f), new Vector3(x, yb - 0.021f, z - 0.07f), 0.017f, 0.015f, sides, Sh(LightCol, 0.95f, 0.45f));
                        // air nozzle
                        mb.Cylinder(new Vector3(x, yb - 0.012f, z + 0.045f), new Vector3(x, yb - 0.030f, z + 0.045f), 0.024f, 0.019f, sides, Sh(PlasticDarkCol, 0.9f));
                        mb.Cylinder(new Vector3(x, yb - 0.029f, z + 0.045f), new Vector3(x, yb - 0.032f, z + 0.045f), 0.010f, 0.010f, sides, Sh(GasketCol, 0.7f));
                    }
                    Bx(mb, new Vector3(xc, yb - 0.016f, z + 0.115f), new Vector3(0.11f, 0.004f, 0.026f), Sh(ScreenCol, 1.3f));            // sign panel (dark, unlit)
                    Bx(mb, new Vector3(xc - s * 0.15f, yb - 0.016f, z + 0.115f), new Vector3(0.024f, 0.005f, 0.024f), Sh(CoverCol, 1f));      // call button
                }
            }
        }

        static void EndQuad(MeshBuilder mb, float xa, float xb, float ya, float yb, float z, float fz, Color32 col)
        {
            Face(mb, new Vector3(xa, ya, z), new Vector3(xa, yb, z), new Vector3(xb, yb, z), new Vector3(xb, ya, z), new Vector3(0f, 0f, fz), col);
        }

        /// <summary>Front end: two partitions and a doorway closed by a pleated curtain.</summary>
        static void BuildFrontBulkhead(MeshBuilder mb)
        {
            float z = ZF - EndInset;
            const float doorHalf = 0.47f, doorTop = 1.98f;
            for (int s = -1; s <= 1; s += 2)
            {
                EndQuad(mb, s * doorHalf, s * 1.92f, -0.02f, 0.16f, z, -1f, Sh(DadoCol, 0.95f));
                EndQuad(mb, s * doorHalf, s * 1.92f, 0.16f, 0.172f, z, -1f, Sh(DadoCol, 0.7f));
                EndQuad(mb, s * doorHalf, s * 1.92f, 0.172f, doorTop, z, -1f, Sh(BezelCol, s < 0 ? 0.96f : 0.945f));
                EndQuad(mb, s * doorHalf, s * 1.92f, doorTop, 2.32f, z, -1f, Sh(BezelCol, s < 0 ? 0.96f : 0.945f));
                Bx(mb, new Vector3(s * doorHalf, 1.0f, z - 0.02f), new Vector3(0.05f, 2.04f, 0.06f), Sh(LiningCol, 0.88f));                 // door post
                Bx(mb, new Vector3(s * 1.10f, 1.40f, z - 0.007f), new Vector3(0.62f, 0.36f, 0.014f), Sh(PlasticDarkCol, 1f));              // framed panel
                Bx(mb, new Vector3(s * 1.10f, 1.40f, z - 0.016f), new Vector3(0.56f, 0.30f, 0.006f), Sh(ScreenCol, 1f));
                Bx(mb, new Vector3(s * 1.10f, 0.66f, z - 0.022f), new Vector3(0.52f, 0.24f, 0.044f), Sh(LiningCol, 0.86f));                // literature pocket
                Bx(mb, new Vector3(s * 1.10f, 0.775f, z - 0.03f), new Vector3(0.54f, 0.02f, 0.064f), Sh(LiningCol, 0.72f));
            }
            EndQuad(mb, -doorHalf, doorHalf, doorTop, 2.32f, z, -1f, Sh(BezelCol, 0.93f));
            Bx(mb, new Vector3(0f, doorTop - 0.005f, z - 0.03f), new Vector3(doorHalf * 2f + 0.04f, 0.035f, 0.05f), Sh(PlasticDarkCol, 0.9f));   // curtain rail
            const int pleats = 22;
            for (int k = 0; k < pleats; k++)
            {
                float xa = -doorHalf + 2f * doorHalf * k / pleats, xb = -doorHalf + 2f * doorHalf * (k + 1) / pleats;
                float za = z - ((k & 1) == 0 ? 0.008f : 0.042f), zb = z - ((k & 1) == 0 ? 0.042f : 0.008f);
                QuadF(mb, new Vector3(xa, 0.035f, za), new Vector3(xa, doorTop, za), new Vector3(xb, doorTop, zb), new Vector3(xb, 0.035f, zb),
                      Sh(CurtainCol, (k & 1) == 0 ? 1.05f : 0.78f), Vector3.back);
            }
        }

        /// <summary>Rear end: a galley face (trolleys, counter recess with ovens, containers above).</summary>
        static void BuildGalleyWall(MeshBuilder mb)
        {
            var rng = new Rng(1207u);
            float z = ZR + EndInset;
            EndQuad(mb, -1.92f, 1.92f, -0.02f, 2.32f, z, 1f, Sh(Palette.MetalPale, 0.78f));
            for (int s = -1; s <= 1; s += 2)
                Bx(mb, new Vector3(s * 1.60f, 1.15f, z + 0.006f), new Vector3(0.66f, 2.34f, 0.012f), Sh(LiningCol, 0.94f));                // plain side panels
            for (int i = 0; i < 8; i++)                                                                                                 // trolleys
            {
                float x = -1.085f + i * 0.31f;
                Bx(mb, new Vector3(x, 0.53f, z + 0.009f), new Vector3(0.29f, 0.90f, 0.018f), Sh(Palette.MetalPale, 0.93f + rng.Signed() * 0.035f));
                Bx(mb, new Vector3(x, 0.90f, z + 0.024f), new Vector3(0.21f, 0.024f, 0.014f), Sh(PlasticDarkCol, 0.8f));
                Bx(mb, new Vector3(x, 0.17f, z + 0.019f), new Vector3(0.20f, 0.03f, 0.004f), Sh(GasketCol, 0.9f));
            }
            Bx(mb, new Vector3(0f, 1.14f, z + 0.004f), new Vector3(2.50f, 0.28f, 0.008f), Sh(LinerCol, 1.1f));                               // counter recess
            Bx(mb, new Vector3(0f, 0.992f, z + 0.03f), new Vector3(2.50f, 0.02f, 0.06f), Sh(Palette.MetalPale, 0.98f));                    // counter edge
            for (int i = -1; i <= 1; i++)                                                                                               // ovens
            {
                Bx(mb, new Vector3(i * 0.62f, 1.14f, z + 0.016f), new Vector3(0.36f, 0.23f, 0.018f), Sh(Palette.MetalPale, 0.84f));
                Bx(mb, new Vector3(i * 0.62f - 0.03f, 1.15f, z + 0.027f), new Vector3(0.22f, 0.13f, 0.006f), Sh(ScreenCol, 1.1f));
                Bx(mb, new Vector3(i * 0.62f + 0.14f, 1.15f, z + 0.03f), new Vector3(0.02f, 0.14f, 0.012f), Sh(PlasticDarkCol, 0.8f));
            }
            for (int r = 0; r < 2; r++)                                                                                                 // containers
                for (int i = 0; i < 6; i++)
                {
                    float x = -1.0325f + i * 0.413f, y = 1.47f + r * 0.32f;
                    Bx(mb, new Vector3(x, y, z + 0.009f), new Vector3(0.39f, 0.29f, 0.018f), Sh(Palette.MetalPale, 0.90f + rng.Signed() * 0.035f));
                    Bx(mb, new Vector3(x, y + 0.10f, z + 0.022f), new Vector3(0.055f, 0.026f, 0.010f), Sh(PlasticDarkCol, 0.8f));
                }
        }

        // =====================================================================================================
        // 2. seats
        // =====================================================================================================
        const float SeatX0 = PlaneDef.AisleHalfWidth + 0.28f, SeatDX = 0.47f;      // seat centres at |x| = 0.54, 1.01, 1.48
        const float SeatRecline = 9f, BackPivotY = 0.43f, BackPivotZ = -0.20f;
        const float CoverFrontH = 0.50f, CoverRearH = 0.64f;                      // lower edges of the headrest cover along the back
        static readonly float SinR = Mathf.Sin(SeatRecline * Mathf.Deg2Rad), CosR = Mathf.Cos(SeatRecline * Mathf.Deg2Rad);
        static readonly float BackLen = (PlaneDef.SeatBackTopY - BackPivotY) / CosR;
        static readonly int RowMin = Mathf.CeilToInt((ZR + 0.66f - PlaneDef.SeatZ0) / PlaneDef.SeatPitch);
        static readonly int RowMax = Mathf.FloorToInt((ZF - 0.80f - PlaneDef.SeatZ0) / PlaneDef.SeatPitch);

        /// <summary>Point of the reclined seat back in seat coordinates: h along the back from its pivot, d toward the sitter, w sideways.</summary>
        static Vector3 BackPt(float h, float d, float w) { return new Vector3(w, BackPivotY + h * CosR + d * SinR, BackPivotZ - h * SinR + d * CosR); }

        static void BackWedge(MeshBuilder mb, float h0, float h1, float w0, float w1, float dA0, float dA1, float dB0, float dB1, Color32 col)
        {
            Hexa(mb, BackPt(h0, dA0, w0), BackPt(h0, dA0, w1), BackPt(h0, dA1, w1), BackPt(h0, dA1, w0),
                     BackPt(h1, dB0, w0), BackPt(h1, dB0, w1), BackPt(h1, dB1, w1), BackPt(h1, dB1, w0), col);
        }

        static void BackBox(MeshBuilder mb, float h0, float h1, float w0, float w1, float d0, float d1, Color32 col) { BackWedge(mb, h0, h1, w0, w1, d0, d1, d0, d1, col); }

        static void BuildSeats(MeshBuilder mb)
        {
            var rng = new Rng(4217u);
            for (int k = RowMin; k <= RowMax; k++)
            {
                float rz = PlaneDef.SeatZ0 + k * PlaneDef.SeatPitch;
                int detail = (k >= -1 && k <= 2) ? 1 : 0;
                for (int s = -1; s <= 1; s += 2)
                {
                    for (int i = 0; i < 3; i++)
                    {
                        bool player = k == 0 && s < 0 && i == 2;                 // the player's own seat: cushion only (the eye is where the head would be)
                        mb.Push(new Vector3(s * (SeatX0 + i * SeatDX), 0f, rz), Quaternion.identity);
                        float tone = 1f + rng.Signed() * 0.035f;
                        SeatCushion(mb, tone);
                        if (!player) { SeatBack(mb, detail, tone); SeatBelt(mb); }
                        mb.Pop();
                    }
                    SeatBlockFrame(mb, s, rz);
                }
            }
        }

        static readonly float[] CushionProf = {       // side view (z, y), clockwise seen from the right: rear bottom, rear top, top, waterfall front, underside
            -0.205f, 0.345f,   -0.205f, 0.440f,   0.150f, 0.462f,   0.212f, 0.452f,   0.246f, 0.420f,   0.243f, 0.365f,   0.200f, 0.345f };

        static void SeatCushion(MeshBuilder mb, float tone)
        {
            const float hw = 0.208f, pw = 0.014f;
            int n = CushionProf.Length / 2;
            Vector2 cen = Vector2.zero;
            for (int i = 0; i < n; i++) cen += new Vector2(CushionProf[i * 2], CushionProf[i * 2 + 1]);
            cen /= n;
            Color32 fabric = Sh(FabricCol, tone), piping = Sh(PipingCol, 1f), side = Sh(FabricCol, 0.80f * tone), under = Sh(PlasticDarkCol, 0.7f);
            for (int i = 0; i < n; i++)
            {
                int k = (i + 1) % n;
                float za = CushionProf[i * 2], ya = CushionProf[i * 2 + 1], zb = CushionProf[k * 2], yb = CushionProf[k * 2 + 1];
                Vector3 facing = new Vector3(0f, (ya + yb) * 0.5f - cen.y, (za + zb) * 0.5f - cen.x);
                bool top = i >= 1 && i <= 3;                                   // top and waterfall: piping along both side edges
                Color32 mid = top ? (i == 3 ? Sh(FabricCol, 0.9f * tone) : fabric) : i == 4 ? Sh(FabricCol, 0.74f * tone) : i == 0 ? side : under, edge = top ? piping : mid;
                QuadF(mb, new Vector3(-hw, ya, za), new Vector3(-hw + pw, ya, za), new Vector3(-hw + pw, yb, zb), new Vector3(-hw, yb, zb), edge, facing);
                QuadF(mb, new Vector3(-hw + pw, ya, za), new Vector3(hw - pw, ya, za), new Vector3(hw - pw, yb, zb), new Vector3(-hw + pw, yb, zb), mid, facing);
                QuadF(mb, new Vector3(hw - pw, ya, za), new Vector3(hw, ya, za), new Vector3(hw, yb, zb), new Vector3(hw - pw, yb, zb), edge, facing);
                for (int s = -1; s <= 1; s += 2)                               // side caps
                    TriF(mb, new Vector3(s * hw, cen.y, cen.x), new Vector3(s * hw, ya, za), new Vector3(s * hw, yb, zb), side, new Vector3(s, 0f, 0f));
            }
        }

        static float BackHalfWidth(float h)
        {
            const float rc = 0.055f;
            if (h < 0.10f) return Mathf.Lerp(0.192f, 0.208f, h / 0.10f);
            if (h < 0.44f) return 0.208f;
            if (h < 0.58f) return Mathf.Lerp(0.208f, 0.186f, Smooth((h - 0.44f) / 0.14f));
            float hc = BackLen - rc;
            if (h < hc) return 0.186f;
            float t = (h - hc) / rc;
            return 0.186f - rc + rc * Mathf.Sqrt(Mathf.Max(0f, 1f - t * t));    // rounded top corners
        }

        static readonly float[] BackFrontKeys = { 0f, 0.035f, 0.10f, 0.050f, 0.30f, 0.042f, 0.50f, 0.036f, 0.66f, 0.030f, 0.7189f, 0.014f };
        static readonly float[] BackRearKeys = { 0f, 0.050f, 0.30f, 0.046f, 0.50f, 0.042f, 0.66f, 0.034f, 0.7189f, 0.016f };

        static Vector3[] BackRing(float h, int detail)
        {
            float hw = BackHalfWidth(h), df = Key(h, BackFrontKeys), dr = Key(h, BackRearKeys);
            float bf = Mathf.Min(0.014f, df * 0.6f), br = Mathf.Min(0.022f, dr * 0.6f);
            if (detail == 0)
                return new[] { BackPt(h, df, -hw + bf), BackPt(h, df, hw - bf), BackPt(h, df - bf, hw), BackPt(h, -dr + br, hw),
                               BackPt(h, -dr, hw - br), BackPt(h, -dr, -hw + br), BackPt(h, -dr + br, -hw), BackPt(h, df - bf, -hw) };
            float fade = 1f - Smooth((h - 0.45f) / 0.25f), cv = 0.008f * fade, bu = 0.007f * (0.4f + 0.6f * fade);     // dished front, slightly bulged shell
            return new[] { BackPt(h, df, -hw + bf), BackPt(h, df - cv, -hw * 0.36f), BackPt(h, df - cv, hw * 0.36f), BackPt(h, df, hw - bf), BackPt(h, df - bf, hw), BackPt(h, -dr + br, hw),
                           BackPt(h, -dr, hw - br), BackPt(h, -dr - bu, hw * 0.36f), BackPt(h, -dr - bu, -hw * 0.36f), BackPt(h, -dr, -hw + br), BackPt(h, -dr + br, -hw), BackPt(h, df - bf, -hw) };
        }

        // face kinds around the ring: 0 front, 1 piping (front bevel), 2 side, 3 rear bevel, 4 rear
        static readonly int[] KindHi = { 0, 0, 0, 1, 2, 3, 4, 4, 4, 3, 2, 1 };
        static readonly int[] KindLo = { 0, 1, 2, 3, 4, 3, 2, 1 };

        static void SeatBack(MeshBuilder mb, int detail, float tone)
        {
            float L = BackLen;
            float[] hs = detail > 0 ? new[] { 0f, 0.05f, 0.10f, 0.20f, 0.30f, 0.40f, CoverFrontH, 0.54f, 0.58f, CoverRearH, L - 0.055f, L - 0.036f, L - 0.020f, L - 0.008f, L }
                                    : new[] { 0f, 0.10f, 0.30f, CoverFrontH, 0.58f, CoverRearH, L - 0.030f, L };
            int[] kinds = detail > 0 ? KindHi : KindLo;
            int m = kinds.Length;
            var ring = new Vector3[hs.Length][];
            for (int l = 0; l < hs.Length; l++) ring[l] = BackRing(hs[l], detail);
            for (int l = 0; l < hs.Length - 1; l++)
            {
                float hm = (hs[l] + hs[l + 1]) * 0.5f;
                Vector3 axis = BackPt(hm, 0f, 0f);
                bool cf = hm > CoverFrontH, cr = hm > CoverRearH;
                for (int k = 0; k < m; k++)
                {
                    int kn = (k + 1) % m;
                    Color32 col;
                    switch (kinds[k])
                    {
                        case 0: col = cf ? Sh(CoverCol, 1f) : Sh(FabricCol, tone); break;
                        case 1: col = cf ? Sh(CoverCol, 0.90f) : Sh(PipingCol, 1f); break;
                        case 2: col = cr ? Sh(CoverCol, 0.90f) : Sh(FabricCol, 0.80f * tone); break;
                        case 3: col = cr ? Sh(CoverCol, 0.86f) : Sh(FabricCol, 0.70f * tone); break;
                        default: col = cr ? Sh(CoverCol, 0.95f) : Sh(FabricCol, 0.88f * tone); break;
                    }
                    Vector3 mid = (ring[l][k] + ring[l][kn] + ring[l + 1][k] + ring[l + 1][kn]) * 0.25f;
                    QuadF(mb, ring[l][k], ring[l][kn], ring[l + 1][kn], ring[l + 1][k], col, mid - axis);
                }
            }
            Vector3[] top = ring[hs.Length - 1];
            Vector3 tc = Vector3.zero;
            for (int k = 0; k < m; k++) tc += top[k];
            tc /= m;
            for (int k = 0; k < m; k++) TriF(mb, tc, top[k], top[(k + 1) % m], Sh(CoverCol, 0.97f), new Vector3(0f, CosR, -SinR));

            // headrest: pad and two wings under the pale cover
            Color32 cover = Sh(CoverCol, 1.02f), coverSide = Sh(CoverCol, 0.95f);
            BackBox(mb, 0.515f, 0.695f, -0.118f, 0.118f, 0.015f, 0.060f, cover);
            for (int s = -1; s <= 1; s += 2)
                Hexa(mb, BackPt(0.528f, 0.026f, s * 0.116f), BackPt(0.535f, 0.066f, s * 0.184f), BackPt(0.535f, 0.086f, s * 0.170f), BackPt(0.528f, 0.058f, s * 0.108f),
                         BackPt(0.688f, 0.026f, s * 0.116f), BackPt(0.680f, 0.066f, s * 0.184f), BackPt(0.680f, 0.086f, s * 0.170f), BackPt(0.688f, 0.058f, s * 0.108f), coverSide);
            if (detail > 0) BackBox(mb, 0.503f, 0.515f, -0.122f, 0.122f, 0.015f, 0.063f, Sh(PipingCol, 1f));

            // rear: folded tray table with its latch, seat pocket
            if (detail > 0)
            {
                BackBox(mb, 0.250f, 0.502f, -0.193f, 0.193f, -0.063f, -0.030f, Sh(PlasticDarkCol, 1f));          // tray surround
                BackBox(mb, 0.259f, 0.493f, -0.184f, 0.184f, -0.073f, -0.030f, Sh(PlasticCol, 1f));             // tray
                BackBox(mb, 0.268f, 0.300f, -0.170f, 0.170f, -0.076f, -0.060f, Sh(PlasticCol, 0.90f));          // tray lip
                BackBox(mb, 0.497f, 0.527f, -0.022f, 0.022f, -0.062f, -0.035f, Sh(PlasticDarkCol, 0.85f));      // latch: base and turn tab
                BackBox(mb, 0.470f, 0.514f, -0.013f, 0.013f, -0.082f, -0.060f, Sh(PlasticDarkCol, 0.62f));
                BackBox(mb, 0.205f, 0.246f, -0.030f, 0.105f, -0.076f, -0.058f, Sh(CoverCol, 0.97f));            // card in the pocket
                BackWedge(mb, 0.030f, 0.215f, -0.176f, 0.176f, -0.045f, -0.064f, -0.045f, -0.088f, Sh(PipingCol, 1.05f));   // pocket
                BackBox(mb, 0.198f, 0.218f, -0.180f, 0.180f, -0.092f, -0.045f, Sh(PipingCol, 1.6f));            // pocket hem
            }
            else
            {
                BackBox(mb, 0.255f, 0.497f, -0.188f, 0.188f, -0.070f, -0.030f, Sh(PlasticCol, 0.98f));
                BackWedge(mb, 0.030f, 0.215f, -0.176f, 0.176f, -0.045f, -0.062f, -0.045f, -0.086f, Sh(PipingCol, 1.1f));
            }
        }

        static void SeatBelt(MeshBuilder mb)
        {
            Color32 strap = Sh(PlasticDarkCol, 0.62f);
            Beam(mb, new Vector3(-0.195f, 0.449f, -0.14f), new Vector3(-0.025f, 0.462f, 0.05f), 0.042f, 0.007f, strap);
            Beam(mb, new Vector3(0.195f, 0.449f, -0.14f), new Vector3(0.050f, 0.461f, 0.03f), 0.042f, 0.007f, strap);
            Bx(mb, new Vector3(0.005f, 0.468f, 0.045f), new Vector3(0.062f, 0.014f, 0.052f), Sh(Palette.MetalPale, 0.92f));       // buckle
        }

        /// <summary>What a block of three seats shares: four armrests, the beams under the cushions, legs on the floor rails, the baggage bar.</summary>
        static void SeatBlockFrame(MeshBuilder mb, float s, float rz)
        {
            Color32 pad = Sh(ArmPadCol), plastic = Sh(PlasticCol, 0.92f), metal = Sh(SeatMetalCol);
            for (int i = 0; i < 4; i++)
            {
                float ax = s * (SeatX0 - SeatDX * 0.5f + i * SeatDX);
                Bx(mb, new Vector3(ax, 0.615f, rz - 0.015f), new Vector3(0.050f, 0.030f, 0.37f), pad);
                Hexa(mb, new Vector3(ax - 0.025f, 0.600f, rz + 0.17f), new Vector3(ax + 0.025f, 0.600f, rz + 0.17f), new Vector3(ax + 0.025f, 0.630f, rz + 0.17f), new Vector3(ax - 0.025f, 0.630f, rz + 0.17f),
                         new Vector3(ax - 0.019f, 0.604f, rz + 0.212f), new Vector3(ax + 0.019f, 0.604f, rz + 0.212f), new Vector3(ax + 0.019f, 0.622f, rz + 0.212f), new Vector3(ax - 0.019f, 0.622f, rz + 0.212f), pad);
                if (i == 0) Bx(mb, new Vector3(ax, 0.41f, rz - 0.03f), new Vector3(0.032f, 0.39f, 0.33f), plastic);          // aisle end panel
                else Bx(mb, new Vector3(ax, 0.47f, rz - 0.135f), new Vector3(0.034f, 0.27f, 0.11f), plastic);                // armrest support
            }
            float xm = s * (SeatX0 + SeatDX), wBlock = SeatDX * 3f - 0.03f;
            Bx(mb, new Vector3(xm, 0.325f, rz + 0.13f), new Vector3(wBlock, 0.040f, 0.050f), metal);
            Bx(mb, new Vector3(xm, 0.325f, rz - 0.15f), new Vector3(wBlock, 0.040f, 0.050f), metal);
            Bx(mb, new Vector3(xm, 0.10f, rz + 0.215f), new Vector3(wBlock - 0.04f, 0.020f, 0.020f), metal);                 // baggage bar
            for (int i = 1; i <= 2; i++)
            {
                float lx = s * (SeatX0 - SeatDX * 0.5f + i * SeatDX);
                Beam(mb, new Vector3(lx, 0.31f, rz + 0.13f), new Vector3(lx, 0.0f, rz + 0.20f), 0.032f, 0.040f, metal);
                Beam(mb, new Vector3(lx, 0.31f, rz - 0.15f), new Vector3(lx, 0.0f, rz - 0.24f), 0.032f, 0.040f, metal);
                Beam(mb, new Vector3(lx, 0.05f, rz + 0.19f), new Vector3(lx, 0.05f, rz - 0.225f), 0.026f, 0.026f, metal);
            }
        }

        // =====================================================================================================
        // 3. outer skin around the wing root (with the wing-to-body fairing)
        // =====================================================================================================
        const float FuseAxisY = 0.83f;
        static readonly float[] FairingProf = {     // (x, y) of the fairing section, left side, from under the window belt round to the belly
            -1.9556f, 0.72f,  -1.9215f, 0.45f,  -1.930f, 0.30f,  -2.000f, 0.12f,  -2.040f, -0.15f,  -2.050f, -0.60f,  -2.030f, -1.00f,
            -1.930f, -1.30f,  -1.600f, -1.50f,  -0.900f, -1.58f,  0f, -1.60f,  0.35f, -1.60f };

        static Vector2 ProfPoint(float[] prof, float t)   // Catmull-Rom through the profile points, t in 0..1
        {
            int n = prof.Length / 2; float f = Mathf.Clamp01(t) * (n - 1); int i = Mathf.Min((int)f, n - 2); f -= i;
            int i0 = Mathf.Max(i - 1, 0), i3 = Mathf.Min(i + 2, n - 1);
            Vector2 p0 = new Vector2(prof[i0 * 2], prof[i0 * 2 + 1]), p1 = new Vector2(prof[i * 2], prof[i * 2 + 1]);
            Vector2 p2 = new Vector2(prof[i * 2 + 2], prof[i * 2 + 3]), p3 = new Vector2(prof[i3 * 2], prof[i3 * 2 + 1]);
            return 0.5f * ((2f * p1) + (p2 - p0) * f + (2f * p0 - 5f * p1 + 4f * p2 - p3) * (f * f) + (3f * p1 - p0 - 3f * p2 + p3) * (f * f * f));
        }

        static void BuildFuselageStub(MeshBuilder mb)
        {
            float R = Mathf.Sqrt(PlaneDef.SkinX * PlaneDef.SkinX + (PlaneDef.WindowY - FuseAxisY) * (PlaneDef.WindowY - FuseAxisY));
            float a0 = Mathf.Asin((FuseAxisY - 0.72f) / R), a1 = 100f * Mathf.Deg2Rad;
            const int nq = 26;
            var zs = new List<float>(); var line = new List<bool>();
            for (int k = 0; k < 25; k++)
            {
                float z = 2.6f - k * 0.56f;
                zs.Add(z); line.Add(false);
                if (k % 3 == 1) { zs.Add(z - 0.02f); line[line.Count - 1] = true; line.Add(false); }      // frame lines
            }
            var G = new Vector3[zs.Count, nq + 1];
            var blend = new float[zs.Count];
            for (int i = 0; i < zs.Count; i++)
            {
                float z = zs[i];
                float f = Smooth((1.6f - z) / 2.2f) * Smooth((z + 10.4f) / 2.4f);
                blend[i] = f;
                for (int q = 0; q <= nq; q++)
                {
                    float t = q / (float)nq, ang = Mathf.Lerp(a0, a1, t);
                    Vector2 c = new Vector2(-R * Mathf.Cos(ang), FuseAxisY - R * Mathf.Sin(ang)), fp = ProfPoint(FairingProf, t);
                    Vector2 p = c + (fp - c) * f;
                    G[i, q] = new Vector3(p.x, p.y, z);
                }
            }
            Grid(mb, G, false, (i, q) => new Vector3(G[i, q].x, G[i, q].y + 0.2f, 0f), (i, q) =>
            {
                if (line[i]) return Sh(PaintCol, LineTone + 0.1f);
                bool fairing = blend[i] > 0.5f && q >= 3;
                if (fairing && q == 3) return Sh(PaintCol, LineTone + 0.1f);
                return Sh(PaintCol, (fairing ? 0.86f : 0.93f) * (1f + (Hash01(i * 7 + q / 5) - 0.5f) * 0.03f));
            });
        }

        // =====================================================================================================
        // 4. left wing (wing-local coordinates: origin = PlaneDef.WingRoot, the wing runs toward -x)
        // =====================================================================================================
        const float WingKinkS = 0.36f, WingKinkTE = 0.25f, WingLineW = 0.026f;
        const float CamM = 0.015f, CamP = 0.4f;                                     // camber of the section
        const float SlatU = 0.135f, SpoilU = 0.66f, FlapU = 0.78f, SlatLoU = 0.08f, FlapLoU = 0.74f;
        const float WingletBlendR = 0.55f, WingletCantDeg = 78f, WingletSweepDeg = 50f, WingletTipChord = 0.60f;
        static readonly float TanSweep = Mathf.Tan(PlaneDef.WingSweepDeg * Mathf.Deg2Rad);
        static readonly float DihTan = Mathf.Tan(PlaneDef.WingDihedralDeg * Mathf.Deg2Rad);
        static readonly float DihLin = DihTan * 0.70f, DihQuad = DihTan * 0.30f;      // the wing bends up a little toward the tip

        struct WSt { public Vector3 le, up; public float chord, tc, s; }

        /// <summary>Section of the main wing at span fraction s (0 = fuselage side, 1 = tip). 'le' is the leading edge at the height of the top surface.</summary>
        static WSt WingStation(float s)
        {
            float S = PlaneDef.WingSemiSpan;
            float zLe = -S * s * TanSweep;
            float teKink = -(PlaneDef.WingRootChord + WingKinkTE), teTip = -(S * TanSweep + PlaneDef.WingTipChord);
            float zTe = s <= WingKinkS ? -(PlaneDef.WingRootChord + WingKinkTE * s / WingKinkS) : teKink + (teTip - teKink) * (s - WingKinkS) / (1f - WingKinkS);
            float phi = Mathf.Atan(DihLin + 2f * DihQuad * Mathf.Max(s, 0f));
            float y = s >= 0f ? S * (DihLin * s + DihQuad * s * s) : S * DihLin * s;
            return new WSt { le = new Vector3(-S * s, y, zLe), up = new Vector3(Mathf.Sin(phi), Mathf.Cos(phi), 0f), chord = zLe - zTe, tc = 0.11f + (0.09f - 0.11f) * Mathf.Clamp01(s), s = s };
        }

        static float WingletArc() { return WingletBlendR * (WingletCantDeg * Mathf.Deg2Rad - Mathf.Atan(DihLin + 2f * DihQuad)); }
        static float WingletLength()
        {
            float phi0 = Mathf.Atan(DihLin + 2f * DihQuad), phi1 = WingletCantDeg * Mathf.Deg2Rad;
            return WingletArc() + (PlaneDef.WingletHeight - WingletBlendR * (Mathf.Cos(phi0) - Mathf.Cos(phi1))) / Mathf.Sin(phi1);
        }

        /// <summary>Section of the winglet at path length l from the wing tip (a blend arc, then straight up at the cant angle).</summary>
        static WSt WingletStation(float l)
        {
            WSt tip = WingStation(1f);
            float phi0 = Mathf.Atan(DihLin + 2f * DihQuad), phi1 = WingletCantDeg * Mathf.Deg2Rad, arc = WingletArc(), lMax = WingletLength();
            float la = Mathf.Min(l, arc), ls = Mathf.Max(0f, l - arc), phi = phi0 + la / WingletBlendR;
            float dx = -WingletBlendR * (Mathf.Sin(phi) - Mathf.Sin(phi0)) - ls * Mathf.Cos(phi1);
            float dy = WingletBlendR * (Mathf.Cos(phi0) - Mathf.Cos(phi)) + ls * Mathf.Sin(phi1);
            float t0 = TanSweep, t1 = Mathf.Tan(WingletSweepDeg * Mathf.Deg2Rad);
            float dz = -(t0 * la + (t1 - t0) * la * la / (2f * arc) + t1 * ls);
            float f = Mathf.Clamp01(l / lMax);
            return new WSt { le = tip.le + new Vector3(dx, dy, dz), up = new Vector3(Mathf.Sin(phi), Mathf.Cos(phi), 0f),
                             chord = tip.chord + (WingletTipChord - tip.chord) * f, tc = 0.09f + (0.08f - 0.09f) * f, s = 1f + l / lMax };
        }

        /// <summary>Point of the aerofoil at chord fraction u (0 = leading edge). The crest of the upper surface lies on the station's reference line.</summary>
        static Vector3 WingPoint(WSt st, float u, bool upper)
        {
            float yt = st.tc / 0.2f * (0.2969f * Mathf.Sqrt(u) - 0.1260f * u - 0.3516f * u * u + 0.2843f * u * u * u - 0.1036f * u * u * u * u);
            float yc = u < CamP ? CamM / (CamP * CamP) * (2f * CamP * u - u * u) : CamM / ((1f - CamP) * (1f - CamP)) * (1f - 2f * CamP + 2f * CamP * u - u * u);
            float y = (upper ? yc + yt : yc - yt) - (CamM + st.tc * 0.5f);
            return st.le + new Vector3(0f, 0f, -u * st.chord) + st.up * (y * st.chord);
        }

        static void AddSpan(List<float> l, float a, float b, int n) { for (int k = 1; k <= n; k++) l.Add(k == n ? b : a + (b - a) * k / n); }

        // chordwise nodes; thin cells carry the panel lines: upper cells 10 (slat), 18 (spoiler hinge), 22 (flap); lower cells 7 and 15
        static void WingKnots(float chord, List<float> up, List<float> lo)
        {
            float du = Mathf.Min(WingLineW / chord, 0.045f);
            up.Clear(); lo.Clear();
            for (int k = 0; k <= 10; k++) up.Add(k == 10 ? SlatU : SlatU * Mathf.Pow(k / 10f, 2.4f));
            up.Add(SlatU + du);
            AddSpan(up, SlatU + du, SpoilU - du, 7);
            up.Add(SpoilU);
            AddSpan(up, SpoilU, FlapU - du, 3);
            up.Add(FlapU);
            AddSpan(up, FlapU, 1f, 4);
            for (int k = 0; k <= 7; k++) lo.Add(k == 7 ? SlatLoU : SlatLoU * Mathf.Pow(k / 7f, 2.4f));
            lo.Add(SlatLoU + du);
            AddSpan(lo, SlatLoU + du, FlapLoU - du, 7);
            lo.Add(FlapLoU);
            AddSpan(lo, FlapLoU, 1f, 3);
        }

        static Color32 WingCol(float s, int mask, bool upper, int cell)
        {
            Color32 line = Sh(PaintCol, LineTone);
            if (s > 1f) return Sh(PaintCol, upper ? 0.97f : 0.90f);                        // winglet
            if (s < 0f) return Sh(PaintCol, 0.9f);
            bool slatSpan = s > 0.07f && s < 0.9767f, spoilSpan = (s > 0.10f && s < 0.2017f) || (s > 0.38f && s < 0.7217f);
            bool flapSpan = s > 0.04f && s < 0.7617f, ailSpan = s > 0.78f && s < 0.9567f;
            float var = 1f + (Hash01(Mathf.FloorToInt(s * 11f) * 13 + cell / 5 + (upper ? 0 : 97)) - 0.5f) * 0.035f;
            if (upper)
            {
                if ((mask & 8) != 0) return line;
                if (cell == 10 && slatSpan) return line;
                if (cell == 18 && spoilSpan) return line;
                if (cell == 22 && (flapSpan || ailSpan)) return line;
                if ((mask & 1) != 0 && cell <= 10) return line;
                if ((mask & 2) != 0 && cell >= 18 && cell <= 22) return line;
                if ((mask & 4) != 0 && cell >= 22) return line;
                if (cell < 10 && slatSpan) return Sh(SlatCol, 1.02f * var);                  // bare metal slats
                if (cell > 18 && cell < 22 && spoilSpan) return Sh(PaintCol, 0.86f * var);
                if (cell > 22 && flapSpan) return Sh(PaintCol, 0.90f * var);
                if (cell > 22 && ailSpan) return Sh(PaintCol, 0.92f * var);
                return Sh(PaintCol, 0.95f * var);
            }
            if ((mask & 8) != 0) return line;
            if (cell == 7 && slatSpan) return line;
            if (cell == 15 && (flapSpan || ailSpan)) return line;
            if ((mask & 1) != 0 && cell <= 7) return line;
            if ((mask & 4) != 0 && cell >= 15) return line;
            if (cell < 7 && slatSpan) return Sh(SlatCol, 0.92f * var);
            return Sh(PaintCol, 0.86f * var);
        }

        static void BuildWing(MeshBuilder mb)
        {
            // span stations; a thin pair of stations carries each chordwise panel line (mask: 1 slat break, 2 spoiler edge, 4 flap / aileron end, 8 full)
            float ds = WingLineW / PlaneDef.WingSemiSpan;
            var sl = new List<Vector2>();
            foreach (float s in new[] { -0.036f, 0f, 0.018f, 0.15f, 0.30f, 0.52f, 0.68f, 0.86f, 0.92f, 1f }) sl.Add(new Vector2(s, 0f));
            float[] feat = { 0.04f, 4, 0.07f, 1, 0.10f, 2, 0.20f, 2, 0.245f, 1, 0.36f, 4, 0.38f, 2, 0.42f, 1, 0.47f, 2, 0.55f, 2, 0.60f, 1, 0.635f, 2,
                             0.72f, 2, 0.76f, 4, 0.78f, 5, 0.955f, 4, 0.975f, 1, 0.992f, 8 };
            for (int i = 0; i < feat.Length; i += 2) { sl.Add(new Vector2(feat[i], feat[i + 1])); sl.Add(new Vector2(feat[i] + ds, 0f)); }
            sl.Sort((a, b) => a.x.CompareTo(b.x));
            var sts = new List<WSt>(); var masks = new List<int>();
            for (int i = 0; i < sl.Count; i++) { sts.Add(WingStation(sl[i].x)); masks.Add((int)sl[i].y); }
            // winglet: blend arc, straight part, rounded-off top
            float arc = WingletArc(), lMax = WingletLength();
            for (int i = 1; i <= 6; i++) { sts.Add(WingletStation(arc * i / 6f)); masks.Add(0); }
            for (int i = 1; i <= 4; i++) { sts.Add(WingletStation(arc + (lMax - arc) * i / 4f)); masks.Add(0); }
            {
                WSt c1 = WingletStation(lMax + 0.05f); c1.le += new Vector3(0f, 0f, -0.10f); c1.chord = WingletTipChord * 0.70f; c1.tc *= 0.6f; sts.Add(c1); masks.Add(0);
                WSt c2 = WingletStation(lMax + 0.085f); c2.le += new Vector3(0f, 0f, -0.27f); c2.chord = WingletTipChord * 0.32f; c2.tc = 0f; sts.Add(c2); masks.Add(0);
            }

            var up = new List<float>(); var lo = new List<float>();
            WingKnots(1f, up, lo);
            int nUp = up.Count, nLo = lo.Count, nj = nUp + nLo - 1, ni = sts.Count;
            var P = new Vector3[ni, nj]; var H = new Vector3[ni, nj];
            for (int i = 0; i < ni; i++)
            {
                WSt st = sts[i];
                WingKnots(st.chord, up, lo);
                for (int j = 0; j < nj; j++)
                {
                    bool upper = j < nUp; float u = upper ? up[nUp - 1 - j] : lo[j - nUp + 1];
                    P[i, j] = WingPoint(st, u, upper);
                    H[i, j] = (upper ? st.up : -st.up) + new Vector3(0f, 0f, 3f * Mathf.Max(0f, 1f - u / 0.04f));
                }
            }
            Grid(mb, P, false, (i, j) => H[i, j], (i, j) =>
            {
                bool upper = j < nUp - 1; int cell = upper ? nUp - 2 - j : j - (nUp - 1);
                return WingCol((sts[i].s + sts[i + 1].s) * 0.5f, masks[i], upper, cell);
            });

            // flap track fairings under the trailing edge
            Canoe(mb, 0.16f, 1.15f); Canoe(mb, 0.40f, 0.95f); Canoe(mb, 0.62f, 0.80f);

            // tip: red navigation light in the leading edge and a white strobe housing beside it
            WSt nav = WingStation(0.984f), strobe = WingStation(0.968f);
            mb.Sphere(WingPoint(nav, 0f, true) + new Vector3(0f, 0f, -0.012f), new Vector3(0.085f, 0.036f, 0.060f), 10, 6, Sh(Palette.FireRed, 1.1f, 1f));
            mb.Sphere(WingPoint(strobe, 0f, true) + new Vector3(0f, 0f, -0.012f), new Vector3(0.075f, 0.034f, 0.055f), 10, 6, Sh(Palette.SkyWhite, 1f, 0.22f));
        }

        static void Canoe(MeshBuilder mb, float s, float scale)
        {
            WSt st = WingStation(s);
            const int nz = 16, nr = 12; const float u0 = 0.42f, u1 = 1.17f;
            float hw = 0.19f * scale, hd = 0.29f * scale;
            Vector3 c0 = WingPoint(st, u0, false) - st.up * (hd * 0.50f);
            Vector3 c1 = WingPoint(st, 1f, false) + new Vector3(0f, 0f, -(u1 - 1f) * st.chord) - st.up * (hd * 0.85f);
            Vector3 lat = new Vector3(st.up.y, -st.up.x, 0f);
            var P = new Vector3[nz + 1, nr]; var C = new Vector3[nz + 1];
            for (int i = 0; i <= nz; i++)
            {
                float t = i / (float)nz, rho = (i == 0 || i == nz) ? 0f : Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Pow(t, 0.70f)), 0.62f);
                C[i] = c0 + (c1 - c0) * t;
                for (int j = 0; j < nr; j++)
                {
                    float a = j * Mathf.PI * 2f / nr;
                    P[i, j] = C[i] + lat * (Mathf.Cos(a) * hw * rho) + st.up * (Mathf.Sin(a) * hd * rho);
                }
            }
            Color32 col = Sh(PaintCol, 0.88f), dark = Sh(PaintCol, LineTone + 0.06f);
            Grid(mb, P, true, (i, j) => i == 0 ? Vector3.forward : i == nz ? Vector3.back : P[i, j] - C[i], (i, j) => i == 5 ? dark : col);
        }

        // =====================================================================================================
        // 5. engine nacelle (nacelle-local coordinates: origin = PlaneDef.NacelleCentre, axis along z, intake at +z)
        // =====================================================================================================

        /// <summary>Surface of revolution around z. zr = (z, r) pairs; walking along the profile the surface faces (dr, -dz):
        /// forward-running profiles face the axis, aft-running ones face outward, outward-running ones face forward.</summary>
        static void Revolve(MeshBuilder mb, float[] zr, float[] ang, System.Func<int, int, Color32> col)
        {
            int ni = zr.Length / 2, nj = ang.Length;
            var P = new Vector3[ni, nj]; var N = new Vector3[ni, nj];
            for (int i = 0; i < ni; i++)
            {
                int ia = Mathf.Max(i - 1, 0), ib = Mathf.Min(i + 1, ni - 1);
                float dz = zr[ib * 2] - zr[ia * 2], dr = zr[ib * 2 + 1] - zr[ia * 2 + 1], len = Mathf.Sqrt(dz * dz + dr * dr);
                float nz = dr / len, nr = -dz / len;
                for (int j = 0; j < nj; j++)
                {
                    float c = Mathf.Cos(ang[j]), s = Mathf.Sin(ang[j]);
                    P[i, j] = new Vector3(zr[i * 2 + 1] * c, zr[i * 2 + 1] * s, zr[i * 2]);
                    N[i, j] = new Vector3(nr * c, nr * s, nz);
                }
            }
            GridN(mb, P, N, true, col);
        }

        static void BuildNacelle(MeshBuilder mb)
        {
            float R = PlaneDef.NacelleRadius, hl = PlaneDef.NacelleLength * 0.5f;
            // angles round the axis (0 = toward the fuselage, 90 deg = top); thin cells carry the lengthwise panel lines
            var al = new List<float>();
            const int nSeg = 64; const float lw = 0.011f;
            float[] lineAng = { 72f * Mathf.Deg2Rad, 108f * Mathf.Deg2Rad, 270f * Mathf.Deg2Rad };
            for (int k = 0; k < nSeg; k++) al.Add(k * Mathf.PI * 2f / nSeg);
            foreach (float la in lineAng) { al.Add(la - lw); al.Add(la + lw); }
            al.Sort();
            float[] ang = al.ToArray();
            var isLine = new bool[ang.Length];
            for (int j = 0; j < ang.Length; j++)
            {
                float a0 = ang[j], a1 = j + 1 < ang.Length ? ang[j + 1] : ang[0] + Mathf.PI * 2f, mid = (a0 + a1) * 0.5f;
                foreach (float la in lineAng) if (Mathf.Abs(mid - la) < lw) isLine[j] = true;
            }
            Color32 line = Sh(PaintCol, LineTone), lip = Sh(LipCol, 1f), liner = Sh(LinerCol, 1f), dark = Sh(FanCol, 0.8f), hot = Sh(HotMetalCol, 1f);

            // intake duct -> lip -> cowl (one smooth run, scaled from the nominal radius 1.12 and half length 2.3)
            float[] cowl = {
                1.50f, 0.895f,  1.80f, 0.878f,  2.05f, 0.858f,  2.10f, 0.855f,  2.20f, 0.862f,  2.262f, 0.880f,  2.290f, 0.905f,  2.300f, 0.940f,
                2.290f, 0.975f,  2.262f, 1.008f,  2.215f, 1.040f,  2.140f, 1.068f,  2.030f, 1.090f,  2.010f, 1.093f,  1.700f, 1.110f,  1.300f, 1.118f,
                0.900f, 1.120f,  0.620f, 1.118f,  0.600f, 1.118f,  0.200f, 1.108f,  -0.280f, 1.080f,  -0.300f, 1.079f,  -0.800f, 1.020f,  -1.200f, 0.945f };
            float kz = hl / 2.3f, kr = R / 1.12f;
            for (int i = 0; i < cowl.Length; i += 2) { cowl[i] *= kz; cowl[i + 1] *= kr; }
            Revolve(mb, cowl, ang, (i, j) =>
            {
                if (i < 3) return Sh(LinerCol, i == 0 ? 0.8f : 1f);
                if (i < 12) return lip;
                if (i == 12 || i == 17 || i == 20) return line;
                if (isLine[j]) return line;
                int group = i < 17 ? 0 : i < 20 ? 1 : 2, sector = Mathf.FloorToInt(ang[j] / (Mathf.PI * 2f) * 6f);
                return Sh(PaintCol, (group == 2 ? 0.90f : 0.94f) * (1f + (Hash01(group * 17 + sector) - 0.5f) * 0.035f));
            });
            // fan nozzle: trailing edge, inside of the duct, its back wall, the core cowl, core nozzle and plug
            Revolve(mb, new[] { -1.200f * kz, 0.945f * kr, -1.200f * kz, 0.925f * kr }, ang, (i, j) => Sh(HotMetalCol, 0.8f));
            Revolve(mb, new[] { -1.200f * kz, 0.925f * kr, -0.850f * kz, 0.915f * kr }, ang, (i, j) => liner);
            Revolve(mb, new[] { -0.850f * kz, 0.915f * kr, -0.850f * kz, 0.600f * kr }, ang, (i, j) => dark);
            Revolve(mb, new[] { -0.850f * kz, 0.600f * kr, -1.250f * kz, 0.600f * kr, -1.600f * kz, 0.530f * kr, -1.950f * kz, 0.420f * kr }, ang, (i, j) => isLine[j] ? Sh(HotMetalCol, 0.75f) : Sh(HotMetalCol, i == 2 ? 0.92f : 1f));
            Revolve(mb, new[] { -1.950f * kz, 0.420f * kr, -1.950f * kz, 0.395f * kr }, ang, (i, j) => Sh(HotMetalCol, 0.7f));
            Revolve(mb, new[] { -1.950f * kz, 0.395f * kr, -1.780f * kz, 0.385f * kr }, ang, (i, j) => liner);
            Revolve(mb, new[] { -1.780f * kz, 0.385f * kr, -1.780f * kz, 0.250f * kr }, ang, (i, j) => dark);
            Revolve(mb, new[] { -1.780f * kz, 0.250f * kr, -2.000f * kz, 0.210f * kr, -2.200f * kz, 0.100f * kr, -2.300f * kz, 0f }, ang, (i, j) => Sh(HotMetalCol, 0.85f));
            // fan face, a hint of blades, spinner
            Revolve(mb, new[] { 1.500f * kz, 0.300f * kr, 1.500f * kz, 0.895f * kr }, ang, (i, j) => dark);
            const int blades = 22;
            for (int b = 0; b < blades; b++)
            {
                float a0 = b * Mathf.PI * 2f / blades, a1 = a0 + 0.62f * Mathf.PI * 2f / blades, tw = 0.16f;
                float r0 = 0.30f * kr, r1 = 0.885f * kr, zf = 1.60f * kz, zb = 1.515f * kz;
                QuadF(mb, new Vector3(r0 * Mathf.Cos(a0), r0 * Mathf.Sin(a0), zf), new Vector3(r1 * Mathf.Cos(a0 + tw), r1 * Mathf.Sin(a0 + tw), zf),
                          new Vector3(r1 * Mathf.Cos(a1 + tw), r1 * Mathf.Sin(a1 + tw), zb), new Vector3(r0 * Mathf.Cos(a1), r0 * Mathf.Sin(a1), zb),
                      Sh(FanCol, (b & 1) == 0 ? 1.5f : 1.15f), Vector3.forward);
            }
            Revolve(mb, new[] { 2.020f * kz, 0f, 1.940f * kz, 0.115f * kr, 1.800f * kz, 0.215f * kr, 1.620f * kz, 0.285f * kr, 1.500f * kz, 0.300f * kr }, ang, (i, j) => i == 0 ? Sh(FanCol, 1.6f) : Sh(SlatCol, 0.72f));

            // strake on the inboard shoulder (two-sided plate)
            {
                float a = 52f * Mathf.Deg2Rad; Vector3 er = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f), et = new Vector3(-Mathf.Sin(a), Mathf.Cos(a), 0f);
                Vector3 A = er * (1.100f * kr) + new Vector3(0f, 0f, 1.20f * kz), B = er * (1.105f * kr) + new Vector3(0f, 0f, 0.25f * kz);
                Vector3 C = er * (1.33f * kr) + new Vector3(0f, 0f, 0.25f * kz), D = er * (1.27f * kr) + new Vector3(0f, 0f, 0.62f * kz);
                Color32 sc = Sh(PaintCol, 0.90f);
                QuadF(mb, A, B, C, D, sc, et); QuadF(mb, A, B, C, D, sc, -et);
            }
            BuildPylon(mb);
        }

        static readonly float[] PylonTop = { -5.40f, 1.13f, -5.00f, 1.12f, -4.30f, 1.10f, -3.60f, 1.12f, -3.10f, 1.17f, -2.90f, 1.22f, -2.60f, 1.25f, -1.80f, 1.27f, -0.50f, 1.26f, 0.60f, 1.22f, 1.20f, 1.16f, 1.55f, 1.085f };
        static readonly float[] PylonBottom = { -5.40f, 1.10f, -5.00f, 1.08f, -4.30f, 1.04f, -3.60f, 0.98f, -3.00f, 0.87f, -2.50f, 0.70f, -2.15f, 0.52f, -1.95f, 0.40f, -1.30f, 0.45f, -1.18f, 0.95f, -1.10f, 1.00f, 1.55f, 1.00f };
        static readonly float[] PylonHalfW = { -5.40f, 0.01f, -5.00f, 0.05f, -4.00f, 0.10f, -3.00f, 0.14f, -2.20f, 0.165f, -1.00f, 0.19f, 0.20f, 0.19f, 1.00f, 0.15f, 1.40f, 0.07f, 1.55f, 0f };

        /// <summary>Pylon from the top of the cowl back and up into the wing's leading edge and lower surface (nacelle-local, built for the nominal nacelle).</summary>
        static void BuildPylon(MeshBuilder mb)
        {
            float[] zs = { 1.55f, 1.47f, 1.40f, 1.20f, 1.00f, 0.60f, 0.20f, -0.50f, -1.00f, -1.10f, -1.18f, -1.30f, -1.60f, -1.95f, -2.15f, -2.50f, -2.90f, -3.10f, -3.60f, -4.00f, -4.30f, -5.00f, -5.40f };
            // shift so that the pylon keeps meeting the wing when PlaneDef moves the nacelle or the wing
            WSt st = WingStation((PlaneDef.WingRoot.x - PlaneDef.NacelleCentre.x) / PlaneDef.WingSemiSpan);
            Vector3 nose = WingPoint(st, 0f, true) + PlaneDef.WingRoot - PlaneDef.NacelleCentre;      // wing leading edge above the engine, nacelle-local
            float dzW = nose.z - (-2.977f), dyW = nose.y - 1.202f;
            const int m = 8;
            var G = new Vector3[zs.Length, m]; var C = new Vector3[zs.Length];
            for (int i = 0; i < zs.Length; i++)
            {
                float z = zs[i], wgt = Smooth((-1.2f - z) / 1.6f);                                    // 0 on the cowl, 1 at the wing
                float yt = Key(z, PylonTop) + dyW * wgt, yb = Key(z, PylonBottom) + dyW * wgt, w = Key(z, PylonHalfW), zz = z + dzW * wgt;
                C[i] = new Vector3(0f, (yt + yb) * 0.5f, zz);
                G[i, 0] = new Vector3(-w, yb + 0.3f * w, zz); G[i, 1] = new Vector3(-w, yt - 0.7f * w, zz); G[i, 2] = new Vector3(-0.55f * w, yt - 0.15f * w, zz); G[i, 3] = new Vector3(0f, yt, zz);
                G[i, 4] = new Vector3(0.55f * w, yt - 0.15f * w, zz); G[i, 5] = new Vector3(w, yt - 0.7f * w, zz); G[i, 6] = new Vector3(w, yb + 0.3f * w, zz); G[i, 7] = new Vector3(0f, yb, zz);
            }
            Grid(mb, G, true, (i, j) => i == 0 ? Vector3.forward : G[i, j] - C[i], (i, j) =>
            {
                float zm = (zs[i] + zs[i + 1]) * 0.5f;
                if (zm < -1.2f && zm > -2.8f && (j == 6 || j == 7)) return Sh(HotMetalCol, 0.95f);       // heat shield over the core nozzle
                return Sh(PaintCol, (j == 2 || j == 3) ? 0.93f : 0.89f);
            });
        }
    }
}
