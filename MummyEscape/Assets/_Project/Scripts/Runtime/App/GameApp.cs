using System.Threading.Tasks;
using MummyEscape.Core;
using MummyEscape.Game;
using MummyEscape.Input;
using MummyEscape.Online;
using MummyEscape.Pvp;
using MummyEscape.Services;
using MummyEscape.UI;
using MummyEscape.UI.Screens;
using MummyEscape.Visual;
using MummyEscape.World;
using UnityEngine;

namespace MummyEscape.App
{
    /// <summary>Composition root: creates every service and the world once, lives for the whole session.</summary>
    public sealed class GameApp : MonoBehaviour
    {
        public static GameApp I { get; private set; }

        public SettingsService Settings { get; private set; }
        public SaveService Save { get; private set; }
        public PrivacyService Privacy { get; private set; }
        public AudioService Audio { get; private set; }
        public ShareService Share { get; private set; }
        public IOnlineService Online { get; private set; }
        public ArtLibrary Art { get; private set; }
        public CameraRig Camera { get; private set; }
        public LightingRig Lighting { get; private set; }
        public FxRig Fx { get; private set; }
        public ScreenGuard Guard { get; private set; }
        public GameController Game { get; private set; }
        public UIRouter UI { get; private set; }
        /// <summary>Lit sprite material of the world (tiles, mummies), for views built later (duel replays).</summary>
        public Material SpriteMaterial { get; private set; }

        public static GameApp Create(Material spriteMaterial, Material unlitMaterial, AudioClip music)
        {
            var go = new GameObject("MummyEscape");
            DontDestroyOnLoad(go);
            var app = go.AddComponent<GameApp>();
            app.Init(spriteMaterial, unlitMaterial, music);
            return app;
        }

        void Init(Material spriteMaterial, Material unlitMaterial, AudioClip music)
        {
            I = this;
            // Portrait only, whatever the phone's auto-rotate setting.
            Screen.autorotateToLandscapeLeft = Screen.autorotateToLandscapeRight = Screen.autorotateToPortraitUpsideDown = false;
            Screen.orientation = ScreenOrientation.Portrait;
            LegacyData.Migrate(); // before the save and the settings are read (the game was renamed)

            if (spriteMaterial == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Lit-Default");
                if (shader != null) spriteMaterial = new Material(shader);
            }

            SpriteMaterial = spriteMaterial;
            Settings = new SettingsService();
            Settings.Load();
            Application.targetFrameRate = Settings.BatterySaver ? 30 : 60;
            Settings.Changed += () => Application.targetFrameRate = Settings.BatterySaver ? 30 : 60;
            Loc.Apply(Settings.Language); // before any text is built
            Loc.Changed += OnLanguageChanged;
            Save = new SaveService();
            Save.Load();
            Privacy = new PrivacyService();
            Privacy.Load();
            Art = new ArtLibrary();
            UIKit.Init(Art);
            Share = new ShareService();

            Audio = Child<AudioService>("Audio");
            Audio.Init(Settings, music);

            Camera = Child<CameraRig>("Camera");
            Camera.Init(Settings);
            Lighting = Child<LightingRig>("Lighting");
            Lighting.Init(Settings);

            Guard = Child<ScreenGuard>("ScreenGuard");
            Fx = Child<FxRig>("Fx");
            Fx.Init(Art, spriteMaterial, unlitMaterial, Settings);

            var maze = Child<MazeView>("Maze");
            maze.Init(Art, spriteMaterial, Fx, Settings);
            var player = Child<PlayerView>("Player");
            player.Init(Art, spriteMaterial, Settings);
            var input = Child<SwipeInput>("Input");
            Game = Child<GameController>("Game");
            var ghost = Child<GhostView>("Ghost");
            ghost.Init(Art, Fx.Unlit);
            Game.Init(this, maze, player, ghost, input);
            // 2v2 campaign: the teammate's mummy, in the same tomb.
            var mate = Child<PlayerView>("Mate"); // noloc
            mate.Init(Art, spriteMaterial, Settings);
            Game.InitCoop(mate);
            Game.CoopEnded += outcome => CoopScreen.ShowOutcome(this, outcome);
            Game.DuelEnded += (match, run) =>
            {
                Analytics.Track("duel_end", ("outcome", run.Outcome.ToString()), ("live", match.IsLive), ("time_ms", run.TimeMs)); // noloc
                Save.AddPassXp(Monetization.BattlePass.MatchXp);
                UI.Open<PvpResultScreen>().Show(match, run);
            };
            Game.RelayEnded += outcome =>
            {
                Save.AddPassXp(Monetization.BattlePass.MatchXp);
                UI.Open<RelayResultScreen>().Show(outcome);
            };

            // Nothing goes online before the player has been informed and has chosen to (GDPR): offline until then.
            Online = Offline(OfflineReason);
            UI = Child<UIRouter>("UI");
            UI.Init();
            // The ad and store SDKs register themselves (their own assemblies); until then, test stand-ins outside release builds.
            if (Application.isEditor || Debug.isDebugBuild)
            {
                // (Replaced on every start: without a domain reload the old ones would still point at the last router.)
                if (Monetization.Ads.Provider == null || Monetization.Ads.Provider is SimulatedAds) Monetization.Ads.Provider = new SimulatedAds(UI);
                if (Monetization.Store.Provider == null || Monetization.Store.Provider is SimulatedStore) Monetization.Store.Provider = new SimulatedStore(UI);
                // The seasonal act playable for testing, whatever the date (the server still sells its items on the real dates).
                Halloween.TestOpen = true;
            }
            PvpServiceFactory.UpdateRequired = AskForUpdate;
            Monetization.Store.UnfinishedFound = () => _ = Monetization.GoldWallet.RecoverAsync(this);
            UI.Open<MainMenuScreen>();
            if (Privacy.NeedsAnswer) UI.Open<WelcomeScreen>();
            // A phone in a language the game does not speak: the player picks one before anything else (over the welcome).
            if (Privacy.Data.AcceptedPolicyVersion == 0 && string.IsNullOrEmpty(Settings.Language) && !Loc.SpeaksSystemLanguage) UI.Open<LanguagePickerScreen>();
            StudioIntro.Play(Audio); // the studio's logo, over the first screen, in silence

            if (Privacy.OnlineAllowed) _ = StartOnline();
            Save.Changed += () => _cloudDirty = true;
        }

