using UnityEngine;

namespace MESOCYCLONE
{
    /// <summary>The instrument cluster (dial faces, chrome bezels, tick marks, tell-tales - no numerals) and the
    /// details of the centre stack (radio with its dial strip, climate knobs, cubby). The dials sit low on the
    /// instrument plane so that they are read through the upper half of the steering wheel.</summary>
    public static class CabGauges
    {
        const float Zp = -0.001f;        // instrument panel, just in front of the binnacle's back wall
        const float Zt = -0.0026f;       // printed marks

        public static void Build(CabMesh g)
        {
            g.mirror = false; g.NoToward();
            Cluster(g);
            Stack(g);
            g.World();
        }

        static void Cluster(CabMesh g)
        {
            g.Frame(CabDims.ClusterCentre, Vector3.right, CabLayout.WheelUp);
            float bx = CabLayout.DialBigX, by = CabLayout.DialY, br = CabLayout.DialBigR;
            float sx = CabLayout.DialSmallX, sy = CabLayout.DialSmallY, sr = CabLayout.DialSmallR;

            g.Mat(CabMesh.Face);
            g.Flat(-0.243f, -0.1375f, 0.233f, 0.0745f, Zp);

            // glossy dial faces and the odometer window
            g.Mat(CabMesh.Gloss, Sw.Black);
            g.Disc(-bx, by, br, Zp - 0.0006f, 36); g.Disc(bx, by, br, Zp - 0.0006f, 36);
            g.Disc(-sx, sy, sr, Zp - 0.0006f, 24); g.Disc(sx, sy, sr, Zp - 0.0006f, 24);
            g.Flat(-bx - 0.014f, by - 0.034f, -bx + 0.014f, by - 0.025f, Zt);
            for (int i = -3; i <= 3; i++) g.Flat(i * 0.03f - 0.009f, 0.034f, i * 0.03f + 0.009f, 0.050f, Zp - 0.0006f);

            // chrome bezels
            g.Mat(CabMesh.Chrome, Sw.Chrome);
            Vector3 axis = new Vector3(0f, 0f, 1f);
            g.Torus(new Vector3(-bx, by, -0.0045f), axis, br + 0.004f, 0.003f, 44, 8);
            g.Torus(new Vector3(bx, by, -0.0045f), axis, br + 0.004f, 0.003f, 44, 8);
            g.Torus(new Vector3(-sx, sy, -0.004f), axis, sr + 0.0035f, 0.0025f, 28, 6);
            g.Torus(new Vector3(sx, sy, -0.004f), axis, sr + 0.0035f, 0.0025f, 28, 6);

            // speedometer: 0..160 over 240 degrees, a mark every 5
            g.Mat(CabMesh.Glow);
            for (int k = 0; k <= 32; k++)
            {
                float a = -120f + 240f * k / 32f;
                if (k % 4 == 0) g.Tick(-bx, by, a, br - 0.015f, br - 0.003f, 0.0022f, Zt);
                else if (k % 2 == 0) g.Tick(-bx, by, a, br - 0.011f, br - 0.003f, 0.0014f, Zt);
                else g.Tick(-bx, by, a, br - 0.008f, br - 0.003f, 0.0010f, Zt);
            }
            // tachometer: 0..6 over 240 degrees, a mark every quarter; the last sixth is the red zone
            for (int k = 0; k <= 24; k++)
            {
                float a = -120f + 240f * k / 24f;
                g.Mat(k >= 20 ? CabMesh.Needle : CabMesh.Glow);
                if (k % 4 == 0) g.Tick(bx, by, a, br - 0.015f, br - 0.003f, 0.0022f, Zt);
                else if (k % 2 == 0) g.Tick(bx, by, a, br - 0.011f, br - 0.003f, 0.0014f, Zt);
                else g.Tick(bx, by, a, br - 0.008f, br - 0.003f, 0.0010f, Zt);
            }
            // fuel (left) and temperature (right): 100 degrees of arc, red at empty and at hot
            for (int k = 0; k <= 8; k++)
            {
                float a = -50f + 100f * k / 8f;
                bool major = k % 4 == 0;
                g.Mat(k == 0 ? CabMesh.Needle : CabMesh.Glow);
                g.Tick(-sx, sy, a, sr - (major ? 0.010f : 0.007f), sr - 0.003f, major ? 0.0020f : 0.0011f, Zt);
                g.Mat(k == 8 ? CabMesh.Needle : CabMesh.Glow);
                g.Tick(sx, sy, a, sr - (major ? 0.010f : 0.007f), sr - 0.003f, major ? 0.0020f : 0.0011f, Zt);
            }
            g.Mat(CabMesh.Glow);
            g.Disc(-sx, sy - 0.014f, 0.0022f, Zt, 8);
            g.Disc(sx, sy - 0.014f, 0.0022f, Zt, 8);

            // tell-tales in two of the windows above the rim: headlamps (a lamp with three rays), cruise
            g.Mat(CabMesh.Green);
            g.Disc(-0.027f, 0.042f, 0.0038f, Zt, 10);
            for (int i = -1; i <= 1; i++) g.Flat(-0.0375f, 0.042f + i * 0.003f - 0.0005f, -0.0325f, 0.042f + i * 0.003f + 0.0005f, Zt);
            g.RingFlat(0.03f, 0.042f, 0.0022f, 0.0038f, Zt, 12);
        }

