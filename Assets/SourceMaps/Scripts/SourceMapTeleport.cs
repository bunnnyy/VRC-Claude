using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

/// <summary>
/// trigger_teleport: sends the local player to the destination when they touch a trigger collider on this
/// object (Unity only sends trigger events to the collider's own object). Like Source without a landmark: the player faces the destination's direction and
/// stops. Works with SourceMovement (it resets its velocity on a teleport this far) and without it. With a filter
/// (bhop blocks) it fires only while the player's name matches, see SourceMapBlocks.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class SourceMapTeleport : UdonSharpBehaviour
{
    public Transform destination;
    [Tooltip("Only for a player with this name (filter_activator_name, e.g. bhop blocks), empty = everyone")]
    public string filterName = "";
    public bool filterNegate;
    [Tooltip("The filter checks the player's class (filter_activator_class) instead of the name")]
    public bool filterClass;
    [Tooltip("Holds the player's name (needed with a filter)")]
    public SourceMapBlocks blocks;

    public override void OnPlayerTriggerEnter(VRCPlayerApi player)
    {
        if (!player.isLocal) return;
        if (filterName == "") Teleport(player);
        else if (blocks != null && blocks.Passes(filterName, filterNegate, filterClass)) Teleport(player);
    }

    /// <summary>
    /// For SourceMapBlocks, when the player's name changed while touching this teleport: sends them if they now pass
    /// the filter. Returns true if it did.
    /// </summary>
    public bool Send()
    {
        if (filterName == "" || blocks == null || !blocks.Passes(filterName, filterNegate, filterClass)) return false;
        Teleport(Networking.LocalPlayer);
        return true;
    }

    void Teleport(VRCPlayerApi player)
    {
        if (destination == null) return;
        player.TeleportTo(destination.position, destination.rotation);
        player.SetVelocity(Vector3.zero); // VRChat keeps the old velocity through a teleport
    }
}
