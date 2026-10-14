using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MummyEscape.App;
using MummyEscape.Online;
using MummyEscape.Pvp;
using UnityEngine;

namespace MummyEscape.Monetization
{
    /// <summary>
    /// The scarabs as the phone sees them: the server holds them once the phone's balance is carried over (once per
    /// account), and every scarab spend is asked there. The save keeps the balance for display.
    /// </summary>
    public static class ScarabWallet
    {
        /// <summary>Carries the scarabs over to the server if not done yet. Returns the error code, or null.</summary>
        public static async Task<string> OpenAsync(GameApp app)
        {
            if (app.Save.Data.ScarabsHeld) return null;
            if (app.Pvp == null) return "OFFLINE"; // noloc
            // The runs waiting are sent first: the server does not pay them yet, so the balance carried over keeps them.
            await app.SendSoloRuns(true);
            if (app.Save.Data.ScarabsHeld) return null;
            string error = await GoldWallet.RunAsync(app, p => p.OpenScarabsAsync(app.Save.ScarabSeed()));
            return error ?? (app.Save.Data.ScarabsHeld ? null : "NETWORK"); // noloc
        }

        /// <summary>A scarab spend on the server (carried over first); the save takes the wallet it sends back.</summary>
        public static async Task<string> RunAsync(GameApp app, Func<IPvpService, Task<WalletResponse>> call) =>
            await OpenAsync(app) ?? await GoldWallet.RunAsync(app, call);

        /// <summary>Turns of the scarab wheel, drawn by the server: the results, or the error code.</summary>
        public static async Task<(string Error, List<SpinResult> Results)> SpinAsync(GameApp app, int count)
        {
            string error = await OpenAsync(app);
            if (error != null) return (error, null);
            WheelSpinResponse r;
            try { r = await app.Pvp.SpinScarabWheelAsync(count); }
            catch (Exception e)
            {
                Debug.LogWarning("[Scarabs] " + e.Message);
                return ("NETWORK", null); // noloc
            }
            if (r?.Wallet != null) app.Save.ApplyWallet(r.Wallet);
            return r == null ? ("NETWORK", null) : (r.Error, r.Results); // noloc
        }

        public static string ErrorText(string code)
        {
            switch (code)
            {
                case "SCARABS": return Loc.T("Pas assez de scarabées."); // noloc
                case "STARS": return Loc.T("Il te faut plus d'étoiles pour cette pièce."); // noloc
                case "COOP_STARS": return Loc.T("Il te faut plus d'étoiles 2v2 pour cette pièce : gagne-les dans la campagne 2v2."); // noloc
                case "OFFLINE": return Loc.T("Les scarabées sont gardés en ligne : connecte-toi pour les dépenser."); // noloc
                default: return GoldWallet.ErrorText(code);
            }
        }
    }
}
