using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MummyEscape.Core;
using MummyEscape.Monetization;
using MummyEscape.Pvp;
using NUnit.Framework;

namespace MummyEscape.Tests
{
    /// <summary>The daily and weekly quests: progress counted by the server from what it checks, rewards paid once on claim.</summary>
    public class QuestTests
    {
        static readonly DateTime Wednesday = new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);
        static DateTime _now;

        /// <summary>Noon of the first day of the week of <see cref="Wednesday"/> whose deal has these quests.</summary>
        static DateTime DayWith(params string[] ids)
        {
            for (int i = 0; i < 7; i++)
            {
                var t = new DateTime(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc).AddDays(i);
                var today = Quests.Today(Quests.DayOf(t));
                if (Array.TrueForAll(ids, id => new List<QuestDef>(today).Contains(Q(id)))) return t;
            }
            Assert.Fail("no day of the week deals " + string.Join(", ", ids));
            return default;
        }

        static (PvpServer, MemoryPvpStore) NewServer(params string[] need)
        {
            var store = new MemoryPvpStore();
            store.SoloStars["alice"] = 40;
            int seed = 3003;
            _now = DayWith(need);
            return (new PvpServer(store, () => _now, () => seed++), store);
        }

        static QuestDef Q(string id) => Quests.Get(id);

        /// <summary>Every daily quest of the book done at that time.</summary>
        static void AllDailies(QuestBook b, DateTime utc)
        {
            foreach (var kind in new[] { QuestKind.CampaignWin, QuestKind.EndlessWin, QuestKind.DuelPlay, QuestKind.DuelWin, QuestKind.DuoPlay })
                Quests.Add(b, kind, 10, utc);
            Quests.Visit(b, Quests.VisitSale, utc);
            Quests.Visit(b, Quests.VisitDuoBoard, utc);
        }

        [Test]
        public void Today_Deals2Pve_2Pvp_1Visit_TheSameForADay()
        {
            for (int i = 0; i < 30; i++)
            {
                string day = Quests.DayOf(Wednesday.AddDays(i));
                var today = new List<QuestDef>(Quests.Today(day));
                Assert.AreEqual(2, today.FindAll(q => q.Group == QuestGroup.Pve).Count, day);
                Assert.AreEqual(2, today.FindAll(q => q.Group == QuestGroup.Pvp).Count, day);
                Assert.AreEqual(1, today.FindAll(q => q.Group == QuestGroup.Visit).Count, day);
                Quests.Today(Quests.DayOf(Wednesday));
                CollectionAssert.AreEqual(today, Quests.Today(day), "dealt again the same");
            }
        }

