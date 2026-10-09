using System.Collections.Generic;
using System.IO;
using MummyEscape.Core;
using MummyEscape.Online;
using UnityEditor;
using UnityEngine;

namespace MummyEscape.EditorTools
{
    /// <summary>
    /// Writes one Unity Gaming Services leaderboard config (.lb) per level: ascending score, keep best. The board ids carry
    /// the generator version, so every new version needs its own boards: the current one and the one before it (still
    /// played on phones that haven't updated). Deploy them with the Deployment window (Services > Deployment) or the
    /// UGS CLI: ugs deploy Assets/_Project/Online. Without them every score is refused and every ranking stays empty.
    /// </summary>
    public static class LeaderboardExporter
    {
        public const string Folder = "Assets/_Project/Online/Leaderboards";

        [MenuItem("Mummy Rush/Online/Export leaderboard configs (.lb)")]
        public static void Export()
        {
            // Older versions keep the configs already written: their level ids may differ from today's (v12 added the modes).
            int count = Export(DifficultyTable.GeneratorVersion);
            AssetDatabase.Refresh();
            Debug.Log($"[Leaderboards] {count} configs written to {Folder}. Deploy them via Services > Deployment.");
        }

        static int Export(int version)
        {
            Directory.CreateDirectory(Folder);
            int count = 0;
            foreach (var id in DifficultyTable.AllLevels())
            {
                string json =
                    "{\n" +
                    "  \"$schema\": \"https://ugs-config-schemas.unity3d.com/v1/leaderboards.schema.json\",\n" +
                    "  \"SortOrder\": \"asc\",\n" +
                    "  \"UpdateType\": \"keepBest\",\n" +
                    $"  \"Name\": \"Mummy Rush v{version} {id}\"\n" +
                    "}\n";
                File.WriteAllText(PathOf(id, version), json);
                count++;
            }
            return count;
        }

        /// <summary>Configs of the current generator version, one per level.</summary>
        public static List<string> Current()
        {
            var paths = new List<string>();
            foreach (var id in DifficultyTable.AllLevels()) paths.Add(PathOf(id, DifficultyTable.GeneratorVersion));
            return paths;
        }

        /// <summary>Levels of the current generator version without a config: the build refuses to go out without them.</summary>
        public static List<string> Missing() => Current().FindAll(p => !File.Exists(p));

        static string PathOf(LevelId id, int version) => Path.Combine(Folder, OnlineServiceFactory.LeaderboardId(id, version) + ".lb").Replace('\\', '/');
    }
}
