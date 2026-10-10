using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using VRC.SDK3.ClientSim;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// A short first-person clip of SourceMovement's ladder on its test map (real Unity + ClientSim, real input): walk
/// into it, climb up, hang on, climb down, sideways, jump off. Frames at 50 fps to recordDir/f#####.jpg
/// plus hud.txt (speed, caption, keys) for make_video.sh. Started by LadderDemoBootstrap.Run.
/// </summary>
public class LadderDemo : MonoBehaviour
{
    public string recordDir = "";
    const float U = 0.01905f;
    UdonBehaviour movement;
    VRCPlayerApi player;
    Transform playerBody;
    Keyboard keyboard;
    Camera cam;
    RenderTexture rt;
    Texture2D shot;
    int frameNumber;
    string caption = "", keys = "";
    readonly StringBuilder hud = new StringBuilder();

    IEnumerator Start()
    {
        Time.captureDeltaTime = 1f / 50f; // one frame per video frame
        for (int i = 0; i < 600 && (player == null || movement == null); i++)
        {
            yield return null;
            player = Networking.LocalPlayer;
            foreach (var u in FindObjectsOfType<UdonBehaviour>())
                if (u.gameObject.name == "SourceMovement") movement = Loaded(u);
        }
        foreach (var c in Resources.FindObjectsOfTypeAll<ClientSimPlayerController>())
            if (c.gameObject.scene.IsValid()) playerBody = c.transform;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        keyboard = InputSystem.AddDevice<Keyboard>("LadderDemoKeyboard");
        for (int i = 0; i < 30; i++) yield return null;
        foreach (var menu in Resources.FindObjectsOfTypeAll<ClientSimMenu>())
            if (menu.gameObject.scene.IsValid()) { menu.WarningAccepted(); menu.CloseMenu(); }
        cam = new GameObject("LadderDemoCamera").AddComponent<Camera>();
        cam.fieldOfView = 74f; // CS:S 106 degree horizontal fov at 16:9
        cam.nearClipPlane = 0.03f;
        cam.farClipPlane = 3000f;
        cam.enabled = false;
        rt = new RenderTexture(1280, 720, 24);
        shot = new Texture2D(1280, 720, TextureFormat.RGB24, false);

        // SourceMovement's test map: the ladder's face at x 144 on a tower, facing -x. With a CS:S folder (env SM_CSS),
        // the game's own ladder model is drawn on it.
        var grid = new Material(Shader.Find("SourceDemo/Grid"));
        foreach (var r in GameObject.Find("TestMap").GetComponentsInChildren<MeshRenderer>())
        {
            var c = r.GetComponent<Collider>();
            if (c != null && c.isTrigger) r.enabled = false;
            else r.sharedMaterial = grid;
        }
        string css = System.Environment.GetEnvironmentVariable("SM_CSS");
        if (!string.IsNullOrEmpty(css)) AddLadderModel(css);
        yield return Teleport(new Vector3(40, 0, 500), 90f);
        yield return Seconds(0.3f, false);

        caption = "Walk into the ladder";
        yield return Seconds(0.5f, true);
        Keys("W");
        for (int i = 0; i < 100 && !(bool)movement.GetProgramVariable("onLadder"); i++) yield return Seconds(1f / 50f, true);
        caption = "Climb up (W)";
        yield return Seconds(1.2f, true);
        caption = "Let go: hang on";
        Keys();
        yield return Seconds(0.6f, true);
        caption = "Climb down (S)";
        Keys("S");
        yield return Seconds(0.8f, true);
        caption = "Sideways (A)";
        Keys("A");
        yield return Seconds(0.2f, true);
        caption = "Sideways (D)";
        Keys("D");
        yield return Seconds(0.4f, true);
        caption = "Hang on";
        Keys();
        yield return Seconds(0.4f, true);
        caption = "Jump off (Space)";
        Keys("Space");
        yield return Seconds(0.2f, true);
        Keys();
        yield return Seconds(1.2f, true);
        System.IO.File.WriteAllText(System.IO.Path.Combine(recordDir, "hud.txt"), hud.ToString());
        Debug.Log($"[LADDERDEMO] done: {frameNumber} frames in {recordDir}");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#endif
    }

