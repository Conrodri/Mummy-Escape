using System;
using System.Threading.Tasks;
using MummyEscape.Core;
using MummyEscape.Pvp;
using MummyEscape.Services;
using UnityEngine;
using UnityEngine.UI;

namespace MummyEscape.UI.Screens
{
    /// <summary>
    /// Before a PvP match: three tombs of the league (easy, medium, hard). Each side bans one within 10 s (in 2v2 each player
    /// votes for his duo's ban), no choice in time and chance bans. The rival's ban shows once the player has banned; then
    /// the map nobody banned is stamped and the match goes on. The server holds the draft: this screen votes and reads it.
    /// The tombs themselves are not shown: their layout stays for the timed preview.
    /// </summary>
    public sealed class DraftScreen : UIScreen, IBackHandler
    {
        public override bool IsModal => true;

        const float PollSeconds = 1f;
        const float ShowPickSeconds = 1.8f;
        /// <summary>The server never closed the draft (connection lost): the match is given up.</summary>
        const float GiveUpSeconds = (PvpDraft.WindowMs + 15_000) / 1000f;
        const int NotVoted = -2;

        static readonly string[] Levels = { "Facile", "Moyen", "Difficile" };
        static readonly Color CardColor = new Color32(52, 41, 31, 255);
        static readonly Color BannedColor = new Color32(30, 22, 17, 255);

        sealed class Card
        {
            public Image Plate;
            public Button Button;
            public Text Details, Stamp, Note;
        }

        readonly Card[] _cards = new Card[PvpDraft.MapCount];
        Text _clock, _status;
        MapDraft _draft;
        string _me;
        int _team;
        bool _duo;
        Func<string, string> _nameOf;
        Func<int, Task<MapDraft>> _ban;
        Func<Task<MapDraft>> _read;
        Action<bool> _then;
        float _t, _nextPoll, _shownAt;
        int _myBan;
        bool _polling, _done;

        protected override void Build()
        {
            UIKit.Backdrop(Root);
            var panel = UIKit.Rect("Draft", Root); // noloc
            UIKit.Place(panel, 0.5f, 0.5f, 960, 1760);
            UIKit.FitInParent(panel);
            UIKit.Column(panel, 30);

            var title = UIKit.Title(panel, "Bannis une carte", 76);
            UIKit.FitText(title, 44);
            UIKit.Size(title, 110);
            _clock = UIKit.Title(panel, "", 130, UIKit.Turquoise);
            UIKit.Size(_clock, 160);
            for (int i = 0; i < _cards.Length; i++) _cards[i] = MakeCard(panel, i);
            _status = UIKit.Label(panel, "", 36, UIKit.Sand);
            UIKit.Size(_status, 140);
        }

