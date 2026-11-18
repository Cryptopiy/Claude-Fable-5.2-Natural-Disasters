// Vertex displacement and shading of Cascade/Cone. Included once per pass after VaryingMesh.hlsl.
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/BuiltinUtilities.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/MaterialUtilities.hlsl"

void ApplyVertexModification(AttributesMesh input, float3 normalWS, inout float3 positionRWS, float3 timeParameters)
{
    float3 a = GetAbsolutePositionWS(positionRWS);
    a.y = ConeHeight(a.xz, 0.0);
    positionRWS = GetCameraRelativePositionWS(a);
}

#ifdef CONE_SHADING
// ---- tunables (radiometric: nits, or nits per unit albedo) ----
#define FLOW_NITS       16000.0     // incandescence of the hottest lava: the fed channel by the vent, the snouts, the pool's seams.
                                    // Emission goes as heat^2, so the crust (heat 0.1-0.3) is a deep red 1-9% of this
#define FLOW_CHANNEL_W  0.28        // width of a flow's fed channel over its half width (a thin line, not the flow's body)
#define FLOW_HOT_RUN    450.0       // the channel cools over this run from its vent (m): white-hot only near the summit
#define FLOW_SPILL      2400.0      // light of a flow on the rock beside it (per unit albedo, falls off over ~160 m): a soft red
                                    // halo on the slope (glow on the fume above the flow too), so a flow is not a slot in a black cone
#define FLOW_STARVE_DAZ float2(0.9, 1.4)   // flank flows closer than this to the slide azimuth (rad) lose their feed at the collapse
                                    // (all of them start in the summit the slide tears away; the farther ones keep draining)
#define FLOW_STARVE_MAX 1.0         // how fully they crust over (0.7 leaves the night cone of 395-476 busier)
#define CONE_PIX_ANGLE  0.0013      // one pixel of the film's ~70 degree lens at 1080p (rad): sizes the anti-alias fades below
#define VENT_LIGHT      0.30        // share of the vent glow (_CVentGlow, nits) that lights the rock right beside the vent
#define VENT_LIGHT_R    620.0       // falloff radius of the vent light (m): a broad, soft glow on the amphitheatre
#define VENT_LIGHT_TOP  (CRATER_RIM - CRATER_DEPTH + 90.0)  // the vent light never sits higher (m): inside the summit crater,
                                    // ~25 m over its floor (the shoulder lifts it ~65 m) and 30-130 m under its rim, so it lights
                                    // the crater and not the outer flank, wherever PlumeDirector puts the vent
#define BOLT_LIGHT      1.0         // lightning on the flank: _CBolt.w / (d^2 + 4e6) per unit albedo (bolt 3 km away: ~2300 * albedo nits)
#define SCAR_FLOW_DELAY 30.0        // the pool overflows down the scar floor this long after the collapse (s); ConeDirector.ScarFlowDelay
#define LAVA_FLOW_COUNT 6

// The daytime palette (linear albedo). The photos' own means are already warm (ash brown 0.175/0.133/0.083, basalt
// 0.043/0.034/0.026, orange gravel 0.158/0.068/0.027); the shaded flank the village sees takes ~1.5x more blue than red
// from the skylight and the haze adds more, so the old pull toward neutral grey left grey clay. Ash brown and rust are
// set warm enough to survive that (CPU preview of M1: summit R-B ~+20, upper flank ~+10 sRGB; the haze keeps the
// lower flank blue-grey); the lava rock stays a neutral black, so it reads cool against them.
#define CONE_ASH_BROWN    float3(0.175, 0.120, 0.068)
#define CONE_LAVA_BLACK   float3(0.060, 0.054, 0.048)
#define CONE_SCORIA_RED   float3(0.300, 0.130, 0.070)  // oxidised (haematite) scoria round the summit
#define CONE_SCORIA_BROWN float3(0.210, 0.115, 0.070)  // weathered scoria between its red patches
// The face the village sees is backlit (the sun is south-west, in front of the camera), so the skylight (R/B ~0.56) lights
// most of it and cancelled the warm albedo. The sunlit caldera floor, cliffs and sea add a warm bounce: by day the fill is
// pulled this far toward a warm neutral of the same luminance (fill R/B ~0.56 -> ~0.91, still slightly cool). Calibrated on
// the v2 -> v4 stills of 185: summit R-B about +7 -> +14, upper flank 0 -> +4; the day grade's cool shadow tint (SMH) and
// the haze hold the rest. Fades out with the darkness (_CState.x) as the ash takes the sun.
#define CONE_BOUNCE       0.5
#define CONE_BOUNCE_TINT  float3(1.17, 0.975, 0.74)   // unit luminance

