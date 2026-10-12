using MummyEscape.Services;
using UnityEditor;

namespace MummyEscape.EditorTools
{
    /// <summary>Which player the editor plays as: the developer's own account, or a clean guest (see <see cref="TestProfile"/>).</summary>
    static class TestProfileMenu
    {
        const string Dev = "Mummy Rush/Test profile/Developer account", Clean = "Mummy Rush/Test profile/Clean guest";

        [MenuItem(Dev, priority = 60)] static void UseDev() => TestProfile.Name = "";
        [MenuItem(Dev, true)] static bool CheckDev() { Menu.SetChecked(Dev, !TestProfile.Active); return !EditorApplication.isPlaying; }
        [MenuItem(Clean, priority = 61)] static void UseClean() => TestProfile.Name = "clean";
        [MenuItem(Clean, true)] static bool CheckClean() { Menu.SetChecked(Clean, TestProfile.Active); return !EditorApplication.isPlaying; }
    }
}
