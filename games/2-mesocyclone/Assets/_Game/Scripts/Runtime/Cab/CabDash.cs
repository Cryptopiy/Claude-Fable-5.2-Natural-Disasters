using UnityEngine;

namespace MESOCYCLONE
{
    /// <summary>The dashboard: an extruded cross-section across the cab in four sections (left end, driver's
    /// zone with the instrument binnacle, centre stack, passenger side), air vents, glove box, steering column
    /// with its stalks, pedals. Profiles are (z, y) points running from the glass base rearward over the top,
    /// down the face and forward again underneath.</summary>
    public static class CabDash
    {
        static Vector2 V(float z, float y) { return new Vector2(z, y); }

        static Vector2[] Join(Vector2[] a, Vector2[] b)
        {
            var r = new Vector2[a.Length + b.Length - 1];
            for (int i = 0; i < a.Length; i++) r[i] = a[i];
            for (int i = 1; i < b.Length; i++) r[a.Length + i - 1] = b[i];
            return r;
        }

        public static void Build(CabMesh g)
        {
            g.World(); g.mirror = false; g.NoToward();
            float h = CabLayout.StackHalf, d0 = CabLayout.DrvX0;
            float sz1 = CabLayout.StackZ(0.76f);                 // the stack face starts at the pad's edge (0.421 = StackZ(1.128))

            Vector2[] padGen = { V(0.792f, 1.168f), V(0.60f, 1.176f), V(0.50f, 1.180f), V(0.455f, 1.176f), V(0.432f, 1.164f), V(0.422f, 1.146f), V(0.421f, 1.128f) };
            Vector2[] faceGen = { V(0.421f, 1.128f), V(0.432f, 1.112f), V(0.438f, 1.10f), V(0.44f, 1.03f), V(0.452f, 1.01f), V(0.455f, 0.84f),
                                  V(0.462f, 0.775f), V(0.486f, 0.742f), V(0.53f, 0.73f), V(0.88f, 0.73f) };
            Vector2[] faceStack = { V(0.421f, 1.128f), V(sz1, 0.76f), V(sz1 + 0.006f, 0.737f), V(sz1 + 0.027f, 0.722f), V(0.43f, 0.718f), V(0.88f, 0.718f) };
            // the driver's zone stays behind the instrument plane until it comes out under the binnacle
            Vector2[] padDrv = { V(0.792f, 1.168f), V(0.60f, 1.176f), V(0.57f, 1.174f) };
            Vector2[] faceDrv = { V(0.57f, 1.174f), V(0.575f, 1.16f), V(0.575f, 0.99f), V(0.445f, 0.975f), V(0.44f, 0.95f), V(0.452f, 0.84f),
                                  V(0.462f, 0.775f), V(0.486f, 0.742f), V(0.53f, 0.73f), V(0.88f, 0.73f) };
            Vector2 pivot = V(0.80f, 0.95f);

            // ---- the four sections ----
            g.Mat(CabMesh.Pad);
            g.Sweep(padGen, -0.80f, d0);
            g.Sweep(padDrv, d0, -h);
            g.Sweep(padGen, -h, 0.80f);
            g.Mat(CabMesh.Dash);
            g.Sweep(faceGen, -0.80f, d0);
            g.Sweep(faceDrv, d0, -h);
            g.Sweep(faceStack, -h, h);
            g.Sweep(faceGen, h, 0.80f);
            g.Cap(Join(padGen, faceGen), d0, true, pivot, false);
            Vector2[] fullStack = Join(padGen, faceStack);
            g.Cap(fullStack, -h, false, pivot, false);
            g.Cap(fullStack, h, true, pivot, false);

            // ---- instrument binnacle: hood with an overhanging lip, the recessed instrument wall, two cheeks ----
            Vector3 c9 = CabLayout.Cluster(0f, 0.075f), c10 = CabLayout.Cluster(0f, -0.138f);
            Vector2[] cove = { V(0.66f, 1.176f), V(0.60f, 1.200f), V(0.54f, 1.226f), V(0.50f, 1.234f), V(0.478f, 1.230f), V(0.468f, 1.220f),
                               V(0.470f, 1.210f), V(0.50f, 1.204f), V(c9.z, c9.y), V(c10.z, c10.y), V(0.430f, 1.003f), V(0.424f, 0.992f), V(0.428f, 0.97f) };
            Vector2[] hull = { V(0.66f, 1.165f), V(0.66f, 1.176f), V(0.60f, 1.200f), V(0.54f, 1.226f), V(0.50f, 1.234f), V(0.478f, 1.230f),
                               V(0.468f, 1.220f), V(0.430f, 1.003f), V(0.424f, 0.992f), V(0.428f, 0.97f), V(0.66f, 0.97f) };
            g.Mat(CabMesh.Pad);
            g.Sweep(cove, d0 + 0.015f, -h - 0.015f);
            g.Pill(hull, d0 - 0.005f, d0 + 0.019f, 0.007f);
            g.Pill(hull, -h - 0.019f, -h + 0.005f, 0.007f);

            // ---- defroster grille along the base of the glass ----
            g.Frame(new Vector3(0f, 1.1718f, 0.70f), Vector3.right, new Vector3(0f, -0.0416f, 0.999f));
            g.Mat(CabMesh.Plastic, Sw.Black);
            g.Flat(-0.56f, -0.013f, 0.56f, 0.013f, -0.0006f);
            g.Mat(CabMesh.Plastic, Sw.CabTrim);
            for (int i = -18; i <= 18; i++) g.Flat(i * 0.03f - 0.0015f, -0.013f, i * 0.03f + 0.0015f, 0.013f, -0.0013f);
            g.Flat(-0.56f, -0.001f, 0.56f, 0.001f, -0.0013f);

            // ---- air vents: outboard left and right, passenger side, two on the centre stack ----
            Vent(g, new Vector3(-0.72f, 1.065f, 0.439f), Vector3.up, 0.09f, 0.045f);
            Vent(g, new Vector3(0.72f, 1.065f, 0.439f), Vector3.up, 0.09f, 0.045f);
            Vent(g, new Vector3(0.24f, 1.065f, 0.439f), Vector3.up, 0.09f, 0.045f);
            Vent(g, CabDims.RadioCentre + Vector3.right * -0.062f + CabLayout.StackUp * 0.095f, CabLayout.StackUp, 0.10f, 0.05f);
            Vent(g, CabDims.RadioCentre + Vector3.right * 0.062f + CabLayout.StackUp * 0.095f, CabLayout.StackUp, 0.10f, 0.05f);

            // ---- glove box: part line, latch ----
            g.Frame(new Vector3(0.47f, 0.925f, 0.4535f), Vector3.right, new Vector3(0f, 0.17f, -0.003f));
            g.Mat(CabMesh.Plastic, Sw.Black);
            g.Flat(-0.23f, 0.067f, 0.23f, 0.070f, -0.0006f);
            g.Flat(-0.23f, -0.075f, 0.23f, -0.072f, -0.0006f);
            g.Flat(-0.23f, -0.072f, -0.227f, 0.067f, -0.0006f);
            g.Flat(0.227f, -0.072f, 0.23f, 0.067f, -0.0006f);
            g.Mat(CabMesh.Gloss, Sw.PlasticGloss);
            g.Box(new Vector3(0f, 0.042f, -0.004f), new Vector3(0.078f, 0.022f, 0.009f), 32);
            g.Mat(CabMesh.Chrome, Sw.Chrome);
            g.Disc(0f, 0.042f, 0.0055f, -0.0091f, 12);

            // ---- light switch left of the column, hood release below ----
            g.Frame(new Vector3(-0.72f, 0.935f, 0.4534f), Vector3.right, new Vector3(0f, 0.17f, -0.003f));
            g.Mat(CabMesh.Gloss, Sw.PlasticGloss);
            g.Box(new Vector3(0f, 0f, -0.002f), new Vector3(0.07f, 0.07f, 0.005f), 32);
            g.Mat(CabMesh.Plastic, Sw.Black);
            Knob(g, new Vector3(0f, 0f, -0.0045f), 0.017f, 0.018f);
            g.Mat(CabMesh.Glow);
            g.Flat(-0.0012f, 0.004f, 0.0012f, 0.013f, -0.0229f);

            // ---- steering column with stalks and the gear lever ----
            g.Frame(CabDims.WheelCentre, Vector3.right, CabLayout.WheelUp);
            g.Mat(CabMesh.Dash);
            g.Cyl(new Vector3(0f, 0f, 0.04f), new Vector3(0f, 0f, 0.25f), 0.044f, 0.055f, 20, true);
            g.Mat(CabMesh.Plastic, Sw.Black);
            g.Cyl(new Vector3(-0.035f, 0.004f, 0.08f), new Vector3(-0.165f, 0.020f, 0.062f), 0.0065f, 0.0055f, 8, false);
            g.Cyl(new Vector3(-0.165f, 0.020f, 0.062f), new Vector3(-0.202f, 0.0245f, 0.057f), 0.0095f, 0.0085f, 10, true);
            g.Cyl(new Vector3(0.035f, 0.004f, 0.08f), new Vector3(0.155f, 0.012f, 0.066f), 0.0065f, 0.0055f, 8, false);
            g.Cyl(new Vector3(0.155f, 0.012f, 0.066f), new Vector3(0.19f, 0.0145f, 0.062f), 0.0095f, 0.0085f, 10, true);
            g.Mat(CabMesh.Chrome, Sw.Aluminium);
            g.Cyl(new Vector3(0.03f, 0.02f, 0.105f), new Vector3(0.185f, 0.034f, 0.078f), 0.0055f, 0.0045f, 8, false);
            g.Cyl(new Vector3(0.046f, -0.012f, 0.13f), new Vector3(0.06f, -0.012f, 0.13f), 0.012f, 0.011f, 12, true);
            g.Mat(CabMesh.Plastic, Sw.Black);
            g.Ball(new Vector3(0.196f, 0.035f, 0.076f), 0.015f, 12, 8);

            // ---- pedals, in the shadow under the dash ----
            g.Mat(CabMesh.Rubber);
            g.Frame(new Vector3(-0.44f, 0.66f, 0.62f), Vector3.right, new Vector3(0f, 0.8f, 0.6f));
            g.Box(Vector3.zero, new Vector3(0.11f, 0.055f, 0.012f));
            g.Frame(new Vector3(-0.25f, 0.63f, 0.66f), Vector3.right, new Vector3(0f, 0.75f, 0.66f));
            g.Box(Vector3.zero, new Vector3(0.05f, 0.13f, 0.012f));
            g.World();
            g.Mat(CabMesh.Plastic, Sw.Black);
            g.Cyl(new Vector3(-0.44f, 0.67f, 0.628f), new Vector3(-0.44f, 0.86f, 0.70f), 0.008f, 0.008f, 6, false);
            g.Cyl(new Vector3(-0.25f, 0.66f, 0.668f), new Vector3(-0.25f, 0.84f, 0.74f), 0.006f, 0.006f, 6, false);
        }

