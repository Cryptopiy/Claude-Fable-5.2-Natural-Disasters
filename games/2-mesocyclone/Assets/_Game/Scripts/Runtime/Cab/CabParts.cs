using UnityEngine;
using UnityEngine.Rendering;

namespace MESOCYCLONE
{
    /// <summary>The moving parts (steering wheel, wiper, gauge needles: each built in its own local frame) and the
    /// three mirrors (housings and arms go into the static mesh; the glass faces are separate quads whose
    /// material the lead replaces).</summary>
    public static class CabParts
    {
        static Vector3 V(float x, float y, float z) { return new Vector3(x, y, z); }

        // ------------------------------------------------------------------ steering wheel
        /// <summary>Local frame: x right, y up in the wheel plane, z along the column away from the driver.
        /// Slots: 0 leather, 1 grained plastic, 2 chrome (swatch).</summary>
        public static CabMesh Wheel()
        {
            var w = new CabMesh();
            Vector3 axis = new Vector3(0f, 0f, 1f);
            const float tube = 0.016f;
            float R = CabDims.WheelRadius - tube;
            w.Mat(0);
            w.Torus(Vector3.zero, axis, R, tube, 56, 10);

            // three spokes dishing from the hub to the rim: 9, 3 and 6 o'clock
            w.Mat(1);
            for (int i = 0; i < 3; i++)
            {
                Vector3 d = i == 0 ? Vector3.left : (i == 1 ? Vector3.right : Vector3.down);
                Vector3 a = d * 0.04f + axis * 0.034f, b = d * (R - 0.004f) + axis * 0.004f;
                float len = (b - a).magnitude, wide = i == 2 ? 0.038f : 0.05f;
                w.Frame(a, b - a, Vector3.Cross(axis, d));
                w.Mat(1);
                w.Pill(CabMesh.RR(0f, 0f, 0.015f, wide, 0.006f, 3), 0f, len, 0.004f);
                if (i < 2)
                {
                    w.Mat(2, Sw.Aluminium);
                    w.Box(V(len * 0.55f, 0f, -0.0078f), V(len * 0.5f, 0.012f, 0.002f), 32);
                }
            }
            w.World();

            // hub: padded centre toward the driver, boss toward the column, a round emblem
            w.Mat(1);
            w.Pill(CabMesh.RR(0.045f, -0.004f, 0.05f, 0.105f, 0.02f, 4), -0.078f, 0.078f, 0.018f);
            w.Cyl(V(0f, 0f, 0.05f), V(0f, 0f, 0.078f), 0.052f, 0.048f, 20, false);
            w.Mat(2, Sw.Chrome);
            w.RingFlat(0f, -0.004f, 0.012f, 0.0155f, 0.0194f, 20);
            w.Disc(0f, -0.004f, 0.006f, 0.0194f, 12);
            return w;
        }

        // ------------------------------------------------------------------ wiper
        /// <summary>Local frame at the pivot: x along the arm, y in the glass plane, z out of the glass. The pivot
        /// stands 7.4 mm above the glass plane, so the rubber edge at z = -2.5 mm rides 5 mm above the glass.</summary>
        public static CabMesh Wiper()
        {
            var w = new CabMesh();
            float from = CabDims.WiperBladeFrom, tip = CabDims.WiperArm, mid = (from + tip) * 0.5f, len = tip - from;
            w.Mat(0);
            w.Cyl(V(0f, 0f, -0.004f), V(0f, 0f, 0.017f), 0.016f, 0.013f, 12, true);
            w.Box(V(0.07f, 0f, 0.014f), V(0.14f, 0.02f, 0.008f));                       // spring housing
            w.Box(V(mid * 0.5f + 0.0675f, 0f, 0.016f), V(mid - 0.13f, 0.009f, 0.005f)); // arm rod up to the blade's middle
            w.Box(V(mid, 0f, 0.012f), V(0.02f, 0.012f, 0.012f));                        // hook
            w.Box(V(mid, 0f, 0.0082f), V(len * 0.6f, 0.006f, 0.006f));                  // bridge
            w.Box(V(mid - len * 0.3f, 0f, 0.006f), V(0.012f, 0.008f, 0.006f));          // claws
            w.Box(V(mid + len * 0.3f, 0f, 0.006f), V(0.012f, 0.008f, 0.006f));
            w.Box(V(mid, 0f, 0.0035f), V(len, 0.009f, 0.004f));                         // blade spine
            w.Box(V(mid, 0f, 0f), V(len, 0.004f, 0.005f));                              // rubber
            return w;
        }

        // ------------------------------------------------------------------ gauge needle
        /// <summary>Local frame: the needle points along +y, the driver looks along +z. Slots: 0 needle, 1 gloss (swatch).</summary>
        public static CabMesh Needle(float length, float tail, float width, float hub)
        {
            var w = new CabMesh();
            w.Mat(0);
            w.Tick(0f, 0f, 0f, -tail, length, width, 0f, width * 0.35f);
            w.Mat(1, Sw.Black);
            CabDash.Knob(w, V(0f, 0f, 0.003f), hub, 0.006f);
            return w;
        }