        // ------------------------------------------------------------------ duels

        IPvpService _pvp;
        IOnlineService _pvpOnline;

        /// <summary>Duel service for the current online state (server, demo rivals, or null when duels are unavailable).</summary>
        public IPvpService Pvp
        {
            get
            {
                if (_pvpOnline != Online)
                {
                    _pvpOnline = Online;
                    _pvp = PvpServiceFactory.For(Online, () => Save.TotalStars);
                    PvpProfile = null;
                }
                if (_pvp is LocalPvpService local) local.PlayerName = Online.PlayerName;
                return _pvp;
            }
        }

        /// <summary>Last profile the server sent (null until loaded).</summary>
        public PvpProfileResponse PvpProfile { get; private set; }

        public async Task<PvpProfileResponse> RefreshPvpProfile()
        {
            var pvp = Pvp;
            if (pvp == null) return null;
            var profile = await pvp.GetProfileAsync();
            if (profile?.Data != null)
            {
                PvpProfile = profile;
                Save.GrantSkins(profile.Data.UnlockedRewards);
            }
            return profile;
        }

        /// <summary>Keeps the cached profile in step after a duel or a purchase.</summary>
        public void UpdatePvpWallet(int seals, System.Collections.Generic.List<string> rewards)
        {
            if (PvpProfile?.Data != null) PvpProfile.Data.Seals = seals;
            Save.GrantSkins(rewards);
        }

        int _syncedStars = -1;

        /// <summary>The server opens the duels at 35 solo stars: it is told the count whenever it grows.</summary>
        public Task SyncSoloStars()
        {
            var pvp = Pvp;
            int stars = Save.TotalStars;
            if (pvp == null || stars == _syncedStars) return Task.CompletedTask;
            _syncedStars = stars;
            return pvp.SyncSoloStarsAsync(stars);
        }

        void OnDestroy() => Loc.Changed -= OnLanguageChanged;

