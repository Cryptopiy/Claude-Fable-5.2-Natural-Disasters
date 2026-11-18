Shader "Cascade/Blanket"
{
    // Pyroclastic currents and collapse dust: a billowing blanket riding on the cone height function (same polar grid).
    Properties
    {
        [HideInInspector] _BlendMode("__blendmode", Float) = 0.0
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
    CBUFFER_END

    #include "Assets/_Game/Shaders/CascadeCommon.hlsl"
    #include "Assets/_Game/Shaders/CascadeCone.hlsl"
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "HDUnlitShader" "Queue" = "Geometry+15" }

        Pass
        {
            Name "DepthForwardOnly"
            Tags { "LightMode" = "DepthForwardOnly" }
            Stencil { WriteMask 8 Ref 0 Comp Always Pass Replace }
            Cull Off
            ZWrite On

            HLSLPROGRAM
            #define SHADERPASS SHADERPASS_DEPTH_ONLY
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Material.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Unlit/Unlit.hlsl"
            #define HAVE_VERTEX_MODIFICATION
            #define VARYINGS_NEED_POSITION_WS
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/VaryingMesh.hlsl"
            #include "Assets/_Game/Shaders/BlanketSurface.hlsl"
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
            Cull Off

            HLSLPROGRAM
            #define SHADERPASS SHADERPASS_FORWARD_UNLIT
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Material.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Unlit/Unlit.hlsl"
            #define HAVE_VERTEX_MODIFICATION
            #define VARYINGS_NEED_POSITION_WS
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/VaryingMesh.hlsl"
            #define BLANKET_SHADING
            #include "Assets/_Game/Shaders/BlanketSurface.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPassForwardUnlit.hlsl"
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
    }
    Fallback Off
}
