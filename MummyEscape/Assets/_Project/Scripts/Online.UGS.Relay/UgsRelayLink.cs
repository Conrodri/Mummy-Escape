using System;
using System.Collections.Generic;
using System.Text;
using MummyEscape.Pvp;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Relay;
using UnityEngine;

namespace MummyEscape.Online
{
    /// <summary>
    /// The four phones of a 2v2 over Unity Relay, without Netcode: a star around the host. Each client talks only to the
    /// host, who stamps every message with the sender's id (nobody can speak for another), hands it to his own game and
    /// forwards it to the others. A dropped connection is a departure: the host tells everyone (a host who drops is seen
    /// as gone by all the clients).
    /// </summary>
    public sealed class UgsRelayLink : IRelayLink, IPeerLink
    {
        public RelayMatch Match { get; }
        public string Me { get; }
        public event Action<RelayMessage> Received;
        public event Action<string> Left;

        /// <summary>Host: every expected player is connected. Client: the host said "go".</summary>
        public bool Ready { get; private set; }
        /// <summary>The connection could not be made or was lost before the start.</summary>
        public bool Failed { get; private set; }

        const string Hello = "#H|"; // noloc
        const string Go = "#G"; // noloc

        NetworkDriver _driver;
        readonly NetworkPipeline _pipe;
        readonly bool _host;
        readonly string _hostId;
        NetworkConnection _server;
        readonly Dictionary<NetworkConnection, string> _peers = new Dictionary<NetworkConnection, string>();
        readonly HashSet<string> _expected;
        readonly HashSet<string> _gone = new HashSet<string>();

        UgsRelayLink(RelayMatch match, string me, bool host, string hostId, NetworkSettings settings, IEnumerable<string> expected)
        {
            Match = match;
            Me = me;
            _host = host;
            _hostId = hostId;
            _expected = expected == null ? new HashSet<string>() : new HashSet<string>(expected);
            _driver = NetworkDriver.Create(settings);
            _pipe = _driver.CreatePipeline(typeof(ReliableSequencedPipelineStage));
        }

        /// <summary>The host: binds to its Relay allocation and waits for <paramref name="others"/> (<paramref name="match"/> is null for a duel).</summary>
        public static UgsRelayLink Host(RelayServerData relay, RelayMatch match, string me, IEnumerable<string> others)
        {
            var settings = new NetworkSettings();
            settings.WithRelayParameters(ref relay);
            var link = new UgsRelayLink(match, me, true, me, settings, others);
            if (link._driver.Bind(NetworkEndpoint.AnyIpv4) != 0 || link._driver.Listen() != 0)
            {
                link.Failed = true;
                Debug.LogWarning("[Relay] Host could not bind to its Relay allocation"); // noloc
            }
            link.Ready = link._expected.Count == 0;
            return link;
        }

        /// <summary>A client: connects to the host through the Relay join code's allocation.</summary>
        public static UgsRelayLink Client(RelayServerData relay, RelayMatch match, string me, string hostId)
        {
            var settings = new NetworkSettings();
            settings.WithRelayParameters(ref relay);
            var link = new UgsRelayLink(match, me, false, hostId, settings, null);
            if (link._driver.Bind(NetworkEndpoint.AnyIpv4) != 0)
            {
                link.Failed = true;
                Debug.LogWarning("[Relay] Client could not bind"); // noloc
                return link;
            }
            link._server = link._driver.Connect(relay.Endpoint);
            return link;
        }

        /// <summary>Host: the players who are connected (and said who they are).</summary>
        public int Connected => _peers.Count;

        /// <summary>Host: starts the match with whoever is there; the missing ones count as gone.</summary>
        public void StartNow()
        {
            if (!_host) return;
            foreach (var id in _expected)
                if (!_peers.ContainsValue(id)) Drop(id);
            Broadcast(Go, default);
            Ready = true;
        }

