using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using VRC.SDK3.ClientSim;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// Play mode test of the map rotation (SourceMapManager) in the SourceMaps test scene with ClientSim: voting, the
/// vote timer, travel, rock the vote, owner lock/start/force/lobby, time limit with extend, back to lobby/rejoin,
/// ties and owner changes. Presses go through the same path as the world buttons. Single player (ClientSim has no
/// real second client), so vote counting with several players is checked by setting the vote counts directly.
/// Started by PlayTestBootstrap.RunVote; results are logged with a [SMTEST] prefix.
/// </summary>
public class VotePlayTestRunner : MonoBehaviour
{
    readonly List<string> report = new List<string>();
    int failures;
    bool finished;
    UdonBehaviour manager;
    VRCPlayerApi player;

    IEnumerator Start()
    {
        for (int i = 0; i < 600 && (player == null || manager == null); i++)
        {
            yield return null;
            player = Networking.LocalPlayer;
            manager = Loaded(FindUdon("SourceMapManager"));
        }
        if (player == null || manager == null) { Check(false, "setup: player and SourceMapManager loaded"); Finish(); yield break; }
        for (int i = 0; i < 30; i++) yield return null;
        foreach (var menu in Resources.FindObjectsOfTypeAll<ClientSimMenu>())
            if (menu.gameObject.scene.IsValid()) { menu.WarningAccepted(); menu.CloseMenu(); }
        yield return Seconds(1f);

        var lobby = (Transform)manager.GetProgramVariable("lobbySpawn");
        int[] cands = Candidates();
        Check(cands.Length == 5, $"lobby: 5 of 6 maps offered (got {cands.Length}: {string.Join(",", cands)})");
        Check(State() == 0 && Near(lobby.position), "lobby: player starts in the lobby, state lobby");
        Check(ActiveMaps() == 0, "lobby: no map switched on while in the lobby");
        Check(Status().Contains("Vote for the next map"), "lobby: board says 'Vote for the next map'");
        yield return Shot("lobby_board", GameObject.Find("VoteBoard").transform, new Vector3(0, 1.9f, -5.2f), 50);

        // Vote: first vote starts the timer, the winner is played by everyone.
        manager.SetProgramVariable("voteSeconds", 3f);
        Press("map", 2);
        yield return Seconds(0.5f);
        int[] votes = (int[])manager.GetProgramVariable("votes");
        Check(votes[2] == 1 && (int)manager.GetProgramVariable("voteEnd") != 0, "vote: vote counted on slot 3, timer started");
        Check(Status().Contains("0:0"), $"vote: board shows the countdown ('{Status().Replace("\n", " | ")}')");
        yield return Shot("lobby_vote", GameObject.Find("VoteBoard").transform, new Vector3(0, 1.9f, -5.2f), 50);
        yield return Seconds(3.5f);
        int map = (int)manager.GetProgramVariable("currentMap");
        Check(State() == 1 && map == cands[2], $"vote: after the timer the voted map is played (map {map}, expected {cands[2]})");
        Check(Near(MapSpawn(map)), "vote: player teleported to the map's spawn");
        Check(ActiveMaps() == 1 && MapRoot(map).activeSelf, "vote: only that map is switched on");
        yield return Seconds(1f);
        var movement = Loaded(FindUdon("SourceMovement"));
        if (movement != null) Check((bool)movement.GetProgramVariable("onGround"), "vote: player stands on the map floor (SourceMovement)");
        yield return Shot("map_panel", MapRoot(map).transform.Find("SourceMapPanel"), new Vector3(0, 1.45f, -1.9f), 55);

        // Rock the vote: 1 player = 100% >= 60%: new vote in the lobby with the timer running.
        Press("rtv", -1);
        yield return Seconds(0.5f);
        Check(State() == 0 && (int)manager.GetProgramVariable("voteEnd") != 0, "rtv: rocking the vote starts a new vote with the timer running");
        Check(Near(lobby.position) && ActiveMaps() == 0, "rtv: everyone is back in the lobby");
        cands = Candidates();
        Check(System.Array.IndexOf(cands, map) < 0, "rtv: the map just played isn't offered again");

        // Owner lock: timer stops, votes still count, only Start ends it.
        Press("lock", -1);
        yield return Seconds(0.3f);
        Check((bool)manager.GetProgramVariable("locked") && (int)manager.GetProgramVariable("voteEnd") == 0, "lock: owner locks the vote, timer stopped");
        Press("map", 0);
        yield return Seconds(4f);
        Check(State() == 0, "lock: locked vote doesn't end on its own");
        manager.SetProgramVariable("timeLimitMinutes", 3f / 60f); // 3 s map for the time limit test below
        Press("start", -1);
        yield return Seconds(0.5f);
        map = (int)manager.GetProgramVariable("currentMap");
        Check(State() == 1 && map == cands[0], "start: owner's Start ends the vote with the voted map");

        // Time limit: a vote with "extend" starts; extend wins and the same map continues.
        yield return Seconds(3.5f);
        Check(State() == 0 && (bool)manager.GetProgramVariable("extendOffered"), "time limit: when time is up a vote starts with 'extend' offered");
        Check(Status().Contains("Time is up"), "time limit: board says time is up");
        Press("extend", -1);
        yield return Seconds(3.5f);
        Check(State() == 1 && (int)manager.GetProgramVariable("currentMap") == map && (int)manager.GetProgramVariable("mapEnd") != 0,
            "extend: extend wins, the same map continues with a new time limit");
        int end = (int)manager.GetProgramVariable("mapEnd");
        Press("addtime", -1);
        yield return Seconds(0.3f);
        int added = (int)manager.GetProgramVariable("mapEnd") - end;
        Check(Mathf.Abs(added - 15 * 60000) < 10, $"addtime: owner adds 15 minutes ({added / 1000} s)");

        // Back to lobby alone, rejoin.
        Press("lobby", -1);
        yield return Seconds(0.3f);
        Check(Near(lobby.position) && ActiveMaps() == 0 && State() == 1, "lobby button: only this player goes back, the map keeps running");
        Press("rejoin", -1);
        yield return Seconds(0.3f);
        Check(Near(MapSpawn(map)) && MapRoot(map).activeSelf, "rejoin: back into the current map");

        // Owner: everyone to the lobby, force a map.
        Press("forcelobby", -1);
        yield return Seconds(0.3f);
        Check(State() == 0 && (int)manager.GetProgramVariable("voteEnd") == 0 && Near(lobby.position), "forcelobby: owner sends everyone to the lobby, new vote waits for a vote");
        cands = Candidates();
        Press("force", -1);
        Press("map", 1);
        yield return Seconds(0.3f);
        Check(State() == 1 && (int)manager.GetProgramVariable("currentMap") == cands[1], "force: owner forces the pressed map");

        // Ties are broken at random: two maps with 2 votes each, 20 rounds, both must win sometimes.
        int first = 0, second = 0, other = 0;
        for (int i = 0; i < 20; i++)
        {
            Press("forcelobby", -1);
            yield return null;
            cands = Candidates();
            var v = new int[cands.Length + 1];
            v[0] = 2; v[1] = 2;
            manager.SetProgramVariable("votes", v);
            Press("start", -1);
            yield return null;
            int won = System.Array.IndexOf(cands, (int)manager.GetProgramVariable("currentMap"));
            if (won == 0) first++; else if (won == 1) second++; else other++;
        }
        Check(first > 0 && second > 0 && other == 0, $"ties: a 2-2 tie goes to either map at random ({first}/{second}/{other})");

        yield return OwnerChanges();

        Finish();
    }

