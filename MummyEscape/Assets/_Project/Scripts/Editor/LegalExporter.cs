using System.IO;
using System.Text;
using MummyEscape.UI.Legal;
using UnityEditor;
using UnityEngine;

namespace MummyEscape.EditorTools
{
    /// <summary>
    /// Writes the in-game privacy policy and terms to docs/*.md at the repository root. Published with GitHub Pages
    /// (Settings › Pages › branch main, folder /docs), they give the public URLs the Play Store and the App Store require.
    /// </summary>
    public static class LegalExporter
    {
        [MenuItem("Mummy Escape/Legal/Export privacy policy and terms to docs/")]
        public static void Export()
        {
            string docs = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "docs"));
            Directory.CreateDirectory(docs);
            File.WriteAllText(Path.Combine(docs, "confidentialite.md"), ToMarkdown("Mummy Escape — Politique de confidentialité", LegalTexts.Privacy), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(docs, "conditions.md"), ToMarkdown("Mummy Escape — Conditions d'utilisation", LegalTexts.Terms), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(docs, "suppression-compte.md"), ToMarkdown("Mummy Escape — Supprimer ton compte et tes données", LegalTexts.AccountDeletion), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(docs, "index.md"), ToMarkdown("Mummy Escape", LegalTexts.Home), new UTF8Encoding(false));
            if (LegalTexts.HasPlaceholders)
                Debug.LogWarning("[Legal] LegalTexts still holds placeholders ([NOM DE L'ÉDITEUR]…): fill them before publishing.");
            Debug.Log("[Legal] Exported to " + docs);
        }

        static string ToMarkdown(string title, string[] lines)
        {
            var sb = new StringBuilder("# " + title + "\n\n");
            foreach (var line in lines)
            {
                if (line.StartsWith("# ")) sb.Append("#").Append(line).Append("\n\n");
                else sb.Append(line.Replace("\n", "  \n")).Append("\n\n");
            }
            return sb.ToString();
        }
    }
}
