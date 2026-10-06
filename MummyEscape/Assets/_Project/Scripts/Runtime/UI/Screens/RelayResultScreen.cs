using System.Threading.Tasks;
using MummyEscape.Core;
using MummyEscape.Game;
using MummyEscape.Online;
using MummyEscape.Pvp;
using MummyEscape.Services;
using UnityEngine;
using UnityEngine.UI;

namespace MummyEscape.UI.Screens
{
    /// <summary>
    /// End of a 2v2: what each duo did, the verdict seen on the phone, then the server's (it replays both relays) with the
    /// duo's new 2v2 Elo. When the teammate left, the player may quit or quit and report him.
    /// </summary>
    public sealed class RelayResultScreen : UIScreen, IBackHandler
    {
        public override bool IsModal => true;
        const string LimitError = "LIMIT"; // noloc

        Text _title, _mine, _rival, _elo, _note;
        Button _again, _menu, _report, _watch;
        RelayOutcome _outcome;
        RelayRecord _record;
        bool _sending;

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
            _mine = Line(panel.transform, UIKit.Gold);
            _rival = Line(panel.transform, UIKit.Turquoise);
            _elo = UIKit.Label(panel.transform, "", 40, UIKit.Sand, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Size(_elo, 64);
            _note = UIKit.Label(panel.transform, "", 26, UIKit.Dim);
            UIKit.FitText(_note, 18);
            UIKit.Size(_note, 90);

            _again = UIKit.Button(panel.transform, "Nouveau match", Again, 40, ButtonStyle.Primary);
            UIKit.Size(_again, 112);
            _watch = UIKit.Button(panel.transform, "Revoir le match", Watch, 34);
            UIKit.Size(_watch, 92);
            _report = UIKit.Button(panel.transform, "Quitter et signaler", Report, 34);
            UIKit.Size(_report, 92);
            _menu = UIKit.Button(panel.transform, "Retour au 2v2", Menu, UIKit.TextSize, ButtonStyle.Ghost);
            UIKit.Size(_menu, 76);
        }

        static Text Line(Transform parent, Color accent)
        {
            var plate = UIKit.Plate(parent, UIKit.SurfaceHi, 24, new Color(accent.r, accent.g, accent.b, 0.45f));
            UIKit.Size(plate, 110);
            var t = UIKit.Label(plate.transform, "", 30, UIKit.Sand, TextAnchor.MiddleLeft);
            UIKit.FitText(t, 18);
            UIKit.Stretch(t.rectTransform, 30, 0, 30, 0);
            return t;
        }

        public void Show(RelayOutcome outcome)
        {
            _outcome = outcome;
            var match = outcome.Match;
            var mySide = match.SideOf(outcome.Me);
            var rivalSide = match.OtherSide(outcome.Me);
            int segments = App.Game.RelayMap?.Segments ?? 4;
            _mine.text = "<b>" + mySide.Name + "</b>\n" + Describe(outcome.Mine, segments, outcome.PartnerQuit != null || outcome.Quitters.Contains(outcome.Me));
            _rival.text = "<b>" + rivalSide.Name + "</b>\n" + Describe(outcome.Rival, segments, outcome.Quitters.Exists(rivalSide.Has));
            ShowVerdict(outcome.Result);
            _elo.text = "";
            bool mateLeft = outcome.PartnerQuit != null;
            string mateName = mateLeft ? mySide.Runner(outcome.PartnerQuit)?.Name : null;
            _report.gameObject.SetActive(mateLeft && !mySide.Runner(outcome.PartnerQuit).Bot);
            UIKit.SetLabel(_menu, mateLeft ? "Quitter" : "Retour au 2v2");
            _again.gameObject.SetActive(!mateLeft);
            _note.text = mateLeft ? Loc.F("{0} a quitté la partie : ton duo perd le match.", mateName) : "";
            _record = RelayReplayStore.Of(match, outcome.Me, outcome.Starter, outcome.Inputs, outcome.RivalInputs, outcome.Result);
            RelayReplayStore.Put(_record);
            _ = Send();
        }

