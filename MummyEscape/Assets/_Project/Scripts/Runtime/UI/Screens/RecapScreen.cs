using System.Collections;
using System.Collections.Generic;
using MummyEscape.Core;
using MummyEscape.Services;
using UnityEngine;
using UnityEngine.UI;

namespace MummyEscape.UI.Screens
{
    /// <summary>
    /// End of level: title, stars popping in one by one, stat tiles (moves vs the ideal path, time, interactions,
    /// life left as ankhs like in the HUD), rewards, then "next level" and the secondary actions.
    /// </summary>
    public sealed class RecapScreen : UIScreen, IBackHandler
    {
        public override bool IsModal => true;

        Text _title, _subtitle, _verdict;
        RectTransform _starsSlot, _verdictRow, _rewards, _lifeSlot;
        Text _moves, _movesCaption, _time, _interactions;
        Button _primary, _retry;
        LevelResult _result;
        bool _primaryIsNext;

        protected override void Build()
        {
            var shade = UIKit.Image(Root, UIKit.Art.White, UIKit.Shade, true);
            UIKit.Stretch(shade.rectTransform, -600, -600, -600, -600);

            var panel = UIKit.Panel(Root);
            var rt = panel.rectTransform;
            UIKit.FitInParent(UIKit.Place(rt, 0.5f, 0.5f, 940, 0));
            var col = UIKit.Column(panel.transform, 22, 44);
            col.padding = new RectOffset(44, 44, 40, 44);
            panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _title = UIKit.Title(panel.transform, "", 84);
            UIKit.FitText(_title, 48);
            UIKit.Size(_title, 110);
            _subtitle = UIKit.Label(panel.transform, "", 28, UIKit.Dim);
            UIKit.Size(_subtitle, 36);

            _starsSlot = UIKit.Rect("StarsSlot", panel.transform);
            UIKit.Size(_starsSlot, 130);

            _verdictRow = UIKit.Rect("Verdict", panel.transform);
            UIKit.Size(_verdictRow, 64);
            _verdict = UIKit.Label(_verdictRow, "", 30, UIKit.Sand);
            UIKit.Stretch(_verdict.rectTransform);

            // 2 × 2 stat tiles.
            var grid = UIKit.Rect("Stats", panel.transform);
            UIKit.Size(grid, 2 * 150 + 18);
            var g = grid.gameObject.AddComponent<GridLayoutGroup>();
            g.cellSize = new Vector2(417, 150);
            g.spacing = new Vector2(18, 18);
            g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            g.constraintCount = 2;
            _moves = Tile(grid, UISprites.Steps, "Coups", out _movesCaption);
            _time = Tile(grid, UISprites.Clock, "Temps", out _);
            _interactions = Tile(grid, UISprites.Hand, "Interactions", out _);
            Tile(grid, null, "Vie", out _);
            _lifeSlot = (RectTransform)grid.GetChild(3).Find("Values/Value");

            // Rewards: chips on as many lines as they need (a record and the expert's time rarely fit side by side).
            _rewards = UIKit.Rect("Rewards", panel.transform); // noloc
            UIKit.Column(_rewards, RewardGap, 0);

            _primary = UIKit.Button(panel.transform, "Niveau suivant", OnPrimary, 40, ButtonStyle.Primary);
            UIKit.Size(_primary, 112);

            var actions = UIKit.Row(panel.transform, 160, 40);
            _retry = UIKit.IconAction(actions.transform, UISprites.Retry, "Rejouer", Retry, 96);
            UIKit.IconAction(actions.transform, UISprites.Podium, "Classement", Leaderboard, 96);
            UIKit.IconAction(actions.transform, UISprites.Share, "Partager", Share, 96);
            UIKit.IconAction(actions.transform, UISprites.Home, "Menu", Menu, 96);
        }

        /// <summary>Stat tile: icon on the left, big value over its caption.</summary>
        static Text Tile(Transform parent, Sprite icon, string caption, out Text captionText)
        {
            var plate = UIKit.Plate(parent, UIKit.SurfaceHi, 24, null, false, "Tile " + caption); // noloc
            var h = plate.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(26, 20, 16, 16);
            h.spacing = 20;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = false;
            if (icon != null) UIKit.Size(UIKit.Image(plate.transform, icon, UIKit.Gold, false, "Icon"), 56, 56);
            var values = UIKit.Rect("Values", plate.transform);
            UIKit.Size(values, 118, -1, 1);
            UIKit.Column(values, 0, 0, TextAnchor.MiddleLeft);
            var value = UIKit.Label(values, "", 50, UIKit.Sand, TextAnchor.MiddleLeft, FontStyle.Bold);
            value.name = "Value"; // noloc
            UIKit.Size(value, 66);
            captionText = UIKit.Label(values, caption, 24, UIKit.Dim, TextAnchor.UpperLeft);
            UIKit.FitText(captionText, 18);
            UIKit.Size(captionText, 40);
            return value;
        }

