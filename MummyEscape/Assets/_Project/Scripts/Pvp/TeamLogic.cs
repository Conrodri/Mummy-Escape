// Mummy Escape — règles des combats d'équipe (2v2 et guerres de guildes), sans stockage : testables telles quelles.
using System;
using System.Collections.Generic;

namespace MummyEscape.Pvp
{
    public static class TeamLogic
    {
        /// <summary>
        /// Ordre de passage d'un duo sur les 3 manches : il alterne, le premier coureur court deux fois (A-B-A ou B-A-B).
        /// </summary>
        public static List<string> DuoOrder(Duo duo, bool creatorFirst)
        {
            string a = duo.Members[creatorFirst ? 0 : 1], b = duo.Members[creatorFirst ? 1 : 0];
            return new List<string> { a, b, a };
        }

        /// <summary>Vérifie l'ordre d'une guerre : la bonne taille, chaque coureur une seule fois, tous membres. Null si valide.</summary>
        public static string CheckWarOrder(Guild guild, IList<string> order, int size)
        {
            if (Array.IndexOf(TeamConfig.WarSizes, size) < 0) return "ORDER";
            if (order == null || order.Count != size) return "ORDER";
            var seen = new HashSet<string>();
            foreach (var id in order)
                if (string.IsNullOrEmpty(id) || !seen.Add(id) || guild.Member(id) == null) return "ORDER";
            return null;
        }

        public static BattleSide NewSide(string teamId, string teamName, int elo, IList<string> order, Func<string, string> nameOf)
        {
            var side = new BattleSide { TeamId = teamId, TeamName = teamName, Elo = elo };
            foreach (var id in order)
            {
                side.Order.Add(id);
                side.Names.Add(nameOf(id) ?? "");
                side.Runs.Add(null);
            }
            return side;
        }

        public static TeamBattle NewBattle(string id, BattleKind kind, int slots, Func<int> newSeed, int generatorVersion, long nowMs, BattleSide a)
        {
            var b = new TeamBattle { Id = id, Kind = kind, Slots = slots, GeneratorVersion = generatorVersion, CreatedAtUnixMs = nowMs, A = a };
            for (int i = 0; i < slots; i++)
            {
                b.Seeds.Add(newSeed());
                b.SlotResults.Add(-1);
            }
            return b;
        }

        /// <summary>Le second camp rejoint : la fenêtre de <see cref="TeamConfig.BattleHours"/> heures commence.</summary>
        public static void Join(TeamBattle b, BattleSide side, long nowMs)
        {
            b.B = side;
            b.StartedAtUnixMs = nowMs;
            b.DeadlineUnixMs = nowMs + TeamConfig.BattleHours * 3_600_000L;
        }

        /// <summary>Le camp de ce joueur dans ce combat (true = A), null s'il n'y court pas.</summary>
        public static bool? SideOf(TeamBattle b, string playerId)
        {
            if (b.A != null && b.A.Order.Contains(playerId)) return true;
            if (b.B != null && b.B.Order.Contains(playerId)) return false;
            return null;
        }

        /// <summary>Le camp d'une équipe (duo ou guilde) dans ce combat, null si elle n'y est pas.</summary>
        public static bool? SideOfTeam(TeamBattle b, string teamId)
        {
            if (b.A != null && b.A.TeamId == teamId) return true;
            if (b.B != null && b.B.TeamId == teamId) return false;
            return null;
        }

        /// <summary>
        /// La manche que ce joueur peut courir maintenant : la première manche de son camp pas encore courue, si c'est la
        /// sienne (un camp court ses manches dans l'ordre). -1 sinon.
        /// </summary>
        public static int NextSlot(TeamBattle b, string playerId)
        {
            if (b.Finished || b.B == null) return -1;
            var sideA = SideOf(b, playerId);
            if (sideA == null) return -1;
            var side = sideA.Value ? b.A : b.B;
            for (int k = 0; k < b.Slots; k++)
            {
                if (side.Runs[k] != null || b.SlotResults[k] >= 0) continue;
                return side.Order[k] == playerId ? k : -1;
            }
            return -1;
        }

        /// <summary>Le coureur que son camp attend (prochaine manche non courue), null si plus personne.</summary>
        public static string Awaited(TeamBattle b, bool sideA)
        {
            if (b.Finished || b.B == null) return null;
            var side = sideA ? b.A : b.B;
            for (int k = 0; k < b.Slots; k++)
                if (side.Runs[k] == null && b.SlotResults[k] < 0) return side.Order[k];
            return null;
        }

        /// <summary>Enregistre une course ; décide la manche si les deux camps l'ont courue, puis le combat si c'est fini.</summary>
        public static void Record(TeamBattle b, bool sideA, int slot, SlotRun run, long nowMs)
        {
            (sideA ? b.A : b.B).Runs[slot] = run;
            ResolveSlot(b, slot);
            CheckFinished(b, nowMs);
        }

        static void ResolveSlot(TeamBattle b, int k)
        {
            if (b.SlotResults[k] >= 0 || b.B == null) return;
            var ra = b.A.Runs[k];
            var rb = b.B.Runs[k];
            if (ra == null || rb == null) return;
            b.SlotResults[k] = (int)DuelResolver.Resolve(ra.Outcome, ra.TimeMs, ra.Progress, rb.Outcome, rb.TimeMs, rb.Progress);
        }

