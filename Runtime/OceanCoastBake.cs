using System;
using UnityEngine;

public static class OceanCoastBake
{
    public static int GetPassCount(int size)
    {
        // final extra step = 1。
        int count = 1;

        for (int step = Mathf.NextPowerOfTwo(size) / 2; step > 0; step >>= 1)
        {
            count++;
        }

        return count;
    }

    public static RenderTexture Build(
        ComputeShader shader,
        Texture2D height,
        float seaLevel,
        int passCount)
    {
        if (shader == null || height == null)
        {
            throw new ArgumentException("Assign a compute shader and height texture.");
        }

        if (height.width != height.height)
        {
            throw new ArgumentException("The ocean height texture must be square.");
        }

        int size = height.width;

        passCount = Mathf.Clamp(passCount, 0, GetPassCount(size));

        int initKernel = shader.FindKernel("InitSeeds");
        int jumpKernel = shader.FindKernel("JumpFlood");

        RenderTexture read = null;
        RenderTexture write = null;

        try
        {
            read = CreateTarget(size, "Ocean Coast A");
            write = CreateTarget(size, "Ocean Coast B");

            shader.SetInts("_Size", size, size);
            shader.SetFloat("_SeaLevel", seaLevel);

            shader.SetTexture(initKernel, "_OceanHeightMap", height);

            shader.SetTexture(initKernel, "_Write", read);

            Dispatch(shader, initKernel, size);

            int completed = 0;

            for (int step = Mathf.NextPowerOfTwo(size) / 2; step > 0 && completed < passCount; step >>= 1)
            {
                Jump(step);
                completed++;
            }

            // JFA+1
            if (completed < passCount)
                Jump(1);

            RenderTexture result = read;

            read = null;

            return result;
        }
        finally
        {
            Release(read);
            Release(write);
        }

        void Jump(int step)
        {
            shader.SetInt("_Step", step);

            shader.SetTexture(jumpKernel, "_Read", read);
            shader.SetTexture(jumpKernel, "_Write", write);

            Dispatch(shader, jumpKernel, size);

            RenderTexture swap = read;
            read = write;
            write = swap;
        }
    }

    static void Dispatch(ComputeShader shader, int kernel, int size)
    {
        shader.GetKernelThreadGroupSizes(kernel, out uint x, out uint y, out _);
        shader.Dispatch(kernel, (size + (int)x - 1) / (int)x, (size + (int)y - 1) / (int)y, 1);
    }

    static RenderTexture CreateTarget(int size, string name)
    {
        var texture = new RenderTexture(size, size, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
        {
            name = name,
            hideFlags = HideFlags.HideAndDontSave,
            enableRandomWrite = true,
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            useMipMap = false,
            autoGenerateMips = false
        };

        try
        {
            if (!texture.Create())
            {
                throw new InvalidOperationException(
                    "Cannot create coast RT.");
            }

            return texture;
        }
        catch
        {
            Release(texture);
            throw;
        }
    }

    public static void Release(RenderTexture texture)
    {
        if (texture == null)
            return;

        texture.Release();

        if (Application.isPlaying)
            UnityEngine.Object.Destroy(texture);
        else
            UnityEngine.Object.DestroyImmediate(texture);
    }
}