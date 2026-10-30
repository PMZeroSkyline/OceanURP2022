#ifndef OCEAN_COAST_SEEDS_INCLUDED
#define OCEAN_COAST_SEEDS_INCLUDED

float OceanLoadGroundHeight(int2 pixel)
{
    return _OceanHeightMap.Load(int3(pixel, 0)).r;
}

float4 OceanFindCoastSeed(int2 pixel, int2 size, float seaLevel)
{
    float height = OceanLoadGroundHeight(pixel);
    bool land = height >= seaLevel;

    float4 result = float4(-1.0, -1.0, 0.0, land ? 1.0 : 0.0);

    float bestDistanceSq = 1e20;

    float2 center = float2(pixel) + 0.5;

    const int2 offsets[4] =
    {
        int2(-1, 0),
        int2(1, 0),
        int2(0, -1),
        int2(0, 1)
    };

    [unroll]
    for (int i = 0; i < 4; i++)
    {
        int2 neighbour = pixel + offsets[i];

        if (any(neighbour < 0) || any(neighbour >= size))
            continue;

        float neighbourHeight = OceanLoadGroundHeight(neighbour);

        bool neighbourLand = neighbourHeight >= seaLevel;

        if (land == neighbourLand)
            continue;

        float t = (seaLevel - height) / (neighbourHeight - height);

        float distanceSq = t * t;

        if (distanceSq >= bestDistanceSq)
            continue;

        bestDistanceSq = distanceSq;

        float2 seedPixel = center + float2(offsets[i]) * t;

        result.xy = seedPixel / float2(size);
        result.z = 1.0;
    }

    return result;
}

#endif