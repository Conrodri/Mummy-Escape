using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MummyEscape.EditorTools
{
    /// <summary>
    /// Mobile test builds. Batch:
    ///   Unity -batchmode -quit -projectPath . -executeMethod MummyEscape.EditorTools.BuildScript.AndroidDev
    ///   Unity -batchmode -quit -projectPath . -executeMethod MummyEscape.EditorTools.BuildScript.IOSXcode
    /// Android: installable APK (adb install -r Builds/Android/MummyEscape.apk).
    /// iOS: Xcode project only — it has to be compiled and signed on a Mac.
    /// </summary>
    public static class BuildScript
    {
        public const string AndroidPath = "Builds/Android/MummyEscape.apk";
        public const string IOSPath = "Builds/iOS";

        [MenuItem("Mummy Escape/Build/Android APK (test)", priority = 40)]
        public static void AndroidDev() => Android(development: true);

        [MenuItem("Mummy Escape/Build/Android APK (release, non signé)", priority = 41)]
        public static void AndroidRelease() => Android(development: false);

        [MenuItem("Mummy Escape/Build/iOS (projet Xcode)", priority = 42)]
        public static void IOSXcode()
        {
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.iOS, ScriptingImplementation.IL2CPP);
            PlayerSettings.iOS.targetOSVersionString = "15.0";
            Build(BuildTarget.iOS, IOSPath, BuildOptions.Development);
        }

        static void Android(bool development)
        {
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel25;
            EditorUserBuildSettings.buildAppBundle = false;
            Build(BuildTarget.Android, AndroidPath, development ? BuildOptions.Development : BuildOptions.None);
        }

        static void Build(BuildTarget target, string path, BuildOptions options)
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildPipeline.GetBuildTargetGroup(target), target))
                Fail($"{target} build support is not installed (unity install-modules -m android ios).");

            if (string.IsNullOrEmpty(PlayerSettings.bundleVersion) || PlayerSettings.bundleVersion == "1.0")
                PlayerSettings.bundleVersion = "0.1.0";
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ProjectSetup.ScenePath },
                locationPathName = path,
                target = target,
                options = options,
            });
            var s = report.summary;
            if (s.result != BuildResult.Succeeded)
                Fail($"{target} build {s.result}: {s.totalErrors} error(s). See the Editor log.");
            Debug.Log($"[Build] {target} OK -> {Path.GetFullPath(path)} ({s.totalSize / (1024f * 1024f):0.0} MB, {s.totalTime.TotalSeconds:0}s)");
        }

        static void Fail(string message)
        {
            Debug.LogError("[Build] " + message);
            if (Application.isBatchMode) EditorApplication.Exit(1);
            throw new BuildFailedException(message);
        }
    }
}
