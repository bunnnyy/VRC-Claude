using System.Linq;
using TMPro;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools > Source Timer > Add Course Boards: for every Start zone named "Start &lt;course&gt;" that has no boards yet, a
/// legit and an auto bhop board (saved with VRChat Persistence under &lt;course&gt;_legit / _auto), side by side on a wall.
/// The boards must be somewhere always active (a board inside a switched-off map can't sync), so they go on a wall
/// you place, e.g. in the lobby (the SourceMaps lobby tool calls Build()).
/// </summary>
public static class CourseBoards
{
    [MenuItem("Tools/Source Timer/Add Course Boards")]
    public static void Menu()
    {
        Selection.activeGameObject = Build(null, Vector3.zero, Quaternion.identity);
    }

    /// <summary>The wall of boards at `position` / `rotation` under `parent` (boards read from the wall's -z side).</summary>
    public static GameObject Build(Transform parent, Vector3 position, Quaternion rotation)
    {
        var starts = Object.FindObjectsOfType<TimerZone>(true)
            .Where(z => z.zoneType == TimerZoneType.Start && z.leaderboard == null && z.name.StartsWith("Start "))
            .OrderBy(z => z.name).ToList();
        if (starts.Count == 0) return null;
        var wall = new GameObject("CourseBoards");
        wall.transform.SetParent(parent, false);
        wall.transform.localPosition = position;
        wall.transform.localRotation = rotation;
        const float width = 2.2f;
        for (int i = 0; i < starts.Count; i++)
        {
            var zone = starts[i];
            string course = zone.name.Substring("Start ".Length);
            string key = course.ToLowerInvariant().Replace(' ', '_');
            float x = (i - (starts.Count - 1) / 2f) * width;
            zone.leaderboard = Board(wall.transform, course + " (legit)", key + "_legit", new Vector3(x, 2.6f, 0));
            zone.autoLeaderboard = Board(wall.transform, course + " (auto bhop)", key + "_auto", new Vector3(x, 0.4f, 0));
            UdonSharpEditorUtility.CopyProxyToUdon(zone);
        }
        return wall;
    }

    static Leaderboard Board(Transform wall, string title, string key, Vector3 at)
    {
        var go = new GameObject("Board " + title);
        go.transform.SetParent(wall, false);
        go.transform.localPosition = at;
        var board = go.AddUdonSharpComponent<Leaderboard>();
        board.title = title;
        board.saveKey = key;
        var canvas = new GameObject("Canvas", typeof(Canvas));
        canvas.transform.SetParent(go.transform, false);
        canvas.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        var rect = canvas.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(200, 200);
        rect.localScale = Vector3.one * 0.01f;
        var text = new GameObject("Text", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
        text.transform.SetParent(canvas.transform, false);
        text.rectTransform.anchorMin = Vector2.zero;
        text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.sizeDelta = Vector2.zero;
        text.fontSize = 11;
        text.alignment = TextAlignmentOptions.Top;
        text.text = title;
        board.board = text;
        UdonSharpEditorUtility.CopyProxyToUdon(board);
        return board;
    }
}
