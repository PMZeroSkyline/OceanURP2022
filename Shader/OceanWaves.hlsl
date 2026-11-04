#ifndef OCEAN_WAVES_INCLUDED
#define OCEAN_WAVES_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

// OceanDomain supplies these per draw. A custom shader keeps its own UnityPerMaterial buffer.
struct OceanPatch
{
    float4 rect;
    float4 edges;
};
StructuredBuffer<OceanPatch> _Patches;
TEXTURE2D(_OceanHeightMap);
SAMPLER(sampler_OceanHeightMap);
TEXTURE2D(_OceanCoastMap);
TEXTURE2D(_OceanShoreLut);
TEXTURE2D(_OceanShoreNoise);

CBUFFER_START(OceanPerDomain)
    float _SeaLevel;
    float _PatchCells;
    float4 _WaveShapes[4];
    float4 _WaveMotions[4];
    int _WaveCount;
    float _WaveTime;
    float _StitchEdges;
    float4 _OceanRect;
    float4 _ShoreWave;
    float4 _ShoreShape;
    float4 _ShoreLife;
    float4 _ShoreNoise;
    float _HasHeightMap;
    float _HasCoastMap;
CBUFFER_END

// World coordinates and coastline field.
float2 OceanWorldToUV(float2 worldXZ)
{
    return (worldXZ - _OceanRect.xy) * _OceanRect.w;
}

float2 OceanUVToWorld(float2 uv)
{
    return _OceanRect.xy + uv * _OceanRect.z;
}


struct OceanCoastSample
{
    float signedDistance;
    float2 distanceGradient;
    float2 offshoreDirection;
    float2 directionX;
    float2 directionZ;
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

float2 OceanDecodeCoastDirection(int2 pixel, int2 size, float4 data)
{
    float2 delta = OceanCoastSeedDelta(pixel, size, data.xy);
    float side = data.w > 0.5 ? -1.0 : 1.0;
    return side * delta / max(length(delta), 0.0001);
}

OceanCoastSample OceanSampleCoast(float2 worldXZ)
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
    // Interpolate directions themselves, so horizontal displacement is continuous.
    float2 n00 = OceanDecodeCoastDirection(p00, size, s00);
    float2 n10 = OceanDecodeCoastDirection(p10, size, s10);
    float2 n01 = OceanDecodeCoastDirection(p01, size, s01);
    float2 n11 = OceanDecodeCoastDirection(p11, size, s11);
    float2 lowerDirection = lerp(n00, n10, f.x);
    float2 upperDirection = lerp(n01, n11, f.x);
    float2 direction = lerp(lowerDirection, upperDirection, f.y);

    float2 dx = lerp(n10 - n00, n11 - n01, f.y)
        * size.x / _OceanRect.z * activeAxes.x;
    float2 dz = (upperDirection - lowerDirection)
        * size.y / _OceanRect.z * activeAxes.y;

    float magnitude = length(direction);
    float denominator = max(magnitude, 0.01);
    float2 n = direction / denominator;

    if (magnitude > 0.01)
    {
        dx = (dx - n * dot(n, dx)) / denominator;
        dz = (dz - n * dot(n, dz)) / denominator;
    }
    else
    {
        dx /= denominator;
        dz /= denominator;
    }

    result.offshoreDirection = n;
    result.directionX = dx;
    result.directionZ = dz;

    result.valid = 1.0;
    return result;
}

// Exact bilinear values and derivatives: the same function drives position and normals.
float OceanGround(float2 p, out float2 gradient)
{
    // Called only for a valid baked coast/height pair; keep this sampler branch-free.
    uint w, h;
    _OceanHeightMap.GetDimensions(w, h);
    int2 size = int2(w, h);
    float2 raw = OceanWorldToUV(p) * size - 0.5;
    float2 pixel = clamp(raw, 0.0, float2(size - 1));
    int2 a = int2(floor(pixel)), b = min(a + 1, size - 1);
    float2 f = pixel - a;
    float v00 = _OceanHeightMap.Load(int3(a, 0)).r;
    float v10 = _OceanHeightMap.Load(int3(b.x, a.y, 0)).r;
    float v01 = _OceanHeightMap.Load(int3(a.x, b.y, 0)).r;
    float v11 = _OceanHeightMap.Load(int3(b, 0)).r;
    gradient = float2(lerp(v10 - v00, v11 - v01, f.y),
        lerp(v01 - v00, v11 - v10, f.x)) * size * _OceanRect.w;
    gradient *= step(0.0, raw) * step(raw, float2(size - 1));
    return lerp(lerp(v00, v10, f.x), lerp(v01, v11, f.x), f.y);
}

