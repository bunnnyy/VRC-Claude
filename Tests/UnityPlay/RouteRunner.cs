using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using VRC.SDK3.ClientSim;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// A bot that bhops an imported map from start to finish with SourceMovement and ClientSim, using only the inputs a
/// player has: movement keys, jump held (auto bhop) and the view direction. Started by PlayTestBootstrap.RunRoute.
///
/// The route is a list of sections (polylines between teleports, Source x/y). Before the run it raycasts the
/// corridor along each section for safe landing spots: floor or block tops the hull can stand on without touching
/// a teleport. At each takeoff it picks the furthest spot it can reach at its current speed with a clear flight
/// path, then air strafes onto it: every tick it picks the wish direction that brings the velocity closest to
/// "distance left / time left", and the key + view direction that give that wish direction.
///
/// With -smRecord dir it renders a first person camera (smoothed view direction) to dir/f00000.jpg... every other
/// frame, and writes dir/hud.txt (speed and the map timer's text per frame) for the overlay.
/// </summary>
public class RouteRunner : MonoBehaviour
{
    public int frameRate = 100;
    public string recordDir = "";
    public int startSection; // debugging: start at the beginning of this section
    const float U = 0.01905f;
    const float Gravity = 800f;
    const float AirCap = 30f;

    // bhop_eazy_v2 (31K4L): five colour sections of four lanes, a wall teleport at the end of each section.
    static readonly Vector2[][] Route =
    {
        new[] { V(32, 0), V(1856, 0), V(1856, 416), V(128, 416), V(128, 832), V(1856, 832), V(1856, 1248), V(-80, 1248) },
        new[] { V(2368, 0), V(4096, 0), V(4096, 416), V(2368, 416), V(2368, 832), V(4096, 832), V(4096, 1248), V(2160, 1248) },
        new[] { V(128, 1888), V(1856, 1888), V(1856, 2304), V(128, 2304), V(128, 2720), V(1856, 2720), V(1856, 3136), V(-80, 3136) },
        new[] { V(2368, 1888), V(4096, 1888), V(4096, 2304), V(2368, 2304), V(2368, 2720), V(4096, 2720), V(4096, 3136), V(2160, 3136) },
        new[] { V(4608, 0), V(6336, 0), V(6336, 416), V(4608, 416), V(4608, 832), V(6336, 832), V(6336, 1248), V(4400, 1248) },
    };
    static readonly Rect EndZone = new Rect(-2256, 4512, 2256 - 1728, 5056 - 4512); // flat coords (unity x, z)

    /// <summary>Source x, y to flat Unity-axis coordinates (x = -y, z = x), Source units.</summary>
    static Vector2 V(float x, float y) { return new Vector2(-y, x); }

    struct Spot { public Vector3 p; public float s, lat; public bool deep, edge; public int seg; public float need; }

    readonly List<List<Spot>> spots = new List<List<Spot>>();
    readonly List<float[]> lengths = new List<float[]>();
    UdonBehaviour movement, timer;
    VRCPlayerApi player;
    Transform playerBody;
    Keyboard keyboard;
    int layers;
    bool finished;

    int section;
    float progress;
    bool hopping, hasTarget, wasOnGround;
    Spot target;
    int hops, fails, frame;
    float viewYaw, camYaw, camYawVel;
    Camera cam;
    RenderTexture rt;
    Texture2D shot;
    readonly StringBuilder hud = new StringBuilder();
    int shotNumber;

