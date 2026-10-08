using TMPro;
using UdonSharp;
using UnityEngine;

/// <summary>World button (Interact) that toggles auto bhop, so VR players can switch too.</summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class AutoBhopButton : UdonSharpBehaviour
{
    public SourceMovement movement;
    [Tooltip("Optional text that shows the current mode")]
    public TextMeshProUGUI label;

    private int shownState = -1;

    public override void Interact()
    {
        if (movement != null) movement._ToggleAutoBhop();
    }

    private void Update()
    {
        if (movement == null || label == null) return;
        int state = movement.autoBhop ? 1 : 0;
        if (state == shownState) return;
        shownState = state;
        label.text = state == 1 ? "Auto bhop: ON" : "Auto bhop: OFF";
    }
}
