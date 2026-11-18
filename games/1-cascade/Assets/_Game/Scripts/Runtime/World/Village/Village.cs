using System.Collections.Generic;
using UnityEngine;

namespace CASCADE
{
    /// <summary>
    /// The white cliff village and its harbour, intact and ruined. Everything is generated once at load from one seed:
    /// about 135 houses in six zones, the chapel, the quay dressing, field walls and 14 boats. Geometry is merged per
    /// altitude band and material ("Intact_b0".."Intact_b7", "Ruined_b0".."Ruined_b6"); the boats are separate objects.
    /// </summary>
    public static class Village
    {
        public const int Seed = 20261;
        /// <summary>Assumed mole (not built here): two fishing boats lie 6 m off its inner side. Change if the mole is elsewhere.</summary>
        public const float MoleZ = -276f, MoleInnerSide = 1f;
        public const int PerchTarget = 40;

        public sealed class Boat
        {
            public Transform t;
            public Vector3 mooring;      // rest position on the calm sea, y = 0 at the waterline
            public float length;
            public float draft;
            public float yaw;            // degrees; the bow (+z of the boat) points along this heading
        }

        public sealed class VillageResult
        {
            public GameObject root;
            public GameObject[] intactBands;     // 8 altitude bands (a house belongs to the band of its floor level)
            public GameObject[] ruinedBands;     // bands 0..6, built inactive; [7] is null
            public Boat[] boats;
            public Vector3 bellPos;              // centre of the chapel bell
            public Vector3 chapelPos;            // nave centre at floor level
            public List<Vector3> gullPerches;
            public float[] bandTopY;             // highest roof, chimney or tower of each band: the swap is hidden once the water is above it
        }

        /// <summary>Altitude band of a ground height (0: below 12 m ... 7: above 150 m, never flooded).</summary>
        public static int BandOf(float groundY) { return VillagePlan.BandOf(groundY); }

