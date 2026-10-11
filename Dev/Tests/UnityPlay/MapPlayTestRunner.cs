using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using VRC.SDK3.ClientSim;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// Play mode tests on an imported Source map with ClientSim and SourceMovement (real PhysX on the generated
/// collision). Started by PlayTestBootstrap.RunMap. Results are logged with a [SMTEST] prefix.
///   stand     - at every teleport destination and a few spawns the player lands on a floor and stays there
///   teleports - falling into every reachable working trigger_teleport (best of 3 spots) brings the player to its
///               destination (followed through relay teleports at the destination)
///   runs      - bhop (hold W + jump, strafe with A/D and turning) along the longest open floors from the destinations,
///               and surf along the map's biggest surfable slopes: no stalls (sudden speed loss with no wall ahead,
///               e.g. a fake wall at a seam between collision mesh triangles)
///   blocks    - bhop blocks: standing on one sends you back, bouncing on it doesn't, switched off you can stand
///   glass     - breakable glass (first, as it stays broken): flying at each pane breaks it before the hull touches it,
///               through it without losing speed; still broken after the map is switched off and on
/// SM_ONLY=runs, SM_ONLY=blocks or SM_ONLY=glass (environment) runs only that part.
/// </summary>
public class MapPlayTestRunner : MonoBehaviour
{
    public int frameRate = 90;
    const float U = 0.01905f;
    readonly List<string> report = new List<string>();
    int failures;
    bool finished;
    UdonBehaviour movement;
    VRCPlayerApi player;
    Transform playerBody;
    Keyboard keyboard;

    IEnumerator Start()
    {
        Time.captureDeltaTime = 1f / frameRate;
        for (int i = 0; i < 600 && (player == null || movement == null); i++)
        {
            yield return null;
            player = Networking.LocalPlayer;
            movement = Loaded(FindUdon("SourceMovement"));
        }
        if (player == null || movement == null) { Check(false, "setup: player and SourceMovement loaded"); Finish(); yield break; }
        foreach (var c in Resources.FindObjectsOfTypeAll<ClientSimPlayerController>())
            if (c.gameObject.scene.IsValid()) playerBody = c.transform;
        // Batchmode has no focused game view: let device input through anyway (as PlayTestRunner).
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        keyboard = InputSystem.AddDevice<Keyboard>("SMMapKeyboard");
        for (int i = 0; i < 30; i++) yield return null;
        foreach (var menu in Resources.FindObjectsOfTypeAll<ClientSimMenu>())
            if (menu.gameObject.scene.IsValid()) { menu.WarningAccepted(); menu.CloseMenu(); }
        for (int i = 0; i < 10; i++) yield return null;

        var markers = FindObjectsOfType<SourceEntity>();
        var floorY = float.MaxValue;
        foreach (var c in FindObjectsOfType<MeshCollider>()) if (!c.isTrigger) floorY = Mathf.Min(floorY, c.bounds.min.y);
        Log($"{markers.Length} entity markers, {FindObjectsOfType<SourceMapTeleport>().Length} working teleports, world bottom y {floorY:F1} m");

        // stand: teleport to each destination (and the first 3 spawns), wait, expect grounded above the world bottom.
        var points = new List<SourceEntity>();
        int spawns = 0;
        foreach (var m in markers)
        {
            if (m.className == "info_teleport_destination") points.Add(m);
            else if (m.className == "info_player_counterterrorist" && spawns++ < 3) points.Add(m);
        }
        if (System.Environment.GetEnvironmentVariable("SM_ONLY") == "glass") { yield return BreakableGlass(); Finish(); yield break; }
        bool onlyRuns = System.Environment.GetEnvironmentVariable("SM_ONLY") == "runs";
        if (!onlyRuns && System.Environment.GetEnvironmentVariable("SM_ONLY") != "blocks") yield return BreakableGlass();
        if (onlyRuns) { yield return Runs(points); Finish(); yield break; }
        if (System.Environment.GetEnvironmentVariable("SM_ONLY") == "blocks") { yield return BhopBlocks(points); Finish(); yield break; }
        int stood = 0, launched = 0;
        var notStanding = new List<string>();
        foreach (var p in points)
        {
            // A destination inside a trigger_push is a launcher (bhop_monster_jam's s1, at the foot of a push up a
            // tower): the push takes the player, as in Source.
            if (InPush(p.transform.position + Vector3.up * 0.5f)) { launched++; continue; }
            yield return Teleport(p.transform.position, p.transform.rotation);
            yield return Frames(2f);
            // Grounded, or still at the destination: some maps put a teleport back to the same destination on the
            // floor below it (bhop_japan's tele_dest_33/34 over pillars), so in Source too you bounce until you move.
            bool ok = player.GetPosition().y > floorY && (OnGround() || Vector3.Distance(player.GetPosition(), p.transform.position) < 2f);
            if (ok) stood++;
            else notStanding.Add($"{p.name} (grounded {OnGround()}, y {player.GetPosition().y:F2} m)");
        }
        Check(stood == points.Count - launched, $"stand: player lands and stays on a floor at {stood}/{points.Count - launched} destinations/spawns" +
            (launched > 0 ? $" ({launched} inside a push, not tested)" : ""));
        foreach (var s in notStanding) Log("   not standing: " + s);

        // teleports: reset at the first point, then drop into each working teleport's trigger from above and expect to
        // reach its destination.
        int arrived = 0, total = 0;
        var missed = new List<string>();
        var unreachable = new List<string>();
        foreach (var t in FindObjectsOfType<SourceMapTeleport>())
        {
            var col = t.GetComponent<Collider>();
            if (col == null || !col.enabled) continue;
            if (t.filterName != "") continue; // bhop block teleports: only for a player with a name (blocks test)
            total++;
            // Drop in from above like a falling player: the first spot on a 5x5 grid over the trigger where there's
            // room above it and no ground above the trigger's top.
            var drops = DropPoints(t, 3);
            if (drops.Count == 0) { unreachable.Add(t.name + " " + t.GetComponent<SourceEntity>().GetValue("hammerid")); total--; continue; }
            Vector3 dest = Relayed(t);
            Vector3 start = drops[0];
            bool reached = false;
            var trace = new StringBuilder();
            // Like a player who can come from anywhere: the trigger works if dropping in at one of its best spots
            // (ground lowest under it) gets there. Spots inside a push are skipped: bhop_arcane_v1's out-of-bounds
            // teleport 423175 shares its box with an updraft (trigger_push up at 1250 u/s), so nobody falls into it.
            foreach (var spot in drops)
            {
                start = spot;
                trace.Clear();
                yield return Teleport(points[0].transform.position, Quaternion.identity);
                yield return Frames(0.3f);
                yield return Teleport(start, Quaternion.identity);
                for (int i = 0; i < frameRate && !reached; i++)
                {
                    yield return null;
                    if (i % 10 == 0) trace.Append($" f{i} {player.GetPosition():F1}");
                    Vector3 d = player.GetPosition() - dest;
                    reached = new Vector2(d.x, d.z).magnitude < 0.5f && Mathf.Abs(d.y) < 1.5f;
                }
                if (reached) break;
            }
            if (reached) arrived++;
            else
            {
                string overlap = "";
                foreach (var cc in FindObjectsOfType<CharacterController>())
                    foreach (var tc in t.GetComponents<MeshCollider>())
                        overlap += Physics.ComputePenetration(cc, cc.transform.position, cc.transform.rotation, tc, tc.transform.position, tc.transform.rotation, out var dir, out var dist)
                            ? $" overlaps {tc.sharedMesh.name} by {dist:F3} m;" : $" clear of {tc.sharedMesh.name} (cc bottom {cc.bounds.min.y:F2}, trigger {tc.bounds.min.y:F2}..{tc.bounds.max.y:F2});";
                missed.Add($"{t.name} {t.GetComponent<SourceEntity>().GetValue("hammerid")}:{overlap} start {start:F1}, trigger size {col.bounds.size:F1}, destination {dest:F1}, player:{trace}");
            }
        }
        Check(total > 0 && arrived == total, $"teleports: {arrived}/{total} working trigger_teleports send the player to their destination");
        foreach (var s in missed) Log("   missed: " + s);
        Log($"   {unreachable.Count} teleports not reachable from above (ground above the trigger), not tested: " + string.Join(", ", unreachable));
        yield return BhopBlocks(points);
        yield return Runs(points);
        Finish();
    }

