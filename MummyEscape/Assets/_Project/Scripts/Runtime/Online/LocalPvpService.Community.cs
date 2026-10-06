using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MummyEscape.Pvp;

namespace MummyEscape.Online
{
    /// <summary>
    /// The demo's simulated community, so the chat and the guild wars can be tried without a server: the demo guilds have
    /// members who talk in their channel, past wars against each other (with replays) and fight the player's guild for
    /// real; the global channel and the demo friends chat back; a few 2v2 replays are shared.
    /// </summary>
    public sealed partial class LocalPvpService
    {
        static readonly string[] GlobalLines =
        {
            "Salut tout le monde !", "Quelqu'un a passé le tombeau 3-7 sans se faire piquer ?", // noloc
            "Les miroirs de l'acte 2 me rendent fou", "Qui pour un 2v2 ce soir ?", "GG à ceux d'hier, belle course", // noloc
            "Astuce : la dalle tournante, on peut la passer en deux coups", "Je cherche une guilde active, des idées ?", // noloc
            "Le skin de la saison est trop beau", "Encore mort à un pas de la sortie…", "Ligue or atteinte enfin !", // noloc
            "Vous jouez plutôt de mémoire ou au feeling ?", "Le casino m'a donné un légendaire, je n'y crois pas", // noloc
            "Bonne nuit les momies", "Les Fils d'Anubis recrutent, venez !", "Quelqu'un a compris les pièges de l'acte 3 ?", // noloc
        };

        static readonly string[] GuildLines =
        {
            "On lance une guerre ce soir ?", "Je prends la première manche", "Bien joué pour la dernière guerre !", // noloc
            "Pensez à courir vos manches avant demain", "J'ai perdu de peu, désolé l'équipe", "Encore 40 points pour le prochain skin", // noloc
            "Qui est dispo pour un 2v2 ?", "Le tombeau de la manche 2 est piégeux, attention aux piques", // noloc
            "Bienvenue aux nouveaux !", "On est remontés dans le classement des guildes", "Je cours ma manche dans 5 min", // noloc
        };

        static readonly string[] Replies =
        {
            "Carrément !", "Pareil pour moi", "Haha oui", "Bonne idée", "Je suis partant", "Pas faux", // noloc
            "Bien vu", "Moi aussi j'ai galéré", "On verra ça", "Courage !", "+1", // noloc
        };

        static readonly string[] AnswersToQuestions =
        {
            "Je pense que oui", "Aucune idée, désolé", "Oui, il faut longer le mur de gauche", // noloc
            "Ça dépend du tombeau", "Teste et tu verras !", // noloc
        };

        static readonly string[] FriendAnswers =
        {
            "Oui, quand tu veux !", "Pas ce soir, demain ?", "Carrément, invite-moi", "Je finis mon tombeau et j'arrive", // noloc
        };

        static readonly string[] FriendLines =
        {
            "Coucou ! Ça va ?", "Tu joues ce soir ?", "On se fait un 2v2 ?", "Bien joué pour ton dernier duel !", // noloc
            "Je viens de finir l'acte 2", "Haha trop bien", "Ok ça marche", "Dis-moi quand tu es prêt", // noloc
        };

        /// <summary>The demo friends of <see cref="OfflineOnlineService"/>.</summary>
        static readonly string[] DemoFriendIds = { "demo-Nefertari#2041", "demo-Imhotep#7310", "demo-Tiye#5562" }; // noloc

        const int AmbientMinMs = 20_000, AmbientMaxMs = 50_000;

        readonly Dictionary<string, long> _nextAmbient = new Dictionary<string, long>();
        readonly HashSet<string> _seededChannels = new HashSet<string>();
        readonly HashSet<string> _announcedWars = new HashSet<string>();
        readonly long _launchedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        bool _chatSeeded;

        static long NowUnixMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        string Pick(string[] lines)
        {
            lock (_rng) return lines[_rng.Next(lines.Length)];
        }

        int Roll(int min, int max)
        {
            lock (_rng) return _rng.Next(min, max);
        }

