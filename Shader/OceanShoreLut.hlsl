#ifndef OCEAN_SHORE_LUT_INCLUDED
#define OCEAN_SHORE_LUT_INCLUDED

void OceanSampleShoreLut(
    float2 uv,
    out float value,
    out float2 derivativeUV)
{
    uint width;
    uint height;
    _OceanShoreLut.GetDimensions(width, height);

    int2 size = int2(width, height);

    // U: periodic texel centers.
    // V: endpoint-inclusive samples.
    float2 pixel = float2(
        frac(uv.x) * size.x - 0.5,
        saturate(uv.y) * (size.y - 1));

    int2 first = int2(floor(pixel));
    float2 f = pixel - float2(first);

    // frac limits first.x to [-1, width - 1].
    int x0 = (first.x + size.x) % size.x;
    int x1 = (x0 + 1) % size.x;

    int y0 = first.y;
    int y1 = min(y0 + 1, size.y - 1);

    float h00 = _OceanShoreLut.Load(int3(x0, y0, 0)).g;
    float h10 = _OceanShoreLut.Load(int3(x1, y0, 0)).g;
    float h01 = _OceanShoreLut.Load(int3(x0, y1, 0)).g;
    float h11 = _OceanShoreLut.Load(int3(x1, y1, 0)).g;

    float lower = lerp(h00, h10, f.x);
    float upper = lerp(h01, h11, f.x);

    value = lerp(lower, upper, f.y);

    // Derivatives of this same bilinear height function.
    derivativeUV = float2(
        lerp(h10 - h00, h11 - h01, f.y) * size.x,
        (upper - lower) * (size.y - 1));

    // Outside the V interval, clamp makes the value constant in V.
    derivativeUV.y *= step(0.0, uv.y) * step(uv.y, 1.0);
}

#endif