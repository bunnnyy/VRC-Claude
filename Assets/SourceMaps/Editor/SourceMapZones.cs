using System.Collections.Generic;
using System.IO;
using SourceMaps.Bsp;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using Num = System.Numerics;

/// <summary>
/// Timer zones for a map from the zone files CS:S bhop servers use with shavit's bhoptimer
/// (github.com/srcwr/zones-cstrike: zones-cstrike.srcwr.com/z/&lt;map&gt;.json, boxes in Source units). Makes SourceTimer
/// TimerZones under the map root: start/end per track (main, bonus 1, ...) and stages as checkpoints. Looked up by name,
/// so SourceMaps compiles without SourceTimer. Each track's Start zone is named "Start &lt;map&gt;[ bonus N]"; SourceTimer's
/// course boards tool gives it saved boards (the lobby tool runs it).
/// </summary>
public static class SourceMapZones
{
    public const string ZonesUrl = "https://srcwr.github.io/zones-cstrike/z/{0}.json";

    [System.Serializable] class Zone { public float[] point_a; public float[] point_b; public string type; public int track; public float[] dest; public int data; }
    [System.Serializable] class ZoneList { public Zone[] z; }

    [MenuItem("Tools/Source Maps/Import Timer Zones (srcwr) For Selected Map")]
    public static void ImportMenu()
    {
        foreach (var go in Selection.gameObjects)
        {
            var info = go.GetComponent<SourceMapInfo>();
            string map = info != null ? info.mapName : go.name;
            Import(go, map, Download(map), BspGeometry.DefaultScale);
        }
    }

    /// <summary>The zone file for a map, or null (no zones known for it / no connection).</summary>
    public static string Download(string map)
    {
        using (var request = UnityWebRequest.Get(string.Format(ZonesUrl, map)))
        {
            var op = request.SendWebRequest();
            while (!op.isDone) System.Threading.Thread.Sleep(20);
            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[Source Maps] no zones for {map} ({request.error}): place Start/End TimerZones by hand");
                return null;
            }
            return request.downloadHandler.text;
        }
    }

    /// <summary>Creates the zones under `mapRoot` (replacing earlier ones). Returns how many were made.</summary>
    public static int Import(GameObject mapRoot, string map, string json, float scale)
    {
        if (string.IsNullOrEmpty(json)) return 0;
        var zoneType = FindType("TimerZone");
        var timerType = FindType("RunTimer");
        if (zoneType == null || timerType == null) { Debug.LogWarning("[Source Maps] SourceTimer isn't in the project: no timer zones"); return 0; }
        var timer = Object.FindObjectOfType(timerType, true);
        if (timer == null)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/SourceTimer/SourceTimer.prefab");
            if (prefab != null) timer = ((GameObject)PrefabUtility.InstantiatePrefab(prefab)).GetComponentInChildren(timerType, true);
        }
        var zones = JsonUtility.FromJson<ZoneList>("{\"z\":" + json + "}").z;

        var old = mapRoot.transform.Find("TimerZones");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var parent = new GameObject("TimerZones").transform;
        parent.SetParent(mapRoot.transform, false);

        int made = 0;
        foreach (var z in zones)
        {
            if (z.point_a == null || z.point_b == null || z.point_a.Length < 3 || z.point_b.Length < 3) continue;
            string type = z.type == "start" ? "Start" : z.type == "end" ? "End" : z.type == "stage" ? "Checkpoint" : null;
            if (type == null) continue; // slide, speed limits... are server settings, not zones
            string track = z.track == 0 ? "" : z.track == 1 ? " bonus" : " bonus " + z.track;
            string name = type == "Checkpoint" ? $"Stage {z.data} {map}{track}" : $"{type} {map}{track}";

            Vector3 a = ToUnity(BspGeometry.ToUnity(new Num.Vector3(z.point_a[0], z.point_a[1], z.point_a[2]), scale));
            Vector3 b = ToUnity(BspGeometry.ToUnity(new Num.Vector3(z.point_b[0], z.point_b[1], z.point_b[2]), scale));
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = (a + b) * 0.5f;
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y), Mathf.Abs(a.z - b.z));

            var zone = (UdonSharpBehaviour)go.AddUdonSharpComponent(zoneType);
            var typeField = zoneType.GetField("zoneType");
            typeField.SetValue(zone, System.Enum.Parse(typeField.FieldType, type));
            zoneType.GetField("timer").SetValue(zone, timer);
            // Where Restart (start zone) or Reset (stage) sends you: the stage's teleport destination, else the floor
            // in the middle of the zone.
            var point = new GameObject(type == "Start" ? "StartPoint" : "Respawn").transform;
            point.SetParent(go.transform, false);
            if (z.dest != null && z.dest.Length >= 3)
                point.position = mapRoot.transform.TransformPoint(ToUnity(BspGeometry.ToUnity(new Num.Vector3(z.dest[0], z.dest[1], z.dest[2]), scale)));
            else point.localPosition = new Vector3(0, -box.size.y * 0.5f + 0.05f, 0);
            if (type != "End") zoneType.GetField("respawnPoint").SetValue(zone, point);
            UdonSharpEditorUtility.CopyProxyToUdon(zone);
            made++;
        }
        Debug.Log($"[Source Maps] {map}: {made} timer zones from zones-cstrike");
        return made;
    }

    static Vector3 ToUnity(Num.Vector3 v) { return new Vector3(v.X, v.Y, v.Z); }

    static System.Type FindType(string name)
    {
        foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies())
        {
            var t = a.GetType(name);
            if (t != null) return t;
        }
        return null;
    }
}
