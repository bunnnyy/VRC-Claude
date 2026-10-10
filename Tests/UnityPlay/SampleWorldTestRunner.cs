using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using VRC.SDK3.ClientSim;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// Play mode test of the sample world (Assets/SourceMapsSample.unity, every test map imported with visuals, collision,
/// markers and timer zones, plus the lobby) with ClientSim and SourceMovement. For each map: the owner forces it,
/// the player arrives at the spawn and stands, up to 15 working teleports send the player to their destination, a run
/// through the start and end zones lands on that map's saved board, solid props have colliders and no tool texture is
/// drawn; plus a screenshot from the spawn. Started by PlayTestBootstrap.RunSample; results with a [SMTEST] prefix.
/// </summary>
public class SampleWorldTestRunner : MonoBehaviour
{
    const float U = 0.01905f;
    readonly List<string> report = new List<string>();
    int failures;
    bool finished;
    UdonBehaviour manager, movement, timer;
    VRCPlayerApi player;

    IEnumerator Start()
    {
        Time.captureDeltaTime = 1f / 90f; // fixed 90 fps like the other play tests: time-based waits stay meaningful
        for (int i = 0; i < 900 && (player == null || manager == null || movement == null); i++)
        {
            yield return null;
            player = Networking.LocalPlayer;
            manager = Loaded(Find("SourceMapManager"), "round");
            movement = Loaded(Find("SourceMovement"), "active");
        }
        if (player == null || manager == null || movement == null) { Check(false, "setup: player, SourceMapManager and SourceMovement loaded"); Finish(); yield break; }
        timer = Loaded(Find("RunTimer"), "running");
        for (int i = 0; i < 30; i++) yield return null;
        foreach (var menu in Resources.FindObjectsOfTypeAll<ClientSimMenu>())
            if (menu.gameObject.scene.IsValid()) { menu.WarningAccepted(); menu.CloseMenu(); }
        yield return Seconds(1f);
        movement.SetProgramVariable("autoBhop", false);

        var maps = (Component[])manager.GetProgramVariable("maps");
        Log($"{maps.Length} maps in the rotation");
        for (int m = 0; m < maps.Length; m++) yield return TestMap(m, maps[m]);
        Finish();
    }