        /// <summary>An air vent on a flat piece of the dash: dark throat, raised bezel, five slats and a thumb tab.</summary>
        public static void Vent(CabMesh g, Vector3 centre, Vector3 up, float w, float h)
        {
            g.Frame(centre, Vector3.right, up);
            g.Mat(CabMesh.Plastic, Sw.Black);
            g.Flat(-w * 0.5f, -h * 0.5f, w * 0.5f, h * 0.5f, -0.0006f);
            g.Mat(CabMesh.Plastic, Sw.CabTrim);
            const float b = 0.007f;
            g.Box(new Vector3(0f, h * 0.5f + b * 0.5f, -0.0035f), new Vector3(w + 2f * b, b, 0.009f), 32);
            g.Box(new Vector3(0f, -h * 0.5f - b * 0.5f, -0.0035f), new Vector3(w + 2f * b, b, 0.009f), 32);
            g.Box(new Vector3(-w * 0.5f - b * 0.5f, 0f, -0.0035f), new Vector3(b, h, 0.009f), 32 | 4 | 8);
            g.Box(new Vector3(w * 0.5f + b * 0.5f, 0f, -0.0035f), new Vector3(b, h, 0.009f), 32 | 4 | 8);
            g.Mat(CabMesh.Plastic, Sw.CabPlastic);
            for (int i = 0; i < 5; i++)
                g.Box(new Vector3(0f, (i - 2) * h / 5.5f, -0.004f), new Vector3(w, 0.0022f, 0.006f), 32 | 1 | 2);
            g.Box(new Vector3(0f, 0f, -0.0078f), new Vector3(0.012f, 0.008f, 0.0025f), 32);
        }

        /// <summary>A round knob in the current frame at pos, its axis toward the viewer (frame -z).</summary>
        public static void Knob(CabMesh g, Vector3 pos, float r, float h)
        {
            Vector2[] p = { new Vector2(r * 1.08f, 0f), new Vector2(r, h * 0.25f), new Vector2(r * 0.92f, h * 0.85f), new Vector2(r * 0.8f, h), new Vector2(0f, h) };
            g.Lathe(p, pos, new Vector3(0f, 0f, -1f), 16);
        }
    }
}
