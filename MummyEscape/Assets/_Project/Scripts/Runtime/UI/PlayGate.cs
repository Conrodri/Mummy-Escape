using System;
using MummyEscape.App;
using MummyEscape.Monetization;
using MummyEscape.UI.Screens;

namespace MummyEscape.UI
{
    /// <summary>
    /// The daily game limits at the doors of the game modes: lets the game start when one is left, otherwise offers a
    /// rewarded ad (3 more solo runs, 1 more match) or the pass (no limit).
    /// </summary>
    public static class PlayGate
    {
        /// <summary>Uses a game of that kind and starts it; when none is left, offers to get more (then starts).</summary>
        public static void Play(GameApp app, PlayMode mode, Action start)
        {
            if (!Ensure(app, mode, () => Play(app, mode, start))) return;
            app.Save.UsePlay(mode);
            start();
        }

        /// <summary>
        /// True when a game of that kind is left (it is used later, when the match is found). Otherwise offers more
        /// and calls <paramref name="retry"/> once they are in.
        /// </summary>
        public static bool Ensure(GameApp app, PlayMode mode, Action retry)
        {
            if (app.Save.CanPlay(mode)) return true;
            OfferMore(app, mode, retry);
            return false;
        }

        static void OfferMore(GameApp app, PlayMode mode, Action retry)
        {
            string used = mode == PlayMode.Solo ? Loc.F("Tu as joué tes {0} parties solo gratuites du jour.", PlayLimits.Max(mode))
                        : mode == PlayMode.Duel ? Loc.F("Tu as joué tes {0} duels 1v1 gratuits du jour.", PlayLimits.Max(mode))
                        : Loc.F("Tu as joué tes {0} matchs 2v2 gratuits du jour.", PlayLimits.Max(mode));
            string text = used + " " + Loc.T("Ils reviennent à minuit.") + "\n\n"
                        + (mode == PlayMode.Solo
                            ? Loc.F("Regarde une courte pub pour {0} parties de plus, ou prends le Pass : plus aucune limite.", PlayLimits.AdRefill(mode))
                            : Loc.T("Regarde une courte pub pour un match de plus, ou prends le Pass : plus aucune limite."));
            var ad = Ads.Available
                ? (Loc.F("Regarder une pub (+{0})", PlayLimits.AdRefill(mode)), ButtonStyle.Primary, (Action)(() => WatchAd(app, mode, retry)))
                : (Loc.T("Pub indisponible pour le moment"), ButtonStyle.Ghost, (Action)null);
            app.UI.Open<OfferDialog>().Configure("Plus de parties", text,
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
            app.Save.AddPlays(mode, PlayLimits.AdRefill(mode));
            app.Audio.Play(Services.Sfx.Coin);
            retry?.Invoke();
        }

        /// <summary>"Parties gratuites aujourd'hui : 7/10", or unlimited with the pass.</summary>
        public static string Status(GameApp app, PlayMode mode)
        {
            if (app.Save.Unlimited) return Loc.T("Pass actif : parties illimitées");
            int left = app.Save.PlaysLeft(mode), max = PlayLimits.Max(mode);
            string count = Math.Max(0, left) + "/" + max;
            return mode == PlayMode.Solo ? Loc.F("Parties solo gratuites aujourd'hui : {0}", count)
                 : mode == PlayMode.Duel ? Loc.F("Duels gratuits aujourd'hui : {0}", count)
                 : Loc.F("Matchs 2v2 gratuits aujourd'hui : {0}", count);
        }
    }
}
