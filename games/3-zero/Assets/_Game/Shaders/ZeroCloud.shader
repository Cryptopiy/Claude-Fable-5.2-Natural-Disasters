Shader "Zero/Cloud"
{
    // Fair-weather cumulus of the cloud deck (CloudBuilder): every cloud is a cluster of puffs. Vertex: position,
    // colour (r = shade 0..1, g = height in the cloud 0..1, b = random), uv0 = centre of the cloud and its radius,
    // uv1 = centre of the puff and its radius.
    // Drawn as vapour, not as solids: each puff is shaded as a soft round volume (its own smooth normal, bent a little
    // by slow 3D noise), its limb thins out into ragged wisps, the sunny side is warm and bright, the shadow side takes
    // the blue of the sky, the flat bases are grey; clouds in front of the explosion are lit through by it.
    // Two passes: the dense core writes depth (so the clouds hide each other and the explosion correctly), then the
    // whole puff blends in with premultiplied alpha. The blast wave throws each cloud outward and tears it apart;
    // just ahead of it the air condenses and the cloud tops flash white as the front passes.
    Properties { }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-200" "RenderPipeline"="UniversalPipeline" }
        HLSLINCLUDE
        #include "ZeroCommon.hlsl"
        float4 _ZCloudParams;
        struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; float4 uv0 : TEXCOORD0; float4 uv1 : TEXCOORD1; };
        struct Varyings { float4 positionCS : SV_POSITION; float3 ws : TEXCOORD0; float4 color : COLOR; float torn : TEXCOORD2; float4 centre : TEXCOORD3; float front : TEXCOORD4; float4 puff : TEXCOORD5; };
        Varyings CloudVert(Attributes v)
        {
            Varyings o;
            float3 ws = TransformObjectToWorld(v.positionOS.xyz);
            float3 cws = TransformObjectToWorld(v.uv0.xyz);
            float3 pws = TransformObjectToWorld(v.uv1.xyz);
            // beyond the hand-over distance the far layer of the land shader takes over
            float cd = distance(cws.xz, _WorldSpaceCameraPos.xz);
            float keep = 1.0 - smoothstep(_ZCloudParams.w - 12000.0, _ZCloudParams.w + 2000.0, cd);
            float2 rad = cws.xz - _ZSite.xz;
            float r = length(rad), R = _ZFront.x;
            float past = R > 1.0 ? saturate((R - r) / 4200.0) : 0.0;
            float k = keep * (1.0 - past * past);
            float2 dir = rad / max(r, 1.0);
            float3 shift = float3(dir.x, 0.0, dir.y) * past * (1800.0 + 2600.0 * v.color.b) + float3(0.0, past * (300.0 + 900.0 * v.color.g), 0.0);
            ws = cws + (ws - cws) * k + shift;
            o.puff = float4(cws + (pws - cws) * k + shift, v.uv1.w * max(k, 0.05));
            o.ws = ws;
            o.positionCS = ZClip(ws);
            o.color = v.color; o.torn = past;
            o.centre = float4(cws, v.uv0.w);
            float fr = (r - R - 900.0) / 2600.0;
            float steep = (_WorldSpaceCameraPos.y - ZCurve(ws).y) / max(distance(ws, _WorldSpaceCameraPos), 1.0);
            o.front = R > 1.0 ? exp(-fr * fr) * smoothstep(0.04, 0.13, steep) : 0.0;     // no hard lines at grazing angles
            return o;
        }
        // the slow noise of the vapour (the same in both passes)
        float PuffNoise(Varyings i) { return ZFbm3(i.ws / 650.0 + float3(_WorldTime * 0.003, 0.0, 0.0), 3); }
        // the round body of the puff (squashed like the mesh) blended with the body of the whole cloud, so the puffs
        // read as one mass and not as balls; bent a little by slow noise: no facets
        float3 PuffNormal(Varyings i)
        {
            float3 Np = normalize((i.ws - i.puff.xyz) * float3(1.0, 1.8, 1.0) + float3(0.0, 1e-3, 0.0));     // (a torn cloud collapses onto its centre: never normalize zero)
            float3 Nc = normalize((i.ws - i.centre.xyz + float3(0.0, i.centre.w * 0.3, 0.0)) * float3(1.0, 1.4, 1.0) + float3(0.0, 1e-3, 0.0));
            float3 Nb = normalize(lerp(Np, Nc, 0.62));
            float3 p = i.ws / 650.0 + float3(_WorldTime * 0.003, 0.0, 0.0);
            float3 g = float3(ZNoise3(p * 1.7 + 3.1), ZNoise3(p * 1.7 + 17.9), ZNoise3(p * 1.7 + 29.3)) - 0.5;
            return normalize(Nb + g * 0.3);
        }
        // how much of the cloud is there: dense in the middle, thinning to ragged wisps at the limb of the whole cloud
        // (the limb follows the blended normal, so the seams between the puffs inside a cloud do not draw).
        // core = without the small wisps (what the depth pass draws), full = with them (only on the true outline)
        void PuffAlpha(Varyings i, float n, bool wisps, out float core, out float full)
        {
            float3 V = normalize(_WorldSpaceCameraPos - i.ws);
            float3 Np = normalize((i.ws - i.puff.xyz) * float3(1.0, 1.8, 1.0) + float3(0.0, 1e-3, 0.0));
            float3 Nc = normalize((i.ws - i.centre.xyz + float3(0.0, i.centre.w * 0.3, 0.0)) * float3(1.0, 1.4, 1.0) + float3(0.0, 1e-3, 0.0));
            float nv = saturate(dot(normalize(lerp(Np, Nc, 0.5)), V));
            float under = saturate((i.ws.y - 2150.0) / 200.0);       // the flat base stays solid
            // the ragged noise works only on the true outline of the cloud (never holes in the middle of a top)
            float rim = smoothstep(0.75, 1.0, length((i.ws - i.centre.xyz) * float3(1.0, 1.4, 1.0)) / max(i.centre.w, 1.0));
            float body = (nv - 0.1) * 1.7 + (n - 0.5) * 1.8 * rim;
            float e = 0.5;
            if (wisps && rim > 0.001) e = ZNoise3(i.ws / 170.0 + float3(0.0, _WorldTime * 0.01, 0.0));     // small ragged wisps
            float k = (1.0 - i.torn * 0.35), flatBase = (1.0 - under) * 0.9;
            core = saturate(max(saturate(body), flatBase) * k);
            full = saturate(max(saturate(body + (e - 0.5) * 1.5 * rim), flatBase) * k);
        }
        ENDHLSL

        Pass
        {
            // the dense core writes depth first
            Name "CloudDepth" Tags { "LightMode"="SRPDefaultUnlit" }
            ColorMask 0 ZWrite On ZTest LEqual Cull Back
            HLSLPROGRAM
            #pragma vertex CloudVert
            #pragma fragment Frag
            half4 Frag(Varyings i) : SV_Target
            {
                float core, full; PuffAlpha(i, PuffNoise(i), false, core, full);
                clip(core - 0.75);                          // the smooth core only: no pinholes from the wisps
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "Forward" Tags { "LightMode"="UniversalForward" }
            Blend One OneMinusSrcAlpha ZWrite Off ZTest LEqual Cull Back
            HLSLPROGRAM
            #pragma vertex CloudVert
            #pragma fragment Frag
            half4 Frag(Varyings i) : SV_Target
            {
                float3 V = normalize(_WorldSpaceCameraPos - i.ws);
                float n = PuffNoise(i);
                // where the depth pass drew the core the cloud is solid; only outside it does it fray (no ground through holes)
                float aCore, aFull; PuffAlpha(i, n, true, aCore, aFull);
                float a = aCore >= 0.75 ? 1.0 : min(aFull, aCore / 0.75);
                if (a < 0.003) discard;
                float3 N = PuffNormal(i);
                // soft light: wrapped sun, a bright edge where the sun grazes, the sky's blue in the shadows, grey bases
                float3 Nb = normalize((i.ws - i.puff.xyz) * float3(1.0, 1.8, 1.0) + float3(0.0, 1e-3, 0.0));
                // shade between the puffs: a face turned in toward the middle of its cloud is hidden by the other puffs;
                // the lower half of a puff lies in the shadow of the puffs above it
                float3 toOut = normalize(i.ws - i.centre.xyz + float3(0.0, i.centre.w * 0.6, 0.0));
                float ao = lerp(0.76, 1.0, saturate(dot(toOut, Nb) * 0.6 + 0.55)) * lerp(0.78, 1.0, saturate(Nb.y * 0.7 + 0.65));
                float ndl = dot(N, _ZSunDir.xyz);
                float wrap = saturate(ndl * 0.6 + 0.4);
                float3 sunC = _ZSunColor.rgb * _ZSunDir.w * (wrap * wrap * 1.1 + 0.25 * pow(saturate(1.0 - abs(ndl)), 3.0));
                // the shadows take the colour of the sky above them: blue by day, red and orange once the sky burns
                float3 amb = lerp(_ZAmbientGround.rgb * 1.2, ZSkyColor(normalize(N + float3(0.0, 0.7, 0.0))) * 1.15, N.y * 0.5 + 0.5);
                float3 lp = normalize(_ZPlasmaPos.xyz - i.ws);
                float pInt = ZPlasmaIntensity(i.ws);
                float3 pl = _ZPlasmaColor.rgb * pInt * saturate(dot(N, lp) * 0.5 + 0.5) * 1.8;     // the explosion is the stronger light: the deck takes its colour
                // the thin edges of a cloud in front of the explosion are lit through by it
                float thin = 1.0 - a;
                float rimP = pow(saturate(dot(-V, lp)), 3.0) * (0.35 + 1.6 * thin) * saturate(dot(N, lp) * 0.8 + 0.6);
                float base = lerp(0.58, 1.05, smoothstep(0.0, 0.6, i.color.g)) * ao;    // the grey flat bases and the shaded folds
                float3 alb = float3(0.96, 0.97, 1.0) * lerp(0.86, 1.0, i.color.r) * base * lerp(0.92, 1.04, n);
                alb = lerp(alb * float3(0.76, 0.8, 0.9), alb, smoothstep(0.0, 0.2, i.color.g));      // flat, darker, bluer bases
                alb = lerp(alb, _PalDustTan.rgb * 0.8, i.torn * 0.6);
                float3 c = alb * (sunC * 0.85 + amb + pl) + _ZPlasmaColor.rgb * pInt * rimP * 1.6;
                // condensation at the shock front: a white flash racing over the cloud tops
                c += float3(1.0, 0.97, 0.92) * i.front * (0.9 + 0.8 * pInt) * (1.0 - i.torn);
                c = ZFinish(c, i.ws);
                return half4(c * a, a);
            }
            ENDHLSL
        }
    }
}
