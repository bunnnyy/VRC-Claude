using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

/// <summary>
/// Local run timer with checkpoints, driven by TimerZones. Independent of any movement system:
/// it only uses trigger zones and VRChat teleports. Finished times go to the synced Leaderboard.
///
/// Optional categories: point Category Source at any script with a bool (e.g. SourceMovement's
/// autoBhop). If that bool is on at any moment during a run, the run goes to the Auto Leaderboard
/// instead, so auto bhop and legit bhop keep separate records.
///
/// Courses: a Start zone can bring its own boards and start point (several courses or maps in one world, each with
/// its own records). Start zones without them use the boards and start point set here.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class RunTimer : UdonSharpBehaviour
{
    [Tooltip("Board for normal runs (legit bhop)")]
    public Leaderboard leaderboard;
    [Header("Categories (optional)")]
    [Tooltip("Script with a bool that marks a run as the other category, e.g. SourceMovement")]
    public UdonSharpBehaviour categorySource;
    [Tooltip("Name of that bool")]
    public string categoryVariable = "autoBhop";
    [Tooltip("Board for runs where the bool was on at any point (auto bhop)")]
    public Leaderboard autoLeaderboard;
    [Header("Display")]
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
    private bool runIsAuto;
    private bool lastWasAuto;
    private Leaderboard courseBoard, courseAutoBoard;
    private Transform courseStart;

    private void Start()
    {
        localPlayer = Networking.LocalPlayer;
        checkpoint = startPoint;
    }

    private void Update()
    {
        if (Input.GetKeyDown(restartKey)) _Restart();
        if (running && !runIsAuto && CategoryIsOn()) runIsAuto = true;
        if (label == null) return;
        if (running) label.text = CategoryName(runIsAuto) + FormatTime(Time.time - startTime);
        else if (lastTime >= 0f) label.text = "Finished " + CategoryName(lastWasAuto) + FormatTime(lastTime);
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
    public bool LastRunWasAuto() { return lastWasAuto; }

    private bool CategoryIsOn()
    {
        return categorySource != null && (bool)categorySource.GetProgramVariable(categoryVariable);
    }

    private string CategoryName(bool auto)
    {
        if (categorySource == null) return "";
        return auto ? "[auto] " : "[legit] ";
    }

    /// <summary>Set by the Start zone the player entered; null = this timer's own boards / start point.</summary>
    public void _SetCourse(Leaderboard legit, Leaderboard auto, Transform start)
    {
        courseBoard = legit;
        courseAutoBoard = auto;
        courseStart = start;
    }

    private Transform StartPoint() { return courseStart != null ? courseStart : startPoint; }

    public void _EnterStart()
    {
        running = false;
        checkpoint = StartPoint();
    }

    public void _LeaveStart()
    {
        running = true;
        startTime = Time.time;
        lastTime = -1f;
        runIsAuto = CategoryIsOn();
    }

    public void _EnterEnd()
    {
        if (!running) return;
        if (CategoryIsOn()) runIsAuto = true;
        running = false;
        lastTime = Time.time - startTime;
        lastWasAuto = runIsAuto;
        Leaderboard board = runIsAuto ? (courseAutoBoard != null ? courseAutoBoard : autoLeaderboard)
                                      : (courseBoard != null ? courseBoard : leaderboard);
        if (board != null) board._Submit(lastTime);
    }

    public void _SetCheckpoint(Transform point) { checkpoint = point; }

    public void _ResetToCheckpoint() { TeleportTo(checkpoint != null ? checkpoint : StartPoint()); }

    public void _Restart()
    {
        running = false;
        checkpoint = StartPoint();
        TeleportTo(checkpoint);
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
