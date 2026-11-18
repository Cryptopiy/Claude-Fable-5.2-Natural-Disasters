using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MESOCYCLONE
{
    /// <summary>The visible channels of the lightning: branched cloud-to-ground bolts (one continuous random-walk
    /// channel, a few pixels wide at any distance) and anvil crawlers that grow outward across the cloud. Stateless
    /// with respect to story time: whatever flash is alive at T has its channel, so seeking and storyboards work.
    /// The channels are additive and drawn after the storm composite with no depth, so nothing can hide a channel
    /// behind the vortex: Spawn keeps bolts clear of the funnel and its dust instead.</summary>
    [DefaultExecutionOrder(-250)]
    public sealed class LightningVisuals : MonoBehaviour
    {
        const int Pool = 8;              // designed channels plus the ambient ground strikes of the last few seconds
        const int Steps = 112;           // unit steps of the main channel's random walk
        sealed class Item
        {
            public GameObject go; public Mesh mesh; public Material mat; public MeshRenderer mr;
            public float t0 = -999f; public bool crawler; public Flash f;
        }
        readonly Item[] items = new Item[Pool];
        readonly List<Vector3> verts = new List<Vector3>(8192);
        readonly List<Vector2> uvs = new List<Vector2>(8192);
        readonly List<int> tris = new List<int>(16384);
        // one strip at a time: its points and the brightness at each (uv.x of bolt_core)
        readonly List<Vector3> pts = new List<Vector3>(Steps + 1);
        readonly List<float> bri = new List<float>(Steps + 1);
        readonly float[] rawX = new float[Steps + 1], rawY = new float[Steps + 1], rawHead = new float[Steps + 1];
        readonly Vector3[] trunk = new Vector3[Steps + 1];
        float pxA;                       // 2 tan(fov/2) / pixel height: metres per pixel at 1 m from the eye
        Vector3 camLocal;                // the eye in the channel object's frame (the object stands on the ground at the flash)
        float armCeil = float.PositiveInfinity, armFloor = float.NegativeInfinity;   // a far web stays in this layer
        Transform root;
        static readonly int idColor = Shader.PropertyToID("_UnlitColor");
        const string MapName = "_UnlitColorMap";

        public static void Textures(List<TexDef> l)
        {
            l.Add(new TexDef("ramp", 64, PaintRamp) { mips = false, clamp = true });
            // mips: a channel a few pixels wide must not alias into beads and rungs as it narrows with distance
            l.Add(new TexDef("bolt_core", 64, PaintCore) { mips = true, clamp = true });
        }

        public static void Materials(List<MatDef> l)
        {
            l.Add(new MatDef("bolt") { kind = MatKind.Unlit, surface = MatSurface.Additive, color = Color.white, baseMap = "bolt_core", doubleSided = true, instancing = false });
            l.Add(new MatDef("crawler") { kind = MatKind.Unlit, surface = MatSurface.Additive, color = Color.white, baseMap = "ramp", doubleSided = true, instancing = false });
        }

        // across a ribbon: a white-hot core and a wide faint glow, zero at the edges. The core is wide enough
        // (FWHM about a fifth of the strip) to stay above a pixel when the strip is only a few pixels wide.
        static float Profile(float v)
        {
            float x = (v - 0.5f) * 2f;
            float core = Mathf.Exp(-(x / 0.24f) * (x / 0.24f));
            float glow = 0.4f * Mathf.Exp(-(x / 0.62f) * (x / 0.62f));
            return Mathf.Clamp01((core + glow) * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((1f - Mathf.Abs(x)) * 4f)));
        }

        // v (rows) runs across the strip; u (columns) is the brightness of that point of the channel, so one
        // mesh carries a trunk that fades out of the cloud base and branches that die out in the air
        static Color32[] PaintCore(int n)
        {
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            {
                float v = Profile((y + 0.5f) / n);
                for (int x = 0; x < n; x++)
                {
                    float b = (x + 0.5f) / n;
                    px[y * n + x] = new Color(v * b, v * b, v * b, 1f);
                }
            }
            return px;
        }

        // u < 0.5: the channel that is already lit (a tail that stays visible, a bright growing tip); u >= 0.5: not reached yet
        static Color32[] PaintRamp(int n)
        {
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n;
                    float v = u < 0.5f ? Mathf.Lerp(0.6f, 1f, Mathf.Pow(u / 0.5f, 2f)) : 0f;
                    v *= Profile((y + 0.5f) / n);                                       // hot core, soft glow across the ribbon
                    px[y * n + x] = new Color(v, v, v, 1f);
                }
            return px;
        }

        public static LightningVisuals Build(Transform worldRoot)
        {
            var go = new GameObject("LightningVisuals");
            var lv = go.AddComponent<LightningVisuals>();
            lv.root = worldRoot;
            for (int i = 0; i < Pool; i++)
            {
                var it = new Item();
                it.go = new GameObject("Channel" + i);
                it.go.transform.SetParent(worldRoot, false);
                it.mesh = new Mesh { name = "Channel" + i, indexFormat = IndexFormat.UInt32 };
                it.mesh.MarkDynamic();
                it.go.AddComponent<MeshFilter>().sharedMesh = it.mesh;
                it.mr = it.go.AddComponent<MeshRenderer>();
                it.mr.shadowCastingMode = ShadowCastingMode.Off;
                it.mr.receiveShadows = false;
                it.go.SetActive(false);
                lv.items[i] = it;
            }
            return lv;
        }

        void Update()
        {
            if (!Director.Running) return;
            float T = Director.T;
            var designed = LightningScore.Designed;
            // retire channels whose flash is over (or lies in the future after a seek)
            for (int i = 0; i < Pool; i++)
            {
                Item it = items[i];
                if (it.t0 < -900f) continue;
                if (T < it.t0 || T > it.t0 + it.f.Duration) { it.t0 = -999f; it.go.SetActive(false); }
            }
            for (int k = 0; k < designed.Count; k++)
            {
                Flash f = designed[k];
                if (f.t0 > T) break;
                if (T > f.t0 + f.Duration) continue;
                if (f.kind != FlashKind.Bolt && !(f.kind == FlashKind.Crawler && f.y > 5000f)) continue;
                Drive(f, T);
            }
            // ambient ground strikes: the same pure function of time the director and the audio read
            int k1 = Mathf.FloorToInt(T / LightningScore.Slot);
            for (int k = k1 - 6; k <= k1; k++)
                if (LightningScore.Ambient(k, out Flash f) && f.kind == FlashKind.Bolt && T >= f.t0 && T <= f.t0 + f.Duration)
                    Drive(f, T);
        }

        void Drive(in Flash f, float T)
        {
            Item it = Find(f.t0);
            if (it == null) it = Spawn(f);
            if (it == null || !it.go.activeSelf) return;      // hidden behind the vortex: its light stays, no channel
            float e = f.Envelope(T);
            if (it.crawler)
            {
                float prog = Mathf.Clamp01((T - f.t0) / Mathf.Max(0.2f, f.rise + f.hold * 0.9f));
                it.mat.SetTextureOffset(MapName, new Vector2(0.5f - 0.5f * prog * 1.08f, 0f));
                float gain = f.anchor == FlashAnchor.Truck ? Tune.F("crawler.gain", 6f) : Tune.F("crawler.gainFar", 6f);
                Color c = Color.Lerp(Palette.FlashBlue.linear, Palette.FlashWhite.linear, 0.35f) * (gain * Mathf.Sqrt(e) * f.power);
                it.mat.SetColor(idColor, c);
            }
            else
            {
                // the thin channel may flicker (restrikes); the world light it casts stays one smooth swell
                float tau = T - f.t0;
                float strikes = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(tau * 38f)) * Mathf.Exp(-tau * 3f);
                Color c = Palette.FlashWhite.linear * (Tune.F("bolt.gain", 10f) * e * strikes * f.power);
                it.mat.SetColor(idColor, c);
            }
        }

        Item Find(float t0)
        {
            for (int i = 0; i < Pool; i++) if (Mathf.Abs(items[i].t0 - t0) < 0.001f) return items[i];
            return null;
        }

        Item Spawn(in Flash f)
        {
            Item it = null;
            for (int i = 0; i < Pool; i++) if (items[i].t0 < -900f) { it = items[i]; break; }
            if (it == null) return null;
            it.t0 = f.t0; it.f = f; it.crawler = f.kind == FlashKind.Crawler;
            if (it.mat == null || it.mat.name != (it.crawler ? "crawlerI" : "boltI"))
            {
                // ambient strikes make slots swap between bolt and crawler more often: free the old copy, never leak it
                if (it.mat != null) Destroy(it.mat);
                it.mat = new Material(GameAssets.I.Mat(it.crawler ? "crawler" : "bolt")) { name = it.crawler ? "crawlerI" : "boltI" };
                if (it.crawler) it.mat.SetTextureScale(MapName, new Vector2(0.5f, 1f));
                it.mr.sharedMaterial = it.mat;
            }
            Vector3 lp = LightningScore.LocalPos(f, Director.S);
            if (f.kind == FlashKind.Bolt && Director.S.torDescend > 0.5f)
            {   // the channel is drawn over the composited storm with no depth: a bolt behind the funnel or its dust
                // would be drawn on top of them. Keep the whole channel (its top swings 350 m, about 770 m with the
                // walk) clear of the vortex plus its debris, as seen from the truck.
                Vector3 tc = Director.TornadoLocal();
                Vector2 t2 = new Vector2(tc.x, tc.z), b2 = new Vector2(lp.x, lp.z);
                float dT = Mathf.Max(t2.magnitude, 1f), dB = Mathf.Max(b2.magnitude, 1f);
                float sep = Vector2.Angle(t2, b2) * Mathf.Deg2Rad;
                float need = Mathf.Atan((Director.S.torRadius * 1.3f + 250f) / dT) + Mathf.Atan(770f / dB);
                if (dB > 0.8f * dT && sep < need)
                {
                    if (!f.designed)
                    {   // an ambient strike behind the vortex: keep the item until the flash ends, never draw it
                        it.go.SetActive(false);
                        return it;
                    }
                    // a designed strike keeps its moment: turn it about the truck just clear of the limb, on its own side
                    float sgn = Mathf.Sign(tc.x * lp.z - tc.z * lp.x);
                    lp = Quaternion.AngleAxis(-sgn * (need - sep + 0.02f) * Mathf.Rad2Deg, Vector3.up) * lp;
                }
            }
            // widths in pixels, not metres: a channel 3 km away and one 25 km away both read as a few pixels
            Camera cam = Bootstrap.Cam;
            float fov = cam != null ? cam.fieldOfView : Config.FovDefault;
            pxA = 2f * Mathf.Tan(0.5f * fov * Mathf.Deg2Rad) / Mathf.Max(1, cam != null ? cam.pixelHeight : 1080);
            camLocal = new Vector3(-lp.x, 2f, -lp.z);
            // the channel object sits at the flash position in world coordinates (the world root carries it along)
            Vector3 world = new Vector3(lp.x + (float)Director.OriginX, 0f, lp.z + (float)Director.OriginZ);
            it.go.transform.localPosition = world;
            it.go.transform.localRotation = Quaternion.identity;
            verts.Clear(); uvs.Clear(); tris.Clear();
            var rng = new Rng(f.seed * 2654435761u + 77u);
            Vector3 toCam = new Vector3(-lp.x, 0f, -lp.z);
            float dist = Mathf.Max(200f, toCam.magnitude);
            toCam /= dist;
            if (it.crawler) BuildCrawler(f, lp, toCam, dist, ref rng);
            else BuildBolt(f, lp, toCam, dist, ref rng);
            it.mesh.Clear();
            it.mesh.SetVertices(verts); it.mesh.SetUVs(0, uvs); it.mesh.SetTriangles(tris, 0, false);
            it.mesh.bounds = new Bounds(new Vector3(0f, 4000f, 0f), new Vector3(90000f, 20000f, 90000f));
            it.go.SetActive(true);
            return it;
        }

        void Ribbon(Vector3 a, Vector3 b, Vector3 side, float w, float ua, float ub)
        {
            int k = verts.Count;
            verts.Add(a - side * w); verts.Add(a + side * w); verts.Add(b + side * w); verts.Add(b - side * w);
            uvs.Add(new Vector2(ua, 0f)); uvs.Add(new Vector2(ua, 1f)); uvs.Add(new Vector2(ub, 1f)); uvs.Add(new Vector2(ub, 0f));
            tris.Add(k); tris.Add(k + 1); tris.Add(k + 2); tris.Add(k); tris.Add(k + 2); tris.Add(k + 3);
        }

        /// <summary>One continuous strip through pts with shared joints (separate quads overlap at every joint and
        /// add up into beads), turned to face the eye at each point. Half-width runs from w0 to w1; uv = (bri, across).</summary>
        void Strip(float w0, float w1)
        {
            int n = pts.Count;
            if (n < 2) return;
            int k0 = verts.Count;
            Vector3 last = Vector3.right;
            for (int i = 0; i < n; i++)
            {
                Vector3 t = (pts[Mathf.Min(i + 1, n - 1)] - pts[Mathf.Max(i - 1, 0)]).normalized;
                Vector3 s = Vector3.Cross(t, (camLocal - pts[i]).normalized);
                // a piece aimed straight at the eye has no facing side: keep the one before it
                if (s.sqrMagnitude > 0.0025f) { s.Normalize(); last = s; } else s = last;
                float hw = Mathf.Lerp(w0, w1, (float)i / (n - 1));
                verts.Add(pts[i] - s * hw); verts.Add(pts[i] + s * hw);
                uvs.Add(new Vector2(bri[i], 0f)); uvs.Add(new Vector2(bri[i], 1f));
                if (i > 0)
                {
                    int k = k0 + 2 * (i - 1);
                    tris.Add(k); tris.Add(k + 1); tris.Add(k + 3);
                    tris.Add(k); tris.Add(k + 3); tris.Add(k + 2);
                }
            }
        }

        // roughly a unit Gaussian from three uniforms (cheap, deterministic, bounded at 3 sigma)
        static float Gauss(ref Rng r) => (r.Value + r.Value + r.Value - 1.5f) * 2f;

        // ---- cloud-to-ground: a correlated random walk of the heading (real channels are tortuous at every scale,
        //      not a smooth worm), fitted to its ends, with a few branches that die out in the air ----
        void BuildBolt(in Flash f, Vector3 lp, Vector3 toCam, float dist, ref Rng rng)
        {
            Vector3 side = Vector3.Cross(Vector3.up, toCam).normalized;
            float top = Mathf.Max(f.y + 900f, 2300f);
            StoryState S = Director.S;
            if (S.wallCloud > 0.3f)
            {   // near the vortex the wall cloud hangs far below the 2800 m base: the channel must come out of the
                // wall, not out of clear air under the deck (same footprint as SdDeck's wIn, one shared copy)
                Vector3 tc = Director.TornadoLocal();
                float wIn = FunnelShape.WallInner(S);
                if (new Vector2(lp.x - tc.x, lp.z - tc.z).magnitude < wIn + 1400f) top = FunnelShape.Top(S) + 200f;
            }
            float ax = rng.Range(-350f, 350f), bx = rng.Range(-200f, 200f);
            float w = Mathf.Max(10f, dist * 3.6f * pxA);       // about 7 px across, the core about 1.4 px

            // raw walk in unit steps; the heading is measured from straight down and pulled back towards it (AR(1))
            const int n = Steps;
            float head = 0f, x = 0f, y = 0f;
            rawX[0] = 0f; rawY[0] = 0f; rawHead[0] = 0f;
            for (int i = 1; i <= n; i++)
            {
                head += -0.15f * head + 0.28f * Gauss(ref rng);
                head = Mathf.Clamp(head, -1.2f, 1.2f);
                float h = head + (rng.Chance(0.5f) ? 0.25f * Gauss(ref rng) : 0f);   // a second, finer octave of kinks
                x += Mathf.Sin(h); y -= Mathf.Cos(h);
                rawX[i] = x; rawY[i] = y; rawHead[i] = head;
            }
            // fit to the end points: the vertical scale lands the walk on the ground, the sideways deviation from
            // the chord is capped at 15 % of the length, and the last point lands exactly on b
            float len = new Vector2(bx - ax, top).magnitude;
            float sy = -top / Mathf.Min(rawY[n], -1f);
            float dev = 0f;
            for (int i = 0; i <= n; i++) dev = Mathf.Max(dev, Mathf.Abs(rawX[i] - rawX[n] * i / n));
            float sx = Mathf.Min(sy, 0.15f * len / Mathf.Max(dev, 1e-3f));
            pts.Clear(); bri.Clear();
            for (int i = 0; i <= n; i++)
            {
                float u = (float)i / n;
                trunk[i] = side * (ax + (bx - ax) * u + (rawX[i] - rawX[n] * u) * sx) + Vector3.up * (top + rawY[i] * sy);
                pts.Add(trunk[i]);
                bri.Add(FunnelShape.Ss(0f, 0.15f, u));            // the top 15 % emerges from the cloud base
            }
            Strip(w, w);

            // 4-7 dim branches from the upper part of the trunk, one per stretch so they never share a root
            int nb = rng.Range(4, 8);
            int lo = Mathf.CeilToInt(0.08f * n), hi = Mathf.FloorToInt(0.6f * n);
            for (int k = 0; k < nb; k++)
            {
                int b0 = lo + (hi - lo) * k / nb, b1 = lo + (hi - lo) * (k + 1) / nb;
                int i = rng.Range(b0, Mathf.Max(b0 + 1, b1));
                float head0 = rawHead[i] + rng.Sign * rng.Range(0.45f, 0.9f);
                int m = rng.Range(18, 40);
                if (Branch(trunk[i], head0, m, 0.4f, 0.5f * w, 0.3f * w, sx, sy, side, ref rng, out Vector3 mid, out float midHead)
                    && rng.Chance(0.35f))
                    Branch(mid, midHead + rng.Sign * rng.Range(0.45f, 0.9f), m / 2, 0.25f, 0.35f * w, 0.2f * w, sx, sy, side, ref rng, out _, out _);
            }
        }

        /// <summary>A side channel: it leans away from where it left, wanders with the trunk's scaling and dies out
        /// in the air (it never lands lit). Returns true if it got as far as its midpoint (mid, midHead), where a
        /// sub-branch may leave it.</summary>
        bool Branch(Vector3 start, float head0, int m, float peak, float w0, float w1, float sx, float sy, Vector3 side, ref Rng rng,
            out Vector3 mid, out float midHead)
        {
            pts.Clear(); bri.Clear();
            pts.Add(start); bri.Add(peak);
            mid = start; midHead = head0;
            bool hasMid = false;
            head0 = Mathf.Clamp(head0, -1.35f, 1.35f);
            float head = head0;
            Vector3 p = start;
            int half = m / 2;
            for (int j = 1; j <= m; j++)
            {
                head += (head0 - head) * 0.15f + 0.28f * Gauss(ref rng);
                head = Mathf.Clamp(head, -1.35f, 1.35f);
                p += side * (Mathf.Sin(head) * sx) - Vector3.up * (Mathf.Cos(head) * sy);
                pts.Add(p); bri.Add(peak * (1f - FunnelShape.Ss(0.65f, 1f, (float)j / m)));
                if (j == half) { mid = p; midHead = head; hasMid = true; }
                if (p.y < 20f) break;
            }
            Strip(w0, w1);
            return hasMid;
        }

        // ---- crawler: a web that spreads from a root; uv.x = normalised arrival time (root 0, tips 1) ----
        void BuildCrawler(in Flash f, Vector3 lp, Vector3 toCam, float dist, ref Rng rng)
        {
            bool overhead = f.anchor == FlashAnchor.Truck;
            Vector3 ax, ay, origin;
            if (overhead) { ax = Vector3.right; ay = Vector3.forward; origin = new Vector3(0f, f.y - 900f, 0f); }
            else { ax = Vector3.Cross(Vector3.up, toCam).normalized; ay = Vector3.up; origin = new Vector3(0f, f.y, 0f) + toCam * 2500f; }
            // the radius sizes the glow inside the cloud; a far web keeps a fixed reach so it stays inside its storm
            float reach = overhead ? Mathf.Min(f.radius, 15000f) * 1.25f : Tune.F("crawler.reachFar", 7500f);
            if (overhead) { armCeil = float.PositiveInfinity; armFloor = float.NegativeInfinity; }
            else { armCeil = origin.y + 1500f; armFloor = Mathf.Max(origin.y - 2000f, Config.CloudBase + 300f); }
            int arms = overhead ? rng.Range(6, 9) : rng.Range(4, 7);
            for (int k = 0; k < arms; k++)
            {
                // overhead: fan right, forward and forward-left, into view; far: long horizontal veins either way
                float ang = overhead ? Mathf.Lerp(-0.6f, 2.5f, (k + rng.Range(0.2f, 0.8f)) / arms)
                                     : ((k & 1) == 0 ? 0f : Mathf.PI) + rng.Range(-0.35f, 0.35f);
                Arm(origin, ang, reach * rng.Range(0.55f, 1f), 0f, reach, ax, ay, 3, ref rng);
            }
        }

        void Arm(Vector3 start, float ang, float length, float travelled, float reach, Vector3 ax, Vector3 ay, int level, ref Rng rng)
        {
            int n = Mathf.Clamp(Mathf.RoundToInt(length / (reach * 0.03f)), 4, 34);
            float seg = length / n;
            Vector3 p = start;
            for (int i = 0; i < n; i++)
            {
                ang += rng.Range(-0.32f, 0.32f);
                Vector3 dir = ax * Mathf.Cos(ang) + ay * Mathf.Sin(ang);
                Vector3 q = p + dir * seg;
                if (q.y > armCeil || q.y < armFloor)
                {   // a far web must not climb into the clear air above the storm or drop out of its base: turn back
                    ang = -ang;
                    dir = ax * Mathf.Cos(ang) + ay * Mathf.Sin(ang);
                    q = p + dir * seg;
                }
                // face the eye and keep a width of a few pixels, whatever the distance of this piece
                Vector3 toEye = camLocal - 0.5f * (p + q);
                float de = Mathf.Max(toEye.magnitude, 1f);
                Vector3 s = Vector3.Cross(dir, toEye / de);
                s = s.sqrMagnitude > 0.0025f ? s.normalized : ax * -Mathf.Sin(ang) + ay * Mathf.Cos(ang);
                float ws = Mathf.Max(30f, de * 3.0f * pxA) * (0.55f + 0.45f * level / 3f);
                float u0 = (travelled + i * seg) / (reach * 1.05f), u1 = (travelled + (i + 1) * seg) / (reach * 1.05f);
                Ribbon(p, q, s, ws, Mathf.Clamp01(u0), Mathf.Clamp01(u1));
                if (level > 0 && i > 1 && rng.Chance(0.22f))
                    Arm(q, ang + rng.Sign * rng.Range(0.5f, 1.2f), (length - (i + 1) * seg) * rng.Range(0.35f, 0.8f), travelled + (i + 1) * seg, reach, ax, ay, level - 1, ref rng);
                p = q;
            }
        }
    }
}
