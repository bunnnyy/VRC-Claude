using System.Linq;
using TMPro;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools > Source Timer > Add Practice Courses: a short bhop lane and a small surf ramp, each with start/end/reset
/// zones and its own legit and auto boards (saved with VRChat Persistence). The SourceMaps lobby tool calls Build()
/// to put them next to the lobby. Sizes in Source units (1 unit = 0.01905 m), like the movement.
/// </summary>
public static class PracticeCourses
{
    const float U = 0.01905f;

    [MenuItem("Tools/Source Timer/Add Practice Courses")]
    public static void Menu()
    {
        Selection.activeGameObject = Build(null, Vector3.zero);
    }

    /// <summary>
    /// Both courses under a new "PracticeCourses" object at `position` (metres, under `parent`): the bhop lane starts
    /// 9.5 m to the +x side and the surf ramp 9.5 m to the -x side, both running along +z, boards facing the middle.
    /// Made to sit next to the SourceMaps lobby floor (14 x 14 m).
    /// </summary>
    public static GameObject Build(Transform parent, Vector3 position)
    {
        var timer = Object.FindObjectOfType<RunTimer>(true);
        if (timer == null)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/SourceTimer/SourceTimer.prefab");
            if (prefab == null) { Debug.LogWarning("[Source Timer] SourceTimer.prefab not found"); return null; }
            timer = ((GameObject)PrefabUtility.InstantiatePrefab(prefab)).GetComponentInChildren<RunTimer>(true);
        }
        // Auto and legit bhop are separate categories when a movement script with autoBhop is in the scene.
        if (timer.categorySource == null)
        {
            timer.categorySource = Object.FindObjectsOfType<UdonSharpBehaviour>(true).FirstOrDefault(b => b.GetType().Name == "SourceMovement");
            UdonSharpEditorUtility.CopyProxyToUdon(timer);
        }

