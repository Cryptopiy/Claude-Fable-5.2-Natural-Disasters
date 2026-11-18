Shader "Zero/Ribbon"
{
    // Ejecta arcs in real time (ArcBuilder): each ribbon evaluates a ballistic flight in the vertex shader.
    // position = launch point, normal = launch velocity, uv0 = (side, half width, u along the trail, seed),
    // uv1 = (first launch time, period, flight time, trail seconds). The head is hot, the trail cools and widens.
    Properties { _Density ("Density", Float) = 1 }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-5" "RenderPipeline"="UniversalPipeline" }
        Blend One OneMinusSrcAlpha ZWrite Off Cull Off
        Pass
        {
            Name "Forward" Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "ZeroCommon.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float _Density;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 velocity : NORMAL; float4 color : COLOR; float4 uv0 : TEXCOORD0; float4 uv1 : TEXCOORD1; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 ws : TEXCOORD0; float4 uv : TEXCOORD1; float4 color : COLOR; float2 extra : TEXCOORD2; };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                float t = _WorldTime - v.uv1.x;
                float P = max(v.uv1.y, 1.0), flight = v.uv1.z, trail = v.uv1.w;
                float age = fmod(max(t, 0.0), P);                    // age of the head in this loop
                float lag = age - v.uv0.z * trail;                   // this vertex is where the head was a moment ago
                float a = clamp(lag, 0.0, flight);
                float3 L = TransformObjectToWorld(v.positionOS.xyz);
                float3 vel = v.velocity;
                float3 c = L + vel * a + float3(0.0, -4.9, 0.0) * a * a;
                float3 tan = normalize(vel + float3(0.0, -9.8 * a, 0.0));
                float3 vd = normalize(_WorldSpaceCameraPos - c);
                float3 side = normalize(cross(tan, vd) + float3(0, 1e-4, 0));
                float d = distance(c, _WorldSpaceCameraPos);
                float hwWant = v.uv0.y * (1.0 + 6.0 * v.uv0.z * v.uv0.z);
                float px = d * 2.0 / max(abs(UNITY_MATRIX_P[1][1]) * _ScreenParams.y, 1.0);     // metres per pixel there
                float hw = max(hwWant, px * 1.6);                    // never thinner than about three pixels (no hairlines): it fades instead
                o.ws = c + side * v.uv0.x * hw;
                o.positionCS = ZClip(o.ws);
                o.uv = v.uv0; o.color = v.color;
                float alive = step(0.0, t) * step(0.0, lag) * smoothstep(0.0, 7.0, age) * (1.0 - smoothstep(flight, flight + 3.0, age));
                o.color.a *= alive * smoothstep(1500.0, 9000.0, c.y - _ZCurve.z);     // nothing low over the horizon (no dotted ring)
                o.extra = float2(hwWant / hw, saturate(lag / max(flight, 1.0)));
                return o;
            }
            half4 Frag(Varyings i) : SV_Target
            {
                float x = i.uv.x, u = i.uv.z, seed = i.uv.w;
                float n = ZFbm2(float2(u * 14.0 + seed * 31.0, x * 1.6 + seed * 7.0), 3);
                // a glowing piece with a thin hot core and a smoky trail that widens, thins and breaks up behind it
                float core = exp(-x * x * 7.0);
                float halo = exp(-x * x * 1.8);
                float a = saturate(halo * (1.25 - u) - (1.0 - n) * (0.35 + 0.8 * u));
                a *= smoothstep(1.0, 0.7, u);
                float umbra = ZUmbra(i.ws);
                float3 smokeA = lerp(float3(0.42, 0.33, 0.27), float3(0.22, 0.19, 0.17), smoothstep(0.0, 0.5, u)) * i.color.rgb;
                float3 light = _ZSunColor.rgb * umbra * 0.7 + _ZPlasmaColor.rgb * ZPlasmaIntensity(i.ws) * 0.8 + _ZAmbientSky.rgb * 1.2;
                float3 c = smokeA * light;
                float cool = 1.0 - i.extra.y * 0.85;                                                   // it cools on the way
                float ember = exp(-u * 9.0) * core * cool;
                float3 hot = lerp(_PalMolten.rgb, _PalPlasmaGold.rgb, smoothstep(0.03, 0.0, u));
                float3 glow = hot * (ember * 2.2 + smoothstep(0.045, 0.0, u) * core * 3.0 * cool);    // the incandescent head
                float alpha = a * i.color.a * _Density * i.extra.x * 0.85;
                float fog = ZFogAmount(i.ws, 0.5);
                c = lerp(c, ZSkyColor(normalize(ZCurve(i.ws) - _WorldSpaceCameraPos)), fog) * _ZSkyParams.z;
                alpha *= 1.0 - fog * 0.75;
                // premultiplied smoke plus the emission of the head (which only loses some of its light in the haze)
                float eK = i.color.a * _Density * i.extra.x * (1.0 - fog * 0.5);
                return half4(c * alpha + glow * eK * _ZSkyParams.z, alpha);
            }
            ENDHLSL
        }
    }
}
