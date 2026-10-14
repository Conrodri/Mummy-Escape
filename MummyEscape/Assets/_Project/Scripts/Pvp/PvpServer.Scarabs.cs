using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MummyEscape.Core;
using MummyEscape.Monetization;

namespace MummyEscape.Pvp
{
    /// <summary>
    /// The scarabs, held by the server once the phone's balance is carried over: earned by the replayed runs (new stars of
    /// a campaign level, each endless tomb once), the pass, the 2v2 first clears and the gold offers; spent only here (shop
    /// pieces and collections at the shared prices, the scarab wheel, a guild).
    /// </summary>
    public sealed partial class PvpServer
    {
        /// <summary>
        /// The most a phone's balance carries over: about what the whole campaign, the ladders and the pass pay. A save
        /// edited for more keeps this much.
        /// </summary>
        public const int ScarabsCarriedMax = 20000;
        const int SeedEntriesMax = 400;

        /// <summary>
        /// Carries the phone's scarab economy over, once per account: the balance (capped), the best stars, the endless
        /// tombs already paid and the pieces owned (only real ones). Asked again, it changes nothing.
        /// </summary>
        public async Task<WalletResponse> OpenScarabsAsync(string me, ScarabSeed seed)
        {
            if (seed == null || (seed.Stars?.Count ?? 0) > SeedEntriesMax || (seed.Owned?.Count ?? 0) > SeedEntriesMax
                || (seed.EndlessCleared?.Count ?? 0) > DifficultyExt.All.Length)
                return new WalletResponse { Error = "UNKNOWN_PRODUCT" };
            var data = await Update(me, d =>
            {
                var w = WalletOf(d);
                if (w.ScarabsOpen) return;
                w.ScarabsOpen = true;
                w.Scarabs += Math.Min(ScarabsCarriedMax, Math.Max(0, seed.Scarabs));
                foreach (var entry in seed.Stars ?? new List<string>())
                    if (ParseStars(entry, out string key, out int stars)) SetStars(w, key, stars);
                var cleared = seed.EndlessCleared ?? new List<int>();
                for (int mode = 0; mode < cleared.Count; mode++)
                {
                    int max = EndlessLines((Difficulty)mode)?.Count ?? 10000;
                    Paid(w, mode, Math.Min(max, Math.Max(0, cleared[mode])));
                }
                foreach (var id in seed.Owned ?? new List<string>())
                    if (IsScarabPiece(id) && !w.Owned.Contains(id)) w.Owned.Add(id);
            });
            return new WalletResponse { Wallet = WalletOf(data) };
        }

        /// <summary>A piece the scarab economy gives: a shop piece or a scarab wheel legendary.</summary>
        static bool IsScarabPiece(string id) =>
            ScarabShop.Get(id) != null || Array.IndexOf(Casino.Scarabs.Legendaries, id) >= 0 || id == Halloween.WheelLegendary;

        static bool ParseStars(string entry, out string key, out int stars)
        {
            key = null;
            stars = 0;
            int eq = entry?.IndexOf('=') ?? -1;
            if (eq <= 0 || !LevelId.TryParseKey(entry.Substring(0, eq), out var id) || !int.TryParse(entry.Substring(eq + 1), out stars)) return false;
            key = id.Key;
            stars = Math.Min(3, Math.Max(0, stars));
            return true;
        }

        static int StarsAt(Wallet w, string key)
        {
            foreach (var entry in w.Stars)
                if (ParseStars(entry, out string k, out int s) && k == key) return s;
            return 0;
        }

        /// <summary>Keeps the best stars of a level.</summary>
        static void SetStars(Wallet w, string key, int stars)
        {
            if (stars <= StarsAt(w, key)) return;
            w.Stars.RemoveAll(e => ParseStars(e, out string k, out _) && k == key);
            w.Stars.Add(key + "=" + stars);
        }

        /// <summary>The campaign stars the server knows (the shop's act collections ask for some).</summary>
        public static int TotalStars(Wallet w)
        {
            int total = 0;
            foreach (var entry in w.Stars)
                if (ParseStars(entry, out _, out int s)) total += s;
            return total;
        }

        /// <summary>Notes the ladder's tombs paid up to <paramref name="tombs"/> (never fewer than before).</summary>
        static void Paid(Wallet w, int mode, int tombs)
        {
            while (w.EndlessPaid.Count <= mode) w.EndlessPaid.Add(0);
            w.EndlessPaid[mode] = Math.Max(w.EndlessPaid[mode], tombs);
        }

        /// <summary>
        /// The scarabs a replayed run pays, noted in the wallet: a campaign escape its new stars (on the moves: the expert time is not measured here),
        /// an endless escape <see cref="BattlePass.EndlessScarabs"/> for a tomb not paid yet. Nothing before they are held here.
        /// </summary>
        static int ScarabsOf(Wallet w, SoloRunReport run, LevelResult result)
        {
            if (!w.ScarabsOpen || result == null || !result.Won) return 0;
            if (run.EndlessMode >= 0)
            {
                if (run.EndlessMode < w.EndlessPaid.Count && run.Depth < w.EndlessPaid[run.EndlessMode]) return 0;
                Paid(w, run.EndlessMode, run.Depth + 1);
                return BattlePass.EndlessScarabs;
            }
            string key = result.Level.Key;
            int coins = Progression.CoinsFor(result, StarsAt(w, key));
            SetStars(w, key, result.Stars);
            return coins;
        }

