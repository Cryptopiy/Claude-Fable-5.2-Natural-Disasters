namespace MESOCYCLONE
{
    /// <summary>Everything the picture and the sound need to know about "now", as plain numbers.
    /// Filled by Story.Evaluate(T): a pure function of the story time (plus the truck's chainage).</summary>
    public struct StoryState
    {
        public float T;                 // story time, seconds
        public int beat;                // 0..7
        public double truckS;           // actual chainage of the truck
        public float truckSpeed;        // m/s

        // --- sky and light ---
        public float sunElev, sunAzim;  // degrees; azimuth clockwise from north
        public float sunLux;            // direct light of the sun
        public float exposureEV;        // fixed exposure, eased
        public float nightFill;         // lux of the dim ambient fill at night
        public float sunsetFill;        // 0..1 warm glow of the sunset horizon as a fill light
        public float greenTint;         // 0..1 sick green of hail-laden cloud
        public float gradeCold;         // 0 warm sunset .. 1 cold slate
        public float starVis;           // 0..1
        public float nightAmb;          // nits of the slate ambient on the storm's faces at night (falls as the tear opens)
        public float stormCore;         // 0..1 the breathing light deep in the departing storm
        public float clearAir;          // 0..1 rain haze washed out of the tear's air

        // --- storm body (world metres, double) ---
        public double stormX, stormZ;   // updraft centre
        public float anvilReach;        // how far the anvil extends downwind (south, toward the truck)
        public float anvilHalfWidth;    // lateral half width of the anvil, metres
        public float anvilBack;         // how far the anvil extends to the north (away from the truck)
        public float mammatus;          // 0..1
        public float deckCover;         // 0..1 low/mid overcast under the anvil
        public float shelfZ;            // world z of the shelf-cloud leading edge (on the road line)
        public float shelfAmount;       // 0..1
        public float sheetActivity;     // 0..1 ambient internal lightning rate
        public float coreGlow;          // 0..1 visibility of the green rain/hail core ahead
        public float coreBack;          // 0..1 the never-ending in-cloud lightning deep in the rain behind the vortex
        public float precipCore;        // 0..1 the forward-flank rain/hail core beside the base
        public float rainSheet;         // 0..1 scale of the rain sheet behind the shelf

        // --- weather at the truck ---
        public float rain;              // 0..1
        public float hail;              // 0..1 rate
        public float hailSize;          // metres (diameter)
        public float hailCover;         // 0..1 stones lying on the ground after the hail
        public float wind;              // m/s
        public float windDir;           // degrees, direction the wind blows TO, clockwise from north
        public float gust;              // 0..1 buffeting
        public float dust;              // 0..1 density at the truck
        public float dustWallZ;         // world z of the dust wall front
        public float fogMfp;            // metres, mean free path of the air around the truck
        public float wetness;           // 0..1 road and glass
        public float wiper;             // 0 off, 1 intermittent, 2 normal, 3 fast
        public float headlights;        // 0..1

        // --- tornado (road coordinates) ---
        public float torS, torLat;      // chainage and lateral offset of the vortex centre
        public float torRadius;         // funnel radius at the ground, metres
        public float torDescend;        // 0 hidden in the wall cloud .. 1 on the ground
        public float torMorph;          // 0 rope .. 0.5 cone .. 1 wedge
        public float torRope;           // 0..1 rope-out (sinuous, thinning)
        public float torWrap;           // 0..1 rain wrapping that hides it
        public float torDebris;         // 0..1 debris cloud
        public float domeScale;         // 0..1 size of the debris dome: 1 full, 0.45 held low while the vortex passes close (beat 6)
        public float wallCloud;         // 0..1 lowering of the wall cloud
        public float torPower;          // 0..1 (sound and wind)

        // --- town ---
        public float townLights;        // 1 all lit .. 0 dark
        public float townErase;         // 0 standing .. 1 flattened
        public float canopy;            // 0..1 gas-station light

        // --- sound-only scores ---
        public float insectA, insectB, insectC;
        public float radio;             // 0..1
        public float pressure;          // 0..1 blocked ear
        public float siren;             // 0..1
        public float engine;            // 0..1
        public float ringing;           // 0..1 after the hail
        public float drips;             // 0..1

        // --- glass ---
        public float crack1, crack2, crack3;   // 0..1 growth of each crack

        // --- camera and UI ---
        public float shake;             // 0..1 of Config.ShakeMaxDeg
        public float black;             // 1 = black screen
        public float title, hint, closing;
    }
}