        /// <summary>Screens are built once with their texts: rebuild them all, back where the player was (the settings).</summary>
        void OnLanguageChanged()
        {
            PvpSkins.ClearCache();
            if (UI == null) return;
            UI.RebuildAll();
            if (Game.Session != null)
            {
                UI.Open<HudScreen>();
                UI.Open<PauseScreen>();
            }
            else UI.Open<MainMenuScreen>();
            UI.Open<SettingsScreen>();
        }

        string OfflineReason => Privacy.NeedsAnswer ? "Hors ligne" : Privacy.Data.IsMinor && !Privacy.Data.ParentalConsent
            ? "Hors ligne (autorisation parentale requise pour jouer en ligne)" : "Hors ligne (mode en ligne désactivé dans Confidentialité)";

        OfflineOnlineService Offline(string reason) =>
            new OfflineOnlineService(reason) { Country = Save.Country, LocalRecord = Save.GetRecord };

        /// <summary>Connects the online features (after consent, at startup or when the player turns them on).</summary>
        public async Task StartOnline()
        {
            var service = OnlineServiceFactory.Create();
            service.Country = Save.Country;
            await service.InitializeAsync();
            if (!service.IsAvailable && !(service is OfflineOnlineService))
            {
                // Backend unreachable or not configured: fall back to the offline service (keeps the reason).
                service = Offline(service.Status);
            }
            if (service is OfflineOnlineService offline) offline.LocalRecord = Save.GetRecord;
            Online = service;
            Analytics.Apply(Privacy.AnalyticsAllowed && service.IsAvailable);
            await AfterAccountChange();
            await SubmitRecords();
        }

        /// <summary>Sends a won run to the leaderboards and remembers that the server has this score.</summary>
        public async Task SubmitScore(LevelResult result)
        {
            if (!await Online.SubmitScoreAsync(result)) return;
            string sent = result.Level.Key + ":" + result.LeaderboardScore;
            if (!Save.Data.SubmittedScores.Contains(sent)) { Save.Data.SubmittedScores.Add(sent); Save.Save(); }
        }

        /// <summary>
        /// The best record of each level that never reached its board (played offline, or while the boards failed) goes up
        /// now; the board keeps the best, so sending again is harmless.
        /// </summary>
        async Task SubmitRecords()
        {
            if (!Online.IsAvailable || Online.IsDemo) return;
            foreach (var rec in Save.Data.Records.ToArray())
            {
                if (!rec.HasBest || !LevelId.TryParseKey(rec.Key, out var id)) continue;
                // Par 0: the moves are the gap to the optimal route, which is all the score keeps of them.
                var best = new LevelResult { Level = id, Won = true, Moves = rec.BestOverPar, Par = 0, TimeMs = rec.BestTimeMs, HpLeft = rec.BestHpLeft };
                if (Save.Data.SubmittedScores.Contains(rec.Key + ":" + best.LeaderboardScore)) continue;
                await SubmitScore(best);
                if (!Online.IsAvailable) return;
            }
            foreach (var mode in DifficultyExt.All)
            {
                await SubmitEndless(mode);
                if (!Online.IsAvailable) return;
            }
            await SubmitTower();
        }

        /// <summary>Sends the furthest tomb of an endless ladder (and its time) to that ladder's board.</summary>
        public Task SubmitEndless(Difficulty mode)
        {
            var p = Save.EndlessOf(mode);
            return p.Cleared > 0 ? SubmitBoard(Endless.BoardId(mode), p.Score) : Task.CompletedTask;
        }

        /// <summary>Sends the best tower run (points, then time) to the tower's board.</summary>
        public Task SubmitTower()
        {
            var best = Save.Data.TowerBest;
            return best.BestFloors > 0 ? SubmitBoard(Tower.BoardId, best.Score) : Task.CompletedTask;
        }

        async Task SubmitBoard(string board, long score)
        {
            // One entry per board: it keeps the best, only the last score sent matters.
            string key = board + ":";
            if (Save.Data.SubmittedScores.Contains(key + score) || !await Online.SubmitBoardAsync(board, score)) return;
            Save.Data.SubmittedScores.RemoveAll(s => s.StartsWith(key));
            Save.Data.SubmittedScores.Add(key + score);
            Save.Save();
        }

