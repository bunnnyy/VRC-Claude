using System.Collections.Generic;
using System.IO;
using System.Linq;
using SourceMaps.Bsp;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Num = System.Numerics;

/// <summary>
/// Tools > Source Maps > Import BSP: builds a map's gameplay side from a CS:S .bsp, independent of the converter
/// used for the visuals (uSource / USource):
///   Collision  - one MeshCollider from every player-solid world brush (incl. invisible clip brushes) and
///                displacement, plus colliders on solid brush entities (func_wall, func_door, ...).
///   Entities   - one marker (SourceEntity) per BSP entity with all its keyvalues and outputs.
///   Teleports  - trigger_teleports become working SourceMapTeleports (on their marker), except filtered ones
///                (bhop block teleports): they need the movement script and stay markers for now.
/// Uses the same axes and scale as uSource (Source x, y, z -> Unity -y, z, x; 1 unit = 0.01905 m by default).
/// Meshes are saved to Assets/SourceMapsImported/&lt;map&gt;/.
/// </summary>
public static class SourceMapImporter
{
    const string OutputRoot = "Assets/SourceMapsImported";
    /// <summary>Teleport triggers get this much taller (units), so a player standing on a thin trigger touches it.</summary>
    const float TriggerRaiseTop = 8f;

    // Brush entities that don't block players (everything else named func_* does).
    static readonly HashSet<string> NonSolid = new HashSet<string>
    {
        "func_illusionary", "func_areaportal", "func_areaportalwindow", "func_dustmotes", "func_dustcloud",
        "func_smokevolume", "func_precipitation", "func_occluder", "func_ladder", "func_buyzone", "func_bomb_target",
        "func_hostage_rescue", "func_clip_vphysics", "func_no_build", "func_fish_pool", "func_viscluster",
        "func_lod",
    };

    [MenuItem("Tools/Source Maps/Import BSP...")]
    public static void ImportMenu()
    {
        string path = EditorUtility.OpenFilePanel("Import Source map", "", "bsp");
        if (string.IsNullOrEmpty(path)) return;
        var root = Import(path, BspGeometry.DefaultScale);
        Selection.activeGameObject = root;
    }

