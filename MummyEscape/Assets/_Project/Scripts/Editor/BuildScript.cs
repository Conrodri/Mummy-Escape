using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using MummyEscape.Core;
using UnityEngine;

namespace MummyEscape.EditorTools
{
    /// <summary>
    /// Mobile builds. Batch:
    ///   Unity -batchmode -quit -projectPath . -executeMethod MummyEscape.EditorTools.BuildScript.AndroidDev
    ///   Unity -batchmode -quit -projectPath . -executeMethod MummyEscape.EditorTools.BuildScript.GooglePlay
    ///   Unity -batchmode -quit -projectPath . -executeMethod MummyEscape.EditorTools.BuildScript.IOSXcode
    /// Android test: installable APK (adb install -r Builds/Android/MummyRush.apk).
    /// Google Play: signed App Bundle (Builds/Android/MummyRush.aab) with the upload key described in
    /// <see cref="Signing"/>; Google re-signs it with the app signing key (Play App Signing).
    /// iOS: Xcode project only — it has to be compiled and signed on a Mac.
    /// </summary>
    public static class BuildScript
    {
        public const string AndroidPath = "Builds/Android/MummyRush.apk";
        public const string BundlePath = "Builds/Android/MummyRush.aab";
        public const string IOSPath = "Builds/iOS";

        /// <summary>Google Play requires a recent target API (35 since Aug. 2025, 36 for new apps and updates from Aug. 2026).</summary>
        const int TargetApi = 36;

        [MenuItem("Mummy Rush/Build/Android APK (test)", priority = 40)]
        public static void AndroidDev() => Android(development: true, bundle: false);

        [MenuItem("Mummy Rush/Build/Android APK (release, non signé)", priority = 41)]
        public static void AndroidRelease() => Android(development: false, bundle: false);

        [MenuItem("Mummy Rush/Build/Google Play (AAB signé)", priority = 42)]
        public static void GooglePlay() => Android(development: false, bundle: true);

        [MenuItem("Mummy Rush/Build/iOS (projet Xcode)", priority = 43)]
        public static void IOSXcode()
        {
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.iOS, ScriptingImplementation.IL2CPP);
            PlayerSettings.iOS.targetOSVersionString = "15.0";
            PlayerSettings.iOS.buildNumber = VersionCode().ToString();
            Build(BuildTarget.iOS, IOSPath, BuildOptions.Development);
        }

        /// <summary>
        /// Store version code derived from the version name: 1.2.3 → 10203 (×100 + build). Bump
        /// PlayerSettings.bundleVersion (Project Settings › Player › Version) before each upload: the Play Console
        /// refuses a code it has already seen.
        /// </summary>
        public static int VersionCode()
        {
            var parts = PlayerSettings.bundleVersion.Split('.');
            int Part(int i) => i < parts.Length && int.TryParse(parts[i], out int v) ? Math.Max(0, Math.Min(99, v)) : 0;
            return ((Part(0) * 100 + Part(1)) * 100 + Part(2)) * 100 + 1;
        }

        static void Android(bool development, bool bundle)
        {
            var target = NamedBuildTarget.Android;
            PlayerSettings.SetApplicationIdentifier(target, "com.mummyrush.game");
            PlayerSettings.SetScriptingBackend(target, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel25;
            PlayerSettings.Android.targetSdkVersion = (AndroidSdkVersions)TargetApi;
            PlayerSettings.Android.bundleVersionCode = VersionCode();
            EditorUserBuildSettings.buildAppBundle = bundle;

            if (bundle)
            {
                var s = Signing.Load();
                if (s == null)
                    Fail($"Clé d'envoi introuvable. Lance « Mummy Rush › Build › Créer la clé d'envoi Google Play » ou définis les variables MUMMY_KEYSTORE*. ({Signing.PropertiesPath})");
                PlayerSettings.Android.useCustomKeystore = true;
                PlayerSettings.Android.keystoreName = s.Keystore;
                PlayerSettings.Android.keystorePass = s.StorePass;
                PlayerSettings.Android.keyaliasName = s.Alias;
                PlayerSettings.Android.keyaliasPass = s.KeyPass;
            }
            else PlayerSettings.Android.useCustomKeystore = false;

            try { Build(BuildTarget.Android, bundle ? BundlePath : AndroidPath, development ? BuildOptions.Development : BuildOptions.None); }
            finally
            {
                // Never leave the passwords in ProjectSettings.asset (public repository).
                PlayerSettings.Android.useCustomKeystore = false;
                PlayerSettings.Android.keystoreName = "";
                PlayerSettings.Android.keystorePass = "";
                PlayerSettings.Android.keyaliasName = "";
                PlayerSettings.Android.keyaliasPass = "";
                EditorUserBuildSettings.buildAppBundle = false;
                AssetDatabase.SaveAssets(); // the build saved them to disk: write the cleared values back
            }
        }

        static void Build(BuildTarget target, string path, BuildOptions options)
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildPipeline.GetBuildTargetGroup(target), target))
                Fail($"{target} build support is not installed (unity install-modules -m android ios).");

            // Each generator version has its own boards: a build without them sends every score into the void.
            var missing = LeaderboardExporter.Missing();
            if (missing.Count > 0)
                Fail($"{missing.Count} leaderboard configs missing for generator v{DifficultyTable.GeneratorVersion} (first: {missing[0]}). Run Mummy Rush > Online > Export leaderboard configs, deploy them, then build again.");

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
