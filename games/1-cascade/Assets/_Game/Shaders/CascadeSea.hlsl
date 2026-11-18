#ifndef CASCADE_SEA_INCLUDED
#define CASCADE_SEA_INCLUDED

// One height function for the whole sea: calm water, the rings of the splash, the drawdown (and the wet film on the
// bared bed), the tsunami wall, the run-up and the flood.
// Uniforms come from WaterDirector (pure functions of the event clocks). Used by the water mesh (vertex and pixel).

float4 _SeaSplash;      // xy centre of curvature of the front (xz), z crest radius from it (m), w crest height (m)
float4 _SeaDome;        // xy where the slide entered the sea (the splash dome)
float4 _SeaAim;         // xy main lobe direction (unit), z drawdown depth (m), w flood level (m)
float4 _SeaBay;         // xy bay centre, z inner radius, w outer radius of the drawdown / flood region
float4 _SeaMisc;        // x splash dome height, y max run-up height (absolute y), z second crest scale, w swell amplitude
float4 _SeaRing;        // x seconds since the splash, y ring amplitude (m; 0 = off)
float4 _CTerrainRect;   // x0, z0, 1/sizeX, 1/sizeZ of the baked ground texture
TEXTURE2D(_CTerrainTex);    // R ground height, G signed distance to the caldera shore (+ on land)

#define SEA_TEETH 0.45          // crest teeth: height swing of ~100 m sections of the crest (x fbm-0.5, about +-7 %)
#define SEA_RING_LEN 300.0      // wavelength of the splash rings (m)
#define SEA_RING_C 52.0         // their phase speed (m/s); the envelope runs ahead at 70 m/s
#define SEA_PUDDLE_H 0.3        // puddles and rivulets stand this far above the bared bed (m)
#define SEA_LOBE_LOD 2.0        // mip of the lobe taps along the front (lat/1400: 22 m texels; lat/380: 12 m at SEA_LOBE2_LOD).
#define SEA_LOBE2_LOD 3.0       // At mip 0 their finest octaves shift the crest by +-1..5 m every 3-20 m, which corrugates the
                                // whole face into flutes from crest to foot (lateral slopes ~0.6): thin bright lines in the reflection
#define SEA_CALDERA_A 4980.0    // Config.CalderaA / CalderaB: the inner shore ellipse round the cone axis (WorldGen.Build)
#define SEA_CALDERA_B 3500.0

struct SeaInfo
{
    float y;        // water surface height
    float wave;     // tsunami component
    float xi;       // distance ahead of the crest (m); negative behind it
    float amp;      // local crest height
    float wf;       // front half width
    float ground;   // ground height below
    float sd;       // signed distance to shore (+ land)
    float depth;    // y - ground
    float lat;      // distance along the front from the main axis (m)
    float film;     // the surface is the wet film on the bared bed (0..1 with the drawdown), 0 elsewhere
    float riv;      // rivulet mask inside the film (0 = puddle or dry)
    float3 lobes;   // SeaLobes(lat) while the wave runs (kept so the water shader finds its breaking sections without new taps)
};

float2 SeaGround(float2 xz)
{
    float2 uv = (xz - _CTerrainRect.xy) * _CTerrainRect.zw;
    float2 e = min(uv, 1.0 - uv);
    float inside = saturate(min(e.x, e.y) * 60.0);
    float2 g = SAMPLE_TEXTURE2D_LOD(_CTerrainTex, s_linear_clamp_sampler, saturate(uv), 0).rg;
    g = lerp(float2(-300.0, -2000.0), g, inside);
#ifdef CASCADE_CONE_INCLUDED
    // the volcano is outside the village bake: give it a shore of its own (surf ring, shallows, the wave dies on it)
    float rc = length(xz - _CCone.xz);
    if (rc < CONE_R + 700.0) { g.x = max(g.x, ConeHeight(xz, 0.0)); g.y = max(g.y, CONE_R - rc); }
#endif
    return g;
}

