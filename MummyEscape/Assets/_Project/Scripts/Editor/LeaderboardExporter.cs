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
                Write(PathOf(id, version), $"Mummy Rush v{version} {id}");
                count++;
            }
            // The endless ladders' boards: their tombs are stored for good, so their ids carry no generator version.
            foreach (var mode in DifficultyExt.All)
            {
                Write(EndlessPath(mode), $"Mummy Rush endless {mode.Letter()}");
                count++;
            }
            // The tower draws its tombs with the generator: its board carries the version.
            Write(TowerPath, $"Mummy Rush tower v{version}");
            count++;
            return count;
        }

        /// <summary>Ascending, keep best: lower scores are better (levels: moves over par then time; endless: see Endless.EncodeScore).</summary>
        static void Write(string path, string name)
        {
            string json =
                "{\n" +
                "  \"$schema\": \"https://ugs-config-schemas.unity3d.com/v1/leaderboards.schema.json\",\n" +
                "  \"SortOrder\": \"asc\",\n" +
                "  \"UpdateType\": \"keepBest\",\n" +
                $"  \"Name\": \"{name}\"\n" +
                "}\n";
            File.WriteAllText(path, json);
        }

        /// <summary>Configs of the current generator version, one per level, the endless boards and the tower's.</summary>
        public static List<string> Current()
        {
            var paths = new List<string>();
            foreach (var id in DifficultyTable.AllLevels()) paths.Add(PathOf(id, DifficultyTable.GeneratorVersion));
            foreach (var mode in DifficultyExt.All) paths.Add(EndlessPath(mode));
            paths.Add(TowerPath);
            return paths;
        }

        static string EndlessPath(Difficulty mode) => Path.Combine(Folder, Endless.BoardId(mode) + ".lb").Replace('\\', '/');

        static string TowerPath => Path.Combine(Folder, Tower.BoardId + ".lb").Replace('\\', '/');

        /// <summary>Levels of the current generator version without a config: the build refuses to go out without them.</summary>
        public static List<string> Missing() => Current().FindAll(p => !File.Exists(p));

        static string PathOf(LevelId id, int version) => Path.Combine(Folder, OnlineServiceFactory.LeaderboardId(id, version) + ".lb").Replace('\\', '/');
    }
}
