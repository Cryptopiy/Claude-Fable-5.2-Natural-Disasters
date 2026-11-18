using UnityEngine;

namespace MESOCYCLONE
{
    /// <summary>Things the wind throws past the truck, as lit instanced meshes (real headlights and flashes light them).
    /// Beat 6 (about 512-566 s, strongest 535-555): leaves, twigs, a leafy branch, shingles, insulation, siding,
    /// planks, cornstalks and scraps cross the headlight cone with the wind and rise, tumbling, at 15-40 m/s, some
    /// just over the hood. The gust front (236-256 s): straw, leaves, grass and grit skate down the road from ahead.
    /// A few authored pieces strike or skim the windscreen. Everything is a designed trajectory and a pure function
    /// of story time: spawn slots are hashed, intensities are tabled from the story curves once at load.</summary>
    [DefaultExecutionOrder(-75)]
    public sealed class DebrisSwarm : MonoBehaviour
    {
        const float Slot = 0.05f, MaxLife = 2.8f;
        const float W6From = 505f, W6To = 575f, WgFrom = 232f, WgTo = 262f;
        const int MaxPerKind = 96;
        const float DesignSpeed = 24f;      // closing speed of the road at the cinematic speed (trajectories stay pure in T)

        // envelopes over the story; multiplied by the gust, wind and tornado power of the moment (tables below)
        static readonly Curve env6 = new Curve("debris.env6", 510f, 0f, 516f, 0.22f, 528f, 0.5f, 535f, 1f, 555f, 1f, 560f, 0.5f, 566f, 0f);
        static readonly Curve envG = new Curve("debris.envG", 236f, 0f, 239f, 0.7f, 241f, 1f, 247f, 0.8f, 252f, 0.25f, 256f, 0f);

        struct Kind
        {
            public float w6, wg, sMin, sMax, spinMin, spinMax, flutter, glassClear;
            public bool large;
        }

        // weights in beat 6 / gust front, size scale, spin rad/s, flutter (light, fluttering pieces), clearance when flat
        // on the glass, too large to pass near the cab
        static readonly Kind[] Kinds =
        {
            new Kind { w6 = 14, wg = 8,  sMin = 0.8f, sMax = 1.3f, spinMin = 8, spinMax = 20, flutter = 0.5f,  glassClear = 0.012f },  // LeafGreen
            new Kind { w6 = 12, wg = 18, sMin = 0.8f, sMax = 1.3f, spinMin = 8, spinMax = 20, flutter = 0.5f,  glassClear = 0.012f },  // LeafDry
            new Kind { w6 = 10, wg = 4,  sMin = 0.7f, sMax = 1.3f, spinMin = 5, spinMax = 12, flutter = 0.1f,  glassClear = 0.03f },   // Twig
            new Kind { w6 = 5,  wg = 0,  sMin = 0.8f, sMax = 1.2f, spinMin = 2, spinMax = 5,  flutter = 0.05f, glassClear = 0.2f, large = true },  // Branch
            new Kind { w6 = 12, wg = 0,  sMin = 0.8f, sMax = 1.2f, spinMin = 6, spinMax = 14, flutter = 0.15f, glassClear = 0.01f },   // Shingle
            new Kind { w6 = 7,  wg = 0,  sMin = 0.7f, sMax = 1.3f, spinMin = 2, spinMax = 6,  flutter = 0.35f, glassClear = 0.06f },   // Insulation
            new Kind { w6 = 5,  wg = 0,  sMin = 0.8f, sMax = 1.2f, spinMin = 4, spinMax = 9,  flutter = 0.05f, glassClear = 0.1f, large = true },   // Siding
            new Kind { w6 = 4,  wg = 0,  sMin = 0.8f, sMax = 1.2f, spinMin = 3, spinMax = 7,  flutter = 0f,    glassClear = 0.1f, large = true },   // Plank
            new Kind { w6 = 7,  wg = 2,  sMin = 0.8f, sMax = 1.2f, spinMin = 3, spinMax = 8,  flutter = 0.05f, glassClear = 0.1f, large = true },   // Cornstalk
            new Kind { w6 = 8,  wg = 8,  sMin = 0.8f, sMax = 1.2f, spinMin = 6, spinMax = 14, flutter = 0.4f,  glassClear = 0.012f },  // CornLeaf
            new Kind { w6 = 4,  wg = 30, sMin = 0.8f, sMax = 1.4f, spinMin = 10, spinMax = 25, flutter = 0.2f, glassClear = 0.01f },   // Straw
            new Kind { w6 = 4,  wg = 18, sMin = 0.02f, sMax = 0.045f, spinMin = 10, spinMax = 20, flutter = 0f, glassClear = 0.02f },  // Grit (unit mesh)
            new Kind { w6 = 4,  wg = 0,  sMin = 0.8f, sMax = 1.2f, spinMin = 6, spinMax = 12, flutter = 0.35f, glassClear = 0.01f },   // Felt
            new Kind { w6 = 2,  wg = 12, sMin = 0.8f, sMax = 1.2f, spinMin = 5, spinMax = 12, flutter = 0.3f,  glassClear = 0.05f },   // Grass
            new Kind { w6 = 2,  wg = 0,  sMin = 0.8f, sMax = 1.2f, spinMin = 6, spinMax = 12, flutter = 0.1f,  glassClear = 0.01f },   // Osb
        };