    // ------------------------------------------------------------------ breakable glass

    /// <summary>
    /// Breakable glass (SourceMapBreakable): glass that one knife hit breaks in CS:S breaks when the player's CS:S box
    /// comes within knife reach (48 u), before it touches the glass, also when fast, and the player flies through
    /// without losing speed. It stays broken when the map is switched off and on (as SourceMapManager does). Other
    /// breakables stay solid.
    /// </summary>
    IEnumerator BreakableGlass()
    {
        var glass = FindObjectsOfType<SourceMapBreakable>();
        int others = 0, othersSolid = 0, expected = 0;
        foreach (var m in FindObjectsOfType<SourceEntity>())
        {
            if (!m.className.StartsWith("func_breakable")) continue;
            // CS:S: one knife hit breaks it (the importer's rule, written again here so the test checks it)
            float.TryParse(m.GetValue("health"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float health);
            float.TryParse(m.GetValue("minhealthdmg"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float minDamage);
            int.TryParse(m.GetValue("spawnflags"), out int flags);
            bool surf = m.className == "func_breakable_surf"; // window glass: any hit breaks it
            bool upright = m.bounds.size.y > Mathf.Min(m.bounds.size.x, m.bounds.size.z); // flat ones stay
            int.TryParse(m.GetValue("propdata"), out int propData);
            if (m.GetValue("model").StartsWith("*") && upright && m.GetValue("damagefilter") == "" && (surf ||
                (propData == 0 && (flags & 1) == 0 && m.GetValue("material") != "7" && health > 0f && health <= 15f && minDamage <= 15f)))
                expected++;
            if (m.GetComponentInChildren<SourceMapBreakable>() != null) continue;
            others++;
            var c = m.GetComponent<MeshCollider>();
            if (c != null && c.enabled) othersSolid++;
        }
        Log($"breakable glass: {glass.Length} panes, {others} other breakables");
        Check(glass.Length == expected, $"glass: {glass.Length}/{expected} breakables one knife hit breaks become breakable glass");
        if (others > 0) Check(othersSolid == others, $"glass: the other breakables (trigger-only, tougher) stay solid ({othersSolid}/{others})");
        if (glass.Length == 0) yield break;

        // Fly at each pane square to it, through its middle, with gravity off: feet 28 u over its bottom, so the hull
        // passes over the teleport strip under it and under the frame over it (bhop_eazy_v2: z 72..168). VRChat's
        // 84 u capsule doesn't fit there, so the map is Hull Only, as in a world (the lobby setup does that). Each pane
        // twice: at 400 u/s, then (made whole again) at 1500 u/s, where VRChat's player trails the hull more.
        var root = glass[0].transform.root.gameObject;
        var layers = HullOnly(root, null);
        float[] speeds = { 400f, 1500f };
        int[] broke = new int[speeds.Length], early = new int[speeds.Length], through = new int[speeds.Length];
        movement.SetProgramVariable("gravityScale", 0f);
        foreach (var g in glass)
        {
            var udon = UdonOf(g);
            Bounds b = g.solid.bounds;
            Vector3 n = b.extents.x < b.extents.z ? Vector3.right : Vector3.forward; // across the pane
            float half = Vector3.Dot(b.extents, n);
            Vector3 start = b.center - n * (half + 130f * U);
            start.y = b.min.y + 28f * U;
            for (int k = 0; k < speeds.Length; k++)
            {
                if (k > 0) // whole again (the test only: the script never restores it)
                {
                    udon.SetProgramVariable("broken", false);
                    g.solid.enabled = true;
                    if (g.visuals != null) g.visuals.gameObject.SetActive(true);
                }
                bool whole = g.solid.enabled && !(bool)udon.GetProgramVariable("broken");
                yield return Teleport(start, Quaternion.LookRotation(n));
                yield return Frames(0.2f);
                movement.SetProgramVariable("velocity", n * speeds[k]);
                float gapAtBreak = float.NaN, minSpeed = float.MaxValue, past = 0f;
                bool teleported = false;
                Vector3 prev = player.GetPosition();
                for (int i = 0; i < frameRate && past < 64f; i++)
                {
                    yield return null;
                    Vector3 pos = player.GetPosition();
                    if ((pos - prev).magnitude > (speeds[k] * Time.deltaTime + 64f) * U) { teleported = true; break; }
                    prev = pos;
                    // Gap between SourceMovement's 32 u wide hull and the pane, along the flight.
                    Vector3 hull = (Vector3)movement.GetProgramVariable("origin") * U;
                    float gap = (Vector3.Dot(b.center - hull, n) - half) / U - 16f;
                    if (float.IsNaN(gapAtBreak) && (bool)udon.GetProgramVariable("broken")) gapAtBreak = gap;
                    past = -gap - 2f * (half / U) - 32f; // how far the hull's back is past the pane
                    minSpeed = Mathf.Min(minSpeed, Speed());
                }
                // Breaks within reach, plus how far the player gets in a physics step and two frames (the check's allowance).
                float most = BspReach + speeds[k] * (Time.fixedDeltaTime + 2f / frameRate) + 2f;
                bool ok = whole && !float.IsNaN(gapAtBreak) && (g.visuals == null || !g.visuals.gameObject.activeSelf);
                if (ok) broke[k]++;
                if (ok && gapAtBreak > 0f && gapAtBreak <= most) early[k]++;
                if (ok && !teleported && past >= 64f && minSpeed > speeds[k] - 1f) through[k]++;
                Log($"   pane at {g.transform.position / U:F0}, {speeds[k]:F0} u/s: broke {(float.IsNaN(gapAtBreak) ? "never" : $"with the hull {gapAtBreak:F1} u away (at most {most:F0})")}, " +
                    $"slowest {minSpeed:F0} u/s, {(teleported ? "teleported" : $"{past:F0} u past it")}, visuals {(g.visuals != null ? "linked" : "none (no uSource)")}");
            }
        }
        movement.SetProgramVariable("gravityScale", 1f);
        HullOnly(root, layers);
        for (int k = 0; k < speeds.Length; k++)
        {
            Check(broke[k] == glass.Length, $"glass: {broke[k]}/{glass.Length} panes break when the player comes close at {speeds[k]:F0} u/s");
            Check(early[k] == glass.Length, $"glass: {early[k]}/{glass.Length} break before the hull touches them, within knife reach ({BspReach:F0} u) plus a step's travel, at {speeds[k]:F0} u/s");
            Check(through[k] == glass.Length, $"glass: {through[k]}/{glass.Length} flown through at {speeds[k]:F0} u/s without losing speed");
        }

        // Switched off and on (SourceMapManager.ShowMap): still broken, until the player leaves the instance.
        root.SetActive(false);
        yield return Frames(0.2f);
        root.SetActive(true);
        yield return Frames(0.2f);
        int still = 0;
        foreach (var g in glass)
            if ((bool)UdonOf(g).GetProgramVariable("broken") && !g.solid.enabled) still++;
        Check(still == glass.Length, $"glass: {still}/{glass.Length} still broken after the map is switched off and on");
    }

    const float BspReach = 48f; // CS:S knife reach (BspMechanics.KnifeReach; Bsp isn't in the runtime assembly)

    /// <summary>
    /// The map's solid colliders on the Walkthrough layer, as SourceMovement's Hull Only does (only the hull collides
    /// with them), or back to `restore` (what this returned). Objects that also carry a trigger keep their layer.
    /// </summary>
    static int[] HullOnly(GameObject root, int[] restore)
    {
        var all = root.GetComponentsInChildren<Collider>(true);
        var layers = new int[all.Length];
        for (int i = 0; i < all.Length; i++)
        {
            layers[i] = all[i].gameObject.layer;
            bool trigger = false;
            foreach (var c in all[i].GetComponents<Collider>()) trigger |= c.isTrigger;
            if (restore != null) all[i].gameObject.layer = restore[i];
            else if (!trigger) all[i].gameObject.layer = 17;
        }
        return layers;
    }

    // ------------------------------------------------------------------ bhop blocks

    /// <summary>
    /// Bhop blocks (SourceMapBlocks), like CS:S: standing on a block sends the player back (name trigger + filtered
    /// teleport, or a func_door that sinks into a teleport); bouncing on it with jump held doesn't; with blocks off,
    /// standing is fine.
    /// </summary>
    IEnumerator BhopBlocks(List<SourceEntity> points)
    {
        var blocksObj = GameObject.Find("Bhop Blocks");
        if (blocksObj == null) { Log("   no bhop blocks in this map"); yield break; }
        var blocks = UdonOf(blocksObj.GetComponent<SourceMapBlocks>());
        var teleports = FindObjectsOfType<SourceMapTeleport>();
        // Spots on top of blocks: a name trigger over a filtered teleport (the name it sets is the filter), or a door.
        var spots = new List<KeyValuePair<Vector3, Vector3>>(); // stand point, where the player should end up
        var spotParts = new List<string>();
        var spotTops = new List<float>();
        var doorSpots = new List<KeyValuePair<Vector3, SourceMapDoor>>();
        foreach (var nt in FindObjectsOfType<SourceMapNameTrigger>())
        {
            if (spots.Count >= 6) break;
            var c = nt.GetComponent<Collider>();
            foreach (var t in teleports)
            {
                if (t.filterName == "" || System.Array.IndexOf(nt.names, t.filterName) < 0) continue;
                var tc = t.GetComponent<Collider>();
                if (tc == null || !tc.bounds.Intersects(c.bounds)) continue;
                // A spot inside both triggers, on the block (the middle of where they overlap).
                var both = new Bounds();
                both.SetMinMax(Vector3.Max(c.bounds.min, tc.bounds.min), Vector3.Min(c.bounds.max, tc.bounds.max));
                var top = new Vector3(both.center.x, both.max.y + 0.5f, both.center.z);
                if (!Physics.Raycast(top, Vector3.down, out var hit, 3f, Solid, QueryTriggerInteraction.Ignore)) continue;
                var body = hit.point + Vector3.up * 0.06f;
                if ((c.ClosestPoint(body) - body).sqrMagnitude > 1e-6f || (tc.ClosestPoint(body) - body).sqrMagnitude > 1e-6f) continue;
                spots.Add(new KeyValuePair<Vector3, Vector3>(hit.point, Relayed(t)));
                spotTops.Add(tc.bounds.max.y);
                spotParts.Add($"name trigger {c.bounds.min.y / U:F1}..{c.bounds.max.y / U:F1}, teleport {tc.bounds.min.y / U:F1}..{tc.bounds.max.y / U:F1} filter '{t.filterName}', floor {hit.point.y / U:F1}");
                break;
            }
        }
        foreach (var d in FindObjectsOfType<SourceMapDoor>())
        {
            if (doorSpots.Count >= 6) break;
            if (!d.dropThrough) continue;
            var b = d.solid.bounds;
            if (Physics.Raycast(new Vector3(b.center.x, b.max.y + 0.5f, b.center.z), Vector3.down, out var hit, 1f, Solid, QueryTriggerInteraction.Ignore))
                doorSpots.Add(new KeyValuePair<Vector3, SourceMapDoor>(hit.point, d));
        }
        Log($"   bhop blocks: testing {spots.Count} name-trigger blocks, {doorSpots.Count} door blocks");

        // Stand: on each block for 1 s -> sent away (to the filtered teleport's destination, or the door's pit).
        int sent = 0, total = 0;
        var notes = new List<string>();
        foreach (var spot in spots)
        {
            total++;
            yield return Teleport(spot.Key + Vector3.up * 0.05f, Quaternion.identity);
            bool reached = false;
            for (int i = 0; i < frameRate && !reached; i++)
            {
                yield return null;
                Vector3 d = player.GetPosition() - spot.Value;
                reached = new Vector2(d.x, d.z).magnitude < 0.5f && Mathf.Abs(d.y) < 1.5f;
            }
            int k = spots.IndexOf(spot);
            reached |= (player.GetPosition() - spot.Key).magnitude > 2f; // or sent back by another teleport over it
            if (reached) sent++; else notes.Add($"stayed on block at {spot.Key / U:F0} (player at {player.GetPosition() / U:F0}, name '{blocks.GetProgramVariable("activator")}', {spotParts[k]})");
        }
        foreach (var spot in doorSpots)
        {
            total++;
            yield return Teleport(spot.Key + Vector3.up * 0.05f, Quaternion.identity);
            Vector3 start = player.GetPosition();
            yield return Frames(1.5f);
            bool gone = (player.GetPosition() - start).magnitude > 1f; // sank, fell into the teleport and went to its destination
            var du = UdonOf(spot.Value);
            var ccc = FindObjectOfType<CharacterController>();
            var box = spot.Value.GetComponent<BoxCollider>();
            bool overlaps = Physics.ComputePenetration(ccc, ccc.transform.position, ccc.transform.rotation, box, box.transform.position, box.transform.rotation, out _, out _);
            if (gone) sent++; else notes.Add($"stayed on door at {spot.Key / U:F0}: overlaps touch box {overlaps}, blocks set {du.GetProgramVariable("blocks") != null}, box trigger {box.isTrigger} layer {box.gameObject.layer}, state {du.GetProgramVariable("state")}, t {du.GetProgramVariable("t")}, door at {spot.Value.transform.position / U:F0}, " +
                $"touch box {spot.Value.GetComponent<BoxCollider>().bounds.min / U:F0}..{spot.Value.GetComponent<BoxCollider>().bounds.max / U:F0}, cc bottom {FindObjectOfType<CharacterController>().bounds.min.y / U:F1}");
        }
        Check(total > 0 && sent == total, $"bhop blocks: standing 1 s on a block sends the player back ({sent}/{total})");

        // Bhop: bounce on each name-trigger block for 2 s holding jump (auto bhop) -> never sent away. Only blocks whose
        // teleport reaches at most 20 units over them (8 more here: triggers are raised): a jump is 23 units up 0.09 s
        // after leaving, so it clears them. Taller ones (bhop_arcane_v1 has 65 units) send bouncers back in CS:S too.
        int kept = 0, bounced = 0;
        foreach (var spot in spots)
        {
            if (spotTops[spots.IndexOf(spot)] - spot.Key.y > (20f + 8f) * U) continue;
            bounced++;
            yield return Teleport(spot.Key + Vector3.up * 0.6f, Quaternion.identity);
            Keys(Key.Space);
            bool stayed = true;
            var heights = new StringBuilder();
            for (int i = 0; i < frameRate * 2 && stayed; i++)
            {
                yield return null;
                stayed = (player.GetPosition() - spot.Key).magnitude < 2f; // teleports go far; a bounce can drift a little
                if (i % 3 == 0) heights.Append($" {(player.GetPosition().y - spot.Key.y) / U:F0}");
            }
            Keys();
            int k = spots.IndexOf(spot);
            if (stayed) kept++; else notes.Add($"bounce sent away from {spot.Key / U:F0} to {player.GetPosition() / U:F0} ({spotParts[k]}; height over the block every 3 frames:{heights})");
            yield return Frames(0.7f); // land
        }
        Check(kept == bounced, $"bhop blocks: bouncing on a block with jump held is safe ({kept}/{bounced})");

        // Off: standing stays.
        blocks.SetProgramVariable("on", false);
        int stood = 0;
        foreach (var spot in spots)
        {
            yield return Teleport(spot.Key + Vector3.up * 0.05f, Quaternion.identity);
            yield return Frames(1f);
            if ((player.GetPosition() - spot.Key).magnitude < 2f) stood++;
        }
        foreach (var spot in doorSpots)
        {
            yield return Teleport(spot.Key + Vector3.up * 0.05f, Quaternion.identity);
            yield return Frames(1.5f);
            if ((player.GetPosition() - spot.Key).magnitude < 2f) stood++;
        }
        blocks.SetProgramVariable("on", true);
        Check(stood == spots.Count + doorSpots.Count, $"bhop blocks off: the player can stand on blocks ({stood}/{spots.Count + doorSpots.Count})");
        foreach (var n in notes) Log("   " + n);
    }

    // ------------------------------------------------------------------ bhop and surf runs

    const int Solid = (1 << 0) | (1 << 17); // world collision: Default, or Walkthrough while SourceMovement has it as hull-only
    int stalls, wallHits;
    Vector3 lastPos; // the player's last position before a map teleport moved them
    readonly List<string> stallLog = new List<string>();

    IEnumerator Runs(List<SourceEntity> points)
    {
        // Bhop: from up to 12 destinations/spawns spread over the map, the direction with the longest open floor.
        int runs = 0;
        float distance = 0f, topSpeed = 0f;
        stalls = wallHits = 0;
        stallLog.Clear();
        int step = Mathf.Max(1, points.Count / 12);
        for (int k = 0; k < points.Count && runs < 12; k += step)
        {
            Vector3 at = points[k].transform.position;
            yield return Teleport(at, Quaternion.identity);
            yield return Frames(1f);
            if (!OnGround()) continue;
            at = player.GetPosition();
            float best = 0f, bestYaw = 0f;
            for (int d = 0; d < 16; d++)
            {
                float len = OpenFloor(at, d * 22.5f);
                if (len > best) { best = len; bestYaw = d * 22.5f; }
            }
            if (best < 400f) continue; // no room for a few hops
            runs++;
            yield return Teleport(at, Quaternion.Euler(0, bestYaw, 0));
            yield return Frames(0.3f);
            Vector3 start = player.GetPosition();
            Vector3 fwd = Quaternion.Euler(0, bestYaw, 0) * Vector3.forward;
            // Like a player: walk 0.5 s with W for speed, then hold jump and strafe in the air with A/D while turning
            // 150 degrees/s left/right each half second, as PlayTestRunner's air strafe (W while strafing in the air caps
            // the gain, as in Source).
            float t = 0f, top = 0f, prevVy = 0f, swing = 0f;
            int jumps = 0;
            yield return Watch(points[k].name + " bhop", () =>
            {
                float dt = Time.deltaTime;
                t += dt;
                float vy = ((Vector3)movement.GetProgramVariable("velocity")).y;
                if (vy > 150f && prevVy <= 150f) jumps++;
                prevVy = vy;
                int half = Mathf.FloorToInt((t - 0.5f) / 0.5f);
                bool left = half % 2 == 0;
                if (t < 0.5f) Keys(Key.W);
                else
                {
                    Keys(Key.Space, left ? Key.A : Key.D);
                    swing += (left ? -150f : 150f) * (half == 0 ? 0.5f : 1f) * dt; // first half turn, so the zig-zag stays centred
                }
                SetYaw(bestYaw + swing);
                top = Mathf.Max(top, Speed());
                return Vector3.Dot(player.GetPosition() - start, fwd) / U < best - 96f && t < 6f;
            });
            Keys();
            float went = Vector3.Dot(lastPos - start, fwd) / U;
            Log($"   bhop from {points[k].name}: open floor {best:F0} u at yaw {bestYaw}, {jumps} jumps in {t:F1} s, top speed {top:F0} u/s, " +
                $"went {went:F0} u" + (went < best / 3f ? $", ended at {lastPos / U:F0}, ahead:{Ahead(lastPos, fwd)}" : ""));
            distance += went;
            topSpeed = Mathf.Max(topSpeed, top);
        }
        Check(stalls == 0, $"bhop: {runs} runs (W, then jump + strafing), {distance:F0} u along open floors, top speed {topSpeed:F0} u/s, {stalls} stalls, {wallHits} wall hits");
        foreach (var s in stallLog) Log("   stall: " + s);

        // Surf: the largest slopes too steep to stand on (Source: floor normal y >= 0.7) facing up, 256 u apart.
        stalls = wallHits = 0;
        stallLog.Clear();
        int surfs = 0, grounded = 0;
        float surfDist = 0f;
        foreach (var r in Ramps(6))
        {
            Vector3 n = r.normal;
            Vector3 along = Vector3.Cross(Vector3.up, n).normalized; // horizontal, along the ramp
            Vector3 into = -new Vector3(n.x, 0, n.z).normalized;
            foreach (float sign in new[] { 1f, -1f })
            {
                Vector3 dir = along * sign;
                // Feet so the hull's lowest corner (23 u out, diagonally) clears the ramp by about 2 u.
                Vector3 feet = r.center + n * 2f * U + Vector3.up * (23f * U * (new Vector2(n.x, n.z).magnitude / n.y));
                if (Physics.CheckBox(feet + Vector3.up * 37f * U, new Vector3(16f, 36f, 16f) * U, Quaternion.identity, Solid, QueryTriggerInteraction.Ignore))
                    continue; // no room to start here
                surfs++;
                var rot = Quaternion.LookRotation(dir);
                yield return Teleport(feet, rot);
                yield return Frames(0.2f); // the teleport settles first (as PlayTestRunner's surf test)
                movement.SetProgramVariable("velocity", dir * 800f);
                bool right = Vector3.Dot(rot * Vector3.right, into) > 0f;
                Vector3 start = player.GetPosition();
                float t = 0f;
                yield return Watch($"ramp at {r.center / U:F0} normal {n:F2}", () =>
                {
                    t += Time.deltaTime;
                    Keys(right ? Key.D : Key.A);
                    SetYaw(rot.eulerAngles.y);
                    if (OnGround()) grounded++;
                    return t < 0.6f;
                });
                Keys();
                float went = Vector3.Dot(lastPos - start, dir) / U;
                Log($"   surf at {r.center / U:F0}, normal y {n.y:F2}: went {went:F0} u in {t:F1} s, end speed {Speed():F0} u/s, " +
                    $"height change {(player.GetPosition().y - start.y) / U:F0} u, grounded frames {grounded}");
                surfDist += went;
                break;
            }
        }
        Check(stalls == 0, $"surf: {surfs} slopes surfed (holding into the ramp), {surfDist:F0} u along them, {stalls} stalls, {wallHits} wall hits, {grounded} grounded frames");
        foreach (var s in stallLog) Log("   stall: " + s);
    }

    /// <summary>
    /// Runs `each` every frame until it returns false, counting stalls: the horizontal speed falls by more than
    /// 100 u/s in one frame although nothing facing against the motion is right ahead. Stops when a map teleport moves the player.
    /// </summary>
    IEnumerator Watch(string what, System.Func<bool> each)
    {
        Vector3 prevVel = (Vector3)movement.GetProgramVariable("velocity");
        Vector3 prevPos = player.GetPosition();
        for (int i = 0; i < frameRate * 8; i++)
        {
            yield return null;
            bool go = each();
            Vector3 vel = (Vector3)movement.GetProgramVariable("velocity");
            if ((player.GetPosition() - prevPos).magnitude > (prevVel.magnitude * Time.deltaTime + 64f) * U) yield break; // teleported
            lastPos = player.GetPosition();
            float before = Flat(prevVel).magnitude, now = Flat(vel).magnitude;
            if (before - now > 100f && InMechanics(prevPos)) { } // pushes and boosters change speed on purpose
            else if (before - now > 100f && WallAhead(prevPos, Flat(prevVel).normalized, before)) wallHits++;
            else if (before - now > 100f)
            {
                stalls++;
                if (stallLog.Count < 10)
                    stallLog.Add($"{what}: at {player.GetPosition() / U:F0} speed {before:F0} -> {now:F0}, velocity {prevVel:F0} -> {vel:F0}, grounded {OnGround()}, ahead:{Ahead(prevPos, Flat(prevVel).normalized)}");
            }
            prevVel = vel;
            prevPos = player.GetPosition();
            if (!go) yield break;
        }
    }

    /// <summary>What the hull would hit within 64 u (distance, normal, collider), for the stall log.</summary>
    string Ahead(Vector3 feet, Vector3 dir)
    {
        var sb = new StringBuilder();
        foreach (var hit in Physics.BoxCastAll(feet + Vector3.up * 37f * U, new Vector3(15f, 34f, 15f) * U, dir, Quaternion.identity, 64f * U, Solid, QueryTriggerInteraction.Ignore))
            sb.Append($" {hit.distance / U:F1} u normal {hit.normal:F2} point {hit.point / U:F0} {hit.collider.name};");
        return sb.Length == 0 ? " nothing" : sb.ToString();
    }

    /// <summary>A surface facing against the motion within two frames of travel (+ 16 u) in front of the 32 x 72 hull.</summary>
    bool WallAhead(Vector3 feet, Vector3 dir, float speed)
    {
        if (dir == Vector3.zero) return true;
        float reach = (speed * Time.deltaTime * 2f + 16f) * U;
        foreach (var hit in Physics.BoxCastAll(feet + Vector3.up * 37f * U, new Vector3(15f, 34f, 15f) * U, dir, Quaternion.identity, reach, Solid, QueryTriggerInteraction.Ignore))
            if (Vector3.Dot(hit.normal, dir) < -0.2f) return true; // a wall or slope facing against the motion
        return false;
    }

    /// <summary>
    /// How far (u) the hull can go from `feet` towards `yaw` with floor under it at the same height (±18 u), allowing
    /// gaps up to 96 u (bhop blocks).
    /// </summary>
    static float OpenFloor(Vector3 feet, float yaw)
    {
        Vector3 dir = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
        float lastFloor = 0f;
        for (float d = 32f; d <= 2000f; d += 32f)
        {
            Vector3 p = feet + dir * d * U;
            // Hull overlap test per step (a BoxCast misses a mesh collider it starts touching): from 20 u up, over steps.
            if (Physics.CheckBox(p + Vector3.up * 46f * U, new Vector3(16f, 26f, 16f) * U, Quaternion.identity, Solid, QueryTriggerInteraction.Ignore))
                return lastFloor;
            if (Physics.Raycast(p + Vector3.up * 18f * U, Vector3.down, out var down, 36f * U, Solid, QueryTriggerInteraction.Ignore) && down.normal.y >= 0.7f)
                lastFloor = d;
            else if (d - lastFloor > 96f)
                return lastFloor;
        }
        return lastFloor;
    }

    struct Ramp { public Vector3 center, normal; public float area; }

    /// <summary>The largest collision triangles (over 64 x 64 u) too steep to stand on but facing up (0.1 &lt; normal y &lt; 0.7), 256 u apart.</summary>
    static List<Ramp> Ramps(int count)
    {
        var all = new List<Ramp>();
        foreach (var mc in FindObjectsOfType<MeshCollider>())
        {
            if (mc.isTrigger || mc.sharedMesh == null || (mc.gameObject.layer != 0 && mc.gameObject.layer != 17)) continue;
            var v = mc.sharedMesh.vertices;
            var tri = mc.sharedMesh.triangles;
            var m = mc.transform.localToWorldMatrix;
            for (int i = 0; i < tri.Length; i += 3)
            {
                Vector3 a = m.MultiplyPoint3x4(v[tri[i]]), b = m.MultiplyPoint3x4(v[tri[i + 1]]), c = m.MultiplyPoint3x4(v[tri[i + 2]]);
                Vector3 cross = Vector3.Cross(b - a, c - a);
                float area = cross.magnitude * 0.5f / (U * U);
                if (area < 64f * 64f) continue;
                Vector3 n = cross.normalized;
                if (n.y < 0f) n = -n; // either winding: take the upward face
                if (n.y <= 0.1f || n.y >= 0.7f) continue;
                all.Add(new Ramp { center = (a + b + c) / 3f, normal = n, area = area });
            }
        }
        all.Sort((x, y) => y.area.CompareTo(x.area));
        Log($"   {all.Count} collision triangles steeper than 45.6 degrees, facing up, over 64x64 u");
        var picked = new List<Ramp>();
        foreach (var r in all)
        {
            if (picked.Count >= count) break;
            if (picked.TrueForAll(p => (p.center - r.center).magnitude > 256f * U)) picked.Add(r);
        }
        return picked;
    }

    void Keys(params Key[] keys) { InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys)); }
    void SetYaw(float yaw) { if (playerBody != null) playerBody.rotation = Quaternion.Euler(0, yaw, 0); }
    float Speed() { return Flat((Vector3)movement.GetProgramVariable("velocity")).magnitude; }
    static Vector3 Flat(Vector3 v) { return new Vector3(v.x, 0, v.z); }

