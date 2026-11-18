Shader "Zero/Glow"
{
    // Big soft additive glows: the plasma veil that bleaches the sky around the object, halos of fires,
    // warm ground glow. param = falloff sharpness (0 = wide veil .. 1 = tight core).
    Properties
    {
        _Power ("Intensity", Float) = 1
        _Life ("Visible from 0 to y seconds after tRef (x = attack), 0 = always", Vector) = (0, 0, 0, 0)
        _SizeMul ("Size multiplier", Float) = 1
        _Pull ("Drawn this far toward the lens (m)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-15" "RenderPipeline"="UniversalPipeline" }
        Blend One One ZWrite Off Cull Off ZTest LEqual
        Pass
        {
            Name "Forward" Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "ZeroSprite.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float _Power; float4 _Life; float _SizeMul; float _Pull;
            CBUFFER_END
            SpriteVaryings Vert(SpriteAttributes v)
            {
                SpriteVaryings o = ZSpriteVert(v, _SizeMul, 0.0, 0.0);
                // a glow around a solid body is drawn in front of it (a quad through the rock would cut hard edges into it)
                if (_Pull > 0.0)
                {
                    o.ws += normalize(_WorldSpaceCameraPos - o.ws) * _Pull;
                    o.positionCS = TransformWorldToHClip(ZCurve(o.ws));
                }
                // a glow is seen from outside: it thins away as the lens enters it (otherwise it would white out the frame)
                float3 centre = TransformObjectToWorld(v.positionOS);
                float d = distance(centre, _WorldSpaceCameraPos) / max(v.uv0.z * _SizeMul, 1.0);
                o.misc.y = saturate((d - 0.9) / 1.6);
                // a flash of an impact: ramps up (never an instant flash) and dies away
                if (_Life.y > 0.0) o.misc.y *= smoothstep(0.0, _Life.x, o.misc.z) * (1.0 - smoothstep(_Life.y * 0.3, _Life.y, o.misc.z));
                return o;
            }
            half4 Frag(SpriteVaryings i) : SV_Target
            {
                float r = length(i.uv.xy);
                float k = lerp(2.2, 7.0, saturate(i.uv.w));
                float a = exp(-r * r * k) * saturate((1.0 - r) * 4.0);
                float f = a * i.color.a * i.misc.y * ZFogTransmitEmissive(i.ws, 0.08);
                return half4(i.color.rgb * f * _Power * _ZSkyParams.z, 1.0);
            }
            ENDHLSL
        }
    }
}
