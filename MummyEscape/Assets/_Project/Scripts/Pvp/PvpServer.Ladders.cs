using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MummyEscape.Core;

namespace MummyEscape.Pvp
{
    /// <summary>A board's top entries as ladder rows (in ladder order), kept a few minutes for every page that reads them.</summary>
    [Serializable]
    public class LadderPool
    {
        public long BuiltAtUnixMs;
        /// <summary>Not every entry was read: a player outside these rows ranks somewhere below all of them.</summary>
        public bool Full;
        public List<LadderRow> Rows = new List<LadderRow>();
    }

    /// <summary>The four ladders (<see cref="Ladder"/>), served from what this server wrote: world, country, friends.</summary>
    public sealed partial class PvpServer
    {
        /// <summary>Cloud Save (private custom items): the cached pools, one key per board (and month, and duel country).</summary>
        public const string LadderCollection = "pvp_ladders";

        /// <summary>
        /// A ladder page for <paramref name="me"/>: the top <see cref="Ladders.Size"/> rows of the scope and the viewer's row.
        /// <paramref name="seasonsAgo"/>: Duel only, 0 = this month, 1 = last month.
        /// World: the board's top (the duel's verified against the protected data, <see cref="GetBoardAsync"/>), the viewer's
        /// rank from the service beyond it. Country: the top <see cref="Ladders.PoolSize"/> of the board, filtered. Friends: the
        /// contacts' own data, exact. The viewer's row is always read fresh.
        /// </summary>
        public async Task<LadderPage> GetLadderAsync(string me, Ladder ladder, LadderScope scope, int seasonsAgo)
        {
            var page = new LadderPage { Ladder = ladder, Scope = scope, BuiltAtUnixMs = NowMs };
            if (!Enum.IsDefined(typeof(Ladder), ladder) || !Enum.IsDefined(typeof(LadderScope), scope)) { page.Error = "UNKNOWN_LADDER"; return page; }
            seasonsAgo = ladder == Ladder.Duel ? Math.Max(0, Math.Min(1, seasonsAgo)) : 0;
            string season = ladder == Ladder.Duel ? SeasonAgo(seasonsAgo) : "";
            page.Season = season;
            (await _store.ReadPlayersAsync(new[] { me })).TryGetValue(me, out var mine);
            string country = mine?.Country ?? "";
            if (scope == LadderScope.Country)
            {
                if (!Ladders.IsCountryCode(country)) { page.Error = "NO_COUNTRY"; return page; }
                page.Country = country;
            }

            if (scope == LadderScope.Friends)
            {
                var ids = new HashSet<string>((await Inbox(me))?.Contacts ?? new List<string>()) { me };
                if (ladder == Ladder.Duo) await DuoPageAsync(page, me, r => ids.Contains(r.PlayerId) || ids.Contains(r.MateId));
                else
                {
                    var data = await _store.ReadPlayersAsync(ids.ToList());
                    Complete(page, data.Select(kv => RowOf(kv.Key, kv.Value, ladder, season)).Where(r => r != null).ToList(), r => r.PlayerId == me);
                }
                return page;
            }
            if (ladder == Ladder.Duo)
            {
                await DuoPageAsync(page, me, r => scope == LadderScope.World || r.Country == country || r.MateCountry == country);
                return page;
            }
            if (ladder == Ladder.Duel && scope == LadderScope.World)
            {
                var board = await GetBoardAsync(me, seasonsAgo, Ladders.Size);
                page.Rows = board.Rows.Select(DuelRow).ToList();
                page.Me = board.Me == null ? null : DuelRow(board.Me);
                return page;
            }

            var pool = await PoolAsync(ladder, seasonsAgo);
            var myRow = RowOf(me, mine, ladder, season);
            if (myRow != null && pool.Rows.Find(r => r.PlayerId == me) is LadderRow listed) myRow.PlayerName = listed.PlayerName;
            if (ladder == Ladder.Duel)
            {
                var verified = await DuelCountryAsync(pool, season, country);
                Placed(page, verified.Rows, me, myRow, verified.Full, 0);
                return page;
            }
            if (scope == LadderScope.World)
            {
                var entry = await _store.ReadBoardEntryAsync(BoardOf(ladder), me, 0);
                if (myRow != null && entry != null) myRow.PlayerName = ChatFilter.Name(entry.PlayerName);
                Placed(page, pool.Rows, me, myRow, pool.Full, entry?.Rank ?? 0);
            }
            else Placed(page, pool.Rows.Where(r => r.Country == country), me, myRow, pool.Full, 0);
            return page;
        }

