using System;
using System.Reflection;
using UnityEngine;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;

/// <summary>Tests for the timer, checkpoints and leaderboard (Assets/SourceTimer).</summary>
public static class TimerTests
{
    static void Check(bool ok, string message) => MovementTests.Check(ok, message);

    public static void Run(Action<string, Action> test)
    {
        test("Timer: time formats as m:ss.fff", Format);
        test("Timer: leaving start, reaching end records the time", RunFlow);
        test("Timer: end without starting does nothing", EndWithoutStart);
        test("Timer: reset goes to last checkpoint, restart goes to start", ResetAndRestart);
        test("Timer zones call the right timer action", Zones);
        test("Categories: legit and auto bhop runs go to separate boards", Categories);
        test("Leaderboard: sorted, best time per player, top 10", BoardOrder);
        test("Leaderboard: rejects bad times", BoardRejects);
        test("Leaderboard: non-owners send their time to the owner", BoardNetwork);
        test("Courses: a start zone's own boards and start point are used", Courses);
        test("Leaderboard: saved best is kept, restored on join, not replaced by a worse time", SavedBest);
    }

    static VRCPlayerApi player;

    static T Make<T>() where T : new()
    {
        player = new VRCPlayerApi { displayName = "Me" };
        Networking.LocalPlayer = player;
        Networking.localIsOwner = true;
        Time.time = 0;
        var obj = new T();
        typeof(T).GetMethod("Start", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(obj, null);
        return obj;
    }

    static Transform Point(float x)
    {
        var t = new GameObject().transform;
        t.position = new Vector3(x, 0, 0);
        return t;
    }

    static (RunTimer timer, Leaderboard board) Setup()
    {
        var board = Make<Leaderboard>();
        var timer = Make<RunTimer>();
        timer.leaderboard = board;
        timer.startPoint = Point(1);
        typeof(RunTimer).GetMethod("Start", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(timer, null);
        return (timer, board);
    }

    static void Format()
    {
        Check(RunTimer.FormatTime(12.345f) == "0:12.345", RunTimer.FormatTime(12.345f));
        Check(RunTimer.FormatTime(75.5f) == "1:15.500", RunTimer.FormatTime(75.5f));
        Check(RunTimer.FormatTime(3.007f) == "0:03.007", RunTimer.FormatTime(3.007f));
    }

    static void RunFlow()
    {
        var (timer, board) = Setup();
        timer._EnterStart();
        Time.time = 5f;
        timer._LeaveStart();
        Check(timer.IsRunning(), "not running");
        Time.time = 17.25f;
        timer._EnterEnd();
        Check(!timer.IsRunning() && Math.Abs(timer.GetLastTime() - 12.25f) < 0.001f, "time " + timer.GetLastTime());
        Check(board.GetCount() == 1 && board.GetName(0) == "Me", "not on the board");
    }

    static void EndWithoutStart()
    {
        var (timer, board) = Setup();
        timer._EnterEnd();
        Check(board.GetCount() == 0 && timer.GetLastTime() < 0, "recorded a time");
    }

    static void ResetAndRestart()
    {
        var (timer, board) = Setup();
        timer._LeaveStart();
        timer._SetCheckpoint(Point(50));
        player.position = new Vector3(99, -100, 0);
        timer._ResetToCheckpoint();
        Check(player.position.x == 50, "reset went to " + player.position);
        Check(timer.IsRunning(), "reset should not stop the timer");
        timer._Restart();
        Check(player.position.x == 1 && !timer.IsRunning(), "restart went to " + player.position);
    }

    static void Zones()
    {
        var (timer, board) = Setup();
        TimerZone Zone(TimerZoneType type) => new TimerZone { zoneType = type, timer = timer };
        Zone(TimerZoneType.Start).OnPlayerTriggerEnter(player);
        Zone(TimerZoneType.Start).OnPlayerTriggerExit(player);
        Check(timer.IsRunning(), "start zone did not start");
        var cp = Zone(TimerZoneType.Checkpoint);
        cp.respawnPoint = Point(70);
        cp.OnPlayerTriggerEnter(player);
        Zone(TimerZoneType.Reset).OnPlayerTriggerEnter(player);
        Check(player.position.x == 70, "reset zone went to " + player.position);
        var other = new VRCPlayerApi { isLocal = false };
        Zone(TimerZoneType.End).OnPlayerTriggerEnter(other);
        Check(timer.IsRunning(), "a remote player finished my run");
        Time.time = 3f;
        Zone(TimerZoneType.End).OnPlayerTriggerEnter(player);
        Check(!timer.IsRunning() && board.GetCount() == 1, "end zone did not finish");
        Zone(TimerZoneType.Restart).OnPlayerTriggerEnter(player);
        Check(player.position.x == 1, "restart zone went to " + player.position);
    }

    static void Submit(Leaderboard board, string name, float time)
    {
        player.displayName = name;
        board._Submit(time);
    }

    static void Categories()
    {
        var (timer, legit) = Setup();
        var auto = Make<Leaderboard>();
        var movement = new SourceMovement { autoBhop = false };
        timer.leaderboard = legit;
        timer.autoLeaderboard = auto;
        timer.categorySource = movement;
        player.displayName = "Me";

        void Race(bool autoAtStart, bool autoMidRun, bool autoAtEnd)
        {
            movement.autoBhop = autoAtStart;
            timer._EnterStart();
            Time.time += 1f;
            timer._LeaveStart();
            Time.time += 5f;
            movement.autoBhop = autoMidRun;
            typeof(RunTimer).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(timer, null);
            Time.time += 5f;
            movement.autoBhop = autoAtEnd;
            timer._EnterEnd();
        }

        Race(false, false, false);
        Check(legit.GetCount() == 1 && auto.GetCount() == 0 && !timer.LastRunWasAuto(), "legit run went to the wrong board");
        Race(true, true, true);
        Check(auto.GetCount() == 1 && timer.LastRunWasAuto(), "auto run went to the wrong board");
        Race(false, true, false); // switched on mid-run, then back off: still an auto run
        Check(timer.LastRunWasAuto(), "turning auto bhop on mid-run kept it legit");
        Check(legit.GetCount() == 1, "mid-run switch landed on the legit board");
    }

    static void Courses()
    {
        var bhop = Make<Leaderboard>(); // (Make gives every object a fresh local player, so make the boards first)
        var bhopAuto = Make<Leaderboard>();
        var (timer, mainBoard) = Setup();
        var start = new TimerZone { zoneType = TimerZoneType.Start, timer = timer, leaderboard = bhop, autoLeaderboard = bhopAuto, respawnPoint = Point(500) };
        var end = new TimerZone { zoneType = TimerZoneType.End, timer = timer };
        start.OnPlayerTriggerEnter(player);
        start.OnPlayerTriggerExit(player);
        Time.time += 4f;
        end.OnPlayerTriggerEnter(player);
        Check(bhop.GetCount() == 1 && mainBoard.GetCount() == 0, "course run went to the main board");
        timer._Restart();
        Check(player.position.x == 500, "restart went to " + player.position + ", not the course start");
        // A start zone without boards goes back to the timer's own.
        var plain = new TimerZone { zoneType = TimerZoneType.Start, timer = timer };
        plain.OnPlayerTriggerEnter(player);
        plain.OnPlayerTriggerExit(player);
        Time.time += 4f;
        end.OnPlayerTriggerEnter(player);
        Check(mainBoard.GetCount() == 1 && bhop.GetCount() == 1, "plain start zone kept the course boards");
        timer._Restart();
        Check(player.position.x == 1, "restart after a plain start went to " + player.position);
    }

    static void SavedBest()
    {
        VRC.SDK3.Persistence.PlayerData.floats.Clear();
        var board = Make<Leaderboard>();
        board.saveKey = "bhop_japan_legit";
        Submit(board, "Me", 30f);
        Check(VRC.SDK3.Persistence.PlayerData.floats["bhop_japan_legit"] == 30f, "best not saved");
        Submit(board, "Me", 40f);
        Check(VRC.SDK3.Persistence.PlayerData.floats["bhop_japan_legit"] == 30f, "a worse time replaced the saved best");
        Submit(board, "Me", 0f);
        Check(VRC.SDK3.Persistence.PlayerData.floats["bhop_japan_legit"] == 30f, "a bad time was saved");
        // New session: the board is empty until the player's data is restored.
        var next = Make<Leaderboard>();
        next.saveKey = "bhop_japan_legit";
        Check(next.GetCount() == 0, "new board not empty");
        next.OnPlayerRestored(player);
        Check(next.GetCount() == 1 && next.GetTime(0) == 30f, "saved best not restored onto the board");
        var unsaved = Make<Leaderboard>();
        unsaved.OnPlayerRestored(player);
        Check(unsaved.GetCount() == 0, "a board without a save key restored a time");
    }

    static void BoardOrder()
    {
        var board = Make<Leaderboard>();
        Submit(board, "A", 30);
        Submit(board, "B", 20);
        Submit(board, "C", 25);
        Submit(board, "A", 35); // worse than A's best: ignored
        Submit(board, "B", 10); // better: replaces B
        Check(board.GetCount() == 3, "count " + board.GetCount());
        Check(board.GetName(0) == "B" && board.GetTime(0) == 10 && board.GetName(1) == "C" && board.GetName(2) == "A", "order");
        for (int i = 0; i < 12; i++) Submit(board, "P" + i, 40 + i);
        Check(board.GetCount() == 10, "not capped at 10: " + board.GetCount());
        Check(board.GetName(9) == "P6", "last place " + board.GetName(9));
        Submit(board, "Fast", 1);
        Check(board.GetName(0) == "Fast" && board.GetCount() == 10, "insert at top");
        Check(board.serializationRequests > 0, "never synced");
    }

    static void BoardRejects()
    {
        var board = Make<Leaderboard>();
        Submit(board, "A", 0f);
        Submit(board, "A", float.NaN);
        Submit(board, "A", -5f);
        Submit(board, "A", 1e9f);
        Check(board.GetCount() == 0, "accepted a bad time");
    }

    static void BoardNetwork()
    {
        var board = Make<Leaderboard>();
        Networking.localIsOwner = false;
        board._Submit(42f);
        Check(board.GetCount() == 0 && board.sentNetworkEvents.Count == 1, "non-owner wrote the board directly");
        var (name, args) = board.sentNetworkEvents[0];
        Check(name == "SubmitTime" && (float)args[0] == 42f, "sent " + name);

        // Owner side: only real network calls count, and the name comes from the caller.
        Networking.localIsOwner = true;
        board.SubmitTime(5f);
        Check(board.GetCount() == 0, "accepted a call outside the network");
        NetworkCalling.InNetworkCall = true;
        NetworkCalling.CallingPlayer = new VRCPlayerApi { displayName = "Remote", isLocal = false };
        board.SubmitTime(42f);
        NetworkCalling.InNetworkCall = false;
        Check(board.GetCount() == 1 && board.GetName(0) == "Remote", "owner did not record the remote time");
    }
}
