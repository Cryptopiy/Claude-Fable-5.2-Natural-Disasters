#ifndef SUPERCELL_FIELD_INCLUDED
#define SUPERCELL_FIELD_INCLUDED

// The supercell as ONE analytic signed-distance field with a definite organisation:
// anvil sheet with overshooting dome and mammatus, striated tower with stacked plates, flat rain-free base with
// a rotating wall cloud and tail cloud, arcus shelf wedge, funnel (rope -> cone -> wedge) with sub-vortices,
// debris dome, dust wall, forward-flank precipitation core. Distances are metres in the moving local frame (truck at
// the origin, y up).
// Constants mirror Config.cs.

#define SC_ANVIL_BASE 9000.0
#define SC_ANVIL_TOP 13000.0
#define SC_CLOUD_BASE 2800.0
#define SC_DECK_TOP 4300.0
#define SC_WALL_BASE 1500.0
#define SC_SHELF_EDGE 700.0
#define SC_MESO_R 10000.0
#define SC_FAR 1.0e6

TEXTURE3D(_ScNoise);
SAMPLER(sampler_ScNoise);

float4 _ScOrigin;    // xy = world xz of the local origin (m), z = ambient time (s), w = frame index
float4 _ScStorm;     // xy = updraft centre (local xz), z = anvil reach downwind (m), w = mammatus 0..1
float4 _ScAnvil;     // x = back reach, y = half width, z = tower rotation (rad), w = deck cover 0..1
float4 _ScShelf;     // x = local z of the leading edge at its apex, y = local x of the apex, z = amount 0..1, w = unused
float4 _ScWall;      // xy = wall cloud centre (local xz), z = lowering 0..1, w = wall rotation (rad)
float4 _ScTor;       // xy = vortex centre (local xz), z = radius at the ground (m), w = descend 0..1
float4 _ScTor2;      // x = morph (0 rope .. 1 wedge), y = rope-out 0..1, z = rain wrap 0..1, w = debris 0..1
float4 _ScTorRot;    // x = shell angle, y = inner/sub-vortex angle, z = funnel height (m), w = rim gain
float4 _ScTor3;      // x = funnel-shadow strength 0..1, y = debris dome scale 0..1 (held low in beat 6), z, w = unused
float4 _ScRain;      // x = rain core extinction (1/m), y = rain-free radius around the wall cloud (m), z = wrap extinction (1/m), w = haze extinction (1/m)
float4 _ScDust;      // x = local z of the dust front, y = wall height (m), z = amount 0..1, w = unused
float4 _ScClear;     // x = clear air 0..1 (rain only under the departing storm), y = chamber mask 0..1 (veined internal flashes), z, w = unused
float4 _ScPrecip;    // xy = forward-flank precipitation core centre (local xz), z = rain extinction (1/m), w = hail extinction (1/m)

#define SC_CORE_AX 5000.0    // forward-flank core: half-width across (east-west), m
#define SC_CORE_AZ 3200.0    // half-depth (north-south), m
#define SC_CORE_EDGE 900.0   // soft rim, m
#define SC_CORE_TOP 7000.0   // the column thins out into the cloud above the base

float4 ScNoise(float3 p, float tile)
{
    return SAMPLE_TEXTURE3D_LOD(_ScNoise, sampler_ScNoise, p / tile, 0);
}

float2 ScRot(float2 v, float a)
{
    float s = sin(a), c = cos(a);
    return float2(v.x * c - v.y * s, v.x * s + v.y * c);
}

// metres behind (north of) the shelf cloud's leading edge; the line is an arc 100 km long, flanks curving back
float ShelfBehind(float2 lxz)
{
    float xr = lxz.x - _ScShelf.y;
    return lxz.y - (_ScShelf.x + xr * xr / 190000.0);
}

// ---------------------------------------------------------------------------------------------- shared shape helpers
// (declared before every Sd* function: the deck, the funnel and the flash shadow all read the one funnel profile)

// polynomial smooth minimum (k in metres): the funnel grows out of the wall cloud without a seam
float ScSmin(float a, float b, float k)
{
    float h = saturate(0.5 + 0.5 * (b - a) / k);
    return lerp(b, a, h) - k * h * (1.0 - h);
}

// The funnel profile. One copy for the whole game: Core/FunnelShape.cs and Editor/VfxGen.cs TorRadius mirror these
// functions exactly, so the flash shadow, the bolts' clearance and the condensation sheets stay on the funnel seen here.