        public void Send(RelayMessage message)
        {
            if (!_driver.IsCreated) return;
            message.From = Me;
            string text = message.Encode();
            if (_host) Broadcast(text, default);
            else if (_server.IsCreated) SendTo(_server, text);
        }

        public void Tick(int raceMs, RelayRace myDuo, int myMaze) => Pump();

        /// <summary>Network pump: called every frame (by the game during the match, by the matchmaker before).</summary>
        public void Pump()
        {
            if (!_driver.IsCreated) return;
            _driver.ScheduleUpdate().Complete();
            if (_host)
            {
                NetworkConnection c;
                while ((c = _driver.Accept()) != default) _peers[c] = null;
            }
            NetworkEvent.Type type;
            while ((type = _driver.PopEvent(out var connection, out var reader)) != NetworkEvent.Type.Empty)
            {
                switch (type)
                {
                    case NetworkEvent.Type.Connect:
                        if (!_host) SendTo(connection, Hello + Me);
                        break;
                    case NetworkEvent.Type.Data:
                        var bytes = new byte[reader.Length];
                        reader.ReadBytes(bytes);
                        OnData(connection, Encoding.UTF8.GetString(bytes));
                        break;
                    case NetworkEvent.Type.Disconnect:
                        OnDisconnect(connection);
                        break;
                }
            }
        }

        void OnData(NetworkConnection from, string text)
        {
            if (_host)
            {
                _peers.TryGetValue(from, out var id);
                if (id == null)
                {
                    // The first words of a client: who he is. Only the expected players, once each.
                    if (!text.StartsWith(Hello, StringComparison.Ordinal)) return;
                    string who = text.Substring(Hello.Length);
                    if (!_expected.Contains(who) || _peers.ContainsValue(who) || _gone.Contains(who)) { _driver.Disconnect(from); _peers.Remove(from); return; }
                    _peers[from] = who;
                    if (!Ready && _expected.IsSubsetOf(_peers.Values)) StartNow();
                    else if (Ready) SendTo(from, Go);
                    return;
                }
                if (!RelayMessage.TryDecode(text, out var m)) return;
                m.From = id; // nobody speaks for another
                Broadcast(m.Encode(), from);
                Received?.Invoke(m);
                return;
            }
            if (text == Go) { Ready = true; return; }
            if (RelayMessage.TryDecode(text, out var message) && message.From != Me) Received?.Invoke(message);
        }

        void OnDisconnect(NetworkConnection connection)
        {
            if (_host)
            {
                if (_peers.TryGetValue(connection, out var id))
                {
                    _peers.Remove(connection);
                    if (id != null) Drop(id);
                }
                return;
            }
            if (!Ready) Failed = true;
            _server = default;
            if (_gone.Add(_hostId)) Left?.Invoke(_hostId);
        }

        /// <summary>Host: a player is gone; the game and the others are told.</summary>
        void Drop(string id)
        {
            if (!_gone.Add(id)) return;
            Broadcast(new RelayMessage { Kind = RelayMessageKind.Quit, From = id }.Encode(), default);
            Left?.Invoke(id);
        }

        void Broadcast(string text, NetworkConnection except)
        {
            foreach (var pair in _peers)
                if (pair.Value != null && pair.Key != except) SendTo(pair.Key, text);
        }

        void SendTo(NetworkConnection connection, string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            if (_driver.BeginSend(_pipe, connection, out var writer) != 0) return;
            writer.WriteBytes(bytes);
            _driver.EndSend(writer);
        }

        public void Dispose()
        {
            Received = null;
            Left = null;
            if (!_driver.IsCreated) return;
            // Last messages (a "quit") leave before the connections close.
            _driver.ScheduleUpdate().Complete();
            if (_host) foreach (var c in _peers.Keys) _driver.Disconnect(c);
            else if (_server.IsCreated) _driver.Disconnect(_server);
            _driver.ScheduleUpdate().Complete();
            _driver.Dispose();
        }
    }
}
