using System.Collections.Generic;
using UnityEngine;

namespace MESOCYCLONE
{
    public enum FlashKind { Sheet, Bolt, Crawler, Transformer }
    public enum FlashAnchor { World, Road, Truck, Storm, Tornado }

    /// <summary>One designed or ambient flash. Position meaning depends on the anchor:
    /// World (x, z metres), Road (chainage, lateral), Truck (right, forward of the truck at flash time),
    /// Storm (east, north of the updraft centre), Tornado (east, north of the vortex).</summary>
    public struct Flash
    {
        public float t0;
        public FlashKind kind;
        public FlashAnchor anchor;
        public float a, b, y;            // anchored position and height
        public float power;              // 1 = a strong full-field flash
        public float rise, hold, decay;  // envelope, seconds
        public float radius;             // reach of the glow inside the cloud, metres
        public Color color;
        public bool silent;              // no thunder (too far, or designed silence)
        public bool designed;
        public bool channelOnly;         // only the visible channel: it rides on a sheet with the same envelope, so it
                                         // adds no light, no storm slot, no sferic and no thunder of its own
        public uint seed;

        public float Duration => rise + hold + decay * 5f;

        /// <summary>One smooth swell with a soft tail: never a flicker (photosensitivity rule).</summary>
        public float Envelope(float T)
        {
            float tau = T - t0;
            if (tau <= 0f || tau >= Duration) return 0f;
            if (tau < rise) { float u = tau / rise; return u * u * (3f - 2f * u); }
            if (tau < rise + hold) return 1f;
            float d = (tau - rise - hold) / decay;
            float tail = Mathf.Clamp01(d - 4f);                       // the last fifth of the decay eases to zero
            return Mathf.Exp(-d) * (1f - tail * tail * (3f - 2f * tail));
        }
    }

    /// <summary>The score of the lightning: designed flashes registered by the beats, plus a deterministic ambient
    /// generator (sheet lightning inside the storm, now and then a ground strike) that is a pure function of story time.
    /// Flash rule (law): at most 3 flashes in any second, full-field flashes at least 0.4 s apart, ramps of at
    /// least 150 ms for designed flashes and 90 ms for dim distant ones. Guaranteed by construction:
    /// ambient flashes live on a 0.6 s slot grid (at most 2 per second) and yield to designed flashes.</summary>
    public static class LightningScore
    {
        public const float Slot = 0.6f;
        public const float MinGap = 0.45f;
        public const float AmbientKeepOut = 0.75f;
        static readonly List<Flash> designed = new List<Flash>(256);
        static bool sorted;

        public static void Clear() { designed.Clear(); sorted = false; }
        public static IReadOnlyList<Flash> Designed { get { Sort(); return designed; } }

        public static void Add(float t, FlashKind kind, FlashAnchor anchor, float a, float b, float y, float power,
            float radius = 9000f, float rise = 0.16f, float hold = 0.08f, float decay = 0.28f, bool silent = false, Color? color = null)
        {
            var f = new Flash
            {
                t0 = t, kind = kind, anchor = anchor, a = a, b = b, y = y, power = power,
                rise = Mathf.Max(rise, 0.15f), hold = hold, decay = decay, radius = radius, silent = silent, designed = true,
                color = color ?? (kind == FlashKind.Transformer ? Palette.TransformerTeal : (kind == FlashKind.Bolt ? Palette.FlashWhite : Palette.FlashBlue)),
                seed = (uint)(t * 977f) + 13u
            };
            designed.Add(f); sorted = false;
        }

