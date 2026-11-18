using UnityEngine;

namespace MESOCYCLONE
{
    /// <summary>Driver of the town. Per frame, without allocation: (1) tells the storm pass where the town's glow is
    /// and how much of it is left; (2) the lights die in eight BLOCKS, nearest the north-west corner first, each
    /// block at once at the moment of a designed teal power flash (expressed as a level of the story's townLights);
    /// (3) erasure: when the story's townErase passes a group's threshold its standing mesh is swapped for its ruin
    /// (hidden inside the wedge); (4) the grain elevator's lamps, floods and steady red beacon belong to no block:
    /// they stay lit until the elevator itself is erased, so the lights never die before the structure nor hang in
    /// the air after it. Everything is a pure function of the story state, so seeking works.</summary>
    [DefaultExecutionOrder(-100)]
    public sealed class TownDriver : MonoBehaviour
    {
        public GameObject[] stand, ruin;
        public float[] eraseThr;
        public Material[] poolMats;
        public Material lampMat, winMat;
        public LightDots lamps, windows;
        public int[] lampStart, winStart;
        public int elevLampStart, elevLampEnd;          // the elevator's range in lamps, after the last block
        public Material beaconMat;
        public LightDots beacon;

        // story times of the power flashes that take one block each (Beat05_Funnel), plus the rise of the flash
        static readonly float[] BlockDieT = { 446f, 449.5f, 453f, 456f, 458.5f, 461f, 463.2f, 466f };
        const float FlashRise = 0.12f;
        static readonly Color WindowWarm = Palette.Hex(0xffd9a0);
        float[] blockThr;
        bool[] blockOn, gone;
        bool elevLit = true;                            // LightDots starts every light as lit
        float lampNits, winNits, poolNits, dotWhite;

        void Start()
        {
            int nb = BlockDieT.Length;
            blockThr = new float[nb];
            blockOn = new bool[nb];
            var st = new StoryState();
            for (int b = 0; b < nb; b++)
            {
                Story.Evaluate(BlockDieT[b] + FlashRise, ref st);
                blockThr[b] = st.townLights;
                blockOn[b] = true;
            }
            gone = new bool[stand.Length];
            lampNits = Tune.F("world.lampNits", 2600f);
            winNits = Tune.F("world.windowNits", 260f);
            poolNits = Tune.F("world.poolNits", 5f);
            dotWhite = Tune.F("world.dotWhite", 14f);
        }

        void Update()
        {
            if (!Director.Running || blockThr == null) return;
            float lights = Director.S.townLights, erase = Director.S.townErase;
            SupercellPass.TownGlowLocal = Director.RoadToLocal(Beat05_Funnel.TownS, Beat05_Funnel.TownLat, 0f);
            SupercellPass.TownGlow = lights;

            // ---- lights: block by block ----
            float dusk = WorldProps.Dusk;
            int id = WorldProps.IdEmissive;
            Color pool = WorldProps.Hdr(Palette.Sodium, poolNits * dusk);
            int lit = 0;
            for (int b = 0; b < blockThr.Length; b++)
            {
                bool on = lights > blockThr[b];
                if (on) lit++;
                if (b < poolMats.Length) poolMats[b].SetColor(id, on ? pool : Color.black);
                if (on == blockOn[b]) continue;
                blockOn[b] = on;
                lamps.SetRange(lampStart[b], lampStart[b + 1], on);
                windows.SetRange(winStart[b], winStart[b + 1], on);
            }
            // the elevator's lights follow its erasure (the last erase threshold, about 462.4 s), not a power block:
            // a block would either kill them early or leave them floating for a moment after the mesh is gone
            bool elevOn = erase < eraseThr[eraseThr.Length - 1];
            if (elevOn != elevLit) { elevLit = elevOn; lamps.SetRange(elevLampStart, elevLampEnd, elevOn); }

            // lamps also stay visible while only the elevator is lit; windows only follow the blocks
            bool showL = (lit > 0 || elevOn) && dusk > 0.002f, showW = lit > 0 && dusk > 0.002f;
            if (lamps.Mr.enabled != showL) lamps.Mr.enabled = showL;
            if (windows.Mr.enabled != showW) windows.Mr.enabled = showW;
            if (showL) lampMat.SetColor(id, WorldProps.Hdr(Palette.Sodium, WorldProps.DotNits(lampNits, dotWhite) * dusk));
            if (showW) winMat.SetColor(id, WorldProps.Hdr(WindowWarm, WorldProps.DotNits(winNits, dotWhite * 0.3f) * dusk));

            // the beacon is steady, never pulsed, so it cannot fall in step with the synchronised turbine beacons
            if (beacon != null)
            {
                bool bOn = elevOn && dusk > 0.002f;
                if (beacon.Mr.enabled != bOn) beacon.Mr.enabled = bOn;
                if (bOn) beaconMat.SetColor(id, WorldProps.Hdr(Palette.TurbineRed, WorldProps.DotNits(lampNits, dotWhite) * dusk));
            }

            // ---- erasure: standing -> ruin ----
            for (int g = 0; g < stand.Length; g++)
            {
                bool flat = erase >= eraseThr[g];
                if (flat == gone[g]) continue;
                gone[g] = flat;
                if (stand[g] != null) stand[g].SetActive(!flat);
                if (ruin[g] != null) ruin[g].SetActive(flat);
            }
        }
    }
}
