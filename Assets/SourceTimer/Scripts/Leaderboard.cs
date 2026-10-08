using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

/// <summary>
/// Synced top 10 best times for this instance, one entry per player. Players send their time to the
/// owner, who merges it and syncs the list, so two people finishing at once can't overwrite each other.
/// Late joiners get the current list automatically.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class Leaderboard : UdonSharpBehaviour
{
    [Tooltip("Optional text that shows the board")]
    public TextMeshProUGUI board;
    public string title = "Best times";

    private const int MaxEntries = 10;
    [UdonSynced] private string[] names = new string[MaxEntries];
    [UdonSynced] private float[] times = new float[MaxEntries];
    [UdonSynced] private int count;

    private void Start()
    {
        for (int i = 0; i < MaxEntries; i++)
            if (names[i] == null) names[i] = "";
        Refresh();
    }

    /// <summary>Called by RunTimer when the local player finishes.</summary>
    public void _Submit(float time)
    {
        if (Networking.IsOwner(gameObject)) Record(Networking.LocalPlayer.displayName, time);
        else SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(SubmitTime), time);
    }

    [NetworkCallable]
    public void SubmitTime(float time)
    {
        if (!NetworkCalling.InNetworkCall || !Networking.IsOwner(gameObject)) return;
        VRCPlayerApi caller = NetworkCalling.CallingPlayer;
        if (caller == null || !caller.IsValid()) return;
        Record(caller.displayName, time); // name comes from the network, not from the caller's data
    }

    public override void OnDeserialization() { Refresh(); }

    public int GetCount() { return count; }
    public string GetName(int i) { return names[i]; }
    public float GetTime(int i) { return times[i]; }

    private void Record(string playerName, float time)
    {
        if (!(time > 0.1f) || time > 86400f) return; // also rejects NaN

        // Keep only each player's best.
        for (int i = 0; i < count; i++)
        {
            if (names[i] != playerName) continue;
            if (time >= times[i]) return;
            for (int j = i; j < count - 1; j++)
            {
                names[j] = names[j + 1];
                times[j] = times[j + 1];
            }
            count--;
            break;
        }

        // Insert in order.
        int slot = count;
        while (slot > 0 && times[slot - 1] > time) slot--;
        if (slot >= MaxEntries) return;
        int last = count < MaxEntries ? count : MaxEntries - 1;
        for (int i = last; i > slot; i--)
        {
            names[i] = names[i - 1];
            times[i] = times[i - 1];
        }
        names[slot] = playerName;
        times[slot] = time;
        if (count < MaxEntries) count++;

        RequestSerialization();
        Refresh();
    }

    private void Refresh()
    {
        if (board == null) return;
        string text = title;
        for (int i = 0; i < count; i++)
            text += "\n" + (i + 1) + ". " + names[i] + "  " + RunTimer.FormatTime(times[i]);
        board.text = text;
    }
}
