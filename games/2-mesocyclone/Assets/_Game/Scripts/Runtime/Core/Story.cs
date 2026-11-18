using UnityEngine;

namespace MESOCYCLONE
{
    /// <summary>The score: every continuous quantity of the game as a keyframed function of story time T.
    /// Keys are (seconds, value). Beat boundaries: 0, 90, 180, 270, 360, 480, 570, 630.</summary>
    public static class Story
    {
        // ---------------- sky and light ----------------
        static readonly Curve sunElev = new Curve("sunElev", 0, 5.5f, 45, 3.3f, 75, 1.7f, 90, 0.8f, 110, -0.7f, 180, -4.5f, 270, -9f, 360, -14f, 720, -26f);
        static readonly Curve sunAzim = Curve.Linear("sunAzim", 0, 232f, 180, 236f, 720, 245f);
        static readonly Curve sunLux = new Curve("sunLux", 0, 100000f, 108, 100000f, 124, 0f);
        static readonly Curve exposure = new Curve("exposure", 0, 10.4f, 45, 9.4f, 75, 8.4f, 90, 7.8f, 120, 6.6f, 150, 4.8f, 180, 3.7f,
            240, 3.0f, 270, 2.7f, 360, 2.6f, 600, 2.6f, 630, 2.6f, 650, 3.0f, 663, 3.8f);
        static readonly Curve sunsetFill = new Curve("sunsetFill", 0, 1f, 60, 0.9f, 100, 0.6f, 140, 0.25f, 178, 0f);
        static readonly Curve nightFill = new Curve("nightFill", 0, 0f, 100, 0f, 150, 1f, 240, 0.7f, 300, 0.3f, 360, 0.16f, 570, 0.12f, 620, 0.3f, 720, 0.3f);
        static readonly Curve greenTint = new Curve("greenTint", 0, 0f, 115, 0f, 150, 0.8f, 270, 1f, 345, 1f, 366, 0.4f, 400, 0f);
        // the grade warms under the canopy so the sodium light reads warm-white, while the storm beyond stays blue
        static readonly Curve gradeCold = new Curve("gradeCold", 0, 0f, 80, 0.1f, 150, 0.75f, 240, 1f, 600, 1f, 640, 0.75f, 652, 0.55f, 663, 0.3f);
        static readonly Curve starVis = new Curve("starVis", 0, 0f, 590, 0f, 614, 1f);
        // nits of the slate ambient on every cloud face at night: it flooded the tear with pale blue, so it falls
        // once the rope-out is over and the departing storm is lit by its own core and pulses instead
        static readonly Curve nightAmb = new Curve("nightAmb", 0, 16f, 594, 16f, 612, 3f);
        // the breathing light deep in the departing storm (the tear and the canopy beat)
        static readonly Curve stormCore = new Curve("stormCore", 0, 0f, 596, 0f, 610, 1f, 640, 0.85f, 720, 0.6f);
        // the rain haze is washed out of the tear's air, so the stars sit on black sky, not on lit mist
        static readonly Curve clearAir = new Curve("clearAir", 0, 0f, 598, 0f, 612, 1f);