        /// <summary>
        /// The country the player chose (<see cref="Ladders.IsCountryCode"/>, or "" for none): it labels and filters every ladder
        /// row. The rows already written are rewritten with it, so the country pages follow at the next pool (a few minutes).
        /// Last month's archived duel board keeps the old label (and drops the player from its country pages, see
        /// <see cref="DuelCountryAsync"/>).
        /// </summary>
        public async Task<ReportResponse> SetCountryAsync(string me, string country)
        {
            country ??= "";
            if (country.Length > 0 && !Ladders.IsCountryCode(country)) return new ReportResponse { Error = "COUNTRY" };
            string before = null;
            var d = await Update(me, x =>
            {
                before = x.Country ?? "";
                x.Country = country;
            });
            if (before == country) return new ReportResponse { Ok = true };
            if (d.Ranked && d.Season == SeasonAgo(0)) await _store.SubmitEloAsync(me, d.Elo, country);
            if ((d.TowerBest?.BestFloors ?? 0) > 0) await _store.SubmitScoreAsync(Ladders.TowerBoard, me, d.TowerBest.Score, country);
            if (Ladders.EndlessPoints(d.EndlessCleared) > 0) await _store.SubmitScoreAsync(Ladders.EndlessBoard, me, EndlessScore(d), country);
            foreach (var id in d.Duos)
                if (await Shared<Duo>(DuosCollection, id) is Duo duo) await IndexDuoAsync(duo);
            return new ReportResponse { Ok = true };
        }

        // ------------------------------------------------------------------ rows

        static string BoardOf(Ladder ladder) =>
            ladder == Ladder.Duel ? Ladders.DuelBoard : ladder == Ladder.Tower ? Ladders.TowerBoard : Ladders.EndlessBoard;

        /// <summary>Ladder order: the higher value first, then the shorter time (an unknown time last), then the ids (ties never move).</summary>
        static int Order(LadderRow a, LadderRow b)
        {
            int c = b.Value.CompareTo(a.Value);
            if (c == 0) c = TieMs(a.TimeMs).CompareTo(TieMs(b.TimeMs));
            if (c == 0) c = string.CompareOrdinal(a.PlayerId, b.PlayerId);
            return c != 0 ? c : string.CompareOrdinal(a.MateId, b.MateId);
        }

        static int TieMs(int timeMs) => timeMs > 0 ? timeMs : int.MaxValue;

        static int EndlessTimeOf(PlayerPvpData d) => (int)Math.Min(int.MaxValue, d.EndlessTimeMs?.Sum(t => (long)Math.Max(0, t)) ?? 0);

        /// <summary>The endless board score of a player, from what was replayed here.</summary>
        static long EndlessScore(PlayerPvpData d) => Ladders.EncodeEndless(Ladders.EndlessPoints(d.EndlessCleared), EndlessTimeOf(d));