    IEnumerator Start()
    {
        Time.captureDeltaTime = 1f / frameRate;
        for (int i = 0; i < 600 && (player == null || movement == null); i++)
        {
            yield return null;
            player = Networking.LocalPlayer;
            movement = Loaded(FindUdon("SourceMovement"));
        }
        foreach (var c in Resources.FindObjectsOfTypeAll<ClientSimPlayerController>())
            if (c.gameObject.scene.IsValid()) playerBody = c.transform;
        foreach (var u in FindObjectsOfType<UdonBehaviour>())
            if (u.GetProgramVariable("categoryVariable") as string == "autoBhop") timer = u;
        if (player == null || movement == null || playerBody == null) { Log("setup failed"); Finish(1); yield break; }
        layers = ((LayerMask)movement.GetProgramVariable("collisionLayers")).value;

        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        keyboard = InputSystem.AddDevice<Keyboard>("RouteKeyboard");
        for (int i = 0; i < 30; i++) yield return null;
        foreach (var menu in Resources.FindObjectsOfTypeAll<ClientSimMenu>())
            if (menu.gameObject.scene.IsValid()) { menu.WarningAccepted(); menu.CloseMenu(); }
        for (int i = 0; i < 10; i++) yield return null;

        float t0 = Time.realtimeSinceStartup;
        int total = 0;
        foreach (var line in Route) { var list = FindSpots(line); Plan(list); spots.Add(list); total += list.Count; }
        Log($"{total} landing spots in {Route.Length} sections ({Time.realtimeSinceStartup - t0:F1} s)");

        // Start in the start zone facing down the first lane.
        section = startSection;
        Vector2 a = Route[section][0], b = Route[section][1];
        viewYaw = camYaw = Yaw(b - a);
        movement.SetProgramVariable("__0_position__param", new Vector3(a.x, 49f, a.y) * U);
        movement.SetProgramVariable("__0_rotation__param", Quaternion.Euler(0, viewYaw, 0));
        movement.SetProgramVariable("__0_keepVelocity__param", false);
        movement.SendCustomEvent("__0_TeleportPlayer");
        for (int i = 0; i < frameRate; i++) yield return null;
        yield return Calibrate();
        movement.SendCustomEvent("__0_TeleportPlayer"); // back to the start, standing still
        for (int i = 0; i < frameRate / 2; i++) yield return null;

        if (recordDir != "") SetupCamera();
        Vector3 last = Origin();
        float runStart = Time.time, sectionStart = Time.time;
        while (true)
        {
            Vector3 pos = Origin();
            if ((pos - last).magnitude > 150f)
            {
                int next = section + 1;
                if (next < Route.Length && (Flat(pos) - Route[next][0]).magnitude < 96f)
                {
                    Log($"section {section + 1} done in {Time.time - sectionStart:F2} s");
                    section = next;
                    sectionStart = Time.time;
                    progress = 0f;
                }
                else if (EndZone.Contains(Flat(pos)))
                {
                    Log($"section {section + 1} done in {Time.time - sectionStart:F2} s; in the end zone");
                    break;
                }
                else
                {
                    fails++;
                    Log($"FAIL section {section + 1}: teleported back from {Flat(last)} (s {progress:F0}) to {Flat(pos)}, target was {Flat(target.p)} s {target.s:F0}");
                    progress = Project(Flat(pos), -1f);
                    DumpRecent();
                    if (fails >= 5) { Log("FAIL: giving up after 5 fails"); break; }
                }
                hopping = false;
                hasTarget = false;
                runOnLanding = false;
            }
            last = pos;
            Step(pos);
            yield return null;
            frame++;
            if (recordDir != "" && frame % 2 == 0) Capture();
            if (hops != lastHops) { lastHops = hops; lastHopTime = Time.time; }
            if (progress > bestProgress + 32f || section != progressSection) { bestProgress = progress; progressSection = section; progressTime = Time.time; }
            if (Time.time - progressTime > 20f) { Log("FAIL: stuck, no progress for 20 s"); DumpRecent(); Probe(Origin()); fails++; break; }
            if (Time.time - lastHopTime > 10f) { Log("FAIL: stuck, no hop for 10 s"); DumpRecent(); Probe(Origin()); fails++; break; }
            if (Time.time - runStart > 600f) { Log("FAIL: no finish within 600 s of game time"); fails++; break; }
        }
        Keys();
        for (int i = 0; i < frameRate * 2; i++)
        {
            yield return null;
            frame++;
            if (recordDir != "" && frame % 2 == 0) Capture();
        }
        string time = timer != null ? Label() : "";
        Log($"run over: {hops} hops, {fails} fails, timer '{time}', {shotNumber} frames recorded");
        if (recordDir != "") System.IO.File.WriteAllText(System.IO.Path.Combine(recordDir, "hud.txt"), hud.ToString());
        Finish(fails == 0 ? 0 : 1);
    }

    /// <summary>One frame of the bot: run up to speed on the ground, then bhop from landing spot to landing spot.</summary>
    string lastKeys = "";
    int lastHops, progressSection;
    float bestProgress, progressTime;
    float lastHopTime;

