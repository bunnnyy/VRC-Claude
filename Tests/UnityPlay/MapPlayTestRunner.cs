using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using VRC.SDK3.ClientSim;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// Play mode tests on an imported Source map with ClientSim and SourceMovement (real PhysX on the generated
/// collision). Started by PlayTestBootstrap.RunMap. Results are logged with a [SMTEST] prefix.
///   stand     - at every teleport destination and a few spawns the player lands on a floor and stays there
///   teleports - falling into every working trigger_teleport brings the player to its destination
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
            Vector3? drop = DropPoint(t);
            if (drop == null) { unreachable.Add(t.name + " " + t.GetComponent<SourceEntity>().GetValue("hammerid")); total--; continue; }
            Vector3 start = drop.Value;
            Vector3 dest = t.destination.position;
            yield return Teleport(points[0].transform.position, Quaternion.identity);
            yield return Frames(0.3f);
            yield return Teleport(start, Quaternion.identity);
            bool reached = false;
            var trace = new StringBuilder();
            for (int i = 0; i < frameRate && !reached; i++)
            {
                yield return null;
                if (i % 10 == 0) trace.Append($" f{i} {player.GetPosition():F1}");
                Vector3 d = player.GetPosition() - dest;
                reached = new Vector2(d.x, d.z).magnitude < 0.5f && Mathf.Abs(d.y) < 1.5f;
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
        Finish();
    }

    /// <summary>
    /// Where to drop the player into a teleport: on a 5x5 grid over each brush, the spot with room above it whose
    /// ground (cast with the 32 x 32 unit hull, which rests on the rim of dips narrower than itself) is lowest.
    /// Null if the ground is above the trigger's original top everywhere (the importer raised it by 8 units).
    /// </summary>
    static Vector3? DropPoint(SourceMapTeleport t)
    {
        const int Solid = 1 << 0; // world collision is on Default
        Vector3? best = null;
        float bestDepth = 0.3f + 9 * U;
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
                float depth = b.size.y + 0.3f;
                if (Physics.BoxCast(above, new Vector3(16, 0.5f, 16) * U, Vector3.down, out var hit, Quaternion.identity, depth, Solid, QueryTriggerInteraction.Ignore))
                    depth = hit.distance;
                if (depth > bestDepth) { bestDepth = depth; best = above; }
            }
        }
        return best;
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
        if (!finished && Time.realtimeSinceStartup > 900f) { Check(false, "watchdog: tests did not finish within 15 minutes"); Finish(); }
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
