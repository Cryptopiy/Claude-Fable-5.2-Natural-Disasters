Shader "Zero/Trail"
{
    // The luminous column from the object to the top of the sky. Tubes along a ruler-straight axis:
    // uv0 = (around 0..1, along 0..1 from the body to the top, layer 0 = hairline core / 1 = strands / 2 = soft sheath, seed).
    Properties
    {
        _Power ("Intensity", Float) = 4
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-30" "RenderPipeline"="UniversalPipeline" }
        Blend One One ZWrite Off Cull Back
        Pass
        {
            Name "Forward" Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "ZeroCommon.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float _Power;
            CBUFFER_END
            float4 _ZAstCenter;
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 uv0 : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 ws : TEXCOORD0; float3 nws : TEXCOORD1; float4 uv : TEXCOORD2; };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.ws = TransformObjectToWorld(v.positionOS.xyz);
                o.nws = TransformObjectToWorldNormal(v.normalOS);
                o.positionCS = ZClip(o.ws);
                o.uv = v.uv0;
                return o;
            }
            half4 Frag(Varyings i) : SV_Target
            {
                float3 V = normalize(_WorldSpaceCameraPos - i.ws);
                float nv = saturate(dot(normalize(i.nws), V));
                float along = i.uv.y, layer = i.uv.z;
                float dObj = distance(_WorldSpaceCameraPos, _ZAstCenter.xyz) / 80.0;
                float flow = _WorldTime * 300.0;
                float topFade = pow(saturate(1.0 - along), 1.6);
                float inten; float3 col;
                if (layer < 0.5)
                {
                    // hairline core: the thin white line of beat 1
                    inten = pow(nv, 1.5) * 9.0 * (0.35 + 0.65 * topFade);
                    col = _PalPlasmaWhite.rgb;
                }
                else if (layer < 1.5)
                {
                    // twisting strands: helical filaments, knots at irregular spacing
                    float a = i.uv.x * 6.2831;
                    float n = ZNoise3(float3(cos(a) * 2.2, sin(a) * 2.2, along * 46.0 - flow * 0.0009 + i.uv.w * 5.0));
                    float knots = ZNoise2(float2(along * 22.0 + i.uv.w * 3.0, 0.5));
                    inten = pow(nv, 2.2) * (0.5 + 1.6 * saturate(n * 1.7 - 0.45)) * (0.6 + 1.2 * knots * knots) * topFade * 1.6;
                    col = lerp(_PalPlasmaGold.rgb, _PalPlasmaWhite.rgb, saturate(inten * 0.6));
                    inten *= smoothstep(26000.0, 12000.0, dObj);
                }
                else if (layer < 2.5)
                {
                    // soft sheath: appears as the object comes closer ("the line thickens into a column")
                    float a = i.uv.x * 6.2831;
                    float n = ZFbm3(float3(cos(a) * 1.3, sin(a) * 1.3, along * 18.0 - flow * 0.0004), 3);
                    inten = pow(nv, 1.6) * (0.25 + 0.9 * n) * topFade * 1.5;
                    col = lerp(_PalPlasmaOrange.rgb, _PalPlasmaGold.rgb, 0.65);
                    inten *= smoothstep(14000.0, 5000.0, dObj);
                }
                else if (layer < 3.5)
                {
                    // the jet: the thin white line that runs ahead of the body and touches the ground
                    float kn = ZNoise2(float2(along * 60.0 - flow * 0.004, 1.5));
                    inten = pow(nv, 1.4) * (5.0 + 5.0 * kn);
                    col = _PalPlasmaWhite.rgb;
                }
                else
                {
                    float a = i.uv.x * 6.2831;
                    float n = ZNoise3(float3(cos(a) * 1.5, sin(a) * 1.5, along * 70.0 - flow * 0.006));
                    inten = pow(nv, 2.6) * (0.3 + 1.2 * saturate(n * 1.6 - 0.4)) * 0.9 * smoothstep(16000.0, 5000.0, dObj);
                    col = lerp(_PalPlasmaGold.rgb, _PalPlasmaWhite.rgb, 0.3);
                }
                inten *= smoothstep(0.0, 0.05, along);      // no hard cap where the column leaves the rock
                inten *= lerp(0.1, 1.0, smoothstep(190000.0, 110000.0, i.ws.y - _ZCurve.z));     // only the air makes it shine
                float tr = ZFogTransmitEmissive(i.ws, 0.05);
                return half4(col * inten * _Power * tr * _ZSkyParams.z, 1.0);
            }
            ENDHLSL
        }
    }
}
