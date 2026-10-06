// Mummy Rush PvP — l'énergie de combat, gardée par le serveur : un duel ou un match 2v2 lancé en coûte un point.
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MummyEscape.Pvp
{
    [System.Serializable]
    public class EnergyResponse
    {
        public EnergyMeter Energy;
        public string Error;
    }

    public sealed partial class PvpServer
    {
        bool IsHuman(string id) => !string.IsNullOrEmpty(id) && !id.StartsWith("bot_") && IsBot?.Invoke(id) != true;

        /// <summary>Le pass de la saison lève la limite.</summary>
        Task<bool> UnlimitedAsync(string playerId) => Task.FromResult(false);

        async Task<bool> HasEnergyAsync(string playerId) =>
            !IsHuman(playerId) || await UnlimitedAsync(playerId) || Energy.Left((await Update(playerId)).Energy, EnergyConfig.PvpMax, NowMs) > 0;

        /// <summary>
        /// Prend un point à chaque joueur humain du match ; "ENERGY" (et rien de pris) quand l'un d'eux n'en a plus.
        /// </summary>
        async Task<string> SpendEnergyAsync(IEnumerable<string> players)
        {
            var payers = new List<string>();
            foreach (var id in players.Where(IsHuman).Distinct())
                if (!await UnlimitedAsync(id)) payers.Add(id);
            foreach (var id in payers)
                if (Energy.Left((await Update(id)).Energy, EnergyConfig.PvpMax, NowMs) <= 0) return "ENERGY";
            foreach (var id in payers)
                await Update(id, d => Energy.Spend(d.Energy, EnergyConfig.PvpMax, NowMs));
            return null;
        }

        /// <summary>Après une pub : un point de plus (<see cref="EnergyConfig.AdRefillsPerDay"/> fois par jour).</summary>
        public async Task<EnergyResponse> RefillEnergyAsync(string me)
        {
            bool refilled = false;
            var data = await Update(me, d => refilled = Energy.Refill(d.Energy, EnergyConfig.PvpAdRefill, NowMs));
            return refilled ? new EnergyResponse { Energy = data.Energy } : new EnergyResponse { Energy = data.Energy, Error = "ADS_LIMIT" };
        }
    }
}
