using UdonSharp;
using UnityEngine;

/// <summary>
/// A trigger_multiple whose outputs rename the player ("!activator AddOutput targetname X" or "classname X", with
/// delays): OnStartTouch ones when a touch starts, OnTrigger ones then and again every "wait" seconds while touching
/// (Source's trigger_multiple), and OnEndTouch ones on leaving.
/// Part of SourceMapBlocks, which tests the touch with Source's player box (see there).
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class SourceMapNameTrigger : UdonSharpBehaviour
{
    public SourceMapBlocks blocks;
    [Tooltip("Names set on touch / while touching (OnTrigger, OnStartTouch)")]
    public string[] names;
    public float[] delays;
    [Tooltip("Per name: sets the player's class (AddOutput classname) instead of the name")]
    public bool[] isClass;
    [Tooltip("Per name: OnStartTouch (once per touch) instead of OnTrigger (again every wait while touching)")]
    public bool[] onStart;
    [Tooltip("Names set when the player leaves (OnEndTouch)")]
    public string[] leaveNames;
    public float[] leaveDelays;
    public bool[] leaveIsClass;
    [Tooltip("Seconds before it fires again while touched; negative = only once (trigger_once)")]
    public float wait = 0.2f;
    [Tooltip("Only fires for this player name (filter_activator_name), empty = everyone")]
    public string filterName = "";
    public bool filterNegate, filterClass;

    bool spent;
    float next;

    /// <summary>
    /// Called by SourceMapBlocks every frame the player's Source box touches this trigger (and once when it stops):
    /// fires on touch, again every `wait` seconds while touching, and the leave outputs when it stops.
    /// </summary>
    public void Touching(bool now, bool before)
    {
        if (now) { if (!before) next = 0f; Fire(!before); return; }
        if (!before || spent || blocks == null || leaveNames == null || !Allowed()) return; // a spent trigger_once is gone
        for (int i = 0; i < leaveNames.Length; i++) blocks.SetNameLater(leaveNames[i], leaveDelays[i], leaveIsClass[i]);
    }

    /// <summary>The map was switched on again (SourceMapBlocks): a trigger_once can fire again.</summary>
    public void _ResetState()
    {
        spent = false;
        next = 0f;
    }

    void Fire(bool start)
    {
        if (blocks == null || spent || !Allowed()) return;
        bool trigger = Time.time >= next;
        for (int i = 0; i < names.Length; i++)
        {
            bool once = onStart != null && i < onStart.Length && onStart[i];
            if (once ? start : trigger) blocks.SetNameLater(names[i], delays[i], isClass[i]);
        }
        if (!trigger) return;
        if (wait < 0f) spent = true;
        next = Time.time + wait;
    }

    bool Allowed() { return filterName == "" || blocks.Passes(filterName, filterNegate, filterClass); }
}