// Lobes of the front at lat metres along it: x lobe (sections lead and lag), y lobe2 (finer peaks and saddles),
// z the teeth value (R of the lobe2 tap). PlumeFx places the crest spray with the same lobes: it should call this too.
float3 SeaLobes(float lat)
{
    float lobe = SAMPLE_TEXTURE2D_LOD(_CNoise2D, s_linear_repeat_sampler, float2(lat / 1400.0, 0.37), SEA_LOBE_LOD).r - 0.5;
    float4 l2 = SAMPLE_TEXTURE2D_LOD(_CNoise2D, s_linear_repeat_sampler, float2(lat / 380.0, 0.71), SEA_LOBE2_LOD);
    return float3(lobe, l2.g - 0.5, l2.r);
}

// ---- the crest, shared with PlumeFx (the crest spray), so spray and white water agree section by section ----
// The plume sections of the front (0..1) at lat, as PlumeFx's crest block has them inline (pl); lb = SeaLobes(lat)
float SeaPlume(float lat, float3 lb)
{
    return smoothstep(0.50, 0.80, SAMPLE_TEXTURE2D_LOD(_CNoise2D, s_linear_repeat_sampler, float2(lat / 760.0, 0.13), 0).r + 0.35 * lb.x);
}
// How hard the front breaks at lat: 0 glassy, 1 breaking. About half the front breaks, in white sections of ~300 m with
// glassy teal stretches of ~350 m between (400 m noise cells and the 350 m lobes; lobe2 tears the ends). The water
// shader whitens the crest, the drapes and the wake with it; the crest spray should take its strength from it too, so
// the plumes rise out of the white sections. (The older inline mix, lobe2 + 0.6 (pl - 0.5) through smoothstep(-0.3,
// 0.25), sat near 0 on ~95 % of the front: pl is rarely above 0)
float SeaBreak(float lat, float3 lb)
{
    float n = SAMPLE_TEXTURE2D_LOD(_CNoise2D, s_linear_repeat_sampler, float2(lat / 1600.0, 0.13), 2.0).r;
    return smoothstep(-0.07, 0.07, n + 0.6 * lb.x + 0.1 * lb.y - 0.423);
}
// Height factor of the crest teeth at the peak: the sea's crest is A * SeaTeethK (times the profile), and the spray's
// base sits on it, so the spray grows out of the white lip and does not hang over a lower tooth
float SeaTeethK(float3 lb) { return 1.0 + SEA_TEETH * (lb.z - 0.5); }

// ---- the splash rings ----
// Lobes round the dome (0..1, mean ~0.5): a seamless circle through the noise tile, at mip 3 so there are ~10-13
// broad lobes (the finest octaves zigzag the inner rings). The R layer only spans 0.26-0.67 on that circle, so it is
// stretched: unstretched the rings and the whitewater stay a pond target
float SeaRingLobe(float2 dn)
{
    float l = SAMPLE_TEXTURE2D_LOD(_CNoise2D, s_linear_repeat_sampler, dn * 0.35 + 0.37, 3.0).r;
    return saturate((l - 0.45) * 2.4 + 0.5);
}
// The flank slid toward the village: the struck sea is thrown down the slide axis (the wave's own aim), 0.3 behind
float SeaRingAim(float2 dn) { return lerp(0.3, 1.0, smoothstep(-0.5, 0.7, dot(dn, _SeaAim.xy))); }