// Catmull-Rom retains the source texel centres and gives continuous first derivatives.
float3 OceanCubic(float3 a, float3 b, float3 c, float3 d, float t)
{
    return b + 0.5 * t * (c - a + t * (2.0 * a - 5.0 * b + 4.0 * c - d
        + t * (3.0 * (b - c) + d - a)));
}
float3 OceanCubicDerivative(float3 a, float3 b, float3 c, float3 d, float t)
{
    return 0.5 * (c - a + 2.0 * t * (2.0 * a - 5.0 * b + 4.0 * c - d)
        + 3.0 * t * t * (3.0 * (b - c) + d - a));
}
float3 OceanSampleShoreProfile(float2 uv, out float3 du, out float3 dv)
{
    uint w, h;
    _OceanShoreLut.GetDimensions(w, h);
    int2 size = int2(w, h);
    float rawY = uv.y * size.y - 0.5;
    float2 pixel = float2(frac(uv.x) * size.x - 0.5, clamp(rawY, 0.0, size.y - 1.0));
    int2 first = int2(floor(pixel));
    float2 f = pixel - first;
    uint4 x = (uint4)(first.x + int4(-1, 0, 1, 2) + size.x) % (uint)size.x;
    float3 rows[4], slopes[4];
    [unroll]
    for (int i = 0; i < 4; i++)
    {
        // Mirrored endpoint samples give zero V slope where the lifecycle clamps.
        int y = abs(first.y + i - 1);
        y = clamp(y < size.y ? y : 2 * (size.y - 1) - y, 0, size.y - 1);
        float3 a = _OceanShoreLut.Load(int3(x.x, y, 0)).rgb;
        float3 b = _OceanShoreLut.Load(int3(x.y, y, 0)).rgb;
        float3 c = _OceanShoreLut.Load(int3(x.z, y, 0)).rgb;
        float3 d = _OceanShoreLut.Load(int3(x.w, y, 0)).rgb;
        rows[i] = OceanCubic(a, b, c, d, f.x);
        slopes[i] = OceanCubicDerivative(a, b, c, d, f.x);
    }
    du = OceanCubic(slopes[0], slopes[1], slopes[2], slopes[3], f.y) * size.x;
    dv = OceanCubicDerivative(rows[0], rows[1], rows[2], rows[3], f.y) * size.y
        * step(0.0, rawY) * step(rawY, size.y - 1.0);
    float3 profile = OceanCubic(rows[0], rows[1], rows[2], rows[3], f.y);

    // U wrapping also changes the wave's lifecycle V. Fade the packet ends to zero
    // with zero first/second derivatives, so adjacent lifecycles cannot tear the mesh.
    // Fixed phase width keeps the shape independent of the LUT resolution.
    float phase = frac(uv.x);
    float t = saturate(min(phase, 1.0 - phase) / 0.06);
    float envelope = t * t * t * (t * (t * 6.0 - 15.0) + 10.0);
    float derivative = 30.0 * t * t * (t - 1.0) * (t - 1.0)
        * (phase < 0.5 ? 1.0 : -1.0) / 0.06;
    du = du * envelope + profile * derivative;
    dv *= envelope;
    return profile * envelope;
}

float OceanShoreNoise(float2 uv, out float2 gradient)
{
    // The reference PNG is stored with the opposite vertical axis.
    uint w, h;
    _OceanShoreNoise.GetDimensions(w, h);
    int2 size = int2(w, h);
    float2 pixel = frac(float2(uv.x, 1.0 - uv.y)) * size - 0.5;
    int2 first = int2(floor(pixel));
    float2 f = pixel - first;
    uint2 a = (uint2)(first + size) % (uint2)size, b = (a + 1) % (uint2)size;
    float v00 = _OceanShoreNoise.Load(int3(a, 0)).r;
    float v10 = _OceanShoreNoise.Load(int3(b.x, a.y, 0)).r;
    float v01 = _OceanShoreNoise.Load(int3(a.x, b.y, 0)).r;
    float v11 = _OceanShoreNoise.Load(int3(b, 0)).r;
    gradient = float2(lerp(v10 - v00, v11 - v01, f.y),
        -lerp(v01 - v00, v11 - v10, f.x)) * size;
    return lerp(lerp(v00, v10, f.x), lerp(v01, v11, f.x), f.y);
}

