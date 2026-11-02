using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MummyEscape.Core;
using MummyEscape.Pvp;
using NUnit.Framework;

namespace MummyEscape.Tests
{
    /// <summary>The four ladders as the server serves them (world, country, friends), and the names it accepts from phones.</summary>
    public class LadderTests
    {
        static readonly DateTime Now = new DateTime(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc);

        sealed class World
        {
            public readonly MemoryPvpStore Store = new MemoryPvpStore();
            public DateTime Clock = Now;
            public readonly PvpServer Server;
            public World() { Server = new PvpServer(Store, () => Clock); }

            public PlayerPvpData Player(string id, string country)
            {
                var d = Store.Players.TryGetValue(id, out var x) ? x : Store.Players[id] = new PlayerPvpData();
                d.Country = country;
                d.DirectoryName = id + "#1";
                return d;
            }

            /// <summary>A tower record as the server writes it after its replays.</summary>
            public Task Tower(string id, string country, int floors, int timeMs)
            {
                var d = Player(id, country);
                d.TowerBest = new TowerRecord { BestFloors = floors, BestTimeMs = timeMs };
                return Store.SubmitScoreAsync(Ladders.TowerBoard, id, d.TowerBest.Score, country);
            }

            public Task Duel(string id, string country, int elo, bool ranked = true)
            {
                var d = Player(id, country);
                d.Season = Seasons.SeasonOf(Clock);
                d.Ranked = ranked;
                d.Elo = elo;
                return Store.SubmitEloAsync(id, elo, country);
            }

            public Task<LadderPage> Get(string me, Ladder ladder, LadderScope scope, int seasonsAgo = 0) =>
                Server.GetLadderAsync(me, ladder, scope, seasonsAgo);

            public void Later() => Clock = Clock.AddMinutes(Ladders.CacheMinutes + 1);
        }

        static List<string> Ids(LadderPage page) => page.Rows.Select(r => r.PlayerId).ToList();

        [Test]
        public async Task Tower_World_RanksFloorsThenTime_TiesByIdNeverMove()
        {
            var w = new World();
            await w.Tower("zed", "FR", 5, 9_000);
            await w.Tower("bob", "BE", 7, 20_000);
            await w.Tower("amy", "FR", 7, 20_000);
            await w.Tower("cat", "FR", 7, 10_000);
            var page = await w.Get("amy", Ladder.Tower, LadderScope.World);
            Assert.IsNull(page.Error);
            CollectionAssert.AreEqual(new[] { "cat", "amy", "bob", "zed" }, Ids(page), "floors, then time, then the id");
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, page.Rows.Select(r => r.Rank));
            Assert.AreEqual(2, page.Me.Rank);
            Assert.IsTrue(page.Me.IsMe && page.Rows[1].IsMe);
            Assert.AreEqual((7, 20_000, "FR"), (page.Me.Value, page.Me.TimeMs, page.Me.Country));
            Assert.AreEqual("BE", page.Rows[2].Country, "each row labelled with its player's country");
        }

        [Test]
        public async Task Tower_World_AViewerBelowTheTopKeepsTheServiceRank()
        {
            var w = new World();
            for (int i = 0; i < Ladders.Size + 4; i++) await w.Tower("p" + i.ToString("000"), "FR", 50, 1_000 + i);
            await w.Tower("me", "FR", 1, 99_000);
            var page = await w.Get("me", Ladder.Tower, LadderScope.World);
            Assert.AreEqual(Ladders.Size, page.Rows.Count);
            Assert.IsFalse(page.Rows.Any(r => r.IsMe));
            Assert.AreEqual(Ladders.Size + 5, page.Me.Rank);
            Assert.IsNull((await w.Get("nobody", Ladder.Tower, LadderScope.World)).Me, "no score: no row of one's own");
        }

