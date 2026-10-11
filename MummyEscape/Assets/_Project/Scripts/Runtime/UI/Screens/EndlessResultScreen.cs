using System.Collections.Generic;
using MummyEscape.Core;
using MummyEscape.Game;
using MummyEscape.Monetization;
using UnityEngine;
using UnityEngine.UI;
using PlayMode = MummyEscape.Monetization.PlayMode;

namespace MummyEscape.UI.Screens
{
    /// <summary>
    /// End of an endless tomb, a Halloween tomb or a tower floor, in the look of the level recap: the outcome as the
    /// title, the run in stat tiles, what it earned in chips, the energy the next run spends, then on (or again).
    /// </summary>
    public sealed class EndlessResultScreen : UIScreen, IBackHandler
    {
        public override bool IsModal => true;
        const float Width = 940;
        static readonly string[] ModeNames = { "Facile", "Normal", "Extrême" };

        Text _title, _subtitle, _note;
        RectTransform _grid, _chips;
        readonly Text[] _values = new Text[4], _captions = new Text[4];
        EnergyBadge _energy;
        Button _main, _ranking;
        Difficulty _mode;
        /// <summary>The Halloween tomb to play on "Main", 0 on an endless ladder.</summary>
        int _halloweenNext;
        /// <summary>A tower floor's end: "Main" climbs on, or opens the tower for a new run.</summary>
        bool _tower, _towerClimbs;

        protected override void Build()
        {
            var panel = ModalKit.Frame(Root, Width, null, 22);
            _title = ModalKit.Header(panel, "", 76, out _subtitle);
            _grid = ModalKit.Tiles(panel, 2, Width);
            for (int i = 0; i < 4; i++) _values[i] = ModalKit.Tile(_grid, null, "", out _captions[i]);
            _chips = ModalKit.Chips(panel);
            _note = UIKit.Label(panel, "", 28, UIKit.Sand);
            UIKit.FitText(_note, 18);
            UIKit.Size(_note, 44);
            _energy = EnergyBadge.Create(panel, PlayMode.Endless, 80);
            _main = UIKit.Button(panel, "Tombeau suivant", Main, 40, ButtonStyle.Primary); // a label now: an empty one has no Text
            UIKit.Size(_main, 112);
            var actions = ModalKit.Actions(panel);
            _ranking = UIKit.IconAction(actions, UISprites.Podium, "Classement", Ranking, 96);
            UIKit.IconAction(actions, UISprites.Home, "Menu", Menu, 96);
        }

        /// <param name="tomb">Number of the tomb just played; <paramref name="progress"/> already counts the run.</param>
        public void Show(Difficulty mode, int tomb, LevelResult result, EndlessProgress progress)
        {
            _mode = mode;
            _halloweenNext = 0;
            _tower = false;
            bool more = progress.Cleared < GameController.EndlessTombCount(mode);
            Title(result.Won ? Loc.F("Tombeau {0} atteint !", progress.Reached) : Loc.T("La momie est tombée…"), result.Won);
            _subtitle.text = Loc.T("Sans fin") + " · " + Loc.T(ModeNames[(int)mode]);
            Tiles((UISprites.Infinity, tomb.ToString(), Loc.T(result.Won ? "Tombeau franchi" : "Tombeau")),
                  (UISprites.Clock, LevelResult.FormatTime(result.TimeMs), Loc.T("Temps")),
                  (UISprites.Retry, progress.Deaths.ToString(), Loc.T("Chutes")),
                  (UISprites.Clock, LevelResult.FormatTime(progress.TimeMs), Loc.T("Temps cumulé")));
            var chips = new List<(Sprite, string, Color)>();
            if (result.Won)
            {
                chips.Add((UIKit.Art.Scarab, "+" + BattlePass.EndlessScarabs, UIKit.Gold));
                chips.Add((UISprites.Crown, Loc.F("+{0} XP", BattlePass.EndlessXp), UIKit.Turquoise));
            }
            ModalKit.LayOutChips(_chips, chips, Width);
            Note(!result.Won ? Loc.T("Même tombeau, nouvel essai.")
               : more ? "" : Loc.T("Tous les tombeaux franchis : d'autres arrivent bientôt."));
            Energy(PlayMode.Endless);
            _ranking.transform.parent.parent.gameObject.SetActive(true);
            _main.gameObject.SetActive(more);
            UIKit.SetLabel(_main, result.Won ? "Tombeau suivant" : "Réessayer");
        }

