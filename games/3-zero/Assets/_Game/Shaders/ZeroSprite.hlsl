#ifndef ZERO_SPRITE_INCLUDED
#define ZERO_SPRITE_INCLUDED
// Shared vertex stage of the sprite shaders (SpriteBuilder meshes). The state of every sprite is a pure function
// of its seed and the event clock: nothing is simulated, any moment can be jumped to.
// uv1 = (seed, param, rotation or loop period, tRef), uv2 = (velocity xyz, mode)
// modes: 0 faces the camera, drifts with its velocity      1 upright, turns around the vertical axis
//        2 stretched along its velocity, drifts            3 faces the camera, LOOPS along its velocity (smoke rising)
//        4 stretched along its velocity, LOOPS (plasma rain, embers)
#include "ZeroCommon.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

struct SpriteAttributes
{
    float3 positionOS : POSITION;
    float4 color : COLOR;
    float4 uv0 : TEXCOORD0;   // corner xy (-1..1), half width, half height
    float4 uv1 : TEXCOORD1;   // seed, param, rotation / loop period, tRef
    float4 uv2 : TEXCOORD2;   // velocity xyz (m/s), mode
};

struct SpriteVaryings
{
    float4 positionCS : SV_POSITION;
    float4 color : COLOR;
    float4 uv : TEXCOORD0;    // xy = corner, z = seed, w = param
    float3 ws : TEXCOORD1;
    float4 misc : TEXCOORD2;  // x = eye depth, y = fade, z = time since tRef, w = loop age 0..1
};

// Centre of a sprite now; age01 = position in its loop (0 for non-looping sprites), born = 0 before a loop starts.
float3 ZSpriteCentre(SpriteAttributes v, out float age01, out float born)
{
    float t = _WorldTime - v.uv1.w;
    age01 = 0.0; born = 1.0;
    float3 off;
    if (v.uv2.w > 2.5)
    {
        float P = max(v.uv1.z, 0.5);
        // an emitter that starts at tRef: every sprite appears when its own loop first begins after that moment
        born = step(1.0, t / P + v.uv1.x);
        age01 = frac(t / P + v.uv1.x);
        off = v.uv2.xyz * (age01 * P);
    }
    else off = v.uv2.xyz * t;
    return TransformObjectToWorld(v.positionOS + off);
}

// sizeScale grows or shrinks the quad; loopGrow > 0 lets looping sprites swell with age; nearFade/farFade are distances (0 = off).
SpriteVaryings ZSpriteVertEx(SpriteAttributes v, float sizeScale, float loopGrow, float nearFade, float farFade)
{
    SpriteVaryings o;
    float age01, born;
    float3 centre = ZSpriteCentre(v, age01, born);
    float3 toCam = _WorldSpaceCameraPos - centre;
    float dist = length(toCam);
    float3 vd = toCam / max(dist, 1e-3);
    float3 right, up;
    float mode = v.uv2.w;
    bool aligned = (mode > 1.5 && mode < 2.5) || mode > 3.5;
    if (aligned)
    {
        float3 vel = v.uv2.xyz; float vl = length(vel);
        up = vl > 1e-3 ? vel / vl : float3(0, 1, 0);
        right = normalize(cross(up, vd) + float3(1e-4, 0, 0));
    }
    else if (mode > 0.5 && mode < 1.5)
    {
        up = float3(0, 1, 0);
        right = normalize(cross(up, vd) + float3(1e-4, 0, 0));
    }
    else
    {
        right = UNITY_MATRIX_V[0].xyz; up = UNITY_MATRIX_V[1].xyz;
        float rot = mode > 2.5 ? v.uv1.x * 6.2831 : v.uv1.z;
        float s, c; sincos(rot, s, c);
        float3 r2 = right * c + up * s; up = up * c - right * s; right = r2;
    }
    if (mode > 2.5) sizeScale *= lerp(1.0, 0.45 + 1.5 * age01, loopGrow);
    float2 corner = v.uv0.xy;
    float3 ws = centre + (right * corner.x * v.uv0.z + up * corner.y * v.uv0.w) * sizeScale;
    o.ws = ws;
    float3 wsC = ZCurve(ws);
    o.positionCS = TransformWorldToHClip(wsC);
    o.color = v.color;
    o.uv = float4(corner, v.uv1.x, v.uv1.y);
    float fade = born;
    if (nearFade > 0.0) fade *= saturate((dist - nearFade * 0.35) / (nearFade * 0.65));
    if (farFade > 0.0) fade *= saturate((farFade - dist) / (farFade * 0.3));
    o.misc = float4(-TransformWorldToView(wsC).z, fade, _WorldTime - v.uv1.w, age01);
    return o;
}

SpriteVaryings ZSpriteVert(SpriteAttributes v, float sizeScale, float nearFade, float farFade)
{
    return ZSpriteVertEx(v, sizeScale, 0.0, nearFade, farFade);
}

// Soft intersection with opaque geometry.
float ZSoft(SpriteVaryings i, float softness)
{
    float2 suv = GetNormalizedScreenSpaceUV(i.positionCS);
    float scene = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
    return saturate((scene - i.misc.x) / softness);
}
#endif