        [Test]
        public async Task Country_FiltersByTheCountryHeldHere_AndNeedsOne()
        {
            var w = new World();
            await w.Tower("amy", "FR", 3, 5_000);
            await w.Tower("bob", "BE", 9, 5_000);
            await w.Tower("cat", "FR", 4, 5_000);
            var page = await w.Get("amy", Ladder.Tower, LadderScope.Country);
            Assert.AreEqual("FR", page.Country);
            CollectionAssert.AreEqual(new[] { "cat", "amy" }, Ids(page));
            Assert.AreEqual(2, page.Me.Rank, "second in France, third in the world");

            w.Player("dan", "");
            foreach (var ladder in new[] { Ladder.Duel, Ladder.Duo, Ladder.Tower, Ladder.Endless })
                Assert.AreEqual("NO_COUNTRY", (await w.Get("dan", ladder, LadderScope.Country)).Error);
            Assert.AreEqual("UNKNOWN_LADDER", (await w.Get("amy", (Ladder)9, LadderScope.World)).Error);
        }

        [Test]
        public async Task SetCountry_IsValidated_AndTheRowsWrittenFollowIt()
        {
            var w = new World();
            await w.Tower("amy", "", 3, 5_000);
            await w.Duel("amy", "", 1200);
            Assert.AreEqual("COUNTRY", (await w.Server.SetCountryAsync("amy", "fr")).Error);
            Assert.AreEqual("COUNTRY", (await w.Server.SetCountryAsync("amy", "FRA")).Error);
            Assert.AreEqual("COUNTRY", (await w.Server.SetCountryAsync("amy", "<b")).Error);
            Assert.AreEqual("NO_COUNTRY", (await w.Get("amy", Ladder.Tower, LadderScope.Country)).Error);

            Assert.IsTrue((await w.Server.SetCountryAsync("amy", "JP")).Ok);
            Assert.AreEqual("JP", w.Store.Players["amy"].Country);
            Assert.AreEqual("JP", w.Store.Countries[Ladders.TowerBoard + "/amy"], "the tower row rewritten with it");
            Assert.AreEqual("JP", w.Store.Countries[Ladders.DuelBoard + "/amy"], "and the month's Elo");
            Assert.AreEqual(5_000, Tower.DecodeScore(w.Store.Scores[Ladders.TowerBoard]["amy"]).timeMs, "with the same score");
            w.Later();
            var page = await w.Get("amy", Ladder.Tower, LadderScope.Country);
            CollectionAssert.AreEqual(new[] { "amy" }, Ids(page));

            Assert.IsTrue((await w.Server.SetCountryAsync("amy", "")).Ok, "\"\" clears it");
            Assert.AreEqual("NO_COUNTRY", (await w.Get("amy", Ladder.Tower, LadderScope.Country)).Error);
        }

        [Test]
        public async Task Friends_AreComputedFromTheirOwnData_WithTheViewer()
        {
            var w = new World();
            await w.Tower("amy", "FR", 3, 5_000);
            await w.Tower("bob", "BE", 9, 5_000);
            await w.Tower("cat", "FR", 4, 5_000);
            w.Player("bob", "BE").TowerBest.BestFloors = 12; // the data is the truth, whatever the board says
            await w.Server.SyncChatProfileAsync("amy", new List<string> { "bob", "ghost" }, false);
            var page = await w.Get("amy", Ladder.Tower, LadderScope.Friends);
            CollectionAssert.AreEqual(new[] { "bob", "amy" }, Ids(page), "a friend and the viewer, nobody else");
            Assert.AreEqual(12, page.Rows[0].Value);
            Assert.AreEqual("bob#1", page.Rows[0].PlayerName);
            Assert.AreEqual(2, page.Me.Rank);
        }

