// Mummy Rush — l'équipe du jeu : son skin et son titre légendaires, réservés à ses comptes.
// C# pur, partagé avec le serveur : il retire le skin et le titre de l'apparence de tout autre joueur.
using System.Collections.Generic;

namespace MummyEscape.Pvp
{
    public static class Developers
    {
        /// <summary>Le skin légendaire animé de l'équipe (couleur de bandelettes).</summary>
        public const string SkinId = "dev_leg";
        /// <summary>Le titre légendaire animé de l'équipe.</summary>
        public const string TitleId = "title_dev";

        /// <summary>
        /// Identifiants de joueur Unity (un par compte : invité de l'éditeur, compte Google du téléphone...). Affichés dans
        /// Paramètres › Confidentialité › Mon compte.
        /// </summary>
        static readonly HashSet<string> PlayerIds = new HashSet<string>
        {
            "af5s3oREeHqvpfcMh2Pc5Ymuml2D", // noloc: Garpzz
        };

        public static bool Is(string playerId) => !string.IsNullOrEmpty(playerId) && PlayerIds.Contains(playerId);

        /// <summary>Retire le skin et le titre de l'équipe de l'apparence d'un joueur qui n'en fait pas partie.</summary>
        public static PlayerLook Restrict(PlayerLook look, string playerId)
        {
            if (look == null || Is(playerId)) return look;
            if (look.Color == SkinId) look.Color = null;
            if (look.Title == TitleId) look.Title = null;
            return look;
        }
    }
}