        /// <summary>A player's row computed from the protected data (exact), null without a score on this ladder.</summary>
        static LadderRow RowOf(string id, PlayerPvpData d, Ladder ladder, string season)
        {
            if (d == null) return null;
            var row = new LadderRow { PlayerId = id, PlayerName = ChatFilter.Name(d.DirectoryName), Country = d.Country ?? "" };
            switch (ladder)
            {
                case Ladder.Duel:
                    if (!(TrueElo(d, season) is int elo)) return null;
                    var record = WithRecord(new PvpBoardRow(), d, season);
                    row.Value = elo;
                    row.Wins = record.Wins;
                    row.Losses = record.Losses;
                    row.Draws = record.Draws;
                    return row;
                case Ladder.Tower:
                    if ((d.TowerBest?.BestFloors ?? 0) <= 0) return null;
                    row.Value = d.TowerBest.BestFloors;
                    row.TimeMs = d.TowerBest.BestTimeMs;
                    return row;
                case Ladder.Endless:
                    row.Value = Ladders.EndlessPoints(d.EndlessCleared);
                    row.TimeMs = EndlessTimeOf(d);
                    return row.Value > 0 ? row : null;
                default:
                    return null;
            }
        }

        /// <summary>A board entry as a row (its name cleaned: the service keeps the name the phone chose).</summary>
        static LadderRow RowOf(Ladder ladder, BoardEntry e)
        {
            var row = new LadderRow { PlayerId = e.PlayerId, PlayerName = ChatFilter.Name(e.PlayerName), Country = e.Country ?? "" };
            if (ladder == Ladder.Duel) row.Value = (int)e.Score;
            else if (ladder == Ladder.Tower) (row.Value, row.TimeMs) = Tower.DecodeScore(e.Score);
            else (row.Value, row.TimeMs) = Ladders.DecodeEndless(e.Score);
            return row;
        }

        static LadderRow DuelRow(PvpBoardRow r) => new LadderRow
        {
            Rank = r.Rank, PlayerId = r.PlayerId, PlayerName = r.PlayerName, Country = r.Country ?? "", Value = r.Elo,
            Wins = r.Wins, Losses = r.Losses, Draws = r.Draws, IsMe = r.IsMe,
        };

        static string At(List<string> list, int i) => list != null && i < list.Count ? list[i] : null;

        static LadderRow DuoRow(DuoSummary s) => new LadderRow
        {
            PlayerId = s.Members[0], MateId = s.Members[1],
            // Summaries written before the names were kept: the duo's name.
            PlayerName = ChatFilter.Name(At(s.Names, 0) ?? s.Name), MateName = At(s.Names, 1) is string mate ? ChatFilter.Name(mate) : "",
            Country = At(s.Countries, 0) ?? "", MateCountry = At(s.Countries, 1) ?? "",
            Value = s.Elo, Wins = s.Wins, Losses = s.Losses, Draws = s.Draws,
        };

        // ------------------------------------------------------------------ pages

        /// <summary>The whole scope is known: ranked in ladder order, the viewer's best row as theirs.</summary>
        static void Complete(LadderPage page, List<LadderRow> rows, Func<LadderRow, bool> isMe)
        {
            rows.Sort(Order);
            for (int i = 0; i < rows.Count; i++)
            {
                rows[i].Rank = i + 1;
                rows[i].IsMe = isMe(rows[i]);
            }
            page.Me = rows.FirstOrDefault(r => r.IsMe);
            page.Rows = rows.Take(Ladders.Size).ToList();
        }

        /// <summary>
        /// The scope's rows (in ladder order, maybe only the top of it: <paramref name="truncated"/>) with the viewer's fresh row
        /// placed among them. Behind all of a truncated list, the viewer's rank is <paramref name="serviceRank"/> (0: unknown).
        /// </summary>
        static void Placed(LadderPage page, IEnumerable<LadderRow> scope, string me, LadderRow mine, bool truncated, int serviceRank)
        {
            var rows = scope.Where(r => r.PlayerId != me).Select(r => r.Copy()).ToList();
            if (mine != null)
            {
                mine.IsMe = true;
                int ahead = rows.Count(r => Order(r, mine) < 0);
                mine.Rank = ahead < rows.Count || !truncated ? ahead + 1 : serviceRank > 0 ? Math.Max(serviceRank, rows.Count + 1) : 0;
                if (mine.Rank > 0 && mine.Rank <= rows.Count + 1) rows.Insert(mine.Rank - 1, mine);
            }
            if (rows.Count > Ladders.Size) rows.RemoveRange(Ladders.Size, rows.Count - Ladders.Size);
            for (int i = 0; i < rows.Count; i++) rows[i].Rank = i + 1;
            page.Rows = rows;
            page.Me = mine;
        }