        [Test]
        public async Task Endless_RowsShowTheWeightedPoints()
        {
            var w = new World();
            w.Player("amy", "FR").EndlessCleared = new List<int> { 4, 0, 1 };
            w.Store.Players["amy"].EndlessTimeMs = new List<int> { 40_000, 0, 30_000 };
            w.Player("bob", "FR").EndlessCleared = new List<int> { 0, 3 };
            w.Store.Players["bob"].EndlessTimeMs = new List<int> { 0, 20_000 };
            await w.Server.SyncChatProfileAsync("amy", new List<string> { "bob" }, false);
            var page = await w.Get("amy", Ladder.Endless, LadderScope.Friends);
            CollectionAssert.AreEqual(new[] { "amy", "bob" }, Ids(page), "7 points beat 6");
            Assert.AreEqual((7, 70_000), (page.Rows[0].Value, page.Rows[0].TimeMs));
            Assert.AreEqual((6, 20_000), (page.Rows[1].Value, page.Rows[1].TimeMs));

            await w.Store.SubmitScoreAsync(Ladders.EndlessBoard, "amy", Ladders.EncodeEndless(7, 70_000), "FR");
            await w.Store.SubmitScoreAsync(Ladders.EndlessBoard, "bob", Ladders.EncodeEndless(7, 60_000), "FR");
            var world = await w.Get("amy", Ladder.Endless, LadderScope.World);
            CollectionAssert.AreEqual(new[] { "bob", "amy" }, Ids(world), "as many points: the shorter total time first");
        }

        [Test]
        public async Task Duel_RowsAreVerifiedAgainstTheProtectedData_InEveryScope()
        {
            var w = new World();
            await w.Duel("amy", "FR", 1300);
            await w.Duel("bob", "FR", 1250);
            await w.Duel("cat", "BE", 1400);
            await w.Store.SubmitEloAsync("bob", 5000, "FR");    // a score no duel gave
            await w.Store.SubmitEloAsync("eve", 9000, "FR");    // nobody the server knows
            w.Player("cat", "BE");
            await w.Store.SubmitEloAsync("cat", 1400, "FR");     // a stale label: cat is in Belgium now

            var world = await w.Get("amy", Ladder.Duel, LadderScope.World);
            CollectionAssert.AreEqual(new[] { "cat", "amy", "bob" }, Ids(world));
            Assert.AreEqual(1250, world.Rows[2].Value);
            Assert.AreEqual("BE", world.Rows[0].Country, "the country held here");
            Assert.AreEqual(Seasons.SeasonOf(Now), world.Season);

            w.Later();
            var france = await w.Get("amy", Ladder.Duel, LadderScope.Country);
            CollectionAssert.AreEqual(new[] { "amy", "bob" }, Ids(france));
            Assert.AreEqual(1250, france.Rows[1].Value);
            Assert.AreEqual(1, france.Me.Rank);

            await w.Server.SyncChatProfileAsync("bob", new List<string> { "cat", "eve" }, false);
            var friends = await w.Get("bob", Ladder.Duel, LadderScope.Friends);
            CollectionAssert.AreEqual(new[] { "cat", "bob" }, Ids(friends));
            Assert.AreEqual(2, friends.Me.Rank);
        }

        static void Duo(MemoryPvpStore store, string a, string b, string countryA, string countryB, int elo, int wins = 1)
        {
            string id = a + "+" + b;
            string key = PvpServer.IndexCollection + "/" + PvpServer.IndexShard(PvpServer.DuoIndexKey, id);
            if (!(store.Shared.TryGetValue(key, out var o) && o is List<DuoSummary> list)) store.Shared[key] = list = new List<DuoSummary>();
            list.Add(new DuoSummary
            {
                Id = id, Name = a + " & " + b, Members = new List<string> { a, b }, Names = new List<string> { a, b },
                Countries = new List<string> { countryA, countryB }, Elo = elo, Wins = wins,
            });
        }

