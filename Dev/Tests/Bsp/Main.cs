using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using SourceMaps.Bsp;

/// <summary>
/// Tests the BSP reader and collision builder on real CS:S maps.
///   dotnet run --project Dev/Tests/Bsp            (maps from Dev/Tests/Bsp/.cache/maps, see get_maps.sh)
///   dotnet run --project Dev/Tests/Bsp -- a.bsp   (specific files)
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
            Console.WriteLine("No maps. Run Dev/Tests/Bsp/get_maps.sh first, or pass .bsp paths.");
            return 1;
        }
        TestMechanics();
        foreach (var file in files) TestMap(file);
        Console.WriteLine($"\n{checks - failures}/{checks} checks passed");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>Booster/push parsing on entities written like the entity lump (no map needed).</summary>
    static void TestMechanics()
    {
        Console.WriteLine("\n== booster and push parsing");
        Entity Parse(string text) => Entity.ParseAll(text)[0];
        var esc = "\u001b";
        var boost = Parse("{\n\"classname\" \"trigger_multiple\"\n\"OnStartTouch\" \"!activator" + esc + "AddOutput" + esc + "basevelocity 0 0 800" + esc + "0" + esc + "-1\"\n" +
                          "\"OnEndTouch\" \"!activator,AddOutput,gravity 0.5,0,-1\"\n\"OnTrigger\" \"!activator,AddOutput,targetname x,0,-1\"\n}");
        var b = BspMechanics.Boosts(boost);
        Check(b.Count == 2 && b[0].Velocity == new System.Numerics.Vector3(0, 0, 800) && !b[0].OnLeave && !b[0].SetGravity,
            "basevelocity output (ESC separated) -> boost on entering");
        Check(b.Count == 2 && b[1].OnLeave && b[1].SetGravity && b[1].Gravity == 0.5f, "gravity output on OnEndTouch (comma separated) -> on leaving");
        var plain = Parse("{\n\"classname\" \"trigger_multiple\"\n\"OnTrigger\" \"!activator,AddOutput,targetname activator,0.09,-1\"\n}");
        Check(BspMechanics.Boosts(plain).Count == 0, "other AddOutputs (targetname) are not boosters");
        var grav = Parse("{\n\"classname\" \"trigger_gravity\"\n\"gravity\" \"0.25\"\n}");
        Check(BspMechanics.Boosts(grav).Count == 1 && BspMechanics.Boosts(grav)[0].Gravity == 0.25f, "trigger_gravity -> gravity scale");
        var push = Parse("{\n\"classname\" \"trigger_push\"\n\"speed\" \"1500\"\n\"pushdir\" \"-90 0 0\"\n}");
        var v = BspMechanics.Push(push);
        Check(Math.Abs(v.Z - 1500) < 0.5f && Math.Abs(v.X) < 0.5f && Math.Abs(v.Y) < 0.5f, $"trigger_push pushdir -90 0 0 speed 1500 -> straight up ({v})");
        var fwd = BspMechanics.Push(Parse("{\n\"classname\" \"trigger_push\"\n\"speed\" \"100\"\n\"pushdir\" \"0 90 0\"\n}"));
        Check(Math.Abs(fwd.Y - 100) < 0.5f, $"trigger_push yaw 90 -> +y ({fwd})");

        Console.WriteLine("\n== breakable glass (breaks within knife reach when one knife hit breaks it in CS:S)");
        Entity Breakable(string keys) => Parse("{\n\"classname\" \"func_breakable\"\n\"model\" \"*1\"\n" + keys + "}");
        Check(BspMechanics.BreaksOnApproach(Breakable("\"material\" \"0\"\n\"health\" \"1\"\n")), "glass, health 1 -> breaks");
        Check(!BspMechanics.BreaksOnApproach(Breakable("\"material\" \"7\"\n\"health\" \"1\"\n")), "unbreakable glass (material 7) -> stays");
        Check(!BspMechanics.BreaksOnApproach(Breakable("\"material\" \"0\"\n\"health\" \"0\"\n")), "health 0 (takes no damage) -> stays");
        Check(!BspMechanics.BreaksOnApproach(Breakable("\"material\" \"0\"\n\"health\" \"1\"\n\"spawnflags\" \"1\"\n")), "Only Break on Trigger -> stays");
        Check(!BspMechanics.BreaksOnApproach(Breakable("\"material\" \"1\"\n\"health\" \"100\"\n")), "tougher than one knife hit (health 100) -> stays");
        Check(!BspMechanics.BreaksOnApproach(Breakable("\"material\" \"0\"\n\"health\" \"1\"\n\"minhealthdmg\" \"50\"\n")), "needs more damage than a knife hit -> stays");
        Check(!BspMechanics.BreaksOnApproach(Parse("{\n\"classname\" \"func_wall\"\n\"model\" \"*1\"\n\"health\" \"1\"\n}")), "not a breakable -> stays");
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
        int noTarget = teleports.Count(t => t.Get("target") == ""); // do nothing in Source either (bhop_kitsune has 7)
        int missing = teleports.Count(t => t.Get("target") != "" && !byName.ContainsKey(t.Get("target")));
        Check(missing == 0, $"all {teleports.Count - noTarget} trigger_teleport targets exist ({missing} missing; {noTarget} have no target)");
        Check(bsp.Entities.Where(e => e.BrushModel > 0).All(e => e.BrushModel < bsp.Models.Length), "brush entity models are in range");

        // Collision
        watch.Restart();
        var world = bsp.ModelBrushes(0);
        var solid = world.Where(i => (bsp.Brushes[i].Contents & BspFile.MaskPlayerSolid) != 0).ToList();
        var whole = new MeshData();
        int badBrushes = 0;
        foreach (int i in solid)
        {
            if (BspGeometry.BrushPolygons(bsp, i).Count < 4) badBrushes++;
            BspGeometry.AddBrush(whole, bsp, i, BspGeometry.DefaultScale);
        }
        var mesh = new MeshData();
        int trimmed = BspGeometry.AddSolidBrushes(mesh, bsp, solid, BspGeometry.DefaultScale);
        int brushTris = mesh.Triangles.Count / 3;
        Console.WriteLine($"  touching brushes: {trimmed} faces trimmed, {whole.Triangles.Count / 3} -> {brushTris} brush triangles");
        if (name == "bhop_eazy_v2")
        {
            // A slope built from brushes side by side (Unity x -2256..-1664 u, falling from y 208 at z 5056 to y -160 at
            // z 5376). Their side faces at x = -1728 reach up to the slope's surface, where a surfer caught on them.
            float s = BspGeometry.DefaultScale;
            Func<MeshData, int> seam = m => Enumerable.Range(0, m.Triangles.Count / 3).Count(t =>
            {
                var v = new[] { m.Vertices[m.Triangles[3 * t]], m.Vertices[m.Triangles[3 * t + 1]], m.Vertices[m.Triangles[3 * t + 2]] };
                return v.All(p => Math.Abs(p.X / s + 1728) < 0.5f && p.Z / s > 5050 && p.Z / s < 5380) &&
                       v.Any(p => Math.Abs(p.Y / s - (208 - 1.15f * (p.Z / s - 5056))) < 1f);
            });
            Check(seam(whole) > 0 && seam(mesh) == 0, $"inner side faces at the ramp seam removed ({seam(whole)} triangles before, {seam(mesh)} after)");
        }
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
        int jailed = 0;
        foreach (var e in standPoints)
        {
            Vector3 feet = e.GetVector("origin");
            // Destinations inside a player clip brush trap the player in Source too: map design ("jails", e.g.
            // bhop_arcane_v1's *_stop destinations for skipping). Counted, not checked.
            if (world.Any(i => bsp.Brushes[i].Contents == (bsp.Brushes[i].Contents & ~BspFile.ContentsSolid) &&
                               (bsp.Brushes[i].Contents & BspFile.ContentsPlayerClip) != 0 && HullInBrush(bsp, i, feet))) { jailed++; continue; }
            // A floor (front face up) below the feet, through the generated mesh. Spawns often float in a spawn
            // room (bhop_japan: 128 units above a floor that teleports you to the start), so allow 512 units.
            // (Off the grid by a fraction of a unit: a ray exactly along a triangle edge can slip between the triangles.)
            var hit = Raycast(tris, BspGeometry.ToUnity(feet + new Vector3(0.37f, 0.21f, 1), BspGeometry.DefaultScale), new Vector3(0, -1, 0), 512 * BspGeometry.DefaultScale);
            string label = $"{e.ClassName} {e.TargetName} at {feet}";
            if (hit == null) { noFloor++; problems.Add("no floor: " + label); }
            else if (hit.Value.Y <= 0) { floorFacesDown++; problems.Add("floor faces down: " + label); }
            // The player hull (32 x 32 x 72 from the feet) must not start inside a solid brush.
            int inside = solid.FirstOrDefault(i => HullInBrush(bsp, i, feet), -1);
            if (inside >= 0) { stuck++; problems.Add($"inside brush {inside}: " + label); }
        }
        if (jailed > 0) Console.WriteLine($"  {jailed} destinations are inside player clips (jails by map design), skipped below");
        Check(noFloor == 0, $"{standPoints.Count - jailed} spawns/teleport destinations have a floor below ({noFloor} without)");
        Check(floorFacesDown == 0, $"floors under them face up (winding), {floorFacesDown} face down");
        Check(stuck == 0, $"no spawn/destination puts the player hull inside a brush ({stuck} stuck)");
        foreach (var p in problems.Take(8)) Console.WriteLine("         " + p);

        // Ambient light (what Source lights props with) at the stand points, 36 units up (chest height).
        var lit = standPoints.Select(e => bsp.AmbientCube(e.GetVector("origin") + new Vector3(0, 0, 36))).Where(c => c != null).ToList();
        float mean = lit.Count == 0 ? 0 : lit.Average(c => c.Average(v => (v.X + v.Y + v.Z) / 3f));
        Console.WriteLine($"  ambient light: {bsp.LeafAmbient.Count(l => l != null && l.Length > 0)} leaves with samples, mean at stand points {mean:F2} (1 = full)");
        Check(lit.Count >= standPoints.Count * 0.9 && mean > 0.002f && mean < 4f,
            $"ambient light samples at {lit.Count}/{standPoints.Count} spawns/destinations, plausible brightness (indirect light only)");

        // Bhop blocks: name triggers, filters, touch doors.
        int nameTrig = bsp.Entities.Count(e => BspMechanics.NameSets(e).Count > 0);
        var filteredTp = bsp.Entities.Where(e => e.ClassName == "trigger_teleport" && e.Get("filtername") != "").ToList();
        int resolved = filteredTp.Count(e => BspMechanics.FilterName(bsp, e, out _) != "?");
        var doors = bsp.Entities.Where(BspMechanics.IsTouchDoor).ToList();
        Console.WriteLine($"  bhop blocks: {nameTrig} name triggers, {resolved}/{filteredTp.Count} filtered teleports resolved, {doors.Count} touch doors");
        Check(resolved == filteredTp.Count, $"every filtered teleport's filter is a filter_activator_name or missing ({resolved}/{filteredTp.Count})");
        if (name == "bhop_eazy_v2")
        {
            var move = BspMechanics.DoorMove(bsp, doors[0]);
            Check(doors.Count == 259 && Math.Abs(move.Z + 9) < 0.01f && Math.Abs(move.X) < 0.01f && Math.Abs(move.Y) < 0.01f,
                $"259 touch-door blocks, the first sinks 9 units (8 thick + lip 1): {move}");
        }
        // Breakable glass: on eazy, the four health-1 glass panes (red lanes 3 and 4, the bonus) break; glass_to_m249
        // (Only Break on Trigger, health 0) and the health-170 plate under a spawn stay. Arcane's wood crate stays too.
        var glass = bsp.Entities.Where(BspMechanics.BreaksOnApproach).Select(e => e.Get("hammerid")).OrderBy(h => h).ToList();
        int breakables = bsp.Entities.Count(e => e.ClassName.StartsWith("func_breakable"));
        Console.WriteLine($"  breakables: {glass.Count} of {breakables} break within knife reach");
        if (name == "bhop_eazy_v2")
            Check(string.Join(",", glass) == "20789,21469,21523,30925" && breakables == 6,
                $"4 of 6 breakables break (red lanes 3 and 4, bonus); glass_to_m249 and the spawn plate stay ({string.Join(",", glass)})");
        if (name == "bhop_arcane_v1") Check(glass.Count == 0 && breakables == 1, "the wood crate (health 100) stays");
        if (name == "bhop_japan")
        {
            var t = bsp.Entities.First(e => BspMechanics.NameSets(e).Any(n => n.Name == "activator"));
            var sets = BspMechanics.NameSets(t);
            Check(sets.Any(n => n.Name == "activator" && Math.Abs(n.Delay - 0.09f) < 1e-4) && sets.Any(n => n.Name == "default" && Math.Abs(n.Delay - 0.1f) < 1e-4),
                "block trigger renames to activator at 0.09 s and default at 0.1 s");
        }

        var kinds = bsp.WorldLights.GroupBy(w => w.Type).OrderBy(g => g.Key).Select(g => $"type {g.Key}: {g.Count()}");
        Console.WriteLine($"  world lights: {string.Join(", ", kinds)}");
        Check(bsp.WorldLights.Length > 0 && bsp.WorldLights.All(w => float.IsFinite(w.Intensity.X) && w.Type >= 0 && w.Type <= 5),
            $"{bsp.WorldLights.Length} world lights read");

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
