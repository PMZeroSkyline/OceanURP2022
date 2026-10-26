using UnityEngine;

[CreateAssetMenu(menuName = "Ocean/Ocean Settings")]
public class OceanSettings : ScriptableObject
{
    public const int Cells = 16;
    public Material surfaceMaterial;

    [Header("Domain and LOD")]
    [Min(1f)] public float size = 1000f;
    [Range(0, 10)] public int maxDepth = 8;
    [Range(1f, 64f)] public float pixelsPerCell = 12f;
    [Range(1, 1024)] public int maxPatches = 512;

    [Header("Wave")]
    [Min(0f)] public float waveAmplitude = 2f;
    [Min(0.1f)] public float waveLength = 32f;
    public float waveSpeed = 4f;
    [Range(0f, 360f)] public float waveDirection = 35f;
    public bool stitchEdges = true;

    [Header("Debug")]
    public bool showLod = true;
    public bool showGrid = true;
    public bool showEdges = true;
    public bool showNormals = false;

}