using System.Collections.Generic;
using System.Linq;
using TMPro;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Map rotation setup:
///   Tools > Source Maps > Add Selected Map To Rotation   (on an imported map root: SourceMapInfo + spawn)
///   Tools > Source Maps > Create Lobby                    (vote manager, lobby board, a panel in every map)
///   Tools > Source Maps > Build Test Scene                (6 small box maps + lobby, for trying it out)
/// </summary>
public static class SourceMapsSetup
{
    const string TestScenePath = "Assets/SourceMaps/SourceMapsTest.unity";
    static readonly Color ButtonColor = new Color(0.2f, 0.45f, 0.85f), AdminColor = new Color(0.85f, 0.35f, 0.2f);

    [MenuItem("Tools/Source Maps/Add Selected Map To Rotation")]
    public static void AddSelectedMap()
    {
        foreach (var go in Selection.gameObjects) AddMap(go);
    }

    /// <summary>Adds SourceMapInfo to a map root, with a spawn at its first player start.</summary>
    public static SourceMapInfo AddMap(GameObject root)
    {
        SourceMapImporter.EnsureProgramAssets();
        var info = root.GetComponent<SourceMapInfo>() ?? root.AddUdonSharpComponent<SourceMapInfo>();
        info.mapName = root.name;
        if (info.spawn == null)
        {
            var spawn = new GameObject("Spawn").transform;
            spawn.SetParent(root.transform, false);
            SourceEntity start = null;
            foreach (string cls in new[] { "info_player_counterterrorist", "info_player_terrorist", "info_player_start" })
            {
                start = root.GetComponentsInChildren<SourceEntity>(true).FirstOrDefault(e => e.className == cls);
                if (start != null) break;
            }
            if (start != null) spawn.SetPositionAndRotation(start.transform.position, start.transform.rotation);
            info.spawn = spawn;
        }
        UdonSharpEditorUtility.CopyProxyToUdon(info);
        EditorUtility.SetDirty(root);
        return info;
    }

    [MenuItem("Tools/Source Maps/Create Lobby")]
    public static void CreateLobbyMenu()
    {
        var manager = CreateLobby(Vector3.zero);
        Selection.activeGameObject = manager.gameObject;
    }

    /// <summary>
    /// Lobby at `position`: a floor, the spawn, the vote board and the owner controls, plus a panel at every map's
    /// spawn, and SourceTimer's practice bhop/surf courses beside it if SourceTimer is there. Uses every
    /// SourceMapInfo in the scene. Moves the VRC World spawn into the lobby.
    /// </summary>
    public static SourceMapManager CreateLobby(Vector3 position)
    {
        SourceMapImporter.EnsureProgramAssets();
        EnsureTextMeshPro();
        foreach (var old in Object.FindObjectsOfType<SourceMapManager>(true)) Object.DestroyImmediate(old.transform.root.gameObject);
        var maps = Object.FindObjectsOfType<SourceMapInfo>(true).OrderBy(m => m.mapName).ToArray();

        var root = new GameObject("SourceMaps Lobby");
        root.transform.position = position;
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Floor";
        floor.transform.SetParent(root.transform, false);
        floor.transform.localPosition = new Vector3(0, -0.25f, 0);
        floor.transform.localScale = new Vector3(14, 0.5f, 14);
        floor.isStatic = true;
        var spawn = new GameObject("LobbySpawn").transform;
        spawn.SetParent(root.transform, false);

        var manager = new GameObject("SourceMapManager").AddUdonSharpComponent<SourceMapManager>();
        manager.transform.SetParent(root.transform, false);
        manager.maps = maps;
        manager.lobbySpawn = spawn;

        var screens = new List<SourceMapScreen> { CreateBoard(root.transform, manager, new Vector3(0, 0, 5)) };
        foreach (var map in maps)
            if (map.spawn != null) screens.Add(CreatePanel(map, manager));
        manager.screens = screens.ToArray();
        UdonSharpEditorUtility.CopyProxyToUdon(manager);

        // Practice bhop and surf courses with their own records, from SourceTimer if it's in the project
        // (looked up by name so SourceMaps doesn't need SourceTimer).
        var practice = FindType("PracticeCourses");
        if (practice != null) practice.GetMethod("Build").Invoke(null, new object[] { root.transform, Vector3.zero });
        // Saved boards for every map course with timer zones, on a wall behind the spawn (always active, unlike maps).
        var boards = FindType("CourseBoards");
        if (boards != null) boards.GetMethod("Build").Invoke(null, new object[] { root.transform, new Vector3(0, 0, -6.5f), Quaternion.Euler(0, 180, 0) });

        var descriptor = Object.FindObjectOfType<VRC.SDKBase.VRC_SceneDescriptor>();
        if (descriptor != null) descriptor.transform.SetPositionAndRotation(spawn.position, spawn.rotation);
        return manager;
    }

