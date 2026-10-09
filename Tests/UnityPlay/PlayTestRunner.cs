using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using VRC.SDK3.ClientSim;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// Play mode tests on the generated test map with ClientSim: real PhysX colliders, ClientSim's
/// CharacterController player and ClientSim's input path (a virtual keyboard). Compares the Source
/// simulation inside SourceMovement with where the player actually ends up.
/// Started by PlayTestBootstrap.Run. Results are logged with a [SMTEST] prefix.
/// </summary>
public class PlayTestRunner : MonoBehaviour
{
    public int frameRate = 90;
    public string only = "";

    const float U = 0.01905f;
    readonly List<string> report = new List<string>();
    int failures;

    UdonBehaviour movement;
    VRCPlayerApi player;
    Transform playerBody;
    Keyboard keyboard;
    Vector3 prevTarget;
    int fixedSteps;
    public bool trace;

    void FixedUpdate() { fixedSteps++; }

    IEnumerator Start()
    {
        Time.captureDeltaTime = 1f / frameRate;
        Log($"frame rate {frameRate}, fixed dt {Time.fixedDeltaTime}");

        // Wait for ClientSim's player and for Udon to load SourceMovement's program.
        for (int i = 0; i < 600 && (player == null || movement == null); i++)
        {
            yield return null;
            player = Networking.LocalPlayer;
            movement = Loaded(FindUdon("SourceMovement"));
        }
        VRC.SDK3.ClientSim.ClientSimPlayerController controller = null;
        foreach (var c in Resources.FindObjectsOfTypeAll<VRC.SDK3.ClientSim.ClientSimPlayerController>())
            if (c.gameObject.scene.IsValid()) controller = c;
        if (player == null || movement == null || controller == null)
        {
            Fail("setup", $"player {player != null}, SourceMovement {movement != null}, controller {controller != null}");
            Finish();
            yield break;
        }
        playerBody = controller.transform;
        var cc = controller.GetComponent<CharacterController>();
        Log($"ClientSim CharacterController: height {cc.height:F2} m, radius {cc.radius:F2} m, step {cc.stepOffset:F2} m, slope {cc.slopeLimit} deg, skin {cc.skinWidth:F3} m");

        // Batchmode has no focused game view: let device input through anyway.
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        keyboard = InputSystem.AddDevice<Keyboard>("SMTestKeyboard");
        for (int i = 0; i < 30; i++) yield return null;
        // Click through ClientSim's startup menu like a user would; it blocks all input while open.
        foreach (var menu in Resources.FindObjectsOfTypeAll<ClientSimMenu>())
        {
            if (!menu.gameObject.scene.IsValid()) continue;
            menu.WarningAccepted();
            menu.CloseMenu();
        }
        for (int i = 0; i < 10; i++) yield return null;
        Check("setup", (bool)movement.GetProgramVariable("active"), "SourceMovement active on start");

        yield return Run("walk", Walk());
        yield return Run("hud", Hud());
        yield return Run("bhop", Bhop());
        yield return Run("strafe", AirStrafe());
        yield return Run("surf", Surf());
        yield return Run("teleport", Teleports());
        yield return Run("ladder", Ladder());
        yield return Run("legit", Legit());
        yield return Run("timer", Timer());
        Finish();
    }

    IEnumerator Run(string name, IEnumerator test)
    {
        if (only != "" && only != name) yield break;
        Log("---- " + name);
        Keys();
        yield return test;
        Keys();
    }

    // ------------------------------------------------------------------ tests

