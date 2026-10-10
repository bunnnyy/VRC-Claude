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
///   - uSource's own colliders are removed (the importer's collision is the map's); solid static props get
///     MeshColliders,
///   - meshes are saved as assets (uSource keeps them inside the scene),
///   - the map's own Source lightmaps (uSource reads them into Unity's lightmap list, which isn't saved with the scene)
///     are saved as textures and put on the surfaces' materials (SourceMaps/Lightmapped shader),
///   - static props are lit like Source lights models: the map's ambient cube plus its strongest visible lights, baked
///     into vertex colours (SourceMaps/Prop shader).
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

    /// <summary>
    /// Light the map like Source: surfaces with its own lightmaps, static props with its ambient light and lights
    /// (off: uSource's materials, lit by Unity lights).
    /// </summary>
    public static bool UseLightmaps = true;

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
        Set(loader, "ParseLightmaps", UseLightmaps);
        Set(loader, "UseLightmapsAsTextureShader", false); // that mode puts one lightmap on a shared material
        Set(loader, "UseGammaLighting", true);
        Set(loader, "DebugTime", new System.Diagnostics.Stopwatch());
        Set(loader, "DebugTimeOutput", new System.Text.StringBuilder());
        loader.GetMethod("Clear").Invoke(null, null);
        var provider = System.Activator.CreateInstance(dirProvider, temp + "/");
        resources.GetMethod("Init", new[] { typeof(int), FindType("uSource.IResourceProvider") }).Invoke(null, new[] { 0, provider });
        var worldSky = RenderSettings.skybox; // uSource sets its own (white when the sky's textures are missing)
        resources.GetMethod("LoadMap").Invoke(null, new object[] { mapName });

        var visuals = (GameObject)vbsp.GetField("BSP_WorldSpawn").GetValue(null);
        if (visuals == null) return null;
        vbsp.GetField("BSP_WorldSpawn").SetValue(null, null); // or uSource destroys it when it loads the next map
        visuals.name = "Visuals";
        visuals.transform.SetParent(parent, false);

        // uSource gives its brush and displacement meshes MeshColliders (and maybe props): drop them all. The map's
        // collision is SourceMapImporter's (solid brushes and clips only, like Source); only solid props get colliders
        // back below. Otherwise there'd be a second copy of the walls about a unit off, and non-solid brushes
        // (func_illusionary...) would block.
        int strayColliders = 0;
        foreach (var c in visuals.GetComponentsInChildren<Collider>(true)) { Object.DestroyImmediate(c); strayColliders++; }
        int removed = RemoveHiddenSurfaces(visuals);
        int doors = LinkDoors(visuals);
        int glass = LinkBreakables(visuals);
        int lit = !UseLightmaps ? 0 : ApplyLightmaps(visuals, "Assets/SourceMapsImported/" + mapName + "/Lightmaps");
        var bsp = BspFile.Load(bspPath);
        int propColliders = AddPropColliders(visuals, bsp, scale);
        int propsLit = !UseLightmaps ? 0 : LightProps(visuals, bsp, scale, "Assets/SourceMapsImported/" + mapName + "/Props");
        int meshes = SaveMeshes(visuals, "Assets/SourceMapsImported/" + mapName + "/" + mapName + "_visuals.asset");
        // Sounds and the sky: uSource drops its file providers after loading a map, so open them again (the CS:S
        // folder with its VPKs, and the map's pakfile first) while they're read.
        var missing = new List<string>();
        int sounds;
        string skyName = bsp.Entities.Count > 0 ? bsp.Entities[0].Get("skyname") : "";
        Material sky;
        byte[] file = File.ReadAllBytes(bspPath);
        int pakOffset = System.BitConverter.ToInt32(file, 8 + 40 * 16), pakLength = System.BitConverter.ToInt32(file, 12 + 40 * 16);
        var init = resources.GetMethod("Init", new[] { typeof(int), FindType("uSource.IResourceProvider") });
        try
        {
            init.Invoke(null, new object[] { 0, null });
            if (pakLength > 0)
                init.Invoke(null, new[] { 0, System.Activator.CreateInstance(FindType("uSource.PAKProvider"), new MemoryStream(file, pakOffset, pakLength)) });
            sounds = SourceMapSounds.Add(parent.gameObject, "Assets/SourceMapsImported/" + mapName + "/Sounds", scale,
                path => ReadGameFile(resources, path), missing);
            sky = SaveSky(resources, skyName, "Assets/SourceMapsImported/" + mapName, mapName);
        }
        finally
        {
            resources.GetMethod("CloseStreams").Invoke(null, null);
            resources.GetMethod("RemoveResourceProviders").Invoke(null, null);
        }
        RenderSettings.skybox = sky != null ? sky : worldSky;
        Debug.Log($"[Source Maps] {mapName} visuals: {visuals.GetComponentsInChildren<MeshRenderer>(true).Length} renderers, " +
                  $"{removed} tool surfaces removed, {strayColliders} uSource colliders removed, {lit} surfaces with Source lightmaps, {propsLit} props lit, {doors} door blocks linked, {glass} breakable glass linked, {propColliders} solid props given colliders, {meshes} meshes saved, " +
                  $"{sounds} sounds" + (missing.Count > 0 ? $" ({missing.Count} sound files missing: {string.Join(", ", missing)})" : "") +
                  $", sky {skyName}" + (sky == null ? " missing (HL2 skies need the CS:S folder's hl2)" : "") +
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
    /// Gives each SourceMapDoor (bhop block) its visible model, so it moves with the collider: uSource makes brush model
    /// N as the N-th child of "[Faces]".
    /// </summary>
    static int LinkDoors(GameObject visuals)
    {
        var faces = visuals.transform.Find("[Faces]");
        var root = visuals.transform.parent;
        if (faces == null || root == null) return 0;
        int linked = 0;
        foreach (var door in root.GetComponentsInChildren<SourceMapDoor>(true))
        {
            var model = ModelVisuals(faces, door);
            if (model == null) continue;
            door.visuals = model;
            UdonSharpEditor.UdonSharpEditorUtility.CopyProxyToUdon(door);
            linked++;
        }
        return linked;
    }

    /// <summary>Breakable glass (SourceMapBreakable) gets its model's visuals, which it switches off when it breaks.</summary>
    static int LinkBreakables(GameObject visuals)
    {
        var faces = visuals.transform.Find("[Faces]");
        var root = visuals.transform.parent;
        if (faces == null || root == null) return 0;
        int linked = 0;
        foreach (var glass in root.GetComponentsInChildren<SourceMapBreakable>(true))
        {
            var model = ModelVisuals(faces, glass);
            if (model == null) continue;
            glass.visuals = model;
            UdonSharpEditor.UdonSharpEditorUtility.CopyProxyToUdon(glass);
            linked++;
        }
        return linked;
    }

    /// <summary>uSource's object for the brush model ("*N" -> [Faces] child N) of the entity `part` belongs to.</summary>
    static Transform ModelVisuals(Transform faces, Component part)
    {
        var marker = part.GetComponentInParent<SourceEntity>();
        int model;
        if (marker == null || !int.TryParse(marker.GetValue("model").TrimStart('*'), out model) || model >= faces.childCount) return null;
        return faces.GetChild(model);
    }

    // uSource's shaders for surfaces Source draws with a lightmap (LightmappedGeneric, WorldVertexTransition, alpha tested).
    static readonly string[] LightmappedShaders = { "Legacy Shaders/Diffuse", "USource/Lightmapped/Generic",
        "USource/Lightmapped/WorldVertexTransition", "USource/CutoutGeneric" };

    /// <summary>
    /// Moves the lightmaps uSource read (LightmapSettings.lightmaps, one per surface group, index on the renderer) onto
    /// the surfaces: each lightmap is saved as a texture, each lit surface gets a SourceMaps/Lightmapped material with
    /// its texture and lightmap. Translucent, additive and unlit surfaces keep uSource's material. Returns surfaces lit.
    /// </summary>
    static int ApplyLightmaps(GameObject root, string folder)
    {
        var lightmaps = LightmapSettings.lightmaps;
        var shader = Shader.Find("SourceMaps/Lightmapped");
        if (lightmaps.Length == 0 || shader == null) return 0;
        AssetDatabase.DeleteAsset(folder); // a re-import starts clean
        Directory.CreateDirectory(folder);
        var saved = new Dictionary<int, Texture2D>();
        int lit = 0;
        foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            int index = r.lightmapIndex;
            if (index < 0 || index >= lightmaps.Length) continue;
            r.lightmapIndex = -1; // the lightmap moves into the material
            var src = r.sharedMaterial;
            if (src == null || src.name.ToLowerInvariant().StartsWith("tools") || !LightmappedShaders.Contains(src.shader.name)) continue;
            if (!saved.TryGetValue(index, out var tex))
            {
                string path = $"{folder}/lightmap_{index}.png";
                File.WriteAllBytes(path, lightmaps[index].lightmapColor.EncodeToPNG());
                AssetDatabase.ImportAsset(path);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
                tex = saved[index] = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
            var mat = new Material(shader) { name = src.name };
            mat.SetTexture("_MainTex", src.mainTexture);
            mat.mainTextureScale = src.mainTextureScale;
            mat.mainTextureOffset = src.mainTextureOffset;
            if (src.HasProperty("_Color")) mat.SetColor("_Color", src.GetColor("_Color"));
            if (src.HasProperty("_SecondTex")) { mat.SetTexture("_SecondTex", src.GetTexture("_SecondTex")); mat.SetFloat("_Blend", 1); }
            if (src.shader.name == "USource/CutoutGeneric") mat.SetFloat("_Cutoff", 0.5f);
            mat.SetTexture("_LightMap", tex);
            AssetDatabase.CreateAsset(mat, $"{folder}/{lit}_{Path.GetFileName(src.name)}.mat");
            r.sharedMaterial = mat;
            lit++;
        }
        LightmapSettings.lightmaps = new LightmapData[0];
        return lit;
    }

    /// <summary>
    /// Lights each static prop the way the engine lights models without baked vertex lighting: at the prop's centre,
    /// the ambient cube VRAD stored there plus the 4 strongest world lights that can see it (a ray against the map's
    /// collision; the sun only if the ray ends in a sky brush), with N.L per vertex. The result goes into a copy of the
    /// mesh's vertex colours, drawn by SourceMaps/Prop. Translucent and other special materials keep uSource's material.
    /// Returns props lit.
    /// </summary>
    static int LightProps(GameObject visuals, BspFile bsp, float scale, string folder)
    {
        var group = visuals.transform.Find("[StaticProps]");
        var shader = Shader.Find("SourceMaps/Prop");
        var root = visuals.transform.parent;
        var collision = root != null ? root.Find("Collision")?.GetComponent<Collider>() : null;
        if (group == null || shader == null || root == null) return 0;
        Physics.SyncTransforms();
        AssetDatabase.DeleteAsset(folder);
        Directory.CreateDirectory(folder);
        var materials = new Dictionary<Material, Material>();
        int lit = 0, unlit = 0;
        foreach (Transform prop in group)
        {
            var renderers = prop.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) continue;
            Bounds b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            Num.Vector3 at = ToSource(root.InverseTransformPoint(b.center) / scale);
            // The centre can sit in a leaf without samples (inside a wall, or a tall prop's top in another leaf):
            // then the nearest of a few points around it, lower ones first.
            Num.Vector3[] cube = null;
            foreach (var o in AmbientProbe)
                if ((cube = bsp.AmbientCube(at + o)) != null) break;
            if (cube == null) { cube = new Num.Vector3[6]; unlit++; }

            // The strongest lights reaching the centre (colour without N.L), with their direction (Unity world space).
            var lights = new List<KeyValuePair<Vector3, Vector3>>();
            foreach (var w in bsp.WorldLights)
            {
                var color = BspFile.LightAt(w, at, out var toLight);
                if (color.X + color.Y + color.Z < 0.001f) continue;
                Vector3 dir = root.TransformDirection(ToUnity(toLight)).normalized;
                float dist = w.Type == BspFile.WorldLight.Sky ? 32768f : Num.Vector3.Distance(w.Origin, at);
                if (collision != null && collision.Raycast(new Ray(b.center, dir), out var hit, dist * scale * root.lossyScale.x))
                {
                    if (w.Type != BspFile.WorldLight.Sky) continue; // something in between
                    Num.Vector3 beyond = ToSource(root.InverseTransformPoint(hit.point + dir * scale) / scale);
                    if (!bsp.InSkyBrush(beyond)) continue;
                }
                lights.Add(new KeyValuePair<Vector3, Vector3>(new Vector3(color.X, color.Y, color.Z), dir));
            }
            lights.Sort((x, y) => (y.Key.x + y.Key.y + y.Key.z).CompareTo(x.Key.x + x.Key.y + x.Key.z));
            if (lights.Count > 4) lights.RemoveRange(4, lights.Count - 4);

            foreach (var r in renderers)
            {
                var mf = r.GetComponent<MeshFilter>();
                var smr = r as SkinnedMeshRenderer;
                Mesh src = smr != null ? smr.sharedMesh : mf != null ? mf.sharedMesh : null;
                if (src == null) continue;
                var mats = r.sharedMaterials;
                bool any = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null || !LightmappedShaders.Contains(mats[i].shader.name)) continue;
                    if (!materials.TryGetValue(mats[i], out var m))
                    {
                        m = new Material(shader) { name = mats[i].name };
                        m.SetTexture("_MainTex", mats[i].mainTexture);
                        m.mainTextureScale = mats[i].mainTextureScale;
                        m.mainTextureOffset = mats[i].mainTextureOffset;
                        if (mats[i].HasProperty("_Color")) m.SetColor("_Color", mats[i].GetColor("_Color"));
                        if (mats[i].shader.name == "USource/CutoutGeneric") m.SetFloat("_Cutoff", 0.5f);
                        AssetDatabase.CreateAsset(m, $"{folder}/{materials.Count}_{Path.GetFileName(mats[i].name)}.mat");
                        materials[mats[i]] = m;
                    }
                    mats[i] = m;
                    any = true;
                }
                if (!any) continue;
                // Leaves and other alpha-tested cards are seen from both sides: light them from the brighter side.
                bool twoSided = mats.Any(m => m != null && m.GetFloat("_Cutoff") > 0f);
                var mesh = Object.Instantiate(src);
                mesh.name = src.name + " lit";
                var normals = mesh.normals;
                var colors = new Color[mesh.vertexCount];
                for (int v = 0; v < colors.Length; v++)
                {
                    Vector3 n = v < normals.Length ? r.transform.TransformDirection(normals[v]).normalized : Vector3.up;
                    Vector3 c = Light(cube, lights, n, root);
                    if (twoSided)
                    {
                        Vector3 back = Light(cube, lights, -n, root);
                        if (back.x + back.y + back.z > c.x + c.y + c.z) c = back;
                    }
                    colors[v] = new Color(Mathf.Min(1f, c.x * 0.5f), Mathf.Min(1f, c.y * 0.5f), Mathf.Min(1f, c.z * 0.5f), 1f);
                }
                mesh.colors = colors;
                if (smr != null) smr.sharedMesh = mesh; else mf.sharedMesh = mesh;
                r.sharedMaterials = mats;
            }
            lit++;
        }
        if (unlit > 0) Debug.Log($"[Source Maps] {unlit} props found no ambient light sample nearby (lit by world lights only)");
        return lit;
    }

    /// <summary>Ambient cube plus lights (N.L) for a world-space normal.</summary>
    static Vector3 Light(Num.Vector3[] cube, List<KeyValuePair<Vector3, Vector3>> lights, Vector3 n, Transform root)
    {
        var a = BspFile.AmbientLight(cube, ToSource(root.InverseTransformDirection(n)));
        Vector3 c = new Vector3(a.X, a.Y, a.Z);
        foreach (var l in lights) c += l.Key * Mathf.Max(0f, Vector3.Dot(n, l.Value));
        return c;
    }

    static readonly Num.Vector3[] AmbientProbe =
    {
        Num.Vector3.Zero, new Num.Vector3(0, 0, -32), new Num.Vector3(0, 0, 32), new Num.Vector3(32, 0, 0), new Num.Vector3(-32, 0, 0),
        new Num.Vector3(0, 32, 0), new Num.Vector3(0, -32, 0), new Num.Vector3(0, 0, -96), new Num.Vector3(0, 0, 96)
    };

    /// <summary>Unity axes (units) to Source axes: the inverse of BspGeometry.ToUnity (x = -Y, y = Z, z = X).</summary>
    static Num.Vector3 ToSource(Vector3 u) { return new Num.Vector3(u.z, -u.x, u.y); }
    static Vector3 ToUnity(Num.Vector3 s) { return new Vector3(-s.Y, s.Z, s.X); }

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

    /// <summary>A game file ("sound/x.wav") from the map's pakfile, the CS:S folder or its VPKs (uSource's providers).</summary>
    static byte[] ReadGameFile(System.Type resources, string path)
    {
        var open = resources.GetMethod("OpenFile", new[] { typeof(string), typeof(bool) });
        foreach (string p in new[] { path.ToLowerInvariant(), path })
        {
            Stream stream = null;
            try { stream = (Stream)open.Invoke(null, new object[] { p, false }); }
            catch (System.Exception) { }
            if (stream == null) continue;
            using (stream)
            {
                var copy = new MemoryStream();
                stream.CopyTo(copy);
                return copy.ToArray();
            }
        }
        return null;
    }

    /// <summary>
    /// The map's sky (worldspawn skyname, its LDR version if there is one) as a Skybox/6 Sided material with its six
    /// faces saved as textures; null if a face is missing. Faces as uSource maps them: rt front, lf back, ft left,
    /// bk right.
    /// </summary>
    static Material SaveSky(System.Type resources, string skyName, string folder, string mapName)
    {
        if (skyName == "") return null;
        string[] faces = { "rt", "lf", "ft", "bk", "up", "dn" };
        string[] slots = { "_FrontTex", "_BackTex", "_LeftTex", "_RightTex", "_UpTex", "_DownTex" };
        string name = null;
        foreach (string candidate in new[] { skyName.Replace("_hdr", ""), skyName })
            if (faces.All(f => ReadGameFile(resources, "materials/skybox/" + candidate + f + ".vtf") != null)) { name = candidate; break; }
        if (name == null) return null;

        var load = resources.GetMethod("LoadTexture");
        var material = new Material(Shader.Find("Skybox/6 Sided"));
        for (int i = 0; i < faces.Length; i++)
        {
            var tex = ((Texture2D[,])load.Invoke(null, new object[] { "skybox/" + name + faces[i], null, true, null }))[0, 0];
            // VTF rows run top to bottom: flip, unless uSource gave us a texture already saved the right way up.
            bool flip = !AssetDatabase.Contains(tex);
            var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(tex, rt, new Vector2(1, flip ? -1 : 1), new Vector2(0, flip ? 1 : 0));
            var copy = new Texture2D(tex.width, tex.height, TextureFormat.RGB24, false);
            RenderTexture.active = rt;
            copy.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(rt);
            string path = folder + "/Sky/" + name + faces[i] + ".png";
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, copy.EncodeToPNG());
            Object.DestroyImmediate(copy);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
            material.SetTexture(slots[i], AssetDatabase.LoadAssetAtPath<Texture2D>(path));
        }
        string matPath = folder + "/" + mapName + "_sky.mat";
        AssetDatabase.DeleteAsset(matPath);
        AssetDatabase.CreateAsset(material, matPath);
        return material;
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
