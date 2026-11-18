using System;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.VFX;

namespace MESOCYCLONE.EditorTools
{
    /// <summary>Rain streaks, splashes and (legacy) sprite hail around the truck as stateless VFX Graph systems
    /// (truck-local space). The rain wraps inside a box that travels with the truck; splashes are closed-form events:
    /// rain drops landing on the road ahead and on the hood, water crowns under every 3D hail stone of HailField (same
    /// hashes, so each crown sits under its stone), and thin water sheets running back over the hood. Lit by the
    /// headlight cones (the rain with the low-beam cutoff), the lightning and the twilight, all passed in as vectors.
    /// Also finishes the ice material (clear coat) after AssetGen has built it.</summary>
    public static class VfxGenWeather
    {
        const string Dir = "Assets/_Game/VFX";

        const string RainHlsl = @"/// Hidden
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
    float kick = clamp((n.x - {KICK0}) * {KICKK}, 0.0, {KICKMAX});
    float above = smoothstep({CUTLO} + kick, {CUTHI} + kick, n.y);
    return (body + core) * lerp(1.0, {STRAY}, above);
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
";

        const string HailHlsl = @"/// Hidden
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
";

        // Splash: three classes of particle by id range. {NRD} rain droplets (4 per splash event), then {DH} droplets
        // per hail stone up to {NHD}, then the hood sheets. Constants are baked per asset (test / final counts).
        const string SplashHlsl = @"/// Hidden
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
    if (id < {NRD})
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
    else if (id < {NHD})
    {
        uint lid = id - {NRD};
        uint j = lid / {DH};
        uint m = lid - j * {DH};
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
        float a = ((float)m + r1) * 6.2832 / (float)({DH});
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
        uint k = id - {NHD};
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
";

        // The rain streaks get the same low-beam cutoff as the fog (TruckLights.BuildCookie): without it the symmetric
        // ~34-degree RainCone lights the streaks up to ~30 degrees above horizontal, a speckled dome over the road,
        // where a real low beam gives a flat-topped band that ends at the horizon. The numbers are TruckLights' code
        // defaults (not tune.txt: a release build ignores tune.txt, and the rain must match what it ships with), turned
        // into sine units of the truck-local direction from each lamp (n.y = sin elevation, n.x ~ sin azimuth). At these
        // few degrees a sine is proportional to its angle to within 0.1 %, so the kick-up slope carries over as it is.
        // Hail and splashes keep their full cone on purpose: hail matches the lit 3D stones, splashes sit low.
        static string RainSource()
        {
            CultureInfo ci = CultureInfo.InvariantCulture;
            string S(float deg) => Mathf.Sin(deg * Mathf.Deg2Rad).ToString("0.0000000", ci);
            return RainHlsl
                .Replace("{KICK0}", S(TruckLights.KickStartDeg))
                .Replace("{KICKK}", TruckLights.KickSlope.ToString("0.0000000", ci))
                .Replace("{KICKMAX}", S(TruckLights.KickMaxDeg))
                .Replace("{CUTLO}", S(TruckLights.CutoffDeg - TruckLights.CutoffSoftDeg))
                .Replace("{CUTHI}", S(TruckLights.CutoffDeg + TruckLights.CutoffSoftDeg))
                .Replace("{STRAY}", TruckLights.Stray.ToString("0.0000000", ci));
        }

        // splash event and particle counts per profile (the hail part follows HailField's stone counts)
        public static int SplashRainEvents(bool final) => final ? 1400 : 700;
        public static int SplashDropsPerStone(bool final) => final ? 6 : 4;
        public static int SplashSheets(bool final) => final ? 260 : 120;

        static int SplashCapacity(bool final)
        {
            int stones = final ? HailField.CountFinal : HailField.CountTest;
            return SplashRainEvents(final) * 4 + stones * SplashDropsPerStone(final) + SplashSheets(final);
        }

        static string SplashSource(bool final)
        {
            int nrd = SplashRainEvents(final) * 4;
            int nhd = nrd + (final ? HailField.CountFinal : HailField.CountTest) * SplashDropsPerStone(final);
            return SplashHlsl.Replace("{NRD}", nrd + "u").Replace("{NHD}", nhd + "u").Replace("{DH}", SplashDropsPerStone(final) + "u");
        }

        static (string name, Type type, object def)[] Exposed(params string[] names)
        {
            var e = new (string name, Type type, object def)[names.Length];
            for (int i = 0; i < names.Length; i++) e[i] = (names[i], typeof(Vector4), (object)Vector4.zero);
            return e;
        }

        public static void Run()
        {
            Texture2D streak = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetGen.Gen + "/Textures/streak.asset");
            Texture2D stone = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetGen.Gen + "/Textures/stone.asset");
            Texture2D drop = DropTexture();
            var rainP = Exposed("R0", "R1", "R2", "CamPos", "HeadL", "HeadR", "HeadDir", "HeadCol", "Flash", "Amb");
            var hailP = Exposed("H0", "H1", "CamPos", "HeadL", "HeadR", "HeadDir", "HeadCol", "Flash", "Amb");
            var splashP = Exposed("S0", "S1", "S2", "CamPos", "HeadL", "HeadR", "HeadDir", "HeadCol", "Flash", "Amb");
            var near = new Bounds(new Vector3(0f, 6f, 20f), new Vector3(120f, 60f, 160f));
            var splashBounds = new Bounds(new Vector3(0f, 3f, 20f), new Vector3(60f, 20f, 84f));
            string rainSrc = RainSource();
            Build("Rain_Test", 12000, rainSrc, "RainParticle", rainP, streak, VfxBlend.Additive, near);
            Build("Rain_Final", 24000, rainSrc, "RainParticle", rainP, streak, VfxBlend.Additive, near);
            // legacy sprite hail: only spawned with the tune hail.sprites=1 (the hail is HailField's 3D ice now)
            Build("Hail_Test", 800, HailHlsl, "HailParticle", hailP, stone, VfxBlend.Alpha, near);
            Build("Hail_Final", 1600, HailHlsl, "HailParticle", hailP, stone, VfxBlend.Alpha, near);
            Build("Splash_Test", SplashCapacity(false), SplashSource(false), "SplashParticle", splashP, drop, VfxBlend.Additive, splashBounds);
            Build("Splash_Final", SplashCapacity(true), SplashSource(true), "SplashParticle", splashP, drop, VfxBlend.Additive, splashBounds);
            FinishMaterials();
            AssetDatabase.SaveAssets();
        }

