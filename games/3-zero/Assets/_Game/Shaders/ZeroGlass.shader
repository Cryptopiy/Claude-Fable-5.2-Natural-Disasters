Shader "Zero/Glass"
{
    // The window panes of the aircraft (GlassBuilder: one quad per window, inside the tunnel). Almost invisible,
    // like real acrylic: a breath of veil, frost crystals grown up from the lower edge around the breather hole,
    // fine scratches that catch the light, glare when the sun or the explosion is near the line of sight.
    // Everything is drawn soft: the camera focuses through the pane, not on it.
    Properties { }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+100" "RenderPipeline"="UniversalPipeline" }
        Blend One OneMinusSrcAlpha ZWrite Off Cull Off
        Pass
        {
            Name "Forward" Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "ZeroCommon.hlsl"
            float4x4 _ZPlaneW2L;
            float4 _ZSunDirPL, _ZPlasmaDirPL, _ZCabinFillL;
            float4 _ZWin, _ZWinSize;
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 ws : TEXCOORD0; float3 pl : TEXCOORD1; };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.ws = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.ws);
                o.pl = mul(_ZPlaneW2L, float4(o.ws, 1.0)).xyz;
                return o;
            }
            half4 Frag(Varyings i) : SV_Target
            {
                // position inside this window (metres): y up from the centre, z along the cabin from the centre
                float zc = (i.pl.z - _ZWin.x) / _ZWin.y;
                float k = round(zc);
                float lz = (zc - k) * _ZWin.y, ly = i.pl.y - _ZWin.z;
                float2 hs = _ZWinSize.xy * 0.5;
                float2 q = abs(float2(lz, ly)) - (hs - _ZWinSize.z);
                float sd = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - _ZWinSize.z;
                clip(-sd);
                float seed = k * 17.31 + (i.pl.x < 0.0 ? 0.0 : 91.7);

                float3 V = normalize(_WorldSpaceCameraPos - i.ws);
                float3 Vp = mul((float3x3)_ZPlaneW2L, -V);           // the line of sight in the aircraft's frame
                // frost: crystals grow up from the bottom of the pane and in from the corners, densest near the hole
                float2 p = float2(lz, ly) * 34.0 + seed;
                float cryst = ZFbm2(p * float2(1.0, 0.6), 4);
                float fern = ZWorley2(p * 1.7).y;
                float fromBottom = saturate((hs.y * 0.26 - (ly + hs.y)) / (hs.y * 0.26));
                float fromEdge = saturate(1.0 - (-sd) / 0.05);
                float frost = saturate(max(fromBottom * 1.2, fromEdge * 0.45) + (cryst - 0.55) * 0.8);
                frost = smoothstep(0.45, 0.95, frost) * 0.6;
                // the breather hole: a tiny dark ring a few centimetres above the lower edge
                float hole = length(float2(lz, ly + hs.y - 0.055));
                float holeRing = smoothstep(0.0032, 0.0022, abs(hole - 0.0024));
                // scratches: long faint arcs, visible only when light is behind them
                float2 sp = float2(lz * 3.0 + ly * 1.1, ly * 9.0 - lz * 0.4) + seed * 0.37;
                float scr = 0.0 * ZNoise2(sp);           // no scratches: against the explosion they read as dashed lines across the sky

                // the light behind the pane: sky in that direction, the sun, the second light (the body, the fire)
                float3 sky = ZSkyColor(normalize(mul(transpose((float3x3)_ZPlaneW2L), Vp)));
                float sunGlare = pow(saturate(dot(Vp, _ZSunDirPL.xyz)), 6.0) * _ZSunDirPL.w;
                float plGlare = pow(saturate(dot(Vp, _ZPlasmaDirPL.xyz)), 4.0) * _ZPlasmaDirPL.w;
                float3 back = sky * 1.1 + _ZSunColor.rgb * sunGlare * 0.6 + _ZPlasmaColor.rgb * plGlare * 0.5;
                float3 c = back * (frost * 0.55 + scr * (sunGlare + plGlare) * 2.0) + _ZSunColor.rgb * sunGlare * 0.035 + _ZPlasmaColor.rgb * plGlare * 0.04;
                c += _ZCabinFillL.rgb * 0.05;                          // the faint reflection of the cabin
                c *= 1.0 - holeRing * 0.8;
                float a = saturate(frost * 0.45 + holeRing * 0.5 + 0.015);
                return half4(c * _ZSkyParams.z, a);
            }
            ENDHLSL
        }
    }
}
