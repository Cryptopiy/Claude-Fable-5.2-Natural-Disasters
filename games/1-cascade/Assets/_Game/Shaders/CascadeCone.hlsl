#ifndef CASCADE_CONE_INCLUDED
#define CASCADE_CONE_INCLUDED

// The volcano as one analytic height function (used by the cone mesh, the plume pass, the blanket and the sea shader).
// h(x,z) = old surface - the failed flank (ConeScar) + the moving mass (ConeMass) + the rubble it leaves on the floor.
// Before the slide the mass sits exactly where the scar will be, so the cone is whole (and the scar code is skipped).
float4 _ConeSlide;      // x slide distance (m), y thickness factor of the moving mass (1 -> 0.16), z fracture 0..1, w slide azimuth (rad)
float4 _ConeGlow;       // x lava flows 0..1, y crater glow 0..1, z ash coat 0..1, w unused

#define CONE_H 1800.0
#define CONE_R 2300.0

// The summit crater (ConeDirector.Profile mirrors these: keep them in sync). The cone is cut at CRATER_RIM by a soft
// minimum, a bowl CRATER_R wide and CRATER_DEPTH deep is sunk into the cut and a low lip rings it. ConeSmooth's shoulder
// adds ~65 m here and tilts it: floor ~1535 m, rim ~1620 m toward the village, ~1690 m on the far (shoulder) side.
// The vent sits on the floor (ConeDirector.VentDepth), so from the village the near rim hides it by ~130 m.
#define CRATER_RIM   1585.0
#define CRATER_K     45.0        // softness of the cut (m): a rounded rim, no apex cusp
#define CRATER_R     150.0       // bowl radius = the rim crest (m)
#define CRATER_DEPTH 115.0       // bowl depth below the cut (m)

// Polar noise lookups. T_Noise2D holds 4 (R), 8 (G), 6 (B, cellular) and 16 (A) cells per texture unit, so a lookup at
// x = phi * N / 2pi has 4N..16N features per turn, and the texture's tiling repeats them N times round the cone. Every
// polar lookup here is therefore 'helical': y also advances k whole units per turn (k * phi / 2pi; seamless at phi = pi
// because the texture tiles), so the N repeats sample different rows and nothing repeats round the cone.
// Erosion gullies: the creases of ridged A noise (LOD 1: no pixel-scale octave), CONE_GULLY_N periods per turn: ~55-75
// gullies per turn, ~75 m apart near the summit, ~140 m at mid-flank, ~230 m by the shore; ~900 m long cells.
#define CONE_GULLY_N 5.0

// The collapse amphitheatre in slide coordinates (u down the slide from the axis, v lateral; metres).
// ConeDirector (ConeBuilder.cs) mirrors these numbers on the CPU: keep them in sync.
#define SCAR_FLOOR 1000.0       // floor level at the axis, under the vent (ConeDirector.VentDrop leaves the vent 55 m above it)
#define SCAR_UC    -60.0        // centre of the rounded head: the head scarp lies about 0.85 * SCAR_R0 behind it
#define SCAR_R0    470.0        // radius of the head; the side walls splay from it downhill
#define SCAR_SPLAY 0.36         // growth of the half width per metre downhill
#define SCAR_RUN   2250.0       // the floor's concave chute meets sea level here, at the old shore

float ConeProfile(float rho)
{
    float t = saturate(1.0 - rho / CONE_R);
    float h = CONE_H * pow(t, 1.85);
    // the summit: the concave cone would end in a 55-degree spike; cut it at the rim level (soft minimum), sink a bowl
    // into the cut and ring it with a lip (rim crest ~1588 m at rho 150, floor ~1470 m)
    float hh = max(CRATER_K - abs(h - CRATER_RIM), 0.0) / CRATER_K;
    h = min(h, CRATER_RIM) - hh * hh * CRATER_K * 0.25;
    h -= CRATER_DEPTH * saturate(1.0 - rho * rho / (CRATER_R * CRATER_R));
    float rr = (rho - (CRATER_R + 15.0)) / 30.0;
    h += 16.0 * exp(-rr * rr);
    h -= max(rho - CONE_R, 0.0) * 0.45;                            // dives under the sea beyond the shore
    return h;
}

// The smooth old surface (profile, shoulder, parasitic cone; no noise) at offset d from the axis.
float ConeSmooth(float2 d)
{
    float h = ConeProfile(length(d));
    float2 sh = d - float2(-620.0, 540.0);
    h += 320.0 * exp(-dot(sh, sh) / (650.0 * 650.0));                                        // a shoulder
    float2 pc = d - float2(950.0, -820.0);
    h += 140.0 * exp(-dot(pc, pc) / (260.0 * 260.0));                                        // a parasitic cone
    return h;
}

