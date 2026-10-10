using UdonSharp;
using UnityEngine;

/// <summary>
/// A trigger_multiple whose outputs rename the player ("!activator AddOutput targetname X" or "classname X", with
/// delays): on touch,
/// then again every "wait" seconds while touching (Source's trigger_multiple), and on leaving for OnEndTouch outputs.
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
        if (now) { if (!before) next = 0f; Fire(); return; }
        if (!before || blocks == null || leaveNames == null || !Allowed()) return;
        for (int i = 0; i < leaveNames.Length; i++) blocks.SetNameLater(leaveNames[i], leaveDelays[i], leaveIsClass[i]);
    }

    void Fire()
    {
        if (blocks == null || spent || Time.time < next || !Allowed()) return;
        for (int i = 0; i < names.Length; i++) blocks.SetNameLater(names[i], delays[i], isClass[i]);
        if (wait < 0f) spent = true;
        next = Time.time + wait;
    }

    bool Allowed() { return filterName == "" || blocks.Passes(filterName, filterNegate, filterClass); }
}
