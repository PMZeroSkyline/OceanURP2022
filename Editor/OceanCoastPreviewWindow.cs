using UnityEditor;
using UnityEngine;

public class OceanCoastPreviewWindow : EditorWindow
{
    [SerializeField] OceanDomain domain;
    [SerializeField] ComputeShader compute;

    [SerializeField] int passes = 32;
    [SerializeField] float displayRange = 100f;
    [SerializeField] float contourSpacing = 10f;
    [SerializeField] int previewMode;

    RenderTexture result;
    Material previewMaterial;

    float builtSize;
    int builtPasses;
    string builtSource;

    static readonly string[] Modes =
    {
        "Distance",
        "Nearest UV",
        "Land / Water"
    };

    [MenuItem("Window/Ocean/Coast Distance Preview")]
    static void Open()
    {
        GetWindow<OceanCoastPreviewWindow>("Coast Distance");
    }

    void OnEnable()
    {
        minSize = new Vector2(360f, 440f);

        if (domain == null && Selection.activeGameObject != null)
        {
            domain = Selection.activeGameObject
                .GetComponent<OceanDomain>();
        }
    }

    void OnDisable()
    {
        OceanCoastBake.Release(result);
        result = null;

        if (previewMaterial != null)
            DestroyImmediate(previewMaterial);

        previewMaterial = null;
    }

    void OnGUI()
    {
        domain = (OceanDomain)EditorGUILayout.ObjectField(
            "Ocean Domain",
            domain,
            typeof(OceanDomain),
            true);

        compute = (ComputeShader)EditorGUILayout.ObjectField(
            "Compute Shader",
            compute,
            typeof(ComputeShader),
            false);

        bool hasHeight = domain != null && domain.HasValidHeight;

        int maxPasses = hasHeight
            ? OceanCoastBake.GetPassCount(domain.HeightTexture.width)
            : 10;

        passes = EditorGUILayout.IntSlider(
            "Jump Passes",
            passes,
            0,
            maxPasses);

        displayRange = Mathf.Max(
            0.01f,
            EditorGUILayout.FloatField(
                "Display Range (m)",
                displayRange));

        contourSpacing = Mathf.Max(
            0.01f,
            EditorGUILayout.FloatField(
                "Contour Spacing (m)",
                contourSpacing));

        previewMode = GUILayout.Toolbar(previewMode, Modes);

        EditorGUILayout.HelpBox(
            "Rebuild after changing inputs or rebaking height. "
            + "0 passes shows initial seeds. Gray means no known seed.",
            MessageType.Info);

        using (new EditorGUI.DisabledScope(
            !hasHeight || compute == null))
        {
            if (GUILayout.Button("Rebuild Preview"))
            {
                try
                {
                    if (previewMaterial == null)
                    {
                        Shader shader = Shader.Find(
                            "Hidden/Ocean/CoastDistancePreview");

                        if (shader == null)
                        {
                            throw new System.InvalidOperationException(
                                "Cannot find preview shader.");
                        }

                        previewMaterial = new Material(shader)
                        {
                            hideFlags = HideFlags.HideAndDontSave
                        };
                    }

                    RenderTexture next = OceanCoastBake.Build(
                        compute,
                        domain.HeightTexture,
                        domain.transform.position.y,
                        passes);

                    OceanCoastBake.Release(result);
                    result = next;

                    builtSize = Mathf.Max(1f, domain.settings.size);
                    builtPasses = passes;
                    builtSource = domain.name;
                }
                catch (System.Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }

        if (!hasHeight)
        {
            EditorGUILayout.HelpBox(
                "Assign a domain with a valid height bake.",
                MessageType.Warning);
        }

        if (result == null || previewMaterial == null)
            return;

        EditorGUILayout.LabelField(
            "Last Result",
            $"{builtSource} / {result.width} px / {builtPasses} passes");

        previewMaterial.SetFloat("_DomainSize", builtSize);
        previewMaterial.SetFloat("_DisplayRange", displayRange);
        previewMaterial.SetFloat("_ContourSpacing", contourSpacing);
        previewMaterial.SetInteger("_PreviewMode", previewMode);

        Rect area = GUILayoutUtility.GetRect(
            1f,
            10000f,
            1f,
            10000f,
            GUILayout.ExpandWidth(true),
            GUILayout.ExpandHeight(true));

        float side = Mathf.Min(area.width, area.height);

        Rect rect = new Rect(
            area.center.x - side * 0.5f,
            area.center.y - side * 0.5f,
            side,
            side);

        if (Event.current.type == EventType.Repaint)
        {
            EditorGUI.DrawPreviewTexture(
                rect,
                result,
                previewMaterial,
                ScaleMode.StretchToFill);
        }
    }
}