using System.IO;
using MummyEscape.Core;
using MummyEscape.Online;
using UnityEditor;
using UnityEngine;

namespace MummyEscape.EditorTools
{
    /// <summary>
    /// Writes one Unity Gaming Services leaderboard config (.lb) per level: ascending score, keep best.
    /// Deploy them with the Deployment window (Services > Deployment) or the UGS CLI: ugs deploy Assets/_Project/Online
    /// </summary>
    public static class LeaderboardExporter
    {
        const string Folder = "Assets/_Project/Online/Leaderboards";

        [MenuItem("Mummy Escape/Online/Export leaderboard configs (.lb)")]
        public static void Export()
        {
            Directory.CreateDirectory(Folder);
            int count = 0;
            foreach (var id in DifficultyTable.AllLevels())
            {
                string lbId = OnlineServiceFactory.LeaderboardId(id);
                string json =
                    "{\n" +
                    "  \"$schema\": \"https://ugs-config-schemas.unity3d.com/v1/leaderboards.schema.json\",\n" +
                    "  \"SortOrder\": \"asc\",\n" +
                    "  \"UpdateType\": \"keepBest\",\n" +
                    $"  \"Name\": \"Mummy Escape {id}\"\n" +
                    "}\n";
                File.WriteAllText(Path.Combine(Folder, lbId + ".lb"), json);
                count++;
            }
            AssetDatabase.Refresh();
            Debug.Log($"[Leaderboards] {count} configs written to {Folder}. Deploy them via Services > Deployment.");
        }
    }
}
