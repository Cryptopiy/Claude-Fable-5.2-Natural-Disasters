// Ground-hugging clouds on the volcano: pyroclastic currents and the dust of the collapse, as a billowing blanket
// that rides on the cone's height function. Included once per pass after VaryingMesh.hlsl.
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/BuiltinUtilities.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/MaterialUtilities.hlsl"

float4 _BlTongue[6];    // x azimuth (rad), y seconds since it started (<= 0 inactive), z angular half width (rad), w thickness scale
float4 _BlDust;         // x seconds since the collapse began, y strength 0..1, z slide distance (m), w fade 0..1
float4 _BlMisc;         // x front speed (m/s), y base thickness (m), z ember glow 0..1, w noise clock

struct Blanket { float thick; float pdc; float dust; float head; float steam; };

Blanket BlanketEval(float2 xz)
{
    Blanket b;
    b.thick = 0.0; b.pdc = 0.0; b.dust = 0.0; b.head = 0.0; b.steam = 0.0;
    float2 d = xz - _CCone.xz;
    float rho = length(d);
    if (rho > 3050.0 || rho < 40.0) return b;
    float phi = atan2(d.y, d.x);
    float t = _BlMisc.w;
    // billows: advected down the slope (radially outward), two phases so that nothing stretches
    float2 dir = d / rho;
    const float T = 24.0;
    float ph0 = frac(t / T), ph1 = frac(t / T + 0.5), w = abs(ph0 * 2.0 - 1.0);
    float2 adv = dir * _BlMisc.x * 0.55;
    float n0 = CNoise2All((xz - adv * T * (ph0 - 0.5)) / 1700.0).r;
    float n1 = CNoise2All((xz - adv * T * (ph1 - 0.5)) / 1700.0 + 0.37).r;
    float big = lerp(n0, n1, w);
    float fine = CNoise2All((xz - adv * t * 0.3) / 560.0).g;
    // smooth, rounded lumps (the polar grid cannot carry sharp detail without spiking)
    float billow = smoothstep(0.25, 0.8, big) * 0.72 + smoothstep(0.3, 0.75, fine) * 0.28;

    for (int i = 0; i < 6; i++)
    {
        float4 tg = _BlTongue[i];
        if (tg.y <= 0.0) continue;
        float dphi = phi - tg.x;
        dphi -= 6.2831853 * floor(dphi / 6.2831853 + 0.5);
        dphi += 0.10 * sin(rho / 430.0 + tg.x * 7.0);                       // the current meanders in its gully
        float lat = exp(-1.5 * (dphi / tg.z) * (dphi / tg.z));
        float front = 100.0 + _BlMisc.x * tg.y * (1.0 - 0.2 * saturate(rho / 3000.0));
        float behind = front - rho;
        float nose = smoothstep(-140.0, 70.0, behind);
        float head = exp(-pow((behind - 130.0) / 280.0, 2.0));
        float body = (0.42 + 0.95 * head) * exp(-max(behind, 0.0) / (1500.0 + 25.0 * tg.y));
        float sea = 1.0 - 0.5 * smoothstep(CONE_R, CONE_R + 800.0, rho);
        float fade = 1.0 - smoothstep(70.0, 125.0, tg.y);
        float th = tg.w * _BlMisc.y * lat * nose * body * sea * fade * smoothstep(50.0, 260.0, rho);
        b.thick = max(b.thick, th);
        b.pdc = max(b.pdc, saturate(th / 30.0));
        b.head = max(b.head, head * nose * lat * fade);
        b.steam = max(b.steam, saturate(th / 20.0) * smoothstep(CONE_R - 160.0, CONE_R + 60.0, rho));
    }
    // dust of the collapse: rolls ahead of the sliding mass, then hangs and thins
    if (_BlDust.y > 0.001)
    {
        float2 uv = ConeSlideUV(d);
        float s = _BlDust.z;
        float wv = 1.1 * (SCAR_R0 + SCAR_SPLAY * max(uv.x - SCAR_UC, 0.0));                     // a little wider than the amphitheatre (CascadeCone.hlsl)
        float lat = exp(-1.2 * (uv.y / wv) * (uv.y / wv));
        float ahead = uv.x - s;
        float env = smoothstep(-1400.0, -200.0, ahead) * (1.0 - smoothstep(350.0, 900.0, ahead)) * smoothstep(-480.0, 100.0, uv.x);   // from the head scarp down
        float roll = CNoise2All((xz - dir * t * 9.0) / 1300.0).b;
        float th = 560.0 * _BlDust.y * lat * env * (0.55 + 0.9 * roll) * (1.0 - 0.5 * smoothstep(CONE_R, CONE_R + 900.0, rho));
        b.dust = saturate(th / 40.0) * (th >= b.thick ? 1.0 : 0.5);
        b.thick = max(b.thick, th);
    }
    b.thick *= 0.55 + 0.95 * billow;
    return b;
}

