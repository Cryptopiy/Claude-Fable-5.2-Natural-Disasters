Shader "Zero/Beam"
{
    // Shafts of light in the cabin air (BeamBuilder): the low sun through the windows of the sunny side; from the
    // entry of the body on, its light, and then the light of the explosion, through the windows that face it.
    // One camera-facing ribbon per window and light, cast in the vertex shader from the window along the light into
    // the cabin until it meets the floor or the far wall. Additive: drifting dust, a few motes, brighter when the eye
    // looks into the light, gone right at the lens.
    // uv0 = (window centre z, light 0 = sun / 1 = second light, u along 0..1, side -1..1), uv1.x = seed.
    Properties { }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+50" "RenderPipeline"="UniversalPipeline" }
        Blend One One ZWrite Off Cull Off
        Pass
        {
            Name "Forward" Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "ZeroCommon.hlsl"
            float4x4 _ZPlaneW2L;
            float4 _ZSunDirPL, _ZPlasmaDirPL;
            float4 _ZWin;                // x = z of the player's window, y = pitch, z = centre height, w = wall x
            struct Attributes { float4 positionOS : POSITION; float4 uv0 : TEXCOORD0; float4 uv1 : TEXCOORD1; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 pl : TEXCOORD0; float4 uv : TEXCOORD1; float4 info : TEXCOORD2; };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                float li = v.uv0.y, u = v.uv0.z, s = v.uv0.w;
                float4 Ld = li < 0.5 ? _ZSunDirPL : _ZPlasmaDirPL;
                float3 L = normalize(Ld.xyz + float3(0.0, 1e-5, 0.0));
                float side = L.x < 0.0 ? -1.0 : 1.0;                 // the light comes in through the windows on its side
                float3 A = -L;
                float3 base = float3(side * _ZWin.w, _ZWin.z, v.uv0.x);
                float lenF = A.y < -0.02 ? (_ZWin.z - 0.05) / -A.y : 9.0;
                float lenW = (2.0 * _ZWin.w) / max(abs(A.x), 0.05);
                float len = min(min(lenF, lenW), 6.5);
                float3 c = base + A * (u * len);
                float3 camPL = mul(_ZPlaneW2L, float4(_WorldSpaceCameraPos, 1.0)).xyz;
                float3 vd = normalize(camPL - c);
                float3 sv = cross(A, vd);
                float svl = length(sv);
                sv = svl > 1e-4 ? sv / svl : float3(0.0, 1.0, 0.0);
                c += sv * s * (0.16 + 0.07 * u);                        // the shaft spreads a little: the sun is not a point
                o.pl = c;
                float3 ws = TransformObjectToWorld(c);                  // the mesh lives in the aircraft's frame
                o.positionCS = TransformWorldToHClip(ws);
                o.uv = v.uv0;
                float through = smoothstep(0.03, 0.12, abs(L.x));
                float str = li < 0.5 ? Ld.w : min(Ld.w, 4.0);
                o.info = float4(str * through, len, saturate(svl * 1.6), v.uv1.x);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float u = i.uv.z, x = i.uv.w, li = i.uv.y;
                float str = i.info.x;
                if (str < 1e-3) return half4(0, 0, 0, 0);
                float prof = exp(-x * x * 3.2) * (0.75 + 0.25 * exp(-x * x * 14.0));
                float along = smoothstep(0.0, 0.06, u) * (1.0 - smoothstep(0.55, 1.0, u));
                // dust in the air: a slow drifting density and a few motes that catch the light
                float t = _RealTime;
                float3 p = i.pl;
                float dens = 0.45 + 1.1 * ZNoise3(p * 2.6 + float3(0.0, t * 0.05, t * 0.03) + i.info.w * 17.0);
                float mote = pow(saturate(ZNoise3(p * 36.0 + float3(t * 0.11, -t * 0.07, t * 0.05)) * 1.3 - 0.3), 16.0) * 8.0;
                // the eye sees more of a shaft it looks into (forward scattering)
                float3 camPL = mul(_ZPlaneW2L, float4(_WorldSpaceCameraPos, 1.0)).xyz;
                float3 V = normalize(camPL - p);
                float3 L = normalize((li < 0.5 ? _ZSunDirPL.xyz : _ZPlasmaDirPL.xyz) + float3(0.0, 1e-5, 0.0));
                float phase = 0.4 + 1.6 * pow(saturate(dot(V, L) * 0.5 + 0.5), 6.0);
                // gone right at the lens
                float nearF = smoothstep(0.3, 1.0, distance(camPL, p));
                float3 col = li < 0.5 ? _ZSunColor.rgb : _ZPlasmaColor.rgb;
                float flick = li < 0.5 ? 1.0 : 1.0 + 0.12 * (ZNoise2(float2(t * 2.1, i.info.w * 13.0)) - 0.5);
                float k = str * prof * along * nearF * i.info.z * flick;
                float3 c = col * k * (dens * phase * 0.13 + mote * prof * 0.05);
                return half4(c * _ZSkyParams.z, 0.0);
            }
            ENDHLSL
        }
    }
}