float3 OceanNormalizedDerivative(float3 unit, float3 derivative, float magnitude)
{
    return (derivative - unit * dot(unit, derivative)) / magnitude;
}

void OceanEvaluateShoreWave(float2 p, OceanCoastSample coast,
    out float3 offset, out float3 dx, out float3 dz, out float foam)
{
    offset = dx = dz = 0.0;
    foam = 0.0;
    if (_HasHeightMap < 0.5 || _ShoreNoise.w < 0.5 || coast.valid < 0.5 || coast.signedDistance >= _ShoreWave.w) return;

    float invWidth = _ShoreWave.y / TWO_PI;
    float distance = coast.signedDistance;
    float2 dg = coast.distanceGradient;
    float2 inland = -coast.offshoreDirection;
    float2 nx = -coast.directionX, nz = -coast.directionZ;
    float d = distance * invWidth;

    // MF_CoastlineOffsets from the reference: anchor on the coast, not on the moving crest.
    float anchor = (p.y + inland.y * distance) * invWidth;
    float2 anchorGradient = float2(nx.y * distance + inland.y * dg.x,
        1.0 + nz.y * distance + inland.y * dg.y) * invWidth;
    float s, c, unused;
    sincos(TWO_PI * anchor * 0.242, s, unused);
    float2 sg = unused * (TWO_PI * 0.242) * anchorGradient;
    sincos(TWO_PI * anchor * 0.751773, unused, c);
    float2 cg = -unused * (TWO_PI * 0.751773) * anchorGradient;
    float timeOffset = anchor * 0.121 - s * 0.135 - c * 0.0375;
    float details = -s * 0.25 + c * 0.15;
    float2 timeGradient = anchorGradient * 0.121 - sg * 0.135 - cg * 0.0375;
    float2 detailGradient = -sg * 0.25 + cg * 0.15;

    if (_ShoreNoise.z > 0.5)
    {
        float2 ng;
        float noise = OceanShoreNoise(p * invWidth * 0.5, ng) - 0.5;
        ng *= invWidth * 0.5;
        float fade = saturate(d * 0.35 - 0.05);
        float2 fg = (fade > 0.0 && fade < 1.0) ? dg * invWidth * 0.35 : 0.0;
        timeOffset += noise * fade * _ShoreNoise.x;
        timeGradient += (ng * fade + noise * fg) * _ShoreNoise.x;
        details += (OceanShoreNoise(p * invWidth * 1.5, ng) - 1.0) * _ShoreNoise.y;
        detailGradient += ng * (invWidth * 1.5 * _ShoreNoise.y);
    }
    timeOffset *= _ShoreLife.w;
    details *= _ShoreLife.w;
    timeGradient *= _ShoreLife.w;
    detailGradient *= _ShoreLife.w;

    float baseDistance = _ShoreLife.x - d;
    float u = baseDistance - (frac(_WaveTime * _ShoreWave.z * invWidth) + timeOffset);
    float v = (baseDistance + details - frac(u)) * _ShoreLife.y;
    float2 ug = -dg * invWidth - timeGradient;
    float2 vg = (timeGradient + detailGradient) * _ShoreLife.y;
    float3 du, dv;
    float3 profile = OceanSampleShoreProfile(float2(u, v), du, dv);
    float3 px = du * ug.x + dv * vg.x;
    float3 pz = du * ug.y + dv * vg.y;
    float forward = profile.r * _ShoreShape.x;
    float height = profile.g * _ShoreWave.x;

    // Travel along the sampled beach slope, flattening towards the offshore direction.
    float3 horizontal = float3(inland.x, 0.0, inland.y);
    float3 travel = horizontal;
    float3 tx = float3(nx.x, 0.0, nx.y), tz = float3(nz.x, 0.0, nz.y);
    float prediction = _ShoreLife.z;
    if (distance + 0.1 < prediction)
    {
        float2 g0, g1;
        float h0 = OceanGround(p, g0);
        float h1 = OceanGround(p + inland * prediction, g1);
        float3 slope = float3(inland.x * prediction, h1 - h0, inland.y * prediction);
        float3 sx = float3(nx.x * prediction,
            dot(g1, float2(1.0, 0.0) + nx * prediction) - g0.x, nx.y * prediction);
        float3 sz = float3(nz.x * prediction,
            dot(g1, float2(0.0, 1.0) + nz * prediction) - g0.y, nz.y * prediction);
        float rawMagnitude = length(slope);
        float magnitude = max(rawMagnitude, 0.0001);
        slope /= magnitude;
        sx = rawMagnitude > 0.0001 ? OceanNormalizedDerivative(slope, sx, magnitude) : sx / magnitude;
        sz = rawMagnitude > 0.0001 ? OceanNormalizedDerivative(slope, sz, magnitude) : sz / magnitude;
        float a = saturate((distance + 0.1) / prediction);
        float2 ag = a > 0.0 ? dg / prediction : 0.0;
        travel = lerp(slope, horizontal, a);
        tx = lerp(sx, tx, a) + (horizontal - slope) * ag.x;
        tz = lerp(sz, tz, a) + (horizontal - slope) * ag.y;
    }
    offset = travel * forward + float3(0.0, height, 0.0);
    dx = tx * forward + travel * (px.r * _ShoreShape.x) + float3(0.0, px.g * _ShoreWave.x, 0.0);
    dz = tz * forward + travel * (pz.r * _ShoreShape.x) + float3(0.0, pz.g * _ShoreWave.x, 0.0);

    // Edge correction uses the DISPLACED XZ; include its Jacobian in the ground gradient.
    if (distance < 0.2 + _ShoreShape.z)
    {
        float2 gg;
        float ground = OceanGround(p + offset.xz, gg) - _SeaLevel - 0.05;
        // Bound the submerged skirt so a cliff cannot pull vertices arbitrarily far down.
        if (ground < -_ShoreShape.w) { ground = -_ShoreShape.w; gg = 0.0; }
        float2 groundGradient = float2(dot(gg, float2(1.0, 0.0) + dx.xz),
            dot(gg, float2(0.0, 1.0) + dz.xz));
        float fade = saturate((distance - 0.2) / _ShoreShape.z);
        float2 fg = (fade > 0.0 && fade < 1.0) ? dg / _ShoreShape.z : 0.0;
        float y = lerp(ground, offset.y, fade);
        float2 yg = lerp(groundGradient, float2(dx.y, dz.y), fade) + (offset.y - ground) * fg;
        if (y < offset.y) { offset.y = y; dx.y = yg.x; dz.y = yg.y; }
    }
    foam = saturate(profile.b * _ShoreShape.y);
}