float BlanketGround(float2 xz) { return max(ConeHeight(xz, 0.0), 0.0); }

void ApplyVertexModification(AttributesMesh input, float3 normalWS, inout float3 positionRWS, float3 timeParameters)
{
    float3 a = GetAbsolutePositionWS(positionRWS);
    Blanket b = BlanketEval(a.xz);
    a.y = BlanketGround(a.xz) + b.thick - 6.0;
    positionRWS = GetCameraRelativePositionWS(a);
}

void GetSurfaceAndBuiltinData(FragInputs input, float3 V, inout PositionInputs posInput, out SurfaceData surfaceData, out BuiltinData builtinData RAY_TRACING_OPTIONAL_PARAMETERS)
{
    ZERO_INITIALIZE(SurfaceData, surfaceData);
    ZERO_BUILTIN_INITIALIZE(builtinData);
    builtinData.opacity = 1.0;
#ifdef VARYINGS_NEED_POSITION_WS
    float3 posAbs = GetAbsolutePositionWS(input.positionRWS);
    Blanket b = BlanketEval(posAbs.xz);
    // soft edge: dithered away where the cloud is thin (TAA resolves the dither)
    float cover = saturate(b.thick / lerp(34.0, 150.0, b.dust * (1.0 - b.pdc))) * (1.0 - 0.999 * _BlDust.w * step(0.5, b.dust));
    float eN = CNoise2All(posAbs.xz / 83.0).r * 0.6 + CNoise2All(posAbs.xz / 23.0).g * 0.4;
    clip(cover - 0.03 - 0.70 * eN - 0.27 * CIgn(posInput.positionSS));
#ifdef BLANKET_SHADING
    float e = 14.0;
    float hx = (BlanketGround(posAbs.xz + float2(e, 0)) + BlanketEval(posAbs.xz + float2(e, 0)).thick) - (BlanketGround(posAbs.xz - float2(e, 0)) + BlanketEval(posAbs.xz - float2(e, 0)).thick);
    float hz = (BlanketGround(posAbs.xz + float2(0, e)) + BlanketEval(posAbs.xz + float2(0, e)).thick) - (BlanketGround(posAbs.xz - float2(0, e)) + BlanketEval(posAbs.xz - float2(0, e)).thick);
    float3 N = normalize(float3(-hx, 2.0 * e, -hz));
    float4 n4 = CNoise2All(posAbs.xz / 230.0);
    float crease = saturate(1.0 - N.y * N.y * 1.6 + (0.5 - n4.b) * 0.5);                 // valleys between billows
    float3 alb = lerp(float3(0.17, 0.155, 0.145), float3(0.62, 0.56, 0.50), b.dust * (1.0 - b.pdc));
    alb = lerp(alb, float3(0.80, 0.79, 0.76), b.steam * saturate(n4.g * 1.6 - 0.2));     // steam where it meets the sea
    float3 L = _CSunDir.xyz;
    float wrap = saturate(dot(N, L) * 0.6 + 0.4);
    float soft = b.dust * (1.0 - b.pdc);                                                  // dust: flat, pale, mostly sky-lit
    wrap = lerp(wrap, 0.55, soft * 0.8);
    crease *= 1.0 - 0.7 * soft;
    float3 col = alb * (_CSunColor.rgb * wrap * (1.0 / PI) + CAmbient(N) * (0.45 + 0.55 * (1.0 - crease)) + _CSkyHorizon.rgb * 0.25 * soft);
    col += alb * _PalEmber.rgb * _BlMisc.z * 1100.0 * (0.35 + 0.65 * b.pdc) * (1.0 - crease * 0.5);       // lit from within and by the lava
    // ember glow only in the gaps between billows, strongest behind the nose
    float glow = _BlMisc.z * b.pdc * (1.0 - b.steam) * pow(crease, 2.2) * (0.25 + 0.75 * b.head);
    col += _PalEmber.rgb * glow * 26000.0;
    builtinData.emissiveColor = col;
#endif
#endif
}
