using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Lighting comparison (step 5): imports one map with visuals, with or without its Source lightmaps, and renders the
/// spawn and a few teleport destinations into Assets/SourcePlayTests/.cache/shots/light_{on|off}_{map}_{n}.png.
///   Unity -batchmode -projectPath P -executeMethod LightingShots.Run -bsp map.bsp -smLightmaps on|off -quit
/// </summary>
public static class LightingShots
{
    public static void Run()
    {
        string[] args = System.Environment.GetCommandLineArgs();
        string bsp = args[System.Array.IndexOf(args, "-bsp") + 1];
        string mode = args[System.Array.IndexOf(args, "-smLightmaps") + 1];
        string map = Path.GetFileNameWithoutExtension(bsp);
        EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single); // like the sample world
        SourceMapVisuals.CssFolder = System.Environment.GetEnvironmentVariable("SM_CSS") ?? ""; // a CS:S folder (cstrike/materials...) for stock textures
        SourceMapVisuals.UseLightmaps = mode == "on";
        var root = SourceMapImporter.Import(bsp, 0.01905f);
        var visuals = SourceMapVisuals.Import(bsp, root.transform, 0.01905f);
        root.transform.Find("Collision")?.gameObject.SetActive(false); // only what players see

        var points = root.GetComponentsInChildren<SourceEntity>()
            .Where(e => e.className == "info_player_counterterrorist" || e.className == "info_teleport_destination").ToList();
        var picks = new[] { points.FirstOrDefault(e => e.className == "info_player_counterterrorist") ?? points.First() }.Concat(points.Where(e => e.className == "info_teleport_destination")
            .OrderBy(e => e.targetName).Where((e, i) => i % 9 == 0).Take(3)).ToList();
        // Plus two props with a model (lit from the ambient cube and lights), seen from 3 m away at eye height.
        var props = visuals != null ? visuals.transform.Find("[StaticProps]") : null;
        var propShots = new System.Collections.Generic.List<Transform>();
        if (props != null)
            foreach (Transform t in props)
                if (t.GetComponentInChildren<Renderer>() != null && propShots.Count < 2 && (propShots.Count == 0 || (propShots[0].position - t.position).magnitude > 20f))
                    propShots.Add(t);
        var cam = new GameObject("ShotCamera").AddComponent<Camera>();
        cam.fieldOfView = 80; cam.nearClipPlane = 0.05f; cam.farClipPlane = 2000;
        var rt = new RenderTexture(960, 540, 24);
        cam.targetTexture = rt;
        string dir = Path.GetFullPath("Assets/SourcePlayTests/.cache/shots");
        Directory.CreateDirectory(dir);
        for (int n = 0; n < picks.Count; n++)
        {
            var p = picks[n].transform;
            cam.transform.SetPositionAndRotation(p.position + Vector3.up * 1.4f, Quaternion.Euler(8, p.eulerAngles.y, 0));
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(960, 540, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 960, 540), 0, 0);
            float sum = tex.GetPixels32().Average(c => (c.r + c.g + c.b) / 3f);
            File.WriteAllBytes($"{dir}/light_{mode}_{map}_{n}.png", tex.EncodeToPNG());
            Debug.Log($"[SMTEST] light {mode} {map} {n} ({picks[n].targetName}): mean brightness {sum:F0}/255");
        }
        for (int n = 0; n < propShots.Count; n++)
        {
            var b = propShots[n].GetComponentInChildren<Renderer>().bounds;
            cam.transform.position = b.center + new Vector3(3f, 0.5f, 3f);
            cam.transform.LookAt(b.center);
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(960, 540, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 960, 540), 0, 0);
            File.WriteAllBytes($"{dir}/light_{mode}_{map}_prop{n}.png", tex.EncodeToPNG());
            Debug.Log($"[SMTEST] light {mode} {map} prop{n} {propShots[n].name}");
        }
        RenderTexture.active = null;
    }
}

/// <summary>Lists the prop renderers of a map in Assets/SourceMapsSample.unity: shader and vertex colours (debugging).</summary>
public static class PropDump
{
    public static void Run()
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/SourceMapsSample.unity");
        var map = GameObject.Find("bhop_japan");
        var props = map.transform.Find("Visuals/[StaticProps]");
        var groups = new System.Collections.Generic.Dictionary<string, int>();
        foreach (var r in props.GetComponentsInChildren<Renderer>(true))
        {
            var mesh = r is SkinnedMeshRenderer smr ? smr.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
            string key = $"{r.GetType().Name} shaders [{string.Join(",", System.Array.ConvertAll(r.sharedMaterials, m => m != null ? m.shader.name : "-"))}] colours {(mesh != null ? mesh.colors.Length > 0 : false)} readable {(mesh != null && mesh.isReadable)}";
            groups[key] = groups.TryGetValue(key, out var n) ? n + 1 : 1;
        }
        foreach (var g in groups) Debug.Log($"[SMTEST] {g.Value}x {g.Key}");
    }
}