        /// <summary>A cloud-to-ground channel drawn inside a designed sheet: give it the sheet's t0 and envelope, so the
        /// eye and the flash rule see one swell. It casts no light and makes no sound (the sheet does both).</summary>
        public static void AddChannel(float t, FlashAnchor anchor, float a, float b, float y, float rise, float hold, float decay)
        {
            var f = new Flash
            {
                t0 = t, kind = FlashKind.Bolt, anchor = anchor, a = a, b = b, y = y, power = 1f,
                rise = Mathf.Max(rise, 0.15f), hold = hold, decay = decay, radius = 1f, silent = true, designed = true,
                channelOnly = true, color = Palette.FlashWhite,
                seed = (uint)(t * 977f) + 29u      // its own salt: never the shape of a bolt that shares the instant
            };
            designed.Add(f); sorted = false;
        }

        static void Sort()
        {
            if (sorted) return;
            designed.Sort((x, y) => x.t0.CompareTo(y.t0));
            for (int i = 1; i < designed.Count; i++)
            {
                // two lobes of one flash share its instant and envelope: to the eye that is a single flash
                float gap = designed[i].t0 - designed[i - 1].t0;
                if (gap > 0.01f && gap < MinGap)
                    Debug.LogError("[LightningScore] designed flashes closer than " + MinGap + " s at T=" + designed[i].t0);
            }
            sorted = true;
        }

        static uint Hash(uint x)
        {
            x ^= x >> 16; x *= 0x7feb352du; x ^= x >> 15; x *= 0x846ca68bu; x ^= x >> 16;
            return x;
        }
        static float H01(uint x) => (Hash(x) & 0xFFFFFF) / 16777216f;

        static bool NearDesigned(float t)
        {
            Sort();
            // lists are short; a linear scan around the time is cheap and allocation free
            for (int i = 0; i < designed.Count; i++)
            {
                float d = designed[i].t0 - t;
                if (d > AmbientKeepOut + 3f) break;
                if (d > -AmbientKeepOut - designed[i].rise - designed[i].hold && d < AmbientKeepOut) return true;
            }
            return false;
        }

