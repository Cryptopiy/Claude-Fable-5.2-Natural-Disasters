Shader "Zero/Smoke"
{
    // Smoke and dust puffs: soft, structured, lit by the two lights (warm on the plasma side, cool in shade).
    // Premultiplied alpha. param = darkness (0 = pale dust, 1 = black smoke).
    Properties
    {
        _Density ("Density", Float) = 1
        _Life ("Visible from -x to +y seconds around tRef, z = attack", Vector) = (100000, 100000, 4, 0)
        _Grow ("Swell with sqrt(age)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend One OneMinusSrcAlpha ZWrite Off Cull Off
        Pass
        {
            Name "Forward" Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "ZeroSprite.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float _Density; float4 _Life; float _Grow;
            CBUFFER_END
            SpriteVaryings Vert(SpriteAttributes v)
            {
                SpriteVaryings o = ZSpriteVertEx(v, 1.0 + _Grow * sqrt(max(_WorldTime - v.uv1.w, 0.0)), 1.0, 5.0, 0.0);
                float t = o.misc.z;
                // drifting puffs live inside a window around their reference time; looping puffs fade in and out with age
                float life = smoothstep(-_Life.x, -_Life.x + _Life.z, t) * (1.0 - smoothstep(_Life.y * 0.45, _Life.y, t));
                float loop = smoothstep(0.0, 0.14, o.misc.w) * (1.0 - smoothstep(0.5, 1.0, o.misc.w));
                o.misc.y *= v.uv2.w > 2.5 ? loop : life;
                return o;
            }
            half4 Frag(SpriteVaryings i) : SV_Target
            {
                float seed = i.uv.z, dark = i.uv.w;
                float2 p = i.uv.xy;
                float r2 = dot(p, p);
                float T = _WorldTime * 0.12 + seed * 19.0;
                float n = ZFbm2(p * 1.7 + seed * 23.0 + float2(0.0, -T), 4);
                float a = saturate((1.0 - r2) * 1.5 - (1.0 - n) * 1.25);
                a = a * a * (3.0 - 2.0 * a);
                // fake volume normal: a sphere bulge plus noise gradient
                float3 nrm = normalize(float3(p * 0.9, sqrt(saturate(1.0 - r2)) + 0.25) + float3(n - 0.5, n - 0.5, 0) * 0.6);
                float3 N = normalize(nrm.x * UNITY_MATRIX_V[0].xyz + nrm.y * UNITY_MATRIX_V[1].xyz - nrm.z * UNITY_MATRIX_V[2].xyz);
                float umbra = ZUmbra(i.ws);
                float3 lp = normalize(_ZPlasmaPos.xyz - i.ws);
                // dust scatters forward as well as back: a cloud is bright from both sides of the light
                float3 light = _ZSunColor.rgb * _ZSunDir.w * (abs(dot(N, _ZSunDir.xyz)) * 0.45 + 0.55) * umbra
                             + _ZPlasmaColor.rgb * ZPlasmaIntensity(i.ws) * (abs(dot(N, lp)) * 0.45 + 0.55)
                             + lerp(_ZAmbientGround.rgb, _ZAmbientSky.rgb, N.y * 0.5 + 0.5) * lerp(_ZSkyParams.y, 1.0, umbra);
                float3 alb = lerp(float3(0.74, 0.70, 0.64), float3(0.045, 0.043, 0.05), dark) * i.color.rgb;
                float3 c = alb * light;
                float alpha = a * i.color.a * _Density * ZSoft(i, 2.5) * i.misc.y;
                c = ZApplyFog(c, i.ws) * _ZSkyParams.z;
                alpha *= 1.0 - ZFogAmount(i.ws, 1.0) * 0.6;
                return half4(c * alpha, alpha);
            }
            ENDHLSL
        }
    }
}
