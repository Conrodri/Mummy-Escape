using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MummyEscape.Core;
using MummyEscape.Pvp;
using NUnit.Framework;

namespace MummyEscape.Tests
{
    /// <summary>
    /// Live duel and 2v2 matchmaking on the server: judged once even when a write loses a race, void when a side never showed
    /// up, refusals that name who failed, neighbour leagues, the bot after a real search (half the stake), the mate queue.
    /// </summary>
    public class MatchmakingTests
    {
        static readonly DateTime Today = new DateTime(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);
        static int Gen => DifficultyTable.GeneratorVersion;

        sealed class World
        {
            public DateTime Now = Today;
            public readonly MemoryPvpStore Store = new MemoryPvpStore();
            public readonly PvpServer Server;
            int _seed = 5100;

            public World(params string[] players)
            {
                Server = new PvpServer(Store, () => Now, () => _seed++);
                foreach (var p in players) Store.OpenDuels(p);
            }

            public void Advance(int ms) => Now = Now.AddMilliseconds(ms);
        }

        static LiveDuelist Me(string id, int elo = 0) => new LiveDuelist { PlayerId = id, Name = id, Elo = elo, Look = new PlayerLook { Mummy = "classic" } };

        /// <summary>A shallow copy: what another server instance read before the stored object changed.</summary>
        static object Copy(object o) => typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(o, null);

        /// <summary>The <paramref name="nth"/> write on an object of type <typeparamref name="T"/> loses its race to <paramref name="rival"/>.</summary>
        static void LoseRace<T>(MemoryPvpStore store, int nth, Action rival)
        {
            int seen = 0;
            store.LoseRaceOnce = (key, current) =>
            {
                if (!(current is T) || ++seen < nth) return null;
                var stale = Copy(current);
                rival();
                return stale;
            };
        }

        static LiveDuel Start(World w, string host, string guest, string key) =>
            w.Server.StartLiveDuelAsync(host, Gen, key, Me(host), Me(guest)).Result.Match;

        // ------------------------------------------------------------------ judged once

        [Test]
        public void LiveDuel_ASettleThatLosesAWriteRace_DoesNotCountTwice()
        {
            var w = new World("alice", "bob");
            var duel = Start(w, "alice", "bob", "race1");
            w.Server.BanLiveDuelAsync("alice", duel.Id, 0).Wait();
            w.Server.BanLiveDuelAsync("bob", duel.Id, 2).Wait();
            w.Server.SubmitLiveDuelAsync("bob", duel.Id, new RunSubmission { Outcome = RunOutcome.Abandoned }, "Bob").Wait();
            w.Advance(LiveDuelConfig.SubmitWindowMs + 1);
            // Alice's poll: its confirmation (1st duel write), then the verdict (2nd), which bob's poll beats on another instance.
            LoseRace<LiveDuel>(w.Store, 2, () => w.Server.LiveDuelResultAsync("bob", duel.Id).Wait());
            var r = w.Server.LiveDuelResultAsync("alice", duel.Id).Result;
            Assert.IsNull(w.Store.LoseRaceOnce, "the race happened");
            Assert.IsTrue(r.Resolved);
            Assert.AreEqual(1, w.Store.Players["alice"].TotalDuels, "one duel counted, not two");
            Assert.AreEqual(1, w.Store.Players["bob"].TotalDuels);
            Assert.AreEqual(1, w.Store.History["alice"].Count);
        }

        [Test]
        public void Relay_ASettleThatLosesAWriteRace_DoesNotCountTwice()
        {
            var w = new World();
            string ab = MakeDuo(w.Server, "alice", "bob"), cd = MakeDuo(w.Server, "carol", "dave");
            var match = w.Server.StartRelayMatchAsync("alice", Gen, "race2", Claim(ab, "alice", "bob"), Claim(cd, "carol", "dave")).Result.Match;
            foreach (var p in new[] { "alice", "bob", "carol", "dave" }) w.Server.BanRelayAsync(p, match.Id, 0).Wait();
            w.Server.SubmitRelayAsync("carol", match.Id, "carol", new List<RelayInput>(), null).Wait();
            w.Advance(RelayServerConfig.SubmitWindowMs + 1);
            LoseRace<RelayMatch>(w.Store, 1, () => w.Server.RelayResultAsync("dave", match.Id).Wait());
            var r = w.Server.RelayResultAsync("alice", match.Id).Result;
            Assert.IsNull(w.Store.LoseRaceOnce, "the race happened");
            Assert.IsFalse(r.Pending);
            var duo = w.Server.GetTeamsAsync("alice").Result.Duos.Single(d => d.Id == ab);
            Assert.AreEqual(1, duo.Matches, "one match counted, not two");
        }