        public void Show(LevelResult result, RecordOutcome outcome)
        {
            _result = result;
            bool walledIn = !result.Won && App.Game.Session != null && App.Game.Session.Defeat == DefeatCause.Trapped;
            _title.text = Loc.T(result.Won ? "ÉVADÉ !" : walledIn ? "EMMURÉE !" : "LA MOMIE A PÉRI");
            UIKit.TintTitle(_title, result.Won ? UIKit.Gold : UIKit.Danger);
            _subtitle.text = Loc.F("Niveau {0}", result.Level) + " · " + Loc.T(DifficultyTable.GetAct(result.Level.Act).Name);

            // Stars (won) pop in one after the other.
            UIKit.ClearChildren(_starsSlot);
            _starsSlot.gameObject.SetActive(result.Won);
            if (result.Won)
            {
                var stars = UIKit.Stars(_starsSlot, result.Stars, 112);
                UIKit.Stretch(stars);
                StartCoroutine(PopStars(stars, result.Stars));
            }

            // Verdict: perfect path badge, distance to the ideal path, or why the mummy fell.
            foreach (Transform c in _verdictRow) if (c != _verdict.transform) Destroy(c.gameObject);
            _verdict.gameObject.SetActive(true);
            if (result.Won && result.OverPar == 0)
            {
                _verdict.gameObject.SetActive(false);
                var badge = UIKit.Chip(_verdictRow, UISprites.Check, Loc.T("Chemin parfait !"), UIKit.Gold, 60);
                UIKit.Place((RectTransform)badge.transform.parent, 0.5f, 0.5f, 0, 60);
                badge.transform.parent.GetComponent<Image>().color = new Color(0.91f, 0.76f, 0.35f, 0.16f);
            }
            else if (result.Won)
            {
                // A tier name, then what the next star asks for: humans rarely walk the ideal path, so no "+40 moves" verdict.
                string tier = Loc.T(result.Stars >= 3 ? "Excellent !" : result.Stars == 2 ? "Bien joué !" : "Évadée de justesse !");
                string goal = result.Stars >= 3 ? Loc.F("chemin idéal : {0} coups", result.Par)
                                                : Loc.F("{0} coups ou moins pour l'étoile suivante", result.MaxMovesFor(result.Stars + 1));
                _verdict.text = $"<b>{tier}</b> · {goal}";
            }
            else
                _verdict.text = walledIn ? Loc.T("Courants, dalles effondrées et barrières ne pardonnent pas.")
                                         : Loc.T("Les pièges ont eu raison de toi.");
            UIKit.Size(_verdictRow, result.Won ? 64 : 84);

            _moves.text = result.Moves.ToString();
            _movesCaption.text = result.Won ? Loc.F("Coups · idéal {0}", result.Par) : Loc.T("Coups");
            _time.text = LevelResult.FormatTime(result.TimeMs);
            _interactions.text = result.Interactions.ToString();
            UIKit.ClearChildren(_lifeSlot);
            var lifeText = _lifeSlot.GetComponent<Text>();
            lifeText.text = "";
            var ankhs = UIKit.Ankhs(_lifeSlot, result.HpLeft, result.MaxHp, 56);
            ankhs.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            UIKit.Stretch(ankhs);

            // Rewards.
            var rewards = new List<(Sprite icon, string text, Color tint)>();
            if (outcome.NewBest && outcome.PreviousBestOverPar >= 0)
                rewards.Add((UISprites.Podium, Loc.F("Nouveau record ! ({0} → {1})", LevelResult.FormatScore(outcome.PreviousBestOverPar, outcome.PreviousBestTimeMs), LevelResult.FormatScore(result.OverPar, result.TimeMs)), UIKit.Turquoise));
            else if (outcome.NewBest)
                rewards.Add((UISprites.Check, Loc.T("Premier passage !"), UIKit.Turquoise));
            if (outcome.CoinsEarned > 0)
                rewards.Add((UIKit.Art.Scarab, "+" + outcome.CoinsEarned, UIKit.Gold));
            if (result.Won && result.TargetMs > 0)
            {
                // The expert mummy (a simulated player) ran this same maze: its time is the one to beat.
                bool beaten = result.TimeMs <= result.TargetMs;
                rewards.Add((beaten ? UISprites.Check : UISprites.Podium,
                    beaten ? Loc.F("Momie experte battue ! ({0})", LevelResult.FormatTime(result.TargetMs))
                           : Loc.F("Momie experte : {0}", LevelResult.FormatTime(result.TargetMs)),
                    beaten ? UIKit.Turquoise : UIKit.Dim));
            }
            LayOutRewards(rewards);

            var next = Progression.Next(result.Level);
            _primaryIsNext = result.Won && next.HasValue;
            UIKit.SetLabel(_primary, _primaryIsNext ? "Niveau suivant" : result.Won ? "Rejouer" : "Réessayer");
            _retry.transform.parent.parent.gameObject.SetActive(_primaryIsNext); // button → slot → captioned column
            // Get the next tombs generating while the player reads the recap.
            App.Game.Prefetch(result.Level);
            if (_primaryIsNext) App.Game.Prefetch(next.Value);
        }