    /// <summary>The lobby vote board: 6 map slots (press a thumbnail to vote), extend, status, owner controls.</summary>
    static SourceMapScreen CreateBoard(Transform parent, SourceMapManager manager, Vector3 at)
    {
        var board = new GameObject("VoteBoard");
        board.transform.SetParent(parent, false);
        board.transform.localPosition = at;
        // World-space canvases read from their -z side: unrotated, the board faces the spawn (which looks along +z).
        var screen = board.AddUdonSharpComponent<SourceMapScreen>();
        screen.manager = manager;

        var back = GameObject.CreatePrimitive(PrimitiveType.Cube);
        back.name = "Backplate";
        back.transform.SetParent(board.transform, false);
        back.transform.localPosition = new Vector3(0, 1.9f, 0.06f);
        back.transform.localScale = new Vector3(7.6f, 3.6f, 0.05f);
        back.GetComponent<MeshRenderer>().sharedMaterial = Colored(new Color(0.08f, 0.09f, 0.12f));

        screen.status = Text(board.transform, "Status", "Vote for the next map", new Vector3(0, 3.35f, 0), new Vector2(7, 0.5f), 0.16f);

        var roots = new GameObject[6];
        var images = new RawImage[6];
        var texts = new TextMeshProUGUI[6];
        for (int i = 0; i < 6; i++)
        {
            float x = (i - 2.5f) * 1.2f;
            var slot = Button(board.transform, "Slot" + (i + 1), "", manager, "map", i, new Vector3(x, 2.35f, 0), new Vector3(1.1f, 0.62f, 0.04f), new Color(0.15f, 0.15f, 0.18f));
            images[i] = Image(slot.transform, new Vector2(1.05f, 0.59f), -0.025f);
            texts[i] = Text(slot.transform, "SlotText", "map", new Vector3(0, -0.6f, 0), new Vector2(1.15f, 0.5f), 0.09f);
            roots[i] = slot;
        }
        screen.slotRoots = roots;
        screen.slotImages = images;
        screen.slotTexts = texts;

        var extend = Button(board.transform, "Extend", "", manager, "extend", -1, new Vector3(0, 1.2f, 0), new Vector3(2.4f, 0.32f, 0.04f), new Color(0.2f, 0.55f, 0.3f));
        screen.extendRoot = extend;
        screen.extendText = Text(extend.transform, "ExtendText", "Extend", new Vector3(0, 0, -0.025f), new Vector2(2.3f, 0.3f), 0.1f);

        var admin = new GameObject("OwnerControls");
        admin.transform.SetParent(board.transform, false);
        screen.adminRoot = admin;
        screen.adminText = Text(admin.transform, "OwnerText", "Owner controls", new Vector3(-3.15f, 0.55f, 0), new Vector2(1.0f, 0.3f), 0.08f);
        string[] actions = { "lock", "start", "force", "forcelobby" };
        string[] labels = { "Lock / unlock vote", "Start now", "Force a map", "Everyone to lobby" };
        for (int i = 0; i < actions.Length; i++)
            Button(admin.transform, actions[i], labels[i], manager, actions[i], -1, new Vector3(-1.75f + i * 1.2f, 0.55f, 0), new Vector3(1.1f, 0.25f, 0.04f), AdminColor);
        Button(board.transform, "Rejoin", "Rejoin map", manager, "rejoin", -1, new Vector3(3.15f, 0.55f, 0), new Vector3(0.9f, 0.25f, 0.04f), ButtonColor);

        UdonSharpEditorUtility.CopyProxyToUdon(screen);
        return screen;
    }

