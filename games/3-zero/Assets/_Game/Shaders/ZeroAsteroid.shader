Shader "Zero/Asteroid"
{
    // The rock: detail at every distance (screen-footprint-faded noise octaves bump the normal), cool sky fill on top,
    // gold under-light from its own plasma, a sparse net of thin molten cracks on the leading underside, and an
    // art-directed reveal through the haze (a pale ghost first, never a crisp dark ball, never a ball of fire).
    // Vertex colour: r = leading-face heat 0..1, g = cavity (0 = deep crack), b = large-scale tone.
    Properties
    {
        _RevealFar ("Reveal far (m)", Float) = 26000
        _RevealNear ("Reveal near (m)", Float) = 9000
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Transparent-490" "RenderPipeline"="UniversalPipeline" }   // after the sky: at thousands of kilometres the sky would win the depth test
        HLSLINCLUDE
        #include "ZeroCommon.hlsl"
        CBUFFER_START(UnityPerMaterial)
        float _RevealFar; float _RevealNear;
        CBUFFER_END
        float4 _ZTipPos; float4 _ZAstCenter;
        struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 color : COLOR; };
        float3 ZVertexWorld(Attributes v, out float3 nws)
        {
            nws = TransformObjectToWorldNormal(v.normalOS);
            return TransformObjectToWorld(v.positionOS.xyz);
        }
        ENDHLSL

        Pass
        {
            Name "Forward" Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            struct Varyings { float4 positionCS : SV_POSITION; float3 ws : TEXCOORD0; float3 nws : TEXCOORD1; float3 os : TEXCOORD2; float4 color : COLOR; };
            Varyings Vert(Attributes v)
            {
                Varyings o; float3 n;
                o.ws = ZVertexWorld(v, n); o.nws = n; o.os = v.positionOS.xyz; o.color = v.color;
                o.positionCS = ZClip(o.ws);
                return o;
            }

            // height detail: octaves fade in only when they are larger than a few pixels
            float Detail(float3 p, float fw)
            {
                float h = 0.0;
                float l = 210.0, a = 1.0;
                for (int i = 0; i < 2; i++)                  // only the broad relief: finer crinkles glittered like crumpled foil
                {
                    float w = saturate(l / (fw * 4.0) - 0.6);
                    if (w > 0.0)
                    {
                        float n = ZNoise3(p / l + 7.3 * i);
                        n = 1.0 - abs(n * 2.0 - 1.0);        // ridged
                        h += a * w * n * n;
                    }
                    l *= 0.36; a *= 0.52;
                }
                return h;
            }

            // a thin line of a cellular net that never gets thinner than about 1.5 pixels (it dims instead)
            float CrackLine(float2 w, float cell, float fw, float width)
            {
                float thr = max(width, 1.5 * fw / cell);
                return (1.0 - smoothstep(thr * 0.35, thr, w.y)) * saturate(width / thr);
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float3 V = normalize(_WorldSpaceCameraPos - i.ws);
                float dist = distance(i.ws, _WorldSpaceCameraPos);
                float3 Ng = normalize(i.nws);
                float fw = max(length(fwidth(i.os)), 0.02);
                float h = Detail(i.os, fw);
                // bump from the height field (screen-space derivatives)
                float3 dpx = ddx(i.ws), dpy = ddy(i.ws);
                float dhx = ddx(h), dhy = ddy(h);
                float3 r1 = cross(dpy, Ng), r2 = cross(Ng, dpx);
                float det = dot(dpx, r1);
                float3 grad = sign(det) * (dhx * r1 + dhy * r2);
                float3 N = normalize(abs(det) * Ng - 9.0 * grad);

                float heat = i.color.r, cav = i.color.g, tone = i.color.b;
                // strata-like tone bands across the body give it the grain of a mountain
                float band = ZNoise3(float3(i.os.x * 0.004, i.os.y * 0.021 + h * 0.6, i.os.z * 0.004));
                float3 alb = lerp(_PalRockDark.rgb * 1.25, float3(0.27, 0.225, 0.185), saturate(tone * 0.6 + h * 0.3 + (band - 0.5) * 0.5));
                alb *= 0.5 + 0.5 * cav;

                // light: sun (far side), cool sky fill from above, gold under-light from the plasma at the tip
                float spaceK = saturate((i.ws.y - _ZCurve.z - 60000.0) / 70000.0);
                float ndl = saturate(dot(N, _ZSunDir.xyz) * lerp(0.85, 1.0, spaceK) + 0.15 * (1.0 - spaceK));
                float3 sun = _ZSunColor.rgb * ndl;
                float hemi = N.y * 0.5 + 0.5;
                float3 amb = lerp(_ZAmbientGround.rgb * 0.5, _ZAmbientSky.rgb * 0.9, hemi) * (1.0 - 0.85 * spaceK);
                float3 toTip = _ZTipPos.xyz - i.ws; float dTip = length(toTip);
                float pl = saturate(dot(Ng, toTip / dTip) * 0.75 + 0.25);      // the smooth normal: the close fire must not make the relief glitter
                // only the burning body glows: out in space it is cold rock (the far "star" is the glow sprite, not the surface)
                float burnK = saturate((_ZAst.w - 0.32) / 0.68);
                float pAtt = 1.15 * burnK / (1.0 + (dTip * dTip) / (18000.0 * 18000.0));      // dark rock, burning cracks: the contrast is the mass
                float3 plasma = _ZPlasmaColor.rgb * pAtt * pl * (0.3 + 0.7 * cav);
                float3 c = alb * (sun + amb + plasma);

                // thin molten cracks on the leading underside: a sparse net, brightest near the tip
                float2 w1 = ZWorley2(i.os.xz / 120.0 + i.os.y / 170.0 + (band - 0.5) * 0.3);
                float2 w2 = ZWorley2(i.os.zy / 31.0 + i.os.x / 43.0);
                float crack = CrackLine(w1, 120.0, fw, 0.035) + 0.55 * CrackLine(w2, 31.0, fw, 0.04);
                float farK = smoothstep(250000.0, 900000.0, dist);
                float hm = smoothstep(0.28, 0.85, heat + (ZNoise3(i.os / 190.0) - 0.5) * 0.5);
                float nose = pow(saturate(heat), 9.0 + 9.0 * farK);                       // the incandescent leading face
                float glow = saturate(crack) * hm * (1.0 - farK) * 0.8 + nose * 1.3;
                // a temperature ramp over the leading face: dull red on the flanks, orange, gold, white only at the stagnation point
                float3 ecol = lerp(_PalFireRed.rgb, _PalPlasmaOrange.rgb, saturate(heat * 1.6));
                ecol = lerp(ecol, _PalPlasmaGold.rgb, saturate(heat * heat * 1.4));
                ecol = lerp(ecol, _PalPlasmaWhite.rgb, saturate(nose * 1.2));
                // warm rim where the hot face turns away: lit from below
                float rim = pow(1.0 - saturate(dot(Ng, V)), 3.0) * hm * 0.6;

                // aerial perspective + reveal ladder
                float fog = ZFogAmount(i.ws, 1.0);      // a mountain in the sky: thinner air between it and the eye than along the ground
                float reveal = smoothstep(_RevealFar, _RevealNear, dist);
                float vis = (1.0 - fog) * reveal * 0.78;
                float3 sky = ZSkyColor(normalize(ZCurve(i.ws) - _WorldSpaceCameraPos));
                float eVis = max(vis, (1.0 - ZFogAmount(i.ws, 0.4)) * 0.9);
                // above the air it is seen like the moon by day: its light is added to the blue, its dark side is sky
                float space = saturate((i.ws.y - _ZCurve.z - 60000.0) / 70000.0);
                float3 spaceAlb = 0.08 + alb * 1.1;                                          // seen above the air like the moon by day
                float ndlS = saturate(dot(normalize(lerp(N, Ng, 0.5)), _ZSunDir.xyz) * 0.85 + 0.08);       // soft, like the moon by day
                float3 spaceCol = sky * 0.45 + spaceAlb * _ZSunColor.rgb * ndlS * 0.95 + plasma * alb;      // its lit faces must not clip to white (they read as snow)
                float3 col = lerp(lerp(sky, c, vis), spaceCol, space) + ecol * (glow * 5.0 + rim * 1.5) * eVis * burnK;
                return half4(col * _ZSkyParams.z, 1.0);
            }
            ENDHLSL
        }
    }
}
