using System.Collections.Generic;
using UnityEngine;

namespace CASCADE
{
    /// <summary>Shared state of one PathArchitecture.Build call: the mesh builders per altitude band, the materials and the lamp list.</summary>
    internal sealed class ArchCtx
    {
        public const int Bands = 8;
        public readonly MeshBuilder[] solid = new MeshBuilder[Bands];     // slabs, steps, walls, paving
        public readonly MeshBuilder[] fragile = new MeshBuilder[Bands];   // railings, lamp posts, poles, benches
        public readonly MeshBuilder[] wire = new MeshBuilder[Bands];      // overhead wires (fragile, no shadows)

        public readonly Material Stone, StoneWet, White, WhiteOld, Paving, Gravel, Rock, Iron, WoodDark, Blue;
        public readonly Material LampA, LampB, Beacon, WinKeeper, WinDark;
        // dressing: plaster trim (copings, surrounds), painted timber, volcanic masonry, planting, wood, bronze
        public readonly Material Trim, DomeBlue, ShutterGreen, ShutterRed, Pumice, Terracotta, Plant, Boug, Wood, Bronze;

        public readonly PathArchitecture.ArchResult res = new PathArchitecture.ArchResult();
        readonly List<float> lampS = new List<float>(96);
        readonly List<Vector3> lampP = new List<Vector3>(96);
        int glassCount;

        public ArchCtx()
        {
            for (int i = 0; i < Bands; i++) { solid[i] = new MeshBuilder(); fragile[i] = new MeshBuilder(); wire[i] = new MeshBuilder(); }
            GameAssets a = GameAssets.I;
            Stone = a.Mat("M_Stone"); StoneWet = a.Mat("M_StoneWet");
            White = a.Mat("M_Whitewash"); WhiteOld = a.Mat("M_WhitewashOld");
            Paving = a.Mat("M_Paving"); Gravel = a.Mat("M_Gravel"); Rock = a.Mat("M_RockWall");
            Iron = a.Mat("M_Iron"); WoodDark = a.Mat("M_WoodDark"); Blue = a.Mat("M_DoorBlue");
            LampA = a.Mat("M_LampA"); LampB = a.Mat("M_LampB");
            Beacon = a.Mat("M_BeaconGlass"); WinKeeper = a.Mat("M_WindowKeeper"); WinDark = a.Mat("M_WindowDark");
            Trim = a.Mat("M_PlasterTrim"); DomeBlue = a.Mat("M_DomeBlue"); Pumice = a.Mat("M_Pumice");
            ShutterGreen = a.Mat("M_ShutterGreen"); ShutterRed = a.Mat("M_ShutterRed");
            Terracotta = a.Mat("M_Terracotta"); Plant = a.Mat("M_Plant"); Boug = a.Mat("M_Bougainvillea");
            Wood = a.Mat("M_Wood"); Bronze = a.Mat("M_Bronze");
            VKit.LeafInit(true);          // the path architecture is built before the village: leaf cards on its planting too
            VKit.CardVerts = 0;           // start of a world build (ClothDirector logs the total)
        }

        public MeshBuilder S(float groundY) { return solid[ArchGeo.Band(groundY)]; }
        public MeshBuilder F(float groundY) { return fragile[ArchGeo.Band(groundY)]; }
        public MeshBuilder W(float groundY) { return wire[ArchGeo.Band(groundY)]; }

        /// <summary>Lamp glass alternates between the two switching groups.</summary>
        public Material NextGlass() { return (glassCount++ & 1) == 0 ? LampA : LampB; }

        public void AddLamp(float chainage, Vector3 head) { lampS.Add(chainage); lampP.Add(head); }

        /// <summary>Bakes all builders into GameObjects and fills the result (lamps sorted in path order).</summary>
        public PathArchitecture.ArchResult Bake(Transform parent)
        {
            var root = new GameObject("PathArchitecture");
            root.transform.SetParent(parent, false);
            res.root = root;
            res.solidBands = new GameObject[Bands];
            res.fragileBands = new GameObject[Bands];
            for (int i = 0; i < Bands; i++)
            {
                res.solidBands[i] = solid[i].Build(root.transform, "ArchSolid_Band" + i, true);
                res.fragileBands[i] = fragile[i].Build(root.transform, "ArchFragile_Band" + i, true);
                wire[i].Build(res.fragileBands[i].transform, "ArchWires_Band" + i, false);
            }

            int n = lampS.Count;
            var order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;
            System.Array.Sort(order, (x, y) => lampS[x] != lampS[y] ? lampS[x].CompareTo(lampS[y]) : x.CompareTo(y));
            res.lampHeads = new List<Vector3>(n);
            res.lampChainage = new List<float>(n);
            for (int i = 0; i < n; i++) { res.lampHeads.Add(lampP[order[i]]); res.lampChainage.Add(lampS[order[i]]); }
            res.bellPos = Vector3.zero;   // the village builder owns the chapel
            return res;
        }
    }
}
