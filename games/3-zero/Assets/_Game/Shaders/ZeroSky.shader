Shader "Zero/Sky"
{
    // The sky from 11.5 km: pale haze on the horizon, deep blue overhead, the sun.
    // Same colour function as the haze; later the glow of the explosion and the red of the burning sky.
    Properties { }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "ZeroCommon.hlsl"
            float4 _ZMoonDir;     // xyz = direction to the moon, w = its angular radius (rad)
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.dir = v.positionOS.xyz;
                return o;
            }
            half4 Frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float3 c = ZSkyColor(d);
                // the moon by day: pale, gibbous in the low sun
                float md = acos(saturate(dot(d, _ZMoonDir.xyz)));
                if (md < _ZMoonDir.w * 1.02)
                {
                    float3 ex = normalize(cross(float3(0, 1, 0), _ZMoonDir.xyz)), ey = cross(_ZMoonDir.xyz, ex);
                    float2 q = float2(dot(d, ex), dot(d, ey)) / _ZMoonDir.w;
                    float3 n = normalize(q.x * ex + q.y * ey - _ZMoonDir.xyz * sqrt(saturate(1.0 - dot(q, q))));
                    float lit = saturate(dot(n, _ZSunDir.xyz) * 1.4 + 0.1);          // n faces the eye: the side toward the sun is lit
                    float maria = 0.8 + 0.2 * ZFbm2(q * 2.5 + 7.0, 3);
                    float edge = smoothstep(1.02, 0.97, md / _ZMoonDir.w);
                    c = lerp(c, c + float3(0.55, 0.55, 0.58) * lit * maria, edge * (1.0 - _ZSkyRed.a));
                }
                // the sun (hidden by anything that passes in front of it)
                float sd = dot(d, _ZSunDir.xyz);
                c += _ZSunColor.rgb * _ZSunDir.w * (26.0 * smoothstep(0.99978, 0.99990, sd) + 0.9 * pow(saturate(sd), 900.0));
                return half4(c * _ZSkyParams.z, 1.0);
            }
            ENDHLSL
        }
    }
}
