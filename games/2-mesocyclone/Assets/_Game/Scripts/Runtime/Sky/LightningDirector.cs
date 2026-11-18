using UnityEngine;

namespace MESOCYCLONE
{
    /// <summary>Plays the lightning score: every frame it gathers the flashes that are alive, hands the four strongest
    /// to the storm volume (light from within), drives the world flash light (fields, poles, cab, glass) and the
    /// ground power-flash glow. One smooth envelope per flash; the bolt and crawler meshes follow the same clock.</summary>
    [DefaultExecutionOrder(-300)]
    public sealed class LightningDirector : MonoBehaviour
    {
        public static LightningDirector I;
        public const int Slots = 4;
        public static readonly Vector4[] Pos = new Vector4[Slots];      // xyz local, w radius
        public static readonly Vector4[] Col = new Vector4[Slots];      // rgb radiance (nits), w external flag
        public static Vector4 GroundPos, GroundCol;                     // strongest power flash near the ground
        public static float Level;                                      // 0..1 overall flash level (glints, UI of the cab)
        public static Color LevelColor = Color.white;
        public static Vector3 MainDirLocal = Vector3.up;                // where the strongest flash is
        public static float SkyBehind;  // 0..1 how strongly the live flashes light the deck behind the truck (mirrors).
                                        // Unlike Level it is not cut by the vortex shadow: the sky behind still lights up.

        struct Live { public Flash f; public float e; public Vector3 p; }

        /// <summary>HLSL smoothstep: 0 at a, 1 at b (Mathf.SmoothStep eases between two values instead).</summary>
        static float Smooth(float a, float b, float x)
        {
            float u = Mathf.Clamp01((x - a) / (b - a));
            return u * u * (3f - 2f * u);
        }
        readonly Live[] live = new Live[24];
        int liveCount;

        public delegate void FlashStarted(in Flash f, Vector3 localPos);
        public static event FlashStarted OnFlashStarted;
        float lastT = -1f;

        void Awake() { I = this; }

        public static void ResetClock() { if (I != null) I.lastT = -1f; }

        void Consider(in Flash f, float T, in StoryState s)
        {
            if (f.channelOnly) return;      // only a drawn channel: its sheet carries the light
            float e = f.Envelope(T);
            bool starting = lastT >= 0f && f.t0 > lastT && f.t0 <= T;
            if (e <= 0f && !starting) return;
            Vector3 p = LightningScore.LocalPos(f, s);
            if (starting && OnFlashStarted != null) OnFlashStarted(f, p);
            if (e <= 0f || liveCount >= live.Length) return;
            live[liveCount++] = new Live { f = f, e = e * f.power, p = p };
        }