    /// <summary>Panel at a map's spawn: status (time left, rtv), RTV and lobby buttons, owner controls.</summary>
    static SourceMapScreen CreatePanel(SourceMapInfo map, SourceMapManager manager)
    {
        var old = map.transform.Find("SourceMapPanel");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var panel = new GameObject("SourceMapPanel");
        panel.transform.SetParent(map.transform, false);
        // 1.5 m to the right of the spawn, facing it.
        panel.transform.position = map.spawn.position + map.spawn.right * 1.5f + map.spawn.forward * 1.0f;
        Vector3 away = panel.transform.position - map.spawn.position;
        panel.transform.rotation = Quaternion.LookRotation(new Vector3(away.x, 0, away.z), Vector3.up); // -z faces the spawn
        var screen = panel.AddUdonSharpComponent<SourceMapScreen>();
        screen.manager = manager;
        screen.status = Text(panel.transform, "Status", "", new Vector3(0, 1.75f, 0), new Vector2(1.6f, 0.4f), 0.08f);
        Button(panel.transform, "RTV", "Rock the vote", manager, "rtv", -1, new Vector3(-0.42f, 1.4f, 0), new Vector3(0.8f, 0.22f, 0.04f), ButtonColor);
        Button(panel.transform, "Lobby", "Back to lobby", manager, "lobby", -1, new Vector3(0.42f, 1.4f, 0), new Vector3(0.8f, 0.22f, 0.04f), ButtonColor);
        var admin = new GameObject("OwnerControls");
        admin.transform.SetParent(panel.transform, false);
        screen.adminRoot = admin;
        Button(admin.transform, "ForceLobby", "Everyone to lobby", manager, "forcelobby", -1, new Vector3(-0.42f, 1.12f, 0), new Vector3(0.8f, 0.22f, 0.04f), AdminColor);
        Button(admin.transform, "AddTime", "+ time", manager, "addtime", -1, new Vector3(0.42f, 1.12f, 0), new Vector3(0.8f, 0.22f, 0.04f), AdminColor);
        UdonSharpEditorUtility.CopyProxyToUdon(screen);
        return screen;
    }

    // The sample world's maps (GameBanana pages in Tests/Bsp/get_maps.sh): author credits for the vote screen.
    static readonly Dictionary<string, string> Authors = new Dictionary<string, string>
    {
        ["bhop_japan"] = "Tony Montana", ["bhop_kitsune"] = "Ghost1447951", ["bhop_eazy_v2"] = "31K4L",
        ["bhop_arcane_v1"] = "Panzerhandschuh", ["bhop_badges"] = "Badges & fission",
    };
    const float MapSpacing = 700f; // metres between map origins (Source maps are up to ~620 m across)

    [MenuItem("Tools/Source Maps/Build Sample World...")]
    public static void BuildSampleWorldMenu()
    {
        string folder = EditorUtility.OpenFolderPanel("Folder with the maps' .bsp files", "", "");
        if (string.IsNullOrEmpty(folder)) return;
        if (SourceMapVisuals.CssFolder == "")
            SourceMapVisuals.CssFolder = EditorUtility.OpenFolderPanel("Your Counter-Strike Source folder (with cstrike and hl2)", "", "");
        BuildSampleWorld(folder, null);
    }

    /// <summary>
    /// A world with every .bsp in `bspFolder`: each map imported (visuals with uSource if installed, collision, markers,
    /// timer zones from zones-cstrike), 700 m apart along +x, added to the rotation with a rendered thumbnail; then the
    /// lobby. `zonesFolder` (optional) holds &lt;map&gt;.json zone files instead of downloading them.
    /// </summary>
    public static void BuildSampleWorld(string bspFolder, string zonesFolder)
    {
        SourceMapImporter.EnsureProgramAssets();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        var world = AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.vrchat.worlds/Samples/UdonExampleScene/Prefabs/VRCWorld.prefab");
        if (world != null) PrefabUtility.InstantiatePrefab(world);
        var movement = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/SourceMovement/SourceMovement.prefab");
        if (movement != null) PrefabUtility.InstantiatePrefab(movement);

        var files = System.IO.Directory.GetFiles(bspFolder, "*.bsp").OrderBy(f => f).ToArray();
        for (int i = 0; i < files.Length; i++)
        {
            string map = System.IO.Path.GetFileNameWithoutExtension(files[i]);
            var root = SourceMapImporter.Import(files[i], SourceMaps.Bsp.BspGeometry.DefaultScale);
            root.transform.position = new Vector3(MapSpacing * (i + 1), 0, 0);
            SourceMapVisuals.Import(files[i], root.transform, SourceMaps.Bsp.BspGeometry.DefaultScale);
            string zoneFile = zonesFolder != null ? System.IO.Path.Combine(zonesFolder, map + ".json") : null;
            string json = zoneFile != null ? (System.IO.File.Exists(zoneFile) ? System.IO.File.ReadAllText(zoneFile) : null) : SourceMapZones.Download(map);
            SourceMapZones.Import(root, map, json, SourceMaps.Bsp.BspGeometry.DefaultScale);
            var info = AddMap(root);
            info.author = Authors.TryGetValue(map, out var author) ? author : "";
            if (info.thumbnail == null) info.thumbnail = RenderThumbnail(root, info.spawn, map);
            UdonSharpEditorUtility.CopyProxyToUdon(info);
        }
        CreateLobby(Vector3.zero);
        EditorSceneManager.SaveScene(scene, "Assets/SourceMapsSample.unity");
    }