// radius of the funnel where it meets the wall cloud (rope -> cone -> wedge)
float ScTorTop(float rG, float morph)
{
    return max(rG * lerp(2.6, 1.55, morph), lerp(260.0, 1150.0, morph));
}

// profile exponent: a rope stays an even tube that flares only near the top, the cone is a true V, the wedge
// widens almost linearly (the old lerp(5, 1.5) kept T420 a stem with a cup on it)
float ScTorExp(float morph)
{
    float e = lerp(6.0, 1.3, smoothstep(0.0, 0.22, morph));
    return lerp(e, 1.0, smoothstep(0.7, 1.0, morph));
}

// how fast the funnel keeps widening above its top, inside the cloud: no straight 'tank' wall when the base rides up
float ScTorSlope(float morph)
{
    return lerp(0.9, 0.6, morph);
}

// radius of the funnel's cap, 350 m inside the cloud where SdFunnel closes it
float ScTorCap(float rG, float morph)
{
    return ScTorTop(rG, morph) + morph * (250.0 + 0.2 * rG) + 350.0 * ScTorSlope(morph);
}

// funnel radius at height y without the surface noise, for the vortex now
float ScFunnelR(float y)
{
    float H = _ScTorRot.z, rG = _ScTor.z, morph = _ScTor2.x;
    float h = saturate(y / H);
    return rG + (ScTorTop(rG, morph) - rG) * pow(h, ScTorExp(morph))
         + morph * (250.0 + 0.2 * rG) * smoothstep(0.45, 1.0, h)       // continuous trumpet, no lip
         + max(y - H, 0.0) * ScTorSlope(morph);                         // keeps widening into the cloud, no tank
}

// footprint of the debris dome: today's narrow dome until the elevator is erased (rG about 1230 m at 462 s), then the
// wide dome for the wedge (rG 1500); the elevator must not stand in the dust before it falls
float ScDomeR(float rG)
{
    float k = smoothstep(1250.0, 1500.0, rG);
    return rG * lerp(1.1, 1.5, k) + lerp(160.0, 300.0, k);
}

// the same, narrowed by the story's dome scale (_ScTor3.y): 0.75x at 0 (FunnelShape.DomeRadius(rG, scale))
float ScDomeR(float rG, float scale)
{
    return ScDomeR(rG) * lerp(0.75, 1.0, scale);
}

// ---------------------------------------------------------------------------------------------- anvil
// how far the sheet's underside dips over the updraft; it was 1500 m, and the sheet swallowed the tower above 7.5 km
#define SC_ANVIL_DIP 700.0

// the anvil's outline: > 0 inside the rim. ang = bearing around the storm, rim = the outline's smaller half-axis (m)
float ScAnvilOutline(float2 q, out float ang, out float rim)
{
    float reach = q.y < 0.0 ? _ScStorm.z : _ScAnvil.x;
    float u = q.y / reach;
    float halfW = lerp(_ScAnvil.y * 0.6, _ScAnvil.y, saturate(-u));
    float v = q.x / halfW;
    ang = atan2(q.x, -q.y);
    rim = min(reach, halfW);
    return 1.0 - sqrt(u * u + v * v) * (1.0 + 0.07 * sin(ang * 3.0 + 1.3) + 0.05 * sin(ang * 7.0 + 4.1));
}

// the sheet's underside and top for an eased edge weight e2 and the squared distance from the updraft
void ScAnvilBaseTop(float e2, float rq2, out float base, out float top)
{
    base = SC_ANVIL_BASE + 500.0 * (1.0 - e2) - SC_ANVIL_DIP * exp(-rq2 / (13000.0 * 13000.0));
    top = lerp(base + 420.0, SC_ANVIL_TOP, e2) + 2600.0 * exp(-rq2 / (5200.0 * 5200.0));   // overshooting top unchanged
}

// sheet thickness (m) for the shading, without texture taps: the thin rim glows, the heavy core over the updraft is slate
float ScAnvilThick(float2 pxz)
{
    float2 q = pxz - _ScStorm.xy;
    float ang, rim;
    float e = saturate(ScAnvilOutline(q, ang, rim) / 0.3);
    float base, top;
    ScAnvilBaseTop(e * e * (3.0 - 2.0 * e), dot(q, q), base, top);
    return top - base;
}

