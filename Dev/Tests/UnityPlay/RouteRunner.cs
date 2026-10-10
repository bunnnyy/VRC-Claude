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
        // Red lanes 3 and 4 each have glass pillars in line with the blocks before and after them: the lines zig-zag
        // round them the way you'd hop it (onto the block before at an angle, curving round the pillar's side).
        new[] { V(4608, 0), V(6336, 0), V(6336, 416), V(4608, 416), V(4680, 740), V(4832, 812), V(4912, 896), V(4992, 840),
                V(5216, 832), V(6336, 832), V(6336, 1248), V(5440, 1248), V(5280, 1320), V(5200, 1376), V(5120, 1320), V(4992, 1200),
                V(4912, 1120), V(4832, 1176), V(4400, 1248) },
    };
    static readonly Rect EndZone = new Rect(-2256, 4512, 2256 - 1728, 5056 - 4512); // flat coords (unity x, z)

    /// <summary>Source x, y to flat Unity-axis coordinates (x = -y, z = x), Source units.</summary>
    static Vector2 V(float x, float y) { return new Vector2(-y, x); }

    struct Spot { public Vector3 p; public float s, lat, ceil; public bool deep, edge, duck, duckSoon; public int seg; public float need; public SourceMapDoor block; }

    readonly List<List<Spot>> spots = new List<List<Spot>>();
    readonly List<float[]> lengths = new List<float[]>();
    UdonBehaviour movement, timer;
    VRCPlayerApi player;
    Transform playerBody;
    Keyboard keyboard;
    int layers;
    float hull = 62f, duckHull = 45f, jumpImpulse = 301.99f; // the movement's hull heights and jump speed
    bool finished;

    int section;
    float progress;
    bool hopping, hasTarget, wasOnGround, wasLanded;
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
        hull = (float)movement.GetProgramVariable("hullHeight");
        duckHull = (float)movement.GetProgramVariable("duckHullHeight");
        jumpImpulse = (float)movement.GetProgramVariable("jumpImpulse");

        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        keyboard = InputSystem.AddDevice<Keyboard>("RouteKeyboard");
        for (int i = 0; i < 30; i++) yield return null;
        foreach (var menu in Resources.FindObjectsOfTypeAll<ClientSimMenu>())
            if (menu.gameObject.scene.IsValid()) { menu.WarningAccepted(); menu.CloseMenu(); }
        for (int i = 0; i < 10; i++) yield return null;

        float t0 = Time.realtimeSinceStartup;
        int total = 0;
        foreach (var line in Route)
        {
            var list = FindSpots(line);
            Plan(list);
            spots.Add(list);
            total += list.Count;
            // Where the plan finds no way on (log): the furthest stretches of spots it can't go on from.
            var dead = new List<string>();
            for (int i = list.Count - 1; i >= 0 && dead.Count < 6; i--)
                if (float.IsInfinity(list[i].need) && (dead.Count == 0 || !dead[dead.Count - 1].StartsWith("s " + Mathf.RoundToInt(list[i].s / 64f) * 64)))
                    dead.Add("s " + Mathf.RoundToInt(list[i].s / 64f) * 64 + " " + Flat(list[i].p));
            Log($"plan section {spots.Count}: {list.Count} spots, no way on near: {string.Join("; ", dead)}");
        }
        int onBlocks = 0, visualColliders = 0;
        foreach (var go in FindObjectsOfType<Transform>()) if (go.name == "Visuals") visualColliders += go.GetComponentsInChildren<Collider>(true).Length;
        Log($"{visualColliders} colliders under the map's Visuals (uSource's own: the movement collides with them too)");
        foreach (var list in spots) foreach (var sp in list) if (sp.block != null) onBlocks++;
        Log($"{total} landing spots in {Route.Length} sections, {onBlocks} on bhop blocks ({Time.realtimeSinceStartup - t0:F1} s)");

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
                    ridge = 0;
                    circling = -1;
                    System.Array.Clear(circled, 0, circled.Length);
                    tabu.Clear();
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
                    // Next time try other spots than the last few that led here.
                    foreach (var t in recentTargets) if (t != Vector3.zero) tabu.Add(t);
                    progress = Project(Flat(pos), -1f);
                    ridge = 0;
                    circling = -1;
                    System.Array.Clear(circled, 0, circled.Length);
                    DumpRecent();
                    if (fails >= MaxFails) { Log($"FAIL: giving up after {MaxFails} fails"); break; }
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
        if (recordDir != "") FlushHud();
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
        int segment = SegmentAt(line, progress);
        if (segment != lastSegment || section != lastSegmentSection)
        {
            lastSegment = segment; lastSegmentSection = section;
            Log($"section {section + 1} segment {segment + 1} at frame {frame}, shot {shotNumber}, game time {Time.time:F2}");
        }
        float lookYaw = Yaw(PointAt(line, progress + 160f) - here);
        Vector2 src = new Vector2(here.y, -here.x); // Source x, y
        for (int i = 0; i < Circles.Length && circling < 0; i++)
            if (!circled[i] && hopping && Circles[i].section == section && Circles[i].area.Contains(src) && (Circles[i].always || v.magnitude < Circles[i].speed))
            { circling = i; circleTurn = 0; }
        if (circling >= 0)
        {
            if (CircleStep(pos, vel, onGround, Circles[circling])) return;
            circled[circling] = true;
            if (circling == 0 && hasTarget) ridge = 2; // then onto the ridge from its first block
            circling = -1;
            return;
        }
        if (ridge > 0 && ridge < 5 && hopping && RidgeStep(pos, vel, onGround)) return;

        if (!hopping)
        {
            // From a standstill (start, a teleport): run until fast, or until the floor ahead ends.
            Record(0, lookYaw);
            heldKey = 0;
            Crouch(false);
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
            hasTarget = PickTarget(pos, vel, out target, out flight, ridge == 2 ? RidgeLaunchTo - RidgeLaunchS0 : float.MaxValue);
            if (hasTarget) recentTargets[hops % recentTargets.Length] = target.p;
            if (hasTarget) Crouch(flight.duck);
            takeoffTime = Time.time;
        }
        wasOnGround = onGround;
        if (onGround && !wasLanded && hasTarget)
        {
            // How far from the target we came down: along the flight (+ long) and across it.
            Vector2 d = here - Flat(target.p), dir = (Flat(target.p) - flight.a).normalized;
            Vector2 m = here - Flat(predicted);
            Log($"landed {Vector2.Dot(d, dir):F1} long, {Vector2.Dot(d, new Vector2(dir.y, -dir.x)):F1} aside of s {target.s:F0} ({(target.deep ? "deep" : target.edge ? "edge" : "normal")}) at {v.magnitude:F0} u/s; the model said {Vector2.Dot(m, dir):F1} long, {Vector2.Dot(m, new Vector2(dir.y, -dir.x)):F1} aside of where we are");
        }
        wasLanded = onGround;
        if (onGround)
        {
            var under = BlockUnder(pos);
            if (under != null) touched[under] = Time.time;
        }
        if (onGround && runOnLanding)
        {
            runOnLanding = false; hopping = false; hasTarget = false;
            Record(0, lookYaw);
            Keys(Key.W);
            return;
        }

        Vector2 noTarget = PointAt(line, progress + 96f); // no spot (end of a section): follow the route line
        bool coast;
        Vector2 want = WantVelocity(here, pos.y, v, vel.y, hasTarget, target, flight, Time.time - takeoffTime, noTarget, out coast);
        Vector2 aim = hasTarget ? new Vector2(target.p.x, target.p.z) : noTarget;

        // Too slow to strafe up to speed: land without jumping and run up again (to 240, or to the edge).
        // Never on a bhop block: it sinks under whoever stays (SourceMapDoor).
        if (!onGround && v.magnitude < 120f && vel.y < 0f && !OnBlock(hasTarget ? target.p : new Vector3(here.x, pos.y, here.y))) runOnLanding = true;
        Strafe(v, want, coast, aim - here, !runOnLanding);
    }

    // bhop_eazy_v2's red lane 1: a ridge (Source x 5056..5440, y -56..56, 62 degree faces, a teleport along its crest
    // at z 152, a wall across its far end) between blocks 576 apart, too far to jump. Surf its side: build speed on
    // the start platform first (circle strafing; the exit needs about 400 u/s), hop to a block before it, jump onto
    // its face beside the crest, climb the face holding A, and fly off it round the end wall onto the block after it.
    const int RidgeSection = 4;
    static readonly Vector2 RidgeFrom = V(5056, 0);
    const float RidgeLaunchFrom = 4780f, RidgeLaunchTo = 5010f; // Source x of the blocks before the ridge (4800..4864, 4928..4992)
    const float RidgeLaunchS0 = 4608f; // Source x where the lane's route line starts (s 0)
    const float RidgeFeet = 140f;
    int ridge; // 0 not there (or circling first, see Circles), 2 hops to a block before the ridge, 3 jumping onto its face, 4 surfing, 5 off it
    bool ridgeIn; // jumping onto the face: past its front, moving in
    int circleTurn; // circling: 1 turning right (D), -1 left (A)
    float circleSince;

    // Where the bot first builds speed by circle strafing (map-specific, like the route): on `area` (Source x, y), round
    // `centre`, until at a takeoff heading `down` the lane (within 60 degrees), at `speed` or more (or after 20 s, as
    // fast as it got), the model finds a landing between `fromS` and `untilS` along the section. `always`: also when
    // already fast (it's for the position, not only the speed).
    struct Circling { public string name; public int section; public Rect area; public Vector2 centre, down; public float speed, fromS, untilS; public bool always; }
    static readonly Circling[] Circles =
    {
        // (Leaving fast down the lane, a hop is ~350 units: from the circle's side it lands on the second block, x 4960.
        // The side is 40 units off the middle, so it lands on that block's outer edge, near where the ridge is passed.)
        new Circling { name = "red lane 1 (to a block before the ridge)", section = 4, area = new Rect(4480, -192, 256, 384), centre = V(4608, -40),
            down = V(1, 0), speed = 420f, fromS = 176f, untilS = 400f, always = true },
    };
    int circling = -1;
    readonly bool[] circled = new bool[1];

    /// <summary>Circle strafing (see Circles); false once it leaves (with the target set).</summary>
    bool CircleStep(Vector3 pos, Vector3 vel, bool onGround, Circling c)
    {
        Vector2 here = Flat(pos), v = Flat(vel);
        bool takeoff = wasOnGround && !onGround;
        if (takeoff) hops++;
        wasOnGround = onGround;
        // The velocity the keys and view sent now will act on: after the pushes already on their way.
        Vector2 vp = v;
        for (int k = Mathf.Max(keyLag, yawLag) - 1; k >= 1; k--)
            if (frame - k >= 0 && keyQueue[(frame - k) % 32] != 0) vp = PushAt(vp, yawQueue[(frame - k) % 32] + 90f * keyQueue[(frame - k) % 32]);
        float travel = vp.magnitude > 30f ? Yaw(vp) : viewYaw;
        if (circleTurn == 0)
        {
            Vector2 r0 = here - c.centre;
            circleTurn = Mathf.DeltaAngle(travel, Yaw(r0.magnitude < 1f ? -v : -r0)) < 0f ? -1 : 1;
            circleSince = Time.time;
        }
        // Wide enough to hold at this speed: strafing turns the velocity atan(30 / v) a tick, a radius of v^2 / 3000.
        // Its side where it heads down the lane lies on the lane's middle line.
        float radius = Mathf.Max(70f, v.sqrMagnitude / 2500f);
        Vector2 centre = c.centre + new Vector2(c.down.y, -c.down.x).normalized * (radius * circleTurn);
        if (takeoff && Mathf.Abs(Mathf.DeltaAngle(Yaw(v), Yaw(c.down))) < 60f && (v.magnitude >= c.speed || Time.time - circleSince > 20f))
        {
            var from = new Takeoff { pos = pos, vel = vel, yaw = viewYaw, held = heldKey, heldFor = heldFrames, ducked = (bool)movement.GetProgramVariable("ducked") };
            if (Search(from, Mathf.Max(progress, c.fromS - 24f), false, out Spot spot, out Flight f, out float miss, out _, c.untilS) == 2)
            {
                circleTurn = 0;
                target = spot; flight = f; hasTarget = true; takeoffTime = Time.time;
                Log($"circling: {v.magnitude:F0} u/s after {Time.time - circleSince:F1} s on {c.name}, to {Flat(spot.p)} miss {miss:F1}");
                return false;
            }
        }
        if (Time.time - circleSince > 30f) { circleTurn = 0; Log($"circling: no way on found in 30 s on {c.name}, hopping on as usual"); return false; }
        if (frame % 200 == 0) Log($"circling at {v.magnitude:F0} u/s");
        // Circle strafing: looking where we go, A or D pushes square to the velocity, which turns it and adds speed
        // every tick; let go while already heading inside the circle.
        progressTime = Time.time; // circling isn't being stuck
        Vector2 r = here - centre;
        if (r.magnitude < 1f) r = Vector2.right;
        // The heading of the circle here (the way round we're going), bent inwards when outside it.
        float round = Yaw(-r) - 90f * circleTurn + Mathf.Clamp((r.magnitude - radius) * 1.5f, -40f, 40f) * circleTurn;
        bool turn = Mathf.DeltaAngle(travel, round) * circleTurn > 0f;
        // The view along that velocity: a view even a few degrees behind it makes the push add nothing (Source caps
        // the speed along the push at 30), one ahead of it pushes a little backwards.
        Act(turn ? circleTurn : 0, Mathf.MoveTowardsAngle(viewYaw, travel, MaxTurn), true);
        return true;
    }

    /// <summary>Red lane 1's ridge (see above); false when the usual hopping should handle this frame.</summary>
    bool RidgeStep(Vector3 pos, Vector3 vel, bool onGround)
    {
        Vector2 here = Flat(pos), v = Flat(vel);
        bool takeoff = wasOnGround && !onGround;
        if (ridge == 2)
        {
            // Taking off from a block before the ridge: onto its face.
            if (!(takeoff && here.y > RidgeLaunchFrom && here.y < RidgeLaunchTo && Mathf.Abs(here.x) < 60f)) return false;
            ridge = 3;
            ridgeIn = false;
            hops++;
            hasTarget = false;
            Log($"ridge: jumping onto its side at {v.magnitude:F0} u/s from {here}");
        }
        wasOnGround = onGround;
        if (onGround) { ridge = 5; return false; } // came down somewhere: back to hopping
        if (ridge == 3)
        {
            // Past the ridge's triangular front and the seam behind it well beside it (Source y -72), keeping the speed
            // along the lane, then in towards its face until the hull touches it (pressing into the face from the side
            // only adds 30 u/s, so come in moving).
            bool clear = here.y >= RidgeFrom.y + 16f && here.x >= 60f; // past the front and its seam, well beside it
            if (clear) ridgeIn = true;
            float side = !ridgeIn ? Mathf.Clamp((72f - here.x) * 5f, -250f, 250f) : -150f;
            Vector2 want = new Vector2(side, Mathf.Sqrt(Mathf.Max(v.sqrMagnitude - side * side, 0f)));
            Strafe(v, want, false, want, true);
            if (ridgeIn && OnFace(pos)) { ridge = 4; Log($"ridge: on the face, feet {pos.y:F0}, {v.magnitude:F0} u/s"); }
            return true;
        }
        // Surfing: look along the ridge, A pushes into the face (it lifts us), let go near the top or rising fast.
        bool push = (vel.y < 0f || pos.y < RidgeFeet - 8f) && pos.y < RidgeFeet + 4f;
        Act(push ? -1 : 0, Mathf.MoveTowardsAngle(viewYaw, 0f, MaxTurn), true); // along the ridge (Source +x)
        // From the middle on, fly off it as soon as the model finds a way round the end wall onto what follows.
        if (here.y > RidgeFrom.y + 230f && frame % 2 == 0)
        {
            var from = new Takeoff { pos = pos, vel = vel, yaw = viewYaw, held = heldKey, heldFor = heldFrames, ducked = (bool)movement.GetProgramVariable("ducked") };
            int found = Search(from, progress, true, out Spot spot, out Flight f, out float miss, out _);
            if (found == 2 || (found == 1 && here.y > RidgeFrom.y + 330f))
            {
                target = spot; flight = f; hasTarget = true; takeoffTime = Time.time; ridge = 5;
                Log($"ridge: off the face at feet {pos.y:F0}, {v.magnitude:F0} u/s, to s {spot.s:F0} at {Flat(spot.p)}{(f.curved ? " curved" : "")} miss {miss:F1}");
                return false;
            }
        }
        if (here.y > RidgeFrom.y + 360f) { ridge = 5; Log("ridge: no way off the face found"); return false; }
        return true;
    }

    /// <summary>The hull touching the ridge's face (Source y < 0 side): its inner bottom edge at or under the face.</summary>
    static bool OnFace(Vector3 pos)
    {
        float inner = pos.x - 16f; // flat x is Source -y: the hull's edge nearest the crest
        return inner < 52f && pos.y <= 56f + (52f - Mathf.Max(inner, 0f)) * (96f / 52f) + 1.5f;
    }

    /// <summary>
    /// The velocity to aim for `t` s into a hop: onto the target, arriving when we come down to its height, along the
    /// curve for a flight around an obstacle; with no target, along `noTarget` at speed. `coast`: close to landing or
    /// already over the spot, stop steering ("distance left / time left" swings around and brakes hard).
    /// </summary>
    static Vector2 WantVelocity(Vector2 here, float y, Vector2 v, float vy, bool has, Spot tgt, Flight path, float t, Vector2 noTarget, out bool coast)
    {
        coast = false;
        if (!has) return (noTarget - here).normalized * Mathf.Max(v.magnitude, 250f);
        Vector2 aim = new Vector2(tgt.p.x, tgt.p.z);
        float tLeft = FlightTime(y, vy, tgt.p.y, path.ceil);
        // Coast near the end (steering then swings "distance left / time left" around); an edge spot (the hull
        // hanging over) needs steering to the last moment and no landing long.
        float coastTime = tgt.edge ? 0.06f : 0.12f, coastDistance = tgt.edge ? 8f : 24f;
        if (float.IsNaN(tLeft) || tLeft < coastTime || (!path.curved && (aim - here).magnitude < coastDistance)) { coast = true; return v; }
        if (path.curved) return (Curve(path, Mathf.Min(t / path.time + 0.1f, 1f)) - here) / (0.1f * path.time);
        if (!tgt.edge) aim += (aim - here).normalized * 6f; // land a little long, never short
        return (aim - here) / tLeft;
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

    const float MaxTurn = 12f;         // degrees the view turns per frame at most (a mouse, not a teleport)

    void Strafe(Vector2 v, Vector2 want, bool coast, Vector2 toAim, bool jump)
    {
        int lag = Mathf.Max(keyLag, yawLag);
        Vector2 vp = v;
        for (int k = lag - 1; k >= 1; k--) // decided, not yet in the velocity
        {
            int f = frame - k;
            if (f >= 0 && keyQueue[f % 32] != 0) vp = PushAt(vp, yawQueue[f % 32] + 90f * keyQueue[f % 32]);
        }
        if (coast) want = vp;
        float look = vp.magnitude > 50f ? Yaw(vp) : toAim.magnitude > 1f ? Yaw(toAim) : viewYaw;
        var d = Decide(vp, want, coast, look, heldKey, heldFrames, viewYaw, Directions);
        Act(d.key, d.yaw, jump);
    }

    /// <summary>This frame's decision (key -1 A, 0 none, 1 D, and the view) into the queues, and this frame's share
    /// sent: the key and the view of the decisions that should arrive together.</summary>
    void Act(int key, float yaw, bool jump)
    {
        if (key != heldKey) { heldKey = key; heldFrames = 0; } else heldFrames++;
        Record(key, yaw);
        int lag = Mathf.Max(keyLag, yawLag);
        int keyFrame = frame - (lag - keyLag), yawFrame = frame - (lag - yawLag);
        int key2 = keyFrame >= 0 ? keyQueue[keyFrame % 32] : 0;
        float yaw2 = yawFrame >= 0 ? yawQueue[yawFrame % 32] : yaw;
        SetYaw(yaw2);
        if (key2 < 0) { if (jump) Keys(Key.Space, Key.A); else Keys(Key.A); }
        else if (key2 > 0) { if (jump) Keys(Key.Space, Key.D); else Keys(Key.D); }
        else if (jump) Keys(Key.Space); else Keys();
    }

    struct Decision { public int key; public float yaw; }

    /// <summary>
    /// One tick's strafe: the key (-1 A, 0 none, 1 D) and view that bring the velocity (after this push and the best
    /// next one) closest to `want`. The view stays within 100 degrees of `look` (the direction of travel) and turns
    /// at most MaxTurn from `yaw`; a key stays down MinHold ticks; no push that loses speed unless we're too fast.
    /// Coasting: no key if allowed, else a view where the held key adds nothing.
    /// </summary>
    static Decision Decide(Vector2 vp, Vector2 want, bool coast, float look, int held, int heldFor, float yaw, int dirs)
    {
        bool mayChange = heldFor >= MinHold || held == 0;
        var best = new Decision { key = held, yaw = Mathf.MoveTowardsAngle(yaw, look, MaxTurn) };
        float bestScore = float.MaxValue;
        if (mayChange) { best.key = 0; bestScore = coast || (want - vp).magnitude <= 4f ? 0f : Best2(vp, want, dirs); }
        if (bestScore == 0f) return best;
        for (int key = -1; key <= 1; key += 2)
        {
            if (!mayChange && key != held) continue;
            for (int i = 0; i < dirs; i++)
            {
                float y = yaw + (i - dirs / 2) * (2f * MaxTurn / dirs);
                float dev = Mathf.Abs(Mathf.DeltaAngle(y, look));
                if (dev > 100f) continue; // never look backwards
                Vector2 v1 = PushAt(vp, y + 90f * key);
                if (v1.magnitude < vp.magnitude - 0.5f && vp.magnitude < want.magnitude + 10f) continue;
                float score = (coast ? (v1 - vp).magnitude * 10f : Best2(v1, want, dirs)) + 0.02f * dev + (key != held ? 0.5f : 0f);
                if (score < bestScore) { bestScore = score; best.key = key; best.yaw = y; }
            }
        }
        return best;
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

    const int Directions = 48;

    /// <summary>Smallest error to `want` after one more tick (any of `dirs` directions, or none).</summary>
    static float Best2(Vector2 v, Vector2 want, int dirs)
    {
        float best = (want - v).magnitude;
        for (int i = 0; i < dirs; i++) best = Mathf.Min(best, (want - PushAt(v, i * 360f / dirs)).magnitude);
        return best;
    }

    string nextHop = ""; // the hop after the one picked, as the model sees it (log)
    const int MaxFails = 30;
    readonly Vector3[] recentTargets = new Vector3[3]; // the last few hops' targets
    readonly List<Vector3> tabu = new List<Vector3>(); // targets that led to a fall in this section
    Takeoff bestLand;    // where the model lands on the best target found (log)
    Vector3 predicted;   // where the model lands on the target picked (log)

    /// <summary>A takeoff as the bot sees it: two ticks into the jump (it notices a frame late), the speed scaled.</summary>
    static Takeoff Seen(Takeoff t, float faster)
    {
        const float dt = 0.02f;
        var v = new Vector3(t.vel.x * faster, t.vel.y, t.vel.z * faster);
        t.pos += new Vector3(v.x * dt, v.y * dt - 0.5f * Gravity * dt * dt, v.z * dt);
        t.vel = new Vector3(v.x, v.y - Gravity * dt, v.z);
        return t;
    }

    /// <summary>A hop's start: feet and velocity at takeoff, the view, the strafe key held (and for how many frames),
    /// ducked or not.</summary>
    struct Takeoff { public Vector3 pos, vel; public float yaw; public int held, heldFor; public bool ducked; }

    /// <summary>The landing spot for this takeoff, logged.</summary>
    bool PickTarget(Vector3 pos, Vector3 vel, out Spot best, out Flight path, float maxS = float.MaxValue)
    {
        var from = new Takeoff { pos = pos, vel = vel, yaw = viewYaw, held = heldKey, heldFor = heldFrames, ducked = (bool)movement.GetProgramVariable("ducked") };
        float speed = Flat(vel).magnitude;
        int found = Search(from, progress, true, out best, out path, out float miss, out int count, maxS);
        if (found == 2)
            Log($"hop {hops} s {progress:F0} -> {best.s:F0} speed {speed:F0} need {path.length / path.time:F0} deep {best.deep}{(path.curved ? " curved" : "")}{(path.duck ? " ducked" : "")} miss {miss:F1} of {count}, then {nextHop}");
        else if (found == 1)
        {
            Log($"hop {hops} s {progress:F0} -> {best.s:F0} speed {speed:F0}: nothing lands within its margin with a way on from there, taking the best (misses by {miss:F1} more); from there:");
            Explain(Seen(bestLand, 1f), best.s, 64f);
        }
        else
            Log($"no landing spot from {Flat(pos)} at {speed:F0} u/s (s {progress:F0}), following the route line");
        return found > 0;
    }

    /// <summary>
    /// The landing spot to aim for from a takeoff at `at` along the section: furthest along, reachable at about the
    /// current speed (strafing can add a little), not much slower than now, with a flight path that hits nothing,
    /// checked by flying it in the model. With `ahead`, a spot only counts if a next hop from it works too, from where
    /// and how fast the model lands there: a block reached too fast or heading the wrong way can leave no way past
    /// the wall after it. 2: found, 1: only one that misses its margin or leads nowhere, 0: nothing.
    /// </summary>
    int Search(Takeoff from, float at, bool ahead, out Spot best, out Flight path, out float bestMiss, out int count, float maxS = float.MaxValue, SourceMapDoor avoid = null)
    {
        best = default;
        path = default;
        bestMiss = float.MaxValue;
        count = 0;
        var list = spots[section];
        Vector2 here = Flat(from.pos);
        float speed = Flat(from.vel).magnitude;
        float endS = list.Count > 0 ? list[list.Count - 1].s : 0f;
        // Under a low ceiling now, or going under one: duck (in the air the feet pull up, see SimulateHop).
        float standRoof = Headroom(here, from.pos.y, hull), duckRoof = Headroom(here, from.pos.y, duckHull);
        bool lowHere = standRoof - from.pos.y < LowRoof;
        int maxSims = ahead ? 40 : 8; // stops at the first that works: only costs where it's hard
        for (int pass = 0; pass < 2; pass++)
        {
            var options = new List<(float score, Spot spot, float time)>();
            foreach (var s in list)
            {
                // The hop after (the lookahead) has to leave the block: a hop to the far corner of the same block is no
                // way on.
                if (s.s < at + (ahead ? 24f : 64f) || s.s > Mathf.Min(at + 700f, maxS)) continue;
                if (s.block != null && (s.block == avoid || Sinking(s.block))) continue; // low by the time we come down
                float roof = s.duck || lowHere ? duckRoof : standRoof;
                float time = FlightTime(from.pos.y, from.vel.y, s.p.y, Mathf.Min(roof, s.ceil));
                if (float.IsNaN(time) || time < 0.2f) continue;
                float need = (new Vector2(s.p.x, s.p.z) - here).magnitude / time;
                // Can't get there, or (first pass) would land too slow to go on from there.
                if (need > Reach(speed, time) || (pass == 0 && need < s.need)) continue;
                // Furthest along, near the middle of the lane, well inside a surface, and without braking (speed lost
                // braking has to be strafed back). Near the end of the section, the middle of the lane: lined up with
                // the doorway to the next one.
                float score = s.s - (s.s > endS - 300f ? 2f : 0.5f) * Mathf.Abs(s.lat) - (s.deep ? 0f : s.edge ? 400f : 250f) - 3f * Mathf.Max(0f, 0.9f * speed - need)
                    + 1.5f * Mathf.Clamp(need - speed, 0f, 60f); // and building speed for the long gaps
                if (maxS == float.MaxValue) // (not the ridge's launch block: there's no other)
                    foreach (var t in tabu) if ((t - s.p).sqrMagnitude < 24f * 24f) { score -= 1000f; break; } // led to a fall before
                options.Add((score, s, time));
            }
            options.Sort((x, y) => y.score.CompareTo(x.score));
            count = options.Count;
            // Spread the few flights we can afford over different places: none within 20 units of a better one.
            var spread = new List<(float score, Spot spot, float time)>();
            foreach (var o in options)
            {
                bool near = false;
                foreach (var q in spread) if ((Flat(q.spot.p) - Flat(o.spot.p)).sqrMagnitude < 400f) { near = true; break; }
                if (!near) spread.Add(o);
            }
            options = spread;
            // Fly the best few in the model (our own steering, input lag, the map's collision) and take the first
            // that lands well inside its spot's margin.
            int simulated = 0;
            foreach (var o in options)
            {
                if (simulated >= maxSims) break;
                bool duck = o.spot.duck || o.spot.duckSoon || lowHere;
                float roof = duck ? duckRoof : standRoof;
                for (int k = 0, tries = 0; k <= 8 && tries < 3; k++)
                {
                    if (!FlightWithBend(from.pos, from.vel.y, o.spot.p, o.time, Mathf.Min(roof, o.spot.ceil), duck, k, out Flight f)) continue;
                    tries++;
                    simulated++;
                    if (!SimulateHop(from, o.spot, f, out float miss, out Takeoff land)) continue;
                    float margin = o.spot.deep ? 20f : o.spot.edge ? 5f : 12f;
                    Spot then = default;
                    Flight thenPath = default;
                    // From the takeoff as the bot will see it, at the model's speed and a little faster (it tends to
                    // land a little fast): a way on that only just works in the model isn't one.
                    bool onward = !ahead || o.spot.s > endS - 64f || maxS < float.MaxValue
                        || (Search(Seen(land, 1f), o.spot.s, false, out then, out thenPath, out _, out _, float.MaxValue, o.spot.block) == 2
                            && Search(Seen(land, 1.05f), o.spot.s, false, out _, out _, out _, out _, float.MaxValue, o.spot.block) == 2);
                    if (ahead && onward) nextHop = o.spot.s > endS - 64f ? "the end" : $"s {then.s:F0} at {Flat(then.p)}{(thenPath.curved ? " curved" : "")} from {Flat(land.pos)} at {Flat(land.vel).magnitude:F0} u/s";
                    if (miss <= margin && onward) { best = o.spot; path = f; bestMiss = miss; if (ahead) predicted = land.pos; return 2; }
                    // Otherwise the best of the rest: landing inside the margin first, then the smallest miss.
                    float over = Mathf.Max(0f, miss - margin) + (onward ? 0f : 1000f);
                    if (over < bestMiss) { bestMiss = over; best = o.spot; path = f; if (ahead) { bestLand = land; predicted = land.pos; } }
                }
            }
        }
        if (bestMiss < float.MaxValue)
        {
            if (bestMiss >= 1000f) bestMiss -= 1000f;
            return 1;
        }
        if (ahead) Explain(from, at, 24f);
        return 0;
    }

    /// <summary>Average speed reachable over a hop of `time` s from `speed`: strafing adds 30 u/s sideways per tick,
    /// so the speed grows to about sqrt(v^2 + 900 * ticks). Counts on most of it.</summary>
    static float Reach(float speed, float time)
    {
        return 0.5f * (speed + Mathf.Sqrt(speed * speed + 900f * 100f * time)) * 0.9f;
    }

    struct Flight { public Vector2 a, c, b; public float time, length, ceil; public bool curved, duck; }

    /// <summary>Where a flight is at `u` (0..1 of its time): a quadratic curve from a to b with control point c.</summary>
    static Vector2 Curve(Flight f, float u) { return (1f - u) * (1f - u) * f.a + 2f * u * (1f - u) * f.c + u * u * f.b; }

    /// <summary>
    /// The flight from `from` (moving up at vy) to `to` in `time` with bend `k`: straight (0), or curving out 16, 32,
    /// 48, 64 units to one side then the other (1..8), like strafing around bhop_eazy_v2's pillars; if the hull
    /// would make it without touching anything.
    /// </summary>
    bool FlightWithBend(Vector3 from, float vy, Vector3 to, float time, float ceil, bool duck, int k, out Flight flight)
    {
        Vector2 a = Flat(from), b = Flat(to), mid = (a + b) * 0.5f, side = new Vector2((b - a).y, -(b - a).x).normalized;
        float off = 16f * ((k + 1) / 2) * (k % 2 == 0 ? -1f : 1f); // 0, 16, -16, 32, -32 ... 64, -64
        flight = new Flight { a = a, b = b, c = mid + side * (2f * off), time = time, ceil = ceil, curved = k != 0, duck = duck }; // passes `off` aside at its middle
        flight.length = 0f;
        for (int i = 1; i <= 12; i++) flight.length += (Curve(flight, i / 12f) - Curve(flight, (i - 1) / 12f)).magnitude;
        return ClearFlight(from.y, vy, flight);
    }

    /// <summary>The first flight with any bend (straight first; bends only if `curves`).</summary>
    bool FindFlight(Vector3 from, float vy, Vector3 to, float time, float ceil, bool duck, bool curves, out Flight flight)
    {
        flight = default;
        for (int k = 0; k <= (curves ? 8 : 0); k++)
            if (FlightWithBend(from, vy, to, time, ceil, duck, k, out flight)) return true;
        return false;
    }

    /// <summary>
    /// Flies a hop in a model of the movement: our own steering each tick (decisions acting after the measured
    /// input lag), air acceleration and gravity, the hull checked against the map. `miss`: how far from the spot it
    /// comes down to the spot's height. False if the hull hits something or touches a teleport on the way.
    /// Ducking or standing up (C pressed at takeoff, acting after the key lag) is CS:S's in the air: the hull
    /// shrinks around its middle, the feet going up half the difference; standing up moves them back down once
    /// there's room below.
    /// </summary>
    bool SimulateHop(Takeoff from, Spot tgt, Flight path, out float miss, out Takeoff land)
    {
        const float Dt = 0.01f;
        miss = float.MaxValue;
        land = default;
        Vector2 p = Flat(from.pos), v = Flat(from.vel);
        float y = from.pos.y, vy = from.vel.y, yaw = from.yaw, shift = (hull - duckHull) * 0.5f;
        bool ducked = from.ducked;
        int held = from.held, heldFor = from.heldFor, lag = Mathf.Max(keyLag, yawLag);
        var pending = new Queue<Decision>();
        for (int i = 0; i < lag - 1; i++) pending.Enqueue(new Decision { key = 0, yaw = yaw });
        for (int tick = 0; tick < 200; tick++)
        {
            Vector2 vp = v;
            foreach (var q in pending) if (q.key != 0) vp = PushAt(vp, q.yaw + 90f * q.key);
            bool coast;
            Vector2 want = WantVelocity(p, y, vp, vy, true, tgt, path, tick * Dt, Vector2.zero, out coast);
            if (coast) want = vp;
            var d = Decide(vp, want, coast, vp.magnitude > 50f ? Yaw(vp) : Yaw(new Vector2(tgt.p.x, tgt.p.z) - p), held, heldFor, yaw, 24);
            if (d.key != held) { held = d.key; heldFor = 0; } else heldFor++;
            yaw = d.yaw;
            pending.Enqueue(d);
            var now = pending.Dequeue();
            if (now.key != 0) v = PushAt(v, now.yaw + 90f * now.key);
            if (tick >= keyLag && path.duck != ducked)
            {
                if (path.duck) { y += shift; ducked = true; }
                else if (!HullHits(p, y - shift, hull)) { y -= shift; ducked = false; }
            }
            float h = ducked ? duckHull : hull;
            Vector2 p0 = p;
            p += v * Dt;
            float y0 = y;
            y += (vy - 0.5f * Gravity * Dt) * Dt;
            vy -= Gravity * Dt;
            if (vy < 0f && y <= tgt.p.y)
            {
                // Landed: auto bhop jumps on the next tick, keeping the speed (no friction on a jump tick).
                miss = (p - new Vector2(tgt.p.x, tgt.p.z)).magnitude;
                land = new Takeoff { pos = new Vector3(p.x, tgt.p.y, p.y), vel = new Vector3(v.x, jumpImpulse, v.y), yaw = yaw, held = held, heldFor = heldFor, ducked = ducked };
                return true;
            }
            if (y > tgt.p.y + 2f && HullHits(p, y, h))
            {
                // A face too steep to stand on (a surf ramp): slide along it. Rising into something overhead: Source
                // stops the head there (vertical speed to zero). Anything else in the way (a wall, a beam's side)
                // ends the hop.
                if (SlideAlong(p0, y0, ref p, ref y, ref v, ref vy, h)) { }
                else if (y > y0 && !HullHits(p, y0, h)) { y = y0; vy = 0f; }
                else { simHit = $"{HitName(p, y, h)} at {p:F0} feet {y:F0}"; return false; }
            }
            // (Not in the first ticks: we're taking off from where we stand, e.g. a block already sinking a little.)
            if (tick >= 4 && tick % 3 == 0 && TouchesTeleport(p, y, h)) { simHit = $"a teleport at {p:F0} feet {y:F0}"; return false; }
        }
        return false;
    }

    /// <summary>
    /// A surf face (too steep to stand on) in the way of this tick's move from (p0, y0): the hull goes up to it and the
    /// velocity is clipped to it like Source's ClipVelocity, the rest of the tick sliding along it. False if what's in
    /// the way isn't one.
    /// </summary>
    bool SlideAlong(Vector2 p0, float y0, ref Vector2 p, ref float y, ref Vector2 v, ref float vy, float h)
    {
        const float Dt = 0.01f;
        Vector3 a = HullCenter(p0, y0, h), move = HullCenter(p, y, h) - a;
        if (move.sqrMagnitude < 1e-10f || !Physics.BoxCast(a, HullHalf(0.25f, h), move.normalized, out RaycastHit hit, Quaternion.identity,
            move.magnitude, layers, QueryTriggerInteraction.Ignore)) return false;
        if (hit.normal.y < 0.05f || hit.normal.y >= 0.7f) return false;
        var vel = new Vector3(v.x, vy, v.y);
        vel -= hit.normal * Vector3.Dot(vel, hit.normal);
        float f = Mathf.Max(0f, hit.distance - 0.05f * U) / move.magnitude; // stop just short of the face
        Vector2 at = Vector2.Lerp(p0, p, f);
        float atY = Mathf.Lerp(y0, y, f);
        Vector2 p2 = at + new Vector2(vel.x, vel.z) * (Dt * (1f - f));
        float y2 = atY + vel.y * Dt * (1f - f);
        if (HullHits(p2, y2, h)) { p2 = at; y2 = atY; }
        p = p2; y = y2; v = new Vector2(vel.x, vel.z); vy = vel.y;
        return true;
    }

    string simHit = ""; // what SimulateHop ran into last (log)

    string HitName(Vector2 p, float y, float h)
    {
        foreach (var c in Physics.OverlapBox(HullCenter(p, y, h), HullHalf(0.25f, h), Quaternion.identity, layers, QueryTriggerInteraction.Ignore)) return c.name;
        return "?";
    }

    /// <summary>Why the nearest spots ahead of a takeoff (from `minAhead` on) can't be reached (log).</summary>
    void Explain(Takeoff from, float at, float minAhead)
    {
        var list = spots[section];
        Vector2 here = Flat(from.pos);
        float speed = Flat(from.vel).magnitude;
        float standRoof = Headroom(here, from.pos.y, hull), duckRoof = Headroom(here, from.pos.y, duckHull);
        bool lowHere = standRoof - from.pos.y < LowRoof;
        int shown = 0;
        foreach (var sp in list)
        {
            if (sp.s < at + minAhead || shown >= 12) continue;
            float time = FlightTime(from.pos.y, from.vel.y, sp.p.y, Mathf.Min(sp.duck || lowHere ? duckRoof : standRoof, sp.ceil));
            if (float.IsNaN(time)) continue;
            float need = (new Vector2(sp.p.x, sp.p.z) - here).magnitude / time;
            string why = "";
            if (shown < 4) // what the straight flight there runs into
            {
                bool duck = sp.duck || sp.duckSoon || lowHere;
                var straight = new Flight { a = here, b = Flat(sp.p), c = (here + Flat(sp.p)) * 0.5f, time = time, ceil = Mathf.Min(duck ? duckRoof : standRoof, sp.ceil), duck = duck };
                straight.length = (straight.b - straight.a).magnitude;
                why = ClearFlight(from.pos.y, from.vel.y, straight) ? (SimulateHop(from, sp, straight, out float m, out _) ? $", flies, miss {m:F1}" : $", the model's hop hits {simHit}")
                    : $", path blocked: {ClearFlightWhy(from.pos.y, from.vel.y, straight)}";
                if (Sinking(sp.block)) why += ", block sinking";
            }
            Log($"  rejected {sp.p:F0} s {sp.s:F0} edge {sp.edge}: need {need:F0} reach {Reach(speed, time):F0} plan {sp.need:F0} time {time:F2}{why}");
            shown++;
        }
    }

    /// <summary>What ClearFlight runs into first (log).</summary>
    string ClearFlightWhy(float y0, float vy, Flight f)
    {
        float half = f.curved ? 17.5f : 15.5f, h = f.duck ? duckHull : hull;
        int n = Mathf.Max(12, Mathf.CeilToInt(f.length / 8f));
        for (int i = 1; i <= n; i++)
        {
            float t = f.time * i / n;
            Vector2 p = Curve(f, (float)i / n);
            float y = HeightAt(y0, vy, t, f.ceil);
            foreach (var c in Physics.OverlapBox(HullCenter(p, y, h), new Vector3(half, h * 0.5f - 0.5f, half) * U, Quaternion.identity, layers, QueryTriggerInteraction.Ignore))
                if (i < n) return $"{c.name} at sample {i}/{n} feet {y:F0}";
            foreach (var c in Physics.OverlapBox(HullCenter(p, y, h), HullHalf(0f, h), Quaternion.identity, ~0, QueryTriggerInteraction.Collide))
                if (c.isTrigger && c.GetComponent<SourceMapTeleport>() != null) return $"teleport {c.name} (hull) at sample {i}/{n} feet {y:F0}";
            Vector3 bottom = new Vector3(p.x, y + 1f + CapsuleRadius, p.y) * U, top = new Vector3(p.x, y + 1f + CapsuleHeight - CapsuleRadius, p.y) * U;
            foreach (var c in Physics.OverlapCapsule(bottom, top, CapsuleRadius * U, ~0, QueryTriggerInteraction.Collide))
                if (c.isTrigger && c.GetComponent<SourceMapTeleport>() != null) return $"teleport {c.name} (capsule) at sample {i}/{n} feet {y:F0}";
        }
        return "nothing?";
    }

    /// <summary>The hull along a flight: no solid in the way (before landing), no teleport touched. Curved flights keep
    /// 2 units more clearance, since strafing won't follow the curve exactly.</summary>
    bool ClearFlight(float y0, float vy, Flight f)
    {
        float half = f.curved ? 17.5f : 15.5f, h = f.duck ? duckHull : hull;
        int n = Mathf.Max(12, Mathf.CeilToInt(f.length / 8f)); // samples at most 8 units apart: thin walls and glass
        for (int i = 1; i <= n; i++)
        {
            float t = f.time * i / n;
            Vector2 p = Curve(f, (float)i / n);
            float y = HeightAt(y0, vy, t, f.ceil);
            if (i < n && Physics.CheckBox(HullCenter(p, y, h), new Vector3(half, h * 0.5f - 0.5f, half) * U, Quaternion.identity, layers, QueryTriggerInteraction.Ignore)) return false;
            if (TouchesTeleport(p, y, h)) return false;
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
                float time = FlightTime(A.p.y, jump, B.p.y, Mathf.Min(A.ceil, B.ceil));
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
                if (FindFlight(A.p, jump, B.p, time, Mathf.Min(A.ceil, B.ceil), A.duck || B.duck || B.duckSoon, h < 6, out Flight f)) { A.need = MinSpeed(f.length / time, time); break; }
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
                    bool any = false;
                    foreach (float top in Surfaces(p)) // every floor in the column: blocks can sit under arches
                        if (SafeAt(p, top))
                        {
                            list.Add(Roofed(new Spot { p = new Vector3(p.x, top, p.y), s = s0 + along, lat = lat, deep = Deep(p, top), seg = i }));
                            any = true;
                        }
                    if (!any && Overhang(p, out float edgeTop))
                        list.Add(Roofed(new Spot { p = new Vector3(p.x, edgeTop, p.y), s = s0 + along, lat = lat, edge = true, seg = i }));
                }
            s0 += len;
        }
        // Spots just before a low ceiling: land there ducked already. Ducking in the air lifts the feet 8.5 units, too
        // high to get in under the ceiling's edge on the hop after.
        for (int i = 0, j = 0; i < list.Count; i++) // spots are in order of s: j, the next duck spot, only moves on
        {
            while (j < list.Count && (list[j].s <= list[i].s || !list[j].duck)) j++;
            if (j < list.Count && list[j].s <= list[i].s + DuckAhead) { var sp = list[i]; sp.duckSoon = true; list[i] = sp; }
        }
        return list;
    }

    const float DuckAhead = 200f;

    /// <summary>The spot's headroom; under a low ceiling (a standing jump bumps its head within LowRoof units) it's a
    /// duck spot: hops to and from it are flown ducked, like a player crouching through a tunnel.</summary>
    Spot Roofed(Spot s)
    {
        Vector2 p = Flat(s.p);
        s.block = BlockUnder(s.p);
        s.ceil = Headroom(p, s.p.y, hull);
        s.duck = s.ceil - s.p.y < LowRoof;
        if (s.duck) s.ceil = Headroom(p, s.p.y, duckHull);
        return s;
    }

    const float LowRoof = 40f;

    readonly List<float> surfaces = new List<float>();

    /// <summary>Heights of the walkable surfaces in the column at `p`, top first.</summary>
    List<float> Surfaces(Vector2 p)
    {
        surfaces.Clear();
        var hits = Physics.RaycastAll(new Vector3(p.x, 600f, p.y) * U, Vector3.down, 1200f * U, layers, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (x, y) => x.distance.CompareTo(y.distance));
        foreach (var h in hits)
        {
            float top = h.point.y / U;
            if (h.normal.y >= 0.7f && (surfaces.Count == 0 || surfaces[surfaces.Count - 1] - top > 1f)) surfaces.Add(top);
        }
        return surfaces;
    }

    /// <summary>The floor `top` at `p` holds the hull 12 units in from any edge, with headroom, away from teleports.</summary>
    bool SafeAt(Vector2 p, float top)
    {
        for (int k = 0; k < 4; k++)
            if (!FloorAt(p + Dir(k * 90f) * 12f, top)) return false;
        return !Physics.CheckBox(HullCenter(p, top, hull), HullHalf(0.5f, hull), Quaternion.identity, layers, QueryTriggerInteraction.Ignore)
            && !TouchesTeleport(p, top, hull);
    }

    /// <summary>A floor at height `top` (within a unit) at `p`.</summary>
    bool FloorAt(Vector2 p, float top)
    {
        return Physics.Raycast(new Vector3(p.x, top + 8f, p.y) * U, Vector3.down, out RaycastHit hit, 9f * U, layers, QueryTriggerInteraction.Ignore)
            && hit.normal.y >= 0.7f && Mathf.Abs(hit.point.y / U - top) <= 1f;
    }

    /// <summary>Standing floor right under the feet at `p` (run-up check): the floor at `feet` height there is safe.</summary>
    bool Safe(Vector3 p, out float top)
    {
        top = p.y;
        return FloorAt(new Vector2(p.x, p.z), p.y) && SafeAt(new Vector2(p.x, p.z), p.y);
    }

    /// <summary>
    /// The hull hanging over the edge of a surface: floor 10 units to one side (the hull still stands on it), the
    /// hull clear of walls and teleports. For squeezing past obstacles beside a block (bhop_eazy_v2's pillars).
    /// </summary>
    bool Overhang(Vector2 p, out float top)
    {
        top = 0f;
        for (int k = 0; k < 4; k++)
        {
            foreach (float h in Surfaces(p + Dir(k * 90f) * 10f))
            {
                if (!Physics.CheckBox(HullCenter(p, h, hull), HullHalf(0.5f, hull), Quaternion.identity, layers, QueryTriggerInteraction.Ignore)
                    && !TouchesTeleport(p, h, hull)) { top = h; return true; }
            }
        }
        return false;
    }

    /// <summary>Same floor at least 28 units out in 8 directions: a block centre or well inside a platform.</summary>
    bool Deep(Vector2 p, float top)
    {
        for (int k = 0; k < 8; k++)
            if (!FloorAt(p + Dir(k * 45f) * 28f, top)) return false;
        return true;
    }

    /// <summary>What the movement's ground trace sees under the hull at `pos` (debugging a stuck player).</summary>
    void Probe(Vector3 pos)
    {
        var half = HullHalf(0f, hull);
        var center = (pos + new Vector3(0, hull * 0.5f + 2f, 0)) * U;
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

    /// <summary>Whether the floor below `p` (feet, possibly mid-jump) is a bhop block that sinks when stood on.</summary>
    bool OnBlock(Vector3 p) { return BlockUnder(p) != null; }

    /// <summary>The bhop block (a sinking func_door) below `p`, or null.</summary>
    SourceMapDoor BlockUnder(Vector3 p)
    {
        if (!Physics.Raycast(new Vector3(p.x, p.y + 8f, p.z) * U, Vector3.down, out RaycastHit hit, 200f * U, layers, QueryTriggerInteraction.Ignore)) return null;
        if (doorOf == null)
        {
            // Every collider of a door: its solid, and the visuals moved with it (uSource's meshes have colliders too).
            doorOf = new Dictionary<Collider, SourceMapDoor>();
            foreach (var d in FindObjectsOfType<SourceMapDoor>())
            {
                if (d.solid != null) doorOf[d.solid] = d;
                if (d.visuals != null) foreach (var c in d.visuals.GetComponentsInChildren<Collider>(true)) doorOf[c] = d;
            }
        }
        return doorOf.TryGetValue(hit.collider, out SourceMapDoor door) ? door : null;
    }

    Dictionary<Collider, SourceMapDoor> doorOf;

    // Blocks we landed on and when: one sinks 9 units in 0.45 s, waits 0.5 s and takes 0.45 s to come back, and coming
    // down on it while it's low drops you into the teleport under it (bhop_eazy_v2's red rails: hop between the three).
    readonly Dictionary<SourceMapDoor, float> touched = new Dictionary<SourceMapDoor, float>();
    const float BlockCycle = 1.5f;

    bool Sinking(SourceMapDoor block) { return block != null && touched.TryGetValue(block, out float when) && Time.time - when < BlockCycle; }

    /// <summary>Whether a teleport is touched with the feet at `feet`: by the hull (`h` tall), or by VRChat's player
    /// capsule following it, which is what fires the map's triggers (taller, narrower).</summary>
    static bool TouchesTeleport(Vector2 p, float feet, float h)
    {
        foreach (var c in Physics.OverlapBox(HullCenter(p, feet, h), HullHalf(0f, h), Quaternion.identity, ~0, QueryTriggerInteraction.Collide))
            if (c.isTrigger && c.GetComponent<SourceMapTeleport>() != null) return true;
        // From 1 unit up, like the hull: the capsule rides a little over the floor (the map's floor teleports are
        // raised to exactly the top of the blocks between them).
        Vector3 bottom = new Vector3(p.x, feet + 1f + CapsuleRadius, p.y) * U, top = new Vector3(p.x, feet + 1f + CapsuleHeight - CapsuleRadius, p.y) * U;
        foreach (var c in Physics.OverlapCapsule(bottom, top, CapsuleRadius * U, ~0, QueryTriggerInteraction.Collide))
            if (c.isTrigger && c.GetComponent<SourceMapTeleport>() != null) return true;
        return false;
    }

    const float CapsuleHeight = 84f, CapsuleRadius = 10.5f; // ClientSim's player: 1.6 m by 0.2 m

    /// <summary>Whether the hull (with feet at y) overlaps the map at `p`.</summary>
    bool HullHits(Vector2 p, float y, float h)
    {
        return Physics.CheckBox(HullCenter(p, y, h), HullHalf(0.25f, h), Quaternion.identity, layers, QueryTriggerInteraction.Ignore);
    }

    /// <summary>The centre of a hull `h` tall with the feet at `feet` (1 unit up: Source's hull floats on the ground),
    /// and its half size made `shrink` smaller (a margin for the casts).</summary>
    static Vector3 HullCenter(Vector2 p, float feet, float h) { return new Vector3(p.x, feet + h * 0.5f + 1f, p.y) * U; }
    static Vector3 HullHalf(float shrink, float h) { return new Vector3(16f - shrink, h * 0.5f - shrink, 16f - shrink) * U; }

    /// <summary>
    /// The highest the feet can go above `top` at `p` before the head hits something (a beam over a block): a hull
    /// `h` tall plus Source's skin below whatever is overhead.
    /// </summary>
    float Headroom(Vector2 p, float top, float h)
    {
        var start = new Vector3(p.x, top + h + 1f, p.y) * U;
        if (Physics.BoxCast(start, new Vector3(15.5f, 0.5f, 15.5f) * U, Vector3.up, out RaycastHit hit, Quaternion.identity, 300f * U, layers, QueryTriggerInteraction.Ignore))
            return top + 1f + hit.distance / U - 0.25f;
        return float.PositiveInfinity;
    }

    /// <summary>Time until the feet come down to `target` from `y` moving up at vy, the head stopping at `ceil` (feet
    /// height) like Source: vertical speed cut to zero, then the fall from there. NaN if never.</summary>
    static float FlightTime(float y, float vy, float target, float ceil)
    {
        if (vy <= 0f || y + vy * vy / (2f * Gravity) <= ceil) return FallTime(y - target, vy);
        float up = (vy - Mathf.Sqrt(Mathf.Max(0f, vy * vy - 2f * Gravity * (ceil - y)))) / Gravity;
        return ceil < target ? float.NaN : up + Mathf.Sqrt(2f * (ceil - target) / Gravity);
    }

    /// <summary>Feet height `t` s after leaving `y0` at vy, with the head stopping at `ceil`.</summary>
    static float HeightAt(float y0, float vy, float t, float ceil)
    {
        float free = y0 + vy * t - 0.5f * Gravity * t * t;
        if (vy <= 0f || y0 + vy * vy / (2f * Gravity) <= ceil) return free;
        float up = (vy - Mathf.Sqrt(Mathf.Max(0f, vy * vy - 2f * Gravity * (ceil - y0)))) / Gravity;
        return t <= up ? free : ceil - 0.5f * Gravity * (t - up) * (t - up);
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

    int lastSegment = -1, lastSegmentSection = -1;

    static int SegmentAt(Vector2[] line, float s)
    {
        for (int i = 0; i + 1 < line.Length; i++)
        {
            float len = (line[i + 1] - line[i]).magnitude;
            if (s <= len) return i;
            s -= len;
        }
        return line.Length - 2;
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
        // The view and keys the movement is acting on now (sent yawLag / keyLag frames ago), so the overlay matches.
        camYaw = sentYaw[(frame - yawLag + 32) % 32];
        float eye = (bool)movement.GetProgramVariable("ducked") ? 47f : 64f; // CS:S eye heights
        cam.transform.SetPositionAndRotation((pos + Vector3.up * eye) * U, Quaternion.Euler(10f, camYaw, 0f));
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        shot.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
        shot.Apply();
        RenderTexture.active = null;
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(recordDir, $"f{shotNumber++:D5}.jpg"), shot.EncodeToJPG(88));
        string keys = sentKeys[(frame - keyLag + 32) % 32] ?? "";
        if ((bool)movement.GetProgramVariable("ducked")) keys += "+Duck";
        hud.Append(Mathf.RoundToInt(v.magnitude)).Append('\t').Append(timer != null ? Label() : "").Append('\t').Append(keys).Append('\n');
        if (shotNumber % 500 == 0) FlushHud(); // a run stopped early keeps its overlay up to here
    }

    void FlushHud()
    {
        System.IO.File.AppendAllText(System.IO.Path.Combine(recordDir, "hud.txt"), hud.ToString());
        hud.Clear();
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
    void SetYaw(float yaw)
    {
        appliedYaw = yaw;
        sentYaw[frame % 32] = yaw;
        playerBody.rotation = Quaternion.Euler(0, yaw, 0);
    }

    readonly float[] sentYaw = new float[32];
    readonly string[] sentKeys = new string[32];
    bool crouching, toggleCrouch; // VRChat's crouch (C toggles it on desktop), and a press of C due with the next keys

    void Crouch(bool on) { toggleCrouch = on != crouching; }

    void Keys(params Key[] keys)
    {
        if (toggleCrouch)
        {
            System.Array.Resize(ref keys, keys.Length + 1);
            keys[keys.Length - 1] = Key.C; // down for one frame: ClientSim toggles on the press
            crouching = !crouching;
            toggleCrouch = false;
        }
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
        lastKeys = string.Join("+", keys);
        sentKeys[frame % 32] = lastKeys;
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
