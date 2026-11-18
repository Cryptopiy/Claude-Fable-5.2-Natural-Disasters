#ifndef CASCADE_PLUMEFX_INCLUDED
#define CASCADE_PLUMEFX_INCLUDED
// The low volumes of the catastrophe (Plume.shader pass 2, half resolution): collapse dust, the splash crown,
// the spray curtain of the wave and the strike on the cliffs. Included by Plume.shader after its helpers
// (RayCyl, PLod, gFoot, FogTau*, the plume uniforms) and before FragComposite, which uses FxStart and _FxMisc.

float4 _FxDust;     // xy slide direction (xz, unit), z front distance from the cone axis (m), w strength
float4 _FxDust2;    // x cloud top above the ground (m), y trail length (m), z clock (s)
float4 _FxSplash;   // xy centre (xz), z crown height (m), w crown radius (m)
float4 _FxSplash2;  // x strength, y mist 0..1, z clock (s)
float4 _FxCrest;    // xy centre of curvature of the front (xz), z front radius (m), w crest height (m, main lobe)
float4 _FxCrest2;   // x front half width (m), y strength, zw main lobe direction (xz)
float4 _FxCrest3;   // x wave clock (s), y front speed (m/s), z max run-up height (m)
float4 _FxImpact;   // x strength of the strike on the cliffs, y cap of the jet height (m), z reach behind the front (m), w top of its volume (m)
float4 _FxImpact2;  // main-lobe wave height (m) 0, 10, 20 and 30 s ago: the strike keeps the height of the wave that made it
float4 _FxMisc;     // x on (0/1), y max steps per interval, z gain of the fire's forward-scattered back light, w gain of the fire key (x _PlUp)

// ---------------- tunables ----------------
#define FX_MIN_STEP     6.0         // m: a short segment (a ray into nearby ground) never samples finer than this
// The fire below (fountain, glowing scar, column foot) is one light for the column (Plume March) and these volumes: the same
// _PlUp, position and falloff, so dust, spray and column agree on where the light comes from.
#ifdef PL_FIRE_Y
#define FX_FIRE_Y       PL_FIRE_Y
#else
#define FX_FIRE_Y       250.0       // m above the vent
#endif
#ifdef PL_FIRE_R
#define FX_FIRE_R       PL_FIRE_R
#else
#define FX_FIRE_R       1600.0      // m: falloff radius, 1 / (1 + d^2 / R^2)
#endif
#define FX_FIRE_ISO_D   0.6         // side light of the fire on ash (diffuse, multiply scattered): the warm side of the dust
#define FX_FIRE_ISO_W   0.25        // on droplets (they scatter forward: most of their fire light is the back-lit rim)
#define FX_STRIP_RIM    0.4         // rim from the daylight strip under the umbrella edge
#define FX_IMP_RISE     2.2         // jet height of the strike per metre of incoming wave
#define FX_IMP_LEAN     0.30        // the sheet of the strike leans inland by this much per metre of height
#define FX_IMP_LIFE0    22.0        // s after the crest: the falling mist starts to clear ...
#define FX_IMP_LIFE1    36.0        // ... and is gone (keep _FxImpact.z >= LIFE1 * front speed + 200 m)
#define FX_IMP_NEAR0    40.0        // m: no strike spray this close to the lens (rain and lens drops own the near field) ...
#define FX_IMP_NEAR1    120.0       // ... full density from here
#define FX_IMP_STRETCH  0.45        // vertical noise frequency of the strike / horizontal (features ~2x taller than wide): jets and curtains, not puffs
#define FX_IMP_FALLTHIN 0.4         // the falling sheet thins by this much: patchy curtains and mist, not a saturated grey wall
#define FX_IMP_RISE_T   4.0         // s: the strike is a blow, ~88 % of its height after 2 s (ease-out cubic), then it falls back
#define FX_IMP_CANOPY   0.8         // m of extra half width per metre above half the jet's peak: the head mushrooms ...
#define FX_IMP_CURL     0.5         // ... and curls inland by this much per metre above 0.6 of the peak
#define FX_IMP_JET      85.0        // m of coast per jet of the strike (was one noise curve with ~140 m between peaks)
// The billow noise (two FxBillow octaves) is ~0.48 +- 0.06: added as it was, it only frayed a smooth iso-surface of the
// envelope (the drum-like crown at 345, the rocket-exhaust strike at 462). These gains stretch it about its mean so it
// tears the envelope into billows, holes and jets; the mean coverage is unchanged
#define FX_BILLOW_MEAN  0.48
#define FX_G_MEAN       0.52        // mean of the smooth fbm layer (G) of the 3D noise
#define FX_SPLASH_TEAR  3.0         // splash (was 0.9)
#define FX_IMP_TEAR     4.5         // strike (was 0.9)
#define FX_CREST_TEAR   4.0         // crest spray (was 1.3): holes, dark cores and jets instead of a bright plateau
#define FX_DUST_TEAR    4.0         // collapse dust (was 1.6): billows and gaps instead of a smooth slab
#define FX_SPLASH_LOBE  260.0       // m: the crown's radius swings by up to +-half this between its ~8 leaning lobes
#define FX_CURT_R       2.1         // radius of the crown's curtain circle in noise space: ~53 cells round, ~26 sheets
#define FX_CURT_H       1400.0      // m of height per noise unit there: cells ~350 m tall, ~3x their width
#define FX_DUST_LOBE    900.0       // m per unit of noise: the dust head's lobes lead and lag by ~+-90 m (up to ~250 m)
#define FX_CREST_NEAR   0.35        // crest spray kept at the lens (FX_IMP_NEAR0) ...
#define FX_CREST_NEAR1  200.0       // ... and in full from this distance (m)
#define FX_CREST_FAR_FLOOR 0.25     // strength of the crest's weak (glassy) sections beyond ~3.5 km (0.70 near)
// Water is lit as white water, not as ash: under the umbrella the zenith is the dark lid and the light that matters is
// the horizon ring and the daylight strip, and a thick droplet cloud keeps most of its multiply scattered fill inside
#define FX_WAT_RING     0.65        // droplet fill: (zenith + horizon) / 2 pulled this far toward the horizon ...
#define FX_WAT_STRIP    0.15        // ... plus this share of the strip (a ~10 deg band all round: ~10-15 % of the sphere)
// Fill deep inside a water volume relative to its thin edges (dust: 0.30). 0.80 made the spray's interior as bright as
// its edges: a flat plateau. The splash crown keeps more (its curtains are thin and it already reads as torn water)
#define FX_WAT_FLOOR    0.50        // crest spray and strike
#define FX_SPLASH_FLOOR 0.70        // splash crown, jets, surge and mist
#define FX_WAT_ALB      float3(0.97, 0.98, 0.99)
// Form: one more self-shadow tap, up and a little toward the eye, tells billow tops and the faces turned up to us (which
// take the sky) from creases and undersides (which do not). The fill was nearly constant, so the crown was a flat card
#define FX_WAT_AO       0.45        // fill of a fully occluded water sample relative to an open one
#define FX_WAT_AO_D     70.0        // m: distance of that tap
#define FX_WAT_AO_K     75.0        // optical depth per unit extinction there (~ the depth of a billow)
// Silver lining: droplets forward-scatter what stands straight behind them (FragMarchFx). The volume passes on
// T (1 + FX_WAT_FWD tauW T) of the background: tau 0.5 ~ 0.97, tau 1 ~ 0.64, tau 2 ~ 0.21, tau 3 ~ 0.07 (bare T: 0.61,
// 0.37, 0.14, 0.05). Close to the diffraction-peak estimate exp(-tau / 2) for thin veils; thick cores stay a mass
#define FX_WAT_FWD      2.0
#define FX_SPLASH_FOOT  75.0        // m: where its billows thin out the splash lifts off the sea by up to this (ragged foot)

