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
        SourceMapVisuals.CssFolder = "";
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