    void Step(Vector3 pos)
    {
        Vector3 vel = (Vector3)movement.GetProgramVariable("velocity");
        bool onGround = (bool)movement.GetProgramVariable("onGround");
        recent[frame % recent.Length] = $"f{frame} pos {pos:F1} vel {vel:F1} ground {onGround} hop {hopping} keys {lastKeys} yaw {viewYaw:F0} target {(hasTarget ? target.p.ToString("F0") : "-")}";
        Vector2 here = Flat(pos), v = new Vector2(vel.x, vel.z);
        progress = Project(here, progress);
        var line = Route[section];
        float lookYaw = Yaw(PointAt(line, progress + 160f) - here);

        if (!hopping)
        {
            // From a standstill (start, a teleport): run until fast, or until the floor ahead ends.
            Record(0, lookYaw);
            heldKey = 0;
            SetYaw(viewYaw);
            Vector2 ahead = here + Dir(viewYaw) * 40f;
            if (v.magnitude > 240f || !Safe(new Vector3(ahead.x, pos.y, ahead.y), out _)) { hopping = true; Keys(Key.W, Key.Space); }
            else Keys(Key.W);
            wasOnGround = onGround;
            return;
        }

        if (wasOnGround && !onGround)
        {
            hops++;
            hasTarget = PickTarget(pos, vel, out target, out flight);
            takeoffTime = Time.time;
            if (!hasTarget) Log($"no landing spot from {here} at {v.magnitude:F0} u/s (s {progress:F0}), following the route line");
        }
        wasOnGround = onGround;
        if (onGround && runOnLanding)
        {
            runOnLanding = false; hopping = false; hasTarget = false;
            Record(0, lookYaw);
            Keys(Key.W);
            return;
        }

        // Velocity wanted now: onto the target, arriving when we come down to its height (along the curve first, for a
        // flight around an obstacle). No spot to aim for (the end of a section): follow the route line, to go through
        // doorways straight.
        Vector2 aim = hasTarget ? new Vector2(target.p.x, target.p.z) : PointAt(line, progress + 96f);
        float h = hasTarget ? target.p.y : pos.y;
        float tLeft = FallTime(pos.y - h, vel.y);
        if (hasTarget && (aim - here).magnitude > 1f) aim += (aim - here).normalized * 6f; // land a little long, never short
        Vector2 want = hasTarget ? (aim - here) / Mathf.Max(tLeft, 0.02f) : (aim - here).normalized * Mathf.Max(v.magnitude, 250f);
        float f = hasTarget ? (Time.time - takeoffTime) / flight.time : 1f;
        if (hasTarget && flight.curved && f < 0.65f)
            want = (Curve(flight, Mathf.Min(f + 0.1f, 1f)) - here) / (0.1f * flight.time);
        // Close to landing, or already over the spot (spots are 28+ units inside their surface): stop steering, or
        // "distance left / time left" swings around and brakes hard.
        else if (hasTarget && (float.IsNaN(tLeft) || tLeft < 0.12f || (aim - here).magnitude < 24f)) want = v;

        // Too slow to strafe up to speed: land without jumping and run up again (to 240, or to the edge).
        if (!onGround && v.magnitude < 120f && vel.y < 0f) runOnLanding = true;
        Strafe(v, want, aim - here, !runOnLanding);
    }

    // ------------------------------------------------------------------ strafing

    // Air strafing like a player: A or D held (A pushes to the left of the view, D to the right), the view turning
    // the same way, roughly along the direction of travel. Sv_airaccelerate 1000: each tick the velocity along the
    // wish direction goes up to 30 u/s. Keys and view changes reach the movement a few frames later (measured by
    // Calibrate): decisions are queued so both arrive together, and planned from the velocity they will act on.
    const int MinHold = 10;            // frames a strafe key stays down once pressed (no flicker)
    int keyLag = 1, yawLag = 1;        // frames from a key press / view change to the velocity changing
    int heldKey, heldFrames;           // the current decision: -1 A, 0 none, 1 D
    readonly int[] keyQueue = new int[32];
    readonly float[] yawQueue = new float[32];
    float appliedYaw;

    void Strafe(Vector2 v, Vector2 want, Vector2 toAim, bool jump)
    {
        int lag = Mathf.Max(keyLag, yawLag);
        Vector2 vp = v;
        for (int k = lag - 1; k >= 1; k--) // decided, not yet in the velocity
        {
            int f = frame - k;
            if (f >= 0 && keyQueue[f % 32] != 0) vp = PushAt(vp, yawQueue[f % 32] + 90f * keyQueue[f % 32]);
        }
        float look = vp.magnitude > 50f ? Yaw(vp) : toAim.magnitude > 1f ? Yaw(toAim) : viewYaw;
        bool mayChange = heldFrames >= MinHold || heldKey == 0;
        int bestKey = heldKey;
        float bestYaw = look, bestScore = float.MaxValue;
        if (mayChange && (want - vp).magnitude <= 4f) { bestKey = 0; bestScore = 0f; }
        else
        {
            if (mayChange) { bestKey = 0; bestScore = Best2(vp, want); }
            for (int key = -1; key <= 1; key += 2)
            {
                if (!mayChange && key != heldKey) continue;
                for (int i = 0; i < Directions; i++)
                {
                    float yaw = i * 360f / Directions;
                    float dev = Mathf.Abs(Mathf.DeltaAngle(yaw, look));
                    if (dev > 100f) continue; // never look backwards
                    float score = Best2(PushAt(vp, yaw + 90f * key), want) + 0.02f * dev + (key != heldKey ? 0.5f : 0f);
                    if (score < bestScore) { bestScore = score; bestKey = key; bestYaw = yaw; }
                }
            }
        }
        if (bestKey != heldKey) { heldKey = bestKey; heldFrames = 0; } else heldFrames++;
        if (bestKey == 0) bestYaw = Mathf.MoveTowardsAngle(viewYaw, look, 360f * Time.deltaTime);
        Record(bestKey, bestYaw);

        // Send this frame's share: the key and the view of the decisions that should arrive together.
        int keyFrame = frame - (lag - keyLag), yawFrame = frame - (lag - yawLag);
        int key2 = keyFrame >= 0 ? keyQueue[keyFrame % 32] : 0;
        float yaw2 = yawFrame >= 0 ? yawQueue[yawFrame % 32] : bestYaw;
        SetYaw(yaw2);
        if (key2 < 0) { if (jump) Keys(Key.Space, Key.A); else Keys(Key.A); }
        else if (key2 > 0) { if (jump) Keys(Key.Space, Key.D); else Keys(Key.D); }
        else if (jump) Keys(Key.Space); else Keys();
    }