        var root = new GameObject("PracticeCourses");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = position;
        BhopCourse(root.transform, timer);
        SurfCourse(root.transform, timer);
        return root;
    }

    /// <summary>Start pad, 8 blocks 128 units wide with 128 unit gaps, end pad; falling resets to the start.</summary>
    static void BhopCourse(Transform parent, RunTimer timer)
    {
        var course = Course(parent, "PracticeBhop", new Vector3(9.5f, 0, 0));
        Solid(course, "StartPad", new Vector3(0, -8, 0), new Vector3(256, 16, 256));
        for (int i = 0; i < 8; i++)
            Solid(course, "Block" + (i + 1), new Vector3(0, -8, 384 + i * 256), new Vector3(128, 16, 128));
        float end = 384 + 8 * 256 + 128;
        Solid(course, "EndPad", new Vector3(0, -8, end), new Vector3(256, 16, 256));
        Zones(course, timer, "practice_bhop", "Bhop practice", new Vector3(0, 0, 0), new Vector3(0, 0, end),
            new Vector3(0, -300, end / 2), new Vector3(1024, 64, end + 1024), -1);
    }

    /// <summary>A 60 degree surf ramp (two faces meeting at a ridge) 3000 units long, start and end pads.</summary>
    static void SurfCourse(Transform parent, RunTimer timer)
    {
        var course = Course(parent, "PracticeSurf", new Vector3(-9.5f, 0, 0));
        Solid(course, "StartPad", new Vector3(0, -8, 0), new Vector3(256, 16, 256));
        Vector3 ridge = new Vector3(0, -200, 0);
        const float halfWidth = 400, thickness = 64, length = 3000, rampZ = 256 + 1500;
        SurfFace(course, "RampLeft", ridge, 60, halfWidth, thickness, length, rampZ);
        SurfFace(course, "RampRight", ridge, -60, -halfWidth, thickness, length, rampZ);
        float end = rampZ + length / 2 + 400;
        Solid(course, "EndPad", new Vector3(0, -900, end), new Vector3(1024, 16, 512));
        Zones(course, timer, "practice_surf", "Surf practice", new Vector3(0, 0, 0), new Vector3(0, -892, end),
            new Vector3(0, -1400, end / 2), new Vector3(3000, 64, end + 1024), 1);
    }

    static Transform Course(Transform parent, string name, Vector3 positionMetres)
    {
        var course = new GameObject(name).transform;
        course.SetParent(parent, false);
        course.localPosition = positionMetres;
        return course;
    }

    /// <summary>
    /// Start (with the course's boards), end and reset zones, and the two boards beside the start on the lobby side
    /// (boardSide -1 = toward -x, +1 = toward +x).
    /// </summary>
    static void Zones(Transform course, RunTimer timer, string key, string title, Vector3 start, Vector3 end, Vector3 resetCenter, Vector3 resetSize, int boardSide)
    {
        var startPoint = new GameObject("StartPoint").transform;
        startPoint.SetParent(course, false);
        startPoint.localPosition = start * U;

        var legit = Board(course, title + " (legit)", key + "_legit", new Vector3(4.5f * boardSide, 0, 1.5f), boardSide);
        var auto = Board(course, title + " (auto bhop)", key + "_auto", new Vector3(4.5f * boardSide, 0, 4.0f), boardSide);
        var startZone = Zone(course, "StartZone", TimerZoneType.Start, timer, start + new Vector3(0, 64, 0), new Vector3(256, 128, 256), new Color(0.2f, 1f, 0.3f, 0.25f));
        startZone.leaderboard = legit;
        startZone.autoLeaderboard = auto;
        startZone.respawnPoint = startPoint;
        UdonSharpEditorUtility.CopyProxyToUdon(startZone);
        Zone(course, "EndZone", TimerZoneType.End, timer, end + new Vector3(0, 64, 0), new Vector3(256, 128, 256), new Color(1f, 0.25f, 0.2f, 0.25f));
        var reset = Zone(course, "ResetZone", TimerZoneType.Reset, timer, resetCenter, resetSize, Color.clear);
        Object.DestroyImmediate(reset.GetComponent<MeshRenderer>());
    }

    static Leaderboard Board(Transform course, string title, string key, Vector3 positionMetres, int side)
    {
        var go = new GameObject("Board " + title);
        go.transform.SetParent(course, false);
        go.transform.localPosition = positionMetres;
        go.transform.localRotation = Quaternion.Euler(0, 90 * side, 0); // text faces back toward the course
        var board = go.AddUdonSharpComponent<Leaderboard>();
        board.title = title;
        board.saveKey = key;
        var canvas = new GameObject("Canvas", typeof(Canvas));
        canvas.transform.SetParent(go.transform, false);
        canvas.transform.localPosition = new Vector3(0, 1.5f, 0);
        canvas.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        var rect = canvas.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(200, 220);
        rect.localScale = Vector3.one * 0.01f;
        var text = new GameObject("Text", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
        text.transform.SetParent(canvas.transform, false);
        text.rectTransform.anchorMin = Vector2.zero;
        text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.sizeDelta = Vector2.zero;
        text.fontSize = 12;
        text.alignment = TextAlignmentOptions.Top;
        text.text = title;
        board.board = text;
        UdonSharpEditorUtility.CopyProxyToUdon(board);
        return board;
    }

    static TimerZone Zone(Transform course, string name, TimerZoneType type, RunTimer timer, Vector3 center, Vector3 size, Color color)
    {
        var box = Solid(course, name, center, size);
        box.isStatic = false;
        box.GetComponent<BoxCollider>().isTrigger = true;
        box.GetComponent<MeshRenderer>().sharedMaterial = SeeThrough(color);
        var zone = box.AddUdonSharpComponent<TimerZone>();
        zone.zoneType = type;
        zone.timer = timer;
        UdonSharpEditorUtility.CopyProxyToUdon(zone);
        return zone;
    }

    static GameObject Solid(Transform parent, string name, Vector3 center, Vector3 size)
    {
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = name;
        cube.transform.SetParent(parent, false);
        cube.transform.localPosition = center * U;
        cube.transform.localScale = size * U;
        cube.isStatic = true;
        return cube;
    }

    static void SurfFace(Transform parent, string name, Vector3 ridge, float angle, float halfWidth, float thickness, float length, float z)
    {
        Quaternion rotation = Quaternion.Euler(0, 0, angle);
        // The ridge is the box's top inner edge: local (halfWidth, thickness / 2).
        Vector3 center = ridge - rotation * new Vector3(halfWidth, thickness / 2, 0) + new Vector3(0, 0, z);
        Solid(parent, name, center, new Vector3(Mathf.Abs(halfWidth) * 2, thickness, length)).transform.localRotation = rotation;
    }

    static Material SeeThrough(Color color)
    {
        string path = "Assets/SourceTimer/Materials/Zone_" + ColorUtility.ToHtmlStringRGBA(color) + ".mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;
        var material = new Material(Shader.Find("Standard")) { color = color };
        material.SetFloat("_Mode", 3); // transparent
        material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
        material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.SetInt("_ZWrite", 0);
        material.EnableKeyword("_ALPHAPREMULTIPLY_ON");
        material.renderQueue = 3000;
        System.IO.Directory.CreateDirectory("Assets/SourceTimer/Materials");
        AssetDatabase.CreateAsset(material, path);
        return material;
    }
}