        /// <summary>Stops every online exchange; the session stays on the device so turning it back on resumes it.</summary>
        public void StopOnline()
        {
            Analytics.Apply(false);
            Online = Offline(OfflineReason);
            RefreshMenus();
        }

        /// <summary>After a sign-in, an account creation or a startup: syncs the save and the shared progression.</summary>
        public async Task AfterAccountChange()
        {
            // The game's team gets its legendary skin and its matching set on every account it signs in with (the title follows from it).
            if (Developers.Is(Online.PlayerId)) Save.GrantSkins(Developers.Skins);
            // Purchases paid while the game was stopped get credited; the team gets its season pass with the wallet.
            _ = Monetization.GoldWallet.RefreshAsync(this);
            _cloudLoaded = false;
            if (Online.Account == AccountState.Account) await LoadCloudSave();
            _syncedStars = -1;
            await SyncSoloStars();
            await PublishProgress();
            _ = MummyEscape.Online.ChatState.RegisterNameAsync(this);
            _ = LoadStatus();
            _ = LoadGroup();
            RefreshMenus();
        }

        /// <summary>The player's group (the duo of two friends), shown on the home; null without one or until loaded.</summary>
        public Duo Group { get; private set; }

        /// <summary>The group screens keep this copy in step (the home draws the teammate from it).</summary>
        public void SetGroup(Duo duo) => Group = duo;

        /// <summary>Loads the group after a sign-in (the home shows the teammate's mummy beside the player's).</summary>
        public async Task LoadGroup()
        {
            var pvp = Pvp;
            if (pvp == null) { Group = null; return; }
            var teams = await pvp.GetTeamsAsync();
            if (teams == null || teams.Error != null) return;
            Group = teams.Duos.Count > 0 ? teams.Duos[0] : null;
            RefreshMenus();
        }

        /// <summary>The team's maintenance message and duel pause, read from the server (null until read).</summary>
        public ServiceStatus Status { get; private set; }
        string _statusShown;

        /// <summary>Reads the service status; its message is shown once per text (a changed message shows again).</summary>
        async Task LoadStatus()
        {
            var pvp = Pvp;
            if (pvp == null || pvp.IsDemo) return;
            var status = await pvp.GetStatusAsync();
            if (status == null || status.Error != null) return;
            Status = status;
            RefreshMenus();
            string text = Loc.Current == Loc.Lang.En && !string.IsNullOrEmpty(status.MessageEn) ? status.MessageEn : status.Message;
            if (string.IsNullOrEmpty(text) || _statusShown == text || UI == null) return;
            _statusShown = text;
            UI.Open<OfferDialog>().Configure("Mummy Rush", text, ("OK", ButtonStyle.Primary, null)); // noloc
        }

        void RefreshMenus()
        {
            if (UI.Current is MainMenuScreen menu) menu.OnShow();
            else if (UI.Current is FriendsScreen friends) friends.OnShow();
        }

        /// <summary>What friends see: the outfit, title, duel rank and progression (furthest level, stars, scores).</summary>
        public Task PublishProgress()
        {
            if (!Online.IsAvailable) return Task.CompletedTask;
            return Online.PublishProgressAsync(BuildProgressSnapshot());
        }

        // Cloud save: written at most every 20 s while dirty, and when the app goes to the background; never before the
        // cloud copy was read (a failed read is not "no save": writing then would erase another device's progress).
        bool _cloudDirty, _cloudLoaded, _cloudLoading;
        float _nextCloudPush, _nextCloudLoad;

        /// <summary>The cloud copy (another device) is folded into this one, then the union goes back up; retried on failure.</summary>
        async Task LoadCloudSave()
        {
            if (_cloudLoading) return;
            _cloudLoading = true;
            try
            {
                string json;
                try { json = await Online.LoadCloudSaveAsync(); }
                catch (System.Exception) { _nextCloudLoad = Time.unscaledTime + 60f; return; }
                if (!string.IsNullOrEmpty(json))
                {
                    try { Save.MergeFrom(JsonUtility.FromJson<SaveData>(json)); }
                    catch (System.Exception e) { Debug.LogWarning("[Save] Unreadable cloud save: " + e.Message); }
                }
                _cloudLoaded = true;
                await PushCloudSave();
            }
            finally { _cloudLoading = false; }
        }

