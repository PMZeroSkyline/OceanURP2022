using UnityEngine;

[CreateAssetMenu(menuName = "Ocean/Ocean Settings")]
public class OceanSettings : ScriptableObject
{
    public const int Cells = 16;
    public const int MaxWaves = 4;
    const float HorizontalSlopeLimit = 0.8f;

    [System.Serializable]
    public struct Wave
    {
        [Min(0f)] public float amplitude;
        [Min(0.1f)] public float wavelength;
        public float speed;
        [Range(0f, 360f)] public float direction;
        [Range(0f, 1f)] public float horizontalRatio;
    }

    public Material surfaceMaterial;

    [Header("Domain and LOD")]
    [Min(1f)] public float size = 1000f;
    [Range(0, 10)] public int maxDepth = 8;
    [Range(1f, 64f)] public float pixelsPerCell = 12f;
    [Range(1, 1024)] public int maxPatches = 512;

    [Header("Waves")]
    [Tooltip("最多使用前 4 组波。振幅为 0 时，该组不产生位移。")]
    public Wave[] waves =
    {
        new Wave
        {
            amplitude = 2f,
            wavelength = 24f,
            speed = 4f,
            direction = 35f,
            horizontalRatio = 0.8f
        },
        new Wave
        {
            amplitude = 0.7f,
            wavelength = 15f,
            speed = 3f,
            direction = 105f,
            horizontalRatio = 0.7f
        },
        new Wave
        {
            amplitude = 0.25f,
            wavelength = 9f,
            speed = 2f,
            direction = 165f,
            horizontalRatio = 0.6f
        }
    };
    public bool stitchEdges = true;

    [Header("Debug")]
    public bool showLod = true;
    public bool showGrid = true;
    public bool showEdges = true;
    public bool showNormals = false;

    public int WriteWaveData(Vector4[] shapes, Vector4[] motions, out Vector3 displacementExtent)
    {
        int count = Mathf.Min(waves == null ? 0 : waves.Length, MaxWaves);

        float totalSlope = 0f;

        for (int i = 0; i < count; i++)
        {
            Wave wave = waves[i];

            float angle = wave.direction * Mathf.Deg2Rad;
            float amplitude = Mathf.Max(0f, wave.amplitude);
            float k = 2f * Mathf.PI / Mathf.Max(0.1f, wave.wavelength);

            float horizontal = amplitude * Mathf.Clamp01(wave.horizontalRatio);

            shapes[i] = new Vector4(Mathf.Cos(angle), Mathf.Sin(angle), amplitude, k);

            motions[i] = new Vector4(horizontal, wave.speed, 0f, 0f);

            totalSlope += horizontal * k;
        }

        float scale = totalSlope > HorizontalSlopeLimit ? HorizontalSlopeLimit / totalSlope : 1f;

        displacementExtent = Vector3.zero;

        for (int i = 0; i < count; i++)
        {
            Vector4 shape = shapes[i];
            Vector4 motion = motions[i];

            motion.x *= scale;
            motions[i] = motion;

            displacementExtent += new Vector3(Mathf.Abs(shape.x) * motion.x, shape.z, Mathf.Abs(shape.y) * motion.x);
        }

        return count;
    }
}