        // authored glass events: time at the glass, kind, offset off the glass (0 = it strikes), across (-0.55..0.55),
        // up the slope (0..0.766). Beat 6 events sit just after flashes (lit) or at the gaze cue of 537 s.
        static readonly float[] GlassDef =
        {
            239.62f, DebrisKit.Straw,     0f,   0.20f, 0.30f,
            241.30f, DebrisKit.LeafDry,   0f,  -0.30f, 0.45f,
            243.40f, DebrisKit.Grass,     0.5f, 0.35f, 0.50f,
            247.05f, DebrisKit.LeafGreen, 0f,   0.10f, 0.25f,
            249.80f, DebrisKit.Straw,     0.8f,-0.40f, 0.40f,
            521.25f, DebrisKit.Shingle,   0.9f, 0.25f, 0.55f,
            527.40f, DebrisKit.LeafGreen, 0f,  -0.35f, 0.35f,
            532.25f, DebrisKit.Insulation,0f,   0.05f, 0.40f,
            537.70f, DebrisKit.Twig,      1.6f,-0.10f, 0.50f,
            541.30f, DebrisKit.LeafDry,   0f,   0.40f, 0.20f,
            545.90f, DebrisKit.Felt,      0.4f,-0.20f, 0.60f,
            549.30f, DebrisKit.Osb,       1.2f, 0.30f, 0.45f,
            553.60f, DebrisKit.Shingle,   0f,  -0.45f, 0.30f,
            558.25f, DebrisKit.CornLeaf,  0.3f, 0.15f, 0.50f,
        };
        const int GlassStride = 5;

        Mesh[] meshes;
        Matrix4x4[][] mats;
        int[] counts;
        float[] wSum6, wSumG;
        float[] i6, iG;
        Vector2[] wind6, windG;
        float rate6, rateG, glassOn, dirOverride;
        Material mat;
        RenderParams rp;
        Matrix4x4 l2w;

        public static DebrisSwarm Build(Transform truck)
        {
            var go = new GameObject("DebrisSwarm");
            go.transform.SetParent(truck, false);
            var d = go.AddComponent<DebrisSwarm>();
            d.Init();
            return d;
        }

        void Init()
        {
            int nk = DebrisKit.KindCount;
            meshes = new Mesh[nk];
            mats = new Matrix4x4[nk][];
            counts = new int[nk];
            for (int k = 0; k < nk; k++) { meshes[k] = DebrisKit.Build(k, 3100u + (uint)k * 7u); mats[k] = new Matrix4x4[MaxPerKind]; }
            wSum6 = new float[nk]; wSumG = new float[nk];
            float a = 0f, b = 0f;
            for (int k = 0; k < nk; k++) { a += Kinds[k].w6; b += Kinds[k].wg; wSum6[k] = a; wSumG[k] = b; }

            float scale = Tune.F("debris.scale", 1f) * (Cli.Final ? 1f : 0.5f);
            rate6 = 30f * scale;                         // pieces per second at full strength (about 45 in the air at once)
            rateG = 20f * scale;
            glassOn = Tune.F("debris.glass", 1f);
            dirOverride = Tune.F("debris.dir", 0f);      // 0: with the story wind; +1 left to right; -1 right to left

            // intensity and wind direction per spawn slot, from the story curves (pure functions of story time)
            var st = new StoryState();
            Table(W6From, W6To, env6, false, ref st, out i6, out wind6);
            Table(WgFrom, WgTo, envG, true, ref st, out iG, out windG);

            mat = WeatherLibrary.Mat(WeatherLibrary.DebrisMat, "atlas");
            if (mat != null && !mat.enableInstancing) mat.enableInstancing = true;
            rp = HailField.MakeParams(mat);
        }

