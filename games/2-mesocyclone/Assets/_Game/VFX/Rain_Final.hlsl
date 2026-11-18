/// Hidden
float RainHash(uint n)
{
    n = (n ^ 61u) ^ (n >> 16); n *= 9u; n = n ^ (n >> 4); n *= 0x27d4eb2du; n = n ^ (n >> 15);
    return (float)(n & 0x00ffffffu) / 16777216.0;
}
/// Hidden
float RainCone(float3 p, float3 lp, float3 ld)
{
    float3 v = p - lp;
    float d = length(v);
    float3 n = v / max(d, 0.01);
    float c = dot(n, ld);
    float body = smoothstep(0.83, 0.97, c) / (1.0 + d * d * 0.012);
    float core = smoothstep(0.95, 0.995, c) * 1.6 / (1.0 + d * d * 0.06);
    float kick = clamp((n.x - 0.0261770) * 0.2679000, 0.0, 0.0453630);
    float above = smoothstep(-0.0157073 + kick, -0.0052360 + kick, n.y);
    return (body + core) * lerp(1.0, 0.0500000, above);
}
void RainParticle(inout VFXAttributes attributes, in float4 R0, in float4 R1, in float4 R2, in float4 CamPos, in float4 HeadL, in float4 HeadR, in float4 HeadDir, in float4 HeadCol, in float4 Flash, in float4 Amb)
{
    uint id = attributes.particleId;
    float h0 = RainHash(id * 5u + 1u);
    float h1 = RainHash(id * 5u + 2u);
    float h2 = RainHash(id * 5u + 3u);
    float h3 = RainHash(id * 5u + 4u);
    float h4 = RainHash(id * 5u + 5u);
    float3 box = R2.xyz;
    float3 vel = R0.xyz * (0.85 + 0.3 * h3);
    float3 q = float3(h0, h1, h2) * box + vel * R0.w;
    q = q - box * floor(q / box);
    float3 p = q + float3(-0.5 * box.x, -0.6, -0.22 * box.z);
    float speed = length(vel);
    float3 dir = vel / max(speed, 0.01);
    float3 toCam = CamPos.xyz - p;
    float dist = length(toCam);
    float3 ax = normalize(cross(dir, toCam / max(dist, 0.01)));
    float wReal = 0.0012 + 0.0024 * h4;
    float w = max(wReal, dist * 0.0017);
    float inCab = step(abs(p.x), 1.15) * step(0.2, p.y) * step(p.y, 2.2) * step(-3.3, p.z) * step(p.z, 3.1);
    float on = step(h3, R1.x) * (1.0 - inCab) * step(0.0, p.y) * smoothstep(0.5, 1.4, dist);
    float cone = RainCone(p, HeadL.xyz, HeadDir.xyz) + RainCone(p, HeadR.xyz, HeadDir.xyz);
    float3 light = Amb.rgb + Flash.rgb + HeadCol.rgb * cone * (0.7 + 0.6 * h4);
    attributes.position = p;
    attributes.axisX = ax;
    attributes.axisY = dir;
    attributes.axisZ = cross(ax, dir);
    attributes.size = 1.0;
    attributes.scaleX = w;
    attributes.scaleY = speed * R1.y * (0.75 + 0.5 * h4) + w * 2.0;
    attributes.color = light;
    attributes.alpha = R1.z * saturate(wReal / w * 4.5) * on;
}