        /// <summary>The ambient flash of time slot k, if any. Deterministic.</summary>
        public static bool Ambient(int k, out Flash f)
        {
            f = default;
            if (k < 0) return false;
            float ts = k * Slot;
            uint h = (uint)k * 2654435761u + 0x51ed27u;
            float t0 = ts + H01(h) * 0.1f;
            var st = new StoryState();
            // only the activity and geometry curves are needed; Evaluate is cheap
            Story.Evaluate(t0, ref st);
            float rate = st.sheetActivity * 0.42f;                 // flashes per second at full activity
            // a tornadic storm at night flickers almost without pause: the funnel and the unseen get twice the rate,
            // still on the 0.6 s slot grid (never more than two ambient flashes in a second)
            bool vortex = st.torDescend > 0.5f && (st.beat == 4 || st.beat == 5);
            if (vortex) rate = Mathf.Max(rate * 2.2f, Tune.F("flash.vortexRate", 0.6f));
            if (H01(h + 1u) > rate * Slot) return false;
            if (NearDesigned(t0)) return false;

            float r1 = H01(h + 2u), r2 = H01(h + 3u), r3 = H01(h + 4u), r4 = H01(h + 5u);
            f.t0 = t0; f.kind = FlashKind.Sheet; f.seed = h; f.designed = false; f.color = Palette.FlashBlue;
            f.rise = 0.1f + 0.12f * r3; f.hold = 0.03f + 0.1f * r4; f.decay = 0.16f + 0.3f * r1;
            int beat = st.beat;
            // some of them reach the ground: a few visible channels a minute in the wall, the funnel and the departing
            // storm. Never in the hail (fog too thick), the unseen (six designed flashes) or the quiet beats.
            // Same slot, same envelope, same count: only the kind and the height change.
            float pBolt = (beat == 2 && t0 < 222f) ? 0.15f : beat == 4 ? 0.25f : beat >= 6 ? 0.2f : 0f;
            bool bolt = H01(h + 7u) < pBolt;
            if (vortex && H01(h + 6u) < 0.7f)
            {   // deep in the rain behind the vortex (north of it, away from the road): the funnel stands black against it
                f.rise = Mathf.Max(f.rise, 0.15f);
                f.anchor = FlashAnchor.Tornado; f.a = (r1 - 0.5f) * 8000f; f.b = 2200f + 4200f * r2;
                f.y = Mathf.Lerp(700f, 3200f, r3); f.radius = 8000f + 4000f * r4;
                f.power = (beat == 5 ? 0.35f : 0.45f) + 0.45f * r4; f.silent = r2 > 0.55f;
                if (bolt) { f.kind = FlashKind.Bolt; f.color = Palette.FlashWhite; f.y = Mathf.Lerp(600f, 1100f, r3); }
                return true;
            }
            if (beat <= 1)
            {   // far inside the anvil and the tower: silent (too far to hear)
                f.anchor = FlashAnchor.Storm; f.a = (r1 - 0.5f) * 30000f; f.b = -r2 * Mathf.Min(st.anvilReach * 0.55f, 40000f);
                f.y = Mathf.Lerp(7000f, 11500f, r3); f.radius = 9000f + 9000f * r4; f.power = 0.35f + 0.4f * r2; f.silent = true;
            }
            else if (beat == 2)
            {   // inside the shelf wall, anywhere along its length
                f.anchor = FlashAnchor.Road; f.a = st.shelfZ + 1500f + 5000f * r2; f.b = (r1 - 0.5f) * 44000f;
                f.y = Mathf.Lerp(1500f, 3500f, r3); f.radius = 5000f + 5000f * r4; f.power = 0.4f + 0.5f * r4;
            }
            else if (beat <= 5)
            {   // around the truck inside the deck: diffuse glow, the roar of thunder never far
                float ang = r1 * Mathf.PI * 2f, dist = 4000f + 14000f * r2;
                f.anchor = FlashAnchor.Truck; f.a = Mathf.Sin(ang) * dist; f.b = Mathf.Cos(ang) * dist + 3000f;
                f.y = Mathf.Lerp(3000f, 6500f, r3); f.radius = 7000f + 7000f * r4; f.power = (beat == 5 ? 0.16f : 0.45f) + 0.4f * r4;
            }
            else
            {   // the departing storm, lit from within
                f.anchor = FlashAnchor.Storm; f.a = (r1 - 0.5f) * 26000f; f.b = (r2 - 0.5f) * 20000f;
                f.y = Mathf.Lerp(4500f, 12000f, r3); f.radius = 7000f + 8000f * r4; f.power = 0.45f + 0.5f * r4; f.silent = r2 > 0.3f;
            }
            // a ground strike lights the cloud low, just under the 2800 m base its channel comes out of
            if (bolt) { f.kind = FlashKind.Bolt; f.color = Palette.FlashWhite; f.y = Mathf.Lerp(1500f, 2200f, r3); }
            return true;
        }

        /// <summary>Position of a flash in the moving local frame (truck at the origin).</summary>
        public static Vector3 LocalPos(in Flash f, in StoryState now)
        {
            switch (f.anchor)
            {
                case FlashAnchor.World: return Director.ToLocal(f.a, f.y, f.b);
                case FlashAnchor.Road: return Director.RoadToLocal(f.a, f.b, f.y);
                case FlashAnchor.Storm: return Director.ToLocal(now.stormX + f.a, f.y, now.stormZ + f.b);
                case FlashAnchor.Tornado:
                    {
                        Vector3 p = Director.TornadoLocal(f.y);
                        return new Vector3(p.x + f.a, p.y, p.z + f.b);
                    }
                default:
                    {   // truck-anchored: fixed to where the truck would be at the flash time (cinematic clock)
                        double s = System.Math.Min(f.t0 * Config.SpeedBrisk, Director.GoalS);
                        PathDef.RoadToWorld(s, Config.LaneOffset, out double x, out double z);
                        return Director.ToLocal(x + f.a, f.y, z + f.b);
                    }
            }
        }
    }
}
