using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

public enum TimerZoneType
{
    Start,      // timer starts when you leave it
    End,        // finishes the run
    Checkpoint, // sets where Reset sends you
    Reset,      // back to the last checkpoint (put under surf ramps, kill floors)
    Restart,    // back to the start
}

/// <summary>Trigger zone for RunTimer. Needs a trigger collider (e.g. Box Collider, Is Trigger) on this object.</summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class TimerZone : UdonSharpBehaviour
{
    public TimerZoneType zoneType;
    public RunTimer timer;
    [Tooltip("Checkpoint: where Reset sends you (default: this object). Start zone: where Restart sends you for this course (default: the RunTimer's Start Point, else this object)")]
    public Transform respawnPoint;
    [Header("Start zone (optional): this course's own boards. End zone: its legit board, so only its runs end here")]
    public Leaderboard leaderboard;
    public Leaderboard autoLeaderboard;

    private bool inside; // the local player is in this start zone

    public override void OnPlayerTriggerEnter(VRCPlayerApi player)
    {
        if (!player.isLocal || timer == null) return;
        if (zoneType == TimerZoneType.Start)
        {
            // No point of its own: the timer's Start Point, or (none either, e.g. a map's zone made by hand) this zone.
            timer._SetCourse(leaderboard, autoLeaderboard, respawnPoint != null || timer.startPoint != null ? respawnPoint : transform);
            timer._EnterStart();
            inside = true;
        }
        else if (zoneType == TimerZoneType.End) timer._EnterEndOf(leaderboard);
        else if (zoneType == TimerZoneType.Checkpoint) timer._SetCheckpoint(respawnPoint != null ? respawnPoint : transform);
        else if (zoneType == TimerZoneType.Reset) timer._ResetToCheckpoint();
        else if (zoneType == TimerZoneType.Restart) timer._Restart();
    }

    public override void OnPlayerTriggerExit(VRCPlayerApi player)
    {
        if (!player.isLocal || timer == null || zoneType != TimerZoneType.Start) return;
        inside = false;
        timer._LeaveStart();
    }

    // Switched off with the player inside (its map rotated away): Unity sends no exit, so lift the speed cap here.
    private void OnDisable()
    {
        if (!inside || timer == null) return;
        inside = false;
        timer._ExitStartZone();
    }
}