static const float3 FxLum = float3(0.2126, 0.7152, 0.0722);

// Henyey-Greenstein phase times pi (isotropic = 0.25), the normalisation the plume's lighting uses
float FxHG(float g, float c) { return (1.0 - g * g) / pow(max(1.0 + g * g - 2.0 * g * c, 1e-4), 1.5) * 0.25; }

// ---------------- low volumes ----------------
// q in noise units (tiles); scale (m per tile) only sets the mip
float FxBillowQ(float3 q, float scale, float t)
{
    float a = CNoise3(q + float3(0.011 * t, -0.017 * t, 0.0), PLod(0.0, scale)).r;
    float b = CNoise3(q * 2.9 + float3(0.31, -0.05 * t, 0.17), PLod(0.0, scale / 2.9)).a;
    return a * 0.62 + b * 0.38;
}
float FxBillow(float3 p, float scale, float t) { return FxBillowQ(p / scale, scale, t); }

// The 3D noise tile repeats every `scale` metres along the world axes. A volume seen edge-on through kilometres of
// itself (the splash mist that lingers over the caldera to ~450 s: ~4.5 km wide, a few hundred metres tall) sums the
// same few columns of the tile along every ray close to a lattice direction (x, z, the diagonals x+-z, 2x+-z, ...):
// there the sums keep the columns' differences instead of averaging out as they do along other rays, so the mist drew
// bright and dark spokes fanning out of each such direction's vanishing point on the horizon (395: the starbursts on
// the far wave at the vanishing points of -x and -x-z, fainter fans at higher-order ones). Turning the
// lattice cannot help (some low-order direction always lies near the horizontal). Instead a smooth warp in noise space
// (three plane waves ~20 tiles long, each shifting the tile by up to 0.75 of it across its own direction, aimed so
// that every near-horizontal ray wanders across the columns in both directions transverse to it) breaks the
// periodicity along long rays. Strain under ~25 %: a billow keeps its shape and its statistics; ALU only.
#define FX_UNTILE_D     0.75
float3 FxUntile(float3 q)
{
    const float3x3 K = float3x3(-0.2923,  0.0276,  0.0618,     // wave vectors (rad per tile), one per row
                                -0.2102,  0.0261, -0.1674,
                                 0.0694, -0.0104, -0.3225);
    const float3x3 E = float3x3(-0.2016, -0.7649, -0.6118,     // their displacement directions (unit, normal to the row of K)
                                 0.4153,  0.8202, -0.3936,
                                 0.8502, -0.4876,  0.1986);
    return q + FX_UNTILE_D * mul(sin(mul(K, q) + float3(0.0, 1.7, 4.1)), E);
}

// main-lobe height of the wave that struck tau seconds ago (piecewise linear through _FxImpact2)
float FxStrikeHeight(float tau)
{
    float k = clamp(tau, 0.0, 30.0) * 0.1;
    float4 h = _FxImpact2;
    return k < 1.0 ? lerp(h.x, h.y, k) : (k < 2.0 ? lerp(h.y, h.z, k - 1.0) : lerp(h.z, h.w, k - 2.0));
}

