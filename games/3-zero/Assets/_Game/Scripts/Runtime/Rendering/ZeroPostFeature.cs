using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace ZERO
{
    /// <summary>The lens of the film camera, before Unity's post-processing (Render Graph): light shafts from the
    /// burning body and the explosion (quarter resolution, see "Hidden/Zero/Fullscreen"), composited together with the
    /// distortion of the shock front as the camera passes through it.</summary>
    public sealed class ZeroPostFeature : ScriptableRendererFeature
    {
        public Material material;
        public static float Strength;        // the shock distortion 0..1 (set by WorldBuilder)
        Pass pass;

        public override void Create() { pass = new Pass(); pass.renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing; }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData data)
        {
            if (material == null) return;
            if (data.cameraData.cameraType != CameraType.Game) return;
            if (UniversalRenderer.IsOffscreenDepthTexture(ref data.cameraData)) return;
            pass.material = material;
            pass.requiresIntermediateTexture = true;
            renderer.EnqueuePass(pass);
        }

        sealed class Pass : ScriptableRenderPass
        {
            public Material material;
            static readonly int idRays = Shader.PropertyToID("_ZRaysTex");

            public override void RecordRenderGraph(RenderGraph rg, ContextContainer frameData)
            {
                var res = frameData.Get<UniversalResourceData>();
                if (res.isActiveTargetBackBuffer) return;
                TextureHandle src = res.activeColorTexture;
                TextureDesc desc = rg.GetTextureDesc(src);
                desc.clearBuffer = false; desc.depthBufferBits = 0; desc.msaaSamples = MSAASamples.None;

                // quarter-resolution work textures for the shafts
                TextureDesc q = desc;
                q.width = Mathf.Max(1, desc.width / 4); q.height = Mathf.Max(1, desc.height / 4);
                q.filterMode = FilterMode.Bilinear; q.wrapMode = TextureWrapMode.Clamp;
                q.name = "_ZRaysA"; TextureHandle ra = rg.CreateTexture(q);
                q.name = "_ZRaysB"; TextureHandle rb = rg.CreateTexture(q);

                rg.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(src, ra, material, 1), passName: "ZeroShaftSource");
                rg.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(ra, rb, material, 2), passName: "ZeroShaftBlur1");
                using (var b = rg.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(rb, ra, material, 3), passName: "ZeroShaftBlur2", returnBuilder: true))
                    b.SetGlobalTextureAfterPass(ra, idRays);

                desc.name = "_ZeroPost";
                TextureHandle dst = rg.CreateTexture(desc);
                using (var b = rg.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(src, dst, material, 0), passName: "ZeroLens", returnBuilder: true))
                {
                    b.UseTexture(ra);
                }
                res.cameraColor = dst;
            }
        }
    }
}
