Shader "Cascade/Cone"
{
    // The volcano: a polar grid displaced by the shared analytic height function (ConeHeight), so the flank collapse
    // is a morph of the same surface. Custom lighting in the HDRP Unlit skeleton (far object: no shadow maps reach it).
    Properties
    {
        [HideInInspector] _BlendMode("__blendmode", Float) = 0.0
        _RockA("Ash scree", 2D) = "grey" {}
        _RockAN("Ash scree normal", 2D) = "bump" {}
        _RockB("Lava rock", 2D) = "grey" {}
        _RockBN("Lava rock normal", 2D) = "bump" {}
        _RockC("Red scoria", 2D) = "grey" {}
        _RockCN("Red scoria normal", 2D) = "bump" {}
    }

    HLSLINCLUDE
    #pragma target 4.5
    #pragma only_renderers d3d11 vulkan
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/FragInputs.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPass.cs.hlsl"

    CBUFFER_START(UnityPerMaterial)
    float _BlendMode;
    float4 _RockA_ST, _RockAN_ST, _RockB_ST, _RockBN_ST, _RockC_ST, _RockCN_ST;
    CBUFFER_END
    TEXTURE2D(_RockA); TEXTURE2D(_RockAN); TEXTURE2D(_RockB); TEXTURE2D(_RockBN); TEXTURE2D(_RockC); TEXTURE2D(_RockCN);
    SAMPLER(sampler_RockA);

    #include "Assets/_Game/Shaders/CascadeCommon.hlsl"
    #include "Assets/_Game/Shaders/CascadeCone.hlsl"
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "HDUnlitShader" "Queue" = "Geometry+10" }

        Pass
        {
            Name "DepthForwardOnly"
            Tags { "LightMode" = "DepthForwardOnly" }
            Stencil { WriteMask 8 Ref 0 Comp Always Pass Replace }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #define SHADERPASS SHADERPASS_DEPTH_ONLY
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Material.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Unlit/Unlit.hlsl"
            #define HAVE_VERTEX_MODIFICATION
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/VaryingMesh.hlsl"
            #include "Assets/_Game/Shaders/ConeSurface.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPassDepthOnly.hlsl"
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }

        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "ForwardOnly" }
            Blend One Zero
            ZWrite On
            ZTest Equal
            Stencil { WriteMask 3 Ref 0 Comp Always Pass Replace }
            Cull Back

            HLSLPROGRAM
            #define SHADERPASS SHADERPASS_FORWARD_UNLIT
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Material.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Unlit/Unlit.hlsl"
            #define HAVE_VERTEX_MODIFICATION
            #define VARYINGS_NEED_POSITION_WS
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/VaryingMesh.hlsl"
            #define CONE_SHADING
            #include "Assets/_Game/Shaders/ConeSurface.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPassForwardUnlit.hlsl"
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
    }
    Fallback Off
}
