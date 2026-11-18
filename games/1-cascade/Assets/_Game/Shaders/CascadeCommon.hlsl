#ifndef CASCADE_COMMON_INCLUDED
#define CASCADE_COMMON_INCLUDED

// Globals published by EnvDirector / StagingBridge every frame. Radiometric values are in nits or lux (HDRP units):
// multiply by GetCurrentExposureMultiplier() before writing colour (the Unlit forward pass does this for emissiveColor).
float4 _CSunDir;        // xyz: direction TO the sun
float4 _CSunColor;      // surface sun colour * lux at the player (already dimmed by the ash)
float4 _CSunRaw;        // the sun above the ash: colour * lux
float4 _CSkyZenith;     // nits
float4 _CSkyHorizon;    // nits
float4 _CFogColor;      // nits
float _CTime;           // story clock, s
float4 _CClocksA;       // eruption, collapse, drawdown, wave (seconds since start, -1 before)
float4 _CClocksB;       // ash storm, silence, arrival, tremor level
float4 _CState;         // darkness, storm, night, ashfall
float4 _CCone;          // cone centre xyz, height
float4 _CVent;          // xyz the vent (absolute)
float4 _CVentGlow;      // rgb nits of the vent glow
// cross-system light sources (all zero = off)
float4 _CStrip;         // rgb nits of the daylight strip under the edge of the ash umbrella, w = 1/scale height of the sunlit air (1/m)  (EnvDirector)
float4 _CStrip2;        // xy centre of the umbrella's shadow footprint (xz), z its radius (m), w exit distance toward the cone (m)  (EnvDirector)
float4 _CBolt;          // xyz the current lightning channel midpoint (absolute), w its intensity (lumen-like, 0 = none)  (ParticleDirector)
float4 _CBeam;          // xyz the lighthouse lantern (absolute), w beam intensity (0 = off): the in-scatter (nits) of the beam
                        // crossed side-on 100 m from the lantern, in ash fog of 200 nits; Plume.shader scales it with the fog
                        // luminance, so it always reads as light in the ash  (ParticleDirector)
float4 _CBeam2;         // xyz beam axis (unit, both directions lit), w cos of the beam half angle  (ParticleDirector)
float4 _PlUnder;        // rgb nits of the fire light on the underside of the ash lid, w its radial scale (m)  (PlumeDirector)

float4 _PalCalderaTeal, _PalDeepWater, _PalCliffDark, _PalCliffOchre, _PalCliffRed, _PalAshGrey, _PalAshBrown;
float4 _PalSunRed, _PalEmber, _PalLava, _PalSteam, _PalWindowAmber, _PalSilt, _PalWeed, _PalFoam, _PalWhitewash, _PalDaySky;

TEXTURE3D(_CNoise3D);   // R billow fbm, G smooth fbm, B cellular, A billow (other seed); tiling
TEXTURE2D(_CNoise2D);   // R,G,B,A: four independent smooth fbm layers; tiling

float CHash11(float n) { return frac(sin(n * 127.1) * 43758.5453); }
float CHash21(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}
float3 CHash33(float3 p)
{
    p = frac(p * float3(0.1031, 0.1030, 0.0973));
    p += dot(p, p.yxz + 33.33);
    return frac((p.xxy + p.yxx) * p.zyx);
}
uint CWang(uint s)
{
    s = (s ^ 61u) ^ (s >> 16); s *= 9u; s = s ^ (s >> 4); s *= 0x27d4eb2du; s = s ^ (s >> 15);
    return s;
}
float CRand(uint s) { return (CWang(s) & 0xFFFFFFu) / 16777216.0; }

float CNoise2(float2 uv) { return SAMPLE_TEXTURE2D_LOD(_CNoise2D, s_linear_repeat_sampler, uv, 0).r; }
float4 CNoise2All(float2 uv) { return SAMPLE_TEXTURE2D_LOD(_CNoise2D, s_linear_repeat_sampler, uv, 0); }
float4 CNoise3(float3 p, float lod) { return SAMPLE_TEXTURE3D_LOD(_CNoise3D, s_trilinear_repeat_sampler, p, lod); }

float CSmooth(float a, float b, float x) { float t = saturate((x - a) / (b - a)); return t * t * (3.0 - 2.0 * t); }

// Interleaved gradient noise with a story-clock index (seek-stable, unlike _FrameCount).
float _CFrameIdx;       // rendered frame index mod 64 (EnvDirector)
float CIgn(float2 pix)
{
    float f = fmod(floor(_CTime * 60.0) + _CFrameIdx, 64.0);
    pix += 5.588238 * f;
    return frac(52.9829189 * frac(0.06711056 * pix.x + 0.00583715 * pix.y));
}

// Analytic sky for custom shaders (reflections, ambient). Not the visible sky: that is HDRP's, plus the plume pass.
float3 CSky(float3 d)
{
    float h = saturate(d.y);
    float3 c = lerp(_CSkyHorizon.rgb, _CSkyZenith.rgb, pow(h, 0.42));
    float s = saturate(dot(d, _CSunDir.xyz));
    c += _CSunRaw.rgb * (1.0 - _CState.x) * (0.02 * pow(s, 8.0) + 0.3 * pow(s, 180.0)) * 0.05;
    // the daylight strip beyond the edge of the umbrella's shadow: a bright seam on the horizon, as tall as the sunlit
    // air seen at the exit distance (zero = off). In the water the skyline band hides it behind the cliffs and the cone.
    c += _CStrip.rgb * (exp(-max(d.y, 0.0) * _CStrip2.w * _CStrip.w) * smoothstep(-0.02, 0.006, d.y));
    // the fire-lit underside of the ash lid where this direction meets it (17 km up, about Sim.UmbrellaBase): the still
    // water mirrors a glowing ceiling toward the volcano; the ash air in front dims it at low elevations
    if (_PlUnder.w > 0.0 && d.y > 0.02)
    {
        const float lidBase = 17000.0;
        float2 hit = _WorldSpaceCameraPos.xz + d.xz * (max(lidBase - _WorldSpaceCameraPos.y, 1000.0) / d.y) - _CVent.xz;
        float rr = length(hit) / _PlUnder.w;
        c += _PlUnder.rgb * (0.25 * smoothstep(0.02, 0.12, d.y) * exp(-0.12 / d.y) / (1.0 + rr * rr));
    }
    return c;
}
float3 CAmbient(float3 n)
{
    float up = n.y * 0.5 + 0.5;
    return lerp(_CSkyHorizon.rgb * 0.35, (_CSkyZenith.rgb + _CSkyHorizon.rgb) * 0.5, up);
}

#endif