        /// <summary>
        /// Optional terrain helper: the ground the harbour row needs. WorldGen.Height rises above the quay level from
        /// x = -3 on (4.8 m at x = 0, 12 m at x = 6), which would bury the back of the quay slab and the ground floor of
        /// the harbour row. This returns h lowered to just under the quay level from the quay wall to behind the harbour
        /// houses (x up to 6.8), blending back to h by x = 11.5; the bed of the stair-road is left as it is.
        /// Use as h = Village.QuayGround(x, z, h) where the terrain mesh is sampled; it changes nothing elsewhere.
        /// </summary>
        public static float QuayGround(float x, float z, float h)
        {
            if (x < Config.QuayEdgeX + 0.5f || x > 11.5f || z < -272f || z > -12f) return h;
            float q = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-272f, -264f, z)) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-26f, -12f, z)));
            float flat = Config.QuayHeight - 0.12f;
            float target = Mathf.Lerp(flat, h, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(6.8f, 11.5f, x)));
            float low = Mathf.Min(h, Mathf.Lerp(h, target, q));
            float s, d;
            if (PathDef.Nearest(x, z, out s, out d) && PathDef.Section(s) >= 1)
            {
                // keep the carved bed of the stair-road and the bank that carries it
                float bed = PathDef.HalfWidth(s) + 0.7f;
                low = Mathf.Lerp(low, h, 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(bed, bed + 1.5f, d)));
            }
            return low;
        }

        public static VillageResult Build(Transform parent)
        {
            PathDef.Build();
            WorldGen.Build();
            VKit.Init();
            var res = new VillageResult { gullPerches = new List<Vector3>(64) };
            res.root = new GameObject("Village");
            res.root.transform.SetParent(parent, false);
            Transform root = res.root.transform;

            VillageZones.Build(Seed);
            HouseGen.Prepare(VillagePlan.Houses);        // dressing decisions (domes, pergolas, cafes, colours), shared by intact and ruin
            var intact = new MeshBuilder[8];
            var ruined = new MeshBuilder[8];
            for (int i = 0; i < 8; i++) { intact[i] = new MeshBuilder(); ruined[i] = new MeshBuilder(); }

            // ---- houses ----
            var roofPerches = new List<Vector3>(32);
            res.bandTopY = new float[8];
            List<HouseDef> hs = VillagePlan.Houses;
            for (int i = 0; i < hs.Count; i++)
            {
                HouseDef h = hs[i];
                HouseGen.Build(intact[h.band], h, roofPerches);
                res.bandTopY[h.band] = Mathf.Max(res.bandTopY[h.band], Mathf.Max(h.roofTop + 1.5f, HouseGen.TopY));   // + chimney, dome
                if (h.band > 6) continue;
                if (h.c.y >= VillagePlan.IntactAbove) HouseGen.Build(ruined[h.band], h, null);     // above the run-up: untouched
                else RuinGen.Build(ruined[h.band], h);
            }

            // ---- chapel ----
            int cb = VillagePlan.BandOf(VillagePlan.ChapelC.y);
            res.chapelPos = VillagePlan.ChapelC;
            res.bellPos = ChapelGen.Build(intact[cb], VillagePlan.ChapelC, VillagePlan.ChapelAxis, new System.Random(Seed + 7));
            res.bandTopY[cb] = Mathf.Max(res.bandTopY[cb], VillagePlan.ChapelC.y + ChapelGen.TowerTop);
            if (cb <= 6) ChapelGen.BuildRuin(ruined[cb], VillagePlan.ChapelC, VillagePlan.ChapelAxis, new System.Random(Seed + 8));

            // ---- quay, fields, wrecks ----
            var propPerches = new List<Vector3>(16);
            HarbourGen.Build(intact[0], new System.Random(Seed + 11), propPerches);
            RuinGen.Quay(ruined[0], new System.Random(Seed + 13));
            FieldGen.Build(intact, ruined, new System.Random(Seed + 17));
            Wrecks(ruined, new System.Random(Seed + 19));

            // ---- boats (separate objects) ----
            var mastPerches = new List<Vector3>(16);
            res.boats = BoatGen.BuildFleet(root, new System.Random(Seed + 23), mastPerches);

            // ---- bake ----
            int vi = 0, vr = 0;
            for (int i = 0; i < 8; i++) { vi += intact[i].VertexCount; vr += ruined[i].VertexCount; }
            Debug.Log("CASCADE village geometry: intact " + vi + " vertices, ruined " + vr + " vertices, " + hs.Count + " houses, "
                + HouseGen.Churches.Count + " rooftop churches" + (HouseGen.HaveQuayChurch ? " (quay look at " + HouseGen.QuayChurchDome
                    + (HouseGen.QuayChurchHero ? ", hero" : ", NO hero") + " dome r " + HouseGen.QuayChurchR.ToString("F2") + " m at " + HouseGen.QuayChurchDist.ToString("F0") + " m)" : " (no quay look)")
                + ", " + HouseGen.Coloured + " coloured");
            res.intactBands = new GameObject[8];
            res.ruinedBands = new GameObject[8];
            for (int i = 0; i < 8; i++)
            {
                res.intactBands[i] = intact[i].Build(root, "Intact_b" + i, true);
                if (i > 6) continue;
                res.ruinedBands[i] = ruined[i].Build(root, "Ruined_b" + i, true);
                res.ruinedBands[i].SetActive(false);
            }

            // ---- where the gulls sit: quay edge, mast tops, harbour roofs, crate stacks ----
            var prng = new System.Random(Seed + 29);
            for (float z = -252f; z < -34f; z += VKit.Rnd(prng, 15f, 21f))
                res.gullPerches.Add(new Vector3(Config.QuayEdgeX + 0.3f, Config.QuayHeight, z));
            res.gullPerches.AddRange(mastPerches);
            for (int i = 0; i < propPerches.Count && res.gullPerches.Count < PerchTarget - 10; i++) res.gullPerches.Add(propPerches[i]);
            int room = Mathf.Min(PerchTarget - res.gullPerches.Count, roofPerches.Count);
            for (int i = 0; i < room; i++) res.gullPerches.Add(roofPerches[i * roofPerches.Count / room]);     // spread along the whole row
            return res;
        }

        /// <summary>Two boats thrown onto the lower terraces (bands 1-3): they lie on the ruins of cliff houses near the harbour.</summary>
        static void Wrecks(MeshBuilder[] ruined, System.Random rng)
        {
            List<HouseDef> hs = VillagePlan.Houses;
            int done = 0, wantBand = 1;
            for (int i = 0; i < hs.Count && done < 2; i++)
            {
                HouseDef h = hs[i];
                if (h.zone != 1 || h.band < wantBand || h.band > 3 || h.c.z < -215f || h.c.z > -62f || h.hw < 3.3f) continue;
                if (VKit.PathGap(h.c.x, h.c.z) < 7f) continue;
                // lying along the terrace (the S axis), so it cannot reach over the edge towards the path
                BoatGen.Wreck(ruined[h.band], h.At(0f, 0.35f, h.apron > 1.2f ? h.hd * 0.5f : 0f), HouseGen.Yaw(h) + 90f, rng, done * 2);
                done++;
                wantBand = h.band + 1;         // the second one higher up
            }
        }
    }
}
