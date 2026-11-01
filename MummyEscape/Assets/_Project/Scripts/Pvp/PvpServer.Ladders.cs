using System;
using System.Threading.Tasks;

namespace MummyEscape.Pvp
{
    /// <summary>The four ladders (<see cref="Ladder"/>), served from what this server wrote: world, country, friends.</summary>
    public sealed partial class PvpServer
    {
        /// <summary>
        /// A ladder page for <paramref name="me"/>: the top <see cref="Ladders.Size"/> rows of the scope and the viewer's row.
        /// <paramref name="seasonsAgo"/>: Duel only, 0 = this month, 1 = last month.
        /// </summary>
        public Task<LadderPage> GetLadderAsync(string me, Ladder ladder, LadderScope scope, int seasonsAgo) =>
            throw new NotImplementedException(); // contract stub: implemented by the server work

        /// <summary>The country the player chose (<see cref="Ladders.IsCountryCode"/>, or "" for none): it labels and filters every ladder row.</summary>
        public Task<ReportResponse> SetCountryAsync(string me, string country) =>
            throw new NotImplementedException(); // contract stub: implemented by the server work
    }
}