// extinction (1/m) at absolute p; white = the water share of it (1 spray, 0 rock dust). A mass fraction, not the local
// density: the soft edge of a water volume is still water, and spray inside the dust stays a mix. fillK = the fill a
// fully self-shadowed water sample keeps (the splash crown keeps more than the crest spray and the strike)
float FxDensity(float3 p, out float white, out float fillK)
{
    float dens = 0.0, wat = 0.0, watS = 0.0; white = 0.0; fillK = FX_WAT_FLOOR;
    if (_FxDust.w > 0.001)
    {
        float2 rel = p.xz - _CCone.xz;
        float rho = length(rel);
        float u = dot(rel, _FxDust.xy), v = rel.x * _FxDust.y - rel.y * _FxDust.x;     // = ConeSlideUV (same azimuth)
        float overSea = smoothstep(CONE_R - 150.0, CONE_R + 350.0, rho);
        float W = 340.0 + 0.45 * max(u, 0.0) + 0.6 * max(rho - CONE_R, 0.0);            // it spreads over the water
        float uf = _FxDust.z;
        // One 2D tap in the slide frame (it drifts at half the front's speed, so the shape keeps boiling): R pushes the
        // head out and back in ~500 m lobes across the slide (seen from the village at ~40 deg off its axis, the front
        // face was one straight wall), B (cellular) raises ~330 m cauliflower towers on the top (it was a flat lid)
        float tail = smoothstep(uf - _FxDust2.y, uf - _FxDust2.y + 1100.0, u);
        float vv = v / (0.85 * W);
        // the splash knocks the dust down over the sea: the crown stands clear of it
        float latK = exp(-vv * vv) * (1.0 - 0.65 * overSea * saturate(_FxSplash2.x));
        // a loose bound before the lobe tap: the head reaches at most 0.52 FX_DUST_LOBE beyond uf
        float al0 = tail * latK * (1.0 - smoothstep(uf + 0.52 * FX_DUST_LOBE - 120.0, uf + 0.52 * FX_DUST_LOBE + 300.0, u));
        float4 nD = float4(0.5, 0.5, 0.5, 0.5);
        float ufv = uf, al = 0.0;
        if (al0 > 0.002)
        {
            nD = CNoise2All(float2(v / 2000.0 + 0.83, (u - 0.5 * uf) / 2000.0 + 0.27));
            ufv = uf + FX_DUST_LOBE * (nD.r - 0.48);
            // al bounds the density from above (the head at the ground is the farthest out, the sides are widest there)
            al = tail * (1.0 - smoothstep(ufv - 120.0, ufv + 300.0, u)) * latK;
        }
        if (al > 0.002)
        {
            // the ground after the collapse: the scar floor plus the moving mass riding down it (ConeHeight without its
            // relief), so the vent-lit layer does not hang in the air where the flank used to be
            float s = _ConeSlide.x, k = saturate(s / 2600.0);
            float Tm = 0.0;
            if (u - s > -660.0)                                                              // behind its head scarp there is no mass
            {
                Tm = ConeScarThickness((u - s) * (1.0 - 0.15 * k), v * (1.0 - 0.2 * k)) * _ConeSlide.y;
                Tm *= 1.0 - smoothstep(CONE_R - 500.0, CONE_R + 250.0, rho) * saturate(s / 300.0);
            }
            float above = p.y - max(ConeProfile(rho) - ConeScarThickness(u, v) + Tm, 0.0);
            float top = _FxDust2.x * lerp(0.5, 1.0, smoothstep(ufv - 1500.0, ufv - 100.0, u));
            top *= 1.0 + 1.5 * saturate((ufv - u) / 1500.0) * saturate(_FxDust2.z / 25.0); // it climbs behind the front
            // it lies low over the water, but as a gravity current: a head ~1.6x taller than the body in the leading 500 m
            top *= lerp(1.0, 0.55, overSea) * (1.0 + 0.6 * overSea * smoothstep(ufv - 700.0, ufv - 150.0, u));
            // cauliflower towers; weaker over the flank, where the tallest dust already meets the volume's 1750 m lid
            top *= lerp(1.0, lerp(0.7, 1.35, nD.b), 0.4 + 0.6 * overSea);
            // A rounded, rolling nose instead of a vertical wall: the head is farthest out just above the ground and lags
            // with height (up to ~0.8 of the cloud's top behind), and the sides draw in toward the top (a dome in section)
            float hk = saturate(max(above, 0.0) / max(top, 50.0));
            float head = 1.0 - smoothstep(ufv - 120.0, ufv + 300.0, u + 0.8 * top * hk * hk);
            float vs = vv / (1.0 - 0.35 * hk);
            float vert = smoothstep(-40.0, 20.0, above) * exp(-max(above, 0.0) / (0.45 * top));   // densest at the ground
            float base = tail * head * exp(-vs * vs) * (1.0 - 0.65 * overSea * saturate(_FxSplash2.x)) * vert;   // <= al * vert
            if (base > 0.002)
            {
                float3 pA = p - float3(_FxDust.x, -0.35, _FxDust.y) * (_FxDust2.z * 16.0);     // rolled forward along the slide
                float n = FxBillow(pA, 320.0, _FxDust2.z) * 0.55 + FxBillow(pA, 110.0, _FxDust2.z) * 0.30 + CNoise3(pA / 40.0, PLod(0.0, 40.0)).r * 0.15;
                float n2 = CNoise3(p / 900.0 + float3(0.0, -0.004 * _FxDust2.z, 0.0), PLod(0.0, 900.0)).g;
                // The billows (+-0.05) only frayed the smooth envelope: the slab had dead-flat profiles. Stretched about their
                // mean they tear it into billows and gaps; scaled by the envelope so they open holes inside the cloud and
                // fray its edge, but never seed loose puffs in its far tail
                float d = saturate(base * 1.25 - 0.24 + (n - FX_BILLOW_MEAN) * FX_DUST_TEAR * smoothstep(0.03, 0.35, base) + (n2 - FX_G_MEAN) * 0.6);
                dens += d * d * 0.012 * _FxDust.w;
            }
        }
    }
    if (_FxSplash2.x > 0.001)
    {
        float2 rel = p.xz - _FxSplash.xy;
        float r = length(rel);
        float H = _FxSplash.z, Rc = _FxSplash.w;
        float lowY = smoothstep(-20.0, 25.0, p.y);
        float ts = _FxSplash2.z;
        // six thin jets thrown up around the entry point, each with its own height and timing (they rise, then fall back)
        float jets = 0.0;
        [unroll] for (int j = 0; j < 6; j++)
        {
            float a = j * 1.047 + CRand((uint)j * 7919u + 13u) * 0.8;
            float rr = Rc * (0.25 + 0.6 * CRand((uint)j * 104729u + 7u));
            float2 cj = float2(cos(a), sin(a)) * rr;
            float hj = H * (0.6 + 0.5 * CRand((uint)j * 15485863u + 3u)) * saturate(sin(saturate((ts - j * 0.8) / 34.0) * 3.14159));
            float rj = 100.0 + 0.18 * hj;
            float2 dj = rel - cj;
            float lean = p.y / max(hj, 1.0);
            dj -= normalize(cj + 1e-3) * lean * 120.0;                                    // jets lean outward
            jets = max(jets, exp(-dot(dj, dj) / (rj * rj)) * (1.0 - smoothstep(hj * 0.85, hj * 1.02 + 1.0, p.y)));
        }
        // the crown: a hollow, flaring water curtain on the sea side of the entry, its rim torn into fingers. All azimuthal
        // noise is read along circles in noise space, so it has no seam and does not repeat
        float2 dirS = rel / max(r, 1.0);
        float seaS = smoothstep(CONE_R - 300.0, CONE_R + 200.0, length(p.xz - _CCone.xz));
        float rag = CNoise2All(dirS * 0.55 + float2(0.31, 0.77)).r;                       // ~14 fingers of the rim
        float Hf = H * (0.45 + 0.75 * smoothstep(0.30, 0.72, rag));
        float Hc = Hf * saturate(sin(saturate(ts / 30.0) * 3.14159));
        // Seen side-on, a ring whose radius depends only on height is a drum: its sides are the ring's tangent lines.
        // ~8 lobes (Worley B, drifting with height so they tilt) push the curtain in and out and flare by different
        // amounts (R of the same tap), so the sides lean, bulge and tear. Half the drift it had: at p.y/2400 a lobe
        // turned ~60 deg round the crown over its height, and the limbs became sets of parallel slanted lines
        float4 nT = CNoise2All(dirS * 0.32 + float2(0.21 + p.y / 4800.0, 0.53 + p.y / 7200.0));
        float tear = FX_SPLASH_LOBE * (nT.b - 0.5);
        float flare = lerp(0.22, 0.75, smoothstep(0.33, 0.63, nT.r));                     // was 0.38 everywhere
        float rc = 0.9 * Rc + flare * p.y + tear;
        float xc = (r - rc) / (90.0 + 0.14 * p.y);                                   // thicker than a march step: no dither lattice
        // ~26 falling sheets with see-through gaps between them, thinning as they rise. Read on a cylinder in noise space
        // (azimuth round a circle, height up the third axis), so every cell edge stands upright or lies level: on circles
        // whose radius grew with height the straight cell edges came out as slanted bands (the 345 diagonal hatching).
        // ~53 cells round, ~350 m tall: sheets ~3x taller than wide. Their texture rides up with the throw (60 m/s) and
        // comes down with the collapse (25 m/s)
        float curt = CNoise3(float3(dirS * FX_CURT_R, (p.y - 60.0 * min(ts, 12.0) + 25.0 * max(ts - 12.0, 0.0)) / FX_CURT_H),
                             PLod(0.0, 400.0)).g;
        float crown = exp(-xc * xc) * (1.0 - smoothstep(0.75 * Hc, Hc + 1.0, p.y)) * seaS
                    * lerp(0.55, 1.0, smoothstep(0.30, 0.70, curt)) * lerp(1.0, 0.6, saturate(p.y / max(Hc, 1.0)));   // soft sheets: hard ones read as hatching
        // fall-back curtains: the crown collapses as rain shafts just outside its rim (~40 around, falling at 30 m/s);
        // the water takes its time to come down, so they outlast the crown
        float shafts = 0.0;
        float fallK = smoothstep(10.0, 18.0, ts) * (1.0 - smoothstep(26.0, 46.0, ts));
        if (fallK > 0.001)
        {
            float az = atan2(rel.y, rel.x);
            float xs = (r - (Rc + flare * Hc + tear)) / 160.0;                           // just outside the torn rim
            shafts = fallK * exp(-xs * xs) * seaS * (1.0 - smoothstep(0.6 * Hf, 0.9 * Hf + 1.0, p.y))
                   * smoothstep(0.55, 0.8, CNoise2All(float2(az * (10.0 / 6.2831853), p.y / 600.0 + ts * 0.05)).r);
        }
        // the base surge: a low billowing ring racing outward over the water, its front wobbling with the crown's lobes.
        // Sectioned by the sea rings' own lobes (the same tap round the same centre) and thrown down the slide axis:
        // taller and shorter sections with gaps between them, strongest toward the village (an even band read as a
        // pond target). Strong lobes stand ~180 m, middling ones ~60 m, weak ones break up
        float xsu = (r + 0.6 * tear - Rc * 1.3) / (0.35 * Rc + 80.0);
        float surge = exp(-xsu * xsu), lobS = 0.0;
        if (surge > 0.003) lobS = SeaRingLobe(dirS);                                    // the tap only near the ring
        else surge = 0.0;
        surge *= exp(-max(p.y, 0.0) / (150.0 * (0.5 + 0.8 * lobS))) * SeaRingAim(dirS) * (0.3 + 1.0 * lobS);
        float xm = r / (Rc * 1.8 + 500.0);
        float mist = _FxSplash2.y * exp(-xm * xm) * (1.0 - smoothstep(60.0, 560.0, p.y));
        // mist x0.54 (was 0.6): the stronger billow gain below would otherwise thicken the mist that lingers to ~400 s
        float body = max(max(max(jets, crown), max(surge * 0.9, mist * 0.54)), shafts * 0.7);
        if (body * lowY > 0.002)
        {
            // untiled (FxUntile): the mist is the one low volume seen edge-on through kilometres of itself
            float n = FxBillowQ(FxUntile((p + float3(0.0, -ts * 11.0, 0.0)) / 120.0), 120.0, ts) * 0.65
                    + FxBillowQ(FxUntile((p + 7.3) / 45.0), 45.0, ts) * 0.35;
            // a ragged foot instead of a ruler-flat cut where the sea takes the volume: dense billows reach down into the
            // surge, thin ones lift off it (the whitewater ring of the water shader shows below)
            float foot = smoothstep(-20.0, lerp(FX_SPLASH_FOOT, 10.0, smoothstep(0.35, 0.65, n)), p.y);
            // = body * foot * 1.2 + n * 0.9 - 1 at the old gain: same mean coverage, real billows and creases
            float d = saturate((body * foot * 1.2 - 0.568 + (n - FX_BILLOW_MEAN) * FX_SPLASH_TEAR) * 2.4);
            float dw = d * d * 0.075 * _FxSplash2.x;
            dens += dw; wat += dw; watS += dw;
        }
    }
    bool crestOn = _FxCrest2.y > 0.001, impactOn = _FxImpact.x > 0.001;
    if (crestOn || impactOn)
    {
        float2 rel = p.xz - _FxCrest.xy;
        float r = length(rel);
        float2 dir = rel / max(r, 1.0);
        float cosA = dot(dir, _FxCrest2.zw);
        float lat = atan2(dir.x * _FxCrest2.w - dir.y * _FxCrest2.z, cosA) * max(_FxCrest.z, 300.0);
        float3 lbs = SeaLobes(lat); float lobe = lbs.x, lobe2 = lbs.y;          // the same lobes as the water surface
        float xi = r - _FxCrest.z + lobe * 300.0 + lobe2 * 60.0;
        float wf = _FxCrest2.x;
        float side = lerp(0.30, 1.0, smoothstep(0.2, 0.86, cosA)) * (1.0 + 0.40 * lobe + 0.20 * lobe2);   // flanks and lobes of the front
        float sdl = SeaGround(p.xz).y;
        float tw = _FxCrest3.x;
        if (crestOn)
        {
            // The spray is rooted in the water's own crest (CascadeSea SeaEvalX), so it grows out of the white lip instead
            // of floating above it as puffs: the base level of the sea here (the drawdown still empties the bay while the
            // wall comes in), the crest teeth, and the land's cap as the sea applies it (room left / crest height)
            float yS = (_SeaAim.w - _SeaAim.z) * (1.0 - smoothstep(_SeaBay.z, _SeaBay.w, length(p.xz - _SeaBay.xy)));
            float py = p.y - yS;
            float A = _FxCrest.w * side;
            // the local half width of the front, as the sea surface has it (CascadeSea s.wf), so the spray sits on that crest
            float wfL = max(0.5 * A * (1.0 - 0.3 * lobe2), 26.0);
            A *= (1.0 + SEA_TEETH * (lbs.z - 0.5)) * lerp(1.0, min(1.0, max(_FxCrest3.z - yS, 0.0) / max(A, 1.0)), smoothstep(-460.0, 20.0, sdl))
               * exp(-max(sdl, 0.0) / 170.0);
            // torn-back plumes rise from the lobe peaks and stream back off the crest; the spray starts inside the crest
            // (0.6-0.88 A, hidden by the water in front) so its foot is the lip itself, and the top edge varies (plumes
            // ~760 m, lobes ~380 m) and breaks the smooth hump line of the crest instead of being a ruler line
            float hA = max(py - A, 0.0);
            float pl = smoothstep(0.50, 0.80, CNoise2All(float2(lat / 760.0, 0.13)).r + 0.35 * lobe);
            // the spray is torn off just behind the peak and only spreads backward: a short front flank (the face stays
            // clear: its white lip is the water shader's) and a long trailing one that leans back as it rises
            float xcr = -0.15 * wfL - hA * lerp(0.5, 1.6, pl);
            float wB = 0.5 * wfL + 30.0 + 0.45 * hA, wF = 0.12 * wfL + 12.0 + 0.30 * hA;
            float sx = (xi - xcr) / (xi > xcr ? wF : wB);
            float topK = 1.2 + 0.9 * pl + 0.4 * lobe2;
            float vert = smoothstep(0.60 * A, 0.88 * A, py) * (1.0 - smoothstep(A * topK, A * (topK + 0.2 + 0.3 * pl) + 60.0, py));
            // some sections of the front break harder than others (the ~380 m lobes and the plumes): an even band of
            // spray along kilometres of crest read as one straight 'cigarette' from the village. Floor 0.7 (was 0.45):
            // lower, the weak sections opened holes and the curtain fell apart into separate puffs. That holds near
            // (425); from 1.5-3.5 km the floor falls to FX_CREST_FAR_FLOOR, so the far front (395) shows white breaking
            // sections with the glassy face between them instead of one even fog bank
            float farS = smoothstep(1200.0, 2400.0, distance(p.xz, _WorldSpaceCameraPos.xz));
            float brk = lerp(lerp(0.70, FX_CREST_FAR_FLOOR, farS), 1.0, SeaBreak(lat, lbs));   // the water's breaking sections
            // the near field (the crest at the cliff below the path, 455-462) belongs to the droplet and rain sprites: a
            // volume there was a white-out wall over half the frame
            float nearK = lerp(FX_CREST_NEAR, 1.0, smoothstep(FX_IMP_NEAR0, FX_CREST_NEAR1, distance(p, _WorldSpaceCameraPos.xyz)));
            float base = exp(-sx * sx) * vert * saturate(A / 40.0) * lerp(0.55, 1.0, exp(-hA / (0.55 * A + 40.0))) * brk * nearK;
            if (base > 0.002)
            {
                // local frame: along the front, up, across (radial). The noise rises at 14 m/s and drifts back from the
                // front at 10 m/s, so the spray streams up and off the crest in veils
                float2 tg = float2(-dir.y, dir.x);
                float3 q = float3(dot(p.xz, tg) / 70.0, (p.y - 14.0 * tw) / 60.0, (r - _FxCrest.z + 10.0 * tw) / lerp(170.0, 70.0, farS));   // far: no radial veils (they converge into a starburst)
                // turned 25 deg about the vertical in noise space: along the front the taps cross the tile obliquely. On
                // its axis the tile repeated every 200 m of front, which the stronger tear below would show as a comb
                q.xz = float2(q.x * 0.906 - q.z * 0.423, q.x * 0.423 + q.z * 0.906);
                float n1 = CNoise3(q * 0.35, PLod(0.0, 120.0)).g;
                // the fine layer is displaced by the coarse one (+-0.75 of its cells along the front), so the torn veils
                // come at irregular spacing instead of a comb of even columns; its radial scale (~77 m, was 131) is near
                // the along-front one, so seen from above the veils do not line up into vertical streaks either
                float3 q2 = float3(q.x * 1.3 + 3.0 * (n1 - 0.5), q.y * 1.3, q.z * 2.2 + 1.5 * (n1 - 0.5)) + 0.3;
                float n = n1 * 0.6 + CNoise3(q2, PLod(0.0, 45.0)).g * 0.4;
                // The noise (+-0.08) only frayed the envelope, so the core saturated into a flat bright plateau (425-462,
                // cumulus, not water). Stretched about its mean it tears holes, dark cores and jets into the body; scaled
                // by the envelope so it frays the edge but never seeds loose puffs in the far tail. Denser (0.030, was
                // 0.020) to keep the mean opacity the holes take away
                float d = saturate((base * 1.2 - 0.46 + (n - FX_G_MEAN) * FX_CREST_TEAR * lerp(1.0, 0.45, farS) * smoothstep(0.05, 0.45, base)) * 2.0);
                float dw = d * d * 0.030 * _FxCrest2.y;
                dens += dw; wat += dw;
            }
        }
        if (impactOn)
        {
            // the strike: where the wall meets the cliffs a sheet of water explodes up the face, then falls back as mist.
            // tau = seconds since the crest passed this point, so the strike runs along the coast with the lobes
            float tau = -xi / max(_FxCrest3.y, 1.0);
            float tp = max(tau, 0.0);
            // only the near coast of the terrain bake has a shore distance (this also leaves out the cone's own shore)
            float2 tuv = (p.xz - _CTerrainRect.xy) * _CTerrainRect.zw;
            float2 tin = min(tuv, 1.0 - tuv);
            float env = smoothstep(-0.3, 0.8, tau) * (1.0 - smoothstep(FX_IMP_LIFE0, FX_IMP_LIFE1, tau)) * saturate(min(tin.x, tin.y) * 10.0);
            float lean = FX_IMP_LEAN * p.y + 5.0 * tp;
            float halfW = 30.0 + 0.22 * p.y + 7.0 * tp;
            // a loose bound first (sheet and mist): the canopy adds up to 1.25 FX_IMP_CANOPY of half width (the tallest
            // jets) and FX_IMP_CURL of lean per metre of height, so the reach widens with height (tight at the foot,
            // where most samples are)
            float hiY = max(p.y, 0.0);
            float xs0 = (sdl - 26.0 - lean - 0.5 * FX_IMP_CURL * hiY) / (halfW + (1.25 * FX_IMP_CANOPY + 0.5 * FX_IMP_CURL) * hiY);
            if (env > 0.001 && abs(xs0) < 5.5 && p.y > -10.0)
            {
                float Aloc = FxStrikeHeight(tau) * side;                                     // open-water height, not capped
                // faces turned toward the wall take the blow; the lee side only gets a wash
                float g0 = SeaGround(p.xz + float2(8.0, 0.0)).y, g1 = SeaGround(p.xz + float2(0.0, 8.0)).y;
                float fk = smoothstep(-0.1, 0.6, dot(float2(g0 - sdl, g1 - sdl), dir) * 0.125);
                float rise = saturate(tau / FX_IMP_RISE_T);
                float riseK = 1.0 - (1.0 - rise) * (1.0 - rise) * (1.0 - rise);              // a blow: fast, then decelerating
                float Hjet = clamp(FX_IMP_RISE * Aloc, 120.0, _FxImpact.y) * lerp(0.5, 1.0, fk);
                // Several jets of different heights instead of one geyser: one per FX_IMP_JET of coast, each with its own
                // place, height and width (hashed per cell, so they hold still along the coast), over a lower continuous
                // sheet. Their heads are round and widen into the canopy above ~half the height (a noise curve made every
                // local maximum a pointed tip: flames)
                float widen = 1.0 + 1.2 * saturate(p.y / max(Hjet * riseK, 1.0) - 0.45);
                float cz = lat / FX_IMP_JET, ci = floor(cz);
                float jet = 0.0;
                [unroll] for (int k = -1; k <= 1; k++)
                {
                    float c = ci + k;
                    uint hs = (uint)(int)(c + 65536.0) * 3u;
                    float jc = c + 0.2 + 0.6 * CRand(hs * 7919u + 11u);
                    float dz = (cz - jc) / ((0.30 + 0.25 * CRand(hs * 104729u + 5u)) * widen);
                    jet = max(jet, CRand(hs * 15485863u + 2u) * sqrt(saturate(1.0 - dz * dz)));
                }
                float fing = max(0.25, jet);
                // the canopy reaches 1.12 x the top: keep it under the volume's ceiling (_FxImpact.w, FxRing)
                float Hmax = min(Hjet * (0.55 + 0.9 * fing), (_FxImpact.w - 10.0) / 1.12);
                float Htop = Hmax * riseK;
                float fall = saturate((tau - FX_IMP_RISE_T - 1.0) / 10.0);
                float core = Htop * (1.0 - 0.75 * fall);                                     // then it falls back
                // the head mushrooms into a broad canopy that curls inland; the falling mist keeps the canopy of the peak.
                // Only the tall jets mushroom (x0.35..1.25 by the jet's own height): when every jet spread, the low ones
                // 85 m apart merged into one continuous sheet (the 462 white wall)
                float upC = max(p.y - 0.5 * Htop, 0.0);
                float halfW2 = halfW + FX_IMP_CANOPY * (0.35 + 0.9 * jet) * upC;
                float lean2 = lean + FX_IMP_CURL * max(p.y - 0.6 * Htop, 0.0);
                // the texture rides with the water: stretched up with the jet (self-similar, the top moves fastest), then
                // sinking with the fallout (as the envelope does), and drawn out upright into jets and curtains
                float qy = (p.y * Hmax / max(Htop, 0.5 * Hmax) + 15.0 * max(tau - FX_IMP_RISE_T - 1.0, 0.0)) * FX_IMP_STRETCH;
                // ragged flanks: the sheet narrows and widens with height (~100 m cells along the jet, ~60 m along the coast)
                float wob = lerp(0.55, 1.45, smoothstep(0.32, 0.64, CNoise2All(float2(lat / 240.0, qy / 180.0)).r));
                float xsh = (sdl - 12.0 - lean2) / (halfW2 * wob);
                // a torn, see-through head instead of a pointed tip
                float sheet = exp(-xsh * xsh) * smoothstep(-10.0, 15.0, p.y) * (1.0 - smoothstep(0.82 * core, 1.12 * core + 1.0, p.y))
                            * lerp(1.0, 0.55, smoothstep(0.5 * core, core, p.y));
                float xm = (sdl - 40.0 - lean2) / (2.0 * halfW2);
                float hm = min(1.3 * Htop, _FxImpact.w);
                // 0.28 (was 0.35): with the stronger tear gain the mist shows as sparse falling curtains, not a grey field
                float mist = 0.20 * fall * exp(-xm * xm) * (1.0 - smoothstep(0.62 * hm, hm + 1.0, p.y));
                float camK = smoothstep(FX_IMP_NEAR0, FX_IMP_NEAR1, distance(p, _WorldSpaceCameraPos.xyz));
                // as it falls back the sheet breaks up: a lower envelope lets the noise open holes in it (at full strength
                // the erosion saturated and the strike's remains were a uniform grey wall seen from the path, ~476 s)
                float base = max(sheet * (1.0 - FX_IMP_FALLTHIN * fall), mist) * env * saturate(Aloc / 30.0) * lerp(0.35, 1.0, fk) * camK;
                if (base > 0.002)
                {
                    float3 q = float3(p.x, qy, p.z);
                    float n = FxBillow(q, 90.0, tw) * 0.6 + FxBillow(q + 3.1, 32.0, tw) * 0.4;
                    // the stretched noise tears the core too (holes, separate jets), not only the rim: ~9 % of the core
                    // is see-through, the fringe breaks into blobs out to base ~0.25
                    float d = saturate((base * 1.25 - 0.66 + (n - FX_BILLOW_MEAN) * lerp(FX_IMP_TEAR, 1.8, fall)) * 2.0);   // the fallout softens into mist, not streaks
                    float dw = d * d * 0.06 * _FxImpact.x;
                    dens += dw; wat += dw;
                }
            }
        }
    }
    white = saturate(wat / max(dens, 1e-6));
    fillK = lerp(FX_WAT_FLOOR, FX_SPLASH_FLOOR, saturate(watS / max(wat, 1e-6)));
    return dens;
}