        // ------------------------------------------------------------------ void

        [Test]
        public void LiveDuel_ARivalWhoNeverShowedUp_VoidsTheDuel_AndGivesTheEnergyBack()
        {
            var w = new World("alice", "bob");
            int eloA = w.Store.Players["alice"].Elo, eloB = w.Store.Players["bob"].Elo;
            // A forged lobby: alice "meets" bob, who never calls the server for this duel.
            var duel = Start(w, "alice", "bob", "forged");
            Assert.IsTrue(duel.A.Confirmed);
            Assert.IsFalse(duel.B.Confirmed);
            Assert.AreEqual(1, w.Store.Players["bob"].Energy.Used, "spent at the start");
            w.Server.BanLiveDuelAsync("alice", duel.Id, 0).Wait();
            w.Advance(PvpDraft.WindowMs + 1);
            w.Server.GetLiveDuelAsync("alice", duel.Id).Wait();
            w.Server.SubmitLiveDuelAsync("alice", duel.Id, new RunSubmission { Outcome = RunOutcome.Abandoned }, "Alice").Wait();
            Assert.IsFalse(w.Server.LiveDuelResultAsync("alice", duel.Id).Result.Resolved, "bob still has time to show up");
            w.Advance(LiveDuelConfig.ConfirmWindowMs);

            var r = w.Server.LiveDuelResultAsync("alice", duel.Id).Result;
            Assert.IsTrue(r.Resolved);
            Assert.IsTrue(r.Void);
            Assert.AreEqual(eloA, r.EloAfter);
            Assert.AreEqual(eloA, w.Store.Players["alice"].Elo);
            Assert.AreEqual(eloB, w.Store.Players["bob"].Elo, "the victim loses nothing");
            Assert.AreEqual(0, w.Store.Players["bob"].Losses + w.Store.Players["alice"].Losses + w.Store.Players["alice"].Wins);
            Assert.AreEqual(0, w.Store.Players["bob"].Energy.Used, "energy back");
            Assert.AreEqual(0, w.Store.Players["alice"].Energy.Used);
            Assert.IsTrue(w.Store.History["bob"][0].Void);
            Assert.IsTrue(w.Store.History["alice"][0].Void);
        }

        [Test]
        public void LiveDuel_TheGuestShowsUpByReadingTheDuel_ThenItCounts()
        {
            var w = new World("alice", "bob");
            var duel = Start(w, "alice", "bob", "honest");
            Assert.IsTrue(w.Server.GetLiveDuelAsync("bob", duel.Id).Result.Match.B.Confirmed);
            w.Server.SubmitLiveDuelAsync("alice", duel.Id, new RunSubmission { Outcome = RunOutcome.Abandoned }, "Alice").Wait();
            w.Advance(LiveDuelConfig.SubmitWindowMs + 1);
            var r = w.Server.LiveDuelResultAsync("alice", duel.Id).Result;
            Assert.IsTrue(r.Resolved);
            Assert.IsFalse(r.Void);
            Assert.AreEqual(1, w.Store.Players["alice"].Losses + w.Store.Players["alice"].Draws, "a real duel: alice quit");
        }

        [Test]
        public void Relay_ADuoThatNeverShowedUp_VoidsTheMatch()
        {
            var w = new World();
            string ab = MakeDuo(w.Server, "alice", "bob"), cd = MakeDuo(w.Server, "carol", "dave");
            var match = w.Server.StartRelayMatchAsync("alice", Gen, "forged2", Claim(ab, "alice", "bob"), Claim(cd, "carol", "dave")).Result.Match;
            Assert.IsTrue(match.A.Confirmed);
            Assert.IsFalse(match.B.Confirmed);
            w.Server.BanRelayAsync("alice", match.Id, 0).Wait();
            w.Advance(RelayServerConfig.ConfirmWindowMs + 1);
            var r = w.Server.RelayResultAsync("alice", match.Id).Result;
            Assert.IsFalse(r.Pending);
            Assert.IsTrue(r.Void);
            Assert.AreEqual(0, r.EloDelta);
            Assert.AreEqual(0, w.Server.GetTeamsAsync("carol").Result.Duos.Single(d => d.Id == cd).Matches);
            foreach (var p in new[] { "alice", "bob", "carol", "dave" }) Assert.AreEqual(0, w.Store.Players[p].Energy.Used, p);
        }

