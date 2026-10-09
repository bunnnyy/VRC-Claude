using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using SourceMaps.Bsp;

/// <summary>
/// Tests the BSP reader and collision builder on real CS:S maps.
///   dotnet run --project Tests/Bsp            (maps from Tests/Bsp/.cache/maps, see get_maps.sh)
///   dotnet run --project Tests/Bsp -- a.bsp   (specific files)
/// Checks are map-independent (teleport targets exist, spawns stand on a floor, nobody spawns inside a wall),
/// plus exact entity counts for maps listed in Expected.
/// </summary>
static class Program
{
    static int failures, checks;

    // Entity counts from an independent dump of the entity lump (previous session, Python).
    static readonly Dictionary<string, Dictionary<string, int>> Expected = new Dictionary<string, Dictionary<string, int>>
    {
        ["bhop_japan"] = new Dictionary<string, int>
        {
            ["trigger_teleport"] = 90, ["info_teleport_destination"] = 37, ["trigger_multiple"] = 39, ["trigger_push"] = 2,
            ["func_button"] = 4, ["filter_activator_name"] = 3, ["prop_physics_override"] = 96, ["prop_dynamic"] = 79,
            ["move_rope"] = 48, ["info_player_counterterrorist"] = 66,
        },
    };

    static int Main(string[] args)
    {
        var files = args.Length > 0 ? args.ToList()
            : Directory.Exists(CacheDir) ? Directory.GetFiles(CacheDir, "*.bsp").OrderBy(f => f).ToList() : new List<string>();
        if (files.Count == 0)
        {
            Console.WriteLine("No maps. Run Tests/Bsp/get_maps.sh first, or pass .bsp paths.");
            return 1;
        }
        foreach (var file in files) TestMap(file);
        Console.WriteLine($"\n{checks - failures}/{checks} checks passed");
        return failures == 0 ? 0 : 1;
    }

    static string CacheDir => Path.Combine(AppContext.BaseDirectory, "../../../.cache/maps");

