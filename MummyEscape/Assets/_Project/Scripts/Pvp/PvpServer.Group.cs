// Mummy Rush PvP — le groupe : le duo vu comme une équipe de deux. Chacun se dit prêt, le chef (le créateur) lance la
// campagne 2v2 ou le duel 2v2, et les deux téléphones suivent le lancement.
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MummyEscape.Pvp
{
    public partial class PvpServer
    {
        /// <summary>Le duo, à jour, avec la tenue du demandeur notée dedans (montrée à l'autre).</summary>
        public Task<DuoResponse> GetDuoAsync(string me, string duoId, PlayerLook look) =>
            MutateDuoAsync(me, duoId, d => { Wear(d, me, look); return null; });

        /// <summary>Prêt (ou plus) à partir.</summary>
        public Task<DuoResponse> SetGroupReadyAsync(string me, string duoId, bool ready, PlayerLook look) =>
            MutateDuoAsync(me, duoId, d =>
            {
                Wear(d, me, look);
                d.Ready ??= new List<string>();
                d.Ready.Remove(me);
                if (ready) d.Ready.Add(me);
                return null;
            });

        /// <summary>Le chef lance un mode pour le groupe, une fois l'autre prêt (un joueur simulé l'est toujours).</summary>
        public Task<DuoResponse> LaunchGroupAsync(string me, string duoId, string mode) =>
            MutateDuoAsync(me, duoId, d =>
            {
                if (!GroupLaunch.IsValid(mode)) return "MODE";
                if (d.Leader != me) return "LEADER";
                foreach (var id in d.Members)
                    if (id != me && !d.IsReady(id) && IsBot?.Invoke(id) != true) return "NOT_READY";
                d.Launch = mode;
                d.LaunchAtUnixMs = NowMs;
                d.Ready?.Clear();
                return null;
            });

        public const string MateQueueKey = "mates";
        /// <summary>Hors ligne seulement : un coéquipier simulé arrive après ce délai de recherche.</summary>
        const long DemoMateMs = 3_000;

        /// <summary>
        /// Cherche un coéquipier pour ce mode (<see cref="GroupLaunch"/>) ; le téléphone redemande toutes les quelques secondes
        /// tant qu'il cherche. Deux chercheurs du même mode forment un groupe, celui qui cherche depuis le plus longtemps en
        /// chef. Réponse : le groupe une fois formé (aussi par l'autre téléphone), sinon ni groupe ni erreur.
        /// </summary>
        public async Task<DuoResponse> SearchMateAsync(string me, string myName, string mode)
        {
            if (!GroupLaunch.IsValid(mode)) return new DuoResponse { Error = "MODE" };
            var mine = await Update(me);
            if (mine.Duos.Count > 0)
            {
                await CancelMateSearchAsync(me);
                return new DuoResponse { Duo = await Shared<Duo>(DuosCollection, mine.Duos[0]) };
            }
            long now = NowMs;
            MateSearch self = null, mate = null;
            await Shared<List<MateSearch>>(QueueCollection, MateQueueKey, queue =>
            {
                queue ??= new List<MateSearch>();
                queue.RemoveAll(s => now - s.SeenAtUnixMs > TeamConfig.MateSearchStaleMs);
                self = queue.Find(s => s.PlayerId == me);
                if (self != null && self.DuoId == null && self.Mode != mode) { queue.Remove(self); self = null; } // another mode: back of the line
                if (self == null) queue.Add(self = new MateSearch { PlayerId = me, Mode = mode, SinceUnixMs = now });
                self.Name = myName ?? "";
                self.SeenAtUnixMs = now;
                mate = null;
                if (self.DuoId != null) return queue; // matched by the other phone, which is forming the group
                mate = queue.Where(s => s != self && s.DuoId == null && s.Mode == mode).OrderBy(s => s.SinceUnixMs).FirstOrDefault();
                if (mate == null && BotName != null && now - self.SinceUnixMs >= DemoMateMs)
                {
                    string bot = "mate_" + NewId(); // noloc
                    mate = new MateSearch { PlayerId = bot, Name = BotName(bot), Mode = mode, SinceUnixMs = now };
                }
                // The duo's id is fixed here, under the queue's lock: neither can be matched twice.
                if (mate != null) self.DuoId = mate.DuoId = NewId();
                return queue;
            });
            if (mate == null) return new DuoResponse();
            bool selfLeads = self.SinceUnixMs <= mate.SinceUnixMs;
            var lead = selfLeads ? self : mate;
            var other = selfLeads ? mate : self;
            var duo = await CreateDuoAsync(new DuoInvite { FromId = lead.PlayerId, FromName = lead.Name }, other.PlayerId, other.Name, self.DuoId, mode);
            await CancelMateSearchAsync(me);
            return new DuoResponse { Duo = duo };
        }

        /// <summary>Le joueur arrête de chercher un coéquipier.</summary>
        public async Task<TeamActionResponse> CancelMateSearchAsync(string me)
        {
            await Shared<List<MateSearch>>(QueueCollection, MateQueueKey, queue => { queue?.RemoveAll(s => s.PlayerId == me); return queue; });
            return new TeamActionResponse { Ok = true };
        }

        async Task<DuoResponse> MutateDuoAsync(string me, string duoId, System.Func<Duo, string> change)
        {
            string error = null;
            var duo = await Shared<Duo>(DuosCollection, duoId, d =>
            {
                error = d == null || !d.Members.Contains(me) ? "UNKNOWN" : change(d);
                return d;
            });
            return error != null ? new DuoResponse { Duo = duo, Error = error } : new DuoResponse { Duo = duo };
        }

        static void Wear(Duo d, string me, PlayerLook look)
        {
            if (look == null) return;
            d.Looks ??= new List<PlayerLook>();
            while (d.Looks.Count < d.Members.Count) d.Looks.Add(null);
            int i = d.Members.IndexOf(me);
            if (i >= 0) d.Looks[i] = Developers.Restrict(PlayerLook.Sanitize(look), me);
        }
    }
}
