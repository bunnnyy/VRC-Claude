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
///   Tools > Source Maps > Build Sample World...           (every .bsp in a folder imported, plus the lobby)
///   Tools > Source Maps > Create Menu Prefabs             (Prefabs/VoteMenu and MapMenu, made again from this code)
/// The menus are prefabs: drag VoteMenu (lobby) or MapMenu (in a map) into a custom lobby; they find the object
/// named "SourceMapManager" themselves.
/// </summary>
public static class SourceMapsSetup
{
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
        if (info.skybox == null) // saved by SourceMapVisuals from the map's skyname
            info.skybox = AssetDatabase.LoadAssetAtPath<Material>("Assets/SourceMapsImported/" + root.name + "/" + root.name + "_sky.mat");
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
        // The old lobby (only what Create Lobby made: a manager moved into a custom lobby goes alone).
        foreach (var old in Object.FindObjectsOfType<SourceMapManager>(true))
        {
            if (old == null) continue;
            var parent = old.transform.parent;
            Undo.DestroyObjectImmediate(parent != null && parent.name == "SourceMaps Lobby" ? parent.gameObject : old.gameObject);
        }
        var maps = Object.FindObjectsOfType<SourceMapInfo>(true).OrderBy(m => m.mapName).ToArray();

        var root = new GameObject("SourceMaps Lobby");
        Undo.RegisterCreatedObjectUndo(root, "Create Lobby");
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
        SetHullOnly(maps);

        // Practice bhop and surf courses with their own records, from SourceTimer if it's in the project
        // (looked up by name so SourceMaps doesn't need SourceTimer).
        var practice = FindType("PracticeCourses");
        if (practice != null) practice.GetMethod("Build").Invoke(null, new object[] { root.transform, Vector3.zero });
        // Saved boards for every map course with timer zones, on a wall behind the spawn (always active, unlike maps).
        var boards = FindType("CourseBoards");
        if (boards != null) boards.GetMethod("Build").Invoke(null, new object[] { root.transform, new Vector3(0, 0, -6.5f), Quaternion.Euler(0, 180, 0) });

