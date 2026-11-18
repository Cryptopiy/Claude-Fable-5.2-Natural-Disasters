Shader "Hidden/Cascade/Particles"
{
    // Stateless particles: every position is a pure function of the particle id and a clock, so the harness can seek.
    // One draw per system (cmd.DrawProcedural, 6 vertices per particle), premultiplied blending, soft depth fade.
    // Kinds: 0 ambient fall (ash, spray rain)  1 embers  2 gulls  3 crest spindrift  4 fountains (buffer)
    //        5 puffs (buffer)  6 incandescent ejecta (bombs, vent fountain)  7 lamp halos (buffer)  8 lightning
    //        9 lighthouse beam (billboard fallback, normally off: the plume pass draws the beam)  10 drops on the lens
    //        11 flotsam on the flood
    HLSLINCLUDE
    #pragma target 4.5
    #pragma only_renderers d3d11 vulkan
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
    #include "Assets/_Game/Shaders/CascadeCommon.hlsl"
    #include "Assets/_Game/Shaders/CascadeCone.hlsl"     // ConeHeight: where bombs land
    #include "Assets/_Game/Shaders/CascadeSea.hlsl"      // SeaEval: what flotsam rides on

    float _PKind;
    float _PClock;          // the clock this system runs on (seconds)
    float4 _PA, _PB, _PC, _PD, _PE;
    float4 _PColor;         // rgb albedo (or emission in nits for additive kinds), a opacity
    float4 _PSize;          // x min size (m), y max size (m), z stretch seconds, w soft fade distance (m)
    float4 _PlFog;          // x 1/mean free path, y base height, z 1/scale height
    StructuredBuffer<float4> _PBufA;    // emitters: xyz position, w start time
    StructuredBuffer<float4> _PBufB;    // emitters: x speed, y size scale, z seed, w life
    float _PEmitters;
    float4 _PLampA, _PLampB;            // the two pooled lamp lights (VillageDirector, read at render time): xyz position, w lumen
    float4 _PLampCol;                   // their linear colour

    #define EJECTA_SLIDE_DURATION 20.0  // Config.SlideDuration: the vent sinks while the flank slides
    #define EJECTA_HEAD 3.0             // glow of a bomb's head segment against the 1.0 of the first tail segment
    #define EJECTA_SPATTER 2.5          // glow of a bomb shattered on the cone (the old head factor 1.4 at 30000 nits)
    // Bomb sizes follow the cube of a uniform rank (most are small, ~10% are big blocks); the glow follows the same rank,
    // SMALL + BIG * rank^3, whose mean is 1.0: the spray dims, a few blocks glow 2-3x (a uniform size read as glitter)
    #define EJECTA_SMALL 0.35
    #define EJECTA_BIG 2.6
    #define EJECTA_SPATTER_MAX 8.0      // m: a shattered block's patch stays 6-16 m across, however big the block was
    // spray droplets (kinds 3/4, droplet look): at most this wide and this long on screen (px). Near the lens they were
    // metres-wide sprites stretched over 0.35-1.2 s: soft smears and rice grains instead of drops
    #define DROP_WIDTH_PX 3.0           // half-width
    #define DROP_STREAK_PX 28.0
    #define ASH_STREAK_PX 18.0          // ash and embers; rain sets its own cap in _PC.y
    // the pooled lamp lights on ash and rain: glassy shards and drops throw a forward lobe toward the lens (peak 1 + GAIN)
    #define LAMP_FWD_GAIN 6.0
    #define LAMP_FWD_POW 8.0
    // A lamp halo's angular size: an aureole in fog is set by the phase function, not by the distance, so a world-sized
    // 2 m sprite filled a third of the frame from 3 m (600). Capped at about 5 degrees; lamps beyond ~23 m are unchanged.
    #define HALO_MAX_TAN 0.09
    // gull plumage (a yellow-legged gull), reflectance relative to the white body (~0.9): mid-grey mantle from above
    // (~0.38), greyer flight feathers under the outer wing from below, black wingtips (~0.06, with their white mirrors
    // blurred in); the sun through the feathers lights the underwing a little
    #define GULL_MANTLE 0.42
    #define GULL_UNDER 0.80
    #define GULL_TIP 0.07
    #define GULL_SUN_THROUGH 0.10
    #define FOUNTAIN_FALL 250.0         // clots fall this far below the vent before they are dropped: the depth test hides
                                        // the ones under the cone, so the wider curtain reaches the slope, not a cut in mid-air
    // Lightning: the quad is now 100 m (main) / 45 m (vent) wide, so the core stays ~8 m. The halo is the light the ash
    // around the channel scatters: at 0.07 over 60 m it was +2-4 L beside a grey hairline (540), at 0.16 over 100 m it
    // carries ~3.8x the energy and reads as a glow (estimated: the sprite adds well under 0.01 of mean luminance)
    #define BOLT_CORE (1.0 / 12.0)      // lightning: half-width of the white-hot core as a fraction of the quad
    #define BOLT_HALO 0.16              // lightning: peak of the halo scattered by the ash and the rain around the channel
    // Spray droplets fade out with distance (crest, strike): far spray belongs to the ray-marched curtain. Capped at 3 px
    // wide, every droplet between 0.4 and 3 km was a solid 2-6 px dot or a 28 px hairline (425 face snow, the 395 starburst
    // of converging streaks, the 476 comb)
    #define DROP_FAR_CREST float2(150.0, 420.0)
    #define DROP_FAR_STRIKE float2(120.0, 350.0)
    // ash before the storm: the flakes forward-scatter the bright horizon ring, so against it they are grey, not black dots
    #define ASH_RING_LIGHT 0.35
    // spray rain: a drop is a ball lens of a wide cone of its surroundings, so it carries the bright horizon ring and the
    // strip, not only the dark lid above (lit by the lid alone it was invisible at 476 and dark dashes over the 455 spray)
    #define RAIN_RING_LIGHT 0.5
    #define RAIN_STRIP_LIGHT 0.15
    // spray droplets are the same ball lenses: the horizon share of the rain, not the mist's 0.75 (bright drops on the
    // dark face were the 425 snow; the far ones now fade out, so this mostly trims the 150-420 m band)
    #define DROP_RING_LIGHT 0.5

    struct V2F
    {
        float4 positionCS : SV_POSITION;
        float4 uvk : TEXCOORD0;      // uv (-1..1), life 0..1, seed
        float4 col : TEXCOORD1;      // rgb radiance factor, a alpha
        float3 misc : TEXCOORD2;     // x view depth, y kind-specific, z additive flag
    };

    float PFog(float3 posAbs)
    {
        float3 ro = _WorldSpaceCameraPos.xyz;
        float3 d = posAbs - ro;
        float t = length(d);
        float3 rd = d / max(t, 1e-3);
        float h0 = (ro.y - _PlFog.y) * _PlFog.z;
        float k = rd.y * _PlFog.z;
        float integral = abs(k) < 1e-5 ? t : (1.0 - exp(-k * t)) / k;
        return exp(-_PlFog.x * exp(-max(h0, -8.0)) * integral);
    }

    // ---- incandescent ejecta ----
    // ballistic flight with linear drag k (1/s), closed form so any age can be evaluated directly
    float3 EjectaPos(float3 a, float3 v0, float k, float t)
    {
        float e = (1.0 - exp(-k * t)) / k;
        return a + v0 * e + float3(0.0, -9.81 * (t - e) / k, 0.0);
    }
    float EjectaGround(float2 xz) { return max(ConeHeight(xz, 0.0), 0.0); }
    // glow of cooling rock: dark red -> orange -> yellow-orange as the normalised temperature T (0..1) rises. Basalt leaves
    // the vent at about 1150 C, an orange heat: a cream-white top burned out to white tracers once the bloom clipped it.
    float3 EjectaRamp(float T)
    {
        float3 c = lerp(float3(1.0, 0.12, 0.01), float3(1.0, 0.34, 0.05), saturate(T * 2.0));
        return lerp(c, float3(1.0, 0.60, 0.24), saturate(T * 2.0 - 1.0));
    }
    // Tilt of the jet at launch time 'born': it wanders from burst to burst (the vent throws one way, then another), so a
    // volley is not one radial fan from a point. Smooth value noise of the launch time (seekable); _PE.zw a steady lean.
    float2 EjectaLean(float born, float period, float amp)
    {
        float x = born / period;
        float i0 = floor(x), f = x - i0;
        f = f * f * (3.0 - 2.0 * f);
        float2 a0 = float2(CHash11(i0 + 17.0), CHash11(i0 + 71.0)) - 0.5;
        float2 a1 = float2(CHash11(i0 + 18.0), CHash11(i0 + 72.0)) - 0.5;
        return lerp(a0, a1, f) * (2.0 * amp) + _PE.zw;
    }
    // Sim.Slide: slide progress 0..1 at collapse clock tc
    float EjectaSlide(float tc)
    {
        float x = saturate(tc / EJECTA_SLIDE_DURATION);
        return x * x * (3.0 - 2.0 * x) * 0.6 + x * x * x * 0.4;
    }
    // the lateral blast when the flank fails (collapse clock)
    float EjectaBlast(float tc) { return CSmooth(0.0, 1.5, tc) * (1.0 - CSmooth(16.0, 25.0, tc)); }
    // Fraction of particles that launch at eruption clock te / collapse clock tc. It is evaluated at each particle's
    // launch time, so a change of rate never makes particles pop in or out in mid-flight.
    // _PC.w onset level, _PB.w steady level, _PC.y blast level, _PE.x silence-clock offset, _PE.y steady factor once calm.
    float EjectaRate(float te, float tc)
    {
        float onset = CSmooth(0.3, 1.2, te) * (1.0 - CSmooth(16.0, 28.0, te));
        float steady = _PB.w * CSmooth(0.3, 3.0, te) * lerp(1.0, _PE.y, CSmooth(0.0, 18.0, te + _PE.x));
        return max(max(_PC.w * onset, steady), _PC.y * EjectaBlast(tc));
    }

    // ---- lightning: per octave, piecewise-linear value noise across the channel gives the kinks ----
    float3 BoltOffset(float u, float seed, float amp, float3 s1, float3 s2, int octs)
    {
        float3 o = float3(0.0, 0.0, 0.0);
        [unroll] for (int lv = 0; lv < 6; lv++)
        {
            if (lv < octs)
            {
                float fr = 4.0 * exp2((float)lv);
                float am = amp * pow(1.75, -(float)lv);
                float x = u * fr;
                float i0 = floor(x), f = x - i0;
                float sd = seed * 13.0 + (float)lv * 7.0;
                float2 a0 = float2(CHash11(i0 + sd), CHash11(i0 + sd + 3.7)) - 0.5;
                float2 a1 = float2(CHash11(i0 + 1.0 + sd), CHash11(i0 + 1.0 + sd + 3.7)) - 0.5;
                float2 off = lerp(a0, a1, f) * am;
                o += s1 * off.x + s2 * off.y;
            }
        }
        return o;
    }

    V2F Vert(uint vid : SV_VertexID)
    {
        V2F o;
        uint pid = vid / 6u;
        uint c = vid % 6u;
        float2 q = float2((c == 1u || c == 2u || c == 4u) ? 1.0 : -1.0, (c == 2u || c == 4u || c == 5u) ? 1.0 : -1.0);
        int kind = (int)(_PKind + 0.5);
        float t = _PClock;
        float h0 = CRand(pid * 3u + 11u), h1 = CRand(pid * 5u + 23u), h2 = CRand(pid * 7u + 41u), h3 = CRand(pid * 13u + 57u);
        float3 cam = _WorldSpaceCameraPos.xyz;
        float3 pos = cam;
        float3 vel = float3(0, -1, 0);
        float size = _PSize.x, alpha = _PColor.a, life01 = 0.5, extra = 0.0;
        float3 rgb = _PColor.rgb;
        float3 emit = float3(0.0, 0.0, 0.0);             // self-emission in nits, added after the lighting (fogged)
        float additive = (kind == 1 || (kind >= 7 && kind <= 10)) ? 1.0 : 0.0;
        bool placed = false;                             // the quad is already laid out in the world (flotsam)
        float3 wp = cam;

        if (kind == 0 || kind == 1)
        {
            // wrap volume around the camera: _PA.xyz box, _PA.w live fraction, _PB.xyz velocity, _PB.w turbulence
            float3 box = _PA.xyz;
            vel = _PB.xyz * (0.7 + 0.6 * h3);
            float3 p0 = float3(h0, h1, h2) * box;
            float3 turb = float3(sin(t * (0.7 + h0) + h1 * 40.0), 0.3 * sin(t * (0.9 + h2) + h0 * 31.0), cos(t * (0.6 + h1) + h2 * 17.0)) * _PB.w;
            float3 p = p0 + vel * t + turb;
            float3 rel = p - cam;
            rel -= box * floor(rel / box + 0.5);
            pos = cam + rel;
            float3 e = 0.5 - abs(rel / box);
            alpha *= saturate(min(e.x, min(e.y, e.z)) * 9.0) * step(h3 * 0.999, _PA.w);
            size = lerp(_PSize.x, _PSize.y, h1 * h1);
            vel += float3(turb.z, 0.0, -turb.x) * 0.6;
            if (kind == 1)
            {
                float age = frac(t / (2.0 + 2.0 * h0) + h2);
                life01 = age;
                alpha *= sin(age * 3.14159) * (0.6 + 0.4 * sin(t * 13.0 + h1 * 50.0));
            }
        }
        else if (kind == 2)
        {
            // gulls: _PA.x time since take-off; perch from the buffer; _PB.xyz flight direction, _PB.w speed
            float4 perch = _PBufA[pid % (uint)max(_PEmitters, 1.0)];
            float tg = _PA.x - h0 * 0.9;
            float3 p = perch.xyz;
            float flap = 0.0;
            if (tg > 0.0)
            {
                float3 dir = normalize(_PB.xyz + float3(h1 - 0.5, 0.0, h2 - 0.5) * 0.7);
                float sp = _PB.w * (0.8 + 0.4 * h3);
                float up = 26.0 * (1.0 - exp(-tg * 0.5)) + tg * (0.7 + 1.6 * h2);
                float fwd = sp * (tg - (1.0 - exp(-tg * 1.4)) / 1.4);
                p += dir * fwd + float3(0, up, 0) + float3(sin(tg * 0.9 + h1 * 6.0), 0.0, cos(tg * 0.7 + h2 * 6.0)) * (3.0 + 5.0 * h3);
                flap = sin(tg * (9.0 + 3.0 * h1) + h2 * 6.0) * saturate(1.0 - tg * 0.02);
                alpha *= 1.0 - smoothstep(48.0, 70.0, tg);
            }
            else if (h3 > 0.86)
            {
                // a few gliders circling over the harbour before the silence
                float a = t * (0.16 + 0.1 * h1) + h2 * 6.28;
                p += float3(cos(a) * (18.0 + 30.0 * h1), 14.0 + 22.0 * h2, sin(a) * (18.0 + 30.0 * h1)) + float3(-25.0, 0.0, 0.0);
                flap = 0.25 * sin(t * 2.0 + h1 * 9.0);
            }
            pos = p;
            size = _PSize.x * (0.85 + 0.3 * h1);
            extra = flap;
        }
        else if (kind == 3)
        {
            // spindrift torn off the crest: _PA.xy wave origin, _PA.z crest radius, _PA.w crest height
            // _PB.xy aim direction, _PB.z crest speed, _PB.w spread (radians); _PC.y droplet look, _PC.z life (s, 0 = 7-13 s)
            float life = _PC.z > 0.0 ? _PC.z * (0.7 + 0.6 * h0) : 7.0 + 6.0 * h0;
            float age = frac(t / life + h1) * life;
            life01 = age / life;
            float rb = _PA.z - _PB.z * age;
            float ang = atan2(_PB.y, _PB.x) + (h2 - 0.5) * 2.0 * _PB.w;
            float2 dir = float2(cos(ang), sin(ang));
            float beam = lerp(0.30, 1.0, smoothstep(0.2, 0.86, dot(dir, _PB.xy)));
            float hc = _PA.w * beam * (0.86 + 0.14 * h3);
            float3 p = float3(_PA.x + dir.x * rb, hc, _PA.y + dir.y * rb);
            float3 v0 = float3(dir.x, 0.0, dir.y) * _PB.z * 0.45 + float3(0.0, 4.0 + 14.0 * h3, 0.0);
            p += v0 * age + float3(0.0, -1.6 * age * age, 0.0);
            pos = p;
            vel = v0 + float3(0.0, -3.2 * age, 0.0);
            // torn streaks of spray, not discs: a narrow size range (a 1.7*h0^2 tail made 40 m soft balls, read as lens bokeh)
            size = lerp(_PSize.x, _PSize.y, life01) * (0.6 + 0.8 * h0 * h0) * saturate(_PA.w / 60.0);
            alpha *= saturate(life01 * 6.0) * (1.0 - life01) * saturate(_PA.w / 30.0) * step(1.0, rb);
        }
        else if (kind == 4 || kind == 5)
        {
            // buffer emitters: fountains (4) rise and fall; puffs (5) swell and drift
            uint ne = (uint)max(_PEmitters, 1.0);
            uint ei = pid % ne;
            float4 ea = _PBufA[ei];
            float4 eb = _PBufB[ei];
            float life = eb.w * (0.75 + 0.5 * h0);
            float age, alive;
            if (_PC.z > 0.5 && _PC.x > 0.0)
            {
                // gated loop (laze): each emitter starts at its own time, then its particles are born evenly over the period _PC.x
                float a0 = t - ea.w - h1 * _PC.x;
                age = a0 - _PC.x * floor(a0 / _PC.x);
                alive = step(0.0, a0) * step(age, life);
            }
            else
            {
                age = t - ea.w - h1 * (kind == 4 ? 2.5 : 1.5);
                if (_PC.x > 0.0) age -= _PC.x * floor(age / _PC.x);                // looping emitters (steam): _PC.x = period
                alive = step(0.0, age) * step(age, life);
            }
            life01 = saturate(age / life);
            float3 p = ea.xyz;
            if (kind == 4)
            {
                float3 dir = normalize(float3((h2 - 0.5) * 0.9, 1.0, (h3 - 0.5) * 0.9) + _PA.xyz);
                float v0 = eb.x * (0.55 + 0.6 * h0);
                float drag = 0.22;
                float e = (1.0 - exp(-drag * age)) / drag;
                p += dir * v0 * e + float3(0.0, -9.81 * 0.55 * (age - e) / drag, 0.0);
                vel = dir * v0 * exp(-drag * age) + float3(0.0, -9.81 * 0.55 * e, 0.0);
                size = eb.y * lerp(_PSize.x, _PSize.y, saturate(life01 * 1.6)) * (0.5 + 1.6 * h1 * h1);
                alpha *= alive * saturate(life01 * 12.0) * pow(1.0 - life01, 1.3);
                p.y = max(p.y, ea.y - 4.0);
            }
            else
            {
                p += float3((h2 - 0.5), 0.2 + h3 * 0.8, (h0 - 0.5)) * eb.x * age + _PA.xyz * age;
                size = eb.y * lerp(_PSize.x, _PSize.y, sqrt(life01)) * (0.5 + 1.6 * h1 * h1);
                alpha *= alive * saturate(life01 * 8.0) * (1.0 - life01);
            }
            pos = p;
            extra = eb.z;
        }
        else if (kind == 6)
        {
            // Incandescent ejecta from the vent. pid = particle * segments + segment: each segment is one slice of a curved
            // trail, like a long exposure. _PD.y mode: 0 bombs (8 segments; they strike the cone and cool there as spatter),
            // 1 fountain clots (3 segments; they fall back into the vent).
            // _PA.xyz the vent before the collapse, _PA.w how far it sinks with the slide (m); _PB.xyz blast direction;
            // _PC.x collapse-clock offset (tc - te), _PC.z additive share (glow at night, dark ash tail by day);
            // _PD.z cooling time (s), _PD.w top launch speed (m/s); _PSize.z seconds per trail segment; _PColor.rgb white-hot nits;
            // _PE.zw steady lean of the jet (downwind).
            bool fountain = _PD.y > 0.5;
            uint nsg = fountain ? 3u : 8u;
            uint bi = pid / nsg;
            float seg = (float)(pid % nsg);
            float nsf = (float)nsg;
            float b0 = CRand(bi * 3u + 101u), b1 = CRand(bi * 5u + 211u), b2 = CRand(bi * 7u + 307u);
            float b3 = CRand(bi * 11u + 401u), b4 = CRand(bi * 13u + 503u), b5 = CRand(bi * 17u + 601u);
            float vb = _PD.w * (fountain ? lerp(0.4, 1.0, pow(b3, 0.8)) : lerp(0.3, 1.0, pow(b3, 1.6)));
            // a clot's whole flight, with time to fall FOUNTAIN_FALL below the vent; a bomb cools within its cycle
            float cyc = fountain ? 2.0 * vb / 9.81 + 4.5 : lerp(24.0, 32.0, b0);
            // Launch phase. Bombs that only fly at full rate (b5 >= 0.35) are phase-locked to the two volleys: the opening blast
            // (te 0.3-3 s) and the flank failure (tc 0.5-3 s, from the clock offset _PC.x). Outside those windows their rate
            // share is below b5, so the lock never shows as a periodic echo.
            float ph = b0 * 7.31;
            if (!fountain && b5 >= 0.6) ph = -(0.3 + 2.7 * frac(b0 * 13.7)) / cyc;
            else if (!fountain && b5 >= 0.35) ph = -(0.5 + 2.5 * frac(b0 * 7.7) - _PC.x) / cyc;
            float age = frac(t / cyc + ph) * cyc;
            float born = t - age;
            float tcb = born + _PC.x;
            float alive = step(0.3, born) * step(b5, EjectaRate(born, tcb));
            // bombs: a heavy tail of a few big blocks over a fine spray; the fountain's clots stay evenly sized
            float bk = fountain ? b4 : b4 * b4 * b4;
            size = lerp(_PSize.x, _PSize.y, bk);
            rgb = float3(0.06, 0.055, 0.05);                                          // the ash wake by day
            additive = _PC.z;
            alpha = 0.0;
            pos = _PA.xyz;
            if (alive > 0.0)
            {
                float bw = fountain ? 0.0 : EjectaBlast(tcb) * step(0.001, _PC.y);
                // bombs: a cone of about +-25 degrees (1.3 made a 42-degree radial sparkler); the fountain's curtain widens
                float spread = fountain ? 0.5 : 0.9;
                float2 lean = EjectaLean(born, fountain ? 2.2 : 3.5, fountain ? 0.12 : 0.22);
                float3 dir = normalize(float3((b1 - 0.5) * spread + lean.x, 1.0, (b2 - 0.5) * spread + lean.y));
                if (bw > 0.0)
                {
                    // launched during the blast: low and fanned out over the slide, toward the village
                    float3 dirB = normalize(_PB.xyz + float3((b1 - 0.5) * 0.9, (b2 - 0.5) * 0.3, (b4 - 0.5) * 0.9));
                    dir = normalize(lerp(dir, dirB, bw));
                }
                float v0 = vb * (fountain ? 0.8 + 0.2 * sin(1.6 * born + b4 * 6.2831853) : 1.0 + 0.15 * bw);   // the fountain pulses
                float k = fountain ? 0.04 : 0.03;                                         // clots slow more than blocks
                float3 A = float3(_PA.x, _PA.y - _PA.w * EjectaSlide(tcb) + 20.0, _PA.z);  // the vent when this one left it
                float3 V0 = dir * v0;
                float tseg = _PSize.z;
                float t1 = age - seg * tseg, t0 = max(t1 - tseg, 0.0);
                // landing: past the apex, bisect the first time the bomb is below the cone surface
                float tl = 1e5;
                float tA = log(1.0 + k * max(V0.y, 0.0) / 9.81) / k;
                if (!fountain && age > tA)
                {
                    float3 pHead = EjectaPos(A, V0, k, age);
                    if (pHead.y < EjectaGround(pHead.xz))
                    {
                        float lo = tA, hi = age;
                        [loop] for (int it = 0; it < 6; it++)
                        {
                            float mid = 0.5 * (lo + hi);
                            float3 pm = EjectaPos(A, V0, k, mid);
                            if (pm.y < EjectaGround(pm.xz)) hi = mid; else lo = mid;
                        }
                        tl = hi;
                    }
                }
                float tau = _PD.z;
                float3 P0 = EjectaPos(A, V0, k, min(t0, tl)), P1 = EjectaPos(A, V0, k, min(t1, tl));
                float T = exp(-min(t1, tl) / tau);
                float fade = pow(1.0 - seg / nsf, 1.6) * step(1e-3, t1);
                // Glow along the trail: the head (the rock, blurred over one segment) carries the light and the tail behind
                // it decays fast, so a bomb reads as a glowing comet. By day (_PC.z low) the tail is mostly the dark ash
                // wake. A full-brightness tail on every segment drew 1.76 s white tracers: fireworks, not lava.
                float glowK = seg < 0.5 ? EJECTA_HEAD : exp(-0.55 * seg) * lerp(0.12, 1.0, _PC.z);
                if (age >= tl && seg < 0.5)
                {
                    // the head has struck the cone: a burst of spatter that cools in place (a shattered bomb shows its hot core)
                    float3 pl = EjectaPos(A, V0, k, tl);
                    float g = EjectaGround(pl.xz);
                    float3 toC = normalize(cam - pl);
                    pl = float3(pl.x, g + 3.0, pl.z) + toC * 10.0;                    // stand clear of the slope in depth
                    size = 2.0 * min(size, EJECTA_SPATTER_MAX);                        // 5-16 m: big blocks glow brighter, not wider
                    P0 = pl - float3(0.0, size * 0.5, 0.0);
                    P1 = pl + float3(0.0, size * 0.5, 0.0);
                    T = max(exp(-tl / tau), 0.55) * exp(-(age - tl) / 7.0);
                    fade = step(0.5, g);                                               // the sea swallows it
                    glowK = EJECTA_SPATTER;
                }
                else if (t0 >= tl) fade = 0.0;                                         // this part of the trail is past the landing point
                // fell back into the crater or onto the slope; over the deep scar a clot dims out in its last 100 m instead
                if (fountain) fade *= saturate((P1.y - (A.y - 20.0 - FOUNTAIN_FALL)) / 100.0);
                pos = 0.5 * (P0 + P1);
                vel = P1 - P0;
                life01 = T;
                emit = _PColor.rgb * EjectaRamp(T) * (T * T) * glowK * (fountain ? 1.0 : EJECTA_SMALL + EJECTA_BIG * bk);
                alpha = _PColor.a * fade;
            }
        }
        else if (kind == 7)
        {
            // lamp halos: buffer position, w = 0..1 group; _PA.x lamps clock (0..1 per group handled in C# via w), _PA.y halo scale.
            // _PC.x > 0.5: the glowing body of the lava fountain instead; buffer B: x size scale, y intensity of each glow.
            uint ei7 = pid % (uint)max(_PEmitters, 1.0);
            float4 ea = _PBufA[ei7];
            pos = ea.xyz;
            float on = saturate((_PA.x - ea.w * 14.0) * 0.9);
            float flick = 0.92 + 0.08 * sin(t * 9.0 + h0 * 40.0);
            alpha = on * flick;
            size = _PSize.x * _PA.y;
            if (_PC.x > 0.5)
            {
                float4 eb7 = _PBufB[ei7];
                size *= eb7.x;
                alpha *= eb7.y;
            }
        }
        else if (kind == 8)
        {
            // Lightning. Main bolt (_PA.w = 0): pid 0..71 the channel A -> B, 72..131 three forks of 20 segments rooted on
            // the displaced channel; _PC.x seed, _PC.y brightness, _PC.z leader reveal 0..1 (the channel draws itself from A).
            // Vent sparks (_PA.w = 1): 8 short bolts of 12 segments; buffer A = end A + brightness, buffer B = end B + seed.
            bool ventMode = _PA.w > 0.5;
            float3 A = _PA.xyz, B = _PB.xyz;
            float seed = _PC.x, bright = _PC.y, reveal = _PC.z;
            float u0, u1;
            int fk = -1;                                                               // fork index, -1 = main channel
            if (ventMode)
            {
                uint vi = pid / 12u;
                float4 va = _PBufA[vi], vb = _PBufB[vi];
                A = va.xyz; B = vb.xyz; bright = va.w; seed = vb.w; reveal = 1.0;
                float sg = (float)(pid % 12u);
                u0 = sg / 12.0; u1 = (sg + 1.0) / 12.0;
            }
            else if (pid < 72u)
            {
                u0 = (float)pid / 72.0; u1 = (float)(pid + 1u) / 72.0;
            }
            else
            {
                uint fp = pid - 72u;
                fk = (int)(fp / 20u);
                float sg = (float)(fp % 20u);
                u0 = sg / 20.0; u1 = (sg + 1.0) / 20.0;
            }
            float len = length(B - A);
            float3 ax = (B - A) / max(len, 1.0);
            float3 s1 = normalize(cross(ax, float3(0.3, 1.0, 0.2)) + float3(1e-4, 0.0, 0.0)), s2 = cross(ax, s1);
            int oct = ventMode ? 4 : 6;
            float amp = len * (ventMode ? 0.10 : 0.07);
            float3 P0, P1;
            float wMul = 1.0, bMul = 1.0;
            if (fk < 0)
            {
                P0 = lerp(A, B, u0) + BoltOffset(u0, seed, amp, s1, s2, oct) * sin(u0 * PI);
                P1 = lerp(A, B, u1) + BoltOffset(u1, seed, amp, s1, s2, oct) * sin(u1 * PI);
                bMul = step(u0, reveal);
            }
            else
            {
                // a fork leaves the main channel at its displaced point uf, runs down and outward, thins and fades to its tip
                float fkf = (float)fk;
                float uf = (fk == 0 ? 0.25 : (fk == 1 ? 0.45 : 0.62)) + (CHash11(seed * 3.1 + fkf) - 0.5) * 0.08;
                float3 root = lerp(A, B, uf) + BoltOffset(uf, seed, amp, s1, s2, oct) * sin(uf * PI);
                float sgn = CHash11(seed * 5.3 + fkf * 1.7) > 0.5 ? 1.0 : -1.0;
                float3 dirF = normalize(ax + s1 * (sgn * (0.6 + 0.6 * CHash11(seed + fkf * 9.1))) + s2 * ((CHash11(seed * 2.3 + fkf) - 0.5) * 0.8) - float3(0.0, 0.35, 0.0));
                float lenF = len * (0.18 + 0.2 * CHash11(seed * 7.7 + fkf * 3.3));
                float3 f1 = normalize(cross(dirF, float3(0.3, 1.0, 0.2)) + float3(1e-4, 0.0, 0.0)), f2 = cross(dirF, f1);
                float sF = seed + 17.0 + fkf * 5.0;
                P0 = root + dirF * (u0 * lenF) + BoltOffset(u0, sF, lenF * 0.12, f1, f2, 5) * sin(u0 * PI * 0.5);
                P1 = root + dirF * (u1 * lenF) + BoltOffset(u1, sF, lenF * 0.12, f1, f2, 5) * sin(u1 * PI * 0.5);
                wMul = 0.55 * (1.0 - 0.6 * u0);
                bMul = 0.4 * pow(1.0 - u0, 0.7) * step(uf + u0 * (1.0 - uf) * 0.5, reveal);
            }
            pos = 0.5 * (P0 + P1);
            vel = P1 - P0;
            size = _PSize.x * wMul;
            alpha = saturate(bright) * bMul;
        }
        else if (kind == 9)
        {
            // lighthouse beam: two long soft quads from the lantern (_PA.xyz) along +-_PB.xyz; _PA.w length, _PC.x strength
            float sgn = pid == 0u ? 1.0 : -1.0;
            pos = _PA.xyz + _PB.xyz * sgn * _PA.w * 0.5;
            vel = _PB.xyz * sgn * _PA.w;
            size = _PSize.x;
            alpha = _PC.x;
        }
        else if (kind == 11)
        {
            // Flotsam: flat debris riding the flood. Carried inland by the run-up, then drawn back toward the bay and
            // turning in the eddies. Buffer A = shore point + the wave clock at which the wall reached it, B.xy = toward the bay.
            // _PA.y end of its life (s after the wall)
            uint ne = (uint)max(_PEmitters, 1.0);
            float4 ea = _PBufA[pid % ne];
            float4 eb = _PBufB[pid % ne];
            float tl = t - ea.w;
            float2 toBay = eb.xy;
            float2 xz = ea.xz + (float2(h0, h2) - 0.5) * 60.0;
            xz -= toBay * ((40.0 + 60.0 * h1) * (1.0 - exp(-max(tl, 0.0) / 3.0)));
            xz += toBay * ((2.0 + 3.0 * h3) * max(tl - 9.0, 0.0));
            xz += float2(sin(0.13 * tl + 6.2831853 * h1), cos(0.11 * tl + 6.2831853 * h2)) * ((8.0 + 14.0 * h3) * saturate(tl / 8.0));
            float ang = h0 * 6.2831853 + 0.06 * tl * (h1 - 0.5);
            float sx = lerp(_PSize.x, _PSize.y, h3 * h3), sy = sx * lerp(0.18, 0.8, h2 * h2);   // planks, beams, doors, roof sheets
            float2 cs = float2(cos(ang), sin(ang));
            float2 cxz = xz + float2(q.x * sx * cs.x - q.y * sy * cs.y, q.x * sx * cs.y + q.y * sy * cs.x);
            SeaInfo si = SeaEval(cxz);
            float y = max(si.y, si.ground) + 0.35;
            wp = float3(cxz.x, y, cxz.y);
            pos = float3(xz.x, y, xz.y);
            placed = true;
            size = sx;
            float hc = CRand(pid * 19u + 77u);
            rgb = hc < 0.6 ? float3(0.12, 0.09, 0.07) : (hc < 0.9 ? float3(0.70, 0.70, 0.68) : float3(0.10, 0.22, 0.48));
            alpha *= CSmooth(1.0, 4.0, tl) * (1.0 - CSmooth(_PA.y - 15.0, _PA.y, tl)) * saturate((si.y - si.ground - 0.2) * 2.0);
        }
        else
        {
            // drops on the lens: screen-space, appear at the impact and slide down slowly (_PA.x seconds since impact)
            float born = h3 * 9.0 - 1.0;
            float age = _PA.x - born;
            float lifeD = 9.0 + 9.0 * h0;
            life01 = saturate(age / lifeD);
            alpha = step(0.0, age) * step(age, lifeD) * saturate(age * 3.0) * (1.0 - life01 * life01);
            size = lerp(0.012, 0.045, h1 * h1);
            extra = age;
        }

        // billboard, stretched along the velocity where asked
        float3 toCam = normalize(cam - pos);
        float dist = length(cam - pos);
        float pix = dist * _PD.x;                              // world size of one pixel at this distance
        // Ash, rain and embers (_PC.x set): blurred by a film shutter, not drawn as long exposures (see the streak below)
        bool shutter = kind <= 1 && _PC.x > 0.5;
        // spray droplets: thin bright streaks, capped on screen like the ash (see the streak below)
        bool drop = (kind == 3 || kind == 4) && _PC.y > 0.5;
        if (shutter && kind == 1) size = min(size, 2.5 * pix);  // an ember near the lens stays a spark, not a glowing bokeh disc
        if (drop) size = min(size, DROP_WIDTH_PX * pix);        // a droplet near the lens is a thin streak, not a metres-wide smear
        if (kind == 7 && _PC.x < 0.5) size = min(size, dist * HALO_MAX_TAN);   // lamp halos (not the fountain's body)
        float sz = max(size, pix * 1.25);
        // far droplets go (the curtain carries the far spray); zero alpha also culls the quad before rasterising (below)
        if (drop)
        {
            float2 farD = kind == 3 ? DROP_FAR_CREST : DROP_FAR_STRIKE;
            alpha *= 1.0 - smoothstep(farD.x, farD.y, dist);
        }
        // a sub-pixel streak keeps its length: only its width is spread over a pixel
        float cover = (kind == 6 || kind == 8) ? size / sz : (size * size) / (sz * sz) + 0.25 * step(6.5, _PKind);
        if (!placed) alpha *= saturate(cover);
        // lightning: the core is kept about a pixel wide with its energy conserved (Frag), so a far bolt neither fades out
        // nor aliases into dashes; the halo around it stays world-sized
        if (kind == 8) life01 = max(BOLT_CORE, 0.6 * pix / max(sz, 1e-3));
        float3 right = UNITY_MATRIX_V[0].xyz, up = UNITY_MATRIX_V[1].xyz;
        if (placed) { }
        else if (kind == 6 || kind == 8 || kind == 9)
        {
            float lenS = length(vel);
            float3 axn = lenS > 1e-4 ? vel / lenS : up;
            float3 side = normalize(cross(axn, toCam) + 1e-5);
            float widen = kind == 9 ? 0.35 + 5.0 * (q.y * 0.5 + 0.5) : 1.0;
            float ext = kind == 9 ? 0.0 : sz * 0.6;                                    // segments overlap at the kinks
            // a bolt's quad is a wide halo: past half a segment its overlap would stack three neighbours (beads)
            if (kind == 8) ext = min(ext, lenS * 0.5);
            wp = pos + side * q.x * sz * widen + axn * q.y * (lenS * 0.5 + ext);
            if (kind == 6 || kind == 8) extra = ext / max(lenS * 0.5 + ext, 1e-3);    // the overlap, cross-faded in Frag
        }
        else if (_PSize.z > 0.0)
        {
            float3 ax = vel * _PSize.z;
            float len = length(ax);
            float3 axn = len > 1e-4 ? ax / len : up;
            if (shutter || drop)
            {
                // Motion blur that conserves energy: the streak is capped on screen (ash 18 px, rain _PC.y, droplets 28 px)
                // and spreads the flake's coverage along its length instead of adding to it. Uncapped full-opacity streaks
                // a metre or two from the lens converged on the wind's vanishing point (warp-speed ash), drew 300 px rain
                // bars and translucent droplet sheets over the lens. _PC.w relaxes the conservation (0 full, 1 none):
                // rain, which would vanish under the ash, and the crest's torn filaments, whose length is their shape.
                float capPx = drop ? DROP_STREAK_PX : (_PC.y > 0.5 ? _PC.y : ASH_STREAK_PX);
                len = min(len, capPx * pix);
                alpha *= pow(max((2.0 * sz) / max(2.0 * sz + len, 1e-6), 1e-6), 1.0 - saturate(_PC.w));
            }
            float3 side = normalize(cross(axn, toCam) + 1e-5);
            wp = pos + side * q.x * sz + axn * q.y * (sz + len * 0.5);
            // droplets: the ends of the streak ramp over one droplet width (Frag), the middle is flat
            if (drop) extra = min(2.0 * sz / (sz + len * 0.5), 1.0);
        }
        else wp = pos + right * q.x * sz + up * q.y * sz;

        // lighting: soft, mostly ambient; fog to the backdrop colour
        float3 L = _CSunDir.xyz;
        float3 light = _CSunColor.rgb * (0.35 + 0.45 * saturate(dot(toCam, L) * 0.5 + 0.5)) * (1.0 / PI) + CAmbient(float3(0, 1, 0));
        if (kind == 0)
        {
            // _PC.y > 0.5 is the spray rain (its streak cap); ash has none. The ring is gone in the storm: its flakes keep
            // today's look there
            if (_PC.y > 0.5) light += _CSkyHorizon.rgb * RAIN_RING_LIGHT + _CStrip.rgb * RAIN_STRIP_LIGHT;
            else light += _CSkyHorizon.rgb * (ASH_RING_LIGHT * (1.0 - saturate(_CState.y)));
        }
        if (kind == 2)
        {
            // A gull is an opaque two-faced card, not a lit puff (one sunlit colour made a flat white boomerang): from
            // above its sunlit back, from below the underwing in its own shade, lit by the sea and the land, a little sun
            // through the feathers and, as the wings flap and bank, some of the low sun. Frag adds the plumage.
            float sunUp = saturate(L.y);
            float above = smoothstep(-0.1, 0.1, toCam.y);
            float3 topL = _CSunColor.rgb * ((0.25 + 0.75 * sunUp) / PI) + CAmbient(float3(0, 1, 0));
            float3 botL = _CSunColor.rgb * ((GULL_SUN_THROUGH + 0.25 * (1.0 - sunUp)) / PI) + CAmbient(float3(0, -1, 0));
            light = lerp(botL, topL, above);
            life01 = above;
        }
        float ft = PFog(pos);
        // mist glows against the bright horizon; a droplet takes the rain's share (DROP_RING_LIGHT)
        if (kind >= 3 && kind <= 5) light += _CSkyHorizon.rgb * (drop ? DROP_RING_LIGHT : 0.75);
        // laze: lit from below by the lava where it meets the sea (zero for the other puffs). Taken at each corner's
        // height, so the colour runs from an amber foot at the waterline to grey steam above it; at the sprite's centre
        // the whole 40-500 m puff was one orange (the 540 flame tongues)
        if (kind == 5) light += _PB.rgb * exp(-max(wp.y, 0.0) * _PB.w);
        if (kind == 5 && _PC.z > 0.5) alpha *= saturate(ft * 4.0);                     // laze in the storm: gone, not a fog-coloured card
        // Any far puff in the storm: deep in the fog it takes _CFogColor, which is brighter than the veil HDRP renders out
        // there, so it showed as a pale card. Physically it has no contrast left at that depth: fade it with the fog.
        if (kind >= 3 && kind <= 5) alpha *= lerp(1.0, saturate(ft * 4.0), saturate(_CState.y * 1.2));
        float glint = 0.6 + 1.4 * step(0.8, frac(h2 * 7.31 + t * (0.5 + h0)));      // tumbling flakes flash (mean 0.88)
        if (kind == 0 && _CBeam.w > 0.0)
        {
            // the lighthouse beam catches the falling ash: flakes inside the cone light up, brightest looking into it
            float3 bl = pos - _CBeam.xyz;
            float bd = max(length(bl), 1e-3);
            float3 bn = bl / bd;
            float c0 = _CBeam2.w;
            float inCone = smoothstep(c0, lerp(c0, 1.0, 0.6), abs(dot(bn, _CBeam2.xyz)));
            float fwd = saturate(dot(bn, toCam));
            light += float3(1.0, 0.93, 0.78) * (_CBeam.w * inCone * (1.0 + 4.0 * pow(fwd, 8.0)) * glint / (1.0 + bd * bd * 1e-4));
        }
        if (kind == 0 && _PC.z > 0.0 && _PLampA.w + _PLampB.w > 0.0)
        {
            // The two pooled lamp lights catch the ash and the spray rain (_PC.z: gain), a snow-globe of glints around
            // each lamp. Lambert radiance per unit albedo of a point source of lm lumen at d metres is lm / (4 pi^2 d^2);
            // glassy shards and drops add a forward lobe, so the ones crossing between a lamp and the lens flare. The ash
            // between the lamp and the flake dims the light (fog density at the flake's height, as PFog).
            float fogK = _PlFog.x * exp(-max((pos.y - _PlFog.y) * _PlFog.z, -8.0));
            float3 la = pos - _PLampA.xyz, lb = pos - _PLampB.xyz;
            float da = length(la), db = length(lb);
            float fa = pow(saturate(dot(la, toCam) / max(da, 1e-3)), LAMP_FWD_POW);
            float fb = pow(saturate(dot(lb, toCam) / max(db, 1e-3)), LAMP_FWD_POW);
            float ea = _PLampA.w * exp(-da * fogK) * (1.0 + LAMP_FWD_GAIN * fa) / (da * da + 0.5);
            float eb = _PLampB.w * exp(-db * fogK) * (1.0 + LAMP_FWD_GAIN * fb) / (db * db + 0.5);
            light += _PLampCol.rgb * ((ea + eb) * (0.025330296 * _PC.z) * glint);
        }
        float3 lit = lerp(_CFogColor.rgb, rgb * light, ft);
        // nothing swells in front of the lens: ash, rain and embers keep clear of the first 1.5-3 m (a flake there is a
        // blurred blot), spray droplets of the first 6-16 m (the rain system is the spray that reaches the walker)
        alpha *= shutter ? saturate((dist - 1.5) * 0.7) : (drop ? saturate((dist - 6.0) / 10.0) : saturate((dist - 0.45) * 2.5));
        o.col = float4(lerp(lit, rgb * ft, additive) + emit * ft, alpha);
        o.positionCS = TransformWorldToHClip(GetCameraRelativePositionWS(wp));
        if (kind == 10)
        {
            // lens drops live in clip space
            float2 c0 = float2(h0 * 2.0 - 1.0, 1.05 - h2 * 2.0 - extra * (0.006 + 0.02 * h1));
            float aspect = _ScreenParams.x / _ScreenParams.y;
            o.positionCS = float4(c0 + q * float2(size / aspect, size * 1.25), 0.5, 1.0);
            // the rim refracts the air around the walker: the local veil, not the bright horizon ring (grey rings)
            o.col = float4(_CFogColor.rgb * 0.5 + CAmbient(float3(0, 1, 0)) * 0.6, alpha * _PColor.a);
        }
        o.uvk = float4(q, life01, kind == 2 ? ft : h0);    // a gull's plumage is shaded under its fog (Frag)
        o.misc = float3(o.positionCS.w, extra, additive);
        // dead puffs, trail segments and unlit bolt segments: alpha is the same for all six corners of these kinds, so drop
        // the quad before rasterising (saves the noise taps of invisible puffs)
        if (kind >= 3 && kind <= 8 && kind != 7 && alpha <= 0.0) o.positionCS = float4(2.0, 2.0, 2.0, 1.0);
        return o;
    }

    float4 Frag(V2F i) : SV_Target
    {
        int kind = (int)(_PKind + 0.5);
        float2 uv = i.uvk.xy;
        float r2 = dot(uv, uv);
        float a = i.col.a;
        bool drop = (kind == 3 || kind == 4) && _PC.y > 0.5;
        float gullShade = 1.0;
        if (kind == 2)
        {
            // a gull: a shallow M that flaps
            float flap = i.misc.y;
            float x = abs(uv.x);
            float wing = 0.34 * x * (0.45 + flap) - 0.22 * x * x * (1.0 + flap) + 0.05;
            float d = abs(uv.y - wing + 0.06);
            a *= smoothstep(0.16 * (1.05 - x), 0.05 * (1.05 - x), d) * step(x, 0.98);
            // plumage: white head and body; the wings grey from above (mantle), white from below with greyer flight
            // feathers toward the hand; black tips on both sides. uvk.z: 1 seen from above, 0 from below
            float above = i.uvk.z;
            float under = lerp(1.0, GULL_UNDER, smoothstep(0.30, 0.64, x));
            gullShade = lerp(1.0, lerp(under, GULL_MANTLE, above), smoothstep(0.10, 0.20, x))
                      * lerp(1.0, GULL_TIP, smoothstep(0.64, 0.86, x));
        }
        else if (drop)
        {
            // a droplet blurred by the shutter: soft across, flat along the streak, its ends ramped over one droplet
            // width (misc.y); an ellipse fading from the middle read as soft rice grains
            float across = saturate(1.0 - uv.x * uv.x);
            a *= across * across * saturate((1.0 - abs(uv.y)) / max(i.misc.y, 1e-3));
        }
        else if (kind == 3 || kind == 4 || kind == 5)
        {
            // cloud-like sprite: soft disc eaten by noise so that it never reads as a card
            // a random rotation per particle, and noise at a frequency that actually erodes the disc
            float ang = i.uvk.w * 6.2831853;
            float2 ru = float2(uv.x * cos(ang) - uv.y * sin(ang), uv.x * sin(ang) + uv.y * cos(ang));
            float n = CNoise2(ru * 0.55 + i.uvk.w * 7.3 + i.uvk.z * 0.15);
            float n2 = CNoise2All(ru * 1.3 + i.uvk.w * 3.1).g;
            float body = saturate(1.0 - r2);
            a *= saturate(body * body * 1.7 - (1.0 - (n * 0.65 + n2 * 0.35)) * 0.95 + 0.15 * body);
        }
        else if (kind == 6)
        {
            // one trail segment: soft across; along, its ends cross-fade linearly with the neighbours over the overlap (misc.y)
            a *= saturate(1.0 - uv.x * uv.x) * saturate((1.0 - abs(uv.y)) / max(2.0 * i.misc.y, 1e-3));
        }
        else if (kind == 7)
        {
            a *= pow(saturate(1.0 - sqrt(r2)), 2.6);
        }
        else if (kind == 8)
        {
            // a thin white-hot core (about a pixel at least, energy kept: uvk.z) inside a soft halo of light scattered by
            // the ash and the rain; the segment ends cross-fade with the neighbours over the overlap (misc.y)
            float x = abs(uv.x), cf = i.uvk.z;
            a *= saturate((1.0 - abs(uv.y)) / max(2.0 * i.misc.y, 1e-3));
            float coreP = saturate(1.0 - x / cf);
            a *= coreP * coreP * coreP * (BOLT_CORE / cf) + BOLT_HALO * (1.0 - x) * (1.0 - x);
        }
        else if (kind == 9)
        {
            float along = uv.y * 0.5 + 0.5;                                        // 0 at the lantern, 1 at the far end
            a *= pow(saturate(1.0 - abs(uv.x)), 1.6) * (1.0 - along) * (1.0 - along) * saturate(along * 40.0);
        }
        else if (kind == 10)
        {
            // a drop: bright lower rim, dark upper rim, faint body
            float r = sqrt(r2);
            float rim = smoothstep(0.55, 0.92, r) * (1.0 - smoothstep(0.92, 1.0, r));
            float body = (1.0 - smoothstep(0.85, 1.0, r)) * 0.10;
            a *= body + rim * (0.25 + 0.55 * saturate(-uv.y * 0.7 + 0.5));
            if (a < 0.003) discard;
            return float4(i.col.rgb * a * GetCurrentExposureMultiplier(), a * 0.35);
        }
        else if (kind == 11)
        {
            // a broken board: a rectangle with ragged edges
            float2 au = abs(uv);
            float edge = max(au.x, au.y) + 0.35 * (CNoise2(uv * 0.37 + i.uvk.w * 17.0) - 0.5);
            a *= saturate((0.9 - edge) * 8.0);
        }
        else
        {
            a *= saturate(1.0 - r2);
        }
        // a bolt's halo is faint in alpha but 240000 nits strong: the usual cut would leave a hard edge around it
        if (a < (kind == 8 ? 1e-5 : 0.003)) discard;
        float sceneZ = LinearEyeDepth(LoadCameraDepth(i.positionCS.xy), _ZBufferParams);
        a *= saturate((sceneZ - i.misc.x) / max(_PSize.w, 0.01));
        float3 rgb = i.col.rgb;
        if (kind == 2)
        {
            // shade the surface part only: the fog in front of the gull (1 - ft, uvk.w) keeps its colour
            float3 fogP = _CFogColor.rgb * (1.0 - i.uvk.w);
            rgb = fogP + (rgb - fogP) * gullShade;
        }
        if ((kind == 3 || kind == 4 || kind == 5) && !drop) rgb *= 0.72 + 0.28 * saturate(0.5 + uv.y * 0.5 + uv.x * 0.25);     // cheap volume shading
        float ex = GetCurrentExposureMultiplier();
        return float4(rgb * a * ex, a * (1.0 - i.misc.z));
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" }
        Pass
        {
            Name "Particles"
            Blend One OneMinusSrcAlpha
            ZWrite Off ZTest LEqual Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
    }
    Fallback Off
}
