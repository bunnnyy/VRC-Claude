using System.Collections.Generic;
using System.IO;
using System.Linq;
using SourceMaps.Bsp;
using UnityEditor;
using UnityEngine;
using Num = System.Numerics;

/// <summary>
/// Visible geometry and textures of a map through DeadZoneLuna's uSource (installed by the world creator, not part of
/// SourceMaps; called by name, so SourceMaps compiles without it). Runs uSource with the movement's scale and the
/// creator's CS:S folder, then cleans up what uSource leaves:
///   - surfaces with tool textures Source never draws (trigger, clip, nodraw, skip, hint...) are removed,
///   - solid static props get MeshColliders (uSource imports props without collision),
///   - meshes are saved as assets (uSource keeps them inside the scene).
/// </summary>
public static class SourceMapVisuals
{
    const string PrefsCssFolder = "SourceMaps.CssFolder";

    /// <summary>The CS:S folder (…/Counter-Strike Source, the one containing cstrike and hl2), remembered per machine.</summary>
    public static string CssFolder
    {
        get { return EditorPrefs.GetString(PrefsCssFolder, ""); }
        set { EditorPrefs.SetString(PrefsCssFolder, value); }
    }

    public static bool USourceInstalled { get { return FindType("uSource.uLoader") != null; } }

    // Tool textures that Source never renders (toolsblack and toolsskybox are drawn, so they stay).
    static readonly string[] Hidden = { "toolsnodraw", "toolstrigger", "toolsclip", "toolsplayerclip", "toolsnpcclip",
        "toolsskip", "toolshint", "toolsinvisible", "toolsareaportal", "toolsoccluder", "toolsfog", "toolsorigin",
        "toolsblocklight", "toolsblockbullets", "toolsblock_los", "toolsskybox2d", "toolsdotted" };

    /// <summary>
    /// Imports the visuals of `bspPath` under `parent` (normally the map root made by SourceMapImporter).
    /// Returns the visuals object, or null if uSource isn't installed.
    /// </summary>
    public static GameObject Import(string bspPath, Transform parent, float scale)
    {
        var loader = FindType("uSource.uLoader");
        var resources = FindType("uSource.uResourceManager");
        var vbsp = FindType("uSource.Formats.Source.VBSP.VBSPFile");
        var dirProvider = FindType("uSource.DirProvider");
        if (loader == null || resources == null || vbsp == null || dirProvider == null)
        {
            Debug.LogWarning("[Source Maps] uSource isn't installed (see the guide): imported collision and markers only.");
            return null;
        }
        string mapName = Path.GetFileNameWithoutExtension(bspPath);

        // uSource reads maps from <game>/maps: give it a folder with just this map, searched first.
        string temp = Path.GetFullPath("Temp/SourceMapsBsp");
        Directory.CreateDirectory(temp + "/maps");
        File.Copy(bspPath, temp + "/maps/" + mapName + ".bsp", true);

        string css = CssFolder;
        Set(loader, "RootPath", css != "" ? css : temp);
        Set(loader, "ModFolders", new[] { "cstrike", "hl2" });
        Set(loader, "DirPaks", new[] { new[] { "cstrike_pak_dir" }, new[] { "hl2_misc_dir", "hl2_textures_dir" } });
        Set(loader, "UnitScale", scale);
        Set(loader, "SaveAssetsToUnity", true);
        Set(loader, "OutputAssetsFolder", "SourceMapsImported/uSource");
        Set(loader, "ParseLights", false);
        Set(loader, "DebugTime", new System.Diagnostics.Stopwatch());
        Set(loader, "DebugTimeOutput", new System.Text.StringBuilder());
        loader.GetMethod("Clear").Invoke(null, null);
        var provider = System.Activator.CreateInstance(dirProvider, temp + "/");
        resources.GetMethod("Init", new[] { typeof(int), FindType("uSource.IResourceProvider") }).Invoke(null, new[] { 0, provider });
        resources.GetMethod("LoadMap").Invoke(null, new object[] { mapName });

        var visuals = (GameObject)vbsp.GetField("BSP_WorldSpawn").GetValue(null);
        if (visuals == null) return null;
        vbsp.GetField("BSP_WorldSpawn").SetValue(null, null); // or uSource destroys it when it loads the next map
        visuals.name = "Visuals";
        visuals.transform.SetParent(parent, false);

        int removed = RemoveHiddenSurfaces(visuals);
        int propColliders = AddPropColliders(visuals, BspFile.Load(bspPath), scale);
        int meshes = SaveMeshes(visuals, "Assets/SourceMapsImported/" + mapName + "/" + mapName + "_visuals.asset");
        Debug.Log($"[Source Maps] {mapName} visuals: {visuals.GetComponentsInChildren<MeshRenderer>(true).Length} renderers, " +
                  $"{removed} tool surfaces removed, {propColliders} solid props given colliders, {meshes} meshes saved" +
                  (css == "" ? " (no CS:S folder set: stock textures missing)" : ""));
        return visuals;
    }