        // ---------------- storm body ----------------
        // the storm slides east only from 520 to 580, hidden under the full deck and the rain (the 520 key must equal
        // the 480 key); it then stands clear of the A-pillar for the tear and behind the canopy for the ending
        static readonly Curve stormX = new Curve("stormX", 0, -9000f, 270, -9500f, 360, -8000f, 480, -8000f, 520, -8000f, 580, -1800f, 630, -1400f, 720, -2200f);
        static readonly Curve stormZ = Curve.Linear("stormZ", 0, 80000f, 90, 60000f, 180, 44000f, 270, 33000f, 360, 22500f, 480, 22500f,
            570, 30000f, 630, 41500f, 720, 48000f);
        static readonly Curve anvilReach = new Curve("anvilReach", 0, 50000f, 60, 62000f, 90, 76000f, 120, 105000f, 570, 80000f, 590, 42000f, 604, 20000f,
            614, 7000f, 630, 5000f, 720, 5000f);
        static readonly Curve anvilHalfWidth = new Curve("anvilHalfWidth", 0, 44000f, 60, 56000f, 95, 78000f, 125, 98000f, 570, 90000f, 630, 60000f);
        static readonly Curve anvilBack = new Curve("anvilBack", 0, 22000f, 540, 22000f, 610, 70000f, 720, 80000f);
        static readonly Curve mammatus = new Curve("mammatus", 0, 0f, 62, 0.08f, 86, 0.6f, 110, 1f, 200, 1f, 270, 0.5f, 570, 0.4f, 630, 0.7f);
        static readonly Curve deckCover = new Curve("deckCover", 0, 0f, 150, 0f, 215, 0.6f, 250, 1f, 588, 1f, 612, 0f);
        static readonly Curve shelfAmount = new Curve("shelfAmount", 0, 0f, 95, 0f, 150, 1f, 280, 1f, 330, 0f);
        static readonly Curve sheetActivity = new Curve("sheetActivity", 0, 0f, 105, 0f, 125, 0.5f, 180, 0.8f, 270, 1f, 345, 1f, 360, 0.6f, 480, 0.35f,
            570, 0.5f, 610, 0.9f, 663, 0.7f);
        static readonly Curve coreGlow = new Curve("coreGlow", 0, 0.15f, 120, 0.5f, 200, 1f, 345, 1f, 370, 0.5f, 570, 0.3f, 630, 0.5f);
        // the light behind the vortex: full while it grows, dimmer in the unseen (the flashes still own that beat)
        // in the unseen the wedge is close and hides most of it: the light it leaves at its limbs has to be stronger
        static readonly Curve coreBack = new Curve("coreBack", 0, 0f, 352, 0f, 376, 1f, 476, 1f, 490, 1.4f, 566, 1.4f, 584, 0.8f, 600, 0.35f, 612, 0f);
        // the forward-flank core beside the rain-free base: plain while the storm is on the horizon, gone once the wall's sheet covers it
        static readonly Curve precipCore = new Curve("precipCore", 0, 1f, 150, 1f, 210, 0f);
        // the rain sheet behind the shelf: a light haze before the shelf exists, the full sheet once the wall stands
        static readonly Curve rainSheet = new Curve("rainSheet", 0, 0.3f, 95, 0.3f, 150, 1f);

        // ---------------- weather at the truck ----------------
        static readonly Curve rain = new Curve("rain", 0, 0f, 241, 0f, 250, 0.8f, 268, 0.9f, 345, 0.85f, 352, 0.35f, 366, 0.2f, 470, 0.25f, 486, 0.8f,
            560, 0.85f, 580, 0.35f, 604, 0.06f, 640, 0.08f, 663, 0.1f);
        static readonly Curve hail = new Curve("hail", 0, 0f, 268, 0f, 273, 0.22f, 290, 0.5f, 305, 0.8f, 320, 1f, 344.6f, 1f, 345, 0f);
        static readonly Curve hailSize = new Curve("hailSize", 268, 0.008f, 288, 0.043f, 304, 0.07f, 320, 0.105f, 345, 0.11f);
        // stones lying on the ground: they pile up through the hail and melt away before the heavy rain returns at 470
        static readonly Curve hailCover = new Curve("hailCover", 0, 0f, 285, 0f, 340, 1f, 400, 0.6f, 440, 0f);
        static readonly Curve wind = new Curve("wind", 0, 3f, 90, 1.2f, 110, 0.3f, 225, 0.3f, 237, 6f, 241, 36f, 262, 24f, 345, 19f, 366, 12f, 440, 18f,
            478, 26f, 500, 32f, 548, 38f, 572, 22f, 600, 8f, 640, 2.5f);
        static readonly Curve windDir = new Curve("windDir", 0, 20f, 230, 20f, 240, 178f, 360, 170f, 400, 300f, 480, 320f, 548, 250f, 600, 200f);
        static readonly Curve gust = new Curve("gust", 0, 0f, 236, 0f, 241, 1f, 256, 0.6f, 345, 0.45f, 366, 0.2f, 470, 0.35f, 520, 0.8f, 546, 1f,
            565, 0.5f, 600, 0.1f, 640, 0f);
        static readonly Curve dust = new Curve("dust", 0, 0f, 204, 0f, 220, 0.5f, 231, 1f, 240.5f, 1f, 247, 0.8f, 262, 0.15f, 280, 0f);
        static readonly Curve fogMfp = new Curve("fogMfp", 0, 16000f, 120, 14000f, 200, 9000f, 230, 6000f, 234, 2600f, 238, 900f, 242, 260f, 252, 700f, 268, 800f, 345, 750f,
            362, 2600f, 470, 3000f, 486, 3400f, 560, 3400f, 585, 3600f, 612, 18000f, 636, 18000f, 663, 7000f);   // the tear is clearer than the opening
        static readonly Curve wetness = new Curve("wetness", 0, 0f, 242, 0f, 256, 1f, 720, 1f);
        static readonly Curve wiper = Curve.Stepped("wiper", 0, 0f, 244, 2f, 250, 3f, 352, 2f, 372, 1f, 484, 3f, 572, 2f, 596, 1f, 622, 0f);
        static readonly Curve headlights = new Curve("headlights", 0, 0f, 96, 0f, 98, 1f);