        static void Stack(CabMesh g)
        {
            g.Frame(CabDims.RadioCentre, Vector3.right, CabLayout.StackUp);
            Vector3 axis = new Vector3(0f, 0f, 1f);

            // ---- radio: gloss plate, dial strip with a scale and a pointer, presets, two knobs ----
            g.Mat(CabMesh.Gloss, Sw.PlasticGloss);
            g.Box(new Vector3(0f, 0f, -0.003f), new Vector3(0.21f, 0.068f, 0.008f), 32);
            g.Mat(CabMesh.Face);
            g.Flat(-0.056f, -0.004f, 0.056f, 0.019f, -0.0076f);
            g.Mat(CabMesh.Radio);
            g.Flat(-0.050f, 0.0015f, 0.050f, 0.0023f, -0.0082f);
            for (int k = 0; k <= 24; k++)
            {
                float x = -0.048f + 0.004f * k;
                float len = k % 4 == 0 ? 0.008f : (k % 2 == 0 ? 0.0055f : 0.0035f);
                g.Flat(x - 0.0004f, 0.004f, x + 0.0004f, 0.004f + len, -0.0082f);
            }
            g.Flat(0.0170f, -0.003f, 0.0190f, 0.018f, -0.0087f);          // the pointer, lit with the scale
            g.Mat(CabMesh.Plastic, Sw.CabTrim);
            for (int i = -2; i <= 2; i++) g.Box(new Vector3(i * 0.022f, -0.02f, -0.0085f), new Vector3(0.017f, 0.011f, 0.004f), 32);
            for (int s = -1; s <= 1; s += 2)
            {
                g.Mat(CabMesh.Plastic, Sw.Black);
                CabDash.Knob(g, new Vector3(s * 0.083f, 0f, -0.007f), 0.012f, 0.016f);
                g.Mat(CabMesh.Chrome, Sw.Chrome);
                g.Torus(new Vector3(s * 0.083f, 0f, -0.0078f), axis, 0.0138f, 0.0014f, 20, 6);
                g.Mat(CabMesh.Radio);
                g.Flat(s * 0.083f - 0.0008f, 0.003f, s * 0.083f + 0.0008f, 0.009f, -0.0234f);
            }

            // ---- climate: three knobs on a satin plate, each with a pointer and a ring of marks ----
            g.Mat(CabMesh.Plastic, Sw.CabTrim);
            g.Box(new Vector3(0f, -0.088f, -0.002f), new Vector3(0.21f, 0.06f, 0.006f), 32);
            for (int i = -1; i <= 1; i++)
            {
                float x = i * 0.066f;
                g.Mat(CabMesh.Plastic, Sw.Black);
                CabDash.Knob(g, new Vector3(x, -0.088f, -0.005f), 0.017f, 0.018f);
                g.Mat(CabMesh.Glow);
                g.Flat(x - 0.001f, -0.088f + 0.004f, x + 0.001f, -0.088f + 0.014f, -0.0234f);
                for (int k = -2; k <= 2; k++) g.Tick(x, -0.088f, k * 55f, 0.0215f, 0.0255f, 0.0016f, -0.0054f);
            }

            // ---- open cubby with a power socket ----
            g.Mat(CabMesh.Plastic, Sw.Black);
            g.Flat(-0.092f, -0.20f, 0.092f, -0.14f, -0.0006f);
            g.Mat(CabMesh.Plastic, Sw.CabTrim);
            g.Box(new Vector3(0f, -0.137f, -0.002f), new Vector3(0.196f, 0.006f, 0.007f), 32);
            g.Box(new Vector3(0f, -0.203f, -0.002f), new Vector3(0.196f, 0.006f, 0.010f), 32);
            g.Box(new Vector3(-0.095f, -0.17f, -0.002f), new Vector3(0.006f, 0.06f, 0.007f), 32 | 4 | 8);
            g.Box(new Vector3(0.095f, -0.17f, -0.002f), new Vector3(0.006f, 0.06f, 0.007f), 32 | 4 | 8);
            g.Mat(CabMesh.Chrome, Sw.Chrome);
            g.Torus(new Vector3(0.055f, -0.17f, -0.0015f), axis, 0.010f, 0.0018f, 18, 6);
        }
    }
}
