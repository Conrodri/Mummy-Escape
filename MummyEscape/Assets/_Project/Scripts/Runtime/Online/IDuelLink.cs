using System;
using System.Threading;
using System.Threading.Tasks;
using MummyEscape.Pvp;

namespace MummyEscape.Online
{
    /// <summary>A live connection to the other players of a match (two for a duel): their messages, their departures.</summary>
    public interface IPeerLink : IDisposable
    {
        string Me { get; }
        /// <summary>A message from another player (never one's own).</summary>
        event Action<RelayMessage> Received;
        /// <summary>A player's connection dropped: for the game, he quit the match.</summary>
        event Action<string> Left;
        void Send(RelayMessage message);
        /// <summary>Network pump, every frame.</summary>
        void Pump();
    }

    /// <summary>What a player looks for: a rival of his league.</summary>
    public sealed class DuelSearch
    {
        public string Me;
        public string Name;
        public PlayerLook Look;
        /// <summary>The player's duel Elo: his league decides who he meets.</summary>
        public int Elo = PvpConfig.StartingElo;
        public bool AllowBots;
        public IPvpService Pvp;
    }

    /// <summary>A duel ready to start: the match the server created, and the connection to the rival (null against a bot).</summary>
    public sealed class DuelStart
    {
        public LiveDuel Match;
        public string Me;
        public IPeerLink Link;
    }

    /// <summary>Finds a rival of the league (or a bot of the league after a minute) and connects the two phones.</summary>
    public interface IDuelMatchmaker
    {
        /// <summary>The duel, or null (cancelled, error: <paramref name="status"/> said why).</summary>
        Task<DuelStart> FindAsync(DuelSearch search, Action<string> status, CancellationToken cancel);
    }

    public static class DuelMatchmakerFactory
    {
        /// <summary>Set by the UGS module when Lobby and Relay are installed.</summary>
        public static Func<IDuelMatchmaker> CreateOnline;

        public static IDuelMatchmaker For(IPvpService pvp) =>
            pvp == null ? null : pvp.IsDemo ? new LocalDuelMatchmaker() : CreateOnline?.Invoke();
    }

    /// <summary>Offline: a simulated rival of the league (editor and development builds).</summary>
    public sealed class LocalDuelMatchmaker : IDuelMatchmaker
    {
        public async Task<DuelStart> FindAsync(DuelSearch search, Action<string> status, CancellationToken cancel)
        {
            status(Loc.T("Recherche d'un adversaire…"));
            try { await Task.Delay(1500, cancel); }
            catch (TaskCanceledException) { return null; }
            var mine = new LiveDuelist { PlayerId = search.Me, Name = search.Name, Look = search.Look };
            var r = await search.Pvp.StartLiveBotDuelAsync("demo_" + Guid.NewGuid().ToString("N"), mine); // noloc
            if (cancel.IsCancellationRequested) return null;
            if (r?.Match == null) { status(Loc.T("Le match n'a pas pu démarrer.")); return null; }
            return new DuelStart { Match = r.Match, Me = r.Match.A.PlayerId };
        }
    }
}