    /// <summary>Removes renderers (or their tool sub-meshes) using textures Source doesn't draw.</summary>
    static int RemoveHiddenSurfaces(GameObject root)
    {
        int removed = 0;
        foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            var mats = r.sharedMaterials;
            var keep = new List<int>();
            for (int i = 0; i < mats.Length; i++)
                if (mats[i] == null || !IsHidden(mats[i].name)) keep.Add(i);
            if (keep.Count == mats.Length) continue;
            removed += mats.Length - keep.Count;
            var mf = r.GetComponent<MeshFilter>();
            if (keep.Count == 0 || mf == null || mf.sharedMesh == null)
            {
                if (mf != null) Object.DestroyImmediate(mf);
                Object.DestroyImmediate(r);
                continue;
            }
            // Keep only the visible sub-meshes.
            var src = mf.sharedMesh;
            var mesh = Object.Instantiate(src);
            mesh.name = src.name;
            mesh.subMeshCount = keep.Count;
            for (int k = 0; k < keep.Count; k++) mesh.SetTriangles(src.GetTriangles(keep[k]), k);
            mf.sharedMesh = mesh;
            r.sharedMaterials = keep.Select(i => mats[i]).ToArray();
        }
        return removed;
    }

    static bool IsHidden(string material)
    {
        string m = material.ToLowerInvariant().Replace('\\', '/');
        string file = m.Substring(m.LastIndexOf('/') + 1);
        // uSource's saved materials are named without their folder ("toolstrigger"); every hidden name starts with "tools".
        return Hidden.Contains(file.Replace(" (instance)", "").Trim());
    }

    /// <summary>
    /// MeshColliders on static props the map marks solid (2 = bounding box, 6 = vphysics; both use the render mesh
    /// here). uSource puts each prop directly under its "[StaticProps]" group, at the position SourceMaps would use, so
    /// props are matched by position.
    /// </summary>
    static int AddPropColliders(GameObject root, BspFile bsp, float scale)
    {
        var solid = new List<Vector3>();
        foreach (var p in bsp.StaticProps)
            if (p.Solid != 0) { var v = BspGeometry.ToUnity(p.Origin, scale); solid.Add(new Vector3(v.X, v.Y, v.Z)); }
        int added = 0;
        var group = root.transform.Find("[StaticProps]");
        if (group == null) return 0;
        foreach (Transform t in group)
        {
            Vector3 at = root.transform.InverseTransformPoint(t.position);
            if (!solid.Any(s => (s - at).sqrMagnitude < 0.0025f)) continue; // within 5 cm
            foreach (var mf in t.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null && mf.GetComponent<Collider>() == null)
                    mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
            // Models with bones come as SkinnedMeshRenderers: collide with the mesh in its current (static) pose.
            foreach (var smr in t.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.sharedMesh == null || smr.GetComponent<Collider>() != null) continue;
                var baked = new Mesh { name = smr.sharedMesh.name + " collision" };
                smr.BakeMesh(baked);
                smr.gameObject.AddComponent<MeshCollider>().sharedMesh = baked;
            }
            added++;
        }
        return added;
    }

    /// <summary>Saves every mesh that only lives in the scene (rendered, skinned, colliders) into one asset file.</summary>
    static int SaveMeshes(GameObject root, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        AssetDatabase.DeleteAsset(path);
        var all = root.GetComponentsInChildren<MeshFilter>(true).Select(m => m.sharedMesh)
            .Concat(root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(m => m.sharedMesh))
            .Concat(root.GetComponentsInChildren<MeshCollider>(true).Select(m => m.sharedMesh))
            .Where(m => m != null).Distinct();
        int n = 0;
        foreach (var mesh in all)
        {
            if (AssetDatabase.Contains(mesh)) continue;
            if (n == 0) AssetDatabase.CreateAsset(mesh, path);
            else AssetDatabase.AddObjectToAsset(mesh, path);
            n++;
        }
        AssetDatabase.SaveAssets();
        return n;
    }

    static void Set(System.Type type, string field, object value)
    {
        var f = type.GetField(field);
        if (f != null) f.SetValue(null, value);
    }

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