        // ---------------- tornado (road coordinates: chainage, lateral) ----------------
        static readonly Curve torS = new Curve("torS", 360, 16300f, 398, 16200f, 420, 16000f, 440, 15800f, 455, 15600f, 465, 15450f, 480, 15250f,
            488, 14400f, 504, 14050f, 521, 14700f, 532, 14700f, 549, 14800f, 558, 15200f, 570, 15600f, 585, 16200f, 604, 16900f);
        // far funnel kept 13-15 degrees clear of the A-pillar (360-451); from 455 on the town choreography is unchanged
        // (the elevator stays outside the debris dome until it is erased at about 462)
        static readonly Curve torLat = new Curve("torLat", 360, -1900f, 398, -1700f, 420, -1450f, 440, -1350f, 455, -1150f, 465, -700f, 480, 500f,
            488, 1500f, 504, 900f, 521, 150f, 532, -700f, 549, -2200f, 558, -3400f, 570, -4300f, 585, -5000f, 604, -5600f);
        static readonly Curve torRadius = new Curve("torRadius", 372, 20f, 398, 40f, 420, 220f, 440, 600f, 455, 1000f, 465, 1300f, 478, 1500f, 488, 1500f,
            504, 1450f, 521, 1350f, 532, 1250f, 549, 1100f, 558, 800f, 570, 500f, 585, 150f, 598, 35f, 604, 12f);
        static readonly Curve torDescend = new Curve("torDescend", 372, 0f, 398, 1f, 596, 1f, 606, 0f);
        static readonly Curve torMorph = new Curve("torMorph", 398, 0f, 420, 0.35f, 440, 0.85f, 450, 1f, 549, 1f, 566, 0.5f, 580, 0.1f, 594, 0f);
        static readonly Curve torRope = new Curve("torRope", 0, 0f, 372, 0.6f, 398, 0.5f, 420, 0f, 566, 0f, 585, 0.7f, 600, 1f);
        static readonly Curve torWrap = new Curve("torWrap", 0, 0f, 472, 0f, 486, 0.6f, 560, 0.6f, 578, 0.2f, 590, 0f);
        static readonly Curve torDebris = new Curve("torDebris", 398, 0f, 420, 0.3f, 455, 1f, 556, 1f, 585, 0.3f, 600, 0f);
        // size of the debris dome. In the unseen the vortex passes about 2 km away, and a full dome would fill the
        // whole glass from 490 to 546 s; held low (and a little narrower) there, the black funnel shows above it
        static readonly Curve domeScale = new Curve("domeScale", 0, 1f, 488, 1f, 494, 0.45f, 544, 0.45f, 552, 1f);
        static readonly Curve wallCloud = new Curve("wallCloud", 0, 0f, 352, 0f, 372, 0.8f, 398, 1f, 570, 1f, 590, 0.6f, 608, 0f);
        static readonly Curve torPower = new Curve("torPower", 0, 0f, 385, 0f, 420, 0.25f, 455, 0.7f, 478, 0.9f, 521, 1f, 548, 1f, 572, 0.55f, 596, 0.15f, 606, 0f);