        // ------------------------------------------------------------------ members

        /// <summary>Every simulated member knows its guild, so it writes in the guild channel (and leads it) like a player.</summary>
        void EnrollDemoMembers()
        {
            foreach (var g in _store.Shared.Values.OfType<Guild>().ToList())
                foreach (var m in g.Members)
                {
                    if (m.PlayerId == Me) continue;
                    if (!_store.Players.TryGetValue(m.PlayerId, out var d)) _store.Players[m.PlayerId] = d = new PlayerPvpData();
                    d.GuildId = g.Id;
                    lock (_names) _names[m.PlayerId] = m.Name;
                }
        }

        Guild MyGuild()
        {
            _store.Players.TryGetValue(Me, out var d);
            return d?.GuildId == null ? null : SharedOf<Guild>(PvpServer.GuildsCollection, d.GuildId);
        }

        GuildMember SomeMember(Guild g, string except = null)
        {
            var others = g?.Members.Where(m => m.PlayerId != Me && m.PlayerId != except).ToList();
            if (others == null || others.Count == 0) return null;
            lock (_rng) return others[_rng.Next(others.Count)];
        }

        // ------------------------------------------------------------------ wars

        /// <summary>The rival of a war: the demo guild of the closest war Elo that is free, with its real members.</summary>
        BattleSide DemoWarSide(int slots, int elo)
        {
            string mine = MyGuild()?.Id;
            var guilds = _store.Shared.Values.OfType<Guild>()
                .Where(g => g.Id != mine && g.Members.Count > 0 && g.Member(Me) == null && g.ActiveWar == null)
                .ToList();
            if (guilds.Count == 0) return null;
            Guild rival;
            var order = new List<string>();
            lock (_rng)
            {
                rival = guilds.OrderBy(g => Math.Abs(g.WarElo - elo) + _rng.Next(150)).First();
                var members = rival.Members.Select(m => m.PlayerId).OrderBy(_ => _rng.Next()).ToList();
                // A small guild sends its best runners twice.
                for (int i = 0; i < slots; i++) order.Add(members[i % members.Count]);
            }
            return TeamLogic.NewSide(rival.Id, $"[{rival.Tag}] {rival.Name}", rival.WarElo, order, id => rival.Member(id)?.Name);
        }

        /// <summary>Past wars between the demo guilds, run by their members, so a guild shows its recent wars and their replays.</summary>
        void SeedDemoWars()
        {
            var guilds = Enumerable.Range(0, DemoGuilds.Length)
                .Select(i => SharedOf<Guild>(PvpServer.GuildsCollection, "demo_guild_" + i)) // noloc
                .ToList();
            if (guilds.Any(g => g == null) || guilds.Any(g => g.RecentWars.Count > 0)) return;
            var rng = new Random(0x3a12);
            var pairs = new[] { (0, 1), (2, 3), (4, 5), (0, 2), (1, 4), (3, 5) };
            long now = NowUnixMs;
            for (int p = 0; p < pairs.Length; p++)
            {
                var (ia, ib) = pairs[p];
                Guild a = guilds[ia], b = guilds[ib];
                long at = now - (p + 1) * 7 * 3_600_000L;
                var war = TeamLogic.NewBattle("demo_war_" + p, BattleKind.GuildWar, 3, () => rng.Next(), Core.DifficultyTable.GeneratorVersion, at, // noloc
                                              Side(a, rng));
                TeamLogic.Join(war, Side(b, rng), at);
                for (int k = 0; k < war.Slots; k++)
                {
                    TeamLogic.Record(war, true, k, _server.BotRun(war.A.Order[k], war.Seeds[k], war.A.Elo), at);
                    TeamLogic.Record(war, false, k, _server.BotRun(war.B.Order[k], war.Seeds[k], war.B.Elo), at);
                }
                TeamLogic.CheckFinished(war, at);
                war.Applied = true; // the guilds' records already count it
                _store.Shared[PvpServer.BattlesCollection + "/" + war.Id] = war;
                a.RecentWars.Add(war.Id);
                b.RecentWars.Add(war.Id);
            }

            static BattleSide Side(Guild g, Random rng)
            {
                var order = g.Members.Select(m => m.PlayerId).OrderBy(_ => rng.Next()).Take(3).ToList();
                return TeamLogic.NewSide(g.Id, $"[{g.Tag}] {g.Name}", g.WarElo, order, id => g.Member(id)?.Name);
            }
        }

