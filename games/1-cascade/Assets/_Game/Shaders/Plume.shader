Shader "Hidden/Cascade/Plume"
{
    // The eruption column and umbrella: one analytic density field with pyroclastic (cauliflower) displacement,
    // ray-marched at reduced resolution (pass 0, at most 64 steps) and composited over sky and far scenery (pass 1).
    // Pass 3 stores the scene distance each half-res texel was marched against, so the composite upsamples the plume
    // and the low volumes (pass 2) only from texels that match the pixel's own depth.
    // Everything is a function of uniforms derived from the eruption clock: seekable, no history.
    HLSLINCLUDE
    #pragma vertex Vert
    #pragma target 4.5
    #pragma only_renderers d3d11 vulkan
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/RenderPass/CustomPass/CustomPassCommon.hlsl"
    #include "Assets/_Game/Shaders/CascadeCommon.hlsl"
    #include "Assets/_Game/Shaders/CascadeCone.hlsl"
    #include "Assets/_Game/Shaders/CascadeSea.hlsl"

    TEXTURE2D_X(_PlumeTex);
    TEXTURE2D_X(_FxTex);
    TEXTURE2D_X(_PlDepthTex);   // half-res: the scene distance (m, capped at PL_TMAX) each plume/fx texel was marched against
    float4 _PlVent;     // xyz vent (absolute world), w vent radius
    float4 _PlCol;      // x front height above the vent, y radius growth per metre, z head factor, w extinction 1/m
    float4 _PlUmb;      // x umbrella radius, y base height above vent, z top height above vent, w column radius cap height
    float4 _PlGlow;     // rgb nits, a falloff height (m)
    float4 _PlMisc;     // x max iterations, y near limit (m), z cap whiteness, w noise time
    float4 _PlUv;       // xy rt-handle scale of the plume buffer, z = march resolution divisor
    float4 _PlFog;      // x 1/mean free path, y base height, z 1/scale height, w 1/mean free path of the ash air at sea level
    float4 _PlBolt0;    // xyz position (absolute), w intensity
    float4 _PlBolt1;
    float4 _PlSteam;    // x steam thread strength 0..1, y flash 0..1
    float4 _PlWind;     // xy drift direction, z lean coefficient (1/m)
    float4 _PlShock;    // x shell radius (m), y strength
    float4 _PlVeil;     // xyz axis of the clearing in the ash storm (world direction), w veil strength 0..1
    float4 _PlVeil2;    // x half angle of the clearing (rad), y depth scale of the veil (m), z EnvDirector.BoltFlash, w clock
    float4 _PlVeilCol;  // rgb nits of the ash veil
    float4 _PlUp;       // rgb nits: the fire below as the key light once the sun is gone (0 by day); also read by PlumeFx
    float4 _PlRoot;     // x depth of the ground below the vent (m, the scar floor while the vent lags behind it), y jet radius factor at the vent
    float4 _PlTherm[7]; // the head's thermals: xyz centre in vent space (wind-leaned frame), w radius (m)

    #define PL_TMAX 60000.0         // scene distances are stored up to this (R16F); farther is "background"
    #define PL_BILAT_TOL 0.04       // relative depth tolerance of the joint-bilateral upsample
    #define PL_FIRE_Y 250.0         // height of the fire light above the vent (m): fountain and glowing scar
    #define PL_FIRE_R 1600.0        // its falloff radius (m) for the low volumes (PlumeFx reads this and _PlUp as they are)
    // The column alone sees the fire as a bigger, stronger source (fountain, flows and the glowing scar together, a source
    // kilometres across seen from kilometres up): the key still reaches its middle once the sun is gone (night 380-500 s).
    // It is the column's main light at night: billow undersides lit orange, tops and creases charcoal (no emission fill).
    // The key is concentrated into the foot (R 4200 -> 3000: the middle of the column kept over half of the root's key and
    // the lower 3-4 km read as one orange carrot); the first kilometre keeps about 90-95% of its key, 6 km about 55%.
    #define PL_KEY_GAIN 3.3         // x _PlUp on the column (irradiance of a disc of radius PL_KEY_R: 1/(1 + h^2/R^2))
    #define PL_KEY_R 3000.0         // falloff radius (m) on the column
    #define PL_FIRE_FIELD 3000.0    // radius (m) of the fire field around the vent: fountain, scar, flows on the flanks
    #define PL_FIRE_AIM 0.75        // a column sample aims its fire shadow taps at the field below it (this share of its own
                                    // offset from the axis): seen from the vent point alone the trumpet shadows every flank
    #define PL_FIRE_SH 0.7          // fire shadow strength on the column: a source kilometres across casts soft shadows (sun 1.1);
                                    // 0.5 left the lobe above a crease nearly as lit as the lobe itself
    #define PL_LID_FLOOR 0.15       // share of the lid's under-light the column keeps below the neck (was 0.45: a flat lamp-shade)
    #define PL_LID_NEAR 14000.0     // ... rising to all of it over this depth (m) under the lid: the trunk close under the glowing
                                    // ceiling, and the flared neck, catch its light (300: no black drain in the middle of the lid)
    #define PL_NECK_FIRE 0.4        // on the lid and the lid-like parts (neck, trunk close under it) the under-light's fire shadow
                                    // keeps this floor: the source is kilometres across and the lit ash air fills in
    #define PL_ROOT_SKIN 0.45       // once dark, the root's ash skin glows at this share of its incandescent core
    #define PL_ROOT_POCKET 2.0      // the root's hot core shows through creases and torn pockets (up to 2.1x), its billows stay
                                    // dark ash (0.12): hot pockets in dark billows, not a smooth candle flame
    #define PL_NECK 1.1             // the column flares to (1 + this) x its radius as it feeds the lid (no T junction)
    #define PL_LID_CAP 2200.0       // feature radius (m) of the lid below its mid-plane: one pouched ceiling, not a 2 km sponge
    #define PL_AMB_WARM 0.4         // by day the column's shaded side sees the bright horizon, the sunlit island and its own lit
                                    // ash, not only the blue zenith: this share of the sky fill turns warm-neutral (ash, not cotton)
    #define PL_VEIL_UP 0.4          // the storm clearing reaches 1/this as far above its axis as below: the column rises out of it
    #define PL_VEIL_RIM 0.5         // the clearing's inner wall is lit: slate x (1 + this), plus the fire glow below...
    #define PL_VEIL_FIRE 0.08       // ... in amber, x the luminance of _PlUnder (lava red on slate read lilac in the cool grade)
    #define PL_VEIL_BOLT 1.0        // a bolt lights the veil around its channel: x BoltFlash x the veil's own luminance at the peak
                                    // of the lobe (the uniform part of the flash, 0.45 x, is already in _PlVeilCol); about +12%
                                    // on that flash's frame-mean step in a CPU model of 540, inside the flash budget
    #define PL_STORM_CURT 0.14      // the storm body's rolling ash curtains: darken / lift the far air by up to this share
    #define PL_LID_AIR 0.52         // the ash air's dissolve of the plume before the night, x _PlFog.w (21 km -> 40 km mean
                                    // free path): the far lid keeps a dark edge over the horizon and the mushroom reads
    #define PL_BOLT_LID 4.0         // broad blink of the lid around a bolt, relative to the local flash
    #define PL_STRIP_FOG 0.5        // how much of the low haze stands in front of the daylight strip
    #define PL_STRIP_HAZE 5000.0    // ...over this depth (m): the sunlit air beyond lights the haze too, so it is not a wall
    #define PL_BEAM_LEN 600.0       // reach of the lighthouse beam (m)
    #define PL_BEAM_HMIN 2.0        // lens radius (m): no singularity at the lantern
    #define PL_BEAM_CORE 0.12       // Gaussian width of the hot core, x tan of the beam half angle (_CBeam2.w, 4 deg): a line
                                    // (both lobes peak alike on a crossing ray, so only a much narrower core reads as one)
    #define PL_BEAM_SKIRT 0.60      // ... and of the soft skirt around it: a Fresnel lens has no hard edge
    #define PL_BEAM_WSKIRT 0.5      // the skirt's share of the in-scatter of a ray crossing the axis (the core has the rest)
    #define PL_BEAM_REACH 2.5       // each lobe is sampled out to this many widths (0.2% of its peak): no visible edge
    #define PL_BEAM_N 6             // samples per lobe and stretch of the view ray (stratified, jittered per frame for TAA)
    #define PL_BEAM_CAPS 2.0        // soft ceilings of the skirt and of the core, in units of _CBeam.w: a near pass saturates
    #define PL_BEAM_CAPC 8.0        //   the skirt first, so the core stays a bright line through it (664). Facing us the sum
                                    //   may reach 10 again, but only over the core's ~1 degree: the sweep flash is the skirt's
    #define PL_BEAM_FOGREF 200.0    // fog luminance (nits) at which _CBeam.w is the side-on radiance at 100 m
    #define PL_BEAM_COL float3(1.0, 0.93, 0.80)

    // Pixel footprint (m) at the current march sample: every noise octave is read at the mip that matches it, so
    // distant billows go soft instead of aliasing into grain.
    static float gFoot = 0.0;
    float PLod(float lodBase, float period) { return max(lodBase, log2(max(gFoot * 96.0 / period, 1e-3))); }

    float SMax(float a, float b, float k) { float h = max(k - abs(a - b), 0.0) / k; return max(a, b) + h * h * k * 0.25; }

    bool RayCyl(float3 ro, float3 rd, float R, float y0, float y1, out float t0, out float t1)
    {
        t0 = 0.0; t1 = -1.0;
        float a = dot(rd.xz, rd.xz);
        float b = dot(ro.xz, rd.xz);
        float c = dot(ro.xz, ro.xz) - R * R;
        float ta = -1e9, tb = 1e9;
        if (a > 1e-9)
        {
            float h = b * b - a * c;
            if (h < 0.0) return false;
            h = sqrt(h);
            ta = (-b - h) / a; tb = (-b + h) / a;
        }
        else if (c > 0.0) return false;
        if (abs(rd.y) < 1e-6) { if (ro.y < y0 || ro.y > y1) return false; }
        else
        {
            float u = (y0 - ro.y) / rd.y, v = (y1 - ro.y) / rd.y;
            ta = max(ta, min(u, v)); tb = min(tb, max(u, v));
        }
        t0 = max(ta, 0.0); t1 = tb;
        return t1 > t0;
    }

    // The scene distance a half-res texel is marched against: the farthest depth of its 2x2 block, so that silhouette
    // texels belong to the background (reversed Z: far = small). Passes 0, 2 and 3 use this same rule.
    float PlBlockSceneT(float2 pix, out float3 rd)
    {
        float2 full = pix * _PlUv.z;
        float d = min(min(LoadCameraDepth(full + float2(-0.5, -0.5)), LoadCameraDepth(full + float2(0.5, -0.5))),
                      min(LoadCameraDepth(full + float2(-0.5, 0.5)), LoadCameraDepth(full + float2(0.5, 0.5))));
        PositionInputs posInput = GetPositionInput(full, _ScreenSize.zw, d, UNITY_MATRIX_I_VP, UNITY_MATRIX_V);
        rd = normalize(posInput.positionWS);
        return d > 0.0 ? length(posInput.positionWS) : 1e9;
    }

    float ColRadius(float z) { return _PlVent.w + _PlCol.y * clamp(z, 0.0, _PlUmb.w); }

    // The column flares into the lid (it spreads out as it nears its level of neutral buoyancy) instead of meeting it in a
    // T, and is back to its own width above the lid. The billows and their noise cells use the flared radius too, so the
    // neck is as torn as the column (with the plain radius it rendered as a smooth trumpet, a disc seen from below).
    // Inside bound B from _PlUmb.y - 3500 m up (below that the flare is at most 1.1x), so bound A needs no widening.
    float NeckK(float z)
    {
        if (_PlUmb.x <= 10.0) return 1.0;
        float nk = smoothstep(_PlUmb.y - 6000.0, _PlUmb.y + 800.0, z);
        return 1.0 + PL_NECK * nk * nk * (1.0 - smoothstep(_PlUmb.z - 2000.0, _PlUmb.z + 1000.0, z)) * saturate(_PlUmb.x / 12000.0);
    }

    // Thermals: slow bulges travelling up the column make the silhouette pulse instead of being a tube.
    float ColBulge(float z)
    {
        float zeta = log(1.0 + _PlCol.y * max(z, 0.0) / _PlVent.w) / _PlCol.y;
        return 1.0 + 0.08 * sin(zeta * 2.3 - _PlMisc.w * 0.21) + 0.05 * sin(zeta * 5.1 - _PlMisc.w * 0.34 + 1.3);   // gentle: strong bulges read as beads on a string
    }

    // The jet: narrow where it leaves the vent and opening like a trumpet into the column over the first two kilometres
    // (a quick flare over 700 m made a pinched balloon sitting on the fountain).
    float JetNarrow(float z) { return lerp(_PlRoot.y, 1.0, smoothstep(-100.0, 2200.0, z)); }

    float2 WindLean(float z) { return _PlWind.xy * (z * z) * _PlWind.z; }

    // Irregular rim of the umbrella: lobes and fingers instead of a perfect disc.
    float UmbRadius(float2 xz)
    {
        float2 d = normalize(xz + 1e-3);
        float n = CNoise2(d * 0.31 + 0.5).r * 0.6 + CNoise2All(d * 0.83 + float2(0.2, _PlMisc.w * 0.0006)).g * 0.4;
        float np = CNoise2All(xz / 9000.0 + 0.3).g;                                       // position-based lobes, not only by azimuth
        return _PlUmb.x * (0.86 + 0.20 * n + 0.14 * np);
    }

    // Smooth base shape in vent space. Returns the inside distance f (m, > 0 inside), the local feature radius rl
    // and uw = 1 where the umbrella dominates. rlS: the feature radius before the lid's underside cap, for the march step.
    float BaseShape(float3 p, out float rl, out float uw, out float rlS)
    {
        float z = p.y;
        p.xz -= WindLean(z);
        float rho = length(p.xz);
        float zf = _PlCol.x;
        float jet = JetNarrow(z);
        float neck = NeckK(z);
        float rz = ColRadius(z) * ColBulge(z) * jet * neck;
        float hr = ColRadius(zf) * _PlCol.z;
        float zc = zf - hr * 0.72;
        rz *= 1.0 + 0.25 * smoothstep(zc - 2.0 * hr, zc, z);                                // the column swells into the head: no neck
        // a rounded foot that reaches below the ground at the vent (the scene depth cuts it), never a flat bottom in the air;
        // 170 m below the floor so the root's large billows (up to 0.52 x 0.82 x 380 m) do not lift it off the ground
        float fcol = min(min(rz - rho, zc - z), z + _PlRoot.x + 170.0 - 0.30 * rho);
        float f = fcol;
        if (z > zc - 1.8 * hr && zf > 0.0)
        {
            // the head: a flattened core and a stack of thermals that are born on top, roll out over the shoulder and
            // sink back into the column (positions from PlumeDirector, a pure function of the eruption clock)
            float th = -1e9;
            [unroll] for (int k = 0; k < 7; k++)
            {
                float3 dq = (p - _PlTherm[k].xyz) * float3(1.0, 1.15, 1.0);
                th = max(th, _PlTherm[k].w - length(dq));
            }
            float fhead = SMax(0.80 * hr - length(float3(p.x, (z - zc) * 1.45, p.z)), th, 0.18 * hr);
            f = SMax(fcol, fhead, 0.35 * hr);
        }
        // billows keep most of the column's scale on the jet (0.78 at the vent): a torn, turbulent root, not a smooth cone
        // with a straight-edged glow; on the axis the jet survives (209 m > 0.52 x 0.82 x 380 m); and they grow with the neck
        rl = ColRadius(z) * lerp(jet, 1.0, 0.6) * neck;
        uw = 0.0;
        rlS = rl;
        if (_PlUmb.x > 10.0)
        {
            float R = UmbRadius(p.xz);
            float yb = _PlUmb.y, yt = _PlUmb.z;
            float e = saturate(rho / R);
            float halfT = lerp((yt - yb) * 0.5, 900.0, e * e);
            float yc = lerp((yt + yb) * 0.5, yb + (yt - yb) * 0.42, e);
            float nose = 1.0 + 0.5 * exp(-pow((1.0 - e) * 6.0, 2.0));          // the advancing rim is thicker
            // mammatus: heavy pouches of ash hanging from the underside (cells a few kilometres across); they, not the
            // billow noise, shape the ceiling seen from below
            float pouch = CNoise2All(p.xz / 3400.0 + float2(0.17, 0.61)).b * 0.65 + CNoise2All(p.xz / 1500.0 + 0.3).b * 0.35;
            // (deeper pouches: with soft shadow taps the ceiling's form comes from them, not from per-sample noise)
            float hang = 1600.0 * pow(saturate(pouch * 1.3 - 0.25), 2.0) * smoothstep(0.12, 0.35, e) * (1.0 - smoothstep(0.75, 0.97, e));
            float dz = z - yc;
            float fumb = min(halfT * nose - (dz < 0.0 ? -dz - hang : dz), (R - rho) * 0.28);
            uw = saturate((fumb - f) / 900.0 + 0.5);
            f = SMax(f, fumb, 2500.0);                                          // a wide fillet: the neck blends into the lid
            rl = lerp(rl, max(halfT * 1.5, 1500.0), uw);
            rlS = rl;
            // below the lid's mid-plane the billows shrink to +/-750 m: with the full 6.7 km scale near the axis the
            // underside was a +/-2 km noise sponge whose holes showed the self-shadowed inside as black blotches. The top
            // and the rim (rl 1500 there) keep their big billows, so the mushroom's edge over the horizon is unchanged.
            rl = lerp(rl, min(rl, PL_LID_CAP), uw * saturate((yc - z) / 1500.0));
        }
        return f;
    }
    float BaseShape(float3 p, out float rl, out float uw) { float rlS; return BaseShape(p, rl, uw, rlS); }

    // Two-phase advected billow noise, -1..1. Never scrolls rigidly: each phase restarts every T seconds.
    float Pyro(float3 p, float rl, float uw, float lod)
    {
        float t = _PlMisc.w;
        p.xz -= WindLean(p.y);
        const float T = 30.0;
        float ph0 = frac(t / T), ph1 = frac(t / T + 0.5);
        float w = abs(ph0 * 2.0 - 1.0);
        float n = 0.0;
        if (uw < 0.98)
        {
            float rz = ColRadius(p.y) * NeckK(p.y);                                        // cells grow with the flare: self-similar billows
            float zeta = log(1.0 + _PlCol.y * max(p.y, 0.0) / _PlVent.w) / _PlCol.y;      // self-similar height
            float3 q = float3(p.x / rz, zeta, p.z / rz) * 0.27;
            float rr = saturate(length(p.xz) / rz);
            float v = (85.0 / rz) * (1.3 - 0.6 * rr * rr) * 0.27;                          // rise in noise units per second
            float3 d = float3(0.013 * t, 0.0, -0.009 * t);
            float per = rz / 0.27;                                                         // metres per period of the base octave
            float a = CNoise3(q - float3(0.0, v * T * (ph0 - 0.5), 0.0) + d, PLod(lod, per)).r;
            float b = CNoise3(q - float3(0.0, v * T * (ph1 - 0.5), 0.0) + d + 0.37, PLod(lod, per)).r;
            float base = lerp(a, b, w);
            float big = CNoise3(q * 0.43 - float3(0.0, v * 0.43 * T * 0.5 * (ph0 - 0.5), 0.0) + 0.19, PLod(lod, per / 0.43)).a;
            base = base * 0.72 + big * 0.28;
            float4 dt4 = CNoise3(q * 2.9 - float3(0.0, v * 2.9 * (t * 0.35), 0.0), PLod(lod, per / 2.9));
            float fine = CNoise3(q * 7.3 - float3(0.0, v * 7.3 * (t * 0.22), 0.0), PLod(lod, per / 7.3)).b;
            // the jet's edge is torn by small eddies: the first two kilometres weigh the finer octaves more (same taps)
            float rag = 1.0 - smoothstep(200.0, 2600.0, p.y);
            n = base * (0.62 - 0.22 * rag) + dt4.a * (0.27 + 0.10 * rag) + fine * (0.11 + 0.12 * rag);
        }
        if (uw > 0.02)
        {
            float rho = max(length(p.xz), 1.0);
            float2 dir = p.xz / rho;
            float3 q = float3(p.x, p.y * 1.7, p.z) / 6200.0;
            float vr = 55.0 * saturate(1.0 - rho / max(_PlUmb.x * 1.2, 1.0)) / 6200.0;
            float3 o0 = float3(dir.x, 0.0, dir.y) * vr * T * (ph0 - 0.5);
            float3 o1 = float3(dir.x, 0.0, dir.y) * vr * T * (ph1 - 0.5);
            // 1.5 mips coarser: the finest Worley octaves landed on 1-3 half-res pixels 15-25 km up (stucco speckle seen
            // from below); a 15-25 km cloud base shows broad soft lobes. Cheaper taps, too.
            float lodU = lod + 1.5;
            float a = CNoise3(q - o0 + float3(0.0, 0.004 * t, 0.0), PLod(lodU, 4800.0)).r;
            float b = CNoise3(q - o1 + 0.41, PLod(lodU, 4800.0)).r;
            float det = CNoise3(q * 3.1 + float3(0.01 * t, 0.0, 0.0), PLod(lodU, 1550.0)).a;
            float und = CNoise3(q * 0.31 + float3(0.23, 0.002 * t, 0.61), PLod(lod, 15000.0)).a;      // slow undulation of the whole lid
            float nu = lerp(a, b, w) * 0.56 + und * 0.30 + det * 0.14;
            n = lerp(n, nu, uw);
        }
        return clamp((n - 0.5) * lerp(2.3, 1.8, uw), -1.0, 1.0);                           // the lid's lobes are softer
    }

    float Field(float3 p, out float rl, out float uw, float lod)
    {
        float fb = BaseShape(p, rl, uw);
        float amp = lerp(0.52, 0.34, uw) * rl;
        if (fb < -amp) return fb;
        return fb + amp * Pyro(p, rl, uw, lod);
    }

    // Optical depth toward a light (L unit): local billows by four taps; the umbrella's own shadow is analytic.
    // On the lid (uw0, u2) the taps read 2 mips coarser and the occupancy ramps softly over 0.45 km of the uncapped scale:
    // with the 2.2 km cap the ramp was 220 m, so each jittered tap was either in or out (a binary shadow, the leopard print
    // at 300 and the stucco at 320-440). The ceiling's shading now changes over its pouches, not per noise texel.
    float SunTauDir(float3 p, float rl, float3 L, float uw0 = 0.0)
    {
        float tau = 0.0, prev = 0.0, s = rl * 0.28;
        for (int i = 0; i < 4; i++)
        {
            float r2, u2;
            float f = Field(p + L * s, r2, u2, 1.5 + 2.0 * uw0);
            tau += smoothstep(-0.3 * u2, 1.0, f / (0.10 * lerp(r2, max(r2, 4500.0), u2))) * (s - prev);
            prev = s; s *= 2.4;
        }
        return tau * _PlCol.w;
    }
    float SunTau(float3 p, float rl) { return SunTauDir(p, rl, _CSunDir.xyz); }

    float UmbrellaShadow(float3 p)
    {
        float R = _PlUmb.x;
        if (R < 10.0 || p.y > _PlUmb.y) return 0.0;
        float3 L = _CSunDir.xyz;
        float k = (lerp(_PlUmb.y, _PlUmb.z, 0.4) - p.y) / max(L.y, 0.05);
        float rho = length(p.xz + L.xz * k);
        return 1.0 - smoothstep(R * 0.72, R * 1.02, rho);
    }

    // Height fog matching the HDRP Fog override (constant colour, exponential in height).
    float FogTauH(float3 roAbs, float3 rd, float t)
    {
        float h0 = (roAbs.y - _PlFog.y) * _PlFog.z;
        float k = rd.y * _PlFog.z;
        float integral = abs(k) < 1e-5 ? t : (1.0 - exp(-k * t)) / k;
        return _PlFog.x * exp(-max(h0, -8.0)) * integral;
    }
    // the ash-laden lower atmosphere (scale height 5 km): the far rim of the lid dissolves into the horizon
    float FogTauA(float3 roAbs, float3 rd, float t)
    {
        float k1 = rd.y / 5000.0;
        float int1 = abs(k1) < 1e-6 ? t : (1.0 - exp(-k1 * t)) / k1;
        return _PlFog.w * exp(-max(roAbs.y, 0.0) / 5000.0) * int1;
    }
    float FogTau(float3 roAbs, float3 rd, float t) { return FogTauH(roAbs, rd, t) + FogTauA(roAbs, rd, t); }

    float4 March(float2 pix)
    {
        float3 rd;
        float tScene = PlBlockSceneT(pix, rd);
        float3 roAbs = _WorldSpaceCameraPos.xyz;
        float3 ro = roAbs - _PlVent.xyz;

        float zf = _PlCol.x;
        float hr = ColRadius(zf) * _PlCol.z;
        float ta0, ta1, tb0, tb1;
        float lean = _PlWind.z * zf * zf;
        // bound A holds the swollen column top, the thermals with their billows, and the root down to the scar floor
        bool hitA = RayCyl(ro, rd, max(ColRadius(_PlUmb.w), hr) * 2.0 + 200.0 + lean, -_PlRoot.x - 200.0, zf + hr, ta0, ta1);
        // bound B holds the lid and the neck that flares into it (+4.5 km: the neck of a young, still small lid)
        bool hitB = _PlUmb.x > 10.0 && RayCyl(ro, rd, _PlUmb.x * 1.25 + 4500.0, _PlUmb.y - 3500.0, _PlUmb.z + 3500.0, tb0, tb1);
        if (!hitA && !hitB) return float4(0, 0, 0, 1);
        float t0 = hitA ? (hitB ? min(ta0, tb0) : ta0) : tb0;
        float t1 = hitA ? (hitB ? max(ta1, tb1) : ta1) : tb1;
        t0 = max(t0, _PlMisc.y);
        t1 = min(t1, tScene);
        if (t1 <= t0) return float4(0, 0, 0, 1);

        float3 L = _CSunDir.xyz;
        float cosT = dot(rd, L);
        float fwd = 0.8 + 2.2 * pow(saturate(cosT), 5.0);
        fwd = lerp(fwd, min(fwd, 1.2), saturate((_CState.x - 0.3) * 5.0));             // no red outline once the sun is dimmed
        float dk = _CState.x;
        // sunlight on the cloud reddens and dies with the sun disc
        float3 redden = lerp(float3(1.0, 1.0, 1.0), _PalSunRed.rgb * 0.8, saturate(dk * 2.2 - 0.15));
        float3 sun = _CSunRaw.rgb * (1.0 / PI) * redden * lerp(1.0, 0.4, saturate(dk * 2.4)) * (1.0 - smoothstep(0.60, 0.86, dk));
        // silver lining: the rim of the umbrella stands above the ash and keeps the full sun while the disc is up
        float3 lining = _CSunRaw.rgb * (2.5 / PI) * redden * (1.0 - smoothstep(0.55, 0.85, dk)) * pow(saturate(cosT), 12.0);
        // once the sun is gone the key light is the fire below: the same shadow taps turn toward it
        float upW = smoothstep(0.55, 0.85, dk);
        float skin = lerp(1.0, PL_ROOT_SKIN, upW);
        float3 ambTop = (_CSkyZenith.rgb + _CSkyHorizon.rgb) * 0.5;
        // the column's fill by day: part of it warm-neutral at the same luminance (the shaded side sees the horizon, the sunlit
        // island and its own lit ash); under the ash the sky is already brown-slate, so this fades out with the darkness
        float3 ambCTop = lerp(ambTop, dot(ambTop, float3(0.2126, 0.7152, 0.0722)) * float3(1.06, 1.0, 0.90),
                              PL_AMB_WARM * (1.0 - smoothstep(0.4, 0.8, dk)));
        float yLid = lerp(_PlUmb.y, _PlUmb.z, 0.45);
        float jitter = CIgn(pix);
        float t = t0;
        float T = 1.0;
        float3 C = 0.0;
        float tMean = 0.0, wMean = 0.0;
        float tauSun = 0.0; int lit = 0;
        bool first = true;
        int maxIt = (int)_PlMisc.x;
        float pixAng = 2.0 * _PlUv.z / (abs(UNITY_MATRIX_P._m11) * _ScreenSize.y);
        bool jumped = false;
        for (int i = 0; i < 64; i++)
        {
            if (i >= maxIt || t >= t1 || T < 0.02) break;
            // a ray that spends its budget skirting the column still gets to the umbrella behind it
            if (!jumped && hitA && hitB && i >= maxIt - 22 && t < tb0 && T > 0.2) { t = tb0; jumped = true; }
            gFoot = t * pixAng;
            // skip the gap between the two bounds
            if (hitA && hitB)
            {
                bool inA = t >= ta0 && t <= ta1, inB = t >= tb0 && t <= tb1;
                if (!inA && !inB) { float nx = min(t < ta0 ? ta0 : 1e9, t < tb0 ? tb0 : 1e9); if (nx > 1e8) break; t = nx; }
            }
            float3 p = ro + rd * t;
            float rl, uw, rlS;
            float fb = BaseShape(p, rl, uw, rlS);
            float amp = lerp(0.52, 0.34, uw) * rl;
            // far parts of the lid in coarse steps: no march horizon (the uncapped radius: the step count is unchanged)
            float stepIn = clamp(rlS * 0.085, 30.0, max(420.0, t * 0.025));
            if (fb < -amp) { t += max(-(fb + amp) * 0.85, stepIn); continue; }
            if (first) { t -= stepIn * jitter; p = ro + rd * t; fb = BaseShape(p, rl, uw, rlS); amp = lerp(0.52, 0.34, uw) * rl; first = false; }
            float n = Pyro(p, rl, uw, 0.0);
            float f = fb + amp * n;
            if (f > 0.0)
            {
                // the lid's density and depth ramp over its uncapped scale (a soft 2 km shell): the billow amplitude keeps the
                // cap, so the ceiling stays one pouched surface, but a jittered first hit no longer lands on a crisp 660 m shell
                float rlR = lerp(rl, rlS, uw);
                float dens = saturate(f / (lerp(0.07, 0.30, uw) * rlR));
                float rimK = 1.0;
                float eU = 0.0;
                float rhoW = length(p.xz - WindLean(p.y));                                  // distance from the leaned axis
                if (uw > 0.01)
                {
                    // the lid thins out towards its rim: a veil, not a slab edge
                    eU = rhoW / max(_PlUmb.x, 1.0);
                    eU += (CNoise2All(p.xz / 14000.0).g - 0.5) * 0.35 + (CNoise2All(p.xz / 4000.0).b - 0.5) * 0.12;
                    dens *= lerp(1.0, saturate((1.12 - eU) / 0.65), uw);
                    rimK = lerp(1.0, smoothstep(1.18, 0.50, eU), uw);                          // the outer half of the lid is a graded veil
                }
                dens *= dens;
                float a = (1.0 - exp(-dens * _PlCol.w * stepIn)) * rimK;
                if (lit < 6)
                {
                    // Once dark the shadow taps look toward the fire. The lid looks at the vent. A column sample looks at the
                    // part of the fire field beneath it (fountain, scar and flows, a source kilometres across): billow
                    // undersides catch the light, the lobe above a crease shades it, and the flanks are not all in the
                    // shadow of the trumpet below (seen from the vent point alone they were, so a flat fill had to light them)
                    float aim = PL_FIRE_AIM * (1.0 - uw) * min(1.0, PL_FIRE_FIELD / max(length(p.xz), 1.0));
                    float3 toFire = float3(p.x * aim, PL_FIRE_Y, p.z * aim) - p;
                    toFire.y = min(toFire.y, -150.0);                                      // from below, also inside the root
                    toFire *= rsqrt(max(dot(toFire, toFire), 1.0));
                    float3 Lk = normalize(lerp(L, toFire, upW) + float3(0.0, 1e-4, 0.0));
                    tauSun = SunTauDir(p, rl, Lk, uw);
                    lit++;
                }
                float ush = UmbrellaShadow(p);
                float sunVis = exp(-tauSun * 1.1) * (1.0 - 0.97 * ush);
                float depth01 = saturate(f / (0.55 * rlR));
                // ash albedo: dark and warm brown at the base, paler with height; white where vapour condenses on rising tops
                float hk = saturate(p.y / 16000.0);
                float3 alb = lerp(float3(0.17, 0.13, 0.10), float3(0.46, 0.44, 0.43), pow(hk, 0.6));
                float capMask = saturate((p.y - (zf - hr * 0.95)) / (hr * 0.5)) * (1.0 - uw) * _PlMisc.z;
                capMask = max(capMask, uw * saturate((p.y - lerp(_PlUmb.y, _PlUmb.z, 0.55)) / 1500.0) * 0.5 * _PlMisc.z);
                alb = lerp(alb, float3(0.62, 0.60, 0.57), capMask * saturate(0.4 + n));
                float exposed = 1.0 - depth01;
                float crease = smoothstep(-0.55, 0.35, n);                                // 0 in the valleys between the lobes
                // the blue sky reaches little of the column's core and creases: a dark mass by day, not blue cotton
                float3 ambCol = ambCTop * lerp(1.0, 0.12, depth01) * (0.12 + 0.88 * crease) * (1.0 - 0.75 * ush);
                // (the lid's ambient follows its noise only gently: per-sample n against the opposite crease term was speckle)
                float3 ambUmb = _CSkyHorizon.rgb * (0.10 + 0.55 * exposed * exposed) * lerp(1.0, smoothstep(-0.8, 0.8, n), 0.3);
                // the sunlit air beyond the shadow's edge lights the lid from the side (_CStrip: LIGHT), from a quarter of the
                // way out: the ceiling grades from fire-lit over the cone to strip-lit toward the rim
                ambUmb += _CStrip.rgb * (0.5 * smoothstep(0.25, 1.05, eU) * saturate((yLid - p.y) / 1500.0 + 0.5));
                float3 amb = lerp(ambCol, ambUmb, uw);
                float3 rad = alb * (sun * sunVis * lerp(min(fwd, 1.6), 0.22, uw) * (0.3 + 0.7 * dens) * (0.55 + 0.45 * crease) + amb);
                // The fire below (night). One lighting model for the column: no emission fill, no flat lamp-shade. Its key
                // and the lid's under-light share the shadow taps and the form term, so lobes lit from below turn orange
                // and their tops and the creases between them stay charcoal (crease^2: the lobe-to-crease contrast of a
                // billow lit from below; the linear term left the lower column one even orange gradient).
                float colForm = 0.12 + 0.88 * crease * crease;
                float fireSh = exp(-lerp(PL_FIRE_SH, 0.6, uw) * tauSun);
                float3 fv = p - float3(0.0, PL_FIRE_Y, 0.0);
                float upE = PL_KEY_GAIN / (1.0 + dot(fv, fv) / (PL_KEY_R * PL_KEY_R));
                rad += alb * _PlUp.rgb * (upE * fireSh * upW * colForm * (1.0 - uw));
                // the incandescent root (scale height _PlGlow.a: 0.5 km by day, 0.7 km once dark): a hot core seen through
                // creases and torn pockets (PL_ROOT_POCKET), dark ash on the billows, a cooler ash skin once dark, and nothing
                // below the ground at the vent (the scar floor). Above it the column is fire-lit, not glowing: no candle flame.
                float gate = (1.0 - uw) * smoothstep(-_PlRoot.x - 30.0, -_PlRoot.x + 30.0, p.y);
                float rC = rhoW / max(ColRadius(p.y) * JetNarrow(p.y), 1.0);
                float glow = exp(-max(p.y, 0.0) / _PlGlow.a) * gate * lerp(skin, 1.0, saturate(1.25 - rC));
                rad += _PlGlow.rgb * glow * (0.12 + PL_ROOT_POCKET * smoothstep(-0.05, 0.55, -n)) * lerp(0.6, 1.0, depth01);
                // the lid's under-light: the ceiling, the flared neck that feeds it (its underside faces the fire like the
                // lid's), and the trunk close under the glowing ceiling (no black drain under a lit ceiling at 300); the
                // column further down keeps only PL_LID_FLOOR of it, shaped like the key
                // nearK: how lid-like a sample is lit (the lid, the flare, the trunk close under the ceiling); below that band
                // nearK = 0 and the column keeps exactly its key-shaped share (form and fire shadow), so the night column
                // (its visible part is under 5 km) is unchanged by this
                float nearK = max(smoothstep(yLid - PL_LID_NEAR, yLid - 1000.0, p.y), saturate((NeckK(p.y) - 1.0) / (0.6 * PL_NECK)));
                float lidK = max(uw, lerp(PL_LID_FLOOR, 1.0, nearK));
                nearK = max(uw, nearK);
                float below = saturate((yLid - p.y) / 2500.0 + 0.3);
                float rr = rhoW / max(_PlUnder.w, 1.0);
                // (lid-like parts take a constant form term: their shape comes from the soft shadow taps and the pouches; the
                // per-sample noise term, against the opposite ambient term, was the leopard print and the stucco)
                float formU = lerp(colForm, 0.85, nearK);
                float fireU = lerp(fireSh, max(fireSh, PL_NECK_FIRE), nearK);
                rad += alb * _PlUnder.rgb * (lidK * below / (1.0 + rr * rr) * formU * lerp(1.0, fireU, upW));
                rad += alb * lining * ((1.0 - depth01) * smoothstep(0.75, 1.1, eU) * uw * exp(-0.5 * tauSun));
                // lightning inside the cloud: a local flash, and a broad blink of the lid above it
                float3 pa = p + _PlVent.xyz;
                float3 d0 = pa - _PlBolt0.xyz, d1 = pa - _PlBolt1.xyz;
                float q0 = dot(d0, d0), q1 = dot(d1, d1);
                rad += alb * float3(0.75, 0.82, 1.0) * (_PlBolt0.w * (1.0 / (q0 + 2.0e6) + uw * PL_BOLT_LID / (q0 + 6.4e7))
                                                      + _PlBolt1.w / (q1 + 2.0e6));
                C += T * a * rad;
                tMean += T * a * t; wMean += T * a;
                T *= 1.0 - a;
            }
            t += f > 0.0 ? stepIn : clamp(-f * 0.6, stepIn, stepIn * 4.0);
        }
        // The pressure wave: a thin shell of condensation racing outward at the speed of sound. Seen from outside it
        // is a faint ring in the humid layer (limb brightening); it passes overhead in silence, then the thump arrives.
        if (_PlShock.y > 0.001)
        {
            float sb = dot(ro, rd), sc = dot(ro, ro) - _PlShock.x * _PlShock.x;
            float sh = sb * sb - sc;
            if (sh > 0.0)
            {
                sh = sqrt(sh);
                float3 ringCol = (_CSunRaw.rgb * (0.25 / PI) + ambTop) * 0.9;
                float tmS = wMean > 1e-4 ? tMean / wMean : 1e9;
                for (int k = 0; k < 2; k++)
                {
                    float tH = k == 0 ? -sb - sh : -sb + sh;
                    if (tH < 400.0 || tH > tScene) continue;
                    float3 ph = ro + rd * tH;
                    float alt = ph.y + _PlVent.y;
                    float layer = smoothstep(2100.0, 3000.0, alt) * (1.0 - smoothstep(3200.0, 4600.0, alt));
                    float limb = 1.0 / (abs(dot(normalize(ph), rd)) + 0.28);
                    float a = min(_PlShock.y * 0.004 * limb * layer, 0.025) * smoothstep(0.35, 0.65, CNoise2(ph.xz / 2500.0));
                    if (tH < tmS) { C = C * (1.0 - a) + ringCol * a; }
                    else { C += T * a * ringCol; }
                    T *= 1.0 - a;
                }
            }
        }
        float alpha = 1.0 - T;
        if (alpha < 0.002) return float4(0, 0, 0, 1);
        // aerial perspective on the plume itself: the HDRP-matching haze greys it toward the fog colour; the ash-laden
        // air in front of the far lid dissolves it into whatever lies behind (sky, daylight strip), not a darker constant
        float tm = tMean / max(wMean, 1e-5);
        float ftH = exp(-FogTauH(roAbs, rd, tm));
        C = C * ftH + _CFogColor.rgb * (1.0 - ftH) * alpha;
        // Before the night the ash air is thinner for this (PL_LID_AIR): the far lid keeps a readable dark edge over the
        // horizon. At night the full dissolve returns, so the lid never comes back as a hard ceiling (570-664 s).
        float ftA = exp(-FogTauA(roAbs, rd, tm) * lerp(PL_LID_AIR, 1.0, _CState.z));
        C *= ftA;
        T = 1.0 - alpha * ftA;
        return float4(C * GetCurrentExposureMultiplier(), T);
    }

    float4 FragMarch(Varyings varyings) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(varyings);
        return March(varyings.positionCS.xy);
    }

    // pass 3: the scene distance of each half-res texel, for the joint-bilateral upsample in the composite
    float FragSceneT(Varyings varyings) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(varyings);
        float3 rd;
        return min(PlBlockSceneT(varyings.positionCS.xy, rd), PL_TMAX);
    }

    // the low volumes (pass 2): collapse dust, splash, crest spray, cliff strike
    #include "Assets/_Game/Shaders/PlumeFx.hlsl"

    // The sun: disc and glow, dimmed and reddened by the ash in front of it. Sky pixels only.
    float3 SunLayer(float3 rd, float T)
    {
        float3 L = _CSunDir.xyz;
        float cs = saturate(dot(rd, L));
        float dark = _CState.x;
        float disc = smoothstep(0.999958, 0.999975, cs);                     // about 0.47 degrees across
        float3 tint = pow(max(T, 1e-4), float3(0.35, 0.9, 1.9));             // ash takes the blue first
        tint = lerp(tint, _PalSunRed.rgb * 0.55, saturate(dark * 2.2 - 0.15));
        float vanish = 1.0 - smoothstep(0.52, 0.74, dark);
        float3 sunCol = _CSunRaw.rgb / max(max(_CSunRaw.r, _CSunRaw.g), _CSunRaw.b);
        float3 c = sunCol * tint * disc * (1.6e6 * lerp(1.0, 0.02, saturate(dark * 2.4))) * vanish;
        // the aureole of clean air falls as a power of the angle (forward scattering by aerosols), with no shoulder: the
        // Gaussian 2500 cs^120 held ~1300 nits flat to 6 deg and then dropped 7x by 12 deg, a frosted disc. th2 ~ angle^2.
        float th2 = 2.0 * (1.0 - cs);
        float halo = 26000.0 / (1.0 + th2 / 1.0e-4) + 900.0 / (1.0 + th2 / 2.5e-3) + 300.0 * pow(cs, 14.0);
        c += sunCol * tint * halo * (1.0 - dark) * (1.0 - dark);
        return c * T;
    }

    // The daylight strip (sky pixels): beyond the edge of the umbrella's shadow the air is sunlit; seen from inside the
    // shadow it is a bright seam on the horizon that narrows as the edge recedes (about atan(H/D)). _CStrip (LIGHT): zero = off.
    float3 PlStripLayer(float3 rd, float T)
    {
        if (_CStrip.w <= 0.0 || _CStrip2.z < 1.0 || dot(_CStrip.rgb, float3(1.0, 1.0, 1.0)) <= 0.0) return 0.0;
        float3 cam = _WorldSpaceCameraPos.xyz;
        float2 o = cam.xz - _CStrip2.xy;
        float2 dir = rd.xz * rsqrt(max(dot(rd.xz, rd.xz), 1e-8));
        float b = dot(o, dir), oo = dot(o, o);
        float R = _CStrip2.z;
        float c = oo - R * R;
        if (c >= 0.0) return 0.0;                                            // outside the shadow: nothing to see from here
        float D = -b + sqrt(b * b - c);
        if (_PlUmb.x > 10.0)
        {
            // the rim of the umbrella, carried along the sun onto the ground (the footprint is a translated copy): only its
            // broad lobes (30 deg and up). The finer terms of UmbRadius changed by a noise texel per degree of azimuth (and
            // the positional one aliased 60 km out), so the strip's height jumped from column to column: a vertical comb.
            float2 dd = normalize(o + dir * D + 1e-3);
            R *= 0.93 + 0.14 * CNoise2(dd * 0.31 + 0.5);
            c = oo - R * R;
            if (c >= 0.0) return 0.0;
            D = -b + sqrt(b * b - c);
        }
        // (EnvDirector already fades _CStrip in as the shadow edge passes over the camera; this only softens the ragged edge)
        float k = exp(-max(rd.y, 0.0) * D * _CStrip.w) * smoothstep(-0.004, 0.006, rd.y) * saturate(-c / (0.05 * R * R));
        return _CStrip.rgb * (k * T * exp(-PL_STRIP_FOG * FogTauH(cam, rd, PL_STRIP_HAZE)));
    }

    // How fast the scene depth changes across the screen here, relative, from the smoother side of each axis: grazing
    // surfaces (the sea toward the horizon) get a wider upsample tolerance, silhouettes do not.
    float PlDepthSlope(float2 pos, float zc)
    {
        float2 lo = max(pos - 1.0, 0.5);
        float zl = LinearEyeDepth(LoadCameraDepth(float2(lo.x, pos.y)), _ZBufferParams);
        float zr = LinearEyeDepth(LoadCameraDepth(pos + float2(1.0, 0.0)), _ZBufferParams);
        float zd = LinearEyeDepth(LoadCameraDepth(float2(pos.x, lo.y)), _ZBufferParams);
        float zu = LinearEyeDepth(LoadCameraDepth(pos + float2(0.0, 1.0)), _ZBufferParams);
        return (min(abs(zr - zc), abs(zc - zl)) + min(abs(zu - zc), abs(zc - zd))) / max(zc, 1e-3);
    }

    // Joint-bilateral upsample of the half-res plume and fx buffers. Each half-res texel was marched against the farthest
    // depth of its 2x2 block; a full-res pixel only takes the texels marched against (about) its own depth, so neither
    // volume bleeds over a nearer silhouette (the scar walls, the cliffs, the wave crest).
    void PlUpsample(float2 pos, float2 uv, float tPix, float depth, bool needPl, bool needFx, out float4 pl, out float4 fx)
    {
        pl = float4(0, 0, 0, 1); fx = float4(0, 0, 0, 1);
        float2 hc = pos / _PlUv.z - 0.5;
        float2 bf = floor(hc);
        float2 f = hc - bf;
        int2 hiC = max(int2(_ScreenSize.xy / _PlUv.z) - 1, int2(0, 0));
        int2 c0 = clamp(int2(bf), int2(0, 0), hiC);
        int2 c1 = clamp(int2(bf) + 1, int2(0, 0), hiC);
        int2 c10 = int2(c1.x, c0.y), c01 = int2(c0.x, c1.y);
        float4 tt = float4(LOAD_TEXTURE2D_X(_PlDepthTex, c0).r, LOAD_TEXTURE2D_X(_PlDepthTex, c10).r,
                           LOAD_TEXTURE2D_X(_PlDepthTex, c01).r, LOAD_TEXTURE2D_X(_PlDepthTex, c1).r);
        float tp = clamp(tPix, 1.0, PL_TMAX);
        float4 e = abs(tt - tp);
        if (all(e < 0.5 * PL_BILAT_TOL * tp))
        {
            // coherent depth (almost everywhere): plain bilinear
            float2 uvS = uv * _PlUv.xy;
            if (needPl) pl = SAMPLE_TEXTURE2D_X_LOD(_PlumeTex, s_linear_clamp_sampler, uvS, 0);
            if (needFx) fx = SAMPLE_TEXTURE2D_X_LOD(_FxTex, s_linear_clamp_sampler, uvS, 0);
            return;
        }
        float tol = PL_BILAT_TOL;
        if (depth > 0.0) tol += 2.5 * PlDepthSlope(pos, LinearEyeDepth(depth, _ZBufferParams));
        float4 w = float4((1.0 - f.x) * (1.0 - f.y), f.x * (1.0 - f.y), (1.0 - f.x) * f.y, f.x * f.y) * saturate(1.0 - e / (tol * tp));
        float ws = dot(w, float4(1.0, 1.0, 1.0, 1.0));
        if (ws < 1e-3)
        {
            // no texel was marched against this depth: take the one that comes closest
            float m = min(min(e.x, e.y), min(e.z, e.w));
            w = step(e, m.xxxx);
            ws = dot(w, float4(1.0, 1.0, 1.0, 1.0));
        }
        w /= ws;
        if (needPl)
            pl = w.x * LOAD_TEXTURE2D_X(_PlumeTex, c0) + w.y * LOAD_TEXTURE2D_X(_PlumeTex, c10)
               + w.z * LOAD_TEXTURE2D_X(_PlumeTex, c01) + w.w * LOAD_TEXTURE2D_X(_PlumeTex, c1);
        if (needFx)
            fx = w.x * LOAD_TEXTURE2D_X(_FxTex, c0) + w.y * LOAD_TEXTURE2D_X(_FxTex, c10)
               + w.z * LOAD_TEXTURE2D_X(_FxTex, c01) + w.w * LOAD_TEXTURE2D_X(_FxTex, c1);
    }

    // Henyey-Greenstein (g = 0.5) relative to side-on scattering: 1 across the beam, 11 looking down it into the lantern.
    float PlBeamPhase(float cs) { return 1.3975 / pow(max(1.25 - cs, 1e-3), 1.5); }

    // Optical depth factor of a fog term along the view ray up to t: (1 - exp(-k t)) / k, safe for small k t.
    float PlFogInt(float k, float t) { float kt = k * t; return abs(kt) < 1e-3 ? t * (1.0 - 0.5 * kt) : (1.0 - exp(-kt)) / k; }

    // In-scatter of one Gaussian lobe of the beam, exp(-tan^2(angle off the axis) / g^2), both opposite beams, along the
    // view ray. PL_BEAM_N samples per stretch inside the cone where the lobe is above 0.2% of its peak, spaced uniformly in
    // the angle the stretch subtends at the lantern (importance sampling of 1/r^2: the closed-form integral times the mean
    // of the rest), stratified and jittered per pixel and frame so TAA resolves them. Normalised to 1 for a ray crossing
    // the axis side-on 100 m from the lantern. w = camera - lantern; fog = (height fog f, k, ash air f, k) of the view ray.
    // tB returns the distance of the stretch nearest the lantern.
    float PlBeamLobe(float3 w, float3 rd, float tScene, float g, float sigL, float4 fog, float jit, inout float tB)
    {
        float3 ax = _CBeam2.xyz;
        float tR = PL_BEAM_REACH * g;
        float c2 = 1.0 / (1.0 + tR * tR);                                   // cos^2 of the sampled cone's half angle
        float A = dot(rd, ax), B = dot(w, ax);
        float wd = dot(w, rd), ww = dot(w, w);
        float qa = A * A - c2, qb = A * B - c2 * wd, qc = B * B - c2 * ww;
        qa = abs(qa) < 1e-9 ? -1e-9 : qa;
        // the reach of the beam along its axis, the camera and the scene
        float lo = 0.0, hi = tScene;
        if (abs(A) > 1e-5)
        {
            float u0 = (-PL_BEAM_LEN - B) / A, u1 = (PL_BEAM_LEN - B) / A;
            lo = max(lo, min(u0, u1)); hi = min(hi, max(u0, u1));
        }
        else if (abs(B) > PL_BEAM_LEN) hi = -1.0;
        if (hi <= lo) return 0.0;
        // inside the double cone: between the roots when the ray crosses the beam, outside them when it runs along it
        float disc = qb * qb - qa * qc;
        float2 i0 = float2(lo, hi), i1 = float2(1.0, 0.0);
        if (disc > 0.0)
        {
            float sq = sqrt(disc);
            float r1 = (-qb - sq) / qa, r2 = (-qb + sq) / qa;
            float rlo = min(r1, r2), rhi = max(r1, r2);
            if (qa < 0.0) i0 = float2(max(lo, rlo), min(hi, rhi));
            else { i0 = float2(lo, min(hi, rlo)); i1 = float2(max(lo, rhi), hi); }
        }
        else if (qa < 0.0) return 0.0;
        float bC = -wd;                                                      // closest approach to the lantern along the ray
        float h = max(sqrt(max(ww - wd * wd, 0.0)), PL_BEAM_HMIN);
        float ig2 = 1.0 / (g * g);
        float sum = 0.0;
        [unroll] for (int k = 0; k < 2; k++)
        {
            float2 iv = k == 0 ? i0 : i1;
            if (iv.y <= iv.x) continue;
            float p0 = atan((iv.x - bC) / h), p1 = atan((iv.y - bC) / h);
            float acc = 0.0;
            [unroll] for (int j = 0; j < PL_BEAM_N; j++)
            {
                float tt = bC + h * tan(lerp(p0, p1, (j + jit) * (1.0 / PL_BEAM_N)));
                float3 q = w + rd * tt;                                      // the sample, seen from the lantern
                float r2 = max(dot(q, q), 1e-2), qx = dot(q, ax);
                float ri = rsqrt(r2), r = r2 * ri;
                float tan2 = max(r2 - qx * qx, 0.0) / max(qx * qx, 1e-4);
                // the lobe, the ash between the lantern and the sample and between the sample and the eye, the phase of
                // light leaving the lantern radially (1 side-on), and the end of the beam's reach
                acc += exp(-tan2 * ig2 - sigL * r - fog.x * PlFogInt(fog.y, tt) - fog.z * PlFogInt(fog.w, tt))
                     * PlBeamPhase(-dot(q, rd) * ri) * (1.0 - smoothstep(0.55 * PL_BEAM_LEN, PL_BEAM_LEN, r));
            }
            sum += (p1 - p0) / h * acc;
            tB = min(tB, clamp(bC, iv.x, iv.y));
        }
        // side-on at distance d the lobe integrates to sqrt(pi) g / d over the angle
        return sum * (100.0 / (1.7724539 * PL_BEAM_N)) / g;
    }

    // The lighthouse beam as light in the ash (M5): a smooth Gaussian profile, a hot core inside a soft skirt (no stepped
    // cones, no hard edge), brightest near the lantern and when it sweeps toward the camera, clipped by the scene,
    // coloured like lit ash. tB: its distance, for the storm veil. Costs nothing while the beam is off.
    float3 PlBeamLayer(float3 rd, float tScene, float2 pos, out float tB)
    {
        tB = 1e9;
        if (_CBeam2.w < 0.5) return 0.0;
        float3 cam = _WorldSpaceCameraPos.xyz;
        float3 w = cam - _CBeam.xyz;
        // extinction of the ash at the lantern: the beam dies within metres in the storm, reaches far in clear night air
        float sigL = _PlFog.x * exp(-max((_CBeam.y - _PlFog.y) * _PlFog.z, -8.0)) + _PlFog.w * exp(-max(_CBeam.y, 0.0) / 5000.0);
        // FogTau of the view ray (height fog + ash air), split so each sample costs one exp per term
        float4 fog = float4(_PlFog.x * exp(-max((cam.y - _PlFog.y) * _PlFog.z, -8.0)), rd.y * _PlFog.z,
                            _PlFog.w * exp(-max(cam.y, 0.0) / 5000.0), rd.y / 5000.0);
        float tanH = sqrt(max(1.0 - _CBeam2.w * _CBeam2.w, 1e-8)) / _CBeam2.w;
        float jit = CIgn(pos);
        float sS = PL_BEAM_WSKIRT * PlBeamLobe(w, rd, tScene, PL_BEAM_SKIRT * tanH, sigL, fog, jit, tB);
        // (the core's cone lies inside the skirt's: a ray that misses the skirt skips it)
        float sC = sS > 0.0 ? (1.0 - PL_BEAM_WSKIRT) * PlBeamLobe(w, rd, tScene, PL_BEAM_CORE * tanH, sigL, fog, frac(jit + 0.618), tB) : 0.0;
        // each lobe has its own soft ceiling: when the beam passes close, the skirt saturates first and the core stays a
        // brighter line inside it (one ceiling on the sum flattened the near pass into a blown slab)
        float s = sS / (1.0 + sS / PL_BEAM_CAPS) + sC / (1.0 + sC / PL_BEAM_CAPC);
        float fogL = dot(_CFogColor.rgb, float3(0.2126, 0.7152, 0.0722));
        float3 tint = lerp(float3(1.0, 1.0, 1.0), _CFogColor.rgb / max(fogL, 1e-3), 0.35);
        return PL_BEAM_COL * tint * (_CBeam.w * s * clamp(fogL / PL_BEAM_FOGREF, 0.3, 6.0));
    }

    float4 FragComposite(Varyings varyings) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(varyings);
        float2 pos = varyings.positionCS.xy;
        float depth = LoadCameraDepth(pos);
        bool nearPix = depth > 0.0 && LinearEyeDepth(depth, _ZBufferParams) < _PlMisc.y;
        bool beamOn = _CBeam.w > 0.0;
        if (nearPix && _PlVeil.w < 0.002 && _FxMisc.x < 0.5 && !beamOn && _CState.y <= 0.02) return float4(0, 0, 0, 1);
        PositionInputs posInput = GetPositionInput(pos, _ScreenSize.zw, depth, UNITY_MATRIX_I_VP, UNITY_MATRIX_V);
        float3 rd = normalize(posInput.positionWS);
        float sceneT = depth > 0.0 ? length(posInput.positionWS) : 1e9;
        float2 uv = pos * _ScreenSize.zw;
        float4 pl = float4(0, 0, 0, 1);
        bool wantFx = false;
        if (_FxMisc.x > 0.5) wantFx = sceneT > FxStart(_WorldSpaceCameraPos.xyz, rd);
        if (!nearPix || wantFx)
        {
            float4 plH, fxH;
            PlUpsample(pos, uv, sceneT, depth, !nearPix, wantFx, plH, fxH);
            if (!nearPix)
            {
                pl = plH;
                // inside the ash storm (mean free path ~26 m) the plume is gone; it only shows where a clearing lowers
                // the storm density (_CState.y). Else its flat-fog aerial perspective leaves a pale ghost on HDRP's lit fog
                float hide = saturate(_CState.y * 1.15);
                pl = lerp(pl, float4(0, 0, 0, 1), hide);
                if (depth == 0.0) pl.rgb += (SunLayer(rd, pl.a) + PlStripLayer(rd, pl.a)) * GetCurrentExposureMultiplier();
            }
            if (wantFx) pl = float4(fxH.rgb + fxH.a * pl.rgb, fxH.a * pl.a);
        }
        float veilM = 0.0;
        if (_PlVeil.w > 0.002)
        {
            // The ash storm as a veil with one ragged clearing in it (fixed in the world, drifting edge).
            float3 ax = _PlVeil.xyz;
            float3 rt = normalize(cross(float3(0.0, 1.0, 0.0), ax));
            float3 up = cross(ax, rt);
            // a chimney, not a porthole: the clearing reaches 1/PL_VEIL_UP as far above its axis as below, so the burning
            // column rises out of it into the storm (it showed the summit under an empty slate sky)
            float vy = dot(rd, up);
            float2 e = float2(dot(rd, rt) / 2.6, vy > 0.0 ? vy * PL_VEIL_UP : vy);
            float th = dot(rd, ax) > 0.0 ? length(e) : 4.0;
            float n = CNoise2(e * 1.9 + float2(_PlVeil2.w * 0.013, -_PlVeil2.w * 0.007)) * 0.65
                    + CNoise2(e * 6.1 - float2(_PlVeil2.w * 0.021, _PlVeil2.w * 0.004)) * 0.35;
            n += CNoise2(e * 17.0 + 0.3 * CNoise2All(e * 4.0).gb) * 0.25;
            float edge = _PlVeil2.x * (0.45 + 1.0 * n);
            // a window with a ragged wall: clear inside 0.75 edge, closed by 1.5 edge (0.2..1.8 left the whole opening a
            // half-veiled haze, a flat 64-74 profile through the 540 clearing)
            float veil = smoothstep(edge * 0.75, edge * 1.5 + 0.004, th);
            veilM = _PlVeil.w * veil;
            float dist = depth > 0.0 ? length(posInput.positionWS) : 1e6;
            float a = veilM * (1.0 - exp(-max(dist - 5.0, 0.0) / _PlVeil2.y));
            // The inner wall of the clearing: a band just outside the opening (to 2.4 edge; the old rim reached 4.5 edge, about
            // the whole frame, so the entire veil was lit), slate brightened and warmed by the fire it frames, most on the
            // sides and below, toward the burning flank. Fire-lit ash is amber: lava red on the slate read lilac in the cool
            // storm grade. Outside the wall the veil is the storm's own dark slate (PlumePass dims _PlVeilCol to its level).
            float wall = veil * (1.0 - smoothstep(edge * 1.5, edge * 2.4 + 0.01, th));
            float fireRim = wall * (1.0 - 0.6 * saturate(e.y / (2.0 * edge + 1e-3)));
            float3 fireC = _PalWindowAmber.rgb * (dot(_PlUnder.rgb, float3(0.2126, 0.7152, 0.0722)) / 0.6);
            float3 vcol = _PlVeilCol.rgb * (1.0 + PL_VEIL_RIM * wall) + fireC * (PL_VEIL_FIRE * fireRim);
            // A bolt lights the ash around its channel: a sheet flash of the veil, strongest toward the bolt (half at ~17 deg).
            // _PlVeil2.z is EnvDirector.BoltFlash, the same pulse, strength and exposure compensation as every other flash, so
            // it stays inside the flash budget; the uniform part of the flash already rides in _PlVeilCol.
            if (_PlVeil2.z > 0.0 && _PlBolt0.w > 0.0)
            {
                float3 bv = _PlBolt0.xyz - _WorldSpaceCameraPos.xyz;
                float lobe = pow(saturate(dot(rd, bv * rsqrt(max(dot(bv, bv), 1.0)))), 16.0);
                vcol += float3(0.80, 0.86, 1.0) * (dot(_PlVeilCol.rgb, float3(0.2126, 0.7152, 0.0722)) * PL_VEIL_BOLT * _PlVeil2.z * lobe);
            }
            pl.rgb = pl.rgb * (1.0 - a) + vcol * (GetCurrentExposureMultiplier() * a);
            pl.a *= 1.0 - a;
        }
        // The storm body: rolling ash curtains that drift with the plume's wind, and a warm bleed on the side of the burning
        // flank, so 504-600 reads as weather and not one flat slate (HDRP's storm fog is a single colour beyond ~50 m).
        // Fixed on the sky sphere (no swimming as the camera turns), far pixels only (fades in beyond 6 m), mean about
        // unchanged: masses in their own shadow darken, masses turned to the light lift. No flash: it drifts slowly.
        if (_CState.y > 0.02)
        {
            float sk = _CState.y * (1.0 - exp(-max(sceneT - 6.0, 0.0) / 40.0));
            float3 cq = rd * 0.7 + float3(0.94, 0.05, 0.34) * (_CTime * 0.006);
            float cn = CNoise3(cq, 0.0).a * 0.65 + CNoise3(cq * 2.6 + 0.37, 0.0).r * 0.35;
            float cs = clamp((cn - 0.48) * 2.5, -1.0, 1.0);                                   // -1 shadowed mass .. +1 lit mass
            float fk = pow(saturate(0.5 + 0.5 * dot(rd, normalize(_CVent.xyz - _WorldSpaceCameraPos.xyz))), 5.0);
            float cdk = PL_STORM_CURT * sk * saturate(-cs);
            float3 cadd = _PlVeilCol.rgb * (2.5 * PL_STORM_CURT * sk * saturate(cs))          // (_PlVeilCol = 0.4 x storm fog)
                        + _PalWindowAmber.rgb * (dot(_PlUnder.rgb, float3(0.2126, 0.7152, 0.0722)) * 0.04 * sk * fk * (0.5 + cn));
            pl.rgb = pl.rgb * (1.0 - cdk) + cadd * GetCurrentExposureMultiplier();
            pl.a *= 1.0 - cdk;
        }
        if (beamOn)
        {
            // the beam is ash lit in front of everything this pass draws; the storm veil hides it by its own distance
            float tB;
            float3 bm = PlBeamLayer(rd, sceneT, pos, tB);
            bm *= 1.0 - veilM * (1.0 - exp(-max(tB - 5.0, 0.0) / max(_PlVeil2.y, 1.0)));
            pl.rgb += bm * GetCurrentExposureMultiplier();
        }
        return pl;
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
            #pragma fragment FragMarch
            ENDHLSL
        }
        Pass
        {
            Name "Composite"
            ZWrite Off ZTest Always Blend One SrcAlpha Cull Off
            HLSLPROGRAM
            #pragma fragment FragComposite
            ENDHLSL
        }
        Pass
        {
            Name "MarchFx"
            ZWrite Off ZTest Always Blend Off Cull Off
            HLSLPROGRAM
            #pragma fragment FragMarchFx
            ENDHLSL
        }
        Pass
        {
            Name "SceneT"
            ZWrite Off ZTest Always Blend Off Cull Off
            HLSLPROGRAM
            #pragma fragment FragSceneT
            ENDHLSL
        }
    }
    Fallback Off
}