        // ---------------- town ----------------
        static readonly Curve townLights = Curve.Linear("townLights", 0, 1f, 441, 1f, 470, 0f);
        static readonly Curve townErase = new Curve("townErase", 0, 0f, 458, 0f, 476, 1f);
        static readonly Curve canopy = new Curve("canopy", 0, 1f, 720, 1f);

        // ---------------- sound scores ----------------
        static readonly Curve insectA = Curve.Linear("insectA", 0, 1f, 124.6f, 1f, 125.1f, 0f, 651, 0f, 660, 0.55f, 720, 0.8f);
        static readonly Curve insectB = Curve.Linear("insectB", 0, 0.9f, 142.7f, 0.9f, 143.2f, 0f, 658, 0f, 668, 0.5f, 720, 0.7f);
        static readonly Curve insectC = Curve.Linear("insectC", 0, 0.7f, 164.8f, 0.7f, 165f, 0f, 640, 0f, 641, 0.5f, 720, 0.6f);
        static readonly Curve radio = new Curve("radio", 0, 0.5f, 180, 0.5f, 240, 0.7f, 345, 0.7f, 360, 0.55f, 455, 0.6f, 462, 0.35f, 469, 0f);
        static readonly Curve pressure = new Curve("pressure", 0, 0f, 130, 0f, 170, 1f, 236, 1f, 242, 0f);
        static readonly Curve siren = new Curve("siren", 0, 0f, 412, 0f, 422, 0.5f, 455, 1f, 466.5f, 1f, 467.2f, 0f);
        static readonly Curve engine = Curve.Linear("engine", 0, 1f, 665.5f, 1f, 666.4f, 0f);
        static readonly Curve ringing = Curve.Linear("ringing", 0, 0f, 345, 0f, 345.25f, 1f, 349, 0.6f, 358, 0f);
        static readonly Curve drips = new Curve("drips", 0, 0f, 345, 0f, 347, 0.8f, 362, 0.3f, 590, 0.2f, 606, 1f, 663, 0.7f);

        // ---------------- glass ----------------
        public const float Crack1Time = 275f, Crack2Time = 310f, Crack3Time = 330f;   // 4:35, 5:10, 5:30

        // ---------------- camera ----------------
        static readonly Curve shake = new Curve("shake", 0, 0.04f, 236, 0.04f, 241, 1f, 256, 0.45f, 270, 0.5f, 320, 0.9f, 344.6f, 0.9f, 346, 0.08f,
            470, 0.2f, 520, 0.6f, 546, 1f, 566, 0.4f, 600, 0.08f, 663, 0.03f);

