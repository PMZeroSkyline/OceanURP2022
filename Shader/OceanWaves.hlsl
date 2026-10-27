#ifndef OCEAN_WAVES_INCLUDED
#define OCEAN_WAVES_INCLUDED

void OceanEvaluateSurface(
    float2 p,
    out float3 positionWS,
    out float3 normalWS)
{
    positionWS = float3(p.x, _SeaLevel, p.y);

    float3 tangentX = float3(1.0, 0.0, 0.0);
    float3 tangentZ = float3(0.0, 0.0, 1.0);

    [loop]
    for (int i = 0; i < _WaveCount; i++)
    {
        float4 shape = _WaveShapes[i];
        float4 motion = _WaveMotions[i];

        float2 direction = shape.xy;
        float amplitude = shape.z;
        float k = shape.w;

        float horizontal = motion.x;
        float speed = motion.y;

        float phase = k * (dot(direction, p) - speed * _WaveTime);

        float sine;
        float cosine;
        sincos(phase, sine, cosine);

        positionWS += float3(direction.x * horizontal * cosine,
            amplitude * sine, direction.y * horizontal * cosine);

        float verticalSlope = amplitude * k * cosine;
        float horizontalSlope = horizontal * k * sine;

        float mixed = -horizontalSlope * direction.x * direction.y;

        tangentX += float3(-horizontalSlope * direction.x * direction.x,
            verticalSlope * direction.x, mixed);
        tangentZ += float3(mixed, verticalSlope * direction.y,
            -horizontalSlope * direction.y * direction.y);
    }

    normalWS = normalize(cross(tangentZ, tangentX));
}


void OceanStitchEdges(float2 p, float2 axis, float cell, float vertexIndex, inout float3 positionWS, inout float3 normalWS)
{
    if (fmod(vertexIndex, 2.0) < 0.5)
        return;

    float3 positionA;
    float3 positionB;
    float3 normalA;
    float3 normalB;

    OceanEvaluateSurface(p - axis * cell, positionA, normalA);
    OceanEvaluateSurface(p + axis * cell, positionB, normalB);

    positionWS = (positionA + positionB) * 0.5;
    normalWS = (normalA + normalB) * 0.5;
}

void OceanEvaluatePatch(float2 uv, float4 rect, float4 edges, out float3 positionWS, out float3 normalWS)
{
    float2 p = rect.xy + uv * rect.z;
    OceanEvaluateSurface(p, positionWS, normalWS);

    if (_StitchEdges < 0.5)
        return;

    float2 vertex = round(uv * _PatchCells);
    float cell = rect.z / _PatchCells;

    bool stitchX = (vertex.x == 0.0 && edges.x > cell * 1.5) || (vertex.x == _PatchCells && edges.y > cell * 1.5);
    if (stitchX)
    {
        OceanStitchEdges(p, float2(0.0, 1.0), cell, vertex.y, positionWS, normalWS);
    }

    bool stitchZ = (vertex.y == 0.0 && edges.z > cell * 1.5) || (vertex.y == _PatchCells && edges.w > cell * 1.5);
    if (stitchZ)
    {
        OceanStitchEdges(p, float2(1.0, 0.0), cell, vertex.x, positionWS, normalWS);
    }
}

#endif