        [Test]
        public void Relay_AnExistingMatch_IsOnlyGivenToItsPlayers()
        {
            var w = new World();
            string ab = MakeDuo(w.Server, "alice", "bob"), cd = MakeDuo(w.Server, "carol", "dave"), ef = MakeDuo(w.Server, "erin", "fred");
            var match = w.Server.StartRelayMatchAsync("alice", Gen, "lobby9", Claim(ab, "alice", "bob"), Claim(cd, "carol", "dave")).Result.Match;
            Assert.IsNotNull(match);
            Assert.AreEqual("UNKNOWN", w.Server.StartRelayMatchAsync("erin", Gen, "lobby9", Claim(ef, "erin", "fred"), Claim(ab, "alice", "bob")).Result.Error);
            Assert.AreEqual("UNKNOWN", w.Server.StartRelayBotsAsync("erin", Gen, "lobby9", Claim(ef, "erin", "fred")).Result.Error);
            Assert.IsTrue(w.Server.StartRelayMatchAsync("carol", Gen, "lobby9", Claim(cd, "carol", "dave"), Claim(ab, "alice", "bob")).Result.Match.B.Confirmed,
                          "a player of the match gets it, and his duo shows up");
        }

        // ------------------------------------------------------------------ refusals and leagues

        [Test]
        public void LiveDuel_Refusals_NameThePlayerAtFault()
        {
            var w = new World("alice", "bob");
            w.Store.Players["dan"] = new PlayerPvpData(); // has not opened the duels
            var locked = w.Server.StartLiveDuelAsync("alice", Gen, "k1", Me("alice"), Me("dan")).Result;
            Assert.AreEqual("LOCKED", locked.Error);
            Assert.AreEqual("dan", locked.Refused);

            w.Store.Players["bob"].Energy = new EnergyMeter { Used = EnergyConfig.PvpMax, AtUnixMs = new DateTimeOffset(Today).ToUnixTimeMilliseconds() };
            var broke = w.Server.StartLiveDuelAsync("alice", Gen, "k2", Me("alice"), Me("bob")).Result;
            Assert.AreEqual("ENERGY", broke.Error);
            Assert.AreEqual("bob", broke.Refused);
            Assert.AreEqual(0, w.Store.Players["alice"].Energy.Used, "nothing taken from alice");
        }

        [Test]
        public void LiveDuel_NeighbourLeaguesMeet_TwoApartDoNot_TheStalePhoneIsBlamed()
        {
            var w = new World("alice", "bob", "carol");
            w.Store.Players["alice"].Elo = PvpConfig.OrMin;
            w.Store.Players["bob"].Elo = PvpConfig.ArgentMin;
            w.Store.Players["carol"].Elo = PvpConfig.DiamantMin;
            Assert.IsNotNull(Start(w, "alice", "bob", "n1"), "gold meets silver");
            // Carol's phone still believed she was gold.
            var r = w.Server.StartLiveDuelAsync("alice", Gen, "n2", Me("alice", PvpConfig.OrMin), Me("carol", PvpConfig.OrMin)).Result;
            Assert.AreEqual("DIVISION", r.Error);
            Assert.AreEqual("carol", r.Refused);
        }

        [Test]
        public void Relay_NeighbourDivisionsMeet()
        {
            var w = new World();
            string ab = MakeDuo(w.Server, "alice", "bob"), cd = MakeDuo(w.Server, "carol", "dave");
            w.Store.Players["alice"].Elo = PvpConfig.OrMin;
            w.Store.Players["carol"].Elo = PvpConfig.PlatineMin;
            var match = w.Server.StartRelayMatchAsync("alice", Gen, "n3", Claim(ab, "alice", "bob"), Claim(cd, "carol", "dave")).Result.Match;
            Assert.IsNotNull(match);
            CollectionAssert.AreEqual(PvpDraft.Open(match.Draft.Seed, League.Platine, new (string, int, bool)[0]).Maps, match.Draft.Maps,
                                      "the higher division's maps");
        }

        // ------------------------------------------------------------------ bots

        [Test]
        public void BotDuel_NeedsAMinuteOfSearch_FromANewPhone_AndConsumesIt()
        {
            var w = new World("alice");
            var mine = Me("alice");
            Assert.AreEqual("SEARCH", w.Server.StartLiveBotDuelAsync("alice", Gen, "b1", mine, PvpDraft.Version, LiveDuelConfig.Protocol).Result.Error);
            Assert.IsTrue(w.Server.StartLiveSearchAsync("alice").Result.Ok);
            w.Advance(30_000);
            Assert.AreEqual("SEARCH", w.Server.StartLiveBotDuelAsync("alice", Gen, "b1", mine, PvpDraft.Version, LiveDuelConfig.Protocol).Result.Error);
            w.Advance(PvpConfig.BotFallbackMs - LiveDuelConfig.BotSearchSlackMs - 30_000);
            Assert.IsNotNull(w.Server.StartLiveBotDuelAsync("alice", Gen, "b1", mine, PvpDraft.Version, LiveDuelConfig.Protocol).Result.Match);
            Assert.AreEqual("SEARCH", w.Server.StartLiveBotDuelAsync("alice", Gen, "b2", mine, PvpDraft.Version, LiveDuelConfig.Protocol).Result.Error,
                            "one search, one bot");
            Assert.IsNotNull(w.Server.StartLiveBotDuelAsync("alice", Gen, "b3", mine).Result.Match, "a 0.1.14 phone is not checked");
        }