    /// <summary>A 512 x 288 picture from the spawn, saved next to the map's meshes.</summary>
    public static Texture2D RenderThumbnail(GameObject mapRoot, Transform spawn, string map)
    {
        if (spawn == null) return null;
        var cam = new GameObject("ThumbnailCamera").AddComponent<Camera>();
        cam.transform.SetPositionAndRotation(spawn.position + Vector3.up * 1.4f, Quaternion.Euler(8, spawn.eulerAngles.y, 0));
        cam.fieldOfView = 80;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 2000;
        var rt = new RenderTexture(512, 288, 24);
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(512, 288, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 512, 288), 0, 0);
        RenderTexture.active = null;
        Object.DestroyImmediate(cam.gameObject);
        rt.Release();
        string path = "Assets/SourceMapsImported/" + map + "/" + map + "_thumbnail.png";
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
        System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
        AssetDatabase.ImportAsset(path);
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    [MenuItem("Tools/Source Maps/Build Test Scene")]
    public static void BuildTestScene()
    {
        SourceMapImporter.EnsureProgramAssets();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        var world = AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.vrchat.worlds/Samples/UdonExampleScene/Prefabs/VRCWorld.prefab");
        if (world != null) PrefabUtility.InstantiatePrefab(world);
        // Six small maps far apart (like imported maps at their own offsets), each a floor with a coloured pillar.
        Color[] colors = { Color.red, Color.green, Color.blue, Color.yellow, Color.cyan, Color.magenta };
        for (int i = 0; i < 6; i++)
        {
            var root = new GameObject("test_map_" + (i + 1));
            root.transform.position = new Vector3(200 + i * 100, 0, 0);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.SetParent(root.transform, false);
            floor.transform.localPosition = new Vector3(0, -0.5f, 10);
            floor.transform.localScale = new Vector3(20, 1, 40);
            var pillar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pillar.transform.SetParent(root.transform, false);
            pillar.transform.localPosition = new Vector3(0, 2, 20);
            pillar.transform.localScale = new Vector3(2, 4, 2);
            pillar.GetComponent<MeshRenderer>().sharedMaterial = Colored(colors[i]);
            var info = AddMap(root);
            info.author = "Test author " + (i + 1);
            info.thumbnail = Swatch(colors[i]);
            UdonSharpEditorUtility.CopyProxyToUdon(info);
        }
        var movement = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/SourceMovement/SourceMovement.prefab");
        if (movement != null) PrefabUtility.InstantiatePrefab(movement); // optional: works without it
        CreateLobby(Vector3.zero);
        EditorSceneManager.SaveScene(scene, TestScenePath);
    }

    // ------------------------------------------------------------------ helpers