        [Test]
        public void Days_AndWeeks_AreUtc_WeeksStartOnMonday()
        {
            Assert.AreEqual("2026-10-14", Quests.DayOf(Wednesday));
            Assert.AreEqual("2026-10-12", Quests.WeekOf(Wednesday));
            Assert.AreEqual("2026-10-12", Quests.WeekOf(new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc)));
            Assert.AreEqual("2026-10-12", Quests.WeekOf(new DateTime(2026, 10, 18, 23, 59, 59, DateTimeKind.Utc)), "Sunday ends the week");
            Assert.AreEqual("2025-12-29", Quests.WeekOf(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)), "the shop sale's epoch Monday");
        }

        [Test]
        public void Roll_ResetsTheDay_AtMidnight_AndTheWeek_OnMonday()
        {
            var Wednesday = DayWith("duel_play");
            var b = new QuestBook();
            Quests.Add(b, QuestKind.DuelPlay, 2, Wednesday);
            b.Claimed.Add("duel_win");
            b.WeeklyClaimed.Add(Quests.WeeklyId);
            b.DaysDone.Add("2026-10-13");

            Quests.Roll(b, Wednesday.AddHours(11)); // 23:00, same day
            Assert.AreEqual(2, Quests.CountOf(b, Q("duel_play")));

            Quests.Roll(b, Wednesday.AddHours(12)); // Thursday 00:00
            Assert.AreEqual(0, Quests.CountOf(b, Q("duel_play")), "a new day");
            Assert.IsEmpty(b.Claimed);
            Assert.AreEqual(1, b.DaysDone.Count, "the week goes on");
            Assert.IsTrue(Quests.IsClaimed(b, Quests.Weekly));

            Quests.Roll(b, new DateTime(2026, 10, 19, 0, 0, 0, DateTimeKind.Utc));
            Assert.IsEmpty(b.DaysDone, "Monday: a new week");
            Assert.IsFalse(Quests.IsClaimed(b, Quests.Weekly));
        }

        [Test]
        public void Add_CountsItsKind_CappedAtTheTarget_AndTellsWhatBecameDone()
        {
            var Wednesday = DayWith(Quests.VisitSale);
            var b = new QuestBook();
            Assert.IsEmpty(Quests.Add(b, QuestKind.CampaignWin, 2, Wednesday));
            Assert.AreEqual(2, Quests.CountOf(b, Q("campaign_win")));
            var done = Quests.Add(b, QuestKind.CampaignWin, 5, Wednesday);
            Assert.AreEqual(1, done.Count);
            Assert.AreEqual("campaign_win", done[0].Id);
            Assert.AreEqual(3, Quests.CountOf(b, Q("campaign_win")), "capped");
            Assert.IsEmpty(Quests.Add(b, QuestKind.CampaignWin, 1, Wednesday), "already done");
            Assert.IsEmpty(Quests.Add(b, QuestKind.Visit, 1, Wednesday), "visits are reported, not counted");
            Assert.AreEqual(0, Quests.CountOf(b, Q(Quests.VisitSale)));
            Assert.AreEqual(1, Quests.Claimable(b, Wednesday));
            Assert.IsEmpty(Quests.Visit(b, "campaign_win", Wednesday), "not a visit");
            Assert.AreEqual(Quests.VisitSale, Quests.Visit(b, Quests.VisitSale, Wednesday)[0].Id);
        }

        [Test]
        public void Weekly_CountsEachDayWithEveryDailyDone_Once()
        {
            var b = new QuestBook();
            var monday = new DateTime(2026, 10, 12, 8, 0, 0, DateTimeKind.Utc);
            AllDailies(b, monday);
            AllDailies(b, monday.AddHours(3));
            Assert.AreEqual(1, b.DaysDone.Count, "a day counts once");
            Assert.AreEqual(1, Quests.CountOf(b, Quests.Weekly));

            List<QuestDef> last = null;
            for (int day = 1; day < Quests.WeeklyDays; day++)
            {
                Assert.IsFalse(Quests.IsDone(b, Quests.Weekly));
                var t = monday.AddDays(day);
                last = new List<QuestDef>();
                foreach (var kind in new[] { QuestKind.CampaignWin, QuestKind.EndlessWin, QuestKind.DuelPlay, QuestKind.DuelWin, QuestKind.DuoPlay })
                    last.AddRange(Quests.Add(b, kind, 10, t));
                last.AddRange(Quests.Visit(b, Quests.VisitSale, t));
                last.AddRange(Quests.Visit(b, Quests.VisitDuoBoard, t));
            }
            Assert.IsTrue(Quests.IsDone(b, Quests.Weekly), "five days");
            Assert.IsTrue(last.Contains(Quests.Weekly), "became done with the last daily");
            AllDailies(b, monday.AddDays(5));
            Assert.AreEqual(Quests.WeeklyDays, Quests.CountOf(b, Quests.Weekly), "capped");
        }

        [Test]
        public async Task WeeklyGames_CountPveAndPvpGames_PaidInScarabsAndSeals()
        {
            var b = new QuestBook();
            var monday = new DateTime(2026, 10, 12, 8, 0, 0, DateTimeKind.Utc);
            Quests.Add(b, QuestKind.CampaignPlay, 30, monday);
            Quests.Add(b, QuestKind.CampaignWin, 30, monday);
            Quests.Add(b, QuestKind.EndlessWin, 19, monday.AddDays(2));
            Assert.AreEqual(49, Quests.CountOf(b, Q("weekly_pve")), "campaign runs and endless escapes, a win not twice");
            Assert.AreEqual("weekly_pve", Quests.Add(b, QuestKind.EndlessWin, 1, monday.AddDays(3)).Find(q => q.Weekly)?.Id);
            Quests.Add(b, QuestKind.DuelPlay, 20, monday);
            Quests.Add(b, QuestKind.DuelWin, 20, monday);
            Quests.Add(b, QuestKind.DuoPlay, 10, monday.AddDays(6));
            Assert.AreEqual(30, Quests.CountOf(b, Q("weekly_pvp")), "duels and 2v2 games, a win not twice");
            Quests.Roll(b, monday.AddDays(7));
            Assert.AreEqual(0, Quests.CountOf(b, Q("weekly_pvp")), "a new week");

            var (server, store) = NewServer();
            await server.OpenScarabsAsync("alice", new ScarabSeed());
            await store.UpdatePlayerAsync("alice", d =>
            {
                Quests.Add(d.Quests = new QuestBook(), QuestKind.CampaignPlay, 50, _now);
                Quests.Add(d.Quests, QuestKind.DuelPlay, 30, _now);
            });
            Assert.AreEqual(150, (await server.ClaimQuestAsync("alice", "weekly_pve")).Wallet.Scarabs);
            Assert.AreEqual(25, (await server.ClaimQuestAsync("alice", "weekly_pvp")).Seals);
        }

        [Test]
        public async Task Claim_PaysEachRewardOnce()
        {
            var (server, store) = NewServer("campaign_win", "duel_play", "endless_win");
            await server.OpenScarabsAsync("alice", new ScarabSeed());
            Assert.AreEqual("UNKNOWN_QUEST", (await server.ClaimQuestAsync("alice", "nope")).Error);
            Assert.AreEqual("NOT_DONE", (await server.ClaimQuestAsync("alice", "duel_play")).Error);

            await store.UpdatePlayerAsync("alice", d =>
            {
                d.Quests = new QuestBook();
                AllDailies(d.Quests, _now);
                for (int i = 0; d.Quests.DaysDone.Count < Quests.WeeklyDays; i++)
                {
                    string day = Quests.DayOf(new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc).AddDays(i));
                    if (!d.Quests.DaysDone.Contains(day)) d.Quests.DaysDone.Add(day);
                }
            });
            Assert.AreEqual(Quests.Today(Quests.DayOf(_now)).Count + 1, Quests.Claimable((await server.GetQuestsAsync("alice")).Quests, _now));

            var r = await server.ClaimQuestAsync("alice", "campaign_win");
            Assert.IsNull(r.Error);
            Assert.AreEqual(30, r.Wallet.Scarabs);
            r = await server.ClaimQuestAsync("alice", "duel_play");
            Assert.AreEqual(5, r.Seals);
            r = await server.ClaimQuestAsync("alice", "endless_win");
            Assert.AreEqual(50, r.Wallet.PassXp);
            r = await server.ClaimQuestAsync("alice", Quests.WeeklyId);
            Assert.AreEqual(5, r.Wallet.Gold);

            foreach (var id in new[] { "campaign_win", "duel_play", "endless_win", Quests.WeeklyId })
                Assert.AreEqual("CLAIMED", (await server.ClaimQuestAsync("alice", id)).Error, id);
            r = await server.GetQuestsAsync("alice");
            Assert.AreEqual(30, r.Wallet.Scarabs);
            Assert.AreEqual(5, r.Seals);
            Assert.AreEqual(50, r.Wallet.PassXp);
            Assert.AreEqual(5, r.Wallet.Gold);

            _now = _now.AddDays(1);
            Assert.AreEqual("NOT_DONE", (await server.ClaimQuestAsync("alice", "duel_win")).Error, "yesterday's quests rolled away");
        }

        [Test]
        public async Task Claim_Scarabs_BeforeTheyAreHeldHere_IsClosed_AndStaysClaimable()
        {
            var (server, store) = NewServer("campaign_win");
            await store.UpdatePlayerAsync("alice", d => Quests.Add(d.Quests = new QuestBook(), QuestKind.CampaignWin, 3, _now));
            Assert.AreEqual("CLOSED", (await server.ClaimQuestAsync("alice", "campaign_win")).Error);
            await server.OpenScarabsAsync("alice", new ScarabSeed());
            Assert.AreEqual(30, (await server.ClaimQuestAsync("alice", "campaign_win")).Wallet.Scarabs);
        }

        [Test]
        public async Task Visit_OnlyVisitQuests()
        {
            var (server, _) = NewServer(Quests.VisitSale);
            Assert.AreEqual("UNKNOWN_QUEST", (await server.VisitQuestAsync("alice", "duel_win")).Error);
            Assert.AreEqual("UNKNOWN_QUEST", (await server.VisitQuestAsync("alice", "made_up")).Error);
            var r = await server.VisitQuestAsync("alice", Quests.VisitSale);
            Assert.IsNull(r.Error);
            Assert.IsTrue(Quests.IsDone(r.Quests, Q(Quests.VisitSale)));
            Assert.IsFalse(Quests.IsDone(r.Quests, Q(Quests.VisitDuoBoard)));
            Assert.AreEqual(20, (await server.ClaimQuestAsync("alice", Quests.VisitSale)).Wallet.PassXp);
        }

        [Test]
        public async Task SoloRuns_CountEscapes_Once_NotDeaths()
        {
            var (server, _) = NewServer();
            var level = Progression.First(Difficulty.Easy);
            var won = WalletTests.SolvedRun("a", level, 0);
            await server.SubmitSoloRunsAsync("alice", new List<SoloRunReport> { won, won });
            await server.SubmitSoloRunsAsync("alice", new List<SoloRunReport> { won });
            var b = (await server.GetQuestsAsync("alice")).Quests;
            Assert.AreEqual(1, Quests.CountOf(b, Q("campaign_win")), "the same run sent again counts once");

            var death = DeathRun("d");
            Assert.IsNotNull(death, "a tomb where the player can die");
            var r = await server.SubmitSoloRunsAsync("alice", new List<SoloRunReport> { death });
            Assert.Greater(r.Wallet.PassXp, BattlePass.SoloXp(true, 3), "the death was replayed (it pays its XP)");
            b = (await server.GetQuestsAsync("alice")).Quests;
            Assert.AreEqual(1, Quests.CountOf(b, Q("campaign_win")), "a death is no escape");
        }

        /// <summary>A campaign run that ends in a death: the first one a short search finds.</summary>
        static SoloRunReport DeathRun(string id)
        {
            for (int act = 1; act <= 5; act++)
                for (int n = 1; n <= 10; n++)
                {
                    var levelId = new LevelId(Difficulty.Normal, act, n);
                    Level level;
                    try { level = LevelGenerator.Generate(levelId, 0); }
                    catch (Exception) { continue; }
                    var path = new List<PlayerAction>();
                    if (!SearchDeath(level, Rules.Initial(level), path, 6)) continue;
                    var run = new SoloRunReport { Id = id, Level = levelId.Key, Variant = 0, Inputs = PvpTests.Stamp(level, path, 2000) };
                    return run;
                }
            return null;
        }

        static bool SearchDeath(Level level, RuleState s, List<PlayerAction> path, int depth)
        {
            if (depth == 0) return false;
            for (int d = 0; d < 4; d++)
            {
                var a = PlayerAction.Move((Dir)d);
                var r = Rules.Step(level, s, a);
                if (r.Has(StepFlags.Blocked) || r.Has(StepFlags.Won)) continue;
                path.Add(a);
                if (r.Has(StepFlags.Died) || SearchDeath(level, r.State, path, depth - 1)) return true;
                path.RemoveAt(path.Count - 1);
            }
            return false;
        }

        [Test]
        public async Task BotDuel_CountsPlayedAndWon_Once()
        {
            var (server, _) = NewServer("duel_play", "duel_win");
            var duel = await server.FindDuelAsync("alice", DifficultyTable.GeneratorVersion, true);
            if (duel.Searching)
            {
                _now = _now.AddMilliseconds(PvpConfig.BotFallbackMs + 1000);
                duel = await server.FindDuelAsync("alice", DifficultyTable.GeneratorVersion, true);
            }
            Assert.IsTrue(duel.Ghost.Bot);
            var level = PvpServer.Arena(duel.Seed);
            var run = PvpTests.Finish(duel.MatchId, PvpTests.Stamp(level, level.Solution.Actions, 300));
            var result = await server.SubmitRunAsync("alice", run, "Alice");
            Assert.IsTrue(result.Resolved);
            Assert.AreEqual("NO_PENDING_DUEL", (await server.SubmitRunAsync("alice", run, "Alice")).Error);
            var b = (await server.GetQuestsAsync("alice")).Quests;
            Assert.AreEqual(1, Quests.CountOf(b, Q("duel_play")), "counted once");
            Assert.AreEqual(result.Result == DuelResult.Win ? 1 : 0, Quests.CountOf(b, Q("duel_win")));
        }
    }
}
