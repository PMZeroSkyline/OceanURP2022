using UnityEngine;

[CreateAssetMenu(menuName = "Ocean/Ocean Settings")]
public class OceanSettings : ScriptableObject
{
    public const int Cells = 16;
    public const int MaxWaves = 4;
    const float HorizontalSlopeLimit = 0.8f;

    [System.Serializable]
    public class ShoreStage
    {
        [Tooltip("Lifecycle coordinate; the generator interpolates between stages.")]
        [Range(0f, 1f)] public float life;
        public AnimationCurve forward = AnimationCurve.Linear(0, 0, 1, 0);
        public AnimationCurve height = AnimationCurve.Linear(0, 0, 1, 0);
        public AnimationCurve foam = AnimationCurve.Linear(0, 0, 1, 0);
    }

    [System.Serializable]
    public struct Wave
    {
        [Min(0f)] public float amplitude;
        [Min(0.1f)] public float wavelength;
        public float speed;
        [Range(0f, 360f)] public float direction;
        [Range(0f, 1f)] public float horizontalRatio;
    }

    [Header("Domain and LOD")]
    [Min(1f)] public float size = 1000f;
    [Range(0, 10)] public int maxDepth = 10;
    [Range(1f, 64f)] public float pixelsPerCell = 3f;
    [Range(1, 1024)] public int maxPatches = 1024;

    [Header("Offshore Waves")]
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

    [Header("Height and Coast Bake")]
    public LayerMask groundLayers = ~0;
    [Range(16, 2048)] public int bakeResolution = 1024;
    [Min(0.1f)] public float captureBelow = 50f;
    [Min(0.1f)] public float captureAbove = 50f;

    public ComputeShader coastCompute;

    [Header("Shore Wave")]
    [Tooltip("Height multiplier; the LUT stores displacement as a fraction of wavelength.")]
    [Min(0f)] public float shoreAmplitude = 1f;
    [Min(0.1f)] public float shoreWavelength = 12f;
    [Tooltip("Travel speed towards the coast, in metres per second.")]
    public float shoreSpeed = 3.48f;
    [Min(0.1f)] public float shoreRange = 60f;

    [Tooltip("Forward displacement multiplier; 1 preserves the reference profile proportions.")]
    [Min(0f)] public float shoreForward = 1f;
    [Range(0f, 3f)] public float shoreFoam = 1f;
    [Min(0.1f)] public float shoreFade = 0.4f;
    [Tooltip("Breaking zone offset, in wavelengths.")]
    public float shorePhaseOffset = 2.37f;
    [Min(0.01f)] public float shoreLifeRate = 0.65f;
    [Min(0.01f)] public float shoreGroundPrediction = 6f;
    [Range(0f, 2f)] public float shoreVariation = 1f;
    public Texture2D shoreNoise;
    [Min(0f)] public float shoreNoisePhase = 0.7f;
    [Min(0f)] public float shoreNoiseLife = 0.241935f;

    [Header("Shore LUT")]
    public Texture2D shoreSource;
    public Vector4 shoreSourceDecode = new Vector4(-0.330324f, 0f, 0.470531f, 0.124962f);
    public Vector2Int shoreLutResolution = new Vector2Int(64, 128);

    public ShoreStage[] shoreStages = new ShoreStage[0];

    [HideInInspector] public Texture2D shoreLut;
    [HideInInspector] public float shoreLutMaxHeight;
    [HideInInspector] public float shoreLutMaxForward;

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

        float scale = totalSlope > HorizontalSlopeLimit
            ? HorizontalSlopeLimit / totalSlope : 1f;

        displacementExtent = Vector3.zero;

        for (int i = 0; i < count; i++)
        {
            Vector4 shape = shapes[i];
            Vector4 motion = motions[i];

            motion.x *= scale;
            motions[i] = motion;

            displacementExtent += new Vector3(
                Mathf.Abs(shape.x) * motion.x,
                shape.z,
                Mathf.Abs(shape.y) * motion.x);
        }

        return count;
    }
}
