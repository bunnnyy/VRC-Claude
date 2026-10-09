using UdonSharp;
using UnityEngine;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

public enum SourceMapMode
{
    Vote,   // everyone votes in the lobby, the winner is played by the whole instance
    Choose, // each player picks a map and goes there alone
}

/// <summary>
/// Map rotation like a CS:S bhop server: a lobby screen offers random maps from the pool, players vote, everyone is
/// sent to the winner. In a map: rock the vote (rtv) and a time limit start a new vote (back in the lobby, with
/// "extend" offered after a time limit). The instance owner (or the master when the instance has no owner) can lock
/// the vote, start it, force a map, extend or send everyone to the lobby.
/// The object's owner counts the votes; players send them with network events. Everything the owner needs is synced
/// (who voted for what, who rocked the vote, the timers), so when the owner leaves the next one carries on.
/// Independent of SourceMovement:
/// travel is a plain TeleportTo (SourceMovement resets its velocity on a jump that far).
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class SourceMapManager : UdonSharpBehaviour
{
    public const int StateLobby = 0, StatePlaying = 1;

    [Header("Maps")]
    public SourceMapInfo[] maps;
    [Tooltip("Where players wait and vote (also put the VRC World spawn here)")]
    public Transform lobbySpawn;
    public SourceMapMode mode = SourceMapMode.Vote;
    [Tooltip("Maps offered per vote (4-6)")]
    [Range(2, 6)] public int choices = 5;

    [Header("Vote (CS:S mapchooser style)")]
    [Tooltip("Seconds from the first vote to the result")]
    public float voteSeconds = 60f;
    [Tooltip("Share of players needed to rock the vote")]
    [Range(0.1f, 1f)] public float rtvRatio = 0.6f;
    [Tooltip("Minutes per map before a new vote starts; 0 = rock the vote only")]
    public float timeLimitMinutes = 30f;
    [Tooltip("Minutes added when \"extend\" wins or an admin extends")]
    public float extendMinutes = 15f;

    [Header("Screens (lobby board and map panels)")]
    public SourceMapScreen[] screens;

    // Synced state (written by the owner)
    [UdonSynced] public int state = StateLobby;
    [UdonSynced] public int currentMap = -1;
    [UdonSynced] public int round;              // +1 on every change between lobby and map: clients travel on it
    [UdonSynced] public int[] candidates = new int[0];
    [UdonSynced] public int[] votes = new int[0]; // per candidate, last entry = "extend"
    [UdonSynced] public bool extendOffered;
    [UdonSynced] public int voteEnd;            // server time (ms) the vote ends, 0 = timer not running
    [UdonSynced] public bool locked;            // admin stopped the timer; only Start ends the vote
    [UdonSynced] public int mapEnd;             // server time (ms) the map ends, 0 = no time limit
    [UdonSynced] public int rtvCount;
    // Who voted for what and who rocked the vote (player ids, 0 = free), synced so a new owner has them too.
    private const int MaxPlayers = 100;
    [UdonSynced] private int[] voterIds = new int[MaxPlayers];
    [UdonSynced] private int[] voterSlots = new int[MaxPlayers];
    [UdonSynced] private int[] rtvIds = new int[MaxPlayers];

    // Local
    [HideInInspector] public int localMap = -1;   // map the local player is in, -1 = lobby
    [HideInInspector] public int localVote = -1;
    [HideInInspector] public bool localRtv;
    [HideInInspector] public bool forceArmed;     // admin: the next map press forces that map
    private int appliedRound = -1;
    private VRCPlayerApi localPlayer;

    private void Start()
    {
        localPlayer = Networking.LocalPlayer;
        ShowMap(-1);
        if (Networking.IsOwner(gameObject) && round == 0) NewVote(false, false);
        _Tick();
    }

    // ------------------------------------------------------------------ player actions (buttons)

    /// <summary>Called by SourceMapButton. slot = index on the screen, -1 if none.</summary>
    public void Press(string action, int slot)
    {
        if (action == "map")
        {
            if (slot < 0 || slot >= candidates.Length) return;
            if (forceArmed && IsAdmin(localPlayer))
            {
                forceArmed = false;
                SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(AdminForceMap), slot);
            }
            else if (mode == SourceMapMode.Choose) Travel(candidates[slot]);
            else if (state == StateLobby)
            {
                localVote = slot;
                SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(Vote), slot);
            }
        }
        else if (action == "extend" && state == StateLobby && extendOffered)
        {
            localVote = candidates.Length;
            SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(Vote), candidates.Length);
        }
        else if (action == "rtv" && mode == SourceMapMode.Vote && state == StatePlaying)
        {
            localRtv = !localRtv;
            SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(RockTheVote), localRtv);
        }
        else if (action == "lobby") Travel(-1);
        else if (action == "rejoin" && state == StatePlaying && mode == SourceMapMode.Vote) Travel(currentMap);
        else if (action == "force") forceArmed = !forceArmed && IsAdmin(localPlayer);
        else if (action == "lock") SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(AdminLock));
        else if (action == "start") SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(AdminStart));
        else if (action == "forcelobby") SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(AdminLobby));
        else if (action == "addtime") SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(AdminExtend));
        RefreshScreens();
    }

    // ------------------------------------------------------------------ owner side (network events)

    [NetworkCallable]
    public void Vote(int slot)
    {
        if (state != StateLobby || mode != SourceMapMode.Vote || slot < 0 || slot >= votes.Length) return;
        if (slot == candidates.Length && !extendOffered) return;
        int id = NetworkCalling.CallingPlayer.playerId;
        int i = System.Array.IndexOf(voterIds, id);
        if (i < 0) i = System.Array.IndexOf(voterIds, 0);
        if (i < 0) return;
        voterIds[i] = id;
        voterSlots[i] = slot;
        if (voteEnd == 0 && !locked) voteEnd = Now() + Mathf.RoundToInt(voteSeconds * 1000f);
        CountVotes();
        Changed();
    }

    [NetworkCallable]
    public void RockTheVote(bool on)
    {
        if (state != StatePlaying) return;
        int id = NetworkCalling.CallingPlayer.playerId;
        int i = System.Array.IndexOf(rtvIds, id);
        if (on && i < 0)
        {
            i = System.Array.IndexOf(rtvIds, 0);
            if (i >= 0) rtvIds[i] = id;
        }
        else if (!on && i >= 0) rtvIds[i] = 0;
        CheckRtv();
    }

    [NetworkCallable]
    public void AdminLock()
    {
        if (!IsAdmin(NetworkCalling.CallingPlayer) || state != StateLobby) return;
        locked = !locked;
        voteEnd = locked || !AnyVotes() ? 0 : Now() + Mathf.RoundToInt(voteSeconds * 1000f);
        Changed();
    }

    [NetworkCallable]
    public void AdminStart()
    {
        if (!IsAdmin(NetworkCalling.CallingPlayer) || state != StateLobby) return;
        EndVote();
    }

    [NetworkCallable]
    public void AdminForceMap(int slot)
    {
        if (!IsAdmin(NetworkCalling.CallingPlayer) || slot < 0 || slot >= candidates.Length) return;
        Play(candidates[slot]);
    }

    [NetworkCallable]
    public void AdminLobby()
    {
        if (!IsAdmin(NetworkCalling.CallingPlayer)) return;
        NewVote(false, false);
    }

    [NetworkCallable]
    public void AdminExtend()
    {
        if (!IsAdmin(NetworkCalling.CallingPlayer) || state != StatePlaying || mapEnd == 0) return;
        mapEnd += Mathf.RoundToInt(extendMinutes * 60000f);
        Changed();
    }

    public override void OnPlayerLeft(VRCPlayerApi player)
    {
        // Their vote and rtv are dropped by the recount (it skips players who left), whichever comes first:
        // this event or the ownership transfer when the leaving player was the owner. A frame later, because
        // during this event the leaving player still counts as in the instance.
        SendCustomEventDelayedFrames(nameof(_Recount), 1);
    }

    public override void OnOwnershipTransferred(VRCPlayerApi player)
    {
        SendCustomEventDelayedFrames(nameof(_Recount), 1);
    }

    /// <summary>Owner: recount votes and rtv without players who left, start rtv if enough, sync.</summary>
    public void _Recount()
    {
        if (!Networking.IsOwner(gameObject)) return;
        CountVotes();
        CheckRtv();
    }

    /// <summary>New vote in the lobby: everyone goes there. offerExtend adds "extend current map".</summary>
    private void NewVote(bool offerExtend, bool startTimer)
    {
        state = StateLobby;
        int played = currentMap;
        extendOffered = offerExtend && currentMap >= 0;
        if (!extendOffered) currentMap = -1;
        candidates = PickCandidates(played);
        votes = new int[candidates.Length + 1];
        ClearVotes();
        locked = false;
        voteEnd = startTimer ? Now() + Mathf.RoundToInt(voteSeconds * 1000f) : 0;
        mapEnd = 0;
        ClearRtv();
        round++;
        Changed();
    }

    private void EndVote()
    {
        int winner = PickWinner();
        if (winner == candidates.Length) Play(currentMap, Mathf.RoundToInt(extendMinutes * 60000f));
        else Play(candidates[winner]);
    }

    private void Play(int map) { Play(map, Mathf.RoundToInt(TimeLimit(map) * 60000f)); }

    private void Play(int map, int durationMs)
    {
        state = StatePlaying;
        currentMap = map;
        extendOffered = false;
        voteEnd = 0;
        locked = false;
        mapEnd = durationMs > 0 ? Now() + durationMs : 0;
        ClearVotes();
        votes = new int[candidates.Length + 1];
        ClearRtv();
        round++;
        Changed();
    }

    /// <summary>Most votes wins, ties broken at random (no votes: a random map).</summary>
    public int PickWinner()
    {
        int best = -1, ties = 0;
        for (int i = 0; i < votes.Length; i++)
        {
            if (votes[i] > best) { best = votes[i]; ties = 1; }
            else if (votes[i] == best) ties++;
        }
        if (best <= 0) return Random.Range(0, candidates.Length);
        int pick = Random.Range(0, ties);
        for (int i = 0; i < votes.Length; i++)
            if (votes[i] == best && pick-- == 0) return i;
        return 0;
    }

    /// <summary>Random maps for the vote; like CS:S, not the map just played (unless the pool is too small).</summary>
    private int[] PickCandidates(int played)
    {
        int pool = maps == null ? 0 : maps.Length;
        int n = Mathf.Min(choices, pool);
        var order = new int[pool];
        for (int i = 0; i < pool; i++) order[i] = i;
        for (int i = pool - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            int t = order[i]; order[i] = order[j]; order[j] = t;
        }
        var result = new int[n];
        int k = 0;
        for (int i = 0; i < pool && k < n; i++)
            if (order[i] != played || pool <= n) result[k++] = order[i];
        return result;
    }

    private void CountVotes()
    {
        for (int i = 0; i < votes.Length; i++) votes[i] = 0;
        for (int i = 0; i < MaxPlayers; i++)
        {
            if (voterIds[i] == 0) continue;
            if (!Utilities.IsValid(VRCPlayerApi.GetPlayerById(voterIds[i]))) { voterIds[i] = 0; continue; } // left
            if (voterSlots[i] >= 0 && voterSlots[i] < votes.Length) votes[voterSlots[i]]++;
        }
    }

    private bool AnyVotes()
    {
        for (int i = 0; i < votes.Length; i++) if (votes[i] > 0) return true;
        return false;
    }

    private void CheckRtv()
    {
        int count = 0;
        for (int i = 0; i < MaxPlayers; i++)
        {
            if (rtvIds[i] == 0) continue;
            if (!Utilities.IsValid(VRCPlayerApi.GetPlayerById(rtvIds[i]))) { rtvIds[i] = 0; continue; } // left
            count++;
        }
        rtvCount = count;
        if (state == StatePlaying && count > 0 && count >= RtvNeeded()) NewVote(false, true);
        else Changed();
    }

    private void ClearVotes()
    {
        for (int i = 0; i < MaxPlayers; i++) voterIds[i] = 0;
    }

    private void ClearRtv()
    {
        for (int i = 0; i < MaxPlayers; i++) rtvIds[i] = 0;
        rtvCount = 0;
    }

    private void Changed()
    {
        RequestSerialization();
        Apply();
    }

    // ------------------------------------------------------------------ every client

    public override void OnDeserialization() { Apply(); }

    /// <summary>Follow the synced state: travel when the round changed (vote mode), refresh the screens.</summary>
    private void Apply()
    {
        if (round != appliedRound)
        {
            bool joining = appliedRound < 0;
            appliedRound = round;
            localVote = -1;
            localRtv = false;
            forceArmed = false;
            if (mode == SourceMapMode.Vote)
            {
                if (state == StatePlaying) Travel(currentMap);
                else if (!joining) Travel(-1);
            }
        }
        RefreshScreens();
    }

    /// <summary>Owner: end votes and maps on time. Everyone: refresh the countdowns.</summary>
    public void _Tick()
    {
        SendCustomEventDelayedSeconds(nameof(_Tick), 0.5f);
        if (Networking.IsOwner(gameObject) && round == 0) NewVote(false, false); // first owner left before starting
        if (Networking.IsOwner(gameObject) && mode == SourceMapMode.Vote)
        {
            int now = Now();
            if (state == StateLobby && voteEnd != 0 && !locked && now - voteEnd >= 0) EndVote();
            else if (state == StatePlaying && mapEnd != 0 && now - mapEnd >= 0) NewVote(true, true);
        }
        RefreshScreens();
    }

    private void Travel(int map)
    {
        localMap = map;
        ShowMap(map);
        if (localPlayer == null) return;
        Transform target = map >= 0 && maps[map].spawn != null ? maps[map].spawn : lobbySpawn;
        if (target == null) return;
        localPlayer.TeleportTo(target.position, target.rotation);
        localPlayer.SetVelocity(Vector3.zero); // VRChat keeps the old velocity through a teleport
    }

    /// <summary>Only the map the local player is in is switched on (renderers and collision).</summary>
    private void ShowMap(int map)
    {
        if (maps == null) return;
        for (int i = 0; i < maps.Length; i++)
            if (maps[i] != null) maps[i].gameObject.SetActive(i == map);
    }

    private void RefreshScreens()
    {
        if (screens == null) return;
        foreach (var s in screens) if (s != null) s.Refresh();
    }

    // ------------------------------------------------------------------ helpers for screens

    /// <summary>Instance owner, or the master when the instance has no owner (public and group instances).</summary>
    public bool IsAdmin(VRCPlayerApi player)
    {
        if (!Utilities.IsValid(player)) return false;
        VRCPlayerApi owner = Networking.InstanceOwner;
        if (Utilities.IsValid(owner)) return owner.playerId == player.playerId;
        return player.isMaster;
    }

    public int RtvNeeded() { return Mathf.Max(1, Mathf.CeilToInt(VRCPlayerApi.GetPlayerCount() * rtvRatio)); }

    public float TimeLimit(int map)
    {
        if (map >= 0 && map < maps.Length && maps[map] != null && maps[map].timeLimitMinutes > 0f) return maps[map].timeLimitMinutes;
        return timeLimitMinutes;
    }

    /// <summary>Seconds left on the vote (-1 = not running) or the map (-1 = no limit).</summary>
    public int SecondsLeft()
    {
        int end = state == StateLobby ? voteEnd : mapEnd;
        if (end == 0) return -1;
        return Mathf.Max(0, Mathf.CeilToInt((end - Now()) / 1000f));
    }

    public string MapName(int map)
    {
        if (maps == null || map < 0 || map >= maps.Length || maps[map] == null) return "";
        return maps[map].mapName;
    }

    private int Now() { return Networking.GetServerTimeInMilliseconds(); }
}