float SdAnvil(float3 p, float2 wxz, out float soft)
{
    soft = 300.0;
    float lowest = SC_ANVIL_BASE - 2400.0;
    if (p.y < lowest - 300.0) return lowest - p.y;
    float2 q = p.xz - _ScStorm.xy;
    float ang, rim;
    float m0 = ScAnvilOutline(q, ang, rim);
    // outside the rim: no texture taps (the fibres move m by at most 0.014, so this bound is safe)
    if (m0 < -0.12) return max(-(m0 + 0.014) * rim * 0.7, lowest - p.y);
    float m = m0;
    [branch] if (m0 < 0.16)
    {
        // fibrous rim: streaks radiating from the storm (cirrus), never islands. Domain-warped and two octaves at a
        // base frequency 2.6x lower than before, so the near rim is ragged, not a row of evenly spaced puffs, and the
        // side-on edge shows no venetian blinds
        float rq = length(q);
        float fw = ScNoise(float3(ang * 9000.0, 1900.0, rq * 0.11), 5200.0).a;
        float fu = ang * 23000.0 + (fw - 0.5) * 5200.0;
        float fib = ScNoise(float3(fu, 0.0, rq * 0.06), 5200.0).a * 0.65
                  + ScNoise(float3(fu * 1.9 + 2900.0, 1100.0, rq * 0.085), 5200.0).a * 0.35;
        m += (fib - 0.5) * 0.028 * (1.0 - smoothstep(0.03, 0.16, m0));   // > 0 inside the rim; streaks only at the rim
    }
    if (m < -0.1) return max(-m * rim * 0.7, lowest - p.y);
    float edge = saturate(m / 0.3);
    float e2 = edge * edge * (3.0 - 2.0 * edge);
    float rq2 = dot(q, q);
    float base, top;
    ScAnvilBaseTop(e2, rq2, base, top);
    // a billowed overshooting dome, not a smooth lens; it travels with the storm (M5, the tear)
    float dome = exp(-rq2 / (9000.0 * 9000.0));
    [branch] if (dome > 0.03 && p.y > top - 2600.0)
        top += (ScNoise(float3(q.x, 4200.0, q.y), 6500.0).r - 0.45) * 900.0 * dome;
    // mammatus: cellular lobes hanging under the sheet, anchored to the land (they never slide)
    float4 nm = ScNoise(float3(wxz.x, 700.0, wxz.y), 9800.0);
    float4 nm2 = ScNoise(float3(wxz.x, 1300.0, wxz.y), 4300.0);
    float lobe = smoothstep(0.30, 0.95, nm.g) * 0.75 + smoothstep(0.35, 0.95, nm2.g) * 0.25;
    base -= _ScStorm.w * 620.0 * lobe * smoothstep(0.1, 0.5, m);
    base += (nm2.a - 0.5) * 60.0;
    base += (nm.r - 0.5) * 240.0 * (1.0 - _ScStorm.w) * e2;   // the underside undulates before the mammatus arrive (same tap)
    float dv = max(base - p.y, p.y - top);
    soft = lerp(1800.0, 260.0, e2);
    return max(dv, -m * rim * 0.8) * lerp(0.7, 0.6, dome);
}

// ---------------------------------------------------------------------------------------------- tower
// the stacked plates are not a wedding cake: unequal radius steps, risers that start at different heights within
// each tier, and plate centres that drift (m), so the stack reads as uneven, leaning, off-centre plates
static const float SC_TIER_CUM[5] = { 0.0, 0.31, 0.47, 0.79, 1.0 };          // radius steps 0.31/0.16/0.32/0.21
static const float SC_TIER_RISE[4] = { 0.50, 0.66, 0.46, 0.60 };             // riser start within each tier (was 0.72 for all)
static const float SC_TIER_OFF[5] = { 700.0, -380.0, 460.0, -160.0, 0.0 };   // plate centre offsets