    static System.Type FindType(string name)
    {
        return System.AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).FirstOrDefault(t => t != null);
    }

    /// <summary>TextMeshPro needs its "Essential Resources" (default font) or every text is invisible.</summary>
    static void EnsureTextMeshPro()
    {
        if (AssetDatabase.FindAssets("t:TMP_Settings").Length > 0) return;
        AssetDatabase.ImportPackage("Packages/com.unity.textmeshpro/Package Resources/TMP Essential Resources.unitypackage", false);
        AssetDatabase.Refresh();
    }

    /// <summary>
    /// An Interact button: an unscaled object (returned, so hiding it hides everything) holding the cube that is
    /// pressed and its label, auto-sized to fit.
    /// </summary>
    static GameObject Button(Transform parent, string name, string label, SourceMapManager manager, string action, int slot,
        Vector3 at, Vector3 size, Color color)
    {
        var holder = new GameObject(name);
        holder.transform.SetParent(parent, false);
        holder.transform.localPosition = at;
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "Press";
        cube.transform.SetParent(holder.transform, false);
        cube.transform.localScale = size;
        cube.GetComponent<MeshRenderer>().sharedMaterial = Colored(color);
        var button = cube.AddUdonSharpComponent<SourceMapButton>();
        button.manager = manager;
        button.action = action;
        button.slot = slot;
        button.InteractionText = label != "" ? label : "Vote";
        UdonSharpEditorUtility.CopyProxyToUdon(button);
        if (label != "")
        {
            var text = Text(holder.transform, "Label", label, new Vector3(0, 0, -size.z / 2 - 0.005f), new Vector2(size.x * 0.92f, size.y * 0.9f), size.y * 0.5f);
            text.enableAutoSizing = true;
            text.fontSizeMin = 1;
            text.fontSizeMax = size.y * 50;
        }
        return holder;
    }

    /// <summary>World-space TextMeshPro text, size and font size in metres.</summary>
    static TextMeshProUGUI Text(Transform parent, string name, string text, Vector3 at, Vector2 size, float fontSize)
    {
        var canvasObject = new GameObject(name, typeof(Canvas));
        canvasObject.transform.SetParent(parent, false);
        canvasObject.transform.localPosition = at;
        canvasObject.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        var rect = canvasObject.GetComponent<RectTransform>();
        rect.sizeDelta = size * 100;
        rect.localScale = Vector3.one * 0.01f;
        var textObject = new GameObject("Text", typeof(RectTransform));
        textObject.transform.SetParent(canvasObject.transform, false);
        var label = textObject.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.alignment = TextAlignmentOptions.Center;
        label.fontSize = fontSize * 100;
        label.enableWordWrapping = true;
        var textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.sizeDelta = Vector2.zero;
        return label;
    }

    static RawImage Image(Transform holder, Vector2 sizeMetres, float z)
    {
        var canvasObject = new GameObject("Thumbnail", typeof(Canvas));
        canvasObject.transform.SetParent(holder, false);
        canvasObject.transform.localPosition = new Vector3(0, 0, z);
        canvasObject.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        var rect = canvasObject.GetComponent<RectTransform>();
        rect.sizeDelta = sizeMetres * 100;
        rect.localScale = Vector3.one * 0.01f;
        var imageObject = new GameObject("Image", typeof(RectTransform));
        imageObject.transform.SetParent(canvasObject.transform, false);
        var image = imageObject.AddComponent<RawImage>();
        var r = imageObject.GetComponent<RectTransform>();
        r.anchorMin = Vector2.zero;
        r.anchorMax = Vector2.one;
        r.sizeDelta = Vector2.zero;
        return image;
    }

    static readonly Dictionary<Color, Material> materials = new Dictionary<Color, Material>();

    static Material Colored(Color color)
    {
        if (materials.TryGetValue(color, out var m) && m != null) return m;
        m = new Material(Shader.Find("Standard")) { color = color };
        string path = "Assets/SourceMaps/Materials/" + ColorUtility.ToHtmlStringRGB(color) + ".mat";
        System.IO.Directory.CreateDirectory("Assets/SourceMaps/Materials");
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) m = existing;
        else AssetDatabase.CreateAsset(m, path);
        materials[color] = m;
        return m;
    }

    /// <summary>A plain coloured texture asset (test thumbnails).</summary>
    static Texture2D Swatch(Color color)
    {
        string path = "Assets/SourceMaps/Materials/thumb_" + ColorUtility.ToHtmlStringRGB(color) + ".asset";
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (tex != null) return tex;
        tex = new Texture2D(4, 4);
        var pixels = new Color[16];
        for (int i = 0; i < 16; i++) pixels[i] = color * (i % 5 == 0 ? 0.7f : 1f);
        tex.SetPixels(pixels);
        tex.Apply();
        AssetDatabase.CreateAsset(tex, path);
        return tex;
    }
}
