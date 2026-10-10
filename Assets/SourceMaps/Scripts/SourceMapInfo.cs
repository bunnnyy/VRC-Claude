using UdonSharp;
using UnityEngine;

/// <summary>
/// One playable map for SourceMapManager: put it on the map's root object (Tools > Source Maps > Add Map does).
/// The root (with everything under it) is switched off while nobody local is in this map.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class SourceMapInfo : UdonSharpBehaviour
{
    public string mapName = "bhop_map";
    public string author = "";
    [Tooltip("Shown on the vote screen")]
    public Texture thumbnail;
    [Tooltip("Where players arrive in this map")]
    public Transform spawn;
    [Tooltip("Map time limit in minutes for this map, 0 = use the manager's")]
    public float timeLimitMinutes = 0f;
    [Tooltip("The map's sky (its skyname), shown while you're in this map; empty = the world's")]
    public Material skybox;
}
