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
/// SM_ONLY=runs (environment) skips stand and teleports.
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
        bool onlyRuns = System.Environment.GetEnvironmentVariable("SM_ONLY") == "runs";
        if (onlyRuns) { yield return Runs(points); Finish(); yield break; }
        int stood = 0;
        var notStanding = new List<string>();
        foreach (var p in points)
        {
            yield return Teleport(p.transform.position, p.transform.rotation);
            yield return Frames(2f);
            // Grounded, or still at the destination: some maps put a teleport back to the same destination on the
            // floor below it (bhop_japan's tele_dest_33/34 over pillars), so in Source too you bounce until you move.
            bool ok = player.GetPosition().y > floorY && (OnGround() || Vector3.Distance(player.GetPosition(), p.transform.position) < 2f);
            if (ok) stood++;
            else notStanding.Add($"{p.name} (grounded {OnGround()}, y {player.GetPosition().y:F2} m)");
        }
        Check(stood == points.Count, $"stand: player lands and stays on a floor at {stood}/{points.Count} destinations/spawns");
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
            // (ground lowest under it) gets there. Some spots are covered by other volumes, e.g. bhop_arcane_v1's
            // out-of-bounds teleport 423175 shares its box with an updraft (trigger_push up at 1250 u/s).
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
        yield return Runs(points);
        Finish();
    }

    // ------------------------------------------------------------------ bhop and surf runs

    const int Solid = 1 << 0; // world collision is on Default
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
    /// 100 u/s in one frame although no vertical wall is right ahead. Stops when a map teleport moves the player.
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
            if (before - now > 100f && WallAhead(prevPos, Flat(prevVel).normalized, before)) wallHits++;
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

    /// <summary>A vertical wall within two frames of travel (+ 16 u) in front of the 32 x 72 hull.</summary>
    bool WallAhead(Vector3 feet, Vector3 dir, float speed)
    {
        if (dir == Vector3.zero) return true;
        float reach = (speed * Time.deltaTime * 2f + 16f) * U;
        foreach (var hit in Physics.BoxCastAll(feet + Vector3.up * 37f * U, new Vector3(15f, 34f, 15f) * U, dir, Quaternion.identity, reach, Solid, QueryTriggerInteraction.Ignore))
            if (Mathf.Abs(hit.normal.y) < 0.3f) return true;
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
            if (mc.isTrigger || mc.sharedMesh == null || mc.gameObject.layer != 0) continue;
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

    /// <summary>
    /// Inside a solid block. Collision is a hollow mesh, so overlap tests only see its faces; from inside, every face
    /// is seen from the back: the first face above is hit with back faces on but not without.
    /// </summary>
    static bool InsideSolid(Vector3 p)
    {
        const int SolidLayer = 1 << 0;
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