    /// <summary>This frame's decision (key -1 A, 0 none, 1 D, and the view), for the queues.</summary>
    void Record(int key, float yaw)
    {
        keyQueue[frame % 32] = key;
        yawQueue[frame % 32] = yaw;
        viewYaw = yaw;
    }

    /// <summary>
    /// Measures input lag: jump in place, press D in the air (view fixed) and count frames until the velocity moves;
    /// then turn the view 90 degrees with D held and count frames until the push turns.
    /// </summary>
    IEnumerator Calibrate()
    {
        float yaw0 = viewYaw;
        SetYaw(yaw0);
        Keys(Key.Space);
        for (int i = 0; i < 50 && ((Vector3)movement.GetProgramVariable("velocity")).y < 100f; i++) yield return null;
        Keys();
        for (int i = 0; i < 3; i++) yield return null;
        Keys(Key.D);
        keyLag = 0;
        for (int i = 1; i <= 8 && keyLag == 0; i++)
        {
            yield return null;
            if (Flat((Vector3)movement.GetProgramVariable("velocity")).magnitude > 5f) keyLag = i;
        }
        for (int i = 0; i < 3; i++) yield return null;
        Vector2 before = Flat((Vector3)movement.GetProgramVariable("velocity"));
        SetYaw(yaw0 + 90f);
        yawLag = 0;
        for (int i = 1; i <= 8 && yawLag == 0; i++)
        {
            yield return null;
            if ((Flat((Vector3)movement.GetProgramVariable("velocity")) - before).magnitude > 5f) yawLag = i;
        }
        Keys();
        for (int i = 0; i < 100 && !(bool)movement.GetProgramVariable("onGround"); i++) yield return null;
        SetYaw(yaw0);
        Log($"input lag: keys {keyLag} frames, view {yawLag} frames");
        if (keyLag == 0 || yawLag == 0) { Log("lag calibration incomplete, assuming 1 frame"); keyLag = Mathf.Max(keyLag, 1); yawLag = Mathf.Max(yawLag, 1); }
    }

    bool runOnLanding;
    float takeoffTime;
    Flight flight;

    static Vector2 PushAt(Vector2 v, float yaw)
    {
        Vector2 w = Dir(yaw);
        float add = AirCap - Vector2.Dot(v, w);
        return add > 0f ? v + w * add : v;
    }

    const int Directions = 120;

    /// <summary>Velocity after one air tick wishing in direction i (unchanged if that adds nothing).</summary>
    static Vector2 Push(Vector2 v, int i)
    {
        Vector2 w = Dir(i * 360f / Directions);
        float add = AirCap - Vector2.Dot(v, w);
        return add > 0f ? v + w * add : v;
    }

    /// <summary>Smallest error to `want` after one more tick (any direction, or none).</summary>
    static float Best2(Vector2 v, Vector2 want)
    {
        float best = (want - v).magnitude;
        for (int i = 0; i < Directions; i++) best = Mathf.Min(best, (want - Push(v, i)).magnitude);
        return best;
    }

    /// <summary>
    /// The landing spot to aim for from a takeoff: furthest along the section, reachable at about the current speed
    /// (strafing can add a little), not much slower than now, with a flight path that hits nothing.
    /// </summary>
    bool PickTarget(Vector3 pos, Vector3 vel, out Spot best, out Flight path)
    {
        best = default;
        path = default;
        var list = spots[section];
        Vector2 here = Flat(pos);
        float speed = new Vector2(vel.x, vel.z).magnitude;
        var options = new List<(float score, Spot spot, float need, float time)>();
        for (int pass = 0; pass < 2 && options.Count == 0; pass++)
        foreach (var s in list)
        {
            if (s.s < progress + 24f || s.s > progress + 700f) continue;
            float time = FallTime(pos.y - s.p.y, vel.y);
            if (float.IsNaN(time) || time < 0.2f) continue;
            float need = (new Vector2(s.p.x, s.p.z) - here).magnitude / time;
            // Can't get there, or (first pass) would land too slow to go on from there.
            if (need > Reach(speed, time) || (pass == 0 && need < s.need)) continue;
            // Furthest along, near the middle of the lane, well inside a surface, and without braking (speed lost
            // braking has to be strafed back).
            float score = s.s - 0.5f * Mathf.Abs(s.lat) - (s.deep ? 0f : s.edge ? 400f : 250f) - 3f * Mathf.Max(0f, 0.9f * speed - need)
                + 1.5f * Mathf.Clamp(need - speed, 0f, 60f); // and building speed for the long gaps
            options.Add((score, s, need, time));
        }
        options.Sort((x, y) => y.score.CompareTo(x.score));
        int tried = 0;
        foreach (var o in options)
            if (FindFlight(pos, vel.y, o.spot.p, o.time, tried++ < 12, out Flight f) && f.length / o.time <= Reach(speed, o.time))
            {
                best = o.spot;
                path = f;
                Log($"hop {hops} s {progress:F0} -> {o.spot.s:F0} speed {speed:F0} need {f.length / o.time:F0} deep {o.spot.deep}{(f.curved ? " curved" : "")} of {options.Count}");
                return true;
            }
        // Nothing: say why, for the nearest spots ahead.
        int shown = 0;
        foreach (var sp in list)
        {
            if (sp.s < progress + 24f || shown >= 12) continue;
            float time = FallTime(pos.y - sp.p.y, vel.y);
            if (float.IsNaN(time)) continue;
            float need = (new Vector2(sp.p.x, sp.p.z) - here).magnitude / time;
            Log($"  rejected {sp.p:F0} s {sp.s:F0} edge {sp.edge}: need {need:F0} reach {Reach(speed, time):F0} plan {sp.need:F0} time {time:F2} clear {FindFlight(pos, vel.y, sp.p, time, true, out _)}");
            shown++;
        }
        return false;
    }