        [Test]
        public async Task Duo_ShowsOnTheCountryOfEitherMember_AndTheFriendsOfEither()
        {
            var w = new World();
            w.Player("amy", "FR");
            Duo(w.Store, "amy", "bob", "FR", "BE", 1100);
            Duo(w.Store, "cat", "dan", "BE", "DE", 1300);
            Duo(w.Store, "eve", "fay", "IT", "FR", 1200);
            Duo(w.Store, "gus", "hal", "FR", "FR", 1500, wins: 0); // never played: not ranked

            var world = await w.Get("amy", Ladder.Duo, LadderScope.World);
            CollectionAssert.AreEqual(new[] { "cat", "eve", "amy" }, Ids(world));
            Assert.AreEqual(("bob", "BE"), (world.Rows[2].MateId, world.Rows[2].MateCountry));
            Assert.AreEqual(3, world.Me.Rank);

            var france = await w.Get("amy", Ladder.Duo, LadderScope.Country);
            CollectionAssert.AreEqual(new[] { "eve", "amy" }, Ids(france), "a duo counts for the country of either member");
            Assert.AreEqual(2, france.Me.Rank);

            await w.Server.SyncChatProfileAsync("amy", new List<string> { "dan" }, false);
            var friends = await w.Get("amy", Ladder.Duo, LadderScope.Friends);
            CollectionAssert.AreEqual(new[] { "cat", "amy" }, Ids(friends), "a friend in either seat");

            Assert.AreEqual(3, (await w.Server.GetDuoBoardAsync(10)).Rows.Count, "the 0.1.14 duo board still answers");
        }

        [Test]
        public async Task ADuoIndexEntry_CarriesItsMembersCountries_AndFollowsAChange()
        {
            var w = new World();
            w.Player("amy", "FR");
            w.Player("bob", "BE");
            await w.Server.SyncChatProfileAsync("bob", new List<string> { "amy" }, false);
            await w.Server.InviteDuoAsync("amy", "Amy", "bob");
            await w.Server.RespondDuoAsync("bob", "Bob", w.Store.Players["bob"].DuoInvites[0].Id, true);
            DuoSummary Summary() => w.Store.Shared.Values.OfType<List<DuoSummary>>().SelectMany(l => l).Single();
            CollectionAssert.AreEqual(new[] { "FR", "BE" }, Summary().Countries);
            CollectionAssert.AreEqual(new[] { "Amy", "Bob" }, Summary().Names);
            await w.Server.SetCountryAsync("bob", "DE");
            CollectionAssert.AreEqual(new[] { "FR", "DE" }, Summary().Countries);
        }

        // ------------------------------------------------------------------ names

        const string Injected = "<size=200><color=red>ADMIN</color></size>";

        [Test]
        public void CleanName_StripsTags_KeepsNull()
        {
            string clean = PvpServer.CleanName(Injected);
            Assert.IsFalse(clean.Contains("<") || clean.Contains(">"), clean);
            Assert.LessOrEqual(clean.Length, 24);
            Assert.IsNull(PvpServer.CleanName(null), "null: the caller's fallback applies");
            Assert.AreEqual("?", PvpServer.CleanName(" \n "));
        }

        [Test]
        public async Task NamesFromPhones_AreCleanedWhereTheServerReceivesThem()
        {
            var w = new World();
            await w.Server.SyncChatProfileAsync("bob", new List<string> { "amy" }, false);
            await w.Server.InviteDuoAsync("amy", Injected, "bob");
            var invite = w.Store.Players["bob"].DuoInvites[0];
            Assert.IsFalse(invite.FromName.Contains("<"), invite.FromName);
            await w.Server.RespondDuoAsync("bob", "\u0007Bob<b>", invite.Id, true);
            var duo = w.Store.Shared.Values.OfType<Duo>().Single();
            Assert.IsFalse(duo.Names.Any(n => n.Contains("<") || n.Contains("\u0007")), string.Join(",", duo.Names));

            Assert.AreEqual("NAME", (await w.Server.RegisterNameAsync("amy", "<b>amy</b>#12")).Error, "the directory refuses it outright");

            await w.Tower("<i>eve", "FR", 2, 1_000); // the service keeps the name the phone chose
            var page = await w.Get("eve", Ladder.Tower, LadderScope.World);
            Assert.IsFalse(page.Rows.Any(r => r.PlayerName.Contains("<")));
        }
    }
}
