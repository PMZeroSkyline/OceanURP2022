using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public class OceanDomain : MonoBehaviour
{
    public static HashSet<OceanDomain> Active = new HashSet<OceanDomain>();

    public static readonly int PatchesId = Shader.PropertyToID("_Patches");
    public static readonly int SeaLevelId = Shader.PropertyToID("_SeaLevel");
    public static readonly int PatchCellsId = Shader.PropertyToID("_PatchCells");

    public static readonly int WaveId = Shader.PropertyToID("_Wave");
    public static readonly int WaveSpeedId = Shader.PropertyToID("_WaveSpeed");
    public static readonly int WaveTimeId = Shader.PropertyToID("_WaveTime");
    public static readonly int StitchEdgesId = Shader.PropertyToID("_StitchEdges");
    public static readonly int DebugViewId = Shader.PropertyToID("_DebugView");

    class CameraDraw
    {
        public OceanQuadtree tree = new OceanQuadtree();
        public MaterialPropertyBlock properties = new MaterialPropertyBlock();
        public uint[] args = new uint[5];
        public ComputeBuffer patches;
        public ComputeBuffer arguments;

        public void EnsureCapacity(int count)
        {
            if (arguments == null)
            {
                arguments = new ComputeBuffer(1, sizeof(uint) * 5, ComputeBufferType.IndirectArguments);
            }
            if (patches != null && patches.count >= count)
            {
                return;
            }
            patches?.Release();
            patches = new ComputeBuffer(Mathf.NextPowerOfTwo(count), OceanPatch.Stride, ComputeBufferType.Structured);
            properties.SetBuffer(PatchesId, patches);
        }
        public void Dispose()
        {
            patches?.Release();
            arguments?.Release();
            patches = null;
            arguments = null;
        }
    }

    [SerializeField] OceanSettings settings;
    Dictionary<Camera, CameraDraw> cameras = new Dictionary<Camera, CameraDraw>();
    List<Camera> obsolete = new List<Camera>();

    // [SerializeField] Material surfaceMaterial;    
    Mesh patchMesh;
    Material runtimeMaterial;
    // ComputeBuffer patchBuffer;
    // ComputeBuffer argsBuffer;

    // [SerializeField, Min(0.01f)] float size = 2f;



    void OnEnable()
    {
        if (settings == null || settings.surfaceMaterial == null)
        {
            Debug.LogError("No Settings Or Surface Material", this);
            return;
        }

        patchMesh = CreatePatchMesh(OceanSettings.Cells);

        runtimeMaterial = new Material(settings.surfaceMaterial)
        {
            name = "Ocean Runtime Material",
            enableInstancing = true
        };
        Active.Add(this);
    }
    void OnDisable()
    {
        Active.Remove(this);

        foreach (CameraDraw draw in cameras.Values)
        {
            draw.Dispose();
        }

        cameras.Clear();
        obsolete.Clear();

        if (patchMesh != null)
            Destroy(patchMesh);

        if (runtimeMaterial != null)
            Destroy(runtimeMaterial);

        patchMesh = null;
        runtimeMaterial = null;
    }
    Mesh CreatePatchMesh(int cells)
    {
        int stride = cells + 1;
        int vertexCount = stride * stride;

        var vertices = new Vector3[vertexCount];
        var normals = new Vector3[vertexCount];

        for (int z = 0; z <= cells; z++)
        {
            for (int x = 0; x <= cells; x++)
            {
                int vertex = z * stride + x;
                vertices[vertex] = new Vector3(
                    x / (float)cells,
                    0f,
                    z / (float)cells
                );
                normals[vertex] = Vector3.up;
            }
        }

        var indices = new int[cells * cells * 6];
        int index = 0;

        for (int z = 0; z < cells; z++)
        {
            for (int x = 0; x < cells; x++)
            {
                int a = z * stride + x;
                int b = a + stride;

                indices[index++] = a;
                indices[index++] = b;
                indices[index++] = a + 1;

                indices[index++] = a + 1;
                indices[index++] = b;
                indices[index++] = b + 1;
            }
        }

        return new Mesh
        {
            name = "Ocean Patch",
            vertices = vertices,
            normals = normals,
            triangles = indices
        };
    }
    public void Draw(CommandBuffer cmd, Camera camera)
    {
        if (settings == null || runtimeMaterial == null)
        {
            return;
        }

        if (!cameras.TryGetValue(camera, out CameraDraw draw))
        {
            draw = new CameraDraw();
            cameras.Add(camera, draw);
        }

        draw.tree.Build(camera, settings, transform.position);
        int count = draw.tree.Patches.Count;

        if (count == 0)
            return;

        draw.EnsureCapacity(count);
        draw.patches.SetData(draw.tree.Patches);
        draw.args[0] = patchMesh.GetIndexCount(0);
        draw.args[1] = (uint)count;
        draw.args[2] = patchMesh.GetIndexStart(0);
        draw.args[3] = (uint)patchMesh.GetBaseVertex(0);
        draw.args[4] = 0u;

        draw.arguments.SetData(draw.args);
        draw.properties.SetFloat(SeaLevelId, transform.position.y);
        draw.properties.SetFloat(PatchCellsId, OceanSettings.Cells);

        float angle = settings.waveDirection * Mathf.Deg2Rad;
        float amplitude = Mathf.Max(0f, settings.waveAmplitude);
        float waveNumber = 2f * Mathf.PI / Mathf.Max(0.1f, settings.waveLength);

        draw.properties.SetVector(WaveId, new Vector4(Mathf.Cos(angle), Mathf.Sign(angle), amplitude, waveNumber));
        draw.properties.SetFloat(WaveSpeedId, settings.waveSpeed);
        draw.properties.SetFloat(StitchEdgesId, settings.stitchEdges ? 1f : 0f);
        draw.properties.SetVector(DebugViewId, new Vector4(settings.showLod ? 1f : 0f, settings.showGrid ? 1f : 0f, settings.showEdges ? 1f : 0f, settings.showNormals ? 1f : 0f));

        cmd.DrawMeshInstancedIndirect(
            patchMesh,
            0,
            runtimeMaterial,
            0,
            draw.arguments,
            0,
            draw.properties
        );
    }
    void LateUpdate()
    {
        obsolete.Clear();
        foreach (var pair in cameras)
        {
            if (pair.Key == null)
                obsolete.Add(pair.Key);
        }
        foreach (Camera camera in obsolete)
        {
            cameras[camera].Dispose();
            cameras.Remove(camera);
        }
    }
}