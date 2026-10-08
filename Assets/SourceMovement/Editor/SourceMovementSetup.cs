using System.IO;
using TMPro;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Menu items that build the prefabs and a test map:
///   Tools > Source Movement > Create Prefabs
///   Tools > Source Movement > Build Test Scene
/// Map sizes are in Source units (1 unit = 0.01905 m), the same scale the movement uses.
/// </summary>
public static class SourceMovementSetup
{
    const float U = 0.01905f;
    const string MovementPrefab = "Assets/SourceMovement/SourceMovement.prefab";
    const string TimerPrefab = "Assets/SourceTimer/SourceTimer.prefab";
    const string ScenePath = "Assets/SourceMovement/SourceTestMap.unity";
    const string WorldPrefab = "Packages/com.vrchat.worlds/Samples/UdonExampleScene/Prefabs/VRCWorld.prefab";

    [MenuItem("Tools/Source Movement/Create Prefabs")]
    public static void CreatePrefabs()
    {
        EnsureProgramAssets();

        // Movement: SourceMovement, a debug speedometer (off) and an optional activation zone (disabled).
        var movementRoot = new GameObject("SourceMovement");
        var movement = movementRoot.AddUdonSharpComponent<SourceMovement>();

        var speedoObject = new GameObject("Speedometer");
        speedoObject.transform.SetParent(movementRoot.transform, false);
        var speedometer = speedoObject.AddUdonSharpComponent<SourceSpeedometer>();
        speedometer.movement = movement;
        speedometer.label = CreateHudText(speedoObject.transform, "0", 64);
        UdonSharpEditorUtility.CopyProxyToUdon(speedometer);

        var zoneObject = new GameObject("MovementZone (optional)");
        zoneObject.transform.SetParent(movementRoot.transform, false);
        zoneObject.AddComponent<BoxCollider>().isTrigger = true;
        var zone = zoneObject.AddUdonSharpComponent<SourceMovementZone>();
        zone.movement = movement;
        UdonSharpEditorUtility.CopyProxyToUdon(zone);
        zoneObject.SetActive(false);

        PrefabUtility.SaveAsPrefabAsset(movementRoot, MovementPrefab);
        Object.DestroyImmediate(movementRoot);

        // Timer: run timer with HUD text and a synced leaderboard board.
        var timerRoot = new GameObject("SourceTimer");
        var boardObject = new GameObject("Leaderboard");
        boardObject.transform.SetParent(timerRoot.transform, false);
        var board = boardObject.AddUdonSharpComponent<Leaderboard>();
        board.board = CreateWorldText(boardObject.transform, "Best times", 1.5f);
        UdonSharpEditorUtility.CopyProxyToUdon(board);

        var hudObject = new GameObject("RunTimer");
        hudObject.transform.SetParent(timerRoot.transform, false);
        var timer = hudObject.AddUdonSharpComponent<RunTimer>();
        timer.leaderboard = board;
        timer.label = CreateHudText(hudObject.transform, "", 48);
        UdonSharpEditorUtility.CopyProxyToUdon(timer);

        PrefabUtility.SaveAsPrefabAsset(timerRoot, TimerPrefab);
        Object.DestroyImmediate(timerRoot);
        AssetDatabase.SaveAssets();
        Debug.Log("[Source Movement] Prefabs saved: " + MovementPrefab + ", " + TimerPrefab);
    }

    [MenuItem("Tools/Source Movement/Build Test Scene")]
    public static void BuildTestScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        CreatePrefabs();
        EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        var world = AssetDatabase.LoadAssetAtPath<GameObject>(WorldPrefab);
        if (world != null) PrefabUtility.InstantiatePrefab(world);
        else Debug.LogWarning("[Source Movement] VRCWorld prefab not found, add a VRC Scene Descriptor yourself.");

