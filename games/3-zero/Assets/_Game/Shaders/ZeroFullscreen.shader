Shader "Hidden/Zero/Fullscreen"
{
    // The lens of the film camera, between the scene and Unity's post-processing (ZeroPostFeature, Render Graph):
    //  - light shafts from the burning body and from the explosion through everything that stands in front of them
    //    (screen-space radial scattering at quarter resolution, two passes, jittered),
    //  - the shock front seen from inside (for a second the picture bends as the camera passes through it).
    // Passes: 0 composite, 1 shaft source, 2-3 radial blur.
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
        float4 _ZPostParams;    // x = shock strength 0..1, y = signed phase (-1 before .. +1 after), z = real time
        float4 _ZPostDir;
        float4 _ZRayLight;      // xy = light position (viewport uv), z = shaft strength, w = source radius (in screen heights)
        float4 _ZRayTint;       // rgb = shaft tint
        float4 _ZLens;          // x = shaft threshold
        float4 _ZPostTexel;     // xy = 1 / full size, zw = 1 / quarter size
        TEXTURE2D_X(_ZRaysTex);

        float3 Src(float2 uv) { return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0).rgb; }
        float Luma(float3 c) { return dot(c, float3(0.2126, 0.7152, 0.0722)); }
        // four bilinear taps = a 4 x 4 box of the full-resolution picture
        float3 Down4(float2 uv)
        {
            float2 o = _ZPostTexel.xy;
            return (Src(uv + float2(-o.x, -o.y)) + Src(uv + float2(o.x, -o.y)) + Src(uv + float2(-o.x, o.y)) + Src(uv + float2(o.x, o.y))) * 0.25;
        }
        float IGN(float2 px) { return frac(52.9829189 * frac(dot(px, float2(0.06711056, 0.00583715)))); }
        // march from this pixel toward the light: whatever bright and unblocked lies on the way scatters into the pixel
        float3 Radial(float2 uv, float len, float2 px)
        {
            float2 d = (_ZRayLight.xy - uv) * (len / 24.0);
            float2 p = uv + d * IGN(px);
            float3 s = 0.0; float w = 1.0, ws = 0.0;
            [unroll] for (int k = 0; k < 24; k++)
            {
                s += Src(p) * w; ws += w;
                w *= 0.96; p += d;
            }
            return s / ws;
        }
        float Hash(float2 p) { float3 p3 = frac(float3(p.xyx) * 0.1031); p3 += dot(p3, p3.yzx + 33.33); return frac((p3.x + p3.y) * p3.z); }
        float Noise(float2 p)
        {
            float2 i = floor(p), f = frac(p); f = f * f * (3.0 - 2.0 * f);
            return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), f.x), f.y);
        }
        ENDHLSL

        Pass    // 0: composite
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings i) : SV_Target
            {
                float2 uv = i.texcoord;
                float k = _ZPostParams.x;
                float3 c;
                if (k > 0.001)
                {
                    float ph = _ZPostParams.y, t = _ZPostParams.z;
                    float aspect = _ZPostTexel.y / _ZPostTexel.x;
                    float2 p = (uv - 0.5) * float2(aspect, 1.0);
                    // a band of compression sweeping across the view, plus fine ripples
                    float r = length(p);
                    float band = r * 3.2 - ph * 2.4;
                    float wave = sin(band * 6.2831) * exp(-abs(band) * 1.1);
                    float n1 = Noise(p * 7.0 + t * 0.35) - 0.5, n2 = Noise(p * 7.0 + 31.7 - t * 0.31) - 0.5;
                    float2 dir = normalize(p + 1e-4);
                    float2 off = (dir * wave * 0.045 + float2(n1, n2) * 0.030) * k;
                    float2 uvr = uv + off * float2(1.0 / aspect, 1.0);
                    // slight spectral split: the wall is a lens
                    c.r = Src(uvr + off * 0.10).r;
                    c.g = Src(uvr).g;
                    c.b = Src(uvr - off * 0.10).b;
                    c *= 1.0 - k * 0.22 * smoothstep(0.25, 0.95, r);
                }
                else c = Src(uv);
                float3 rays = SAMPLE_TEXTURE2D_X_LOD(_ZRaysTex, sampler_LinearClamp, uv, 0).rgb;
                // the shafts fade with distance from their source: a corona of rays, never a veil over the frame
                float aspectL = _ZPostTexel.y / _ZPostTexel.x;
                float2 dl = (uv - _ZRayLight.xy) * float2(aspectL, 1.0);
                c += rays * _ZRayTint.rgb * _ZRayLight.z * exp(-dot(dl, dl) / 0.3);
                return half4(c, 1.0);
            }
            ENDHLSL
        }

        Pass    // 1: shaft source (quarter resolution): what is brighter than the scene, near the light
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings i) : SV_Target
            {
                float2 uv = i.texcoord;
                float3 c = Down4(uv);
                float l = Luma(c);
                float3 b = c * saturate((l - _ZLens.x) / max(l, 1e-4));
                float aspect = _ZPostTexel.w > 0.0 ? _ZPostTexel.y / _ZPostTexel.x : 1.0;
                float2 dp = (uv - _ZRayLight.xy) * float2(aspect, 1.0);
                float m = exp(-dot(dp, dp) / max(_ZRayLight.w * _ZRayLight.w, 1e-4));
                return half4(min(b * m, 12.0), 1.0);
            }
            ENDHLSL
        }

        Pass    // 2: radial blur, long
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings i) : SV_Target { return half4(Radial(i.texcoord, 0.9, i.positionCS.xy), 1.0); }
            ENDHLSL
        }

        Pass    // 3: radial blur, short (smooths the steps of the first)
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings i) : SV_Target { return half4(Radial(i.texcoord, 0.22, i.positionCS.xy + 23.0), 1.0); }
            ENDHLSL
        }
    }
}
