using TMPro;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Assets/SourceTimer/Scoreboard.prefab: a Leaderboard on a dark rounded panel (title, then rank / name / time rows),
/// 2 x 2.1 m, its middle at the object. Drag it in anywhere and set Title and Save Key; point a Start zone's
/// Leaderboard / Auto Leaderboard (or the RunTimer's) at it. The course boards and practice courses place it too.
///   Tools > Source Timer > Create Scoreboard Prefab (made again from this code)
/// </summary>
public static class ScoreboardPrefab
{
    public const string Path = "Assets/SourceTimer/Scoreboard.prefab";
    const string SpritePath = "Assets/SourceTimer/Scoreboard_rounded.png";
    static readonly Color Panel = new Color32(15, 18, 24, 255), Text = new Color32(242, 244, 248, 255), Line = new Color32(255, 255, 255, 20);

    [MenuItem("Tools/Source Timer/Create Scoreboard Prefab")]
    public static void Menu()
    {
        Selection.activeObject = Get(true);
    }

    /// <summary>The prefab asset, made if it isn't there (or `force`: made again over it, which keeps placed
    /// instances' overrides; deleting it first would give its objects new IDs and lose them).</summary>
    public static GameObject Get(bool force = false)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Path);
        if (prefab != null && !force) return prefab;
        var root = Build();
        prefab = PrefabUtility.SaveAsPrefabAsset(root, Path);
        Object.DestroyImmediate(root);
        return prefab;
    }

    /// <summary>A scoreboard instance under `parent` (its middle at `localPosition`, read from its -z side).</summary>
    public static Leaderboard Place(Transform parent, string title, string saveKey, Vector3 localPosition, Quaternion localRotation)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(Get(), parent);
        go.name = "Board " + title;
        go.transform.localPosition = localPosition;
        go.transform.localRotation = localRotation;
        var board = go.GetComponent<Leaderboard>();
        board.title = title;
        board.saveKey = saveKey;
        board.titleLabel.text = title;
        UdonSharpEditorUtility.CopyProxyToUdon(board);
        return board;
    }

    static GameObject Build()
    {
        var root = new GameObject("Scoreboard");
        var board = root.AddUdonSharpComponent<Leaderboard>();
        // 400 x 420 units of 5 mm.
        var canvas = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas));
        canvas.transform.SetParent(root.transform, false);
        canvas.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        var rect = (RectTransform)canvas.transform;
        rect.sizeDelta = new Vector2(400, 420);
        rect.localScale = Vector3.one * 0.005f;
        Box(rect, "Background", Vector2.zero, new Vector2(400, 420), Panel);
        board.titleLabel = Label(rect, "Title", "Best times", new Vector2(0, 175), new Vector2(360, 50), 28, FontStyles.Bold, TextAlignmentOptions.Center);
        board.titleLabel.enableAutoSizing = true;
        board.titleLabel.fontSizeMin = 14;
        board.titleLabel.fontSizeMax = 28;
        Box(rect, "Divider", new Vector2(0, 143), new Vector2(360, 2), Line);
        board.board = Label(rect, "Rows", "", new Vector2(0, -40), new Vector2(350, 350), 22, FontStyles.Normal, TextAlignmentOptions.TopLeft);
        board.board.lineSpacing = 12;
        UdonSharpEditorUtility.CopyProxyToUdon(board);
        return root;
    }

    static Image Box(RectTransform parent, string name, Vector2 at, Vector2 size, Color color)
    {
        var image = new GameObject(name, typeof(RectTransform)).AddComponent<Image>();
        image.rectTransform.SetParent(parent, false);
        image.rectTransform.anchoredPosition = at;
        image.rectTransform.sizeDelta = size;
        image.sprite = Rounded();
        image.type = Image.Type.Sliced;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    static TextMeshProUGUI Label(RectTransform parent, string name, string text, Vector2 at, Vector2 size, float fontSize, FontStyles style, TextAlignmentOptions align)
    {
        var label = new GameObject(name, typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
        label.rectTransform.SetParent(parent, false);
        label.rectTransform.anchoredPosition = at;
        label.rectTransform.sizeDelta = size;
        label.text = text;
        label.fontSize = fontSize;
        label.fontStyle = style;
        label.color = Text;
        label.alignment = align;
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.raycastTarget = false;
        return label;
    }

    /// <summary>White rounded-corner sprite (16 unit corners, 9-sliced), made once.</summary>
    static Sprite Rounded()
    {
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath);
        if (sprite != null) return sprite;
        const int n = 64, r = 16;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = Mathf.Max(r - (x + 0.5f), (x + 0.5f) - (n - r), 0f), dy = Mathf.Max(r - (y + 0.5f), (y + 0.5f) - (n - r), 0f);
                tex.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(r + 0.5f - Mathf.Sqrt(dx * dx + dy * dy))));
            }
        System.IO.File.WriteAllBytes(SpritePath, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(SpritePath);
        var importer = (TextureImporter)AssetImporter.GetAtPath(SpritePath);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteBorder = new Vector4(r, r, r, r);
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath);
    }
}
