using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Measures a converter's import of a map against SourceMaps' own collision (same scene):
/// renderer/triangle/material stats, alignment of the visible geometry with our collision (vertical rays around
/// every spawn and teleport destination) and screenshots. Results go to the log with a [CONV] prefix.
/// </summary>
public static class ConverterMeasure
{
    const int VisualLayer = 30;

    public static void Measure(string label, string bspPath, GameObject converted, double importSeconds)
    {
        var renderers = converted.GetComponentsInChildren<MeshRenderer>(true);
        long tris = 0;
        var materials = new HashSet<Material>();
        foreach (var r in renderers)
        {
            var mf = r.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null) tris += mf.sharedMesh.triangles.LongLength / 3;
            foreach (var m in r.sharedMaterials) if (m != null) materials.Add(m);
        }
        int noTexture = materials.Count(m => !m.HasProperty("_MainTex") || m.mainTexture == null);
        int badShader = materials.Count(m => m.shader == null || m.shader.name == "Hidden/InternalErrorShader");
        Log($"{label}: import {importSeconds:F1} s, {renderers.Length} renderers, {tris} triangles, {materials.Count} materials " +
            $"({noTexture} without a texture, {badShader} with a broken shader)");

        // Visual geometry as colliders on their own layer, for the rays only.
        foreach (var r in renderers)
        {
            var mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;
            r.gameObject.layer = VisualLayer;
            var c = r.gameObject.AddComponent<MeshCollider>();
            c.sharedMesh = mf.sharedMesh;
        }
        // SourceMaps collision and markers next to it.
        var ours = SourceMapImporter.Import(bspPath, 0.01905f);
        Bounds vb = new Bounds(), ob = ours.GetComponentInChildren<MeshCollider>().bounds;
        bool first = true;
        foreach (var r in renderers) { if (first) vb = r.bounds; else vb.Encapsulate(r.bounds); first = false; }
        Log($"{label}: visual bounds {vb.min:F1}..{vb.max:F1}, our collision {ob.min:F1}..{ob.max:F1}");
        Physics.queriesHitBackfaces = true; // visual meshes may be single sided either way
        Physics.SyncTransforms();

        int match = 0, differ = 0, onlyOurs = 0, onlyVisual = 0, rays = 0;
        var diffs = new List<float>();
        var points = ours.GetComponentsInChildren<SourceEntity>()
            .Where(e => e.className == "info_teleport_destination" || e.className.StartsWith("info_player_")).ToList();
        foreach (var p in points)
            for (int i = 0; i < 25; i++)
            {
                Vector3 origin = p.transform.position + new Vector3((i % 5 - 2) * 2.5f, 2f, (i / 5 - 2) * 2.5f);
                bool a = Physics.Raycast(origin, Vector3.down, out var ha, 20f, 1 << 0, QueryTriggerInteraction.Ignore);
                bool b = Physics.Raycast(origin, Vector3.down, out var hb, 20f, 1 << VisualLayer, QueryTriggerInteraction.Ignore);
                rays++;
                if (a && b)
                {
                    float d = Mathf.Abs(ha.point.y - hb.point.y);
                    diffs.Add(d);
                    if (d < 0.05f) match++; else differ++;
                }
                else if (a) onlyOurs++;
                else if (b) onlyVisual++;
            }
        diffs.Sort();
        float median = diffs.Count > 0 ? diffs[diffs.Count / 2] : -1;
        Log($"{label}: alignment over {rays} rays: {match} match within 5 cm ({100f * match / rays:F1}%), {differ} differ, " +
            $"{onlyOurs} hit only our collision (clip brushes, missing visuals), {onlyVisual} hit only visuals (non-solid details), median diff {median * 100:F1} cm");

        // Screenshots from three teleport destinations.
        var light = new GameObject("Sun").AddComponent<Light>();
        light.type = LightType.Directional;
        light.transform.rotation = Quaternion.Euler(50, 30, 0);
        RenderSettings.ambientLight = new Color(0.5f, 0.5f, 0.5f);
        var cam = new GameObject("ShotCamera").AddComponent<Camera>();
        cam.fieldOfView = 75;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 1000f;
        cam.cullingMask = ~(1 << 0); // visuals only, not our collision
        var rt = new RenderTexture(1280, 720, 24);
        cam.targetTexture = rt;
        string dir = Path.GetFullPath("../shots");
        Directory.CreateDirectory(dir);
        var dests = points.Where(e => e.className == "info_teleport_destination").OrderBy(e => e.targetName).ToList();
        for (int s = 0; s < 3 && dests.Count > 0; s++)
        {
            var d = dests[(s * dests.Count) / 3];
            cam.transform.SetPositionAndRotation(d.transform.position + Vector3.up * 1.2f, Quaternion.Euler(10, d.transform.eulerAngles.y, 0));
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            File.WriteAllBytes($"{dir}/{label}_{s}_{d.targetName}.png", tex.EncodeToPNG());
        }
        Log($"{label}: screenshots in {dir}");
        EditorSceneManager.SaveScene(converted.scene, "Assets/ConverterTest.unity");
    }

    public static void Log(string s) { Debug.Log("[CONV] " + s); }

    public static string Arg(string name)
    {
        string[] args = System.Environment.GetCommandLineArgs();
        int i = System.Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
