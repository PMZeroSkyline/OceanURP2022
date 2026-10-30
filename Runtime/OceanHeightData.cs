using UnityEngine;

public sealed class OceanHeightData : ScriptableObject
{
    [SerializeField, HideInInspector] Texture2D height;
    [SerializeField, HideInInspector] Texture2D coast;

    [SerializeField, HideInInspector] Vector3 position;
    [SerializeField, HideInInspector] Vector4 parameters;
    [SerializeField, HideInInspector] Vector2 range;
    [SerializeField, HideInInspector] int layers;

    public Texture2D Height => height;
    public Texture2D Coast => coast;
    public Vector2 Range => range;

    public bool HasCoast => height != null && coast != null && coast.format == TextureFormat.RGBAFloat && coast.width == height.width && coast.height == height.height;

    static Vector4 Parameters(OceanSettings settings)
    {
        return new Vector4(settings.size, settings.bakeResolution, settings.captureBelow, settings.captureAbove);
    }

    public bool Matches(OceanSettings settings, Vector3 center)
    {
        if (settings == null || height == null)
            return false;

        int resolution = Mathf.Clamp(
            settings.bakeResolution, 16, 2048);

        return height.format == TextureFormat.RFloat
            && height.width == resolution
            && height.height == resolution
            && position == center
            && parameters == Parameters(settings)
            && layers == settings.groundLayers.value;
    }

    public void Write(
        Texture2D texture,
        Texture2D coastTexture,
        OceanSettings settings,
        Vector3 center,
        Vector2 capturedRange)
    {
        height = texture;
        coast = coastTexture;

        position = center;
        parameters = Parameters(settings);
        range = capturedRange;
        layers = settings.groundLayers.value;
    }
}