#ifndef OCEAN_COAST_DATA_INCLUDED
#define OCEAN_COAST_DATA_INCLUDED

// 0: continuous distance; 1: old point-sampled preview.
#define OCEAN_COAST_POINT_PREVIEW 0

struct OceanCoastSample
{
    float2 seedUV;
    float signedDistance;
    float2 distanceGradient;
    float2 offshoreDirection;
    float land;
    float valid;
};

bool OceanCoastCoordinates(float2 worldXZ, out float2 uv, out int2 size)
{
    uv = OceanWorldToUV(worldXZ);
    size = int2(0, 0);

    if (_HasCoastMap < 0.5 || any(uv < 0.0) || any(uv > 1.0))
        return false;

    uint width;
    uint height;
    _OceanCoastMap.GetDimensions(width, height);
    size = int2(width, height);
    return true;
}

float2 OceanCoastSeedDelta(int2 pixel, int2 size, float2 seedUV)
{
    float2 centerUV = (float2(pixel) + 0.5) / float2(size);
    return (centerUV - seedUV) * _OceanRect.z;
}

float OceanDecodeCoastDistance(int2 pixel, int2 size, float4 data)
{
    float side = data.w > 0.5 ? -1.0 : 1.0;
    return side * length(OceanCoastSeedDelta(pixel, size, data.xy));
}

// Only for comparing the old preview. Its staircase distance has no
// useful continuous gradient, so distanceGradient remains zero.
OceanCoastSample OceanSampleCoastPoint(float2 worldXZ)
{
    OceanCoastSample result = (OceanCoastSample)0;
    float2 uv;
    int2 size;

    if (!OceanCoastCoordinates(worldXZ, uv, size))
        return result;

    int2 pixel = min(int2(floor(uv * float2(size))), size - 1);
    float4 data = _OceanCoastMap.Load(int3(pixel, 0));

    result.seedUV = data.xy;
    result.land = data.w;
    result.valid = data.z;

    if (data.z < 0.5)
        return result;

    float2 delta = OceanCoastSeedDelta(pixel, size, data.xy);
    float distanceMetres = length(delta);
    float side = data.w > 0.5 ? -1.0 : 1.0;

    result.signedDistance = side * distanceMetres;
    result.offshoreDirection = side * delta / max(distanceMetres, 0.0001);
    return result;
}

OceanCoastSample OceanSampleCoastBilinear(float2 worldXZ)
{
    OceanCoastSample result = (OceanCoastSample)0;
    float2 uv;
    int2 size;

    if (!OceanCoastCoordinates(worldXZ, uv, size))
        return result;

    // Integer coordinates correspond to texel centers.
    float2 texel = uv * float2(size) - 0.5;
    float2 clamped = clamp(texel, 0.0, float2(size - 1));
    int2 p0 = int2(floor(clamped));
    int2 p1 = min(p0 + 1, size - 1);
    float2 f = clamped - float2(p0);

    int2 p00 = p0;
    int2 p10 = int2(p1.x, p0.y);
    int2 p01 = int2(p0.x, p1.y);
    int2 p11 = p1;

    float4 s00 = _OceanCoastMap.Load(int3(p00, 0));
    float4 s10 = _OceanCoastMap.Load(int3(p10, 0));
    float4 s01 = _OceanCoastMap.Load(int3(p01, 0));
    float4 s11 = _OceanCoastMap.Load(int3(p11, 0));

    // Keep an actual seed for the NearestUV debug view.
    float4 nearestData = f.y < 0.5
        ? (f.x < 0.5 ? s00 : s10)
        : (f.x < 0.5 ? s01 : s11);

    result.seedUV = nearestData.xy;
    result.land = nearestData.w;

    if (min(min(s00.z, s10.z), min(s01.z, s11.z)) < 0.5)
        return result;

    // Decode each texel at its own center BEFORE interpolating.
    float d00 = OceanDecodeCoastDistance(p00, size, s00);
    float d10 = OceanDecodeCoastDistance(p10, size, s10);
    float d01 = OceanDecodeCoastDistance(p01, size, s01);
    float d11 = OceanDecodeCoastDistance(p11, size, s11);

    float lower = lerp(d00, d10, f.x);
    float upper = lerp(d01, d11, f.x);
    result.signedDistance = lerp(lower, upper, f.y);

    // Derivatives with respect to world X and Z, in metres/metre.
    float2 gradient = float2(
        lerp(d10 - d00, d11 - d01, f.y),
        upper - lower) * float2(size) / _OceanRect.z;

    // Clamp extends the border value constantly along the clamped axis.
    float2 activeAxes = step(0.0, texel) * step(texel, float2(size - 1));
    gradient *= activeAxes;

    result.distanceGradient = gradient;
    float gradientLengthSq = dot(gradient, gradient);

    if (gradientLengthSq > 1e-8)
        result.offshoreDirection = gradient * rsqrt(gradientLengthSq);

    result.land = result.signedDistance <= 0.0 ? 1.0 : 0.0;
    result.valid = 1.0;
    return result;
}

OceanCoastSample OceanSampleCoast(float2 worldXZ)
{
#if OCEAN_COAST_POINT_PREVIEW
    return OceanSampleCoastPoint(worldXZ);
#else
    return OceanSampleCoastBilinear(worldXZ);
#endif
}

#endif