        static void Table(float from, float to, Curve env, bool gust, ref StoryState st, out float[] inten, out Vector2[] wind)
        {
            int n = Mathf.CeilToInt((to - from) / Slot) + 1;
            inten = new float[n]; wind = new Vector2[n];
            for (int k = 0; k < n; k++)
            {
                float t = from + k * Slot;
                Story.Evaluate(t, ref st);
                float f = gust
                    ? Mathf.Clamp01(0.3f + 0.5f * st.gust + 0.3f * st.wind / 36f)
                    : Mathf.Clamp01(0.25f + 0.35f * st.gust + 0.25f * st.wind / 38f + 0.25f * st.torPower);
                inten[k] = env.Eval(t) * f;
                float a = (st.windDir - (float)PathDef.HeadingDeg(t * Config.SpeedBrisk)) * Mathf.Deg2Rad;
                wind[k] = new Vector2(Mathf.Sin(a), Mathf.Cos(a));        // where the wind blows to, truck frame (x right, y forward)
            }
        }

        void LateUpdate()
        {
            for (int k = 0; k < counts.Length; k++) counts[k] = 0;
            if (!Director.Running || mat == null) return;
            float T = Director.T;
            bool in6 = T > W6From && T < W6To + MaxLife, inG = T > WgFrom && T < WgTo + MaxLife;
            if (!in6 && !inG) return;
            l2w = transform.parent.localToWorldMatrix;

            if (in6) Window(T, W6From, i6, wind6, false, rate6);
            if (inG) Window(T, WgFrom, iG, windG, true, rateG);
            if (glassOn > 0f)
                for (int e = 0; e < GlassDef.Length; e += GlassStride)
                    if (Mathf.Abs(T - GlassDef[e]) < 2.5f) GlassPiece(e, T);

            for (int k = 0; k < counts.Length; k++)
                if (counts[k] > 0) Graphics.RenderMeshInstanced(rp, meshes[k], 0, mats[k], counts[k]);
        }

        void Window(float T, float from, float[] inten, Vector2[] wind, bool gust, float rateMax)
        {
            int kLast = Mathf.Min(inten.Length - 1, Mathf.FloorToInt((T - from) / Slot));
            int kFirst = Mathf.Max(0, Mathf.FloorToInt((T - MaxLife - from) / Slot));
            for (int k = kFirst; k <= kLast; k++)
            {
                float lambda = inten[k] * rateMax * Slot;
                if (lambda <= 0.0001f) continue;
                uint hk;
                unchecked { hk = WeatherLibrary.Hash((uint)k * 2654435761u + (gust ? 0x51ED27u : 0x2F1A7u)); }
                int cnt = Mathf.Min(3, Mathf.FloorToInt(lambda + WeatherLibrary.U(hk, 0)));     // stochastic rounding: mean = lambda
                for (int j = 0; j < cnt; j++)
                {
                    uint h;
                    unchecked { h = WeatherLibrary.Hash(hk + (uint)(j + 1) * 0x9E3779B9u); }
                    float t0 = from + (k + WeatherLibrary.U(h, 1)) * Slot;
                    float age = T - t0;
                    if (age < 0f) continue;
                    if (gust) GustPiece(h, age, wind[k], inten[k]);
                    else CrossPiece(h, age, wind[k], inten[k]);
                }
            }
        }

        int Pick(float u, bool gust)
        {
            float[] sum = gust ? wSumG : wSum6;
            float x = u * sum[sum.Length - 1];
            for (int k = 0; k < sum.Length; k++) if (x < sum[k]) return k;
            return sum.Length - 1;
        }

        float LateralDir(Vector2 wind)
        {
            if (dirOverride > 0.5f) return 1f;
            if (dirOverride < -0.5f) return -1f;
            return wind.x >= 0f ? 1f : -1f;
        }

        static float U(uint h, uint k) => WeatherLibrary.U(h, k);

