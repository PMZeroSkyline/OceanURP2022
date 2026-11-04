using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(OceanSettings))]
public sealed class OceanSettingsEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var settings = (OceanSettings)target;
        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "Empty stages use the source profile. Import stages to edit wave shapes, then Generate.\n"
            + "Curve X = wave phase; Forward / Height = displacement divided by wave width.",
            MessageType.Info);

        bool canGenerate = AssetDatabase.IsMainAsset(settings)
            && !EditorApplication.isPlayingOrWillChangePlaymode;
        using (new EditorGUI.DisabledScope(!canGenerate))
        {
            if (GUILayout.Button("Use Reference Profile"))
                Run(() => OceanShoreLutGenerator.UseReference(settings), settings);
            if (GUILayout.Button("Import Reference as Editable Stages"))
                Run(() => OceanShoreLutGenerator.ImportEditable(settings), settings);
            if (GUILayout.Button("Generate Shore LUT"))
                Run(() => OceanShoreLutGenerator.Bake(settings), settings);
        }
    }

    static void Run(Action action, OceanSettings settings)
    {
        try
        {
            action();
        }
        catch (Exception error)
        {
            Debug.LogException(error, settings);
        }
    }
}

public static class OceanShoreLutGenerator
{
    // User-provided FluidFlux Unity port: original signed profile, not a synthetic replacement.
    const string ReferencePath =
        "Assets/FluxFluid/Runtime/Resources/FluidFlux/T_DefaultWaveProfile_Forard.png";

