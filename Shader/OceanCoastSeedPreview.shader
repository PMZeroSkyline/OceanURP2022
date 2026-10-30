Shader "Ocean/CoastSeedPreview"
{
    Properties
    {
        [Enum(LandWater, 0, CoastSeeds, 1, SeedUV, 2)]
        _PreviewMode("Preview Mode", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "CoastSeedPreview"

            Tags
            {
                "LightMode" = "UniversalForwardOnly"
            }

            Cull Off
            ZWrite Off
            ZTest Always

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

            TEXTURE2D(_OceanHeightMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _OceanRect;
                float _SeaLevel;
                float _HasHeightMap;
                float _PreviewMode;
            CBUFFER_END

            #include "OceanCoordinates.hlsl"
            #include "OceanCoastSeeds.hlsl"

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

                float2 worldXZ = patch.rect.xy
                    + input.positionOS.xz * patch.rect.z;

                float3 positionWS = float3(
                    worldXZ.x,
                    _SeaLevel,
                    worldXZ.y);

                output.positionCS =
                    TransformWorldToHClip(positionWS);

                output.worldXZ = worldXZ;

                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                if (_HasHeightMap < 0.5)
                    return half4(1.0, 0.35, 0.05, 1.0);

                float2 uv = OceanWorldToUV(input.worldXZ);

                if (any(uv < 0.0) || any(uv > 1.0))
                    return half4(0.6, 0.0, 1.0, 1.0);

                uint width;
                uint height;

                _OceanHeightMap.GetDimensions(width, height);

                int2 size = int2(width, height);

                int2 pixel = min(int2(floor(uv * float2(size))), size - 1);

                float4 seed = OceanFindCoastSeed(pixel, size, _SeaLevel);

                half3 waterColor = half3(0.03, 0.25, 0.5);
                half3 landColor = half3(0.65, 0.5, 0.25);

                half3 color = lerp(waterColor, landColor, seed.w);

                if (_PreviewMode < 0.5)
                    return half4(color, 1.0);

                if (_PreviewMode < 1.5)
                {
                    if (seed.z > 0.5)
                        color = half3(1.0, 1.0, 1.0);

                    return half4(color, 1.0);
                }

                if (seed.z > 0.5)
                    return half4(seed.xy, 0.0, 1.0);

                return half4(0.0, 0.0, 0.0, 1.0);
            }

            ENDHLSL
        }
    }
}