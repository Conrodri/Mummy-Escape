using System.Collections.Generic;
using System.Threading.Tasks;
using MummyEscape.Online;
using MummyEscape.Pvp;
using MummyEscape.UI;
using MummyEscape.UI.Screens;

namespace MummyEscape.App
{
    /// <summary>
    /// What the server told this session: the duel profile, the player's group and the service status. Owned by
    /// <see cref="GameApp"/>, kept in step by the screens that learn something new, drawn by the menus.
    /// </summary>
    public sealed class PvpSession
    {
        readonly GameApp _app;
        string _statusShown;

        public PvpSession(GameApp app) => _app = app;

        /// <summary>Last profile the server sent (null until loaded, forgotten when the PvP service changes).</summary>
        public PvpProfileResponse Profile { get; internal set; }

        /// <summary>The player's group (the duo of two friends), shown on the home; null without one or until loaded.</summary>
        public Duo Group { get; private set; }

        /// <summary>The team's maintenance message and duel pause, read from the server (null until read).</summary>
        public ServiceStatus Status { get; private set; }

        public async Task<PvpProfileResponse> RefreshProfile()
        {
            var pvp = _app.Pvp;
            if (pvp == null) return null;
            var profile = await pvp.GetProfileAsync();
            if (profile?.Data != null)
            {
                Profile = profile;
                _app.Save.GrantSkins(profile.Data.UnlockedRewards);
            }
            return profile;
        }

        /// <summary>Keeps the cached profile in step after a duel or a purchase.</summary>
        public void UpdateWallet(int seals, List<string> rewards)
        {
            if (Profile?.Data != null) Profile.Data.Seals = seals;
            _app.Save.GrantSkins(rewards);
        }

        /// <summary>The group screens keep this copy in step (the home draws the teammate from it).</summary>
        public void SetGroup(Duo duo) => Group = duo;

        /// <summary>Loads the group after a sign-in (the home shows the teammate's mummy beside the player's).</summary>
        public async Task LoadGroup()
        {
            var pvp = _app.Pvp;
            if (pvp == null) { Group = null; return; }
            var teams = await pvp.GetTeamsAsync();
            if (teams == null || teams.Error != null) return;
            Group = teams.Duos.Count > 0 ? teams.Duos[0] : null;
            _app.RefreshMenus();
        }

        /// <summary>Reads the service status; its message is shown once per text (a changed message shows again).</summary>
        public async Task LoadStatus()
        {
            var pvp = _app.Pvp;
            if (pvp == null || pvp.IsDemo) return;
            var status = await pvp.GetStatusAsync();
            if (status == null || status.Error != null) return;
            Status = status;
            _app.RefreshMenus();
            string text = Loc.Current == Loc.Lang.En && !string.IsNullOrEmpty(status.MessageEn) ? status.MessageEn : status.Message;
            if (string.IsNullOrEmpty(text) || _statusShown == text || _app.UI == null) return;
            _statusShown = text;
            _app.UI.Open<OfferDialog>().Configure("Mummy Rush", text, ("OK", ButtonStyle.Primary, null)); // noloc
        }
    }
}
