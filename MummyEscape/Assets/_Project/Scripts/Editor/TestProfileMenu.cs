using System;
using System.IO;
using MummyEscape.Services;
using UnityEditor;
using UnityEngine;

namespace MummyEscape.EditorTools
{
    /// <summary>Which player the editor plays as: the developer's own account, or a clean guest (see <see cref="TestProfile"/>).</summary>
    static class TestProfileMenu
    {
        const string Dev = "Mummy Rush/Test profile/Developer account", Clean = "Mummy Rush/Test profile/Clean guest",
                     Fresh = "Mummy Rush/Test profile/New clean phone (reset)";

        // The device-wide preferences a fresh install starts without (settings, prompts already shown, offline demo, chat).
        static readonly string[] DevicePrefs =
        {
            "music", "sfx", "shake", "fx", "haptics", "preview", "lang", "pvp_bots", "controls", "home_theme", "pad_right",
            "battery", "notif_energy", "notif_events", "notif_quests", "notif_asked", "rating_asked", "pvp_demo", "chat_enabled",
        };

        [MenuItem(Dev, priority = 60)] static void UseDev() => TestProfile.Name = "";
        [MenuItem(Dev, true)] static bool CheckDev() { Menu.SetChecked(Dev, !TestProfile.Active); return !EditorApplication.isPlaying; }
        [MenuItem(Clean, priority = 61)] static void UseClean() => TestProfile.Name = "clean";
        [MenuItem(Clean, true)] static bool CheckClean() { Menu.SetChecked(Clean, TestProfile.Active); return !EditorApplication.isPlaying; }

        /// <summary>
        /// A phone on its first launch: the clean guest's files are deleted and it takes a new profile name, so a new UGS
        /// session and player id with nothing on the server; the device preferences go back to their defaults.
        /// </summary>
        [MenuItem(Fresh, priority = 62)]
        static void UseFresh()
        {
            if (TestProfile.Active)
                foreach (var file in new[] { "save.json", "privacy.json", "duel_replays.json", "relay_replays.json" })
                    File.Delete(Path.Combine(Application.persistentDataPath, TestProfile.File(file)));
            foreach (var key in DevicePrefs) PlayerPrefs.DeleteKey(key);
            TestProfile.Name = "clean" + DateTime.UtcNow.ToString("yyMMddHHmmss"); // noloc: UGS profile names are letters and digits
            Debug.Log("[TestProfile] new clean phone: " + TestProfile.Name);
        }
        [MenuItem(Fresh, true)] static bool CheckFresh() => !EditorApplication.isPlaying;
    }
}
