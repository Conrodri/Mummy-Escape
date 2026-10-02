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
            EditorApplication.delayCall += () =>
                EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ProjectSetup.ScenePath);
        }
    }
}