    IEnumerator Walk()
    {
        yield return Spawn(new Vector3(0, 0, 100), 0f);
        float maxErr = 0f, maxY = 0f;
        Keys(Key.W);
        yield return Frames(2f, () => { maxErr = Mathf.Max(maxErr, TrackError()); maxY = Mathf.Max(maxY, Mathf.Abs(Real().y)); });
        Vector3 p0 = Real();
        yield return Frames(1f, () => { maxErr = Mathf.Max(maxErr, TrackError()); maxY = Mathf.Max(maxY, Mathf.Abs(Real().y)); });
        float realSpeed = Flat(Real() - p0).magnitude;
        Log($"sim speed {Speed():F1} u/s, real speed {realSpeed:F1} u/s, max tracking error beyond one physics step {maxErr:F2} u, max |y| {maxY:F2} u, grounded {OnGround()}");
        Check("walk", Mathf.Abs(Speed() - 250f) < 1f, "sim reaches 250 u/s");
        Check("walk", Mathf.Abs(realSpeed - 250f) < 5f, "player really moves at 250 u/s (got " + realSpeed.ToString("F1") + ")");
        Check("walk", maxErr < 4f, "player follows the simulation (error " + maxErr.ToString("F2") + " u)");
        Check("walk", maxY < 2f, "stays on the floor");

        Keys();
        yield return Frames(1f, null);
        p0 = Real();
        yield return Frames(0.5f, null);
        Log($"after release: sim speed {Speed():F1}, real moved {Flat(Real() - p0).magnitude:F2} u in 0.5 s");
        Check("walk", Speed() < 0.5f && Flat(Real() - p0).magnitude < 1f, "stops when W is released");
    }

    IEnumerator Hud()
    {
        // The walk left the start zone, so the run timer should be running and the speedometer showing.
        yield return Frames(0.1f, null);
        var texts = FindObjectsOfType<TextMeshProUGUI>();
        TextMeshProUGUI speedo = null, timer = null;
        foreach (var t in texts)
        {
            if (t.transform.parent.parent.name == "Speedometer") speedo = t;
            if (t.transform.parent.parent.name == "RunTimer") timer = t;
        }
        Vector3 head = player.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
        Check("hud", speedo != null && speedo.gameObject.activeInHierarchy, "speedometer text exists and is shown");
        if (speedo != null)
        {
            Log($"speedometer text '{speedo.text}', {Vector3.Distance(speedo.transform.position, head):F2} m from head, font size {speedo.fontSize}");
            Check("hud", speedo.text == Mathf.RoundToInt(Speed()).ToString(), "speedometer shows the sim speed");
            Check("hud", Vector3.Distance(speedo.transform.position, head) < 1.5f, "speedometer floats in front of the view");
        }
        Check("hud", timer != null, "timer text exists");
        if (timer != null)
        {
            Log($"timer text '{timer.text}', {Vector3.Distance(timer.transform.position, head):F2} m from head");
            Check("hud", timer.text.StartsWith("[auto] ") || timer.text.StartsWith("[legit] "), "timer is running after leaving the start zone");
        }
    }

    IEnumerator Bhop()
    {
        yield return Spawn(new Vector3(0, 0, 300), 0f);
        int jumps = 0;
        float prevVy = 0f;
        float peak = 0f, maxErr = 0f;
        Keys(Key.W, Key.Space);
        yield return Frames(3f, () =>
        {
            float vy = ((Vector3)movement.GetProgramVariable("velocity")).y;
            if (vy > 150f && prevVy <= 150f) jumps++; // several ticks run per frame, so watch for the jump impulse
            prevVy = vy;
            peak = Mathf.Max(peak, Real().y);
            maxErr = Mathf.Max(maxErr, TrackError());
        });
        Keys();
        Log($"auto bhop: {jumps} jumps in 3 s, real peak height {peak:F1} u (Source: 57), max tracking error beyond one physics step {maxErr:F2} u, speed {Speed():F1}");
        Check("bhop", jumps >= 4, "holding jump keeps hopping");
        Check("bhop", Mathf.Abs(peak - 57f) < 3f, "jump height matches Source");
        Check("bhop", maxErr < 4f, "player follows the simulation in the air");
    }

    IEnumerator AirStrafe()
    {
        yield return Spawn(new Vector3(0, 0, 300), 0f);
        Keys(Key.W);
        yield return Frames(1f, null);
        float yaw = 0f, maxErr = 0f;
        Vector3 p0 = Real();
        for (int i = 0; i < 8; i++)
        {
            bool left = i % 2 == 0;
            float rate = left ? -150f : 150f;
            Keys(Key.Space, left ? Key.A : Key.D);
            if (i == 0) rate *= 0.5f; // first half turn, so the zigzag stays centred
            yield return Frames(0.5f, () => { yaw += rate * Time.deltaTime; SetYaw(yaw); maxErr = Mathf.Max(maxErr, TrackError()); });
        }
        Keys();
        float distance = Flat(Real() - p0).magnitude;
        Log($"air strafe: sim speed {Speed():F1} u/s after 4 s, real distance {distance:F0} u, max tracking error beyond one physics step {maxErr:F2} u");
        Check("strafe", Speed() > 400f, "strafing gains speed");
        Check("strafe", maxErr < 4f, "player follows the simulation while strafing");
    }