        async Task PushCloudSave()
        {
            if (!_cloudLoaded) return;
            _cloudDirty = false;
            _nextCloudPush = Time.unscaledTime + 20f;
            if (!await Online.SaveCloudSaveAsync(JsonUtility.ToJson(Save.Data))) _cloudDirty = true; // tried again in 20 s
        }

        void Update()
        {
            if (Online == null || Online.Account != AccountState.Account) return;
            if (!_cloudLoaded) { if (_nextCloudLoad > 0f && Time.unscaledTime >= _nextCloudLoad) _ = LoadCloudSave(); return; } // retry a failed read
            if (_cloudDirty && Time.unscaledTime >= _nextCloudPush) _ = PushCloudSave();
        }

        bool _sendingRuns;
        const int RunsPerBatch = 5;

        /// <summary>
        /// Sends the solo runs waiting for the server's replay, which grants their pass XP: by batches of
        /// <see cref="RunsPerBatch"/> while playing (one call for several runs), everything when <paramref name="all"/>
        /// (pause, pass screen). A failed call leaves them queued for the next time.
        /// </summary>
        public async Task SendSoloRuns(bool all)
        {
            if (_sendingRuns || Pvp == null) return;
            _sendingRuns = true;
            try
            {
                while (Save.PendingRunCount >= (all ? 1 : RunsPerBatch))
                {
                    var runs = Save.NextRuns(PvpServer.SoloRunsPerCall);
                    WalletResponse r;
                    try { r = await Pvp.SubmitSoloRunsAsync(runs); }
                    catch (System.Exception e) { Debug.LogWarning("[Pass] Runs not sent: " + e.Message); return; }
                    if (r == null || r.Error != null) { Debug.LogWarning("[Pass] Runs refused: " + r?.Error); return; }
                    Save.RunsSent(runs);
                    Save.ApplyWallet(r.Wallet);
                }
            }
            finally { _sendingRuns = false; }
        }

        void OnApplicationPause(bool paused)
        {
            if (paused) _ = SendSoloRuns(true);
            if (paused) Notifications.Plan(this);
            if (paused && _cloudDirty && Online != null && Online.Account == AccountState.Account) _ = PushCloudSave();
        }

        /// <summary>Signs in to an existing account (connects the backend first when needed).</summary>
        public async Task<string> SignIn(string username, string password)
        {
            var service = Online;
            if (service is OfflineOnlineService && !service.IsDemo) service = OnlineServiceFactory.Create();
            service.Country = Save.Country;
            string error = await service.SignInAsync(username, password);
            if (error != null) return error;
            if (service is OfflineOnlineService offline) offline.LocalRecord = Save.GetRecord;
            Online = service;
            await AfterAccountChange();
            return null;
        }

        /// <summary>Attaches Google Play Games to the current guest (it becomes an account), then backs the progression up.</summary>
        public async Task<string> LinkGoogle()
        {
            string error = await Online.LinkGoogleAsync();
            if (error == null) await AfterAccountChange();
            return error;
        }

        /// <summary>Signs in with the phone's Google Play Games account (connects the backend first when needed).</summary>
        public async Task<string> SignInWithGoogle()
        {
            var service = Online;
            if (service is OfflineOnlineService && !service.IsDemo) service = OnlineServiceFactory.Create();
            service.Country = Save.Country;
            string error = await service.SignInWithGoogleAsync();
            if (error != null) return error;
            if (service is OfflineOnlineService offline) offline.LocalRecord = Save.GetRecord;
            Online = service;
            await AfterAccountChange();
            return null;
        }

        bool _updateAsked;

        /// <summary>The server refuses this version (its levels differ from the server's): once a session, the way to the update.</summary>
        void AskForUpdate()
        {
            if (_updateAsked || UI == null) return;
            _updateAsked = true;
            UI.Open<OfferDialog>().Configure("Mise à jour",
                Loc.T("Une nouvelle version de Mummy Rush est sortie. Mets le jeu à jour pour continuer à jouer en ligne ; le solo reste ouvert."),
                ("Mettre à jour", ButtonStyle.Primary, () => Application.OpenURL("market://details?id=" + Application.identifier)), // noloc
                ("Plus tard", ButtonStyle.Ghost, null));
        }