// ray intervals of the active volumes (relative ray: ro is absolute), first entry returned by FxStart
bool FxCone(float3 roAbs, float3 rd, out float t0, out float t1)
{
    t0 = 0.0; t1 = -1.0;
    if (_FxDust.w < 0.001 && _FxSplash2.x < 0.001) return false;
    float top = max(1750.0, _FxSplash.z * 1.2 + 200.0);
    float R = _FxSplash2.x > 0.001 ? 5200.0 : min(5200.0, _FxDust.z + 1900.0);
    return RayCyl(roAbs - float3(_CCone.x, 0.0, _CCone.z), rd, R, -60.0, top, t0, t1);
}
bool FxRing(float3 roAbs, float3 rd, out float t0, out float t1)
{
    t0 = 0.0; t1 = -1.0;
    bool crestOn = _FxCrest2.y > 0.001, impactOn = _FxImpact.x > 0.001;
    if (!crestOn && !impactOn) return false;
    float wf = _FxCrest2.x, A = _FxCrest.w;
    // behind the front: the torn-back plumes reach ~3.4 A back, the strike on the cliffs lives _FxImpact.z behind it
    float back = max(crestOn ? 1.5 * wf + 360.0 + 3.4 * A : 0.0, impactOn ? _FxImpact.z : 0.0);
    // ahead of the front: the short front flank of the spray (2.5 x its width at the local wf <= 1.5 wf) plus the lobe
    // offsets (+-180 m); the strike starts 0.3 s ahead of the crest. Tighter than before, so the steps land on the spray
    float Ro = _FxCrest.z + 0.3 * wf + 230.0, Ri = max(_FxCrest.z - back, 1.0);
    float2 o = roAbs.xz - _FxCrest.xy;
    float a = dot(rd.xz, rd.xz);
    if (a < 1e-8) return false;
    float b = dot(o, rd.xz);
    float c = dot(o, o) - Ro * Ro;
    float h = b * b - a * c;
    if (h < 0.0) return false;
    h = sqrt(h);
    float to0 = (-b - h) / a, to1 = (-b + h) / a;
    float ci = dot(o, o) - Ri * Ri;
    float hi = b * b - a * ci;
    float ent = max(to0, 0.0), ext = to1;
    if (hi > 0.0)
    {
        hi = sqrt(hi);
        float ti0 = (-b - hi) / a, ti1 = (-b + hi) / a;
        if (ti0 > ent) ext = min(ext, ti0);             // the ray enters the inner disc: stop there
        else if (ti1 > ent) ent = max(ent, ti1);        // it starts inside the inner disc: begin where it leaves
    }
    // clip to the height of the spray (plume tops up to 2.8 x the lobed and toothed height, <= 1.38 A, + 60 m; the
    // strike up to _FxImpact.w)
    float yTop = max(crestOn ? 3.8 * A + 90.0 : 0.0, impactOn ? _FxImpact.w : 0.0);
    if (abs(rd.y) > 1e-6)
    {
        float ta = (0.0 - roAbs.y) / rd.y, tb = (yTop - roAbs.y) / rd.y;
        ent = max(ent, min(ta, tb)); ext = min(ext, max(ta, tb));
    }
    else if (roAbs.y > yTop) return false;
    t0 = ent; t1 = ext;
    return t1 > t0;
}
float FxStart(float3 roAbs, float3 rd)
{
    float s = 1e9, a0, a1;
    if (FxCone(roAbs, rd, a0, a1)) s = min(s, a0);
    if (FxRing(roAbs, rd, a0, a1)) s = min(s, a0);
    return s;
}