        /// <summary>Beat 6: a piece crossing the headlights with the wind and rising; the truck closes on it.</summary>
        void CrossPiece(uint h, float age, Vector2 wind, float inten)
        {
            int kind = Pick(U(h, 2), false);
            float dir = LateralDir(wind);
            bool near = U(h, 3) < 0.22f && !Kinds[kind].large;
            float z0, y0, x0, x1, rise, closing, hop;
            if (near)
            {   // just in front of the glass, over the hood line
                z0 = Mathf.Lerp(3.6f, 7f, U(h, 4)); y0 = Mathf.Lerp(1.35f, 2.6f, U(h, 5));
                x0 = Mathf.Lerp(6f, 9f, U(h, 6)); x1 = Mathf.Lerp(6f, 9f, U(h, 7));
                rise = Mathf.Lerp(0.4f, 2.2f, U(h, 8)); closing = DesignSpeed * Mathf.Lerp(0.04f, 0.25f, U(h, 9)); hop = 0.3f;
            }
            else
            {   // through the cone, from outside the light to outside the light
                float q = U(h, 4);
                z0 = 7f + 36f * q * q; float yq = U(h, 5); y0 = 0.15f + 2.2f * yq * yq;
                x0 = 0.5f * z0 + Mathf.Lerp(2.5f, 5.5f, U(h, 6)); x1 = 0.5f * z0 + Mathf.Lerp(2.5f, 5.5f, U(h, 7));
                rise = Mathf.Lerp(1f, 6f, U(h, 8)); closing = DesignSpeed * Mathf.Lerp(0.25f, 0.9f, U(h, 9)); hop = Mathf.Lerp(0f, 1.2f, U(h, 10));
            }
            float vx = Mathf.Lerp(15f, 40f, U(h, 11)) * (0.7f + 0.3f * inten) * (Kinds[kind].large ? 0.7f : 1f);
            vx = Mathf.Max(vx, (x0 + x1) / (MaxLife - 0.05f));        // the whole crossing fits in the spawn look-back
            float dur = (x0 + x1) / vx;
            if (age > dur) return;
            if (z0 - closing * dur < 3.6f) closing = Mathf.Max(0f, (z0 - 3.6f) / dur);    // never into the truck
            float u = age / dur;
            var p = new Vector3(dir * Mathf.Lerp(-x0, x1, u), y0 + rise * u + hop * Mathf.Sin(Mathf.PI * u), z0 - closing * age);
            Emit(kind, h, p, age);
        }

        /// <summary>Gust front: straw, leaves and grit come down the road from ahead, skating low or airborne, and pass
        /// beside the truck or over its roof.</summary>
        void GustPiece(uint h, float age, Vector2 wind, float inten)
        {
            int kind = Pick(U(h, 2), true);
            float x0 = Mathf.Lerp(-11f, 11f, U(h, 3)), z0 = Mathf.Lerp(26f, 58f, U(h, 4)), z1 = -14f;
            bool skate = U(h, 5) < 0.6f;
            float y0 = skate ? Mathf.Lerp(0.03f, 0.3f, U(h, 6)) : Mathf.Lerp(0.4f, 3.2f, U(h, 6));
            float y1 = y0 + (skate ? 0f : Mathf.Lerp(0f, 2f, U(h, 8)));
            float x1 = x0 + wind.x * 10f + Mathf.Lerp(-4f, 4f, U(h, 7));
            float f = z0 / (z0 - z1);
            float xc = Mathf.Lerp(x0, x1, f), yc = Mathf.Lerp(y0, y1, f);
            if (Mathf.Abs(xc) < 1.9f && yc < 2.6f)
            {   // keep the cab clear: shift the path sideways
                float push = (xc >= 0f ? 1f : -1f) * (1.9f - Mathf.Abs(xc) + 0.4f);
                x0 += push; x1 += push;
            }
            float sp = Mathf.Lerp(22f, 42f, U(h, 9)) * (0.75f + 0.25f * inten);
            float len = Mathf.Sqrt((x1 - x0) * (x1 - x0) + (z1 - z0) * (z1 - z0));
            sp = Mathf.Max(sp, len / (MaxLife - 0.05f));
            float dur = len / sp;
            if (age > dur) return;
            float u = age / dur;
            var p = new Vector3(Mathf.Lerp(x0, x1, u), Mathf.Lerp(y0, y1, u), Mathf.Lerp(z0, z1, u));
            if (skate) p.y = y0 + Mathf.Abs(Mathf.Sin(u * Mathf.PI * Mathf.Lerp(3f, 7f, U(h, 10)))) * Mathf.Lerp(0.12f, 0.6f, U(h, 11));
            Emit(kind, h, p, age);
        }

        void Emit(int kind, uint h, Vector3 p, float age)
        {
            if (counts[kind] >= MaxPerKind) return;
            Kind kd = Kinds[kind];
            if (kd.flutter > 0f)
            {
                float fq = Mathf.Lerp(2f, 4.5f, U(h, 20)), ph = U(h, 21) * 6.2832f;
                p += new Vector3(0f, Mathf.Sin(age * fq * 6.2832f + ph), 0.6f * Mathf.Cos(age * fq * 4.4f + ph)) * (kd.flutter * 0.6f);
            }
            float spin = Mathf.Lerp(kd.spinMin, kd.spinMax, U(h, 22));
            Quaternion q = Quaternion.AngleAxis(spin * age * Mathf.Rad2Deg, WeatherLibrary.Dir(h)) * Base(h);
            float s = Mathf.Lerp(kd.sMin, kd.sMax, U(h, 23));
            mats[kind][counts[kind]++] = l2w * Matrix4x4.TRS(p, q, new Vector3(s, s, s));
        }

