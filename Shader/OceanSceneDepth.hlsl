#ifndef OCEAN_SCENE_DEPTH_INCLUDED
#define OCEAN_SCENE_DEPTH_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

bool OceanTryGetScenePosition(
    float2 screenUV,
    out float3 scenePositionWS)
{
    scenePositionWS = float3(0.0, 0.0, 0.0);

    float rawDepth = SampleSceneDepth(screenUV);

#if UNITY_REVERSED_Z
    if (rawDepth <= 0.0)
        return false;

    float deviceDepth = rawDepth;
#else
    if (rawDepth >= 1.0)
        return false;

    float deviceDepth = lerp(
        UNITY_NEAR_CLIP_VALUE,
        1.0,
        rawDepth);
#endif

    scenePositionWS = ComputeWorldSpacePosition(
        screenUV,
        deviceDepth,
        UNITY_MATRIX_I_VP);

    return true;
}

float OceanGetSceneDistance(
    float3 surfacePositionWS,
    float3 scenePositionWS)
{
    // Away from the camera; handles perspective and orthographic views.
    float3 rayDirectionWS = normalize(
        -GetWorldSpaceViewDir(surfacePositionWS));

    return max(0.0, dot(
        scenePositionWS - surfacePositionWS,
        rayDirectionWS));
}

#endif