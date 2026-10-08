using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

/// <summary>Debug speedometer: horizontal speed in Source units per second, floating in front of the view.</summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class SourceSpeedometer : UdonSharpBehaviour
{
    public SourceMovement movement;
    [Tooltip("Text on a world space canvas under this object")]
    public TextMeshProUGUI label;
    [Tooltip("Show the speedometer")]
    public bool show;
    [Tooltip("Position relative to the eyes, in metres")]
    public Vector3 headOffset = new Vector3(0f, -0.3f, 1f);

    private VRCPlayerApi localPlayer;
    private int shownSpeed = -1;

    private void Start()
    {
        localPlayer = Networking.LocalPlayer;
    }

    public override void PostLateUpdate()
    {
        if (label == null || movement == null || localPlayer == null) return;
        if (label.gameObject.activeSelf != show) label.gameObject.SetActive(show);
        if (!show) return;

        VRCPlayerApi.TrackingData head = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
        transform.SetPositionAndRotation(head.position + head.rotation * headOffset, head.rotation);

        int speed = Mathf.RoundToInt(movement.GetSpeed());
        if (speed == shownSpeed) return;
        shownSpeed = speed;
        label.text = speed.ToString();
    }
}
