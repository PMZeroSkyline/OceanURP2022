Shader "Ocean/CoastSurfacePreview"
{
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "CoastSurfacePreview"

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

            TEXTURE2D(_OceanCoastMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _OceanRect;
                float4 _CoastDebug;
                float _SeaLevel;
                float _HasCoastMap;
            CBUFFER_END

            #include "OceanCoordinates.hlsl"
            #include "OceanCoastData.hlsl"

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
                OceanPatch patch = _Patches[input.instanceID];

                float2 worldXZ = patch.rect.xy
                    + input.positionOS.xz * patch.rect.z;

                Varyings output;

                output.worldXZ = worldXZ;

                output.positionCS = TransformWorldToHClip(
                    float3(worldXZ.x, _SeaLevel, worldXZ.y));

                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                if (_HasCoastMap < 0.5)
                    return half4(1.0, 0.35, 0.05, 1.0);

                float2 uv = OceanWorldToUV(input.worldXZ);

                if (any(uv < 0.0) || any(uv > 1.0))
                    return half4(0.6, 0.0, 1.0, 1.0);

                OceanCoastSample coast =
                    OceanSampleCoast(input.worldXZ);

                float3 water = float3(0.04, 0.3, 0.7);
                float3 land = float3(0.65, 0.3, 0.08);

                float3 farColor = lerp(
                    water,
                    land,
                    coast.land);

                // LandWater。
                if (_CoastDebug.x > 2.5)
                    return half4(farColor, 1.0);

                if (coast.valid < 0.5)
                    return half4(0.15, 0.15, 0.15, 1.0);

                // NearestUV。
                if (_CoastDebug.x > 1.5)
                    return half4(coast.seedUV, 0.0, 1.0);

                // OffshoreDirection。
                if (_CoastDebug.x > 0.5)
                {
                    return half4(
                        coast.offshoreDirection * 0.5 + 0.5,
                        0.0,
                        1.0);
                }

                // Distance。
                float distanceMetres =
                    abs(coast.signedDistance);

                float fade = saturate(
                    distanceMetres / max(_CoastDebug.y, 0.01));

                float3 color = lerp(
                    float3(1.0, 1.0, 1.0),
                    farColor,
                    fade);

                float bands = distanceMetres
                    / max(_CoastDebug.z, 0.01);

                float edge = abs(frac(bands + 0.5) - 0.5);

                float contour = 1.0 - smoothstep(
                    0.0,
                    max(fwidth(bands), 0.0001),
                    edge);

                color *= 1.0 - 0.5 * contour;

                return half4(color, 1.0);
            }

            ENDHLSL
        }
    }
}