    IEnumerator Surf()
    {
        // Left face of the test map's ramp: ridge at x 400, y -256, faces down at 60 degrees towards -x.
        for (int hold = 1; hold >= 0; hold--)
        {
                        yield return Teleport(new Vector3(200, -560, 5000), 0f, false);
            yield return Frames(0.2f, null);
            movement.SetProgramVariable("velocity", new Vector3(0, 0, 1000));
            float y0 = Real().y, maxErr = 0f, maxGap = 0f, minGap = 1e9f;
            bool grounded = false;
            if (hold == 1) Keys(Key.D);
            yield return Frames(1f, () =>
            {
                grounded |= OnGround();
                maxErr = Mathf.Max(maxErr, TrackError());
                float gap = RampGap(Real());
                maxGap = Mathf.Max(maxGap, gap);
                minGap = Mathf.Min(minGap, gap);
            });
            Keys();
            trace = false;
            Vector3 v = (Vector3)movement.GetProgramVariable("velocity");
            float drop = Real().y - y0;
            string what = hold == 1 ? "holding D" : "no input";
            Log($"surf {what}: height change {drop:F0} u in 1 s, speed along ramp {v.z:F0}, real gap to ramp {minGap:F2}..{maxGap:F2} u, max tracking error beyond one physics step {maxErr:F2} u, grounded {grounded}");
            Check("surf", !grounded, "never stands on the ramp (" + what + ")");
            Check("surf", v.z > 990f, "keeps speed along the ramp (" + what + ")");
            Check("surf", maxErr < 4f, "player follows the simulation on the ramp (" + what + ")");
            Check("surf", minGap > -2f && maxGap < 4f, "player stays on the ramp surface (" + what + ")");
            if (hold == 1) Check("surf", Mathf.Abs(drop) < 60f, "holding into the ramp keeps height");
            else Check("surf", drop < -200f, "no input slides down");
        }
    }

    IEnumerator Teleports()
    {
        // Plain TeleportTo while running: velocity resets, simulation follows. VRChat keeps the old velocity for
        // one physics step, so the player can overshoot by that much unless the caller also zeroes the velocity.
        for (int zero = 0; zero <= 1; zero++)
        {
            yield return Spawn(new Vector3(0, 0, 300), 0f);
            Keys(Key.W);
            yield return Frames(1.5f, null);
            Keys();
            yield return null; // input reaches Udon a frame later; teleport while still moving fast but with W up
            yield return null;
            player.TeleportTo(new Vector3(0, 0, 2500) * U, Quaternion.identity);
            if (zero == 1) player.SetVelocity(Vector3.zero);
            yield return Frames(0.5f, null);
            string what = zero == 1 ? "TeleportTo + SetVelocity(0)" : "TeleportTo";
            Log($"{what} 2200 u: real z {Real().z:F1}, sim z {SimOrigin().z:F1}, speed {Speed():F1}");
            float allowed = zero == 1 ? 0.5f : 250f * (Time.fixedDeltaTime + 2f / frameRate);
            Check("teleport", Mathf.Abs(Real().z - 2500f) < allowed, what + " lands on the destination (within " + allowed.ToString("F1") + " u)");
            Check("teleport", Mathf.Abs(Real().z - SimOrigin().z) < 0.5f, what + ": simulation follows the player");
            Check("teleport", Speed() < 1f, what + " over 64 u resets velocity");
        }

        // TeleportPlayer keeping velocity (a portal).
        yield return Spawn(new Vector3(0, 0, 300), 0f);
        Keys(Key.W);
        yield return Frames(1f, null);
        Keys(Key.W, Key.Space);
        yield return WaitAir();
        Keys();
        float before = Speed();
        yield return Teleport(new Vector3(0, 100, 2000), 0f, true);
        float after = Speed(), err = 0f;
        yield return Frames(0.3f, () => err = Mathf.Max(err, TrackError()));
        Log($"TeleportPlayer keepVelocity: speed {before:F1} -> {after:F1}, real z {Real().z:F0} after 0.3 s, max tracking error beyond one physics step {err:F2} u");
        Check("teleport", Mathf.Abs(after - before) < 1f, "TeleportPlayer keeps speed");
        Check("teleport", err < 4f && Real().z > 2050f, "player moves on from the destination");

        // TeleportPlayer resetting velocity.
        yield return Teleport(new Vector3(0, 0, 1000), 0f, false);
        yield return Frames(0.3f, null);
        Log($"TeleportPlayer reset: speed {Speed():F1}, real z {Real().z:F0}");
        Check("teleport", Speed() < 1f && Mathf.Abs(Real().z - 1000f) < 2f, "TeleportPlayer can reset velocity");

        // Short TeleportTo (32 u) while running keeps speed.
        Keys(Key.W);
        yield return Frames(1.5f, null);
        Vector3 here = Real();
        float s0 = Speed();
        player.TeleportTo((here + new Vector3(32, 0, 0)) * U, Quaternion.identity);
        yield return Frames(0.2f, null);
        Keys();
        Log($"short TeleportTo 32 u: speed {s0:F1} -> {Speed():F1}, x {here.x:F1} -> {Real().x:F1}");
        Check("teleport", Speed() > 240f && Real().x - here.x > 28f, "short TeleportTo moves the player and keeps speed");
    }

