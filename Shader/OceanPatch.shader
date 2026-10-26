Shader "OceanPatch"
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
            Name "PatchDebug"

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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct OceanPatch
            {
                float4 rect;
                float4 edges;
            };

            StructuredBuffer<OceanPatch> _Patches;

            CBUFFER_START(UnityPerMaterial)
                float _SeaLevel;
                float _PatchCells;
                
                float4 _Wave;
                float _WaveSpeed;
                float _WaveTime;
                float _StitchEdges;
                float4 _DebugView;
            CBUFFER_END

            #include "OceanWaves.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half3 color : TEXCOORD1;
                nointerpolation float4 coarseEdges : TEXCOORD2;
                float3 normalWS : TEXCOORD3;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;

                OceanPatch patch = _Patches[input.instanceID];

                float3 positionWS;
                float3 normalWS;

                OceanEvaluatePatch(input.positionOS.xz, patch.rect, patch.edges, positionWS, normalWS);

                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalWS = normalWS;
                output.uv = input.positionOS.xz;

                output.color = 0.5 + 0.4 * cos(float3(0.0, 2.0, 4.0) + patch.rect.w * 1.7);

                float cell = patch.rect.z / _PatchCells;
                output.coarseEdges = step(float4(1.5, 1.5, 1.5, 1.5), patch.edges / cell);

                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);
                if (_DebugView.w > 0.5)
                    return half4(normalWS * 0.5 + 0.5, 1.0);

                float3 color = lerp(half3(0.03, 0.25, 0.35), input.color, _DebugView.x);

                Light mainLight = GetMainLight();
                half diffuse = saturate(dot(normalWS, mainLight.direction));

                color *= 0.2 + mainLight.color * diffuse;

                float2 cell = input.uv * _PatchCells;
                float2 edge = min(frac(cell), 1.0 - frac(cell));

                edge /= max(fwidth(cell), 0.0001);
                float grid = 1.0 - smoothstep(0.5, 1.0, min(edge.x, edge.y));

                color = lerp(color, color * 0.25, grid * _DebugView.y);
                float4 distanceToEdge = float4(input.uv.x, 1.0 - input.uv.x, input.uv.y, 1.0 - input.uv.y);
                float2 pixelWidth = max(fwidth(input.uv), 0.00001);

                float4 edgePixels = distanceToEdge / float4(pixelWidth.x, pixelWidth.x, pixelWidth.y, pixelWidth.y);

                float4 marks = (1.0 - smoothstep(0.75, 2.25, edgePixels)) * input.coarseEdges;

                float highlight = max(max(marks.x, marks.y), max(marks.z, marks.w));
                color = lerp(color, half3(1.0, 0.15, 0.03), highlight * _DebugView.z);

                return half4(color, 1.0);
            }
            ENDHLSL

        }
    }
}