// Signed distance (m, + on land) to the caldera's inner shore outside the village bake: the ellipse of WorldGen.Build
// with its three shore harmonics (parametric angle phi, degrees 26-349; the village sector is in the bake).
// strait = 1 in the gap of the ring, where the rim sinks under the sea and there is no shore.
float SeaRingSd(float2 xz, out float strait)
{
    float2 d = xz - _CCone.xz;
    float2 e = d / float2(SEA_CALDERA_A, SEA_CALDERA_B);
    float rho = max(length(e), 1e-4);
    float ph = atan2(e.y, e.x);
    ph = ph < 0.0 ? ph + 6.2831853 : ph;
    float deg = ph * 57.2957795;
    float w = smoothstep(26.0, 52.0, deg) * (1.0 - smoothstep(322.0, 349.0, deg));
    float k = 1.0 + w * (0.035 * sin(3.0 * ph + 1.3) + 0.02 * sin(7.0 * ph + 0.4) + 0.012 * sin(13.0 * ph + 2.1));
    strait = smoothstep(253.0, 262.0, deg) * (1.0 - smoothstep(292.0, 301.0, deg));   // WorldGen GapFrom/GapTo -+ 9
    float2 g = d / (float2(SEA_CALDERA_A * SEA_CALDERA_A, SEA_CALDERA_B * SEA_CALDERA_B) * rho);  // gradient of rho (1/m)
    return (rho - k) / max(length(g), 1e-7);
}

float SeaProfile(float xi, float wf, float lat)
{
    float p;
    if (xi >= 0.0) p = exp(-pow(abs(xi / wf), 1.7));
    else
    {
        float b = -xi;
        float wb = wf * 1.5 + 110.0;
        p = 1.0 / (1.0 + (b / wb) * (b / wb));
        // the sea behind the wall stays high and heaves: decaying crests every few hundred metres
        float tail = exp(-b / 1500.0) * smoothstep(60.0, 420.0, b);
        p += tail * (0.20 + 0.45 * (SAMPLE_TEXTURE2D_LOD(_CNoise2D, s_linear_repeat_sampler, float2(b / 520.0, lat / 1800.0), 0).r - 0.5));   // irregular swells behind
    }
    float tr = (xi - 2.5 * wf) / (1.5 * wf);
    p -= 0.02 * exp(-tr * tr);              // a shallow trough just ahead of the wall
    return p;
}

// The drawdown bares the bed: a thin wet film that hugs it. Mirror puddles fill the hollows and rivulets run back
// towards the bay; elsewhere the film hides just under the ground, so the wet edges follow this noise and not the
// different triangulations of the water grid (5 m) and the terrain chunks (2-8 m). Only where the bed is near the
// surface during the drawdown: 2 ground taps and 2 noise taps.
void SeaFilm(float2 xz, float2 g, float wBay, inout SeaInfo s)
{
    float onK = saturate(_SeaAim.z / 3.0) * wBay * (1.0 - saturate(_SeaAim.w / 2.0));
    if (onK < 0.001 || g.x > 0.6 || g.x + 0.5 < s.y) return;
    float gx = SeaGround(xz + float2(4.0, 0.0)).x, gz = SeaGround(xz + float2(0.0, 4.0)).x;
    float slopeG = length(float2(gx - g.x, gz - g.x)) * 0.25;                  // ground gradient (m/m)
    // puddles: 40 m cells of the fbm, edges roughened by its 10 m layer; only on near-level ground
    float4 nb = SAMPLE_TEXTURE2D_LOD(_CNoise2D, s_linear_repeat_sampler, xz / 160.0, 0);
    float pud = smoothstep(0.52, 0.66, nb.r + 0.18 * (nb.a - 0.5)) * (1.0 - smoothstep(0.05, 0.14, slopeG));
    // rivulets: contour lines of a noise stretched along the radius of the bay (24 whole tiles round it: no seam),
    // meandering by the G layer; about 8-15 m wide so the 5 m grid carries them
    float2 rel = xz - _SeaBay.xy;
    float ang = atan2(rel.y, rel.x) * (24.0 / 6.2831853);
    float rn = SAMPLE_TEXTURE2D_LOD(_CNoise2D, s_linear_repeat_sampler, float2(ang + 0.8 * (nb.g - 0.5), length(rel) / 900.0), 0).r;
    float riv = (1.0 - smoothstep(0.035, 0.08, abs(rn - 0.5))) * (1.0 - smoothstep(0.2, 0.4, slopeG));
    float wet = max(pud, 0.85 * riv) * onK * smoothstep(0.6, -0.4, g.x);      // fades out before the old waterline
    // dry: below the ground (deeper on slopes, where the two meshes disagree most); wet: above it
    float fy = g.x + lerp(-0.6 - 3.0 * slopeG, SEA_PUDDLE_H, wet);
    if (fy > s.y) { s.y = fy; s.film = onK; s.riv = riv * (1.0 - pud); }
}