        /// <summary>The 2v2 ladder: every duo that played, from the duo index (complete: the ranks are exact).</summary>
        async Task DuoPageAsync(LadderPage page, string me, Func<LadderRow, bool> inScope)
        {
            var rows = (await ReadDuoIndexAsync())
                .Where(s => s.Members != null && s.Members.Count == 2 && s.Wins + s.Losses + s.Draws > 0)
                .Select(DuoRow).Where(inScope).ToList();
            Complete(page, rows, r => r.PlayerId == me || r.MateId == me);
        }

        /// <summary>
        /// The top <see cref="Ladders.PoolSize"/> entries of a UGS board, as the service ranks them (written by this server only),
        /// shared by every page a few minutes.
        /// </summary>
        async Task<LadderPool> PoolAsync(Ladder ladder, int seasonsAgo)
        {
            string key = ladder == Ladder.Duel ? "Duel_" + SeasonAgo(seasonsAgo) : ladder.ToString(); // noloc
            return await CachedAsync(key, async () =>
            {
                // shortcut: country pages only see this global top PoolSize; past ~1000 players per board, keep a board per country.
                var entries = await _store.ReadBoardAsync(BoardOf(ladder), seasonsAgo, Ladders.PoolSize);
                var rows = entries.Select(e => RowOf(ladder, e)).ToList();
                rows.Sort(Order);
                return new LadderPool { Full = entries.Count >= Ladders.PoolSize, Rows = rows };
            });
        }

        /// <summary>
        /// A country's duel rows, verified against the protected data as the world board is (<see cref="VerifiedBoardAsync"/>):
        /// the true Elo of the month, and the country the player holds now. Cached per country.
        /// </summary>
        Task<LadderPool> DuelCountryAsync(LadderPool pool, string season, string country) =>
            CachedAsync("Duel_" + season + "_" + country, async () => // noloc
            {
                var inCountry = pool.Rows.Where(r => r.Country == country).ToList();
                var candidates = inCountry.Take(Ladders.Size + PvpConfig.BoardMargin).ToList();
                var data = await _store.ReadPlayersAsync(candidates.ConvertAll(r => r.PlayerId));
                var rows = new List<LadderRow>();
                foreach (var r in candidates)
                    if (data.TryGetValue(r.PlayerId, out var d) && d?.Country == country && RowOf(r.PlayerId, d, Ladder.Duel, season) is LadderRow v)
                    {
                        v.PlayerName = r.PlayerName;
                        rows.Add(v);
                    }
                rows.Sort(Order);
                bool truncated = pool.Full || candidates.Count < inCountry.Count || rows.Count > Ladders.Size;
                return new LadderPool { Full = truncated, Rows = rows.Take(Ladders.Size).ToList() };
            });

        async Task<LadderPool> CachedAsync(string key, Func<Task<LadderPool>> build)
        {
            long now = NowMs;
            var cached = await Shared<LadderPool>(LadderCollection, key);
            if (cached != null && cached.BuiltAtUnixMs <= now && now - cached.BuiltAtUnixMs < Ladders.CacheMinutes * 60_000L) return cached;
            var pool = await build();
            pool.BuiltAtUnixMs = now;
            try { await Shared<LadderPool>(LadderCollection, key, _ => pool); }
            catch (Exception) { /* the cache is a saving only: this page is served all the same */ }
            return pool;
        }
    }
}
