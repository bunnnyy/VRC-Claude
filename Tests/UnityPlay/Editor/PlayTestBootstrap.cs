using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.SDK3.ClientSim;

/// <summary>
/// Batchmode entry point: opens the test map, adds the PlayTestRunner and enters play mode with ClientSim.
///   Unity -batchmode -projectPath P -executeMethod PlayTestBootstrap.Run [-smFrameRate 90] [-smOnly name]
/// The runner exits the editor with code 0 (all passed) or 1.
/// </summary>
public static class PlayTestBootstrap
{
    public static void Run()
    {
        var settings = ClientSimSettings.Instance;
        settings.enableClientSim = true;
        settings.hideMenuOnLaunch = true;
        settings.setTargetFrameRate = false;
        settings.initializationDelay = 0f;
        ClientSimSettings.SaveSettings(settings);

        EditorSceneManager.OpenScene("Assets/SourceMovement/SourceTestMap.unity");
        // Compile the U# programs like a fresh import would: run.sh copies the committed program assets in,
        // and those point at compiled programs that only exist in the project that made them.
        UdonSharp.Compiler.UdonSharpCompilerV1.CompileSync();
        var runner = new GameObject("PlayTestRunner").AddComponent<PlayTestRunner>();
        string[] args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "-smFrameRate") runner.frameRate = int.Parse(args[i + 1]);
            if (args[i] == "-smOnly") runner.only = args[i + 1];
            if (args[i] == "-smRecord") runner.recordDir = args[i + 1];
        }
        EditorApplication.isPlaying = true;
    }

    /// <summary>
    /// -executeMethod PlayTestBootstrap.ImportMap -bsp path: imports a map with SourceMapImporter into a new scene
    /// with VRCWorld (spawn at the map's first CT spawn) and SourceMovement, saved as Assets/MapTest.unity.
    /// </summary>
    public static void ImportMap()
    {
        string[] args = System.Environment.GetCommandLineArgs();
        string bsp = args[System.Array.IndexOf(args, "-bsp") + 1];
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        var map = SourceMapImporter.Import(bsp, 0.01905f);
        var world = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(
            "Packages/com.vrchat.worlds/Samples/UdonExampleScene/Prefabs/VRCWorld.prefab"));
        foreach (var m in map.GetComponentsInChildren<SourceEntity>())
            if (m.className == "info_player_counterterrorist") { world.transform.position = m.transform.position; break; }
        // Like Create Lobby: VRChat respawns players below this (default -100 m), deep maps go to about -312 m.
        world.GetComponent<VRC.SDKBase.VRC_SceneDescriptor>().RespawnHeightY = -1000f;
        PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/SourceMovement/SourceMovement.prefab"));
        EditorSceneManager.SaveScene(scene, "Assets/MapTest.unity");
        Debug.Log("[SMTEST] imported " + bsp);
    }

    /// <summary>
    /// -executeMethod PlayTestBootstrap.BuildSample -bspDir dir -zonesDir dir: the sample world from every map in bspDir
    /// (no CS:S folder here, so stock textures are missing), saved as Assets/SourceMapsSample.unity.
    /// </summary>
    public static void BuildSample()
    {
        string[] args = System.Environment.GetCommandLineArgs();
        string bsps = args[System.Array.IndexOf(args, "-bspDir") + 1];
        string zones = args[System.Array.IndexOf(args, "-zonesDir") + 1];
        SourceMapVisuals.CssFolder = "";
        SourceMapsSetup.BuildSampleWorld(bsps, zones);
        Debug.Log("[SMTEST] sample world built");
    }

    /// <summary>-executeMethod PlayTestBootstrap.RunSample: play-tests Assets/SourceMapsSample.unity.</summary>
    public static void RunSample()
    {
        var settings = ClientSimSettings.Instance;
        settings.enableClientSim = true;
        settings.hideMenuOnLaunch = true;
        settings.setTargetFrameRate = false;
        settings.initializationDelay = 0f;
        ClientSimSettings.SaveSettings(settings);
        EditorSceneManager.OpenScene("Assets/SourceMapsSample.unity");
        new GameObject("SampleWorldTestRunner").AddComponent<SampleWorldTestRunner>();
        EditorApplication.isPlaying = true;
    }

    /// <summary>-executeMethod PlayTestBootstrap.RunVote: play-tests the map rotation in Assets/SourceMaps/SourceMapsTest.unity.</summary>
    public static void RunVote()
    {
        var settings = ClientSimSettings.Instance;
        settings.enableClientSim = true;
        settings.hideMenuOnLaunch = true;
        settings.setTargetFrameRate = false;
        settings.initializationDelay = 0f;
        ClientSimSettings.SaveSettings(settings);
        EditorSceneManager.OpenScene("Assets/SourceMaps/SourceMapsTest.unity");
        new GameObject("VotePlayTestRunner").AddComponent<VotePlayTestRunner>();
        EditorApplication.isPlaying = true;
    }

    /// <summary>-executeMethod PlayTestBootstrap.RunMap [-smFrameRate 90]: play-tests Assets/MapTest.unity.</summary>
    public static void RunMap()
    {
        var settings = ClientSimSettings.Instance;
        settings.enableClientSim = true;
        settings.hideMenuOnLaunch = true;
        settings.setTargetFrameRate = false;
        settings.initializationDelay = 0f;
        ClientSimSettings.SaveSettings(settings);

        EditorSceneManager.OpenScene("Assets/MapTest.unity");
        var runner = new GameObject("MapPlayTestRunner").AddComponent<MapPlayTestRunner>();
        string[] args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == "-smFrameRate") runner.frameRate = int.Parse(args[i + 1]);
        EditorApplication.isPlaying = true;
    }

    /// <summary>-executeMethod PlayTestBootstrap.ExportPackage -smPackage path: the folders a world needs.</summary>
    public static void ExportPackage()
    {
        string[] args = System.Environment.GetCommandLineArgs();
        string path = System.Array.IndexOf(args, "-smPackage") is int i && i >= 0 ? args[i + 1] : "SourceMovement.unitypackage";
        AssetDatabase.ExportPackage(new[] { "Assets/SourceMovement", "Assets/SourceTimer" }, path, ExportPackageOptions.Recurse);
        Debug.Log("[SMTEST] exported " + path);
    }
}
