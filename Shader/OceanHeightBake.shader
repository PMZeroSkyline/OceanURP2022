Shader "Hidden/Ocean/HeightBake"
{
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Cull Off
            ZWrite Off
            ZTest Always

            Blend One One
            BlendOp Max

            ColorMask R

            HLSLPROGRAM

            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _OceanRect;
                float4 _HeightRange;
            CBUFFER_END

            #include "OceanCoordinates.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float height : TEXCOORD0;
            };

            Varyings Vert(float3 positionOS : POSITION)
            {
                Varyings output;

                float3 positionWS =
                    TransformObjectToWorld(positionOS);

                float2 uv =
                    OceanWorldToUV(positionWS.xz);

                output.positionCS = float4(
                    uv * 2.0 - 1.0,
                    0.0,
                    1.0);

                #if UNITY_UV_STARTS_AT_TOP
                    output.positionCS.y =
                        -output.positionCS.y;
                #endif

                output.height = positionWS.y;

                return output;
            }

            float Frag(Varyings input) : SV_Target
            {
                clip(input.height - _HeightRange.x);
                clip(_HeightRange.y - input.height);

                return input.height;
            }

            ENDHLSL
        }
    }
}