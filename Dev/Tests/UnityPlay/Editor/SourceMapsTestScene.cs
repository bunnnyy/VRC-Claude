using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// The map rotation's test scene (run.sh vote): six small box "maps" far apart plus the lobby, saved as
/// Assets/SourceMaps/SourceMapsTest.unity in the test project.
///   Unity -batchmode -projectPath P -executeMethod SourceMapsTestScene.Build -quit
/// </summary>
public static class SourceMapsTestScene
{
    public static void Build()
    {
        SourceMapImporter.EnsureProgramAssets();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        var world = AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.vrchat.worlds/Samples/UdonExampleScene/Prefabs/VRCWorld.prefab");
        if (world != null) PrefabUtility.InstantiatePrefab(world);
        // Six small maps far apart (like imported maps at their own offsets), each a floor with a coloured pillar.
        Color[] colors = { Color.red, Color.green, Color.blue, Color.yellow, Color.cyan, Color.magenta };
        for (int i = 0; i < 6; i++)
        {
            var root = new GameObject("test_map_" + (i + 1));
            root.transform.position = new Vector3(200 + i * 100, 0, 0);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.SetParent(root.transform, false);
            floor.transform.localPosition = new Vector3(0, -0.5f, 10);
            floor.transform.localScale = new Vector3(20, 1, 40);
            var pillar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pillar.transform.SetParent(root.transform, false);
            pillar.transform.localPosition = new Vector3(0, 2, 20);
            pillar.transform.localScale = new Vector3(2, 4, 2);
            pillar.GetComponent<MeshRenderer>().sharedMaterial = SourceMapsSetup.Colored(colors[i]);
            var info = SourceMapsSetup.AddMap(root);
            info.author = "Test author " + (i + 1);
            info.thumbnail = SourceMapsSetup.Swatch(colors[i]);
            UdonSharpEditorUtility.CopyProxyToUdon(info);
        }
        var movement = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/SourceMovement/SourceMovement.prefab");
        if (movement != null) PrefabUtility.InstantiatePrefab(movement); // optional: works without it
        SourceMapsSetup.CreateLobby(Vector3.zero);
        EditorSceneManager.SaveScene(scene, "Assets/SourceMaps/SourceMapsTest.unity");
    }
}
