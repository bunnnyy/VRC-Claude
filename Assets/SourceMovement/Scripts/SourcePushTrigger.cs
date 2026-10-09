using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

/// <summary>
/// Source trigger_push for SourceMovement. Put it on a trigger collider. While the player is inside, the push
/// moves them; when they leave, a sideways push is kept as momentum (like Source). Upward pushes lift.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class SourcePushTrigger : UdonSharpBehaviour
{
    [Tooltip("SourceMovement in the scene. Left empty, the object named SourceMovement is used.")]
    public SourceMovement movement;
    [Tooltip("Push velocity in Source units/s on Unity axes (trigger_push: speed * push direction)")]
    public Vector3 push;

    private void Start()
    {
        if (movement != null) return;
        GameObject found = GameObject.Find("SourceMovement");
        if (found != null) movement = found.GetComponent<SourceMovement>();
    }

    public override void OnPlayerTriggerEnter(VRCPlayerApi player)
    {
        if (player.isLocal && movement != null) movement.SetPush(push);
    }

    public override void OnPlayerTriggerExit(VRCPlayerApi player)
    {
        if (player.isLocal && movement != null) movement.SetPush(Vector3.zero);
    }
}
