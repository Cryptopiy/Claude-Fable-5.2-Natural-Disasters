/// Hidden
float TorHash(uint n)
{
    n = (n ^ 61u) ^ (n >> 16); n *= 9u; n = n ^ (n >> 4); n *= 0x27d4eb2du; n = n ^ (n >> 15);
    return (float)(n & 0x00ffffffu) / 16777216.0;
}
/// Hidden
float TorRadius(float h, float rG, float morph)
{
    float rTop = max(rG * lerp(2.6, 1.55, morph), lerp(260.0, 1150.0, morph));
    float e = lerp(6.0, 1.3, smoothstep(0.0, 0.22, morph));
    e = lerp(e, 1.0, smoothstep(0.7, 1.0, morph));
    float hs = saturate(h);
    return rG + (rTop - rG) * pow(hs, e) + morph * (250.0 + 0.2 * rG) * smoothstep(0.45, 1.0, hs);
}
/// Hidden
float3 TorGlow(float3 p, float4 lp, float4 lc)
{
    float3 v = lp.xyz - p;
    float r = length(v);
    float q = r / (0.28 * lp.w);
    float2 n = normalize(p.xz + float2(0.0001, 0.0));
    float2 l = normalize(v.xz + float2(0.0001, 0.0));
    float face = saturate(dot(n, l) * 0.9 + 0.02) * 1.3 + 0.012;
    return lc.rgb * face * exp(-2.2 * r / lp.w) / (1.0 + q * q);
}
void TornadoParticle(inout VFXAttributes attributes, in float4 Tor0, in float4 Tor1, in float4 Tor2, in float4 CamRel, in float4 F0Pos, in float4 F0Col, in float4 F1Pos, in float4 F1Col, in float4 GPos, in float4 GCol, in float4 Amb)
{
    uint id = attributes.particleId;
    float u0 = TorHash(id * 7u + 1u);
    float u1 = TorHash(id * 7u + 2u);
    float u2 = TorHash(id * 7u + 3u);
    float u3 = TorHash(id * 7u + 4u);
    float u4 = TorHash(id * 7u + 5u);
    float cls = TorHash(id * 7u + 6u);
    float rG = Tor0.x;
    float desc = Tor0.y;
    float morph = Tor0.z;
    float rope = Tor0.w;
    float debris = Tor1.x;
    float H = Tor1.y;
    float t = Tor2.x;
    float3 p = float3(0.0, 0.0, 0.0);
    float sz = 0.0;
    float al = 0.0;
    float hide = 1.0;
    float3 albedo = float3(0.42, 0.42, 0.44);
    float2 toCam = normalize(CamRel.xz + float2(0.0001, 0.0));
    if (Tor2.w < 0.5)
    {
        if (cls < 0.7)
        {
            float hh = frac(u1 + t * (0.003 + 0.006 * u2));
            float y = hh * H;
            float tipY = H * (1.0 - desc);
            float R = TorRadius(hh, rG, morph) * saturate((y - tipY) / (H * 0.14) + 0.02);
            float ang = u0 * 6.2831853 + Tor1.z * lerp(1.5, 0.7, u1);
            float rr = R * (0.92 + 0.2 * u3);
            float2 bend = rope * float2(sin(hh * 5.1 + t * 0.35), cos(hh * 3.7 - t * 0.27)) * (130.0 + 240.0 * (1.0 - hh)) * (1.0 - hh * hh);
            bend += rope * float2(0.92, 0.4) * 800.0 * (1.0 - hh) * (1.0 - hh);
            p = float3(cos(ang) * rr + bend.x, y, sin(ang) * rr + bend.y);
            sz = clamp(0.2 * R + 22.0, 18.0, 340.0) * (0.6 + 0.8 * u4);
            al = 0.11 * step(tipY, y) * saturate(R / 12.0);
            hide = smoothstep(-0.3, 0.15, dot(float2(cos(ang), sin(ang)), toCam));
        }
        else
        {
            float dome = 1.0 - saturate(CamRel.w);
            float rho = rG + (1.7 * rG * u1 + 120.0) * lerp(0.75, 1.0, dome);
            float y = 25.0 + (150.0 + 0.3 * rG) * u2 * (1.0 - 0.7 * u1) * dome;
            float ang = u0 * 6.2831853 + Tor1.w * 0.45 * (0.6 + 0.8 * u3) / (1.0 + 1.7 * u1);
            p = float3(cos(ang) * rho, y, sin(ang) * rho);
            sz = 130.0 + 260.0 * u4;
            al = 0.05 * debris * step(0.999, desc) * (1.0 - 0.5 * u1);
            albedo = float3(0.34, 0.28, 0.21);
            float along = dot(p.xz, -toCam);
            float lateral = abs(p.x * toCam.y - p.z * toCam.x);
            hide = 1.0 - step(0.0, along) * step(lateral, rG * 0.9);
        }
    }
    else
    {
        float rho = rG * (0.72 + 0.95 * u1 * u1) + 60.0;
        float y = u2 * u2 * (300.0 + 0.3 * rG) + 4.0;
        float lofted = step(0.84, u4);
        rho = lerp(rho, rG * (1.0 + 0.9 * u1) + 150.0, lofted);
        y = lerp(y, 150.0 + u2 * 0.75 * H, lofted);
        float ang = u0 * 6.2831853 + Tor1.w * (0.55 + 0.9 * u3) / sqrt(0.72 + 0.95 * u1 * u1);
        y += 22.0 * sin(t * (0.4 + u4) + u0 * 40.0);
        p = float3(cos(ang) * rho, max(y, 2.0), sin(ang) * rho);
        sz = (2.5 + 11.0 * u4 * u4 * u4) * (1.0 + step(0.992, u3) * 3.0);
        al = 0.92 * debris * step(0.999, desc);
        albedo = float3(0.10, 0.085, 0.07);
        float along = dot(p.xz, -toCam);
        float lateral = abs(p.x * toCam.y - p.z * toCam.x);
        hide = 1.0 - step(0.0, along) * step(lateral, TorRadius(saturate(y / H), rG, morph) * 0.95);
    }
    float3 light = Amb.rgb + TorGlow(p, F0Pos, F0Col) + TorGlow(p, F1Pos, F1Col) + TorGlow(p, GPos, GCol);
    float on = step(frac(u0 * 13.7 + u1 * 3.1), Tor2.z);
    attributes.position = p;
    attributes.size = sz * on;
    attributes.angleZ = u3 * 360.0 + t * (u4 - 0.5) * 6.0;
    attributes.color = albedo * light;
    attributes.alpha = al * hide * Tor2.y * on;
}
