Shader "Zero/Plasma"
{
    // Plasma sheath shells around the leading face of the asteroid: a thin, intensely bright skin (white core, gold,
    // orange only at the rim) with flow-aligned filaments. Additive, HDR. From far away it is a blinding shape;
    // from underneath it thins to streaming filaments so the rock ceiling stays readable.
    // Vertex colour r = leading-face weight.
    Properties
    {
        _Inflate ("Inflate (m)", Float) = 20
        _Power ("Intensity", Float) = 6
        _Fresnel ("Fresnel power", Float) = 2.2
        _Core ("Core amount", Float) = 0.6
        _NearFade ("Fade under this distance (m)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-20" "RenderPipeline"="UniversalPipeline" }
        Blend One One ZWrite Off Cull Back
        Pass
        {
            Name "Forward" Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "ZeroCommon.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float _Inflate; float _Power; float _Fresnel; float _Core; float _NearFade;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 ws : TEXCOORD0; float3 nws : TEXCOORD1; float3 os : TEXCOORD2; float lead : TEXCOORD3; };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                float3 p = v.positionOS.xyz + v.normalOS * _Inflate;
                o.os = p; o.lead = v.color.r;
                o.ws = TransformObjectToWorld(p);
                o.nws = TransformObjectToWorldNormal(v.normalOS);
                o.positionCS = ZClip(o.ws);
                return o;
            }
            half4 Frag(Varyings i) : SV_Target
            {
                float3 V = normalize(_WorldSpaceCameraPos - i.ws);
                float3 N = normalize(i.nws);
                float dist = distance(i.ws, _WorldSpaceCameraPos);
                float nv = saturate(dot(N, V));
                float fres = pow(1.0 - nv, _Fresnel);
                // flow: filaments streaming from the tip back along the body (z of object space = long axis)
                float flow = _WorldTime * 420.0;
                float3 q = i.os;
                float f1 = ZNoise3(float3(q.x * 0.020, q.y * 0.020, q.z * 0.0028 - flow * 0.0028));
                float f2 = ZNoise3(float3(q.x * 0.055 + 9.1, q.y * 0.055, q.z * 0.007 - flow * 0.007));
                float fil = pow(saturate(f1 * 1.7 - 0.45), 2.0) * 1.6 + pow(saturate(f2 * 1.8 - 0.6), 2.0) * 1.2;
                // reveal ladder: from far away only the hottest part of the leading face shows (a point of light)
                float distB = dist / 80.0;                       // the body is 13 times the size it was modelled at, and seen from much farther
                float farK = smoothstep(9000.0, 22000.0, distB);
                // under the object the sheath retreats to the hottest zone around the tip: the rock ceiling must stay readable
                float nearK = smoothstep(500.0, 2600.0, distB);
                float lead = smoothstep(0.05 + 0.72 * farK + 0.5 * (1.0 - nearK), 0.9 + 0.08 * farK, i.lead) * lerp(1.0, 0.7, farK);
                float core = _Core * nv * lerp(0.10, 1.0, nearK) * (0.75 + 0.5 * f1);
                float inten = lead * (core + fres * (0.35 + 1.5 * fil) * lerp(0.3, 1.0, nearK) + fil * 0.14 * (1.0 - nearK));
                inten *= (_NearFade > 0.0) ? smoothstep(_NearFade * 0.5, _NearFade, dist) : 1.0;
                // colour ramp by intensity: orange rim -> gold -> white
                float3 col = lerp(_PalPlasmaOrange.rgb, _PalPlasmaGold.rgb, saturate(inten * 2.2));
                col = lerp(col, _PalPlasmaWhite.rgb, saturate(inten * 1.5 - 0.55));
                float tr = ZFogTransmitEmissive(i.ws, 0.06);
                return half4(col * inten * _Power * tr * _ZSkyParams.z, 1.0);
            }
            ENDHLSL
        }
    }
}