        // ------------------------------------------------------------------ spends

        /// <summary>A shop piece, at today's price, once the player has the stars its act asks for (the 2v2 stars for a 2v2 piece).</summary>
        public async Task<WalletResponse> BuyScarabPieceAsync(string me, string id)
        {
            var item = ScarabShop.Get(id);
            if (item == null) return new WalletResponse { Error = "UNKNOWN_PRODUCT" };
            if (!ScarabShop.OnSale(item, _utcNow())) return new WalletResponse { Error = "EVENT" };
            if (item.MinCoopStars > 0)
            {
                var data = await Update(me); // 2v2 stars only grow: reading them before the spend is enough
                if (CoopLogic.TotalStars(data.CoopStars) < item.MinCoopStars) return new WalletResponse { Wallet = WalletOf(data), Error = "COOP_STARS" };
            }
            return await SpendAsync(me, _ => ScarabShop.PriceNow(item, _utcNow()), w =>
            {
                if (w.Owned.Contains(id)) return "OWNED";
                if (TotalStars(w) < item.MinStars) return "STARS";
                w.Owned.Add(id);
                return null;
            }, scarabs: true);
        }

        /// <summary>An act's collection: its missing pieces at once, 20 % off.</summary>
        public Task<WalletResponse> BuyScarabSetAsync(string me, int act)
        {
            var set = ScarabShop.ThemeSet(act);
            if (set.Count == 0) return Task.FromResult(new WalletResponse { Error = "UNKNOWN_PRODUCT" });
            return SpendAsync(me, w => ScarabShop.SetPrice(act, w.Owned), w =>
            {
                int added = 0;
                if (TotalStars(w) < act * ScarabShop.StarsPerTheme) return "STARS";
                foreach (var i in set) if (!w.Owned.Contains(i.Id)) { w.Owned.Add(i.Id); added++; }
                return added == 0 ? "OWNED" : null;
            }, scarabs: true);
        }

        /// <summary>A legendary of the scarab wheel bought outright.</summary>
        public Task<WalletResponse> BuyWheelLegendaryAsync(string me, string id)
        {
            var wheel = Casino.ScarabsAt(_utcNow());
            if (wheel.DirectPrice <= 0 || Array.IndexOf(wheel.Legendaries, id) < 0) return Task.FromResult(new WalletResponse { Error = "UNKNOWN_PRODUCT" });
            return SpendAsync(me, _ => wheel.DirectPrice, w =>
            {
                if (w.Owned.Contains(id)) return "OWNED";
                w.Owned.Add(id);
                return null;
            }, scarabs: true);
        }

        /// <summary>
        /// One turn of the scarab wheel (or <see cref="Casino.MultiSpins"/> for the price of <see cref="Casino.MultiSpinsPaid"/>):
        /// price, draws and prizes decided here.
        /// </summary>
        public async Task<WheelSpinResponse> SpinScarabWheelAsync(string me, int count = 1)
        {
            if (count != 1 && count != Casino.MultiSpins) return new WheelSpinResponse { Error = "COUNT" };
            var wheel = Casino.ScarabsAt(_utcNow());
            int price = Casino.PriceOf(wheel, count);
            var response = new WheelSpinResponse();
            var data = await Update(me, d =>
            {
                var w = WalletOf(d);
                response.Ok = false;
                response.Results = new List<SpinResult>();
                response.Result = null;
                response.Error = !w.ScarabsOpen ? "CLOSED" : w.Scarabs < price ? "SCARABS" : null;
                if (response.Error != null) return;
                w.Scarabs -= price;
                var results = Casino.SpinMany(wheel, count, _random, w.Owned, ref w.WheelSpins);
                foreach (var r in results)
                {
                    if (r.Kind == PrizeKind.Currency) w.Scarabs += r.Amount;
                    else if (r.Legendary != null && !w.Owned.Contains(r.Legendary)) w.Owned.Add(r.Legendary);
                }
                response.Ok = true;
                response.Results = results;
                response.Result = Casino.Best(results);
            });
            response.Wallet = WalletOf(data);
            return response;
        }

        /// <summary>Takes a guild's price once the scarabs are held here; null when paid (or still paid by the phone).</summary>
        async Task<string> PayGuildAsync(string me)
        {
            string error = null;
            await Update(me, d =>
            {
                var w = WalletOf(d);
                error = null;
                if (!w.ScarabsOpen) return;
                if (w.Scarabs < TeamConfig.GuildCreationScarabs) { error = "SCARABS"; return; }
                w.Scarabs -= TeamConfig.GuildCreationScarabs;
            });
            return error;
        }
    }
}
