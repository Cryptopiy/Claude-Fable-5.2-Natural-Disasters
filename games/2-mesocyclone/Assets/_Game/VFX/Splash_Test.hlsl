/// Hidden
float SplashHash(uint n)
{
    n = (n ^ 61u) ^ (n >> 16); n *= 9u; n = n ^ (n >> 4); n *= 0x27d4eb2du; n = n ^ (n >> 15);
    return (float)(n & 0x00ffffffu) / 16777216.0;
}
/// Hidden
float SplashCone(float3 p, float3 lp, float3 ld)
{
    float3 v = p - lp;
    float d = length(v);
    float c = dot(v / max(d, 0.01), ld);
    return smoothstep(0.83, 0.97, c) / (1.0 + d * d * 0.012);
}
/// Hidden
float SplashHoodY(float x, float z)
{
    float t = saturate((z - 0.84) / 1.94);
    return lerp(1.15, 1.06, t * t * 0.6 + t * 0.4) + 0.035 * (1.0 - (x / 0.86) * (x / 0.86));
}
void SplashParticle(inout VFXAttributes attributes, in float4 S0, in float4 S1, in float4 S2, in float4 CamPos, in float4 HeadL, in float4 HeadR, in float4 HeadDir, in float4 HeadCol, in float4 Flash, in float4 Amb)
{
    uint id = attributes.particleId;
    float T = S0.x;
    float speed = S0.w;
    float3 p = float3(0.0, -50.0, 0.0);
    float3 vel = float3(0.0, 1.0, 0.0);
    float sz = 0.002;
    float vis = 0.0;
    float gain = 1.0;
    float sheet = 0.0;
    float sheetW = 0.05;
    float sheetL = 0.2;
    if (id < 2800u)
    {
        uint ev = id / 4u;
        uint dr = id - ev * 4u;
        float e0 = SplashHash(ev * 11u + 1u);
        float e1 = SplashHash(ev * 11u + 2u);
        float e2 = SplashHash(ev * 11u + 3u);
        float per = 0.35 + 0.3 * e0;
        float cyc = T / per + e1;
        float ph = frac(cyc);
        uint c = (uint)floor(cyc);
        uint k = ev * 7919u + c * 104729u;
        float r1 = SplashHash(k + 1u);
        float r2 = SplashHash(k + 2u);
        float r3 = SplashHash(k + 3u);
        float r4 = SplashHash(k * 3u + dr * 17u + 5u);
        float r5 = SplashHash(k * 3u + dr * 17u + 6u);
        float tau = ph * per;
        float life = 0.16 + 0.08 * e2;
        float onHood = step(e2, 0.16);
        float a = ((float)dr + 0.2 + 0.6 * r3) * 1.5708 + r4 * 0.6;
        float vh = (0.45 + 0.8 * r5) * lerp(1.0, 0.7, onHood);
        float vy = (0.9 + 1.0 * r4) * lerp(1.0, 0.6, onHood);
        float3 v0 = float3(cos(a) * vh, vy, sin(a) * vh);
        float3 e = float3(0.0, 0.004, 10.0);
        if (onHood > 0.5)
        {
            float hx = (r1 - 0.5) * 1.6;
            float hz = 0.95 + r2 * 1.75;
            e = float3(hx, SplashHoodY(hx, hz) + 0.004, hz);
            v0.z -= S2.w;
        }
        else
        {
            float z = 4.0 + r2 * r2 * 30.0;
            float hw = 1.6 + z * 0.5;
            e = float3((r1 - 0.5) * 2.0 * hw, 0.004, z);
            v0.z -= speed;
        }
        p = e + v0 * tau + float3(0.0, -4.9 * tau * tau, 0.0);
        vel = v0 + float3(0.0, -9.8 * tau, 0.0);
        sz = (0.0018 + 0.0018 * r5) * S2.z;
        float on = step(SplashHash(ev * 11u + 4u), S0.y);
        vis = on * step(tau, life) * (1.0 - tau / life) * step(e.y - 0.01, p.y);
        gain = 1.4;
    }
    else if (id < 4800u)
    {
        uint lid = id - 2800u;
        uint j = lid / 4u;
        uint m = lid - j * 4u;
        float u0 = SplashHash(j * 7u + 1u);
        float u1 = SplashHash(j * 7u + 2u);
        float u2 = SplashHash(j * 7u + 3u);
        float u3 = SplashHash(j * 7u + 4u);
        float u4 = SplashHash(j * 7u + 5u);
        float u5 = SplashHash(j * 7u + 6u);
        float per = 0.8 + 0.7 * u0;
        float cyc = T / per + u1;
        float ph = frac(cyc);
        float tau = (ph - 0.5) * per;
        float hood = step(u2, 0.14);
        float hx = (u3 - 0.5) * 1.62;
        float hz = 0.92 + u4 * 1.8;
        float3 ip = lerp(float3((u3 - 0.5) * 20.0, 0.004, 3.2 + u4 * u4 * 46.0), float3(hx, SplashHoodY(hx, hz) + 0.004, hz), hood);
        float d = S1.x * (0.55 + 0.9 * u5 * u5);
        float big = saturate(sqrt(d / 0.05));
        uint c = (uint)floor(cyc);
        float r1 = SplashHash(j * 131u + c * 7919u + m * 13u + 1u);
        float r2 = SplashHash(j * 131u + c * 7919u + m * 13u + 2u);
        float r3 = SplashHash(j * 131u + c * 7919u + m * 13u + 3u);
        float a = ((float)m + r1) * 6.2832 / (float)(4u);
        float vh = (0.7 + 1.6 * r2) * lerp(0.6, 1.5, big) * lerp(1.0, 0.8, hood);
        float vy = (1.1 + 1.9 * r3) * lerp(0.6, 1.4, big) * lerp(1.0, 0.55, hood);
        float3 v0 = float3(cos(a) * vh + S1.y * 0.3, vy, sin(a) * vh - lerp(speed, S2.w, hood));
        float life = 0.3;
        sz = (0.002 + 0.003 * r1) * lerp(0.7, 1.6, big) * S2.z;
        gain = lerp(1.3, 0.9, hood);
        if (m == 0u)
        {
            v0 = float3(S1.y * 0.3, 0.6 * big, -lerp(speed, S2.w, hood));
            sz = (0.04 + 0.16 * big) * (0.5 + max(tau, 0.0) * 4.0) * S2.z;
            life = 0.38;
            gain = 0.14 * big * step(0.035, d) * step(r2, 0.5);
        }
        p = ip + v0 * tau + float3(0.0, -4.9 * tau * tau, 0.0);
        vel = v0 + float3(0.0, -9.8 * tau, 0.0);
        float on = step(u0 * 0.97 + u2 * 0.03, S0.z) * step(0.0, tau) * step(tau, life);
        float wet = lerp(0.35, 1.0, S1.w);
        vis = on * (1.0 - saturate(tau / life)) * wet * step(ip.y - 0.02, p.y) * step(0.012, d);
    }
    else
    {
        uint k = id - 4800u;
        float h0 = SplashHash(k * 5u + 901u);
        float h1 = SplashHash(k * 5u + 902u);
        float h2 = SplashHash(k * 5u + 903u);
        float h3 = SplashHash(k * 5u + 904u);
        float per = 1.4 + 1.8 * h1;
        float cyc = T / per + h2;
        float ph = frac(cyc);
        uint c = (uint)floor(cyc);
        float jx = SplashHash(k * 977u + c * 7919u + 1u);
        float x = clamp((h0 - 0.5) * 1.5 + (jx - 0.5) * 0.2, -0.8, 0.8) + sin(ph * 6.0 + h3 * 6.28) * 0.03;
        float z = lerp(2.2 + 0.5 * jx, 0.9, ph);
        p = float3(x, SplashHoodY(x, z) + 0.003, z);
        vel = float3(0.0, 0.0, -1.0);
        sheet = 1.0;
        sheetW = 0.03 + 0.05 * h3;
        sheetL = 0.12 + 0.25 * h0;
        sz = sheetW;
        vis = step(h3, S2.y) * sin(ph * 3.14159) * 0.3;
    }
    float3 toCam = CamPos.xyz - p;
    float dist = length(toCam);
    float cone = SplashCone(p, HeadL.xyz, HeadDir.xyz) + SplashCone(p, HeadR.xyz, HeadDir.xyz);
    float3 light = Amb.rgb * 0.6 + Flash.rgb * 0.8 + HeadCol.rgb * cone * (1.0 - sheet);
    float spd = length(vel);
    float3 dir = vel / max(spd, 0.001);
    float3 ax = normalize(cross(dir, toCam / max(dist, 0.01)) + float3(0.00001, 0.0, 0.0));
    float3 ay = dir;
    float w = max(sz, dist * 0.0016);
    float len = w + spd * 0.0025;
    if (sheet > 0.5)
    {
        ax = float3(1.0, 0.0, 0.0);
        ay = normalize(float3(0.0, 0.05, -1.0));
        w = sheetW;
        len = sheetL;
    }
    attributes.position = p;
    attributes.axisX = ax;
    attributes.axisY = ay;
    attributes.axisZ = cross(ax, ay);
    attributes.size = 1.0;
    attributes.scaleX = w;
    attributes.scaleY = len;
    attributes.color = light * gain;
    attributes.alpha = S2.x * vis * saturate(sz / w * 2.5) * smoothstep(0.3, 0.7, dist);
}
