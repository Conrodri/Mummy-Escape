using System;
using MummyEscape.Pvp;
using MummyEscape.Services;
using UnityEngine;
using UnityEngine.UI;

namespace MummyEscape.UI.Screens
{
    /// <summary>
    /// The quests: today's daily quests (each with its progress bar and reward) and the weekly quest, with the time left
    /// before they reset. A finished quest lights up and a tap collects its reward. Progress is counted by the server.
    /// </summary>
    public sealed class QuestsScreen : UIScreen
    {
        public override NavTab Tab => NavTab.Home;

        RectTransform _list, _signIn;
        Text _note, _dailyLeft, _weeklyLeft;
        bool _busy;
        float _nextClock;
        const string ClaimedError = "CLAIMED", NotDoneError = "NOT_DONE"; // noloc

        protected override void Build()
        {
            UIKit.Backdrop(Root);
            Header("Quêtes");
            var body = Body(190, 40, 40);
            UIKit.Column(body, 16);

            // Signed out: what the quests need, and the way in.
            _signIn = UIKit.Card(body, 30, 14);
            var why = UIKit.Label(_signIn, "Connecte-toi avec Google Play Jeux pour les quêtes", 30, UIKit.Sand);
            UIKit.FitText(why, 18);
            UIKit.Size(why, 84);
            UIKit.Size(UIKit.Button(_signIn, "Se connecter avec Google Play Jeux", () => App.RequireOnline(() =>
            {
                Refresh();
                _ = App.Quests.RefreshAsync();
            }), 30, ButtonStyle.Primary), UIKit.ButtonHeight);

            _note = UIKit.Label(body, "", 30, UIKit.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.FitText(_note, 18);
            UIKit.Size(_note, 48);

            _list = UIKit.Scroll(body, out var scroll);
            UIKit.Size(scroll, -1, -1, -1, 1);
            _list.GetComponent<VerticalLayoutGroup>().spacing = 12;

            App.Quests.Changed += () => { if (this != null && isActiveAndEnabled && !_busy) Refresh(); };
        }

        public override void OnShow()
        {
            _note.text = "";
            Refresh();
            _ = App.Quests.RefreshAsync();
        }

        void Update()
        {
            if (Time.unscaledTime < _nextClock) return;
            _nextClock = Time.unscaledTime + 1f;
            SetClocks();
        }

        void Refresh()
        {
            var quests = App.Quests;
            _signIn.gameObject.SetActive(!quests.Available);
            var book = quests.Available ? quests.Book : null;

            UIKit.ClearChildren(_list);
            _dailyLeft = Heading("Quêtes du jour");
            foreach (var q in Quests.Today(Quests.DayOf(DateTime.UtcNow))) Row(q, book);
            UIKit.Size(UIKit.Rect("Gap", _list), 16); // noloc
            _weeklyLeft = Heading("Quêtes de la semaine");
            foreach (var q in Quests.Weeklies) Row(q, book);
            SetClocks();
        }

        /// <summary>A section title with its countdown on the right; returns the countdown.</summary>
        Text Heading(string title)
        {
            var row = UIKit.Row(_list, 52, 12);
            UIKit.Size(UIKit.SectionTitle(row.transform, title), -1, -1, 1);
            var left = UIKit.Label(row.transform, "", 24, UIKit.Dim, TextAnchor.MiddleRight);
            UIKit.FitText(left, 16);
            UIKit.Size(left, -1, 380);
            return left;
        }

        void SetClocks()
        {
            if (_dailyLeft == null) return;
            var now = DateTime.UtcNow;
            _dailyLeft.text = Loc.F("Nouvelles quêtes dans {0}", Left(now.Date.AddDays(1) - now));
            int toMonday = ((int)DayOfWeek.Monday - (int)now.DayOfWeek + 7) % 7;
            _weeklyLeft.text = Loc.F("Fin dans {0}", Left(now.Date.AddDays(toMonday == 0 ? 7 : toMonday) - now));
        }

        static string Left(TimeSpan t) =>
            t.Days > 0 ? Loc.F("{0} j {1} h", t.Days, t.Hours) : Loc.F("{0} h {1:00} min", t.Hours, t.Minutes);

        /// <summary>A quest: its text and progress bar, its reward, and a check once collected; a finished one is lit and claims on a tap.</summary>
        void Row(QuestDef q, QuestBook book)
        {
            int count = book != null ? Mathf.Min(Quests.CountOf(book, q), q.Target) : 0;
            bool claimed = book != null && Quests.IsClaimed(book, q);
            bool ready = !claimed && book != null && Quests.IsDone(book, q);

            Color fill = claimed ? ShopScreen.WornFill : ready ? new Color32(36, 112, 102, 255) : UIKit.SurfaceHi;
            var plate = UIKit.Plate(_list, fill, 22, ready ? (Color?)UIKit.Gold : q.Weekly ? new Color(1f, 0.84f, 0.25f, 0.55f) : UIKit.Rim, false, "Quest " + q.Id); // noloc
            UIKit.Size(plate, q.Weekly ? 150 : 132);
            var h = plate.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(24, 20, 12, 12);
            h.spacing = 18;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = false;

            var info = UIKit.Rect("Info", plate.transform); // noloc
            UIKit.Size(info, -1, -1, 1);
            UIKit.Column(info, 8, 0, TextAnchor.MiddleLeft);
            var text = UIKit.Label(info, "", 30, claimed ? UIKit.Dim : UIKit.Sand, TextAnchor.MiddleLeft, FontStyle.Bold);
            text.text = TextOf(q);
            UIKit.FitText(text, 18);
            UIKit.Size(text, 44);
            var line = UIKit.Row(info, 30, 12);
            var bar = UIFx.Bar(line.transform, 24, ready || claimed ? UIKit.Success : UIKit.Gold);
            UIKit.Size(bar.transform.parent, 24, -1, 1);
            bar.fillAmount = count / (float)q.Target;
            var progress = UIKit.Label(line.transform, "", 26, UIKit.Sand, TextAnchor.MiddleRight, FontStyle.Bold);
            progress.text = q.Kind == QuestKind.DailyDays ? Loc.F("{0}/{1} jours", count, q.Target) : count + "/" + q.Target; // noloc
            UIKit.Size(progress, 30, q.Weekly ? 140 : 80);

            if (claimed)
                UIKit.Size(UIKit.Image(plate.transform, UISprites.Check, UIKit.Success, false, "Check"), 52, 52); // noloc
            else if (ready)
            {
                var go = UIKit.Label(plate.transform, "Récupérer", 26, UIKit.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
                UIKit.FitText(go, 16);
                UIKit.Size(go, 52, 130);
                plate.raycastTarget = true;
                var btn = plate.gameObject.AddComponent<Button>();
                btn.targetGraphic = plate;
                btn.onClick.AddListener(() => { App.Audio.Play(Sfx.Click, 0f); Claim(q); });
                plate.gameObject.AddComponent<PressScale>().Amount = 0.97f;
                UIFx.Pulse(go, 0.06f, 1.2f); // the plate itself shrinks when pressed (PressScale)
            }

            // The reward always ends the row: "Récupérer" or the check sits on its left.
            var (icon, tint) = RewardLook(q.Reward);
            UIKit.Chip(plate.transform, icon, "+" + q.Amount, tint, 64);
        }

        async void Claim(QuestDef q)
        {
            if (_busy) return;
            _busy = true;
            string error = await App.Quests.ClaimAsync(q.Id);
            _busy = false;
            if (this == null) return;
            if (error == null)
            {
                App.Audio.Play(q.Reward == QuestRewardKind.Gold ? Sfx.Win : Sfx.Coin);
                _note.color = UIKit.Gold;
                _note.text = Loc.F("Récompense récupérée : {0}", RewardText(q));
            }
            else
            {
                _note.color = UIKit.Danger;
                _note.text = Loc.T(error == ClaimedError ? "Récompense déjà récupérée."
                                 : error == NotDoneError ? "Cette quête n'est pas encore terminée."
                                 : "La récompense n'a pas pu être récupérée. Réessaie plus tard.");
            }
            UIFx.PopIn(_note);
            if (isActiveAndEnabled) Refresh();
        }

        /// <summary>The quest's text with its target.</summary>
        public static string TextOf(QuestDef q) => Loc.F(q.Text, q.Target);

        public static (Sprite icon, Color tint) RewardLook(QuestRewardKind kind)
        {
            switch (kind)
            {
                case QuestRewardKind.PassXp: return (UISprites.Crown, UIKit.Turquoise);
                case QuestRewardKind.Seals: return (UISprites.Seal, UIKit.Turquoise);
                case QuestRewardKind.Gold: return (UIKit.Art.GoldScarab, TreasureScreen.GoldColor);
                default: return (UIKit.Art.Scarab, UIKit.Gold);
            }
        }

        public static string RewardText(QuestDef q)
        {
            switch (q.Reward)
            {
                case QuestRewardKind.PassXp: return Loc.F("+{0} XP", q.Amount);
                case QuestRewardKind.Seals: return Loc.F("+{0} sceaux", q.Amount);
                case QuestRewardKind.Gold: return Loc.F("+{0} scarabées dorés", q.Amount);
                default: return Loc.F("+{0} scarabées", q.Amount);
            }
        }

        /// <summary>Daily quests done today, out of how many (0 when the quests are not available).</summary>
        public static (int done, int total) DailyProgress(QuestService quests)
        {
            int done = 0;
            var book = quests.Available ? quests.Book : null;
            var today = Quests.Today(Quests.DayOf(DateTime.UtcNow));
            if (book != null) foreach (var q in today) if (Quests.IsDone(book, q)) done++;
            return (done, today.Count);
        }
    }

    /// <summary>
    /// Quest lines on a result screen: the progress of the quests the game just moved forward ("Quête terminée" once done,
    /// <see cref="QuestService.TakeProgress"/>),
    /// also those the server reports a moment later, while the screen is up. Takes no room when there are none.
    /// </summary>
    public sealed class QuestRecap : MonoBehaviour
    {
        LayoutElement _layout;
        int _count;
        bool _pending;

        /// <summary>Adds the (empty) lines area at this point of a result panel's column.</summary>
        public static QuestRecap Create(Transform panel)
        {
            var rt = UIKit.Rect("Quests", panel); // noloc
            UIKit.Column(rt, 8, 0);
            var recap = rt.gameObject.AddComponent<QuestRecap>();
            recap._layout = rt.gameObject.AddComponent<LayoutElement>();
            recap._layout.ignoreLayout = true;
            return recap;
        }

        /// <summary>The same lines as text, one per quest that moved forward, each after a line break ("" when none).</summary>
        public static string Text(App.GameApp app)
        {
            var moved = app.Quests?.TakeProgress();
            if (moved == null || moved.Count == 0) return "";
            var lines = "";
            foreach (var q in moved) lines += "\n" + LineOf(app.Quests.Book, q);
            return lines;
        }

        /// <summary>"Quête terminée : …" once done, otherwise the quest and its progress.</summary>
        static string LineOf(QuestBook book, QuestDef q)
        {
            int count = Quests.CountOf(book, q);
            return Quests.IsDone(book, q) ? Loc.F("Quête terminée : {0}", QuestsScreen.TextOf(q))
                : QuestsScreen.TextOf(q) + " · " + (q.Kind == QuestKind.DailyDays ? Loc.F("{0}/{1} jours", count, q.Target) : count + "/" + q.Target); // noloc
        }

        void OnEnable()
        {
            var quests = App.GameApp.I?.Quests;
            if (quests == null) return;
            quests.Changed += Pull;
            // Pulled on the next frame: the screen is enabled once while it is built, then hidden at once.
            _pending = true;
        }

        void Update()
        {
            if (!_pending) return;
            _pending = false;
            Pull();
        }

        void OnDisable()
        {
            var quests = App.GameApp.I?.Quests;
            if (quests != null) quests.Changed -= Pull;
            UIKit.ClearChildren(transform);
            _count = 0;
            if (_layout != null) _layout.ignoreLayout = true;
        }

        void Pull()
        {
            if (this == null || !isActiveAndEnabled) return;
            var quests = App.GameApp.I.Quests;
            var done = quests.TakeProgress();
            if (done == null) return;
            foreach (var q in done)
            {
                bool finished = Quests.IsDone(quests.Book, q);
                var label = UIKit.Chip(transform, finished ? UISprites.Check : null, LineOf(quests.Book, q), finished ? UIKit.Gold : UIKit.Sand, 56);
                label.horizontalOverflow = HorizontalWrapMode.Wrap;
                UIKit.FitText(label, 16);
                UIKit.Size(label, 40, 0, 1);
                UIFx.PopIn(label.transform.parent, 0.1f * _count);
                _count++;
            }
            if (done.Count == 0) return;
            _layout.ignoreLayout = false;
            if (done.Exists(q => Quests.IsDone(quests.Book, q))) App.GameApp.I.Audio.Play(Sfx.Coin, 0f);
        }
    }
}
