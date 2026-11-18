using UnityEngine;

namespace MESOCYCLONE
{
    /// <summary>The tornado's shape without its surface noise: the one C# copy of the funnel the storm shader draws.
    /// Every function here must stay equal to its twin in SupercellField.hlsl (ScTorTop, ScTorExp, ScTorSlope, ScTorCap,
    /// ScFunnelR, ScDomeR) and to Editor/VfxGen.cs TorRadius. If one copy drifts, the flash shadow, the bolts' clearance
    /// and the condensation sheets come loose from the funnel the player actually sees.
    /// Metres; y is the height above the ground, rG the funnel radius at the ground, m the morph (0 rope .. 1 wedge).</summary>
    public static class FunnelShape
    {
        /// <summary>HLSL smoothstep: 0 at a, 1 at b, eased in between. Not Mathf.SmoothStep, which eases between two values.</summary>
        public static float Ss(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        // HLSL lerp does not clamp its weight; Mathf.Lerp does. Use the shader's version so a tune.txt value outside
        // 0..1 still gives the same shape on both sides.
        static float Lerp(float a, float b, float t) => a + (b - a) * t;

        /// <summary>Height where the funnel enters the cloud. It follows the wall cloud: 1650 m while the wall is fully
        /// down, higher as it lifts, so the rope stretches thin during the rope-out instead of growing a cap.</summary>
        public static float Top(in StoryState s) => Lerp(Config.CloudBase, Config.WallCloudBase, Mathf.Clamp01(s.wallCloud)) + 150f;

        /// <summary>Radius where the funnel meets the cloud base (ScTorTop).</summary>
        public static float TopRadius(float rG, float m) => Mathf.Max(rG * Lerp(2.6f, 1.55f, m), Lerp(260f, 1150f, m));

        /// <summary>Profile exponent (ScTorExp): a rope stays an even tube that flares only near the top, the cone
        /// becomes a true V, and the wedge widens almost linearly.</summary>
        public static float Exponent(float m)
        {
            float e = Lerp(6f, 1.3f, Ss(0f, 0.22f, m));
            return Lerp(e, 1f, Ss(0.7f, 1f, m));
        }

        /// <summary>How fast the funnel keeps widening above its top, inside the cloud (ScTorSlope). No straight
        /// "tank" wall can show when the cloud base rides up.</summary>
        public static float Slope(float m) => Lerp(0.9f, 0.6f, m);

        /// <summary>Funnel radius at height y (ScFunnelR) for an explicit ground radius, morph and top height.</summary>
        public static float Radius(float y, float rG, float m, float H)
        {
            float h = Mathf.Clamp01(y / H);
            return rG + (TopRadius(rG, m) - rG) * Mathf.Pow(h, Exponent(m))
                 + m * (250f + 0.2f * rG) * Ss(0.45f, 1f, h)      // continuous trumpet, no lip
                 + Mathf.Max(y - H, 0f) * Slope(m);              // keeps widening into the cloud
        }

        /// <summary>Funnel radius at height y for the story state now.</summary>
        public static float Radius(float y, in StoryState s) => Radius(y, s.torRadius, s.torMorph, Top(s));

        /// <summary>Radius of the funnel's cap, 350 m inside the cloud where the shader closes it (ScTorCap).</summary>
        public static float CapRadius(float rG, float m) => TopRadius(rG, m) + m * (250f + 0.2f * rG) + 350f * Slope(m);

        public static float CapRadius(in StoryState s) => CapRadius(s.torRadius, s.torMorph);

        /// <summary>Inner radius of the fully lowered wall cloud around the vortex (SdDeck's wIn). While a funnel is on
        /// the ground the wall reaches past its cap, so the flare never pokes out from under the cloud.</summary>
        public static float WallInner(in StoryState s)
        {
            // the shader multiplies by step(0.001, torDescend), which is 1 from 0.001 up
            return s.torDescend >= 0.001f ? Mathf.Max(1100f, 1.1f * CapRadius(s) + 150f) : 1100f;
        }

        /// <summary>Footprint radius of the debris dome (ScDomeR). It keeps the narrow dome until the elevator is
        /// erased (rG about 1230 m at 462 s) and spreads to the wide dome for the wedge (rG 1500).
        /// scale is StoryState.domeScale (0..1): it narrows the dome to 0.75x at 0, as the shader does with _ScTor3.y.
        /// Pass the story value itself, not an already-lerped factor.</summary>
        public static float DomeRadius(float rG, float scale = 1f)
        {
            float k = Ss(1250f, 1500f, rG);
            return (rG * Lerp(1.1f, 1.5f, k) + Lerp(160f, 300f, k)) * Lerp(0.75f, 1f, scale);
        }

        public static float DomeRadius(in StoryState s) => DomeRadius(s.torRadius, s.domeScale);
    }
}