        var descriptor = Object.FindObjectOfType<VRC.SDKBase.VRC_SceneDescriptor>();
        if (descriptor != null)
        {
            descriptor.transform.SetPositionAndRotation(spawn.position, spawn.rotation);
            // VRChat respawns players who fall below this (default -100 m); Source maps go down to about -312 m.
            float lowest = -100f;
            foreach (var c in Object.FindObjectsOfType<MeshCollider>(true)) lowest = Mathf.Min(lowest, c.bounds.min.y);
            descriptor.RespawnHeightY = Mathf.Min(descriptor.RespawnHeightY, lowest - 50f);
            EditorUtility.SetDirty(descriptor);
        }
        return manager;
    }

    // Menu look: dark rounded panels, light text, one blue accent; owner controls in amber.
    static readonly Color PanelColor = new Color32(15, 18, 24, 255), CardColor = new Color32(30, 35, 46, 255),
        TextColor = new Color32(242, 244, 248, 255), Accent = new Color32(76, 141, 255, 255), Green = new Color32(47, 175, 110, 255),
        OwnerFill = new Color32(46, 40, 34, 255), OwnerText = new Color32(255, 181, 71, 255), Divider = new Color32(255, 255, 255, 20);

    /// <summary>
    /// The lobby vote board, a UI menu (VRChat's pointer, desktop and VR): status at the top, 6 map cards (thumbnail,
    /// name, author, votes; press one to vote), extend, and a footer with the owner controls and Rejoin.
    /// </summary>
    static SourceMapScreen CreateBoard(Transform parent, SourceMapManager manager, Vector3 at)
    {
        var board = (GameObject)PrefabUtility.InstantiatePrefab(MenuPrefab(VoteMenuPath, BuildVoteMenu), parent);
        board.name = "VoteBoard";
        board.transform.localPosition = at;
        return Wire(board, manager);
    }

    /// <summary>The vote menu prefab's content (its manager is found at runtime, or set by Create Lobby).</summary>
    static GameObject BuildVoteMenu()
    {
        var board = new GameObject("VoteMenu");
        var screen = board.AddUdonSharpComponent<SourceMapScreen>();
        SourceMapManager manager = null; // set per instance (Wire), or found by name at runtime
        // 1600 x 900 units of 4 mm (6.4 x 3.6 m), its middle 1.9 m up. Unrotated, its front faces the spawn.
        var menu = Menu(board.transform, new Vector2(1600, 900), 0.004f, new Vector3(0, 1.9f, 0));
        screen.status = Label(menu, "Status", "Vote for the next map", new Vector2(0, 345), new Vector2(1500, 150), 44, TextColor, FontStyles.Bold);

        // The cards in a row that stays centred however many are shown.
        var cards = Group(menu, "Cards", new Vector2(0, 60), new Vector2(1500, 300));
        var row = cards.gameObject.AddComponent<HorizontalLayoutGroup>();
        row.spacing = 18;
        row.childAlignment = TextAnchor.MiddleCenter;
        row.childControlWidth = row.childControlHeight = false;
        row.childForceExpandWidth = row.childForceExpandHeight = false;
        var roots = new GameObject[6];
        var images = new RawImage[6];
        var texts = new TextMeshProUGUI[6];
        var selected = new Image[6];
        for (int i = 0; i < 6; i++)
        {
            var card = UiButton(cards, "Slot" + (i + 1), "", manager, "map", i, Vector2.zero, new Vector2(232, 300), CardColor, TextColor);
            var mask = Box(card, "Thumbnail", new Vector2(0, 74), new Vector2(216, 122), Color.white);
            mask.gameObject.AddComponent<Mask>().showMaskGraphic = false; // rounded corners
            var image = new GameObject("Image", typeof(RectTransform)).AddComponent<RawImage>();
            Place(image.rectTransform, mask.rectTransform, Vector2.zero, new Vector2(216, 122));
            image.raycastTarget = false;
            images[i] = image;
            texts[i] = Label(card, "SlotText", "map", new Vector2(0, -62), new Vector2(216, 130), 25, TextColor, FontStyles.Normal);
            selected[i] = Box(card, "YourVote", new Vector2(0, -144), new Vector2(120, 6), Accent); // your vote: a blue bar
            roots[i] = card;
        }
        screen.slotRoots = roots;
        screen.slotImages = images;
        screen.slotTexts = texts;
        screen.slotSelected = selected;

        var extend = UiButton(menu, "Extend", "", manager, "extend", -1, new Vector2(0, -185), new Vector2(620, 96), Green, TextColor);
        screen.extendRoot = extend;
        screen.extendText = Label(extend, "ExtendText", "Extend", Vector2.zero, new Vector2(600, 90), 28, TextColor, FontStyles.Bold);

        Box(menu, "Divider", new Vector2(0, -298), new Vector2(1500, 2), Divider);
        var admin = Group(menu, "OwnerControls", Vector2.zero, new Vector2(1600, 900));
        screen.adminRoot = admin.gameObject;
        screen.adminText = Label(admin, "OwnerText", "Owner controls", new Vector2(-690, -360), new Vector2(180, 70), 22, OwnerText, FontStyles.Bold);
        string[] actions = { "lock", "start", "force", "forcelobby" };
        string[] labels = { "Lock / unlock vote", "Start now", "Force a map", "Everyone to lobby" };
        for (int i = 0; i < actions.Length; i++)
            UiButton(admin, actions[i], labels[i], manager, actions[i], -1, new Vector2(-470 + i * 230, -360), new Vector2(210, 64), OwnerFill, OwnerText);
        UiButton(menu, "Rejoin", "Rejoin map", manager, "rejoin", -1, new Vector2(650, -360), new Vector2(220, 64), Accent, TextColor);

        UdonSharpEditorUtility.CopyProxyToUdon(screen);
        return board;
    }

    /// <summary>Menu at a map's spawn: status (time left, rtv), Rock the vote and Back to lobby, owner controls.</summary>
    static SourceMapScreen CreatePanel(SourceMapInfo map, SourceMapManager manager)
    {
        var old = map.transform.Find("SourceMapPanel");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var panel = (GameObject)PrefabUtility.InstantiatePrefab(MenuPrefab(MapMenuPath, BuildMapMenu), map.transform);
        panel.name = "SourceMapPanel";
        // 1.5 m to the right of the spawn, facing it.
        panel.transform.position = map.spawn.position + map.spawn.right * 1.5f + map.spawn.forward * 1.0f;
        Vector3 away = panel.transform.position - map.spawn.position;
        panel.transform.rotation = Quaternion.LookRotation(new Vector3(away.x, 0, away.z), Vector3.up); // front faces the spawn
        return Wire(panel, manager);
    }

    /// <summary>The map menu prefab's content.</summary>
    static GameObject BuildMapMenu()
    {
        var panel = new GameObject("MapMenu");
        var screen = panel.AddUdonSharpComponent<SourceMapScreen>();
        SourceMapManager manager = null; // set per instance (Wire), or found by name at runtime
        // 640 x 400 units of 2.5 mm (1.6 x 1 m), its middle 1.45 m up.
        var menu = Menu(panel.transform, new Vector2(640, 400), 0.0025f, new Vector3(0, 1.45f, 0));
        screen.status = Label(menu, "Status", "", new Vector2(0, 125), new Vector2(600, 110), 28, TextColor, FontStyles.Bold);
        UiButton(menu, "RTV", "Rock the vote", manager, "rtv", -1, new Vector2(-150, 10), new Vector2(280, 84), Accent, TextColor);
        UiButton(menu, "Lobby", "Back to lobby", manager, "lobby", -1, new Vector2(150, 10), new Vector2(280, 84), CardColor, TextColor);
        var admin = Group(menu, "OwnerControls", Vector2.zero, new Vector2(640, 400));
        screen.adminRoot = admin.gameObject;
        UiButton(admin, "ForceLobby", "Everyone to lobby", manager, "forcelobby", -1, new Vector2(-150, -115), new Vector2(280, 64), OwnerFill, OwnerText);
        UiButton(admin, "AddTime", "+ time", manager, "addtime", -1, new Vector2(150, -115), new Vector2(280, 64), OwnerFill, OwnerText);
        UdonSharpEditorUtility.CopyProxyToUdon(screen);
        return panel;
    }

    const string VoteMenuPath = "Assets/SourceMaps/Prefabs/VoteMenu.prefab", MapMenuPath = "Assets/SourceMaps/Prefabs/MapMenu.prefab";

    [MenuItem("Tools/Source Maps/Create Menu Prefabs")]
    public static void CreateMenuPrefabs()
    {
        MenuPrefab(VoteMenuPath, BuildVoteMenu, true);
        Selection.activeObject = MenuPrefab(MapMenuPath, BuildMapMenu, true);
    }

    /// <summary>A menu prefab asset, built and saved if it isn't there (or `force`: saved again over it, which keeps
    /// placed instances' overrides; deleting it first would give its objects new IDs and lose them).</summary>
    static GameObject MenuPrefab(string path, System.Func<GameObject> build, bool force = false)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab != null && !force) return prefab;
        SourceMapImporter.EnsureProgramAssets();
        EnsureTextMeshPro();
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
        var root = build();
        prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        return prefab;
    }

    /// <summary>Points a menu instance's screen and buttons at the manager.</summary>
    static SourceMapScreen Wire(GameObject menu, SourceMapManager manager)
    {
        foreach (var button in menu.GetComponentsInChildren<SourceMapButton>(true))
        {
            button.manager = manager;
            UdonSharpEditorUtility.CopyProxyToUdon(button);
        }
        var screen = menu.GetComponent<SourceMapScreen>();
        screen.manager = manager;
        UdonSharpEditorUtility.CopyProxyToUdon(screen);
        return screen;
    }

    // The sample world's maps (GameBanana pages in Tests/Bsp/get_maps.sh): author credits for the vote screen.
    static readonly Dictionary<string, string> Authors = new Dictionary<string, string>
    {
        ["bhop_japan"] = "Tony Montana", ["bhop_kitsune"] = "Ghost1447951", ["bhop_eazy_v2"] = "31K4L",
        ["bhop_arcane_v1"] = "Panzerhandschuh", ["bhop_badges"] = "Badges & fission", ["bhop_monster_jam"] = "Aoki",
    };
    const float MapSpacing = 700f; // metres between map origins (Source maps are up to ~620 m across)

    [MenuItem("Tools/Source Maps/Build Sample World...")]
    public static void BuildSampleWorldMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        string folder = EditorUtility.OpenFolderPanel("Folder with the maps' .bsp files", "", "");
        if (string.IsNullOrEmpty(folder)) return;
        string css = SourceMapVisuals.CssFolder;
        if (css == "" || SourceMapVisuals.ValidCss(css) != css)
        {
            css = SourceMapVisuals.ValidCss(EditorUtility.OpenFolderPanel("Your Counter-Strike Source folder (with cstrike and hl2)", css, ""));
            if (css != null) SourceMapVisuals.CssFolder = css;
        }
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

        var lobbySky = RenderSettings.skybox; // each map's import sets its own sky
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
        RenderSettings.skybox = lobbySky;
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

    // ------------------------------------------------------------------ helpers

    /// <summary>
    /// Puts every map in SourceMovement's Hull Only list (if SourceMovement is in the scene): while Source movement
    /// is on, the maps' colliders move to VRChat's Walkthrough layer and only the CS:S hull collides with them, so
    /// VRChat's taller capsule doesn't snag under low ceilings and in duck tunnels. Other entries are kept.
    /// </summary>
    static void SetHullOnly(SourceMapInfo[] maps)
    {
        var type = FindType("SourceMovement");
        var field = type?.GetField("hullOnly");
        if (field == null) return;
        foreach (UdonSharp.UdonSharpBehaviour movement in Object.FindObjectsOfType(type, true))
        {
            var list = ((GameObject[])field.GetValue(movement) ?? new GameObject[0])
                .Where(g => g != null && g.GetComponent<SourceMapInfo>() == null).ToList();
            list.AddRange(maps.Select(m => m.gameObject));
            field.SetValue(movement, list.ToArray());
            UdonSharpEditorUtility.CopyProxyToUdon(movement);
            // SourceMovement is usually a prefab instance: record the change as an override, or saving drops it.
            PrefabUtility.RecordPrefabInstancePropertyModifications(movement);
            var udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(movement);
            if (udon != null) PrefabUtility.RecordPrefabInstancePropertyModifications(udon);
            EditorUtility.SetDirty(movement);
        }
    }

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
    /// A world-space UI menu (pointer clicks in VRChat and ClientSim: VRCUiShape and a collider over it) under
    /// `parent`, `size` in canvas units of `metresPerUnit`, with a rounded dark panel behind everything.
    /// </summary>
    static RectTransform Menu(Transform parent, Vector2 size, float metresPerUnit, Vector3 at)
    {
        var go = new GameObject("Menu", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
        go.transform.SetParent(parent, false);
        go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        var rect = (RectTransform)go.transform;
        rect.localPosition = at;
        rect.sizeDelta = size;
        rect.localScale = Vector3.one * metresPerUnit;
        go.AddComponent<VRC.SDK3.Components.VRCUiShape>();
        var hit = go.AddComponent<BoxCollider>(); // what the pointer hits
        hit.isTrigger = true;
        hit.size = new Vector3(size.x, size.y, 0.02f / metresPerUnit);
        Box(rect, "Background", Vector2.zero, size, PanelColor).raycastTarget = false;
        return rect;
    }

    /// <summary>
    /// A rounded UI button calling SourceMapButton.Click (which tells SourceMapManager), with an optional label;
    /// returns its object (hiding it hides the label too).
    /// </summary>
    static GameObject UiButton(RectTransform parent, string name, string label, SourceMapManager manager, string action, int slot,
        Vector2 at, Vector2 size, Color fill, Color textColor)
    {
        var image = Box(parent, name, at, size, fill);
        var button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        image.color = Color.white; // the button's tint is the colour: hover lighter, press darker
        var colors = button.colors;
        colors.normalColor = colors.selectedColor = fill;
        colors.highlightedColor = Color.Lerp(fill, Color.white, 0.15f);
        colors.pressedColor = Color.Lerp(fill, Color.black, 0.25f);
        colors.disabledColor = Color.Lerp(fill, Color.black, 0.5f);
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.08f;
        button.colors = colors;
        var press = image.gameObject.AddUdonSharpComponent<SourceMapButton>();
        press.manager = manager;
        press.action = action;
        press.slot = slot;
        UdonSharpEditorUtility.CopyProxyToUdon(press);
        var udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(press);
        UnityEditor.Events.UnityEventTools.AddStringPersistentListener(button.onClick, udon.SendCustomEvent, "Click");
        if (label != "") Label(image.rectTransform, "Label", label, Vector2.zero, size - new Vector2(20, 8), size.y * 0.34f, textColor, FontStyles.Bold).enableWordWrapping = false; // one line, shrunk to fit
        return image.gameObject;
    }

    /// <summary>A rounded box (UI image) in `parent`.</summary>
    static Image Box(RectTransform parent, string name, Vector2 at, Vector2 size, Color color)
    {
        var image = new GameObject(name, typeof(RectTransform)).AddComponent<Image>();
        Place(image.rectTransform, parent, at, size);
        image.sprite = RoundedSprite();
        image.type = Image.Type.Sliced;
        image.color = color;
        return image;
    }

    static Image Box(GameObject parent, string name, Vector2 at, Vector2 size, Color color)
    {
        return Box((RectTransform)parent.transform, name, at, size, color);
    }

    /// <summary>A UI text, font size in canvas units.</summary>
    static TextMeshProUGUI Label(RectTransform parent, string name, string text, Vector2 at, Vector2 size, float fontSize, Color color, FontStyles style)
    {
        var label = new GameObject(name, typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
        Place(label.rectTransform, parent, at, size);
        label.text = text;
        label.fontSize = fontSize;
        label.fontStyle = style;
        label.color = color;
        label.alignment = TextAlignmentOptions.Center;
        label.enableWordWrapping = true;
        label.enableAutoSizing = true; // long names shrink to fit
        label.fontSizeMin = fontSize * 0.5f;
        label.fontSizeMax = fontSize;
        label.raycastTarget = false;
        return label;
    }

    static TextMeshProUGUI Label(GameObject parent, string name, string text, Vector2 at, Vector2 size, float fontSize, Color color, FontStyles style)
    {
        return Label((RectTransform)parent.transform, name, text, at, size, fontSize, color, style);
    }

    /// <summary>An empty UI rectangle (a group to show or hide).</summary>
    static RectTransform Group(RectTransform parent, string name, Vector2 at, Vector2 size)
    {
        var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
        Place(rect, parent, at, size);
        return rect;
    }

    static void Place(RectTransform rect, RectTransform parent, Vector2 at, Vector2 size)
    {
        rect.SetParent(parent, false);
        rect.anchoredPosition = at;
        rect.sizeDelta = size;
    }

    /// <summary>The menus' rounded corner sprite (white, 9-sliced; 16 unit corners), made once as an asset.</summary>
    static Sprite RoundedSprite()
    {
        const string path = "Assets/SourceMaps/Prefabs/rounded.png";
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite != null) return sprite;
        const int n = 64, r = 16;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                // Distance outside the rounded rectangle, anti-aliased over one pixel.
                float dx = Mathf.Max(r - (x + 0.5f), (x + 0.5f) - (n - r), 0f), dy = Mathf.Max(r - (y + 0.5f), (y + 0.5f) - (n - r), 0f);
                float a = Mathf.Clamp01(r + 0.5f - Mathf.Sqrt(dx * dx + dy * dy));
                tex.SetPixel(x, y, new Color(1, 1, 1, a));
            }
        System.IO.Directory.CreateDirectory("Assets/SourceMaps/Prefabs");
        System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteBorder = new Vector4(r, r, r, r);
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    static readonly Dictionary<Color, Material> materials = new Dictionary<Color, Material>();

    internal static Material Colored(Color color)
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
    internal static Texture2D Swatch(Color color)
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