    /// <summary>
    /// Instance owner and object owner changes, with ClientSim's simulated remote players (one client: the new
    /// owner shares memory with the old one, so this checks the logic, not two separate clients).
    /// </summary>
    IEnumerator OwnerChanges()
    {
        var board = GameObject.Find("VoteBoard").transform;
        var ownerControls = board.Find("OwnerControls").gameObject;
        Press("forcelobby", -1);
        yield return Seconds(0.3f);

        // Someone else is the instance owner: we're master but not admin.
        ClientSimMain.SpawnRemotePlayer("Owner");
        yield return Seconds(0.3f);
        VRCPlayerApi remoteOwner = Remote("Owner");
        player.GetClientSimPlayer().isInstanceOwner = false;
        remoteOwner.GetClientSimPlayer().isInstanceOwner = true;
        yield return Seconds(0.7f);
        Check(player.isMaster && !ownerControls.activeSelf, "instance owner elsewhere: the master doesn't get owner controls");
        int round = (int)manager.GetProgramVariable("round");
        Press("lock", -1);
        yield return Seconds(0.3f);
        Check(!(bool)manager.GetProgramVariable("locked"), "instance owner elsewhere: owner actions from others are refused");

        // The instance owner leaves: the master takes over the owner controls.
        ClientSimMain.RemovePlayer(remoteOwner);
        yield return Seconds(0.7f);
        Check(ownerControls.activeSelf, "instance owner left: the master gets the owner controls");
        Press("lock", -1);
        yield return Seconds(0.3f);
        Check((bool)manager.GetProgramVariable("locked"), "instance owner left: the master's owner actions work");
        Press("lock", -1); // unlock again

        // The player holding the votes (object owner) leaves mid-vote: votes and timer carry over to the next owner.
        ClientSimMain.SpawnRemotePlayer("Holder");
        yield return Seconds(0.3f);
        VRCPlayerApi holder = Remote("Holder");
        manager.SetProgramVariable("voteSeconds", 3f);
        int[] cands = Candidates();
        Press("map", 0);
        yield return Seconds(0.2f);
        Networking.SetOwner(holder, manager.gameObject);
        yield return Seconds(0.2f);
        Check(!Networking.IsOwner(player, manager.gameObject), "owner leaves: the remote player now holds the votes");
        ClientSimMain.RemovePlayer(holder);
        yield return Seconds(0.3f);
        int[] v = (int[])manager.GetProgramVariable("votes");
        Check(Networking.IsOwner(player, manager.gameObject) && v[0] == 1 && (int)manager.GetProgramVariable("voteEnd") != 0,
            "owner leaves: the master takes over with the vote and the running timer");
        yield return Seconds(3.5f);
        Check(State() == 1 && (int)manager.GetProgramVariable("currentMap") == cands[0], "owner leaves: the vote still ends on time with the right map");

        // A player who voted leaves: their vote is dropped.
        Press("forcelobby", -1);
        yield return Seconds(0.3f);
        ClientSimMain.SpawnRemotePlayer("Voter");
        yield return Seconds(0.3f);
        VRCPlayerApi voter = Remote("Voter");
        var ids = (int[])manager.GetProgramVariable("voterIds");
        var slots = (int[])manager.GetProgramVariable("voterSlots");
        ids[0] = voter.playerId; slots[0] = 1; // as if they voted for slot 2
        manager.SetProgramVariable("voteSeconds", 600f);
        Press("map", 0);
        yield return Seconds(0.2f);
        v = (int[])manager.GetProgramVariable("votes");
        Check(v[0] == 1 && v[1] == 1, "voter leaves: both votes counted before");
        ClientSimMain.RemovePlayer(voter);
        yield return Seconds(0.3f);
        v = (int[])manager.GetProgramVariable("votes");
        Check(v[0] == 1 && v[1] == 0, $"voter leaves: their vote is dropped ({v[0]}/{v[1]})");

        // Rock the vote with 2 players needs 2; when the other one leaves, 1 of 1 is enough.
        Press("start", -1);
        yield return Seconds(0.3f);
        ClientSimMain.SpawnRemotePlayer("Other");
        yield return Seconds(0.3f);
        Press("rtv", -1);
        yield return Seconds(0.3f);
        Check(State() == 1 && (int)manager.GetProgramVariable("rtvCount") == 1, "rtv: 1 of 2 players isn't enough (needs 60%)");
        ClientSimMain.RemovePlayer(Remote("Other"));
        yield return Seconds(0.3f);
        Check(State() == 0, "rtv: when the other player leaves, 1 of 1 rocks the vote");
        player.GetClientSimPlayer().isInstanceOwner = true;
    }

