using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MummyEscape.Game;
using MummyEscape.Online;
using MummyEscape.Pvp;
using MummyEscape.Services;
using MummyEscape.Visual;
using UnityEngine;
using UnityEngine.UI;

namespace MummyEscape.UI.Screens
{
    /// <summary>
    /// Duel home: league, Elo and world rank, the "find a rival" button (unlocked at 35 solo stars), the month's
    /// rewards to collect and the last duels to watch again (the seal shop is in the Boutique). Everything shown comes from the server profile.
    /// </summary>
    public sealed class PvpScreen : UIScreen
    {
        public override NavTab Tab => NavTab.Duel;

        Image _badge;
        Text _league, _elo, _record, _season, _info;
        Text _seals, _plays;
        Button _find, _claim;
        RectTransform _history, _saved;
        Text _historyHint, _savedTitle, _savedHint;
        Button _cancelReplace;
        DuelRecord _replacing;
        ScrollRect _scroll;
        bool _busy;
        int _request;
        UIKit.Segmented _modes;
        /// <summary>The duel search running (the button cancels it).</summary>
        CancellationTokenSource _duelSearch;

        protected override void Build()
        {
            UIKit.Backdrop(Root);
            Header("Duel");
            var body = Body(190, 40, 40);
            UIKit.Column(body, 16);
            _modes = Modes(body, Router, 0);
            var list = UIKit.Scroll(body, out _scroll);
            UIKit.Size(_scroll, -1, -1, -1, 1);
            list.GetComponent<VerticalLayoutGroup>().spacing = 20;

            // Profile: league badge, Elo and rank, record, seals.
            var card = UIKit.Panel(list, "Profile"); // noloc
            card.raycastTarget = false;
            UIKit.Size(card, 260);
            var row = card.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(28, 28, 20, 20);
            row.spacing = 26;
            row.childAlignment = TextAnchor.MiddleLeft;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = false;
            _badge = UIKit.Image(card.transform, UISprites.Circle, UIKit.Gold, false, "Badge"); // noloc
            UIKit.Size(_badge, 170, 170);
            var swords = UIKit.Image(_badge.transform, UISprites.Swords, UIKit.Ink, false, "Icon"); // noloc
            UIKit.Place(swords.rectTransform, 0.5f, 0.5f, 104, 104);
            var info = UIKit.Rect("Info", card.transform);
            UIKit.Size(info, 210, -1, 1);
            UIKit.Column(info, 4, 0, TextAnchor.MiddleLeft);
            _league = UIKit.Title(info, "", 54, null, TextAnchor.MiddleLeft);
            UIKit.FitText(_league, 30);
            UIKit.Size(_league, 70);
            _elo = UIKit.Label(info, "", 30, UIKit.Sand, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIKit.FitText(_elo, 20);
            UIKit.Size(_elo, 42);
            _record = UIKit.Label(info, "", 26, UIKit.Dim, TextAnchor.MiddleLeft);
            UIKit.FitText(_record, 18);
            UIKit.Size(_record, 36);
            var wallet = UIKit.Row(info, 56, 12);
            wallet.childAlignment = TextAnchor.MiddleLeft;
            _seals = UIKit.Chip(wallet.transform, UISprites.Seal, "0", UIKit.Turquoise, 56);

            _season = UIKit.Label(list, "", 26, UIKit.Dim);
            UIKit.FitText(_season, 18);
            UIKit.Size(_season, 40);

            _find = UIKit.Button(list, "Chercher un adversaire", FindDuel, 44, ButtonStyle.Primary);
            UIKit.FitText(_find.GetComponentInChildren<Text>(), 24);
            UIKit.Size(_find, 116);
            _plays = UIKit.Label(list, "", 24, UIKit.Dim);
            UIKit.Size(_plays, 34);

            var bots = UIKit.Toggle(list, "Un bot de ma ligue si personne en vue (1 min)", App.Settings.PvpBots, App.Settings.SetPvpBots);
            UIKit.Size(bots, 64);

            _info = UIKit.Label(list, "", 28, UIKit.Sand);
            UIKit.Size(_info, 84);

            var rules = UIKit.Label(list, "En direct contre un joueur de ta ligue : même tombeau, même départ, il court à tes côtés. Le premier sorti gagne, le premier mort perd ; au bout de 3 minutes, le plus avancé.", 26, UIKit.Dim);
            UIKit.FitText(rules, 18);
            UIKit.Size(rules, 110);

            _claim = UIKit.Button(list, "Récompenses", Claim, 30, ButtonStyle.Primary);
            UIKit.FitText(_claim.GetComponentInChildren<Text>(), 18);
            UIKit.Size(_claim, 96);

            _savedTitle = UIKit.SectionTitle(list, "");
            _savedHint = UIKit.Label(list, "", 24, UIKit.Dim, TextAnchor.MiddleLeft);
            UIKit.FitText(_savedHint, 16);
            UIKit.Size(_savedHint, 60);
            _cancelReplace = UIKit.Button(list, "Annuler", () => { _replacing = null; FillHistory(); }, 28);
            UIKit.Size(_cancelReplace, 80);
            _saved = UIKit.Rect("Saved", list); // noloc
            UIKit.Column(_saved, 12, 0);

            UIKit.SectionTitle(list, "Derniers duels");
            _historyHint = UIKit.Label(list, "", 24, UIKit.Dim, TextAnchor.MiddleLeft);
            UIKit.FitText(_historyHint, 16);
            UIKit.Size(_historyHint, 60);
            _history = UIKit.Rect("History", list); // noloc
            UIKit.Column(_history, 12, 0);
        }

        public override void OnShow()
        {
            ProfileSetupScreen.AskIfNeeded(Router, App);
            App.Lighting.SetMood(false);
            _modes.Select(0);
            _busy = false;
            _replacing = null;
            _info.text = "";
            _scroll.verticalNormalizedPosition = 1f;
            Show(App.PvpProfile);
            Reload();
            FillHistory();
        }

        async void Reload()
        {
            if (App.Pvp == null)
            {
                Show(null);
                _info.text = Loc.T("Les duels se jouent en ligne : active le mode en ligne (Paramètres › Confidentialité).");
                return;
            }
            int request = ++_request;
            if (App.PvpProfile == null) _info.text = Loc.T("Chargement…");
            await App.SyncSoloStars();
            var profile = await App.RefreshPvpProfile();
            if (request != _request || this == null) return;
            if (profile == null)
            {
                _info.text = Loc.T("Connexion au serveur des duels impossible.");
                return;
            }
            if (!_busy) _info.text = App.Pvp.IsDemo ? "<color=#E8C35A>" + Loc.T("Démo hors ligne : adversaires simulés") + "</color>" : "";
            Show(profile);
            var history = await App.Pvp.GetHistoryAsync();
            if (request != _request || this == null) return;
            if (history?.Duels != null)
            {
                Online.ReplayStore.Merge(history.Duels);
                FillHistory();
            }
        }

        void Show(PvpProfileResponse profile)
        {
            var d = profile?.Data;
            bool locked = App.Save.TotalStars < PvpConfig.RequiredSoloStars;
            bool available = App.Pvp != null && d != null;
            int elo = d?.Elo ?? PvpConfig.StartingElo;
            var league = Leagues.FromElo(elo);
            bool top = profile != null && profile.WorldRank >= 1 && profile.WorldRank <= PvpConfig.Top100Size;

            _badge.color = top ? UIKit.Turquoise : PvpSkins.LeagueColor(league);
            _league.text = top ? Loc.T("Top 100") : Loc.F("Ligue {0}", Loc.T(PvpSkins.LeagueName(league)));
            UIKit.TintTitle(_league, top ? UIKit.Turquoise : PvpSkins.LeagueColor(league));
            string leagueName = Loc.T(PvpSkins.LeagueName(league));
            _elo.text = d == null ? "Elo —" // noloc
                      : top ? Loc.F("Elo {0} · {1} · {2}e mondial", elo, leagueName, profile.WorldRank)
                      : profile.WorldRank > 0 ? Loc.F("Elo {0} · {1}e mondial", elo, profile.WorldRank)
                      : Loc.F("Elo {0} · pas encore classé ce mois-ci", elo);
            _record.text = d == null ? "" : Loc.P(d.Wins, "{0} victoire", "{0} victoires") + " · " + Loc.P(d.Draws, "{0} nul", "{0} nuls") + " · " + Loc.P(d.Losses, "{0} défaite", "{0} défaites")
                + (d.TotalDuels < PvpConfig.PlacementDuels ? "  ·  " + Loc.F("placement {0}/{1}", d.TotalDuels, PvpConfig.PlacementDuels) : "");
            _seals.text = (d?.Seals ?? 0).ToString();

            if (d != null)
            {
                int next = 0;
                foreach (int step in SeasonRewards.ParticipationSteps) if (d.SeasonCountedDuels < step) { next = step; break; }
                _season.text = Capitalized(PvpSkins.MonthName(d.Season)) + " : " + Loc.P(d.SeasonDuels, "{0} duel", "{0} duels")
                    + (next > 0 ? "  ·  " + Loc.F("torche de saison {0}/{1}", d.SeasonCountedDuels, next) : "")
                    + (d.DailyChestGranted ? "" : "  ·  " + Loc.T("coffre du jour à gagner"));
            }
            else _season.text = "";

            _find.gameObject.SetActive(App.Pvp != null);
            _plays.text = PlayGate.Status(App, Monetization.PlayMode.Duel);
            UIKit.SetLabel(_find, _duelSearch != null ? "Annuler la recherche" : _busy ? "Recherche d'un adversaire…" : locked ? Loc.F("{0} étoiles en solo pour débloquer ({1}/{0})", PvpConfig.RequiredSoloStars, App.Save.TotalStars) : "Chercher un adversaire");
            _find.interactable = available && !locked && (!_busy || _duelSearch != null);

            bool claimable = d?.LastSeason != null && !d.LastSeason.RewardsClaimed;
            _claim.gameObject.SetActive(claimable);
            if (claimable) UIKit.SetLabel(_claim, Loc.F("Récompenses de {0}", PvpSkins.MonthName(d.LastSeason.Season)));
            _claim.interactable = !_busy;
        }

        /// <summary>
        /// The duels kept apart (until the player removes or replaces them), then the last duels played, each to
        /// watch again from both sides.
        /// </summary>
        void FillHistory()
        {
            var store = Online.ReplayStore.Saved;
            if (_replacing != null && store.Count < Online.ReplayStore.SavedSize) _replacing = null;
            UIKit.ClearChildren(_saved);
            _savedTitle.text = Loc.F("Replays enregistrés ({0}/{1})", store.Count, Online.ReplayStore.SavedSize);
            _savedHint.text = _replacing != null ? "<color=#E8C35A>" + Loc.T("Les 10 places sont prises : touche le replay à remplacer.") + "</color>"
                            : store.Count == 0 ? Loc.T("Enregistre un duel (+) pour le garder : il restera ici tant que tu ne le remplaces pas.")
                            : Loc.T("Gardés tant que tu ne les remplaces pas.");
            _cancelReplace.gameObject.SetActive(_replacing != null);
            foreach (var duel in store)
            {
                var d = duel;
                if (_replacing != null)
                {
                    var row = DuelRow(_saved, d, () => Replace(d));
                    var replace = UIKit.Button(row.transform, "Remplacer", () => Replace(d), 24, ButtonStyle.Primary);
                    UIKit.FitText(replace.GetComponentInChildren<Text>(), 16);
                    UIKit.Size(replace, 84, 200, 0);
                    continue;
                }
                var h = DuelRow(_saved, d, () => Watch(d));
                UIKit.IconButton(h.transform, UISprites.Close, () => { Online.ReplayStore.Forget(d.MatchId); FillHistory(); }, 72);
                UIKit.IconButton(h.transform, UISprites.Play, () => Watch(d), 84).interactable = d.Me != null && ReplayScreen.Playable(d.GeneratorVersion);
            }

            UIKit.ClearChildren(_history);
            var duels = Online.ReplayStore.Duels;
            _historyHint.text = duels.Count == 0 ? Loc.T("Tes 10 derniers duels apparaîtront ici, à revoir des deux points de vue.")
                                                 : Loc.T("Les 10 derniers, à revoir des deux points de vue. Les plus anciens laissent la place aux nouveaux.");
            foreach (var duel in duels)
            {
                var d = duel;
                var h = DuelRow(_history, d, () => Watch(d));
                bool kept = Online.ReplayStore.IsSaved(d.MatchId);
                var save = UIKit.IconButton(h.transform, kept ? UISprites.Check : UISprites.Plus, () => Keep(d), 72);
                save.interactable = !kept && d.Me != null && ReplayScreen.Playable(d.GeneratorVersion);
                UIKit.IconButton(h.transform, UISprites.Play, () => Watch(d), 84).interactable = d.Me != null && ReplayScreen.Playable(d.GeneratorVersion);
            }
        }

        void Keep(DuelRecord d)
        {
            if (Online.ReplayStore.Keep(d)) App.Audio.Play(Sfx.Coin);
            else
            {
                // Full: the player picks the one to give up, in the list above.
                _replacing = d;
                _scroll.verticalNormalizedPosition = 1f;
            }
            FillHistory();
        }

        void Replace(DuelRecord old)
        {
            if (_replacing == null) return;
            Online.ReplayStore.Keep(_replacing, old.MatchId);
            _replacing = null;
            App.Audio.Play(Sfx.Coin);
            FillHistory();
        }

        /// <summary>One duel: result, rival, date and Elo change; the caller adds the buttons at the end.</summary>
        HorizontalLayoutGroup DuelRow(RectTransform parent, DuelRecord d, System.Action onClick)
        {
            UIKit.ListItem(parent, 132, onClick, out var h);
            var badge = UIKit.Image(h.transform, UISprites.Circle, ResultColor(d), false, "Result"); // noloc
            UIKit.Size(badge, 84, 84, 0);
            var letter = UIKit.Title(badge.transform, !d.Resolved ? "…" : d.Result == DuelResult.Win ? Loc.T("V") : d.Result == DuelResult.Draw ? Loc.T("N") : Loc.T("D"), 44, UIKit.Ink); // noloc
            UIKit.Stretch(letter.rectTransform);

            var col = UIKit.Rect("Text", h.transform); // noloc
            UIKit.Size(col, -1, -1, 1);
            UIKit.Column(col, 2, 0, TextAnchor.MiddleLeft);
            string rival = d.Rival == null ? Loc.T("En attente d'un adversaire")
                         : Loc.F("contre {0}", string.IsNullOrEmpty(d.Rival.PlayerName) ? Loc.T("Momie anonyme") : d.Rival.PlayerName);
            var name = UIKit.Label(col, rival, 30, UIKit.Sand, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIKit.FitText(name, 20);
            UIKit.Size(name, 44);
            int delta = d.EloAfter - d.EloBefore;
            string when = System.DateTimeOffset.FromUnixTimeMilliseconds(d.PlayedAtUnixMs).ToLocalTime().ToString("dd/MM HH:mm"); // noloc
            string elo = d.Resolved ? "  ·  Elo " + (delta > 0 ? "+" : "") + delta : ""; // noloc
            var sub = UIKit.Label(col, when + elo + (d.Reported ? "  ·  " + Loc.T("signalé") : ""), 24, UIKit.Dim, TextAnchor.MiddleLeft);
            UIKit.FitText(sub, 16);
            UIKit.Size(sub, 34);
            return h;
        }

        static Color ResultColor(DuelRecord d) =>
            !d.Resolved ? UIKit.Dim : d.Result == DuelResult.Win ? UIKit.Gold : d.Result == DuelResult.Draw ? UIKit.Sand : UIKit.Danger;

        void Watch(DuelRecord d)
        {
            if (_busy || d.Me == null) return;
            if (!ReplayScreen.Playable(d.GeneratorVersion)) { App.Audio.Play(Sfx.Bump); return; }
            Router.Open<ReplayScreen>().Show(d);
        }

        static string Capitalized(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        async void Claim()
        {
            var last = App.PvpProfile?.Data?.LastSeason;
            string month = last != null ? PvpSkins.MonthName(last.Season) : "";
            _busy = true;
            Show(App.PvpProfile);
            var r = await App.Pvp.ClaimSeasonRewardsAsync();
            if (this == null) return;
            _busy = false;
            if (r != null && r.Error == null && r.NewRewards.Count > 0)
            {
                App.Save.GrantSkins(r.NewRewards);
                var names = new List<string>();
                foreach (var id in r.NewRewards) names.Add(Loc.T(SkinCatalog.Get(id).Name));
                _info.text = Loc.F("Débloqué : {0}", string.Join(", ", names)) + "\n<size=22>" + Loc.T("À porter depuis la Boutique") + "</size>";
                App.Audio.Play(Sfx.Win);
            }
            else if (r?.Error == "NOT_ELIGIBLE")
                _info.text = Loc.F("Pas de récompense pour {0} : il fallait {1} duels (dont {2} la dernière semaine), ou {3} duels comptés pour la torche.",
                                   month, SeasonRewards.MinDuelsForRankReward, SeasonRewards.MinDuelsLastWeekForRankReward, SeasonRewards.ParticipationSteps[0]);
            else _info.text = ErrorText(r?.Error);
            await App.RefreshPvpProfile();
            if (this != null) Show(App.PvpProfile);
        }

        /// <summary>
        /// The live duel search: a rival of the league, connected (or a bot of the league after a minute if the player
        /// accepts them). The button cancels a search in progress.
        /// </summary>
        async void FindDuel()
        {
            if (_duelSearch != null) { CancelDuelSearch(); return; }
            if (_busy || App.Pvp == null) return;
            if (!PlayGate.Ensure(App, FindDuel)) return;
            var matchmaker = DuelMatchmakerFactory.For(App.Pvp);
            if (matchmaker == null)
            {
                _info.text = Loc.T("Les duels en direct ne sont pas disponibles sur cette version.");
                return;
            }
            _busy = true;
            var search = _duelSearch = new CancellationTokenSource();
            _info.text = Loc.T("Recherche d'un adversaire…");
            Show(App.PvpProfile);
            await App.SyncSoloStars();
            var start = await FindLiveAsync(App, matchmaker, text => { if (this != null && _duelSearch == search) _info.text = text; }, search.Token);
            if (this == null || search.IsCancellationRequested)
            {
                start?.Link?.Dispose();
                return;
            }
            _busy = false;
            _duelSearch = null;
            if (start == null)
            {
                Show(App.PvpProfile); // the status says why
                return;
            }
            VersusLive(Router, start);
        }

        void CancelDuelSearch()
        {
            if (_duelSearch == null) return;
            _duelSearch.Cancel();
            _duelSearch = null;
            _busy = false;
            if (_info != null) _info.text = "";
            if (App.PvpProfile != null) Show(App.PvpProfile);
        }

        public override void OnHide() => CancelDuelSearch();

        /// <summary>1v1 (this screen) or 2v2 (<see cref="DuoScreen"/>), one tab away from each other; Back leads home.</summary>
        internal static UIKit.Segmented Modes(Transform body, UIRouter router, int selected) =>
            new UIKit.Segmented(body, new[] { "1v1", "2v2" }, i => // noloc
            {
                if (i == selected) return;
                router.Reset<MainMenuScreen>();
                if (i == 0) router.Open<PvpScreen>();
                else router.Open<DuoScreen>();
            }, 96);

        /// <summary>Looks for a live rival of the player's league (the matchmaker tells how it goes through <paramref name="status"/>).</summary>
        public static Task<DuelStart> FindLiveAsync(MummyEscape.App.GameApp app, IDuelMatchmaker matchmaker, Action<string> status, CancellationToken cancel)
        {
            var look = PvpSkins.Look(app.Save.Loadout);
            look.Title = TitleBook.Equipped(app);
            return matchmaker.FindAsync(new DuelSearch
            {
                Me = app.Online.PlayerId,
                Name = app.Online.PlayerName,
                Look = look,
                Elo = app.PvpProfile?.Data?.Elo ?? PvpConfig.StartingElo,
                AllowBots = app.Settings.PvpBots,
                Pvp = app.Pvp,
            }, status, cancel);
        }

        /// <summary>The VS screen while the tomb is drawn, then the live duel (HUD, preview, the start together).</summary>
        public static void VersusLive(UIRouter router, DuelStart start)
        {
            var app = MummyEscape.App.GameApp.I;
            var match = new PvpMatch(start.Match, start.Me);
            PlayGate.SpendPvp(app);
            _ = Task.Run(() => PvpServer.Arena(match.Seed));
            router.Open<VsScreen>().Show(app.Save.Loadout, app.Online.PlayerName, match.Duel.MyElo, match.Ghost, () =>
            {
                router.Open<HudScreen>();
                _ = app.Game.StartDuel(match, start.Link);
            });
        }

        /// <summary>The VS screen while the tomb is drawn, then the run (HUD, preview).</summary>
        public static void Versus(UIRouter router, FindDuelResponse duel)
        {
            var app = MummyEscape.App.GameApp.I;
            _ = System.Threading.Tasks.Task.Run(() => PvpServer.Arena(duel.Seed));
            PlayGate.SpendPvp(app);
            router.Open<VsScreen>().Show(app.Save.Loadout, app.Online.PlayerName, duel.MyElo, duel.Ghost, () =>
            {
                router.Open<HudScreen>();
                _ = app.Game.StartDuel(new PvpMatch(duel));
            });
        }

        /// <summary>The server's error codes, in words.</summary>
        public static string ErrorText(string code)
        {
            switch (code)
            {
                case "LOCKED": return Loc.F("Gagne {0} étoiles en solo pour débloquer les duels.", PvpConfig.RequiredSoloStars); // noloc
                case "OUTDATED": return Loc.T("Mets le jeu à jour pour affronter les autres joueurs."); // noloc
                case "SEALS": return Loc.T("Pas assez de sceaux."); // noloc
                case "LEAGUE": return Loc.T("Atteins d'abord la ligue demandée."); // noloc
                case "OWNED": return Loc.T("Tu l'as déjà."); // noloc
                case "NOTHING_TO_CLAIM": return Loc.T("Rien à récupérer pour le moment.");
                case "ENERGY": return Loc.T("Plus d'énergie de combat : un point revient toutes les 6 minutes."); // noloc
                case "ADS_LIMIT": return Loc.T("Plus de pubs aujourd'hui"); // noloc
                case "INVALID_RUN": return Loc.T("Course refusée par le serveur : comptée comme un abandon.");
                case "NO_PENDING_DUEL": return Loc.T("Ce duel a expiré (plus de 10 minutes) : il compte comme un abandon.");
                default: return Loc.T("Connexion au serveur des duels impossible.");
            }
        }
    }
}
