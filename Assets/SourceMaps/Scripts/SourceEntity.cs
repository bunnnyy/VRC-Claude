using UdonSharp;
using UnityEngine;

/// <summary>
/// Marker for one entity imported from a Source BSP. Does nothing by itself: it keeps the entity's data
/// (classname, targetname, every keyvalue and output) so other scripts can implement it, e.g. SourceMovement
/// for boosters later. Brush entities (triggers, func_*) also store their volume.
/// Outputs keep Source's format: "target,input,parameter,delay,times" (Source separates them with ESC or commas).
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class SourceEntity : UdonSharpBehaviour
{
    public string className;
    public string targetName;
    [Tooltip("All keyvalues in BSP order. Keys repeat for outputs (OnStartTouch, OnTrigger...).")]
    public string[] keys;
    public string[] values;
    [Tooltip("Brush entities: bounds of the brush model in local space (meters). Zero size for point entities.")]
    public Bounds bounds;

    /// <summary>First value for a key (case-insensitive), or "" if missing.</summary>
    public string GetValue(string key)
    {
        if (keys == null) return "";
        string lower = key.ToLower();
        for (int i = 0; i < keys.Length; i++)
            if (keys[i].ToLower() == lower) return values[i];
        return "";
    }

#if !COMPILER_UDONSHARP && UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (bounds.size == Vector3.zero) return;
        Gizmos.color = new Color(1f, 0.6f, 0f, 0.8f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(bounds.center, bounds.size);
    }
#endif
}
