Shader "Zero/Cabin"
{
    // The aircraft. Interior (_Interior = 1): vertex-coloured lining and seats lit by what comes through the windows:
    // patches of sunlight and, later, of the light of the explosion (an analytic test against the row of window
    // openings: the patches move when the aircraft rolls and when the light moves), plus the soft fill of the two
    // window walls. Exterior (_Interior = 0): wing and engine under the sun, the sky and the explosion.
    // Vertex colour: rgb = albedo, a = emission (lights).
    Properties
    {
        _Interior ("Interior", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry-10" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Forward" Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "ZeroCommon.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float _Interior;
            CBUFFER_END
            float4x4 _ZPlaneW2L;         // unity world -> plane-local
            float4 _ZSunDirPL;           // plane-local direction to the sun, w = strength in the cabin
            float4 _ZPlasmaDirPL;        // plane-local direction to the second light, w = strength in the cabin
            float4 _ZCabinFillL, _ZCabinFillR, _ZCabinAmb;
            float4 _ZWin;                // x = z of the player's window, y = pitch, z = centre height, w = wall x
            float4 _ZWinSize;            // x = width, y = height, z = corner radius, w = skin x
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 ws : TEXCOORD0; float3 pl : TEXCOORD1; float3 npl : TEXCOORD2; float4 color : COLOR; };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.ws = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.ws);
                o.pl = mul(_ZPlaneW2L, float4(o.ws, 1.0)).xyz;
                o.npl = mul((float3x3)_ZPlaneW2L, TransformObjectToWorldNormal(v.normalOS));
                o.color = v.color;
                return o;
            }

            float WinRect(float y, float z)
            {
                float zc = (z - _ZWin.x) / _ZWin.y;
                float lz = (zc - round(zc)) * _ZWin.y;
                float2 q = abs(float2(lz, y - _ZWin.z)) - (_ZWinSize.xy * 0.5 - _ZWinSize.z);
                float sd = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - _ZWinSize.z;
                return saturate(-sd / 0.014);
            }
            // How much of a light from direction L (plane-local, to the light) reaches P through the windows.
            float Window(float3 P, float3 L)
            {
                if (abs(L.x) < 0.03) return 0.0;
                float side = L.x < 0.0 ? -1.0 : 1.0;
                float t1 = (side * _ZWin.w - P.x) / L.x, t2 = (side * _ZWinSize.w - P.x) / L.x;
                float a = 1.0;
                if (t1 > 0.002) { float3 h = P + L * t1; a *= WinRect(h.y, h.z); }
                if (t2 > 0.002) { float3 h = P + L * t2; a *= WinRect(h.y, h.z); }
                return a;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float3 N = normalize(i.npl);
                float3 sc = i.color.rgb;                                           // vertex colours are sRGB like the palette
                float3 alb = lerp(sc / 12.92, pow((sc + 0.055) / 1.055, 2.4), step(0.04045, sc));
                float3 c;
                if (_Interior > 0.5)
                {
                    float3 Ls = _ZSunDirPL.xyz, Lp = _ZPlasmaDirPL.xyz;
                    float3 light = _ZSunColor.rgb * _ZSunDirPL.w * Window(i.pl, Ls) * saturate(dot(N, Ls) * 0.9 + 0.1)
                                 + _ZPlasmaColor.rgb * _ZPlasmaDirPL.w * Window(i.pl, Lp) * saturate(dot(N, Lp) * 0.55 + 0.45);   // wrapped: the window shapes of the fire land on the seat backs too
                    float sideL = saturate(dot(N, normalize(float3(-1.0, 0.3, 0.0))) * 0.55 + 0.45);
                    float sideR = saturate(dot(N, normalize(float3(1.0, 0.3, 0.0))) * 0.55 + 0.45);
                    float nearL = 1.0 / (1.0 + ((i.pl.x + _ZWin.w) * (i.pl.x + _ZWin.w)) / 3.0);
                    float nearR = 1.0 / (1.0 + ((_ZWin.w - i.pl.x) * (_ZWin.w - i.pl.x)) / 1.3);
                    float low = saturate(0.45 + i.pl.y * 0.5);                      // the floor and the foot wells are darker
                    float3 fill = (_ZCabinFillL.rgb * sideL * nearL + _ZCabinFillR.rgb * sideR * nearR) * low + _ZCabinAmb.rgb;
                    c = alb * (light + fill) + alb * i.color.a * 2.2 * _ZCabinAmb.a;
                }
                else
                {
                    float3 V = normalize(_WorldSpaceCameraPos - i.ws);
                    float3 Vp = mul((float3x3)_ZPlaneW2L, V);
                    float3 Ls = _ZSunDirPL.xyz, Lp = _ZPlasmaDirPL.xyz;
                    float ndl = dot(N, Ls);
                    float3 sun = _ZSunColor.rgb * _ZSunDir.w * saturate(ndl * 0.85 + 0.15);
                    float3 Hh = normalize(Ls + Vp);
                    float spec = pow(saturate(dot(N, Hh)), 60.0) * 0.6 * saturate(ndl * 4.0);
                    float3 pl = _ZPlasmaColor.rgb * _ZPlasmaDirPL.w * 0.9 * (saturate(dot(N, Lp) * 0.85 + 0.15) + pow(saturate(dot(N, normalize(Lp + Vp))), 40.0) * 0.5);
                    float3 amb = lerp(_ZAmbientGround.rgb * 1.6, _ZAmbientSky.rgb * 1.3, N.y * 0.5 + 0.5);
                    c = alb * (sun + pl + amb) + _ZSunColor.rgb * _ZSunDir.w * spec + alb * i.color.a * 6.0;
                }
                return half4(c * _ZSkyParams.z, 1.0);
            }
            ENDHLSL
        }
    }
}
