using UnityEngine;

namespace MESOCYCLONE
{
    /// <summary>A. Utility poles and wires along the road: a wooden pole every 60 m on the right side (the line
    /// crosses to the left just before the gas station), crossarm, three insulators, three sagging wires, a
    /// transformer can on every sixth pole. In the damage swath the poles lean, are snapped or lie on the verge and
    /// the wires are gone. Poles: 16 per mesh, visible to 2.5 km. Wires and insulators: 4 spans per mesh, cross-fade
    /// out at about 400 m.</summary>
    public static class PolesBuilder
    {
        const float Lat = 9.5f;
        const float CrossS = 15630f;                 // beyond this chainage the line runs on the left side
        const int First = -10, Last = 291;           // pole index: chainage = index * 60 m
        const float ArmY = 10.2f, WireY = 10.47f, Sag = 1.15f;
        static readonly float[] ArmX = { -1.05f, 0.32f, 1.05f };
        const int Standing = 0, Lean = 1, Snapped = 2, Flat = 3, Gone = 4;

        static double PoleS(int k)
        {
            double s = k * (double)Config.PoleSpacing;
            // keep the side roads and the town road clear
            if (System.Math.Abs(s - 3300.0) < 1.0 || System.Math.Abs(s - 9900.0) < 1.0 || System.Math.Abs(s - 15000.0) < 1.0) s += 9.0;
            return s;
        }

        static float PoleLat(int k) => PoleS(k) > CrossS ? -Lat : Lat;

        public static void Build(Transform root)
        {
            int n = Last - First + 1;
            var state = new int[n];
            var rng = new Rng(4101);
            for (int i = 0; i < n; i++)
            {
                if (!WorldProps.InSwath(PoleS(First + i))) continue;
                float v = rng.Value;
                state[i] = v < 0.40f ? Lean : (v < 0.70f ? Snapped : (v < 0.86f ? Flat : Gone));
            }
            var holder = new GameObject("Poles").transform;
            holder.SetParent(root, false);

            // ---- poles: 16 per chunk ----
            for (int c0 = 0; c0 < n; c0 += 16)
            {
                var ch = new Chunk(PoleS(First + Mathf.Min(n - 1, c0 + 8)), Lat);
                for (int i = c0; i < Mathf.Min(n, c0 + 16); i++) Pole(ch, First + i, state[i], ref rng);
                GameObject go = ch.Emit("PoleChunk", holder, true, WorldProps.Atlas);
                WorldProps.Lod(go, 2500f);
            }

            // ---- wires and insulators: 4 spans per chunk ----
            for (int c0 = 0; c0 < n - 1; c0 += 4)
            {
                var ch = new Chunk(PoleS(First + Mathf.Min(n - 1, c0 + 2)), Lat);
                bool any = false;
                for (int i = c0; i < Mathf.Min(n - 1, c0 + 4); i++)
                {
                    int k = First + i;
                    bool a = state[i] == Standing, b = state[i + 1] == Standing;
                    if (a) { Insulators(ch, k); any = true; }
                    if (a && b) Span(ch, k, k + 1);
                    else if (a) Hanging(ch, k, 1f, ref rng);
                    else if (b) Hanging(ch, k + 1, -1f, ref rng);
                }
                if (c0 + 4 >= n - 1 && state[n - 1] == Standing) { Insulators(ch, Last); any = true; }
                if (!any && ch.mb.VertexCount == 0) continue;
                GameObject go = ch.Emit("WireChunk", holder, false, WorldProps.Atlas);
                WorldProps.Lod(go, 400f);
            }
        }

        static Matrix4x4 Frame(Chunk ch, int k)
        {
            double s = PoleS(k);
            return Matrix4x4.TRS(ch.P(s, PoleLat(k), 0f), ch.RoadRot(s), Vector3.one);
        }

        static void StandingPole(MeshBuilder mb, int k, bool crossarm)
        {
            mb.Swatch(Sw.Wood);
            mb.Cylinder(Vector3.zero, new Vector3(0f, Config.PoleHeight, 0f), 0.16f, 0.11f, 6, true);
            if (crossarm) mb.Box(new Vector3(0f, ArmY, 0f), new Vector3(2.4f, 0.12f, 0.14f));
            if (((k % 6) + 6) % 6 == 0)
            {
                mb.Swatch(Sw.Grey40);
                mb.Cylinder(new Vector3(0.40f, 8.2f, 0f), new Vector3(0.40f, 9.25f, 0f), 0.27f, 0.27f, 8, true);
            }
        }

