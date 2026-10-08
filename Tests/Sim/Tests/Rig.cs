using System;
using System.Reflection;
using UnityEngine;
using VRC.SDKBase;

/// <summary>Drives one SourceMovement instance and a simulated VRChat player frame by frame.</summary>
public class Rig
{
    public const float U = 0.01905f; // metres per Source unit
    public readonly SourceMovement move = new SourceMovement();
    public readonly VRCPlayerApi player = new VRCPlayerApi();
    public float frameTime = 0.01f;
    public bool oneFrameLatency; // VRChat applies our velocity a frame late
    Vector3 pendingVelocity;

    public Rig(Vector3 startUnits)
    {
        Physics.world.Clear();
        Input.scroll = 0;
        Input.keysDown.Clear();
        Time.time = 0;
        Networking.LocalPlayer = player;
        player.position = startUnits * U;
        Call("Start");
    }

    // ------------------------------------------------------------- world building (Source units)
    public static void Floor(float y = 0, float size = 100000) => Box(new Vector3(0, y - 50, 0), new Vector3(size, 100, size));
    public static Physics.Box Box(Vector3 center, Vector3 size, float rotZ = 0, float rotX = 0) =>
        Physics.AddBox(center * U, size * U, Quaternion.Euler(rotX, 0, rotZ));

    // ------------------------------------------------------------- input
    public void Move(float forward, float right)
    {
        move.InputMoveVertical(forward, default);
        move.InputMoveHorizontal(right, default);
    }
    public void Jump(bool held) => move.InputJump(held, default);
    public float Yaw { get => player.yaw; set => player.yaw = value; }

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
    public float VelYaw => Mathf.Atan2(Vel.x, Vel.z) * Mathf.Rad2Deg;

    public T Get<T>(string field) =>
        (T)typeof(SourceMovement).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(move);
    public void Set(string field, object value) =>
        typeof(SourceMovement).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(move, value);
    public void Call(string method) =>
        typeof(SourceMovement).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public).Invoke(move, null);
}