        /// <summary>Joining a demo guild: its leader makes the player an officer, so wars can be launched.</summary>
        async Task WelcomeAsync(string playerName)
        {
            var guild = MyGuild();
            var leader = guild?.Members.Find(m => m.Role == GuildRole.Leader && m.PlayerId != Me);
            if (leader == null) return;
            await _server.SetGuildRoleAsync(leader.PlayerId, Me, true);
            await Say(leader, ChatConfig.Guild, $"Bienvenue {playerName} ! Je t'ai nommé officier : lance une guerre quand tu veux."); // noloc
        }

        /// <summary>The guild talks about its wars: one launched, one won or lost since the game started.</summary>
        async Task AnnounceWarsAsync(GuildResponse r)
        {
            var guild = r?.Guild;
            if (guild == null) return;
            foreach (var war in r.Wars)
            {
                if (war.B == null || war.CreatedAtUnixMs < _launchedAtUnixMs) continue;
                bool mineA = war.A.TeamId == guild.Id;
                string rival = (mineA ? war.B : war.A).TeamName;
                string key = war.Id + (war.Finished ? "_end" : "_start"); // noloc
                if (!_announcedWars.Add(key)) continue;
                var member = SomeMember(guild);
                if (member == null) continue;
                if (!war.Finished)
                {
                    await Say(member, ChatConfig.Guild, $"Guerre lancée contre {rival} ! Courez vos manches, on compte sur vous."); // noloc
                    continue;
                }
                var (winsA, winsB) = TeamLogic.Score(war);
                int us = mineA ? winsA : winsB, them = mineA ? winsB : winsA;
                var result = mineA ? war.Result : DuelResolver.Invert(war.Result);
                await Say(member, ChatConfig.Guild, result == DuelResult.Win ? $"Victoire contre {rival}, {us} à {them} ! Bravo à tous" // noloc
                                                  : result == DuelResult.Loss ? $"Défaite contre {rival}, {us} à {them}. On se rattrapera !" // noloc
                                                  : $"Égalité contre {rival}, {us} à {them}."); // noloc
            }
        }

        // ------------------------------------------------------------------ chat

        Task<ChatSendResponse> Say(GuildMember member, string channel, string text) =>
            _server.SendChatAsync(member.PlayerId, member.Name, channel, text);

        /// <summary>A day of conversation in the global channel, in each guild and with the demo friends (once a launch).</summary>
        void SeedDemoChat()
        {
            if (_chatSeeded) return;
            _chatSeeded = true;
            long now = NowUnixMs;
            var rng = new Random(0xc4a7);

            var global = new ChatChannel { Key = "global" }; // noloc
            var lines = GlobalLines.OrderBy(_ => rng.Next()).Take(12).ToList();
            long at = now - 4 * 3_600_000L;
            for (int i = 0; i < lines.Count; i++)
            {
                at += (5 + rng.Next(15)) * 60_000L;
                string id = "chat_" + rng.Next(DemoNames.Length); // noloc
                Add(global, id, ChatterName(id, rng), lines[i], at);
                if (i == 7)
                {
                    at += 3 * 60_000L;
                    var (shared, replayRef) = BotRelay(rng, at);
                    Add(global, shared.OwnerId, shared.OwnerName, replayRef.Result == DuelResult.Draw ? "Égalité parfaite, regardez ce relais !" : "Regardez ce relais, on gagne sur le fil !", at, replayRef); // noloc
                }
            }
            _store.Shared[PvpServer.ChatCollection + "/global"] = global; // noloc

            foreach (var guild in _store.Shared.Values.OfType<Guild>().ToList()) SeedGuildChat(guild, rng);

            // Two demo friends wrote: one conversation unread, one answered.
            var inbox = SharedOf<ChatInbox>(PvpServer.ChatInboxCollection, Me) ?? new ChatInbox();
            SeedDirect(inbox, DemoFriendIds[2], new[] { (false, "Tu as vu la nouvelle saison du Pass ?"), (true, "Oui, le skin d'Anubis est top"), (false, "Haha je l'ai déjà !") }, now - 5 * 3_600_000L); // noloc
            SeedDirect(inbox, DemoFriendIds[0], new[] { (false, "Coucou !"), (false, "On se fait un 2v2 ce soir ?") }, now - 25 * 60_000L); // noloc
            _store.Shared[PvpServer.ChatInboxCollection + "/" + Me] = inbox;
        }

