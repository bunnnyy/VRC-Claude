using System.IO;
using UnityEngine;
using UnityEditor.SceneManagement;

/// <summary>
/// DeadZoneLuna/uSource: loads a map from a game folder (RootPath/cstrike/maps/NAME.bsp) into the open scene,
/// saving meshes and textures as assets. -executeMethod ImportWithDeadZoneLuna.Run -bsp path -gameRoot dir
/// </summary>
public static class ImportWithDeadZoneLuna
{
    public static void Run()
    {
        string bsp = Path.GetFullPath(ConverterMeasure.Arg("-bsp"));
        string root = Path.GetFullPath(ConverterMeasure.Arg("-gameRoot"));
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        uSource.uLoader.RootPath = root;
        uSource.uLoader.ModFolders = new[] { "cstrike" };
        uSource.uLoader.DirPaks = new[] { new[] { "cstrike_pak_dir" } };
        uSource.uLoader.UnitScale = 0.01905f;
        uSource.uLoader.SaveAssetsToUnity = true;
        uSource.uLoader.OutputAssetsFolder = "uSourceOut";
        uSource.uLoader.ParseLights = false;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        uSource.uResourceManager.LoadMap(Path.GetFileNameWithoutExtension(bsp));
        watch.Stop();
        ConverterMeasure.Measure("uSource", bsp, uSource.Formats.Source.VBSP.VBSPFile.BSP_WorldSpawn, watch.Elapsed.TotalSeconds);
    }
}
