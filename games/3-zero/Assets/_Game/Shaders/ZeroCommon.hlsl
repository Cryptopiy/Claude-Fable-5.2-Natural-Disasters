#ifndef ZERO_COMMON_INCLUDED
#define ZERO_COMMON_INCLUDED

// Shared library of every ZERO shader: globals, noise, the curvature of the Earth, the sky and the aerial
// perspective seen from an aircraft, the two lights (the sun and the thing that falls / the explosion).
// The outside world is laid out in flat map coordinates (x east, z north, y height above the ground);
// ZCurve bends it around the planet, centred under the camera, in every outside vertex shader.
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

// ---------------- globals (set by ShaderGlobals.cs) ----------------
float _WorldTime;            // event clock (seconds since the flight began)
float _RealTime;             // real clock (seconds, wrapped hourly)
float4 _OriginOffset;        // absolute flat = unity world + offset
float4 _ZSunDir;             // xyz = to the sun, w = sun strength 0..1 (it dims when the sky fills with dust)
float4 _ZSunColor;
float4 _ZPlasmaPos;          // xyz = the second light (the body, then the fireball), flat unity world
float4 _ZPlasmaColor;        // rgb = colour, a = intensity
float4 _ZPlasmaDirV;         // xyz = apparent direction viewer -> second light, w = veil strength in the sky
float4 _ZAmbientSky;
float4 _ZAmbientGround;
float4 _ZFogParams;          // x = density 1/m at the ground, y = height falloff 1/m, z = ground height (unity y), w = max opacity
float4 _ZSkyHorizon;
float4 _ZSkyZenith;
float4 _ZSkyParams;          // x = sky dim, y = horizon glow of the explosion 0..1, z = exposure, w = unused
float4 _ZSkyRed;             // rgb = colour of the burning sky, a = amount 0..1
float4 _ZCurve;              // x = 1 / planet radius, y = planet radius, z = ground height (unity y), w = 1 on
float4 _ZSite;               // xyz = impact site (flat unity world), w = seconds since the contact (negative before)
float4 _ZFront;              // x = blast wave radius, y = fireball radius, z = flash 0..1, w = re-entry 0..1
float4 _ZAst;                // xyz = leading point of the body (flat unity world), w = how much it burns 0..1
float4 _ZBaseGlow;           // x = glow of the haze around the foot of the explosion, y = its half-width (rad), z = its half-height (rad)
float4 _PalSkyWhite, _PalSkyBlue, _PalHaze, _PalWheat, _PalFieldGreen, _PalRoadGrey, _PalDustTan, _PalShadowBlue, _PalShadowDeep;
float4 _PalPlasmaWhite, _PalPlasmaGold, _PalPlasmaOrange, _PalFireRed, _PalRockDark, _PalMolten, _PalSoil, _PalScorch;

float3 ZAbs(float3 ws) { return ws + _OriginOffset.xyz; }

// ---------------- the planet ----------------
// Flat map position -> where it is seen from the camera on a sphere (exact for the camera's own meridian frame).
float3 ZCurve(float3 ws)
{
    float2 d = ws.xz - _WorldSpaceCameraPos.xz;
    float dist = length(d);
    if (dist < 1.0 || _ZCurve.w < 0.5) return ws;
    float th = dist * _ZCurve.x;
    float s, c; sincos(th, s, c);
    float sh = sin(th * 0.5);
    float h = ws.y - _ZCurve.z;
    float3 o;
    o.xz = _WorldSpaceCameraPos.xz + (d / dist) * ((_ZCurve.y + h) * s);
    o.y = _ZCurve.z + h * c - 2.0 * _ZCurve.y * sh * sh;
    return o;
}

float4 ZClip(float3 wsFlat) { return TransformWorldToHClip(ZCurve(wsFlat)); }