float2 ConeSlideDir() { return float2(cos(_ConeSlide.w), sin(_ConeSlide.w)); }

float2 ConeSlideUV(float2 d)
{
    float2 sd = ConeSlideDir();
    return float2(dot(d, sd), d.x * sd.y - d.y * sd.x);
}

// Floor of the amphitheatre along the slide axis: nearly level under the vent, rising a little into the head, then a
// concave chute that reaches sea level at SCAR_RUN. The soft ramp keeps the floor free of kinks.
float ConeScarFloor(float u)
{
    float ramp = 0.5 * (u + sqrt(u * u + 300.0 * 300.0)) - 150.0;
    float f = 1.0 - min(ramp, SCAR_RUN) / SCAR_RUN;
    return SCAR_FLOOR * f * f;
}

// The flank that fails, in slide coordinates. x: thickness above the amphitheatre floor (m); y: the horseshoe distance
// e (0 on the axis, ~0.83 on the rim, ellipse-rounded behind SCAR_UC); z: the wall mask (1 inside .. 0 outside).
// A rounded head scarp behind the summit (the notch in the silhouette), ragged walls that splay downhill and a U floor.
// Measuring the thickness down to a designed floor (not a constant depth) is what makes the floor a U: the cone falls
// away sideways, so a constant depth would leave a ridge along the axis.
float3 ConeScar(float u, float v)
{
    float ragged = 110.0 * (CNoise2(float2(u / 520.0, v > 0.0 ? 0.23 : 0.71)) - 0.5);       // slumped, ragged walls
    float halfW = SCAR_R0 + SCAR_SPLAY * max(u - SCAR_UC, 0.0);
    float e = length(float2(min(u - SCAR_UC, 0.0) / SCAR_R0, (abs(v) + ragged) / halfW));
    float lat = 1.0 - smoothstep(0.68, 1.0, e);                                            // steep walls (~45-60 degrees)
    float2 sd = ConeSlideDir();
    float above = max(ConeSmooth(u * sd + v * float2(sd.y, -sd.x)) - ConeScarFloor(u), 0.0);
    return float3(above * lat * (1.0 - 0.4 * e * e), e, lat);                               // (1 - 0.4 e^2): the floor curves up into the walls
}

float ConeScarThickness(float u, float v) { return ConeScar(u, v).x; }

// Thickness of the moving mass over slide coordinates uv (m), before _ConeSlide.y thins it. At s = 0 it is exactly the
// failed flank (the cone is whole); within the first third of the run it slumps into a rounded lobe whose edges thin.
float ConeMass(float2 uv)
{
    float s = _ConeSlide.x;
    float k = saturate(s / 2600.0);
    float u = (uv.x - s) * (1.0 - 0.15 * k), v = uv.y * (1.0 - 0.2 * k);
    float3 sc = ConeScar(u, v);
    float eL = length(float2((u - 250.0) / 760.0, v / 520.0));
    float q = saturate(1.0 - eL * eL);
    float lobe = 470.0 * q * sqrt(q);
    float m = smoothstep(0.0, 0.35, k);
    return lerp(sc.x, lobe, m) * (1.0 - 0.5 * k * smoothstep(0.4, 1.0, lerp(sc.y, eL, m)));
}

// Where the slide has left hummocky rubble on the amphitheatre floor (0..1): behind the passing mass, off the walls,
// and not out to the shore (the coastline stays where the sea shaders expect it).
float ConeDepositMask(float2 uv, float e)
{
    float s = _ConeSlide.x;
    return smoothstep(uv.x + 350.0, uv.x + 1100.0, s) * (1.0 - smoothstep(0.45, 0.85, e)) * (1.0 - smoothstep(1700.0, 2150.0, uv.x));
}

// Lateral wander of the gullies (rad): gentle meanders, ~50 m at mid-flank over ~750 m. Shared by ConeHeight, the flank
// shading (streaks, old flows) and the lava flows, so all of them follow one grain down the slope. (A stronger, shorter
// wander bent whole sectors of gullies in step: a fingerprint, not erosion.)
float ConeWander(float phi, float rho)
{
    return (CNoise2All(float2(phi * (1.0 / 6.2831853), rho / 12000.0 + 0.7)).a - 0.5) * 0.2;
}

