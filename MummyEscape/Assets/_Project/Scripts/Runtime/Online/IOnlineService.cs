using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MummyEscape.Core;

namespace MummyEscape.Online
{
    public enum LeaderboardScope { Global, Country, Friends }

    public sealed class LeaderboardRow
    {
        public int Rank;          // 1-based
        public string PlayerId;
        public string PlayerName;
        /// <summary>Moves above the optimal route of the maze that player drew (0 = perfect run).</summary>
        public int OverPar;
        /// <summary>Play time of that run in milliseconds (tie breaker; 0 = unknown).</summary>
        public int TimeMs;
        public bool IsMe;
        /// <summary>ISO 3166 alpha-2 code ("FR"), empty when unknown.</summary>
        public string Country = "";
        /// <summary>A time within a hair of the tomb's perfect minimum on a big maze: not proof of cheating, but flagged.</summary>
        public bool Suspicious;
    }

    public sealed class LeaderboardPage
    {
        /// <summary>Best first, at most the requested limit.</summary>
        public List<LeaderboardRow> Rows = new List<LeaderboardRow>();
        /// <summary>The local player's entry in this scope (Rank 0 = ranked but outside the fetched range), null if no score.</summary>
        public LeaderboardRow Me;
    }

    public sealed class FriendInfo
    {
        public string PlayerId;
        public string Name;
        public bool Online;
    }

    public sealed class FriendRequest
    {
        public string PlayerId;
        public string Name;
    }

    /// <summary>Public progression snapshot a player publishes so friends can follow them.</summary>
    [Serializable]
    public sealed class ProgressSnapshot
    {
        public string FurthestLevel;
        public int TotalStars;
        public List<LevelRecord> Records = new List<LevelRecord>();
        /// <summary>Outfit and title shown on the friends list (null in snapshots published before they existed).</summary>
        public Pvp.PlayerLook Look;
        /// <summary>Duel rating (0 = never played a duel).</summary>
        public int Elo;
    }

    /// <summary>Offline = no session; Guest = anonymous player (tied to this install); Account = username + password.</summary>
    public enum AccountState { Offline, Guest, Account }

    /// <summary>
    /// Account rules (those of Unity Authentication "Username &amp; Password"). No e-mail is ever asked: the account
    /// only needs a username and a password (data minimisation), the trade-off being that a forgotten password
    /// cannot be recovered.
    /// </summary>
    public static class AccountRules
    {
        public const int UsernameMin = 3, UsernameMax = 20, PasswordMin = 8, PasswordMax = 30;

        /// <summary>Error message in French, or null when valid.</summary>
        public static string CheckUsername(string u)
        {
            if (string.IsNullOrEmpty(u) || u.Length < UsernameMin || u.Length > UsernameMax)
                return Loc.F("Identifiant : {0} à {1} caractères.", UsernameMin, UsernameMax);
            foreach (char c in u)
                if (!(char.IsLetterOrDigit(c) && c < 128) && c != '.' && c != '-' && c != '_' && c != '@')
                    return "Identifiant : lettres, chiffres et . - _ @ uniquement (sans accents ni espaces).";
            return null;
        }

        public static string CheckPassword(string p)
        {
            if (string.IsNullOrEmpty(p) || p.Length < PasswordMin || p.Length > PasswordMax)
                return Loc.F("Mot de passe : {0} à {1} caractères.", PasswordMin, PasswordMax);
            bool upper = false, lower = false, digit = false, symbol = false;
            foreach (char c in p)
            {
                if (char.IsUpper(c)) upper = true;
                else if (char.IsLower(c)) lower = true;
                else if (char.IsDigit(c)) digit = true;
                else symbol = true;
            }
            return upper && lower && digit && symbol ? null : "Mot de passe : au moins une majuscule, une minuscule, un chiffre et un symbole.";
        }
    }

    /// <summary>
    /// Everything social: identity, per-level leaderboards, friends, progression sharing.
    /// The game must stay fully playable when <see cref="IsAvailable"/> is false.
    /// </summary>
    public interface IOnlineService
    {
        /// <summary>Error of <see cref="LinkGoogleAsync"/>: the Google account belongs to another player.</summary>
        public const string GoogleTaken = "GOOGLE_TAKEN";