    IEnumerator Ladder()
    {
        // Test map ladder: face at x = 144 on a 512 unit tower (top at y 512), facing the lane (-x).
        yield return Spawn(new Vector3(60, 0, 500), 90f);
        Keys(Key.W);
        for (int i = 0; i < 2 * frameRate && !(bool)movement.GetProgramVariable("onLadder"); i++) yield return null;
        Check("ladder", (bool)movement.GetProgramVariable("onLadder"), "walking into the ladder grabs it");
        yield return Frames(0.3f, null);
        float y0 = Real().y, maxErr = 0f;
        yield return Frames(1f, () => maxErr = Mathf.Max(maxErr, TrackError()));
        float climb = Real().y - y0;
        Log($"ladder: real climb {climb:F1} u in 1 s (Source: 200), max tracking error beyond one physics step {maxErr:F2} u");
        Check("ladder", Mathf.Abs(climb - 200f) < 5f, "climbs at 200 u/s");
        Check("ladder", maxErr < 4f, "player follows the simulation on the ladder");

        Keys();
        yield return Frames(0.1f, null); // the key release reaches Udon a frame later
        y0 = Real().y;
        yield return Frames(0.5f, null);
        Check("ladder", Mathf.Abs(Real().y - y0) < 1f, "hangs on with no keys");

        Keys(Key.W); // climb until standing on top, then let go (holding on walks off the far side)
        for (int i = 0; i < 4 * frameRate && !(OnGround() && Real().y > 500f); i++) yield return null;
        Keys();
        yield return Frames(0.5f, null);
        Log($"ladder top: real {Real():F1}, grounded {OnGround()}");
        Check("ladder", OnGround() && Mathf.Abs(Real().y - 512f) < 2f && Real().x > 136f, "climbs out onto the top"); // hull over the tower's edge at x 152

        yield return Spawn(new Vector3(60, 0, 500), 90f);
        Keys(Key.W);
        yield return Frames(1f, null);
        Keys(Key.Space);
        for (int i = 0; i < frameRate / 2 && (bool)movement.GetProgramVariable("onLadder"); i++) yield return null;
        Keys();
        Vector3 v = (Vector3)movement.GetProgramVariable("velocity");
        Log($"ladder jump off: velocity {v:F1}");
        Check("ladder", !(bool)movement.GetProgramVariable("onLadder") && v.x < -200f, "jump pushes off the ladder");
        yield return Frames(1.5f, null);
    }

    IEnumerator Legit()
    {
        // The world button (what VR players use) switches auto bhop off.
        UdonBehaviour button = FindUdon("AutoBhopButton");
        button.SendCustomEvent("_interact");
        yield return Frames(0.1f, null);
        string label = Text("AutoBhopButton");
        Log($"after pressing the button: autoBhop {movement.GetProgramVariable("autoBhop")}, label '{label}'");
        Check("legit", !(bool)movement.GetProgramVariable("autoBhop") && label == "Auto bhop: OFF", "button turns auto bhop off");

        yield return Spawn(new Vector3(0, 0, 300), 0f);
        int jumps = 0;
        float prevVy = 0f;
        System.Action count = () =>
        {
            float vy = ((Vector3)movement.GetProgramVariable("velocity")).y;
            if (vy > 150f && prevVy <= 150f) jumps++;
            prevVy = vy;
        };
        Keys(Key.Space);
        yield return Frames(2f, count);
        Check("legit", jumps == 1, "holding jump jumps once, no pogo (" + jumps + ")");
        Keys();
        yield return Frames(0.1f, count);
        Keys(Key.Space);
        yield return Frames(0.2f, count);
        Keys();
        Check("legit", jumps == 2, "a new press jumps again (" + jumps + ")");
        yield return Frames(1f, null);
    }

