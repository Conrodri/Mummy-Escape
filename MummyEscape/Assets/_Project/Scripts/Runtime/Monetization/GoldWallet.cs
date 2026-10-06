using System;
using System.Threading.Tasks;
using MummyEscape.App;
using MummyEscape.Online;
using MummyEscape.Pvp;
using UnityEngine;

namespace MummyEscape.Monetization
{
    /// <summary>
    /// The golden wallet as the phone sees it. The server keeps it (purchases checked with Google, every price checked):
    /// golden scarabs are bought and spent only online, and the save holds a copy for display.
    /// </summary>
    public static class GoldWallet
    {
        public static bool Available(GameApp app) => app.Pvp != null;

        /// <summary>
        /// A purchase needs a lasting account (Google Play Games): a guest profile can be lost with the phone, and the
        /// golden scarabs with it. The demo buys nothing real.
        /// </summary>
        public static bool CanPurchase(GameApp app) => Available(app) && (app.Online.IsDemo || app.Online.Account == AccountState.Account);

        /// <summary>Runs a wallet call; the save takes the wallet the server sends back. Returns the error code, or null.</summary>
        public static async Task<string> RunAsync(GameApp app, Func<IPvpService, Task<WalletResponse>> call)
        {
            var pvp = app.Pvp;
            if (pvp == null) return "OFFLINE"; // noloc
            WalletResponse r;
            try { r = await call(pvp); }
            catch (Exception e)
            {
                Debug.LogWarning("[Wallet] " + e.Message);
                r = null;
            }
            if (r?.Wallet != null) app.Save.ApplyWallet(r.Wallet);
            return r == null ? "NETWORK" : r.Error; // noloc
        }

        public static Task RefreshAsync(GameApp app) => Available(app) ? RunAsync(app, p => p.GetWalletAsync()) : Task.CompletedTask;

        public static string ErrorText(string code)
        {
            switch (code)
            {
                case "OFFLINE": return Loc.T("Les scarabées dorés sont gardés en ligne : connecte-toi pour les utiliser."); // noloc
                case "GOLD": return Loc.T("Pas assez de scarabées dorés."); // noloc
                case "OWNED": return Loc.T("Tu l'as déjà."); // noloc
                case "PENDING": return Loc.T("Paiement en attente : tes scarabées dorés arriveront dès qu'il sera validé."); // noloc
                case "ALREADY_CREDITED": return Loc.T("Cet achat a déjà été crédité."); // noloc
                case "NOT_VERIFIED": return Loc.T("Achat non vérifié par Google : rien n'a été crédité. Si tu as été débité, contacte-nous."); // noloc
                case "STORE_UNAVAILABLE": return Loc.T("Vérification impossible pour le moment : ton achat n'est pas perdu, réessaie plus tard."); // noloc
                case "SEASON": return Loc.T("La saison du pass a changé : rouvre le pass."); // noloc
                case "NO_PASS": return Loc.T("Il faut le pass premium."); // noloc
                default: return Loc.T("Connexion impossible : réessaie plus tard.");
            }
        }
    }
}
