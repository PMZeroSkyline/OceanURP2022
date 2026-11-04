using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public class OceanHeightBake : IDisposable
{
    public RenderTexture Texture { get; private set; }
    public Vector2 Range { get; private set; }

    public static OceanHeightBake Capture(OceanSettings settings, Transform owner)
    {
        Shader shader = Shader.Find("Hidden/Ocean/HeightBake");
        if (shader == null)
            throw new InvalidOperationException("Cannot find Hidden/Ocean/HeightBake.");

        Vector3 center = owner.position;
        float size = Mathf.Max(1f, settings.size);
        int resolution = Mathf.Clamp(settings.bakeResolution, 16, 2048);
        int layers = settings.groundLayers.value;
        var result = new OceanHeightBake
        {
            Range = new Vector2(
                center.y - Mathf.Max(.1f, settings.captureBelow),
                center.y + Mathf.Max(.1f, settings.captureAbove))
        };
        Material material = null;
        CommandBuffer cmd = null;
        var temporaryMeshes = new List<Mesh>();
        RenderTexture previous = RenderTexture.active;
        try
        {
            result.Texture = CreateTarget(resolution, RenderTextureFormat.RFloat,
                FilterMode.Bilinear, false, "Ocean Ground Height");
            material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            material.SetVector("_OceanRect", new Vector4(
                center.x - size * .5f, center.z - size * .5f, size, 1f / size));
            material.SetVector("_HeightRange", new Vector4(result.Range.x, result.Range.y, 0f, 0f));

            cmd = new CommandBuffer { name = "Ocean Height Bake" };
            cmd.SetRenderTarget(result.Texture);
            cmd.ClearRenderTarget(false, true, new Color(result.Range.x, 0f, 0f, 0f));
            var volume = new Bounds(
                new Vector3(center.x, (result.Range.x + result.Range.y) * .5f, center.z),
                new Vector3(size, result.Range.y - result.Range.x, size));

            foreach (MeshRenderer renderer in Object.FindObjectsOfType<MeshRenderer>())
            {
                if (!Include(renderer.gameObject) || !renderer.enabled
                    || renderer.forceRenderingOff || !volume.Intersects(renderer.bounds))
                    continue;
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null)
                    Draw(filter.sharedMesh, renderer.localToWorldMatrix, cmd, material);
            }
            foreach (Terrain terrain in Object.FindObjectsOfType<Terrain>())
            {
                if (!Include(terrain.gameObject) || !terrain.enabled
                    || !terrain.drawHeightmap || terrain.terrainData == null)
                    continue;
                TerrainData data = terrain.terrainData;
                var bounds = new Bounds(terrain.transform.position + data.size * .5f, data.size);
                if (!volume.Intersects(bounds))
                    continue;
                Mesh mesh = BuildTerrainMesh(data);
                temporaryMeshes.Add(mesh);
                Draw(mesh, terrain.transform.localToWorldMatrix, cmd, material);
            }
            Graphics.ExecuteCommandBuffer(cmd);
            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
        finally
        {
            RenderTexture.active = previous;
            cmd?.Release();
            DestroyObject(material);
            foreach (Mesh mesh in temporaryMeshes)
                DestroyObject(mesh);
        }

        bool Include(GameObject obj) => obj.scene == owner.gameObject.scene
            && obj.activeInHierarchy && (layers & (1 << obj.layer)) != 0;
    }

    static void Draw(Mesh mesh, Matrix4x4 matrix, CommandBuffer cmd, Material material)
    {
        for (int sub = 0; sub < mesh.subMeshCount; sub++)
            cmd.DrawMesh(mesh, matrix, material, sub, 0);
    }

    static Mesh BuildTerrainMesh(TerrainData data)
    {
        int n = data.heightmapResolution, cells = n - 1;
        int holeResolution = data.holesResolution;
        float[,] heights = data.GetHeights(0, 0, n, n);
        bool[,] surface = data.GetHoles(0, 0, holeResolution, holeResolution);
        var vertices = new Vector3[n * n];
        var triangles = new List<int>(cells * cells * 6);
        for (int z = 0; z < n; z++)
        for (int x = 0; x < n; x++)
            vertices[z * n + x] = new Vector3(x / (float)cells * data.size.x,
                heights[z, x] * data.size.y, z / (float)cells * data.size.z);

        for (int z = 0; z < cells; z++)
        for (int x = 0; x < cells; x++)
        {
            if (!surface[z * holeResolution / cells, x * holeResolution / cells])
                continue;
            int a = z * n + x, b = a + n;
            triangles.Add(a); triangles.Add(b); triangles.Add(b + 1);
            triangles.Add(a); triangles.Add(b + 1); triangles.Add(a + 1);
        }
        var mesh = new Mesh
        {
            name = "Ocean Terrain Capture",
            hideFlags = HideFlags.HideAndDontSave,
            indexFormat = IndexFormat.UInt32
        };
        try
        {
            mesh.vertices = vertices;
            mesh.SetTriangles(triangles, 0);
            return mesh;
        }
        catch
        {
            DestroyObject(mesh);
            throw;
        }
    }

    internal static RenderTexture CreateTarget(int size, RenderTextureFormat format,
        FilterMode filter, bool randomWrite, string name)
    {
        var texture = new RenderTexture(size, size, 0, format, RenderTextureReadWrite.Linear)
        {
            name = name,
            hideFlags = HideFlags.HideAndDontSave,
            enableRandomWrite = randomWrite,
            filterMode = filter,
            wrapMode = TextureWrapMode.Clamp,
            useMipMap = false,
            autoGenerateMips = false
        };
        try
        {
            if (!texture.Create())
                throw new InvalidOperationException("Cannot create " + name + ".");
            return texture;
        }
        catch
        {
            OceanCoastBake.Release(texture);
            throw;
        }
    }

    public void Dispose()
    {
        OceanCoastBake.Release(Texture);
        Texture = null;
    }

    internal static void DestroyObject(Object obj)
    {
        if (obj == null) return;
        if (Application.isPlaying) Object.Destroy(obj);
        else Object.DestroyImmediate(obj);
    }
}

