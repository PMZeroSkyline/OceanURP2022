Shader "Ocean/ThicknessPreview"
{
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
            Name "ThicknessPreview"

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

            TEXTURE2D(_OceanCoastMap);
            TEXTURE2D(_OceanShoreLut);

            CBUFFER_START(UnityPerMaterial)
                float _SeaLevel;
                float _PatchCells;

                float4 _WaveShapes[4];
                float4 _WaveMotions[4];
                int _WaveCount;

                float _WaveTime;
                float _StitchEdges;
                float4 _DebugView;

                float4 _OceanRect;
                float4 _ShoreWave;
                float _HasCoastMap;
            CBUFFER_END

            #include "OceanCoordinates.hlsl"
            #include "OceanCoastData.hlsl"
            #include "OceanShoreLut.hlsl"
            #include "OceanWaves.hlsl"
            #include "OceanSceneDepth.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                OceanPatch patch = _Patches[input.instanceID];

                float3 positionWS;
                float3 normalWS;

                OceanEvaluatePatch(
                    input.positionOS.xz,
                    patch.rect,
                    patch.edges,
                    positionWS,
                    normalWS);

                Varyings output;

                output.positionCS = TransformWorldToHClip(positionWS);
                output.positionWS = positionWS;

                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 screenUV =
                    input.positionCS.xy / _ScaledScreenParams.xy;

                float3 scenePositionWS;

                if (!OceanTryGetScenePosition(
                    screenUV,
                    scenePositionWS))
                {
                    return half4(1.0, 0.25, 0.0, 1.0);
                }

                float distanceMetres = OceanGetSceneDistance(
                    input.positionWS,
                    scenePositionWS);

                // Preview only: 0 metres = black, 10+ metres = white.
                float gray = saturate(distanceMetres / 10.0);

                return half4(gray, gray, gray, 1.0);
            }

            ENDHLSL
        }
    }
}