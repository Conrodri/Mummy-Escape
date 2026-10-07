using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MummyEscape.Pvp
{
    /// <summary>Une ligne de l'annuaire des codes amis : le nom public (Nom#1234) et le joueur qui le porte.</summary>
    [Serializable]
    public class NameEntry
    {
        public string PlayerId;
        public string Name;
    }

    [Serializable]
    public class FindPlayerResponse
    {
        public string PlayerId;
        public string Name;
        public string Error;   // "NOT_FOUND", "AMBIGUOUS", "SELF"
    }

    /// <summary>
    /// Annuaire des codes amis, pour retrouver un ami quand le service d'amis ne le trouve pas : le code tapé sans ses
    /// majuscules (lettoh#76668), ou un ancien code (un nouveau nom donne un nouveau #numéro) quand le nom seul ne désigne
    /// qu'un joueur. Rangé par nom en minuscules sur <see cref="NameShards"/> clés : une recherche ne lit qu'une clé.
    /// </summary>
    public sealed partial class PvpServer
    {
        public const string NameIndexKey = "names";
        public const int NameShards = 64;

        /// <summary>Le nom sans son #numéro, en minuscules : ce qui range et retrouve un joueur.</summary>
        public static string NameKey(string name)
        {
            name = (name ?? "").Trim();
            int hash = name.LastIndexOf('#');
            return (hash >= 0 ? name.Substring(0, hash) : name).ToLowerInvariant();
        }

        static string NameShard(string name) => NameIndexKey + "_" + Fnv(NameKey(name)) % NameShards;

        /// <summary>Le joueur annonce son nom public (à la connexion, après un changement de nom).</summary>
        public async Task<ReportResponse> RegisterNameAsync(string me, string name)
        {
            name = name?.Trim();
            if (string.IsNullOrEmpty(name) || name.Length > 40 || NameKey(name).Length == 0) return new ReportResponse { Error = "NAME" };
            string old = null;
            await Update(me, d =>
            {
                old = d.DirectoryName;
                d.DirectoryName = name;
            });
            // Renamed: the old name leaves its own key.
            if (old != null && old != name && NameShard(old) != NameShard(name))
                await Shared<List<NameEntry>>(IndexCollection, NameShard(old), list => { list?.RemoveAll(e => e == null || e.PlayerId == me); return list; });
            await Shared<List<NameEntry>>(IndexCollection, NameShard(name), list =>
            {
                list ??= new List<NameEntry>();
                list.RemoveAll(e => e == null || e.PlayerId == me);
                list.Add(new NameEntry { PlayerId = me, Name = name });
                return list;
            });
            return new ReportResponse { Ok = true };
        }

        /// <summary>
        /// Le joueur derrière un code ami : le code exact sans tenir compte des majuscules, sinon le seul joueur qui porte ce
        /// nom (quel que soit son #numéro).
        /// </summary>
        public async Task<FindPlayerResponse> FindPlayerAsync(string me, string code)
        {
            code = (code ?? "").Trim();
            if (NameKey(code).Length == 0) return new FindPlayerResponse { Error = "NOT_FOUND" };
            var list = await Shared<List<NameEntry>>(IndexCollection, NameShard(code)) ?? new List<NameEntry>();
            var sameName = list.Where(e => e != null && NameKey(e.Name) == NameKey(code)).ToList();
            var exact = sameName.FirstOrDefault(e => string.Equals(e.Name, code, StringComparison.OrdinalIgnoreCase));
            var found = exact ?? (sameName.Count == 1 ? sameName[0] : null);
            if (found == null) return new FindPlayerResponse { Error = sameName.Count > 1 ? "AMBIGUOUS" : "NOT_FOUND" };
            if (found.PlayerId == me) return new FindPlayerResponse { Error = "SELF" };
            return new FindPlayerResponse { PlayerId = found.PlayerId, Name = found.Name };
        }

        /// <summary>Le joueur quitte l'annuaire (suppression de ses données).</summary>
        async Task ForgetNameAsync(string me, string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            await Shared<List<NameEntry>>(IndexCollection, NameShard(name), list => { list?.RemoveAll(e => e == null || e.PlayerId == me); return list; });
        }
    }
}
