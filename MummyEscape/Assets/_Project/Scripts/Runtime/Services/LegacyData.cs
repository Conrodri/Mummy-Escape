using System.IO;
using UnityEngine;

namespace MummyEscape.Services
{
    /// <summary>
    /// The game was renamed from "Mummy Escape" to "Mummy Rush". On desktop and in the editor the data folder follows the
    /// company and product names (…/Mummy Escape/Mummy Escape → …/Mummy Rush/Mummy Rush): the save, the privacy choices
    /// and the kept duels are copied over once, before anything reads them. On phones the package changed too, so there
    /// is nothing to carry over.
    /// </summary>
    public static class LegacyData
    {
        const string OldName = "Mummy Escape"; // noloc

        public static void Migrate()
        {
            try
            {
                string target = Application.persistentDataPath;
                if (File.Exists(Path.Combine(target, "save.json"))) return; // noloc
                var product = Directory.GetParent(target);
                var root = product?.Parent;
                if (root == null) return;
                string source = Path.Combine(root.FullName, OldName, OldName);
                if (!Directory.Exists(source) || Path.GetFullPath(source) == Path.GetFullPath(target)) return;
                Directory.CreateDirectory(target);
                foreach (var file in Directory.GetFiles(source, "*.json"))
                {
                    string dest = Path.Combine(target, Path.GetFileName(file));
                    if (!File.Exists(dest)) File.Copy(file, dest);
                }
                Debug.Log("[Save] Data carried over from " + source);
            }
            catch (System.Exception e) { Debug.LogWarning("[Save] Old data not carried over: " + e.Message); }
        }
    }
}
