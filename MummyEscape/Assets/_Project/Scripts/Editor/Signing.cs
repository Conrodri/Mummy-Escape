using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace MummyEscape.EditorTools
{
    /// <summary>
    /// Google Play upload key. It lives OUTSIDE the repository (public): %USERPROFILE%/.mummyescape/ holds the
    /// keystore and signing.properties (paths and passwords). CI can use the environment variables MUMMY_KEYSTORE,
    /// MUMMY_KEYSTORE_PASS, MUMMY_KEY_ALIAS and MUMMY_KEY_PASS instead.
    /// With Play App Signing, Google keeps the real app signing key: a lost upload key can be reset through the
    /// Play Console (upload_certificate.pem helps), but back the folder up anyway.
    /// </summary>
    public sealed class Signing
    {
        public string Keystore, StorePass, Alias, KeyPass;

        public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".mummyescape");
        public static string PropertiesPath => Path.Combine(Folder, "signing.properties");
        static string KeystorePath => Path.Combine(Folder, "upload-keystore.jks");

        public static Signing Load()
        {
            string env = Environment.GetEnvironmentVariable("MUMMY_KEYSTORE");
            if (!string.IsNullOrEmpty(env))
            {
                return new Signing
                {
                    Keystore = env,
                    StorePass = Environment.GetEnvironmentVariable("MUMMY_KEYSTORE_PASS"),
                    Alias = Environment.GetEnvironmentVariable("MUMMY_KEY_ALIAS") ?? "upload",
                    KeyPass = Environment.GetEnvironmentVariable("MUMMY_KEY_PASS") ?? Environment.GetEnvironmentVariable("MUMMY_KEYSTORE_PASS"),
                };
            }
            if (!File.Exists(PropertiesPath)) return null;
            var p = new Dictionary<string, string>();
            foreach (var line in File.ReadAllLines(PropertiesPath))
            {
                int eq = line.IndexOf('=');
                if (eq > 0 && !line.TrimStart().StartsWith("#")) p[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
            p.TryGetValue("storeFile", out var file);
            if (string.IsNullOrEmpty(file) || !File.Exists(file)) return null;
            p.TryGetValue("storePassword", out var storePass);
            p.TryGetValue("keyAlias", out var alias);
            p.TryGetValue("keyPassword", out var keyPass);
            return new Signing { Keystore = file, StorePass = storePass, Alias = alias ?? "upload", KeyPass = keyPass ?? storePass };
        }

        [MenuItem("Mummy Rush/Build/Créer la clé d'envoi Google Play", priority = 60)]
        public static void CreateUploadKey() => Debug.Log("[Signing] " + Create());

        /// <summary>Creates the upload keystore once (never overwrites an existing one).</summary>
        public static string Create()
        {
            if (File.Exists(KeystorePath) || File.Exists(PropertiesPath))
                return "Une clé existe déjà dans " + Folder + " (rien n'a été modifié).";
            Directory.CreateDirectory(Folder);

            string keytool = Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines", "AndroidPlayer", "OpenJDK", "bin",
                Application.platform == RuntimePlatform.WindowsEditor ? "keytool.exe" : "keytool");
            if (!File.Exists(keytool)) return "keytool introuvable (module Android / OpenJDK de Unity) : " + keytool;

            string password = RandomPassword(28);
            // PKCS12: the key password equals the store password. Passwords go through the environment, not the command line.
            string error = Run(keytool, password,
                $"-genkeypair -keystore \"{KeystorePath}\" -storetype PKCS12 -alias upload -keyalg RSA -keysize 4096 -validity 10000 " +
                "-dname \"CN=Mummy Rush, O=Mummy Rush\" -storepass:env MUMMY_PASS -keypass:env MUMMY_PASS");
            if (error != null) return "Échec keytool : " + error;
            Run(keytool, password, $"-exportcert -rfc -keystore \"{KeystorePath}\" -alias upload -storepass:env MUMMY_PASS -file \"{Path.Combine(Folder, "upload_certificate.pem")}\"");

            File.WriteAllText(PropertiesPath,
                "# Clé d'envoi Google Play de Mummy Rush. NE PAS COMMITER, NE PAS PERDRE : sauvegarder ce dossier (gestionnaire de mots de passe, disque externe).\n" +
                $"storeFile={KeystorePath}\nstorePassword={password}\nkeyAlias=upload\nkeyPassword={password}\n");
            return "Clé d'envoi créée dans " + Folder + " — sauvegarde ce dossier.";
        }

        static string Run(string exe, string password, string args)
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true,
            };
            psi.EnvironmentVariables["MUMMY_PASS"] = password;
            using (var p = Process.Start(psi))
            {
                string err = p.StandardError.ReadToEnd() + p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                return p.ExitCode == 0 ? null : err;
            }
        }

        static string RandomPassword(int length)
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
            var bytes = new byte[length];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            var c = new char[length];
            for (int i = 0; i < length; i++) c[i] = chars[bytes[i] % chars.Length];
            return new string(c);
        }
    }
}