// ConeDirector.LavaFlows (C#): x azimuth (rad, 0 = +x, toward the village), y eruption-clock start (s), z front speed
// (m/s). Keep in sync. The last runs down the scar floor on the collapse clock (start = collapse + SCAR_FLOW_DELAY;
// its y is that moment on the nominal eruption clock) along the slide azimuth.
static const float3 kConeLavaFlows[LAVA_FLOW_COUNT] =
{
    float3(0.42, 40.0, 10.0), float3(0.85, 58.0, 8.0), float3(-1.25, 82.0, 10.0),
    float3(1.30, 120.0, 7.0), float3(0.62, 150.0, 9.0), float3(-0.349066, 146.0, 14.0)
};

// Biplanar-style photo texture lookup: top-down and the two side projections weighted by the normal.
struct Tri { float3 w; float2 xz, xy, zy; };
Tri MakeTri(float3 p, float3 N, float scale)
{
    Tri t;
    float3 w = pow(abs(N), 4.0);
    t.w = w / (w.x + w.y + w.z);
    t.xz = p.xz / scale; t.xy = p.xy / scale; t.zy = p.zy / scale;
    return t;
}
float3 TriAlbedo(TEXTURE2D_PARAM(tex, smp), Tri t)
{
    return SAMPLE_TEXTURE2D(tex, smp, t.xz).rgb * t.w.y + SAMPLE_TEXTURE2D(tex, smp, t.xy).rgb * t.w.z + SAMPLE_TEXTURE2D(tex, smp, t.zy).rgb * t.w.x;
}
// tangent-space normal maps as world-space slope perturbations (whiteout-style, good enough at these distances)
float3 TriNormal(TEXTURE2D_PARAM(tex, smp), Tri t, float3 N, float strength)
{
    float3 a = UnpackNormal(SAMPLE_TEXTURE2D(tex, smp, t.xz));
    float3 b = UnpackNormal(SAMPLE_TEXTURE2D(tex, smp, t.xy));
    float3 c = UnpackNormal(SAMPLE_TEXTURE2D(tex, smp, t.zy));
    float3 nTop = float3(a.x, 0.0, a.y);
    float3 nZ = float3(b.x, b.y, 0.0);
    float3 nX = float3(0.0, c.y, c.x);
    return normalize(N + strength * (nTop * t.w.y + nZ * t.w.z + nX * t.w.x));
}
float SrgbToLin(float x) { return x * x; }

// soft shadow of the cone on itself: march toward the sun over the height function
float ConeSunShadow(float3 p, float3 L)
{
    if (L.y < 0.02) return 0.0;
    float sh = 1.0, t = 25.0;
    [unroll] for (int i = 0; i < 12; i++)
    {
        float3 q = p + L * t;
        float h = ConeHeight(q.xz, 0.0);
        sh = min(sh, saturate(1.0 + (q.y - h) / (0.06 * t + 6.0)));
        t *= 1.55;
        if (q.y > CONE_H + 200.0) break;
    }
    return sh * sh * (3.0 - 2.0 * sh);
}

// Soft shadow from the vent, a point light low in the amphitheatre: no early-out for a low light (the walls stand above
// it); 8 taps spread over the whole path to the light, denser near the receiver; a wide penumbra (the source is large).
float ConeVentShadow(float3 p, float3 lp)
{
    float3 dv = lp - p;
    float len = length(dv);
    float sh = 1.0;
    [unroll] for (int i = 0; i < 8; i++)
    {
        float f = (i + 0.5) * (i + 0.5) / 64.0;
        float3 q = p + dv * f;
        float h = ConeHeight(q.xz, 0.0);
        sh = min(sh, saturate(1.0 + (q.y - h) / (0.12 * f * len + 8.0)));
    }
    return sh * sh * (3.0 - 2.0 * sh);
}

// Incandescence by temperature 0..1: deep red cracks, orange, yellow where it is hottest.
float3 ConeLavaColor(float g)
{
    float3 c = lerp(float3(1.0, 0.07, 0.008), _PalLava.rgb, saturate(g * 2.0));
    return lerp(c, float3(1.0, 0.62, 0.28), smoothstep(0.65, 1.0, g));
}

