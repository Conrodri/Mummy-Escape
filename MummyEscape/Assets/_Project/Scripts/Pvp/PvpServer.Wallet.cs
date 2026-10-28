// Mummy Rush — le portefeuille doré, gardé par le serveur : les achats du Play Store y sont crédités après vérification
// auprès de Google, et toute dépense de scarabées dorés passe par ici (le téléphone n'en a qu'une copie).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MummyEscape.Monetization;

namespace MummyEscape.Pvp
{
    /// <summary>Ce que le joueur a payé ou gagné en scarabées dorés.</summary>
    [Serializable]
    public class Wallet
    {
        public int Gold;
        /// <summary>Saisons dont le joueur a le pass premium.</summary>
        public List<string> Passes = new List<string>();
        /// <summary>Skins payants possédés : exclusivités du Trésor (gold_…) et pièces du pass (pass_…).</summary>
        public List<string> Skins = new List<string>();
        /// <summary>Paliers du pass récupérés de la saison en cours (seulement ceux qui donnent de l'or ou un skin).</summary>
        public string ClaimsSeason;
        public List<int> FreeClaimed = new List<int>();
        public List<int> PremiumClaimed = new List<int>();
        /// <summary>
        /// XP du pass de la saison <see cref="ClaimsSeason"/>, tenue par le serveur : courses solo rejouées, matchs jugés,
        /// paliers achetés. Les paliers réclamables en découlent.
        /// </summary>
        public int PassXp;
        /// <summary>Dernières courses solo et derniers matchs déjà comptés dans l'XP (un renvoi ne compte pas deux fois).</summary>
        public List<string> XpSeen = new List<string>();
        /// <summary>Derniers achats crédités (identifiants de commande), pour l'historique du joueur.</summary>
        public List<string> Orders = new List<string>();
        /// <summary>
        /// Derniers achats en or que le téléphone livre lui-même (scarabées, paliers), par identifiant de demande : rejouer une
        /// demande ne débite pas deux fois, et un téléphone qui a perdu la réponse retrouve ici ce qu'il doit livrer.
        /// </summary>
        public List<string> Requests = new List<string>();

        /// <summary>
        /// The scarabs are held here once <see cref="ScarabsOpen"/>: the phone's balance was carried over once (capped), then
        /// they are earned by replayed runs, the pass, the 2v2 first clears and the gold offers, and spent only here.
        /// </summary>
        public bool ScarabsOpen;
        public int Scarabs;
        /// <summary>Best stars per campaign level ("1-6=3"): a level pays for its new stars only.</summary>
        public List<string> Stars = new List<string>();
        /// <summary>Per endless ladder, the tombs already paid (each escape pays once).</summary>
        public List<int> EndlessPaid = new List<int>();
        /// <summary>Pieces bought with scarabs or won at the scarab wheel (and those carried over).</summary>
        public List<string> Owned = new List<string>();
        /// <summary>Turns of the scarab wheel since its last legendary (the <see cref="Casino.PityAfter"/> guarantee).</summary>
        public int WheelSpins;
        /// <summary>Name changes paid for (the first is free, then <see cref="GoldShop.RenamePrice"/>); the first choice is not one.</summary>
        public int Renames;
    }

    /// <summary>The phone's scarab economy, carried over once to the server (<see cref="PvpServer.OpenScarabsAsync"/>).</summary>
    [Serializable]
    public class ScarabSeed
    {
        public int Scarabs;
        /// <summary>Best stars per level, "key=stars".</summary>
        public List<string> Stars = new List<string>();
        /// <summary>Tombs escaped on each endless ladder.</summary>
        public List<int> EndlessCleared = new List<int>();
        public List<string> Owned = new List<string>();
    }

    [Serializable]
    public class WalletResponse
    {
        public Wallet Wallet;
        public string Error;
    }

    /// <summary>Un achat tel que le magasin le confirme.</summary>
    public class PurchaseCheck
    {
        public bool Ok;
        /// <summary>Identifiant de commande du magasin : un achat n'est crédité qu'une fois, sur un seul compte.</summary>
        public string OrderId;
        public string Error;
    }

