using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

/// <summary>
/// A trigger whose outputs play or stop ambient_generic sounds ("sound,PlaySound" / "StopSound" on OnStartTouch or
/// OnTrigger): when the local player walks in, the sounds play for them (the map's own triggers are touched by each
/// player on their own, so it's local, like a client-side sound).
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class SourceMapSoundTrigger : UdonSharpBehaviour
{
    public AudioSource[] play;
    public AudioSource[] stop;

    public override void OnPlayerTriggerEnter(VRCPlayerApi player)
    {
        if (!Utilities.IsValid(player) || !player.isLocal) return;
        if (stop != null) foreach (var s in stop) if (s != null) s.Stop();
        if (play != null) foreach (var s in play) if (s != null) s.Play();
    }
}
