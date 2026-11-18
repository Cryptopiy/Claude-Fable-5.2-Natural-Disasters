using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CASCADE
{
    /// <summary>
    /// Things that move in the wind: Greek flags (a pole on the quay, the two harbour cafes, the chapel tower, the rooftop
    /// churches' bell gables, the boats' mast tops, the lighthouse gallery), laundry on flat roofs near the path, and cafe
    /// string lights (static; their bulbs are window glass group A, so they light with the windows at te ~ 66 s). Built after
    /// the village; also builds the ground vegetation (ScrubGen) into its bands.
    /// The cloth is a pure function of the story clocks: a morning breeze from the north-west, the shock gust and then an
    /// in-draught towards the column, the air shoved ahead of the wave, the storm that tears it away, the calm of the silence.
    /// Each piece vanishes with its altitude band when the wave takes the village. Two dynamic meshes (flags, laundry) are
    /// rewritten every frame: about 2.5k vertices, a few hundred sines.
    /// </summary>
    public static class ClothDirector
    {
        // ---- tunables ----
        public const int MaxLaundryRoofs = 12;          // flat roofs with a washing line
        const float LaundryRange = 16f;                 // ... within this of the path edge (full-detail houses)
        const float LinePostH = 1.6f;                   // washing-line posts above the roof floor
        const float TearAt = 5f, TearSpread = 6f;       // storm-clock seconds over which the cloth is torn away (Storm passes 0.5 at ~7 s)
        public const bool KeepLighthouseFlag = true;    // the lighthouse flag rides out the storm and hangs limp in the silence
        static readonly Vector3 DayTo = new Vector3(0.7071f, 0f, -0.7071f);   // the morning breeze from the north-west blows to the south-east
        // wind strength: 0 hanging, 1 a flag straight out, above 1 the storm
        const float BreezeW = 0.3f, BreezeGust = 0.22f; // morning
        const float EruptW = 0.82f;                     // the in-draught towards the column (te 12-40 s on)
        const float ShockW = 0.9f;                      // added in the shock gust (te = Config.ShockDelay)
        const float WallW = 0.6f;                       // added by the air shoved ahead of the wave (impact - 5 s)
        const float StormW = 1.05f, CalmW = 0.05f;
        const int FlagU = 9, FlagV = 5, ClothU = 5, ClothV = 6;    // cloth grids
        const float QuayPoleX = -12.2f, QuayPoleZ = -232f, QuayPoleH = 6.5f;   // between the walking strip and the seaward gear, 12 m ahead of the start
        const float CableR = 0.012f, BulbSpacing = 0.45f;
        const float TwoPi = Mathf.PI * 2f;

        sealed class Piece
        {
            public Vector3 a, b;            // flag: hoist bottom and top; laundry: the pegged corners of the top edge (carrier space if any)
            public Vector3 wall;            // outward normal of the wall a bracket flag hangs from (zero: free)
            public float len, hgt, sag;     // flag: fly length; laundry: drop and sag of the line between the pegs
            public float phase, tear;       // wave phase; storm clock at which it is torn away
            public int band, v0, nu, nv;
            public bool survives, keep;     // stands above the run-up (its house survives); rides out the storm
            public Transform carrier;       // boats: a and b live in its space and it hides with it
            public Vector2 uv0, uv1;
        }

        static readonly List<Piece> flags = new List<Piece>(16), cloths = new List<Piece>(80);
        static readonly List<Vector3> flagV = new List<Vector3>(512), clothV = new List<Vector3>(2048), pts = new List<Vector3>(16);
        static Mesh flagMesh, clothMesh;
        static GameObject[] bands;
        static MeshBuilder[] stat, statR, glow, glowR;     // per band: poles and posts (shadows), cables and bulbs (none); intact and ruined
        static int cafes;
        // per-frame wind state
        static float tNow, wErupt, wShock, wWall, wStorm, wCalm;

        public static void Build(Transform root)
        {
            flags.Clear(); cloths.Clear(); flagMesh = null; clothMesh = null; bands = null; cafes = 0;
            GameLoop gl = GameLoop.I;
            VillageDirector vd = gl != null && gl.world != null ? gl.world.village : null;
            if (vd == null || vd.vil == null || vd.vil.intactBands == null) return;
            Village.VillageResult vil = vd.vil;
            ScrubGen.Build(vil, vd.arch);
            Material flagMat = VKit.Find("M_Flag"), clothMat = VKit.Find("M_Laundry");
            if (flagMat == null || clothMat == null) { Debug.Log("CASCADE_INFO cloth skipped: M_Flag / M_Laundry are not built yet"); return; }
            bands = vil.intactBands;
            stat = new MeshBuilder[8]; statR = new MeshBuilder[8]; glow = new MeshBuilder[8]; glowR = new MeshBuilder[8];
            for (int i = 0; i < 8; i++) { stat[i] = new MeshBuilder(); statR[i] = new MeshBuilder(); glow[i] = new MeshBuilder(); glowR[i] = new MeshBuilder(); }

            QuayFlag();
            CafeFlags();
            ChapelFlag();
            ChurchFlags();
            BoatPennants(vil);
            LighthouseFlag(vd.arch);
            int roofs = Laundry();
            StringLights();

            for (int i = 0; i < 8; i++)
            {
                if (bands[i] != null) { stat[i].Build(bands[i].transform, "Cloth_Props", true); glow[i].Build(bands[i].transform, "Cloth_Lights", false); }
                GameObject r = vil.ruinedBands != null && i < vil.ruinedBands.Length ? vil.ruinedBands[i] : null;
                if (r != null) { statR[i].Build(r.transform, "Cloth_Props", true); glowR[i].Build(r.transform, "Cloth_Lights", false); }
            }
            stat = statR = glow = glowR = null;
            flagMesh = MakeMesh(root, "Flags", flagMat, flags, flagV);
            clothMesh = MakeMesh(root, "Laundry", clothMat, cloths, clothV);
            Debug.Log("CASCADE_INFO cloth: " + flags.Count + " flags, " + cloths.Count + " cloths on " + roofs + " roofs, string lights at " + cafes + " cafes, " +
                      (flagV.Count + clothV.Count) + " dynamic vertices");
            Tick(0f);
        }

        public static void Tick(float dt)
        {
            if (flagMesh == null && clothMesh == null) return;
            tNow = Story.Time;
            float te = Story.T(Ev.Eruption), ti = Story.T(Ev.Wave) - Config.WaveCross, ts = Story.T(Ev.AshStorm);
            wErupt = MathX.Smooth(12f, 40f, te);
            wShock = Story.Bump(te - Config.ShockDelay, 3.5f);
            wWall = Story.Bump(ti + 5f, 9f);
            wStorm = MathX.Smooth(0f, 6f, ts);
            wCalm = MathX.Smooth(0f, 22f, Story.T(Ev.Silence));
            if (flagMesh != null)
            {
                for (int i = 0; i < flags.Count; i++) { Piece p = flags[i]; if (Alive(p, ts)) ShapeFlag(p); else Collapse(p, flagV); }
                flagMesh.SetVertices(flagV);
                flagMesh.RecalculateNormals();
            }
            if (clothMesh != null)
            {
                for (int i = 0; i < cloths.Count; i++) { Piece p = cloths[i]; if (Alive(p, ts)) ShapeCloth(p); else Collapse(p, clothV); }
                clothMesh.SetVertices(clothV);
                clothMesh.RecalculateNormals();
            }
        }

        // ---------------- wind and cloth shapes ----------------

        /// <summary>Wind at a piece: strength (0 calm, 1 a flag straight out, above 1 the storm) and the horizontal direction it blows to.</summary>
        static float Wind(Piece p, Vector3 at, out Vector3 to)
        {
            // one gust field over the village (neighbouring flags agree) and a little of the piece's own
            float g = Mathf.Clamp01(0.5f + 0.5f * (0.65f * MathX.Noise1(tNow * 0.31f, 11) + 0.35f * MathX.Noise1(tNow * 0.83f + p.phase * 3f, 12)));
            float w = BreezeW + BreezeGust * g;
            w = Mathf.Lerp(w, EruptW + 0.18f * g, wErupt);
            w += ShockW * wShock + WallW * wWall;
            w = Mathf.Lerp(w, StormW + 0.3f * g, wStorm);
            w = Mathf.Lerp(w, CalmW + 0.06f * g, wCalm);
            Vector3 toCone = Config.ConeCenter - at;
            toCone.y = 0f;
            toCone = toCone.sqrMagnitude > 1f ? toCone.normalized : Vector3.left;
            Vector3 d = Vector3.Slerp(DayTo, toCone, wErupt);
            Vector3 stormTo = (new Vector3(-toCone.x, 0f, -toCone.z - 0.5f)).normalized;      // the storm drives the ash off the column, over the village
            d = Vector3.Lerp(d, stormTo, wStorm);
            d = d.sqrMagnitude > 1e-4f ? d.normalized : stormTo;
            float veer = 17f * MathX.Noise1(tNow * 0.13f + p.phase, 21);                        // the direction wanders a little
            to = Quaternion.AngleAxis(veer, Vector3.up) * d;
            return w;
        }

        /// <summary>
        /// A flag on its hoist (a bottom, b top): the cloth shears down from the hoist as the wind drops (a limp flag hangs
        /// along its pole), and two travelling waves run out to the fly, the short fast one only in strong wind. Constant
        /// frequencies (amplitudes follow the wind), so the waves never jump when the wind changes.
        /// </summary>
        static void ShapeFlag(Piece p)
        {
            Vector3 a = p.a, b = p.b;
            if (p.carrier != null) { a = p.carrier.TransformPoint(a); b = p.carrier.TransformPoint(b); }
            Vector3 to;
            float w = Wind(p, b, out to);
            Vector3 hu = b - a;
            float H = hu.magnitude;
            if (H < 1e-4f) return;
            hu /= H;
            Vector3 fly = to;
            if (p.wall != Vector3.zero)
            {
                float k = Vector3.Dot(fly, p.wall);
                if (k < 0.3f) fly += p.wall * (0.3f - k);                                      // a bracket flag never flies into its wall
            }
            fly -= hu * Vector3.Dot(fly, hu);                                                   // square to the hoist (an angled staff)
            if (fly.sqrMagnitude < 1e-6f) fly = Vector3.Cross(hu, Vector3.right);
            fly.Normalize();
            float lift = Mathf.Clamp01(w * 1.15f);
            float droop = Mathf.Lerp(1.35f, 0.1f, Mathf.Sqrt(lift));                            // radians below the horizontal
            Vector3 ax = fly * Mathf.Cos(droop) - Vector3.up * Mathf.Sin(droop);
            Vector3 nrm = Vector3.Cross(hu, fly).normalized;
            float L = p.len, amp = L * (0.03f + 0.06f * Mathf.Min(w, 1.3f)), fast = Mathf.Clamp01(w - 0.3f);
            for (int j = 0; j < p.nv; j++)
            {
                float v = j / (float)(p.nv - 1);
                Vector3 root = a + hu * (v * H);
                for (int i = 0; i < p.nu; i++)
                {
                    float u = i / (float)(p.nu - 1), x = u * L;
                    float wave = amp * Mathf.Pow(u, 1.4f) * (0.7f * Mathf.Sin(TwoPi * (x / (L * 0.85f) - 1.1f * tNow) + p.phase + 0.6f * v)
                                                          + 0.45f * fast * Mathf.Sin(TwoPi * (x / (L * 0.4f) - 2.7f * tNow) + 1.7f * p.phase + 1.3f * v));
                    float sag = p.hgt * 0.3f * u * u * (1f - lift);                               // the fly end droops in a light breeze
                    flagV[p.v0 + j * p.nu + i] = root + ax * x + nrm * wave - Vector3.up * sag;
                }
            }
        }

        /// <summary>A cloth pegged along its top edge (a-b) on a washing line: it swings out with the wind across the line, more at
        /// the hem, and flutters.</summary>
        static void ShapeCloth(Piece p)
        {
            Vector3 to;
            float w = Wind(p, p.a, out to);
            Vector3 along = p.b - p.a;
            along.y = 0f;
            if (along.sqrMagnitude < 1e-8f) return;
            along.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, along);
            float wc = Mathf.Min(w, 1.2f);
            float swing = Mathf.Clamp(Vector3.Dot(to, side) * wc * 0.95f, -0.85f, 0.85f);        // radians at the hem
            float amp = 0.02f + 0.05f * wc;
            for (int j = 0; j < p.nv; j++)
            {
                float d = 1f - j / (float)(p.nv - 1);                                             // 0 at the line, 1 at the hem
                for (int i = 0; i < p.nu; i++)
                {
                    float u = i / (float)(p.nu - 1);
                    Vector3 top = Vector3.Lerp(p.a, p.b, u) - Vector3.up * (p.sag * 4f * u * (1f - u));
                    float flick = Mathf.Sin(TwoPi * 0.9f * tNow + p.phase + 2.2f * u + 1.4f * d);
                    float ang = swing * (0.45f + 0.55f * d) + amp * 2.5f * flick * d;
                    Vector3 hang = side * Mathf.Sin(ang) - Vector3.up * Mathf.Cos(ang);
                    clothV[p.v0 + j * p.nu + i] = top + hang * (d * p.hgt) + side * (amp * d * Mathf.Sin(TwoPi * 1.7f * tNow + 1.3f * p.phase + 3.1f * u + 2f * d));
                }
            }
        }

        /// <summary>Still there: not torn away by the storm, its band (or boat) not taken by the wave.</summary>
        static bool Alive(Piece p, float ts)
        {
            if (!p.keep && ts > p.tear) return false;
            if (p.carrier != null) return p.carrier.gameObject.activeInHierarchy;
            if (p.survives || bands == null || p.band < 0 || p.band >= bands.Length || bands[p.band] == null) return true;
            return bands[p.band].activeSelf;
        }

        /// <summary>A piece that is gone: all its vertices on one point (degenerate triangles draw nothing).</summary>
        static void Collapse(Piece p, List<Vector3> verts)
        {
            Vector3 a = p.carrier != null ? p.carrier.TransformPoint(p.a) : p.a;
            for (int k = 0; k < p.nu * p.nv; k++) verts[p.v0 + k] = a;
        }

        static Mesh MakeMesh(Transform root, string name, Material m, List<Piece> list, List<Vector3> verts)
        {
            verts.Clear();
            if (list.Count == 0) return null;
            var uv = new List<Vector2>(list.Count * 45);
            var tris = new List<int>(list.Count * 192);
            for (int k = 0; k < list.Count; k++)
            {
                Piece p = list[k];
                p.v0 = verts.Count;
                for (int j = 0; j < p.nv; j++)
                    for (int i = 0; i < p.nu; i++)
                    {
                        verts.Add(p.carrier != null ? p.carrier.TransformPoint(p.a) : p.a);
                        uv.Add(new Vector2(Mathf.Lerp(p.uv0.x, p.uv1.x, i / (float)(p.nu - 1)), Mathf.Lerp(p.uv0.y, p.uv1.y, j / (float)(p.nv - 1))));
                    }
                for (int j = 0; j < p.nv - 1; j++)
                    for (int i = 0; i < p.nu - 1; i++)
                    {
                        int q = p.v0 + j * p.nu + i;
                        tris.Add(q); tris.Add(q + p.nu); tris.Add(q + p.nu + 1);
                        tris.Add(q); tris.Add(q + p.nu + 1); tris.Add(q + 1);
                    }
            }
            var mesh = new Mesh { name = name, indexFormat = verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.MarkDynamic();
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = m;
            mr.shadowCastingMode = ShadowCastingMode.On;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            mr.motionVectorGenerationMode = MotionVectorGenerationMode.Camera;
            return mesh;
        }

        // ---------------- pieces ----------------

        static Piece AddFlag(Vector3 bottom, Vector3 top, float len, int band, bool survives, Vector3 wall, Transform carrier = null)
        {
            var p = new Piece { a = bottom, b = top, len = len, hgt = Vector3.Distance(bottom, top), band = band, survives = survives, wall = wall, carrier = carrier, nu = FlagU, nv = FlagV };
            p.phase = MathX.Hash(flags.Count, 71) * TwoPi;
            p.tear = TearAt + TearSpread * MathX.Hash(flags.Count, 73);
            p.uv0 = new Vector2(0.004f, 0.006f); p.uv1 = new Vector2(0.996f, 0.994f);
            flags.Add(p);
            return p;
        }

        /// <summary>Builders for the static parts of a piece: the intact band, and the ruined band too where the house survives.</summary>
        static int Copies(int band, bool survives) { return survives && band <= 6 ? 2 : 1; }
        static MeshBuilder Props(int band, int copy) { return copy == 0 ? stat[band] : statR[band]; }
        static MeshBuilder Lights(int band, int copy) { return copy == 0 ? glow[band] : glowR[band]; }

        /// <summary>An iron pole from foot to top with a bronze finial.</summary>
        static void Pole(MeshBuilder mb, Vector3 foot, Vector3 top, float r0, float r1)
        {
            mb.Cylinder(VKit.Iron, foot, top, r0, r1, 7, false);
            Vector3 d = (top - foot).normalized;
            mb.Cylinder(VKit.Bronze, top, top + d * 0.08f, r1 * 1.8f, r1 * 0.4f, 7, true);
        }

        /// <summary>A free-standing flag pole on the seaward side of the quay, in the first frames of the walk.</summary>
        static void QuayFlag()
        {
            Vector3 foot = new Vector3(QuayPoleX, Config.QuayHeight, QuayPoleZ), up = Vector3.up;
            MeshBuilder mb = Props(0, 0);
            mb.Box(VKit.Stone, foot + up * 0.15f, new Vector3(0.5f, 0.3f, 0.5f), 0f, 8);
            Pole(mb, foot + up * 0.3f, foot + up * QuayPoleH, 0.065f, 0.04f);
            AddFlag(foot + up * (QuayPoleH - 1.15f), foot + up * (QuayPoleH - 0.15f), 1.5f, 0, false, Vector3.zero);
        }

        /// <summary>A bracket flag at the end of each harbour cafe's facade, under the coping, away from the tavern sign over its door.</summary>
        static void CafeFlags()
        {
            List<HouseDef> hs = VillagePlan.Houses;
            Vector3 up = Vector3.up;
            for (int i = 0; i < hs.Count; i++)
            {
                HouseDef h = hs[i];
                if (h.zone != 0 || !h.cafe) continue;
                float sgn = h.doorS < 90f && h.doorS > 0f ? -1f : 1f;
                Vector3 bp = h.At(sgn * (h.hw - 0.3f), h.MainH - 0.35f, h.hd);
                Vector3 dir = (h.F * Mathf.Cos(35f * Mathf.Deg2Rad) + up * Mathf.Sin(35f * Mathf.Deg2Rad)).normalized;
                MeshBuilder mb = Props(h.band, 0);
                VKit.Box(mb, VKit.Iron, bp + h.F * 0.015f, h.S * 0.06f, up * 0.1f, h.F * 0.015f, 32);
                Pole(mb, bp, bp + dir * 1.9f, 0.022f, 0.018f);
                AddFlag(bp + dir * 0.95f, bp + dir * 1.8f, 1.2f, h.band, false, h.F);
            }
        }

        /// <summary>A flag pole on the top slab of the chapel's bell tower, at the corner towards the path.</summary>
        static void ChapelFlag()
        {
            Vector3 C = VillagePlan.ChapelC, A = VillagePlan.ChapelAxis, up = Vector3.up;
            if (A.sqrMagnitude < 0.5f) return;
            Vector3 B = Vector3.Cross(up, A);
            const float SlabTop = 10.12f;                    // ChapelGen.Tower: top slab at 10 m, 0.12 m thick
            Vector3 T = C - A * (ChapelGen.NaveHalfL - ChapelGen.TowerHalf) + B * (ChapelGen.NaveHalfW + 0.7f + ChapelGen.TowerHalf);
            Vector3 foot = T + up * SlabTop + (B - A).normalized * 1.25f;
            int band = VillagePlan.BandOf(C.y);
            bool survives = C.y >= VillagePlan.IntactAbove;
            for (int k = 0; k < Copies(band, survives); k++) Pole(Props(band, k), foot, foot + up * 2.6f, 0.045f, 0.03f);
            AddFlag(foot + up * 1.75f, foot + up * 2.55f, 1.2f, band, survives, Vector3.zero);
        }

        /// <summary>A short pole on one end of each rooftop church's bell gable (HouseGen.Churches), on the lower tier's coping.</summary>
        static void ChurchFlags()
        {
            const float LowerTier = 2.1f;                    // ChapelGen: lower tier 2.0 m + its 0.1 m coping, at scale 1
            Vector3 up = Vector3.up;
            for (int i = 0; i < HouseGen.Churches.Count; i++)
            {
                HouseGen.ChurchInfo ch = HouseGen.Churches[i];
                if (ch.gableK <= 0f) continue;
                Vector3 n = ch.face; n.y = 0f;
                if (n.sqrMagnitude < 1e-4f) continue;
                n.Normalize();
                Vector3 u = VKit.WallU(n);
                float k = ch.gableK, side = MathX.Hash(i, 79) < 0.5f ? -1f : 1f;
                Vector3 foot = ch.gableTop - up * ((ChapelGen.GableTop - LowerTier) * k) + u * (side * (ChapelGen.GableHalfW - 0.15f) * k);
                bool survives = ch.band >= 7;
                Pole(Props(Mathf.Clamp(ch.band, 0, 7), 0), foot, foot + up * 1.9f, 0.03f, 0.022f);
                AddFlag(foot + up * 1.25f, foot + up * 1.85f, 0.9f, Mathf.Clamp(ch.band, 0, 7), survives, Vector3.zero);
            }
        }

        /// <summary>A small flag at each mast top (fishing boats with a mast or a light mast), riding with its boat.</summary>
        static void BoatPennants(Village.VillageResult vil)
        {
            if (vil.boats == null) return;
            for (int i = 0; i < vil.boats.Length; i++)
            {
                Village.Boat bt = vil.boats[i];
                if (bt == null || bt.t == null) continue;
                Transform mast = null; bool sail = false, cabin = false;
                for (int k = 0; k < bt.t.childCount; k++)
                {
                    string nm = bt.t.GetChild(k).name;
                    if (nm.EndsWith("_M_WoodDark")) mast = bt.t.GetChild(k);
                    else if (nm.EndsWith("_M_Canvas")) sail = true;          // the furled sail: a mast boat
                    else if (nm.EndsWith("_M_WindowDark")) cabin = true;     // the wheelhouse: a light mast on the foredeck
                }
                if (mast == null || (!sail && !cabin)) continue;             // rowing boats have no mast
                MeshFilter mf = mast.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                float top = mf.sharedMesh.bounds.max.y - 0.03f;
                float mz = (sail ? 0.12f : 0.26f) * bt.length;              // BoatGen.Emit: mast at 0.12 L, light mast at 0.26 L
                if (top < 1.5f) continue;
                AddFlag(new Vector3(0f, top - 0.26f, mz), new Vector3(0f, top, mz), 0.39f, 0, false, Vector3.zero, bt.t);
            }
        }

        /// <summary>A tall pole on the lighthouse gallery deck, its flag above the cupola (clear of the lantern whichever way it flies).</summary>
        static void LighthouseFlag(PathArchitecture.ArchResult arch)
        {
            if (arch == null || arch.beaconPos == Vector3.zero) return;
            Vector3 B = arch.beaconPos, up = Vector3.up;
            float deck = B.y - 0.7f - ArchLighthouse.LanternH * 0.5f;     // ArchLighthouse.Tower: glazing 0.7 m above the deck, beacon at its middle
            Vector3 d = PathDef.Pos(PathDef.Length) - B;
            d.y = 0f;
            d = d.sqrMagnitude > 1e-4f ? d.normalized : Vector3.forward;
            d = Quaternion.AngleAxis(-40f, up) * d;                        // a little round from the walker's line
            Vector3 foot = new Vector3(B.x, deck, B.z) + d * 2.95f;
            const float H = 5.6f;                                         // the cupola's finial stands about 5.3 m above the deck
            MeshBuilder mb = Props(7, 0);
            Pole(mb, foot, foot + up * H, 0.05f, 0.032f);
            for (int k = 0; k < 2; k++)
                VKit.Box(mb, VKit.Iron, foot - d * 0.05f + up * (k == 0 ? 0.55f : 1.0f), d * 0.06f, up * 0.03f, Vector3.Cross(up, d) * 0.03f);   // clamps to the railing
            Piece p = AddFlag(foot + up * (H - 0.75f), foot + up * (H - 0.1f), 1.0f, 7, true, Vector3.zero);
            p.keep = KeepLighthouseFlag;
        }

        /// <summary>
        /// Washing lines on flat roofs near the path (no dome, cistern, roof room or roof table; room under the roof rule for the
        /// posts): two iron posts, a sagging line, 4-7 cloths in white, pale blue, rose and ochre. The line keeps clear of the
        /// chimney at the back of the roof and of the parapets however far the cloth swings.
        /// </summary>
        static int Laundry()
        {
            List<HouseDef> hs = VillagePlan.Houses;
            var cand = new List<int>(64); var key = new List<float>(64);
            for (int i = 0; i < hs.Count; i++)
            {
                HouseDef h = hs[i];
                if (h.vault || h.roofBox || h.zone == 5) continue;
                HouseGen.Extra x = HouseGen.ExtraOf(h);
                if (x.dome != 0 || x.cistern || x.roofSeats || x.room < LinePostH - VWalls.ParapetH + 0.05f) continue;
                if (VKit.PathGap(h.c.x, h.c.z) > LaundryRange) continue;
                float inS = h.hw - VWalls.ParapetT, inF = h.hd - VWalls.ParapetT;
                if (Mathf.Max(inS, inF) < 1.4f || Mathf.Min(inS, inF) < 1.2f) continue;
                if (h.hw >= h.hd && h.hd < 1.45f) continue;                    // a line along S needs depth for its swing
                cand.Add(i); key.Add(VKit.Hash(h.seed, 61));
            }
            int[] idx = cand.ToArray();
            System.Array.Sort(key.ToArray(), idx);
            int made = 0;
            Vector3 up = Vector3.up;
            for (int c = 0; c < idx.Length && made < MaxLaundryRoofs; c++)
            {
                HouseDef h = hs[idx[c]];
                var r = new System.Random(h.seed ^ 0x4C41554E);
                bool alongS = h.hw >= h.hd;
                float half = (alongS ? h.hw : h.hd) - 0.5f;
                float across = alongS ? Mathf.Clamp(0.25f * h.hd, -h.hd + 1.75f, h.hd - 1.15f) : 0f;
                float start = alongS ? -half + 0.15f : Mathf.Max(-half + 0.15f, -h.hd + 1.0f);     // the chimney stands at the back
                const float sag = 0.1f, lineY = LinePostH - 0.04f;
                bool survives = h.c.y >= VillagePlan.IntactAbove;
                Vector3 pa = RoofPoint(h, alongS, -half, across, h.MainH), pb = RoofPoint(h, alongS, half, across, h.MainH);
                for (int k = 0; k < Copies(h.band, survives); k++)
                {
                    MeshBuilder mb = Props(h.band, k);
                    mb.Cylinder(VKit.Iron, pa, pa + up * LinePostH, 0.025f, 0.022f, 6, true);
                    mb.Cylinder(VKit.Iron, pb, pb + up * LinePostH, 0.025f, 0.022f, 6, true);
                    pts.Clear();
                    for (int j = 0; j <= 10; j++) { float t = j / 10f; pts.Add(Vector3.Lerp(pa, pb, t) + up * (lineY - 4f * sag * t * (1f - t))); }
                    Lights(h.band, k).Tube(VKit.Rope, pts, CableR, 4);
                }
                float pos = start;
                for (int n = 0; n < 7; n++)
                {
                    float w = VKit.Rnd(r, 0.35f, 0.9f);
                    if (pos + w > half - 0.15f) break;
                    float drop = VKit.Rnd(r, 0.45f, 1.0f);
                    float t0 = (pos + half) / (2f * half), t1 = (pos + w + half) / (2f * half);
                    Vector3 a = RoofPoint(h, alongS, pos, across, h.MainH + lineY - 4f * sag * t0 * (1f - t0));
                    Vector3 b = RoofPoint(h, alongS, pos + w, across, h.MainH + lineY - 4f * sag * t1 * (1f - t1));
                    double v = r.NextDouble();
                    int cell = v < 0.4 ? 0 : v < 0.65 ? 1 : v < 0.8 ? 2 : 3;          // white, pale blue, rose, ochre
                    Vector2 o = new Vector2((cell & 1) * 0.5f, (cell >> 1) * 0.5f);
                    var p = new Piece
                    {
                        a = a, b = b, hgt = drop, sag = 0.02f, band = h.band, survives = survives, nu = ClothU, nv = ClothV,
                        phase = VKit.Rnd(r, 0f, TwoPi), tear = TearAt + TearSpread * (float)r.NextDouble(),
                        uv0 = o + new Vector2(0.02f, 0.02f), uv1 = o + new Vector2(0.48f, 0.48f)
                    };
                    cloths.Add(p);
                    pos += w + VKit.Rnd(r, 0.08f, 0.2f);
                }
                made++;
            }
            return made;
        }

        /// <summary>A point on a roof: along the washing line (S or F), across it, at height y above the floor.</summary>
        static Vector3 RoofPoint(HouseDef h, bool alongS, float along, float across, float y)
        {
            return alongS ? h.At(along, y, across) : h.At(across, y, along);
        }

        /// <summary>Cafe string lights: festoons on the harbour cafes' awnings and on poles over the outer tables, and under and in
        /// front of the cafe pergolas higher up.</summary>
        static void StringLights()
        {
            List<HouseDef> hs = VillagePlan.Houses;
            for (int i = 0; i < hs.Count; i++)
            {
                HouseDef h = hs[i];
                if (h.zone == 0 && h.cafe && h.awning && !h.noGroundOpenings) { HarbourCafe(h); cafes++; continue; }
                HouseGen.Extra x = HouseGen.ExtraOf(h);
                if (h.zone != 0 && x.cafe && x.pergola && PergolaCafe(h)) cafes++;
            }
        }

        static void HarbourCafe(HouseDef h)
        {
            Vector3 up = Vector3.up;
            MeshBuilder lb = Lights(h.band, 0), pb = Props(h.band, 0);
            // the awning as VWalls.Awning builds it for the harbour row (HouseGen.Dressing): on the quay slab, 2.4 m deep
            float y = Config.QuayHeight - h.c.y, len = h.hw * 2f - 0.5f;
            Vector3 aw = h.At(-h.hw + 0.25f, y, h.hd) + up * (2.72f - y) + h.F * 0.03f;
            Vector3 rail = aw + h.F * 2.4f - up * 0.5f + h.F * 0.12f;               // just in front of the valance
            int posts = Mathf.Max(2, Mathf.RoundToInt(len / 3.2f) + 1);
            for (int i = 0; i + 1 < posts; i++)
                Festoon(lb, rail + h.S * (len * i / (posts - 1)), rail + h.S * (len * (i + 1) / (posts - 1)), 0.22f);
            // poles along the outer row of tables (x = -3.7, inside the landward strip) tied back to the ends of the rail
            float f = h.c.x + 3.7f;                                                  // the harbour row faces -x: x = c.x - f
            if (f < h.hd + 3.2f) return;
            int np = h.hw * 2f > 9f ? 3 : 2;
            Vector3 prev = Vector3.zero;
            for (int i = 0; i < np; i++)
            {
                float s = Mathf.Lerp(-h.hw + 0.4f, h.hw - 0.4f, i / (float)(np - 1));
                Vector3 foot = h.At(s, y, f), top = foot + up * 2.85f;
                Pole(pb, foot, top, 0.035f, 0.03f);
                if (i > 0) Festoon(lb, prev, top, 0.3f);
                prev = top;
                if (i == 0) Festoon(lb, rail, top, 0.12f);
                if (i == np - 1) Festoon(lb, rail + h.S * len, top, 0.12f);
            }
        }

        /// <summary>Two festoons under the rafters of a cafe pergola and one in front of its beam (layout as HouseGen.Pergola).</summary>
        static bool PergolaCafe(HouseDef h)
        {
            const float BeamY = 2.42f;                       // HouseGen.BeamY: the pergola beam above the floor
            float fW = h.hd, fP = h.pF1 - 0.42f, half = h.hw - 0.35f;
            if (fP - fW < 0.95f || half < 1.3f) return false;
            bool survives = h.c.y >= VillagePlan.IntactAbove;
            for (int k = 0; k < Copies(h.band, survives); k++)
            {
                MeshBuilder lb = Lights(h.band, k);
                for (int j = 0; j < 2; j++)
                {
                    float f = Mathf.Lerp(fW, fP, j == 0 ? 0.3f : 0.75f);
                    Festoon(lb, h.At(-half - 0.05f, BeamY + 0.2f, f), h.At(half + 0.05f, BeamY + 0.2f, f), 0.2f);
                }
                int np = Mathf.Max(2, Mathf.CeilToInt(2f * half / 3.2f) + 1);
                for (int i = 0; i + 1 < np; i++)
                    Festoon(lb, h.At(Mathf.Lerp(-half, half, i / (float)(np - 1)), BeamY + 0.06f, fP + 0.14f),
                                h.At(Mathf.Lerp(-half, half, (i + 1) / (float)(np - 1)), BeamY + 0.06f, fP + 0.14f), 0.24f);
            }
            return true;
        }

        /// <summary>A cable hanging from a to b (parabola, sag at the middle) with a bulb every BulbSpacing (window glass A: it
        /// lights with the windows and goes dark with the village's power).</summary>
        static void Festoon(MeshBuilder mb, Vector3 a, Vector3 b, float sag)
        {
            const int Seg = 12;
            Vector3 up = Vector3.up;
            pts.Clear();
            for (int i = 0; i <= Seg; i++) { float t = i / (float)Seg; pts.Add(Vector3.Lerp(a, b, t) - up * (4f * sag * t * (1f - t))); }
            mb.Tube(VKit.Iron, pts, CableR, 4);
            int nb = Mathf.Max(1, Mathf.FloorToInt(Vector3.Distance(a, b) / BulbSpacing));
            for (int i = 0; i < nb; i++)
            {
                float t = (i + 0.5f) / nb;
                Vector3 p = Vector3.Lerp(a, b, t) - up * (4f * sag * t * (1f - t));
                mb.Cylinder(VKit.Iron, p, p - up * 0.03f, 0.011f, 0.011f, 4, false);
                mb.Cylinder(VKit.WinA, p - up * 0.03f, p - up * 0.09f, 0.025f, 0.02f, 6, true);
            }
        }
    }
}
