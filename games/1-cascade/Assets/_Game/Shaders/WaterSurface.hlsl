// Vertex displacement and shading of Cascade/Water. Included once per pass after VaryingMesh.hlsl.
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/BuiltinUtilities.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/MaterialUtilities.hlsl"

void ApplyVertexModification(AttributesMesh input, float3 normalWS, inout float3 positionRWS, float3 timeParameters)
{
    float3 a = GetAbsolutePositionWS(positionRWS);
    a.y += SeaHeight(a.xz);
    positionRWS = GetCameraRelativePositionWS(a);
}

#ifdef WATER_SHADING
TEXTURE2D(_COcean); SAMPLER(sampler_COcean);      // FFT ocean tile: RG slopes, B whitecaps, A height (Tools/gen_textures.py)
TEXTURE2D(_CFoam); SAMPLER(sampler_CFoam);        // R foam height (threshold by coverage), G bubbles, B blotches, A streaks (constant along v)
float4 _PlFog;                                     // x 1/mean free path (EnvDirector)
TEXTURE2D(_CSkyline);                            // R albedo, G distance (km), A sine of the skyline elevation, per azimuth from the bay
float4 _SeaFlow;                                   // x seconds since the strike, yz mean flow of the flood (m/s, xz; + inland), w on (WaterDirector)
float4 _SeaFlow2;                                  // xy that flow integrated over time (m): rafts and streaks ride the water without stretching
#define OCEAN_SR 1.6
// tunables
#define WS_LIP_GAIN 0.35        // vent glow through the thin crest lip (fraction of the vent radiance)
#define WS_BODY_NEUTRAL 0.5     // share of the neutral-cool irradiance the water body takes under the ash (foam takes all of it)
#define WS_BOLT_GLINT 0.25      // specular gain of a lightning stroke (about 1/(4 N.V) for a facing facet)
#define WS_BOLT_DIFF 1.0        // diffuse gain of the stroke on foam, spray and the body
#define WS_COL_GLOW 0.8         // the column foot mirrored in the puddles: fraction of the vent glow radiance (warm against the grey bed)
#define WS_FLOW_EDDY 1.0        // eddy speed per unit noise gradient (m/s; the fbm gradient is ~2-4 per tile)
#define WS_WALL_SSS 0.6         // horizon light carried through the upper wall toward the crest (fraction of the horizon radiance)
#define WS_WALL_FD 6.0          // m: finite-difference step of the large normal on the wall (smooths <15 m bumps; ripples add detail)
#define WS_LIP 1.0              // coverage of the white breaking lip just ahead of the peak (at range; x0.82 close up: patches)
#define WS_CREST 0.82           // coverage of the crest whitewater in a breaking section (x SeaBreak: ~0.25 where glassy)
#define WS_BEHIND 0.50          // coverage of the foam in the wake behind the crest (lace and patches)
#define WS_LIP_SILVER 0.25      // horizon light through the thin white top of the crest where it stands against the sky
#define WS_SURF 0.8             // coverage of the surf ring where the sea meets rock (cliffs, cone, caldera rim)
#define WS_SCUM 0.7             // coverage of the scum line at the retreating edge during the drawdown
#define WS_FLOOD_COV float2(0.28, 0.30)   // flood foam coverage, times the churn: base + rafts (lace and streaks, no slabs)
#define WS_SILT_ALB float3(0.20, 0.155, 0.11)   // albedo of the flood water: suspended ash and soil scatter (grey-brown, lum ~0.16)
#define WS_FOAM_BACK 0.75       // horizon light thin crest foam carries from the water behind it (the water body takes 1.8)
#define WS_LACE_LIFT float2(1.30, 1.10)   // foam over wave / flood water is at least this x the water under it: thin lace, thick pile
#define WS_FAR_SECT float2(1200.0, 2400.0) // m: from here the lip and the foot follow the breaking sections (395: front 2.3-2.8 km; 425: 1.4 km)
#define WS_LIP_GLASSY 0.30      // lip coverage on a glassy section at range (under ~0.3 the area threshold leaves no foam)

// One octave of the ocean: two lookups with crossing drift directions (no rigid scrolling), rotated per octave.
float2 OceanOctave(float2 xz, float tileM, float ang, float t, out float h, out float cap)
{
    float c = cos(ang), s = sin(ang);
    float2x2 R = float2x2(c, -s, s, c);
    float2 uv = mul(R, xz) / tileM;
    float speed = sqrt(9.81 * (tileM / 5.0) / 6.2831853) / tileM;      // phase speed of the peak wave, in tiles per second
    float4 o1 = SAMPLE_TEXTURE2D(_COcean, sampler_COcean, uv + float2(speed, 0.0) * t);
    float4 o2 = SAMPLE_TEXTURE2D(_COcean, sampler_COcean, uv * 0.93 + float2(0.37, 0.61) + float2(speed * 0.8, speed * 0.35) * t);
    h = o1.a * 0.55 + o2.a * 0.45;
    cap = max(o1.b, o2.b);
    float2 sl = ((o1.rg - 0.5) * 0.55 + (o2.rg - 0.5) * 0.45) * (2.0 * OCEAN_SR);
    return mul(transpose(R), sl);
}

// mip of the 256-texel noise tile (tileM metres) for a pixel footprint of foot metres: explicit LOD, legal in branches
float SeaNoiseLod(float foot, float tileM) { return max(log2(foot * 256.0 / tileM), 0.0); }

