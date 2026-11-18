Shader "Zero/Earth"
{
    // The land seen from 11.5 km: a patchwork of fields, forests, rivers and lakes out to the horizon, cloud shadows,
    // the far cloud layer (near clouds are meshes), and what the impact does to it: the ring of the blast wave racing
    // outward, the scorched land behind it, fires, the molten ground around the crater.
    Properties { }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Forward" Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "ZeroCommon.hlsl"
            TEXTURE2D(_ZCloudTex); SAMPLER(sampler_ZCloudTex);
            float4 _ZCloudParams;    // x = 1 / world period of the coverage texture, y = coverage threshold, z = cloud top height, w = hand-over distance to the cloud meshes
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 ws : TEXCOORD0; };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.ws = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = ZClip(o.ws);
                return o;
            }

            float Coverage(float2 p)
            {
                float c = SAMPLE_TEXTURE2D_LOD(_ZCloudTex, sampler_ZCloudTex, p * _ZCloudParams.x, 0).r;
                return c;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float3 a = ZAbs(i.ws);
                float2 p = a.xz;
                float dist = distance(i.ws, _WorldSpaceCameraPos);
                float hd = distance(i.ws.xz, _WorldSpaceCameraPos.xz);

                // ---- the land ----
                float wField = saturate(1.15 - dist / 80000.0);
                float biome = ZFbm2(p / 46000.0 + 3.7, 4);
                float dry = ZFbm2(p / 120000.0 + 11.3, 3);
                float3 w1 = ZWorley2Id(p / 1700.0);
                float3 w2 = ZWorley2Id(p / 5600.0 + 7.1);
                float id = frac(w1.z * 7.31 + w2.z * 3.17);
                float3 fieldCol = lerp(_PalFieldGreen.rgb, _PalWheat.rgb, smoothstep(0.2, 0.7, id));
                fieldCol = lerp(fieldCol, _PalSoil.rgb * 1.35, step(0.84, id) * 0.7);
                fieldCol *= 0.84 + 0.3 * w2.z;
                fieldCol *= lerp(0.78, 1.0, smoothstep(0.0, 0.07, w1.y));          // hedges and tracks between the fields
                float3 farm = lerp(lerp(_PalFieldGreen.rgb, _PalWheat.rgb, 0.5) * 0.93, fieldCol, wField);
                float3 forest = float3(0.21, 0.31, 0.21) * (0.8 + 0.4 * ZNoise2(p / 900.0) * wField);
                float fo = smoothstep(0.50, 0.60, biome + 0.1 * ZNoise2(p / 3100.0));
                float3 land = lerp(farm, forest, fo);
                land = lerp(land, _PalDustTan.rgb * 0.9, smoothstep(0.55, 0.8, dry) * 0.5);
                float rv = abs(ZFbm2(p / 41000.0 + 21.0, 4) - 0.5);
                float river = 1.0 - smoothstep(0.003, 0.009 + dist * 4e-8, rv);
                float lake = smoothstep(0.70, 0.73, ZFbm2(p / 27000.0 + 41.0, 4));
                float water = saturate(river + lake);
                land = lerp(land, float3(0.17, 0.27, 0.40), water);
                // towns: pale specks along the rivers
                float town = smoothstep(0.78, 0.9, ZNoise2(p / 9000.0 + 71.0)) * smoothstep(0.035, 0.012, rv) * (1.0 - water);
                land = lerp(land, float3(0.74, 0.72, 0.70), town * smoothstep(0.3, 0.7, ZNoise2(p / 260.0)) * wField);
                // relief
                float h0 = ZFbm2(p / 30000.0 + 5.0, 3);
                float2 grad = float2(ZFbm2((p + float2(700.0, 0.0)) / 30000.0 + 5.0, 3) - h0, ZFbm2((p + float2(0.0, 700.0)) / 30000.0 + 5.0, 3) - h0) * 26.0;
                float3 N = normalize(float3(-grad.x, 1.0, -grad.y));

                // ---- clouds: their shadows everywhere, the layer itself beyond the reach of the cloud meshes ----
                float2 so = _ZSunDir.xz / max(_ZSunDir.y, 0.25) * _ZCloudParams.z;
                float det = ZFbm2(p / 2300.0 + 13.0, 3) * 0.16 - 0.08;
                float cov = Coverage(p) + det;
                float covS = Coverage(p + so) + det;
                float shadow = smoothstep(_ZCloudParams.y - 0.03, _ZCloudParams.y + 0.07, covS);
                float cloud = smoothstep(_ZCloudParams.y, _ZCloudParams.y + 0.1, cov) * smoothstep(_ZCloudParams.w - 14000.0, _ZCloudParams.w + 4000.0, hd);

                // ---- the impact ----
                float t = _ZSite.w;
                float r = ZSiteRadius(i.ws), R = _ZFront.x;
                float behind = R > 1.0 ? smoothstep(R + 300.0, R - 2500.0, r) : 0.0;
                float3 ash = lerp(_PalScorch.rgb * 1.2, _PalScorch.rgb * 2.2, ZNoise2(p / 2600.0));
                land = lerp(land, ash, behind * (0.55 + 0.4 * saturate(1.0 - r / 220000.0)));
                cloud *= 1.0 - behind; shadow *= 1.0 - behind;
                // the front: a bright line of condensed air racing over the land and the far cloud deck, a softer glow just ahead of it
                float wline = 2000.0 + dist * 0.011;
                float ahead = (r - R) / (wline * 3.0);
                // seen at a grazing angle the band would collapse into hard horizontal lines across the frame: it fades there
                float steep = (_WorldSpaceCameraPos.y - ZCurve(i.ws).y) / max(dist, 1.0);
                float rim = R > 1.0 ? (exp(-((r - R) * (r - R)) / (wline * wline)) + 0.35 * exp(-ahead * ahead) * step(R, r)) * smoothstep(320000.0, 150000.0, dist) * smoothstep(0.04, 0.13, steep) : 0.0;
                float dust = R > 1.0 ? smoothstep(R, R - 9000.0, r) * smoothstep(R - 60000.0, R - 12000.0, r) : 0.0;      // the dust the wave drags
                float fireN = smoothstep(0.58, 0.8, ZNoise2(p / 2100.0 + 9.0)) * (0.6 + 0.4 * ZNoise2(p / 300.0 + _WorldTime * 0.7));
                float fire = behind * fireN * exp(-r / 150000.0) * saturate(t / 20.0);
                float molten = t > 0.0 ? smoothstep(1.25, 0.2, r / max(_ZFront.y, 1.0)) : 0.0;

                // ---- light ----
                float ndl = saturate(dot(N, _ZSunDir.xyz));
                float3 sun = _ZSunColor.rgb * _ZSunDir.w * ndl;
                float3 lp = normalize(_ZPlasmaPos.xyz - i.ws);
                float pInt = ZPlasmaIntensity(i.ws);
                float3 pl = _ZPlasmaColor.rgb * pInt * saturate(dot(N, lp) * 0.6 + 0.4);
                float3 c = land * (sun * (1.0 - 0.8 * shadow) + pl + _ZAmbientSky.rgb * (1.0 - 0.35 * shadow));      // a cloud hides some of the sky too
                c = lerp(c, _PalDustTan.rgb * (sun * 0.7 + pl + _ZAmbientSky.rgb) * 0.45, dust * 0.4);
                float3 ccol = float3(0.97, 0.98, 1.0) * (sun * (0.72 + 0.28 * smoothstep(0.0, 0.25, cov - covS + 0.08)) + _ZAmbientSky.rgb * 1.25 + _ZPlasmaColor.rgb * pInt * 0.8);
                c = lerp(c, ccol, cloud);
                c += float3(1.0, 0.95, 0.86) * rim * (1.3 * dot(sun + _ZAmbientSky.rgb, float3(0.33, 0.33, 0.33)) + 0.9 * pInt + 0.25);
                c += lerp(_PalFireRed.rgb, _PalPlasmaOrange.rgb, fireN) * fire * 2.6;
                c += lerp(_PalPlasmaOrange.rgb, _PalPlasmaWhite.rgb, molten * molten) * molten * 5.0;
                // the ground under the explosion burns and its light carries through the haze: the base sits in a glow, not on a shelf
                float rg = r / max(_ZFront.y * 1.9, 1.0);
                float baseGlow = t > 0.0 ? exp(-rg * rg) * saturate(t / 2.0) * (0.35 + 0.65 * exp(-t / 150.0)) : 0.0;
                float3 glowC = lerp(_PalFireRed.rgb, _PalPlasmaOrange.rgb, 0.55 + 0.25 * fireN) * baseGlow * 4.0;
                return half4(ZFinish(c, i.ws) + glowC * ZFogTransmitEmissive(i.ws, 0.25) * _ZSkyParams.z, 1.0);
            }
            ENDHLSL
        }
    }
}