/// <summary>
/// Screenshots of each map in Assets/SourceMapsSample.unity at the start zone, the middle (the middle stage checkpoint,
/// else the teleport destination nearest halfway) and the end zone, looking along the course:
/// Assets/SourcePlayTests/.cache/shots/course_{map}_{0,1,2}.png.
/// </summary>
public static class CourseShots
{
    public static void Run()
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/SourceMapsSample.unity");
        string dir = Path.GetFullPath("Assets/SourcePlayTests/.cache/shots");
        Directory.CreateDirectory(dir);
        var cam = new GameObject("CourseCamera").AddComponent<Camera>();
        cam.fieldOfView = 80; cam.nearClipPlane = 0.05f; cam.farClipPlane = 3000;
        var rt = new RenderTexture(1280, 720, 24);
        cam.targetTexture = rt;
        foreach (var info in Object.FindObjectsOfType<SourceMapInfo>(true))
        {
            var root = info.gameObject;
            root.SetActive(true);
            string map = info.mapName;
            var zones = root.transform.Find("TimerZones");
            var start = zones?.Find("Start " + map);
            var end = zones?.Find("End " + map);
            if (start == null || end == null) { Debug.Log($"[SMTEST] {map}: no start/end zone"); continue; }
            var stages = zones.Cast<Transform>().Where(t => t.name.StartsWith("Stage ") && t.name.EndsWith(" " + map))
                .OrderBy(t => int.TryParse(t.name.Split(' ')[1], out int n) ? n : 0).ToList();
            // Middle: the middle stage; else the middle teleport destination in the map's own order (tele_dest_N,
            // stage3a...), which follows the course.
            string middleName;
            Vector3 middle;
            if (stages.Count > 0) { middle = stages[stages.Count / 2].position; middleName = stages[stages.Count / 2].name; }
            else
            {
                var dests = root.GetComponentsInChildren<SourceEntity>(true)
                    .Where(e => e.className == "info_teleport_destination" && !e.targetName.EndsWith("_stop")) // arcane's jails
                    .OrderBy(e => NaturalKey(e.targetName)).ToList();
                middle = dests.Count > 0 ? dests[dests.Count / 2].transform.position : (start.position + end.position) / 2;
                middleName = dests.Count > 0 ? dests[dests.Count / 2].targetName : "halfway";
            }
            Vector3 a = Eye(start.position), m = Eye(middle), b = Eye(end.position);
            Shot(cam, rt, a, $"{dir}/course_{map}_0.png");
            Shot(cam, rt, m, $"{dir}/course_{map}_1.png");
            Shot(cam, rt, b, $"{dir}/course_{map}_2.png");
            Debug.Log($"[SMTEST] {map}: start {a:F0}, middle {m:F0} ({middleName}), end {b:F0}");
        }
    }

    /// <summary>Eye height (1.6 m) over the floor under a point.</summary>
    static Vector3 Eye(Vector3 p)
    {
        if (Physics.Raycast(p + Vector3.up * 1f, Vector3.down, out var hit, 20f, 1, QueryTriggerInteraction.Ignore)) p = hit.point;
        return p + Vector3.up * 1.6f;
    }

    static Vector3 Flat(Vector3 v) { return new Vector3(v.x, 0, v.z); }

    /// <summary>Numbers inside a name compare as numbers (tele_dest_9 before tele_dest_10).</summary>
    static string NaturalKey(string s)
    {
        return System.Text.RegularExpressions.Regex.Replace(s ?? "", @"\d+", m => m.Value.PadLeft(6, '0'));
    }

    /// <summary>From an eye point, the view with the longest open line of sight (of 16 directions, up to 80 m).</summary>
    static void Shot(Camera cam, RenderTexture rt, Vector3 from, string path)
    {
        Vector3 dir = Vector3.forward;
        float best = -1f;
        for (int i = 0; i < 16; i++)
        {
            Vector3 d = Quaternion.Euler(0, i * 22.5f, 0) * Vector3.forward;
            float dist = Physics.Raycast(from, d, out var hit, 80f, 1, QueryTriggerInteraction.Ignore) ? hit.distance : 80f;
            if (dist > best) { best = dist; dir = d; }
        }
        cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(dir) * Quaternion.Euler(5, 0, 0));
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
        File.WriteAllBytes(path, tex.EncodeToPNG());
        RenderTexture.active = null;
    }
}
