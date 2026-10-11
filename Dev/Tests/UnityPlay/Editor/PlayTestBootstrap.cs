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
    /// (stock textures only with a CS:S folder in env SM_CSS), saved as Assets/SourceMapsSample.unity.
    /// </summary>
    public static void BuildSample()
    {
        string[] args = System.Environment.GetCommandLineArgs();
        string bsps = args[System.Array.IndexOf(args, "-bspDir") + 1];
        string zones = args[System.Array.IndexOf(args, "-zonesDir") + 1];
        SourceMapVisuals.CssFolder = System.Environment.GetEnvironmentVariable("SM_CSS") ?? ""; // a CS:S folder (cstrike, hl2) for stock content
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

    /// <summary>
    /// -executeMethod PlayTestBootstrap.BuildRoute -bsp path [-zones file.json]: a map with visuals, timer zones and
    /// SourceMovement for RouteRunner, saved as Assets/RouteTest.unity.
    /// </summary>
    public static void BuildRoute()
    {
        string[] args = System.Environment.GetCommandLineArgs();
        string bsp = args[System.Array.IndexOf(args, "-bsp") + 1];
        int z = System.Array.IndexOf(args, "-zones");
        // The assets were just copied in: compile the U# programs before serializing timer zones.
        UdonSharp.Compiler.UdonSharpCompilerV1.CompileSync();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        var map = SourceMapImporter.Import(bsp, 0.01905f);
        SourceMapVisuals.CssFolder = System.Environment.GetEnvironmentVariable("SM_CSS") ?? ""; // a CS:S folder (cstrike, hl2) for stock content
        SourceMapVisuals.Import(bsp, map.transform, 0.01905f);
        string name = System.IO.Path.GetFileNameWithoutExtension(bsp);
        SourceMapZones.Import(map, name, z >= 0 ? System.IO.File.ReadAllText(args[z + 1]) : SourceMapZones.Download(name), 0.01905f);
        var world = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(
            "Packages/com.vrchat.worlds/Samples/UdonExampleScene/Prefabs/VRCWorld.prefab"));
        var start = GameObject.Find("Start " + name);
        if (start != null) world.transform.position = start.transform.Find("StartPoint").position;
        world.GetComponent<VRC.SDKBase.VRC_SceneDescriptor>().RespawnHeightY = -1000f;
        var movement = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/SourceMovement/SourceMovement.prefab"));
        var movementScript = movement.GetComponent<SourceMovement>();
        movementScript.hullOnly = new[] { map }; // VRChat's capsule is taller than the CS:S hull: only the hull hits the map
        UdonSharpEditor.UdonSharpEditorUtility.CopyProxyToUdon(movementScript);
        EditorSceneManager.SaveScene(scene, "Assets/RouteTest.unity");
        Debug.Log("[SMTEST] route scene built from " + bsp);
    }

    /// <summary>
    /// -executeMethod PlayTestBootstrap.RunRoute -smRecord dir [-smFrameRate 100] [-smRoute file]: RouteRunner on
    /// Assets/RouteTest.unity (with the route from `file`, else bhop_eazy_v2's).
    /// </summary>
    public static void RunRoute()
    {
        var settings = ClientSimSettings.Instance;
        settings.enableClientSim = true;
        settings.hideMenuOnLaunch = true;
        settings.setTargetFrameRate = false;
        settings.initializationDelay = 0f;
        ClientSimSettings.SaveSettings(settings);
        if (!System.IO.File.Exists("Assets/RouteTest.unity")) { Debug.LogError("[SMTEST] FAIL no route scene"); EditorApplication.Exit(1); return; }
        EditorSceneManager.OpenScene("Assets/RouteTest.unity");
        UdonSharp.Compiler.UdonSharpCompilerV1.CompileSync();
        var runner = new GameObject("RouteRunner").AddComponent<RouteRunner>();
        string[] args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "-smFrameRate") runner.frameRate = int.Parse(args[i + 1]);
            if (args[i] == "-smRecord") runner.recordDir = args[i + 1];
            if (args[i] == "-smRouteSection") runner.startSection = int.Parse(args[i + 1]);
            if (args[i] == "-smRoute") runner.routeFile = args[i + 1];
        }
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