        public static (int winsA, int winsB) Score(TeamBattle b)
        {
            int a = 0, c = 0;
            foreach (int r in b.SlotResults)
            {
                if (r == (int)DuelResult.Win) a++;
                else if (r == (int)DuelResult.Loss) c++;
            }
            return (a, c);
        }

        /// <summary>
        /// Termine le combat si c'est joué : en 2v2 dès qu'un duo a 2 victoires, sinon quand toutes les manches sont
        /// décidées. Passé la date limite, chaque manche non courue compte comme un abandon. Renvoie true s'il est fini.
        /// </summary>
        public static bool CheckFinished(TeamBattle b, long nowMs)
        {
            if (b.Finished) return true;
            if (b.B == null) return false;
            bool late = nowMs > b.DeadlineUnixMs;
            for (int k = 0; k < b.Slots; k++)
            {
                if (b.SlotResults[k] >= 0) continue;
                if (late)
                {
                    b.A.Runs[k] = b.A.Runs[k] ?? Forfeit(b.A.Order[k], b.A.Names[k]);
                    b.B.Runs[k] = b.B.Runs[k] ?? Forfeit(b.B.Order[k], b.B.Names[k]);
                }
                ResolveSlot(b, k);
            }

            var (winsA, winsB) = Score(b);
            bool allDecided = !b.SlotResults.Contains(-1);
            bool earlyDuo = b.Kind == BattleKind.Duo && (winsA >= TeamConfig.DuoWinsNeeded || winsB >= TeamConfig.DuoWinsNeeded);
            if (!allDecided && !earlyDuo) return false;
            b.Finished = true;
            b.Result = winsA > winsB ? DuelResult.Win : winsA < winsB ? DuelResult.Loss : DuelResult.Draw;
            return true;
        }

        static SlotRun Forfeit(string playerId, string name) =>
            new SlotRun { PlayerId = playerId, PlayerName = name, Outcome = RunOutcome.Abandoned };

        /// <summary>
        /// Ce qu'un joueur voit d'un combat : tant qu'une manche n'est pas décidée, la course adverse est cachée (on sait
        /// seulement qu'elle est courue) — personne ne peut repérer un tombeau avant d'y courir.
        /// </summary>
        public static TeamBattle ViewFor(TeamBattle b, string viewerTeamId)
        {
            var mine = SideOfTeam(b, viewerTeamId);
            var copy = (TeamBattle)b.MemberwiseCloneBattle();
            copy.A = Hide(b, b.A, mine == false);
            copy.B = Hide(b, b.B, mine == true);
            copy.ViewerTeam = viewerTeamId;
            return copy;
        }

        static BattleSide Hide(TeamBattle b, BattleSide side, bool opponent)
        {
            if (side == null || !opponent) return side;
            var copy = new BattleSide { TeamId = side.TeamId, TeamName = side.TeamName, Elo = side.Elo, Order = side.Order, Names = side.Names };
            for (int k = 0; k < side.Runs.Count; k++)
            {
                var r = side.Runs[k];
                bool decided = k < b.SlotResults.Count && b.SlotResults[k] >= 0;
                copy.Runs.Add(r == null || decided ? r : new SlotRun { PlayerId = r.PlayerId, PlayerName = r.PlayerName, Look = r.Look, Inputs = null });
            }
            return copy;
        }

        /// <summary>Une course hors manche décidée dont on ne montre rien (voir <see cref="ViewFor"/>).</summary>
        public static bool IsHidden(SlotRun r) => r != null && r.Inputs == null;

        /// <summary>Les skins de guilde atteints avec ces points.</summary>
        public static List<string> GuildRewards(int points)
        {
            var list = new List<string>();
            foreach (var (need, reward) in TeamConfig.GuildSkinTiers)
                if (points >= need) list.Add(reward);
            return list;
        }

        public static string CheckGuildName(string name, string tag)
        {
            name = name?.Trim();
            tag = tag?.Trim();
            if (string.IsNullOrEmpty(name) || name.Length < TeamConfig.GuildNameMin || name.Length > TeamConfig.GuildNameMax) return "NAME";
            if (string.IsNullOrEmpty(tag) || tag.Length < TeamConfig.GuildTagMin || tag.Length > TeamConfig.GuildTagMax) return "TAG";
            foreach (char c in tag) if (!char.IsLetterOrDigit(c)) return "TAG";
            foreach (char c in name) if (char.IsControl(c) || c == '<' || c == '>') return "NAME";
            return null;
        }
    }

    static class TeamBattleCopy
    {
        /// <summary>Copie de surface (les camps sont remplacés par l'appelant).</summary>
        public static TeamBattle MemberwiseCloneBattle(this TeamBattle b) => new TeamBattle
        {
            Id = b.Id, Kind = b.Kind, Slots = b.Slots, Seeds = b.Seeds, GeneratorVersion = b.GeneratorVersion,
            CreatedAtUnixMs = b.CreatedAtUnixMs, StartedAtUnixMs = b.StartedAtUnixMs, DeadlineUnixMs = b.DeadlineUnixMs,
            A = b.A, B = b.B, SlotResults = b.SlotResults, Finished = b.Finished, Result = b.Result, Applied = b.Applied,
            EloDeltaA = b.EloDeltaA, EloDeltaB = b.EloDeltaB,
        };
    }
}