// ---------------- noise ----------------
float ZHash11(float p) { p = frac(p * 0.1031); p *= p + 33.33; p *= p + p; return frac(p); }
float ZHash21(float2 p) { float3 p3 = frac(float3(p.xyx) * 0.1031); p3 += dot(p3, p3.yzx + 33.33); return frac((p3.x + p3.y) * p3.z); }
float ZHash31(float3 p) { p = frac(p * 0.1031); p += dot(p, p.zyx + 31.32); return frac((p.x + p.y) * p.z); }
float3 ZHash33(float3 p) { p = frac(p * float3(0.1031, 0.1030, 0.0973)); p += dot(p, p.yxz + 33.33); return frac((p.xxy + p.yxx) * p.zyx); }

float ZNoise2(float2 p)
{
    float2 i = floor(p), f = frac(p); f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(ZHash21(i), ZHash21(i + float2(1, 0)), f.x), lerp(ZHash21(i + float2(0, 1)), ZHash21(i + float2(1, 1)), f.x), f.y);
}
float ZNoise3(float3 p)
{
    float3 i = floor(p), f = frac(p); f = f * f * (3.0 - 2.0 * f);
    float n000 = ZHash31(i), n100 = ZHash31(i + float3(1, 0, 0)), n010 = ZHash31(i + float3(0, 1, 0)), n110 = ZHash31(i + float3(1, 1, 0));
    float n001 = ZHash31(i + float3(0, 0, 1)), n101 = ZHash31(i + float3(1, 0, 1)), n011 = ZHash31(i + float3(0, 1, 1)), n111 = ZHash31(i + float3(1, 1, 1));
    return lerp(lerp(lerp(n000, n100, f.x), lerp(n010, n110, f.x), f.y), lerp(lerp(n001, n101, f.x), lerp(n011, n111, f.x), f.y), f.z);
}
float ZFbm2(float2 p, int oct) { float a = 0.5, s = 0.0; for (int i = 0; i < oct; i++) { s += a * ZNoise2(p); p = p * 2.03 + 17.1; a *= 0.5; } return s; }
float ZFbm3(float3 p, int oct) { float a = 0.5, s = 0.0; for (int i = 0; i < oct; i++) { s += a * ZNoise3(p); p = p * 2.03 + 11.7; a *= 0.5; } return s; }
// cellular: x = F1, y = F2 - F1, z = cell id
float3 ZWorley2Id(float2 p)
{
    float2 i = floor(p), f = frac(p); float f1 = 8.0, f2 = 8.0, id = 0.0;
    for (int y = -1; y <= 1; y++) for (int x = -1; x <= 1; x++)
    {
        float2 g = float2(x, y); float2 o = float2(ZHash21(i + g), ZHash21(i + g + 57.3));
        float2 r = g + o - f; float d = dot(r, r);
        if (d < f1) { f2 = f1; f1 = d; id = ZHash21(i + g + 113.7); } else if (d < f2) f2 = d;
    }
    f1 = sqrt(f1); f2 = sqrt(f2);
    return float3(f1, f2 - f1, id);
}
float2 ZWorley2(float2 p) { return ZWorley2Id(p).xy; }

float3 ZRotate(float3 v, float3 axis, float ang)
{
    float s, c; sincos(ang, s, c);
    return v * c + cross(axis, v) * s + axis * dot(axis, v) * (1.0 - c);
}

// ---------------- sky and aerial perspective ----------------
// Seen from 11.5 km: the horizon lies about 3.4 degrees below eye level; below it is haze over land.
// Three stops, so the warm haze never mixes with the blue into grey: a thin bright band on the horizon, a clear
// light blue a few degrees above it, the deep blue of the high air overhead.
float4 _ZSkyMid;
float3 ZSkyBase(float3 d)
{
    float t = max(d.y + 0.059, 0.0);                          // 0 at the geometric horizon seen from 11.5 km
    float w1 = 1.0 - exp(-t * 13.0);
    float w2 = 1.0 - exp(-t * 2.8);
    float3 c = lerp(_ZSkyHorizon.rgb, _ZSkyMid.rgb, w1);
    return lerp(c, _ZSkyZenith.rgb, w2 * w2);
}