        public async Task SignOut()
        {
            if (Online.Account == AccountState.Account && _cloudDirty) await PushCloudSave();
            await Online.SignOutAsync();
            if (!Online.IsDemo) Online = Offline("Déconnecté (Paramètres › Compte pour te reconnecter)");
            RefreshMenus();
        }

        /// <summary>
        /// Erases everything held online about the player (profile, account, scores, friends, cloud save) and turns
        /// the online mode off. Works for guests too (their anonymous profile is deleted).
        /// </summary>
        public async Task<string> DeleteOnlineData()
        {
            if (!Online.IsAvailable && !Online.IsDemo)
            {
                // Online mode off or signed out: reconnect the cached session just to delete it.
                var service = OnlineServiceFactory.Create();
                await service.InitializeAsync();
                if (!service.IsAvailable) return service is OfflineOnlineService ? null : Loc.F("Connexion impossible : {0}", Loc.T(service.Status));
                Online = service;
            }
            // The PvP server first: once the account is gone, nobody can ask it to forget the player any more.
            if (Pvp is IPvpService pvp)
            {
                var erased = await pvp.DeleteDataAsync();
                if (erased == null || !erased.Ok) return erased?.Error ?? Loc.T("Connexion impossible. Vérifie ton accès à Internet.");
            }
            string error = await Online.DeleteAccountAsync();
            if (error != null) return error;
            _cloudDirty = false;
            Privacy.SetOnline(false);
            Online = Offline("Données en ligne supprimées — mode hors ligne");
            RefreshMenus();
            return null;
        }

        /// <summary>Everything the game knows about the player, local and online, as one JSON document.</summary>
        public async Task<string> ExportPersonalData()
        {
            string online = Online.IsAvailable || Online.IsDemo ? await Online.ExportOnlineDataAsync() : "null";
            string pvp = "null";
            if ((Online.IsAvailable || Online.IsDemo) && Pvp is IPvpService service)
            {
                var export = await service.ExportDataAsync();
                if (export?.Data != null) pvp = JsonUtility.ToJson(export.Data, true);
            }
            return "{\n\"exportedAtUtc\": \"" + System.DateTime.UtcNow.ToString("o") + "\",\n" +
                   "\"game\": \"Mummy Rush " + Application.version + "\",\n" +
                   "\"privacyChoices\": " + JsonUtility.ToJson(Privacy.Data, true) + ",\n" +
                   "\"localSave\": " + JsonUtility.ToJson(Save.Data, true) + ",\n" +
                   "\"online\": " + (string.IsNullOrEmpty(online) ? "null" : online) + ",\n" +
                   "\"pvp\": " + pvp + "\n}";
        }

        /// <summary>Erases the progression and the choices stored on this device (the welcome screen shows again).</summary>
        public void WipeLocalData()
        {
            Save.Wipe();
            Privacy.Reset();
            _cloudDirty = false;
            Online = Offline(OfflineReason);
        }

        public void SetCountry(string code)
        {
            Save.SetCountry(code);
            Online.Country = Save.Country;
        }

        public ProgressSnapshot BuildProgressSnapshot()
        {
            var look = Visual.PvpSkins.Look(Save.Loadout);
            look.Title = TitleBook.Equipped(this);
            var pvp = PvpProfile?.Data;
            return new ProgressSnapshot
            {
                FurthestLevel = Save.FurthestUnlocked().ToString(),
                TotalStars = Save.TotalStars,
                Records = Save.Data.Records,
                Look = look,
                Elo = pvp != null && pvp.TotalDuels > 0 ? pvp.Elo : 0,
            };
        }

        string _publishedLook;

        /// <summary>Republishes the progression when the outfit or the title changed (friends see the new look).</summary>
        public void PublishLookIfChanged()
        {
            var snapshot = BuildProgressSnapshot();
            string key = JsonUtility.ToJson(snapshot.Look) + snapshot.Elo;
            if (key == _publishedLook) return;
            _publishedLook = key;
            _ = PublishProgress();
        }

        T Child<T>(string name) where T : Component
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            return go.AddComponent<T>();
        }
    }
}
