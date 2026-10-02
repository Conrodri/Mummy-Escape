namespace MummyEscape.UI.Legal
{
    /// <summary>
    /// Privacy policy and terms of use, shown in the game and exported to <c>docs/</c> (public URL for the stores).
    /// Lines starting with "# " are section titles. Keep in sync with what the code really does, and bump
    /// <see cref="Services.PrivacyService.PolicyVersion"/> when a change matters: every player is asked again.
    /// <para>TODO avant publication : remplacer les trois champs ci-dessous par l'identité réelle de l'éditeur.</para>
    /// </summary>
    public static partial class LegalTexts
    {
        public const string Publisher = "[NOM DE L'ÉDITEUR — personne physique ou société]";
        public const string Address = "[ADRESSE POSTALE DE L'ÉDITEUR]";
        public const string Contact = "[ADRESSE E-MAIL DE CONTACT]";
        public const string Mediator = "[NOM ET SITE DU MÉDIATEUR DE LA CONSOMMATION]";
        public const string Updated = "2 octobre 2026";

        /// <summary>True while the publisher fields above still hold placeholders (the README and a warning flag it).</summary>
        public static bool HasPlaceholders => Publisher.StartsWith("[") || Contact.StartsWith("[");

        public static readonly string[] Privacy =
        {
            $"Version {Services.PrivacyService.PolicyVersion} — mise à jour le {Updated}.",
            "# En bref",
            "Mummy Escape se joue entièrement hors ligne : dans ce cas, aucune donnée ne quitte ton téléphone. " +
            "Le jeu ne contient ni publicité, ni mesure d'audience, ni traceur, et n'utilise aucun identifiant publicitaire. " +
            "Aucune adresse e-mail, aucun nom réel et aucun numéro de téléphone ne te sont demandés.",
            "# Qui est responsable de tes données ?",
            $"{Publisher}, {Address}. Contact pour toute question ou demande : {Contact}.",
            "# Données conservées sur ton téléphone",
            "• Ta progression (records, étoiles, scarabées, apparences), tes réglages et le pays que tu as éventuellement choisi.\n" +
            "• Tes choix de confidentialité : la date de ta réponse, le mode en ligne ou hors ligne, si tu es mineur et si un parent a donné son accord. " +
            "Ton année de naissance sert uniquement à ce calcul : elle n'est ni conservée ni envoyée.\n" +
            "Ces données restent sur l'appareil ; tu peux les effacer à tout moment (Paramètres › Confidentialité) ou en désinstallant le jeu.",
            "# Données traitées en ligne (seulement si tu actives le mode en ligne)",
            "• Un identifiant de joueur aléatoire, créé par le service d'authentification, et un jeton de session gardé sur ton téléphone.\n" +
            "• Ton pseudonyme, public (attribué au hasard, modifiable). N'utilise pas ton vrai nom.\n" +
            "• Tes meilleurs scores par niveau (coups au-dessus du chemin idéal et temps), publics dans les classements, avec ton pays seulement si tu l'as choisi.\n" +
            "• Tes amis, tes demandes d'ami et ton statut « en ligne », visibles de tes amis.\n" +
            "• Ta progression (niveau atteint, étoiles), visible de tes amis seulement si tu actives le partage (désactivé par défaut).\n" +
            "• Si tu crées un compte (facultatif) : ton identifiant de connexion, ton mot de passe (conservé chiffré par notre prestataire, jamais lisible par nous) " +
            "et une copie de sauvegarde de ta progression, pour la retrouver sur un autre appareil.\n" +
            "• Les données techniques indispensables au service (adresse IP, modèle d'appareil, journaux d'erreurs), utilisées pour son fonctionnement et sa sécurité.",
            "# Pourquoi et sur quelle base ?",
            "• Fournir les fonctions en ligne que tu as choisies (classements, amis, compte, sauvegarde) : exécution des conditions d'utilisation (RGPD, art. 6.1.b).\n" +
            "• Sécuriser le service, lutter contre la triche et les abus : intérêt légitime (art. 6.1.f).\n" +
            "• Partage de ta progression avec tes amis : ton choix, que tu peux retirer à tout moment.\n" +
            "Sous l'âge du consentement numérique de ton pays (15 ans en France), le mode en ligne demande l'accord d'un parent ou tuteur (RGPD art. 8, loi Informatique et Libertés art. 45).",
            "# Qui peut voir ces données ?",
            "• Les autres joueurs : ton pseudonyme, tes scores et ton pays (s'il est choisi) ; tes amis voient aussi ton statut et ta progression si tu la partages.\n" +
            "• Notre prestataire technique, Unity Technologies (services Unity Gaming Services : Authentication, Leaderboards, Friends, Cloud Save), qui agit pour notre compte et selon nos instructions.\n" +
            "Tes données ne sont ni vendues, ni louées, ni utilisées pour de la publicité.",
            "# Transferts hors de l'Union européenne",
            "Unity Technologies est établie aux États-Unis. Les transferts sont encadrés par le cadre de protection des données UE–États-Unis " +
            "(décision d'adéquation de la Commission européenne du 10 juillet 2023) et, à défaut, par les clauses contractuelles types de la Commission.",
            "# Combien de temps ?",
            "• Sur ton téléphone : jusqu'à ce que tu les effaces ou désinstalles le jeu.\n" +
            "• En ligne : tant que ton profil existe. La suppression demandée dans le jeu est immédiate ; " +
            "les profils inactifs depuis 3 ans sont supprimés. Les journaux techniques suivent la politique de conservation de notre prestataire.",
            "# Tes droits",
            "Tu peux accéder à tes données, les corriger, les effacer, les récupérer dans un format lisible (portabilité), t'opposer à un traitement ou le limiter, " +
            "et définir des directives sur le sort de tes données après ton décès. Directement dans le jeu (Paramètres › Confidentialité) :\n" +
            "• « Exporter mes données » : tout ce que le jeu et le serveur savent de toi ;\n" +
            "• « Supprimer mes données en ligne » : efface ton profil, ton compte, tes scores, tes amis et ta sauvegarde ;\n" +
            "• désactiver le mode en ligne ou le partage de progression ; changer de pseudonyme ou de pays.\n" +
            $"Tu peux aussi écrire à {Contact} : nous répondons sous un mois. " +
            "Si tu estimes que tes droits ne sont pas respectés, tu peux saisir la CNIL (www.cnil.fr, 3 place de Fontenoy, TSA 80715, 75334 Paris Cedex 07) " +
            "ou l'autorité de protection des données de ton pays.",
            "# Sécurité",
            "Les échanges avec le serveur sont chiffrés (HTTPS). Les mots de passe ne sont jamais stockés en clair. " +
            "Nous collectons le minimum : pas d'e-mail, pas de nom réel, pas de géolocalisation.",
            "# Décisions automatisées",
            "Aucune décision produisant des effets juridiques ne t'est appliquée de manière automatisée.",
            "# Modifications",
            "Si cette politique change de manière importante, le jeu te présentera la nouvelle version et te redemandera ton choix.",
        };

        /// <summary>Public web page required by Google Play: how to delete the account without reinstalling the game.</summary>
        public static readonly string[] AccountDeletion =
        {
            "Cette page explique comment supprimer ton compte Mummy Escape et les données associées, édité par " + Publisher + ".",
            "# Depuis le jeu (immédiat)",
            "1. Ouvre Mummy Escape.\n2. Va dans Paramètres › Confidentialité.\n3. Touche « Supprimer mes données en ligne », puis confirme.",
            "# Sans le jeu",
            $"Écris à {Contact} depuis l'adresse de ton choix, avec pour objet « Suppression de compte Mummy Escape », en indiquant ton identifiant " +
            "de connexion (ou ton pseudonyme et ton code ami). Nous supprimons le compte sous 30 jours au plus tard et te confirmons la suppression. " +
            "Pour éviter les suppressions abusives, nous pouvons te demander une preuve que le compte t'appartient (par exemple le code ami affiché dans le jeu).",
            "# Ce qui est supprimé",
            "Ton profil et son identifiant, ton compte (identifiant de connexion et mot de passe), ton pseudonyme, tes scores dans les classements, " +
            "tes amis et demandes d'ami, ta progression partagée et ta sauvegarde en ligne.",
            "# Ce qui est conservé",
            "Rien d'autre n'est conservé par l'éditeur. Les journaux techniques de notre prestataire (Unity) sont effacés selon sa propre durée de conservation. " +
            "La progression enregistrée sur ton téléphone reste disponible hors ligne ; tu peux l'effacer dans Paramètres › Confidentialité ou en désinstallant le jeu.",
        };

        /// <summary>Landing page of the public site (docs/).</summary>
        public static readonly string[] Home =
        {
            "Un puzzle-labyrinthe égyptien : tu es la momie, et tu dois t'échapper du tombeau plongé dans le noir, coup après coup.",
            "# Informations",
            "• [Politique de confidentialité](confidentialite.md)\n• [Conditions d'utilisation](conditions.md)\n• [Supprimer ton compte et tes données](suppression-compte.md)\n• [English version](en/index.md)",
            "# Contact",
            Contact,
        };

        public static readonly string[] Terms =
        {
            $"Version {Services.PrivacyService.PolicyVersion} — mise à jour le {Updated}.",
            "# Objet",
            $"Ces conditions encadrent l'utilisation du jeu Mummy Escape, édité par {Publisher} ({Address}, {Contact}). " +
            "En jouant en ligne, tu les acceptes.",
            "# Le jeu",
            "Le jeu est gratuit. Il se joue hors ligne ; les fonctions en ligne (classements, amis, compte, sauvegarde) sont facultatives. " +
            "Elles sont fournies sans garantie de disponibilité permanente et peuvent évoluer ou s'arrêter ; ta progression locale reste alors jouable.",
            "# Âge",
            "Sous l'âge du consentement numérique de ton pays (15 ans en France), le mode en ligne n'est accessible qu'avec l'accord d'un parent ou tuteur légal.",
            "# Ton compte",
            "Le compte est facultatif et se compose d'un identifiant et d'un mot de passe, que tu dois garder secrets. " +
            "Aucune adresse e-mail n'étant demandée, un mot de passe oublié ne peut pas être récupéré. " +
            "Tu peux supprimer ton compte et toutes tes données en ligne à tout moment depuis le jeu.",
            "# Règles de conduite",
            "Il est interdit de choisir un pseudonyme injurieux, discriminatoire, à caractère sexuel ou usurpant l'identité d'autrui, " +
            "de tricher (modification du jeu, automatisation, exploitation de failles) ou de perturber le service. " +
            "En cas de manquement, les scores concernés peuvent être retirés et le profil supprimé.",
            "# Propriété intellectuelle",
            "Le jeu, ses graphismes, ses musiques et son code sont protégés. Tu disposes d'un droit d'usage personnel et non commercial.",
            "# Responsabilité",
            "L'éditeur ne saurait être tenu responsable des interruptions du service en ligne ni des pertes de progression dues à un mot de passe oublié " +
            "ou à la désinstallation du jeu sans compte. Rien dans ces conditions ne limite les droits que tu tiens de la loi en tant que consommateur.",
            "# Données personnelles",
            "Leur traitement est décrit dans la politique de confidentialité, accessible dans le jeu (Paramètres › Confidentialité).",
            "# Droit applicable",
            "Ces conditions sont soumises au droit français. En cas de litige, contacte-nous d'abord à l'adresse ci-dessus ; " +
            "à défaut d'accord amiable, les tribunaux compétents sont ceux prévus par la loi. " +
            $"Conformément au Code de la consommation, tu peux recourir gratuitement à un médiateur de la consommation : {Mediator}.",
        };
    }
}
