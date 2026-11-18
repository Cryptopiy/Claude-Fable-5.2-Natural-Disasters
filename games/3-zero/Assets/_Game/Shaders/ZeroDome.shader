Shader "Zero/Dome"
{
    // The blast wave in the air. _Kind 0: the shell of compressed air, a dome seen as a thin bright limb that climbs
    // the sky (unit hemisphere scaled to the radius of the wave). _Kind 1: the wall of dust and torn cloud that the
    // wave pushes along the ground (unit cylinder: radius 1, height 1, uv.x around, uv.y up).
    Properties
    {
        _Kind ("Kind", Float) = 0
        _Power ("Intensity", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-10" "RenderPipeline"="UniversalPipeline" }
        Blend One OneMinusSrcAlpha ZWrite Off Cull Off
        Pass
        {
            Name "Forward" Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "ZeroCommon.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float _Kind; float _Power;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 ws : TEXCOORD0; float3 nws : TEXCOORD1; float2 uv : TEXCOORD2; float3 wsC : TEXCOORD3; };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.ws = TransformObjectToWorld(v.positionOS.xyz);
                o.nws = TransformObjectToWorldNormal(v.normalOS);
                if (_Kind > 0.5)
                {
                    // the wall billows: big rolling towers that lean forward over its foot
                    float around = v.uv.x * 6.2831;
                    float2 q = float2(cos(around), sin(around)) * 46.0;
                    float n = ZFbm3(float3(q * 0.5, v.uv.y * 2.0 - _WorldTime * 0.04), 3);
                    float h = (o.ws.y - _ZCurve.z);
                    o.ws += o.nws * ((n - 0.45) * 5200.0 + h * 0.35) * v.uv.y;
                }
                o.wsC = ZCurve(o.ws);
                o.positionCS = TransformWorldToHClip(o.wsC);
                o.uv = v.uv;
                return o;
            }
            half4 Frag(Varyings i) : SV_Target
            {
                float3 V = normalize(_WorldSpaceCameraPos - i.wsC);
                float3 N = normalize(i.nws);
                float nv = abs(dot(N, V));
                float h = max(i.ws.y - _ZCurve.z, 0.0);
                float dist = distance(i.ws, _WorldSpaceCameraPos);
                float3 lightAll = _ZSunColor.rgb * _ZSunDir.w * 0.8 + _ZAmbientSky.rgb + _ZPlasmaColor.rgb * ZPlasmaIntensity(i.ws) * 0.9;
                if (_Kind < 0.5)
                {
                    // the limb of the shell: dense air shines, thinner with height; a faint body inside the limb
                    float rim = pow(1.0 - nv, 7.0);
                    float air = exp(-h / 26000.0);
                    float a = saturate((rim * 0.6 + 0.02) * air * _Power);
                    a *= smoothstep(600.0, 9000.0, dist);                 // it thins as it reaches the lens
                    float3 c = float3(0.93, 0.96, 1.0) * lightAll;
                    c = lerp(ZSkyColor(-V), c, 1.0 - ZFogAmount(i.ws, 0.3));
                    return half4(c * a * _ZSkyParams.z, a);
                }
                else
                {
                    // the wall: billows with a ragged top, lit from behind by the fire, pale where the sun finds it
                    float around = i.uv.x * 6.2831;
                    float2 q = float2(cos(around), sin(around)) * 46.0;
                    float n = ZFbm3(float3(q, i.uv.y * 3.0 - _WorldTime * 0.03), 4);
                    float n2 = ZFbm3(float3(q * 3.7, i.uv.y * 9.0 - _WorldTime * 0.09) + 5.0, 3);
                    float lobes = ZFbm3(float3(q * 0.16, _WorldTime * 0.01), 3);            // towers and gaps, tens of kilometres wide
                    float top = 0.2 + 0.4 * lobes + 0.15 * n;
                    float body = smoothstep(top, top - 0.25, i.uv.y) * smoothstep(0.0, 0.03, i.uv.y) * smoothstep(1.0, 0.86, i.uv.y);
                    float a = saturate(body * (0.78 + 0.35 * n2) * _Power);
                    a *= saturate(nv * 3.5);                                               // edge-on the shell would pile up into a hard post
                    a *= smoothstep(300.0, 5000.0, dist);
                    // lit like a storm front: dark heart, bright tops where the low sun catches them, a silver lining when it is behind
                    float3 Nn = normalize(N + float3(0.0, (n2 - 0.5) * 1.2 + i.uv.y * 0.6, 0.0));
                    float ndl = dot(Nn, _ZSunDir.xyz);
                    float lining = pow(saturate(dot(-V, _ZSunDir.xyz)), 4.0) * smoothstep(top - 0.12, top, i.uv.y) * 3.0;
                    float3 dustCol = lerp(float3(0.16, 0.12, 0.1), float3(0.62, 0.55, 0.48), saturate(i.uv.y * 1.3 + n2 * 0.5 - 0.35));
                    float3 lit = _ZSunColor.rgb * _ZSunDir.w * (saturate(ndl * 0.6 + 0.35) + lining) + _ZAmbientSky.rgb * 0.8 + _ZPlasmaColor.rgb * ZPlasmaIntensity(i.ws) * 0.6;
                    float3 c = dustCol * lit * (0.6 + 0.6 * n2);
                    c += _PalPlasmaOrange.rgb * _ZPlasmaColor.a * 0.35 * smoothstep(0.5, 0.0, i.uv.y) * (1.0 - n2);     // the fire behind it glows through its foot
                    c = lerp(ZSkyColor(-V), c, 1.0 - ZFogAmount(i.ws, 0.45));
                    return half4(c * a * _ZSkyParams.z, a);
                }
            }
            ENDHLSL
        }
    }
}
