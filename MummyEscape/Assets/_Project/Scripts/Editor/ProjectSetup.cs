using System.IO;
using MummyEscape.App;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MummyEscape.EditorTools
{
    /// <summary>
    /// One-click project setup: lit sprite material, Main scene with the bootstrap, build settings, mobile player
    /// settings. Safe to run again. Batch: Unity -batchmode -quit -executeMethod MummyEscape.EditorTools.ProjectSetup.Run
    /// </summary>
    public static class ProjectSetup
    {
        public const string ScenePath = "Assets/_Project/Scenes/Main.unity";
        const string MaterialPath = "Assets/_Project/Art/SpriteLit.mat";
        const string UnlitMaterialPath = "Assets/_Project/Art/SpriteUnlit.mat";

        [MenuItem("Mummy Rush/Setup Project (scène + réglages)", priority = 0)]
        public static void Run()
        {
            Directory.CreateDirectory("Assets/_Project/Scenes");
            Directory.CreateDirectory("Assets/_Project/Art");

            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Lit-Default");
                if (shader == null) { Debug.LogError("[Setup] URP 2D Sprite-Lit-Default shader not found."); return; }
                material = new Material(shader) { name = "SpriteLit" };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }

            var unlit = AssetDatabase.LoadAssetAtPath<Material>(UnlitMaterialPath);
            if (unlit == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
                if (shader == null) { Debug.LogError("[Setup] URP 2D Sprite-Unlit-Default shader not found."); return; }
                unlit = new Material(shader) { name = "SpriteUnlit" };
                AssetDatabase.CreateAsset(unlit, UnlitMaterialPath);
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("GameBootstrap");
            var bootstrap = go.AddComponent<GameBootstrap>();
            var so = new SerializedObject(bootstrap);
            so.FindProperty("spriteLitMaterial").objectReferenceValue = material;
            so.FindProperty("spriteUnlitMaterial").objectReferenceValue = unlit;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

            PlayerSettings.companyName = "Mummy Rush";
            PlayerSettings.productName = "Mummy Rush";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.mummyrush.game");
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, "com.mummyrush.game");
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.allowedAutorotateToLandscapeLeft = false;
            PlayerSettings.allowedAutorotateToLandscapeRight = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.SplashScreen.backgroundColor = new Color(0.03f, 0.02f, 0.01f);
            PlayerSettings.statusBarHidden = true;

            AppIconGenerator.Generate();
            AssetDatabase.SaveAssets();
            Debug.Log($"[Setup] Done. Open {ScenePath} and press Play.");
        }
    }
}
