/// Hidden
float HailHash(uint n)
{
    n = (n ^ 61u) ^ (n >> 16); n *= 9u; n = n ^ (n >> 4); n *= 0x27d4eb2du; n = n ^ (n >> 15);
    return (float)(n & 0x00ffffffu) / 16777216.0;
}
/// Hidden
float HailCone(float3 p, float3 lp, float3 ld)
{
    float3 v = p - lp;
    float d = length(v);
    float c = dot(v / max(d, 0.01), ld);
    return smoothstep(0.83, 0.97, c) / (1.0 + d * d * 0.012);
}
void HailParticle(inout VFXAttributes attributes, in float4 H0, in float4 H1, in float4 CamPos, in float4 HeadL, in float4 HeadR, in float4 HeadDir, in float4 HeadCol, in float4 Flash, in float4 Amb)
{
    uint id = attributes.particleId;
    float u0 = HailHash(id * 7u + 1u);
    float u1 = HailHash(id * 7u + 2u);
    float u2 = HailHash(id * 7u + 3u);
    float u3 = HailHash(id * 7u + 4u);
    float u4 = HailHash(id * 7u + 5u);
    float u5 = HailHash(id * 7u + 6u);
    float period = 0.8 + 0.7 * u0;
    float ph = frac(H0.x / period + u1);
    float hood = step(u2, 0.14);
    float hx = (u3 - 0.5) * 1.62;
    float hz = 0.92 + u4 * 1.8;
    float hoodY = lerp(1.15, 1.06, saturate((hz - 0.84) / 1.94)) + 0.035 * (1.0 - (hx / 0.86) * (hx / 0.86));
    float3 ipHood = float3(hx, hoodY, hz);
    float3 ipRoad = float3((u3 - 0.5) * 20.0, 0.0, 3.2 + u4 * u4 * 46.0);
    float3 ip = lerp(ipRoad, ipHood, hood);
    float3 vFall = float3(H1.x, -27.0 - 9.0 * u5, -H0.w + H1.z);
    float fallT = 0.5;
    float3 p = ip;
    float3 vel = vFall;
    float alive = 1.0;
    if (ph < fallT)
    {
        float tt = (fallT - ph) * period;
        p = ip - vFall * tt;
    }
    else
    {
        float tt = (ph - fallT) * period;
        float3 vbRoad = float3((u1 - 0.5) * 7.0, 3.5 + 5.5 * u0, -H0.w * 0.8);
        float3 vbHood = float3((u1 - 0.5) * 5.0, 2.5 + 3.5 * u0, -2.0 - 6.0 * u5);
        float3 vb = lerp(vbRoad, vbHood, hood);
        p = ip + vb * tt + float3(0.0, -4.9 * tt * tt, 0.0);
        vel = vb + float3(0.0, -9.8 * tt, 0.0);
        alive = step(ip.y - 0.02, p.y) * lerp(step(3.0, p.z), step(0.86, p.z), hood);
    }
    float3 toCam = CamPos.xyz - p;
    float dist = length(toCam);
    float d = H0.z * (0.55 + 0.9 * u5 * u5);
    float sz = max(d, dist * 0.0014);
    float speed = length(vel);
    float3 dir = vel / max(speed, 0.01);
    float3 ax = normalize(cross(dir, toCam / max(dist, 0.01)) + float3(0.0001, 0.0, 0.0));
    float len = sz + speed * 0.006;
    float on = step(u0 * 0.97 + u2 * 0.03, H0.y) * alive * smoothstep(0.35, 0.8, dist);
    float3 light = Amb.rgb + Flash.rgb + HeadCol.rgb * (HailCone(p, HeadL.xyz, HeadDir.xyz) + HailCone(p, HeadR.xyz, HeadDir.xyz));
    attributes.position = p;
    attributes.axisX = ax;
    attributes.axisY = dir;
    attributes.axisZ = cross(ax, dir);
    attributes.size = 1.0;
    attributes.scaleX = sz;
    attributes.scaleY = len;
    attributes.color = light;
    attributes.alpha = saturate(d / sz * 1.5) * sqrt(sz / len) * on;
}
