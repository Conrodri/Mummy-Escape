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
            Save = new SaveService();
            Save.Load();
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

            Online = OnlineServiceFactory.Create();
            UI = Child<UIRouter>("UI");
            UI.Init();
            UI.Open<MainMenuScreen>();

            InitOnline();
        }

        async void InitOnline()
        {
            Online.Country = Save.Country;
            await Online.InitializeAsync();
            if (!Online.IsAvailable && !(Online is OfflineOnlineService))
            {
                // Backend unreachable or not configured: fall back to the offline service (keeps the reason).
                Online = new OfflineOnlineService(Online.Status) { Country = Save.Country };
            }
            if (Online is OfflineOnlineService offline) offline.LocalRecord = Save.GetRecord;
            if (Online.IsAvailable) await Online.PublishProgressAsync(BuildProgressSnapshot());
            if (UI.Current is MainMenuScreen menu) menu.OnShow();
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
