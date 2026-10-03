# Mummy Escape

Puzzle-labyrinthe mobile (Unity 6000.3 LTS, URP 2D). Une momie s'échappe d'un tombeau plongé dans le noir : la carte est montrée au début (10 s par étage), puis la momie ne voit plus que les cases voisines grâce à sa torche. Chaque swipe = 1 case, objectif = sortir en un minimum de coups. Chaque partie tire un nouveau labyrinthe.

```
Escape/
├── MummyEscape/            projet Unity
│   └── Assets/_Project/
│       ├── Scripts/Core/       règles, solveur, générateur (C# pur, sans UnityEngine)
│       ├── Scripts/Runtime/    jeu : vues, input, UI, services, online
│       ├── Scripts/Online.UGS/ implémentation Unity Gaming Services (compilée si les paquets sont présents)
│       ├── Scripts/Editor/     setup, aperçu des niveaux, export des leaderboards
│       ├── Scripts/Editor.Pipeline/  commandes CLI/MCP (mummy_levels, mummy_level, mummy_setup, mummy_autoplay…)
│       ├── Tests/EditMode/     tests de résolution / déterminisme / règles
│       └── Scenes/Main.unity
├── tools/
│   ├── CoreTests/          mêmes tests, via `dotnet test` (rapide, sans Unity)
│   └── LevelLab/           inspection et statistiques du générateur en ligne de commande
└── .mcp.json               serveur MCP Unity intégré pour Claude Code
```

## Lancer le jeu

1. Ouvrir `MummyEscape/` avec Unity 6000.3.x (`unity open MummyEscape`).
2. Ouvrir `Assets/_Project/Scenes/Main.unity` → Play.
   Si la scène manque : menu **Mummy Escape › Setup Project**.

Contrôles éditeur : flèches / ZQSD / WASD ; clic sur un piège adjacent visible = désamorcer. Sur mobile : swipe, tap sur un piège pour le désamorcer, maintenir le bouton carte pour dézoomer.

**Tester en format téléphone dans l'éditeur** : dans l'onglet Game, basculer le menu déroulant « Game » sur **Simulator** et choisir un téléphone (la souris simule le toucher : glisser = swipe). Play démarre toujours sur `Main.unity`, quelle que soit la scène ouverte.

## Builds mobiles

Modules installés pour 6000.3.23f1 : Android (SDK 34–37, NDK r27c, OpenJDK 17) et iOS.

| Cible | Menu | Sortie |
|-------|------|--------|
| Android (test) | Mummy Escape › Build › Android APK (test) | `MummyEscape/Builds/Android/MummyEscape.apk` |
| **Google Play** | Mummy Escape › Build › Google Play (AAB signé) — ou `unity command mummy_build_play` | `MummyEscape/Builds/Android/MummyEscape.aab` |
| iOS | Mummy Escape › Build › iOS (projet Xcode) | `MummyEscape/Builds/iOS/` (à compiler et signer sur un Mac) |

En ligne de commande :
```bash
"C:/Program Files/Unity/Hub/Editor/6000.3.23f1/Editor/Unity.exe" -batchmode -quit -projectPath MummyEscape \
  -buildTarget Android -executeMethod MummyEscape.EditorTools.BuildScript.AndroidDev -logFile -
```

Installer sur un téléphone Android (options développeur + débogage USB activés) :
```bash
ADB="C:/Program Files/Unity/Hub/Editor/6000.3.23f1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe"
"$ADB" install -r MummyEscape/Builds/Android/MummyEscape.apk
```
Ou copier l'APK sur le téléphone et l'ouvrir (autoriser les « sources inconnues »). Les builds Android sont IL2CPP / ARM64, Android 7.1+ (API 25), API cible 36. L'icône est générée par **Mummy Escape › Generate app icon**.

**Google Play.** L'AAB est signé avec la **clé d'envoi**, rangée hors du dépôt public dans `~/.mummyescape/` (`upload-keystore.jks`, `signing.properties`, `upload_certificate.pem`). Elle se crée une fois avec *Build › Créer la clé d'envoi Google Play* (`unity command mummy_signing_create`, qui n'écrase jamais une clé existante). En CI, on la fournit par les variables `MUMMY_KEYSTORE`, `MUMMY_KEYSTORE_PASS`, `MUMMY_KEY_ALIAS` et `MUMMY_KEY_PASS`. **Sauvegarde ce dossier.** Le code de version est dérivé de la version (`1.2.3` → `1020301`) : monte la version avant chaque envoi. Fiche, visuels, déclarations et marche à suivre : [`store/google-play/`](store/google-play/PUBLICATION.md).

