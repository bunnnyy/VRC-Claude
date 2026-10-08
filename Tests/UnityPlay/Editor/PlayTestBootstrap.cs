using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.SDK3.ClientSim;

/// <summary>
/// Batchmode entry point: opens the test map, adds the PlayTestRunner and enters play mode with ClientSim.
///   Unity -batchmode -projectPath P -executeMethod PlayTestBootstrap.Run [-smFrameRate 90] [-smOnly name]
/// The runner exits the editor with code 0 (all passed) or 1.
/// </summary>
public static class PlayTestBootstrap
{
    public static void Run()
    {
        var settings = ClientSimSettings.Instance;
        settings.enableClientSim = true;
        settings.hideMenuOnLaunch = true;
        settings.setTargetFrameRate = false;
        settings.initializationDelay = 0f;
        ClientSimSettings.SaveSettings(settings);

        EditorSceneManager.OpenScene("Assets/SourceMovement/SourceTestMap.unity");
        var runner = new GameObject("PlayTestRunner").AddComponent<PlayTestRunner>();
        string[] args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "-smFrameRate") runner.frameRate = int.Parse(args[i + 1]);
            if (args[i] == "-smOnly") runner.only = args[i + 1];
        }
        EditorApplication.isPlaying = true;
    }
}
