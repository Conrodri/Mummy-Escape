// Mummy Rush PvP — le groupe : le duo vu comme une équipe de deux. Chacun se dit prêt, le chef (le créateur) lance la
// campagne 2v2 ou le duel 2v2, et les deux téléphones suivent le lancement.
using System.Collections.Generic;
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
