Shader "Ocean/Patch"
{
    Properties
    {
        _BaseColor("Surface Color", Color) = (0.34, 0.47, 0.53, 1)
        _DetailScale("Detail Cycles Per Metre", Float) = 0.8
        _DetailStrength("Detail Normal Strength", Range(0, 1)) = 0.2
        _DetailSpeed("Detail Animation Speed", Float) = 1
        [Normal] [NoScaleOffset] _DetailNormalMap("Detail Normal Map (Optional)", 2D) = "bump" {}
        _DetailMapStrength("Detail Map Strength", Range(0, 2)) = 1
        _FoamStrength("Foam Strength", Range(0, 1)) = 0
        _FoamScale("Foam Cycles Per Metre", Float) = 2
        _FoamSpeed("Foam Animation Speed", Float) = 1
        _FoamBreakup("Foam Breakup", Range(0, 1)) = 0.65
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            Name "OceanSurface"
            Tags { "LightMode" = "UniversalForwardOnly" }
            Cull Off
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "OceanWaves.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_DetailNormalMap);
            SAMPLER(sampler_DetailNormalMap);
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _DetailScale;
                float _DetailStrength;
                float _DetailSpeed;
                float _DetailMapStrength;
                float _FoamStrength;
                float _FoamScale;
                float _FoamSpeed;
                float _FoamBreakup;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float foam : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                OceanPatch patch = _Patches[input.instanceID];
                OceanEvaluatePatch(input.positionOS.xz, patch.rect, patch.edges,
                    output.positionWS, output.normalWS, output.foam);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                return output;
            }

            half4 Frag(Varyings input, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                // Match the displaced surface's termination to the baked terrain.
                if (_HasHeightMap > 0.5)
                {
                    float2 uv = OceanWorldToUV(input.positionWS.xz);
                    if (all(uv >= 0.0) && all(uv <= 1.0))
                    {
                        float ground = SAMPLE_TEXTURE2D_LOD(_OceanHeightMap, sampler_OceanHeightMap, uv, 0).r;
                        clip(input.positionWS.y - ground - 0.003);
                    }
                }

                float2 detailUV = input.positionWS.xz * max(_DetailScale, 0.0)
                    + _WaveTime * _DetailSpeed * float2(0.025, 0.017);
                float3 mapNormal = UnpackNormalScale(
                    SAMPLE_TEXTURE2D(_DetailNormalMap, sampler_DetailNormalMap, detailUV), _DetailMapStrength);
                float3 normal = OceanSurfaceNormal(input.positionWS.xz, input.normalWS, _WaveTime,
                    _DetailScale, _DetailStrength, _DetailSpeed, mapNormal);
                normal *= IS_FRONT_VFACE(face, 1.0, -1.0);
                float foam = OceanSurfaceFoam(input.foam, input.positionWS.xz, _WaveTime,
                    _FoamScale, _FoamSpeed, _FoamBreakup) * _FoamStrength;

                Light light = GetMainLight();
                half3 color = lerp(_BaseColor.rgb, half3(0.92, 0.96, 0.97), foam);
                color *= 0.22 + 0.78 * saturate(dot(normal, light.direction)) * light.color;
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
}
