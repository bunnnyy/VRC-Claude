using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

/// <summary>
/// Source trigger_push for SourceMovement. Put it on a trigger collider (or several: a brush each). While the player
/// is inside, the push moves them; overlapping pushes add up; when they leave the last one, a sideways push is kept
/// as momentum (like Source). Upward pushes lift. VRChat sends enter/leave per collider, so contacts are counted;
/// colliders switched off with the player inside (a name-gated push, the map switched off) count as left.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class SourcePushTrigger : UdonSharpBehaviour
{
    [Tooltip("SourceMovement in the scene. Left empty, the object named SourceMovement is used.")]
    public SourceMovement movement;
    [Tooltip("Push velocity in Source units/s on Unity axes (trigger_push: speed * push direction)")]
    public Vector3 push;

    private int contacts;
    private Collider[] colliders;

    private void Start()
    {
        colliders = GetComponents<Collider>();
        if (movement != null) return;
        GameObject found = GameObject.Find("SourceMovement");
        if (found != null) movement = found.GetComponent<SourceMovement>();
    }

    public override void OnPlayerTriggerEnter(VRCPlayerApi player)
    {
        if (!player.isLocal || movement == null) return;
        if (contacts++ == 0) movement.AddPush(push);
    }

    public override void OnPlayerTriggerExit(VRCPlayerApi player)
    {
        if (!player.isLocal || contacts == 0) return;
        if (--contacts == 0) Release();
    }

    private void Update()
    {
        // Unity sends no exit for a collider that's switched off: if all of ours are off, the player has left.
        if (contacts == 0 || colliders == null) return;
        for (int i = 0; i < colliders.Length; i++) if (colliders[i] != null && colliders[i].enabled) return;
        Release();
    }

    private void OnDisable() { if (contacts > 0) Release(); }

    private void Release()
    {
        contacts = 0;
        if (movement != null) movement.RemovePush(push);
    }
}