        /// <summary>Fills the state for story time T. truckS/truckSpeed are copied in by the caller.</summary>
        public static void Evaluate(float T, ref StoryState s)
        {
            s.T = T;
            s.beat = Config.BeatAt(T);

            s.sunElev = sunElev.Eval(T); s.sunAzim = sunAzim.Eval(T); s.sunLux = sunLux.Eval(T);
            s.exposureEV = exposure.Eval(T); s.nightFill = nightFill.Eval(T);
            s.sunsetFill = sunsetFill.Eval(T);
            s.greenTint = greenTint.Eval(T); s.gradeCold = gradeCold.Eval(T); s.starVis = starVis.Eval(T);
            s.nightAmb = nightAmb.Eval(T); s.stormCore = stormCore.Eval(T); s.clearAir = clearAir.Eval(T);

            s.stormX = stormX.Eval(T); s.stormZ = stormZ.Eval(T);
            s.anvilReach = anvilReach.Eval(T); s.mammatus = mammatus.Eval(T); s.deckCover = deckCover.Eval(T);
            s.anvilHalfWidth = anvilHalfWidth.Eval(T); s.anvilBack = anvilBack.Eval(T);
            s.shelfAmount = shelfAmount.Eval(T);
            s.shelfZ = 18400f - 90f * (T - 100f);
            s.sheetActivity = sheetActivity.Eval(T); s.coreGlow = coreGlow.Eval(T); s.coreBack = coreBack.Eval(T);
            s.precipCore = precipCore.Eval(T); s.rainSheet = rainSheet.Eval(T);

            s.rain = rain.Eval(T); s.hail = hail.Eval(T); s.hailSize = hailSize.Eval(T); s.hailCover = hailCover.Eval(T);
            s.wind = wind.Eval(T); s.windDir = windDir.Eval(T); s.gust = gust.Eval(T);
            s.dust = dust.Eval(T); s.dustWallZ = s.shelfZ - 350f;
            s.fogMfp = fogMfp.Eval(T); s.wetness = wetness.Eval(T); s.wiper = wiper.Eval(T);
            s.headlights = headlights.Eval(T);

            s.torS = torS.Eval(T); s.torLat = torLat.Eval(T); s.torRadius = torRadius.Eval(T);
            s.torDescend = torDescend.Eval(T); s.torMorph = torMorph.Eval(T); s.torRope = torRope.Eval(T);
            s.torWrap = torWrap.Eval(T); s.torDebris = torDebris.Eval(T); s.wallCloud = wallCloud.Eval(T);
            s.torPower = torPower.Eval(T); s.domeScale = domeScale.Eval(T);

            s.townLights = townLights.Eval(T); s.townErase = townErase.Eval(T); s.canopy = canopy.Eval(T);

            s.insectA = insectA.Eval(T); s.insectB = insectB.Eval(T); s.insectC = insectC.Eval(T);
            s.radio = radio.Eval(T); s.pressure = pressure.Eval(T); s.siren = siren.Eval(T);
            s.engine = engine.Eval(T); s.ringing = ringing.Eval(T); s.drips = drips.Eval(T);

            s.crack1 = Mathf.Clamp01((T - Crack1Time) / 0.35f);
            s.crack2 = Mathf.Clamp01((T - Crack2Time) / 0.5f);
            s.crack3 = Mathf.Clamp01((T - Crack3Time) / 0.9f);

            s.shake = shake.Eval(T);
            s.black = 1f - Mathf.SmoothStep(0f, 1f, T / Config.StartFadeIn);
            if (T > Config.FadeOutStart)
                s.black = Mathf.SmoothStep(0f, 1f, (T - Config.FadeOutStart) / (Config.FadeOutEnd - Config.FadeOutStart));
            s.title = Mathf.SmoothStep(0f, 1f, (T - 0.6f) / 1.2f) * (1f - Mathf.SmoothStep(0f, 1f, (T - 3.2f) / 1.3f));
            s.hint = Mathf.SmoothStep(0f, 1f, (T - 4.5f) / 1f) * (1f - Mathf.SmoothStep(0f, 1f, (T - 10f) / 1f));
            s.closing = Mathf.SmoothStep(0f, 1f, (T - Config.ClosingLineTime) / 2f) *
                        (1f - Mathf.SmoothStep(0f, 1f, (T - (Config.FadeOutStart - 1f)) / 3f));
        }

        /// <summary>World position (double) of the tornado centre for a state.</summary>
        public static void TornadoWorld(in StoryState s, out double x, out double z)
        {
            PathDef.RoadToWorld(s.torS, s.torLat, out x, out z);
        }

        /// <summary>Height (m) where the funnel enters the cloud. Only a shortcut: the formula lives in FunnelShape.Top,
        /// the one C# copy of the funnel.</summary>
        public static float FunnelTop(in StoryState s) => FunnelShape.Top(s);
    }
}
