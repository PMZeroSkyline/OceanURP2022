using System;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(OceanSettings))]
public sealed class OceanSettingsEditor : Editor
{
    float previewDistance = 0.5f;
    readonly Vector3[] points = new Vector3[257];

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var settings = (OceanSettings)target;

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "U: wave cycle. V: distance / shore range. G: signed height.\n"
            + "Edit curves, then Generate. The preview reads the saved LUT.",
            MessageType.Info);

        bool canGenerate = AssetDatabase.IsMainAsset(settings)
            && !EditorApplication.isPlayingOrWillChangePlaymode;

        using (new EditorGUI.DisabledScope(!canGenerate))
        {
            if (GUILayout.Button("Generate Shore LUT"))
            {
                try
                {
                    Bake(settings);
                }
                catch (Exception error)
                {
                    Debug.LogException(error, settings);
                }
            }
        }

        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.ObjectField(
                "Generated LUT",
                settings.shoreLut,
                typeof(Texture2D),
                false);

            EditorGUILayout.FloatField(
                "Max Absolute Height",
                settings.shoreLutMaxHeight);
        }

        Texture2D texture = settings.shoreLut;
        if (texture == null || !texture.isReadable)
            return;

        EditorGUILayout.LabelField(
            "LUT Size",
            $"{texture.width} x {texture.height}");

        previewDistance = EditorGUILayout.Slider(
            "Preview Distance / Range",
            previewDistance,
            0f,
            1f);

        DrawPreview(texture, settings.shoreLutMaxHeight);

        EditorGUILayout.LabelField(
            "Phase 0 → 1; horizontal line = zero height.");
    }

    static bool Finite(float value) =>
        !float.IsNaN(value) && !float.IsInfinity(value);

    static void ValidateCurve(
        AnimationCurve curve,
        string label,
        bool periodic)
    {
        if (curve == null || curve.length < 2)
        {
            throw new InvalidOperationException(
                label + " needs at least two keys.");
        }

        Keyframe[] keys = curve.keys;

        foreach (Keyframe key in keys)
        {
            if (!Finite(key.time) || !Finite(key.value)
                || !Finite(key.inTangent) || !Finite(key.outTangent))
            {
                throw new InvalidOperationException(
                    label + " needs finite values and tangents.");
            }
        }

        Keyframe first = keys[0];
        Keyframe last = keys[keys.Length - 1];

        if (Mathf.Abs(first.time) > 0.00001f
            || Mathf.Abs(last.time - 1f) > 0.00001f)
        {
            throw new InvalidOperationException(
                label + " must start at time 0 and end at time 1.");
        }

        if (periodic)
        {
            if (Mathf.Abs(first.value - last.value) > 0.00001f
                || Mathf.Abs(first.outTangent - last.inTangent) > 0.0001f)
            {
                throw new InvalidOperationException(
                    label
                    + " needs matching endpoint values and matching inside tangents.");
            }
        }
        else if (Mathf.Abs(first.value) > 0.00001f
            || Mathf.Abs(last.value) > 0.00001f
            || Mathf.Abs(first.outTangent) > 0.0001f
            || Mathf.Abs(last.inTangent) > 0.0001f)
        {
            throw new InvalidOperationException(
                label
                + " needs zero endpoint values and zero inside tangents.");
        }
    }

    static Texture2D Generate(
        OceanSettings settings,
        out float maxHeight)
    {
        ValidateCurve(settings.shoreProfile, "Shore Profile", true);
        ValidateCurve(settings.shoreEnvelope, "Shore Envelope", false);

        int width = Mathf.Clamp(
            settings.shoreLutResolution.x, 16, 1024);

        int height = Mathf.Clamp(
            settings.shoreLutResolution.y, 4, 256);

        var pixels = new Color[width * height];
        maxHeight = 0f;

        for (int y = 0; y < height; y++)
        {
            // V includes both endpoints of the shore band.
            float v = y / (float)(height - 1);
            float envelope = settings.shoreEnvelope.Evaluate(v);

            for (int x = 0; x < width; x++)
            {
                // U is periodic and samples texel centers.
                float u = (x + 0.5f) / width;

                float value =
                    settings.shoreProfile.Evaluate(u) * envelope;

                if (!Finite(value))
                {
                    throw new InvalidOperationException(
                        "The LUT contains a non-finite height.");
                }

                // Exactly zero at both band endpoints, including roundoff.
                if (y == 0 || y == height - 1)
                    value = 0f;

                pixels[y * width + x] =
                    new Color(0f, value, 0f, 1f);

                maxHeight = Mathf.Max(
                    maxHeight,
                    Mathf.Abs(value));
            }
        }

        var texture = new Texture2D(
            width,
            height,
            TextureFormat.RGBAFloat,
            false,
            true)
        {
            name = "Shore Wave LUT",
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
            DestroyImmediate(texture);
            throw;
        }
    }

    static void Bake(OceanSettings settings)
    {
        string path = AssetDatabase.GetAssetPath(settings);

        if (string.IsNullOrEmpty(path)
            || !AssetDatabase.IsMainAsset(settings))
        {
            throw new InvalidOperationException(
                "Save OceanSettings as a main asset first.");
        }

        if (settings.shoreLut != null
            && AssetDatabase.GetAssetPath(settings.shoreLut) != path)
        {
            throw new InvalidOperationException(
                "The generated LUT must belong to these settings.");
        }

        Texture2D generated = Generate(
            settings,
            out float maxHeight);

        try
        {
            if (settings.shoreLut == null)
            {
                AssetDatabase.AddObjectToAsset(generated, settings);
                settings.shoreLut = generated;
            }
            else
            {
                // Preserve the existing texture reference when regenerating.
                EditorUtility.CopySerialized(
                    generated,
                    settings.shoreLut);

                settings.shoreLut.Apply(false, false);
            }

            settings.shoreLutMaxHeight = maxHeight;

            EditorUtility.SetDirty(settings.shoreLut);
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssetIfDirty(settings);
        }
        finally
        {
            if (!AssetDatabase.Contains(generated))
                DestroyImmediate(generated);
        }
    }

    // Same U/V sample layout that the shader will use next lesson.
    static float SampleHeight(
        Texture2D texture,
        float u,
        float v)
    {
        float x = u * texture.width - 0.5f;
        float y = Mathf.Clamp01(v) * (texture.height - 1);

        int ix = Mathf.FloorToInt(x);
        int y0 = Mathf.FloorToInt(y);
        int y1 = Mathf.Min(y0 + 1, texture.height - 1);

        int x0 =
            (ix % texture.width + texture.width) % texture.width;

        int x1 = (x0 + 1) % texture.width;

        float tx = x - ix;
        float ty = y - y0;

        float a = Mathf.Lerp(
            texture.GetPixel(x0, y0).g,
            texture.GetPixel(x1, y0).g,
            tx);

        float b = Mathf.Lerp(
            texture.GetPixel(x0, y1).g,
            texture.GetPixel(x1, y1).g,
            tx);

        return Mathf.Lerp(a, b, ty);
    }

    void DrawPreview(Texture2D texture, float maxHeight)
    {
        Rect rect = GUILayoutUtility.GetRect(
            100f,
            160f,
            GUILayout.ExpandWidth(true));

        if (Event.current.type != EventType.Repaint)
            return;

        EditorGUI.DrawRect(
            rect,
            new Color(0.06f, 0.07f, 0.09f));

        float scale = (rect.height * 0.5f - 8f)
            / Mathf.Max(maxHeight, 0.001f);

        for (int i = 0; i < points.Length; i++)
        {
            float u = i / (float)(points.Length - 1);
            float value = SampleHeight(
                texture,
                u,
                previewDistance);

            points[i] = new Vector3(
                rect.x + 8f + u * (rect.width - 16f),
                rect.center.y - value * scale,
                0f);
        }

        Color previous = Handles.color;

        Handles.BeginGUI();

        Handles.color = Color.gray;
        Handles.DrawLine(
            new Vector3(rect.x, rect.center.y, 0f),
            new Vector3(rect.xMax, rect.center.y, 0f));

        Handles.color = Color.cyan;
        Handles.DrawAAPolyLine(2f, points);

        Handles.EndGUI();
        Handles.color = previous;
    }
}