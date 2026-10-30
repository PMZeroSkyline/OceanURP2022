using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public class OceanHeightBake : IDisposable
{
    public RenderTexture Texture { get; private set; }
    public Vector2 Range { get; private set; }
    int layers;
    public static OceanHeightBake Capture(OceanSettings settings, Transform owner)
    {
        Shader shader =
            Shader.Find("Hidden/Ocean/HeightBake");

        if (shader == null)
        {
            throw new InvalidOperationException(
                "Cannot find Hidden/Ocean/HeightBake.");
        }

        Vector3 center = owner.position;
        float size = Mathf.Max(1f, settings.size);

        int resolution = Mathf.Clamp(
            settings.bakeResolution, 16, 2048);

        var result = new OceanHeightBake
        {
            layers = settings.groundLayers.value,

            Range = new Vector2(
                center.y - Mathf.Max(0.1f, settings.captureBelow),
                center.y + Mathf.Max(0.1f, settings.captureAbove))
        };

        Material material = null;
        CommandBuffer cmd = null;

        var temporaryMeshes = new List<Mesh>();
        RenderTexture previous = RenderTexture.active;

        try
        {
            result.Texture = new RenderTexture(
                resolution,
                resolution,
                0,
                RenderTextureFormat.RFloat,
                RenderTextureReadWrite.Linear)
            {
                name = "Ocean Ground Height",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false
            };

            if (!result.Texture.Create())
            {
                throw new InvalidOperationException(
                    "Cannot create height RT.");
            }

            material = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };

            material.SetVector("_OceanRect", new Vector4(
                center.x - size * 0.5f,
                center.z - size * 0.5f,
                size,
                1f / size));

            material.SetVector("_HeightRange", new Vector4(
                result.Range.x,
                result.Range.y,
                0f,
                0f));

            cmd = new CommandBuffer
            {
                name = "Ocean Height Bake"
            };

            cmd.SetRenderTarget(result.Texture);

            cmd.ClearRenderTarget(
                false,
                true,
                new Color(result.Range.x, 0f, 0f, 0f));

            var volume = new Bounds(
                new Vector3(
                    center.x,
                    (result.Range.x + result.Range.y) * 0.5f,
                    center.z),
                new Vector3(
                    size,
                    result.Range.y - result.Range.x,
                    size));

            foreach (MeshRenderer renderer
                in Object.FindObjectsOfType<MeshRenderer>())
            {
                if (!Include(renderer.gameObject)
                    || !renderer.enabled
                    || renderer.forceRenderingOff
                    || !volume.Intersects(renderer.bounds))
                {
                    continue;
                }

                MeshFilter filter =
                    renderer.GetComponent<MeshFilter>();

                if (filter != null && filter.sharedMesh != null)
                {
                    Draw(
                        filter.sharedMesh,
                        renderer.localToWorldMatrix,
                        cmd,
                        material);
                }
            }

            foreach (Terrain terrain
                in Object.FindObjectsOfType<Terrain>())
            {
                if (!Include(terrain.gameObject)
                    || !terrain.enabled
                    || !terrain.drawHeightmap
                    || terrain.terrainData == null)
                {
                    continue;
                }

                TerrainData data = terrain.terrainData;

                var bounds = new Bounds(
                    terrain.transform.position + data.size * 0.5f,
                    data.size);

                if (!volume.Intersects(bounds))
                    continue;

                Mesh mesh = BuildTerrainMesh(data);
                temporaryMeshes.Add(mesh);

                Draw(
                    mesh,
                    terrain.transform.localToWorldMatrix,
                    cmd,
                    material);
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

        bool Include(GameObject obj)
        {
            return obj.scene == owner.gameObject.scene
                && obj.activeInHierarchy
                && (result.layers & (1 << obj.layer)) != 0;
        }
    }

    static void Draw(
        Mesh mesh,
        Matrix4x4 matrix,
        CommandBuffer cmd,
        Material material)
    {
        for (int sub = 0; sub < mesh.subMeshCount; sub++)
            cmd.DrawMesh(mesh, matrix, material, sub, 0);
    }

    static Mesh BuildTerrainMesh(TerrainData data)
    {
        int n = data.heightmapResolution;
        int cells = n - 1;
        int holeResolution = data.holesResolution;

        float[,] heights =
            data.GetHeights(0, 0, n, n);

        bool[,] surface = data.GetHoles(
            0, 0, holeResolution, holeResolution);

        var vertices = new Vector3[n * n];
        var triangles = new List<int>(cells * cells * 6);

        for (int z = 0; z < n; z++)
        {
            for (int x = 0; x < n; x++)
            {
                vertices[z * n + x] = new Vector3(
                    x / (float)cells * data.size.x,
                    heights[z, x] * data.size.y,
                    z / (float)cells * data.size.z);
            }
        }

        for (int z = 0; z < cells; z++)
        {
            for (int x = 0; x < cells; x++)
            {
                if (!surface[
                    z * holeResolution / cells,
                    x * holeResolution / cells])
                {
                    continue;
                }

                int a = z * n + x;
                int b = a + n;

                triangles.Add(a);
                triangles.Add(b);
                triangles.Add(b + 1);

                triangles.Add(a);
                triangles.Add(b + 1);
                triangles.Add(a + 1);
            }
        }

        var mesh = new Mesh
        {
            name = "Ocean Terrain Capture",
            hideFlags = HideFlags.HideAndDontSave,
            indexFormat = IndexFormat.UInt32
        };

        mesh.vertices = vertices;
        mesh.SetTriangles(triangles, 0);

        return mesh;
    }

    public void Dispose()
    {
        if (Texture == null)
            return;

        Texture.Release();
        DestroyObject(Texture);
        Texture = null;
    }

    static void DestroyObject(Object obj)
    {
        if (obj == null)
            return;

        if (Application.isPlaying)
            Object.Destroy(obj);
        else
            Object.DestroyImmediate(obj);
    }
}