        [Test]
        public void BotStake_IsHalved_RoundedUp()
        {
            var win = new PlayerPvpData { Elo = 1000 };
            DuelBookkeeping.ApplyResult(win, "bot_1", 1000, DuelResult.Win, halfStake: true);
            Assert.AreEqual(1000 + PvpConfig.PointsStart / 2, win.Elo);
            var loss = new PlayerPvpData { Elo = PvpConfig.ArgentMin + 50 }; // silver: 60 % of 20 = 12 lost, halved: 6
            DuelBookkeeping.ApplyResult(loss, "bot_1", 1000, DuelResult.Loss, halfStake: true);
            Assert.AreEqual(PvpConfig.ArgentMin + 44, loss.Elo);
            var bronze = new PlayerPvpData { Elo = 800, TotalDuels = 1000 }; // stake 10 × 40 % = 4, halved: 2
            DuelBookkeeping.ApplyResult(bronze, "bot_1", 800, DuelResult.Loss, halfStake: true);
            Assert.AreEqual(798, bronze.Elo);
        }

        // ------------------------------------------------------------------ mates

        [Test]
        public void MateSearch_AGroupWhoseCreationFailed_IsFormedOnTheNextPoll_ByEither()
        {
            var w = new World();
            long now = new DateTimeOffset(Today).ToUnixTimeMilliseconds();
            string key = PvpServer.QueueCollection + "/" + PvpServer.MateQueueKey;
            // Both were matched under the queue's lock, then the creation threw: nobody has the duo.
            w.Store.Shared[key] = new List<MateSearch>
            {
                new MateSearch { PlayerId = "a", Name = "Ana", Mode = GroupLaunch.Relay, SinceUnixMs = now - 5000, SeenAtUnixMs = now, DuoId = "fixed" },
                new MateSearch { PlayerId = "b", Name = "Bob", Mode = GroupLaunch.Relay, SinceUnixMs = now - 3000, SeenAtUnixMs = now, DuoId = "fixed" },
            };
            var duo = w.Server.SearchMateAsync("b", "Bob", GroupLaunch.Relay).Result.Duo;
            Assert.AreEqual("fixed", duo?.Id);
            Assert.AreEqual("a", duo.Leader, "a searched first");
            Assert.AreEqual("fixed", w.Server.SearchMateAsync("a", "Ana", GroupLaunch.Relay).Result.Duo?.Id);

            // Matched with someone who left the queue without forming anything: back to searching.
            w.Store.Shared[key] = new List<MateSearch>
            {
                new MateSearch { PlayerId = "c", Name = "Cid", Mode = GroupLaunch.Coop, SinceUnixMs = now, SeenAtUnixMs = now, DuoId = "lost" },
            };
            Assert.IsNull(w.Server.SearchMateAsync("c", "Cid", GroupLaunch.Coop).Result.Duo);
            Assert.IsNull(((List<MateSearch>)w.Store.Shared[key]).Single(s => s.PlayerId == "c").DuoId);
            Assert.IsNotNull(w.Server.SearchMateAsync("d", "Dan", GroupLaunch.Coop).Result.Duo, "c can be matched again");
            Assert.AreEqual("OUTDATED", w.Server.SearchMateAsync("e", "Eve", GroupLaunch.Coop, Gen + 1).Result.Error);
        }

        // ------------------------------------------------------------------ helpers (as in RelayTests)

        static string MakeDuo(PvpServer server, string a, string b)
        {
            Assert.IsTrue(server.InviteDuoAsync(a, a, b).Result.Ok);
            var invite = server.GetTeamsAsync(b).Result.Invites.Single(i => i.FromId == a);
            Assert.IsTrue(server.RespondDuoAsync(b, b, invite.Id, true).Result.Ok);
            return server.GetTeamsAsync(a).Result.Duos.Single(d => d.Members.Contains(b)).Id;
        }

        static RelaySide Claim(string duoId, params string[] players) => new RelaySide
        {
            DuoId = duoId,
            Runners = players.Select(p => new RelayRunner { PlayerId = p, Name = p, Look = new PlayerLook { Mummy = "classic" } }).ToList(),
        };
    }
}