float SdTower(float3 p, out float soft)
{
    soft = 200.0;
    float h = saturate((p.y - SC_CLOUD_BASE) / (SC_ANVIL_TOP - SC_CLOUD_BASE));
    float2 q = p.xz - _ScStorm.xy - float2(0.0, -2200.0) * h;          // slight downshear tilt
    float r = length(q);
    float below = SC_CLOUD_BASE - 250.0 - p.y;
    if (r > SC_MESO_R * 2.3) return max(r - SC_MESO_R * 1.95, below);
    if (below > 0.0) return below;
    float vert = max(SC_CLOUD_BASE - p.y, p.y - SC_ANVIL_TOP);
    float flare = smoothstep(0.52, 0.84, h) * 0.8;
    // a conservative bound before any tap: wider than the true radius plus the offsets, lumps and barber pole
    float Rb = SC_MESO_R * (1.18 - 0.30 * saturate((h - 0.10) / 0.24) + flare) + 1800.0;
    float db = max(r - Rb, vert);
    if (db > 1500.0) return db - 1000.0;
    float2 qr = ScRot(q, -_ScAnvil.z);                                  // the whole tower turns, slowly
    float th = atan2(qr.y, qr.x);
    float hx = saturate(h / 0.34);
    float4 nt = float4(0.5, 0.5, 0.5, 0.5);
    [branch] if (h < 0.36)
    {
        float2 dir = qr / max(r, 1.0);
        nt = ScNoise(float3(dir.x * 1000.0, p.y * 0.5, dir.y * 1000.0), 6000.0);
    }
    // radius profile: a skirt of stacked plates at the base, a waist, a flare into the anvil. The plate lips lean
    // and wander around the tower (tier heights 763/1127/607/971 m)
    float lean = (0.22 * sin(th + 0.9) + 0.9 * (nt.a - 0.5)) * smoothstep(0.0, 0.15, hx) * (1.0 - smoothstep(0.85, 1.0, hx));
    float x = clamp(hx * 4.0 + 0.18 * sin(hx * 9.42478) + lean, 0.0, 3.999);
    int ti = min((int)x, 3);
    float k = smoothstep(SC_TIER_RISE[ti], 1.0, x - ti);
    float stair = lerp(SC_TIER_CUM[ti], SC_TIER_CUM[ti + 1], k);
    float off = lerp(SC_TIER_OFF[ti], SC_TIER_OFF[ti + 1], k);
    r = length(q - float2(1.0, 0.45) * off);                            // across the view: the plates are visibly not concentric
    float low = 1.0 - smoothstep(0.0, 0.34, h);
    float R = SC_MESO_R * (lerp(1.10, 0.74, stair) + flare);            // skirt 1.18 -> 1.10
    R += 430.0 * sin(4.0 * th + p.y / 820.0) * smoothstep(0.26, 0.42, h) * (1.0 - smoothstep(0.55, 0.8, h));   // barber pole
    R += (160.0 * sin(3.0 * th + 1.7) + 520.0 * (nt.r - 0.5)) * low;   // lumpy plate outlines
    float d = max(r - R, vert);
    if (d > 1500.0) return d - 1000.0;
    float3 pn = float3(qr.x, p.y, qr.y);
    float bill = smoothstep(0.27, 0.52, h);
    float4 nb = ScNoise(pn, 7600.0);
    d -= (nb.r - 0.38) * 1050.0 * bill;                                 // cauliflower flank up high
    // laminar plates low: grooves of 260 m with a y-stretch of 3 (no sub-pixel bands at 80 km)
    d -= (ScNoise(pn * float3(1.0, 3.0, 1.0), 6000.0).a - 0.5) * 260.0 * (1.0 - bill);
    return d * 0.6;                                                     // the steepest riser slope is now 5.3 (was 6.8)
}

// ---------------------------------------------------------------------------------------------- deck, wall cloud, tail cloud
float SdDeck(float3 p, float2 wxz, float dd, out float soft)
{
    soft = 190.0;
    float cover = _ScAnvil.w;
    if (cover <= 0.001) return SC_FAR;
    if (p.y > SC_DECK_TOP + 500.0) return p.y - SC_DECK_TOP - 200.0;
    if (p.y < SC_WALL_BASE - 900.0) return SC_WALL_BASE - 650.0 - p.y;
    float2 q = p.xz - _ScStorm.xy;
    float region = max(smoothstep(1500.0, 6000.0, dd), 1.0 - smoothstep(15000.0, 21000.0, length(q)));
    float c = saturate(cover * region);
    if (c <= 0.001) return 700.0;
    float4 n1 = ScNoise(float3(wxz.x, 250.0, wxz.y), 5200.0);          // scud texture
    float4 n0 = ScNoise(float3(wxz.x, 900.0, wxz.y), 23000.0);         // the pattern of the tear
    float2 wq = p.xz - _ScWall.xy;
    float wr = length(wq);
    float wa = atan2(wq.y, wq.x) + _ScWall.w;
    float ragged = 1.0 + 0.16 * sin(wa * 3.0) + 0.10 * sin(wa * 7.0 + 1.3);
    // the lowered wall cloud reaches past the funnel's cap (wedge: 3544 m, it was 2790*ragged), so the flare never
    // pokes out from under it; ragged only on the outer edge, so the mask never inverts (FunnelShape.WallInner)
    float wIn = max(1100.0, (1.1 * ScTorCap(_ScTor.z, _ScTor2.x) + 150.0) * step(0.001, _ScTor.w));
    float wMask = smoothstep(wIn + 1400.0 * ragged, wIn, wr);
    float wallK = wMask * saturate(4.0 * max(_ScWall.z, _ScTor.w));
    // under the wall the base noise calms down: the funnel's cap stays inside the cloud (H+350 is at least 144 m in)
    float base = SC_CLOUD_BASE + ((n1.r - 0.5) * 420.0 + (n0.a - 0.5) * 500.0) * lerp(1.0, 0.35, wallK);
    float wall = _ScWall.z * (SC_CLOUD_BASE - SC_WALL_BASE) * wMask;
    float2 td = float2(0.75, 0.66);                                     // tail cloud reaches toward the rain (north-east)
    float along = dot(wq, td);
    float across = abs(dot(wq, float2(-td.y, td.x)));
    float tail = _ScWall.z * 650.0 * smoothstep(700.0, 150.0, across) * smoothstep(7500.0, 2000.0, along) * step(0.0, along);
    base -= max(wall, tail) * (0.85 + 0.3 * n1.g);
    float d = max(base - p.y, p.y - SC_DECK_TOP);
    // holes open where the slow noise is high, never under the wall around a funnel; they return as the wall and the
    // funnel go (603-608), so no unholed disc of deck lingers in the tear
    d += (1.0 - c) * (760.0 + 2600.0 * n0.a) * (1.0 - wallK);
    return d * 0.75;
}