        string ChatterName(string id, Random rng)
        {
            lock (rng) lock (_names) // the order used everywhere: random first, then names
            {
                if (!_names.TryGetValue(id, out var name)) _names[id] = name = DemoNames[rng.Next(DemoNames.Length)] + "#" + (1000 + rng.Next(9000));
                return name;
            }
        }

        void SeedGuildChat(Guild guild, Random rng)
        {
            string key = "guild_" + guild.Id; // noloc
            if (!_seededChannels.Add(key) || SharedOf<ChatChannel>(PvpServer.ChatCollection, key) != null) return;
            var members = guild.Members.Where(m => m.PlayerId != Me).ToList();
            if (members.Count == 0) return;
            long now = NowUnixMs;
            var room = new ChatChannel { Key = key };
            var lines = GuildLines.OrderBy(_ => rng.Next()).Take(6 + rng.Next(3)).ToList();
            long at = now - 6 * 3_600_000L;
            foreach (var line in lines)
            {
                at += (10 + rng.Next(40)) * 60_000L;
                var m = members[rng.Next(members.Count)];
                Add(room, m.PlayerId, m.Name, line, at);
            }
            _store.Shared[PvpServer.ChatCollection + "/" + key] = room;
        }

        void SeedDirect(ChatInbox inbox, string friendId, (bool mine, string text)[] lines, long startUnixMs)
        {
            string key = string.CompareOrdinal(Me, friendId) < 0 ? "dm_" + Me + "_" + friendId : "dm_" + friendId + "_" + Me; // noloc
            if (SharedOf<ChatChannel>(PvpServer.ChatCollection, key) != null) return;
            var room = new ChatChannel { Key = key };
            string friendName = BotNameOf(friendId);
            ChatMessage last = null;
            for (int i = 0; i < lines.Length; i++)
                last = Add(room, lines[i].mine ? Me : friendId, lines[i].mine ? NameOf(Me) : friendName, lines[i].text, startUnixMs + i * 4 * 60_000L);
            _store.Shared[PvpServer.ChatCollection + "/" + key] = room;
            inbox.Conversations.RemoveAll(c => c.Other == friendId);
            inbox.Conversations.Insert(0, new ChatConversation
            {
                Other = friendId, OtherName = friendName, LastSeq = last.Seq, LastAtUnixMs = last.AtUnixMs, LastFrom = last.From, LastText = last.Text,
            });
            inbox.Conversations.Sort((x, y) => y.LastAtUnixMs.CompareTo(x.LastAtUnixMs));
        }

        static ChatMessage Add(ChatChannel room, string from, string name, string text, long at, ChatReplayRef replay = null)
        {
            var m = new ChatMessage { Seq = room.NextSeq++, From = from, FromName = name, AtUnixMs = at, Text = text, Replay = replay };
            room.Messages.Add(m);
            return m;
        }

