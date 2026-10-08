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
    [Tooltip("Checkpoint respawn point, defaults to this object")]
    public Transform respawnPoint;

    public override void OnPlayerTriggerEnter(VRCPlayerApi player)
    {
        if (!player.isLocal || timer == null) return;
        if (zoneType == TimerZoneType.Start) timer._EnterStart();
        else if (zoneType == TimerZoneType.End) timer._EnterEnd();
        else if (zoneType == TimerZoneType.Checkpoint) timer._SetCheckpoint(respawnPoint != null ? respawnPoint : transform);
        else if (zoneType == TimerZoneType.Reset) timer._ResetToCheckpoint();
        else if (zoneType == TimerZoneType.Restart) timer._Restart();
    }

    public override void OnPlayerTriggerExit(VRCPlayerApi player)
    {
        if (player.isLocal && timer != null && zoneType == TimerZoneType.Start) timer._LeaveStart();
    }
}
