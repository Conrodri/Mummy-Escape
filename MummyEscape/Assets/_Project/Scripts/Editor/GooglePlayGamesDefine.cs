using System.Linq;
using UnityEditor;
using UnityEditor.Build;

namespace MummyEscape.EditorTools
{
    /// <summary>
    /// Turns the Google Play Games sign-in on (scripting define MUMMY_GPGS for Android) as soon as the Google Play
    /// Games plugin is in the project, and off again if it is removed, so the project compiles either way.
    /// </summary>
    [InitializeOnLoad]
    static class GooglePlayGamesDefine
    {
        const string Define = "MUMMY_GPGS";

        static GooglePlayGamesDefine() => EditorApplication.delayCall += Sync;

        static void Sync()
        {
            bool plugin = System.AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "Google.Play.Games");
            var target = NamedBuildTarget.Android;
            var defines = PlayerSettings.GetScriptingDefineSymbols(target).Split(';').Where(d => d.Length > 0).ToList();
            if (plugin == defines.Contains(Define)) return;
            if (plugin) defines.Add(Define); else defines.Remove(Define);
            PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", defines));
            UnityEngine.Debug.Log(plugin ? "[Google] Play Games plugin found: sign-in with Google Play Games on." : "[Google] Play Games plugin gone: sign-in off.");
        }
    }
}