// ---------------------------------------------------------------------------------------------- shelf (arcus)
float SdShelf(float3 p, float2 wxz, float dd, out float soft)
{
    soft = 120.0;
    float amt = _ScShelf.z;
    if (amt <= 0.001) return SC_FAR;
    if (p.y > SC_DECK_TOP + 300.0) return p.y - SC_DECK_TOP;
    if (dd < -2500.0) return -dd - 900.0;
    float4 n = ScNoise(float3(wxz.x * 0.35, p.y * 1.6, wxz.y), 4200.0);    // stretched along the line: laminar
    float4 nt = ScNoise(float3(wxz.x, 0.0, wxz.y), 2600.0);                // turbulent underside
    dd += (n.a - 0.5) * 520.0;                                             // scalloped lip
    float back = max(dd, 0.0);
    float top = SC_SHELF_EDGE + 110.0 + back * 0.60;                       // smooth face rising back at ~31 degrees
    top += 70.0 * sin(back / 210.0 + n.r * 3.0);                           // laminar tiers
    top = min(top, SC_DECK_TOP);
    float bot = SC_SHELF_EDGE + 520.0 * (1.0 - exp(-back / 1300.0))
              + (SC_CLOUD_BASE - SC_SHELF_EDGE - 520.0) * smoothstep(2600.0, 7500.0, back);
    bot += (nt.g - 0.5) * 300.0 * smoothstep(100.0, 900.0, back);          // whale's mouth
    top = lerp(bot + 40.0, top, amt);
    float d = max(max(p.y - top, bot - p.y), -dd * 0.8);
    return d * 0.7;
}