    /// <summary>
    /// Where to drop the player into a teleport: on a 5x5 grid over each brush, the spots with room above them whose
    /// ground (cast with the 32 x 32 unit hull, which rests on the rim of dips narrower than itself) is lowest, best
    /// first. Empty if the ground is above the trigger's original top everywhere (the importer raised it by 8 units).
    /// </summary>
    static List<Vector3> DropPoints(SourceMapTeleport t, int count)
    {
        var found = new List<KeyValuePair<float, Vector3>>();
        foreach (var col in t.GetComponents<MeshCollider>())
        {
            if (!col.enabled) continue;
            Bounds b = col.bounds;
            for (int i = 0; i < 25; i++)
            {
                float x = Mathf.Lerp(b.min.x, b.max.x, (i % 5 + 0.5f) / 5f), z = Mathf.Lerp(b.min.z, b.max.z, (i / 5 + 0.5f) / 5f);
                var above = new Vector3(x, b.max.y + 0.3f, z);
                var inside = new Vector3(x, b.center.y, z);
                if ((col.ClosestPoint(inside) - inside).sqrMagnitude > 1e-6f) continue; // not over this brush
                if (Physics.Raycast(above, Vector3.up, 1.5f, Solid, QueryTriggerInteraction.Ignore)) continue; // no headroom
                if (Physics.CheckBox(above + new Vector3(0, 37, 0) * U, new Vector3(17, 37, 17) * U, Quaternion.identity, Solid, QueryTriggerInteraction.Ignore)) continue; // hull in a wall
                if (InsideSolid(above)) continue; // in a solid block: no face to overlap, so CheckBox misses it
                if (InPush(above)) continue;
                float depth = b.size.y + 0.3f;
                if (Physics.BoxCast(above, new Vector3(16, 0.5f, 16) * U, Vector3.down, out var hit, Quaternion.identity, depth, Solid, QueryTriggerInteraction.Ignore))
                    depth = hit.distance;
                if (depth > 0.3f + 9 * U) found.Add(new KeyValuePair<float, Vector3>(depth, above));
            }
        }
        found.Sort((a, b) => b.Key.CompareTo(a.Key));
        var result = new List<Vector3>();
        foreach (var f in found) if (result.Count < count) result.Add(f.Value);
        return result;
    }