        static void Build(string name, int capacity, string hlsl, string fn, (string name, Type type, object def)[] exposed, Texture2D tex,
            VfxBlend blend, Bounds bounds)
        {
            var o = new OutputOptions
            {
                lit = false, blend = blend, sort = VfxSort.Off, primitive = VfxPrimitive.Quad, orient = VfxOrient.None, queue = VfxQueue.Default,
                softParticles = false, useExposureWeight = true, exposureWeight = 1f, mainTexture = tex, worldSpace = false,
                boundsCenter = bounds.center, boundsSize = bounds.size,
                hlslAsIncludeFile = true, blockName = fn, deltaTimeUpdate = true
            };
            try
            {
                VisualEffectAsset a = VfxGraphBuilder.BuildStateless(Dir + "/" + name + ".vfx", capacity, hlsl, exposed, o);
                Debug.Log("[VfxGen] " + name + (a != null ? " ok, capacity " + capacity : " FAILED"));
            }
            catch (Exception e) { Debug.LogError("[VfxGen] " + name + " exception: " + e); }
        }

        /// <summary>A soft round droplet with a small bright core (a drop shows the light source as a point).
        /// Built here because only the splash VFX uses it; re-saved in place so the GUID stays.</summary>
        static Texture2D DropTexture()
        {
            const int n = 64;
            string path = AssetGen.Gen + "/Textures/splash_drop.asset";
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true, false)
            {
                name = "splash_drop", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear
            };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = ((x + 0.5f) / n - 0.5f) * 2f, v = ((y + 0.5f) / n - 0.5f) * 2f;
                    float r2 = u * u + v * v;
                    float edge = Mathf.Clamp01((1f - Mathf.Sqrt(r2)) * 4f);
                    float c = Mathf.Clamp01((Mathf.Exp(-r2 * 5.5f) * 0.75f + Mathf.Exp(-r2 * 40f) * 0.6f) * edge);
                    px[y * n + x] = new Color(c, c, c, c);
                }
            tex.SetPixels32(px);
            tex.Apply(true, false);
            var old = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (old != null)
            {
                EditorUtility.CopySerialized(tex, old);
                UnityEngine.Object.DestroyImmediate(tex);
                EditorUtility.SetDirty(old);
                return old;
            }
            AssetDatabase.CreateAsset(tex, path);
            return tex;
        }

        /// <summary>Layered ice: a glassy clear coat over the frosted body of hail_ice. AssetGen has no coat setting, so it
        /// is set here on the generated material; ValidateMaterial turns on _MATERIAL_FEATURE_CLEAR_COAT, which puts the
        /// variant in the build. AssetGen leaves _CoatMask alone on later runs.</summary>
        static void FinishMaterials()
        {
            string path = AssetGen.Gen + "/Materials/" + WeatherLibrary.IceMat + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                Debug.LogWarning("[VfxGenWeather] " + path + " not found: is WeatherLibrary registered in Library.Materials()?");
                return;
            }
            m.SetFloat("_CoatMask", 1f);
            HDMaterial.ValidateMaterial(m);
            EditorUtility.SetDirty(m);
        }
    }
}
