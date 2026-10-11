using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// trigger_teleport: sends the local player to the destination when they touch a trigger collider on this
/// object (Unity only sends trigger events to the collider's own object). Without a landmark the player faces the
/// destination's direction and stops (CS:S keeps the velocity; here a teleport is a reset). With a landmark (seamless
/// level exits) they keep their offset from the landmark, their view and their speed, like Source. Works with
/// SourceMovement (through its _Teleport event, found by name) and without it. With a filter (bhop blocks) it fires
/// only while the player's name matches, see SourceMapBlocks.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class SourceMapTeleport : UdonSharpBehaviour
{
    public Transform destination;
    [Tooltip("trigger_teleport landmark: arrive at the same offset from the destination as from this, keeping view and speed")]
    public Transform landmark;
    [Tooltip("Only for a player with this name (filter_activator_name, e.g. bhop blocks), empty = everyone")]
    public string filterName = "";
    public bool filterNegate;
    [Tooltip("The filter checks the player's class (filter_activator_class) instead of the name")]
    public bool filterClass;
    [Tooltip("Holds the player's name (needed with a filter)")]
    public SourceMapBlocks blocks;

    UdonBehaviour movement;
    bool lookedUp;

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
        bool keep = landmark != null;
        Vector3 position = keep ? destination.position + (player.GetPosition() - landmark.position) : destination.position;
        Quaternion rotation = keep ? player.GetRotation() : destination.rotation;
        if (!lookedUp)
        {
            lookedUp = true;
            GameObject go = GameObject.Find("SourceMovement"); // by name, so SourceMaps works without it
            if (go != null) movement = (UdonBehaviour)go.GetComponent(typeof(UdonBehaviour));
        }
        if (movement != null)
        {
            movement.SetProgramVariable("teleportPosition", position);
            movement.SetProgramVariable("teleportRotation", rotation);
            movement.SetProgramVariable("teleportKeepVelocity", keep);
            movement.SendCustomEvent("_Teleport");
            return;
        }
        Vector3 velocity = player.GetVelocity();
        player.TeleportTo(position, rotation);
        player.SetVelocity(keep ? velocity : Vector3.zero); // VRChat keeps the old velocity through a teleport
    }
}
