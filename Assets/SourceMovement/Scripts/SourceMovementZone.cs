using UdonSharp;
using VRC.SDKBase;

/// <summary>
/// Optional: Source movement is on only while the local player is inside a zone: this object's trigger colliders
/// (e.g. a Box Collider with Is Trigger), or any other SourceMovementZone's. Turn off Active On Start on
/// SourceMovement when using it. SourceMovement counts the colliders the player is in, so overlapping zones work.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class SourceMovementZone : UdonSharpBehaviour
{
    public SourceMovement movement;

    public override void OnPlayerTriggerEnter(VRCPlayerApi player)
    {
        if (player.isLocal && movement != null) movement._ZoneEnter();
    }

    public override void OnPlayerTriggerExit(VRCPlayerApi player)
    {
        if (player.isLocal && movement != null) movement._ZoneExit();
    }
}