    /// <summary>Vérifie un achat auprès du magasin (Google Play Developer API), puis le consomme.</summary>
    public interface IPurchaseVerifier
    {
        Task<PurchaseCheck> VerifyAsync(string productId, string purchaseToken);
        Task ConsumeAsync(string productId, string purchaseToken);
    }

    /// <summary>Un achat crédité (collection partagée) : à qui, quand, quoi.</summary>
    [Serializable]
    public class PurchaseRecord
    {
        public string OrderId;
        public string PlayerId;
        public string ProductId;
        public int Gold;
        public long AtUnixMs;
        /// <summary>Not yet in the owner's wallet: a retry after a failure in between credits it then (older records were credited).</summary>
        public bool Pending;
    }

    /// <summary>
    /// The arbiter of new orders: one item per shard that, once created, is only rewritten under its write lock, so two
    /// accounts presenting the same order at once are served one after the other (Cloud Save cannot create an item only
    /// if it is absent, which leaves the per-order record open to that race).
    /// </summary>
    [Serializable]
    public class OrderClaims
    {
        public List<string> Orders = new List<string>();
        public List<string> Players = new List<string>();
    }

    public sealed partial class PvpServer
    {
        public const string OrdersCollection = "pvp_orders";
        const int OrdersKept = 50;
        const int ClaimShards = 16, ClaimsKept = 500;
        const int RequestsKept = 50;

        /// <summary>Le magasin réel (null : aucun achat réel n'est crédité).</summary>
        public IPurchaseVerifier Purchases;
        /// <summary>Achats simulés ("test-…") acceptés : mode démo, et comptes de l'équipe.</summary>
        public bool AllowTestPurchases;

        static Wallet WalletOf(PlayerPvpData d) => d.Wallet ??= new Wallet();

        public async Task<WalletResponse> GetWalletAsync(string me)
        {
            if (!Developers.Is(me)) return new WalletResponse { Wallet = WalletOf(await Update(me)) };
            // L'équipe a le pass de chaque saison, offert à la première ouverture du portefeuille.
            var season = BattlePass.At(_utcNow());
            var data = await Update(me, d =>
            {
                var w = WalletOf(d);
                if (!w.Passes.Contains(season.Id)) w.Passes.Add(season.Id);
                if (!w.Skins.Contains(season.Legendary)) w.Skins.Add(season.Legendary);
            });
            return new WalletResponse { Wallet = WalletOf(data) };
        }

        /// <summary>Le pass de la saison en cours, qui lève les limites d'énergie.</summary>
        async Task<bool> HasPassAsync(string playerId) =>
            Developers.Is(playerId) || WalletOf(await Update(playerId)).Passes.Contains(BattlePass.At(_utcNow()).Id);

