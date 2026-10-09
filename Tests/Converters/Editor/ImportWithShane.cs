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
        var watch = System.Diagnostics.Stopwatch.StartNew();
        File.Copy(bsp, asset, true);
        AssetDatabase.ImportAsset(asset, ImportAssetOptions.ForceUpdate);
        watch.Stop();
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(asset);
        if (prefab == null) { ConverterMeasure.Log("USource: import produced no GameObject"); return; }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        ConverterMeasure.Measure("USource", bsp, go, watch.Elapsed.TotalSeconds);
    }
}