        var movementInstance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(MovementPrefab));
        var speedometer = movementInstance.GetComponentInChildren<SourceSpeedometer>();
        speedometer.show = true; // handy while testing
        UdonSharpEditorUtility.CopyProxyToUdon(speedometer);

        var timerInstance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(TimerPrefab));
        var timer = timerInstance.GetComponentInChildren<RunTimer>();
        timerInstance.GetComponentInChildren<Leaderboard>().transform.position = new Vector3(-400, 160, 300) * U;

        BuildMap(timer);
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);
        Debug.Log("[Source Movement] Test scene saved: " + ScenePath);
    }

    /// <summary>
    /// Course along +Z: spawn and start zone, a 4000 unit bhop lane, a drop onto a 60 degree surf ramp,
    /// and an end platform. Falling anywhere resets you to the last checkpoint.
    /// </summary>
    static void BuildMap(RunTimer timer)
    {
        var map = new GameObject("TestMap").transform;

        // Spawn, start zone and the bhop lane (top surface at y = 0).
        Solid(map, "BhopLane", new Vector3(0, -32, 2000), new Vector3(512, 64, 4000));
        var start = Point(map, "StartPoint", new Vector3(0, 0, 100));
        timer.startPoint = start;
        UdonSharpEditorUtility.CopyProxyToUdon(timer);
        Zone(map, "StartZone", TimerZoneType.Start, timer, new Vector3(0, 64, 128), new Vector3(512, 128, 256));
        Zone(map, "Checkpoint", TimerZoneType.Checkpoint, timer, new Vector3(0, 64, 3900), new Vector3(512, 128, 200))
            .respawnPoint = Point(map, "CheckpointPoint", new Vector3(0, 0, 3900));

        // A 16 unit step and a 24 unit wall beside the lane, to feel the step limit.
        Solid(map, "Step16", new Vector3(-400, 8, 600), new Vector3(256, 16, 256));
        Solid(map, "Wall24", new Vector3(-400, 12, 1000), new Vector3(256, 24, 256));

        // Surf ramp: two 60 degree faces meeting at a ridge, running along Z below the end of the lane.
        Vector3 ridge = new Vector3(400, -256, 0);
        const float halfWidth = 512, thickness = 64, length = 6000, rampZ = 7300;
        SurfFace(map, "SurfRampLeft", ridge, 60, halfWidth, thickness, length, rampZ);
        SurfFace(map, "SurfRampRight", ridge, -60, -halfWidth, thickness, length, rampZ);

        // End platform past the ramp.
        Solid(map, "EndPlatform", new Vector3(400, -988, 10900), new Vector3(2048, 64, 1000));
        Zone(map, "EndZone", TimerZoneType.End, timer, new Vector3(400, -892, 10900), new Vector3(2048, 128, 1000));

        // Catch-all reset under everything.
        Zone(map, "ResetZone", TimerZoneType.Reset, timer, new Vector3(0, -2000, 6000), new Vector3(20000, 200, 20000));

        var descriptor = Object.FindObjectOfType<VRC.SDKBase.VRC_SceneDescriptor>();
        if (descriptor != null) descriptor.transform.position = start.position;
    }

    static void SurfFace(Transform parent, string name, Vector3 ridge, float angle, float halfWidth, float thickness, float length, float z)
    {
        Quaternion rotation = Quaternion.Euler(0, 0, angle);
        // The ridge is the box's top inner edge: local (halfWidth, thickness / 2).
        Vector3 center = ridge - rotation * new Vector3(halfWidth, thickness / 2, 0) + new Vector3(0, 0, z);
        Solid(parent, name, center, new Vector3(Mathf.Abs(halfWidth) * 2, thickness, length)).transform.rotation = rotation;
    }

    // ------------------------------------------------------------------ helpers (Source units in, metres out)

    static GameObject Solid(Transform parent, string name, Vector3 center, Vector3 size)
    {
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = name;
        cube.transform.SetParent(parent, false);
        cube.transform.position = center * U;
        cube.transform.localScale = size * U;
        cube.isStatic = true;
        return cube;
    }

    static TimerZone Zone(Transform parent, string name, TimerZoneType type, RunTimer timer, Vector3 center, Vector3 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = center * U;
        var box = go.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = size * U;
        var zone = go.AddUdonSharpComponent<TimerZone>();
        zone.zoneType = type;
        zone.timer = timer;
        UdonSharpEditorUtility.CopyProxyToUdon(zone);
        return zone;
    }

    static Transform Point(Transform parent, string name, Vector3 position)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = position * U;
        return go.transform;
    }

    /// <summary>Small text on a world space canvas; the owning script moves it in front of the view.</summary>
    static TextMeshProUGUI CreateHudText(Transform parent, string text, float fontSize)
    {
        TextMeshProUGUI label = CreateWorldText(parent, text, 0.6f);
        label.fontSize = fontSize;
        return label;
    }

    static TextMeshProUGUI CreateWorldText(Transform parent, string text, float widthMetres)
    {
        var canvasObject = new GameObject("Canvas", typeof(Canvas));
        canvasObject.transform.SetParent(parent, false);
        canvasObject.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        var rect = canvasObject.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(widthMetres * 1000, widthMetres * 1000);
        rect.localScale = Vector3.one * 0.001f;

        var textObject = new GameObject("Text", typeof(RectTransform));
        textObject.transform.SetParent(canvasObject.transform, false);
        var label = textObject.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.alignment = TextAlignmentOptions.Center;
        label.fontSize = 72;
        var textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.sizeDelta = Vector2.zero;
        return label;
    }

    /// <summary>Every UdonSharpBehaviour needs an UdonSharpProgramAsset next to its script.</summary>
    static void EnsureProgramAssets()
    {
        bool created = false;
        foreach (string folder in new[] { "Assets/SourceMovement/Scripts", "Assets/SourceTimer/Scripts" })
        {
            foreach (string scriptPath in Directory.GetFiles(folder, "*.cs"))
            {
                string assetPath = Path.ChangeExtension(scriptPath, ".asset").Replace('\\', '/');
                if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(assetPath) != null) continue;
                var programAsset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
                programAsset.sourceCsScript = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath.Replace('\\', '/'));
                AssetDatabase.CreateAsset(programAsset, assetPath);
                created = true;
            }
        }
        if (!created) return;
        AssetDatabase.Refresh();
        UdonSharp.Compiler.UdonSharpCompilerV1.CompileSync();
    }
}