    IEnumerator Timer()
    {
        // A legit run (auto bhop is still off from the legit test) through the real trigger zones.
        UdonBehaviour timer = FindUdon("RunTimer");
        yield return Spawn(new Vector3(0, 0, 100), 0f);
        Check("timer", !(bool)timer.GetProgramVariable("running"), "not running inside the start zone");
        Keys(Key.W);
        yield return Frames(1.5f, null);
        Keys();
        yield return Frames(1f, null); // stop before teleporting on
        Check("timer", (bool)timer.GetProgramVariable("running"), "starts when leaving the start zone");

        // Through the checkpoint zone, then fall into the reset zone: back to the checkpoint, timer keeps running.
        yield return Teleport(new Vector3(0, 0, 3900), 0f, false);
        yield return Frames(0.2f, null);
        player.TeleportTo(new Vector3(0, -1950, 6000) * U, Quaternion.identity);
        player.SetVelocity(Vector3.zero);
        yield return Frames(0.5f, null);
        Log($"after the reset zone: at {Real():F1}, speed {Speed():F1}, running {timer.GetProgramVariable("running")}");
        Check("timer", Flat(Real() - new Vector3(0, 0, 3900)).magnitude < 2f && Mathf.Abs(Real().y) < 2f, "reset zone sends you to the checkpoint");
        Check("timer", (bool)timer.GetProgramVariable("running"), "timer keeps running after a reset");

        // Into the end zone.
        yield return Teleport(new Vector3(400, -950, 10900), 0f, false);
        yield return Frames(0.3f, null);
        float time = (float)timer.GetProgramVariable("lastTime");
        string hud = Text("RunTimer"), board = Text("Leaderboard (Legit)"), autoBoard = Text("Leaderboard (Auto)");
        Log($"finished: lastTime {time:F3}, HUD '{hud}', legit board '{board.Replace("\n", " | ")}', auto board '{autoBoard.Replace("\n", " | ")}'");
        Check("timer", time > 0f && !(bool)timer.GetProgramVariable("running"), "end zone finishes the run");
        Check("timer", hud.StartsWith("Finished [legit] "), "HUD shows the finished legit time");
        Check("timer", board.Contains(RunTimerFormat(time)) && !autoBoard.Contains(RunTimerFormat(time)), "time is on the legit board only");

        FindUdon("AutoBhopButton").SendCustomEvent("_interact"); // back to auto bhop
        yield return Frames(0.1f, null);
        Check("timer", (bool)movement.GetProgramVariable("autoBhop"), "button turns auto bhop back on");
    }

    // ------------------------------------------------------------------ helpers

    static string RunTimerFormat(float seconds)
    {
        int ms = Mathf.FloorToInt(seconds * 1000f);
        return (ms / 60000) + ":" + ((ms / 1000) % 60).ToString("00") + "." + (ms % 1000).ToString("000");
    }

    static UdonBehaviour Loaded(UdonBehaviour udon)
    {
        try { return udon != null && udon.GetProgramVariable("active") != null ? udon : null; }
        catch (System.NullReferenceException) { return null; } // program not loaded yet
    }

    void Update()
    {
        // Never leave a batchmode editor hanging if a test gets stuck.
        if (!finished && Time.realtimeSinceStartup > 600f)
        {
            Fail("watchdog", "tests did not finish within 10 minutes");
            Finish();
        }
    }

    static UdonBehaviour FindUdon(string objectName)
    {
        foreach (var udon in FindObjectsOfType<UdonBehaviour>())
            if (udon.gameObject.name == objectName) return udon;
        return null;
    }