    /// <summary>
    /// Where the teleport really puts the player: a destination inside another working teleport forwards them (in
    /// Source too, e.g. bhop_arcane_v1's *_stop relays), up to 3 hops (as SampleWorldTestRunner).
    /// </summary>
    static Vector3 Relayed(SourceMapTeleport t)
    {
        var all = FindObjectsOfType<SourceMapTeleport>();
        Vector3 dest = t.destination.position;
        for (int hop = 0; hop < 3; hop++)
        {
            SourceMapTeleport next = null;
            foreach (var other in all)
                foreach (var c in other.GetComponents<MeshCollider>())
                    for (float h = 0.1f; h < 1.7f; h += 0.4f) // anywhere the player's body would be
                        if (c.enabled && (c.ClosestPoint(dest + Vector3.up * h) - (dest + Vector3.up * h)).sqrMagnitude < 1e-6f) next = other;
            if (next == null || next == t) break;
            dest = next.destination.position;
        }
        return dest;
    }

    /// <summary>Inside a push or booster trigger (trigger_push, basevelocity / gravity boosters), at body height.</summary>
    static bool InMechanics(Vector3 feet)
    {
        foreach (var e in FindObjectsOfType<SourceEntity>())
        {
            if (e.className != "trigger_push" && e.GetComponent("SourceBoostTrigger") == null) continue;
            foreach (var c in e.GetComponents<Collider>())
                for (float h = 0.1f; h < 1.4f; h += 0.4f)
                    if (c.enabled && (c.ClosestPoint(feet + Vector3.up * h) - (feet + Vector3.up * h)).sqrMagnitude < 1e-6f) return true;
        }
        return false;
    }