// withFilm = false leaves the wet film out (the water shader's large-scale normal: the film lies level)
SeaInfo SeaEvalX(float2 xz, bool withFilm)
{
    SeaInfo s = (SeaInfo)0;
    float2 g = SeaGround(xz);
    s.ground = g.x; s.sd = g.y;
    float wBay = 1.0 - smoothstep(_SeaBay.z, _SeaBay.w, length(xz - _SeaBay.xy));
    float y = (_SeaAim.w - _SeaAim.z) * wBay;
    float2 d = xz - _SeaSplash.xy;
    float r = length(d);
    float2 dd = xz - _SeaDome.xy;
    s.wave = 0.0; s.xi = 1e5; s.amp = 0.0; s.wf = 60.0; s.lat = 0.0; s.film = 0.0; s.riv = 0.0;
    float A = _SeaSplash.w;
    if (A > 0.02)
    {
        float2 nd = d / max(r, 1.0);
        float cosT = dot(nd, _SeaAim.xy);
        A *= lerp(0.30, 1.0, smoothstep(0.2, 0.86, cosT)) * smoothstep(100.0, 520.0, length(dd));
        // the crest is not a ruler line: lobes along the front (fixed in the world), and the front itself wobbles
        float lat = atan2(nd.x * _SeaAim.y - nd.y * _SeaAim.x, cosT) * max(_SeaSplash.z, 300.0);
        s.lat = lat;
        float3 lb = SeaLobes(lat);
        s.lobes = lb;
        float lobe = lb.x, lobe2 = lb.y;
        A *= 1.0 + 0.40 * lobe + 0.20 * lobe2;                       // peaks and saddles along the front
        s.amp = A;
        s.wf = max(0.5 * A * (1.0 - 0.3 * lobe2), 26.0);
        s.xi = r - _SeaSplash.z + lobe * 300.0 + lobe2 * 60.0;       // sections lead and lag by a few hundred metres
        float hw = A * SeaProfile(s.xi, s.wf, lat);
        // teeth: the top edge breaks into separate crests about 100 m apart (R layer of the same tap: 95 m cells)
        hw *= 1.0 + (SeaTeethK(lb) - 1.0) * exp(-(s.xi * s.xi) / (0.64 * s.wf * s.wf));
        // second, lower crest far behind
        float x2 = s.xi + 1150.0;
        hw += A * _SeaMisc.z * exp(-(x2 * x2) / (2.0 * 150.0 * 150.0));
        // turbulence riding on the wave: the surface of a moving mass, not a smooth ramp
        float env = saturate(1.0 - abs(s.xi + 200.0) / 1000.0) * saturate(A / 50.0);
        // calm over the crest: there the white lip and the spray draw the edge, not +-20 m bumps on the 10 m grid (a rock ridge)
        env *= 1.0 - 0.8 * exp(-(s.xi * s.xi) / (0.2025 * s.wf * s.wf));
        float tn = SAMPLE_TEXTURE2D_LOD(_CNoise2D, s_linear_repeat_sampler, xz / 173.0 + _CTime * float2(0.011, 0.007), 0).r - 0.5;
        tn += (SAMPLE_TEXTURE2D_LOD(_CNoise2D, s_linear_repeat_sampler, xz / 61.0 - _CTime * float2(0.023, 0.014), 0).g - 0.5) * 0.55;
        hw += A * 0.085 * tn * env;
        // the land takes the wave: it breaks down towards the run-up height near the shore and dies inland
        float capK = smoothstep(-460.0, 20.0, s.sd);
        float room = max(_SeaMisc.y - y, 0.0);
        hw *= lerp(1.0, min(1.0, room / max(A, 1.0)), capK);
        hw *= exp(-max(s.sd, 0.0) / 170.0);
        s.wave = hw;
    }
    y += _SeaMisc.x * 0.12 * exp(-dot(dd, dd) / (620.0 * 620.0));      // the splash dome lifts the sea (the crown of spray is a volume)
    // the struck sea: rings race out from the splash over the caldera (bands of reflected horizon from the village);
    // they die where the bay is already draining. Lobed and thrown toward the village (the birth of the tsunami), and
    // their sections lead and lag by a share of the way run (up to +-70 m), so they do not read as a pebble's rings.
    // Between the lobes a ring fades out (~28 % of the circle, in ~5 gaps): seen at 1-2 degrees the lead and lag move a
    // ring by only 1-3 px, so only broken rings stop reading as a pond target
    if (_SeaRing.y > 0.01)
    {
        float rr = length(dd);
        float2 dn = dd / max(rr, 1.0);
        float lobeR = SeaRingLobe(dn);
        rr += (lobeR - 0.5) * min(0.12 * rr, 140.0);
        float front = 300.0 + 70.0 * _SeaRing.x;
        float env = smoothstep(front, front - 250.0, rr) * smoothstep(250.0, 700.0, rr) * exp(-rr / 4000.0)
                  * (1.0 - wBay * saturate(_SeaAim.z / 4.0)) * SeaRingAim(dn) * (0.10 + 1.5 * smoothstep(0.2, 0.8, lobeR));
        y += _SeaRing.y * env * sin(6.2831853 * (rr - SEA_RING_C * _SeaRing.x) / SEA_RING_LEN);
    }
    // flood water in the bay heaves and churns
    float churn = saturate(_SeaAim.w / 12.0) * wBay;
    if (churn > 0.001)
    {
        float cn = SAMPLE_TEXTURE2D_LOD(_CNoise2D, s_linear_repeat_sampler, xz / 97.0 + _CTime * float2(0.017, -0.012), 0).r - 0.5;
        cn += (SAMPLE_TEXTURE2D_LOD(_CNoise2D, s_linear_repeat_sampler, xz / 31.0 - _CTime * float2(0.03, 0.021), 0).a - 0.5) * 0.5;
        y += cn * 7.0 * churn;
    }
    s.y = y + s.wave;
    // the film is a floor under the sea, the wave and the run-up (a max: their caps stay as they were)
    if (withFilm) SeaFilm(xz, g, wBay, s);
    s.depth = s.y - s.ground;
    return s;
}

