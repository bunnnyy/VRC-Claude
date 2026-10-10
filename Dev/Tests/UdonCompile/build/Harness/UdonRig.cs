// Udon backend for the shared movement tests: runs the compiled SourceMovement Udon program in
// VRChat's real Udon VM. Every extern goes through VRChat's real Udon wrapper, except the few that
// are Unity-native (physics, input, time, euler angles), which are answered by the test world.
// The local player is wired up through VRCPlayerApi's hook delegates, the same way ClientSim does it.
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common.Delegates;
using VRC.Udon.Common.Interfaces;
using VRC.Udon.VM;

public class Rig
{
    public const float U = 0.01905f;

    static IUdonProgram program;
    static IUdonWrapper wrapper;
    static readonly List<(uint address, object value, Type type)> initialHeap = new List<(uint, object, Type)>();
    static VRCPlayerApi player;
    static Rig current;

    public float frameTime = 0.01f;
    public bool oneFrameLatency;
    public int teleportDelayFrames;
    Vector3 pendingTeleport;
    float pendingYaw;
    int teleportFramesLeft = -1;
    readonly UdonVM vm;
    Vector3 position, velocity, pendingVelocity;
    float yaw, pitch, scroll, walk = 2f, run = 4f, strafe = 2f, jump = 3f, gravityStrength = 1f, headHeight = 1.6f;
    readonly HashSet<KeyCode> keysDown = new HashSet<KeyCode>();
    readonly HashSet<KeyCode> keysHeld = new HashSet<KeyCode>();

    public static void Init(IUdonProgram compiled, IUdonWrapper realWrapper)
    {
        program = compiled;
        wrapper = new TestWrapper(realWrapper);
        IUdonHeap heap = program.Heap;
        for (uint a = 0; a < heap.GetHeapCapacity(); a++)
            if (heap.IsHeapVariableInitialized(a))
                initialHeap.Add((a, heap.GetHeapVariable(a), heap.GetHeapVariableType(a)));

        player = (VRCPlayerApi)Activator.CreateInstance(typeof(VRCPlayerApi), true);
        player.isLocal = true;
        player.displayName = "LocalPlayer";
        Hook("_LocalPlayer", (Func<VRCPlayerApi>)(() => player));
        Hook("_GetPosition", (Func<VRCPlayerApi, Vector3>)(p => current.position));
        Hook("_GetVelocity", (Func<VRCPlayerApi, Vector3>)(p => current.velocity));
        Hook("_SetVelocity", (Action<VRCPlayerApi, Vector3>)((p, v) => current.velocity = v));
        Hook("_GetTrackingData", (Func<VRCPlayerApi, VRCPlayerApi.TrackingDataType, VRCPlayerApi.TrackingData>)((p, t) =>
            new VRCPlayerApi.TrackingData(current.position + new Vector3(0, current.headHeight, 0), CollisionWorld.Euler(current.pitch, current.yaw, 0))));
        Hook("_GetAvatarEyeHeightAsMeters", (Func<VRCPlayerApi, float>)(p => 1.6f));
        Hook("_TeleportTo", (Action<VRCPlayerApi, Vector3, Quaternion>)((p, pos, rot) =>
        {
            current.pendingTeleport = pos;
            current.pendingYaw = CollisionWorld.EulerAngles(rot).y;
            current.teleportFramesLeft = current.teleportDelayFrames;
            current.ApplyTeleport();
        }));
        Hook("_GetWalkSpeed", (Func<VRCPlayerApi, float>)(p => current.walk));
        Hook("_GetRunSpeed", (Func<VRCPlayerApi, float>)(p => current.run));
        Hook("_GetStrafeSpeed", (Func<VRCPlayerApi, float>)(p => current.strafe));
        Hook("_GetJumpImpulse", (Func<VRCPlayerApi, float>)(p => current.jump));
        Hook("_GetGravityStrength", (Func<VRCPlayerApi, float>)(p => current.gravityStrength));
        Hook("_SetWalkSpeed", (Action<VRCPlayerApi, float>)((p, v) => current.walk = v));
        Hook("_SetRunSpeed", (Action<VRCPlayerApi, float>)((p, v) => current.run = v));
        Hook("_SetStrafeSpeed", (Action<VRCPlayerApi, float>)((p, v) => current.strafe = v));
        Hook("_SetJumpImpulse", (Action<VRCPlayerApi, float>)((p, v) => current.jump = v));
        Hook("_SetGravityStrength", (Action<VRCPlayerApi, float>)((p, v) => current.gravityStrength = v));
    }

