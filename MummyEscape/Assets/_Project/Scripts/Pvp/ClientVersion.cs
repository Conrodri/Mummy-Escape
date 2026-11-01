// Mummy Rush — les versions du jeu : le code de version du store, et la plus ancienne que le serveur laisse jouer.
// C# pur, partagé avec le serveur (qui l'envoie dans ServiceStatus) et le script de build.
using System;

namespace MummyEscape.Pvp
{
    public static class ClientVersion
    {
        /// <summary>
        /// Plus ancienne version du jeu encore acceptée : un jeu plus ancien bloque l'écran sur « Mettre à jour ». À monter
        /// une fois la nouvelle version disponible sur le store (sinon les joueurs n'ont rien à installer), puis redéployer le
        /// module. L'équipe peut aussi la monter sans redéployer (<see cref="ServiceStatus.MinVersion"/> via SetStatus).
        /// </summary>
        public const string Minimum = "0.1.15";

        /// <summary>Code de version du store tiré du nom : 1.2.3 → 1020301 (chaque partie de 0 à 99).</summary>
        public static int Code(string version)
        {
            var parts = (version ?? "").Split('.');
            int Part(int i) => i < parts.Length && int.TryParse(parts[i], out int v) ? Math.Max(0, Math.Min(99, v)) : 0;
            return ((Part(0) * 100 + Part(1)) * 100 + Part(2)) * 100 + 1;
        }

        /// <summary>Ce jeu est plus ancien que <paramref name="minimum"/> (vide : aucune exigence).</summary>
        public static bool IsOutdated(string version, string minimum) => !string.IsNullOrEmpty(minimum) && Code(version) < Code(minimum);

        /// <summary>La plus récente des deux exigences (vide si aucune).</summary>
        public static string Higher(string a, string b) => string.IsNullOrEmpty(a) ? b : string.IsNullOrEmpty(b) || Code(a) >= Code(b) ? a : b;
    }
}