        Card MakeCard(Transform parent, int index)
        {
            var card = new Card { Plate = UIKit.Plate(parent, CardColor, 28, UIKit.Rim, false, "Map" + index) }; // noloc
            UIKit.Size(card.Plate, 360);
            card.Plate.raycastTarget = true;
            card.Button = card.Plate.gameObject.AddComponent<Button>();
            card.Button.transition = Selectable.Transition.None;
            card.Button.onClick.AddListener(() => Vote(index));
            var rt = card.Plate.transform;

            var level = UIKit.Title(rt, Levels[index], 60, index == 0 ? UIKit.Success : index == 1 ? UIKit.Gold : UIKit.Danger, TextAnchor.MiddleLeft);
            UIKit.Place(level.rectTransform, 0, 1, 560, 90, 44, -26);
            var stars = UIKit.Stars(rt, index + 1, 64);
            UIKit.Place(stars, 1, 1, 230, 64, -44, -40);

            card.Details = UIKit.Label(rt, "", 38, UIKit.Sand, TextAnchor.UpperLeft);
            UIKit.Stretch(card.Details.rectTransform, 44, 128, 44, 80);

            card.Note = UIKit.Label(rt, "", 32, UIKit.Turquoise, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIKit.BottomBand(card.Note.rectTransform, 64, 22);
            card.Note.rectTransform.offsetMin = new Vector2(44, card.Note.rectTransform.offsetMin.y);
            card.Note.rectTransform.offsetMax = new Vector2(-44, card.Note.rectTransform.offsetMax.y);

            card.Stamp = UIKit.Title(rt, "", 110, UIKit.Danger);
            UIKit.Place(card.Stamp.rectTransform, 0.62f, 0.5f, 620, 160);
            card.Stamp.rectTransform.localRotation = Quaternion.Euler(0, 0, -12f);
            UIKit.DropShadow(card.Stamp, 8, 0.7f);
            return card;
        }

        /// <summary>
        /// Opens the draft of a match. <paramref name="ban"/> sends the player's ban (-1: time out) and <paramref name="read"/>
        /// reads the draft back, both returning it as the server has it (null on failure); <paramref name="then"/> gets false
        /// when the server could not be reached.
        /// </summary>
        public void Show(MapDraft draft, string me, bool duo, Func<string, string> nameOf, Func<int, Task<MapDraft>> ban,
                         Func<Task<MapDraft>> read, Action<bool> then)
        {
            _draft = draft;
            _me = me;
            _team = draft.VoteOf(me)?.Team ?? 0;
            _duo = duo;
            _nameOf = nameOf;
            _ban = ban;
            _read = read;
            _then = then;
            _t = 0f;
            _nextPoll = PollSeconds;
            _shownAt = -1f;
            _myBan = NotVoted;
            _polling = _done = false;
            for (int i = 0; i < _cards.Length; i++)
            {
                var id = PvpArena.LevelFor(draft.Maps[i]);
                int floors = DifficultyTable.Floors(id.Mode, id.Act);
                _cards[i].Details.text =
                    Loc.T(DifficultyTable.GetAct(id.Act).Name) + "\n" + LevelNames.Mode(id.Mode) + " · " + Loc.P(floors, "{0} étage", "{0} étages");
            }
            Refresh();
        }

        void Update()
        {
            if (_done || _draft == null) return;
            _t += Time.unscaledDeltaTime;
            if (_shownAt >= 0f)
            {
                if (_t - _shownAt >= ShowPickSeconds) Finish(true);
                return;
            }
            float left = PvpDraft.BanMs / 1000f - _t;
            if (_myBan == NotVoted && left <= 0f) Vote(-1);
            _clock.text = _myBan == NotVoted ? Mathf.CeilToInt(Mathf.Max(0f, left)).ToString() : "";
            if (_t >= GiveUpSeconds)
            {
                _status.text = Loc.T("Connexion perdue : le match est annulé.");
                Finish(false);
                return;
            }
            if (!_polling && _t >= _nextPoll) Read(_read);
        }

        /// <summary>The player bans (once); -1 when the time ran out.</summary>
        void Vote(int map)
        {
            if (_done || _myBan != NotVoted || _draft.Closed) return;
            _myBan = map;
            if (map >= 0) App.Audio.Play(Sfx.Door);
            Refresh();
            Read(() => _ban(map));
        }

        async void Read(Func<Task<MapDraft>> call)
        {
            _polling = true;
            MapDraft draft = null;
            try { draft = await call(); }
            catch (Exception e) { Debug.LogWarning($"[Draft] {e.Message}"); }
            if (this == null || _done) return;
            _polling = false;
            _nextPoll = _t + PollSeconds;
            if (draft == null || _shownAt >= 0f) return;
            _draft = draft;
            Refresh();
        }

        void Refresh()
        {
            bool closed = _draft.Closed;
            bool rivalShown = closed || (!_duo && _myBan != NotVoted); // never before the player's own ban
            for (int i = 0; i < _cards.Length; i++)
            {
                var card = _cards[i];
                string stamp = "", note = "";
                bool banned = false;
                if (closed)
                {
                    bool ours = _draft.Bans[_team] == i, theirs = _draft.Bans[1 - _team] == i;
                    banned = ours || theirs;
                    if (i == _draft.Pick) stamp = Loc.T("JOUÉE");
                    else if (banned) stamp = Loc.T("BANNIE");
                    if (ours && theirs) note = Loc.T("Bannie par les deux camps");
                    else if (ours) note = _duo ? Loc.T("Bannie par ton duo") : Loc.T("Ton bannissement");
                    else if (theirs) note = _duo ? Loc.T("Bannie par le duo adverse") : Loc.F("Bannie par {0}", RivalName());
                }
                else
                {
                    if (_myBan == i)
                    {
                        banned = !_duo;
                        if (!_duo) stamp = Loc.T("BANNIE");
                        note = _duo ? Loc.T("Ton vote") : Loc.T("Ton bannissement");
                    }
                    foreach (var v in _draft.Votes)
                    {
                        if (v.PlayerId == _me || !v.Voted || v.Map != i) continue;
                        if (v.Team == _team) note = Join(note, Loc.F("Vote de {0}", _nameOf?.Invoke(v.PlayerId)));
                        else if (rivalShown) { note = Join(note, Loc.F("Bannie par {0}", _nameOf?.Invoke(v.PlayerId))); stamp = Loc.T("BANNIE"); banned = true; }
                    }
                }
                card.Stamp.text = stamp;
                UIKit.TintTitle(card.Stamp, closed && i == _draft.Pick ? UIKit.Turquoise : UIKit.Danger);
                card.Note.text = note;
                card.Plate.color = banned ? BannedColor : CardColor;
                card.Button.interactable = !closed && _myBan == NotVoted;
            }

            if (closed)
            {
                _clock.text = "";
                _status.text = Loc.F("Carte jouée : {0}", Loc.T(Levels[_draft.Pick]));
                _shownAt = _t;
                App.Audio.Play(Sfx.Versus);
            }
            else if (_myBan == NotVoted) _status.text = _duo ? Loc.T("Vote pour la carte que ton duo bannit.") : Loc.T("Touche la carte que tu ne veux pas jouer.");
            else
            {
                string wait = _duo ? Loc.T("En attente des autres joueurs…") : Loc.T("En attente de ton adversaire…");
                _status.text = _myBan < 0 ? Loc.T("Temps écoulé : le hasard bannit pour toi.") + "\n" + wait : wait;
            }
        }

        string RivalName()
        {
            var rival = _draft.Votes.Find(v => v.Team != _team);
            return rival == null ? "" : _nameOf?.Invoke(rival.PlayerId);
        }

        static string Join(string a, string b) => string.IsNullOrEmpty(a) ? b : a + " · " + b;

        void Finish(bool ok)
        {
            if (_done) return;
            _done = true;
            Router.Close(this);
            var then = _then;
            _then = null;
            then?.Invoke(ok);
        }

        /// <summary>No way out: the draft ends by itself within seconds.</summary>
        public void OnBack() { }
    }
}
