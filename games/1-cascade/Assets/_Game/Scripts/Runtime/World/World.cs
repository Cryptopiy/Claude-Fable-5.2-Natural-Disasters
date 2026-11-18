using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace CASCADE
{
    /// <summary>Builds the world behind the black screen and ticks the systems that animate it.</summary>
    public class World
    {
        public Transform root;
        readonly List<Action<float>> ticks = new List<Action<float>>(32);

        public void AddTick(Action<float> t) { ticks.Add(t); }

        public IEnumerator Build(Transform parent, string iso, bool final)
        {
            var go = new GameObject("World");
            go.transform.SetParent(parent, false);
            root = go.transform;

            if (iso == "hdrp")
            {
                BuildHdrpProof();
                yield break;
            }
            if (iso == "column")
            {
                BuildColumnIso();
                var pd = PlumeDirector.Create(root, final);
                AddTick(pd.Tick);
                yield break;
            }
            // ---- the real world ----
            TerrainBuilder.Bake();
            yield return null;
            IEnumerator tb = TerrainBuilder.Build(root);
            while (tb.MoveNext()) yield return null;
            WaterBuilder.Build(root);
            var water = new WaterDirector();
            AddTick(water.Tick);
            yield return null;
            GameObject coneGo = ConeBuilder.Build(root);
            var cone = new ConeDirector();
            AddTick(cone.Tick);
            var blanket = BlanketDirector.Create(root, coneGo);
            AddTick(blanket.Tick);
            var plume = PlumeDirector.Create(root, final);
            var particles = ParticleDirector.Create(root, final);
            AddTick(particles.Tick);     // first: it publishes PlumeDirector.Bolt0/Bolt1 for this frame
            AddTick(plume.Tick);
            yield return null;
            village = VillageDirector.Create(root, this);
            AddTick(village.Tick);
            yield return null;
            FarVillages.Build(root);
            AddTick(FarVillages.Tick);
            ClothDirector.Build(root);
            AddTick(ClothDirector.Tick);
            yield return null;
            // beat-specific staging (each beat class also owns its gaze cues)
            Beat01_Harbour.Build(root); Beat02_Climb.Build(root); Beat03_Column.Build(root); Beat04_Drawdown.Build(root);
            Beat05_Wall.Build(root); Beat06_AshFire.Build(root); Beat07_Silence.Build(root);
        }

        public VillageDirector village;

        void BuildPlaceholderCone()
        {
            var a = GameAssets.I;
            var mb = new MeshBuilder();
            Vector3 c = Config.ConeCenter;
            mb.Cylinder(a.Mat("M_IsoRock"), c + Vector3.down * 5f, c + Vector3.up * 900f, 2300f, 760f, 48);
            mb.Cylinder(a.Mat("M_IsoRock"), c + Vector3.up * 900f, c + Vector3.up * 1800f, 760f, 180f, 48);
            mb.Build(root, "ConePlaceholder", false);
        }

        public void Tick(float dt)
        {
            for (int i = 0; i < ticks.Count; i++) ticks[i](dt);
        }

        /// <summary>Lit proof scene for the pipeline setup: ground, white blocks, a blue door, an emissive window.</summary>
        void BuildHdrpProof()
        {
            var a = GameAssets.I;
            Vector3 o = PathDef.Pos(0f);
            Prim(PrimitiveType.Cube, o + new Vector3(0f, -0.5f, 20f), new Vector3(60f, 1f, 80f), a.Mat("M_Stone"));
            for (int i = 0; i < 6; i++)
            {
                float z = 8f + i * 9f;
                Prim(PrimitiveType.Cube, o + new Vector3(7f + (i % 2) * 2f, 2.5f, z), new Vector3(6f, 5f + (i % 3), 6f), a.Mat("M_Whitewash"));
                Prim(PrimitiveType.Cube, o + new Vector3(3.95f + (i % 2) * 2f, 1f, z), new Vector3(0.12f, 2f, 1f), a.Mat("M_DoorBlue"));
                Prim(PrimitiveType.Cube, o + new Vector3(3.95f + (i % 2) * 2f, 3.2f, z + 1.8f), new Vector3(0.1f, 1f, 0.8f), a.Mat("M_WindowA"));
            }
            Prim(PrimitiveType.Sphere, o + new Vector3(-4f, 1f, 12f), Vector3.one * 2f, a.Mat("M_Whitewash"));
            Prim(PrimitiveType.Cylinder, o + new Vector3(-3f, 2f, 22f), new Vector3(0.12f, 2f, 0.12f), a.Mat("M_Iron"));
        }

        /// <summary>Isolation scene for hero effect 1: flat sea, a plain cone, and a railing and lamp post as the human ruler.</summary>
        void BuildColumnIso()
        {
            var a = GameAssets.I;
            var mb = new MeshBuilder();
            mb.Quad(a.Mat("M_IsoSea"), new Vector3(-60000f, 0f, -60000f), new Vector3(-60000f, 0f, 60000f), new Vector3(60000f, 0f, 60000f), new Vector3(60000f, 0f, -60000f));
            Vector3 c = Config.ConeCenter;
            mb.Cylinder(a.Mat("M_IsoRock"), c + Vector3.down * 5f, c + Vector3.up * 900f, 2300f, 760f, 48);
            mb.Cylinder(a.Mat("M_IsoRock"), c + Vector3.up * 900f, c + Vector3.up * 1800f, 760f, 180f, 48);
            // foreground ruler along the first 80 m of the promenade
            for (float s = PathDef.SStairEnd; s < PathDef.SStairEnd + 90f; s += 2f)
            {
                Vector3 p = PathDef.Pos(s), f = PathDef.FlatDir(s);
                Vector3 left = new Vector3(-f.z, 0f, f.x);
                Vector3 b = p + left * 2.7f;
                mb.Cylinder(a.Mat("M_Iron"), b, b + Vector3.up * 1.0f, 0.03f, 0.03f, 6);
                Vector3 b2 = PathDef.Pos(s + 2f) + new Vector3(-PathDef.FlatDir(s + 2f).z, 0f, PathDef.FlatDir(s + 2f).x) * 2.7f;
                mb.Cylinder(a.Mat("M_Iron"), b + Vector3.up * 1.0f, b2 + Vector3.up * 1.0f, 0.03f, 0.03f, 5);
                mb.Cylinder(a.Mat("M_Iron"), b + Vector3.up * 0.55f, b2 + Vector3.up * 0.55f, 0.022f, 0.022f, 5);
                mb.Box(a.Mat("M_Stone"), p + Vector3.down * 0.15f, new Vector3(6f, 0.3f, 2.2f), Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg);
                if (Mathf.Abs((s - PathDef.SStairEnd) % 22f - 12f) < 0.5f)
                {
                    mb.Cylinder(a.Mat("M_Iron"), b + left * 0.3f, b + left * 0.3f + Vector3.up * 4f, 0.06f, 0.04f, 6);
                    mb.Box(a.Mat("M_LampA"), b + left * 0.3f + Vector3.up * 4.15f, new Vector3(0.28f, 0.4f, 0.28f), 0f);
                }
            }
            mb.Build(root, "ColumnIso", true);
        }

        GameObject Prim(PrimitiveType type, Vector3 pos, Vector3 scale, Material m)
        {
            var g = GameObject.CreatePrimitive(type);
            UnityEngine.Object.Destroy(g.GetComponent<Collider>());
            g.transform.SetParent(root, false);
            g.transform.position = pos;
            g.transform.localScale = scale;
            g.GetComponent<MeshRenderer>().sharedMaterial = m;
            return g;
        }
    }
}
