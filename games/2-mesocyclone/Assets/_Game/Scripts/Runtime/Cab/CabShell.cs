using UnityEngine;

namespace MESOCYCLONE
{
    /// <summary>The shell of the cabin: floor, headliner, rear wall, door panels, pillars, the rubber seals that
    /// bound the glass openings exactly (the glass itself is drawn by the lead from the same hard points) and the
    /// reveals of the openings. Seals lie 0.5 mm inside the glass planes with their inner edge on the opening;
    /// trim lies 1 mm further in and overlaps the seals, so no seam is a bare edge-to-edge joint.</summary>
    public static class CabShell
    {
        const float E = -0.0005f, Et = -0.0015f;  // seal plane and trim plane, measured outward from a glass plane
        const float Xt = 0.7985f;                 // |x| of the side trim plane
        const float Zb = -1.0995f, Zt = -1.0985f; // rear wall: seal plane, trim plane
        const float Bx = 0.012f;                  // how far a seal runs on underneath the trim (covers the 1 mm step at any angle)
        const float Back = Zt - 0.006f;           // side walls, floor and liner run on past the rear wall

        static Vector3 V(float x, float y, float z) { return new Vector3(x, y, z); }
        static Vector2 P(float z, float y) { return new Vector2(z, y); }

        public static void Build(CabMesh g)
        {
            g.World(); g.mirror = false; g.Toward(CabLayout.Mid);
            Floor(g);
            Roof(g);
            RearWall(g);
            WindscreenSeal(g);
            for (int s = 0; s < 2; s++) { g.mirror = s == 1; Side(g); }
            g.mirror = false; g.NoToward(); g.World();
        }

        static void Floor(CabMesh g)
        {
            float f = CabLayout.FloorY;
            g.Mat(CabMesh.Carpet);
            g.Q(V(-0.805f, f, Back), V(-0.805f, f, 0.62f), V(0.805f, f, 0.62f), V(0.805f, f, Back));
            g.Q(V(-0.805f, f, 0.62f), V(-0.805f, 0.86f, 0.96f), V(0.805f, 0.86f, 0.96f), V(0.805f, f, 0.62f));
            // rubber floor mats with raised ribs
            g.Mat(CabMesh.Rubber);
            for (int s = -1; s <= 1; s += 2)
            {
                float xc = s * 0.41f;
                g.Q(V(xc - 0.27f, f + 0.004f, -0.02f), V(xc - 0.27f, f + 0.004f, 0.60f), V(xc + 0.27f, f + 0.004f, 0.60f), V(xc + 0.27f, f + 0.004f, -0.02f));
                for (int i = 0; i < 7; i++)
                {
                    float z = 0.05f + i * 0.08f;
                    g.Q(V(xc - 0.23f, f + 0.007f, z), V(xc - 0.23f, f + 0.007f, z + 0.03f), V(xc + 0.23f, f + 0.007f, z + 0.03f), V(xc + 0.23f, f + 0.007f, z));
                }
            }
        }

        static void Roof(CabMesh g)
        {
            float y = CabLayout.LinerY, zf = CabDims.WTL.z;
            g.Mat(CabMesh.Liner);
            g.Q(V(-0.66f, y, zf), V(0.66f, y, zf), V(0.66f, y, Back), V(-0.66f, y, Back));
            // dark header strip along the top of the windscreen, dome lamp
            g.Mat(CabMesh.Rubber);
            g.Q(V(-0.66f, y - 0.0006f, 0.3f), V(0.66f, y - 0.0006f, 0.3f), V(0.66f, y - 0.0006f, zf - 0.0005f), V(-0.66f, y - 0.0006f, zf - 0.0005f));
            g.Mat(CabMesh.Plastic, Sw.CabLiner);
            g.Box(V(0f, y - 0.004f, -0.22f), V(0.17f, 0.012f, 0.10f), 8);
            g.Mat(CabMesh.Plastic, Sw.WarmWhite);
            g.Box(V(0f, y - 0.008f, -0.22f), V(0.12f, 0.012f, 0.065f), 8);
        }