        /// <summary>
        /// Crédite un pack de scarabées dorés acheté sur le Play Store, après l'avoir vérifié auprès de Google ; rappeler avec le
        /// même achat ne le crédite pas deux fois (et le consomme, si la première fois s'était arrêtée avant).
        /// </summary>
        public async Task<WalletResponse> VerifyPurchaseAsync(string me, string productId, string purchaseToken)
        {
            var pack = GoldShop.Pack(productId);
            if (pack == null || string.IsNullOrEmpty(purchaseToken) || purchaseToken.Length > 1000) return new WalletResponse { Error = "UNKNOWN_PRODUCT" };
            string orderId;
            bool test = purchaseToken.StartsWith("test-", StringComparison.Ordinal);
            if (test)
            {
                if (!AllowTestPurchases && !Developers.Is(me)) return new WalletResponse { Error = "NOT_VERIFIED" };
                orderId = purchaseToken;
            }
            else
            {
                if (Purchases == null) return new WalletResponse { Error = "NOT_VERIFIED" };
                var check = await Purchases.VerifyAsync(productId, purchaseToken);
                if (check == null || !check.Ok || string.IsNullOrEmpty(check.OrderId)) return new WalletResponse { Error = check?.Error ?? "NOT_VERIFIED" };
                orderId = check.OrderId;
            }

            // Une commande appartient au premier compte qui l'a présentée : le registre permanent pour les commandes connues,
            // l'arbitre sous verrou pour les nouvelles.
            int gold = pack.Gold + pack.Bonus;
            string key = OrderKey(orderId);
            var record = await Shared<PurchaseRecord>(OrdersCollection, key);
            string owner = record?.PlayerId;
            if (owner == null)
            {
                // shortcut: a shard's very first write is unlocked (it does not exist yet), so the first orders of each shard
                // can still race; pre-create the 16 shards at deploy if that window matters.
                await Shared<OrderClaims>(OrdersCollection, ClaimShard(orderId), c =>
                {
                    c ??= new OrderClaims();
                    int i = c.Orders.IndexOf(orderId);
                    if (i >= 0) { owner = c.Players[i]; return c; }
                    owner = me;
                    c.Orders.Add(orderId);
                    c.Players.Add(me);
                    if (c.Orders.Count > ClaimsKept) { c.Orders.RemoveAt(0); c.Players.RemoveAt(0); }
                    return c;
                });
            }
            if (owner != me) return new WalletResponse { Error = "ALREADY_CREDITED" };
            if (record == null)
                record = await Shared<PurchaseRecord>(OrdersCollection, key, r =>
                    r ?? new PurchaseRecord { OrderId = orderId, PlayerId = me, ProductId = productId, Gold = gold, AtUnixMs = NowMs, Pending = true });

            // Crédité une seule fois, même si une tentative précédente s'est arrêtée entre le registre et le portefeuille.
            var data = !record.Pending
                ? await Update(me)
                : await Update(me, d =>
                {
                    var w = WalletOf(d);
                    if (w.Orders.Contains(orderId)) return;
                    w.Gold += gold;
                    w.Orders.Add(orderId);
                    if (w.Orders.Count > OrdersKept) w.Orders.RemoveRange(0, w.Orders.Count - OrdersKept);
                });
            if (record.Pending)
                await Shared<PurchaseRecord>(OrdersCollection, key, r => { if (r != null) r.Pending = false; return r; });
            if (!test) await Purchases.ConsumeAsync(productId, purchaseToken);
            return new WalletResponse { Wallet = WalletOf(data) };
        }

        /// <summary>The arbiter shard of an order (a stable hash: the same on every server instance).</summary>
        static string ClaimShard(string orderId)
        {
            uint h = 2166136261;
            foreach (char ch in orderId) h = (h ^ ch) * 16777619;
            return "claims_" + (h % ClaimShards); // noloc
        }