        // ------------------------------------------------------------------ mirrors
        /// <summary>Normal of a flat mirror at c that shows 'target' to the driver's eye.</summary>
        public static Vector3 MirrorNormal(Vector3 c, Vector3 target)
        {
            return ((CabDims.Eye - c).normalized + (target - c).normalized).normalized;
        }

        /// <summary>The rear-view mirror shows the rear window. Its centre is only 35 mm from the raked glass, so
        /// the yaw toward the driver is limited to 7 degrees: more and its driver-side end would pass through the pane.</summary>
        public static Vector3 RearMirrorNormal
        {
            get
            {
                Vector3 n = MirrorNormal(CabDims.MirrorCentre, new Vector3(0f, 1.46f, CabDims.RearWallZ));
                float pitch = Mathf.Asin(Mathf.Clamp(-n.y, -1f, 1f));
                float yaw = Mathf.Min(Mathf.Atan2(-n.x, -n.z), 7f * Mathf.Deg2Rad);
                return new Vector3(-Mathf.Sin(yaw) * Mathf.Cos(pitch), -Mathf.Sin(pitch), -Mathf.Cos(yaw) * Mathf.Cos(pitch));
            }
        }

        public static Vector3 SideMirrorNormal(int side)
        {
            Vector3 c = side < 0 ? CabDims.SideMirrorL : CabDims.SideMirrorR;
            return MirrorNormal(c, new Vector3(side * 2.5f, 1.0f, -20f));
        }

        static void MirrorFrame(CabMesh g, Vector3 c, Vector3 n)
        {
            Vector3 fx = Vector3.Cross(Vector3.up, -n).normalized;
            g.Frame(c, fx, Vector3.Cross(-n, fx));         // frame z = -n: behind the glass
        }

        /// <summary>Housings, stalk and arms of the three mirrors, into the static mesh.</summary>
        public static void Housings(CabMesh g)
        {
            g.mirror = false; g.NoToward();

            // rear-view mirror: slim housing, short stalk to a pad on the glass
            Vector3 n = RearMirrorNormal;
            MirrorFrame(g, CabDims.MirrorCentre, n);
            g.Mat(CabMesh.Plastic, Sw.Black);
            g.Pill(CabMesh.RR(0.005f, 0f, 0.008f, 0.076f, 0.0035f, 2), -0.124f, 0.124f, 0.0015f);
            Vector3 back = g.P(0f, 0f, 0.008f);
            g.World();
            Vector3 pad = back - CabLayout.GlassOut * Vector3.Dot(back - CabLayout.GlassOrigin, CabLayout.GlassOut);   // foot of the stalk on the glass
            g.Cyl(back, pad - CabLayout.GlassOut * 0.008f, 0.007f, 0.007f, 10, false);
            g.Ball(back, 0.006f, 10, 6);
            g.Cyl(pad - CabLayout.GlassOut * 0.010f, pad - CabLayout.GlassOut * 0.0006f, 0.015f, 0.018f, 14, true);

            // side mirrors: chrome shells on two arms from the door skin just below the sill
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 c = s < 0 ? CabDims.SideMirrorL : CabDims.SideMirrorR;
                MirrorFrame(g, c, SideMirrorNormal(s));
                g.Mat(CabMesh.Chrome, Sw.Chrome);
                g.Pill(CabMesh.RR(0.037f, 0f, 0.07f, 0.128f, 0.03f, 4), -0.098f, 0.098f, 0.02f);
                g.Mat(CabMesh.Plastic, Sw.Black);
                g.Pill(CabMesh.RR(0.006f, 0f, 0.01f, 0.118f, 0.004f, 2), -0.09f, 0.09f, 0.003f);     // dark rim behind the glass
                Vector3 foot = g.P(0f, -0.05f, 0.04f);
                g.World();
                float xs = s * 0.925f;
                g.Cyl(V(xs, 1.092f, 0.50f), foot + V(0f, 0f, -0.03f), 0.009f, 0.008f, 8, false);
                g.Cyl(V(xs, 1.092f, 0.60f), foot + V(0f, 0f, 0.03f), 0.009f, 0.008f, 8, false);
                g.Box(V(xs, 1.088f, 0.55f), V(0.016f, 0.045f, 0.15f), s < 0 ? 2 : 1);
            }
            g.World();
        }

        /// <summary>The glass of a mirror as its own object: a quad centred on the transform, spanning local x and y,
        /// visible from (and reflecting toward) local +z. UV (0,0) at local (-w/2, -h/2).</summary>
        public static Renderer Face(Transform cab, string name, Vector3 centre, Vector3 normal, float w, float h, Material mat)
        {
            var mb = new MeshBuilder();
            float hw = w * 0.5f, hh = h * 0.5f;
            mb.QuadUV(new Vector3(-hw, -hh, 0f), new Vector3(hw, -hh, 0f), new Vector3(hw, hh, 0f), new Vector3(-hw, hh, 0f),
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f));
            GameObject go = mb.ToObject(name, cab, mat);
            go.transform.localPosition = centre;
            go.transform.localRotation = Quaternion.LookRotation(normal, Vector3.up);
            var mr = go.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = ShadowCastingMode.Off;
            return mr;
        }
    }
}
