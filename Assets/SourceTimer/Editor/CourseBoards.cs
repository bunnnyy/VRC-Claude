using System.Linq;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools > Source Timer > Add Course Boards: for every Start zone named "Start &lt;course&gt;" that has no boards yet, a
/// legit and an auto bhop board (Scoreboard prefab, saved with VRChat Persistence under &lt;course&gt;_legit / _auto), side by side on a wall.
/// Its "End &lt;course&gt;" zones get the legit board too, so they only finish that course's runs.
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
        var all = Object.FindObjectsOfType<TimerZone>(true);
        var starts = all.Where(z => z.zoneType == TimerZoneType.Start && z.leaderboard == null && z.name.StartsWith("Start "))
            .OrderBy(z => z.name).ToList();
        if (starts.Count == 0) { HideUnusedTimerBoards(all); return null; }
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
            zone.leaderboard = Board(wall.transform, course + " (legit)", key + "_legit", new Vector3(x, 3.4f, 0));
            zone.autoLeaderboard = Board(wall.transform, course + " (auto bhop)", key + "_auto", new Vector3(x, 1.15f, 0));
            UdonSharpEditorUtility.CopyProxyToUdon(zone);
            foreach (var end in all.Where(z => z.zoneType == TimerZoneType.End && z.name == "End " + course))
            {
                end.leaderboard = zone.leaderboard;
                UdonSharpEditorUtility.CopyProxyToUdon(end);
            }
        }
        HideUnusedTimerBoards(all);
        return wall;
    }

    /// <summary>
    /// When every start zone has its own boards, the RunTimer's own two (in SourceTimer.prefab, which stands at the
    /// scene origin, often the spawn) are never used: switch them off instead of leaving them on the floor.
    /// </summary>
    static void HideUnusedTimerBoards(TimerZone[] zones)
    {
        if (zones.Any(z => z.zoneType == TimerZoneType.Start && (z.leaderboard == null || z.autoLeaderboard == null))) return;
        foreach (var timer in Object.FindObjectsOfType<RunTimer>(true))
            foreach (var board in new[] { timer.leaderboard, timer.autoLeaderboard })
                if (board != null && board.gameObject.activeSelf && !zones.Any(z => z.leaderboard == board || z.autoLeaderboard == board))
                    board.gameObject.SetActive(false);
    }

    static Leaderboard Board(Transform wall, string title, string key, Vector3 at)
    {
        return ScoreboardPrefab.Place(wall, title, key, at, Quaternion.identity);
    }
}