// ---------------------------------------------------------------------------------------------- funnel
// (ScSmin and the profile helpers ScTorTop .. ScFunnelR sit above the anvil: the deck needs them too)
float SdFunnel(float3 p, out float soft)
{
    soft = 60.0;
    float desc = _ScTor.w;
    if (desc <= 0.001) return SC_FAR;
    float H = _ScTorRot.z;
    float2 q0 = p.xz - _ScTor.xy;
    float r0 = length(q0);
    float rG = _ScTor.z;
    float morph = _ScTor2.x, rope = _ScTor2.y;
    float rMax = ScTorCap(rG, morph) * 1.15 + 600.0 + rope * 900.0;    // the widest the funnel gets, at its cap
    if (r0 > rMax + 900.0 || p.y > H + 900.0) return max(r0 - rMax, p.y - H - 350.0);
    float h = saturate(p.y / H);
    float t = _ScOrigin.z;
    // the rope wanders and is dragged sideways at the ground
    float2 bend = rope * float2(sin(h * 5.1 + t * 0.35), cos(h * 3.7 - t * 0.27)) * (130.0 + 240.0 * (1.0 - h)) * (1.0 - h * h);
    bend += rope * float2(0.92, 0.4) * 800.0 * (1.0 - h) * (1.0 - h);
    float2 q = q0 - bend;
    float r = length(q);
    float th = atan2(q.y, q.x);
    // a wedge is a broad trunk that widens as it rises and flares into the wall cloud: an inverted cone of black
    // standing on its debris dome (no collar, no lip, no tank: it keeps widening into the cloud above its top)
    float R = ScFunnelR(p.y);
    R += rG * 0.13 * morph * sin(th * 4.0 - _ScTorRot.y * 4.0) * exp(-p.y / 420.0);   // sub-vortices orbiting the base
    float tipY = H * (1.0 - desc);                                                      // it descends from the wall cloud
    R *= saturate((p.y - tipY) / (H * 0.14) + 0.02);
    float d = max(r - R, tipY - p.y);
    if (d > 520.0) return d - 380.0;
    // the surface lives in the turning, helically sheared frame of the shell: bulges turn with it, the
    // striations lean as they rise and slide sideways - the visible proof of the wind speed
    float phi = th - _ScTorRot.x + p.y / lerp(420.0, 1500.0, morph);
    float2 cs = float2(cos(phi), sin(phi)) * 2400.0;
    float4 nb = ScNoise(float3(cs.x, p.y * 0.55, cs.y), 2600.0);                     // large bulges
    float4 ns = ScNoise(float3(cs.x * 2.3, p.y * 0.15, cs.y * 2.3), 2600.0);         // long thin vertical bands
    float stri = 1.0 - abs(ns.a * 2.0 - 1.0);
    d -= (nb.r - 0.45) * (40.0 + R * 0.2);
    d -= (stri - 0.55) * (18.0 + R * 0.08);
    // ragged, translucent skin: condensation and dust torn off the edge, rising
    float4 nf = ScNoise(float3(cs.x * 5.0, p.y * 1.1 - t * 9.0, cs.y * 5.0), 2600.0);
    d -= (nf.b - 0.5) * (14.0 + R * 0.05);
    soft = 26.0 + R * 0.085;
    // capped 350 m above its top, always inside the cloud base: no flat lid ever shows, even while the holes open
    return max(d, p.y - (H + 350.0)) * 0.7;
}

// ---------------------------------------------------------------------------------------------- debris cloud
// a churning dome of dust and debris around the foot, flung up and out and spreading wider than the foot (a soft
// ellipsoid, never a flat-topped bar); sub-vortices stand in it as taller, denser fountains that orbit the core
// faster than the shell turns
float SdDebris(float3 p, out float soft)
{
    soft = 300.0;
    float amt = _ScTor2.w * step(0.999, _ScTor.w);
    // beat 6 (490-546 s): the vortex passes about 2 km from the truck, and a full dome would fill the glass. The story
    // holds it low (_ScTor3.y = domeScale, 0.45 there, else 1): the height and the fountains scale by it, the footprint
    // by lerp(0.75, 1, it) (ScDomeR). A scale of 0 (uniform never set) draws no dome rather than a 0 m sheet.
    float ds = _ScTor3.y;
    if (amt * ds <= 0.001) return SC_FAR;
    float rG = _ScTor.z;
    float HD = amt * (220.0 + 0.42 * rG) * ds;
    // the highest surface is a fountain top: 1.8 HD, +35 m from the smooth union, +120 m from the n2 rag. 'p.y - 1.9 HD'
    // overstepped it by up to ~120 m once the dome is held low (small HD), so the bound is now measured from there
    if (p.y > HD * 1.9 + 300.0) return p.y - HD * 1.8 - 160.0;
    float2 q = p.xz - _ScTor.xy;
    float r = length(q);
    float Rx = ScDomeR(rG, ds);
    if (r > Rx + 1400.0) return r - Rx - 700.0;
    float t = _ScOrigin.z;
    float2 qr = ScRot(q, -_ScTorRot.y);
    float yN = saturate(p.y / max(HD, 1.0));
    float3 pn = float3(qr.x, p.y * 1.3 - t * 28.0, qr.y);
    float4 n = ScNoise(pn, 1500.0);
    float4 n2 = ScNoise(pn * float3(1.0, 0.8, 1.0) + 517.0, 560.0);
    // iq's ellipsoid distance: a dome as wide as Rx and Ht tall that stays Lipschitz-safe at the rim
    float Ht = max(HD * (0.7 + 0.6 * n.r), 1.0);
    float k0 = length(float2(r / Rx, p.y / Ht));
    float k1 = length(float2(r / (Rx * Rx), p.y / (Ht * Ht)));
    float d = k0 * (k0 - 1.0) / max(k1, 1e-6);
    float colR = 45.0 + rG * 0.06;
    for (int k = 0; k < 4; k++)
    {
        float a = _ScTorRot.y * 1.6 + k * 1.5707963;
        float2 c = float2(cos(a), sin(a)) * rG * 1.02;
        float dc = length(q - c) - colR * (1.0 + 0.8 * yN);
        dc = max(dc, p.y - HD * (1.3 + 0.5 * n.g));
        d = ScSmin(d, dc, 140.0);
    }
    d -= (n2.b - 0.5) * 240.0;
    return d * 0.7;
}

