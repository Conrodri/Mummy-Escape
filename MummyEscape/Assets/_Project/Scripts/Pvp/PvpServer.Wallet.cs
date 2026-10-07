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
        /// <summary>Derniers achats crédités (identifiants de commande), pour l'historique du joueur.</summary>
        public List<string> Orders = new List<string>();
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
    }

    public sealed partial class PvpServer
    {
        public const string OrdersCollection = "pvp_orders";
        const int OrdersKept = 50;

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

            // Le registre global : une commande appartient au premier compte qui l'a présentée.
            int gold = pack.Gold + pack.Bonus;
            string owner = null;
            bool fresh = false;
            await Shared<PurchaseRecord>(OrdersCollection, OrderKey(orderId), r =>
            {
                if (r != null) { owner = r.PlayerId; return r; }
                fresh = true;
                owner = me;
                return new PurchaseRecord { OrderId = orderId, PlayerId = me, ProductId = productId, Gold = gold, AtUnixMs = NowMs };
            });
            if (owner != me) return new WalletResponse { Error = "ALREADY_CREDITED" };
            var data = fresh
                ? await Update(me, d =>
                {
                    var w = WalletOf(d);
                    w.Gold += gold;
                    w.Orders.Add(orderId);
                    if (w.Orders.Count > OrdersKept) w.Orders.RemoveRange(0, w.Orders.Count - OrdersKept);
                })
                : await Update(me);
            if (!test) await Purchases.ConsumeAsync(productId, purchaseToken);
            return new WalletResponse { Wallet = WalletOf(data) };
        }

        /// <summary>The order's Cloud Save key: only ASCII letters, digits, - and _ are allowed there (GPA.3342-… becomes GPA_3342-…).</summary>
        static string OrderKey(string orderId) =>
            new string(orderId.Select(c => c == '.' ? '_' : c)
                .Where(c => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_').Take(120).ToArray());

        /// <summary>Retire <paramref name="price"/> du portefeuille et applique <paramref name="grant"/> ; "GOLD" s'il en manque.</summary>
        async Task<WalletResponse> SpendAsync(string me, int price, Func<Wallet, string> grant)
        {
            string error = null;
            var data = await Update(me, d =>
            {
                var w = WalletOf(d);
                error = w.Gold < price ? "GOLD" : grant(w);
                if (error == null) w.Gold -= price;
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

        /// <summary>10 paliers du pass : le serveur prend l'or, le jeu avance la progression (gardée par l'appareil).</summary>
        public Task<WalletResponse> BuyTiersAsync(string me) => SpendAsync(me, GoldShop.TierBundlePrice, _ => null);

        /// <summary>Des scarabées contre de l'or : le jeu crédite les scarabées (monnaie de l'appareil) quand le serveur a pris l'or.</summary>
        public Task<WalletResponse> BuyScarabsAsync(string me, string offerId)
        {
            var offer = GoldShop.Offer(offerId);
            return offer == null ? Task.FromResult(new WalletResponse { Error = "UNKNOWN_PRODUCT" }) : SpendAsync(me, offer.Gold, _ => null);
        }

        public Task<WalletResponse> BuyGoldItemAsync(string me, string skinId)
        {
            var item = GoldShop.Exclusive(skinId);
            if (item == null) return Task.FromResult(new WalletResponse { Error = "UNKNOWN_PRODUCT" });
            return SpendAsync(me, item.Gold, w =>
            {
                if (w.Skins.Contains(item.SkinId)) return "OWNED";
                w.Skins.Add(item.SkinId);
                return null;
            });
        }

        /// <summary>
        /// Récupère les récompenses du pass qui donnent de l'or ou un skin (les scarabées restent sur l'appareil). La progression
        /// est celle du jeu : le serveur ne donne que ce que la saison en cours contient, une fois, et la piste premium avec le pass.
        /// </summary>
        public async Task<WalletResponse> ClaimPassRewardsAsync(string me, string seasonId, List<int> freeTiers, List<int> premiumTiers)
        {
            var season = BattlePass.At(_utcNow());
            if (seasonId != season.Id) return new WalletResponse { Error = "SEASON" };
            string error = null;
            var data = await Update(me, d =>
            {
                var w = WalletOf(d);
                if (w.ClaimsSeason != season.Id)
                {
                    w.ClaimsSeason = season.Id;
                    w.FreeClaimed.Clear();
                    w.PremiumClaimed.Clear();
                }
                foreach (int tier in (freeTiers ?? new List<int>()).Distinct())
                    if (tier >= 1 && tier <= BattlePass.Tiers && !w.FreeClaimed.Contains(tier))
                        if (Grant(w, BattlePass.Free(tier))) w.FreeClaimed.Add(tier);
                if ((premiumTiers?.Count ?? 0) > 0 && !w.Passes.Contains(season.Id)) { error = "NO_PASS"; return; }
                foreach (int tier in (premiumTiers ?? new List<int>()).Distinct())
                    if (tier >= 1 && tier <= BattlePass.Tiers && !w.PremiumClaimed.Contains(tier))
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
                default: return false;
            }
        }

        /// <summary>Les skins payants (Trésor, pass) qu'un joueur porte sans les posséder ne se montrent pas aux autres.</summary>
        async Task<PlayerLook> VerifiedLookAsync(PlayerLook claimed, string playerId)
        {
            var look = Developers.Restrict(PlayerLook.Sanitize(claimed), playerId);
            if (look == null || !IsHuman(playerId)) return look;
            var owned = WalletOf(await Update(playerId)).Skins;
            string Keep(string id) => id != null && (id.StartsWith("gold_", StringComparison.Ordinal) || id.StartsWith("pass_", StringComparison.Ordinal)) && !owned.Contains(id) ? null : id;
            look.Mummy = Keep(look.Mummy);
            look.Color = Keep(look.Color);
            look.Torch = Keep(look.Torch);
            look.Hat = Keep(look.Hat);
            look.Shoes = Keep(look.Shoes);
            return look;
        }
    }
}
