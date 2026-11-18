Shader "Hidden/MESOCYCLONE/Supercell"
{
    // Fullscreen Custom Pass (HDRP, AfterOpaqueAndSky).
    // Pass 0 "March": half/third resolution sphere-traced march of the storm field (at most 40 loop steps, bounded),
    //                 MRT: RT0 rgb = in-scattered light (pre-multiplied, haze-faded), a = transmittance;
    //                      RT1 r = mean scatter distance (m), g = haze fade.
    // Pass 1 "Composite": full resolution, depth-compare composite over the camera colour, stars, exposure.
    HLSLINCLUDE
    #pragma vertex Vert
    #pragma target 4.5
    #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch switch2

    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/RenderPass/CustomPass/CustomPassCommon.hlsl"
    #include "SupercellField.hlsl"

    #define SC_STEPS 40
    #define SC_FLASHES 4
    #define SC_GLOWS 2

    float4 _ScRes;          // xy = low-res target size in pixels, zw = 1 / size
    float4 _ScSunDir;       // xyz = direction TO the sun
    float4 _ScSunCol;       // rgb = radiance of a white cloud face lit by the sun (nits)
    float4 _ScSunGlow;      // rgb = radiance of the glow around the sun (nits), a = sun disc 0..1
    float4 _ScSunSky;       // xyz = true direction to the sun (for the glow and the disc)
    float4 _ScAmbTop;       // rgb = ambient radiance from above (nits)
    float4 _ScAmbBot;       // rgb = ambient radiance from below (nits)
    float4 _ScTint;         // x = green tint 0..1, y = haze length (m), z = star visibility 0..1, w = star radiance (nits)
    float4 _ScFog;          // x = extinction at the base (1/m), y = base height, z = scale height, w = unused
    float4 _ScFlashPos[SC_FLASHES];   // xyz local, w = radius (m)
    float4 _ScFlashCol[SC_FLASHES];   // rgb = radiance at the source (nits), w = 0 internal (glows through), 1 external (lights faces)
    float4 _ScGlowPos[SC_GLOWS];      // ground glows (town, power flash): xyz, w = radius
    float4 _ScGlowCol[SC_GLOWS];
    float4 _ScCore;                   // the light deep in the rain behind the vortex: xyz local, w = radius (m)
    float4 _ScCoreCol;                // rgb = radiance at the source (nits)
    float4 _ScCore2;                  // the light inside the departing storm in the tear: xyz local, w = radius (m)
    float4 _ScCore2Col;               // rgb = radiance at the source (nits), a = 1
    TEXTURE2D(_ScCloudTex);
    TEXTURE2D(_ScAuxTex);

    float3 ScViewRay(float2 uv)
    {
        float3 pRWS = ComputeWorldSpacePosition(uv, UNITY_RAW_FAR_CLIP_VALUE, UNITY_MATRIX_I_VP);
        return normalize(pRWS);
    }

    // transmittance of HDRP's exponential height fog along the ray up to distance t (so clouds drown in the same fog as the land)
    float ScFogT(float3 ro, float3 rd, float t)
    {
        float H = max(_ScFog.z, 1.0);
        float h0 = max(ro.y - _ScFog.y, 0.0);
        float k = rd.y / H;
        float kt = k * t;
        float integral = abs(kt) < 1e-3 ? t : (1.0 - exp(-kt)) / k;
        return exp(-_ScFog.x * exp(-h0 / H) * max(integral, 0.0));
    }

    float3 ScNormal(float3 p, float eps)
    {
        float s, m;
        const float2 k = float2(1.0, -1.0);
        return normalize(k.xyy * MapClouds(p + k.xyy * eps, s, m) + k.yyx * MapClouds(p + k.yyx * eps, s, m) +
                         k.yxy * MapClouds(p + k.yxy * eps, s, m) + k.xxx * MapClouds(p + k.xxx * eps, s, m));
    }

    // light from a flash behind the vortex does not reach what stands in its shadow: this makes the silhouette.
    // The shadow caster is the real funnel profile (ScFunnelR at the height where the light's path passes the
    // vortex, rope drag included), not a cylinder of the ground radius with a hard height cut: those projected to
    // ruler-straight lit polygons in the rain. edgeN (0..1, world-anchored noise) rags the edge like a curtain, the
    // penumbra widens behind the vortex (a flash is a broad source) and the top fades out softly in the cloud.
    // _ScTor3.x scales it (a diagnosis knob: 0 turns it off).
    float ScFunnelShadow(float3 p, float3 lp, float edgeN)
    {
        if (_ScTor.w < 0.999 || _ScTor3.x <= 0.0) return 1.0;
        float H = _ScTorRot.z;
        float2 a = p.xz, c = _ScTor.xy;
        float2 ab = lp.xz - a;
        float L2 = max(dot(ab, ab), 1.0);
        float tt = saturate(dot(c - a, ab) / L2);
        float h = lerp(p.y, lp.y, tt);
        float hn = saturate(h / H);
        c += _ScTor2.y * float2(0.92, 0.4) * 800.0 * (1.0 - hn) * (1.0 - hn);   // rope drag, as SdFunnel
        tt = saturate(dot(c - a, ab) / L2);
        h = lerp(p.y, lp.y, tt);
        float Rf = ScFunnelR(h) * 1.05 + 60.0;
        float dist = length(a + ab * tt - c) + (edgeN - 0.5) * Rf * 0.6;        // ragged, curtain-like edge
        float pen = 0.3 * tt * sqrt(L2);                                          // penumbra widens behind the vortex
        float side = smoothstep(Rf * 0.7, Rf * 1.1 + pen, dist);
        float below = 1.0 - smoothstep(H * 0.85, H * 1.35, h);                   // soft top, no hard 'h < H' plane
        float self = 1.0 - smoothstep(Rf * 0.85, Rf * 1.15, length(a - c));      // the vortex does not shadow itself (soft)
        return lerp(1.0, lerp(1.0, side, below * (1.0 - self)), _ScTor3.x);
    }

    // Henyey-Greenstein phase times 4 pi (1 = isotropic); c = cosine between the light's travel and the view
    float ScHG4pi(float c, float g)
    {
        float g2 = g * g;
        return (1.0 - g2) / pow(max(1.0 + g2 - 2.0 * g * c, 1e-3), 1.5);
    }

    // the silver lining: light from the flashes and from the core behind the vortex, scattered forward through the
    // funnel's and the wall's thin outer skin toward the eye. March puts it only on the outer part of the soft skin,
    // so the rim lights up and the body stays black. Same glow falloff as ScFlashLight; the phase is capped at 8x.
    float3 ScRim(float3 p, float3 rd)
    {
        float3 sum = 0.0;
        for (int i = 0; i < SC_FLASHES; i++)
        {
            float3 v = _ScFlashPos[i].xyz - p;
            float R = _ScFlashPos[i].w;
            float r = length(v);
            float q = r / (0.28 * R);
            float glow = exp(-2.2 * r / R) / (1.0 + q * q);
            sum += _ScFlashCol[i].rgb * glow * min(ScHG4pi(dot(rd, v / max(r, 1.0)), 0.65), 8.0);
        }
        if (_ScCoreCol.r + _ScCoreCol.g + _ScCoreCol.b > 1e-5)
        {
            float3 v = _ScCore.xyz - p;
            float R = _ScCore.w;
            float r = length(v);
            float q = r / (0.35 * R);
            float g = exp(-1.4 * r / R) / (1.0 + q * q);
            sum += _ScCoreCol.rgb * g * min(ScHG4pi(dot(rd, v / max(r, 1.0)), 0.65), 8.0);
        }
        return sum * _ScTorRot.w;
    }

    // edgeN: the ragged-edge noise for the funnel shadow (ThinMedia's tap in the rain, 0.5 on surfaces)
    float3 ScFlashLight(float3 p, float3 N, float crevice, float shadowed, float body, float probe, float edgeN)
    {
        float3 sum = 0.0;
        // the lowered wall cloud over the vortex is the underside of the whole storm: it hangs dark over the funnel and
        // grows into it, and the lit rain and cloud beyond frame both. Lit like the rest it read as a cap or a mushroom
        // on the funnel. Full for the steady core glow, partly for the flashes (they do light it from inside)
        float wallDark = 0.0;
        [branch] if (probe > 0.5 && body < 0.5 && _ScTor3.z > 0.0)
        {
            float wIn = max(1100.0, (1.1 * ScTorCap(_ScTor.z, _ScTor2.x) + 150.0) * step(0.001, _ScTor.w));
            wallDark = _ScTor3.z * saturate(_ScWall.z) * smoothstep(wIn + 1400.0, wIn * 0.8, length(p.xz - _ScWall.xy))
                     * (1.0 - smoothstep(SC_CLOUD_BASE - 200.0, SC_CLOUD_BASE + 600.0, p.y));
        }
        for (int i = 0; i < SC_FLASHES; i++)
        {
            float3 v = _ScFlashPos[i].xyz - p;
            float R = _ScFlashPos[i].w;
            float r = length(v);
            float q = r / (0.28 * R);
            float glow = exp(-2.2 * r / R) / (1.0 + q * q);
            float nl = dot(N, v / max(r, 1.0));
            float face = lerp(crevice, saturate(nl * 0.6 + 0.4), _ScFlashCol[i].w);
            face = lerp(face, saturate(nl * 0.9 + 0.03) * 1.3 + 0.02, body);
            [branch] if (probe > 0.5 && body < 0.5 && _ScFlashCol[i].w < 0.5 && glow > 0.003)
            {
                float sd2, md2;
                float thick = saturate(-MapClouds(p + v / max(r, 1.0) * 700.0, sd2, md2) / 420.0);
                // the wall's glow-through fades out across the funnel/wall blend band (body = fw): continuous at the joint
                glow *= lerp(lerp(1.25, 0.45, thick), 1.0, saturate(body * 2.0));
                // the tear: an internal flash lights veins and chambers, never one even wash over the whole storm.
                // The mask is anchored to the storm (it moves ~190 m/s in the world; world anchoring would slide)
                // and seeded by the flash's offset in the storm, never by the slot index (slots re-sort and would pop).
                // Its mean is about 1, so the energy is kept.
                [branch] if (_ScClear.y > 0.001)
                {
                    float3 pS = float3(p.x - _ScStorm.x, p.y, p.z - _ScStorm.y);
                    float3 sd = float3(_ScFlashPos[i].x - _ScStorm.x, 0.0, _ScFlashPos[i].z - _ScStorm.y) * 0.13;
                    // chambers kilometres across (a 5.2 km tile speckled the whole storm like a leopard at 15 km)
                    float vein = ScNoise(pS * float3(1.0, 2.0, 1.0) + sd, 24000.0).b;
                    glow *= lerp(1.0, 0.55 + 0.9 * smoothstep(0.3, 0.7, vein), _ScClear.y);
                }
            }
            // an explicit if, not ?: (FXC evaluates both sides of ?:): unused slots and unshadowed media skip the shadow
            float sh = 1.0;
            [branch] if (shadowed > 0.5 && dot(_ScFlashCol[i].rgb, float3(1.0, 1.0, 1.0)) > 1e-5) sh = ScFunnelShadow(p, _ScFlashPos[i].xyz, edgeN);
            sum += _ScFlashCol[i].rgb * glow * face * sh * (1.0 - 0.6 * wallDark);
        }
        for (int j = 0; j < SC_GLOWS; j++)
        {
            float3 v = _ScGlowPos[j].xyz - p;
            float R = _ScGlowPos[j].w;
            float r = length(v);
            float q = r / (0.35 * R);
            sum += _ScGlowCol[j].rgb * exp(-1.5 * r / R) / (1.0 + q * q) * saturate(dot(N, v / max(r, 1.0)) * 0.5 + 0.5);
        }
        if (_ScCoreCol.r + _ScCoreCol.g + _ScCoreCol.b > 1e-5)
        {
            // rain and cloud between the eye and this light glow forward (rain scatters ahead, like headlights in fog);
            // cloud faces wrap the light; the funnel and the debris catch it only on their rims: a silhouette
            float3 v = _ScCore.xyz - p;
            float R = _ScCore.w;
            float r = length(v);
            float q = r / (0.35 * R);
            float g = exp(-1.4 * r / R) / (1.0 + q * q);
            float nl = dot(N, v / max(r, 1.0));
            float f = probe < 0.5 ? 0.35 + 1.6 * saturate(-nl) * saturate(-nl) : saturate(nl * 0.6 + 0.45);
            // funnel and debris: rims only, plus a faint fill from the lit rain all around them (_ScTor3.w), so a
            // wedge that fills the glass is a dark turning wall with its striations, not a black hole
            f = lerp(f, saturate(nl * 1.1 + 0.12) * 1.2 + _ScTor3.w, body);
            f *= 1.0 - wallDark;
            float shc = 1.0;
            [branch] if (shadowed > 0.5) shc = ScFunnelShadow(p, _ScCore.xyz, edgeN);
            // the wrap: rain scatters the glow around the wedge many times, so even the curtain in its shadow keeps a
            // little of it (only where there is a wrap, i.e. the unseen): the wedge is half-seen inside a lit veil
            if (probe < 0.5) shc = max(shc, 0.3 * _ScTor2.z);
            sum += _ScCoreCol.rgb * g * f * shc;
        }
        if (_ScCore2Col.r + _ScCore2Col.g + _ScCore2Col.b > 1e-5)
        {
            // the tear: one placed glow in the heart of the departing storm instead of a flat blue night ambient on
            // every face. It glows through like an internal flash (the thin media pass crevice = 1)
            float3 v = _ScCore2.xyz - p;
            float R = _ScCore2.w;
            float r = length(v);
            float q = r / (0.35 * R);
            float g = exp(-1.6 * r / R) / (1.0 + q * q);
            float f = lerp(crevice, saturate(dot(N, v / max(r, 1.0)) * 0.9 + 0.03) * 1.3 + 0.02, body);
            sum += _ScCore2Col.rgb * g * f;
        }
        return sum;
    }

    // the forward-flank core's own shadow toward the sun: its sunward face is lit, its heart dark
    float ScCoreSunT(float3 p)
    {
        float2 L = _ScSunSky.xz;
        float l = length(L);
        if (l < 1e-3) return 1.0;
        float2 ab = float2(SC_CORE_AX, SC_CORE_AZ);
        float2 u = (p.xz - _ScPrecip.xy) / ab, v = (L / l) / ab;
        float A = dot(v, v), B = dot(u, v), C = dot(u, u) - 1.0;
        if (C >= 0.0) return 1.0;                                       // in the soft rim: fully lit
        float s = (-B + sqrt(B * B - A * C)) / A;                       // metres to the rim, toward the sun
        return exp(-s * lerp(_ScPrecip.z, _ScPrecip.w, 0.3) * 0.6);
    }

    float3 ScAlbedo(float mat)
    {
        if (mat < 2.5) return float3(1.0, 1.0, 1.0);              // anvil, tower
        if (mat < 3.5) return float3(0.80, 0.82, 0.86);           // deck, wall cloud
        if (mat < 4.5) return float3(0.72, 0.76, 0.84);           // shelf
        if (mat < 5.5) return float3(0.50, 0.50, 0.52);           // funnel
        if (mat < 6.5) return float3(0.32, 0.27, 0.22);           // debris: the same dust as the VFX skirt (0.34, 0.28, 0.21)
        return float3(0.46, 0.38, 0.29);                          // dust
    }

    // fw: 0 wall cloud .. 1 funnel (MapClouds); soft: the surface's skin thickness (m)
    float3 ScShade(float3 p, float3 rd, float t, float mat, float d0, float fw, float soft)
    {
        float s, m;
        // the funnel and the wall cloud it grows out of are one surface: across the blend band their normals, green,
        // flash response and albedo all lerp by fw, so the joint shows no step
        bool pair = (mat > 2.5 && mat < 3.5) || (mat > 4.5 && mat < 5.5);
        float fwp = pair ? fw : 0.0;
        // crisp lips on the plates and the anvil (the 390 m risers were blurred into soft bands); deck, shelf look unchanged
        float epsB = mat < 2.5 ? 60.0 + t * 0.006 : 120.0 + t * 0.012;
        float eps = lerp(epsB, clamp(0.6 * soft, 30.0, 150.0) + t * 0.006, fwp);   // ~70 m on a 600 m funnel at 4 km
        float3 N = ScNormal(p, eps);
        float ao = saturate(0.3 + 0.7 * MapClouds(p + N * 500.0, s, m) / 500.0);
        float3 L = _ScSunDir.xyz;
        float3 col = lerp(_ScAmbBot.rgb, _ScAmbTop.rgb, N.y * 0.5 + 0.5) * ao;
        if (mat < 1.5)
        {
            // the anvil's underside is not one flat value: the thin rim glows (1.2), the open sheet is a little
            // darker (0.92), the heavy core over the updraft goes STORM_SLATE (0.5). Sun and flash terms untouched.
            float thk = ScAnvilThick(p.xz);
            col *= 1.2 - 0.28 * saturate((thk - 420.0) / 3580.0) - 0.42 * saturate((thk - 4000.0) / 3300.0);
        }
        if (_ScSunCol.r + _ScSunCol.g + _ScSunCol.b > 1e-4)
        {
            float3 po = p + N * (260.0 + max(-d0, 0.0) * 1.5);            // start outside the fuzzy skin
            float vis = smoothstep(-40.0, 150.0, MapClouds(po + L * 400.0, s, m));
            vis *= smoothstep(-60.0, 220.0, MapClouds(po + L * 1400.0, s, m));
            vis *= smoothstep(-80.0, 300.0, MapClouds(po + L * 4500.0, s, m));
            vis = lerp(0.2, 1.0, vis);                                     // recesses keep some bounced light
            float ndl = saturate(dot(N, L) * 0.62 + 0.38);
            ndl *= ndl;
            col += _ScSunCol.rgb * vis * ndl * (0.5 + 0.5 * ao) * 1.35;
        }
        // green is light that crossed the hail core: undersides and the lip only, never faces lit from the front,
        // never the dust (the bible: never green on front-lit cloud, STORM_SLATE everywhere else)
        float under = saturate(0.25 - N.y * 1.25);          // 1 facing down, 0.25 vertical, 0 facing up
        float green = _ScTint.x * under * (pair ? lerp(1.0, 0.35, fw) : (mat > 2.5 && mat < 4.5 ? 1.0 : (mat > 6.5 ? 0.0 : 0.35)));
        col *= lerp(float3(1.0, 1.0, 1.0), float3(0.62, 1.0, 0.78), green);
        float body = pair ? fw : (mat > 4.5 ? 1.0 : 0.0);
        col += ScFlashLight(p, N, lerp(1.7, 0.8, ao), mat > 5.5 ? 1.0 : 0.0, body, 1.0, 0.5);
        return col * (pair ? lerp(ScAlbedo(3.0), ScAlbedo(5.0), fw) : ScAlbedo(mat));
    }

    struct MarchOut { float4 c0 : SV_Target0; float4 c1 : SV_Target1; };

    MarchOut March(Varyings varyings)
    {
        float2 pix = varyings.positionCS.xy;
        float2 uv = pix * _ScRes.zw;
        float3 rd = ScViewRay(uv);
        float3 ro = _WorldSpaceCameraPos.xyz;

        // far-most depth of the full-resolution block this texel covers
        float2 fpix = uv * _ScreenSize.xy;
        float2 o = 0.25 * _ScreenSize.xy * _ScRes.zw;
        float2 fmax = _ScreenSize.xy - 1.0;
        float dA = LoadCameraDepth(clamp(fpix + float2(-o.x, -o.y), 0.0, fmax));
        float dB = LoadCameraDepth(clamp(fpix + float2(o.x, -o.y), 0.0, fmax));
        float dC = LoadCameraDepth(clamp(fpix + float2(-o.x, o.y), 0.0, fmax));
        float dD = LoadCameraDepth(clamp(fpix + float2(o.x, o.y), 0.0, fmax));
    #if UNITY_REVERSED_Z
        float dRaw = min(min(dA, dB), min(dC, dD));
    #else
        float dRaw = max(max(dA, dB), max(dC, dD));
    #endif
        float tMax = 170000.0;
        if (dRaw != UNITY_RAW_FAR_CLIP_VALUE)
            tMax = length(ComputeWorldSpacePosition(uv, dRaw, UNITY_MATRIX_I_VP));
        if (rd.y < -1e-4) tMax = min(tMax, ro.y / -rd.y);
        if (rd.y > 1e-4) tMax = min(tMax, (16500.0 - ro.y) / rd.y);

        float jit = InterleavedGradientNoise(pix, (int)_ScOrigin.w);
        float t = 40.0 + 60.0 * jit;
        float T = 1.0;
        float3 S = 0.0;
        float tAcc = 0.0, wAcc = 0.0, fAcc = 0.0;
        float prevD = 1.0e5, prevT = t;
        int shades = 0;
        float3 solidCol = 0.0;
        float3 rimCol = 0.0;
        bool inside = false;
        float2 cs = ScCoreSpan(ro, rd);                                   // the forward-flank core along this ray

        for (int i = 0; i < SC_STEPS; i++)
        {
            if (t >= tMax || T < 0.02) break;
            float3 p = ro + rd * t;
            float soft, mat, fw;
            float d = MapClouds(p, soft, mat, fw);
            float minStep = 22.0 + t * 0.011;
            if (d < 0.0 && prevD > 0.0 && (t - prevT) > 2.5 * minStep)
            {
                float a = prevT, b = t, s2, m2;                       // refine the surface hit (6 bisections)
                for (int j = 0; j < 6; j++)
                {
                    float mid = 0.5 * (a + b);
                    if (MapClouds(ro + rd * mid, s2, m2) < 0.0) b = mid; else a = mid;
                }
                t = b; p = ro + rd * t;
                d = MapClouds(p, soft, mat, fw);
            }
            // explicit ifs, not ?: (FXC evaluates both sides of ?:, texture taps included); the core's span test keeps
            // its fetch off every other sample. The core counts as rain, so the twilight green lands in it too
            float rainPart = 0.0, hailPart = 0.0, edgeN = 0.5, thin = 0.0, core = 0.0;
            [branch] if (p.y < SC_CLOUD_BASE + 200.0) thin = ThinMedia(p, rainPart, edgeN);
            [branch] if (t > cs.x - 500.0 && t < cs.y + 500.0) core = ScCore(p, hailPart);
            thin += core; rainPart += core;
            float stepLen;
            float dens = thin;
            float3 col = 0.0;
            if (d < 0.0)
            {
                if (!inside && shades < 2)
                {
                    solidCol = ScShade(p, rd, t, mat, d, fw, soft);
                    shades++;
                    // the silver rim: on the funnel and the wall across the blend band, and on the debris dome where
                    // the core light is not hidden behind the vortex (computed once per entry, at most twice a pixel)
                    float rimW = 0.0;
                    if (mat > 5.5 && mat < 6.5) rimW = 0.6 * ScFunnelShadow(p, _ScCore.xyz, 0.5);
                    else if ((mat > 2.5 && mat < 3.5) || (mat > 4.5 && mat < 5.5)) rimW = fw;
                    rimCol = 0.0;
                    [branch] if (rimW > 0.01) rimCol = ScRim(p, rd) * rimW;
                }
                inside = true;
                float sigma = (mat > 6.5 ? 1.0 / 260.0 : 1.0 / 110.0) * saturate(-d / soft);
                if (mat > 6.5) sigma *= smoothstep(120.0, 700.0, t);          // the camera may be inside the dust
                // the dome thins toward its edge (it is 40 % dense at the rim), so it reads as dust, not a solid bar;
                // the same scaled footprint as SdDebris
                if (mat > 5.5 && mat < 6.5) sigma *= 1.0 - 0.6 * smoothstep(0.55, 1.0, length(p.xz - _ScTor.xy) / ScDomeR(_ScTor.z, _ScTor3.y));
                stepLen = clamp(soft * 0.45, minStep, 260.0 + t * 0.05) * (0.85 + 0.3 * jit);
                dens += sigma;
                // the rim lands on the outer quarter of the soft skin; the core stays black
                float skin = 1.0 - saturate(-d / soft);
                col = (solidCol + rimCol * skin * skin * skin) * sigma;
            }
            else
            {
                inside = false;
                float cap = thin > 2.0e-6 ? 320.0 + t * 0.09 : 2500.0 + t * 0.5;
                // sheets seen at a grazing angle (anvil, deck, shelf from below): their distance is vertical, so the
                // free path along the ray is longer; the bisection below absorbs any overshoot into a hanging lobe
                float along = d;
                if (mat < 1.5 || (mat > 2.5 && mat < 4.5))
                {
                    float so;
                    float solid = min(SdTower(p, so), SdFunnel(p, so));
                    along = min(d * 0.85 / max(abs(rd.y), 0.1), max(solid, d));
                }
                stepLen = clamp(along, minStep, cap);
            }
            // the core has no surface: land the first sample just inside its span, then take about five samples across
            // it, so it never pops as the distance changes (a miss leaves cs.x at 1e9: no change)
            if (t < cs.x) stepLen = min(stepLen, cs.x - t + (0.25 + 0.5 * jit) * minStep);
            else if (t < cs.y) stepLen = min(stepLen, max(minStep, (cs.y - cs.x) * 0.2));
            if (dens > 1.0e-7)
            {
                // thin media: lit by the flashes (the lit rain), by the ambient, and green in twilight
                if (thin > 0.0)
                {
                    float3 amb = (_ScAmbTop.rgb + _ScAmbBot.rgb) * 0.5;
                    float3 tl = amb + _ScSunCol.rgb * 0.25;
                    [branch] if (core > 0.0)
                    {
                        // under the storm: less sky from above (x0.6) and its own shadow; hail whiter than rain (1.0 vs 0.85)
                        float3 tc = (amb * 0.6 + _ScSunCol.rgb * 0.25 * ScCoreSunT(p)) * lerp(0.85, 1.0, hailPart);
                        tl = lerp(tl, tc, core / thin);
                    }
                    tl *= lerp(float3(1.0, 1.0, 1.0), float3(0.55, 1.0, 0.72), _ScTint.x * saturate(rainPart / max(thin, 1e-9)));
                    tl += ScFlashLight(p, -rd, 1.0, 1.0, 0.0, 0.0, edgeN);
                    col += tl * thin;
                }
                float a = 1.0 - exp(-dens * stepLen);
                float fade = exp(-t / _ScTint.y);
                float w = T * a;
                S += w * fade * col / dens;
                tAcc += w * t; fAcc += w * fade; wAcc += w;
                T *= 1.0 - a;
            }
            prevD = d; prevT = t;
            t += stepLen;
        }

        MarchOut o2;
        o2.c0 = float4(S, T);
        o2.c1 = float4(wAcc > 1e-5 ? tAcc / wAcc : 200000.0, wAcc > 1e-5 ? fAcc / wAcc : 1.0, 0.0, 0.0);
        return o2;
    }

    float ScHash(float3 p)
    {
        p = frac(p * 0.3183099 + 0.1);
        p *= 17.0;
        return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
    }

    float3 ScStars(float3 rd, float pixAngle)
    {
        float3 sum = 0.0;
        for (int layer = 0; layer < 2; layer++)
        {
            float scale = layer == 0 ? 190.0 : 95.0;
            float3 g = rd * scale;
            float3 cell = floor(g);
            float h = ScHash(cell + layer * 31.0);
            if (h > 0.985 - 0.02 * layer)
            {
                float3 c = cell + 0.5 + (float3(ScHash(cell + 7.0), ScHash(cell + 13.0), ScHash(cell + 23.0)) - 0.5) * 0.5;
                // onto the sky sphere: every hashed star is seen, not only those whose jittered centre happened to
                // fall near it
                c = normalize(c) * scale;
                float d2 = dot(g - c, g - c);
                float b = ScHash(cell + 3.7);
                float rpx = 0.8 * pixAngle * scale;
                float size = max(layer == 0 ? 0.012 : 0.03, rpx * rpx);
                float tw = 0.85 + 0.15 * sin(_ScOrigin.z * (2.0 + 5.0 * b) + b * 40.0);
                // magnitudes as in the real sky: many faint stars, few bright ones (N(>L) ~ L^-1.5; median ~0.19,
                // top 5 % above 1, brightest ~1.9)
                float amp = (layer == 0 ? 0.12 : 0.14) * pow(max(b, 0.02), -0.667);
                sum += lerp(float3(0.75, 0.85, 1.0), float3(1.0, 0.9, 0.78), ScHash(cell + 5.1)) * exp(-d2 / size) * amp * tw;
            }
        }
        return sum;
    }

    float4 Composite(Varyings varyings) : SV_Target
    {
        float2 pix = varyings.positionCS.xy;
        float2 uv = pix * _ScreenSize.zw;
        float2 uvLow = min(uv, 1.0 - 0.5 * _ScRes.zw) * _RTHandleScale.xy;
        float4 c = SAMPLE_TEXTURE2D_LOD(_ScCloudTex, s_linear_clamp_sampler, uvLow, 0);
        float4 aux = SAMPLE_TEXTURE2D_LOD(_ScAuxTex, s_linear_clamp_sampler, uvLow, 0);
        float dRaw = LoadCameraDepth(pix);
        bool sky = dRaw == UNITY_RAW_FAR_CLIP_VALUE;
        float3 pRWS = ComputeWorldSpacePosition(uv, dRaw, UNITY_MATRIX_I_VP);
        float3 rd = normalize(ComputeWorldSpacePosition(uv, UNITY_RAW_FAR_CLIP_VALUE, UNITY_MATRIX_I_VP));
        float tScene = sky ? 1.0e9 : length(pRWS);
        // thin near objects (wires, blades, pillars, wipers) keep crisp edges: anything nearer than the cloud gets none of it
        float w = sky ? 1.0 : smoothstep(0.90, 1.0, tScene / max(aux.x, 1.0));
        float fogT = ScFogT(_WorldSpaceCameraPos.xyz, rd, min(aux.x, 170000.0));
        float cover = (1.0 - c.a) * aux.y * fogT * w;
        float3 light = c.rgb * fogT * w;
        if (sky && _ScTint.z > 0.001 && rd.y > -0.02)
            light += ScStars(rd, length(ddx(rd))) * _ScTint.w * _ScTint.z * c.a * fogT * smoothstep(-0.01, 0.12, rd.y);
        if (sky && _ScSunGlow.r + _ScSunGlow.g > 1e-4)
        {
            float ca = saturate(dot(rd, _ScSunSky.xyz));
            float glow = pow(ca, 6.0) * 0.22 + pow(ca, 40.0) * 0.5 + pow(ca, 400.0) * 1.6;
            float disc = smoothstep(0.99994, 0.99997, ca) * _ScSunGlow.a * 60.0;
            float lowBand = exp(-max(rd.y, 0.0) * 9.0);
            light += _ScSunGlow.rgb * (glow * (0.35 + 0.65 * lowBand) + disc) * (1.0 - cover) * smoothstep(-0.02, 0.01, rd.y);
        }
        float dither = (InterleavedGradientNoise(pix, 0) - 0.5) / 255.0;
        return float4(light * GetCurrentExposureMultiplier() + dither, 1.0 - cover);
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" }
        Pass
        {
            Name "March"
            ZWrite Off ZTest Always Blend Off Cull Off
            HLSLPROGRAM
                #pragma fragment March
            ENDHLSL
        }
        Pass
        {
            Name "Composite"
            ZWrite Off ZTest Always Blend One SrcAlpha Cull Off
            HLSLPROGRAM
                #pragma fragment Composite
            ENDHLSL
        }
    }
    Fallback Off
}
