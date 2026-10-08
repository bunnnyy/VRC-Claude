// Minimal stand-ins for the VRChat SDK / UdonSharp API used by the scripts.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace UdonSharp
{
    public enum BehaviourSyncMode { Any, None, NoVariableSync, Continuous, Manual }

    [AttributeUsage(AttributeTargets.Class)]
    public class UdonBehaviourSyncModeAttribute : Attribute { public UdonBehaviourSyncModeAttribute(BehaviourSyncMode m) { } }

    [AttributeUsage(AttributeTargets.Field)]
    public class UdonSyncedAttribute : Attribute { }

    public class UdonSharpBehaviour : MonoBehaviour
    {
        public virtual void InputJump(bool value, VRC.Udon.Common.UdonInputEventArgs args) { }
        public virtual void InputMoveVertical(float value, VRC.Udon.Common.UdonInputEventArgs args) { }
        public virtual void InputMoveHorizontal(float value, VRC.Udon.Common.UdonInputEventArgs args) { }
        public virtual void OnPlayerRespawn(VRC.SDKBase.VRCPlayerApi player) { }
        public virtual void OnPlayerTriggerEnter(VRC.SDKBase.VRCPlayerApi player) { }
        public virtual void OnPlayerTriggerExit(VRC.SDKBase.VRCPlayerApi player) { }
        public virtual void OnDeserialization() { }
        public virtual void PostLateUpdate() { }
        public virtual void Interact() { }

        // Network simulation hooks, set by tests.
        public int serializationRequests;
        public List<(string name, object[] args)> sentNetworkEvents = new List<(string, object[])>();
        public void RequestSerialization() => serializationRequests++;
        public object GetProgramVariable(string name) =>
            GetType().GetField(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(this);
        public void SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget target, string eventName, params object[] args)
            => sentNetworkEvents.Add((eventName, args));
    }
}

namespace VRC.Udon.Common
{
    public struct UdonInputEventArgs { }
}

namespace VRC.Udon.Common.Interfaces
{
    public enum NetworkEventTarget { All, Owner, Others, Self }
}

namespace VRC.SDK3.UdonNetworkCalling
{
    [AttributeUsage(AttributeTargets.Method)]
    public class NetworkCallableAttribute : Attribute
    {
        public NetworkCallableAttribute() { }
        public NetworkCallableAttribute(int maxEventsPerSecond) { }
    }

    public static class NetworkCalling
    {
        public static bool InNetworkCall;
        public static VRC.SDKBase.VRCPlayerApi CallingPlayer;
    }
}

namespace VRC.SDKBase
{
    public class VRCPlayerApi
    {
        public enum TrackingDataType { Head, LeftHand, RightHand, Origin }

        public struct TrackingData
        {
            public Vector3 position;
            public Quaternion rotation;
        }

        public bool isLocal = true;
        public string displayName = "LocalPlayer";
        public int playerId = 1;
        public bool IsValid() => true;

        // Simulated character controller state (metres).
        public Vector3 position;
        public Vector3 velocity;
        public float yaw, pitch;
        public float walk = 2f, run = 4f, strafe = 2f, jump = 3f, gravityStrength = 1f;

        public Vector3 GetPosition() => position;
        public Vector3 GetVelocity() => velocity;
        public void SetVelocity(Vector3 v) => velocity = v;
        public Quaternion GetRotation() => Quaternion.Euler(0, yaw, 0);
        public Action<Vector3, Quaternion> onTeleport; // lets tests delay teleports like a real client might
        public void TeleportTo(Vector3 p, Quaternion r)
        {
            if (onTeleport != null) onTeleport(p, r);
            else { position = p; yaw = r.eulerAngles.y; }
        }
        public TrackingData GetTrackingData(TrackingDataType t) => new TrackingData
        {
            position = position + Vector3.up * 1.6f,
            rotation = Quaternion.Euler(pitch, yaw, 0),
        };

        public float GetWalkSpeed() => walk;
        public float GetRunSpeed() => run;
        public float GetStrafeSpeed() => strafe;
        public float GetJumpImpulse() => jump;
        public float GetGravityStrength() => gravityStrength;
        public void SetWalkSpeed(float v) => walk = v;
        public void SetRunSpeed(float v) => run = v;
        public void SetStrafeSpeed(float v) => strafe = v;
        public void SetJumpImpulse(float v) => jump = v;
        public void SetGravityStrength(float v) => gravityStrength = v;
    }

    public static class Networking
    {
        public static VRCPlayerApi LocalPlayer;
        public static bool localIsOwner = true;
        public static bool IsOwner(GameObject go) => localIsOwner;
        public static VRCPlayerApi GetOwner(GameObject go) => LocalPlayer;
        public static void SetOwner(VRCPlayerApi p, GameObject go) { }
    }
}