    /// <summary>Text of the TextMeshPro label on the canvas under the named object.</summary>
    static string Text(string objectName)
    {
        foreach (var t in FindObjectsOfType<TextMeshProUGUI>())
            if (t.transform.parent.parent.name == objectName) return t.text;
        return "";
    }

    IEnumerator Spawn(Vector3 sourcePos, float yaw)
    {
        yield return Teleport(sourcePos, yaw, false);
        yield return Frames(0.3f, null);
    }

    IEnumerator Teleport(Vector3 sourcePos, float yaw, bool keepVelocity)
    {
        movement.SetProgramVariable("__0_position__param", sourcePos * U);
        movement.SetProgramVariable("__0_rotation__param", Quaternion.Euler(0, yaw, 0));
        movement.SetProgramVariable("__0_keepVelocity__param", keepVelocity);
        movement.SendCustomEvent("__0_TeleportPlayer");
        for (int i = 0; i < 2; i++)
        {
            yield return null;
            if (trace) Log($"trace teleport real {Real():F2} origin {SimOrigin():F2} wait {movement.GetProgramVariable("teleportWait")}");
        }
        prevTarget = SimTarget();
    }

    IEnumerator WaitAir()
    {
        for (int i = 0; i < 200 && OnGround(); i++) yield return null;
    }

    IEnumerator Frames(float seconds, System.Action each)
    {
        int n = Mathf.RoundToInt(seconds * frameRate);
        for (int i = 0; i < n; i++)
        {
            int f0 = fixedSteps;
            yield return null;
            each?.Invoke();
            if (trace) Log($"trace f{i} fixed {fixedSteps - f0} real {Real():F2} prevTarget {prevTarget:F2} err {TrackError():F2} gap {RampGap(Real()):F2} simgap {RampGap(SimOrigin()):F2} origin {SimOrigin():F2} wait {movement.GetProgramVariable("teleportWait")} vel {(Vector3)movement.GetProgramVariable("velocity"):F1}");
            prevTarget = SimTarget();
        }
    }

    void Keys(params Key[] keys)
    {
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
    }

    void SetYaw(float yaw)
    {
        playerBody.rotation = Quaternion.Euler(0, yaw, 0);
    }

    Vector3 Real() { return player.GetPosition() / U; }
    Vector3 SimOrigin() { return (Vector3)movement.GetProgramVariable("origin"); }
    Vector3 SimTarget() { return (Vector3)movement.GetProgramVariable("lastTarget"); }
    float Speed() { return Flat((Vector3)movement.GetProgramVariable("velocity")).magnitude; }
    bool OnGround() { return (bool)movement.GetProgramVariable("onGround"); }
    static Vector3 Flat(Vector3 v) { return new Vector3(v.x, 0, v.z); }

    /// <summary>
    /// Where physics put the player vs where the simulation asked for (the previous frame's target), minus one
    /// physics step of travel: ClientSim moves the player in FixedUpdate, so between steps it trails by up to that much.
    /// </summary>
    float TrackError()
    {
        float step = Mathf.Max(Time.fixedDeltaTime, 1f / frameRate);
        float travel = ((Vector3)movement.GetProgramVariable("velocity")).magnitude * step;
        return Mathf.Max(0f, (Real() - prevTarget).magnitude - travel);
    }

    /// <summary>Gap between the 32x72 box hull and the left ramp face (normal (-0.866, 0.5) through the ridge).</summary>
    static float RampGap(Vector3 feet)
    {
        Vector3 n = new Vector3(-0.8660254f, 0.5f, 0);
        Vector3 corner = feet + new Vector3(16, 0, 0);
        return Vector3.Dot(corner - new Vector3(400, -256, 0), n);
    }

    void Check(string test, bool ok, string what)
    {
        string line = (ok ? "PASS  " : "FAIL  ") + test + ": " + what;
        if (!ok) failures++;
        report.Add(line);
        Log(line);
    }

    void Fail(string test, string what) { Check(test, false, what); }

    static void Log(string s) { Debug.Log("[SMTEST] " + s); }

    bool finished;

    void Finish()
    {
        finished = true;
        var sb = new StringBuilder();
        foreach (string line in report) sb.AppendLine(line);
        sb.AppendLine(failures == 0 ? "ALL PASSED" : failures + " FAILED");
        Log("\n" + sb);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(failures == 0 ? 0 : 1);
#endif
    }
}