    /// <summary>Average speed reachable over a hop of `time` s from `speed`: strafing adds 30 u/s sideways per tick,
    /// so the speed grows to about sqrt(v^2 + 900 * ticks). Counts on most of it.</summary>
    static float Reach(float speed, float time)
    {
        return 0.5f * (speed + Mathf.Sqrt(speed * speed + 900f * 100f * time)) * 0.9f;
    }

    struct Flight { public Vector2 a, c, b; public float time, length; public bool curved; }

    /// <summary>Where a flight is at `u` (0..1 of its time): a quadratic curve from a to b with control point c.</summary>
    static Vector2 Curve(Flight f, float u) { return (1f - u) * (1f - u) * f.a + 2f * u * (1f - u) * f.c + u * u * f.b; }

    /// <summary>
    /// A flight the hull can make from `from` (moving up at vy) to `to` in `time`: straight, or (if `curves`)
    /// bending out up to 64 units to either side around an obstacle, like strafing around bhop_eazy_v2's pillars.
    /// </summary>
    bool FindFlight(Vector3 from, float vy, Vector3 to, float time, bool curves, out Flight flight)
    {
        Vector2 a = Flat(from), b = Flat(to), mid = (a + b) * 0.5f, side = new Vector2((b - a).y, -(b - a).x).normalized;
        flight = new Flight { a = a, b = b, c = mid, time = time };
        for (int k = 0; k <= (curves ? 8 : 0); k++)
        {
            float off = 16f * ((k + 1) / 2) * (k % 2 == 0 ? -1f : 1f); // 0, 16, -16, 32, -32 ... 64, -64
            flight.c = mid + side * (2f * off); // the curve passes `off` to the side at its middle
            flight.curved = k != 0;
            if (!ClearFlight(from.y, vy, flight)) continue;
            flight.length = 0f;
            for (int i = 1; i <= 12; i++) flight.length += (Curve(flight, i / 12f) - Curve(flight, (i - 1) / 12f)).magnitude;
            return true;
        }
        return false;
    }

    /// <summary>The hull along a flight: no solid in the way (before landing), no teleport touched. Curved flights keep
    /// 2 units more clearance, since strafing won't follow the curve exactly.</summary>
    bool ClearFlight(float y0, float vy, Flight f)
    {
        float half = f.curved ? 17.5f : 15.5f;
        for (int i = 1; i <= 12; i++)
        {
            float t = f.time * i / 12f;
            Vector2 p = Curve(f, i / 12f);
            float y = y0 + vy * t - 0.5f * Gravity * t * t;
            var center = new Vector3(p.x, y + 37f, p.y) * U;
            if (i < 12 && Physics.CheckBox(center, new Vector3(half, 35.5f, half) * U, Quaternion.identity, layers, QueryTriggerInteraction.Ignore)) return false;
            if (TouchesTeleport(center)) return false;
        }
        return true;
    }