// Lava flow i at polar position (phi, rho) of the cone; wander = ConeWander(phi, rho). cover: how much of the flow covers
// the point (0..1); heat 0..1 (the fed channel and the front); age: 0 at the front .. 1 far behind it; side: lateral
// position over the half width (signed, + toward increasing phi); spill: its glow on the rock beside it; runT: seconds
// since it left its vent.
void ConeLavaFlow(int i, float phi, float rho, float wander, float te, float tc,
                  out float cover, out float heat, out float age, out float side, out float spill, out float runT)
{
    float3 f = kConeLavaFlows[i];
    bool scarFlow = i == LAVA_FLOW_COUNT - 1;
    float k = (float)i;
    float az = scarFlow ? _ConeSlide.w : f.x;
    runT = scarFlow ? tc - SCAR_FLOW_DELAY : te - f.y;
    cover = 0.0; heat = 0.0; age = 1.0; side = 2.0; spill = 0.0;
    float front = 300.0 + f.z * max(runT, 0.0);
    // a flank flow follows the grain of the slope (the gullies' wander), so it runs down between the ribs instead of
    // across them (the ribs hid every other stretch of it from the village). It is pinned to its azimuth at the shore,
    // where ParticleDirector puts its laze. The scar flow keeps to the axis of its chute.
    float lane = scarFlow ? 0.0 : wander - ConeWander(az, CONE_R);
    float dphi = phi - az + lane;
    dphi -= 6.2831853 * floor(dphi / 6.2831853 + 0.5);
    float halfW = (scarFlow ? 34.0 + 0.022 * rho : 20.0 + 0.017 * rho) * (0.85 + 0.3 * CHash11(k + 3.1));
    float lat0 = dphi * rho;
    if (abs(lat0) > halfW + 420.0) return;                                                     // beyond its spill light
    // one noise fetch along the run: r, g aperiodic meanders inside the lane (~200 m and ~100 m, not fixed sines);
    // g also the roofed reaches of the channel (100-300 m: shorter ones read as a dashed road line), a its flicker (~50 m)
    float4 fn = CNoise2All(float2(rho / 800.0, 0.37 * k + 0.05));
    float wig = 60.0 * (fn.r - 0.5) + 22.0 * (fn.g - 0.5);
    float lat = lat0 + wig * (scarFlow ? 0.45 : 1.0);
    side = lat / halfW;
    float x = abs(side);
    float behind = front - rho - 35.0 * x * x;                                                // a rounded snout
    float alive = smoothstep(-5.0, 55.0, behind) * smoothstep(260.0, 340.0, rho) * step(0.0, runT);
    cover = (1.0 - smoothstep(0.6, 1.0, x)) * alive;
    // The collapse tears the summit away, and every flank flow starts there: the ones beside the breach lose their
    // feed. Their channels drain and crust over within ~20 s (no glowing teeth beside the scar at 334/345), their snouts
    // dim over ~50 s and their crust ages (the cracks dull to deep red) over ~2 min; the flows farther round the cone keep
    // draining the edifice. The scar flow and the pool become the hottest fire on the cone (a hierarchy at 395).
    float starve = 0.0;
    if (!scarFlow)
    {
        float dAz = abs(az - _ConeSlide.w);
        dAz = min(dAz, 6.2831853 - dAz);
        starve = FLOW_STARVE_MAX * saturate(_ConeSlide.z) * (1.0 - smoothstep(FLOW_STARVE_DAZ.x, FLOW_STARVE_DAZ.y, dAz));
    }
    age = saturate(behind / 1400.0 + 0.6 * starve * smoothstep(10.0, 120.0, tc));
    float frontHot = exp(-max(behind, 0.0) / 90.0) * (1.0 - smoothstep(0.7, 1.0, x)) * (1.0 - 0.85 * starve * smoothstep(6.0, 50.0, tc));
    // the fed channel: a thin line, white-hot only near its vent, cooling to a dull red down the run (0.06, not 0.2: a
    // long reach glowing at 0.2 read as a slot or a flame wall) and crusting with age; roofed reaches and skylights break
    // it up (a'a at night: a dark flow with a hot thread and a hot snout). The scar flow's thread is narrower still: its
    // body is wide (~130 m), so a 0.28 channel lit a broad band
    float run = max(rho - 300.0, 0.0);
    float channel = saturate(1.0 - x / (scarFlow ? 0.16 : FLOW_CHANNEL_W)) * (0.06 + 0.94 * exp(-run / FLOW_HOT_RUN)) * (1.0 - 0.5 * age);
    channel *= smoothstep(0.36, 0.50, fn.g) * (0.75 + 0.25 * smoothstep(0.35, 0.65, fn.a));
    channel *= 1.0 - 0.92 * starve * smoothstep(3.0, 22.0, tc);
    heat = saturate(channel + frontHot);
    float off = max(abs(lat) - halfW, 0.0);
    spill = exp(-off / 160.0) * (1.0 - smoothstep(300.0, 390.0, off)) * alive * (0.45 + 0.55 * exp(-max(behind, 0.0) / 500.0))
          * (0.4 + 0.6 * exp(-run / 900.0))                                                   // a cooler flow lights less
          * (1.0 - 0.6 * starve * smoothstep(5.0, 60.0, tc));                                  // and a crusted one less again
}