    /// <summary>CS:S's 128-unit aluminium ladder model (through uSource), stacked up the test ladder's 512 units.</summary>
    void AddLadderModel(string css)
    {
        var loader = System.Type.GetType("uSource.uLoader, uSource");
        var resources = System.Type.GetType("uSource.uResourceManager, uSource");
        loader.GetField("RootPath").SetValue(null, css);
        loader.GetField("UnitScale").SetValue(null, U);
        loader.GetField("SaveAssetsToUnity").SetValue(null, false);
        resources.GetMethod("Init", new[] { typeof(int), System.Type.GetType("uSource.IResourceProvider, uSource") }).Invoke(null, new object[] { 0, null });
        var ladder = GameObject.Find("Ladder");
        ladder.GetComponent<MeshRenderer>().enabled = false;
        float y = 0f;
        for (int i = 0; i < 20 && y < 512f * U; i++)
        {
            var model = (Transform)resources.GetMethod("LoadModel").Invoke(null, new object[] { "props/cs_assault/ladderaluminium128", false, false, false });
            var b = WorldBounds(model);
            if (b.size.x > b.size.z) { model.rotation = Quaternion.Euler(0, 90, 0) * model.rotation; b = WorldBounds(model); } // rungs across z
            model.position += new Vector3(144f * U, y, 500f * U) - new Vector3(b.max.x, b.min.y, b.center.z); // against the wall
            if (i == 0) Debug.Log($"[LADDERDEMO] ladder model: {b.size / U} units");
            y += b.size.y;
        }
    }

    static Bounds WorldBounds(Transform model)
    {
        var b = new Bounds();
        bool first = true;
        foreach (var r in model.GetComponentsInChildren<Renderer>())
        {
            var mesh = r is SkinnedMeshRenderer s ? new Mesh() : r.GetComponent<MeshFilter>().sharedMesh;
            if (r is SkinnedMeshRenderer skinned) skinned.BakeMesh(mesh);
            foreach (var v in mesh.vertices)
            {
                var w = r.transform.TransformPoint(v);
                if (first) { b = new Bounds(w, Vector3.zero); first = false; } else b.Encapsulate(w);
            }
        }
        return b;
    }

    IEnumerator Seconds(float s, bool record)
    {
        int n = Mathf.Max(1, Mathf.RoundToInt(s * 50f));
        for (int i = 0; i < n; i++)
        {
            yield return null;
            if (record) Capture();
        }
    }

    void Capture()
    {
        float eye = (bool)movement.GetProgramVariable("ducked") ? 47f : 64f; // CS:S eye heights
        cam.transform.SetPositionAndRotation(player.GetPosition() + Vector3.up * eye * U, Quaternion.Euler(-10f, playerBody.eulerAngles.y, 0));
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        shot.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
        shot.Apply();
        RenderTexture.active = null;
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(recordDir, $"f{frameNumber++:D5}.jpg"), shot.EncodeToJPG(90));
        Vector3 v = (Vector3)movement.GetProgramVariable("velocity");
        hud.Append(Mathf.RoundToInt(v.magnitude)).Append('\t').Append(caption).Append('\t').Append(keys).Append('\n');
    }

    void Keys(params string[] names)
    {
        var list = new System.Collections.Generic.List<Key>();
        foreach (var n in names) list.Add(n == "Space" ? Key.Space : n == "W" ? Key.W : n == "A" ? Key.A : n == "S" ? Key.S : Key.D);
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(list.ToArray()));
        keys = string.Join("+", names);
    }

    IEnumerator Teleport(Vector3 sourcePos, float yaw)
    {
        movement.SetProgramVariable("__0_position__param", sourcePos * U);
        movement.SetProgramVariable("__0_rotation__param", Quaternion.Euler(0, yaw, 0));
        movement.SetProgramVariable("__0_keepVelocity__param", false);
        movement.SendCustomEvent("__0_TeleportPlayer");
        yield return null;
        yield return null;
        playerBody.rotation = Quaternion.Euler(0, yaw, 0);
    }

    static UdonBehaviour Loaded(UdonBehaviour udon)
    {
        try { return udon != null && udon.GetProgramVariable("active") != null ? udon : null; }
        catch (System.NullReferenceException) { return null; }
    }
}