        static void RearWall(CabMesh g)
        {
            float gx = CabDims.RearGlassX, y0 = CabDims.RearGlassY0, y1 = CabDims.RearGlassY1, w = CabLayout.SealW;
            float fl = CabLayout.FloorY - 0.01f, top = CabLayout.LinerY + 0.01f;
            // trim in three shallow layers (1 mm apart) so that the pieces overlap instead of meeting edge to edge
            g.Mat(CabMesh.Dash);
            g.Q(V(-0.805f, fl, Zt), V(-0.805f, y0 - w, Zt), V(0.805f, y0 - w, Zt), V(0.805f, fl, Zt));
            g.Mat(CabMesh.Liner);
            g.Q(V(-0.805f, y0 - w - 0.005f, Zt + 0.001f), V(-0.805f, y1 + w + 0.005f, Zt + 0.001f), V(-gx - w, y1 + w + 0.005f, Zt + 0.001f), V(-gx - w, y0 - w - 0.005f, Zt + 0.001f));
            g.Q(V(gx + w, y0 - w - 0.005f, Zt + 0.001f), V(gx + w, y1 + w + 0.005f, Zt + 0.001f), V(0.805f, y1 + w + 0.005f, Zt + 0.001f), V(0.805f, y0 - w - 0.005f, Zt + 0.001f));
            g.Q(V(-0.805f, y1 + w, Zt + 0.002f), V(-0.805f, top, Zt + 0.002f), V(0.805f, top, Zt + 0.002f), V(0.805f, y1 + w, Zt + 0.002f));
            // seal around the rear glass: a ring with shared corners
            float o = w + Bx;
            Vector3 i0 = V(-gx, y0, Zb), i1 = V(-gx, y1, Zb), i2 = V(gx, y1, Zb), i3 = V(gx, y0, Zb);
            Vector3 o0 = V(-gx - o, y0 - o, Zb), o1 = V(-gx - o, y1 + o, Zb), o2 = V(gx + o, y1 + o, Zb), o3 = V(gx + o, y0 - o, Zb);
            g.Mat(CabMesh.Rubber);
            g.Q(o0, o1, i1, i0); g.Q(o1, o2, i2, i1); g.Q(o2, o3, i3, i2); g.Q(o3, o0, i0, i3);
            // reveal of the opening through the rear wall (outward)
            float zi = CabDims.RearWallZ + 0.002f, zo = CabDims.RearWallZ - 0.07f;
            g.Toward(V(0f, (y0 + y1) * 0.5f, (zi + zo) * 0.5f));
            g.Q(V(-gx, y0, zi), V(gx, y0, zi), V(gx, y0, zo), V(-gx, y0, zo));
            g.Q(V(-gx, y1, zi), V(gx, y1, zi), V(gx, y1, zo), V(-gx, y1, zo));
            g.Q(V(-gx, y0, zi), V(-gx, y1, zi), V(-gx, y1, zo), V(-gx, y0, zo));
            g.Q(V(gx, y0, zi), V(gx, y1, zi), V(gx, y1, zo), V(gx, y0, zo));
            g.Toward(CabLayout.Mid);
        }

        static void WindscreenSeal(CabMesh g)
        {
            float w = CabLayout.SealW, v1 = CabDims.GlassHeight + 0.03f, o = w + Bx;
            float h0 = CabLayout.GlassHalf(0f), h1 = CabLayout.GlassHalf(v1);
            g.Frame(CabLayout.GlassOrigin, Vector3.right, CabLayout.GlassUp);
            g.Mat(CabMesh.Rubber);
            float hb = CabLayout.GlassHalf(-w);
            Vector3 inL = V(-h0, 0f, E), inR = V(h0, 0f, E), outL = V(-hb - o, -w, E), outR = V(hb + o, -w, E);
            g.Q(outL, inL, inR, outR);                                                  // bottom
            g.Q(outL, V(-h1 - o, v1, E), V(-h1, v1, E), inL);                           // left
            g.Q(inR, V(h1, v1, E), V(h1 + o, v1, E), outR);                             // right
            // reveals on the side and top edges of the opening (the body's thickness around the glass, outward)
            float hT = CabDims.GlassHeight, ht = CabLayout.GlassHalf(hT);
            const float r0 = -0.002f, r1 = 0.04f;
            g.Toward(CabLayout.Glass(0f, hT * 0.5f, 0.02f));
            g.Q(V(-h0, 0f, r0), V(-ht, hT, r0), V(-ht, hT, r1), V(-h0, 0f, r1));
            g.Q(V(h0, 0f, r0), V(ht, hT, r0), V(ht, hT, r1), V(h0, 0f, r1));
            g.Q(V(-ht, hT, r0), V(ht, hT, r0), V(ht, hT, r1), V(-ht, hT, r1));
            g.Toward(CabLayout.Mid);
            g.World();
        }

