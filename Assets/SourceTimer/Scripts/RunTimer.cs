using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

/// <summary>
/// Local run timer with checkpoints, driven by TimerZones. Independent of any movement system:
/// it only uses trigger zones and VRChat teleports. Finished times go to the synced Leaderboard.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class RunTimer : UdonSharpBehaviour
{
    public Leaderboard leaderboard;
    [Tooltip("Where Restart teleports to (put it inside the start zone)")]
    public Transform startPoint;
    [Tooltip("Optional timer text on a world space canvas under this object")]
    public TextMeshProUGUI label;
    [Tooltip("Position of the timer text relative to the eyes, in metres")]
    public Vector3 headOffset = new Vector3(0f, 0.25f, 1f);
    [Tooltip("Desktop key that restarts the run")]
    public KeyCode restartKey = KeyCode.G;

    private VRCPlayerApi localPlayer;
    private Transform checkpoint;
    private bool running;
    private float startTime;
    private float lastTime = -1f;

    private void Start()
    {
        localPlayer = Networking.LocalPlayer;
        checkpoint = startPoint;
    }

    private void Update()
    {
        if (Input.GetKeyDown(restartKey)) _Restart();
        if (label == null) return;
        if (running) label.text = FormatTime(Time.time - startTime);
        else if (lastTime >= 0f) label.text = "Finished " + FormatTime(lastTime);
        else label.text = "";
    }

    public override void PostLateUpdate()
    {
        if (label == null || localPlayer == null) return;
        VRCPlayerApi.TrackingData head = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
        transform.SetPositionAndRotation(head.position + head.rotation * headOffset, head.rotation);
    }

    public bool IsRunning() { return running; }
    public float GetLastTime() { return lastTime; }

    public void _EnterStart()
    {
        running = false;
        checkpoint = startPoint;
    }

    public void _LeaveStart()
    {
        running = true;
        startTime = Time.time;
        lastTime = -1f;
    }

    public void _EnterEnd()
    {
        if (!running) return;
        running = false;
        lastTime = Time.time - startTime;
        if (leaderboard != null) leaderboard._Submit(lastTime);
    }

    public void _SetCheckpoint(Transform point) { checkpoint = point; }

    public void _ResetToCheckpoint() { TeleportTo(checkpoint != null ? checkpoint : startPoint); }

    public void _Restart()
    {
        running = false;
        checkpoint = startPoint;
        TeleportTo(startPoint);
    }

    private void TeleportTo(Transform point)
    {
        if (point == null || localPlayer == null) return;
        localPlayer.TeleportTo(point.position, point.rotation);
        localPlayer.SetVelocity(Vector3.zero);
    }

    /// <summary>m:ss.fff</summary>
    public static string FormatTime(float seconds)
    {
        int ms = Mathf.FloorToInt(seconds * 1000f);
        int s = (ms / 1000) % 60;
        int f = ms % 1000;
        return (ms / 60000) + ":" + (s < 10 ? "0" : "") + s + "." + (f < 100 ? "0" : "") + (f < 10 ? "0" : "") + f;
    }
}
