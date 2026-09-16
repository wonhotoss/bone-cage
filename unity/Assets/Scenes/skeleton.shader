// Flat color drawn over everything: the skeleton sits inside the body, so it passes no depth test
// and draws last, and culls nothing so the cubes need no winding.
Shader "bone-cage/skeleton"{
    Properties{
        _Color("color", Color) = (1, 0.65, 0.3, 1)
    }
    SubShader{
        Tags{ "Queue" = "Overlay" "RenderType" = "Opaque" }
        Pass{
            ZTest Always
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4 _Color;

            float4 vert(float4 p : POSITION) : SV_POSITION{
                return TransformObjectToHClip(p.xyz);
            }

            float4 frag() : SV_Target{
                return _Color;
            }
            ENDHLSL
        }
    }
}
