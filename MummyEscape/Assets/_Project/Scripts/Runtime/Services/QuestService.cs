using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MummyEscape.App;
using MummyEscape.Online;
using MummyEscape.Pvp;
using UnityEngine;

namespace MummyEscape.Services
{
    /// <summary>
    /// The daily and weekly quests as the phone sees them. The server counts them from the games it checks and pays the
    /// rewards; the phone keeps the last copy it was sent and mirrors the games it just sent, so a result screen can tell
    /// at once which quest it finished (the next refresh replaces the mirror with the server's count).
    /// </summary>
    public sealed class QuestService
    {
        readonly GameApp _app;
        QuestBook _book = new QuestBook();
        readonly List<QuestDef> _moved = new List<QuestDef>();

        public QuestService(GameApp app) => _app = app;

        public event Action Changed;

        // shortcut: the phone's clock decides the day and week; a phone set to another date sees the server's copy roll later.
        static DateTime Now => DateTime.UtcNow;

        /// <summary>The last copy the server sent (empty before), rolled to the current day and week.</summary>
        public QuestBook Book
        {
            get
            {
                Quests.Roll(_book, Now);
                return _book;
            }
        }

        /// <summary>Quests need the online features (Google Play Games sign-in, or the demo) and the PvP server.</summary>
        public bool Available => _app.SignedIn && _app.Pvp != null;

        public int Claimable => Quests.Claimable(Book, Now);

        public async Task RefreshAsync()
        {
            if (!Available) return;
            QuestResponse r;
            try { r = await _app.Pvp.GetQuestsAsync(); }
            catch (Exception e)
            {
                Debug.LogWarning("[Quests] " + e.Message);
                return;
            }
            Apply(r);
        }

        /// <summary>A screen seen: counted here at once, then told to the server.</summary>
        public void Visit(string id)
        {
            var q = Quests.Get(id);
            if (!Available || q == null || Quests.IsDone(Book, q)) return;
            Quests.Visit(_book, id, Now);
            Changed?.Invoke();
            _ = VisitAsync(id);
        }

        async Task VisitAsync(string id)
        {
            try { Apply(await _app.Pvp.VisitQuestAsync(id)); }
            catch (Exception e) { Debug.LogWarning("[Quests] " + e.Message); }
        }

        /// <summary>The reward of a finished quest, paid by the server: null on success, else the error code.</summary>
        public async Task<string> ClaimAsync(string id)
        {
            var pvp = _app.Pvp;
            if (!Available) return "OFFLINE"; // noloc
            QuestResponse r;
            try { r = await pvp.ClaimQuestAsync(id); }
            catch (Exception e)
            {
                Debug.LogWarning("[Quests] " + e.Message);
                r = null;
            }
            if (r == null) return PvpServiceFactory.NetworkError;
            Apply(r);
            return r.Error;
        }

        /// <summary>A game the server will count (sent online) ended: the same count here, before the result shows.</summary>
        public void Record(QuestKind kind, int n = 1)
        {
            if (!Available) return;
            var book = Book;
            var before = new Dictionary<QuestDef, int>();
            foreach (var q in Quests.Today(book.Day)) before[q] = Quests.CountOf(book, q);
            foreach (var q in Quests.Weeklies) before[q] = Quests.CountOf(book, q);
            Quests.Add(book, kind, n, Now);
            foreach (var pair in before)
                if (Quests.CountOf(book, pair.Key) > pair.Value && !Quests.IsClaimed(book, pair.Key) && !_moved.Contains(pair.Key)) _moved.Add(pair.Key);
            Changed?.Invoke();
        }

        /// <summary>The quests that moved forward since the last call (the result screens show their progress).</summary>
        public List<QuestDef> TakeProgress()
        {
            var done = new List<QuestDef>(_moved);
            _moved.Clear();
            return done;
        }

        /// <summary>The server's answer: its quest book, and the wallet and seals a claim changed.</summary>
        void Apply(QuestResponse r)
        {
            if (r == null) return;
            if (r.Wallet != null) _app.Save.ApplyWallet(r.Wallet);
            if (r.Error == null) _app.Session.UpdateWallet(r.Seals, null);
            if (r.Quests != null) _book = r.Quests;
            Changed?.Invoke();
        }
    }
}