SeaInfo SeaEval(float2 xz) { return SeaEvalX(xz, true); }
float SeaHeight(float2 xz) { return SeaEval(xz).y; }

TEXTURE2D(_CWaterNormal);
SAMPLER(sampler_CWaterNormal);

// Small waves: four scrolling layers of a tiling wavelet map (mip-mapped: the far sea goes smooth instead of
// aliasing, and no regular lattice can appear). Returns the slope and the foam-like ridge value of the wavelets.
float2 SeaRipples(float2 xz, float dist, float t, float amp, out float ridge)
{
    float4 a = SAMPLE_TEXTURE2D(_CWaterNormal, sampler_CWaterNormal, xz / 263.0 + t * float2(0.004, 0.0027));
    float4 b = SAMPLE_TEXTURE2D(_CWaterNormal, sampler_CWaterNormal, xz.yx / 71.0 + t * float2(-0.011, 0.007));
    float4 c = SAMPLE_TEXTURE2D(_CWaterNormal, sampler_CWaterNormal, xz / 19.0 + t * float2(0.021, -0.016));
    float4 e = SAMPLE_TEXTURE2D(_CWaterNormal, sampler_CWaterNormal, xz.yx / 5.3 + t * float2(0.035, 0.044));
    float nearK = saturate(240.0 / max(dist, 1.0));
    float2 slope = (a.rg - 0.5) * 0.50 + (b.gr - 0.5) * 0.55 + (c.rg - 0.5) * 0.45 + (e.gr - 0.5) * 0.35 * nearK;
    ridge = b.b * 0.5 + c.b * 0.5;
    return slope * amp * 0.5;
}

#endif