void OceanEvaluateOffshoreWeight(OceanCoastSample coast, out float weight, out float2 gradient)
{
    weight = 1.0;
    gradient = 0.0;
    if (coast.valid < 0.5 || _ShoreNoise.w < 0.5) return;
    float width = TWO_PI / _ShoreWave.y;
    float range = max(_ShoreWave.w - width, 0.1);
    float u = saturate((coast.signedDistance - width) / range);
    weight = u * u * (3.0 - 2.0 * u);
    gradient = 6.0 * u * (1.0 - u) / range * coast.distanceGradient;
}

void OceanEvaluateSurface(
    float2 p,
    out float3 positionWS,
    out float3 normalWS,
    out float foam)
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

    OceanCoastSample coast = OceanSampleCoast(p);
    float weight;
    float2 weightGradient;
    OceanEvaluateOffshoreWeight(coast, weight, weightGradient);
    float3 shore, shoreX, shoreZ;
    OceanEvaluateShoreWave(p, coast, shore, shoreX, shoreZ, foam);

    positionWS = float3(p.x, _SeaLevel, p.y) + lerp(shore, displacement, weight);
    // Product rule for the full XYZ blend, including coast, LUT, noise and terrain derivatives.
    float3 tangentX = float3(1.0, 0.0, 0.0) + lerp(shoreX, derivativeX, weight)
        + (displacement - shore) * weightGradient.x;
    float3 tangentZ = float3(0.0, 0.0, 1.0) + lerp(shoreZ, derivativeZ, weight)
        + (displacement - shore) * weightGradient.y;
    foam *= 1.0 - weight;

    float3 n = cross(tangentZ, tangentX);
    float lengthSq = dot(n, n);
    normalWS = lengthSq > 1e-12 ? n * rsqrt(lengthSq) : float3(0.0, 1.0, 0.0);
}