        /// <summary>A 2v2 between two simulated duos, kept as a shared replay.</summary>
        (SharedReplay shared, ChatReplayRef replayRef) BotRelay(Random rng, long at)
        {
            int seed = rng.Next();
            var map = PvpServer.RelayArenaFor(seed);
            RelaySide Duo(string id, int elo)
            {
                var race = RelayBots.Play(map, rng, elo);
                var side = new RelaySide { DuoId = id, Elo = elo, Inputs = new List<RelayInput>(race.Inputs), Verified = RelaySummary.Of(race) };
                for (int r = 0; r < 2; r++)
                {
                    string runner = id + "_" + r;
                    side.Runners.Add(new RelayRunner { PlayerId = runner, Name = ChatterName(runner, rng), Look = Visual.PvpSkins.RandomLook(rng) });
                }
                side.Name = side.Runners[0].Name.Split('#')[0] + " & " + side.Runners[1].Name.Split('#')[0];
                side.Starter = side.Runners[0].PlayerId;
                return side;
            }
            var match = new RelayMatch
            {
                Id = "demo_relay_" + seed, Seed = seed, GeneratorVersion = Core.DifficultyTable.GeneratorVersion, CreatedAtUnixMs = at, Settled = true, // noloc
                A = Duo("chatduo_a", 1250), B = Duo("chatduo_b", 1150), // noloc
            };
            match.Result = RelayJudge.Resolve(match.A.Verified, match.B.Verified);
            // Shared by the duo that won (proud of it).
            bool aShares = match.Result != DuelResult.Loss;
            RelaySide sharing = aShares ? match.A : match.B, other = aShares ? match.B : match.A;
            var owner = sharing.Runners[0];
            var shared = new SharedReplay
            {
                Id = "demo_share_" + seed, Kind = ChatConfig.RelayReplay, OwnerId = owner.PlayerId, OwnerName = owner.Name, SharedAtUnixMs = at, Relay = match, // noloc
            };
            _store.Shared[PvpServer.SharedReplaysCollection + "/" + shared.Id] = shared;
            var result = aShares ? match.Result : DuelResult.Win;
            return (shared, new ChatReplayRef { Id = shared.Id, Kind = shared.Kind, Result = result, Title = sharing.Name + " — " + other.Name });
        }

        /// <summary>While a channel is read, someone says something now and then.</summary>
        async Task AmbientAsync(string channel)
        {
            long now = NowUnixMs;
            if (!_nextAmbient.TryGetValue(channel, out long next)) next = _nextAmbient[channel] = now + Roll(AmbientMinMs / 2, AmbientMaxMs / 2);
            if (now < next) return;
            _nextAmbient[channel] = now + Roll(AmbientMinMs, AmbientMaxMs);
            if (channel == ChatConfig.Global)
            {
                string id = "chat_" + Roll(0, DemoNames.Length); // noloc
                await _server.SendChatAsync(id, ChatterName(id, _rng), channel, Pick(GlobalLines));
            }
            else if (channel == ChatConfig.Guild)
            {
                var member = SomeMember(MyGuild());
                if (member != null) await Say(member, channel, Pick(GuildLines));
            }
        }

        /// <summary>Someone answers the player a few seconds later: always a friend in private, often the guild, sometimes the global.</summary>
        async Task ReplyLaterAsync(string channel, string text)
        {
            bool direct = channel.StartsWith(ChatConfig.DirectPrefix, StringComparison.Ordinal);
            int chance = direct ? 100 : channel == ChatConfig.Guild ? 75 : 50;
            if (Roll(0, 100) >= chance) return;
            await Task.Delay(Roll(2000, 5000));
            bool question = text.Contains("?");
            string line = direct ? Pick(question ? FriendAnswers : FriendLines) : Pick(question ? AnswersToQuestions : Replies);
            await Run(async () =>
            {
                if (direct)
                {
                    string friend = channel.Substring(ChatConfig.DirectPrefix.Length);
                    return await _server.SendChatAsync(friend, BotNameOf(friend), ChatConfig.Direct(Me), line);
                }
                if (channel == ChatConfig.Guild)
                {
                    var member = SomeMember(MyGuild());
                    return member == null ? null : await Say(member, channel, line);
                }
                string id = "chat_" + Roll(0, DemoNames.Length); // noloc
                return await _server.SendChatAsync(id, ChatterName(id, _rng), channel, line);
            });
        }
    }
}
