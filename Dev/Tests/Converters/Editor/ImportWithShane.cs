using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Shane-SDK/USource: a ScriptedImporter for .bsp files. Copies the map into Assets/Maps, imports it and puts the
/// resulting prefab in the scene. -executeMethod ImportWithShane.Run -bsp path
/// </summary>
public static class ImportWithShane
{
    public static void Run()
    {
        string bsp = Path.GetFullPath(ConverterMeasure.Arg("-bsp"));
        string asset = "Assets/Maps/" + Path.GetFileName(bsp);
        Directory.CreateDirectory("Assets/Maps");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        // Its settings load in a static constructor, which fails when first run inside an asset import: load them now.
        var settings = USource.USource.settings;
        settings.sourceToUnityScale = 0.01905f;
        settings.readBSPFiles = true;
        settings.GamePaths.Clear();
        settings.GamePaths.Add(Path.GetFullPath(ConverterMeasure.Arg("-gameRoot")) + "/cstrike");
        USource.USource.ResourceManager.Refresh(); // rebuild the file providers with the new game path
        var watch = System.Diagnostics.Stopwatch.StartNew();
        File.Copy(bsp, asset, true);
        AssetDatabase.ImportAsset(asset, ImportAssetOptions.ForceUpdate);
        // Default import makes no materials; "setup dependencies" imports the map's vmt/vtf files first.
        var importer = (USource.AssetImporters.BspImporter)AssetImporter.GetAtPath(asset);
        var options = importer.importOptions;
        options.setupDependencies = true;
        importer.importOptions = options;
        importer.SaveAndReimport();
        watch.Stop();
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(asset);
        if (prefab == null) { ConverterMeasure.Log("USource: import produced no GameObject"); return; }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        // USource maps Source (x, y, z) to Unity (x, z, y); uSource and SourceMaps use (-y, z, x). Same thing turned 90
        // degrees about Y, so turn it back to compare.
        go.transform.rotation = Quaternion.Euler(0, -90, 0);
        ConverterMeasure.Measure("USource", bsp, go, watch.Elapsed.TotalSeconds);
    }
}
