using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

/// <summary>
/// A func_door bhop block ("touch opens"): touching it moves it by `moveLocal` at `speed` (often 9 units down at 20 u/s),
/// it stays open `wait` seconds and comes back. When it opens over a teleport (`dropThrough`), it stops being solid
/// at the bottom, so whoever is still on it falls into the teleport (in CS:S they sink into it with the door).
/// Sits on the door's "Touch" child (its trigger) and moves the parent (the solid door) and its visuals; the solid
/// object carries no trigger, so SourceMovement's Hull Only can put it on the Walkthrough layer. Local: each player
/// moves their own doors.
/// Part of SourceMapBlocks (does nothing while blocks are off).
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class SourceMapDoor : UdonSharpBehaviour
{
    public SourceMapBlocks blocks;
    [Tooltip("Offset when open (metres, in the door's parent's space)")]
    public Vector3 moveLocal;
    [Tooltip("Metres per second")]
    public float speed = 0.381f;
    [Tooltip("Seconds open before it returns; negative = stays open")]
    public float wait = 0.5f;
    public bool dropThrough;
    [Tooltip("The door's visible model (moved with it)")]
    public Transform visuals;
    public Collider solid;

    Transform door;
    Vector3 move, closedPos, visualsClosedPos;
    float t; // 0 closed .. 1 open
    int state; // 0 closed, 1 opening, 2 open, 3 closing
    float openSince;

    void Start()
    {
        door = transform.parent != null ? transform.parent : transform;
        move = door.parent != null ? door.parent.TransformVector(moveLocal) : moveLocal;
        closedPos = door.position;
        if (visuals != null) visualsClosedPos = visuals.position;
    }

    public override void OnPlayerTriggerEnter(VRCPlayerApi player)
    {
        if (!player.isLocal || blocks == null || !blocks.on) return;
        if (state == 0 || state == 3) state = 1;
    }

    void Update()
    {
        if (state == 0) return;
        float step = move.magnitude > 0.0001f ? speed * Time.deltaTime / move.magnitude : 1f;
        if (state == 1)
        {
            t = Mathf.Min(1f, t + step);
            if (t >= 1f) { state = 2; openSince = Time.time; if (dropThrough && solid != null) solid.enabled = false; }
        }
        else if (state == 2)
        {
            if (wait < 0f || Time.time < openSince + wait) return;
            state = 3;
            if (solid != null) solid.enabled = true;
        }
        else if (state == 3)
        {
            t = Mathf.Max(0f, t - step);
            if (t <= 0f) state = 0;
        }
        door.position = closedPos + move * t;
        if (visuals != null) visuals.position = visualsClosedPos + move * t;
    }
}