    VRCPlayerApi Remote(string name)
    {
        foreach (var p in VRCPlayerApi.AllPlayers) if (!p.isLocal && p.displayName.Contains(name)) return p;
        return null;
    }

    void Press(string action, int slot)
    {
        manager.SetProgramVariable("__0_action__param", action);
        manager.SetProgramVariable("__0_slot__param", slot);
        manager.SendCustomEvent("__0_Press");
    }

    int State() { return (int)manager.GetProgramVariable("state"); }
    int[] Candidates() { return (int[])manager.GetProgramVariable("candidates"); }
    Component[] Maps() { return (Component[])manager.GetProgramVariable("maps"); }
    GameObject MapRoot(int map) { return Maps()[map].gameObject; }
    Vector3 MapSpawn(int map)
    {
        var info = Maps()[map].GetComponent<UdonBehaviour>();
        return ((Transform)info.GetProgramVariable("spawn")).position;
    }
    int ActiveMaps()
    {
        int n = 0;
        foreach (var m in Maps()) if (m.gameObject.activeSelf) n++;
        return n;
    }
    bool Near(Vector3 p) { return Vector3.Distance(player.GetPosition(), p) < 1.5f; }

    string Status()
    {
        var board = GameObject.Find("VoteBoard");
        var text = board != null ? board.transform.Find("Status/Text") : null;
        return text != null ? text.GetComponent<TMPro.TextMeshProUGUI>().text : "";
    }

    IEnumerator Seconds(float s)
    {
        float t = Time.time + s;
        while (Time.time < t) yield return null;
    }

    /// <summary>Picture for the guide (docs/images): a camera in front of `target` (offset in its local space).</summary>
    IEnumerator Shot(string name, Transform target, Vector3 offset, float fov)
    {
        yield return null; // (WaitForEndOfFrame never comes in batch mode)
        var cam = new GameObject("ShotCamera").AddComponent<Camera>();
        cam.transform.SetPositionAndRotation(target.TransformPoint(Vector3.zero) + target.rotation * offset, target.rotation);
        cam.fieldOfView = fov;
        var rt = new RenderTexture(1280, 720, 24);
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
        string dir = Path.GetFullPath("Assets/SourcePlayTests/.cache/shots");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes($"{dir}/{name}.png", tex.EncodeToPNG());
        RenderTexture.active = null;
        Destroy(cam.gameObject);
        Log("screenshot " + dir + "/" + name + ".png");
    }

    static UdonBehaviour FindUdon(string objectName)
    {
        foreach (var udon in FindObjectsOfType<UdonBehaviour>())
            if (udon.gameObject.name == objectName) return udon;
        return null;
    }

    static UdonBehaviour Loaded(UdonBehaviour udon)
    {
        try { return udon != null && udon.GetProgramVariable("round") != null || udon != null && udon.GetProgramVariable("active") != null ? udon : null; }
        catch (System.NullReferenceException) { return null; }
    }

    void Update()
    {
        if (!finished && Time.realtimeSinceStartup > 600f) { Check(false, "watchdog: tests did not finish within 10 minutes"); Finish(); }
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
