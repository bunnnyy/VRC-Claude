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
            bool ok = OnGround() && player.GetPosition().y > floorY;
            if (ok) stood++;
            else notStanding.Add($"{p.name} (grounded {OnGround()}, y {player.GetPosition().y:F2} m)");
        }
        Check(stood == points.Count, $"stand: player lands and stays on a floor at {stood}/{points.Count} destinations/spawns");
        foreach (var s in notStanding) Log("   not standing: " + s);

        // teleports: start in the middle of each working teleport's trigger, expect to reach its destination.
        int arrived = 0, total = 0;
        var missed = new List<string>();
        foreach (var t in FindObjectsOfType<SourceMapTeleport>())
        {
            var col = t.GetComponent<Collider>();
            if (col == null || !col.enabled) continue;
            total++;
            Bounds b = col.bounds;
            foreach (var c in t.GetComponents<Collider>()) b.Encapsulate(c.bounds);
            Vector3 dest = t.destination.position;
            yield return Teleport(new Vector3(b.center.x, b.min.y + 0.05f, b.center.z), Quaternion.identity);
            bool reached = false;
            for (int i = 0; i < frameRate && !reached; i++)
            {
                yield return null;
                Vector3 d = player.GetPosition() - dest;
                reached = new Vector2(d.x, d.z).magnitude < 0.5f && Mathf.Abs(d.y) < 1.5f;
            }
            if (reached) arrived++;
            else missed.Add($"{t.name}: player at {player.GetPosition():F1}, destination {dest:F1}");
        }
        Check(total > 0 && arrived == total, $"teleports: {arrived}/{total} working trigger_teleports send the player to their destination");
        foreach (var s in missed) Log("   missed: " + s);
        Finish();
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