// ---------------------------------------------------------------------------------------------- dust wall of the gust front
float SdDust(float3 p, float2 wxz, out float soft)
{
    soft = 260.0;
    float amt = _ScDust.z;
    if (amt <= 0.001) return SC_FAR;
    if (p.y > 1500.0) return p.y - 1100.0;
    float xr = p.x - _ScShelf.y;
    float dd = p.z - (_ScDust.x + xr * xr / 190000.0);
    if (dd < -1200.0) return -dd - 500.0;
    float t = _ScOrigin.z;
    float4 n = ScNoise(float3(wxz.x, p.y * 1.3 - t * 16.0, wxz.y + t * 22.0), 1500.0);
    float Hd = _ScDust.y * amt * (1.0 - exp(-max(dd, 0.0) / 380.0)) * (0.65 + 0.7 * n.r);
    float d = max(max(p.y - Hd, -dd), dd - 4200.0);
    return (d - (n.g - 0.5) * 240.0) * 0.7;
}

// ---------------------------------------------------------------------------------------------- union
// mat: 1 anvil, 2 tower, 3 deck/wall cloud, 4 shelf, 5 funnel, 6 debris, 7 dust
// fw: 0 wall cloud .. 1 funnel across the blend band where the funnel grows out of the cloud; the shading lerps the two
// materials by it, so the joint has no luminance step (0 wherever there is no funnel)
float MapClouds(float3 p, out float soft, out float mat, out float fw)
{
    fw = 0.0;
    float2 wxz = p.xz + _ScOrigin.xy;
    float s;
    float d = SdAnvil(p, wxz, s); soft = s; mat = 1.0;
    float dt = SdTower(p, s); if (dt < d) { d = dt; soft = s; mat = 2.0; }
    if (p.y < SC_DECK_TOP + 900.0)
    {
        float dd = ShelfBehind(p.xz);
        float x;
        x = SdDeck(p, wxz, dd, s); if (x < d) { d = x; soft = s; mat = 3.0; }
        x = SdShelf(p, wxz, dd, s); if (x < d) { d = x; soft = s; mat = 4.0; }
        x = SdFunnel(p, s);
        // the blend radius grows with the funnel (a wide wedge melts into a wide collar, a rope into a narrow one)
        float rT = ScTorTop(_ScTor.z, _ScTor2.x);
        float kf = lerp(220.0, clamp(0.35 * rT, 300.0, 800.0), _ScTor2.x);
        fw = saturate(0.5 + 0.5 * (d - x) / clamp(150.0 + 0.3 * rT, 250.0, 700.0));
        if (x < d + kf) { float ds = ScSmin(d, x, kf); if (x < d) { soft = s; mat = 5.0; } d = ds; }
        x = SdDebris(p, s); if (x < d) { d = x; soft = s; mat = 6.0; }
        x = SdDust(p, wxz, s); if (x < d) { d = x; soft = s; mat = 7.0; }
    }
    else d = min(d, p.y - SC_DECK_TOP - 500.0);
    return d;
}

// the field without the blend weight: normals, AO, sun visibility, the flash probe and the bisection
float MapClouds(float3 p, out float soft, out float mat)
{
    float fw;
    return MapClouds(p, soft, mat, fw);
}

