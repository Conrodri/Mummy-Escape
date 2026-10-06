using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MummyEscape.Pvp
{
    /// <summary>
    /// Index des duos et des guildes réparti sur <see cref="IndexShards"/> clés (selon un hachage stable de l'id) : deux
    /// écritures sur des équipes différentes ne se disputent presque jamais la même clé, et aucune clé ne grossit sans fin.
    /// L'ancienne clé unique (avant le découpage) est encore lue ; une entrée d'une part la remplace.
    /// </summary>
    public sealed partial class PvpServer
    {
        public const int IndexShards = 16;

        /// <summary>La part de l'index qui range cet id.</summary>
        public static string IndexShard(string baseKey, string id) => baseKey + "_" + Fnv(id) % IndexShards;

        /// <summary>L'ancienne clé unique puis les parts, dans l'ordre où <see cref="MergeIndex{T}"/> les fusionne.</summary>
        public static IEnumerable<string> IndexKeys(string baseKey) =>
            new[] { baseKey }.Concat(Enumerable.Range(0, IndexShards).Select(i => baseKey + "_" + i));

        /// <summary>Une entrée par id, la plus récente (celle des parts) l'emportant sur l'ancienne clé.</summary>
        public static List<T> MergeIndex<T>(IEnumerable<List<T>> lists, Func<T, string> id)
        {
            var byId = new Dictionary<string, T>();
            foreach (var list in lists)
                if (list != null)
                    foreach (var e in list)
                        if (e != null) byId[id(e)] = e;
            return byId.Values.ToList();
        }

        // FNV-1a : string.GetHashCode change d'un processus à l'autre.
        static uint Fnv(string s)
        {
            uint h = 2166136261;
            foreach (char c in s ?? "") { h ^= c; h *= 16777619; }
            return h;
        }

        async Task<List<T>> ReadIndexAsync<T>(string baseKey, Func<T, string> id) where T : class
        {
            var lists = await Task.WhenAll(IndexKeys(baseKey).Select(k => Shared<List<T>>(IndexCollection, k)));
            return MergeIndex(lists, id);
        }

        /// <summary>Range (ou retire, si <paramref name="entry"/> est null) l'entrée de cet id dans sa part.</summary>
        async Task PutIndexAsync<T>(string baseKey, string entryId, T entry, Func<T, string> id) where T : class
        {
            await Shared<List<T>>(IndexCollection, IndexShard(baseKey, entryId), list =>
            {
                list ??= new List<T>();
                list.RemoveAll(e => e == null || id(e) == entryId);
                if (entry != null) list.Add(entry);
                return list;
            });
            // Retirée : elle ne doit pas réapparaître depuis l'ancienne clé.
            if (entry == null)
                await Shared<List<T>>(IndexCollection, baseKey, list =>
                {
                    list?.RemoveAll(e => e == null || id(e) == entryId);
                    return list;
                });
        }

        Task<List<DuoSummary>> ReadDuoIndexAsync() => ReadIndexAsync<DuoSummary>(DuoIndexKey, s => s.Id);
        Task<List<GuildSummary>> ReadGuildIndexAsync() => ReadIndexAsync<GuildSummary>(GuildIndexKey, s => s.Id);
    }
}
