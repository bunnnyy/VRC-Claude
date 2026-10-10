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

        // World button to toggle auto bhop (VR players have no B key). Move it wherever you like.
        var button = GameObject.CreatePrimitive(PrimitiveType.Cube);
        button.name = "AutoBhopButton";
        button.transform.SetParent(movementRoot.transform, false);
        button.transform.localPosition = new Vector3(0, 1f, 0);
        button.transform.localScale = Vector3.one * 0.25f;
        var toggle = button.AddUdonSharpComponent<AutoBhopButton>();
        toggle.movement = movement;
        var buttonLabel = CreateWorldText(button.transform, "Auto bhop", 0.6f);
        buttonLabel.transform.parent.localPosition = new Vector3(0, 1.2f, 0); // above the cube (cube space)
        buttonLabel.transform.parent.localScale = Vector3.one * 0.004f;
        toggle.label = buttonLabel;
        UdonSharpEditorUtility.CopyProxyToUdon(toggle);

        PrefabUtility.SaveAsPrefabAsset(movementRoot, MovementPrefab);
        Object.DestroyImmediate(movementRoot);

        // Timer: run timer with HUD text and synced boards for legit and auto bhop runs.
        var timerRoot = new GameObject("SourceTimer");
        var board = CreateBoard(timerRoot.transform, "Leaderboard (Legit)", "Best times (legit)", 0f);
        var autoBoard = CreateBoard(timerRoot.transform, "Leaderboard (Auto)", "Best times (auto bhop)", 1.6f);

        var hudObject = new GameObject("RunTimer");
        hudObject.transform.SetParent(timerRoot.transform, false);
        var timer = hudObject.AddUdonSharpComponent<RunTimer>();
        timer.leaderboard = board;
        timer.autoLeaderboard = autoBoard;
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
        timerInstance.transform.position = new Vector3(-400, 160, 300) * U; // boards beside the spawn
        timer.categorySource = movementInstance.GetComponent<SourceMovement>(); // auto bhop runs get their own board
        UdonSharpEditorUtility.CopyProxyToUdon(timer);
        movementInstance.transform.Find("AutoBhopButton").position = new Vector3(200, 48, 250) * U;

        BuildMap(timer);
        // The test map is hull-only: VRChat's taller capsule passes through it, the CS:S hull collides (ducking).
        var movementScript = movementInstance.GetComponent<SourceMovement>();
        movementScript.hullOnly = new[] { GameObject.Find("TestMap") };
        UdonSharpEditorUtility.CopyProxyToUdon(movementScript);
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

        // A 512 unit tower beside the lane with a ladder on the side facing the lane (CS:S func_ladder style:
        // a trigger volume on the ladder layer against the climbable face).
        Solid(map, "LadderTower", new Vector3(200, 256, 500), new Vector3(96, 512, 96));
        var ladder = Solid(map, "Ladder", new Vector3(148, 256, 500), new Vector3(8, 512, 64));
        ladder.GetComponent<BoxCollider>().isTrigger = true;
        ladder.layer = 22;

        // Water beside the lane: a deep pool, then a waist-deep pool ending at a ledge to climb out onto.
        // Water is a trigger volume on the Water layer (4); the floor under it is a normal collider.
        Solid(map, "PoolFloor", new Vector3(-900, -32, 1356), new Vector3(512, 64, 1912));
        Water(map, "DeepWater", new Vector3(-900, 100, 800), new Vector3(512, 200, 600));
        Water(map, "ShallowWater", new Vector3(-900, 20, 1600), new Vector3(512, 40, 600));
        Solid(map, "PoolLedge", new Vector3(-900, 20, 2100), new Vector3(512, 40, 400));

        // Boosters on their own platform: a sideways trigger_push, an upward push column, a basevelocity
        // launch pad, and half-gravity / normal-gravity pads. Their movement field is left empty on purpose:
        // they find the object named SourceMovement.
        Solid(map, "BoostFloor", new Vector3(900, -32, 1000), new Vector3(512, 64, 1200));
        Trigger<SourcePushTrigger>(map, "PushPad", new Vector3(900, 64, 600), new Vector3(256, 128, 128)).push = new Vector3(0, 0, 800);
        Trigger<SourcePushTrigger>(map, "PushUp", new Vector3(1060, 256, 1000), new Vector3(128, 512, 128)).push = new Vector3(0, 1000, 0);
        Trigger<SourceBoostTrigger>(map, "LaunchPad", new Vector3(900, 32, 1000), new Vector3(128, 64, 128)).addVelocity = new Vector3(0, 600, 0);
        var low = Trigger<SourceBoostTrigger>(map, "HalfGravityPad", new Vector3(900, 32, 1300), new Vector3(128, 64, 128));
        low.setGravity = true;
        low.gravityScale = 0.5f;
        Trigger<SourceBoostTrigger>(map, "NormalGravityPad", new Vector3(900, 32, 1500), new Vector3(128, 64, 128)).setGravity = true;
        foreach (var trigger in map.GetComponentsInChildren<UdonSharpBehaviour>())
            if (trigger is SourcePushTrigger || trigger is SourceBoostTrigger) UdonSharpEditorUtility.CopyProxyToUdon(trigger);

        // A wall facing -x beside its own floor, one mesh with vertical seams every 200 units, like a wall
        // built from several brushes in a converted map (the seams can report an edge normal along the wall).
        Solid(map, "WallFloor", new Vector3(1600, -32, 1000), new Vector3(512, 64, 1200));
        SeamedWall(map, "SeamedWall", 1856, 256, 400, 600, 800, 1000, 1200, 1400, 1600);
        // Ducking: a ceiling 50 units over the floor (only a ducked hull fits under it) and a 62 unit ledge beside the
        // booster platform (a crouch jump reaches it, a plain jump doesn't).
        Solid(map, "DuckGap", new Vector3(1520, 100, 1300), new Vector3(352, 100, 200));
        Solid(map, "Ledge62", new Vector3(720, 31, 1450), new Vector3(128, 62, 200));
        // A 64 unit bhop block as a mesh (like a converted func_door) on the same floor, to stand on its edge.
        MeshBlock(map, "EdgeBlock", new Vector3(1500, 24, 700), new Vector3(64, 48, 64));

        // Surf ramp: two 60 degree faces meeting at a ridge, running along Z below the end of the lane.
        Vector3 ridge = new Vector3(400, -256, 0);
        // One mesh with seams across the direction of travel every 1500 units, like coplanar brush faces in
        // converted maps (mesh colliders can report a fake edge normal at seams).
        SurfRamp(map, "SurfRamp", ridge, 60, 1024, 4300, 5800, 7300, 8800, 10300);

        // End platform past the ramp.
        Solid(map, "EndPlatform", new Vector3(400, -988, 10900), new Vector3(2048, 64, 1000));
        Zone(map, "EndZone", TimerZoneType.End, timer, new Vector3(400, -892, 10900), new Vector3(2048, 128, 1000));

        // Catch-all reset under everything.
        Zone(map, "ResetZone", TimerZoneType.Reset, timer, new Vector3(0, -2000, 6000), new Vector3(20000, 200, 20000));

        var descriptor = Object.FindObjectOfType<VRC.SDKBase.VRC_SceneDescriptor>();
        if (descriptor != null) descriptor.transform.position = start.position;
    }

    /// <summary>
    /// A surf ramp as one solid triangular prism (mesh collider, like a converted Source map): two faces at
    /// the given angle meeting at the ridge, each slopeLength long, running along Z through the cuts. Each
    /// slice between cuts gets its own triangles, so the faces have seams across the direction of travel, like
    /// coplanar brush faces in a converted map.
    /// </summary>
    static void SurfRamp(Transform parent, string name, Vector3 ridge, float angle, float slopeLength, params float[] cuts)
    {
        float rad = angle * Mathf.Deg2Rad;
        Vector3 down = new Vector3(Mathf.Cos(rad), -Mathf.Sin(rad), 0) * slopeLength;
        Vector3 top = ridge, left = ridge + new Vector3(-down.x, down.y, 0), right = ridge + down;
        var vertices = new System.Collections.Generic.List<Vector3>();
        var triangles = new System.Collections.Generic.List<int>();
        void Quad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3) // clockwise seen from outside
        {
            int i = vertices.Count;
            vertices.AddRange(new[] { p0, p1, p2, p3 });
            triangles.AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 });
        }
        for (int c = 0; c + 1 < cuts.Length; c++)
        {
            Vector3 a = new Vector3(0, 0, cuts[c]), b = new Vector3(0, 0, cuts[c + 1]);
            Quad(left + a, top + a, top + b, left + b);    // left face
            Quad(top + a, right + a, right + b, top + b);  // right face
            Quad(right + a, left + a, left + b, right + b); // bottom
        }
        Vector3 first = new Vector3(0, 0, cuts[0]), last = new Vector3(0, 0, cuts[cuts.Length - 1]);
        int n = vertices.Count;
        vertices.AddRange(new[] { left + first, right + first, top + first, left + last, top + last, right + last });
        triangles.AddRange(new[] { n, n + 2, n + 1, n + 3, n + 5, n + 4 }); // end caps
        for (int i = 0; i < vertices.Count; i++) vertices[i] *= U;
        var mesh = new Mesh { name = name };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        Object.DestroyImmediate(go.GetComponent<BoxCollider>());
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshCollider>().sharedMesh = mesh;
        go.isStatic = true;
    }

    /// <summary>A box as a mesh collider (8 corners, 12 triangles), centre and size in Source units.</summary>
    static void MeshBlock(Transform parent, string name, Vector3 center, Vector3 size)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = center * U;
        go.transform.localScale = size * U;
        Object.DestroyImmediate(go.GetComponent<BoxCollider>());
        go.AddComponent<MeshCollider>().sharedMesh = go.GetComponent<MeshFilter>().sharedMesh;
        go.isStatic = true;
    }

    /// <summary>
    /// A wall face at x facing -x, from y 0 to height, as one mesh split at each z cut (vertical seams).
    /// </summary>
    static void SeamedWall(Transform parent, string name, float x, float height, params float[] cuts)
    {
        var vertices = new System.Collections.Generic.List<Vector3>();
        var triangles = new System.Collections.Generic.List<int>();
        for (int c = 0; c + 1 < cuts.Length; c++)
        {
            int i = vertices.Count;
            vertices.AddRange(new[] { new Vector3(x, 0, cuts[c]), new Vector3(x, height, cuts[c]),
                new Vector3(x, height, cuts[c + 1]), new Vector3(x, 0, cuts[c + 1]) });
            triangles.AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 });
        }
        for (int i = 0; i < vertices.Count; i++) vertices[i] *= U;
        var mesh = new Mesh { name = name };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        Object.DestroyImmediate(go.GetComponent<BoxCollider>());
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshCollider>().sharedMesh = mesh;
        go.isStatic = true;
    }

    // ------------------------------------------------------------------ helpers (Source units in, metres out)

    static Leaderboard CreateBoard(Transform parent, string name, string title, float x)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(x, 0, 0);
        var board = go.AddUdonSharpComponent<Leaderboard>();
        board.title = title;
        board.board = CreateWorldText(go.transform, title, 1.5f);
        UdonSharpEditorUtility.CopyProxyToUdon(board);
        return board;
    }

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

    /// <summary>A visible, see-through trigger box with a U# behaviour on it.</summary>
    static T Trigger<T>(Transform parent, string name, Vector3 center, Vector3 size) where T : UdonSharpBehaviour
    {
        var box = Solid(parent, name, center, size);
        box.GetComponent<BoxCollider>().isTrigger = true;
        box.GetComponent<MeshRenderer>().sharedMaterial = SeeThrough(new Color(1f, 0.6f, 0.1f, 0.35f));
        return box.AddUdonSharpComponent<T>();
    }

    static Material SeeThrough(Color color)
    {
        var material = new Material(Shader.Find("Standard")) { color = color };
        // Standard shader in transparent mode.
        material.SetFloat("_Mode", 3);
        material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
        material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.SetInt("_ZWrite", 0);
        material.EnableKeyword("_ALPHAPREMULTIPLY_ON");
        material.renderQueue = 3000;
        return material;
    }

    static void Water(Transform parent, string name, Vector3 center, Vector3 size)
    {
        var water = Solid(parent, name, center, size);
        water.GetComponent<BoxCollider>().isTrigger = true;
        water.layer = 4;
        water.GetComponent<MeshRenderer>().sharedMaterial = SeeThrough(new Color(0.2f, 0.45f, 0.9f, 0.4f));
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
                // The scripts are already in the current U# format; without this U# waits for an upgrade pass
                // and refuses to serialize the new behaviours until the next editor update.
                programAsset.ScriptVersion = UdonSharpProgramVersion.CurrentVersion;
                AssetDatabase.CreateAsset(programAsset, assetPath);
                created = true;
            }
        }
        if (created) AssetDatabase.Refresh();
        // Always compile: serializing a behaviour whose program is older than its script fails.
        UdonSharp.Compiler.UdonSharpCompilerV1.CompileSync();
    }
}
