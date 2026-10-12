using System.IO;
using UnityEngine;

namespace MummyEscape.Services
{
    /// <summary>
    /// Editor only: a named test profile plays as another guest (its own Unity Authentication profile, so a new player
    /// id without the team's perks or purchases) with its own save and privacy files, leaving the developer's account
    /// untouched. Menu Mummy Rush › Test profile. Always off in a build.
    /// </summary>
    public static class TestProfile
    {
        const string Key = "mummy.test_profile"; // noloc

        public static string Name
        {
#if UNITY_EDITOR
            get => PlayerPrefs.GetString(Key, "");
#else
            get => "";
#endif
            set { PlayerPrefs.SetString(Key, value ?? ""); PlayerPrefs.Save(); }
        }

        public static bool Active => Name.Length > 0;

        /// <summary>"save.json" → "save.clean.json" under the profile "clean"; the name itself without a profile.</summary>
        public static string File(string fileName) =>
            Active ? Path.GetFileNameWithoutExtension(fileName) + "." + Name + Path.GetExtension(fileName) : fileName;
    }
}