        static void Pole(Chunk ch, int k, int st, ref Rng rng)
        {
            MeshBuilder mb = ch.mb;
            double s = PoleS(k);
            float lat = PoleLat(k);
            if (st == Standing)
            {
                ch.At(s, lat);
                StandingPole(mb, k, true);
                return;
            }
            // fall direction: never toward the road (yaw 0 = along the road, 90 = straight away from it)
            float dirYaw = rng.Range(15f, 165f);
            Quaternion dir = ch.RoadRot(s, dirYaw);
            Vector3 p = ch.P(s, lat, 0f);
            if (st == Lean || st == Flat)
            {
                float lean = st == Lean ? rng.Range(20f, 75f) : rng.Range(86f, 88.5f);
                mb.Push(p + new Vector3(0f, st == Flat ? 0.16f : 0f, 0f), dir * Quaternion.Euler(lean, 0f, 0f) * Quaternion.Euler(0f, 90f - dirYaw, 0f));
                StandingPole(mb, k, rng.Chance(0.6f));
                return;
            }
            // a short stub with a splintered top
            float stub = st == Gone ? rng.Range(0.3f, 0.8f) : rng.Range(1.3f, 3.6f);
            ch.At(s, lat);
            mb.Swatch(Sw.Wood);
            mb.Cylinder(Vector3.zero, new Vector3(0f, stub, 0f), 0.16f, 0.15f, 6, true);
            mb.Swatch(Sw.DebrisWood);
            for (int i = 0; i < 3; i++)
            {
                float a = i * 2.1f + rng.Range(0f, 0.8f), h = rng.Range(0.35f, 0.8f);
                var b = new Vector3(Mathf.Cos(a) * 0.07f, stub - 0.05f, Mathf.Sin(a) * 0.07f);
                mb.Cylinder(b, b + new Vector3(Mathf.Cos(a) * 0.06f, h, Mathf.Sin(a) * 0.06f), 0.06f, 0.02f, 4, false);
            }
            if (st == Gone) return;
            // the rest of the pole lies on the verge; a few reach across the shoulder but never into the lane
            float len = Config.PoleHeight - stub;
            if (lat > 0f && rng.Chance(0.25f))
            {
                float reach = lat - rng.Range(4.1f, 5.0f);                       // tip stops at lateral 4.1 .. 5.0
                float c = Mathf.Clamp(reach / (len + 0.5f), 0f, 0.95f);
                dirYaw = -Mathf.Asin(c) * Mathf.Rad2Deg + (rng.Chance(0.5f) ? 0f : 180f + 2f * Mathf.Asin(c) * Mathf.Rad2Deg);
                dir = ch.RoadRot(s, dirYaw);
            }
            Vector3 a0 = p + dir * new Vector3(0f, 0.15f, 0.5f), a1 = p + dir * new Vector3(0f, 0.13f, 0.5f + len);
            mb.Reset();
            mb.Swatch(Sw.Wood);
            mb.Cylinder(a0, a1, 0.15f, 0.11f, 6, true);
        }

        static void Insulators(Chunk ch, int k)
        {
            MeshBuilder mb = ch.mb;
            ch.At(PoleS(k), PoleLat(k));
            mb.Swatch(Sw.Grey60);
            for (int j = 0; j < 3; j++) mb.Box(new Vector3(ArmX[j], ArmY + 0.16f, 0f), new Vector3(0.10f, 0.20f, 0.10f), 4);
        }

        static void Wire(MeshBuilder mb, Vector3 a, Vector3 b)
        {
            mb.Cylinder(a, b, Config.WireThickness * 0.6f, Config.WireThickness * 0.6f, 3, false);
        }

        static void Span(Chunk ch, int k0, int k1)
        {
            MeshBuilder mb = ch.mb;
            Matrix4x4 f0 = Frame(ch, k0), f1 = Frame(ch, k1);
            mb.Reset();
            mb.Swatch(Sw.Grey20);
            const int Seg = 8;
            for (int j = 0; j < 3; j++)
            {
                Vector3 a = f0.MultiplyPoint3x4(new Vector3(ArmX[j], WireY, 0f)), b = f1.MultiplyPoint3x4(new Vector3(ArmX[j], WireY, 0f));
                Vector3 prev = a;
                for (int i = 1; i <= Seg; i++)
                {
                    float t = (float)i / Seg;
                    Vector3 q = Vector3.Lerp(a, b, t);
                    q.y -= Sag * 4f * t * (1f - t);
                    Wire(mb, prev, q);
                    prev = q;
                }
            }
        }

        /// <summary>Broken wires hanging from the last standing pole toward the gap (sign = +1 north, -1 south).</summary>
        static void Hanging(Chunk ch, int k, float sign, ref Rng rng)
        {
            MeshBuilder mb = ch.mb;
            Matrix4x4 f = Frame(ch, k);
            mb.Reset();
            mb.Swatch(Sw.Grey20);
            for (int j = 0; j < 3; j++)
            {
                if (rng.Chance(0.3f)) continue;
                float run = rng.Range(3f, 9f), side = PoleLat(k) > 0f ? rng.Range(0f, 1.5f) : rng.Range(-1.5f, 0f);
                Vector3 a = f.MultiplyPoint3x4(new Vector3(ArmX[j], WireY, 0f));
                Vector3 b = f.MultiplyPoint3x4(new Vector3(ArmX[j] + side * 0.3f, WireY * 0.55f, sign * run * 0.45f));
                Vector3 c = f.MultiplyPoint3x4(new Vector3(ArmX[j] + side * 0.7f, 0.9f, sign * run * 0.85f));
                Vector3 d = f.MultiplyPoint3x4(new Vector3(ArmX[j] + side, 0.05f, sign * run));
                Wire(mb, a, b); Wire(mb, b, c); Wire(mb, c, d);
            }
        }
    }
}