        static Quaternion Base(uint h)
        {
            return Quaternion.Euler(U(h, 30) * 360f, U(h, 31) * 360f, U(h, 32) * 360f);
        }

        /// <summary>An authored piece that strikes (offset 0) or skims the windscreen: in from the wind side, along the
        /// glass, over the roof. Lies flat on the glass at the instant of contact.</summary>
        void GlassPiece(int e, float T)
        {
            float tHit = GlassDef[e];
            int kind = (int)GlassDef[e + 1];
            float off = GlassDef[e + 2], gu = GlassDef[e + 3], gv = GlassDef[e + 4];
            bool gust = tHit < 300f;
            Vector3 n = WeatherLibrary.GlassOut;
            uint h = WeatherLibrary.Hash((uint)(e + 1) * 0x2545F491u);
            Kind kd = Kinds[kind];
            float s = Mathf.Lerp(kd.sMin, kd.sMax, U(h, 23));
            float clear = kd.glassClear * s + off + 0.01f;
            Vector3 w = CabDims.GlassPoint(gu, gv) + n * clear;
            Vector3 w2 = CabDims.GlassPoint(gu * 0.9f, CabDims.GlassHeight + 0.05f) + n * (clear + 0.08f) + Vector3.up * 0.06f;
            float side;
            if (gust) side = 0f;
            else
            {
                int k = Mathf.Clamp(Mathf.FloorToInt((tHit - W6From) / Slot), 0, wind6.Length - 1);
                side = -LateralDir(wind6[k]);                 // comes from where the wind comes from
            }
            Vector3 p0 = gust ? w + new Vector3(Mathf.Lerp(-1.5f, 1.5f, U(h, 2)), 0.6f, 16f) : w + new Vector3(side * 7f, 0.4f, 8f);
            Vector3 p1 = w2 + new Vector3(gust ? Mathf.Lerp(-2f, 2f, U(h, 3)) : -side * Mathf.Lerp(1f, 3f, U(h, 3)), 3.5f, -6f);
            float s1 = gust ? 30f : 22f;
            float d0 = (w - p0).magnitude / s1, d1 = (w2 - w).magnitude / (s1 * 0.45f), d2 = (p1 - w2).magnitude / (s1 * 0.7f);
            float a = T - tHit;
            if (a < -d0 || a > d1 + d2) return;
            Vector3 p;
            if (a < 0f) p = Vector3.Lerp(w, p0, -a / d0);
            else if (a < d1) p = Vector3.Lerp(w, w2, a / d1);
            else p = Vector3.Lerp(w2, p1, (a - d1) / d2);
            // flat against the glass at contact and while it slides up it (so no part pokes through), tumbling before and
            // (faster, about a new axis) once it leaves the glass
            Quaternion flat = Quaternion.FromToRotation(Vector3.up, n) * Quaternion.AngleAxis(U(h, 4) * 360f, Vector3.up);
            float spin = Mathf.Lerp(kd.spinMin, kd.spinMax, U(h, 22));
            Quaternion q;
            if (a < 0f) q = Quaternion.AngleAxis(spin * a * Mathf.Rad2Deg, WeatherLibrary.Dir(h)) * flat;
            else if (a < d1) q = flat;
            else q = Quaternion.AngleAxis(spin * 1.8f * (a - d1) * Mathf.Rad2Deg, WeatherLibrary.Dir(h + 99u)) * flat;
            if (counts[kind] < MaxPerKind) mats[kind][counts[kind]++] = l2w * Matrix4x4.TRS(p, q, new Vector3(s, s, s));
        }

        /// <summary>Story times at which an authored piece strikes the glass (offset 0) in [a, b): for a knock in the
        /// sound, if wanted. Pure function.</summary>
        public static int GlassStrikes(float a, float b, float[] times)
        {
            int c = 0;
            for (int e = 0; e < GlassDef.Length; e += GlassStride)
                if (GlassDef[e + 2] <= 0f && GlassDef[e] >= a && GlassDef[e] < b)
                {
                    if (times != null && c < times.Length) times[c] = GlassDef[e];
                    c++;
                }
            return c;
        }
    }
}
