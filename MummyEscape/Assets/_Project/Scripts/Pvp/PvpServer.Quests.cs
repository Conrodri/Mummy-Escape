using System;
using System.Threading.Tasks;
using MummyEscape.Monetization;

namespace MummyEscape.Pvp
{
    [Serializable]
    public class QuestResponse
    {
        public QuestBook Quests;
        public Wallet Wallet;
        public int Seals;
        public string Error;
    }

    /// <summary>
    /// The daily and weekly quests (<see cref="Pvp.Quests"/>): counted here from the runs and matches the server checks
    /// (<see cref="CountQuest"/>), the visits reported by the phone, the rewards paid on claim.
    /// </summary>
    public sealed partial class PvpServer
    {
        static QuestBook QuestsOf(PlayerPvpData d) => d.Quests ??= new QuestBook();

        /// <summary>One more game of that kind on today's quests, in the write that applies the game's result.</summary>
        void CountQuest(PlayerPvpData d, QuestKind kind) => Quests.Add(QuestsOf(d), kind, 1, _utcNow());

        /// <summary>A resolved 1v1 duel: played, and won.</summary>
        void CountDuel(PlayerPvpData d, DuelResult result)
        {
            CountQuest(d, QuestKind.DuelPlay);
            if (result == DuelResult.Win) CountQuest(d, QuestKind.DuelWin);
        }

        static QuestResponse QuestAnswer(PlayerPvpData d, string error = null) =>
            new QuestResponse { Quests = QuestsOf(d), Wallet = WalletOf(d), Seals = d.Seals, Error = error };

        public async Task<QuestResponse> GetQuestsAsync(string me) =>
            QuestAnswer(await Update(me, d => Quests.Roll(QuestsOf(d), _utcNow())));

        public async Task<QuestResponse> VisitQuestAsync(string me, string id)
        {
            var q = Quests.Get(id);
            if (q == null || q.Kind != QuestKind.Visit) return new QuestResponse { Error = "UNKNOWN_QUEST" };
            return QuestAnswer(await Update(me, d => Quests.Visit(QuestsOf(d), id, _utcNow())));
        }

        /// <summary>
        /// Pays a finished quest of today (or this week) once. Scarabs before they are held here: "CLOSED", the quest stays
        /// unclaimed (like the spends; the phone opens them at its first wallet).
        /// </summary>
        public async Task<QuestResponse> ClaimQuestAsync(string me, string id)
        {
            var q = Quests.Get(id);
            if (q == null) return new QuestResponse { Error = "UNKNOWN_QUEST" };
            var utc = _utcNow();
            var season = BattlePass.At(utc);
            string error = null;
            var data = await Update(me, d =>
            {
                var b = QuestsOf(d);
                Quests.Roll(b, utc);
                var w = WalletOf(d);
                error = !Quests.IsDone(b, q) ? "NOT_DONE" : Quests.IsClaimed(b, q) ? "CLAIMED"
                      : q.Reward == QuestRewardKind.Scarabs && !w.ScarabsOpen ? "CLOSED" : null;
                if (error != null) return;
                switch (q.Reward)
                {
                    case QuestRewardKind.PassXp: AddXp(w, season, "quest:" + (q.Weekly ? b.Week : b.Day) + ":" + q.Id, q.Amount); break; // noloc
                    case QuestRewardKind.Scarabs: w.Scarabs += q.Amount; break;
                    case QuestRewardKind.Seals: d.Seals += q.Amount; break;
                    case QuestRewardKind.Gold: w.Gold += q.Amount; break;
                }
                (q.Weekly ? b.WeeklyClaimed : b.Claimed).Add(q.Id);
            });
            return QuestAnswer(data, error);
        }
    }
}
