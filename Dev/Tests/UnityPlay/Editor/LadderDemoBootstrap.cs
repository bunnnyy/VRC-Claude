using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.SDK3.ClientSim;

/// <summary>
/// -executeMethod LadderDemoBootstrap.Run -smRecord dir: plays SourceMovement's test map with ClientSim and records the
/// ladder showcase (LadderDemo) into dir. Then: make_video.sh dir out.mp4.
/// </summary>
public static class LadderDemoBootstrap
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
        UdonSharp.Compiler.UdonSharpCompilerV1.CompileSync();
        string[] args = System.Environment.GetCommandLineArgs();
        var demo = new GameObject("LadderDemo").AddComponent<LadderDemo>();
        demo.recordDir = args[System.Array.IndexOf(args, "-smRecord") + 1];
        System.IO.Directory.CreateDirectory(demo.recordDir);
        EditorApplication.isPlaying = true;
    }
}