    /// <summary>Inside a trigger_push volume: the push (e.g. an updraft) decides where the player goes, not the fall.</summary>
    static bool InPush(Vector3 p)
    {
        foreach (var e in FindObjectsOfType<SourceEntity>())
            if (e.className == "trigger_push")
                foreach (var c in e.GetComponents<Collider>())
                    if (c.enabled && (c.ClosestPoint(p) - p).sqrMagnitude < 1e-6f) return true;
        return false;
    }

    /// <summary>
    /// Inside a solid block. Collision is a hollow mesh, so overlap tests only see its faces; from inside, every face
    /// is seen from the back: the first face above is hit with back faces on but not without.
    /// </summary>
    static bool InsideSolid(Vector3 p)
    {
        const int SolidLayer = (1 << 0) | (1 << 17); // Default, or Walkthrough (hull-only)
        bool before = Physics.queriesHitBackfaces;
        Physics.queriesHitBackfaces = true;
        bool any = Physics.Raycast(p, Vector3.up, out var first, 2000f, SolidLayer, QueryTriggerInteraction.Ignore);
        Physics.queriesHitBackfaces = false;
        bool front = Physics.Raycast(p, Vector3.up, out var outside, 2000f, SolidLayer, QueryTriggerInteraction.Ignore);
        Physics.queriesHitBackfaces = before;
        return any && (!front || first.distance < outside.distance - 0.001f);
    }

