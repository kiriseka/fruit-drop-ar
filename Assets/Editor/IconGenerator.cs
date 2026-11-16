using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

/// <summary>
/// Development tool, not part of the game. Makes the app icon by rendering the game's own fruit basket on the
/// game's lemon colour, and sets it as the default icon for every platform. Run it again if the basket changes.
/// </summary>
public static class IconGenerator
{
    const string k_IconPath = "Assets/Textures/AppIcon.png";
    const string k_BasketPath = "Assets/Prefabs/FruitBasket.prefab";
    const int k_Size = 1024;

    [MenuItem("Fruit Drop/Generate App Icon")]
    public static void Generate()
    {
        // Built far from the scene's own objects, rendered once and removed again.
        var stage = new GameObject("Icon Stage") { hideFlags = HideFlags.HideAndDontSave };
        stage.transform.position = new Vector3(0f, 200f, 0f);
        var basket = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(k_BasketPath), stage.transform);
        basket.transform.localPosition = Vector3.zero;
        basket.transform.localRotation = Quaternion.Euler(0f, 20f, 0f);

        var cameraObject = new GameObject("Icon Camera");
        cameraObject.transform.SetParent(stage.transform, false);

        // From the front and above, close enough that the basket fills the middle and a launcher's round mask
        // still leaves it whole.
        cameraObject.transform.localPosition = new Vector3(0f, 0.46f, -0.52f);
        cameraObject.transform.LookAt(stage.transform.position + Vector3.up * 0.05f);
        var camera = cameraObject.AddComponent<Camera>();
        camera.fieldOfView = 32f;
        camera.nearClipPlane = 0.05f;
        camera.farClipPlane = 5f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.957f, 0.769f, 0.188f, 1f);

        var target = new RenderTexture(k_Size, k_Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
        camera.targetTexture = target;
        camera.Render();
        RenderTexture.active = target;
        var image = new Texture2D(k_Size, k_Size, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, k_Size, k_Size), 0, 0);
        image.Apply();
        RenderTexture.active = null;
        camera.targetTexture = null;
        File.WriteAllBytes(k_IconPath, image.EncodeToPNG());
        Object.DestroyImmediate(image);
        target.Release();
        Object.DestroyImmediate(target);
        Object.DestroyImmediate(stage);

        AssetDatabase.ImportAsset(k_IconPath);
        var importer = (TextureImporter)AssetImporter.GetAtPath(k_IconPath);
        importer.mipmapEnabled = false;
        importer.alphaSource = TextureImporterAlphaSource.None;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.SaveAndReimport();

        var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(k_IconPath);
        PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
        AssetDatabase.SaveAssets();
        Debug.Log($"[IconGenerator] Wrote {k_IconPath} ({k_Size} px) and set it as the default icon");
    }
}