// thin media that have no surface: the rain/hail core, the rain wrap around the vortex, haze. Extinction in 1/m.
// edge: a world-anchored, vertically stretched noise (0..1) from the same tap as the fall streaks; it rags the edge of
// the funnel's flash shadow in the rain, so the shadow reads as a curtain and costs no extra sample
float ThinMedia(float3 p, out float rainPart, out float edge)
{
    float dd = ShelfBehind(p.xz);
    float2 wxz = p.xz + _ScOrigin.xy;
    float4 sn = ScNoise(float3(wxz.x, p.y * 0.12, wxz.y), 6400.0);
    float shafts = 0.55 + 0.9 * sn.a;                                                       // fall streaks
    edge = sn.g;
    float rain = _ScRain.x * smoothstep(900.0, 3600.0, dd) * step(p.y, SC_CLOUD_BASE) * shafts;
    float2 wq = p.xz - _ScWall.xy;
    rain *= lerp(1.0, smoothstep(_ScRain.y * 0.7, _ScRain.y * 1.15, length(wq)), saturate(_ScWall.z * 1.3));
    // the tear: the first clear air of the game. The rain behind the old shelf line covered the whole sky below the
    // base and read as a blue haze; now only a foot of rain stays under the departing storm, lit by its core light
    float2 sq = p.xz - _ScStorm.xy - float2(0.0, 3000.0);
    rain *= lerp(1.0, smoothstep(13000.0, 7000.0, length(sq)), _ScClear.x);
    float2 tq = p.xz - _ScTor.xy;
    float tr = length(tq);
    float wrapR = _ScTor.z * 1.5 + 900.0;
    float ta = atan2(tq.y, tq.x) - _ScTorRot.x * 0.6;
    float spiral = smoothstep(0.2, 0.8, sin(ta * 2.0 + tr / 650.0) * 0.5 + 0.5);       // two curtains wound around the core
    float ring = smoothstep(_ScTor.z * 0.9, _ScTor.z * 1.25, tr) * smoothstep(wrapR * 1.3, wrapR * 0.9, tr);
    float wrap = _ScRain.z * _ScTor2.z * ring * spiral * step(p.y, SC_WALL_BASE + 700.0) * shafts;
    rainPart = rain + wrap;
    float mist = _ScRain.w * saturate(_ScWall.z) * smoothstep(13000.0, 6000.0, length(wq)) * exp(-max(p.y, 0.0) / 1000.0);
    return rainPart + mist;
}

// the forward-flank core beside the rain-free base (beats 1-2): a rain wall of leaning fall streaks with a whiter
// hail shaft at its updraft (west) end. Its texture is anchored to the core, so it travels with the storm and never
// slides; it evolves over minutes. Extinction in 1/m; hail = 0..1 how much of it is hail.
float ScCore(float3 p, out float hail)
{
    hail = 0.0;
    float2 q = p.xz - _ScPrecip.xy;
    float2 u = q / float2(SC_CORE_AX, SC_CORE_AZ);
    float e = (length(u) - 1.0) * SC_CORE_AZ;                          // ~ metres outside the rim
    if (_ScPrecip.z <= 0.0 || e > SC_CORE_EDGE || p.y > SC_CORE_TOP) return 0.0;
    float foot = smoothstep(SC_CORE_EDGE, -SC_CORE_EDGE, e);
    float2 lw = q + float2(-0.30, 0.08) * (SC_CLOUD_BASE - p.y);       // streaks lean: bottoms ~840 m east of their tops
    float n = ScNoise(float3(lw.x, p.y * 0.04 + _ScOrigin.z * 12.0, lw.y), 2400.0).a;
    float streak = 0.35 + 1.3 * smoothstep(0.3, 0.7, n);
    float vert = 1.0 - smoothstep(SC_CLOUD_BASE - 600.0, SC_CORE_TOP, p.y);   // continuous through the base, fades upward
    hail = smoothstep(-0.30, -0.62, u.x);                               // the 2-3 km nearest the updraft
    return lerp(_ScPrecip.z, _ScPrecip.w, hail) * foot * streak * vert;
}

// the ray's interval through the core's bounding cylinder (footprint plus rim, ground to SC_CORE_TOP); the march
// uses it to land samples inside the core, which has no surface to find. (1e9, -1e9) = miss or off
float2 ScCoreSpan(float3 ro, float3 rd)
{
    float2 none = float2(1.0e9, -1.0e9);
    if (_ScPrecip.z <= 0.0) return none;
    float2 ab = float2(SC_CORE_AX, SC_CORE_AZ) * (1.0 + SC_CORE_EDGE / SC_CORE_AZ);
    float2 o = (ro.xz - _ScPrecip.xy) / ab, d = rd.xz / ab;
    float A = dot(d, d), B = dot(o, d), C = dot(o, o) - 1.0;
    float disc = B * B - A * C;
    if (A < 1.0e-12 || disc <= 0.0) return none;
    float sq = sqrt(disc);
    float t0 = max((-B - sq) / A, 0.0), t1 = (-B + sq) / A;
    if (rd.y > 1.0e-5) t1 = min(t1, (SC_CORE_TOP - ro.y) / rd.y);
    return t1 > t0 ? float2(t0, t1) : none;
}

#endif