        const float RewardHeight = 60;
        const float RewardGap = 12;
        const float RewardSpacing = 16;
        /// <summary>Inside width of the panel (940 wide, 44 of padding on each side).</summary>
        const float RewardWidth = 940 - 2 * 44;

        /// <summary>
        /// Fills the rewards line after line: a chip goes on the current line while it fits, else starts a new one; a chip
        /// wider than the panel gets the whole line and its text shrinks.
        /// </summary>
        void LayOutRewards(List<(Sprite icon, string text, Color tint)> rewards)
        {
            UIKit.ClearChildren(_rewards);
            _rewards.gameObject.SetActive(rewards.Count > 0);
            HorizontalLayoutGroup line = null;
            float used = 0;
            int lines = 0;
            foreach (var (icon, text, tint) in rewards)
            {
                if (line == null) { line = UIKit.Row(_rewards, RewardHeight, RewardSpacing); lines++; }
                var label = UIKit.Chip(line.transform, icon, text, tint, RewardHeight);
                var chip = (RectTransform)label.transform.parent;
                LayoutRebuilder.ForceRebuildLayoutImmediate(chip);
                float w = LayoutUtility.GetPreferredWidth(chip);
                if (w > RewardWidth)
                {
                    label.horizontalOverflow = HorizontalWrapMode.Wrap;
                    UIKit.FitText(label, 16);
                    UIKit.Size(label, RewardHeight - 16, 0, 1);
                    UIKit.Size(chip, RewardHeight, RewardWidth);
                    w = RewardWidth;
                }
                if (used > 0 && used + RewardSpacing + w > RewardWidth)
                {
                    line = UIKit.Row(_rewards, RewardHeight, RewardSpacing);
                    lines++;
                    chip.SetParent(line.transform, false);
                    used = w;
                }
                else used += (used > 0 ? RewardSpacing : 0) + w;
            }
            UIKit.Size(_rewards, lines == 0 ? 0 : lines * RewardHeight + (lines - 1) * RewardGap);
        }

        IEnumerator PopStars(RectTransform row, int count)
        {
            for (int i = 0; i < row.childCount; i++) row.GetChild(i).localScale = i < count ? Vector3.zero : Vector3.one;
            yield return new WaitForSecondsRealtime(0.25f);
            for (int i = 0; i < count && i < row.childCount; i++)
            {
                var star = row.GetChild(i);
                App.Audio.Play(Sfx.Coin, 0f);
                for (float t = 0; t < 0.28f; t += Time.unscaledDeltaTime)
                {
                    float k = t / 0.28f;
                    // Overshoot then settle.
                    float s = k < 0.6f ? Mathf.Lerp(0f, 1.25f, k / 0.6f) : Mathf.Lerp(1.25f, 1f, (k - 0.6f) / 0.4f);
                    star.localScale = Vector3.one * s;
                    yield return null;
                }
                star.localScale = Vector3.one;
            }
        }

        public void OnBack() => Menu();

        void OnPrimary()
        {
            if (_primaryIsNext) Next(); else Retry();
        }

        void Share() => App.Share.ShareText(_result.ShareText(ShareService.GameUrl));

        void Next()
        {
            var next = Progression.Next(_result.Level);
            if (!next.HasValue) return;
            PlayGate.Solo(App, next.Value, () =>
            {
                Router.Close(this);
                _ = App.Game.StartLevel(next.Value);
            });
        }

        void Retry()
        {
            PlayGate.Solo(App, _result.Level, () =>
            {
                Router.Close(this);
                App.Game.Restart();
            });
        }

        void Leaderboard()
        {
            var lb = Router.Get<LeaderboardScreen>();
            lb.Focus(_result.Level);
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