// The colour of the sky (and therefore of the haze) in a direction: the sun's veil, the veil of the falling body
// or of the explosion, the glow that climbs from the horizon over the impact, the red of a sky full of falling fire.
float3 ZSkyColor(float3 d)
{
    float3 c = ZSkyBase(d) * _ZSkyParams.x;
    float sd = saturate(dot(d, _ZSunDir.xyz));
    c += _ZSunColor.rgb * (pow(sd, 3.0) * 0.10 + pow(sd, 12.0) * 0.12 + pow(sd, 64.0) * 0.25) * _ZSunDir.w * (1.0 + 0.6 * exp(-max(d.y, 0.0) * 6.0));   // golden hour: a warm glow along the horizon toward the sun
    float pdv = saturate(dot(d, _ZPlasmaDirV.xyz));
    float pp = pdv * pdv; pp *= pp;
    c += _ZPlasmaColor.rgb * (pp * 0.05 + pp * pp * pp * 0.16) * _ZPlasmaDirV.w;
    // the glow of the explosion spreads along the horizon on its side of the sky
    float2 dh = normalize(d.xz + 1e-5), ph = normalize(_ZPlasmaDirV.xz + 1e-5);
    float side = saturate(dot(dh, ph) * 0.5 + 0.5);
    float low = exp(-max(d.y + 0.05, 0.0) * 5.0);
    c += _ZPlasmaColor.rgb * (side * side * low * 0.5) * _ZSkyParams.y;
    // the haze around the foot of the explosion glows with its fire: the base stands in lit dust, not on an empty band
    if (_ZBaseGlow.x > 0.001)
    {
        float3 sdir = normalize(ZCurve(_ZSite.xyz) - _WorldSpaceCameraPos);
        float ang = acos(saturate(dot(normalize(d.xz + 1e-6), normalize(sdir.xz + 1e-6))));
        float dv = d.y - sdir.y;
        float hv = dv > 0.0 ? _ZBaseGlow.z * 2.2 : _ZBaseGlow.z;
        float g = exp(-(ang * ang) / (_ZBaseGlow.y * _ZBaseGlow.y)) * exp(-(dv * dv) / (hv * hv));
        c += lerp(_PalFireRed.rgb, _PalPlasmaOrange.rgb, 0.6) * g * _ZBaseGlow.x;
    }
    // the burning sky: brightest overhead, where the ejecta come back down
    float up = saturate(d.y * 0.75 + 0.35);
    c = lerp(c, c * 0.35 + _ZSkyRed.rgb * (0.35 + 0.65 * up), _ZSkyRed.a);
    return c;
}

// Fog opacity toward a flat world point (exponential atmosphere, analytic).
float ZFogAmount(float3 ws, float densityScale)
{
    float3 cam = _WorldSpaceCameraPos;
    float3 v = ws - cam; float L = length(v);
    float k = _ZFogParams.y;
    float kdy = k * v.y;
    float hf = abs(kdy) > 0.001 ? (1.0 - exp(-kdy)) / kdy : 1.0;
    float tau = _ZFogParams.x * densityScale * L * exp(-k * (cam.y - _ZFogParams.z)) * hf;
    return (1.0 - exp(-tau)) * _ZFogParams.w;
}

float3 ZApplyFog(float3 col, float3 ws)
{
    float3 d = normalize(ZCurve(ws) - _WorldSpaceCameraPos);
    float f = ZFogAmount(ws, 1.0);
    return lerp(col, ZSkyColor(d), f);
}

// Additive / emissive things only lose energy in the haze (and less of it: light punches through).
float ZFogTransmitEmissive(float3 ws, float scale) { return 1.0 - ZFogAmount(ws, scale); }

float3 ZFinish(float3 col, float3 ws)
{
    col = ZApplyFog(col, ws);
    return col * _ZSkyParams.z;
}

// ---------------- the lights ----------------
float ZUmbra(float3 ws) { return 1.0; }

// The light of the body / of the explosion on a point of the world: falls off with distance from its source.
float ZPlasmaIntensity(float3 ws)
{
    float pd = distance(ws, _ZPlasmaPos.xyz);
    return _ZPlasmaColor.a / (1.0 + (pd * pd) / (2.2e5 * 2.2e5));
}

// Distance of a flat world point from the impact site along the ground.
float ZSiteRadius(float3 ws) { return length(ws.xz - _ZSite.xz); }

#endif
