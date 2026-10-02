using System.Threading.Tasks;
using MummyEscape.Core;
using MummyEscape.Game;
using MummyEscape.Input;
using MummyEscape.Online;
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
            Application.targetFrameRate = 60;

            if (spriteMaterial == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Lit-Default");
                if (shader != null) spriteMaterial = new Material(shader);
            }

            Settings = new SettingsService();
            Settings.Load();
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
            Game.Init(this, maze, player, input);

            // Nothing goes online before the player has been informed and has chosen to (GDPR): offline until then.
            Online = Offline(OfflineReason);
            UI = Child<UIRouter>("UI");
            UI.Init();
            UI.Open<MainMenuScreen>();
            if (Privacy.NeedsAnswer) UI.Open<WelcomeScreen>();

            if (Privacy.OnlineAllowed) _ = StartOnline();
            Save.Changed += () => _cloudDirty = true;
        }

        void OnDestroy() => Loc.Changed -= OnLanguageChanged;

        /// <summary>Screens are built once with their texts: rebuild them all, back where the player was (the settings).</summary>
        void OnLanguageChanged()
        {
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
            await AfterAccountChange();
        }

        /// <summary>Stops every online exchange; the session stays on the device so turning it back on resumes it.</summary>
        public void StopOnline()
        {
            Online = Offline(OfflineReason);
            RefreshMenus();
        }

        /// <summary>After a sign-in, an account creation or a startup: syncs the save and the shared progression.</summary>
        public async Task AfterAccountChange()
        {
            if (Online.Account == AccountState.Account)
            {
                // The cloud copy (another device) is folded into this one, then the union goes back up.
                var json = await Online.LoadCloudSaveAsync();
                if (!string.IsNullOrEmpty(json))
                {
                    try { Save.MergeFrom(JsonUtility.FromJson<SaveData>(json)); }
                    catch (System.Exception e) { Debug.LogWarning("[Save] Unreadable cloud save: " + e.Message); }
                }
                await PushCloudSave();
            }
            await PublishProgress();
            RefreshMenus();
        }

        void RefreshMenus()
        {
            if (UI.Current is MainMenuScreen menu) menu.OnShow();
            else if (UI.Current is FriendsScreen friends) friends.OnShow();
        }

        /// <summary>Shares the progression with friends only if the player opted in; otherwise removes any old copy.</summary>
        public Task PublishProgress()
        {
            if (!Online.IsAvailable) return Task.CompletedTask;
            return Privacy.Data.ShareProgress ? Online.PublishProgressAsync(BuildProgressSnapshot()) : Online.ClearPublishedProgressAsync();
        }

        // Cloud save: written at most every 20 s while dirty, and when the app goes to the background.
        bool _cloudDirty;
        float _nextCloudPush;

        Task PushCloudSave()
        {
            _cloudDirty = false;
            _nextCloudPush = Time.unscaledTime + 20f;
            return Online.SaveCloudSaveAsync(JsonUtility.ToJson(Save.Data));
        }

        void Update()
        {
            if (_cloudDirty && Time.unscaledTime >= _nextCloudPush && Online.Account == AccountState.Account) _ = PushCloudSave();
        }

        void OnApplicationPause(bool paused)
        {
            if (paused && _cloudDirty && Online != null && Online.Account == AccountState.Account) _ = PushCloudSave();
        }

        /// <summary>Creates an account for the current guest, then backs the progression up.</summary>
        public async Task<string> CreateAccount(string username, string password)
        {
            string error = await Online.CreateAccountAsync(username, password);
            if (error == null) await AfterAccountChange();
            return error;
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
            return "{\n\"exportedAtUtc\": \"" + System.DateTime.UtcNow.ToString("o") + "\",\n" +
                   "\"game\": \"Mummy Escape " + Application.version + "\",\n" +
                   "\"privacyChoices\": " + JsonUtility.ToJson(Privacy.Data, true) + ",\n" +
                   "\"localSave\": " + JsonUtility.ToJson(Save.Data, true) + ",\n" +
                   "\"online\": " + (string.IsNullOrEmpty(online) ? "null" : online) + "\n}";
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

        public ProgressSnapshot BuildProgressSnapshot() => new ProgressSnapshot
        {
            FurthestLevel = Save.FurthestUnlocked().ToString(),
            TotalStars = Save.TotalStars,
            Records = Save.Data.Records,
        };

        T Child<T>(string name) where T : Component
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            return go.AddComponent<T>();
        }
    }
}