        /// <summary>End of a Halloween tomb: on to the next one (the first again once the act is finished), or the same one.</summary>
        public void ShowHalloween(int tomb, LevelResult result, bool finished)
        {
            _halloweenNext = !result.Won ? tomb : App.Save.HalloweenDone ? tomb % Halloween.TombCount + 1 : App.Save.HalloweenNext;
            _tower = false;
            Title(finished ? Loc.T("Acte Halloween terminé !") : result.Won ? Loc.F("Tombeau {0} franchi !", tomb) : Loc.T("La momie est tombée…"), result.Won);
            _subtitle.text = Loc.T("Halloween");
            Tiles((UISprites.Map, tomb.ToString(), Loc.T("Tombeau")),
                  (UISprites.Clock, LevelResult.FormatTime(result.TimeMs), Loc.T("Temps")),
                  (UISprites.Flag, App.Save.Data.HalloweenCleared + " / " + Halloween.TombCount, Loc.T("Tombeaux franchis")),
                  (UISprites.Steps, result.Moves.ToString(), Loc.T("Coups")));
            var chips = new List<(Sprite, string, Color)>();
            if (finished)
            {
                chips.Add((UISprites.Check, Loc.T(Visual.HalloweenSkins.Resolve(Halloween.RewardSkin).Name), UIKit.Turquoise));
                chips.Add((UISprites.Palette, Loc.T("Thème d'accueil Halloween"), UIKit.Gold));
            }
            ModalKit.LayOutChips(_chips, chips, Width);
            Note(result.Won ? "" : Loc.T("Même tombeau, nouvel essai."));
            Energy(PlayMode.Solo);
            _ranking.transform.parent.parent.gameObject.SetActive(false);
            // The event may close while a tomb is played: that run counts, no new one starts.
            _main.gameObject.SetActive(Halloween.IsOpen(System.DateTime.UtcNow));
            UIKit.SetLabel(_main, result.Won ? "Tombeau suivant" : "Réessayer");
        }

        /// <summary>End of a tower floor: on to the next one, or the run is over (a death, or under 2 stars).</summary>
        public void ShowTower(int floor, LevelResult result, Services.TowerOutcome outcome, TowerRun run)
        {
            _tower = true;
            _towerClimbs = outcome.Continues;
            _halloweenNext = 0;
            Title(outcome.Continues ? Loc.F("Étage {0} franchi !", floor)
                : result.Won ? Loc.T("Moins de 2 étoiles : la course s'arrête")
                : Loc.T("La momie est tombée…"), outcome.Continues);
            _subtitle.text = Loc.T("Tour de défi");
            Tiles((UISprites.Tower, floor.ToString(), Loc.T("Étage")),
                  (UISprites.Clock, LevelResult.FormatTime(result.TimeMs), Loc.T("Temps")),
                  (UIKit.Art.Star, result.Won ? result.Stars + " / 3" : "–", Loc.T("Étoiles")), // noloc
                  (UISprites.Flag, run.Cleared.ToString(), Loc.T("Étages franchis")));
            var chips = new List<(Sprite, string, Color)>();
            if (outcome.RewardSkin != null) chips.Add((UISprites.Check, Loc.T(Visual.TowerSkins.Resolve(outcome.RewardSkin).Name), UIKit.Turquoise));
            else if (outcome.Record) chips.Add((UISprites.Podium, Loc.T("Nouveau record !"), UIKit.Turquoise));
            ModalKit.LayOutChips(_chips, chips, Width);
            Note(outcome.Continues && run.CurseDue ? Loc.T("Choisis un malus pour continuer.") : "");
            Energy(outcome.Continues ? (PlayMode?)null : PlayMode.Endless);
            _ranking.transform.parent.parent.gameObject.SetActive(true);
            _main.gameObject.SetActive(true);
            UIKit.SetLabel(_main, !outcome.Continues ? "Nouvelle course" : run.CurseDue ? "Choisir un malus" : "Étage suivant");
        }

        void Title(string text, bool good)
        {
            _title.text = text.ToUpperInvariant();
            ModalKit.Tint(_title, good);
        }

        /// <summary>Fills the tiles in order (two or four of them).</summary>
        void Tiles(params (Sprite icon, string value, string caption)[] tiles)
        {
            for (int i = 0; i < _values.Length; i++)
            {
                bool shown = i < tiles.Length;
                _values[i].transform.parent.parent.gameObject.SetActive(shown);
                if (shown) ModalKit.SetTile(_values[i], _captions[i], tiles[i].icon, tiles[i].value, tiles[i].caption);
            }
        }

        void Note(string text)
        {
            _note.text = text;
            _note.gameObject.SetActive(text.Length > 0);
        }

        /// <summary>The energy the next run spends (none in the middle of a tower run: its floors are paid).</summary>
        void Energy(PlayMode? mode)
        {
            _energy.gameObject.SetActive(mode.HasValue);
            if (mode.HasValue) _energy.Mode = mode.Value;
        }

        public void OnBack() => Menu();

        void Main()
        {
            if (_tower)
            {
                Router.Close(this);
                if (_towerClimbs && App.Save.ActiveTower?.CurseDue == false) _ = App.Game.StartTower();
                else // a new run, or the handicap to pick: both on the tower screen
                {
                    App.Game.Abandon();
                    Router.Reset<MainMenuScreen>();
                    Router.Open<TowerScreen>();
                }
                return;
            }
            if (_halloweenNext > 0)
            {
                int tomb = _halloweenNext;
                PlayGate.HalloweenRun(App, () =>
                {
                    Router.Close(this);
                    _ = App.Game.StartHalloween(tomb);
                });
                return;
            }
            var mode = _mode;
            PlayGate.EndlessRun(App, () =>
            {
                Router.Close(this);
                _ = App.Game.StartEndless(mode);
            });
        }

        void Ranking()
        {
            // Before it opens: it loads the board it shows.
            if (_tower) Router.Get<LeaderboardScreen>().FocusTower();
            else Router.Get<LeaderboardScreen>().FocusEndless(_mode);
            App.Game.Abandon();
            Router.Reset<MainMenuScreen>();
            Router.Open<LeaderboardScreen>();
        }

        void Menu()
        {
            App.Game.Abandon();
            Router.Reset<MainMenuScreen>();
        }
    }
}
