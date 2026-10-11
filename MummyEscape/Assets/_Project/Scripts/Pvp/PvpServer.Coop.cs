// Mummy Rush — le serveur de la campagne 2v2 : il rejoue chaque passage, garde la progression du duo (meilleur passage
// de chaque tombeau), les étoiles 2v2 de chaque joueur, et classe les duos.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MummyEscape.Core;

namespace MummyEscape.Pvp
{
    public sealed partial class PvpServer
    {
        /// <summary>
        /// Un passage du duo, envoyé par chacun des deux joueurs (le même, l'hôte de la partie ayant ordonné les actions) :
        /// rejoué, il met à jour le meilleur passage du duo et les étoiles de ce joueur. La récompense (scarabées, sur
        /// l'appareil) n'est due qu'au premier passage du joueur (<see cref="CoopClearResponse.FirstClear"/>).
        /// </summary>
        public async Task<CoopClearResponse> SubmitCoopAsync(string me, string duoId, int tomb, List<CoopRunInput> inputs, int timeMs)
        {
            var duo = await Shared<Duo>(DuosCollection, duoId);
            var t = CoopCampaign.Get(tomb);
            if (duo == null || !duo.Members.Contains(me) || t == null) return new CoopClearResponse { Error = "UNKNOWN" };
            if (!CoopLogic.IsOpen(duo, tomb)) return new CoopClearResponse { Error = "LOCKED" };
            var session = CoopLogic.Replay(t, inputs, out int minMs);
            // Faster than the game animates: a modified game.
            if (session == null || timeMs + CoopConfig.TimeSlackMs < minMs) return new CoopClearResponse { Error = "INVALID" };
            var result = t.Result(true, session.Moves, timeMs);
            int stars = result.Stars;

            duo = await Shared<Duo>(DuosCollection, duoId, d =>
            {
                d.Coop ??= new List<CoopRecord>();
                var best = d.Coop.Find(r => r.Tomb == tomb);
                bool first = best == null;
                if (first) d.Coop.Add(best = new CoopRecord { Tomb = tomb });
                if (first || LevelResult.CompareRuns(result.OverPar, timeMs, Math.Max(0, best.Moves - t.Par), best.TimeMs) < 0)
                {
                    best.Moves = session.Moves;
                    best.TimeMs = timeMs;
                }
                best.Stars = Math.Max(best.Stars, stars);
                return d;
            });
            await IndexDuoAsync(duo);

            var response = new CoopClearResponse { Stars = stars, Duo = duo };
            await Update(me, d =>
            {
                d.CoopStars ??= new List<CoopStar>();
                var mine = d.CoopStars.Find(s => s.Tomb == tomb);
                response.FirstClear = mine == null;
                if (mine == null) d.CoopStars.Add(mine = new CoopStar { Tomb = tomb });
                response.NewStars = Math.Max(0, stars - mine.Stars);
                mine.Stars = Math.Max(mine.Stars, stars);
                response.TotalStars = CoopLogic.TotalStars(d.CoopStars);
                var w = WalletOf(d);
                if (response.FirstClear && w.ScarabsOpen) w.Scarabs += CoopConfig.FirstClearScarabs;
                response.Wallet = w;
            });
            return response;
        }

        /// <summary>Le classement de la campagne 2v2 : les mêmes duos que le 2v2, par étoiles puis par temps cumulé.</summary>
        public async Task<DuoBoardResponse> GetCoopBoardAsync(int limit)
        {
            var list = await ReadDuoIndexAsync();
            return new DuoBoardResponse
            {
                Rows = list.Where(s => s.CoopCleared > 0)
                           .OrderByDescending(s => s.CoopStars).ThenByDescending(s => s.CoopCleared).ThenBy(s => s.CoopTimeMs)
                           .ThenBy(s => s.Id, StringComparer.Ordinal).Take(limit).ToList(),
            };
        }
    }
}