    /// <summary>For batch mode: -executeMethod SourceMapImporter.ImportFromCommandLine -bsp path/to/map.bsp</summary>
    public static void ImportFromCommandLine()
    {
        string[] args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == "-bsp") Import(args[i + 1], BspGeometry.DefaultScale);
    }

    public static GameObject Import(string bspPath, float scale)
    {
        EnsureProgramAssets();
        string mapName = Path.GetFileNameWithoutExtension(bspPath);
        var bsp = BspFile.Load(bspPath);

        string folder = OutputRoot + "/" + mapName;
        Directory.CreateDirectory(folder);
        string meshAssetPath = folder + "/" + mapName + "_collision.asset";
        AssetDatabase.DeleteAsset(meshAssetPath);
        var meshes = new List<Mesh>();

        var root = new GameObject(mapName);

        // World collision: solid world brushes and all displacements in one mesh.
        var worldMesh = new MeshData();
        var worldSolid = bsp.ModelBrushes(0).Where(b => (bsp.Brushes[b].Contents & BspFile.MaskPlayerSolid) != 0).ToList();
        int worldBrushes = worldSolid.Count;
        int innerFaces = BspGeometry.AddSolidBrushes(worldMesh, bsp, worldSolid, scale);
        for (int d = 0; d < bsp.DispInfos.Length; d++) BspGeometry.AddDisplacement(worldMesh, bsp, d, scale);
        var collision = new GameObject("Collision");
        collision.transform.SetParent(root.transform, false);
        collision.isStatic = true;
        collision.AddComponent<MeshCollider>().sharedMesh = ToMesh(worldMesh, mapName + " world", meshes);

        // Entity markers
        var entityRoot = new GameObject("Entities");
        entityRoot.transform.SetParent(root.transform, false);
        var markers = new List<GameObject>();
        var byName = new Dictionary<string, Transform>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var e in bsp.Entities)
        {
            var go = new GameObject(e.TargetName != "" ? e.ClassName + " " + e.TargetName : e.ClassName);
            go.transform.SetParent(entityRoot.transform, false);
            go.transform.localPosition = ToUnity(BspGeometry.ToUnity(e.GetVector("origin"), scale));
            go.transform.localRotation = Rotation(e);
            var marker = go.AddUdonSharpComponent<SourceEntity>();
            marker.className = e.ClassName;
            marker.targetName = e.TargetName;
            marker.keys = new string[e.Pairs.Count];
            marker.values = new string[e.Pairs.Count];
            for (int i = 0; i < e.Pairs.Count; i++)
            {
                marker.keys[i] = e.Pairs[i].Key;
                marker.values[i] = e.Pairs[i].Value.Replace('\u001b', ','); // Source's output separator
            }
            int model = e.BrushModel;
            if (model > 0 && model < bsp.Models.Length)
            {
                Vector3 a = ToUnity(BspGeometry.ToUnity(bsp.Models[model].Mins, scale));
                Vector3 b = ToUnity(BspGeometry.ToUnity(bsp.Models[model].Maxs, scale));
                marker.bounds = new Bounds((a + b) * 0.5f, new Vector3(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y), Mathf.Abs(a.z - b.z)));
                if (e.ClassName.StartsWith("func_") && IsSolid(e))
                {
                    var mesh = new MeshData();
                    innerFaces += BspGeometry.AddSolidBrushes(mesh, bsp,
                        bsp.ModelBrushes(model).Where(brush => (bsp.Brushes[brush].Contents & BspFile.MaskPlayerSolid) != 0), scale);
                    if (mesh.Triangles.Count > 0) go.AddComponent<MeshCollider>().sharedMesh = ToMesh(mesh, go.name, meshes);
                }
            }
            UdonSharpEditorUtility.CopyProxyToUdon(marker);
            markers.Add(go);
            if (e.TargetName != "" && !byName.ContainsKey(e.TargetName)) byName[e.TargetName] = go.transform;
        }

        // Working teleports
        int teleports = 0, filtered = 0, noTarget = 0;
        for (int i = 0; i < bsp.Entities.Count; i++)
        {
            var e = bsp.Entities[i];
            if (e.ClassName != "trigger_teleport" || e.BrushModel <= 0) continue;
            if (e.Get("filtername") != "") { filtered++; continue; }
            Transform target;
            if (!byName.TryGetValue(e.Get("target"), out target)) { noTarget++; continue; }
            var go = markers[i];
            AddTriggerColliders(go, bsp, e, scale, meshes);
            var teleport = go.AddUdonSharpComponent<SourceMapTeleport>();
            teleport.destination = target;
            UdonSharpEditorUtility.CopyProxyToUdon(teleport);
            teleports++;
        }

        // Pushes and boosters for SourceMovement (if it's in the project; looked up by name so SourceMaps compiles
        // without it). Filtered ones stay markers, like filtered teleports.
        var pushType = FindType("SourcePushTrigger");
        var boostType = FindType("SourceBoostTrigger");
        int pushes = 0, boosts = 0, filteredMechanics = 0;
        for (int i = 0; i < bsp.Entities.Count; i++)
        {
            var e = bsp.Entities[i];
            if (e.BrushModel <= 0) continue;
            bool push = e.ClassName == "trigger_push";
            var boostList = push ? new List<BspMechanics.Boost>() : BspMechanics.Boosts(e);
            if (!push && boostList.Count == 0) continue;
            if (e.Get("filtername") != "") { filteredMechanics++; continue; }
            if ((push && pushType == null) || (!push && boostType == null)) continue;
            var go = markers[i];
            AddTriggerColliders(go, bsp, e, scale, meshes);
            if (push)
            {
                var c = go.AddUdonSharpComponent(pushType);
                pushType.GetField("push").SetValue(c, ToUnity(BspGeometry.DirectionToUnity(BspMechanics.Push(e))));
                UdonSharpEditorUtility.CopyProxyToUdon(c);
                pushes++;
            }
            foreach (var b in boostList)
            {
                var c = go.AddUdonSharpComponent(boostType);
                boostType.GetField("addVelocity").SetValue(c, ToUnity(BspGeometry.DirectionToUnity(b.Velocity)));
                boostType.GetField("setGravity").SetValue(c, b.SetGravity);
                boostType.GetField("gravityScale").SetValue(c, b.Gravity);
                boostType.GetField("onLeave").SetValue(c, b.OnLeave);
                UdonSharpEditorUtility.CopyProxyToUdon(c);
                boosts++;
            }
        }

        // Water and ladder volumes as trigger boxes (SourceMovement's water layer 4 and ladder layer 22).
        int water = AddVolumes(root.transform, "Water", bsp, BspFile.ContentsWater, 4, scale);
        int ladders = AddVolumes(root.transform, "Ladders", bsp, BspFile.ContentsLadder, 22, scale);

        // Save all meshes in one asset file.
        AssetDatabase.CreateAsset(meshes[0], meshAssetPath);
        for (int i = 1; i < meshes.Count; i++) AssetDatabase.AddObjectToAsset(meshes[i], meshAssetPath);
        AssetDatabase.SaveAssets();

        Debug.Log($"[Source Maps] {mapName}: {worldBrushes} solid brushes + {bsp.DispInfos.Length} displacements " +
                  $"({worldMesh.Triangles.Count / 3} triangles, {innerFaces} faces trimmed where brushes touch), {bsp.Entities.Count} entity markers, {teleports} working teleports, " +
                  $"{filtered} filtered teleports left as markers, {noTarget} teleports without a destination, " +
                  $"{pushes} pushes, {boosts} boosters, {filteredMechanics} filtered pushes/boosters left as markers, " +
                  $"{water} water volumes, {ladders} ladders");
        return root;
    }

    /// <summary>One convex trigger collider per brush of the entity's brush model, on the marker itself.</summary>
    static void AddTriggerColliders(GameObject go, BspFile bsp, Entity e, float scale, List<Mesh> meshes)
    {
        if (go.GetComponent<MeshCollider>() != null) return; // already done for another component
        foreach (int brush in bsp.ModelBrushes(e.BrushModel))
        {
            var mesh = new MeshData();
            BspGeometry.AddBrush(mesh, bsp, brush, scale, TriggerRaiseTop);
            if (mesh.Triangles.Count == 0) continue;
            var col = go.AddComponent<MeshCollider>();
            col.sharedMesh = ToMesh(mesh, go.name + " brush " + brush, meshes);
            col.convex = true; // Unity triggers must be convex; each brush is
            col.isTrigger = true;
            col.enabled = e.Get("StartDisabled") != "1";
        }
    }

    /// <summary>A trigger box (on `layer`) per world brush with these contents, under a new child object.</summary>
    static int AddVolumes(Transform root, string name, BspFile bsp, int contents, int layer, float scale)
    {
        GameObject parent = null;
        int n = 0;
        foreach (int brush in bsp.ModelBrushes(0))
        {
            if ((bsp.Brushes[brush].Contents & contents) == 0) continue;
            var polys = BspGeometry.BrushPolygons(bsp, brush);
            if (polys.Count == 0) continue;
            var bounds = new Bounds(ToUnity(BspGeometry.ToUnity(polys[0][0], scale)), Vector3.zero);
            foreach (var poly in polys)
                foreach (var v in poly) bounds.Encapsulate(ToUnity(BspGeometry.ToUnity(v, scale)));
            if (parent == null)
            {
                parent = new GameObject(name);
                parent.transform.SetParent(root, false);
            }
            var box = new GameObject(name + " " + brush);
            box.transform.SetParent(parent.transform, false);
            box.layer = layer;
            var col = box.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.center = bounds.center;
            col.size = bounds.size;
            n++;
        }
        return n;
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

    static bool IsSolid(Entity e)
    {
        if (NonSolid.Contains(e.ClassName)) return false;
        if (e.ClassName == "func_brush" && e.Get("Solidity") == "1") return false; // 1 = never solid
        return true;
    }

    /// <summary>Unity rotation for an entity's "angles" (pitch yaw roll) or legacy "angle" (yaw) key.</summary>
    static Quaternion Rotation(Entity e)
    {
        Num.Vector3 angles = e.GetVector("angles");
        if (e.Get("angles") == "" && e.Get("angle") != "")
        {
            float yaw;
            if (float.TryParse(e.Get("angle"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out yaw) && yaw >= 0)
                angles = new Num.Vector3(0, yaw, 0);
        }
        Vector3 forward = ToUnity(BspGeometry.DirectionToUnity(BspGeometry.RotateSource(Num.Vector3.UnitX, angles)));
        Vector3 up = ToUnity(BspGeometry.DirectionToUnity(BspGeometry.RotateSource(Num.Vector3.UnitZ, angles)));
        return Quaternion.LookRotation(forward, up);
    }

    static Vector3 ToUnity(Num.Vector3 v) { return new Vector3(v.X, v.Y, v.Z); }

    /// <summary>MeshData to a Unity mesh, with shared vertices welded.</summary>
    static Mesh ToMesh(MeshData data, string name, List<Mesh> meshes)
    {
        var index = new Dictionary<Vector3, int>();
        var vertices = new List<Vector3>();
        var triangles = new int[data.Triangles.Count];
        for (int i = 0; i < triangles.Length; i++)
        {
            Vector3 v = ToUnity(data.Vertices[data.Triangles[i]]);
            int vi;
            if (!index.TryGetValue(v, out vi))
            {
                vi = vertices.Count;
                vertices.Add(v);
                index[v] = vi;
            }
            triangles[i] = vi;
        }
        var mesh = new Mesh { name = name };
        if (vertices.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        meshes.Add(mesh);
        return mesh;
    }

    public static void EnsureProgramAssets()
    {
        bool created = false;
        foreach (string scriptPath in Directory.GetFiles("Assets/SourceMaps/Scripts", "*.cs"))
        {
            string assetPath = Path.ChangeExtension(scriptPath, ".asset").Replace('\\', '/');
            if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(assetPath) != null) continue;
            var programAsset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
            programAsset.sourceCsScript = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath.Replace('\\', '/'));
            programAsset.ScriptVersion = UdonSharpProgramVersion.CurrentVersion; // see SourceMovementSetup
            AssetDatabase.CreateAsset(programAsset, assetPath);
            created = true;
        }
        if (!created) return;
        AssetDatabase.Refresh();
        UdonSharp.Compiler.UdonSharpCompilerV1.CompileSync();
    }
}
