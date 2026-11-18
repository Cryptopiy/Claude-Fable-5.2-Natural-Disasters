Shader "Zero/Streak"
{
    // Luminous streaks stretched along their velocity: the plasma rain of the crater, ember trails, sparks.
    // Additive HDR. param = heat (0 = orange ember, 1 = white plasma).
    Properties { _Power ("Intensity", Float) = 4 }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+8" "RenderPipeline"="UniversalPipeline" }
        Blend One One ZWrite Off Cull Off
        Pass
        {
            Name "Forward" Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "ZeroSprite.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float _Power;
            CBUFFER_END
            SpriteVaryings Vert(SpriteAttributes v)
            {
                float age01, born;
                float3 centre = ZSpriteCentre(v, age01, born);
                float d = distance(centre, _WorldSpaceCameraPos);
                // never thinner than about a pixel and never wider than about six: no bars across the frame
                float minS = d * 0.0013 / max(v.uv0.z, 1e-4), maxS = d * 0.0025 / max(v.uv0.z, 1e-4);
                float grow = min(max(1.0, minS), maxS);
                SpriteVaryings o = ZSpriteVert(v, 1.0, 3000.0, 0.0);      // a streak that comes within a few km of the lens fades (no bars across the frame)
                // looping streaks (plasma rain, embers) fade in and out over their fall
                if (v.uv2.w > 2.5) o.misc.y *= sin(age01 * 3.14159);
                else o.misc.y *= step(0.0, o.misc.z);
                // widen only across the streak so thin lines never shimmer
                float3 right = normalize(cross(normalize(v.uv2.xyz + float3(0, 1e-4, 0)), normalize(_WorldSpaceCameraPos - centre)) + float3(1e-4, 0, 0));
                o.ws += right * v.uv0.x * v.uv0.z * (grow - 1.0);
                o.positionCS = ZClip(o.ws);
                o.misc.w = 1.0 / max(grow, 1.0);
                return o;
            }
            half4 Frag(SpriteVaryings i) : SV_Target
            {
                float2 p = i.uv.xy;
                float across = saturate(1.0 - p.x * p.x);
                float along = saturate(p.y * 0.5 + 0.5);           // 0 tail .. 1 head
                float a = across * across * pow(along, 1.6) * smoothstep(1.0, 0.86, along);
                float headGlow = across * pow(saturate((along - 0.78) / 0.14), 3.0) * smoothstep(1.0, 0.94, along) * 2.5;      // a white-hot head that the bloom catches
                float flick = 0.75 + 0.5 * ZNoise2(float2(i.uv.z * 91.0, _WorldTime * 7.0 + along * 3.0));
                float heat = i.uv.w;
                float3 col = lerp(_PalPlasmaOrange.rgb, _PalPlasmaGold.rgb, saturate(heat * 2.0));
                col = lerp(col, _PalPlasmaWhite.rgb, saturate(heat * 2.0 - 1.0 + along * 0.4 * heat));
                float f = flick * i.color.a * i.misc.y * i.misc.w * ZFogTransmitEmissive(i.ws, 0.4);
                float3 headCol = lerp(_PalPlasmaGold.rgb, _PalPlasmaWhite.rgb, 0.4 + 0.6 * heat);
                return half4((col * a + headCol * headGlow * (0.4 + 0.6 * heat)) * i.color.rgb * f * _Power * _ZSkyParams.z, 1.0);
            }
            ENDHLSL
        }
    }
}
