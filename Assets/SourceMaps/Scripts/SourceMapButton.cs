using UdonSharp;
using UnityEngine;

/// <summary>
/// World button (Interact, works on desktop and VR) for SourceMapManager. Actions: map (vote for the map in slot), extend, rtv, lobby, rejoin; owner: lock, start, force, forcelobby, addtime.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class SourceMapButton : UdonSharpBehaviour
{
    public SourceMapManager manager;
    public string action = "map";
    [Tooltip("Slot on the vote screen for \"map\"")]
    public int slot = -1;

    public override void Interact()
    {
        if (manager != null) manager.Press(action, slot);
    }
}
