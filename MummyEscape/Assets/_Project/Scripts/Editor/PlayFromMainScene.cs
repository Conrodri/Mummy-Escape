using UnityEditor;
using UnityEditor.SceneManagement;

namespace MummyEscape.EditorTools
{
    /// <summary>Play always starts the game from the Main scene, whatever scene is open in the Editor.</summary>
    [InitializeOnLoad]
    static class PlayFromMainScene
    {
        static PlayFromMainScene()
        {
            EditorApplication.delayCall += Assign;
            EditorApplication.playModeStateChanged += s => { if (s == PlayModeStateChange.ExitingEditMode) Assign(); };
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
