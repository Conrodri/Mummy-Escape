using MummyEscape.Core;
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
    /// End of a duel: the run is sent, the server replays it and answers with the verdict (win, draw, loss), the new
    /// Elo and the seals earned. When nobody was waiting, the run is kept as the ghost of the next challenger.
    /// </summary>
    public sealed class PvpResultScreen : UIScreen, IBackHandler
    {
        public override bool IsModal => true;

        Text _title, _subtitle, _me, _rival, _elo, _note;
        RectTransform _rewards;
        Button _again, _menu, _retry, _watch;
        DuelRecord _record;
        PvpMatch _match;
        RunSubmission _run;
        bool _sending;
        BattleKind _lastBattleKind;

        protected override void Build()
        {
            var shade = UIKit.Image(Root, UIKit.Art.White, UIKit.Shade, true);
            UIKit.Stretch(shade.rectTransform, -600, -600, -600, -600);

            var panel = UIKit.Panel(Root);
            UIKit.FitInParent(UIKit.Place(panel.rectTransform, 0.5f, 0.5f, 940, 0));
            var col = UIKit.Column(panel.transform, 20, 44);
            col.padding = new RectOffset(44, 44, 40, 44);
            panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _title = UIKit.Title(panel.transform, "", 84);
            UIKit.FitText(_title, 48);
            UIKit.Size(_title, 110);
            _subtitle = UIKit.Label(panel.transform, "", 28, UIKit.Dim);
            UIKit.FitText(_subtitle, 20);
            UIKit.Size(_subtitle, 40);

            _me = Line(panel.transform, UIKit.Gold);
            _rival = Line(panel.transform, UIKit.Turquoise);

            _elo = UIKit.Label(panel.transform, "", 40, UIKit.Sand, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Size(_elo, 64);
            _rewards = UIKit.Row(panel.transform, 64, 16).GetComponent<RectTransform>();
            _note = UIKit.Label(panel.transform, "", 26, UIKit.Dim);
            UIKit.FitText(_note, 18);
            UIKit.Size(_note, 76);

            _retry = UIKit.Button(panel.transform, "Renvoyer la course", Send, 36, ButtonStyle.Primary);
            UIKit.Size(_retry, 100);
            _again = UIKit.Button(panel.transform, "Nouveau duel", Again, 40, ButtonStyle.Primary);
            UIKit.Size(_again, 112);
            _watch = UIKit.Button(panel.transform, "Revoir le duel", Watch, 34);
            UIKit.Size(_watch, 92);
            _menu = UIKit.Button(panel.transform, "Retour aux duels", Menu, UIKit.TextSize, ButtonStyle.Ghost);
            UIKit.Size(_menu, 76);
        }

        /// <summary>One side of the duel: a coloured plate with who and how far.</summary>
        static Text Line(Transform parent, Color accent)
        {
            var plate = UIKit.Plate(parent, UIKit.SurfaceHi, 24, new Color(accent.r, accent.g, accent.b, 0.45f));
            UIKit.Size(plate, 96);
            var t = UIKit.Label(plate.transform, "", 32, UIKit.Sand, TextAnchor.MiddleLeft);
            UIKit.FitText(t, 20);
            UIKit.Stretch(t.rectTransform, 30, 0, 30, 0);
            return t;
        }

        /// <summary>A round of a team battle (2v2 or guild war) rather than a duel.</summary>
        bool IsRound => _match?.Duel.BattleId != null;

        public void Show(PvpMatch match, RunSubmission run)
        {
            _match = match;
            _run = run;
            _record = null;
            UIKit.SetLabel(_again, IsRound ? "Retour au combat" : "Nouveau duel");
            string rival = match.HasGhost ? match.Ghost.PlayerName : null;
            _subtitle.text = rival != null ? Loc.F("contre {0} · Elo {1}", rival, match.Ghost.Elo) : Loc.T("Premier sur ce tombeau");
            _me.text = "<b>" + Loc.T("Toi") + "</b>  ·  " + OutcomeText(run.Outcome, run.TimeMs, run.Progress);
            _rival.transform.parent.gameObject.SetActive(match.HasGhost);
            if (match.IsLive)
            {
                // What the player saw of the rival when his own run stopped (the server has the last word).
                var (outcome, time, progress) = match.RivalSoFar();
                _rival.text = "<b>" + rival + "</b>  ·  " + (match.RivalQuit ? Loc.T("a quitté le duel")
                            : outcome == RunOutcome.TimedOut ? Loc.F("en course, {0} % du chemin", Mathf.RoundToInt(progress * 100f))
                            : OutcomeText(outcome, time, progress));
                if (run.Outcome == RunOutcome.TimedOut && Mathf.RoundToInt(run.Progress * 100f) < 100 && run.Inputs.Count > 0
                    && RunActions.MsOf(run.Inputs[run.Inputs.Count - 1].Tick) < PvpConfig.TimeLimitMs - 1000)
                    _me.text = "<b>" + Loc.T("Toi") + "</b>  ·  " + Loc.F("arrêté à {0} % du chemin", Mathf.RoundToInt(run.Progress * 100f));
            }
            else if (match.HasGhost)
                _rival.text = "<b>" + rival + "</b>  ·  " + OutcomeText(match.Ghost.Outcome, match.Ghost.TimeMs, match.Ghost.Progress);
            Send();
        }

        static string OutcomeText(RunOutcome outcome, int timeMs, float progress)
        {
            int pct = Mathf.RoundToInt(progress * 100f);
            switch (outcome)
            {
                case RunOutcome.Finished: return Loc.F("sorti en {0}", LevelResult.FormatTime(timeMs));
                case RunOutcome.Died: return Loc.F("mort à {0} % du chemin ({1})", pct, LevelResult.FormatTime(timeMs));
                case RunOutcome.TimedOut: return Loc.F("temps écoulé, {0} % du chemin", pct);
                default: return Loc.T("abandon");
            }
        }

        async void Send()
        {
            if (_sending) return;
            _sending = true;
            _title.text = Loc.T("Verdict des dieux…");
            UIKit.TintTitle(_title, UIKit.Gold);
            _elo.text = "";
            _note.text = Loc.T("Le serveur rejoue ta course.");
            UIKit.ClearChildren(_rewards);
            _rewards.gameObject.SetActive(false);
            SetButtons(false, false);

            var pvp = App.Pvp;
            SubmitRunResponse r;
            if (_match.IsLive && pvp != null)
            {
                // Live: both runs are judged together; the rival's may take a moment (a minute at most).
                r = await pvp.SubmitLiveDuelAsync(_match.MatchId, _run, App.Online.PlayerName);
                for (int attempt = 0; attempt < 30 && this != null && r != null && r.Error == null && !r.Resolved; attempt++)
                {
                    _note.text = Loc.F("En attente de la course de {0}…", _match.Ghost?.PlayerName ?? "?");
                    await System.Threading.Tasks.Task.Delay(2500);
                    if (this == null) return;
                    var next = await pvp.GetLiveDuelResultAsync(_match.MatchId);
                    if (next != null) r = next;
                }
            }
            else r = pvp == null ? null : await pvp.SubmitRunAsync(_run, App.Online.PlayerName);
            if (this == null) return;
            _sending = false;
            if (r?.Battle != null) _lastBattleKind = r.Battle.Kind;

            if (r == null || r.Error == PvpServiceFactory.NetworkError)
            {
                _title.text = Loc.T("Envoi impossible");
                UIKit.TintTitle(_title, UIKit.Danger);
                _note.text = Loc.T("Vérifie ta connexion : le duel reste ouvert 10 minutes.");
                SetButtons(true, true);
                return;
            }
            if (r.Error != null)
            {
                _title.text = Loc.T("DÉFAITE");
                UIKit.TintTitle(_title, UIKit.Danger);
                _note.text = PvpScreen.ErrorText(r.Error);
                SetButtons(true, false);
                return;
            }

            if (IsRound)
            {
                ShowRound(r);
                SetButtons(true, false);
                return;
            }
            if (!r.Resolved)
            {
                _title.text = Loc.T("COURSE ENREGISTRÉE");
                UIKit.TintTitle(_title, UIKit.Turquoise);
                _note.text = _match.IsLive ? Loc.T("Le verdict attend la course de ton adversaire : ton Elo bougera dans une minute.")
                           : Loc.T("Personne n'attendait sur ce tombeau : ta course devient le fantôme du prochain challenger. Ton Elo bougera à ce moment-là.");
            }
            else
            {
                bool win = r.Result == DuelResult.Win, draw = r.Result == DuelResult.Draw;
                _title.text = Loc.T(win ? "VICTOIRE !" : draw ? "MATCH NUL" : "DÉFAITE");
                UIKit.TintTitle(_title, win ? UIKit.Gold : draw ? UIKit.Sand : UIKit.Danger);
                int delta = r.EloAfter - r.EloBefore;
                string sign = delta > 0 ? "+" : "";
                string color = delta > 0 ? "#40E0D0" : delta < 0 ? "#D65440" : "#9C8B70";
                _elo.text = $"Elo {r.EloBefore} → {r.EloAfter}  <color={color}>({sign}{delta})</color>";
                _note.text = Loc.F("Ligue {0}", Loc.T(PvpSkins.LeagueName(r.League)));
                App.Audio.Play(win ? Sfx.Win : draw ? Sfx.Coin : Sfx.Death);
                if (win) App.Save.AddPassXp(Monetization.BattlePass.WinBonusXp);
            }
            if (r.SealsGained > 0)
            {
                UIKit.Chip(_rewards, UISprites.Seal, "+" + r.SealsGained, UIKit.Turquoise, 60);
                _rewards.gameObject.SetActive(true);
            }
            Keep(r);
            App.UpdatePvpWallet(r.Seals, null);
            SetButtons(true, false);
        }

        /// <summary>The round's verdict, the battle's score and, once it is over, the team's result and Elo.</summary>
        void ShowRound(SubmitRunResponse r)
        {
            var b = r.Battle;
            bool duo = b?.Kind == BattleKind.Duo;
            if (!r.Resolved)
            {
                _title.text = Loc.T("MANCHE COURUE");
                UIKit.TintTitle(_title, UIKit.Turquoise);
                _note.text = Loc.T("Ton vis-à-vis n'a pas encore couru cette manche : elle se décidera quand il l'aura fait.");
            }
            else
            {
                bool win = r.Result == DuelResult.Win, draw = r.Result == DuelResult.Draw;
                _title.text = Loc.T(win ? "MANCHE GAGNÉE !" : draw ? "MANCHE NULLE" : "MANCHE PERDUE");
                UIKit.TintTitle(_title, win ? UIKit.Gold : draw ? UIKit.Sand : UIKit.Danger);
                App.Audio.Play(win ? Sfx.Win : draw ? Sfx.Coin : Sfx.Death);
                _note.text = "";
            }
            if (b == null) return;
            bool mineA = b.A.TeamId == b.ViewerTeam;
            var (wa, wb) = TeamLogic.Score(b);
            int mine = mineA ? wa : wb, theirs = mineA ? wb : wa;
            if (b.Finished)
            {
                var result = mineA ? b.Result : DuelResolver.Invert(b.Result);
                int delta = r.EloAfter - r.EloBefore;
                string color = delta > 0 ? "#40E0D0" : delta < 0 ? "#D65440" : "#9C8B70";
                string verdict = result == DuelResult.Win ? Loc.T(duo ? "Ton duo gagne le combat" : "Ta guilde gagne la guerre")
                               : result == DuelResult.Loss ? Loc.T(duo ? "Ton duo perd le combat" : "Ta guilde perd la guerre")
                               : Loc.T("Égalité");
                _elo.text = $"{mine} – {theirs}  ·  {verdict}"; // noloc
                _note.text = (duo ? Loc.T("Elo 2v2") : Loc.T("Elo de guerre")) + $" {r.EloBefore} → {r.EloAfter}  <color={color}>({(delta > 0 ? "+" : "")}{delta})</color>"; // noloc
            }
            else
            {
                _elo.text = Loc.F("Score {0} – {1}", mine, theirs);
                if (_note.text.Length == 0) _note.text = Loc.T("Le combat continue : tes coéquipiers courent les manches suivantes.");
            }
        }

        /// <summary>Saves the duel on the phone right away (the server's copy replaces it on the next visit to the duels).</summary>
        void Keep(SubmitRunResponse r)
        {
            // A forfeit before anyone raced the tomb leaves nothing to watch.
            if (IsRound) return; // rounds are watched from their battle
            if (_run.Outcome == RunOutcome.Abandoned && !_match.HasGhost) return;
            _record = new DuelRecord
            {
                MatchId = _run.MatchId, Seed = _match.Seed, GeneratorVersion = DifficultyTable.GeneratorVersion,
                PlayedAtUnixMs = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Resolved = r.Resolved, Result = r.Result, EloBefore = r.EloBefore, EloAfter = r.EloAfter,
                Me = new DuelRun
                {
                    PlayerName = App.Online.PlayerName, Look = _run.Look, Elo = r.EloBefore, Outcome = _run.Outcome,
                    TimeMs = _run.TimeMs, Progress = _run.Progress, Inputs = _run.Inputs,
                },
                Rival = DuelRun.Of(_match.Ghost),
            };
            Online.ReplayStore.Add(_record);
        }

        void Watch()
        {
            var record = _record;
            if (record == null) return;
            App.Game.Abandon();
            Router.Reset<MainMenuScreen>();
            Router.Open<PvpScreen>();
            Router.Open<ReplayScreen>().Show(record);
        }

        void SetButtons(bool done, bool canRetry)
        {
            _watch.gameObject.SetActive(done && !canRetry && _record != null);
            _retry.gameObject.SetActive(canRetry);
            _again.gameObject.SetActive(!canRetry);
            _again.interactable = done;
            _menu.interactable = done;
        }

        public void OnBack()
        {
            if (!_sending) Menu();
        }

        async void Again()
        {
            if (IsRound)
            {
                App.Game.Abandon();
                Router.Reset<MainMenuScreen>();
                Router.Open<PvpScreen>();
                TeamView.OpenBattleHome(Router, _lastBattleKind);
                return;
            }
            if (!PlayGate.Ensure(App, Monetization.PlayMode.Duel, Again)) return;
            // The menu button stays: it cancels the search.
            _again.interactable = false;
            _note.text = Loc.T("Recherche d'un adversaire…");
            var search = _search = new System.Threading.CancellationTokenSource();
            var matchmaker = DuelMatchmakerFactory.For(App.Pvp);
            var start = matchmaker == null ? null
                      : await PvpScreen.FindLiveAsync(App, matchmaker, text => { if (this != null && _search == search) _note.text = text; }, search.Token);
            if (this == null || search.IsCancellationRequested) { start?.Link?.Dispose(); return; }
            _search = null;
            if (start == null)
            {
                _again.interactable = _menu.interactable = true;
                return;
            }
            Router.Close(this);
            PvpScreen.VersusLive(Router, start);
        }

        System.Threading.CancellationTokenSource _search;

        void Menu()
        {
            _search?.Cancel();
            _search = null;
            App.Game.Abandon();
            Router.Reset<MainMenuScreen>();
            Router.Open<PvpScreen>();
        }
    }
}