        void Update()
        {
            if (!Director.Running) return;
            float T = Director.T;
            StoryState s = Director.S;
            liveCount = 0;

            var designed = LightningScore.Designed;
            for (int i = 0; i < designed.Count; i++)
            {
                Flash f = designed[i];
                if (f.t0 > T) break;
                if (T - f.t0 > 6f) continue;
                Consider(f, T, s);
            }
            int k1 = Mathf.FloorToInt(T / LightningScore.Slot);
            for (int k = k1 - 6; k <= k1; k++)
                if (LightningScore.Ambient(k, out Flash f)) Consider(f, T, s);
            lastT = T;

            // strongest first (tiny insertion sort, no allocation)
            for (int i = 1; i < liveCount; i++)
            {
                Live x = live[i]; int j = i - 1;
                while (j >= 0 && live[j].e < x.e) { live[j + 1] = live[j]; j--; }
                live[j + 1] = x;
            }

            float nits = Tune.F("flash.nits", 90f);
            float luxScale = Tune.F("flash.lux", 85f);
            float lux = 0f, level = 0f, vsum = 0f;
            Vector3 wsum = Vector3.zero; Color csum = Color.black;
            GroundPos = new Vector4(0, -1e5f, 0, 1f); GroundCol = Vector4.zero;
            float groundBest = 0f;
            int slot = 0;
            // a patch of deck 4 km behind the truck and 3 km up: what the rear-view mirror shows
            float hr = Director.HeadingDeg * Mathf.Deg2Rad;
            Vector3 behind = new Vector3(-Mathf.Sin(hr) * 4000f, 3000f, -Mathf.Cos(hr) * 4000f);
            float sky = 0f;
            for (int i = 0; i < liveCount; i++)
            {
                Live l = live[i];
                Color c = l.f.color.linear;
                float dist = l.p.magnitude;
                if (l.f.kind == FlashKind.Transformer)
                {
                    if (l.e > groundBest)
                    {
                        groundBest = l.e;
                        GroundPos = new Vector4(l.p.x, Mathf.Max(l.p.y, 30f), l.p.z, 2600f);
                        float g = l.e * Tune.F("flash.tealNits", 55f);
                        GroundCol = new Vector4(c.r * g, c.g * g, c.b * g, 1f);
                    }
                    float el = l.e * 0.25f / (1f + (dist / 2500f) * (dist / 2500f));
                    lux += el * luxScale; wsum += l.p.normalized * el; csum += c * el; level += el;
                    vsum += el;                     // teal ground flashes keep their full fog
                    continue;
                }
                if (slot < Slots)
                {
                    Pos[slot] = new Vector4(l.p.x, l.p.y, l.p.z, l.f.radius);
                    float n = l.e * nits;
                    Col[slot] = new Vector4(c.r * n, c.g * n, c.b * n, l.f.kind == FlashKind.Bolt ? 1f : 0f);
                    slot++;
                }
                // only designed strikes are close enough to light the world harder than a sheet; an ambient bolt
                // replaces a sheet and must not add light
                float near = (l.f.kind == FlashKind.Bolt && l.f.designed) ? 1.5f : 1f;
                // the mirrors see the deck behind the truck, which no vortex can shadow
                float db = (l.p - behind).magnitude * (1f / 8000f);
                sky += l.e * near / (1f + db * db);
                float eg = l.e * near / (1f + (dist / 6000f) * (dist / 6000f));
                float veil = 1f;
                if (s.torDescend > 0.999f)
                {   // a flash behind the vortex leaves the truck in its shadow: the world stays dark, the silhouette stands
                    Vector3 tc = Director.TornadoLocal();
                    Vector2 c2 = new Vector2(tc.x, tc.z), ab = new Vector2(l.p.x, l.p.z);
                    float ab2 = Mathf.Max(ab.sqrMagnitude, 1f);
                    float ttRaw = Vector2.Dot(c2, ab) / ab2;
                    if (ttRaw > 0f && ttRaw < 1f)
                    {   // the vortex stands between the truck and the flash (a flash in front of it no longer counts)
                        float H = FunnelShape.Top(s);
                        float hc = l.p.y * ttRaw;                       // height where the line passes the vortex
                        float hn = Mathf.Clamp01(hc / H);
                        c2 += s.torRope * new Vector2(0.92f, 0.4f) * 800f * (1f - hn) * (1f - hn);   // SdFunnel's rope drag
                        float tt = Mathf.Clamp01(Vector2.Dot(c2, ab) / ab2);
                        float miss = (ab * tt - c2).magnitude;
                        // the funnel the player sees at that height (one shared profile), not its ground radius:
                        // a line crossing near the wide wedge top is in shadow too
                        float rf = FunnelShape.Radius(hc, s) * 1.05f + 60f;
                        float below = 1f - Smooth(H * 0.85f, H * 1.35f, hc);   // soft top, no hard height cut
                        eg *= Mathf.Lerp(1f, Mathf.Lerp(Tune.F("flash.behindFloor", 0.05f), 1f, Smooth(rf * 0.8f, rf * 1.4f, miss)), below);
                        // the fog between the truck and the funnel is lit only weakly, so the wedge stays a silhouette
                        // and not a smear; flashes in front keep their full white-out
                        veil = Mathf.Lerp(1f, Tune.F("flash.veilBehind", 0.35f), below * (1f - Smooth(rf * 1.4f, rf * 3.5f, miss)));
                    }
                }
                lux += eg * luxScale; wsum += l.p.normalized * eg; csum += c * eg; level += eg; vsum += eg * veil;
            }
            for (; slot < Slots; slot++) { Pos[slot] = new Vector4(0, 5000, 0, 1f); Col[slot] = Vector4.zero; }

            SkyBehind = Mathf.Clamp01(sky);
            Level = Mathf.Clamp01(level);
            if (level > 1e-4f)
            {
                LevelColor = csum / level;
                MainDirLocal = wsum.normalized;
            }
            if (Atmosphere.I != null)
                Atmosphere.I.SetFlash(wsum.sqrMagnitude > 1e-8f ? wsum.normalized * 5000f : Vector3.zero, level > 1e-4f ? csum / level : Color.white, lux,
                    level > 1e-4f ? vsum / level : 1f);    // fog veil: the level-weighted mean over the live flashes
        }
    }
}