    /// <summary>
    /// Works backwards from the end of the section: for each spot, the slowest arrival that still lets the run go
    /// on (hop to a later spot in the same or the next segment, arriving there fast enough for it). Spots near the
    /// end need nothing; unreachable ones get infinity.
    /// </summary>
    void Plan(List<Spot> list)
    {
        if (list.Count == 0) return;
        float jump = (float)movement.GetProgramVariable("jumpImpulse");
        float lastS = list[list.Count - 1].s;
        for (int a = list.Count - 1; a >= 0; a--)
        {
            var A = list[a];
            if (A.s >= lastS - 64f) { A.need = 0f; list[a] = A; continue; }
            hopsFrom.Clear();
            for (int b = a + 1; b < list.Count && list[b].s <= A.s + 600f; b++)
            {
                var B = list[b];
                if (B.s < A.s + 24f || B.seg > A.seg + 1 || float.IsInfinity(B.need)) continue;
                float time = FallTime(A.p.y - B.p.y, jump);
                if (float.IsNaN(time) || time < 0.2f) continue;
                float need = new Vector2(B.p.x - A.p.x, B.p.z - A.p.z).magnitude / time;
                if (need < B.need) continue; // we'd land there too slow to go on
                hopsFrom.Add((MinSpeed(need, time), b, time));
            }
            // The easiest hop whose flight path is clear (glass panels and walls across lanes).
            hopsFrom.Sort((x, y) => x.v.CompareTo(y.v));
            A.need = float.PositiveInfinity;
            for (int h = 0; h < hopsFrom.Count && h < 40; h++)
            {
                var B = list[hopsFrom[h].b];
                float time = hopsFrom[h].time;
                if (FindFlight(A.p, jump, B.p, time, h < 6, out Flight f)) { A.need = MinSpeed(f.length / time, time); break; }
            }
            list[a] = A;
        }
    }

    readonly List<(float v, int b, float time)> hopsFrom = new List<(float v, int b, float time)>();

    /// <summary>The slowest takeoff whose Reach covers an average of `need` over `time` (inverse of Reach).</summary>
    static float MinSpeed(float need, float time)
    {
        float k = need / 0.45f, c = 90000f * time;
        return Mathf.Max(0f, (k * k - c) / (2f * k));
    }

    /// <summary>Landing spots along a section: raycast a grid in the corridor of each segment.</summary>
    List<Spot> FindSpots(Vector2[] line)
    {
        var list = new List<Spot>();
        float s0 = 0f;
        for (int i = 0; i + 1 < line.Length; i++)
        {
            Vector2 a = line[i], b = line[i + 1], d = (b - a).normalized, side = new Vector2(d.y, -d.x);
            float len = (b - a).magnitude;
            for (float along = 0f; along <= len; along += 16f)
                for (float lat = -176f; lat <= 176f; lat += 8f)
                {
                    Vector2 p = a + d * along + side * lat;
                    if (Safe(new Vector3(p.x, 300f, p.y), out float top))
                        list.Add(new Spot { p = new Vector3(p.x, top, p.y), s = s0 + along, lat = lat, deep = Deep(p, top), seg = i });
                    else if (Overhang(p, out top))
                        list.Add(new Spot { p = new Vector3(p.x, top, p.y), s = s0 + along, lat = lat, edge = true, seg = i });
                }
            s0 += len;
        }
        return list;
    }

    /// <summary>Floor under `p` that the hull can stand on (12 units in from any edge) without touching a teleport.</summary>
    bool Safe(Vector3 p, out float top)
    {
        top = 0f;
        Vector3 from = new Vector3(p.x, p.y > 200f ? p.y : p.y + 8f, p.z) * U;
        if (!Physics.Raycast(from, Vector3.down, out RaycastHit hit, 600f * U, layers, QueryTriggerInteraction.Ignore) || hit.normal.y < 0.7f) return false;
        top = hit.point.y / U;
        for (int k = 0; k < 4; k++)
        {
            Vector2 o = Dir(k * 90f) * 12f;
            if (!Physics.Raycast(from + new Vector3(o.x, 0, o.y) * U, Vector3.down, out RaycastHit h2, 600f * U, layers, QueryTriggerInteraction.Ignore)
                || Mathf.Abs(h2.point.y / U - top) > 1f) return false;
        }
        var center = new Vector3(p.x, top + 37f, p.z) * U;
        return !Physics.CheckBox(center, new Vector3(15.5f, 35.5f, 15.5f) * U, Quaternion.identity, layers, QueryTriggerInteraction.Ignore)
            && !TouchesTeleport(center);
    }

    /// <summary>
    /// The hull hanging over the edge of a surface: floor 10 units to one side (the hull still stands on it), the
    /// hull clear of walls and teleports. For squeezing past obstacles beside a block (bhop_eazy_v2's glass panels).
    /// </summary>
    bool Overhang(Vector2 p, out float top)
    {
        top = 0f;
        for (int k = 0; k < 4; k++)
        {
            Vector2 o = p + Dir(k * 90f) * 10f;
            if (!Physics.Raycast(new Vector3(o.x, 300f, o.y) * U, Vector3.down, out RaycastHit hit, 600f * U, layers, QueryTriggerInteraction.Ignore)
                || hit.normal.y < 0.7f) continue;
            top = hit.point.y / U;
            var center = new Vector3(p.x, top + 37f, p.y) * U;
            if (!Physics.CheckBox(center, new Vector3(15.5f, 35.5f, 15.5f) * U, Quaternion.identity, layers, QueryTriggerInteraction.Ignore)
                && !TouchesTeleport(center)) return true;
        }
        return false;
    }