        static string Describe(RelaySummary s, int segments, bool quit)
        {
            if (s.Finished) return Loc.F("sortis en {0}", LevelResult.FormatTime(s.TimeMs));
            if (quit) return Loc.F("abandon après {0} / {1} étapes", s.Segments, segments);
            if (s.Lost) return Loc.F("momie morte à l'étape {0} / {1}", s.Segments + 1, segments);
            return Loc.F("{0} / {1} étapes", s.Segments, segments);
        }

        void ShowVerdict(DuelResult result)
        {
            bool win = result == DuelResult.Win, draw = result == DuelResult.Draw;
            _title.text = Loc.T(win ? "VICTOIRE !" : draw ? "MATCH NUL" : "DÉFAITE");
            UIKit.TintTitle(_title, win ? UIKit.Gold : draw ? UIKit.Sand : UIKit.Danger);
        }

        async Task Send()
        {
            _sending = true;
            var pvp = App.Pvp;
            var o = _outcome;
            if (_note.text == "") _note.text = Loc.T("Le serveur rejoue les deux relais…");
            var r = pvp == null ? null : await pvp.SubmitRelayAsync(o.Match.Id, o.Starter, o.Inputs, o.Quitters);
            // The other duo has a moment to send its relay: ask again a few times.
            for (int k = 0; k < 8 && r != null && r.Error == null && r.Pending; k++)
            {
                await Task.Delay(2500);
                if (this == null || _outcome != o) return;
                r = await pvp.GetRelayResultAsync(o.Match.Id);
            }
            // The replay takes the server's copy: both relays as it replayed them, and its verdict.
            var record = _record;
            if (record != null && r != null && r.Error == null && !r.Pending && r.Match?.A != null && r.Match.B != null)
            {
                record.Match = r.Match;
                record.Resolved = true;
                record.Result = r.Result;
                record.EloDelta = r.EloDelta;
                record.NewElo = r.NewElo;
                RelayReplayStore.Put(record);
            }
            if (this == null || _outcome != o) return;
            _sending = false;
            if (r == null || r.Error != null)
            {
                _elo.text = "";
                if (_outcome.PartnerQuit == null) _note.text = Loc.T("Résultat non confirmé : vérifie ta connexion.");
                return;
            }
            if (r.Pending)
            {
                if (_outcome.PartnerQuit == null) _note.text = Loc.T("Le duo adverse n'a pas encore envoyé son relais : ton Elo bougera dans une minute.");
                return;
            }
            ShowVerdict(r.Result);
            if (r.Result == DuelResult.Win) App.Save.AddPassXp(Monetization.BattlePass.WinBonusXp);
            string sign = r.EloDelta > 0 ? "+" : "";
            string color = r.EloDelta > 0 ? "#40E0D0" : r.EloDelta < 0 ? "#D65440" : "#9C8B70"; // noloc
            _elo.text = Loc.F("Elo 2v2 {0}", r.NewElo) + $"  <color={color}>({sign}{r.EloDelta})</color>";
            if (_outcome.PartnerQuit == null) _note.text = r.Result == o.Result ? "" : Loc.T("Verdict corrigé par le serveur après vérification des relais.");
            App.Audio.Play(r.Result == DuelResult.Win ? Sfx.Win : r.Result == DuelResult.Draw ? Sfx.Coin : Sfx.Death);
        }

        async void Report()
        {
            var o = _outcome;
            _report.interactable = false;
            var r = App.Pvp == null ? null : await App.Pvp.ReportRelayQuitAsync(o.Match.Id, o.PartnerQuit);
            if (this == null) return;
            _note.text = r?.Ok == true ? Loc.T("Signalement envoyé. Merci !") : r?.Error == LimitError ? Loc.T("Trop de signalements aujourd'hui.") : Loc.T("Signalement impossible.");
            await Task.Delay(900);
            if (this != null) Menu();
        }

        void Watch()
        {
            var record = _record;
            if (record == null) return;
            Menu();
            Router.Open<ReplayScreen>().ShowRelay(record);
        }

        void Again()
        {
            Menu();
            (Router.Current as DuoScreen)?.SearchAgain();
        }

        void Menu()
        {
            App.Game.LeaveRelay();
            Router.Reset<MainMenuScreen>();
            Router.Open<PvpScreen>();
            Router.Open<DuoScreen>();
        }

        public void OnBack()
        {
            if (!_sending) Menu();
        }
    }
}