// One light model for dust and water. Sun (only a trace of the ash-light once the umbrella has it), the ash sky
// (warm and self-shadowed for dust; for droplets the horizon ring and the strip, neutral-cool and bright inside, so the
// water reads white against the dust and the column), the daylight strip as a low rim, and
// the fire below: once dark it is the key (the self-shadow taps aim at it, as the column's do), a warm side on the dust
// and, through water, a forward-scattered molten back light (_FxMisc.z). tauW sums the water opacities for the silver
// lining (FragMarchFx); nMax caps the steps of this stretch.
void FxSegment(float3 roAbs, float3 rd, float ta, float tb, float tScene, float jitter, float pixAng, float nMax, inout float3 C, inout float T, inout float tMean, inout float wMean, inout float tauW)
{
    tb = min(tb, tScene);
    if (tb <= ta || T < 0.02) return;
    int n = (int)clamp(ceil((tb - ta) / FX_MIN_STEP), 6.0, max(nMax, 6.0));
    float stepLen = (tb - ta) / n;
    float3 L = _CSunDir.xyz;
    float ventK = smoothstep(0.55, 0.85, _CState.x);                                   // the column's switch (upW in Plume March)
    float3 sunC = _CSunColor.rgb * ((1.0 / PI) * (1.0 - 0.75 * ventK));
    float3 fireC = _PlUp.rgb * _FxMisc.w;
    float cs = dot(rd, L);
    float hgD = FxHG(0.55, cs), hgW = FxHG(0.75, cs);                                  // droplets scatter forward harder
    float3 amb = (_CSkyZenith.rgb + _CSkyHorizon.rgb) * 0.5;
    // droplets: the bright horizon ring and the strip rather than the dark lid, neutral-cool once dark (water against
    // warm rock dust and the black column)
    float3 ring = lerp(amb, _CSkyHorizon.rgb, FX_WAT_RING) + _CStrip.rgb * FX_WAT_STRIP;
    float3 ambW = lerp(ring, dot(ring, FxLum) * float3(0.86, 0.95, 1.10), 0.7 * _CState.x);
    float3 rimS = _CStrip.rgb * (FX_STRIP_RIM * FxHG(0.6, dot(rd, normalize(float3(L.x, 0.03, L.z)))));
    // the sky-occlusion tap: up, tilted ~27 deg toward the eye (the ring behind the camera lights the faces turned to us;
    // straight up, a near-vertical curtain shadowed its own whole face)
    float3 dU = normalize(float3(-rd.x, 2.0, -rd.z)) * FX_WAT_AO_D;
    int lit = 0; float tauS = 0.0, tauU = 0.0;
    for (int i = 0; i < 64; i++)
    {
        if (i >= n || T < 0.02) break;
        float t = ta + (i + jitter) * stepLen;
        float3 p = roAbs + rd * t;
        gFoot = t * pixAng;
        float w, fillK;
        float sig = FxDensity(p, w, fillK);
        if (sig > 1e-6)
        {
            // the fire below as a light (an extended source: no singularity at the vent)
            float3 toV = _CVent.xyz + float3(0.0, FX_FIRE_Y, 0.0) - p;
            float dV2 = max(dot(toV, toV), 1.0);
            toV *= rsqrt(dV2);
            float3 Ev = fireC / (1.0 + dV2 / (FX_FIRE_R * FX_FIRE_R));
            float hgV = min(FxHG(lerp(0.5, 0.85, w), dot(rd, toV)), 6.0);
            float ventPh = lerp(FX_FIRE_ISO_D, FX_FIRE_ISO_W, w) + _FxMisc.z * hgV * lerp(0.4, 1.0, w);
            if (lit < 10 || (i & 3) == 0)
            {
                float3 Lk = normalize(lerp(L, toV, ventK) + float3(0.0, 1e-3, 0.0));
                // the key's two self-shadow taps and the sky-occlusion tap, the last for water only (the dust keeps its
                // look and its cost: +1 density per lit sample only while splash, spray or strike is on). One FxDensity
                // call site in a loop: every call site inlines all of it
                tauS = 0.0; tauU = 0.0;
                int nk = w > 0.05 ? 3 : 2;
                [loop] for (int k = 0; k < 3; k++)
                {
                    if (k >= nk) break;
                    float3 off = k == 0 ? Lk * 60.0 : (k == 1 ? Lk * 260.0 : dU);
                    float wS, fS;
                    float dk = FxDensity(p + off, wS, fS);
                    if (k == 2) tauU = dk * FX_WAT_AO_K;
                    else tauS += dk * (k == 0 ? 120.0 : 300.0);
                }
                lit++;
            }
            float trS = exp(-tauS);
            float trK = exp(-tauS * lerp(1.0, 0.45, w));                               // droplets: multiple scattering carries the key deeper
            float3 alb = lerp(float3(0.50, 0.45, 0.40), FX_WAT_ALB, w);
            // the strip rim only lights the thin edges (the key's self-shadow stands in for its occlusion); a water body
            // keeps most of its fill inside (multiple scattering), dust does not; billow tops keep the sky, creases lose it
            float aoU = lerp(FX_WAT_AO, 1.0, exp(-tauU));
            float3 ambT = lerp(amb * (0.30 + 0.70 * trS), ambW * (fillK + (1.0 - fillK) * trS) * aoU, w) + rimS * (0.10 + 0.90 * trS);
            float3 rad = alb * ((sunC * (0.35 + 1.8 * lerp(hgD, hgW, w)) + Ev * ventPh) * trK + ambT);
            float a = 1.0 - exp(-sig * stepLen);
            C += T * a * rad;
            tMean += T * a * t; wMean += T * a;
            tauW += a * w;
            T *= 1.0 - a;
        }
    }
}

