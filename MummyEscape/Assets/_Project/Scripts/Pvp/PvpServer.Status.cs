// Mummy Rush PvP — état du service : un message de maintenance et la pause des duels, posés par l'équipe, lus par chaque jeu.
using System.Threading.Tasks;

namespace MummyEscape.Pvp
{
    public partial class PvpServer
    {
        const string SystemCollection = "system";
        const string StatusKey = "status";

        /// <summary>L'état courant (vide tant que l'équipe n'a rien posé).</summary>
        public async Task<ServiceStatus> GetStatusAsync() =>
            await Shared<ServiceStatus>(SystemCollection, StatusKey) ?? new ServiceStatus();

        /// <summary>Pose l'état (équipe seulement) : un message vide et PvpOff à faux remettent tout en marche.</summary>
        public async Task<ServiceStatus> SetStatusAsync(string me, ServiceStatus status)
        {
            if (!Developers.Is(me)) return new ServiceStatus { Error = "FORBIDDEN" };
            var next = status ?? new ServiceStatus();
            next.Error = null;
            next.UpdatedAtUnixMs = NowMs;
            return await Shared<ServiceStatus>(SystemCollection, StatusKey, _ => next);
        }
    }
}