    static bool Finite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);

    static Texture2D Source(OceanSettings settings)
    {
        if (settings.shoreSource == null)
            settings.shoreSource = AssetDatabase.LoadAssetAtPath<Texture2D>(ReferencePath);
        if (settings.shoreSource == null || !settings.shoreSource.isReadable)
            throw new InvalidOperationException("A readable reference profile is required.");
        return settings.shoreSource;
    }

    // Hardware bilinear convention: texel centres, repeating U and clamped V.
    public static Color SampleTexture(Texture2D texture, float u, float v)
    {
        float x = Mathf.Repeat(u, 1f) * texture.width - .5f;
        float y = Mathf.Clamp(v * texture.height - .5f, 0f, texture.height - 1f);
        int ix = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
        int x0 = (ix + texture.width) % texture.width;
        int x1 = (x0 + 1) % texture.width, y1 = Mathf.Min(y0 + 1, texture.height - 1);
        return Color.Lerp(
            Color.Lerp(texture.GetPixel(x0, y0), texture.GetPixel(x1, y0), x - ix),
            Color.Lerp(texture.GetPixel(x0, y1), texture.GetPixel(x1, y1), x - ix), y - y0);
    }

    static Color SampleSource(Texture2D source, Vector4 decode, float u, float life)
    {
        // Unity imports the PNG bottom-up; its original lifecycle starts at the top.
        Color value = SampleTexture(source, u, 1f - life);
        value.r = (value.r + decode.x) * decode.z;
        value.g = (value.g + decode.y) * decode.w;
        value.a = 1f;
        return value;
    }

    static void ValidateCurve(AnimationCurve curve)
    {
        if (curve == null || curve.length < 2)
            throw new InvalidOperationException("Each curve needs at least two keys.");
        Keyframe[] keys = curve.keys;
        foreach (Keyframe key in keys)
            if (!Finite(key.time) || !Finite(key.value)
                || !Finite(key.inTangent) || !Finite(key.outTangent))
                throw new InvalidOperationException("Curve values and tangents must be finite.");
        if (Mathf.Abs(keys[0].time) > .00001f || Mathf.Abs(keys[keys.Length - 1].time - 1f) > .00001f)
            throw new InvalidOperationException("Profile curves must span U = 0..1.");
        if (Mathf.Abs(keys[0].value - keys[keys.Length - 1].value) > .00001f)
            throw new InvalidOperationException("Periodic profile endpoint values must match.");
    }

    static Color Evaluate(OceanSettings.ShoreStage stage, float u) => new Color(
        stage.forward.Evaluate(u), stage.height.Evaluate(u), stage.foam.Evaluate(u), 1f);

    public static Texture2D Generate(OceanSettings settings, out Vector2 bounds)
    {
        var stages = (settings.shoreStages ?? Array.Empty<OceanSettings.ShoreStage>())
            .Where(s => s != null).OrderBy(s => s.life).ToArray();
        for (int i = 0; i < stages.Length; i++)
        {
            var stage = stages[i];
            if (!Finite(stage.life) || stage.life < 0f || stage.life > 1f
                || (i > 0 && stage.life <= stages[i - 1].life))
                throw new InvalidOperationException("Stage life values must be distinct and within 0..1.");
            ValidateCurve(stage.forward);
            ValidateCurve(stage.height);
            ValidateCurve(stage.foam);
        }
        Texture2D source = stages.Length == 0 ? Source(settings) : null;
        int width = Mathf.Clamp(settings.shoreLutResolution.x, 16, 1024);
        int height = Mathf.Clamp(settings.shoreLutResolution.y, 4, 512);
        var pixels = new Color[width * height];
        bounds = Vector2.zero;
        for (int y = 0; y < height; y++)
        {
            float life = (y + .5f) / height;
            int next = 0;
            while (next < stages.Length - 1 && stages[next].life < life)
                next++;

            for (int x = 0; x < width; x++)
            {
                float u = (x + .5f) / width;
                Color value;
                if (source != null)
                {
                    value = SampleSource(source, settings.shoreSourceDecode, u, life);
                }
                else
                {
                    var b = stages[next];
                    var a = stages[Mathf.Max(0, next - 1)];
                    value = next == 0 || life >= b.life
                        ? Evaluate(b, u)
                        : Color.Lerp(Evaluate(a, u), Evaluate(b, u), Mathf.InverseLerp(a.life, b.life, life));
                }
                if (!Finite(value.r) || !Finite(value.g) || !Finite(value.b))
                    throw new InvalidOperationException("The LUT contains non-finite samples.");
                value.b = Mathf.Clamp01(value.b);
                pixels[y * width + x] = value;
                bounds.x = Mathf.Max(bounds.x, Mathf.Abs(value.r));
                bounds.y = Mathf.Max(bounds.y, Mathf.Abs(value.g));
            }
        }
        var texture = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true)
        {
            name = "Shore LUT - Reference Forward Height Foam",
            wrapModeU = TextureWrapMode.Repeat,
            wrapModeV = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        try
        {
            texture.SetPixels(pixels);
            texture.Apply(false, false);
            return texture;
        }
        catch
        {
            UnityEngine.Object.DestroyImmediate(texture);
            throw;
        }
    }

    public static void Bake(OceanSettings settings)
    {
        string path = AssetDatabase.GetAssetPath(settings);
        if (!AssetDatabase.IsMainAsset(settings))
            throw new InvalidOperationException("Save OceanSettings as a main asset first.");
        if (settings.shoreLut != null && AssetDatabase.GetAssetPath(settings.shoreLut) != path)
            throw new InvalidOperationException("The generated LUT must belong to these settings.");
        Texture2D generated = Generate(settings, out Vector2 bounds);
        try
        {
            if (settings.shoreLut == null)
            {
                AssetDatabase.AddObjectToAsset(generated, settings);
                settings.shoreLut = generated;
            }
            else
            {
                EditorUtility.CopySerialized(generated, settings.shoreLut);
                settings.shoreLut.Apply(false, false);
            }
            settings.shoreLutMaxForward = bounds.x;
            settings.shoreLutMaxHeight = bounds.y;
            EditorUtility.SetDirty(settings.shoreLut);
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssetIfDirty(settings);
            SceneView.RepaintAll();
        }
        finally
        {
            if (!AssetDatabase.Contains(generated))
                UnityEngine.Object.DestroyImmediate(generated);
        }
    }

    public static void UseReference(OceanSettings settings)
    {
        Undo.RecordObject(settings, "Use Reference Shore Profile");
        Texture2D source = Source(settings);
        settings.shoreStages = Array.Empty<OceanSettings.ShoreStage>();
        settings.shoreLutResolution = new Vector2Int(source.width, source.height);
        Bake(settings);
    }

    public static void ImportEditable(OceanSettings settings)
    {
        Undo.RecordObject(settings, "Import Reference Shore Stages");
        Texture2D source = Source(settings);
        var stages = new OceanSettings.ShoreStage[source.height];
        for (int y = 0; y < source.height; y++)
        {
            float life = (y + .5f) / source.height;
            var keys = new[]
            {
                new Keyframe[source.width + 2],
                new Keyframe[source.width + 2],
                new Keyframe[source.width + 2]
            };
            for (int x = 0; x < source.width + 2; x++)
            {
                float u = x == 0 ? 0f : x == source.width + 1 ? 1f : (x - .5f) / source.width;
                Color value = SampleSource(source, settings.shoreSourceDecode, u, life);
                for (int channel = 0; channel < 3; channel++)
                    keys[channel][x] = new Keyframe(u, value[channel]);
            }
            stages[y] = new OceanSettings.ShoreStage
            {
                life = life,
                forward = LinearCurve(keys[0]),
                height = LinearCurve(keys[1]),
                foam = LinearCurve(keys[2])
            };
        }
        settings.shoreStages = stages;
        settings.shoreLutResolution = new Vector2Int(source.width, source.height);
        Bake(settings);
    }

    static AnimationCurve LinearCurve(Keyframe[] keys)
    {
        var curve = new AnimationCurve(keys);
        for (int i = 0; i < keys.Length; i++)
        {
            AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
            AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
        }
        return curve;
    }
}
