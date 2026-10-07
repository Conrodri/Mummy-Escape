// Mummy Rush — droits du joueur sur ses données en ligne : tout exporter, tout effacer (suppression du compte).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MummyEscape.Pvp
{
    /// <summary>Tout ce que le serveur PvP garde au nom d'un joueur, pour l'export de ses données.</summary>
    [Serializable]
    public class PvpDataExport
    {
        public PlayerPvpData Pvp;
        public List<DuelRecord> DuelHistory = new List<DuelRecord>();
        public List<RelayMatch> RelayHistory = new List<RelayMatch>();
        public List<Duo> Duos = new List<Duo>();
        public Guild Guild;
        public ChatInbox Chat;
        /// <summary>Les messages encore sur le serveur que le joueur a écrits.</summary>
        public List<ChatMessage> Messages = new List<ChatMessage>();
        public List<SharedReplay> SharedReplays = new List<SharedReplay>();
    }

    [Serializable]
    public class PvpDataExportResponse
    {
        public PvpDataExport Data;
        public string Error;
    }

    public sealed partial class PvpServer
    {
        public async Task<PvpDataExportResponse> ExportPlayerDataAsync(string me)
        {
            var export = new PvpDataExport
            {
                Pvp = await Update(me),
                DuelHistory = await _store.UpdateHistoryAsync(me, null) ?? new List<DuelRecord>(),
                RelayHistory = (await Shared<RelayHistory>(RelayHistoryCollection, me))?.Matches ?? new List<RelayMatch>(),
                Chat = await Inbox(me),
            };
            foreach (var id in export.Pvp.Duos)
                if (await Shared<Duo>(DuosCollection, id) is Duo duo) export.Duos.Add(duo);
            if (export.Pvp.GuildId != null) export.Guild = await Shared<Guild>(GuildsCollection, export.Pvp.GuildId);
            foreach (var key in export.Chat?.Posted ?? new List<string>())
            {
                var room = await Shared<ChatChannel>(ChatCollection, key);
                if (room != null) export.Messages.AddRange(room.Messages.Where(m => m.From == me).Select(PublicCopy));
            }
            foreach (var id in export.Chat?.SharedReplays ?? new List<string>())
                if (await Shared<SharedReplay>(SharedReplaysCollection, id) is SharedReplay shared) export.SharedReplays.Add(shared);
            return new PvpDataExportResponse { Data = export };
        }

        /// <summary>
        /// Efface tout ce que le serveur garde au nom du joueur, avant la suppression de son compte : il quitte sa guilde et
        /// ses duos, ses messages et ses replays partagés disparaissent des canaux, puis ses données, son historique et son
        /// classement. Ce qui reste chez les autres (leurs propres duels contre lui) ne porte plus que l'identifiant d'un
        /// compte supprimé.
        /// </summary>
        public async Task<ReportResponse> DeletePlayerDataAsync(string me)
        {
            var data = await Update(me);
            await ForgetNameAsync(me, data.DirectoryName);
            if (data.GuildId is string guildId)
            {
                await LeaveGuildAsync(me);
                // Une guilde dont il était le dernier membre n'a plus personne à qui appartenir.
                var left = await Shared<Guild>(GuildsCollection, guildId);
                if (left != null && left.Members.Count == 0) await _store.DeleteSharedAsync(GuildsCollection, guildId);
            }
            foreach (var duoId in data.Duos.ToList())
            {
                var duo = await Shared<Duo>(DuosCollection, duoId);
                if (duo?.ActiveBattle != null) await Shared<Duo>(DuosCollection, duoId, d => { if (d != null) d.ActiveBattle = null; return d; });
                await LeaveDuoAsync(me, duoId);
                await _store.DeleteSharedAsync(DuosCollection, duoId);
            }

            var box = await Inbox(me) ?? new ChatInbox();
            var channels = new HashSet<string>(box.Posted);
            foreach (var c in box.Conversations)
                channels.Add(string.CompareOrdinal(me, c.Other) < 0 ? "dm_" + me + "_" + c.Other : "dm_" + c.Other + "_" + me);
            foreach (var key in channels)
                await Shared<ChatChannel>(ChatCollection, key, room =>
                {
                    if (room == null) return null;
                    room.Messages.RemoveAll(m => m.From == me);
                    return room;
                });
            foreach (var id in box.SharedReplays) await _store.DeleteSharedAsync(SharedReplaysCollection, id);
            foreach (var collection in new[] { ChatInboxCollection, ChatReportsCollection, RelayHistoryCollection, QuitCollection })
                await _store.DeleteSharedAsync(collection, me);

            await _store.DeletePlayerAsync(me);
            return new ReportResponse { Ok = true };
        }
    }
}
