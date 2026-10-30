Shader "Ocean/DomainUV"
{
    Properties
    {
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "DomainUV"

            Tags
            {
                "LightMode" = "UniversalForwardOnly"
            }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM

            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct OceanPatch
            {
                float4 rect;
                float4 edges;
            };

            StructuredBuffer<OceanPatch> _Patches;

            CBUFFER_START(UnityPerMaterial)
                float4 _OceanRect;

                float4 _WaveShapes[4];
                float4 _WaveMotions[4];

                int _WaveCount;
                float _WaveTime;

                float _SeaLevel;
                float _PatchCells;
                float _StitchEdges;
            CBUFFER_END

            #include "OceanCoordinates.hlsl"
            #include "OceanWaves.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 worldXZ : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;

                OceanPatch patch = _Patches[input.instanceID];

                float3 positionWS;
                float3 normalWS;

                OceanEvaluatePatch(input.positionOS.xz, patch.rect, patch.edges, positionWS, normalWS);

                output.positionCS = TransformWorldToHClip(positionWS);

                output.worldXZ = positionWS.xz;

                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = OceanWorldToUV(input.worldXZ);

                if (any(uv < 0.0) || any(uv > 1.0))
                    return half4(0.0, 0.0, 1.0, 1.0);

                return half4(uv.x, uv.y, 0.0, 1.0);
            }

            ENDHLSL
        }
    }
}