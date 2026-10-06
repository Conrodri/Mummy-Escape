using System.Collections;
using MummyEscape.Online;
using MummyEscape.Pvp;
using UnityEngine;

namespace MummyEscape.Game
{
    /// <summary>
    /// The live duel: both players see the tomb for the same time (no skipping), wait for each other, then start together.
    /// Each action goes to the rival's phone, where it is replayed on his clock like a ghost. The first one out wins, the
    /// first one dead loses: as soon as the rival is out or dead on the player's clock, the player's run stops there. The
    /// server judges on the times it verifies.
    /// </summary>
    public sealed partial class GameController
    {
        IPeerLink _duelLink;
        bool _rivalReady;

        /// <summary>The preview is over and the rival's phone is not done with his yet: both start together.</summary>
        public bool DuelWaiting { get; private set; }
        public bool InLiveDuel => Match != null && Match.IsLive;

        void AttachDuelLink(IPeerLink link)
        {
            DetachDuelLink();
            _duelLink = link;
            _rivalReady = false;
            if (link == null) return;
            link.Received += OnDuelMessage;
            link.Left += OnDuelLeft;
        }

        void DetachDuelLink()
        {
            DuelWaiting = false;
            if (_duelLink == null) return;
            _duelLink.Received -= OnDuelMessage;
            _duelLink.Left -= OnDuelLeft;
            _duelLink.Dispose();
            _duelLink = null;
        }

        void OnDuelMessage(RelayMessage m)
        {
            var match = Match;
            if (match == null || !match.IsLive) return;
            switch (m.Kind)
            {
                case RelayMessageKind.Input:
                    match.AddRivalInput(m.Tick, m.Direction);
                    break;
                case RelayMessageKind.Ready:
                    _rivalReady = true;
                    break;
                case RelayMessageKind.Quit:
                    match.RivalQuit = true;
                    Changed?.Invoke();
                    break;
            }
        }

        void OnDuelLeft(string playerId)
        {
            if (Match == null || !Match.IsLive) return;
            Match.RivalQuit = true;
            Changed?.Invoke();
        }

        /// <summary>Tells the rival the preview is over and waits for his phone (a few seconds at most).</summary>
        IEnumerator WaitForRival()
        {
            if (_duelLink == null) yield break;
            _input.Enabled = false;
            _duelLink.Send(new RelayMessage { Kind = RelayMessageKind.Ready, From = _duelLink.Me });
            DuelWaiting = true;
            PreviewChanged?.Invoke();
            float left = LiveDuelConfig.ReadyWaitMs / 1000f;
            while (!_rivalReady && Match != null && !Match.RivalQuit && left > 0f)
            {
                left -= Time.unscaledDeltaTime;
                yield return null;
            }
            DuelWaiting = false;
        }

        /// <summary>The action just recorded, to the rival's phone.</summary>
        void SendDuelInput()
        {
            if (_duelLink == null || Match == null || Match.Inputs.Count == 0) return;
            var i = Match.Inputs[Match.Inputs.Count - 1];
            _duelLink.Send(new RelayMessage { Kind = RelayMessageKind.Input, From = _duelLink.Me, Tick = i.Tick, Direction = i.Direction });
        }

        /// <summary>The rival out or dead first (on the player's clock), or gone: the player's run stops where it is.</summary>
        void UpdateLiveDuel(PvpMatch match)
        {
            if (!match.IsLive || match.Over || Previewing || DuelWaiting) return;
            if (match.RivalQuit || match.RivalOver) EndDuel(RunOutcome.TimedOut);
        }
    }
}