        /// <summary>Everything of the left side; the right side is its mirror image.</summary>
        static void Side(CabMesh g)
        {
            float y0 = CabDims.SideGlassY0, y1 = CabDims.SideGlassY1, zr = CabDims.SideGlassZ0, w = CabLayout.SealW;
            float yb = y0 - w, yt = y1 + w, zs = zr - w, ys = y0 - 0.002f;
            float f0 = CabLayout.SideFront(y0), f1 = CabLayout.SideFront(y1);
            const float fw = 0.03f, o = 0.10f, r0 = -0.002f;

            // ---- door frame: x = forward, y = up, z = outward ----
            g.Frame(V(-CabDims.CabHalfWidth, 0f, 0f), Vector3.forward, Vector3.up);
            g.Toward(CabLayout.Mid);
            // seal around the side glass: a ring with shared corners; its outer edge runs on under the trim
            Vector3 i0 = V(zr, y0, E), i1 = V(zr, y1, E), i2 = V(f1, y1, E), i3 = V(f0, y0, E);
            Vector3 o0 = V(zs - Bx, yb - Bx, E), o1 = V(zs - Bx, yt + Bx, E);
            Vector3 o2 = V(CabLayout.SideFront(yt + Bx) + fw + Bx, yt + Bx, E), o3 = V(CabLayout.SideFront(yb - Bx) + fw + Bx, yb - Bx, E);
            g.Mat(CabMesh.Rubber);
            g.Q(o0, o1, i1, i0); g.Q(o1, o2, i2, i1); g.Q(o2, o3, i3, i2); g.Q(o3, o0, i0, i3);
            // reveals through the door frame, on the edges of the opening
            float fs = CabLayout.SideFront(ys);
            g.Q(V(zr, ys, r0), V(zr, y1, r0), V(zr, y1, o), V(zr, ys, o));
            g.Q(V(zr, y1, r0), V(f1, y1, r0), V(f1, y1, o), V(zr, y1, o));
            g.Q(V(fs, ys, r0), V(f1, y1, r0), V(f1, y1, o), V(fs, ys, o));

            // outer sill: weatherstrip and the painted door top rolling over to the outer skin
            Vector2[] sillPaint = { P(0.125f, 0.95f), P(0.124f, 1.09f), P(0.118f, 1.108f), P(0.105f, 1.116f), P(0.04f, ys) };
            Vector2[] sillRubber = { P(0.04f, ys), P(r0, ys) };
            g.Mat(CabMesh.Paint); g.Sweep(sillPaint, -0.46f, 0.84f);
            g.Mat(CabMesh.Rubber); g.Sweep(sillRubber, -0.46f, 0.84f);

            // inner door panel: padded shoulder roll, cloth insert, hard lower panel
            Vector2[] roll = { P(Et, yb), P(-0.012f, yb - 0.003f), P(-0.024f, 1.078f), P(-0.028f, 1.05f) };
            Vector2[] cloth = { P(-0.028f, 1.05f), P(-0.027f, 0.99f) };
            Vector2[] lower = { P(-0.027f, 0.99f), P(-0.024f, 0.93f), P(-0.010f, 0.90f), P(-0.005f, 0.88f), P(-0.005f, CabLayout.FloorY - 0.01f) };
            g.Mat(CabMesh.Pad); g.Sweep(roll, -0.46f, 0.70f);
            g.Mat(CabMesh.Fabric); g.Sweep(cloth, -0.46f, 0.70f);
            g.Mat(CabMesh.Dash); g.Sweep(lower, -0.46f, 0.70f);
            g.Pill(CabMesh.RR(-0.022f, 0.60f, 0.036f, 0.12f, 0.012f), -0.28f, 0.26f, 0.01f);      // map pocket
            g.Mat(CabMesh.Pad);
            g.Pill(CabMesh.RR(-0.058f, 0.965f, 0.075f, 0.05f, 0.018f), -0.36f, 0.40f, 0.012f);    // armrest
            g.Mat(CabMesh.Gloss, Sw.PlasticGloss);
            g.Box(V(0.28f, 0.9905f, -0.058f), V(0.11f, 0.004f, 0.04f), 4);                        // switch plate
            g.Box(V(0.30f, 1.062f, -0.0285f), V(0.12f, 0.036f, 0.008f), 32);                      // handle bezel
            g.Mat(CabMesh.Plastic, Sw.CabTrim);
            g.Box(V(0.255f, 0.9945f, -0.058f), V(0.022f, 0.005f, 0.014f), 4);
            g.Box(V(0.295f, 0.9945f, -0.058f), V(0.022f, 0.005f, 0.014f), 4);
            g.Mat(CabMesh.Chrome, Sw.Chrome);
            g.Box(V(0.315f, 1.062f, -0.0345f), V(0.065f, 0.016f, 0.006f), 32);                    // door handle

            // speaker grille
            const float sx = 0.45f, sy = 0.66f, sr = 0.075f;
            g.Mat(CabMesh.Plastic, Sw.Black);
            g.Disc(sx, sy, sr, -0.0056f, 24);
            g.Mat(CabMesh.Plastic, Sw.CabTrim);
            g.RingFlat(sx, sy, sr, sr + 0.008f, -0.0064f, 28);
            g.Mat(CabMesh.Plastic, Sw.CabPlastic);
            for (int i = -4; i <= 4; i++)
            {
                float dy = i * 0.015f, half = Mathf.Sqrt(sr * sr - dy * dy) - 0.002f;
                g.Flat(sx - half, sy + dy - 0.0028f, sx + half, sy + dy + 0.0028f, -0.0066f);
            }

            // ---- world frame from here ----
            g.World();
            g.Toward(CabLayout.Mid);

            // A-pillar trim: from the seal of the side glass to the seal of the windscreen, gently rounded
            g.Mat(CabMesh.Liner);
            Vector3 bulge = new Vector3(0.8f, -0.05f, -0.6f).normalized * 0.022f;
            g.Grid(6, 5, (u, v) =>
            {
                float y = Mathf.Lerp(1.09f, 1.83f, v);
                float vg = (y - CabDims.WBL.y) / CabLayout.GlassUp.y;
                Vector3 a = new Vector3(-Xt, y, CabLayout.SideFront(y) + fw);
                Vector3 b = CabLayout.Glass(-(CabLayout.GlassHalf(vg) + CabLayout.SealW), vg, Et);
                return Vector3.Lerp(a, b, u) + bulge * (4f * u * (1f - u));
            }, 0.12f, 0.74f);

            // B-pillar trim, rear quarter, roof side
            const float xb = -0.765f;
            float top = CabLayout.LinerY, fl = CabLayout.FloorY - 0.01f, zf = CabDims.WTL.z;
            g.Q(V(-Xt, fl, zs), V(-Xt, top, zs), V(xb, top, -0.475f), V(xb, fl, -0.475f));
            g.Q(V(xb, fl, -0.475f), V(xb, top, -0.475f), V(xb, top, -0.60f), V(xb, fl, -0.60f));
            g.Q(V(xb, fl, -0.60f), V(xb, top, -0.60f), V(-Xt, top, -0.63f), V(-Xt, fl, -0.63f));
            g.Q(V(-Xt, 1.10f, -0.62f), V(-Xt, yt + 0.003f, -0.62f), V(-Xt, yt + 0.003f, Back), V(-Xt, 1.10f, Back));
            g.Q(V(-0.66f, top, zf), V(-0.66f, top, Back), V(-0.804f, 1.7636f, Back), V(-0.804f, 1.7636f, zf));
            g.Mat(CabMesh.Dash);
            g.Q(V(-Xt, fl, -0.62f), V(-Xt, 1.10f, -0.62f), V(-Xt, 1.10f, Back), V(-Xt, fl, Back));
            g.Q(V(-Xt, fl, 0.69f), V(-Xt, 1.10f, 0.69f), V(-Xt, 1.10f, 0.96f), V(-Xt, fl, 0.96f));   // kick panel

            // seat belt on the B-pillar
            g.Mat(CabMesh.Rubber);
            g.Q(V(xb + 0.0015f, 0.56f, -0.515f), V(xb + 0.0015f, 1.60f, -0.515f), V(xb + 0.0015f, 1.60f, -0.565f), V(xb + 0.0015f, 0.56f, -0.565f));
            g.Mat(CabMesh.Gloss, Sw.PlasticGloss);
            g.Box(V(xb + 0.006f, 1.62f, -0.54f), V(0.012f, 0.055f, 0.07f), 1);
            g.Mat(CabMesh.Plastic, Sw.CabTrim);
            g.Box(V(xb + 0.012f, 0.58f, -0.54f), V(0.024f, 0.13f, 0.09f), 1);

            // sun visor folded against the liner, on its rod
            g.Mat(CabMesh.Liner);
            g.Pill(CabMesh.RR(0.215f, 1.781f, 0.15f, 0.02f, 0.008f, 3, -2f), -0.63f, -0.17f, 0.006f);
            g.Mat(CabMesh.Plastic, Sw.CabLiner);
            g.Cyl(V(-0.645f, 1.786f, 0.287f), V(-0.16f, 1.786f, 0.287f), 0.004f, 0.004f, 6, true);
            g.Box(V(-0.652f, 1.792f, 0.287f), V(0.03f, 0.016f, 0.03f), 8);
            g.Box(V(-0.155f, 1.792f, 0.287f), V(0.02f, 0.016f, 0.02f), 8);
        }
    }
}