    static void Hook(string name, Delegate d)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        FieldInfo f = typeof(VRCPlayerApi).GetField(name, flags) ?? typeof(Networking).GetField(name, flags)
            ?? throw new MissingFieldException("VRCPlayerApi", name);
        f.SetValue(null, d);
    }

    public Rig(Vector3 startUnits)
    {
        current = this;
        CollisionWorld.Clear();
        foreach (var (address, value, type) in initialHeap)
            program.Heap.SetHeapVariable(address, value is Array a ? a.Clone() : value, type);
        vm = new UdonVM(wrapper, new ZeroTime());
        vm.LoadProgram(program);
        position = startUnits * U;
        RunEvent("_start");
    }

    // ------------------------------------------------------------- world building (Source units)
    public static void Floor(float y = 0, float size = 100000) => Box(new Vector3(0, y - 50, 0), new Vector3(size, 100, size));
    public static void Water(Vector3 center, Vector3 size) => CollisionWorld.Add(center * U, size * U, CollisionWorld.Euler(0, 0, 0), 4);
    public bool InWater => GetVar<int>("waterLevel") >= 2;
    public int WaterLevel => GetVar<int>("waterLevel");
    public static void Ladder(Vector3 center, Vector3 size) => CollisionWorld.Add(center * U, size * U, CollisionWorld.Euler(0, 0, 0), 22);
    public static void Box(Vector3 center, Vector3 size, float rotZ = 0, float rotX = 0) =>
        CollisionWorld.Add(center * U, size * U, CollisionWorld.Euler(rotX, 0, rotZ));

    // ------------------------------------------------------------- input and events
    public void Move(float forward, float right)
    {
        SetVar("inputMoveVerticalFloatValue", forward);
        RunEvent("_inputMoveVertical");
        SetVar("inputMoveHorizontalFloatValue", right);
        RunEvent("_inputMoveHorizontal");
    }
    public void Jump(bool held)
    {
        SetVar("inputJumpBoolValue", held);
        RunEvent("_inputJump");
    }
    public void Scroll(float delta) => scroll = delta;
    public void PressKey(KeyCode key) => keysDown.Add(key);
    public void HoldKey(KeyCode key, bool down) { if (down) keysHeld.Add(key); else keysHeld.Remove(key); }
    /// <summary>Crouch in VRChat: the head drops to 1 m (ClientSim's crouch height) from 1.6 m.</summary>
    public void Crouch(bool down) => headHeight = down ? 1.0f : 1.6f;
    public bool Ducked => GetVar<bool>("ducked");
    public float Yaw { get => yaw; set => yaw = value; }
    public float Pitch { get => pitch; set => pitch = value; }
    public bool AutoBhop { get => GetVar<bool>("autoBhop"); set => SetVar("autoBhop", value); }
    public void Respawn()
    {
        SetVar("onPlayerRespawnPlayer", player);
        RunEvent("_onPlayerRespawn");
    }
    public void SetActive(bool on)
    {
        SetVar("__0_on__param", on);
        RunEvent("__0_SetMovementActive");
    }
    public void Teleport(Vector3 units) => position = units * U;
    public void SetVelocity(Vector3 v) => SetVar("velocity", v);
    public void SetPush(Vector3 units) { SetVar("__0_push__param", units); RunEvent("__0_SetPush"); }
    public void AddVelocity(Vector3 units) { SetVar("__0_impulse__param", units); RunEvent("__0_AddVelocity"); }
    public void SetGravityScale(float scale) { SetVar("__0_scale__param", scale); RunEvent("__0_SetGravityScale"); }
    public void TeleportPlayer(Vector3 units, float yaw, bool keepVelocity)
    {
        SetVar("__0_position__param", units * U);
        SetVar("__0_rotation__param", CollisionWorld.Euler(0, yaw, 0));
        SetVar("__0_keepVelocity__param", keepVelocity);
        RunEvent("__0_TeleportPlayer");
    }

    void ApplyTeleport()
    {
        if (teleportFramesLeft-- != 0) return;
        position = pendingTeleport;
        yaw = pendingYaw;
    }

    // ------------------------------------------------------------- stepping
    public void Frame()
    {
        RunEvent("_update");
        scroll = 0;
        keysDown.Clear();
        Vector3 v = oneFrameLatency ? pendingVelocity : velocity;
        pendingVelocity = velocity;
        position += v * frameTime;
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
    public Vector3 Pos => position / U;
    public Vector3 Origin => GetVar<Vector3>("origin");
    public Vector3 Vel => GetVar<Vector3>("velocity");
    public float Speed
    {
        get
        {
            RunEvent("GetSpeed");
            return GetVar<float>("__0_GetSpeed__ret");
        }
    }
    public bool OnGround => GetVar<bool>("onGround");
    public bool OnLadder => GetVar<bool>("onLadder");
    public float VelYaw => (float)(Math.Atan2(Vel.x, Vel.z) * 180.0 / Math.PI);
    public Vector3 PlayerVelocity => velocity;
    public float PlayerWalk => walk;
    public float PlayerRun => run;
    public float PlayerGravity => gravityStrength;

    // ------------------------------------------------------------- Udon plumbing
    void RunEvent(string name)
    {
        current = this;
        vm.SetProgramCounter(program.EntryPoints.GetAddressFromSymbol(name));
        uint result = vm.Interpret();
        if (result != 0) throw new Exception($"Udon VM failed in {name} (result {result})");
    }

    static T GetVar<T>(string symbol) => program.Heap.GetHeapVariable<T>(program.SymbolTable.GetAddressFromSymbol(symbol));
    static void SetVar<T>(string symbol, T value) => program.Heap.SetHeapVariable(program.SymbolTable.GetAddressFromSymbol(symbol), value);

    /// <summary>
    /// VRChat's security filter (which blocks access to blacklisted Unity objects) needs the native engine.
    /// It only guards object access and never changes values, so the tests pass objects through unfiltered.
    /// </summary>
    public class PassThroughSecurityFilter : IUdonSecurityFilter, IUdonSecurityFilter<UnityEngine.Object>
    {
        void IUdonSecurityFilter.ApplyFilter<T>(ref T objectToFilter) { }
        void IUdonSecurityFilter<UnityEngine.Object>.ApplyFilter(ref UnityEngine.Object objectToFilter) { }
        public void ApplyLightCullingMaskFilter(ref int mask) { }
        public int LightReservedLayerMask { get; set; }
    }

    class ZeroTime : IUdonVMTimeSource
    {
        public long CurrentTime => 0;
    }

    /// <summary>VRChat's real Udon wrapper with the Unity-native externs answered by the test world.</summary>
    class TestWrapper : IUdonWrapper
    {
        readonly IUdonWrapper inner;
        readonly Dictionary<string, UdonExternDelegate> overrides = new Dictionary<string, UdonExternDelegate>();
        static readonly FieldInfo hitPoint = typeof(RaycastHit).GetField("m_Point", BindingFlags.NonPublic | BindingFlags.Instance);
        static readonly FieldInfo hitNormal = typeof(RaycastHit).GetField("m_Normal", BindingFlags.NonPublic | BindingFlags.Instance);
        static readonly FieldInfo hitDistance = typeof(RaycastHit).GetField("m_Distance", BindingFlags.NonPublic | BindingFlags.Instance);

        public TestWrapper(IUdonWrapper inner)
        {
            this.inner = inner;
            overrides["UnityEngineTime.__get_deltaTime__SystemSingle"] = (heap, a) => heap.SetHeapVariable(a[0], current.frameTime);
            overrides["UnityEngineInput.__GetAxis__SystemString__SystemSingle"] = (heap, a) =>
                heap.SetHeapVariable(a[1], heap.GetHeapVariable<string>(a[0]) == "Mouse ScrollWheel" ? current.scroll : 0f);
            overrides["UnityEngineInput.__GetKeyDown__UnityEngineKeyCode__SystemBoolean"] = (heap, a) =>
                heap.SetHeapVariable(a[1], current.keysDown.Contains(heap.GetHeapVariable<KeyCode>(a[0])));
            overrides["UnityEngineInput.__GetKey__UnityEngineKeyCode__SystemBoolean"] = (heap, a) =>
                heap.SetHeapVariable(a[1], current.keysHeld.Contains(heap.GetHeapVariable<KeyCode>(a[0])));
            overrides["UnityEngineQuaternion.__get_eulerAngles__UnityEngineVector3"] = (heap, a) =>
                heap.SetHeapVariable(a[1], CollisionWorld.EulerAngles(heap.GetHeapVariable<Quaternion>(a[0])));
            overrides["UnityEnginePhysics.__BoxCast__UnityEngineVector3_UnityEngineVector3_UnityEngineVector3_UnityEngineRaycastHitRef_UnityEngineQuaternion_SystemSingle_SystemInt32_UnityEngineQueryTriggerInteraction__SystemBoolean"] =
                (heap, a) =>
                {
                    Vector3 center = heap.GetHeapVariable<Vector3>(a[0]);
                    Vector3 dir = heap.GetHeapVariable<Vector3>(a[2]);
                    bool hit = CollisionWorld.BoxCast(center, heap.GetHeapVariable<Vector3>(a[1]), dir,
                        heap.GetHeapVariable<float>(a[5]), out float distance, out Vector3 normal, heap.GetHeapVariable<int>(a[6]));
                    object boxed = new RaycastHit();
                    if (hit)
                    {
                        hitPoint.SetValue(boxed, center + dir * distance);
                        hitNormal.SetValue(boxed, normal);
                        hitDistance.SetValue(boxed, distance);
                    }
                    heap.SetHeapVariable(a[3], (RaycastHit)boxed);
                    heap.SetHeapVariable(a[8], hit);
                };
            overrides["UnityEnginePhysics.__Raycast__UnityEngineVector3_UnityEngineVector3_UnityEngineRaycastHitRef_SystemSingle_SystemInt32_UnityEngineQueryTriggerInteraction__SystemBoolean"] =
                (heap, a) =>
                {
                    Vector3 origin = heap.GetHeapVariable<Vector3>(a[0]);
                    Vector3 dir = heap.GetHeapVariable<Vector3>(a[1]);
                    bool hit = CollisionWorld.Raycast(origin, dir, heap.GetHeapVariable<float>(a[3]), out float distance, out Vector3 normal, heap.GetHeapVariable<int>(a[4]));
                    object boxed = new RaycastHit();
                    if (hit)
                    {
                        hitPoint.SetValue(boxed, origin + dir * distance);
                        hitNormal.SetValue(boxed, normal);
                        hitDistance.SetValue(boxed, distance);
                    }
                    heap.SetHeapVariable(a[2], (RaycastHit)boxed);
                    heap.SetHeapVariable(a[6], hit);
                };
            overrides["UnityEnginePhysics.__CheckBox__UnityEngineVector3_UnityEngineVector3_UnityEngineQuaternion_SystemInt32_UnityEngineQueryTriggerInteraction__SystemBoolean"] =
                (heap, a) => heap.SetHeapVariable(a[5], CollisionWorld.CheckBox(heap.GetHeapVariable<Vector3>(a[0]),
                    heap.GetHeapVariable<Vector3>(a[1]), heap.GetHeapVariable<int>(a[3])));
        }

        public UdonExternDelegate GetExternFunctionDelegate(string signature) =>
            overrides.TryGetValue(signature, out var d) ? d : inner.GetExternFunctionDelegate(signature);
        public int GetExternFunctionParameterCount(string signature) => inner.GetExternFunctionParameterCount(signature);
        public void RegisterWrapperModule(IUdonWrapperModule module) => inner.RegisterWrapperModule(module);
        public IUdonWrapperModule GetWrapperModuleByName(string name) => inner.GetWrapperModuleByName(name);
    }
}