float4 FragMarchFx(Varyings varyings) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(varyings);
    if (_FxMisc.x < 0.5) return float4(0, 0, 0, 1);
    float2 pix = varyings.positionCS.xy;
    // the same scene distance as passes 0 and 3 (farthest depth of the 2x2 block), so the bilateral upsample matches
    float3 rd;
    float tScene = PlBlockSceneT(pix, rd);
    float3 roAbs = _WorldSpaceCameraPos.xyz;
    float pixAng = 2.0 * _PlUv.z / (abs(UNITY_MATRIX_P._m11) * _ScreenSize.y);
    float jitter = CIgn(pix);
    float3 C = 0.0; float T = 1.0, tMean = 0.0, wMean = 0.0, tauW = 0.0;
    float a0, a1, b0, b1;
    bool hA = FxCone(roAbs, rd, a0, a1), hB = FxRing(roAbs, rd, b0, b1);
    float nAll = _FxMisc.y;
    // Up to three stretches, marched in order through ONE call of FxSegment (each call site inlines it with its four
    // FxDensity: separate calls made the shader ~3.6x larger)
    float3 sT0 = 0.0, sT1 = 0.0, sN = 0.0;
    int ns = 0;
    if (hA && hB && min(a1, b1) > max(a0, b0))
    {
        // The intervals overlap (the cylinder round the cone holds the village and the whole ring segment while the
        // splash runs): FxDensity has every volume at a point, so marching both counted the shared stretch twice, and
        // the spray and the strike lost half their density at once when the splash ended (~451 s). March the union
        // once: the shared stretch at full resolution, the parts before and after it sharing one budget, as before
        float s0 = max(a0, b0), s1 = min(a1, b1);
        float u0 = min(a0, b0), u1 = min(max(a1, b1), tScene);
        float lP = s0 - u0, lQ = max(u1 - s1, 0.0);
        float nP = nAll * lP / max(lP + lQ, 1e-3);
        sT0 = float3(u0, s0, s1); sT1 = float3(s0, s1, u1); sN = float3(nP, nAll, nAll - nP); ns = 3;
    }
    else if (hA && hB)
    {
        bool bFirst = b0 < a0;
        sT0 = bFirst ? float3(b0, a0, 0.0) : float3(a0, b0, 0.0);
        sT1 = bFirst ? float3(b1, a1, 0.0) : float3(a1, b1, 0.0);
        sN = float3(nAll, nAll, 0.0); ns = 2;
    }
    else if (hA) { sT0.x = a0; sT1.x = a1; sN.x = nAll; ns = 1; }
    else if (hB) { sT0.x = b0; sT1.x = b1; sN.x = nAll; ns = 1; }
    [loop] for (int si = 0; si < 3; si++)
    {
        if (si >= ns) break;
        float ta = si == 0 ? sT0.x : (si == 1 ? sT0.y : sT0.z);
        float tb = si == 0 ? sT1.x : (si == 1 ? sT1.y : sT1.z);
        float nS = si == 0 ? sN.x : (si == 1 ? sN.y : sN.z);
        FxSegment(roAbs, rd, ta, tb, tScene, jitter, pixAng, nS, C, T, tMean, wMean, tauW);
    }
    if (1.0 - T < 0.002) return float4(0, 0, 0, 1);
    // Silver lining: droplets scatter much of what they intercept into a narrow forward cone, so a veil passes on part of
    // whatever stands straight behind it: the horizon ring and the strip glow through torn edges, the dark lid and the
    // cliffs do not. It was the horizon colour added to any veil within ~10 deg of the horizon, which turned every
    // spray band against the lid or the headland into a frosted pane (455, 462, 395). Now it raises the volume's
    // transmission of the real background (the composite multiplies it in): single forward scattering ~ tau e^-tau
    // (tauW = the water's sum of opacities), weighted once more by T so thin veils and fringes pass the light while
    // thick cores stay a mass. Free: two sums of the march
    float alpha = 1.0 - min(T * (1.0 + FX_WAT_FWD * tauW * T), 1.0);
    float tm = tMean / max(wMean, 1e-5);
    // the height fog HDRP also has: toward its colour
    float ftH = exp(-FogTauH(roAbs, rd, tm));
    C = C * ftH + _CFogColor.rgb * (1.0 - ftH) * alpha;
    // the ash-laden air only dissolves the volume into whatever lies behind it (sky, strip or fogged scenery)
    float ftA = exp(-FogTauA(roAbs, rd, tm));
    C *= ftA; alpha *= ftA;
    return float4(C * GetCurrentExposureMultiplier(), 1.0 - alpha);
}

#endif
