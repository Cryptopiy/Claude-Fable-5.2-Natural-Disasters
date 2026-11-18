Shader "Zero/Fireball"
{
    // The explosion cloud and the wake of the burning body: every billow is one displaced sphere (ImpactFX lays dozens of
    // them out as wake, fireball, stem, mushroom head, base surge and the rampart of the blast wave).
    // Drawn as gas, not as a solid: the opacity is the path of the view ray through the billow times an uneven,
    // boiling density (3D noise through the volume, in units of the billow's own size, flowing as it rises), so the core
    // is dense and dark, the limb thins out into wisps, and neighbouring billows melt into one mass. Light is scattered:
    // what lies deep on the side away from the sun is dark, thin gas glows when the sun or the fire is behind it.
    // Hot: an incandescent turbulent body (white -> gold -> orange -> deep red) that cools into smoke from the cracks out.
    // Premultiplied alpha. _Seed comes per billow from a property block.
    Properties
    {
        _Heat ("Heat 0..1.5", Float) = 1
        _Power ("Intensity", Float) = 30
        _Alpha ("Opacity", Float) = 1
        _Seed ("Seed", Float) = 0
        _Rise ("Upward flow speed", Float) = 0.06
        _Scale ("Noise scale", Float) = 3
        _Disp ("Billow displacement", Float) = 0.3
        _Under ("Fire light from below", Float) = 0.5
        _Haze ("Haze density scale", Float) = 0.35
        _Amb ("Ambient light scale", Float) = 1
        _Rim ("Fire backlight scale", Float) = 1
        _HeatFall ("Heat falls off with distance from the body (m), 0 = off", Float) = 0
        _FootFade ("Height over which the foot dissolves (m)", Float) = 2600
        _Dust ("0 = smoke, 1 = pale dust", Float) = 0
        _GlowTop ("The glow fades out at this height above the ground (m), 0 = off", Float) = 0
        _Cracks ("0 = glow everywhere it is hot, 1 = only through gaps in the smoke", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-60" "RenderPipeline"="UniversalPipeline" }
        Blend One OneMinusSrcAlpha ZWrite Off Cull Back
        Pass
        {
            Name "Forward" Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "ZeroCommon.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float _Heat; float _Power; float _Alpha; float _Seed; float _Rise; float _Scale; float _Disp; float _Under; float _Haze; float _Amb; float _Rim; float _HeatFall; float _FootFade; float _Dust; float _GlowTop; float _Cracks;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 ws : TEXCOORD0; float3 nws : TEXCOORD1; float3 wsC : TEXCOORD2; float rad : TEXCOORD3; };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                float3 d = normalize(v.positionOS.xyz);
                float3 so = float3(_Seed * 7.13, _Seed * 3.71, _Seed * 5.19);
                // the billow boils: big slow lobes and smaller fast ones
                float n1 = ZFbm3(d * 1.9 + so + float3(0.0, -_WorldTime * _Rise * 0.5, 0.0), 3);
                float n2 = ZNoise3(d * 6.5 + so * 1.7 + float3(0.0, -_WorldTime * _Rise * 1.3, 0.0));
                float3 p = v.positionOS.xyz + v.normalOS * ((n1 - 0.45) * _Disp + (n2 - 0.5) * _Disp * 0.35);
                o.ws = TransformObjectToWorld(p);
                o.nws = TransformObjectToWorldNormal(v.normalOS);
                o.wsC = ZCurve(o.ws);
                o.positionCS = TransformWorldToHClip(o.wsC);
                float4x4 m = GetObjectToWorldMatrix();
                o.rad = length(float3(m._m00, m._m10, m._m20));       // the billow's size in metres
                return o;
            }
            half4 Frag(Varyings i) : SV_Target
            {
                float3 V = normalize(_WorldSpaceCameraPos - i.wsC);
                float3 Ns = normalize(i.nws);                         // the round body of the billow: no facets, no relief
                float nv = saturate(dot(Ns, V));
                float3 so = float3(_Seed * 7.13, _Seed * 3.71, _Seed * 5.19);
                // the gas: domain-warped noise through the volume, measured in the billow's own size, rising as it boils
                float tt = _WorldTime * _Rise;
                float3 p = i.ws / max(i.rad * 0.6, 1.0) + so;
                float3 w = float3(ZNoise3(p * 0.8 + float3(0.0, -tt, 0.0)), ZNoise3(p * 0.8 + float3(19.1, -tt, 7.3)), ZNoise3(p * 0.8 + float3(37.3, 0.0, -tt))) - 0.5;
                float3 q = (p + w * 1.3 + float3(0.0, -tt * 1.6, 0.0)) * (_Scale * 0.45);
                float n = ZFbm3(q, 4);                                   // the big rolls, about 0.25 .. 0.75
                // small eddies that roll faster, warped by the big ones (filaments, not blotches)
                float t2 = ZFbm3(q * 3.3 + w * 1.2 + 7.0 - float3(0.0, tt * 2.2, 0.0), 3);

                // opacity = the path through the billow times the density: an opaque core, a limb that frays into wisps
                float thick = pow(nv, 0.6);
                float dens = saturate(n * 1.9 - 0.42 + (t2 - 0.5) * 0.5);
                // seen from close by (the wall at the end) a finer roll of dust appears, so it never turns into a smooth dune
                float nearK = saturate(1.6 - distance(i.wsC, _WorldSpaceCameraPos) / max(i.rad * 2.5, 1.0));
                float t3 = 0.5;
                if (nearK > 0.001)
                {
                    t3 = ZFbm3(i.ws / 800.0 + w * 0.8 - float3(0.0, _WorldTime * 0.05, 0.0), 3);
                    dens = saturate(dens + (t3 - 0.5) * 1.2 * nearK);
                }
                // heat as a temperature field: hottest where the view crosses the most gas, cooler toward the limb, broken
                // softly by the rolls and filaments; soot only on the cooling limb and only as the fire ages (never patches)
                float H = _HeatFall > 0.0 ? _Heat * exp(-distance(i.ws, _ZAst.xyz) / _HeatFall) : _Heat;     // the wake of the body cools behind it
                float heat = saturate(H * (0.15 + 0.95 * pow(thick, 1.5) * (0.4 + 0.9 * n) + 0.3 * t2) - (1.0 - saturate(H)) * 0.35);     // a hot core that follows the billows, gold, then an orange-red limb
                float soot = smoothstep(0.62, 0.25, t2) * saturate((0.8 - H) * 1.6) * (1.0 - 0.6 * thick);
                heat *= 1.0 - 0.7 * soot;
                float od = thick * (1.1 + 2.8 * dens) * 2.4 * (1.0 + 3.0 * heat);     // fire is optically thick: a front lobe hides the one behind
                // a soft limb: the outline of the mesh never shows, the gas thins out before it
                float limb = saturate((thick - 0.22) * 1.7 + (n - 0.5) * 1.6 + (t2 - 0.5) * 0.9);
                float alpha = saturate((1.0 - exp(-od)) * limb * _Alpha);
                alpha *= lerp(1.0, smoothstep(0.0, 0.55, thick), saturate(heat * 2.0));    // the cooler limb of a hot lobe is thin: no dark outline across the fire behind it
                // the foot dissolves into the dust over the ground instead of ending on a straight cut
                alpha *= saturate((i.ws.y - _ZCurve.z - 300.0) / _FootFade + (n - 0.5) * 0.8);
                // the far end of the wake frays out instead of ending in a cap
                if (_HeatFall > 0.0) alpha *= saturate(1.55 - distance(i.ws, _ZAst.xyz) / (_HeatFall * 2.4) + (n - 0.5) * 0.8);
                float3 hot = lerp(_PalFireRed.rgb * 0.7, _PalPlasmaOrange.rgb, saturate(heat * 2.4 - 0.2));
                hot = lerp(hot, _PalPlasmaGold.rgb, saturate(heat * 2.2 - 0.9));
                hot = lerp(hot, _PalPlasmaWhite.rgb, saturate(heat * 2.6 - 1.6));
                float glow = (0.9 + (_Power - 0.9) * pow(heat, 3.2)) * smoothstep(0.06, 0.3, heat);

                // scattered light: thin gas lets the light through, the deep side away from the sun and the bellies are dark
                float3 L = _ZSunDir.xyz;
                float ndl = dot(Ns, L);
                float depthToSun = (saturate(0.5 - ndl * 0.6) * (0.7 + 0.9 * dens) + dens * 0.3) * (0.3 + 0.7 * thick);
                float sunT = exp(-depthToSun * 2.6);
                float fwd = pow(saturate(dot(-V, L)), 4.0);
                float belly = saturate(-Ns.y);
                float top = saturate(Ns.y);
                float3 sun = _ZSunColor.rgb * _ZSunDir.w * (0.06 + 1.15 * sunT) * (0.8 + 1.8 * fwd * (1.0 - thick)) * (1.0 - 0.7 * belly) * (1.0 + 0.35 * top);
                float3 amb = lerp(_ZAmbientGround.rgb * 0.7, _ZAmbientSky.rgb * 1.15, Ns.y * 0.5 + 0.5) * _Amb * (0.75 - 0.3 * dens * thick);
                float3 fire = lerp(_PalFireRed.rgb, _PalPlasmaOrange.rgb, 0.6) * _Under * saturate(-Ns.y * 0.7 + 0.35) * (0.45 + 0.55 * n);
                // the fire behind: its light comes through the thin gas on the side that faces it
                float3 Lp = normalize(_ZPlasmaPos.xyz - i.ws);
                float back = pow(saturate(dot(-V, Lp)), 2.0) * (1.0 - thick * 0.85) * saturate(dot(Ns, Lp) * 1.2 + 0.35) * _Rim;
                float3 smokeDark = lerp(float3(0.10, 0.085, 0.075), float3(0.30, 0.25, 0.20), _Dust);
                float3 smokeLight = lerp(float3(0.62, 0.57, 0.52), float3(0.82, 0.70, 0.56), _Dust);
                // thin gas is paler than the dense core
                float3 alb = lerp(smokeDark, smokeLight, saturate(0.45 - dens * 0.35 + (Ns.y * 0.5 + 0.5) * 0.3 + (t2 - 0.5) * 0.1 + (1.0 - thick) * 0.15 + (t3 - 0.5) * 0.8 * nearK));
                float3 smoke = alb * (sun + amb + fire) + _ZPlasmaColor.rgb * min(_ZPlasmaColor.a, 3.0) * back * alb * 1.8;
                glow = glow > 4.0 ? 4.0 + (glow - 4.0) / (1.0 + 0.2 * (glow - 4.0)) : glow;    // a shoulder only at the top: the core stays white-hot, never a flat disc
                if (_GlowTop > 0.0) glow *= saturate(1.0 - (i.ws.y - _ZCurve.z) / _GlowTop);           // the column burns at its foot, not up its whole height
                glow *= lerp(1.0, smoothstep(0.5, 0.68, n), _Cracks);                                 // and only through the gaps in its smoke
                float3 c = smoke * (1.0 - saturate(heat * 1.4)) + hot * glow;

                float tr = ZFogTransmitEmissive(i.ws, _Haze);
                float3 sky = ZSkyColor(-V);
                c = lerp(sky, c, lerp(tr, 1.0, saturate(heat * 2.0)));
                return half4(c * alpha * _ZSkyParams.z, alpha);
            }
            ENDHLSL
        }
    }
}
