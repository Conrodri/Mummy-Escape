using System.Threading.Tasks;

namespace MummyEscape.Online
{
    /// <summary>
    /// Google Play Games on the phone: hands out the one-time server auth code that Unity Authentication trades for a
    /// player (sign in) or attaches to the current one (link). Supplied by the Online.GooglePlay assembly on Android when
    /// the Google Play Games plugin is in the project; null elsewhere (iOS, editor, desktop).
    /// </summary>
    public interface IGoogleSignIn
    {
        /// <summary>
        /// The auth code, or null when the player is not signed in to Play Games (refused, no Play Games app, not set up).
        /// <paramref name="interactive"/> shows Google's sign-in sheet when the automatic sign-in at launch did not happen.
        /// </summary>
        Task<string> GetAuthCodeAsync(bool interactive);
    }

    public static class GoogleSignIn
    {
        public static IGoogleSignIn Provider;
        public static bool Supported => Provider != null;
    }
}