        /// <summary>The order's Cloud Save key: only ASCII letters, digits, - and _ are allowed there (GPA.3342-… becomes GPA_3342-…).</summary>
        static string OrderKey(string orderId) =>
            new string(orderId.Select(c => c == '.' ? '_' : c)
                .Where(c => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_').Take(120).ToArray());

        /// <summary>
        /// Retire <paramref name="price"/> du portefeuille et applique <paramref name="grant"/> ; "GOLD" s'il en manque. Avec un
        /// <paramref name="requestId"/>, une demande déjà acceptée est rendue telle quelle, sans débiter de nouveau.
        /// </summary>
        Task<WalletResponse> SpendAsync(string me, int price, Func<Wallet, string> grant, string requestId = null) =>
            SpendAsync(me, _ => price, grant, requestId);

        /// <summary>Le prix lu dans le portefeuille même (un ensemble ne coûte que ses pièces manquantes).</summary>
        /// With <paramref name="scarabs"/>, the price is in scarabs ("SCARABS" when short, "CLOSED" before they are held here).
        async Task<WalletResponse> SpendAsync(string me, Func<Wallet, int> priceOf, Func<Wallet, string> grant, string requestId = null, bool scarabs = false)
        {
            if (requestId != null && (requestId.Length == 0 || requestId.Length > 64)) return new WalletResponse { Error = "UNKNOWN_PRODUCT" };
            string error = null;
            var data = await Update(me, d =>
            {
                var w = WalletOf(d);
                w.Requests ??= new List<string>();
                if (requestId != null && w.Requests.Contains(requestId)) return;
                int price = priceOf(w); // avant le don : ce qui manque au joueur maintenant
                if (scarabs && !w.ScarabsOpen) error = "CLOSED";
                else if ((scarabs ? w.Scarabs : w.Gold) < price) error = scarabs ? "SCARABS" : "GOLD";
                else error = grant(w);
                if (error != null) return;
                if (scarabs) w.Scarabs -= price;
                else w.Gold -= price;
                if (requestId == null) return;
                w.Requests.Add(requestId);
                if (w.Requests.Count > RequestsKept) w.Requests.RemoveRange(0, w.Requests.Count - RequestsKept);
            });
            return new WalletResponse { Wallet = WalletOf(data), Error = error };
        }

        public Task<WalletResponse> BuyPassAsync(string me)
        {
            var season = BattlePass.At(_utcNow());
            return SpendAsync(me, GoldShop.PassPrice, w =>
            {
                if (w.Passes.Contains(season.Id)) return "OWNED";
                w.Passes.Add(season.Id);
                if (!w.Skins.Contains(season.Legendary)) w.Skins.Add(season.Legendary);
                return null;
            });
        }

        /// <summary>10 paliers du pass : le serveur prend l'or et avance l'XP qu'il tient jusqu'à 10 paliers plus haut.</summary>
        public Task<WalletResponse> BuyTiersAsync(string me, string requestId = null)
        {
            var season = BattlePass.At(_utcNow());
            return SpendAsync(me, GoldShop.TierBundlePrice, w =>
            {
                OpenSeason(w, season);
                int target = Math.Min(BattlePass.Tiers, BattlePass.TierOf(w.PassXp) + GoldShop.TierBundleSize);
                w.PassXp = Math.Max(w.PassXp, target * BattlePass.XpPerTier);
                return null;
            }, requestId);
        }

        /// <summary>A change of name: the first is free, the next ones cost <see cref="GoldShop.RenamePrice"/>.</summary>
        public Task<WalletResponse> PayRenameAsync(string me) =>
            SpendAsync(me, w => w.Renames == 0 ? 0 : GoldShop.RenamePrice, w => { w.Renames++; return null; });

        /// <summary>Le portefeuille passe à la saison en cours : XP et paliers récupérés repartent de zéro.</summary>
        static void OpenSeason(Wallet w, PassSeason season)
        {
            if (w.ClaimsSeason == season.Id) return;
            w.ClaimsSeason = season.Id;
            w.FreeClaimed.Clear();
            w.PremiumClaimed.Clear();
            w.PassXp = 0;
        }

        const int XpSeenKept = 300;

        /// <summary>Adds pass XP once per id (a solo run, a match); false when that id was already counted.</summary>
        static bool AddXp(Wallet w, PassSeason season, string id, int xp)
        {
            OpenSeason(w, season);
            w.XpSeen ??= new List<string>();
            if (w.XpSeen.Contains(id)) return false;
            w.XpSeen.Add(id);
            if (w.XpSeen.Count > XpSeenKept) w.XpSeen.RemoveRange(0, w.XpSeen.Count - XpSeenKept);
            w.PassXp = Math.Min(BattlePass.Tiers * BattlePass.XpPerTier, w.PassXp + Math.Max(0, xp));
            return true;
        }

        /// <summary>The highest tier that can be claimed: the one the server's XP reaches (the team has them all).</summary>
        static int ClaimableTiers(Wallet w, string me) => Developers.Is(me) ? BattlePass.Tiers : BattlePass.TierOf(w.PassXp);

        /// <summary>Des scarabées contre de l'or : crédités ici une fois les scarabées tenus par le serveur, sinon par le jeu.</summary>
        public Task<WalletResponse> BuyScarabsAsync(string me, string offerId, string requestId = null)
        {
            var offer = GoldShop.Offer(offerId);
            return offer == null ? Task.FromResult(new WalletResponse { Error = "UNKNOWN_PRODUCT" }) : SpendAsync(me, offer.Gold, w =>
                {
                    if (w.ScarabsOpen) w.Scarabs += offer.Scarabs; // held here: credited at once (before, the phone credited them)
                    return null;
                }, requestId);
        }

        public Task<WalletResponse> BuyGoldItemAsync(string me, string skinId)
        {
            var set = GoldShop.Set(skinId);
            if (set != null)
                return SpendAsync(me, w => GoldShop.SetPrice(set, w.Skins), w =>
                {
                    int missing = 0;
                    foreach (var id in set.SkinIds) if (!w.Skins.Contains(id)) { w.Skins.Add(id); missing++; }
                    return missing == 0 ? "OWNED" : null;
                });
            var item = GoldShop.Exclusive(skinId);
            if (item == null) return Task.FromResult(new WalletResponse { Error = "UNKNOWN_PRODUCT" });
            if (!GoldShop.OnSale(item, _utcNow())) return Task.FromResult(new WalletResponse { Error = "EVENT" });
            return SpendAsync(me, ShopSale.GoldPrice(item, _utcNow()), w =>
            {
                if (w.Skins.Contains(item.SkinId)) return "OWNED";
                w.Skins.Add(item.SkinId);
                return null;
            });
        }

        /// <summary>
        /// Récupère les récompenses du pass (or, skins, et scarabées une fois ceux-ci tenus ici). La progression
        /// est celle du jeu : le serveur ne donne que ce que la saison en cours contient, une fois, et la piste premium avec le pass.
        /// </summary>
        public async Task<WalletResponse> ClaimPassRewardsAsync(string me, string seasonId, List<int> freeTiers, List<int> premiumTiers)
        {
            var season = BattlePass.At(_utcNow());
            if (seasonId != season.Id) return new WalletResponse { Error = "SEASON" };
            if ((freeTiers?.Count ?? 0) > BattlePass.Tiers || (premiumTiers?.Count ?? 0) > BattlePass.Tiers) return new WalletResponse { Error = "UNKNOWN_PRODUCT" };
            string error = null;
            var data = await Update(me, d =>
            {
                var w = WalletOf(d);
                OpenSeason(w, season);
                int top = ClaimableTiers(w, me); // the tiers the XP held here reaches
                foreach (int tier in (freeTiers ?? new List<int>()).Distinct())
                    if (tier >= 1 && tier <= top && !w.FreeClaimed.Contains(tier))
                        if (Grant(w, BattlePass.Free(tier))) w.FreeClaimed.Add(tier);
                if ((premiumTiers?.Count ?? 0) > 0 && !w.Passes.Contains(season.Id)) { error = "NO_PASS"; return; }
                foreach (int tier in (premiumTiers ?? new List<int>()).Distinct())
                    if (tier >= 1 && tier <= top && !w.PremiumClaimed.Contains(tier))
                        if (Grant(w, BattlePass.Premium(season, tier))) w.PremiumClaimed.Add(tier);
            });
            return new WalletResponse { Wallet = WalletOf(data), Error = error };
        }

        static bool Grant(Wallet w, PassReward reward)
        {
            switch (reward.Kind)
            {
                case PassRewardKind.Gold: w.Gold += reward.Amount; return true;
                case PassRewardKind.Skin:
                    if (!w.Skins.Contains(reward.SkinId)) w.Skins.Add(reward.SkinId);
                    return true;
                case PassRewardKind.Scarabs:
                    if (!w.ScarabsOpen) return false; // not held here yet: the phone collects them
                    w.Scarabs += reward.Amount;
                    return true;
                default: return false;
            }
        }

        /// <summary>Les skins payants (Trésor, pass) qu'un joueur porte sans les posséder ne se montrent pas aux autres.</summary>
        async Task<PlayerLook> VerifiedLookAsync(PlayerLook claimed, string playerId)
        {
            var look = Developers.Restrict(PlayerLook.Sanitize(claimed), playerId);
            if (look == null || !IsHuman(playerId)) return look;
            var owned = WalletOf(await Update(playerId)).Skins;
            string Keep(string id) => GoldShop.IsPaid(id) && !owned.Contains(id) ? null : id;
            look.Mummy = Keep(look.Mummy);
            look.Color = Keep(look.Color);
            look.Torch = Keep(look.Torch);
            look.Hat = Keep(look.Hat);
            look.Shoes = Keep(look.Shoes);
            return look;
        }
    }
}
