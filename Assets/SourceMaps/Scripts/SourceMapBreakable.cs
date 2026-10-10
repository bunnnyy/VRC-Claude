using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

/// <summary>
/// Breakable glass: a func_breakable that one knife hit breaks in CS:S (bhop_eazy_v2's glass panes, see
/// BspMechanics.BreaksOnApproach). VRChat has no knife, so coming within knife reach breaks it: it sits on the glass's
/// "Break" child, whose trigger box is the glass grown by the reach sideways (not up or down, so glass you stand on or
/// under stays), and switches off the glass's collider and visuals. The solid object carries no trigger, so
/// SourceMovement's Hull Only can put it on the Walkthrough layer. Local, like knifing it yourself; it stays broken until
/// you leave the instance (nothing resets when the map is switched off and on again).
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class SourceMapBreakable : UdonSharpBehaviour
{
    [Tooltip("The glass's collider (on its marker)")]
    public Collider solid;
    [Tooltip("The glass's visible model (switched off when it breaks)")]
    public Transform visuals;
    public bool broken;

    public override void OnPlayerTriggerEnter(VRCPlayerApi player)
    {
        if (player.isLocal && !broken) Break();
    }

    public void Break()
    {
        broken = true;
        if (solid != null) solid.enabled = false;
        if (visuals != null) visuals.gameObject.SetActive(false);
        Collider reach = GetComponent<Collider>();
        if (reach != null) reach.enabled = false;
    }
}