void OceanEvaluatePatch(float2 uv, float4 rect, float4 edges,
    out float3 positionWS, out float3 normalWS, out float foam)
{
    float2 p = rect.xy + uv * rect.z;
    float cell = rect.z / _PatchCells;
    float2 vertex = round(uv * _PatchCells);
    float2 stepXZ = 0.0;
    if (_StitchEdges > 0.5)
    {
        bool edgeX = (vertex.x == 0.0 && edges.x > cell * 1.5)
            || (vertex.x == _PatchCells && edges.y > cell * 1.5);
        bool edgeZ = (vertex.y == 0.0 && edges.z > cell * 1.5)
            || (vertex.y == _PatchCells && edges.w > cell * 1.5);
        if (edgeX && fmod(vertex.y, 2.0) > 0.5) stepXZ.y = cell;
        if (edgeZ && fmod(vertex.x, 2.0) > 0.5) stepXZ.x = cell;
    }
    // Evaluate once normally, or the two COARSE endpoints for an odd fine-edge vertex.
    // One loop body also keeps the shader compiler from duplicating the whole evaluator five times.
    int count = any(stepXZ != 0.0) ? 2 : 1;
    positionWS = normalWS = 0.0;
    foam = 0.0;
    [loop]
    for (int i = 0; i < count; i++)
    {
        float3 position, normal;
        float mask;
        OceanEvaluateSurface(p + stepXZ * (i == 0 ? -1.0 : 1.0), position, normal, mask);
        positionWS += position / count;
        normalWS += normal / count;
        foam += mask / count;
    }
}

// Fragment API: geometricNormal comes from OceanEvaluatePatch/Surface. Scale is
// cycles/metre, strength is a dimensionless slope, and speed controls cycles/second.
// textureNormalTS is an unpacked XZ-mapped normal; pass (0,0,1) without a normal map.
// These are shading ripples: filtering never changes geometry or the LOD selection.
float3 OceanSurfaceNormal(float2 worldXZ, float3 geometricNormal, float time,
    float scale, float strength, float speed, float3 textureNormalTS)
{
    float lengthSq = dot(geometricNormal, geometricNormal);
    float3 normal = lengthSq > 1e-12
        ? geometricNormal * rsqrt(lengthSq) : float3(0.0, 1.0, 0.0);
    float2 d0 = float2(0.8, 0.6);
    float2 d1 = float2(-0.6, 0.8);
    float2 d2 = float2(0.28, -0.96);
    float3 phase = TWO_PI * (float3(dot(worldXZ, d0), dot(worldXZ, d1), dot(worldXZ, d2))
        * max(scale, 0.0) * float3(1.0, 1.73, 2.37) - time * speed * float3(0.31, 0.43, 0.57));
    float3 visible = 1.0 - smoothstep(1.0, PI, fwidth(phase));
    float3 slope = cos(phase) * visible * float3(0.55, 0.3, 0.15) * strength;
    float2 gradient = d0 * slope.x + d1 * slope.y + d2 * slope.z
        - textureNormalTS.xy / max(textureNormalTS.z, 0.2);
    float3 worldGradient = float3(gradient.x, 0.0, gradient.y);
    float3 surfaceGradient = worldGradient - normal * dot(normal, worldGradient);
    // Orthogonal projection preserves the geometric normal's hemisphere, including a curling lip.
    return normalize(normal - surfaceGradient);
}

// Fragment API: profileFoam is the interpolated output of OceanEvaluatePatch/Surface.
// This adds animated breakup to that mask; tint and opacity remain the material's choice.
float OceanSurfaceFoam(float profileFoam, float2 worldXZ, float time,
    float scale, float speed, float breakup)
{
    float2 phase = TWO_PI * (float2(dot(worldXZ, float2(0.8, 0.6)),
        dot(worldXZ, float2(-0.6, 0.8))) * max(scale, 0.0) * float2(1.0, 1.37)
        - time * speed * float2(0.19, 0.23));
    float2 visible = 1.0 - smoothstep(1.0, PI, fwidth(phase));
    float noise = 0.5 + dot(sin(phase) * visible, float2(0.25, 0.25));
    float coverage = saturate(profileFoam);
    float width = max(fwidth(coverage - noise), 0.02);
    float mask = smoothstep(noise - width, noise + width, coverage);
    return coverage * lerp(1.0, mask, saturate(breakup));
}

#endif
