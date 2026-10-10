using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using VRC.SDK3.ClientSim;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// A short showcase clip of SourceMovement's ladders on the test map (real Unity + ClientSim, real input): walk into
/// the ladder, climb up, hang on, climb down, move sideways, jump off. A fixed wide camera and a CS:S-sized stand-in
/// for the player (ClientSim's avatar hidden); the ladder is widened so sideways moves show. Frames at 50 fps to recordDir/f#####.jpg plus hud.txt (speed, caption,
/// keys) for make_video.sh. Started by LadderDemoBootstrap.Run.
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
    Transform standIn;
    RenderTexture rt;
    Texture2D shot;
    int frameNumber;
    string caption = "", keys = "";
    readonly StringBuilder hud = new StringBuilder();
    Vector3 camAim;

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
        for (int i = 0; i < 10; i++) yield return null;
        SetUpScene();

        // The test map's ladder: its face at x 144 on a tower, facing -x; the player starts in front of it facing +x.
        yield return Teleport(new Vector3(40, 0, 500), 90f);
        yield return Seconds(0.3f, null, false);

        caption = "Walk into the ladder";
        yield return Seconds(0.8f, null, true);
        Keys("W");
        for (int i = 0; i < 100 && !(bool)movement.GetProgramVariable("onLadder"); i++) yield return Seconds(1f / 50f, null, true);
        caption = "Climb up (W)";
        yield return Seconds(1.6f, null, true);
        caption = "Let go: hang on";
        Keys();
        yield return Seconds(0.8f, null, true);
        caption = "Climb down (S)";
        Keys("S");
        yield return Seconds(1.0f, null, true);
        caption = "Sideways (A)";
        Keys("A");
        yield return Seconds(0.6f, null, true);
        caption = "Sideways (D)";
        Keys("D");
        yield return Seconds(1.2f, null, true);
        caption = "Hang on";
        Keys();
        yield return Seconds(0.5f, null, true);
        caption = "Jump off (Space)";
        Keys("Space");
        yield return Seconds(0.2f, null, true);
        Keys();
        yield return Seconds(1.6f, null, true);
        System.IO.File.WriteAllText(System.IO.Path.Combine(recordDir, "hud.txt"), hud.ToString());
        Debug.Log($"[LADDERDEMO] done: {frameNumber} frames in {recordDir}");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#endif
    }

    void SetUpScene()
    {
        // Dev-texture grid so motion shows (as PlayTestRunner's demo); the ladder in its own colour.
        var grid = new Material(Shader.Find("SourceDemo/Grid"));
        var wood = new Material(grid) { color = new Color(0.85f, 0.55f, 0.25f) };
        foreach (var r in GameObject.Find("TestMap").GetComponentsInChildren<MeshRenderer>())
        {
            var c = r.GetComponent<Collider>();
            if (r.name == "Ladder") r.sharedMaterial = wood;
            else if (c != null && c.isTrigger) r.enabled = false;
            else r.sharedMaterial = grid;
        }
        // A wider tower and ladder (64 units is too narrow to show moving sideways).
        var tower = GameObject.Find("LadderTower").transform;
        var ladder = GameObject.Find("Ladder").transform;
        tower.localScale = new Vector3(tower.localScale.x, tower.localScale.y, tower.localScale.z * 4f);
        ladder.localScale = new Vector3(ladder.localScale.x, ladder.localScale.y, ladder.localScale.z * 5f);
        Physics.SyncTransforms();
        // The map is static-batched, so scaling moved only the colliders: draw the wider boxes as new cubes.
        foreach (var t in new[] { tower, ladder })
        {
            var b = t.GetComponent<Collider>().bounds;
            var r = t.GetComponent<MeshRenderer>();
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(cube.GetComponent<Collider>());
            cube.transform.SetPositionAndRotation(b.center, Quaternion.identity);
            cube.transform.localScale = b.size;
            cube.GetComponent<MeshRenderer>().sharedMaterial = r.sharedMaterial;
            r.enabled = false;
        }

        var box = GameObject.CreatePrimitive(PrimitiveType.Cube); // the player: CS:S hull, 32 wide, 62 (ducked 45) tall
        Destroy(box.GetComponent<Collider>());
        box.GetComponent<MeshRenderer>().sharedMaterial = new Material(Shader.Find("Standard")) { color = new Color(0.2f, 0.55f, 0.95f) };
        standIn = box.transform;
        var nose = GameObject.CreatePrimitive(PrimitiveType.Cube); // shows which way the player faces
        Destroy(nose.GetComponent<Collider>());
        nose.GetComponent<MeshRenderer>().sharedMaterial = new Material(Shader.Find("Standard")) { color = Color.white };
        nose.transform.SetParent(standIn, false);
        nose.transform.localPosition = new Vector3(0, 0.3f, 0.5f);
        nose.transform.localScale = new Vector3(0.5f, 0.15f, 0.2f);

        cam = new GameObject("LadderDemoCamera").AddComponent<Camera>();
        cam.fieldOfView = 60f;
        cam.nearClipPlane = 0.05f;
        rt = new RenderTexture(1280, 720, 24);
        shot = new Texture2D(1280, 720, TextureFormat.RGB24, false);
    }

    IEnumerator Seconds(float s, System.Action each, bool record)
    {
        int n = Mathf.Max(1, Mathf.RoundToInt(s * 50f));
        for (int i = 0; i < n; i++)
        {
            yield return null;
            each?.Invoke();
            if (record) Capture();
        }
    }

    void Capture()
    {
        Vector3 feet = player.GetPosition();
        bool ducked = (bool)movement.GetProgramVariable("ducked");
        float height = (ducked ? 45f : 62f) * U;
        standIn.SetPositionAndRotation(feet + Vector3.up * height * 0.5f, Quaternion.Euler(0, playerBody.eulerAngles.y, 0));
        standIn.localScale = new Vector3(32f * U, height, 32f * U);
        // ClientSim's own avatar would sit inside the stand-in: hide it.
        foreach (var r in FindObjectsOfType<SkinnedMeshRenderer>()) r.enabled = false;
        // A fixed wide shot from the side and in front of the ladder (so heights show), turning to follow the player.
        Vector3 aim = feet + Vector3.up * 40f * U;
        camAim = frameNumber == 0 ? aim : Vector3.Lerp(camAim, aim, 0.1f);
        cam.transform.position = new Vector3(-420f, 230f, 260f) * U;
        cam.transform.LookAt(new Vector3(camAim.x, Mathf.Max(camAim.y, 150f * U), camAim.z));
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