    IEnumerator TestMap(int index, Component info)
    {
        var infoUdon = info.GetComponent<UdonBehaviour>();
        string name = (string)infoUdon.GetProgramVariable("mapName");
        var root = info.gameObject;

        // Force this map (owner controls), like pressing "Force a map" and its slot on the board.
        Press("forcelobby", -1);
        yield return Seconds(0.3f);
        int slot = System.Array.IndexOf((int[])manager.GetProgramVariable("candidates"), index);
        Check(slot >= 0, $"{name}: offered on the vote board");
        if (slot < 0) yield break;
        Press("force", -1);
        Press("map", slot);
        yield return Seconds(2.5f);
        var spawn = (Transform)infoUdon.GetProgramVariable("spawn");
        Check(root.activeSelf && (bool)movement.GetProgramVariable("onGround") && player.GetPosition().y > spawn.position.y - 10f,
            $"{name}: forced, player arrives and stands near the spawn (at {player.GetPosition():F1}, spawn {spawn.position:F1})");

        // Visuals: something is drawn, no tool texture is drawn, solid props have colliders.
        var visuals = root.transform.Find("Visuals");
        int renderers = 0, tools = 0, propColliders = 0, staticProps = 0;
        if (visuals != null)
        {
            foreach (var r in visuals.GetComponentsInChildren<MeshRenderer>(true))
            {
                renderers++;
                foreach (var mat in r.sharedMaterials)
                {
                    string n = mat != null ? mat.name.ToLower() : "";
                    n = n.Substring(n.LastIndexOf('/') + 1);
                    if (n.StartsWith("tools") && n != "toolsblack" && !n.StartsWith("toolsskybox")) tools++;
                }
            }
            var props = visuals.Find("[StaticProps]");
            if (props != null)
            {
                propColliders = props.GetComponentsInChildren<MeshCollider>(true).Length;
                // Only props with a model count: stock HL2/CS:S models are missing without a CS:S folder.
                foreach (Transform p in props) if (p.GetComponentInChildren<Renderer>(true) != null) staticProps++;
            }
        }
        Check(renderers > 0 && tools == 0 && (staticProps == 0 || propColliders > 0),
            $"{name}: visuals imported ({renderers} renderers, {tools} tool surfaces drawn, {staticProps} static props with a model, {propColliders} prop colliders)");
        // Source lighting: props drawn with SourceMaps/Prop (vertex light); door blocks move their visible model.
        int litProps = 0, propRenderers = 0;
        var propGroup = visuals != null ? visuals.Find("[StaticProps]") : null;
        if (propGroup != null)
            foreach (var r in propGroup.GetComponentsInChildren<Renderer>(true))
            {
                propRenderers++;
                // (Material only: in play mode static batching merges the meshes, so their vertex colours aren't per prop.)
                if (System.Array.Exists(r.sharedMaterials, m => m != null && m.shader.name == "SourceMaps/Prop")) litProps++;
            }
        var doors = root.GetComponentsInChildren<SourceMapDoor>(true);
        int linked = System.Array.FindAll(doors, d => d.visuals != null).Length;
        Check((propRenderers == 0 || litProps > 0) && linked == doors.Length,
            $"{name}: Source lighting on {litProps}/{propRenderers} prop renderers; {linked}/{doors.Length} door blocks move their model");
        yield return Shot(name, spawn);

        // Teleports: up to 15, spread over the map.
        var teleports = root.GetComponentsInChildren<SourceMapTeleport>(true);
        int tested = 0, arrived = 0;
        var missed = new List<string>();
        for (int i = 0; i < teleports.Length && tested < 15; i += Mathf.Max(1, teleports.Length / 15))
        {
            var t = teleports[i];
            if (t.filterName != "") continue; // bhop block teleports (MapPlayTestRunner's blocks test)
            var drops = DropPoints(t, 3);
            if (drops.Count == 0) continue;
            tested++;
            // Where the player really ends up: a destination inside another working teleport forwards them (in
            // Source too, e.g. bhop_arcane_v1's *_stop relay destinations), so follow up to 3 hops.
            Vector3 dest = t.destination.position;
            for (int hop = 0; hop < 3; hop++)
            {
                SourceMapTeleport next = null;
                foreach (var other in teleports)
                    foreach (var c in other.GetComponents<MeshCollider>())
                        for (float h = 0.1f; h < 1.7f; h += 0.4f) // anywhere the player's body would be
                            if (c.enabled && (c.ClosestPoint(dest + Vector3.up * h) - (dest + Vector3.up * h)).sqrMagnitude < 1e-6f) next = other;
                if (next == null || next == t) break;
                dest = next.destination.position;
            }
            bool reached = false;
            foreach (var drop in drops) // the trigger works if dropping in at one of its 3 best spots gets there
            {
                Teleport(spawn.position);
                yield return Seconds(0.3f);
                Teleport(drop);
                for (float until = Time.time + 2f; Time.time < until && !reached;)
                {
                    yield return null;
                    Vector3 d = player.GetPosition() - dest;
                    reached = new Vector2(d.x, d.z).magnitude < 0.5f && Mathf.Abs(d.y) < 1.5f;
                }
                if (reached) break;
            }
            if (reached) arrived++; else missed.Add($"{t.name} -> {dest:F1}, player at {player.GetPosition():F1}");
        }
        Check(tested > 0 && arrived == tested, $"{name}: {arrived}/{tested} sampled teleports send the player to their destination ({teleports.Length} working in total)");
        foreach (var s in missed) Log("   missed: " + s);

        // Timer: through the main start and end zones onto the map's saved legit board.
        var zones = root.transform.Find("TimerZones");
        var start = zones != null ? zones.Find("Start " + name) : null;
        var end = zones != null ? zones.Find("End " + name) : null;
        Check(start != null && end != null, $"{name}: start and end zones from zones-cstrike ({(zones != null ? zones.childCount : 0)} zones)");
        if (start == null || end == null || timer == null) yield break;
        var board = (UdonBehaviour)start.GetComponent<UdonBehaviour>().GetProgramVariable("leaderboard");
        Check(board != null, $"{name}: the start zone has a saved board in the lobby");
        if (board == null) yield break;
        int before = (int)board.GetProgramVariable("count");
        Teleport(start.position);
        yield return Seconds(0.5f);
        Teleport(start.position + Vector3.up * 8f);
        yield return Seconds(0.25f);
        Teleport(end.position);
        yield return Seconds(0.5f);
        float last = (float)timer.GetProgramVariable("lastTime");
        float saved;
        bool hasSaved = VRC.SDK3.Persistence.PlayerData.TryGetFloat(player, name + "_legit", out saved);
        Check((int)board.GetProgramVariable("count") == before + 1 && hasSaved && Mathf.Abs(saved - last) < 0.001f,
            $"{name}: a run start -> end is timed ({last:F2} s), on the map's board and saved as {name}_legit");
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

    bool OnGround() { return (bool)movement.GetProgramVariable("onGround"); }

    void Press(string action, int slot)
    {
        manager.SetProgramVariable("__0_action__param", action);
        manager.SetProgramVariable("__0_slot__param", slot);
        manager.SendCustomEvent("__0_Press");
    }

    void Teleport(Vector3 position)
    {
        movement.SetProgramVariable("__0_position__param", position);
        movement.SetProgramVariable("__0_rotation__param", Quaternion.identity);
        movement.SetProgramVariable("__0_keepVelocity__param", false);
        movement.SendCustomEvent("__0_TeleportPlayer");
    }

    /// <summary>Like MapPlayTestRunner: the spots over the trigger with headroom whose ground (hull cast) is lowest, best first.</summary>
    static List<Vector3> DropPoints(SourceMapTeleport t, int count)
    {
        const int Solid = 1 << 0;
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
                if ((col.ClosestPoint(inside) - inside).sqrMagnitude > 1e-6f) continue;
                if (Physics.Raycast(above, Vector3.up, 1.5f, Solid, QueryTriggerInteraction.Ignore)) continue;
                if (Physics.CheckBox(above + new Vector3(0, 37, 0) * U, new Vector3(17, 37, 17) * U, Quaternion.identity, Solid, QueryTriggerInteraction.Ignore)) continue; // hull in a wall
                if (InsideSolid(above)) continue; // in a solid block: no face to overlap, so CheckBox misses it
                if (InPush(above)) continue; // a push (e.g. an updraft) decides where the player goes
                float depth = b.size.y + 0.3f;
                if (Physics.BoxCast(above, new Vector3(16, 0.5f, 16) * U, Vector3.down, out var hit, Quaternion.identity, depth, Solid, QueryTriggerInteraction.Ignore))
                    depth = hit.distance;
                if (depth > 0.3f + 9 * U) found.Add(new KeyValuePair<float, Vector3>(depth, above));
            }
        }
        found.Sort((x, y) => y.Key.CompareTo(x.Key));
        var result = new List<Vector3>();
        foreach (var f in found) if (result.Count < count) result.Add(f.Value);
        return result;
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

    IEnumerator Shot(string name, Transform spawn)
    {
        yield return null;
        var cam = new GameObject("ShotCamera").AddComponent<Camera>();
        cam.transform.SetPositionAndRotation(spawn.position + Vector3.up * 1.4f, Quaternion.Euler(8, spawn.eulerAngles.y, 0));
        cam.fieldOfView = 80;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 2000;
        var rt = new RenderTexture(1280, 720, 24);
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
        string dir = Path.GetFullPath("Assets/SourcePlayTests/.cache/shots");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes($"{dir}/sample_{name}.png", tex.EncodeToPNG());
        RenderTexture.active = null;
        Destroy(cam.gameObject);
    }

    IEnumerator Seconds(float s)
    {
        float t = Time.time + s;
        while (Time.time < t) yield return null;
    }

    static UdonBehaviour Find(string objectName)
    {
        foreach (var udon in FindObjectsOfType<UdonBehaviour>())
            if (udon.gameObject.name == objectName) return udon;
        return null;
    }

    static UdonBehaviour Loaded(UdonBehaviour udon, string variable)
    {
        try { return udon != null && udon.GetProgramVariable(variable) != null ? udon : null; }
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
