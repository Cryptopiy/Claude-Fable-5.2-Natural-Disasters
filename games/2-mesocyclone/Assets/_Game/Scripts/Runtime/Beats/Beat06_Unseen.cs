using UnityEngine;

namespace MESOCYCLONE
{
    /// <summary>Beat 6 — The unseen (8:00-9:30) [10]. Rain wraps the tornado; only six designed flashes show it,
    /// each a composition, closer every time. The dark between them is worse than the flashes.</summary>
    public static class Beat06_Unseen
    {
        public static readonly float[] FlashTimes = { 488f, 504f, 521f, 532f, 549f, 558f };

        public static void Build(Transform root)
        {
            Gaze.Local(480f, 10f, 2f, 72f, 2f);            // searching the dark
            Gaze.Tornado(484.5f, 450f, 71f, 1.4f, -22f);   // F1: its left limb against the lit sky, a black wall to the right
            Gaze.Local(493f, 0f, -2f, 72f, 2f);
            Gaze.Tornado(500f, 480f, 71f, 1.4f, -36f);     // F2: nearer, wider: lit rain on the left, the flank rim-lit
            Gaze.Local(509f, 4f, 0f, 72f, 2f);
            Gaze.Tornado(516f, 560f, 70f, 1.6f, 0f, 4f);   // F3 = M4: the wedge spans the glass above the road
            Gaze.Local(526f, 0f, -3f, 72f, 1.6f);          // the gust shoves the truck
            Gaze.Tornado(529f, 420f, 71f, 1.2f);           // F4: teal from below, the debris curtain
            Gaze.Local(537f, 6f, -5f, 72f, 1.8f);          // the headlights: something is coming through them
            Gaze.Tornado(546.5f, 480f, 71f, 1.2f, 20f);    // F5: after the longest dark, it is past the road: a bolt beside its limb
            Gaze.Local(553f, -18f, 0f, 72f, 2f);
            Gaze.Tornado(556f, 500f, 71f, 1.4f);           // F6: leaving
            Gaze.Local(563f, 0f, -2f, 72f, 2.2f);

            // F1: behind it (north) -> silhouette
            LightningScore.Add(FlashTimes[0], FlashKind.Sheet, FlashAnchor.Tornado, -1800f, 3200f, 900f, 1.0f, 9000f, 0.25f, 0.35f, 0.45f);
            LightningScore.Add(FlashTimes[0], FlashKind.Sheet, FlashAnchor.Tornado, 2700f, 3000f, 1200f, 0.4f, 9000f, 0.25f, 0.35f, 0.45f, true);
            // F2: behind and to the side: half lit rain
            LightningScore.Add(FlashTimes[1], FlashKind.Sheet, FlashAnchor.Tornado, -3000f, 1500f, 2000f, 1.0f, 9000f, 0.25f, 0.3f, 0.45f);
            // F3 (M4): a long crawler on the near flank, high: it lights the face of the wedge from the side, and the
            // wedge fills the glass above the road
            LightningScore.Add(FlashTimes[2], FlashKind.Crawler, FlashAnchor.Tornado, -2400f, -1000f, 1500f, 0.6f, 7000f, 0.35f, 1.1f, 0.55f);
            // F4: a power flash under the debris curtain
            LightningScore.Add(FlashTimes[3], FlashKind.Transformer, FlashAnchor.Tornado, 900f, -1100f, 14f, 1.4f, 3000f, 0.18f, 0.3f, 0.3f);
            // F5: a bolt on the far side: it has already crossed the road
            LightningScore.Add(FlashTimes[4], FlashKind.Bolt, FlashAnchor.Tornado, 600f, 3000f, 1500f, 1.3f, 8000f, 0.18f, 0.25f, 0.45f);
            // F6: far backlight
            LightningScore.Add(FlashTimes[5], FlashKind.Sheet, FlashAnchor.Tornado, -1500f, 4200f, 1000f, 0.9f, 10000f, 0.25f, 0.3f, 0.5f);
        }
    }
}
