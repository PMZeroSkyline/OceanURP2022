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


    [Header("Height Bake")]
    public LayerMask groundLayers = ~0;
    [Range(16, 2048)] public int bakeResolution = 512;
    [Min(0.1f)] public float captureBelow = 50f;
    [Min(0.1f)] public float captureAbove = 50f;

    public enum CoastView
    {
        Distance,
        OffshoreDirection,
        NearestUV,
        LandWater
    }
    [Header("Coast Bake")]
    public ComputeShader coastCompute;

    [Header("Coast Preview")]
    public CoastView coastView = CoastView.Distance;

    [Min(0.01f)] public float coastDisplayRange = 100f;
    [Min(0.01f)] public float coastContourSpacing = 10f;

    [Header("Shore Wave")]
    [Min(0f)] public float shoreAmplitude = 1f;
    [Min(0.1f)] public float shoreWavelength = 20f;
    public float shoreSpeed = 2f;
    [Min(0.1f)] public float shoreRange = 60f;

    [Header("Shore LUT")]
    public Vector2Int shoreLutResolution = new Vector2Int(256, 64);

    public AnimationCurve shoreProfile = new AnimationCurve(
        new Keyframe(0f, 0f, 2f * Mathf.PI, 2f * Mathf.PI),
        new Keyframe(0.25f, 1f, 0f, 0f),
        new Keyframe(0.5f, 0f, -2f * Mathf.PI, -2f * Mathf.PI),
        new Keyframe(0.75f, -1f, 0f, 0f),
        new Keyframe(1f, 0f, 2f * Mathf.PI, 2f * Mathf.PI));

    public AnimationCurve shoreEnvelope = new AnimationCurve(
        new Keyframe(0f, 0f, 0f, 0f),
        new Keyframe(0.5f, 1f, 0f, 0f),
        new Keyframe(1f, 0f, 0f, 0f));

    [HideInInspector] public Texture2D shoreLut;
    [HideInInspector] public float shoreLutMaxHeight;


    public Vector4 GetShoreWaveData()
    {
        return new Vector4(
            Mathf.Max(0f, shoreAmplitude),
            2f * Mathf.PI / Mathf.Max(0.1f, shoreWavelength),
            shoreSpeed,
            Mathf.Max(0.1f, shoreRange));
    }
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
