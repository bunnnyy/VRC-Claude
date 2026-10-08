using UdonSharp;
using VRC.SDKBase;

/// <summary>
/// Optional: Source movement is on only while the local player is inside this object's trigger
/// collider (e.g. a Box Collider with Is Trigger). Turn off Active On Start on SourceMovement when using it.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class SourceMovementZone : UdonSharpBehaviour
{
    public SourceMovement movement;

    public override void OnPlayerTriggerEnter(VRCPlayerApi player)
    {
        if (player.isLocal && movement != null) movement.SetMovementActive(true);
    }

    public override void OnPlayerTriggerExit(VRCPlayerApi player)
    {
        if (player.isLocal && movement != null) movement.SetMovementActive(false);
    }
}
