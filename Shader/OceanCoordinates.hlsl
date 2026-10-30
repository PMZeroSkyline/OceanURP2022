#ifndef OCEAN_COORDINATES_INCLUDED
#define OCEAN_COORDINATES_INCLUDED

float2 OceanWorldToUV(float2 worldXZ)
{
    return (worldXZ - _OceanRect.xy) * _OceanRect.w;
}

float2 OceanUVToWorld(float2 uv)
{
    return _OceanRect.xy + uv * _OceanRect.z;
}

#endif