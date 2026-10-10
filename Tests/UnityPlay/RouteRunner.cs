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

    struct Spot { public Vector3 p; public float s, lat; }

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
        foreach (var line in Route) { var list = FindSpots(line); spots.Add(list); total += list.Count; }
        Log($"{total} landing spots in {Route.Length} sections ({Time.realtimeSinceStartup - t0:F1} s)");

        // Start in the start zone facing down the first lane.
        Vector2 a = Route[0][0], b = Route[0][1];
        viewYaw = camYaw = Yaw(b - a);
        movement.SetProgramVariable("__0_position__param", new Vector3(a.x, 49f, a.y) * U);
        movement.SetProgramVariable("__0_rotation__param", Quaternion.Euler(0, viewYaw, 0));
        movement.SetProgramVariable("__0_keepVelocity__param", false);
        movement.SendCustomEvent("__0_TeleportPlayer");
        for (int i = 0; i < frameRate; i++) yield return null;

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
                }
                hopping = false;
                hasTarget = false;
            }
            last = pos;
            Step(pos);
            yield return null;
            frame++;
            if (recordDir != "" && frame % 2 == 0) Capture();
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
    void Step(Vector3 pos)
    {
        Vector3 vel = (Vector3)movement.GetProgramVariable("velocity");
        bool onGround = (bool)movement.GetProgramVariable("onGround");
        Vector2 here = Flat(pos), v = new Vector2(vel.x, vel.z);
        progress = Project(here, progress);
        var line = Route[section];
        float lookYaw = Yaw(PointAt(line, progress + 160f) - here);

        if (!hopping)
        {
            // From a standstill (start, a teleport): run until fast, or until the floor ahead ends.
            viewYaw = lookYaw;
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
            hasTarget = PickTarget(pos, vel, out target);
            if (!hasTarget) Log($"no landing spot from {here} at {v.magnitude:F0} u/s (s {progress:F0}), heading for the section end");
        }
        wasOnGround = onGround;
        if (onGround) { Keys(Key.Space); return; }

        // Velocity wanted now: straight onto the target, arriving when we come down to its height.
        Vector2 aim = hasTarget ? new Vector2(target.p.x, target.p.z) : line[line.Length - 1];
        float h = hasTarget ? target.p.y : pos.y;
        float tLeft = FallTime(pos.y - h, vel.y);
        Vector2 want = hasTarget ? (aim - here) / Mathf.Max(tLeft, 0.02f) : (aim - here).normalized * Mathf.Max(v.magnitude, 250f);
        if (hasTarget && (float.IsNaN(tLeft) || tLeft < 0.02f)) want = v;

        // Air acceleration (sv_airaccelerate 1000): the velocity along the wish direction goes up to 30 u/s.
        float bestErr = (want - v).magnitude, bestYaw = 0f;
        bool push = false;
        if (bestErr > 4f)
            for (int i = 0; i < 180; i++)
            {
                float yaw = i * 2f;
                Vector2 w = Dir(yaw);
                float add = AirCap - Vector2.Dot(v, w);
                if (add <= 0f) continue;
                float err = (want - (v + w * add)).magnitude;
                if (err < bestErr - 0.5f) { bestErr = err; bestYaw = yaw; push = true; }
            }
        if (!push)
        {
            viewYaw = Mathf.MoveTowardsAngle(viewYaw, Yaw(aim - here), 360f * Time.deltaTime);
            SetYaw(viewYaw);
            Keys(Key.Space);
            return;
        }
        // Key combination whose direction keeps the view closest to where we're going.
        Vector2 toAim = aim - here;
        float look = toAim.magnitude > 1f ? Yaw(toAim) : viewYaw;
        float bestDiff = 999f, bestAngle = 0f;
        int bestKey = 0;
        for (int k = 0; k < 8; k++)
        {
            float diff = Mathf.Abs(Mathf.DeltaAngle(bestYaw - k * 45f, look));
            if (diff < bestDiff) { bestDiff = diff; bestKey = k; bestAngle = k * 45f; }
        }
        viewYaw = bestYaw - bestAngle;
        SetYaw(viewYaw);
        switch (bestKey)
        {
            case 0: Keys(Key.Space, Key.W); break;
            case 1: Keys(Key.Space, Key.W, Key.D); break;
            case 2: Keys(Key.Space, Key.D); break;
            case 3: Keys(Key.Space, Key.S, Key.D); break;
            case 4: Keys(Key.Space, Key.S); break;
            case 5: Keys(Key.Space, Key.S, Key.A); break;
            case 6: Keys(Key.Space, Key.A); break;
            default: Keys(Key.Space, Key.W, Key.A); break;
        }
    }

    /// <summary>
    /// The landing spot to aim for from a takeoff: furthest along the section, reachable at about the current speed
    /// (strafing can add a little), not much slower than now, with a flight path that hits nothing.
    /// </summary>
    bool PickTarget(Vector3 pos, Vector3 vel, out Spot best)
    {
        best = default;
        var list = spots[section];
        Vector2 here = Flat(pos);
        float speed = new Vector2(vel.x, vel.z).magnitude;
        var options = new List<(float score, Spot spot, float need, float time)>();
        foreach (var s in list)
        {
            if (s.s < progress + 24f || s.s > progress + 700f) continue;
            float time = FallTime(pos.y - s.p.y, vel.y);
            if (float.IsNaN(time) || time < 0.2f) continue;
            float need = (new Vector2(s.p.x, s.p.z) - here).magnitude / time;
            if (need > speed + 20f) continue;
            float score = s.s - 0.5f * Mathf.Abs(s.lat) - (need < 0.6f * speed ? 300f : 0f);
            options.Add((score, s, need, time));
        }
        options.Sort((x, y) => y.score.CompareTo(x.score));
        foreach (var o in options)
            if (ClearFlight(pos, vel.y, new Vector2(o.spot.p.x, o.spot.p.z), o.time)) { best = o.spot; return true; }
        return false;
    }

    /// <summary>The hull along a straight flight to `to`: no solid in the way (before landing), no teleport touched.</summary>
    bool ClearFlight(Vector3 from, float vy, Vector2 to, float time)
    {
        Vector2 start = Flat(from);
        for (int i = 1; i <= 12; i++)
        {
            float t = time * i / 12f;
            Vector2 p = Vector2.Lerp(start, to, (float)i / 12f);
            float y = from.y + vy * t - 0.5f * Gravity * t * t;
            var center = new Vector3(p.x, y + 37f, p.y) * U;
            if (i < 12 && Physics.CheckBox(center, new Vector3(15.5f, 35.5f, 15.5f) * U, Quaternion.identity, layers, QueryTriggerInteraction.Ignore)) return false;
            if (TouchesTeleport(center)) return false;
        }
        return true;
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
            for (float along = 0f; along <= len; along += 8f)
                for (float lat = -176f; lat <= 176f; lat += 16f)
                {
                    Vector2 p = a + d * along + side * lat;
                    if (Safe(new Vector3(p.x, 300f, p.y), out float top))
                        list.Add(new Spot { p = new Vector3(p.x, top, p.y), s = s0 + along, lat = lat });
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
        float want = v.magnitude > 100f ? Yaw(v) : viewYaw;
        camYaw = Mathf.SmoothDampAngle(camYaw, want, ref camYawVel, 0.25f, Mathf.Infinity, 2f / frameRate);
        cam.transform.SetPositionAndRotation((pos + Vector3.up * 64f) * U, Quaternion.Euler(12f, camYaw, 0f));
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        shot.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
        shot.Apply();
        RenderTexture.active = null;
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(recordDir, $"f{shotNumber++:D5}.jpg"), shot.EncodeToJPG(88));
        hud.Append(Mathf.RoundToInt(v.magnitude)).Append('\t').Append(timer != null ? Label() : "").Append('\n');
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
    void SetYaw(float yaw) { playerBody.rotation = Quaternion.Euler(0, yaw, 0); }
    void Keys(params Key[] keys) { InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys)); }

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
