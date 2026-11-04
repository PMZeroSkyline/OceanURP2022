using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[ExecuteAlways, DisallowMultipleComponent]
public class OceanDomain : MonoBehaviour
{
    public static readonly HashSet<OceanDomain> Active = new HashSet<OceanDomain>();
    public OceanSettings settings;
    public Material surfaceMaterial;
    public OceanHeightData bakedHeight;
    public bool animateInEditor = true;

    sealed class CameraDraw
    {
        public readonly OceanQuadtree tree = new OceanQuadtree();
        public readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
        public readonly uint[] args = new uint[5];
        public ComputeBuffer patches, arguments;

        public void Upload(Mesh mesh)
        {
            int count = tree.Patches.Count;
            if (arguments == null)
                arguments = new ComputeBuffer(1, sizeof(uint) * 5, ComputeBufferType.IndirectArguments);
            if (patches == null || patches.count < count)
            {
                patches?.Release();
                patches = new ComputeBuffer(Mathf.NextPowerOfTwo(count), OceanPatch.Stride);
                properties.SetBuffer("_Patches", patches);
            }
            patches.SetData(tree.Patches);
            args[0] = mesh.GetIndexCount(0);
            args[1] = (uint)count;
            args[2] = mesh.GetIndexStart(0);
            args[3] = (uint)mesh.GetBaseVertex(0);
            arguments.SetData(args);
        }

        public void Dispose() { patches?.Release(); arguments?.Release(); }
    }

    readonly Dictionary<Camera, CameraDraw> cameras = new Dictionary<Camera, CameraDraw>();
    readonly List<Camera> obsolete = new List<Camera>();
    readonly OceanQuadtree.ShoreField shoreField = new OceanQuadtree.ShoreField();
    readonly Vector4[] waveShapes = new Vector4[OceanSettings.MaxWaves];
    readonly Vector4[] waveMotions = new Vector4[OceanSettings.MaxWaves];
    Mesh patchMesh;
    double lastTime;
    float waveTime;

    public Texture2D HeightTexture => bakedHeight != null ? bakedHeight.Height : null;
    public Texture2D CoastTexture => bakedHeight != null ? bakedHeight.Coast : null;
    public bool HasValidHeight => bakedHeight != null && bakedHeight.Matches(settings, transform.position);
    public bool HasValidCoast => HasValidHeight && bakedHeight.HasCoast;

    void OnEnable()
    {
        patchMesh = CreatePatchMesh();
        lastTime = Time.realtimeSinceStartupAsDouble;
        Active.Add(this);
    }

    void OnDisable()
    {
        Active.Remove(this);
        foreach (CameraDraw draw in cameras.Values) draw.Dispose();
        cameras.Clear();
        if (patchMesh != null)
        {
            if (Application.isPlaying) Destroy(patchMesh);
            else DestroyImmediate(patchMesh);
        }
        patchMesh = null;
        shoreField.Update(null, 0);
    }

    void Update()
    {
        double now = Time.realtimeSinceStartupAsDouble;
        if (Application.isPlaying) waveTime = Time.time;
        else if (animateInEditor) waveTime += (float)(now - lastTime);
        lastTime = now;
        obsolete.Clear();
        foreach (var pair in cameras)
            if (pair.Key == null) obsolete.Add(pair.Key);
        foreach (Camera camera in obsolete)
        {
            cameras[camera].Dispose();
            cameras.Remove(camera);
        }
    }

