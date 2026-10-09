using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

/// <summary>
/// trigger_teleport: sends the local player to the destination when they touch a trigger collider on this
/// object (Unity only sends trigger events to the collider's own object). Like Source without a landmark: the player faces the destination's direction and
/// stops. Works with SourceMovement (it resets its velocity on a teleport this far) and without it.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class SourceMapTeleport : UdonSharpBehaviour
{
    public Transform destination;

    public override void OnPlayerTriggerEnter(VRCPlayerApi player)
    {
        if (!player.isLocal || destination == null) return;
        player.TeleportTo(destination.position, destination.rotation);
        player.SetVelocity(Vector3.zero); // VRChat keeps the old velocity through a teleport
    }
}
