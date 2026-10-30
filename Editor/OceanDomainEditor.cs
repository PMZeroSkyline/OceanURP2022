using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[CustomEditor(typeof(OceanDomain))]
public class OceanDomainEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var domain = (OceanDomain)target;

        bool hasCompute = domain.settings != null && domain.settings.coastCompute != null;
        bool canBake = 
            hasCompute &&
            domain.isActiveAndEnabled && 
            !EditorApplication.isPlayingOrWillChangePlaymode && 
            !EditorUtility.IsPersistent(domain);


        if (!hasCompute)
        {
            EditorGUILayout.HelpBox(
                "Assign Coast Compute in OceanSettings.",
                MessageType.Info);
        }

        using (new EditorGUI.DisabledScope( !canBake))
        {
            if (GUILayout.Button("Bake Height"))
            {
                try
                {
                    BakeAndSave(domain);

                    EditorApplication.QueuePlayerLoopUpdate();
                    SceneView.RepaintAll();
                }
                catch (System.Exception exception)
                {
                    Debug.LogException(exception, domain);
                }
            }
        }

        EditorGUILayout.LabelField("Height Bake", domain.HasValidHeight ? "Valid" : "Missing / Outdated");
        EditorGUILayout.LabelField("Coast Data", domain.HasValidCoast ? "Valid" : "Missing / Outdated");

        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.ObjectField("Height Texture", domain.HeightTexture, typeof(Texture2D), false);
            EditorGUILayout.ObjectField("Coast Texture", domain.CoastTexture, typeof(Texture2D), false);
        }
    }
    static void CheckOwner(Texture2D texture, string path)
    {
        if (texture != null
            && AssetDatabase.GetAssetPath(texture) != path)
        {
            throw new System.InvalidOperationException(
                "Bake textures must belong to their data asset.");
        }
    }
    static void DestroyTransient(Object obj)
    {
        if (obj != null && !AssetDatabase.Contains(obj))
            DestroyImmediate(obj);
    }
    static void BakeAndSave(OceanDomain domain)
    {
        string path = AssetDatabase.GetAssetPath(domain.bakedHeight);

        if (string.IsNullOrEmpty(path))
        {
            path = EditorUtility.SaveFilePanelInProject("Save Ocean Height", domain.name + "_Height", "asset", "Choose where to save the baked height data.");
        }

        if (string.IsNullOrEmpty(path))
            return;

        Object existing = AssetDatabase.LoadMainAssetAtPath(path);

        if (existing != null && !(existing is OceanHeightData))
        {
            throw new System.InvalidOperationException("Choose an OceanHeightData asset or a new path.");
        }

        var data = existing as OceanHeightData;

        if (data != null)
        {
            CheckOwner(data.Height, path);
            CheckOwner(data.Coast, path);
        }

        Texture2D height = null;
        Texture2D coast = null;
        RenderTexture coastRT = null;

        try
        {
            using (OceanHeightBake capture = OceanHeightBake.Capture(
                domain.settings,
                domain.transform))
            {
                height = ReadTexture(
                    capture.Texture,
                    TextureFormat.RFloat,
                    "Ground Height",
                    FilterMode.Bilinear);

                int passes = OceanCoastBake.GetPassCount(
                    height.width);

                coastRT = OceanCoastBake.Build(
                    domain.settings.coastCompute,
                    height,
                    domain.transform.position.y,
                    passes);

                coast = ReadTexture(
                    coastRT,
                    TextureFormat.RGBAFloat,
                    "Coast Data",
                    FilterMode.Point);

                if (data == null)
                {
                    data = CreateInstance<OceanHeightData>();
                    data.name = Path.GetFileNameWithoutExtension(path);

                    AssetDatabase.CreateAsset(data, path);
                }

                Texture2D savedHeight = StoreTexture(
                    height,
                    data.Height,
                    data);

                Texture2D savedCoast = StoreTexture(
                    coast,
                    data.Coast,
                    data);

                data.Write(
                    savedHeight,
                    savedCoast,
                    domain.settings,
                    domain.transform.position,
                    capture.Range);

                EditorUtility.SetDirty(data);
                AssetDatabase.SaveAssetIfDirty(data);

                Undo.RecordObject(domain, "Assign Ocean Bake");

                domain.bakedHeight = data;

                EditorUtility.SetDirty(domain);

                PrefabUtility.RecordPrefabInstancePropertyModifications(
                    domain);

                EditorSceneManager.MarkSceneDirty(
                    domain.gameObject.scene);
            }
        }
        finally
        {
            OceanCoastBake.Release(coastRT);

            DestroyTransient(height);
            DestroyTransient(coast);
            DestroyTransient(data);
        }
    }

    static Texture2D ReadTexture(
        RenderTexture source,
        TextureFormat format,
        string name,
        FilterMode filter)
    {
        var result = new Texture2D(
            source.width,
            source.height,
            format,
            false,
            true)
        {
            name = name,
            filterMode = filter,
            wrapMode = TextureWrapMode.Clamp
        };

        RenderTexture previous = RenderTexture.active;

        try
        {
            RenderTexture.active = source;

            result.ReadPixels(
                new Rect(0, 0, source.width, source.height),
                0,
                0,
                false);

            result.Apply(false, false);

            return result;
        }
        catch
        {
            DestroyImmediate(result);
            throw;
        }
        finally
        {
            RenderTexture.active = previous;
        }
    }
    static Texture2D StoreTexture(
        Texture2D source,
        Texture2D saved,
        OceanHeightData owner)
    {
        if (saved == null)
        {
            saved = source;
            AssetDatabase.AddObjectToAsset(saved, owner);
        }
        else
        {
            EditorUtility.CopySerialized(source, saved);
            saved.Apply(false, false);
        }

        EditorUtility.SetDirty(saved);
        return saved;
    }
}