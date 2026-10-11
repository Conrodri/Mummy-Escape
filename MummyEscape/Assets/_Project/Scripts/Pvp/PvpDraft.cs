// Mummy Rush PvP — le tirage des cartes avant un match : trois tombeaux propres au PvP, tirés de la graine du match et
// de la difficulté de la ligue ; chaque camp en bannit un (en 2v2, au vote de ses deux joueurs) et le restant est joué.
// Même graine + mêmes bannissements = même tombeau, sur les téléphones et sur le serveur, qui le régénère pour rejouer
// les courses.
using System;
using System.Collections.Generic;
using MummyEscape.Core;

namespace MummyEscape.Pvp
{
    /// <summary>Le vote d'un joueur (ou d'un bot) : la carte qu'il bannit.</summary>
    [Serializable]
    public class DraftVote
    {
        public string PlayerId;
        /// <summary>0 = camp A, 1 = camp B.</summary>
        public int Team;
        public bool Voted;
        /// <summary>La carte bannie (0 à 2) ; -1 : pas de choix à temps, le bannissement est tiré au sort.</summary>
        public int Map = -1;
    }

    /// <summary>Le tirage d'un match, tel que le serveur le garde.</summary>
    [Serializable]
    public class MapDraft
    {
        /// <summary>Graine du tirage (les cartes, les bannissements au hasard).</summary>
        public int Seed;
        /// <summary>Les graines des trois tombeaux proposés, du plus facile au plus dur (<see cref="PvpArena.Generate"/>).</summary>
        public List<int> Maps = new List<int>();
        public List<DraftVote> Votes = new List<DraftVote>();
        /// <summary>Premier vote d'un joueur : les autres ont <see cref="PvpDraft.WindowMs"/> pour voter, ensuite le hasard vote pour eux.</summary>
        public long FirstVoteUnixMs;
        /// <summary>Fin du tirage (0 tant qu'il est ouvert) : le match commence.</summary>
        public long ClosedUnixMs;
        /// <summary>La carte bannie par chaque camp, une fois le tirage fini.</summary>
        public List<int> Bans = new List<int>();
        /// <summary>La carte jouée (0 à 2), -1 tant que le tirage est ouvert.</summary>
        public int Pick = -1;

        public bool Closed => ClosedUnixMs > 0;
        public DraftVote VoteOf(string playerId) => Votes.Find(v => v.PlayerId == playerId);
    }

    public static class PvpDraft
    {
        /// <summary>Version du protocole des matchs : un jeu sans tirage reçoit « OUTDATED ».</summary>
        public const int Version = 1;
        public const int MapCount = 3;
        /// <summary>Le temps de bannir, sur le téléphone.</summary>
        public const int BanMs = 10_000;
        /// <summary>Après le premier vote, le serveur attend les autres ce temps (10 s, plus le réseau) avant de tirer pour eux.</summary>
        public const int WindowMs = BanMs + 5_000;

        /// <summary>
        /// La difficulté de chaque ligue : le contrat d'un acte (<see cref="DifficultyTable.Spec"/>), en Normal pour Bronze, Argent
        /// et Or (actes 1 à 3), en Extrême pour Platine et Diamant (actes 2 et 3). Jamais au-delà de l'acte 3 : c'est ce que
        /// tout joueur du PvP a ouvert en solo.
        /// </summary>
        static readonly (Difficulty Mode, int Act)[] Tiers =
        {
            (Difficulty.Normal, 1), (Difficulty.Normal, 2), (Difficulty.Normal, 3), (Difficulty.Extreme, 2), (Difficulty.Extreme, 3),
        };

        /// <summary>Les niveaux d'un acte rangés par tiers de difficulté (<see cref="DifficultyTable.Sawtooth"/>) : une carte par tiers.</summary>
        static readonly int[][] Thirds = { new[] { 1, 2, 4 }, new[] { 3, 5, 7 }, new[] { 6, 8, 9, 10 } };

        /// <summary>
        /// Graine d'un tombeau du tirage : négative, ce qui la distingue des graines des anciens duels, avec le contrat qu'elle
        /// reprend écrit dedans (bits 0-3 niveau, 4-6 acte, 7-8 mode, 9-30 hasard). Le tombeau ne dépend que d'elle.
        /// </summary>
        public static int MapSeed(LevelId id, uint salt) =>
            unchecked((int)(0x80000000u | (salt & 0x3FFFFFu) << 9 | (uint)id.Mode << 7 | (uint)id.Act << 4 | (uint)id.Index));

