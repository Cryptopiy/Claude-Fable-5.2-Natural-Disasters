using UnityEngine;

namespace MESOCYCLONE
{
    /// <summary>The 40/20/40 bench: two cloth seats with bolsters, reclined backs and head restraints on chrome
    /// posts, and the centre section with its back folded down as an armrest. Every piece is a closed, bevelled
    /// solid, so the backs can be looked at from any side. The head restraint stays 25 cm behind the eye.</summary>
    public static class CabSeats
    {
        static Vector3 V(float x, float y, float z) { return new Vector3(x, y, z); }

        public static void Build(CabMesh g)
        {
            g.NoToward();
            for (int s = 0; s < 2; s++) { g.mirror = s == 1; Seat(g); }
            g.mirror = false;
            Centre(g);
            g.World();
        }

        /// <summary>The driver's seat (x from -0.66 to -0.10); the passenger's is its mirror image.</summary>
        static void Seat(CabMesh g)
        {
            g.World();
            // plinth
            g.Mat(CabMesh.Plastic, Sw.CabPlastic);
            g.Box(V(-0.38f, 0.585f, -0.03f), V(0.52f, 0.17f, 0.44f), 4 | 8);
            g.Box(V(-0.675f, 0.60f, -0.10f), V(0.02f, 0.06f, 0.16f), 2);            // recline lever housing
            g.Mat(CabMesh.Plastic, Sw.Black);
            g.Box(V(-0.105f, 0.84f, -0.13f), V(0.02f, 0.07f, 0.045f), 4);           // belt buckle
            g.Mat(CabMesh.Plastic, Sw.DeepRed);
            g.Box(V(-0.105f, 0.877f, -0.13f), V(0.015f, 0.005f, 0.03f), 4);

            // cushion, back, head restraint
            g.Mat(CabMesh.Fabric);
            g.Pill(CabMesh.RR(-0.01f, 0.735f, 0.54f, 0.15f, 0.05f, 4), -0.66f, -0.10f, 0.03f);
            g.Pill(CabMesh.RR(-0.288f, 1.086f, 0.12f, 0.66f, 0.045f, 4, 13.1f), -0.65f, -0.11f, 0.03f);
            // the head restraint sits low and well back: the camera swings 18 cm rearward when it looks over the shoulder
            g.Pill(CabMesh.RR(-0.352f, 1.425f, 0.095f, 0.15f, 0.04f, 4, 8f), -0.50f, -0.26f, 0.035f);
            g.Mat(CabMesh.Chrome, Sw.Chrome);
            g.Cyl(V(-0.44f, 1.36f, -0.335f), V(-0.44f, 1.42f, -0.345f), 0.006f, 0.006f, 8, false);
            g.Cyl(V(-0.32f, 1.36f, -0.335f), V(-0.32f, 1.42f, -0.345f), 0.006f, 0.006f, 8, false);

            // bolsters: along the cushion sides (frame x = forward, profile z = -world x)
            g.Mat(CabMesh.Fabric);
            g.Frame(Vector3.zero, Vector3.forward, Vector3.up);
            g.Pill(CabMesh.RR(0.622f, 0.795f, 0.075f, 0.07f, 0.03f, 3), -0.25f, 0.23f, 0.025f);
            g.Pill(CabMesh.RR(0.138f, 0.795f, 0.075f, 0.07f, 0.03f, 3), -0.25f, 0.23f, 0.025f);
            // bolsters along the back: frame x runs up the reclined back
            Vector3 upBack = new Vector3(0f, Mathf.Cos(13.1f * Mathf.Deg2Rad), -Mathf.Sin(13.1f * Mathf.Deg2Rad));
            g.Frame(V(0f, 0.86f, -0.176f), upBack, Vector3.left);
            g.Pill(CabMesh.RR(-0.005f, 0.622f, 0.07f, 0.07f, 0.03f, 3), 0f, 0.52f, 0.025f);
            g.Pill(CabMesh.RR(-0.005f, 0.138f, 0.07f, 0.07f, 0.03f, 3), 0f, 0.52f, 0.025f);
            g.World();
        }

        /// <summary>Centre section: a narrow cushion with the folded-down back as a padded armrest.</summary>
        static void Centre(CabMesh g)
        {
            g.World();
            g.Mat(CabMesh.Plastic, Sw.CabPlastic);
            g.Box(V(0f, 0.585f, -0.03f), V(0.19f, 0.17f, 0.42f), 4 | 8);
            g.Mat(CabMesh.Fabric);
            g.Pill(CabMesh.RR(-0.01f, 0.725f, 0.52f, 0.13f, 0.045f, 4), -0.092f, 0.092f, 0.02f);
            g.Frame(Vector3.zero, Vector3.forward, Vector3.up);
            g.Mat(CabMesh.Leather);
            g.Pill(CabMesh.RR(0f, 0.843f, 0.17f, 0.11f, 0.03f, 4), -0.27f, 0.20f, 0.02f);
            // two cup holders in a tray at the front of the armrest
            g.World();
            g.Mat(CabMesh.Plastic, Sw.CabPlastic);
            g.Box(V(0f, 0.80f, 0.265f), V(0.17f, 0.05f, 0.11f), 4);
            g.Frame(V(0f, 0.8256f, 0.265f), Vector3.right, Vector3.forward);
            g.Mat(CabMesh.Plastic, Sw.Black);
            g.Disc(-0.042f, 0f, 0.034f, 0f, 20);
            g.Disc(0.042f, 0f, 0.034f, 0f, 20);
            g.Mat(CabMesh.Chrome, Sw.Aluminium);
            g.Torus(new Vector3(-0.042f, 0f, 0f), new Vector3(0f, 0f, 1f), 0.035f, 0.002f, 24, 6);
            g.Torus(new Vector3(0.042f, 0f, 0f), new Vector3(0f, 0f, 1f), 0.035f, 0.002f, 24, 6);
            g.World();
        }
    }
}
