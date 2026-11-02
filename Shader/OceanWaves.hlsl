#ifndef OCEAN_WAVES_INCLUDED
#define OCEAN_WAVES_INCLUDED

void OceanEvaluateShoreWave(
    float2 worldXZ,
    out float height,
    out float2 gradient)
{
    height = 0.0;
    gradient = float2(0.0, 0.0);

#if defined(OCEAN_COAST_DATA_INCLUDED) && defined(OCEAN_SHORE_LUT_INCLUDED)
    if (_ShoreWave.x <= 0.0)
        return;

    OceanCoastSample coast = OceanSampleCoast(worldXZ);
    if (coast.valid < 0.5)
        return;

    float amplitude = _ShoreWave.x;
    float inverseWavelength = _ShoreWave.y / TWO_PI;
    float speed = _ShoreWave.z;
    float range = _ShoreWave.w;

    float distanceMetres = coast.signedDistance;
    float v = distanceMetres / range;

    if (v <= 0.0 || v >= 1.0)
        return;

    float u =
        (distanceMetres + speed * _WaveTime)
        * inverseWavelength;

    float lutHeight;
    float2 derivativeUV;

    OceanSampleShoreLut(
        float2(u, v),
        lutHeight,
        derivativeUV);

    height = amplitude * lutHeight;

    // Distance changes both U and V.
    float slope = amplitude * (
        derivativeUV.x * inverseWavelength
        + derivativeUV.y / range);

    gradient = slope * coast.distanceGradient;
#endif
}

void OceanEvaluateOffshoreWeight(
    float2 restXZ,
    out float weight,
    out float2 gradient)
{
    weight = 1.0;
    gradient = float2(0.0, 0.0);

#if defined(OCEAN_COAST_DATA_INCLUDED) && defined(OCEAN_SHORE_LUT_INCLUDED)
    OceanCoastSample coast = OceanSampleCoast(restXZ);
    if (coast.valid < 0.5)
        return;

    float range = max(_ShoreWave.w, 0.1);
    float u = saturate(coast.signedDistance / range);

    weight = u * u * (3.0 - 2.0 * u);

    float slope = 6.0 * u * (1.0 - u) / range;
    gradient = slope * coast.distanceGradient;
#endif
}

void OceanEvaluateSurface(
    float2 p,
    out float3 positionWS,
    out float3 normalWS)
{
    float3 displacement = float3(0.0, 0.0, 0.0);
    float3 derivativeX = float3(0.0, 0.0, 0.0);
    float3 derivativeZ = float3(0.0, 0.0, 0.0);

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

        float phase =
            k * (dot(direction, p) - speed * _WaveTime);

        float sine;
        float cosine;
        sincos(phase, sine, cosine);

        displacement += float3(
            direction.x * horizontal * cosine,
            amplitude * sine,
            direction.y * horizontal * cosine);

        float verticalSlope = amplitude * k * cosine;
        float horizontalSlope = horizontal * k * sine;

        float mixed =
            -horizontalSlope * direction.x * direction.y;

        derivativeX += float3(
            -horizontalSlope * direction.x * direction.x,
            verticalSlope * direction.x,
            mixed);

        derivativeZ += float3(
            mixed,
            verticalSlope * direction.y,
            -horizontalSlope * direction.y * direction.y);
    }

    float weight;
    float2 weightGradient;
    OceanEvaluateOffshoreWeight(p, weight, weightGradient);

    float3 basePosition = float3(p.x, _SeaLevel, p.y);
    positionWS = basePosition + weight * displacement;

    // Product rule: d(w * D) = w * dD + D * dw.
    float3 tangentX = float3(1.0, 0.0, 0.0)
        + weight * derivativeX
        + displacement * weightGradient.x;

    float3 tangentZ = float3(0.0, 0.0, 1.0)
        + weight * derivativeZ
        + displacement * weightGradient.y;

    float shoreHeight;
    float2 shoreGradient;

    // Shore waves remain anchored to the displaced world XZ.
    OceanEvaluateShoreWave(
        positionWS.xz,
        shoreHeight,
        shoreGradient);

    positionWS.y += shoreHeight;

    tangentX.y += dot(shoreGradient, tangentX.xz);
    tangentZ.y += dot(shoreGradient, tangentZ.xz);

    normalWS = normalize(cross(tangentZ, tangentX));
}

void OceanStitchEdges(
    float2 p,
    float2 axis,
    float cell,
    float vertexIndex,
    inout float3 positionWS,
    inout float3 normalWS)
{
    if (fmod(vertexIndex, 2.0) < 0.5)
        return;

    float3 positionA;
    float3 positionB;
    float3 normalA;
    float3 normalB;

    OceanEvaluateSurface(
        p - axis * cell,
        positionA,
        normalA);

    OceanEvaluateSurface(
        p + axis * cell,
        positionB,
        normalB);

    positionWS = (positionA + positionB) * 0.5;
    normalWS = (normalA + normalB) * 0.5;
}

void OceanEvaluatePatch(
    float2 uv,
    float4 rect,
    float4 edges,
    out float3 positionWS,
    out float3 normalWS)
{
    float2 p = rect.xy + uv * rect.z;
    OceanEvaluateSurface(p, positionWS, normalWS);

    if (_StitchEdges < 0.5)
        return;

    float2 vertex = round(uv * _PatchCells);
    float cell = rect.z / _PatchCells;

    bool stitchX =
        (vertex.x == 0.0 && edges.x > cell * 1.5)
        || (vertex.x == _PatchCells && edges.y > cell * 1.5);

    if (stitchX)
    {
        OceanStitchEdges(
            p,
            float2(0.0, 1.0),
            cell,
            vertex.y,
            positionWS,
            normalWS);
    }

    bool stitchZ =
        (vertex.y == 0.0 && edges.z > cell * 1.5)
        || (vertex.y == _PatchCells && edges.w > cell * 1.5);

    if (stitchZ)
    {
        OceanStitchEdges(
            p,
            float2(1.0, 0.0),
            cell,
            vertex.x,
            positionWS,
            normalWS);
    }
}

#endif