public static class OceanCoastBake
{
    public static int GetPassCount(int size)
    {
        int count = 1; // Final extra step = 1 (JFA+1).
        for (int step = Mathf.NextPowerOfTwo(size) / 2; step > 0; step >>= 1)
            count++;
        return count;
    }

    public static RenderTexture Build(
        ComputeShader shader, Texture2D height, float seaLevel, int passCount)
    {
        if (shader == null || height == null)
            throw new ArgumentException("Assign a compute shader and height texture.");
        if (height.width != height.height)
            throw new ArgumentException("The ocean height texture must be square.");

        int size = height.width;
        passCount = Mathf.Clamp(passCount, 0, GetPassCount(size));
        int initKernel = shader.FindKernel("InitSeeds");
        int jumpKernel = shader.FindKernel("JumpFlood");
        RenderTexture read = null, write = null;
        try
        {
            read = OceanHeightBake.CreateTarget(size, RenderTextureFormat.ARGBFloat,
                FilterMode.Point, true, "Ocean Coast A");
            write = OceanHeightBake.CreateTarget(size, RenderTextureFormat.ARGBFloat,
                FilterMode.Point, true, "Ocean Coast B");
            shader.SetInts("_Size", size, size);
            shader.SetFloat("_SeaLevel", seaLevel);
            shader.SetTexture(initKernel, "_OceanHeightMap", height);
            shader.SetTexture(initKernel, "_Write", read);
            Dispatch(shader, initKernel, size);

            int completed = 0;
            for (int step = Mathf.NextPowerOfTwo(size) / 2;
                step > 0 && completed < passCount; step >>= 1)
            {
                Jump(step);
                completed++;
            }
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

    public static void Release(RenderTexture texture)
    {
        if (texture == null) return;
        texture.Release();
        OceanHeightBake.DestroyObject(texture);
    }
}
