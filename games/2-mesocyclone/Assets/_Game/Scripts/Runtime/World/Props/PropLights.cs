using UnityEngine;

namespace MESOCYCLONE
{
    /// <summary>Driver of the small lights of the land: crossroad beacons (0.5 s on / 0.5 s off, dead when the grid
    /// fails), the hazard lights of the abandoned car (1 Hz until T = 250), farm yard lights with their pools
    /// (on at dusk, dead when the grid fails) and the gas station (all levels multiply by the story's canopy value).
    /// Per frame: a handful of emissive colours on material instances and four light intensities. No allocation.</summary>
    [DefaultExecutionOrder(-100)]
    public sealed class PropLights : MonoBehaviour
    {
        // filled by the builders (any of them may stay null)
        public LightDots beaconDots, hazardDots, yardDots;
        public Material beaconMat, hazardMat, yardMat, yardPoolMat, canopyMat, trimMat, shopMat, soffitMat;
        public Light[] spots;
        public float spotCandela;

        public const float HazardEndT = 250f;
        static readonly Color ShopWarm = Palette.Hex(0xffd9a0);
        // the soffit's glow: the canopy's warm light thinned toward white, as painted metal lit by the panels reads
        static readonly Color SoffitTint = Color.Lerp(Palette.CanopyWarm, Color.white, 0.35f);
        float beaconNits, hazardNits, yardNits, poolNits, canopyNits, trimNits, shopNits, soffitNits, dotWhite;
        float lastCanopy = -1f;

        void Start()
        {
            beaconNits = Tune.F("world.beaconNits", 6000f);
            hazardNits = Tune.F("world.hazardNits", 4000f);
            yardNits = Tune.F("world.yardNits", 2600f);
            poolNits = Tune.F("world.poolNits", 5f);
            // 100, not 150: at the canopy's exposure 150 nits clipped the panels to neutral white; now they keep their hue
            canopyNits = Tune.F("world.canopyNits", 100f);
            trimNits = Tune.F("world.canopyTrimNits", 80f);
            shopNits = Tune.F("world.shopNits", 260f);
            soffitNits = Tune.F("world.soffitNits", 20f);                                     // 12-25: brighter reads as a lit room
            dotWhite = Tune.F("world.dotWhite", 14f);
        }

        /// <summary>A soft-edged pulse: 1 for onTime seconds of every period.</summary>
        public static float Pulse(float t, float period, float onTime)
        {
            const float Edge = 0.07f;
            float ph = Mathf.Repeat(t, period);
            return Mathf.SmoothStep(0f, 1f, ph / Edge) * (1f - Mathf.SmoothStep(0f, 1f, (ph - onTime + Edge) / Edge));
        }

        static void Show(LightDots d, bool visible)
        {
            if (d != null && d.Mr.enabled != visible) d.Mr.enabled = visible;
        }

        void Update()
        {
            if (!Director.Running) return;
            float T = Director.T, amb = Director.Ambient;
            int id = WorldProps.IdEmissive;

            if (beaconMat != null)
            {
                float e = T < WorldProps.PowerOutT ? Pulse(amb + 0.37f, 1f, 0.5f) : 0f;
                beaconMat.SetColor(id, WorldProps.Hdr(Palette.DashAmber, WorldProps.DotNits(beaconNits, dotWhite * 1.5f) * e));
                Show(beaconDots, e > 0.002f);
            }
            if (hazardMat != null)
            {
                float e = T < HazardEndT ? Pulse(amb + 0.11f, 1f, 0.5f) : 0f;
                hazardMat.SetColor(id, WorldProps.Hdr(Palette.DashAmber, WorldProps.DotNits(hazardNits, dotWhite) * e));
                Show(hazardDots, e > 0.002f);
            }
            if (yardMat != null)
            {
                float e = T < WorldProps.PowerOutT ? WorldProps.Dusk : 0f;
                yardMat.SetColor(id, WorldProps.Hdr(Palette.Sodium, WorldProps.DotNits(yardNits, dotWhite) * e));
                if (yardPoolMat != null) yardPoolMat.SetColor(id, WorldProps.Hdr(Palette.Sodium, poolNits * e));
                Show(yardDots, e > 0.002f);
            }

            float c = Director.S.canopy;
            if (c != lastCanopy)
            {
                lastCanopy = c;
                if (canopyMat != null) canopyMat.SetColor(id, WorldProps.Hdr(Palette.CanopyWarm, canopyNits * c));
                if (trimMat != null) trimMat.SetColor(id, WorldProps.Hdr(Palette.CanopyWarm, trimNits * c));
                if (shopMat != null) shopMat.SetColor(id, WorldProps.Hdr(ShopWarm, shopNits * c));
                if (soffitMat != null) soffitMat.SetColor(id, WorldProps.Hdr(SoffitTint, soffitNits * c));
                if (spots != null)
                    for (int i = 0; i < spots.Length; i++)
                        if (spots[i] != null) spots[i].intensity = spotCandela * c;
            }
        }
    }
}