    public void Draw(CommandBuffer cmd, Camera camera)
    {
        if (settings == null || surfaceMaterial == null || patchMesh == null) return;
        if (!cameras.TryGetValue(camera, out CameraDraw draw))
            cameras.Add(camera, draw = new CameraDraw());

        int waveCount = settings.WriteWaveData(waveShapes, waveMotions, out Vector3 extent);
        bool height = HasValidHeight, coast = height && bakedHeight.HasCoast;
        Texture2D lut = settings.shoreLut;
        bool hasLut = lut != null && lut.width >= 2 && lut.height >= 2 && lut.format == TextureFormat.RGBAFloat;
        bool shore = coast && hasLut;
        float width = Mathf.Max(.1f, settings.shoreWavelength);
        Vector4 wave = settings.GetShoreWaveData();
        wave.x *= shore ? width : 0f;
        var shape = new Vector4(shore ? Mathf.Max(0, settings.shoreForward) * width : 0,
            shore ? Mathf.Max(0, settings.shoreFoam) : 0, Mathf.Max(.1f, settings.shoreFade), 0);
        // Absolute Catmull-Rom weights sum to at most 1.25 per axis.
        float forwardBound = shape.x * Mathf.Max(0, settings.shoreLutMaxForward) * 1.5625f;
        shape.w = forwardBound + wave.x * Mathf.Max(0, settings.shoreLutMaxHeight) * 1.5625f + .1f;
        extent += new Vector3(forwardBound, shape.w, forwardBound);

        Vector3 center = transform.position;
        float size = Mathf.Max(1, settings.size);
        shoreField.Update(shore ? CoastTexture : null, size);
        draw.tree.Build(camera, settings, center, extent, shoreField);
        if (draw.tree.Patches.Count == 0) return;
        draw.Upload(patchMesh);

        MaterialPropertyBlock block = draw.properties;
        block.SetFloat("_SeaLevel", center.y);
        block.SetFloat("_PatchCells", OceanSettings.Cells);
        block.SetInteger("_WaveCount", waveCount);
        block.SetVectorArray("_WaveShapes", waveShapes);
        block.SetVectorArray("_WaveMotions", waveMotions);
        block.SetFloat("_WaveTime", waveTime);
        block.SetFloat("_StitchEdges", settings.stitchEdges ? 1 : 0);
        block.SetVector("_OceanRect", new Vector4(center.x - size * .5f, center.z - size * .5f, size, 1f / size));
        block.SetFloat("_HasHeightMap", height ? 1 : 0);
        block.SetFloat("_HasCoastMap", coast ? 1 : 0);
        block.SetTexture("_OceanHeightMap", height ? HeightTexture : Texture2D.blackTexture);
        block.SetTexture("_OceanCoastMap", coast ? CoastTexture : Texture2D.blackTexture);
        block.SetTexture("_OceanShoreLut", hasLut ? lut : Texture2D.blackTexture);
        block.SetTexture("_OceanShoreNoise", settings.shoreNoise != null ? settings.shoreNoise : Texture2D.grayTexture);
        block.SetVector("_ShoreWave", wave);
        block.SetVector("_ShoreShape", shape);
        block.SetVector("_ShoreLife", new Vector4(settings.shorePhaseOffset, Mathf.Max(.01f, settings.shoreLifeRate),
            Mathf.Max(.01f, settings.shoreGroundPrediction), Mathf.Clamp(settings.shoreVariation, 0, 2)));
        block.SetVector("_ShoreNoise", new Vector4(Mathf.Max(0, settings.shoreNoisePhase), Mathf.Max(0, settings.shoreNoiseLife),
            settings.shoreNoise != null ? 1 : 0, shore ? 1 : 0));

        // Direct material binding makes edits and material swaps immediately visible.
        surfaceMaterial.enableInstancing = true;
        cmd.DrawMeshInstancedIndirect(patchMesh, 0, surfaceMaterial, 0, draw.arguments, 0, block);
    }

    static Mesh CreatePatchMesh()
    {
        const int cells = OceanSettings.Cells, stride = cells + 1;
        var vertices = new Vector3[stride * stride];
        var indices = new int[cells * cells * 6];
        for (int z = 0; z <= cells; z++)
        for (int x = 0; x <= cells; x++)
            vertices[z * stride + x] = new Vector3(x / (float)cells, 0, z / (float)cells);
        int i = 0;
        for (int z = 0; z < cells; z++)
        for (int x = 0; x < cells; x++)
        {
            int a = z * stride + x, b = a + stride;
            indices[i++] = a; indices[i++] = b; indices[i++] = a + 1;
            indices[i++] = a + 1; indices[i++] = b; indices[i++] = b + 1;
        }
        var mesh = new Mesh { name = "Ocean Patch", hideFlags = HideFlags.HideAndDontSave, vertices = vertices, triangles = indices };
        mesh.UploadMeshData(true);
        return mesh;
    }
}
