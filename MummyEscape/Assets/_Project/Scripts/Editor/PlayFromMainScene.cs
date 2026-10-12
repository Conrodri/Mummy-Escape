using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace MummyEscape.EditorTools
{
    /// <summary>Play always starts the game from the Main scene, whatever scene is open in the Editor.</summary>
    [InitializeOnLoad]
    static class PlayFromMainScene
    {
        static PlayFromMainScene()
        {
            EditorApplication.delayCall += Assign;
            EditorApplication.playModeStateChanged += s =>
            {
                if (s != PlayModeStateChange.ExitingEditMode) return;
                // Play mode tests enter Play from the runner's own InitTestScene: forcing Main there would drop the test controller.
                // A killed run can leave that scene open after its file was deleted; its stale controller would rerun the tests on every Play.
                var active = SceneManager.GetActiveScene();
                if (active.name.StartsWith("InitTestScene") && File.Exists(active.path)) EditorSceneManager.playModeStartScene = null;
                else Assign();
            };
        }

        static int _retries;

        static void Assign()
        {
            if (EditorSceneManager.playModeStartScene != null) return;
            var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ProjectSetup.ScenePath);
            if (scene != null) EditorSceneManager.playModeStartScene = scene;
            else if (++_retries < 100) EditorApplication.delayCall += Assign; // asset database still importing on first open
        }
    }
}
