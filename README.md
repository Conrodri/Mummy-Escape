# Mummy Escape

Puzzle-labyrinthe mobile (Unity 6000.3 LTS, URP 2D). Une momie s'échappe d'un tombeau plongé dans le noir : la carte entière est montrée 5 s au début, puis la momie ne voit plus que les cases voisines grâce à sa torche. Chaque swipe = 1 case, objectif = sortir en un minimum de coups. Chaque partie tire un nouveau labyrinthe.

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
Ou copier l'APK sur le téléphone et l'ouvrir (autoriser les « sources inconnues »). Les builds Android sont IL2CPP / ARM64, Android 7.1+ (API 25). L'icône est générée par **Mummy Escape › Generate app icon**.

## Contrat de difficulté

**Déroulé d'une partie** : la carte entière (étages empilés, le 1er en bas) est affichée 5 s (bouton « Prêt » pour passer), puis le brouillard tombe. La torche éclaire les cases voisines ; la **poussière** l'éteint (on ne voit plus que sa propre case, impossible de désamorcer) et longer une **torche murale** la rallume (à partir de l'acte 2). Chaque nouvelle partie d'un niveau tire un **nouveau labyrinthe** (variante n° = nombre de parties jouées) : il faut à la fois de la logique et de la mémoire.

Tout est déterministe : un couple (niveau, variante) produit **la même carte sur tous les appareils** (graine = hash(version du générateur, niveau, variante), PRNG PCG32 maison). Chaque labyrinthe est validé par un solveur BFS exact (position, boutons, pièges désamorcés, PV, cécité, torche) :

| Acte | Coups min. (par) | Plafond | Étages | Obstacles obligatoires (portes / portails) |
|------|-----------------|---------|--------|-------------------------------------------|
| 1 | 15 | 24 → 30 | 1 | 1 → 2 |
| 2 | 22 | 32 → 38 | 1 → 2 | 2 → 3 |
| 3 | 28 | 44 → 50 | 2 | 3 |
| 4 | 34 | 52 → 60 | 2 → 3 | 3 → 4 |
| 5 | 40 | 62 → 70 | 3 | 4 |

(+6 coups de plafond par étage supplémentaire.) Garanties du générateur (`Core/Generation/LevelGenerator.cs`, vérifiées par `Core/Solving/LevelValidator.cs`) :
- au moins **1 interaction obligatoire** (bouton ou téléporteur) sur le chemin optimal ; sans elle, la sortie est inatteignable ;
- les **culs-de-sac servent** : bouton, téléporteur, échelle, point de chute… les impasses vides sont rebouclées ou comblées (seule exception : derrière une fausse porte, qui est le leurre) ;
- la **sortie est loin** de l'entrée (≥ 2/3 du côté du tombeau), ou derrière une porte dont le bouton est lui-même loin ;
- espacement minimal entre points d'intérêt, PV suffisants pour le par.

Les tests vérifient pour **chaque** niveau et plusieurs variantes : spec respectée, déterminisme par variante, nouvelle carte à chaque partie, par rejoué à l'identique dans `GameSession`, mécaniques réellement obligatoires, impasses utiles, sortie lointaine.

## Tests

```bash
cd tools/CoreTests && dotnet test                       # ~860 tests
dotnet test --filter "TestCategory!=Slow"               # sans le balayage de 25 variantes par niveau
```
Dans Unity : Window › General › Test Runner › EditMode.

## LevelLab

```bash
cd tools/LevelLab
dotnet run -c Release                      # stats sur 20 variantes de chaque niveau (par, mécaniques, tentatives, temps)
dotnet run -c Release -- --variants 100 --act 2
dotnet run -- 2-7 [variante]               # carte ASCII + solution optimale d'un labyrinthe
dotnet run -- --why 1-7 [préfixe] [n]      # raisons de rejet des tentatives (+ carte partielle du 1er rejet au préfixe donné)
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
2. Activer Authentication (anonyme), Leaderboards, Friends, Cloud Save dans le dashboard.
3. Menu **Mummy Escape › Online › Export leaderboard configs** → déployer `Assets/_Project/Online` via Services › Deployment (ou `ugs deploy`).

Score de classement = `coups au-delà du par×10000 + PV perdus×1000 + interactions` (plus bas = meilleur). Chaque partie tirant un labyrinthe différent, on compare l'écart au chemin optimal (« parfait », « +3 coups ») plutôt que le nombre brut de coups. La progression des amis est publiée dans Cloud Save (clé publique `progress`).

## Avant publication

- Installer les modules de build : `unity install-modules --module android --module ios`.
- `ShareService.GameUrl` est un lien factice à remplacer par celui du store.
- Le partage Android envoie du texte seulement (pas d'image) ; le bouton Quitter est masqué sur iOS (règle Apple).
- Graphismes (pixel-art) et sons sont générés par code (`ArtLibrary`, `AudioService`) : placeholders cohérents à remplacer par de vrais assets.
- Bundle id : `com.mummyescape.game`.
