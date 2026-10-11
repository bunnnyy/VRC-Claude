using UdonSharp;
using UnityEngine;

/// <summary>
/// A menu button (a UI Button whose On Click calls Click) or a world button (Interact) for SourceMapManager. Actions: map (vote for the map in slot), extend, rtv, lobby, rejoin; owner: lock, start, force, forcelobby, addtime.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class SourceMapButton : UdonSharpBehaviour
{
    public SourceMapManager manager;
    public string action = "map";
    [Tooltip("Slot on the vote screen for \"map\"")]
    public int slot = -1;

    public void Click()
    {
        if (manager == null) // dragged in from a prefab: the lobby's manager, by name
        {
            GameObject found = GameObject.Find("SourceMapManager");
            if (found != null) manager = found.GetComponent<SourceMapManager>();
        }
        if (manager != null) manager.Press(action, slot);
    }

    public override void Interact()
    {
        Click();
    }
}
