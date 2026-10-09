using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

/// <summary>
/// Bhop map boosters for SourceMovement: what a trigger_multiple does with outputs like
/// "!activator,AddOutput,basevelocity 0 0 800" or "!activator,AddOutput,gravity 0.5", and what trigger_gravity
/// does. Put it on a trigger collider; it fires when the player enters (OnStartTouch) or leaves (OnEndTouch).
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class SourceBoostTrigger : UdonSharpBehaviour
{
    [Tooltip("SourceMovement in the scene. Left empty, the object named SourceMovement is used.")]
    public SourceMovement movement;
    [Tooltip("Velocity added once, in Source units/s on Unity axes (basevelocity). Zero for none.")]
    public Vector3 addVelocity;
    [Tooltip("Also set the player's gravity multiplier (gravity output, trigger_gravity)")]
    public bool setGravity;
    [Tooltip("Gravity multiplier: 1 normal, 0.5 half")]
    public float gravityScale = 1f;
    [Tooltip("Fire when the player leaves instead of enters (OnEndTouch)")]
    public bool onLeave;

    private void Start()
    {
        if (movement != null) return;
        GameObject found = GameObject.Find("SourceMovement");
        if (found != null) movement = found.GetComponent<SourceMovement>();
    }

    public override void OnPlayerTriggerEnter(VRCPlayerApi player)
    {
        if (player.isLocal && !onLeave) Fire();
    }

    public override void OnPlayerTriggerExit(VRCPlayerApi player)
    {
        if (player.isLocal && onLeave) Fire();
    }

    private void Fire()
    {
        if (movement == null) return;
        if (addVelocity != Vector3.zero) movement.AddVelocity(addVelocity);
        if (setGravity) movement.SetGravityScale(gravityScale);
    }
}
