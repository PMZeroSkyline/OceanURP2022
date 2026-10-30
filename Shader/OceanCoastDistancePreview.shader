Shader "Hidden/Ocean/CoastDistancePreview"
{
    Properties
    {
        _MainTex("Coast Data", 2D) = "black" {}
    }

    SubShader
    {
        Pass
        {
            Cull Off
            ZWrite Off
            ZTest Always

            CGPROGRAM

            #pragma target 4.5
            #pragma vertex vert_img
            #pragma fragment Frag

            #include "UnityCG.cginc"

            Texture2D<float4> _MainTex;

            float _DomainSize;
            float _DisplayRange;
            float _ContourSpacing;
            int _PreviewMode;

            float4 Frag(v2f_img input) : SV_Target
            {
                uint width;
                uint height;

                _MainTex.GetDimensions(width, height);

                int2 size = int2(width, height);

                int2 pixel = clamp(
                    int2(floor(input.uv * float2(size))),
                    0,
                    size - 1);

                float4 data = _MainTex.Load(int3(pixel, 0));

                float3 water = float3(0.04, 0.3, 0.7);
                float3 land = float3(0.65, 0.3, 0.08);

                if (_PreviewMode == 2)
                    return float4(lerp(water, land, data.w), 1.0);

                if (data.z < 0.5)
                    return float4(0.15, 0.15, 0.15, 1.0);

                if (_PreviewMode == 1)
                    return float4(data.xy, 0.0, 1.0);

                float2 uv = (float2(pixel) + 0.5) / float2(size);

                float distanceMetres = length((uv - data.xy) * _DomainSize);

                float signedDistance = data.w > 0.5 ? -distanceMetres : distanceMetres;

                float fade = saturate(abs(signedDistance) / max(_DisplayRange, 0.01));

                float3 farColor = lerp(water, land, data.w);

                float3 color = lerp(float3(1.0, 1.0, 1.0), farColor, fade);

                float bands = abs(signedDistance) / max(_ContourSpacing, 0.01);

                float edge = abs(frac(bands + 0.5) - 0.5);

                float Line = 1.0 - smoothstep(0.0, max(fwidth(bands), 0.0001), edge);

                color *= 1.0 - 0.5 * Line;

                return float4(color, 1.0);
            }

            ENDCG
        }
    }
}