    /// <summary>Same floor at least 28 units out in 8 directions: a block centre or well inside a platform.</summary>
    bool Deep(Vector2 p, float top)
    {
        for (int k = 0; k < 8; k++)
        {
            Vector2 o = p + Dir(k * 45f) * 28f;
            if (!Physics.Raycast(new Vector3(o.x, 300f, o.y) * U, Vector3.down, out RaycastHit hit, 600f * U, layers, QueryTriggerInteraction.Ignore)
                || Mathf.Abs(hit.point.y / U - top) > 1f) return false;
        }
        return true;
    }

    /// <summary>What the movement's ground trace sees under the hull at `pos` (debugging a stuck player).</summary>
    void Probe(Vector3 pos)
    {
        var half = new Vector3(16f, 36f, 16f) * U;
        var center = (pos + new Vector3(0, 36f + 2f, 0)) * U;
        if (Physics.BoxCast(center, half, Vector3.down, out RaycastHit hit, Quaternion.identity, 4f * U, layers, QueryTriggerInteraction.Ignore))
        {
            Log($"probe: box cast down hits {hit.collider.name} ({hit.collider.GetType().Name}) at {hit.point / U:F2} normal {hit.normal:F3} distance {hit.distance / U:F2}");
            float back = 0.25f * U;
            if (Physics.Raycast(hit.point + hit.normal * back, -hit.normal, out RaycastHit f1, back * 2f, layers, QueryTriggerInteraction.Ignore))
                Log($"probe: ray along reported normal -> {f1.normal:F3}");
            else Log("probe: ray along reported normal misses");
            Vector3 c = center + Vector3.down * hit.distance, to = hit.point - c;
            if (Physics.Raycast(c, to.normalized, out RaycastHit f2, to.magnitude + back, layers, QueryTriggerInteraction.Ignore))
                Log($"probe: ray from hull centre -> {f2.normal:F3} at {f2.point / U:F2}");
            else Log("probe: ray from hull centre misses");
        }
        else Log("probe: box cast down hits nothing");
        Log($"probe: real player at {player.GetPosition() / U:F3}, velocity {player.GetVelocity() / U:F2}, sim lastTarget {(Vector3)movement.GetProgramVariable("lastTarget"):F3}");
        var cc = playerBody.GetComponent<CharacterController>();
        if (cc != null) Log($"probe: CharacterController grounded {cc.isGrounded}, radius {cc.radius / U:F1} u, height {cc.height / U:F1} u, center {cc.center / U:F1}, skin {cc.skinWidth / U:F2} u, step {cc.stepOffset / U:F1} u");
        var hh = (Vector3)movement.GetProgramVariable("hullHalf");
        var hc = (Vector3)movement.GetProgramVariable("hullCenter");
        Log($"probe: sim hullHalf {hh:F3} hullCenter {hc:F3}, last trace fraction {movement.GetProgramVariable("trFraction")} normal {(Vector3)movement.GetProgramVariable("trNormal"):F3}");
        var o = Origin();
        foreach (var hit2 in Physics.BoxCastAll((o + hc) * U, hh * U, Vector3.down, Quaternion.identity, 2.25f * U, layers, QueryTriggerInteraction.Ignore))
            Log($"probe: sim-style cast hits {hit2.collider.name} at {hit2.point / U:F2} normal {hit2.normal:F3} distance {hit2.distance / U:F3}");
        foreach (var c in Physics.OverlapBox((o + hc) * U, hh * U, Quaternion.identity, layers, QueryTriggerInteraction.Ignore))
            Log($"probe: hull overlaps {c.name} ({c.GetType().Name}) bounds {c.bounds.min / U:F1}..{c.bounds.max / U:F1}");
        foreach (var d in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right })
            if (Physics.BoxCast((o + hc) * U, hh * U * 0.999f, d, out RaycastHit side, Quaternion.identity, 4f * U, layers, QueryTriggerInteraction.Ignore))
                Log($"probe: sideways {d} hits {side.collider.name} at {side.point / U:F2} normal {side.normal:F3} distance {side.distance / U:F2}");
        Log($"probe: pushVelocity {(Vector3)movement.GetProgramVariable("pushVelocity"):F1} onLadder {movement.GetProgramVariable("onLadder")} waterLevel {movement.GetProgramVariable("waterLevel")}");
        Log($"probe: movement onGround {movement.GetProgramVariable("onGround")} origin {Origin():F3} velocity {(Vector3)movement.GetProgramVariable("velocity"):F2}");
    }

    static bool TouchesTeleport(Vector3 center)
    {
        foreach (var c in Physics.OverlapBox(center, new Vector3(16f, 36f, 16f) * U, Quaternion.identity, ~0, QueryTriggerInteraction.Collide))
            if (c.isTrigger && c.GetComponent<SourceMapTeleport>() != null) return true;
        return false;
    }

    /// <summary>Time until the feet come down to `above` units lower than now, moving up at vy. NaN if never.</summary>
    static float FallTime(float above, float vy)
    {
        float disc = vy * vy + 2f * Gravity * above;
        if (disc < 0f) return float.NaN;
        return (vy + Mathf.Sqrt(disc)) / Gravity;
    }

    /// <summary>Distance along the section of the closest point to `p` near the current progress (any, if negative).</summary>
    float Project(Vector2 p, float near)
    {
        var line = Route[section];
        float best = 1e9f, bestS = near < 0 ? 0 : near, s0 = 0f;
        for (int i = 0; i + 1 < line.Length; i++)
        {
            Vector2 a = line[i], b = line[i + 1];
            float len = (b - a).magnitude;
            float t = Mathf.Clamp(Vector2.Dot(p - a, b - a) / (len * len), 0f, 1f);
            float s = s0 + t * len, d = (a + (b - a) * t - p).magnitude;
            if ((near < 0 || (s > near - 300f && s < near + 600f)) && d < best) { best = d; bestS = s; }
            s0 += len;
        }
        return near < 0 ? bestS : Mathf.Max(bestS, near - 300f);
    }

    static Vector2 PointAt(Vector2[] line, float s)
    {
        for (int i = 0; i + 1 < line.Length; i++)
        {
            float len = (line[i + 1] - line[i]).magnitude;
            if (s <= len) return line[i] + (line[i + 1] - line[i]) * (s / len);
            s -= len;
        }
        return line[line.Length - 1];
    }

    // ------------------------------------------------------------------ recording

    void SetupCamera()
    {
        System.IO.Directory.CreateDirectory(recordDir);
        cam = new GameObject("RouteCamera").AddComponent<Camera>();
        cam.fieldOfView = 74f; // CS:S 106 degree horizontal fov at 16:9
        cam.nearClipPlane = 0.03f;
        cam.farClipPlane = 3000f;
        cam.enabled = false;
        rt = new RenderTexture(1280, 720, 24);
        shot = new Texture2D(1280, 720, TextureFormat.RGB24, false);
    }

    void Capture()
    {
        // Eye at the simulated origin (100 tick, smoother than the 50 Hz CharacterController), looking where we go.
        Vector3 pos = Origin();
        Vector3 vel = (Vector3)movement.GetProgramVariable("velocity");
        Vector2 v = new Vector2(vel.x, vel.z);
        // The player's view, lightly smoothed (decisions change it every frame; a mouse doesn't).
        camYaw = Mathf.SmoothDampAngle(camYaw, appliedYaw, ref camYawVel, 0.08f, Mathf.Infinity, 2f / frameRate);
        cam.transform.SetPositionAndRotation((pos + Vector3.up * 64f) * U, Quaternion.Euler(10f, camYaw, 0f));
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        shot.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
        shot.Apply();
        RenderTexture.active = null;
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(recordDir, $"f{shotNumber++:D5}.jpg"), shot.EncodeToJPG(88));
        hud.Append(Mathf.RoundToInt(v.magnitude)).Append('\t').Append(timer != null ? Label() : "").Append('\t').Append(lastKeys).Append('\n');
    }

    string Label()
    {
        var label = timer.GetProgramVariable("label") as TMPro.TextMeshProUGUI;
        return label != null ? label.text : "";
    }

    // ------------------------------------------------------------------ helpers

    Vector3 Origin() { return (Vector3)movement.GetProgramVariable("origin"); }
    static Vector2 Flat(Vector3 p) { return new Vector2(p.x, p.z); }
    static Vector2 Dir(float yaw) { return new Vector2(Mathf.Sin(yaw * Mathf.Deg2Rad), Mathf.Cos(yaw * Mathf.Deg2Rad)); }
    static float Yaw(Vector2 d) { return Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg; }
    void SetYaw(float yaw) { appliedYaw = yaw; playerBody.rotation = Quaternion.Euler(0, yaw, 0); }
    void Keys(params Key[] keys)
    {
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
        lastKeys = string.Join("+", keys);
    }

    readonly string[] recent = new string[120];

    /// <summary>The last frames of the bot's state, oldest first (after a fail).</summary>
    void DumpRecent()
    {
        for (int i = 1; i <= recent.Length; i++)
        {
            string line = recent[(frame + i) % recent.Length];
            if (line != null && (frame + i) % 3 == 0) Log("  " + line);
        }
    }

    static UdonBehaviour FindUdon(string objectName)
    {
        foreach (var udon in FindObjectsOfType<UdonBehaviour>())
            if (udon.gameObject.name == objectName) return udon;
        return null;
    }

    static UdonBehaviour Loaded(UdonBehaviour udon)
    {
        try { return udon != null && udon.GetProgramVariable("active") != null ? udon : null; }
        catch (System.NullReferenceException) { return null; }
    }

    void Update()
    {
        if (!finished && Time.realtimeSinceStartup > 3000f) { Log("FAIL: watchdog, not finished within 50 minutes"); Finish(1); }
    }

    static void Log(string s) { Debug.Log("[SMTEST] " + s); }

    void Finish(int code)
    {
        finished = true;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(code);
#endif
    }
}
