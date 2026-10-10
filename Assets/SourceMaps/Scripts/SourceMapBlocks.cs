using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// Bhop blocks of one map, like CS:S: the local player's "targetname", which the map's trigger_multiples set with
/// "!activator AddOutput targetname ..." after short delays, and the things that check it (filtered teleports, pushes
/// and boosters), plus func_door blocks that sink when touched. Touches of name triggers and filtered teleports are
/// tested with Source's player box, not VRChat's capsule (which floats above the floor). Typical block: standing on it names you "activator"
/// after 0.09 s and "default" again at 0.1 s; a teleport filtered on "activator" covers the block, so whoever is still
/// on it then is sent back. A clean bhop leaves the block sooner. Untick "On" to make blocks plain platforms.
/// Everything here is local: each player's name and doors are their own (like most bhop servers).
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class SourceMapBlocks : UdonSharpBehaviour
{
    [Tooltip("Bhop blocks work like in CS:S. Off: every block is a plain platform you can stand on")]
    public bool on = true;
    [Tooltip("Teleports that only fire for a player with a certain name (set by the importer)")]
    public SourceMapTeleport[] filtered;
    [Tooltip("Triggers that rename the player (set by the importer)")]
    public SourceMapNameTrigger[] nameTriggers;
    [Tooltip("Push/booster triggers that only work for a player with a certain name (set by the importer)")]
    public Collider[] gated;
    public string[] gatedNames;
    public bool[] gatedNegate;
    public bool[] gatedClass;
    [Tooltip("Half size of Source's player box (metres) without SourceMovement, and how far above the feet it starts " +
             "(set by the importer). With SourceMovement its current hull is used (standing or ducked).")]
    public Vector3 hullHalf = new Vector3(0.3048f, 0.5906f, 0.3048f);
    public float hullBottom = 0.1143f;
    [Tooltip("Metres per Source unit (set by the importer)")]
    public float unitScale = 0.01905f;

    /// <summary>The local player's name and class, as the map's outputs set them ("" and "player" at the start).</summary>
    [System.NonSerialized] public string activator = "";
    [System.NonSerialized] public string activatorClass = "player";

    const int QueueSize = 64;
    float[] queueTime = new float[QueueSize];
    string[] queueName = new string[QueueSize];
    bool[] queueClass = new bool[QueueSize];
    int queued;
    bool[] touching;

    void OnEnable()
    {
        queued = 0;
        activatorClass = "player";
        Rename("", false);
    }

    /// <summary>"!activator AddOutput targetname name" (or "classname name") with a delay (seconds).</summary>
    public void SetNameLater(string name, float delay, bool isClass)
    {
        if (!on) return;
        if (queued == QueueSize) return; // full: drop, like an overflowing event queue
        queueTime[queued] = Time.time + delay;
        queueName[queued] = name;
        queueClass[queued] = isClass;
        queued++;
    }

    void Update()
    {
        TouchNameTriggers();
        // Due changes in time order, each one checked on its own: a name held for 0.01 s still counts, at any frame rate.
        while (queued > 0)
        {
            int first = 0;
            for (int i = 1; i < queued; i++) if (queueTime[i] < queueTime[first]) first = i;
            if (queueTime[first] > Time.time) return;
            string name = queueName[first];
            bool isClass = queueClass[first];
            queued--;
            queueTime[first] = queueTime[queued];
            queueName[first] = queueName[queued];
            queueClass[first] = queueClass[queued];
            Rename(name, isClass);
        }
    }

    void Rename(string name, bool isClass)
    {
        if (isClass) activatorClass = name; else activator = name;
        CheckTeleports();
        if (gated != null)
            for (int i = 0; i < gated.Length; i++)
                if (gated[i] != null) gated[i].enabled = Passes(gatedNames[i], gatedNegate[i], gatedClass[i]);
    }

    /// <summary>
    /// Source checks a filtered teleport's filter every tick while the player's box touches it. Physics trigger events
    /// come from VRChat's capsule, which floats above the floor and needs the triggers raised; here the touch is
    /// tested with Source's box instead, so a player who jumped off 0.09 s ago no longer counts.
    /// </summary>
    void CheckTeleports()
    {
        if (filtered == null || filtered.Length == 0) return;
        Collider[] hits = HullHits();
        if (hits == null) return;
        foreach (var hit in hits)
        {
            if (hit == null) continue; // VRChat hides some colliders (the player's own) from Udon as null
            foreach (var t in filtered)
                if (t != null && hit.gameObject == t.gameObject && t.Send()) return;
        }
    }

    UdonBehaviour movement;
    bool lookedUp;

    /// <summary>
    /// Half size of the player's box now: SourceMovement's hull (CS:S 32 x 62, ducked 32 x 45) if it's in the world,
    /// read by name so SourceMaps works without it; else hullHalf.
    /// </summary>
    Vector3 CurrentHalf()
    {
        if (!lookedUp)
        {
            lookedUp = true;
            var go = GameObject.Find("SourceMovement");
            if (go != null) movement = (UdonBehaviour)go.GetComponent(typeof(UdonBehaviour));
        }
        if (movement == null) return hullHalf;
        object width = movement.GetProgramVariable("hullWidth");
        object height = movement.GetProgramVariable((bool)movement.GetProgramVariable("ducked") ? "duckHullHeight" : "hullHeight");
        if (width == null || height == null) return hullHalf;
        return new Vector3((float)width * 0.5f, (float)height * 0.5f, (float)width * 0.5f) * unitScale;
    }

    /// <summary>The triggers Source's player box touches (null without a local player).</summary>
    Collider[] HullHits()
    {
        var player = Networking.LocalPlayer;
        if (player == null) return null;
        Vector3 half = CurrentHalf();
        Vector3 center = player.GetPosition() + Vector3.up * (hullBottom + half.y);
        return Physics.OverlapBox(center, half, Quaternion.identity, -1, QueryTriggerInteraction.Collide);
    }

    /// <summary>Name triggers touched by Source's player box this frame (thin triggers on blocks, like Source).</summary>
    void TouchNameTriggers()
    {
        if (!on || nameTriggers == null || nameTriggers.Length == 0) return;
        if (touching == null || touching.Length != nameTriggers.Length) touching = new bool[nameTriggers.Length];
        Collider[] hits = HullHits();
        if (hits == null) return;
        for (int i = 0; i < nameTriggers.Length; i++)
        {
            var nt = nameTriggers[i];
            if (nt == null) continue;
            bool now = false;
            foreach (var hit in hits)
                if (hit != null && hit.gameObject == nt.gameObject) { now = true; break; }
            if (now || touching[i]) nt.Touching(now, touching[i]);
            touching[i] = now;
        }
    }

    /// <summary>Does the local player pass a filter_activator_name (or _class, isClass) on this name?</summary>
    public bool Passes(string filterName, bool negate, bool isClass)
    {
        return on && (((isClass ? activatorClass : activator) == filterName) != negate);
    }
}