// Height above sea level at world xz. detail = 0 for cheap lookups (plume), 1 with fine noise.
// Every relief scale here is at least ~90 m across: deep relief finer than that aliased into knife-edged fins on the
// 640-sector mesh (a sawtooth silhouette, 'wood chip' shading, fur on the scar walls, lava flows broken into dashes).
float ConeHeight(float2 xz, float detail)
{
    float2 d = xz - _CCone.xz;
    float rho = length(d);
    if (rho > CONE_R + 700.0) return -300.0;
    float phi = atan2(d.y, d.x);
    float hel = phi * (1.0 / 6.2831853);                                                      // helical row offset (see above)
    float h = ConeSmooth(d);
    // erosion relief: broad asymmetric ribs (~600 m), secondary ribs (~250 m), then V gullies on the creases of ridged
    // noise running down the fall line (25-45 m deep), deepest on the mid flanks
    float flank = sin(3.14159 * saturate(rho / CONE_R));
    float g = CNoise2(float2(phi * (3.0 / 6.2831853), rho / 2600.0 + 0.3));
    float g2 = CNoise2All(float2(phi * (6.0 / 6.2831853), rho / 2000.0 + 0.1 + hel)).g;
    float rel = ((g - 0.5) * 260.0 + (g2 - 0.5) * 70.0) * flank;
    float wander = ConeWander(phi, rho);
    float gA = SAMPLE_TEXTURE2D_LOD(_CNoise2D, s_linear_repeat_sampler, float2((phi + wander) * (CONE_GULLY_N / 6.2831853), rho / 14400.0 + 0.11 + hel), 1.0).a;
    float r1 = 1.0 - abs(2.0 * gA - 1.0);
    float gullyK = smoothstep(200.0, 800.0, rho) * (1.0 - smoothstep(CONE_R - 350.0, CONE_R + 150.0, rho));
    rel += (0.55 - pow(saturate(r1), 1.5)) * 70.0 * gullyK;
    if (detail > 0.5)
    {
        // fine relief: only in the per-pixel normal (the mesh is too coarse for it). Rills between the gullies (~45 m
        // apart at mid-flank, on the same grain) and soft lumps (~40-90 m); nothing deep at the 2-7 m pixel of the village.
        float r2 = 1.0 - abs(2.0 * CNoise2All(float2((phi + wander) * (20.0 / 6.2831853), rho / 1500.0 + 0.53 + 3.0 * hel)).g - 1.0);
        rel += (0.5 - pow(saturate(r2), 1.5)) * 16.0 * gullyK;
        rel += (CNoise2All(xz / 700.0).g - 0.5) * 30.0 * flank + (CNoise2(xz / 150.0) - 0.5) * 8.0;
    }
    // a notched crater rim
    float rimN = CNoise2(float2(phi * (4.0 / 6.2831853), 0.61 + hel));
    float rr = (rho - (CRATER_R + 10.0)) / 45.0;
    h -= 30.0 * smoothstep(0.55, 0.8, rimN) * exp(-rr * rr);
    // the scar, the sliding mass and the rubble it leaves (all cancel exactly before the slide, so skip them then)
    float s = _ConeSlide.x;
    if (s > 0.5)
    {
        float2 uv = ConeSlideUV(d);
        float3 sc = ConeScar(uv.x, uv.y);
        // the fresh amphitheatre is not the old eroded flank: keep only a trace of the gullies on its walls and floor
        rel *= 1.0 - 0.7 * sc.z * saturate(s / 300.0);
        float Tm = ConeMass(uv);
        Tm *= 1.0 - smoothstep(CONE_R - 500.0, CONE_R + 250.0, rho) * saturate(s / 300.0);        // the sea takes it
        float2 um = float2(uv.x - s, uv.y);
        float lump = CNoise2All(um / 760.0).r * 0.6 + CNoise2All(um / 290.0).g * 0.4;
        h += -sc.x + Tm * _ConeSlide.y * lerp(1.0, 0.45 + 1.1 * lump, saturate(s / 250.0));
        float hum = CNoise2All(uv / 330.0 + 0.37).g;
        h += 26.0 * ConeDepositMask(uv, sc.y) * (0.25 + 1.5 * hum * hum);
    }
    return h + rel;
}

// 0 outside the scar, 1 deep inside it.
float ConeScarMask(float2 xz)
{
    float2 uv = ConeSlideUV(xz - _CCone.xz);
    return saturate(ConeScar(uv.x, uv.y).x / 240.0);
}

// How much of the moving mass covers this point (0..1). Before the slide that is the whole flank that will fail.
float ConeDebrisMask(float2 xz)
{
    float2 d = xz - _CCone.xz;
    float sea = 1.0 - smoothstep(CONE_R - 500.0, CONE_R + 250.0, length(d)) * saturate(_ConeSlide.x / 300.0);   // as in ConeHeight
    return saturate(ConeMass(ConeSlideUV(d)) * _ConeSlide.y * sea / 60.0);
}

#endif
