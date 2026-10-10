using System.Collections.Generic;
using System.IO;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;

/// <summary>
/// A map's sounds and music like Source: every ambient_generic becomes an AudioSource on its marker, heard within its
/// radius (so areas have their own music), and triggers that play them (OnStartTouch / OnTrigger "PlaySound") get a
/// SourceMapSoundTrigger. Sound files come from the map's pakfile or the CS:S folder (read by the caller) and are
/// saved as assets.
///   - looping: as in Source, only wavs with a loop point (cue chunk) loop,
///   - plays from the start unless "Start Silent" (spawnflag 16), or when a logic_auto plays it at map spawn,
///   - "Play everywhere" (spawnflag 1): heard everywhere (2D), otherwise fades out linearly to its radius,
///   - volume = health / 10, pitch / 100.
/// Soundscript names (no file extension, e.g. "explode_6") are skipped.
/// </summary>
public static class SourceMapSounds
{
    const float DefaultRadius = 1250f;

    /// <summary>Adds the sounds under `root` (a map imported by SourceMapImporter); returns how many play.</summary>
    public static int Add(GameObject root, string folder, float scale, System.Func<string, byte[]> read, List<string> missing)
    {
        var markers = root.GetComponentsInChildren<SourceEntity>(true);
        var atSpawn = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var m in markers)
            if (m.className == "logic_auto")
                foreach (var o in Outputs(m, "OnMapSpawn"))
                    if (o[1] == "playsound") atSpawn.Add(o[0]);

        var byName = new Dictionary<string, List<AudioSource>>(System.StringComparer.OrdinalIgnoreCase);
        var clips = new Dictionary<string, AudioClip>();
        var loops = new Dictionary<string, bool>();
        int count = 0;
        foreach (var m in markers)
        {
            if (m.className != "ambient_generic") continue;
            string file = SoundPath(m.GetValue("message"));
            if (file == null) continue;
            if (!clips.ContainsKey(file))
            {
                byte[] data = read(file);
                clips[file] = data != null ? SaveClip(data, folder + "/" + file.Substring("sound/".Length)) : null;
                loops[file] = data != null && HasCue(data);
                if (data == null) missing.Add(file);
            }
            if (clips[file] == null) continue;

            int flags = (int)Number(m.GetValue("spawnflags"), 0);
            var source = m.gameObject.AddComponent<AudioSource>();
            source.clip = clips[file];
            source.loop = loops[file];
            source.playOnAwake = (flags & 16) == 0 || atSpawn.Contains(m.targetName);
            source.volume = Mathf.Clamp01(Number(m.GetValue("health"), 10f) / 10f);
            source.pitch = Mathf.Clamp(Number(m.GetValue("pitch"), 100f), 1f, 255f) / 100f;
            source.spatialBlend = (flags & 1) != 0 ? 0f : 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.maxDistance = Number(m.GetValue("radius"), DefaultRadius) * scale;
            source.minDistance = Mathf.Min(1f, source.maxDistance * 0.5f);
            source.dopplerLevel = 0f;
            if (m.targetName != "")
            {
                if (!byName.ContainsKey(m.targetName)) byName[m.targetName] = new List<AudioSource>();
                byName[m.targetName].Add(source);
            }
            count++;
        }

        // Triggers that play or stop them.
        foreach (var m in markers)
        {
            if (!m.className.StartsWith("trigger_") || m.bounds.size == Vector3.zero) continue;
            var play = new List<AudioSource>();
            var stop = new List<AudioSource>();
            foreach (string evt in new[] { "OnStartTouch", "OnTrigger", "OnStartTouchAll" })
                foreach (var o in Outputs(m, evt))
                {
                    if (!byName.ContainsKey(o[0])) continue;
                    if (o[1] == "playsound" || o[1] == "togglesound") play.AddRange(byName[o[0]]);
                    else if (o[1] == "stopsound") stop.AddRange(byName[o[0]]);
                }
            if (play.Count == 0 && stop.Count == 0) continue;
            var go = new GameObject("Sound"); // its own object: the marker may have other trigger scripts
            go.transform.SetParent(m.transform, false);
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = m.bounds.center + new Vector3(0f, 4f * scale, 0f); // 8 units taller: VRChat's capsule floats
            box.size = m.bounds.size + new Vector3(0f, 8f * scale, 0f);
            var trigger = go.AddUdonSharpComponent<SourceMapSoundTrigger>();
            trigger.play = play.ToArray();
            trigger.stop = stop.ToArray();
            UdonSharpEditorUtility.CopyProxyToUdon(trigger);
        }
        return count;
    }

    /// <summary>"sound/…" path of an ambient_generic's message, or null for a soundscript name.</summary>
    public static string SoundPath(string message)
    {
        string s = message.Trim().TrimStart('*', '#', '@', '>', '<', '^', ')', '}', '$', '!', '?', '&', '~', '`', '+', '%')
            .Replace('\\', '/').ToLowerInvariant();
        if (!s.EndsWith(".wav") && !s.EndsWith(".mp3") && !s.EndsWith(".ogg")) return null;
        return "sound/" + s.TrimStart('/');
    }

    /// <summary>Outputs of an event as {target, input (lower case)}.</summary>
    static List<string[]> Outputs(SourceEntity m, string evt)
    {
        var result = new List<string[]>();
        if (m.keys == null) return result;
        for (int i = 0; i < m.keys.Length; i++)
        {
            if (!m.keys[i].Equals(evt, System.StringComparison.OrdinalIgnoreCase)) continue;
            string[] parts = m.values[i].Split(',');
            if (parts.Length >= 2) result.Add(new[] { parts[0].Trim(), parts[1].Trim().ToLowerInvariant() });
        }
        return result;
    }

    /// <summary>A wav with a "cue " chunk has a loop point: Source loops it.</summary>
    static bool HasCue(byte[] d)
    {
        if (d.Length < 12 || d[0] != 'R' || d[1] != 'I' || d[2] != 'F' || d[3] != 'F') return false;
        for (int i = 12; i + 8 <= d.Length;)
        {
            if (d[i] == 'c' && d[i + 1] == 'u' && d[i + 2] == 'e' && d[i + 3] == ' ') return true;
            int size = System.BitConverter.ToInt32(d, i + 4);
            if (size < 0) return false;
            i += 8 + size + (size & 1);
        }
        return false;
    }

    static AudioClip SaveClip(byte[] data, string assetPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(assetPath));
        File.WriteAllBytes(assetPath, data);
        AssetDatabase.ImportAsset(assetPath);
        return AssetDatabase.LoadAssetAtPath<AudioClip>(assetPath);
    }

    static float Number(string s, float fallback)
    {
        float f;
        return float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out f) ? f : fallback;
    }
}