## Contrat de difficulté

**Déroulé d'une partie** : chaque étage est affiché tour à tour pendant 10 s, l’étage de départ en dernier (bouton « Étage suivant » / « Prêt » pour passer ; le temps s’arrête dans le menu pause ; aperçu désactivable dans les Paramètres), puis le brouillard tombe. La torche éclaire les cases voisines ; la **poussière** l'éteint (on ne voit plus que sa propre case, impossible de désamorcer) et longer une **torche murale** la rallume (à partir de l'acte 2). Chaque nouvelle partie d'un niveau tire un **nouveau labyrinthe** (variante n° = nombre de parties jouées) : il faut à la fois de la logique et de la mémoire.

Tout est déterministe : un couple (niveau, variante) produit **la même carte sur tous les appareils** (graine = hash(version du générateur, niveau, variante), PRNG PCG32 maison). Chaque labyrinthe est validé par un solveur BFS exact (position, boutons, pièges désamorcés, PV, cécité, torche).

**On joue sur la mémoire et la logique, pas sur la longueur.** Un humain fait environ 2× le chemin idéal (simulation `LevelLab --human`) : le par reste donc court (≈ 30 coups au plus, un niveau dure ~2 minutes) et la difficulté monte par les **mécaniques à enchaîner** (portes, portails verrouillés ou maudits, mécanique de l'acte, pièges, poussière, 2 étages), jamais par la distance.

| Acte | Par min. | Plafond | Étages | Taille | Mécaniques obligatoires | Mécanique de l'acte |
|------|---------|---------|--------|--------|-------------------------|---------------------|
| 1 | 14 | 20 → 24 | 1 | 5 → 6 | 1 → 2 (porte / portail) | — |
| 2 | 16 | 24 → 28 | 1 | 6 | 2 (porte + portail) | 1 → 2 courants |
| 3 | 18 | 26 → 30 | 1 | 6 | 2 (portail verrouillé ou non + porte) | 1 → 2 dalles fragiles |
| 4 | 20 | 28 → 29 | 1 → 2 | 6 → 5 | 2 (laser + portail / porte) | barrières rouges et bleues |
| 5 | 22 | 26 → 28 | 2 | 4 | 2 (portail maudit ou non, porte, laser) | 2 jets de flammes + 1 dalle |

(+4 coups de plafond par étage supplémentaire, +3 pour un portail verrouillé, qui demande son levier. Si une graine ne donne aucun tombeau valide, la recherche reprend avec +3 puis +6 coups de marge, de façon déterministe.)

**Un thème et une mécanique par acte** (`Runtime/Visual/TombTheme.cs` pour l'ambiance, `DifficultyTable` pour les règles) :

| Acte | Thème | Mécanique propre | Glyphes ASCII |
|------|-------|------------------|---------------|
| 1 | L'Antichambre : grès doré, hiéroglyphes | portes / boutons, pièges, portails | |
| 2 | Les Galeries inondées : pierre mouillée, mousse, gouttes | **courants** : emportent la momie jusqu'au bout, impossibles à remonter | `{ > } <` (haut, droite, bas, gauche) |
| 3 | Les Ruines effondrées : pierre grise, os, éboulis | **dalles fragiles** : s'effondrent dès qu'on les quitte | `x` |
| 4 | La Cité d'Anubis : métal, néons cyan | **leviers + barrières laser** : un levier inverse rouges (ouvertes) et bleues (fermées) | `$` levier, `|` rouge, `=` bleue |
| 5 | Le Sanctuaire embrasé : basalte, lave | **jets de flammes** : crachent un pas sur trois (déphasés), touchent comme des pics | `0` `1` `2` (phase) |

Courants, dalles et barrières coûtent au pire un détour, **jamais la partie** : le générateur ne garde un courant, une dalle ou une barrière bleue que là où il reste toujours un chemin de retour (un courant ou une dalle est un raccourci à sens unique, doublé d'un chemin plus long). Le jeu garde par sécurité la détection « Emmurée ! » (`Solver.CanEscape`), qui ne doit plus jamais se déclencher.

**Ambiance** : chaque acte a sa palette, ses décors rares (os, cartouches, flaques, câbles, lave…), son éclairage (ambiance, bloom, couleur des torches murales) et ses particules flottantes. `World/FxRig.cs` joue les effets : poussière des pas, étincelles et flash de lumière des boutons/leviers, portes qui s'ouvrent, implosion/explosion des portails, gerbes d'eau, éboulis, flammes, fumée de cécité, flamme qui saute de la torche murale à celle de la momie, victoire et mort. Les effets en boucle (braises des torches, tourbillon des portails, écume des courants, rayon de la sortie) ne tournent que sur les cases visibles ; l'option « Effets lumineux avancés » coupe les effets décoratifs.

**Musique par acte** (`Services/MusicComposer.cs`) : chaque thème est *codé*, façon Strudel/Tidal (motifs texte `"0 ~ 1 2 _ 4"` = degré de la gamme, silence, tenue), puis rendu en boucle sans couture sur un thread de fond au premier lancement de l'acte (fondu enchaîné de 1,8 s) :
1. oud, bourdon, darbouka (rythme maqsum) et flûte ney, gamme hijaz ;
2. nappes lentes, cloches en écho, gouttes et houle (dorien) ;
3. bourdon grave avec triton, tambours lointains, notes isolées, éboulis (locrien) ;
4. synthés sur gamme égyptienne : kick, basse carrée, arpège en doubles croches, lead ;
5. taikos, bourdon saturé, trémolo d'oud, chœur et crépitements (double harmonique).

Pour utiliser de vraies pistes (Suno…), déposer `menu`, `act1` … `act5` (.mp3/.ogg/.wav) dans `Assets/_Project/Resources/Music/` : elles remplacent automatiquement les thèmes codés. `act2_f2` = musique propre à l’étage 2, `act2_b` (tout suffixe) = variante tirée au hasard à chaque partie (`Services/MusicCatalog.cs`). Le **Juke-box** de l’accueil liste toutes les pistes par thème et par étage pour les écouter.

**Interface** (`UI/UIKit.cs`, `UI/UISprites.cs`) : polices Cinzel (titres) et Nunito (texte) sous licence OFL dans `Resources/Fonts`, formes arrondies et icônes dessinées en SDF au démarrage, trois styles de bouton (principal doré, secondaire, discret).

**Anti-capture de la carte** (`Services/ScreenGuard.cs`, `Plugins/iOS/MummyScreenGuard.mm`), active seulement pendant l’aperçu :
- Android : `FLAG_SECURE` sur la fenêtre (captures et enregistrements noirs) ;
- iOS (capture impossible à bloquer) : une capture d'écran jette le tombeau et en tire un nouveau (nouvel aperçu, message au joueur) ; un enregistrement / recopie d'écran (`UIScreen.isCaptured`) masque la carte et met le compte à rebours en pause.
- Test dans l'éditeur : `GameApp.I.Guard.SimulateScreenshot()` / `SimulateCapture(true)`.

Garanties du générateur (`Core/Generation/LevelGenerator.cs`, vérifiées par `Core/Solving/LevelValidator.cs`) :
- au moins **1 interaction obligatoire** (bouton ou téléporteur) sur le chemin optimal ; sans elle, la sortie est inatteignable ;
- **chaque élément sert** (`CheckEverythingUsed`) : chaque bouton / levier est actionné, chaque portail et échelle emprunté, chaque porte, barrière, courant, dalle, piège, poussière et jet de flammes est sur le chemin idéal, chaque torche murale rallume la torche. Plus de fausse porte, de portail leurre ni de portail caché (tous visibles à l'aperçu) ;
- **jamais bloqué** (`CheckNoDeadLock`) : depuis **toute** situation atteignable (position, leviers, dalles effondrées, PV, torche, cécité, pièges désamorcés, rythme des flammes), il reste un chemin vers la sortie sans mourir. Exemple rejeté : 1 PV, torche éteinte par la poussière et des pics impossibles à désamorcer sur le seul chemin du retour ;
- les **culs-de-sac servent** : bouton, téléporteur, échelle, point de chute… les impasses vides sont rebouclées ou comblées ;
- **2 étages maximum** (tout doit tenir en mémoire après l'aperçu) ; chaque téléporteur est au fond d'un cul-de-sac : une seule sortie à l'arrivée, un pas en arrière pour repartir ;
- la **sortie est loin** de l'entrée (≥ 2/3 du côté du tombeau), ou derrière une porte dont le bouton est lui-même loin ;
- espacement minimal entre points d'intérêt, PV suffisants pour le par.

Les tests vérifient pour **chaque** niveau et plusieurs variantes (60 par niveau dans le balayage `Slow`) : spec respectée, déterminisme par variante, nouvelle carte à chaque partie, par rejoué à l'identique dans `GameSession`, mécaniques réellement obligatoires, éléments tous utiles, aucun blocage possible, impasses utiles, sortie lointaine, par plafonné.

## Langues

Français et anglais, au choix dans **Paramètres › Langue** (par défaut : la langue du téléphone, l'anglais si elle n'est pas prise en charge). Changer de langue reconstruit tous les écrans, même en pleine partie.

- **Le texte français est la clé** : `Loc.T("Jouer")`, `Loc.F("Niveau {0}", id)`, `Loc.P(n, "{0} étage", "{0} étages")`. Les libellés passés à `UIKit` (`Label`, `Button`, `SetLabel`…) sont traduits automatiquement ; seuls les textes dynamiques (`.text =`, interpolations) appellent `Loc` explicitement. `Core` passe par `CoreText` (français par défaut, ce que vérifient les tests).
- **Traductions** dans `Runtime/Localization/Loc.En.cs` ; textes légaux anglais dans `LegalTexts.En.cs` (exportés dans `docs/en/`).
- **Vérification** : `unity command mummy_loc_check` (ou *Mummy Escape › Localization › Check missing translations*) liste les textes sans traduction et les interpolations à passer par `Loc.F`. Une ligne marquée `// noloc` est ignorée.
- **Ajouter une langue** : une valeur dans `Loc.Lang`, son entrée dans `Loc.Languages` et une table `Loc.Xx.cs`.

## Tests

```bash
cd tools/CoreTests && dotnet test                       # ~1480 tests
dotnet test --filter "TestCategory!=Slow"               # sans le balayage de 60 variantes par niveau
```
Dans Unity : Window › General › Test Runner › EditMode.

## LevelLab

```bash
cd tools/LevelLab
dotnet run -c Release                      # stats sur 20 variantes de chaque niveau (par, mécaniques, tentatives, temps)
dotnet run -c Release -- --variants 100 --act 2
dotnet run -- 2-7 [variante]               # carte ASCII + solution optimale d'un labyrinthe
dotnet run -- --why 1-7 [préfixe] [n]      # raisons de rejet des tentatives (+ carte partielle du 1er rejet au préfixe donné)
dotnet run -c Release -- --human [--sessions 30] [--act 2] [--profile moyen]   # joueurs humains simulés : mémoire imparfaite, erreurs, chrono
dotnet run -c Release -- --human-why 3-8 attentif [n]   # issue de chaque partie simulée (morts, emmurée, abandon)
```

**Modifier la génération** : toute modification de `LevelGenerator` / `DifficultyTable` / `Rules` change les labyrinthes. Il faut alors
1. incrémenter `DifficultyTable.GeneratorVersion` (les leaderboards sont indexés par version → pas de scores incomparables),
2. relancer LevelLab (aucun niveau ne doit échouer, temps raisonnable sur l'acte 5) et les tests,
3. ré-exporter les leaderboards (ci-dessous).

## MCP / Pipeline

`.mcp.json` déclare le serveur `unity mcp` pour ce projet : redémarrer Claude Code et approuver le serveur. Il a besoin d'un éditeur ouvert (`unity open MummyEscape`). Commandes spécifiques au jeu :

```bash
unity command mummy_levels           # tableau des niveaux depuis l'éditeur
unity command mummy_level --id 1-6 --variant 0   # détail d'un labyrinthe
unity command mummy_setup            # (re)crée scène + réglages
unity command mummy_capture          # capture de la vue Game
unity command mummy_autoplay --steps 40 --until torch_out   # (en Play) saute l'aperçu et joue la solution optimale
                                     # --until : torch_out | torch_relit | button | teleport
```

## En ligne (Unity Gaming Services)

Sans configuration, le jeu tourne **hors ligne** (sauvegarde locale, classement/amis désactivés proprement). Pour activer :
1. Lier le projet : Edit › Project Settings › Services (organisation + projet UGS).
2. Activer Authentication (anonyme + Username & Password), Leaderboards, Friends, Cloud Save dans le dashboard.
3. Menu **Mummy Escape › Online › Export leaderboard configs** → déployer `Assets/_Project/Online` via Services › Deployment (ou `ugs deploy`).

Score de classement = `coups au-delà du par×10⁸ + temps en ms` (plus bas = meilleur) : on trie d'abord sur l'écart au chemin optimal (« parfait », « +3 coups »), puis sur le **temps** (chronomètre lancé à la fin de l'aperçu, arrêté en pause). Chaque partie tirant un labyrinthe différent, le nombre brut de coups ne serait pas comparable. Les ids de leaderboard incluent la version du générateur et du format de score (`v2s2_1-1`). La progression des amis est publiée dans Cloud Save (clé publique `progress`).

## Comptes et RGPD

**Fonctionnement d'un compte sur mobile.** Au premier lancement en ligne, Unity Authentication crée une session **anonyme** (identifiant aléatoire, jeton gardé sur le téléphone) : c'est le mode « invité », sans aucune saisie. Le joueur peut ensuite **créer un compte** (identifiant + mot de passe, *Username & Password* d'UGS) rattaché à ce même profil. Il pourra s'y reconnecter sur un autre appareil, avec sa progression sauvegardée dans Cloud Save (clé privée `save`, fusionnée avec la progression locale à chaque connexion). Il n'y a volontairement **pas d'e-mail** (minimisation des données). La contrepartie est qu'un mot de passe oublié ne se récupère pas, ce que le jeu indique. Plus tard, on pourra ajouter « Se connecter avec Apple / Google » (UGS les gère). Apple l'exige seulement si un autre login social est proposé.

**Conformité intégrée (RGPD, ePrivacy, art. 8 et 25) :**
- **Rien ne part en ligne avant un choix éclairé.** L'écran d'accueil (`WelcomeScreen`) explique les modes hors ligne et en ligne et donne accès à la politique et aux CGU. Le mode hors ligne est complet.
- **Âge.** L'année de naissance n'est demandée que pour jouer en ligne (contrôle neutre). Elle n'est ni conservée ni envoyée ; seul le booléen « mineur » est gardé. Le seuil est l'âge du consentement numérique du pays (15 ans en France, 13 à 16 ans selon le pays de l'UE ; 16 ans si le pays est inconnu). En dessous, l'accord d'un parent est demandé (année de naissance d'un adulte + case à cocher) et il est révocable.
- **Confidentialité par défaut.** Le partage de progression avec les amis est désactivé et le pays n'est pas affiché dans les classements par défaut. Le pseudonyme est aléatoire, avec le rappel de ne pas utiliser son vrai nom.
- **Droits exercés dans le jeu** (Paramètres › Confidentialité) :
  - export JSON de tout ce que savent le téléphone et le serveur (art. 15 et 20) ;
  - **suppression des données en ligne et du compte** (art. 17, obligatoire aussi sur Google Play et l'App Store) ;
  - effacement des données du téléphone ;
  - retrait des choix à tout moment.
- **Preuve du choix.** Le choix est horodaté avec la version de la politique (`PrivacyService.PolicyVersion`). Incrémenter cette version redemande l'accord à tous les joueurs.
- **Ni publicité, ni analytics, ni traceur** (Unity Analytics désactivé). Ne pas en ajouter sans revoir la politique, le manifeste iOS et un éventuel bandeau de consentement.

**À faire avant publication :**
1. Remplir `Runtime/UI/Legal/LegalTexts.cs` : éditeur (nom, adresse), e-mail de contact et médiateur de la consommation. Mettre à jour la date.
2. Menu **Mummy Escape › Legal › Export privacy policy and terms to docs/**, puis publier `docs/` avec GitHub Pages (Settings › Pages › `main` / `docs`). Ces URLs publiques sont demandées par les stores.
3. Dashboard UGS : activer **Username & Password** dans Authentication. Mettre en place la purge des profils inactifs depuis 3 ans (promise dans la politique), par exemple avec un script Cloud Code planifié ou l'Admin API.
4. **Google Play, section Sécurité des données :**
   - données collectées : identifiants utilisateur (ID), activité dans l'appli (contenu de jeu) et nom (pseudonyme) ;
   - finalité : fonctionnement de l'appli ; aucune donnée partagée avec des tiers ;
   - chiffrement en transit : oui ; suppression possible dans l'appli : oui (fournir aussi l'URL de la politique).
5. **App Store, étiquettes de confidentialité :** User ID, Gameplay Content et Other User Content, reliés à l'utilisateur, sans tracking, pour la finalité App Functionality. Elles reprennent `Plugins/iOS/PrivacyInfo.xcprivacy`, le manifeste inclus dans le build Xcode.
6. Public visé : déclarer la tranche d'âge dans les consoles (si les moins de 13 ans sont visés, Google Play impose la politique *Families* et Apple la catégorie Enfants, avec des contraintes supplémentaires).
7. Ces textes et ce fonctionnement ne remplacent pas un avis juridique : à faire relire avant la sortie.

## Avant publication

- Installer les modules de build : `unity install-modules --module android --module ios`.
- `ShareService.GameUrl` pointe vers la fiche Google Play (`com.mummyescape.game`) ; ajouter le lien App Store pour iOS.
- Le partage Android envoie du texte seulement (pas d'image) ; le bouton Quitter est masqué sur iOS (règle Apple).
- Graphismes (pixel-art) et sons sont générés par code (`ArtLibrary`, `AudioService`) : placeholders cohérents à remplacer par de vrais assets.
- Bundle id : `com.mummyescape.game`.