    static void Check(bool ok, string what)
    {
        checks++;
        if (!ok) failures++;
        Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what);
    }

    static void TestMap(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        Console.WriteLine($"\n== {name}");
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var bsp = BspFile.Load(path);
        Console.WriteLine($"  loaded in {watch.ElapsedMilliseconds} ms: v{bsp.Version}, {bsp.Entities.Count} entities, {bsp.Brushes.Length} brushes, " +
                          $"{bsp.Models.Length} models, {bsp.DispInfos.Length} displacements");

        // Entities
        Check(bsp.Entities.Count > 0 && bsp.Entities[0].ClassName == "worldspawn", "first entity is worldspawn");
        if (Expected.TryGetValue(name, out var expected))
            foreach (var kv in expected)
            {
                int n = bsp.Entities.Count(e => e.ClassName == kv.Key);
                Check(n == kv.Value, $"{kv.Key}: {n} (expected {kv.Value})");
            }

        var byName = new Dictionary<string, Entity>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in bsp.Entities)
            if (e.TargetName != "" && !byName.ContainsKey(e.TargetName)) byName[e.TargetName] = e;
        var teleports = bsp.Entities.Where(e => e.ClassName == "trigger_teleport").ToList();
        int missing = teleports.Count(t => !byName.ContainsKey(t.Get("target")));
        Check(missing == 0, $"all {teleports.Count} trigger_teleport targets exist ({missing} missing)");
        Check(bsp.Entities.Where(e => e.BrushModel > 0).All(e => e.BrushModel < bsp.Models.Length), "brush entity models are in range");

        // Collision
        watch.Restart();
        var world = bsp.ModelBrushes(0);
        var solid = world.Where(i => (bsp.Brushes[i].Contents & BspFile.MaskPlayerSolid) != 0).ToList();
        var mesh = new MeshData();
        int badBrushes = 0;
        foreach (int i in solid)
        {
            if (BspGeometry.BrushPolygons(bsp, i).Count < 4) badBrushes++;
            BspGeometry.AddBrush(mesh, bsp, i, BspGeometry.DefaultScale);
        }
        int brushTris = mesh.Triangles.Count / 3;
        for (int i = 0; i < bsp.DispInfos.Length; i++) BspGeometry.AddDisplacement(mesh, bsp, i, BspGeometry.DefaultScale);
        Console.WriteLine($"  collision built in {watch.ElapsedMilliseconds} ms: {solid.Count} solid world brushes ({brushTris} triangles), " +
                          $"{mesh.Triangles.Count / 3 - brushTris} displacement triangles");
        Check(badBrushes == 0, $"every solid brush is a closed volume (>= 4 faces), {badBrushes} broken");
        Check(mesh.Vertices.All(v => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z)), "all vertices finite");

        // Points where a player stands: spawns and teleport destinations.
        var standPoints = bsp.Entities.Where(e => e.ClassName == "info_teleport_destination" || e.ClassName.StartsWith("info_player_")).ToList();
        int noFloor = 0, floorFacesDown = 0, stuck = 0;
        var problems = new List<string>();
        var tris = new Tri[mesh.Triangles.Count / 3];
        for (int i = 0; i < tris.Length; i++)
            tris[i] = new Tri(mesh.Vertices[mesh.Triangles[i * 3]], mesh.Vertices[mesh.Triangles[i * 3 + 1]], mesh.Vertices[mesh.Triangles[i * 3 + 2]]);
        foreach (var e in standPoints)
        {
            Vector3 feet = e.GetVector("origin");
            // A floor (front face up) below the feet, through the generated mesh. Spawns often float in a spawn
            // room (bhop_japan: 128 units above a floor that teleports you to the start), so allow 512 units.
            var hit = Raycast(tris, BspGeometry.ToUnity(feet + new Vector3(0, 0, 1), BspGeometry.DefaultScale), new Vector3(0, -1, 0), 512 * BspGeometry.DefaultScale);
            string label = $"{e.ClassName} {e.TargetName} at {feet}";
            if (hit == null) { noFloor++; problems.Add("no floor: " + label); }
            else if (hit.Value.Y <= 0) { floorFacesDown++; problems.Add("floor faces down: " + label); }
            // The player hull (32 x 32 x 72 from the feet) must not start inside a solid brush.
            int inside = solid.FirstOrDefault(i => HullInBrush(bsp, i, feet), -1);
            if (inside >= 0) { stuck++; problems.Add($"inside brush {inside}: " + label); }
        }
        Check(noFloor == 0, $"{standPoints.Count} spawns/teleport destinations have a floor below ({noFloor} without)");
        Check(floorFacesDown == 0, $"floors under them face up (winding), {floorFacesDown} face down");
        Check(stuck == 0, $"no spawn/destination puts the player hull inside a brush ({stuck} stuck)");
        foreach (var p in problems.Take(8)) Console.WriteLine("         " + p);

        // Brush entity models are stored relative to the entity origin: bounds should be near zero.
        var brushEnts = bsp.Entities.Where(e => e.BrushModel > 0).ToList();
        int farOff = brushEnts.Count(e =>
        {
            var m = bsp.Models[e.BrushModel];
            Vector3 c = (m.Mins + m.Maxs) * 0.5f;
            return c.Length() > (m.Maxs - m.Mins).Length() + 64;
        });
        Console.WriteLine($"  {brushEnts.Count} brush entities, {farOff} with model bounds far from their origin");
    }

    struct Tri
    {
        public Vector3 A, B, C, N;
        public Tri(Vector3 a, Vector3 b, Vector3 c) { A = a; B = b; C = c; N = Vector3.Cross(b - a, c - a); }
    }

    /// <summary>Nearest hit along a ray (both sides), returning the hit triangle's face normal.</summary>
    static Vector3? Raycast(Tri[] tris, Vector3 origin, Vector3 dir, float maxDist)
    {
        float best = maxDist;
        Vector3? normal = null;
        foreach (var t in tris)
        {
            // Möller-Trumbore
            Vector3 e1 = t.B - t.A, e2 = t.C - t.A, p = Vector3.Cross(dir, e2);
            float det = Vector3.Dot(e1, p);
            if (Math.Abs(det) < 1e-9f) continue;
            float inv = 1 / det;
            Vector3 s = origin - t.A;
            float u = Vector3.Dot(s, p) * inv;
            if (u < 0 || u > 1) continue;
            Vector3 q = Vector3.Cross(s, e1);
            float v = Vector3.Dot(dir, q) * inv;
            if (v < 0 || u + v > 1) continue;
            float d = Vector3.Dot(e2, q) * inv;
            if (d >= 0 && d < best) { best = d; normal = t.N; }
        }
        return normal;
    }

    /// <summary>
    /// Does an axis-aligned player hull at `feet` overlap a brush? Uses every side including bevels,
    /// which makes the plane test exact for boxes (this is how Source traces hulls against brushes).
    /// </summary>
    static bool HullInBrush(BspFile bsp, int brushIndex, Vector3 feet)
    {
        var brush = bsp.Brushes[brushIndex];
        Vector3 mins = new Vector3(-16, -16, 0), maxs = new Vector3(16, 16, 72);
        for (int s = 0; s < brush.NumSides; s++)
        {
            var plane = bsp.Planes[bsp.BrushSides[brush.FirstSide + s].Plane];
            Vector3 n = plane.Normal;
            float nearest = Vector3.Dot(n, feet)
                + Math.Min(n.X * mins.X, n.X * maxs.X) + Math.Min(n.Y * mins.Y, n.Y * maxs.Y) + Math.Min(n.Z * mins.Z, n.Z * maxs.Z);
            if (nearest >= plane.Dist - 0.1f) return false; // separated (touching counts as outside)
        }
        return true;
    }
}
