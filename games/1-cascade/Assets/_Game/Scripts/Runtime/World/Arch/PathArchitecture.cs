using System.Collections.Generic;
using UnityEngine;

namespace CASCADE
{
    /// <summary>
    /// Everything the walker walks on and holds on to: quay slab and mole, the stair-road and the narrow stair, the
    /// belvedere, the rim promenade, the headland path, railings, lamp posts, wires, the lighthouse and the keeper's
    /// house. Pure load-time geometry baked through MeshBuilder; nothing here runs per frame.
    /// Files: ArchCtx (state), ArchGeo (helpers), ArchParts (props), ArchQuay, ArchStairs + ArchLandings, ArchNarrow,
    /// ArchPromenade, ArchHeadland, ArchLighthouse.
    /// </summary>
    public static class PathArchitecture
    {
        public sealed class ArchResult
        {
            public GameObject root;
            public GameObject[] solidBands;      // 8 altitude bands: slabs, steps, walls, paving (survive the wave)
            public GameObject[] fragileBands;    // 8 altitude bands: railings, lamp posts, poles, wires, benches
            public List<Vector3> lampHeads;      // centre of every lantern, in path order
            public List<float> lampChainage;     // chainage of each lamp (same order)
            public Vector3 beaconPos;            // centre of the lighthouse lantern
            public Vector3 keeperWindowPos;      // centre of the keeper's window pane
            public Vector3 keeperWindowNormal;   // outward normal of that wall (towards the approaching walker)
            public Vector3 keeperDoorPos;        // centre of the keeper's door
            public Vector3 bellPos;              // left at zero: the village builder owns the chapel
        }

        /// <summary>Builds the whole path architecture under parent. Call once, behind the black screen.</summary>
        public static ArchResult Build(Transform parent)
        {
            PathDef.Build();
            WorldGen.Build();
            var c = new ArchCtx();
            ArchQuay.Build(c);
            ArchStairs.Build(c);
            ArchNarrow.Build(c);
            ArchPromenade.Build(c);
            ArchHeadland.Build(c);
            ArchLighthouse.Build(c);
            return c.Bake(parent);
        }
    }
}
