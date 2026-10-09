using System;
using System.Reflection;
using UnityEngine;
using VRC.SDKBase;

/// <summary>C# backend: drives the SourceMovement script directly with a simulated VRChat player.</summary>
public class Rig
{
    public const float U = 0.01905f; // metres per Source unit
    public readonly SourceMovement move = new SourceMovement();
    public readonly VRCPlayerApi player = new VRCPlayerApi();
    public float frameTime = 0.01f;
    public bool oneFrameLatency; // VRChat applies our velocity a frame late
    public int teleportDelayFrames; // VRChat applies teleports this many frames late
    Vector3 pendingVelocity, pendingTeleport;
    float pendingYaw;
    int teleportFramesLeft = -1;

    public Rig(Vector3 startUnits)
    {
        CollisionWorld.Clear();
        Input.scroll = 0;
        Input.keysDown.Clear();
        Time.time = 0;
        Networking.LocalPlayer = player;
        player.position = startUnits * U;
        player.onTeleport = (p, r) => { pendingTeleport = p; pendingYaw = r.eulerAngles.y; teleportFramesLeft = teleportDelayFrames; ApplyTeleport(); };
        Call("Start");
    }

    // ------------------------------------------------------------- world building (Source units)
    public static void Floor(float y = 0, float size = 100000) => Box(new Vector3(0, y - 50, 0), new Vector3(size, 100, size));
    /// <summary>Ladder volume on layer 22 (SourceMovement's default ladder layer).</summary>
    /// <summary>Water volume on layer 4 (SourceMovement's default water layer).</summary>
    public static void Water(Vector3 center, Vector3 size) => CollisionWorld.Add(center * U, size * U, CollisionWorld.Euler(0, 0, 0), 4);
    public bool InWater => Get<int>("waterLevel") >= 2;
    public int WaterLevel => Get<int>("waterLevel");
    public static void Ladder(Vector3 center, Vector3 size) => CollisionWorld.Add(center * U, size * U, CollisionWorld.Euler(0, 0, 0), 22);
    public static void Box(Vector3 center, Vector3 size, float rotZ = 0, float rotX = 0) =>
        CollisionWorld.Add(center * U, size * U, CollisionWorld.Euler(rotX, 0, rotZ));

    // ------------------------------------------------------------- input and events
    public void Move(float forward, float right)
    {
        move.InputMoveVertical(forward, default);
        move.InputMoveHorizontal(right, default);
    }
    public void Jump(bool held) => move.InputJump(held, default);
    public void Scroll(float delta) => Input.scroll = delta;
    public void PressKey(KeyCode key) => Input.keysDown.Add(key);
    public float Yaw { get => player.yaw; set => player.yaw = value; }
    public float Pitch { get => player.pitch; set => player.pitch = value; }
    public bool AutoBhop { get => move.autoBhop; set => move.autoBhop = value; }
    public void Respawn() => move.OnPlayerRespawn(player);
    public void SetActive(bool on) => move.SetMovementActive(on);
    public void Teleport(Vector3 units) => player.TeleportTo(units * U, Quaternion.identity);
    public void SetVelocity(Vector3 v) => Set("velocity", v);
    public void TeleportPlayer(Vector3 units, float yaw, bool keepVelocity) =>
        move.TeleportPlayer(units * U, Quaternion.Euler(0, yaw, 0), keepVelocity);

    void ApplyTeleport()
    {
        if (teleportFramesLeft-- != 0) return;
        player.position = pendingTeleport;
        player.yaw = pendingYaw;
    }

    // ------------------------------------------------------------- stepping
    public void Frame()
    {
        Time.deltaTime = frameTime;
        Time.time += frameTime;
        Call("Update");
        Input.scroll = 0;
        Input.keysDown.Clear();
        // Stand-in for VRChat's character controller: move by the velocity we were given.
        Vector3 v = oneFrameLatency ? pendingVelocity : player.velocity;
        pendingVelocity = player.velocity;
        player.position += v * frameTime;
        if (teleportFramesLeft >= 0) ApplyTeleport();
    }

    public void Run(float seconds, Action eachFrame = null)
    {
        int frames = (int)Math.Round(seconds / frameTime);
        for (int i = 0; i < frames; i++)
        {
            eachFrame?.Invoke();
            Frame();
        }
    }

    // ------------------------------------------------------------- readouts (Source units)
    public Vector3 Pos => player.position / U;
    public Vector3 Origin => Get<Vector3>("origin");
    public Vector3 Vel => move.GetSourceVelocity();
    public float Speed => move.GetSpeed();
    public bool OnGround => move.IsOnGround();
    public void SetPush(Vector3 units) => move.SetPush(units);
    public void AddVelocity(Vector3 units) => move.AddVelocity(units);
    public void SetGravityScale(float scale) => move.SetGravityScale(scale);
    public bool OnLadder => Get<bool>("onLadder");
    public float VelYaw => Mathf.Atan2(Vel.x, Vel.z) * Mathf.Rad2Deg;
    public Vector3 PlayerVelocity => player.velocity;
    public float PlayerWalk => player.walk;
    public float PlayerRun => player.run;
    public float PlayerGravity => player.gravityStrength;

    T Get<T>(string field) =>
        (T)typeof(SourceMovement).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(move);
    void Set(string field, object value) =>
        typeof(SourceMovement).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(move, value);
    void Call(string method) =>
        typeof(SourceMovement).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public).Invoke(move, null);
}