    IEnumerator Teleport(Vector3 position, Quaternion rotation)
    {
        movement.SetProgramVariable("__0_position__param", position);
        movement.SetProgramVariable("__0_rotation__param", rotation);
        movement.SetProgramVariable("__0_keepVelocity__param", false);
        movement.SendCustomEvent("__0_TeleportPlayer");
        yield return null;
        yield return null;
    }

    IEnumerator Frames(float seconds)
    {
        int n = Mathf.RoundToInt(seconds * frameRate);
        for (int i = 0; i < n; i++) yield return null;
    }

    bool OnGround() { return (bool)movement.GetProgramVariable("onGround"); }

    /// <summary>The Udon program behind a U# component (an object can hold several, e.g. a marker plus a teleport).</summary>
    static UdonBehaviour UdonOf(Component proxy)
    {
        string type = proxy.GetType().Name;
        foreach (var u in proxy.GetComponents<UdonBehaviour>())
            if (u.programSource != null && u.programSource.name == type) return u;
        return null;
    }

    static UdonBehaviour FindUdon(string objectName)
    {
        foreach (var udon in FindObjectsOfType<UdonBehaviour>())
            if (udon.gameObject.name == objectName) return udon;
        return null;
    }

    static UdonBehaviour Loaded(UdonBehaviour udon)
    {
        try { return udon != null && udon.GetProgramVariable("active") != null ? udon : null; }
        catch (System.NullReferenceException) { return null; }
    }

    void Update()
    {
        if (!finished && Time.realtimeSinceStartup > 1500f) { Check(false, "watchdog: tests did not finish within 25 minutes"); Finish(); }
    }

    void Check(bool ok, string what)
    {
        string line = (ok ? "PASS  " : "FAIL  ") + what;
        if (!ok) failures++;
        report.Add(line);
        Log(line);
    }

    static void Log(string s) { Debug.Log("[SMTEST] " + s); }

    void Finish()
    {
        finished = true;
        var sb = new StringBuilder();
        foreach (string line in report) sb.AppendLine(line);
        sb.AppendLine(failures == 0 ? "ALL PASSED" : failures + " FAILED");
        Log("\n" + sb);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(failures == 0 ? 0 : 1);
#endif
    }
}
