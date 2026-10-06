using System;
using MummyEscape.App;
using MummyEscape.Core;
using MummyEscape.Monetization;
using MummyEscape.Pvp;
using MummyEscape.UI.Screens;

namespace MummyEscape.UI
{
    /// <summary>
    /// The combat energy at the doors of the game modes. Solo: act 1 is free, then a run costs a point of the solo energy
    /// (10, kept by the device). Online: a duel or a 2v2 match costs a point of the combat energy (3, kept by the server).
    /// A point comes back every 6 minutes; when none is left, a rewarded ad gives some back, and the pass lifts every limit.
    /// </summary>
    public static class PlayGate
    {
        static long NowMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        /// <summary>Spends a point for that level and starts it; when none is left, offers more (then starts).</summary>
        public static void Solo(GameApp app, LevelId level, Action start)
        {
            if (!app.Save.CanPlaySolo(level))
            {
                Offer(app, PlayMode.Solo, () => Solo(app, level, start));
                return;
            }
            app.Save.UseSolo(level);
            start();
        }

        /// <summary>
        /// True when a point of combat energy is left (the server takes it when the match starts). Otherwise offers more
        /// and calls <paramref name="retry"/> once they are in.
        /// </summary>
        public static bool Ensure(GameApp app, Action retry)
        {
            if (app.Save.Unlimited || PvpLeft(app) > 0) return true;
            Offer(app, PlayMode.Duel, retry);
            return false;
        }

        /// <summary>The copy of the server's energy this phone shows (the server took its point when the match started).</summary>
        static EnergyMeter PvpMeter(GameApp app) => app.PvpProfile?.Data?.Energy;

        public static int PvpLeft(GameApp app) =>
            PvpMeter(app) is EnergyMeter m ? Energy.Left(m, EnergyConfig.PvpMax, NowMs) : EnergyConfig.PvpMax;

        /// <summary>A match started: the copy loses the point the server took.</summary>
        public static void SpendPvp(GameApp app)
        {
            if (!app.Save.Unlimited && PvpMeter(app) is EnergyMeter m) Energy.Spend(m, EnergyConfig.PvpMax, NowMs);
        }

        static void Offer(GameApp app, PlayMode mode, Action retry)
        {
            bool solo = mode == PlayMode.Solo;
            long next = solo ? app.Save.SoloEnergyNextInMs : PvpMeter(app) is EnergyMeter m ? Energy.NextInMs(m, NowMs) : 0;
            int adsLeft = solo ? app.Save.SoloAdsLeft : PvpMeter(app) is EnergyMeter p ? Energy.AdsLeft(p, NowMs) : EnergyConfig.AdRefillsPerDay;
            int refill = solo ? EnergyConfig.SoloAdRefill : EnergyConfig.PvpAdRefill;
            string text = (solo ? Loc.T("Plus d'énergie pour les parties solo (l'acte 1 reste libre).")
                                : Loc.T("Plus d'énergie de combat pour les duels et les matchs 2v2."))
                        + " " + Loc.F("Un point revient toutes les 6 minutes (le prochain dans {0}).", Energy.Clock(next)) + "\n\n"
                        + Loc.F("Regarde une courte pub pour +{0} d'énergie, ou prends le Pass : plus aucune limite.", refill);
            var ad = adsLeft <= 0 ? (Loc.T("Plus de pubs aujourd'hui"), ButtonStyle.Ghost, (Action)null)
                   : Ads.Available ? (Loc.F("Regarder une pub (+{0})", refill), ButtonStyle.Primary, (Action)(() => WatchAd(app, mode, retry)))
                   : (Loc.T("Pub indisponible pour le moment"), ButtonStyle.Ghost, (Action)null);
            app.UI.Open<OfferDialog>().Configure("Plus d'énergie", text,
                ad,
                ("Voir le Pass", ButtonStyle.Secondary, () => app.UI.Open<PassScreen>()),
                ("Plus tard", ButtonStyle.Ghost, null));
        }

        static async void WatchAd(GameApp app, PlayMode mode, Action retry)
        {
            var ads = Ads.Provider;
            if (ads == null) return;
            bool watched;
            try { watched = await ads.ShowRewardedAsync(); }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning("[Ads] " + e.Message);
                watched = false;
            }
            if (!watched) return;
            if (mode == PlayMode.Solo)
            {
                if (!app.Save.RefillSolo()) return;
            }
            else
            {
                if (app.Pvp == null) return;
                var refilled = await app.Pvp.RefillEnergyAsync();
                if (refilled?.Energy != null && app.PvpProfile?.Data != null) app.PvpProfile.Data.Energy = refilled.Energy;
                if (refilled == null || refilled.Error != null)
                {
                    app.UI.Open<OfferDialog>().Configure("Plus d'énergie", PvpScreen.ErrorText(refilled?.Error), ("OK", ButtonStyle.Primary, null)); // noloc
                    return;
                }
            }
            app.Audio.Play(Services.Sfx.Coin);
            retry?.Invoke();
        }

        /// <summary>"Énergie : 7/10 · +1 dans 4:12", or unlimited with the pass.</summary>
        public static string Status(GameApp app, PlayMode mode)
        {
            if (app.Save.Unlimited) return Loc.T("Pass actif : parties illimitées");
            bool solo = mode == PlayMode.Solo;
            int left = solo ? app.Save.SoloEnergyLeft : PvpLeft(app);
            int max = solo ? EnergyConfig.SoloMax : EnergyConfig.PvpMax;
            long next = solo ? app.Save.SoloEnergyNextInMs : PvpMeter(app) is EnergyMeter m ? Energy.NextInMs(m, NowMs) : 0;
            string count = solo ? Loc.F("Énergie : {0}", left + "/" + max) : Loc.F("Énergie de combat : {0}", left + "/" + max);
            return next > 0 ? count + " · " + Loc.F("+1 dans {0}", Energy.Clock(next)) : count;
        }
    }
}
