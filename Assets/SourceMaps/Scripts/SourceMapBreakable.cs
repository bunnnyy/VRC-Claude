using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

/// <summary>
/// Breakable glass: a func_breakable that one knife hit breaks in CS:S (bhop_eazy_v2's glass panes, see
/// BspMechanics.BreaksOnApproach). VRChat has no knife, so coming within knife reach breaks it: it switches off the
/// glass's collider and visuals once CS:S's player box (32 x 62 units) at the player is within `reach` of the glass's
/// box, beside it (upright panes only: the importer leaves flat ones solid). Sits on the glass's "Break" child, whose
/// trigger box around the glass only switches the check on nearby; the solid object carries no trigger, so
/// SourceMovement's Hull Only can put it on the Walkthrough layer. Local, like knifing it yourself; it stays broken
/// until you leave the instance (nothing resets when the map is switched off and on again).
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class SourceMapBreakable : UdonSharpBehaviour
{
    [Tooltip("The glass's collider (on its marker)")]
    public Collider solid;
    [Tooltip("The glass's visible model (switched off when it breaks)")]
    public Transform visuals;
    [Tooltip("Knife reach in Source units (CS:S slash: 48)")]
    public float reach = 48f;
    [Tooltip("Metres per Source unit, as the map was imported")]
    public float unitScale = 0.01905f;
    public bool broken;

    bool near; // the player is in the trigger box around the glass: check every frame

    public override void OnPlayerTriggerEnter(VRCPlayerApi player) { if (player.isLocal) near = true; }
    public override void OnPlayerTriggerExit(VRCPlayerApi player) { if (player.isLocal) near = false; }
    void OnDisable() { near = false; }

    void Update()
    {
        if (!near || broken || solid == null) return;
        VRCPlayerApi player = Networking.LocalPlayer;
        if (player == null) return;
        Vector3 feet = player.GetPosition();
        Bounds glass = solid.bounds;
        float half = 16f * unitScale;
        if (feet.y >= glass.max.y - unitScale || feet.y + 62f * unitScale <= glass.min.y) return; // on it or under it
        float dx = Mathf.Max(0f, Mathf.Abs(feet.x - glass.center.x) - glass.extents.x - half);
        float dz = Mathf.Max(0f, Mathf.Abs(feet.z - glass.center.z) - glass.extents.z - half);
        // VRChat moves the player once per physics step, so it trails SourceMovement's box by up to a step's travel, and
        // the next check is a frame away: reach that much further when moving fast.
        Vector3 v = player.GetVelocity();
        float ahead = new Vector2(v.x, v.z).magnitude * (Mathf.Max(Time.deltaTime, Time.fixedDeltaTime) + Time.deltaTime);
        if (dx * dx + dz * dz <= (reach * unitScale + ahead) * (reach * unitScale + ahead)) Break();
    }

    public void Break()
    {
        broken = true;
        if (solid != null) solid.enabled = false;
        if (visuals != null) visuals.gameObject.SetActive(false);
    }
}