float3 WaterShade(float3 posAbs, float3 V)
{
    float2 xz = posAbs.xz;
    float dist = length(posAbs - _WorldSpaceCameraPos.xyz);
    SeaInfo s = SeaEval(xz);
    // pixel footprints, taken before any branch: the foam taps inside branches use explicit gradients, so those
    // branches really skip their work when the wave or the flood is absent instead of being flattened
    float2 dxz = ddx(xz), dyz = ddy(xz);
    float pixFoot = sqrt(max(dot(dxz, dxz), dot(dyz, dyz)));                                  // metres per pixel on the water
    float2 dLx = float2(ddx(s.lat), ddx(posAbs.y)), dLy = float2(ddy(s.lat), ddy(posAbs.y));   // face coordinates (along the front, up)
    float dXi = sqrt(max(ddx(s.xi) * ddx(s.xi), ddy(s.xi) * ddy(s.xi)));                      // metres across the front per pixel

    float waveK = saturate(s.amp / 60.0);
    float wBay = 1.0 - smoothstep(_SeaBay.z, _SeaBay.w, length(xz - _SeaBay.xy));
    float churn = saturate(_SeaAim.w / 12.0) * wBay;
    float nearWall = waveK * exp(-pow(s.xi / (2.5 * s.wf), 2.0));
    float faceH = saturate(s.wave / max(s.amp, 1.0));
    float wallK = waveK * saturate(1.0 - abs(s.xi) / (3.0 * s.wf));
    // large-scale normal from the height function itself (independent of the mesh; the wet film is left out: it lies level).
    // On the wall the step is broad: the shape is 100 m scale, the small detail comes from the ripples
    float e = max(clamp(dist * 0.004, 1.2, 14.0), WS_WALL_FD * saturate(wallK * 2.0));
    float2 slope = float2(SeaEvalX(xz + float2(e, 0.0), false).y - SeaEvalX(xz - float2(e, 0.0), false).y,
                          SeaEvalX(xz + float2(0.0, e), false).y - SeaEvalX(xz - float2(0.0, e), false).y) / (2.0 * e);
    // shore distance for the surf: the village bake and the cone (in s.sd), else the caldera ring analytically
    float2 uvB = (xz - _CTerrainRect.xy) * _CTerrainRect.zw;
    float2 eB = min(uvB, 1.0 - uvB);
    float sdW = s.sd, strait = 0.0;
    [branch] if (min(eB.x, eB.y) * 60.0 < 1.0)
    {
        float sr = SeaRingSd(xz, strait);
        if (sr > sdW) sdW = sr; else strait = 0.0;             // the strait only matters where the ring is the nearer shore
    }
    // the wet film on the bared bed (drawdown): puddles and rivulets, level and still
    float filmK = s.film * saturate(1.0 - s.depth / 1.5);
    slope *= 1.0 - filmK;

    // ---- small waves: an FFT ocean at four scales; on the wall face the texture runs down the face ----
    float faceK = saturate(wallK * 1.5) * saturate(length(slope) * 1.5);        // steep part of the wall
    float rough = _SeaMisc.w * (1.0 + 1.2 * nearWall + 1.6 * churn) * (1.0 - 0.7 * faceK);
    rough *= lerp(1.0, lerp(0.08, 0.5, s.riv), filmK);                          // puddles are mirrors, rivulets shiver
    float2 nrm = normalize(xz - _SeaSplash.xy + 1e-3);
    float2 tng = float2(-nrm.y, nrm.x);
    float2 flowXZ = xz;
    if (faceK > 0.01)
    {
        // on the steep face the ocean texture is projected onto the face itself (along the front, and up the face)
        // and slides down it: sheets of water pouring off a moving wall, not ripples stretched into blades
        float2 wc = float2(dot(xz, tng), posAbs.y * 1.1 - _CTime * 3.0);   // the chop climbs the face into the crest
        flowXZ = lerp(xz, wc, faceK);
    }
    float h1, h2, h3, h4, c1, c2, c3, c4;
    float2 rip = OceanOctave(flowXZ, 190.0, 0.35, _CTime, h1, c1) * 0.42
               + OceanOctave(flowXZ, 53.0, 1.10, _CTime, h2, c2) * 0.34
               + OceanOctave(flowXZ, 14.5, -0.45, _CTime, h3, c3) * 0.24
               + OceanOctave(flowXZ, 3.9, 1.85, _CTime, h4, c4) * 0.16 * saturate(260.0 / max(dist, 1.0));
    rip *= 0.55 * rough * lerp(1.0, 0.35, saturate((dist - 300.0) / 1500.0));
    float crestH = (h1 * 0.5 + h2 * 0.3 + h3 * 0.2);                  // 0..1, high on small crests
    float3 N = normalize(float3(-(slope.x + rip.x), 1.0, -(slope.y + rip.y)));

    float3 L = _CSunDir.xyz;
    float3 sunC = _CSunColor.rgb;
    float3 skyAvg = (_CSkyZenith.rgb + _CSkyHorizon.rgb) * 0.5;
    // a lightning stroke: a cold light high in the column (the plume and the cone use the same falloff law)
    float3 boltC = float3(0.75, 0.82, 1.0);
    float Eb = 0.0;
    float3 Lb = float3(0.0, 1.0, 0.0);
    if (_CBolt.w > 1.0)
    {
        float3 toB = _CBolt.xyz - posAbs;
        float dB2 = dot(toB, toB);
        Lb = toB * rsqrt(dB2);
        Eb = _CBolt.w / (dB2 + 4.0e6);                                  // lux at the water
    }
    float3 irr = sunC * saturate(L.y * 0.85 + 0.15) + skyAvg * PI      // lux reaching the surface
               + boltC * (Eb * WS_BOLT_DIFF * saturate(dot(N, Lb) * 0.6 + 0.4));
    // under the ash white water keeps a neutral-cool white of its own instead of the brown of the rock dust
    const float3 lumW = float3(0.2126, 0.7152, 0.0722);
    const float3 coolW = float3(0.88, 0.95, 1.06);
    float3 irrN = lerp(irr, dot(irr, lumW) * coolW, 0.6 * _CState.x);
    float3 horN = lerp(_CSkyHorizon.rgb, dot(_CSkyHorizon.rgb, lumW) * coolW, 0.6 * _CState.x);

    // ---- water body: turquoise over sand, Aegean blue in the deep, the sea floor through the shallows ----
    float depth = max(s.depth, 0.0);
    float3 teal = _PalCalderaTeal.rgb * 0.30;
    float3 deep = _PalDeepWater.rgb * 0.40;
    float3 bodyAlb = lerp(teal, deep, 1.0 - exp(-depth / 14.0));
    // by day the water is a lighter turquoise along the rock (scree shelves at the cliff foot, the cone's beach)
    bodyAlb = lerp(bodyAlb, teal * 1.6, 0.5 * exp(min(sdW, 0.0) / 25.0) * (1.0 - _CState.x));
    // the open sea under the ash takes a touch of its brown and stays slate-teal
    bodyAlb = lerp(bodyAlb, _PalAshBrown.rgb * 0.30, _CState.x * 0.12 * (1.0 - churn));
    // flood water is turbid: the cliffs' ash and soil in suspension scatter, so it is a lighter, opaque grey-brown, not
    // the darkened ash colour (albedo 0.03, below the clear teal: black oil at 476). Quadratic below churn ~0.67: the
    // silt settles as the flood drains, so the calm bay of the silence and the arrival (churn ~0.1) keeps its dark water
    bodyAlb = lerp(bodyAlb, WS_SILT_ALB, churn * saturate(churn * 1.5));
    // the wall is a dark mass: green-black low on the face, thinner and tealer towards the crest
    float3 wallAlb = lerp(float3(0.09, 0.11, 0.10), lerp(_PalCalderaTeal.rgb, _PalDeepWater.rgb, 0.4) * 0.55, faceH * faceH);   // green-grey water, tealer at the thin top
    wallAlb = lerp(wallAlb, _PalFoam.rgb * 0.28, 0.12 * faceH);
    // aerated drapes down the face: 25-50 m wide sheets (blotch layer, stretched down the face and thresholded)
    float sheets = SAMPLE_TEXTURE2D(_CFoam, sampler_CFoam, float2(s.lat / 260.0 + 0.6 * (CNoise2(float2(s.lat / 600.0, posAbs.y / 300.0)) - 0.5), posAbs.y / 420.0)).b;
    wallAlb *= 0.75 + 0.6 * smoothstep(0.45, 0.70, sheets);
    bodyAlb = lerp(bodyAlb, wallAlb, wallK * saturate(faceH * 1.6));
    float3 body = bodyAlb * lerp(irr, irrN, WS_BODY_NEUTRAL) * (1.0 / PI);
    float3 bedAlb = lerp(_PalSilt.rgb, float3(0.42, 0.38, 0.30), CNoise2(xz / 37.0)) * 0.7;
    bedAlb *= lerp(1.0, 0.6, filmK);                                                       // wet silt under the film is darker
    float3 bed = bedAlb * irr * (1.0 / PI) * exp(-depth * float3(0.50, 0.14, 0.11));
    body = lerp(bed, body, 1.0 - exp(-depth / 3.2));

    // ---- light through the water: small crests and the top of the wall glow green when the light is behind them ----
    float3 Lh = normalize(float3(L.x, 0.0, L.z));
    float back = pow(saturate(dot(V, -Lh) * 0.8 + 0.2), 3.0);
    float sssSmall = saturate(crestH * 1.6 - 0.55) * (0.4 + 0.6 * back) * (1.0 - wallK);
    float peak = waveK * saturate((s.wave - 0.45 * s.amp) / max(0.55 * s.amp, 1.0)) * saturate(1.0 - abs(s.xi) / (1.2 * s.wf));
    float3 sssCol = _PalCalderaTeal.rgb * float3(0.55, 1.0, 0.85);
    sssCol = lerp(sssCol, sssCol * 0.5 + _PalLava.rgb * 0.12, _CState.x);       // at night the thin crest carries the ember sky
    // the daylight strip under the umbrella edge lies on the horizon behind the crest (zero when off; fades for crests seen high up)
    float3 horB = _CSkyHorizon.rgb + _CStrip.rgb * smoothstep(0.45, 0.0, -V.y);
    body += sssCol * (sunC * (0.05 * sssSmall + 0.10 * peak * back) + _CSkyHorizon.rgb * (0.25 * sssSmall) + horB * (3.0 * peak)) * 0.6 * (1.0 - churn);
    // light through the wall: horizon and strip light scattered forward through the thinning upper face, filtered teal
    // (the water takes the red whatever the source), so the wall climbs from green-black to lit glass at the lip
    float3 sssW = _PalCalderaTeal.rgb * float3(0.55, 1.0, 0.85);
    float3 horT = horN + _CStrip.rgb * (0.5 * smoothstep(0.45, 0.0, -V.y));
    body += sssW * horT * (WS_WALL_SSS * wallK * smoothstep(0.35, 0.95, faceH) * (0.4 + 0.6 * back) * (1.0 - churn));
    // the vent behind the wave shines through the thin lip: amber glass at the very top, green and dark deeper down
    float3 toVent = normalize(_CVent.xyz - posAbs);
    float lipK = waveK * smoothstep(0.80, 0.97, faceH) * step(0.0, s.xi) * saturate(1.0 - s.xi / (1.2 * s.wf));
    float thick = 4.0 + 30.0 * (1.0 - faceH);                                  // metres of water the light crosses
    float3 lipGlow = exp(-thick * float3(0.35, 0.07, 0.06)) * _CVentGlow.rgb * (WS_LIP_GAIN * lipK * pow(saturate(dot(-V, toVent)), 2.0) * (1.0 - churn));
    body += lipGlow;

    // ---- reflection and sun glitter ----
    float NV = saturate(dot(N, V));
    // on the torn wall face (rW) the water is glossy, not a mirror: Fresnel is capped, a downward reflection sees the dark
    // sea ahead instead of the folded sky, and the skyline edge is broad. Otherwise every ripple of the face crossing the
    // horizon fold or the skyline draws a razor-thin bright contour from crest to foot
    float rW = faceK * saturate(wallK * 1.5);
    // the torn water round the wave and on the flood takes most of the same treatment: there the folded horizon drew
    // thin bright 'hair' strokes along every ripple (the wave body at 462)
    float rG = max(rW, 0.7 * saturate(wallK * 2.0 + churn));
    float F = min(0.02 + 0.98 * pow(1.0 - NV, 5.0), lerp(0.75, 0.30, rG));
    F *= lerp(1.0, 0.65, churn);                       // churned, aerated flood water breaks up the mirror: its silt shows
    float3 R = reflect(-V, N);
    float below = rG * smoothstep(0.0, -0.08, R.y);
    R.y = abs(R.y) + 0.02;
    float3 Rn = normalize(R);
    float3 refl = CSky(Rn);
    // the cliffs and the cone stand in the reflection: a darker, hazed band under the skyline
    float4 sky = SAMPLE_TEXTURE2D_LOD(_CSkyline, s_linear_repeat_sampler, float2(atan2(Rn.z, Rn.x) / 6.2831853 + 0.5, 0.5), 0);
    float3 skyAvgR = (_CSkyZenith.rgb + _CSkyHorizon.rgb) * 0.5;
    float3 land = sky.r * (skyAvgR * 0.8 + _CSunColor.rgb * 0.08 * saturate(L.y + 0.2));
    float ftL = exp(-sky.g * 1000.0 * _PlFog.x);
    land = land * ftL + _CFogColor.rgb * (1.0 - ftL);
    float bw = 0.012 + 0.10 * rG;
    refl = lerp(refl, land, smoothstep(sky.a + bw, sky.a - bw, Rn.y));
    refl = lerp(refl, horN * 0.2, below);
    // mirror puddles carry the image of the column: dark ash above, its foot glowing from the vent (closest approach
    // of the reflected ray to the column axis; drawn over the cone band: the scar faces the village)
    if (filmK > 0.01 && _CVentGlow.r > 1.0)
    {
        float3 o = posAbs - _CVent.xyz;
        float b = Rn.y, dRo = dot(Rn, o);
        float sAx = clamp((o.y - b * dRo) / max(1.0 - b * b, 1e-4), 0.0, 4000.0);   // height on the axis above the vent
        float tR = max(b * sAx - dRo, 0.0);                                         // along the reflected ray
        float3 cp = o + Rn * tR - float3(0.0, sAx, 0.0);
        float rCol = 380.0 + 0.15 * sAx;                                            // PlumeDirector.VentRadius, Growth
        float inCol = (1.0 - smoothstep(0.55 * rCol, 1.05 * rCol, length(cp))) * step(1.0, tR) * filmK;
        refl = lerp(refl, refl * 0.4 + _CVentGlow.rgb * (WS_COL_GLOW * exp(-sAx / 540.0)), inCol);
    }
    float3 Hv = normalize(L + V);
    float NH = saturate(dot(N, Hv));
    float a2 = 0.0012 + saturate(dist / 1500.0) * 0.03;
    float dn = NH * NH * (a2 - 1.0) + 1.0;
    float D = a2 / (PI * dn * dn);
    float3 spec = sunC * min(D * 0.25, 60.0) * F * saturate(dot(N, L) * 4.0) * (1.0 - _CState.x * 0.85);
    float3 colW = body * (1.0 - F) + refl * F;          // the water under any foam, without its glints (foam scatters, it does not mirror)
    float3 col = colW + spec;
    // the vent glow on the water: a long broken orange road under the column (an emitter above the crater);
    // on the mirror puddles the column image above carries it
    if (_CVentGlow.r > 1.0)
    {
        float3 Lv = normalize(_CVent.xyz + float3(0.0, 250.0, 0.0) - posAbs);
        float NHv = saturate(dot(N, normalize(Lv + V)));
        float av = 0.003 + 0.02 * saturate(rough - 0.6) + saturate(dist / 2500.0) * 0.01 + 0.03 * rG;   // broad on the torn water
        float dv = NHv * NHv * (av - 1.0) + 1.0;
        col += _CVentGlow.rgb * min(av / (PI * dv * dv) * 0.02, 40.0) * F * (1.0 - 0.6 * filmK);
    }
    // a cold glint of the stroke, modelled on the vent road: a broad lobe (the lit cloud round the channel is km wide)
    if (Eb > 0.0)
    {
        float NHb = saturate(dot(N, normalize(Lb + V)));
        float ab = 0.004 + 0.03 * saturate(rough - 0.6) + saturate(dist / 2500.0) * 0.012 + 0.03 * rG;
        float db = NHb * NHb * (ab - 1.0) + 1.0;
        col += boltC * (Eb * min(ab / (PI * db * db) * WS_BOLT_GLINT, 80.0)) * F;
    }

    // ---- foam: a real foam pattern thresholded by coverage (lace first, then patches) ----
    float pulse = 0.75 + 0.35 * sin(_CTime * 0.8 + xz.x * 0.07 + xz.y * 0.05);
    float drawK = saturate(_SeaAim.z / 30.0);
    float shore = saturate(1.0 - depth / (pulse * 1.3 + dist * 0.004 + 3.0 * drawK)) * step(-120.0, s.ground) * (1.0 - filmK);
    float4 fA = SAMPLE_TEXTURE2D(_CFoam, sampler_CFoam, xz / 9.0 + _CTime * float2(0.010, -0.006));
    float4 fB = SAMPLE_TEXTURE2D(_CFoam, sampler_CFoam, xz.yx / 31.0 - _CTime * float2(0.004, 0.007));
    float fh = fA.r * 0.62 + fB.r * 0.38;
    float cov = shore * 0.62;                                      // lace at the shore, not a neon line
    // whitecaps on the open sea once the eruption has roughened it (and in every tremor); the 53 m and 14.5 m octaves
    // only near: thresholded at 1-2 km they are single-pixel speckle
    float caps = max(c1, max(c2 * saturate(900.0 / dist), c3 * saturate(300.0 / dist))) * saturate((rough - 0.9) * 0.8);
    cov = max(cov, caps * 0.55);
    // the sea running out during the drawdown: streaks of foam dragged seaward (only a thin scum on the bared bed)
    cov = max(cov, lerp(0.35, 0.15, filmK) * drawK * wBay * SAMPLE_TEXTURE2D(_CFoam, sampler_CFoam, (xz + nrm * _CTime * 4.0) / 40.0).a);
    // and a scum line along its retreating edge, so the waterline reads on the dark wet bed (a line where the lace is
    // mip-averaged). The bed shelves at ~0.13 there (WorldGen.Seabed), so this depth band is ~2-4 pixels wide
    float scum = drawK * wBay * saturate(1.0 - depth / (0.15 + 0.3 * pixFoot)) * (1.0 - filmK);
    cov = max(cov, WS_SCUM * scum);
    fh = lerp(fh, max(fh, 0.6), scum * saturate(pixFoot / 2.0));
    // surf where the sea meets rock (cliffs, the cone, the caldera rim): a lace ring at least two pixels wide at any
    // distance, keyed to the shore distance (on plunging cliffs a depth band is thinner than a pixel). None along the
    // harbour quay (WorldGen.QuaySector), in the strait, while the bay is drained or flooded, or under the wave
    float quayK = smoothstep(-292.0, -268.0, xz.y) * (1.0 - smoothstep(-8.0, 14.0, xz.y)) * step(-400.0, xz.x);
    float levelK = saturate(abs(_SeaAim.w - _SeaAim.z) * wBay / 4.0);
    float surf = saturate(1.0 - abs(sdW) / (3.0 + 2.0 * pixFoot)) * (1.0 - quayK) * (1.0 - smoothstep(0.5, 0.85, strait))
               * (1.0 - levelK) * (1.0 - saturate(s.wave / 8.0)) * (1.0 - filmK) * saturate(2.0 - dist / 7000.0);
    [branch] if (surf > 0.002)
    {
        // sets of swell: brighter and fainter runs along the shore, drifting
        float sn = SAMPLE_TEXTURE2D_LOD(_CNoise2D, s_linear_repeat_sampler, xz / 260.0 + _CTime * float2(0.006, -0.004), 0).r;
        float sets = smoothstep(0.38, 0.62, sn);
        cov = max(cov, surf * WS_SURF * (0.7 + 0.3 * sets));
        // far away the lace is mip-averaged below the threshold: there the ring is a line (about half to full white)
        fh = lerp(fh, max(fh, 0.55 + 0.3 * sets), surf * saturate(pixFoot / 2.0));
    }
    // whitewater under the splash crown: on the same lobes as the rings and thrown farther toward the village (the
    // slide's axis), its edge torn by ~250 m blotches (one 2 km tile on the dome: a smaller tile repeats across it).
    // A plain disc read as a pond target from the village
    float domeK = saturate(_SeaMisc.x / 300.0);
    [branch] if (domeK > 0.001)
    {
        float2 ddome = xz - _SeaDome.xy;
        float rd = length(ddome);
        float2 dnd = ddome / max(rd, 1.0);
        float rag = SAMPLE_TEXTURE2D_LOD(_CNoise2D, s_linear_repeat_sampler, ddome / 2000.0 + 0.5, max(SeaNoiseLod(pixFoot, 2000.0), 2.0)).g;
        float lobD = SeaRingLobe(dnd);
        float reachD = 700.0 * sqrt(0.6 + 0.8 * lobD) * lerp(0.8, 1.25, smoothstep(-0.5, 0.7, dot(dnd, _SeaAim.xy)))
                     * (0.7 + 0.6 * smoothstep(0.3, 0.7, rag));
        float xd = rd / reachD;
        // its outer part is segmented by lobe as well: seen from the village at ~1 degree a change of reach moves the near
        // edge by under a pixel, so only sections with and without whitewater (the threshold cuts at coverage ~0.65)
        // break the ellipse. The core round the impact stays whole
        float segD = lerp(1.0, lerp(0.40, 1.15, smoothstep(0.20, 0.70, lobD)), smoothstep(0.25, 0.60, xd));
        cov = max(cov, domeK * exp(-xd * xd) * segD);
    }
    float2 su = float2(s.lat, s.xi);
    float aer = 0.0;
    // the wave reaching the land: coverage near the shore (kept off the steep face, which pours instead)
    float landK = smoothstep(-300.0, -10.0, s.sd) * saturate(s.wave / 20.0);
    float farLine = 0.0;
    float tipK = 0.0;                   // crest and lip white water: thresholded crisp (lace and patches, not a soft veil)
    float silverK = 0.0;                // the thin white top of the crest (lit from behind where it stands against the sky)
    float lostF = 0.0;                  // share of the foam texture below a pixel: threshold by the area it covers
    [branch] if (waveK > 0.001)
    {
        // on the wall: white at the crest, pouring drapes on the upper face, churning at the foot and behind the crest
        float fy = posAbs.y;
        // face foam: the foam texture projected on the face (along the front, up the face), stretched 6:1 down the
        // face and sliding down it at ~24 m/s: falling white water, not lace cells
        float2 sW = float2(1.0 / 40.0, 1.0 / 240.0), sW2 = float2(1.0 / 17.0, 1.0 / 120.0);
        float4 fW = SAMPLE_TEXTURE2D_GRAD(_CFoam, sampler_CFoam, float2(su.x / 40.0, fy / 240.0 + _CTime * 0.10), dLx * sW, dLy * sW);
        float4 fW2 = SAMPLE_TEXTURE2D_GRAD(_CFoam, sampler_CFoam, float2(su.x / 17.0 + 0.37, fy / 120.0 + _CTime * 0.21), dLx * sW2, dLy * sW2);
        // drapes 25-50 m wide (streak layer at a coarse scale, 15 m columns), wandering, finer streaks inside
        float wl = su.x + 90.0 * (CNoise2(float2(su.x / 380.0, fy / 260.0)) - 0.5);
        float2 sF = float2(1.0 / 700.0, 1.0 / 900.0), sF2 = float2(1.0 / 230.0, 1.0 / 400.0);
        float fRaw = SAMPLE_TEXTURE2D_GRAD(_CFoam, sampler_CFoam, float2(wl / 700.0, fy / 900.0 + _CTime * 0.004), dLx * sF, dLy * sF).a * 0.65
                   + SAMPLE_TEXTURE2D_GRAD(_CFoam, sampler_CFoam, float2(wl / 230.0 + 0.5, fy / 400.0 + 0.77), dLx * sF2, dLy * sF2).a * 0.35;
        float fingers = smoothstep(0.45, 0.70, fRaw);
        float streakT = fW.b * 0.55 + fW2.r * 0.45;
        // breaking sections (SeaBreak, shared with the crest spray): white where the front breaks, glassy teal stretches
        // of a few hundred metres between, so the far wave is not one even white band; close up the lip stays continuous
        float brk = SeaBreak(s.lat, s.lobes);
        float brkW = lerp(0.30, 1.0, brk);
        // the crest white water, +-0.32 wf (at 0.40 wf and near-solid coverage it was +-45 m of even white: a snowfield)
        float crest = exp(-pow((s.xi + 0.10 * s.wf) / (0.32 * s.wf), 2.0)) * brkW;
        // the breaking lip: a continuous band of white water just ahead of the peak (the top ~15 % of the face), its
        // lower edge torn by the drapes; at least ~1.5 pixels deep at range
        float lipX = (s.xi - (0.20 + 0.06 * fRaw) * s.wf) / ((0.11 + 0.08 * fRaw) * s.wf + 1.5 * pixFoot);
        float lip = exp(-lipX * lipX);
        // the lip turns solid only where it is too thin to carry texture (under ~4-10 px tall, or its whitewater cells
        // under ~2 px): far away it must read as a line, closer it is lumps, holes and streaks like the rest
        float resK = max(1.0 - smoothstep(4.0, 10.0, 0.15 * s.wf / max(dXi, 1e-3)), smoothstep(2.0, 6.0, pixFoot));
        tipK = waveK * saturate(2.0 * max(crest, lip));
        // the churned skirt at the real toe (the profile is down to ~2 % there), on the flat water only
        float footX = (s.xi - 2.3 * s.wf) / (0.45 * s.wf);
        float foot = exp(-footX * footX) * (1.0 - faceK);
        // at range the lip and the toe skirt follow the breaking sections too: there the area threshold turns any
        // coverage over ~0.65 solid, so a lip at x1 drew one continuous white line along the whole front (395, 625).
        // Glassy sections keep a faint trace (coverage ~0.3); close up (425 and nearer) the lip stays continuous
        float farS = smoothstep(WS_FAR_SECT.x, WS_FAR_SECT.y, dist);
        float lipSec = lerp(1.0, lerp(WS_LIP_GLASSY, 1.0, brk), farS);
        foot *= lerp(1.0, lerp(0.50, 1.0, brk), farS);
        // after the strike the front runs out over deep water beyond the rim, where a long, lower swell no longer breaks:
        // its white water fades 40-120 s after the strike (it drew a ruler line across the far sea at 600-664)
        float runOut = 1.0 - smoothstep(40.0, 120.0, _SeaFlow.x);
        float behind = saturate(-s.xi / 60.0) * exp(s.xi / 900.0) * lerp(0.65, 1.0, brk);   // a breaking section leaves more wake
        // how far down the face a finger reaches: 0 at the crest, 1 at the foot
        float down = saturate(1.0 - faceH);
        // aerated water pouring down the face: a soft, continuous lightening in long vertical sheets (no threshold)
        float reach = fRaw * 1.1 - 0.1;
        aer = smoothstep(reach + 0.15, reach - 0.3, down) * (0.55 + 0.45 * streakT) * step(0.0, s.xi) * faceK * waveK * smoothstep(0.55, 0.85, faceH) * brkW;
        // pouring whitewater: drapes of white falling from the lip down the upper half of the face, wherever the
        // fingers reach, fewer below the glassy sections
        float pour = waveK * step(0.0, s.xi) * smoothstep(0.50, 0.92, faceH) * smoothstep(0.25, 0.65, fingers) * brkW;
        // (the run-up coverage stays off the crest near the shore: added to it, the crest went solid white again)
        float wcov = waveK * runOut * (max(crest * WS_CREST, lip * WS_LIP * lerp(0.82, 1.0, resK) * lipSec) + foot * 0.65 + behind * WS_BEHIND)
                   + landK * 0.62 * (1.0 - faceK) * (1.0 - tipK);
        wcov = max(wcov, 0.85 * pour * runOut);
        // at the foot, behind the crest and on the flood the foam lies on the water (world xz, advected with the flow);
        // only the steep face uses the face projection. The 39 m / 13 m lace fades into broad blotches (mean-matched)
        // from 150 m or once a pixel nears the lace cells, before thresholding can turn it into speckle
        float farK = saturate((dist - 400.0) / 2500.0);
        float farX = saturate(max((dist - 150.0) / 900.0, (pixFoot - 0.2) / 1.3));
        float2 xa = xz - nrm * _CTime * 6.0;
        float2x2 Rf = float2x2(0.80, -0.60, 0.60, 0.80);
        float fxz = SAMPLE_TEXTURE2D_GRAD(_CFoam, sampler_CFoam, xa / 39.0, dxz / 39.0, dyz / 39.0).r * 0.65
                  + SAMPLE_TEXTURE2D_GRAD(_CFoam, sampler_CFoam, mul(Rf, xa) / 13.0, mul(Rf, dxz) / 13.0, mul(Rf, dyz) / 13.0).r * 0.35;
        fxz = lerp(fxz, fB.b - 0.08, farX);
        float wh = lerp(lerp(fxz, fW.r * 0.6 + fW2.r * 0.4, faceK), 0.55, farK * 0.7);
        // the crest and the lip are tumbling white water: 4-5 m lace cells in ~25 m blotches, in face coordinates
        // (rolling down the face at ~4 m/s, streaming back over the top), with the fine lace on top at full contrast.
        // These stay resolved out to ~2 km, where the fine lace is only its mip mean and thresholding it gave an even
        // white band (the snowfield). Blotch mixed in (mean-matched): patches with holes rather than a net of cells
        float2 sT = float2(1.0 / 110.0, 1.0 / 90.0);
        float4 fT4 = SAMPLE_TEXTURE2D_GRAD(_CFoam, sampler_CFoam, float2(su.x, fy) * sT + float2(0.29, _CTime * 0.04), dLx * sT, dLy * sT);
        float fT = fT4.r * 0.6 + (fT4.b - 0.09) * 0.4;
        wh = lerp(wh, fT + 0.6 * (wh - 0.40), tipK);
        cov = max(cov, wcov);
        fh = lerp(fh, wh, saturate(wallK * 2.0 + landK));
        fh = lerp(fh, max(fh, 0.75), lip * waveK * resK * lerp(1.0, brk, farS) * runOut);   // an unresolved lip is a solid line where it breaks
        // the share of the tip's texture below a pixel: the fine lace from ~0.3 m a pixel, the coarse cells from ~2 m
        lostF = tipK * (0.4 * smoothstep(0.3, 1.2, pixFoot) + 0.6 * smoothstep(2.0, 6.0, pixFoot));
        silverK = waveK * max(crest, lip) * smoothstep(0.80, 0.97, faceH);
        // a crisp far crest line: solid foam at least a pixel tall at 4 km (applied after the flood foam below) where the
        // front breaks; almost none on the glassy sections (at 0.35 it still drew a ruler line along the whole front)
        float fl = (s.xi + 0.05 * s.wf) / (0.25 * s.wf + dist * 0.004);
        farLine = waveK * exp(-fl * fl) * smoothstep(900.0, 2500.0, dist) * lerp(0.10, 1.0, brk) * runOut;
    }
    // the run-up and the flood: foam that streams with the water (flow map + streaks), rafts and dark channels
    float flowDone = 0.0;
    [branch] if (_SeaFlow.w > 0.5 && churn + landK > 0.01)
    {
        flowDone = 1.0;
        float2 vel = _SeaFlow.yz;
        // eddies: the curl of a slow noise field (uv gradient of the fbm)
        float2 ue = xz / 300.0;
        float lodE = SeaNoiseLod(pixFoot, 300.0);
        float n0 = SAMPLE_TEXTURE2D_LOD(_CNoise2D, s_linear_repeat_sampler, ue, lodE).r;
        float nX = SAMPLE_TEXTURE2D_LOD(_CNoise2D, s_linear_repeat_sampler, ue + float2(0.04, 0.0), lodE).r;
        float nZ = SAMPLE_TEXTURE2D_LOD(_CNoise2D, s_linear_repeat_sampler, ue + float2(0.0, 0.04), lodE).r;
        vel += float2(nZ - n0, n0 - nX) * (WS_FLOW_EDDY / 0.04);
        // two-phase flow map (period 6 s): the lace streams with the water and never stretches for long
        float ph0 = frac(_CTime / 6.0), ph1 = frac(_CTime / 6.0 + 0.5);
        float2 g13x = dxz / 13.0, g13y = dyz / 13.0;
        float f0 = SAMPLE_TEXTURE2D_GRAD(_CFoam, sampler_CFoam, (xz - vel * (6.0 * (ph0 - 0.5))) / 13.0, g13x, g13y).r;
        float f1 = SAMPLE_TEXTURE2D_GRAD(_CFoam, sampler_CFoam, (xz - vel * (6.0 * (ph1 - 0.5))) / 13.0 + 0.37, g13x, g13y).r;
        float fflow = lerp(f0, f1, abs(2.0 * ph0 - 1.0));
        // streaks along the flow axis (the streak layer is constant along v), riding the integrated mean flow
        float2 ax = _SeaAim.xy, ac = float2(-ax.y, ax.x);
        float2 q = xz - _SeaFlow2.xy;
        // rafts of foam and dark open channels between them (55 m cells), carried by the same flow. A second, rotated
        // 517 m tap (130 m cells) clusters them: the two periods are incommensurate, so the 220 m tile never shows
        float2x2 Rq = float2x2(0.80, -0.60, 0.60, 0.80);
        float r1 = SAMPLE_TEXTURE2D_LOD(_CNoise2D, s_linear_repeat_sampler, q / 220.0, SeaNoiseLod(pixFoot, 220.0)).r;
        float r2 = SAMPLE_TEXTURE2D_LOD(_CNoise2D, s_linear_repeat_sampler, mul(Rq, q) / 517.0 + 0.31, SeaNoiseLod(pixFoot, 517.0)).r;
        float rafts = smoothstep(0.30, 0.74, 0.6 * r1 + 0.4 * r2);    // the sum is narrower (std ~0.09): a narrower window
        // thin streaks along the flow: the streak layer (constant along v) on a 48 m x 400 m tile draws 0.2-1 m lines
        // tens of metres long (on the old 14 m tile they were below a pixel past ~150 m); the lace stretched 5:1 along
        // the flow (5 m x 25 m cells) marbles the churn at any range, where the 13 m lace is only its mean
        float2 qa = float2(dot(q, ac), dot(q, ax)), qdx = float2(dot(dxz, ac), dot(dxz, ax)), qdy = float2(dot(dyz, ac), dot(dyz, ax));
        float2 sS = float2(1.0 / 48.0, 1.0 / 400.0), sM = float2(1.0 / 120.0, 1.0 / 600.0);
        float streak = SAMPLE_TEXTURE2D_GRAD(_CFoam, sampler_CFoam, qa * sS, qdx * sS, qdy * sS).a;
        float marb = SAMPLE_TEXTURE2D_GRAD(_CFoam, sampler_CFoam, qa * sM + 0.21, qdx * sM, qdy * sM).r;
        streak *= lerp(0.5, 1.0, rafts);                              // streaks gather in the rafts, the channels stay dark
        float k = saturate(churn * 2.0 + landK) * (1.0 - faceK) * (1.0 - tipK);     // the crest keeps its own white water
        fh = lerp(fh, 0.55 * fflow + 0.20 * marb + 0.10 * streak + 0.15 * fB.b, k);
        // well below solid even in the rafts (about 0.6), so they are dense lace and not white slabs; the flood settles
        // 70-160 s after the strike (WaterDirector.FloodLevel), and the foam with it
        cov = max(cov, churn * (WS_FLOOD_COV.x + WS_FLOOD_COV.y * rafts));
        cov *= lerp(1.0, 0.75 + 0.30 * rafts, saturate(landK * (1.0 - nearWall) + churn) * (1.0 - faceK));
        // inside the rafts the lace gathers into dense 4-9 m patches with open water between: at an even coverage the
        // cells drew a uniform fishnet at the strike foot (455) and chain-link on the flood (476). The patches are the mean
        // of two blotch layers, the 31 m tile (fB) and a rotated 37 m tile (incommensurate, like the rafts): the 31 m tile
        // alone repeated every 31 m along x and z, rows of chevrons over the flood at 476. The mean has std 0.076 (one
        // layer 0.107), so its window is narrower: same contrast, mean 0.92. Once a pixel's long axis nears the patches
        // they fade to that mean: the filter averages a tile only along the view, and across it each tile's repeat
        // survives as stripes converging on the horizon (at 476 the area threshold turned them into the chevron rows)
        float bq = SAMPLE_TEXTURE2D_GRAD(_CFoam, sampler_CFoam, mul(Rq, xz) / 37.0 + 0.53 + _CTime * float2(-0.005, 0.003),
                                         mul(Rq, dxz) / 37.0, mul(Rq, dyz) / 37.0).b;
        float clump = lerp(0.72, 1.12, smoothstep(0.39, 0.57, 0.5 * (fB.b + bq)));
        clump = lerp(clump, 0.92, smoothstep(0.6, 2.0, pixFoot));
        cov *= lerp(1.0, clump, k);
        // the 13 m lace is below a pixel past ~0.4 m (its cells are 0.2-0.5 m): thresholding its mip mean drew rafts as
        // solid pills on clear water, so there the threshold takes the area the lace covers instead
        lostF = max(lostF, saturate(churn * 2.0) * (1.0 - faceK) * (1.0 - tipK) * saturate((pixFoot - 0.12) / 0.5));
    }
    cov = max(cov, churn * 0.62 * (1.0 - flowDone));
    cov = max(cov, farLine);
    fh = lerp(fh, 1.0, farLine);
    // threshold: coverage 0 -> nothing, 0.5 -> the lace network, 1 -> solid foam. Crisp, so the crest, the wake and the
    // flood break into lace and patches; only the steep face below the lip keeps a soft edge (its projection is stretched).
    // Where the texture is below a pixel its mip mean says nothing, so there the result is the area a normal spread
    // (sd ~0.16, the foam height's) leaves above the threshold: grey churn, not solid slabs or nothing
    float zoneK = saturate(wallK * 2.0 + landK + churn);                                  // the wave and the flood
    float soft = lerp(lerp(0.22, 0.18, zoneK), 0.40, faceK * (1.0 - tipK));
    float bias = cov * 0.25 - 0.1;
    soft = lerp(soft, 0.40, lostF);
    bias = lerp(bias, 0.5, lostF);
    float foam = saturate((fh - (1.0 - cov)) / soft + bias);
    foam *= saturate(cov * 4.0);
    float bub = lerp(1.0, 0.72 + 0.28 * fA.g, saturate(2.0 - dist / 60.0));                // bubbles up close
    // white water under the ash: lit by the neutral-cool irradiance, so it reads as water and not as dust
    float3 foamCol = _PalFoam.rgb * 0.80 * (irrN * (1.0 / PI) * (0.65 + 0.35 * saturate(dot(N, L) + 0.5)) + horN * 0.35) * bub;
    foamCol *= lerp(1.0, lerp(0.55, 1.05, faceH), wallK);                                 // foam low on the wall sits in its shadow
    foamCol *= lerp(1.0, 0.9, _CState.x);                                                 // under the ash foam is a little greyer
    // thin foam at the crest is lit from behind: it scatters the same transmitted horizon light that makes the glass glow
    // (diffusely, so about 40 % of the water's forward glow; at 0.25 the lace sat below the glass it covers at 455)
    float3 backLit = sssCol * horB * WS_FOAM_BACK * peak + lipGlow * 0.6;
    float3 aerCol = (foamCol + backLit) * 0.62;                                           // aerated water under the surface: no lace terms
    aerCol = max(aerCol, colW * 1.05);                    // bubbles make the water milkier, never darker than the glass they fill
    // on the wave and the flood: thick piles bright, thin lace grey (a film over dark water), and relief from the
    // ripple normal under a three-quarter light from the viewer's side (the lit horizon ring surrounds the bay under the
    // lid; a front light would flatten the lumps, a back light black out the near face). Off on the open sea and the
    // shore, which read well
    float pileK = zoneK * (1.0 - lostF);
    float pile = saturate((fh - (1.0 - cov)) * 2.5);
    float3 Lr = normalize(float3(V.x - V.z, 0.6, V.z + V.x));
    foamCol *= lerp(1.0, lerp(0.60, 1.10, pile) * lerp(1.0, 0.72 + 0.40 * saturate(dot(N, Lr)), saturate(1.5 - pixFoot)), pileK);
    foamCol *= lerp(float3(1.0, 1.0, 1.0), float3(0.90, 0.84, 0.74), 0.8 * churn);         // the flood's foam carries its silt
    foamCol += backLit;
    // the thin white top of the crest against the sky carries the horizon behind it, so the lip reads as a bright edge
    // (under the ash, lit foam alone sits at the value of the sky beside it). The neutral horizon and half the strip, as
    // the wall glow takes them (the full strip would blow the edge out). Off where the crest is seen from above
    foamCol += _PalFoam.rgb * horT * (WS_LIP_SILVER * silverK * smoothstep(0.03, -0.10, V.y));
    col = lerp(col, aerCol, saturate(aer * 0.42));
    // foam is a scatterer on top of the water: thin lace passes on the light of the water under it and adds its own, so
    // it is never darker than that water (the grey crackle net on the backlit 455 crest); thick piles stay at least a
    // little above it. On the wave and the flood only (pileK): the open sea and the shore keep their read
    float3 under = lerp(colW, aerCol, saturate(aer * 0.42));
    foamCol = lerp(foamCol, max(foamCol, under * lerp(WS_LACE_LIFT.x, WS_LACE_LIFT.y, pile)), pileK);
    col = lerp(col, foamCol, saturate(foam));
    return col;
}
#endif

void GetSurfaceAndBuiltinData(FragInputs input, float3 V, inout PositionInputs posInput, out SurfaceData surfaceData, out BuiltinData builtinData RAY_TRACING_OPTIONAL_PARAMETERS)
{
    ZERO_INITIALIZE(SurfaceData, surfaceData);
    ZERO_BUILTIN_INITIALIZE(builtinData);
    builtinData.opacity = 1.0;
#ifdef WATER_SHADING
    builtinData.emissiveColor = WaterShade(GetAbsolutePositionWS(input.positionRWS), V);
#endif
}