        // ---- account (all error-returning calls give a French message, or null on success)
        AccountState Account { get; }
        /// <summary>Login of the account (empty for a guest).</summary>
        string Username { get; }
        /// <summary>Turns the current guest into an account: progress, scores and friends are kept.</summary>
        Task<string> CreateAccountAsync(string username, string password);
        /// <summary>Signs in to an existing account (another device, after a sign-out...).</summary>
        Task<string> SignInAsync(string username, string password);
        /// <summary>The account signs in with a password (false for an account made with Google only).</summary>
        bool HasPassword { get; }
        /// <summary>Google Play Games is attached to this player: signing in with it on any Android phone finds it again.</summary>
        bool GoogleLinked { get; }
        /// <summary>
        /// Attaches the phone's Google Play Games account to the current guest, who becomes an account (progress,
        /// scores and friends kept). <see cref="GoogleTaken"/> when that Google account already has its own progress.
        /// </summary>
        Task<string> LinkGoogleAsync();
        /// <summary>Signs in to the player of the phone's Google Play Games account (a new one if it has none yet).</summary>
        Task<string> SignInWithGoogleAsync();
        /// <summary>Ends the session on this device; online features stop until the next sign-in.</summary>
        Task SignOutAsync();
        Task<string> ChangePasswordAsync(string current, string next);
        /// <summary>Erases the online account and all its data (GDPR art. 17, store requirements).</summary>
        Task<string> DeleteAccountAsync();
        /// <summary>Private cloud copy of the local save (accounts only), null when none.</summary>
        Task<string> LoadCloudSaveAsync();
        Task SaveCloudSaveAsync(string json);
        /// <summary>Removes the progression snapshot friends could read.</summary>
        Task ClearPublishedProgressAsync();
        /// <summary>Everything the server holds about the player, as JSON (GDPR art. 15 and 20).</summary>
        Task<string> ExportOnlineDataAsync();

        bool IsAvailable { get; }
        string PlayerId { get; }
        string PlayerName { get; }
        /// <summary>Human readable reason when unavailable (shown in the UI).</summary>
        string Status { get; }
        /// <summary>True when the data shown is generated demo content (offline, editor and development builds only).</summary>
        bool IsDemo { get; }
        /// <summary>Player country (ISO alpha-2), attached to submitted scores for the per-country ranking.</summary>
        string Country { get; set; }

        Task InitializeAsync();
        Task<string> SetPlayerNameAsync(string name);

        Task SubmitScoreAsync(LevelResult result);
        /// <summary>Top <paramref name="limit"/> of a level. Country scope uses <see cref="Country"/>.</summary>
        Task<LeaderboardPage> GetLeaderboardAsync(LevelId level, LeaderboardScope scope, int limit);

        Task<IReadOnlyList<FriendInfo>> GetFriendsAsync();
        Task<IReadOnlyList<FriendRequest>> GetFriendRequestsAsync();
        /// <summary>Sends a friend request to this friend code (Name#1234). Returns the error to show, or null once sent.</summary>
        Task<string> SendFriendRequestAsync(string playerName);
        /// <summary>Sends a friend request to this player (seen in a chat, a ranking...). Returns the error to show, or null.</summary>
        Task<string> SendFriendRequestToIdAsync(string playerId);
        Task AcceptFriendRequestAsync(string playerId);
        Task DeclineFriendRequestAsync(string playerId);
        Task RemoveFriendAsync(string playerId);

        Task PublishProgressAsync(ProgressSnapshot snapshot);
        Task<ProgressSnapshot> GetProgressAsync(string playerId);
    }

    /// <summary>Picks the best available implementation (UGS registers itself when its packages are installed).</summary>
    public static class OnlineServiceFactory
    {
        public static Func<IOnlineService> Create = () => new OfflineOnlineService();

        /// <summary>Leaderboard id for a level. Includes the generator version so different layouts never mix.</summary>
        public static string LeaderboardId(LevelId id) => $"v{DifficultyTable.GeneratorVersion}s{LevelResult.ScoreFormat}_{id.Key}";
    }

    /// <summary>
    /// A friend code as typed: the public name, a # and digits (as many as the service gave: Lycoris#16510). Spaces and a
    /// full-width ＃ are tidied; typed without its #, every place it could go before the final digits is a candidate.
    /// </summary>
    public static class FriendCode
    {
        public static List<string> Candidates(string typed)
        {
            var list = new List<string>();
            string code = (typed ?? "").Replace(" ", "").Replace('＃', '#');
            int hash = code.LastIndexOf('#');
            if (hash > 0 && hash < code.Length - 1)
            {
                list.Add(code);
                return list;
            }
            if (hash >= 0) return list;
            int digits = 0;
            while (digits < code.Length && char.IsDigit(code[code.Length - 1 - digits])) digits++;
            // At least 4 digits after the #, and a name before it; the shortest number first (the usual code).
            for (int n = 4; n <= digits && n < code.Length; n++)
                list.Add(code.Substring(0, code.Length - n) + "#" + code.Substring(code.Length - n));
            return list;
        }
    }
}
