using System.Threading.Tasks;
using GooglePlayGames;
using GooglePlayGames.BasicApi;
using UnityEngine;

namespace MummyEscape.Online
{
    /// <summary>
    /// Google Play Games v2: the phone signs the player in on its own at launch (or through Google's sheet on demand),
    /// then gives a server auth code for Unity Authentication. Only on Android devices: the editor has no Play Games.
    /// </summary>
    public sealed class PlayGamesSignIn : IGoogleSignIn
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            PlayGamesPlatform.Activate();
            GoogleSignIn.Provider = new PlayGamesSignIn();
        }
#endif

        public async Task<string> GetAuthCodeAsync(bool interactive)
        {
            var games = PlayGamesPlatform.Instance;
            if (!games.IsAuthenticated())
            {
                var status = await Authenticate(games, false);
                if (status != SignInStatus.Success && interactive) status = await Authenticate(games, true);
                if (status != SignInStatus.Success)
                {
                    Debug.Log("[Google] Play Games sign-in: " + status);
                    return null;
                }
            }
            var code = new TaskCompletionSource<string>();
            // A fresh code every time: each one can be traded only once.
            games.RequestServerSideAccess(true, c => code.TrySetResult(string.IsNullOrEmpty(c) ? null : c));
            return await code.Task;
        }

        static Task<SignInStatus> Authenticate(PlayGamesPlatform games, bool manually)
        {
            var done = new TaskCompletionSource<SignInStatus>();
            if (manually) games.ManuallyAuthenticate(s => done.TrySetResult(s));
            else games.Authenticate(s => done.TrySetResult(s));
            return done.Task;
        }
    }
}
