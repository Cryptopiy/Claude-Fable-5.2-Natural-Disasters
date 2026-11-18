Shader "Hidden/MESOCYCLONE/Windscreen"
{
    // Fullscreen Custom Pass (BeforePostProcess): water, wiper clearing and hail cracks on the cab glass.
    // The glass panes are analytic quads in cab space (CabDims): ray/plane intersection gives glass coordinates in
    // metres, the camera depth decides whether an interior object is in front. Everything is a pure function of
    // (glass position, time, uniforms): no feedback buffers, deterministic, restart-safe.
    // The pass runs before TAA. Pass 1 'GlassMV' also writes the glass motion into the camera motion-vector buffer under
    // drops, runners and cracks, so TAA keeps them on the glass instead of dragging them along the world's 24 m/s motion.
    // Pass 0 'Glass' is the colour-only fallback when the frame has no motion-vector buffer.
    HLSLINCLUDE
    #pragma vertex Vert
    #pragma target 4.5
    #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch switch2

    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/RenderPass/CustomPass/CustomPassCommon.hlsl"

    #define WS_PANES 4
    TEXTURE2D_X(_WsSource);
    float4x4 _WsCabInv;            // local frame (truck at origin) -> cab space
    float4 _WsO[WS_PANES];         // pane origin (cab space)
    float4 _WsU[WS_PANES];         // unit axis u, w = u min
    float4 _WsV[WS_PANES];         // unit axis v, w = u max
    float4 _WsB[WS_PANES];         // x = v max, y = slant of the u limits per metre of v (windscreen narrows upward), z = pane id, w = front-edge slant (side glass)
    float4 _WsState;               // x = rain 0..1, y = truck speed m/s, z = time s, w = wetness 0..1
    float4 _WsWiper;               // x = seconds since the sweep started, y = sweep duration, z = cycle period, w = 1 if wipers run
    float4 _WsWiperGeo;            // x = pivot L u, y = pivot R u, z = pivot v, w = seconds since last wiped when off
    float4 _WsLight;               // rgb = flash colour * level, a = headlight level
    float4 _WsLightDir;            // xy = direction of the flash in windscreen coordinates, z = dash glow 0..1, w = hail 0..1
    float4 _WsCrack[3];            // xy = impact (glass metres), z = growth 0..1, w = seed
    float4 _WsCrackP[3];           // x = radial count, y = max length (m), z = ring count, w = bullseye radius (m)
    float4 _WsClear;               // x = clear air 0..1 (the tear: the rain haze is gone, so the glass mist thins too)
    float4x4 _WsCabCur;            // cab space -> world, this frame (glass motion vectors)
    float4x4 _WsCabPrev;           // cab space -> world, last rendered frame (equal to _WsCabCur after a skipped frame)

    float3 WsHash3(float2 p)
    {
        float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
        p3 += dot(p3, p3.yxz + 33.33);
        return frac((p3.xxy + p3.yzz) * p3.zyx);
    }
    float WsHash1(float n) { return frac(sin(n * 127.1 + 3.7) * 43758.5453); }
    float WsNoise1(float x) { float i = floor(x), f = frac(x); f = f * f * (3.0 - 2.0 * f); return lerp(WsHash1(i), WsHash1(i + 1.0), f); }
    float WsLin1(float x) { float i = floor(x), f = frac(x); return lerp(WsHash1(i), WsHash1(i + 1.0), f); }   // piecewise linear: kinks, not waves

    // beads: one spherical cap per hashed cell, born, growing, knocked off. Returns coverage; disp = lens displacement (m).
    // near = 1 on the bead grown by 'grow' (cell units): HDRP TAA takes the motion vector of the closest-depth diagonal
    // neighbour, so the glass motion has to reach a little past the drop's edge or the rim still smears
    float WsBeads(float2 p, float cell, float density, float t, float seed, float grow, out float2 disp, out float near)
    {
        float2 g = p / cell;
        float2 id = floor(g);
        float2 f = frac(g) - 0.5;
        float3 h = WsHash3(id + seed);
        float3 h2 = WsHash3(id * 1.7 + seed + 31.0);
        float life = frac(t * (0.04 + 0.09 * h.z) + h.x * 7.0);
        float r = (0.07 + 0.2 * h2.x * h2.x) * smoothstep(0.0, 0.2, life) * (1.0 - smoothstep(0.92, 1.0, life));
        float2 d = f - (h.xy - 0.5) * 0.56;
        float q = dot(d, d) / (r * r + 1e-6);
        // clustered, never a regular lattice: a slow patchiness thins whole neighbourhoods
        float patch = WsNoise1(id.x * 0.31 + seed) * WsNoise1(id.y * 0.27 + seed * 1.3 + 7.0);
        float live = step(h2.y, density * (0.25 + 1.5 * patch));
        float rn = r + grow;
        near = live * step(1e-3, r) * step(dot(d, d), rn * rn);
        float on = live * step(q, 1.0);
        disp = -d * cell * 1.7 * on;
        return on * sqrt(saturate(1.0 - q));
    }

    // runners: one drop per column segment sliding along the flow with a thin trail behind it.
    // near = 1 on the head and the trail grown by 'grow' (metres in p), for the glass motion vectors
    float WsRunner(float2 p, float t, float flow, float density, float seed, float grow, out float2 disp, out float trail, out float near)
    {
        const float colW = 0.034;
        float cx = p.x / colW;
        float id = floor(cx);
        float fx = frac(cx) - 0.5;
        float3 h = WsHash3(float2(id, seed));
        float L = 0.40 + 0.55 * h.x;
        float speed = (0.06 + 0.26 * h.y) * flow;
        float s = (p.y - speed * (t + 0.6 * sin(t * (0.7 + h.z) + h.x * 6.28)) + h.z * L) / L;
        float seg = floor(s);
        float fy = frac(s);
        float3 h2 = WsHash3(float2(id * 7.0 + seg, seed + 3.0));
        float wob = (h2.x - 0.5) * 0.4 + 0.07 * sin(p.y * 38.0 + h2.y * 6.28);
        float dx = (fx - wob) * colW;
        float dy = (fy - 0.85) * L;                        // + ahead of the head, - behind it
        float on = step(h2.z, density);
        float rh = 0.0042 * (0.7 + 0.7 * h2.y);
        float q = (dx * dx + dy * dy) / (rh * rh);
        float head = on * step(q, 1.0);
        float behind = saturate(-dy / 0.22);
        float w = 0.0021 * (1.0 - behind);
        trail = on * step(dy, 0.0) * (1.0 - behind) * (1.0 - smoothstep(w * 0.5, w, abs(dx)));
        float rn = rh + grow;
        near = on * max(step(dx * dx + dy * dy, rn * rn), step(dy, 0.0) * step(behind, 0.999) * step(abs(dx), w + grow));
        disp = head * float2(-dx, -dy) * 1.7 + (1.0 - head) * trail * float2(-dx * 1.3, 0.0);
        return max(head * sqrt(saturate(1.0 - q)), trail * 0.35);
    }

    // time since the wiper blade last passed this point of the windscreen (seconds)
    float WsSinceWiped(float2 p)
    {
        float best = _WsWiperGeo.w;
        if (_WsWiper.w < 0.5) return best;
        for (int i = 0; i < 2; i++)
        {
            float2 d = p - float2(i == 0 ? _WsWiperGeo.x : _WsWiperGeo.y, _WsWiperGeo.z);
            float r = length(d);
            float ang = degrees(atan2(d.y, d.x));
            float x = (ang - 4.0) / 96.0;
            if (r < 0.14 || r > 0.50 || x < 0.0 || x > 1.0) continue;
            float tau = 0.5 - sin(asin(1.0 - 2.0 * x) / 3.0);          // inverse smoothstep
            float tUp = _WsWiper.y * tau * 0.5;
            float tDown = _WsWiper.y * (1.0 - tau * 0.5);
            float ts = _WsWiper.x;
            float since = ts >= tDown ? ts - tDown : (ts >= tUp ? ts - tUp : ts + _WsWiper.z - tDown);
            best = min(best, since);
        }
        return best;
    }

    // star crack: radials with a slight meander and chord rings between them. Returns the line mask.
    // near = 1 on the line grown by 1.5 px and on the bullseye haze, for the glass motion vectors
    float WsCrack(float2 p, float4 imp, float4 par, float px, out float2 nrm, out float haze, out float near)
    {
        nrm = 0.0; haze = 0.0; near = 0.0;
        if (imp.z <= 0.001) return 0.0;
        float2 d = p - imp.xy;
        float r = length(d);
        haze = imp.z * (1.0 - smoothstep(par.w * 0.4, par.w, r));
        if (r > par.y * 1.02) return 0.0;
        float th = atan2(d.y, d.x);
        float N = par.x;
        float sector = 6.2831853 / N;
        float k0 = floor(th / sector);
        float best = 1.0e3;
        float lenA = 0.0, lenB = 0.0, thA = 0.0, thB = 0.0, fade = 1.0;
        for (int j = 0; j < 2; j++)
        {
            float k = k0 + j;
            float km = k - N * floor(k / N);
            float thk = (k + 0.6 * (WsHash1(km * 17.0 + imp.w) - 0.5)) * sector;
            // real stars have one or two long runners and many short stubs, not a web of equal spokes: radial 0 (toward +u,
            // so crack 2 still runs across the view) runs full length, the one opposite it 0.7, the rest mostly short
            float hl = WsHash1(km * 5.3 + imp.w * 1.7);
            float runner = km < 0.5 ? 1.0 : (abs(km - floor(N * 0.5)) < 0.5 ? 0.7 : 0.0);
            float len = par.y * imp.z * (runner > 0.0 ? runner : 0.16 + 0.84 * pow(saturate(hl), 2.2));   // saturate: hl is 0..1 already, it only spares FXC's negative-pow warning
            float dth = th - thk;
            dth -= 6.2831853 * round(dth / 6.2831853);
            // lateral offset from the straight ray, in metres: millimetre kinks plus a slow two-frequency wander, so long
            // cracks bend like fractures instead of ruling straight lines (at most about 4 deg, inside the two-radial search)
            float lat = 0.0045 * (WsLin1(r * 42.0 + km * 13.1 + imp.w) - 0.5)
                      + 0.11 * r * (WsLin1(r * 4.5 + km * 3.3 + imp.w) - 0.5)
                      + 0.035 * r * (WsLin1(r * 13.0 + km * 7.7 + imp.w) - 0.5);
            float sd = r * sin(clamp(dth, -1.5, 1.5)) - lat;
            float dist = abs(sd);
            if (j == 0) { lenA = len; thA = thk; } else { lenB = len; thB = thk; }
            if (r < len && dist < best)
            {
                best = dist; nrm = float2(-sin(thk), cos(thk)) * (sd >= 0.0 ? 1.0 : -1.0);
                fade = 1.0 - smoothstep(0.55 * len, len, r);
            }
        }
        float km0 = k0 - N * floor(k0 / N);
        for (int q = 0; q < 4; q++)
        {
            if (q >= (int)par.z) break;
            float rho = par.y * (0.07 + 0.085 * q) * (0.8 + 0.4 * WsHash1(km0 * 3.1 + q * 11.0 + imp.w)) * imp.z;
            if (WsHash1(q * 5.0 + imp.w) > 0.8 || WsHash1(km0 * 9.7 + q * 5.0 + imp.w) > 0.86 || rho > 0.8 * min(lenA, lenB)) continue;
            float2 A = rho * float2(cos(thA), sin(thA)), B = rho * float2(cos(thB), sin(thB));
            float2 ab = B - A;
            float2 ad = d - A;
            float hh = saturate(dot(ad, ab) / max(dot(ab, ab), 1e-8));
            float2 off = ad - ab * hh;
            float dist = length(off);
            if (dist < best) { best = dist; nrm = off / max(dist, 1e-6); fade = 0.8; }
        }
        float w = max(0.00022, 0.55 * px) * lerp(0.6, 1.0, fade);            // hairlines that thin toward the tip
        near = max(step(best, 2.2 * w + 1.5 * px), step(0.05, haze));
        return (1.0 - smoothstep(w, 2.2 * w, best)) * lerp(0.35, 1.0, fade);
    }

    float3 WsLoad(float2 pix)
    {
        pix = clamp(pix, 0.5, _ScreenSize.xy - 0.5);
        return LOAD_TEXTURE2D_X_LOD(_WsSource, (uint2)pix, 0).rgb;
    }

    // shades one glass pixel: colOut = the glass colour, mvOut = the glass motion vector (alpha 1 where it must replace
    // the world's, alpha 0 where the world's is kept)
    void GlassShade(Varyings varyings, out float4 colOut, out float4 mvOut)
    {
        colOut = 0.0; mvOut = 0.0;
        float2 pix = varyings.positionCS.xy;
        float2 uv = pix * _ScreenSize.zw;
        float dRaw = LoadCameraDepth(pix);
        float3 rdW = normalize(ComputeWorldSpacePosition(uv, UNITY_RAW_FAR_CLIP_VALUE, UNITY_MATRIX_I_VP));
        float tScene = dRaw == UNITY_RAW_FAR_CLIP_VALUE ? 1.0e9 : length(ComputeWorldSpacePosition(uv, dRaw, UNITY_MATRIX_I_VP));
        float3 ro = mul(_WsCabInv, float4(_WorldSpaceCameraPos.xyz, 1.0)).xyz;
        float3 rd = mul((float3x3)_WsCabInv, rdW);

        // nearest pane hit that nothing of the interior covers
        float tHit = 1.0e9; float2 g = 0.0; float pane = 0.0;
        for (int i = 0; i < WS_PANES; i++)
        {
            float3 n = cross(_WsU[i].xyz, _WsV[i].xyz);
            float den = dot(rd, n);
            if (abs(den) < 1e-4) continue;
            float t = dot(_WsO[i].xyz - ro, n) / den;
            if (t <= 0.02 || t >= tHit || t > tScene - 0.01) continue;
            float3 q = ro + rd * t - _WsO[i].xyz;
            float gu = dot(q, _WsU[i].xyz), gv = dot(q, _WsV[i].xyz);
            float narrow = _WsB[i].y * gv;
            if (gv < 0.0 || gv > _WsB[i].x || gu < _WsU[i].w + narrow || gu > _WsV[i].w - narrow - _WsB[i].w * gv) continue;
            tHit = t; g = float2(gu, gv); pane = _WsB[i].z;
        }
        if (pane < 0.5) discard;

        // glass metres -> pixels (to turn lens displacements into screen offsets)
        float2 gx = ddx(g), gy = ddy(g);
        float det = gx.x * gy.y - gx.y * gy.x;
        float px = sqrt(abs(det));                                     // metres of glass per pixel
        float2x2 toPix = float2x2(gy.y, -gy.x, -gx.y, gx.x) / (abs(det) > 1e-12 ? det : 1e-12);

        float rain = _WsState.x, speed = _WsState.y, time = _WsState.z, wet = _WsState.w;
        float2 disp = 0.0;
        float cover = 0.0, trail = 0.0, mist = 0.0;
        float3 add = 0.0;
        float dim = 1.0;
        float nearW = 0.0;                                             // 1 where the glass motion replaces the world's
        float cm = 0.0; float2 cn = 0.0;                               // the strongest crack line here and its normal

        if (wet > 0.001)
        {
            float since = pane < 1.5 ? WsSinceWiped(g) : 1.0e3;
            float regrow = 1.0 - exp(-since * (0.25 + 2.2 * rain));     // the glass fills up again behind the blade
            float dens = saturate(wet * (0.25 + 0.75 * rain)) * regrow;
            // flow: gravity when slow, air shear up and outward on the windscreen at speed, backward on the side glass
            float shear = saturate((speed - 7.0) / 9.0);
            float2 flow = pane < 1.5 ? normalize(lerp(float2(0.0, -1.0), float2(g.x * 0.9, 1.0), shear))
                                     : normalize(lerp(float2(0.0, -1.0), float2(pane > 3.5 ? 0.0 : -1.0, -0.25), shear));
            float2 pf = float2(dot(g, float2(flow.y, -flow.x)), dot(g, flow));
            float2 d1, d2, d3, d4; float tr1, tr2, n1, n2, n3, n4;
            // grow = 1.5 px in each function's own units (bead cells; runner metres, x1.9 for the second runner layer)
            float b1 = WsBeads(g, 0.0095, dens * 0.9, time, 11.0, 1.5 * px / 0.0095, d1, n1);
            float b2 = WsBeads(g + 0.37, 0.0048, dens * 0.8, time * 1.7, 23.0, 1.5 * px / 0.0048, d2, n2);
            float fl = 0.25 + 1.6 * shear;
            float r1 = WsRunner(pf, time, fl, dens * rain * 1.4, 5.0, 1.5 * px, d3, tr1, n3);
            float r2 = WsRunner(pf * 1.9 + 0.71, time * 1.3, fl, dens * rain, 9.0, 2.85 * px, d4, tr2, n4);
            // mist pixels keep the world motion on purpose: the mist is the blurred world, so it moves with the world
            nearW = max(max(n1, n2), max(n3, n4));
            d3 = d3.x * float2(flow.y, -flow.x) + d3.y * flow;
            d4 = (d4.x * float2(flow.y, -flow.x) + d4.y * flow) / 1.9;
            disp = d1 + d2 + d3 + d4;
            cover = saturate(b1 + b2 + r1 + r2);
            trail = saturate(tr1 + tr2);
            mist = wet * (0.25 + 0.75 * rain) * regrow * (1.0 - saturate(cover * 2.0 + trail)) * (pane < 1.5 ? 1.0 : 1.6);
            // the tear: the air has been washed clear, so the film on the glass thins as well (the stars stay pin points)
            mist *= 1.0 - 0.6 * _WsClear.x;
            // the wet ridge right behind the blade
            if (pane < 1.5 && since < 0.12) { mist *= 0.2; disp += 0.002 * float2(sin(g.y * 900.0), 0.0) * (1.0 - since / 0.12); }
            // glints of the lightning and the headlight spill in the beads
            float2 ln = normalize(disp + 1e-6);
            float spec = pow(saturate(dot(ln, _WsLightDir.xy) * 0.5 + 0.5), 10.0) * cover;
            add += spec * (_WsLight.rgb * 0.16 + _WsLight.a * float3(0.010, 0.009, 0.008));
        }

        // cracks: windscreen only; they stay for the rest of the game
        if (pane < 1.5)
        {
            for (int c = 0; c < 3; c++)
            {
                float2 nrm; float haze, nc;
                float m = WsCrack(g, _WsCrack[c], _WsCrackP[c], px, nrm, haze, nc);
                nearW = max(nearW, nc);
                if (m > 0.0 || haze > 0.0)
                {
                    disp += nrm * 0.0035 * m + (WsHash3(floor(g * 500.0) + c).xy - 0.5) * 0.006 * haze;
                    // only a trace of darkening: between flashes a crack should be glass catching light, not ink
                    dim *= 1.0 - 0.10 * m;
                    if (m > cm) { cm = m; cn = nrm; }
                    // the fracture faces catch the light in short segments, not along the whole line
                    float seg = step(0.62, WsHash1(floor(length(g - _WsCrack[c].xy) * 28.0) + c * 7.0 + floor(atan2(nrm.y, nrm.x) * 3.0)));
                    float glint = pow(abs(dot(nrm, _WsLightDir.xy)), 6.0) * seg;
                    add += m * (glint * _WsLight.rgb * 0.22 + _WsLight.rgb * 0.015 + float3(1.0, 0.72, 0.29) * 0.024 * _WsLightDir.z + _WsLight.a * 0.004);
                    add += haze * (0.09 * _WsLight.rgb + 0.002 * _WsLight.a);
                    mist = max(mist, haze * 0.8);
                }
            }
            // faint reflection of the instrument glow low in the glass
            add += float3(1.0, 0.72, 0.29) * 0.0025 * _WsLightDir.z * smoothstep(0.30, 0.0, g.y) * smoothstep(0.1, -0.5, g.x);
        }

        float2 off = mul(toPix, disp);
        off = clamp(off, -60.0, 60.0);
        float3 col = WsLoad(pix + off);
        [branch] if (mist > 0.01)
        {
            // a filled disc, not a ring: a ring of taps with no centre prints every thin bright feature (a delineator,
            // a crawler channel) as offset copies. 12 Vogel taps, rotated per pixel and per TAA frame (_TaaFrameInfo.z,
            // HDRP's deterministic 0..7 index), which TAA integrates into a smooth disc
            float rad = 1.5 + 5.0 * mist;
            float rot = 6.2831853 * frac(52.9829189 * frac(dot(pix + 5.588238 * _TaaFrameInfo.z, float2(0.06711056, 0.00583715))));
            float3 blur = 0.0;
            [unroll] for (int j = 0; j < 12; j++)
            {
                float sn, cs;
                sincos(rot + j * 2.3999632, sn, cs);
                blur += WsLoad(pix + rad * sqrt((j + 0.5) / 12.0) * float2(cs, sn));
            }
            col = lerp(col, blur * (1.0 / 12.0), saturate(mist * 1.3));
        }
        // a fracture face takes the brighter of its two sides, so the cracks pick up the headlit road, the lit wedge and
        // the flashes as fine light edges; on a flat sky this is a faint silver hairline (about 1.12x)
        [branch] if (cm > 0.0)
        {
            float2 sp = mul(toPix, cn);
            float3 other = WsLoad(pix - sp * rsqrt(max(dot(sp, sp), 1e-12)) * 2.5);
            col = lerp(col, max(col, other) * 1.35, cm * 0.7);
        }
        col = col * dim + add * GetCurrentExposureMultiplier();
        colOut = float4(col, 1.0);

        // glass motion: where the cab-fixed point under this pixel was on screen last frame (the cab heaves, pitches and
        // jolts against the camera). Camera-relative positions, as HDRP's own camera motion vectors use them:
        // UNITY_MATRIX_PREV_VP takes this frame's camera-relative positions
        float3 pCab = ro + rd * tHit;
        float3 pNow = mul(_WsCabCur, float4(pCab, 1.0)).xyz - _WorldSpaceCameraPos.xyz;
        float3 pPrev = mul(_WsCabPrev, float4(pCab, 1.0)).xyz - _WorldSpaceCameraPos.xyz;
        float4 clipNow = mul(UNITY_MATRIX_UNJITTERED_VP, float4(pNow, 1.0));
        float4 clipPrev = mul(UNITY_MATRIX_PREV_VP, float4(pPrev, 1.0));
        float2 mv = clipNow.xy / clipNow.w - clipPrev.xy / clipPrev.w;
    #if UNITY_UV_STARTS_AT_TOP
        mv.y = -mv.y;
    #endif
        // same encoding as HDRP's CameraMotionVectors (clip-space delta * 0.5); the alpha drives the blend of target 1
        mvOut = float4(mv * 0.5, 0.0, step(0.5, nearW));
    }

    float4 FragGlass(Varyings v) : SV_Target
    {
        float4 c, m;
        GlassShade(v, c, m);
        return c;
    }

    void FragGlassMV(Varyings v, out float4 c : SV_Target0, out float4 m : SV_Target1)
    {
        GlassShade(v, c, m);
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" }
        // 0: colour only (the frame has no motion-vector buffer)
        Pass
        {
            Name "Glass"
            ZWrite Off ZTest Always Blend Off Cull Off
            HLSLPROGRAM
                #pragma fragment FragGlass
            ENDHLSL
        }
        // 1: colour + glass motion vectors (MRT: camera colour, camera motion vectors). Target 1 blends by the output
        // alpha: 1 writes the glass motion under drops and cracks, 0 keeps the world's
        Pass
        {
            Name "GlassMV"
            ZWrite Off ZTest Always Cull Off
            Blend 0 One Zero
            Blend 1 SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
                #pragma fragment FragGlassMV
            ENDHLSL
        }
    }
    Fallback Off
}