        /// <summary>Le contrat d'une graine du tirage ; faux pour une graine d'ancien duel (positive) ou illisible.</summary>
        public static bool TryDecode(int seed, out LevelId id)
        {
            id = default;
            if (seed >= 0) return false;
            uint u = (uint)seed;
            int mode = (int)(u >> 7 & 3), act = (int)(u >> 4 & 7), index = (int)(u & 15);
            if (mode > (int)Difficulty.Extreme || act < 1 || act > DifficultyTable.ActCount || index < 1 || index > DifficultyTable.LevelsPerAct) return false;
            id = new LevelId((Difficulty)mode, act, index);
            return true;
        }

        /// <summary>Les trois tombeaux d'un match de cette ligue : un facile, un moyen, un dur, dans l'acte de la ligue.</summary>
        public static List<int> Draw(int seed, League league)
        {
            var rng = new Pcg32(Pcg32.Hash(0x4452414654UL /* "DRAFT" */, (ulong)(uint)seed));
            var (mode, act) = Tiers[(int)league];
            var maps = new List<int>();
            foreach (var third in Thirds) maps.Add(MapSeed(new LevelId(mode, act, rng.Pick(third)), rng.NextUInt()));
            return maps;
        }

        /// <summary>Un tirage neuf ; les bots votent tout de suite (sans lancer le compte à rebours des joueurs).</summary>
        public static MapDraft Open(int seed, League league, IEnumerable<(string PlayerId, int Team, bool Bot)> voters)
        {
            var draft = new MapDraft { Seed = seed, Maps = Draw(seed, league) };
            foreach (var (id, team, bot) in voters)
                draft.Votes.Add(new DraftVote { PlayerId = id, Team = team, Voted = bot, Map = bot ? BotBan(seed, id) : -1 });
            return draft;
        }

        /// <summary>Un bot bannit une carte au hasard (toujours la même pour ce match).</summary>
        public static int BotBan(int seed, string botId) => (int)(Pcg32.Hash(Salt(seed, 7), Text(botId)) % MapCount);

        /// <summary>Le vote d'un joueur (une fois, tant que le tirage est ouvert) ; -1 = pas de choix à temps. Faux s'il est refusé.</summary>
        public static bool Vote(MapDraft draft, string playerId, int map, long nowMs)
        {
            var vote = draft?.VoteOf(playerId);
            if (vote == null || vote.Voted || draft.Closed) return false;
            vote.Voted = true;
            vote.Map = map >= 0 && map < MapCount ? map : -1;
            if (draft.FirstVoteUnixMs == 0) draft.FirstVoteUnixMs = nowMs;
            return true;
        }

        /// <summary>Ferme le tirage quand tout le monde a voté, ou <see cref="WindowMs"/> après le premier vote. Vrai s'il vient de fermer.</summary>
        public static bool TryClose(MapDraft draft, long nowMs)
        {
            if (draft == null || draft.Closed) return false;
            bool all = draft.Votes.TrueForAll(v => v.Voted);
            bool late = draft.FirstVoteUnixMs > 0 && nowMs - draft.FirstVoteUnixMs >= WindowMs;
            if (!all && !late) return false;
            draft.Bans = new List<int> { TeamBan(draft, 0), TeamBan(draft, 1) };
            draft.Pick = Resolve(draft.Seed, draft.Bans[0], draft.Bans[1]);
            draft.ClosedUnixMs = Math.Max(1, nowMs);
            return true;
        }

        /// <summary>
        /// Le bannissement d'un camp : la carte de ses votes ; deux votes différents, l'une des deux au hasard ; aucun vote,
        /// une carte au hasard.
        /// </summary>
        public static int TeamBan(MapDraft draft, int team)
        {
            var picks = new List<int>();
            foreach (var v in draft.Votes)
                if (v.Team == team && v.Map >= 0 && !picks.Contains(v.Map)) picks.Add(v.Map);
            picks.Sort();
            var rng = new Pcg32(Salt(draft.Seed, 1 + team));
            return picks.Count == 0 ? rng.Range(0, MapCount) : picks[rng.Range(0, picks.Count)];
        }

        /// <summary>La carte jouée : celle qu'aucun camp n'a bannie ; s'ils ont banni la même, l'une des deux autres au hasard.</summary>
        public static int Resolve(int seed, int banA, int banB)
        {
            var left = new List<int>();
            for (int m = 0; m < MapCount; m++) if (m != banA && m != banB) left.Add(m);
            return left[new Pcg32(Salt(seed, 3)).Range(0, left.Count)];
        }

        static ulong Salt(int seed, int what) => Pcg32.Hash(Pcg32.Hash(0x42414EUL /* "BAN" */, (ulong)(uint)seed), (ulong)what);

        /// <summary>Empreinte stable d'un texte (jamais string.GetHashCode, qui change d'un processus à l'autre).</summary>
        static ulong Text(string s)
        {
            ulong h = 14695981039346656037UL;
            foreach (char c in s ?? "") unchecked { h ^= c; h *= 1099511628211UL; }
            return h;
        }
    }
}