float3 ConeShade(float3 posAbs, float3 V)
{
    float2 xz = posAbs.xz;
    float eps = 9.0;
    float h0 = ConeHeight(xz, 1.0);
    float hx = ConeHeight(xz + float2(eps, 0.0), 1.0) - ConeHeight(xz - float2(eps, 0.0), 1.0);
    float hz = ConeHeight(xz + float2(0.0, eps), 1.0) - ConeHeight(xz - float2(0.0, eps), 1.0);
    float3 Ng = normalize(float3(-hx, 2.0 * eps, -hz));
    float slope = 1.0 - Ng.y;

    float2 d = xz - _CCone.xz;
    float rho = length(d);
    float phiS = atan2(d.y, d.x);
    float hk = saturate(h0 / CONE_H);
    float4 n4 = CNoise2All(xz / 310.0);
    float4 n9 = CNoise2All(xz / 900.0 + 0.19);
    float nf = CNoise2(xz / 150.0);                                                         // ~37 m cells (was 9 m: pixel speckle at 5 km)
    // cavity: how far below its surroundings this point lies (gully floors dark, ribs lit)
    float hC = ConeHeight(xz, 0.0);
    float hAvg = (ConeHeight(xz + float2(60.0, 0.0), 0.0) + ConeHeight(xz - float2(60.0, 0.0), 0.0) + ConeHeight(xz + float2(0.0, 60.0), 0.0) + ConeHeight(xz - float2(0.0, 60.0), 0.0)) * 0.25;
    float cav = saturate((hAvg - hC) / 14.0);
    float rib = saturate((hC - hAvg) / 14.0);

    // photo rock at three scales. From the village (2.5-7 km) a photo's pebbles become 10-50 m blobs (the 'wood chip'
    // flank), so the 160 m tile is gone by ~3.3 km and the two coarse tiles keep only a third of their contrast around
    // the photo's mean colour: what reads at that range is built below (gullies, fall-line streaks, scoria, old flows)
    float dist = length(posAbs - _WorldSpaceCameraPos.xyz);
    Tri t1 = MakeTri(posAbs, Ng, 160.0);
    Tri t2 = MakeTri(posAbs, Ng, 700.0);
    Tri t3 = MakeTri(posAbs, Ng, 2600.0);
    float mixK = saturate((dist - 1500.0) / 3500.0);                                       // the village is 2.5-7 km from the flank
    float w1 = 0.55 * (1.0 - smoothstep(0.0, 0.5, mixK)), w3 = 0.40 * mixK, w2 = 1.0 - w1 - w3;
    float keepC = lerp(1.0, 0.35, mixK);                                                   // photo contrast kept
    // the photos' mean colours (their last mip): each photo keeps its own detail around a mean that is moved toward the
    // volcanic palette (see CONE_ASH_BROWN)
    float3 mA = SAMPLE_TEXTURE2D_LOD(_RockA, sampler_RockA, float2(0.5, 0.5), 12.0).rgb;
    float3 mB = SAMPLE_TEXTURE2D_LOD(_RockB, sampler_RockA, float2(0.5, 0.5), 12.0).rgb;
    float3 mC = SAMPLE_TEXTURE2D_LOD(_RockC, sampler_RockA, float2(0.5, 0.5), 12.0).rgb;
    float3 aA = TriAlbedo(TEXTURE2D_ARGS(_RockA, sampler_RockA), t1) * w1 + TriAlbedo(TEXTURE2D_ARGS(_RockA, sampler_RockA), t2) * w2
              + TriAlbedo(TEXTURE2D_ARGS(_RockA, sampler_RockA), t3) * w3;
    float3 aB = TriAlbedo(TEXTURE2D_ARGS(_RockB, sampler_RockA), t1) * w1 + TriAlbedo(TEXTURE2D_ARGS(_RockB, sampler_RockA), t2) * w2
              + TriAlbedo(TEXTURE2D_ARGS(_RockB, sampler_RockA), t3) * w3;
    float3 aC = TriAlbedo(TEXTURE2D_ARGS(_RockC, sampler_RockA), t1);
    aA = lerp(mA, CONE_ASH_BROWN, 0.6) + (aA - mA) * keepC;
    aB = lerp(mB, CONE_LAVA_BLACK, 0.4) + (aB - mB) * keepC;
    aC = mC + (aC - mC) * keepC * (w1 / 0.55);
    // (the photos are imported as sRGB textures: the samples are already linear)

    // zones: ash scree on the smooth flanks, black lava rock on ribs and steep faces, oxidised scoria over the upper
    // flank (~110-225 m patches that close up toward the summit: a warm summit breaking into patches, not a band). Its
    // albedo is its own (the gravel photo's mean is only its colour at range); the photo grain shows up close only
    float lavaW = saturate(rib * 1.6 + smoothstep(0.42, 0.62, slope) + (n4.r - 0.55) * 1.2);
    float redW = smoothstep(0.36, 0.70, hk + (n9.g - 0.5) * 0.5) * 0.92 * smoothstep(0.28, 0.52, n9.r + 0.3 * (hk - 0.6));
    redW *= 1.0 - 0.5 * _ConeGlow.z;                                                        // the falling ash buries it, steep faces too (no red rim under the vent light)
    float3 scoria = lerp(CONE_SCORIA_RED, CONE_SCORIA_BROWN, n4.g) * (0.85 + 0.3 * n4.b) + (aC - mC);
    float3 alb = lerp(aA * 0.95, aB * 1.5, lavaW);
    alb = lerp(alb, scoria, redW);
    // the relief in the albedo too: fresh dark scree in the gully floors, weathered paler rib crests
    alb = lerp(alb, alb * 0.62, cav * 0.7);
    alb = lerp(alb, alb * 1.25 + 0.01, rib * 0.5);
    alb *= 0.88 + 0.24 * nf;
    // scree streaks down the fall line, on the gullies' grain (~50 m wide, ~650 m long; helical lookups as in
    // CascadeCone.hlsl, so nothing repeats round the cone)
    float wanderS = ConeWander(phiS, rho);
    float helS = phiS * (1.0 / 6.2831853);
    float stk = CNoise2All(float2((phiS + wanderS) * (9.0 / 6.2831853), rho / 10400.0 + helS)).a;
    alb *= 0.70 + 0.60 * saturate((stk - 0.5) * 1.8 + 0.5);
    // old lava flows: dark tongues 150-300 m wide down the lower flanks, on the same grain
    float flows = smoothstep(0.50, 0.58, CNoise2All(float2((phiS + wanderS) * (9.0 / 6.2831853), rho / 5200.0 + 2.0 * helS)).b) * smoothstep(500.0, 1200.0, rho);
    alb = lerp(alb, aB * 1.1, flows);
    // sulphur-stained fumaroles on the crater rim (patches ~60 m, crest and inner face: not a pale band round the
    // summit); wet black rock at the waterline
    alb = lerp(alb, float3(0.55, 0.50, 0.32), 0.35 * smoothstep(105.0, 135.0, rho) * (1.0 - smoothstep(158.0, 185.0, rho)) * smoothstep(0.45, 0.70, CNoise2(xz / 240.0)));
    alb *= lerp(0.45, 1.0, smoothstep(0.0, 12.0, h0));

    // the scar: fresh, bedded, grey and rusty rock on the walls, dark rubble on the floor; the moving mass: broken and
    // darker; the crown fracture opens first
    float s = _ConeSlide.x;
    float2 uvS = ConeSlideUV(d);
    float3 sc = ConeScar(uvS.x, uvS.y);
    float scar = saturate(sc.x / 240.0);
    float debris = ConeDebrisMask(xz);
    float started = saturate(_ConeSlide.z);
    float wallK = smoothstep(0.15, 0.40, slope);                                            // beds and chutes show on the walls only
    // aperiodic beds (~45 m) that pinch and swell along the wall (a pure sine combed it into even rows), warped gently
    // (~25 m over ~80 m, not jagged) and dipping 2-4 degrees round the cone (which also keeps the two periods apart)
    float bed = h0 / 180.0 + 0.35 * (n4.r - 0.5) + phiS * (1.0 / 6.2831853);
    float strata = saturate((CNoise2(float2(bed, phiS * (2.0 / 6.2831853) + 0.17)) - 0.5) * 2.4 + 0.5);
    // (darker once the night falls: under the vent light the pale walls round the pool read as a burning bush at 540)
    float3 fresh = lerp(float3(0.26, 0.225, 0.18), float3(0.33, 0.21, 0.13), strata * 0.7 * wallK) * lerp(0.8, 1.1, nf)
                 * lerp(1.0, 0.72, _CState.z);
    // talus chutes down the fall line: a coordinate along the horseshoe (arc around the head, then down the side walls)
    // is constant down every wall, so they run straight down it (~40 m wide, ~100 m long)
    float along = uvS.x > SCAR_UC ? sign(uvS.y) * (SCAR_R0 * 1.5707963 + uvS.x - SCAR_UC) : SCAR_R0 * atan2(uvS.y, SCAR_UC - uvS.x);
    float chute = saturate((CNoise2(float2(along / 170.0, h0 / 420.0)) - 0.5) * 2.0 + 0.5);
    fresh *= lerp(1.0, 0.7 + 0.6 * chute, wallK);
    fresh = lerp(fresh, float3(0.085, 0.077, 0.072), smoothstep(0.55, 0.75, strata) * 0.8 * wallK);   // dark lava beds (~20%)
    alb = lerp(alb, fresh, scar * (1.0 - debris) * started);
    float3 rubble = aB * (0.6 + 0.5 * n4.b) + float3(0.012, 0.008, 0.005);
    alb = lerp(alb, rubble, ConeDepositMask(uvS, sc.y) * (1.0 - debris) * 0.85);
    alb = lerp(alb, aB * 0.9 + 0.01, debris * saturate(s / 200.0));
    float crack = smoothstep(0.05, 0.0, abs(sc.y - 0.9)) * saturate(sc.x / 10.0) * (1.0 - smoothstep(1500.0, 2100.0, uvS.x))
                * started * (1.0 - saturate(s / 500.0));                                   // the horseshoe outline, before it moves
    alb *= 1.0 - 0.85 * crack;
    float floorK = 1.0 - smoothstep(0.5, 0.8, sc.y);                                       // the amphitheatre floor, not its walls
    // flank torn away by the slide: its floor and its walls right up to the rim crest (sc.z is the ragged wall mask; the
    // depth alone left the upper walls, where the cut is thin, painted with the torn flows as glowing gashes)
    float scarCut = max(saturate(sc.x / 50.0), smoothstep(0.0, 0.08, sc.z)) * saturate(s / 150.0);

    // falling ash turns everything grey
    alb = lerp(alb, _PalAshGrey.rgb * 0.55, _ConeGlow.z * saturate(Ng.y * 1.5 - 0.3));
    // broad fields of lighter and darker, browner and greyer rock (~360-725 m): no two parts of the cone alike; the hue
    // swing is what tells brown ash from grey ash and black lava fields at 5 km
    float4 mac = CNoise2All(xz / 2900.0 + 0.53);
    alb *= (0.66 + 0.68 * smoothstep(0.3, 0.7, mac.r)) * lerp(float3(1.12, 0.98, 0.84), float3(0.90, 0.97, 1.06), smoothstep(0.35, 0.65, mac.g));

    // detail normals from the photos, fading with distance
    float nK = saturate(1.2 - dist / 2500.0) * 0.5 + 0.06;
    float3 N = TriNormal(TEXTURE2D_ARGS(_RockAN, sampler_RockA), t1, Ng, nK * (1.0 - lavaW * 0.5));
    N = normalize(lerp(N, TriNormal(TEXTURE2D_ARGS(_RockBN, sampler_RockA), t1, Ng, nK * 1.2), lavaW));

    float3 lava = float3(0.0, 0.0, 0.0);
    // metres of ground under one pixel, the long way (the pool and the scar floor are seen at ~10-15 degrees from the
    // village): crack and seam patterns finer than a few pixels fade to their energy mean instead of glittering
    float footprint = dist * CONE_PIX_ANGLE / max(abs(dot(Ng, V)), 0.15);

    // ---- lava flows down the village-facing flank (and, after the collapse, down the scar floor) ----
    float fCover = 0.0, fHeat = 0.0, fAge = 1.0, fSide = 0.0, fSpill = 0.0, fClock = 0.0, fSpeed = 0.0, fSeed = 0.0;
    if (_ConeGlow.x > 0.001 && rho > 250.0 && rho < CONE_R + 250.0)
    {
        float wet = saturate((h0 + 4.0) / 6.0);                                             // quenched black under the sea
        [unroll] for (int i = 0; i < LAVA_FLOW_COUNT; i++)
        {
            float cvI, htI, agI, sdI, spI, ckI;
            ConeLavaFlow(i, phiS, rho, wanderS, _CClocksA.x, _CClocksA.y, cvI, htI, agI, sdI, spI, ckI);
            // the collapse tears away whatever of a flank flow lay in the scar; the scar flow runs on the new floor
            float keep = ((i == LAVA_FLOW_COUNT - 1) ? 1.0 - debris : 1.0 - scarCut) * wet;
            cvI *= keep;
            htI *= keep * keep;                                                             // a cut end crusts over, no glowing teeth
            fSpill = max(fSpill, spI * keep);
            if (cvI > fCover) { fCover = cvI; fHeat = htI; fAge = agI; fSide = sdI; fClock = ckI; fSpeed = kConeLavaFlows[i].z; fSeed = (float)i; }
        }
    }
    if (fCover > 0.001)
    {
        // a crust of dark plates with glowing cracks, carried downhill (pure function of the clocks)
        float2 dirR = d / max(rho, 1.0);
        float2 cp = (xz - dirR * (0.6 * fSpeed * max(fClock, 0.0))) / 240.0 + fSeed * float2(0.137, 0.291);
        float4 cn = CNoise2All(cp);
        // narrow creases: the noise sits near 0.5 with a small spread, so the old bands (0.80/0.84) covered ~65% of the
        // flow and lit it as a sheet (the 'flame wall'); these cover ~22% (mean 0.18, rms 0.36)
        float cracks = max(smoothstep(0.94, 0.995, 1.0 - abs(2.0 * cn.r - 1.0)), 0.7 * smoothstep(0.93, 0.99, 1.0 - abs(2.0 * cn.g - 1.0)));
        cracks = lerp(cracks, 0.36, saturate((footprint - 5.0) / 25.0));                    // sub-pixel: the network's rms (emission ~ glow^2), no shimmer
        float levee = smoothstep(0.55, 0.95, abs(fSide));
        // a dark crust whose red cracks dim with age; the levees are cold (no flame fringe); only the channel and the
        // snout run hot. Emission ~ glow^2: crust 1-9% of FLOW_NITS (deep red), channel and snout up to 100% (yellow)
        float glow = fCover * saturate(cracks * (0.55 - 0.35 * fAge) * (1.0 - levee) + 0.04 * (1.0 - fAge) + fHeat);
        lava += ConeLavaColor(glow) * (glow * glow) * (FLOW_NITS * _ConeGlow.x);
        // fresh lava is black glass, darker still on the levees; its body is convex, so its sides catch the light
        alb = lerp(alb, float3(0.05, 0.046, 0.043) * (1.0 - 0.35 * levee), fCover);
        N = normalize(N + float3(-dirR.y, 0.0, dirR.x) * (0.6 * fSide * fCover));
    }

    // ---- a lava lake on the floor under the vent: a ragged disc (~215 m) with a crusted, cracked skin (no straight
    // lines). The column's foot (PlumePass: ~210 m, its billows to ~370 m) stands on its middle, so from the village (floor
    // seen at ~10-20 degrees) the lake reads as the hot shore ring around that foot: at ~185 m (130-240) it hid under the
    // foot and 395 lost its focal fire. Its shore wanders gently (~60 m cells, +-45 m): 27 m cells of +-75 m made flame tongues ----
    float rv = length(xz - _CVent.xz);
    float poolEdge = rv + 90.0 * (CNoise2(xz / 240.0 + 0.61) - 0.5);
    float pool = (1.0 - smoothstep(150.0, 280.0, poolEdge)) * started * (1.0 - debris) * floorK * saturate(Ng.y * 2.0)
               * smoothstep(400.0, 1350.0, s);                                              // wells up as the mass clears the vent
    if (pool > 0.001)
    {
        // a dark crust of ~75 m plates with thin white-hot seams (~15% of the skin: the old 27 m plates with seams over a
        // third of it glittered at the 20-30 m footprint of the grazing view), hot under the vent and along the shore
        float4 pn = CNoise2All(xz / 300.0 + float2(0.0, _CTime * 0.0006));
        float pc = smoothstep(0.93, 0.99, 1.0 - abs(2.0 * pn.r - 1.0));
        pc = lerp(pc, 0.35, saturate((footprint - 8.0) / 40.0));                           // sub-pixel seams: their rms
        float pg = saturate(0.12 + 0.62 * pc + 0.55 * exp(-rv / 100.0) + 0.3 * exp(-abs(poolEdge - 205.0) / 35.0));   // the hot shore
        lava += ConeLavaColor(pg) * (pg * pg) * pool * (FLOW_NITS * (0.45 + 0.55 * _ConeGlow.x));
        alb = lerp(alb, float3(0.05, 0.046, 0.043), pool);
    }

    // ---- the crater: a broken incandescent ring on the inner face of the rim (spatter-fed patches that drift) and a dim
    // glow on the bowl floor. Inside only: from the village the near rim is the skyline and hides all of it (a ring on
    // the crest or the outer slope read as an orange collar painted on the cone); the fire shows in the jet and steam ----
    float rc = (rho - (CRATER_R - 14.0)) / 12.0;
    float craterGlow = (exp(-rc * rc) + 0.25 * (1.0 - smoothstep(30.0, CRATER_R - 10.0, rho))) * _ConeGlow.y;
    craterGlow *= 0.15 + smoothstep(0.35, 0.75, CNoise2(float2(phiS * (6.0 / 6.2831853), 0.37 + _CTime * 0.003 + 2.0 * helS)));
    craterGlow *= 1.0 - saturate(scar * started * 1.5);                                   // the rim is gone where the flank went
    lava += _PalLava.rgb * craterGlow * 7000.0;

    // ---- light ----
    float3 L = _CSunDir.xyz;
    float ndl = saturate(dot(N, L));
    float sh = 0.0;
    if (ndl > 0.0) sh = ConeSunShadow(float3(xz.x, h0 + 3.0, xz.y), L);                  // most of what the village sees faces away from the sun
    float ao = (1.0 - 0.55 * cav) * (0.85 + 0.15 * rib);
    // skylight is brighter toward the sun's side of the sky: the shaded face keeps its relief
    float3 Lh = normalize(float3(L.x, 0.0, L.z) + 1e-4);
    float skySide = saturate(dot(N, Lh) * 0.5 + 0.5);
    float3 amb = CAmbient(N) * (0.55 + 0.75 * skySide) + _CSkyHorizon.rgb * 0.18 * saturate(N.y);
    // the warm ground bounce on the shaded face (see CONE_BOUNCE): day only, luminance kept
    float ambL = dot(amb, float3(0.2126, 0.7152, 0.0722));
    amb = lerp(amb, ambL * CONE_BOUNCE_TINT, CONE_BOUNCE * saturate(1.0 - _CState.x));
    float3 col = alb * (_CSunColor.rgb * ndl * sh * (1.0 / PI) + amb * 0.85 * ao);

    // the vent lights its amphitheatre (before the collapse: the inside of the crater), shadowed by the walls and rim
    float3 lp = _CVent.xyz + float3(0.0, 120.0, 0.0);
    lp.y = min(lp.y, VENT_LIGHT_TOP);                                                      // no orange collar on the outer flank
    float3 lv = lp - float3(xz.x, h0, xz.y);
    float lv2 = dot(lv, lv);
    float reachR = lerp(1000.0, 1600.0, started);
    bool rimHides = s < 1.0 && rho > CRATER_R + 40.0;                                       // whole crater: its rim shades the outer flank (skip 8 lookups)
    if (!rimHides && lv2 < reachR * reachR && dot(_CVentGlow.rgb, float3(1.0, 1.0, 1.0)) > 0.0)
    {
        float wrapV = saturate(dot(N, lv * rsqrt(lv2)) * 0.8 + 0.2);
        if (wrapV > 0.0)
        {
            float shV = ConeVentShadow(float3(xz.x, max(h0, hC) + 4.0, xz.y), lp);
            float reach = 1.0 - smoothstep(0.6, 1.0, lv2 / (reachR * reachR));
            col += alb * _CVentGlow.rgb * (VENT_LIGHT / (1.0 + lv2 / (VENT_LIGHT_R * VENT_LIGHT_R))) * wrapV * shV * reach;
        }
    }

    // lightning: the current bolt lights the flank for its few frames (unshadowed: it is kilometres up)
    if (_CBolt.w > 0.0)
    {
        float3 tb = _CBolt.xyz - posAbs;
        float tb2 = dot(tb, tb);
        col += alb * float3(0.75, 0.82, 1.0) * (_CBolt.w * BOLT_LIGHT / (tb2 + 4.0e6)) * saturate(dot(N, tb * rsqrt(tb2)));
    }

    // the glow of the lava lights the rock around it
    col += alb * _PalLava.rgb * (craterGlow * 900.0 + fSpill * FLOW_SPILL * _ConeGlow.x);
    return col + lava;
}
#endif

void GetSurfaceAndBuiltinData(FragInputs input, float3 V, inout PositionInputs posInput, out SurfaceData surfaceData, out BuiltinData builtinData RAY_TRACING_OPTIONAL_PARAMETERS)
{
    ZERO_INITIALIZE(SurfaceData, surfaceData);
    ZERO_BUILTIN_INITIALIZE(builtinData);
    builtinData.opacity = 1.0;
#ifdef CONE_SHADING
    builtinData.emissiveColor = ConeShade(GetAbsolutePositionWS(input.positionRWS), V);
#endif
}
