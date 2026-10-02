# Mummy Escape

Puzzle-labyrinthe mobile (Unity 6000.3 LTS, URP 2D). Une momie s'échappe d'un tombeau plongé dans le noir : elle ne voit que les cases adjacentes, chaque swipe = 1 case, objectif = sortir en un minimum de coups.

```
Escape/
├── MummyEscape/            projet Unity
│   └── Assets/_Project/
│       ├── Scripts/Core/       règles, solveur, générateur (C# pur, sans UnityEngine)
│       ├── Scripts/Runtime/    jeu : vues, input, UI, services, online
│       ├── Scripts/Online.UGS/ implémentation Unity Gaming Services (compilée si les paquets sont présents)
│       ├── Scripts/Editor/     setup, aperçu des niveaux, export des leaderboards
│       ├── Scripts/Editor.Pipeline/  commandes CLI/MCP (mummy_levels, mummy_level, mummy_setup)
│       ├── Tests/EditMode/     tests de résolution / déterminisme / règles
│       └── Scenes/Main.unity
├── tools/
│   ├── CoreTests/          mêmes tests, via `dotnet test` (rapide, sans Unity)
│   └── LevelLab/           inspection et « bake » des niveaux en ligne de commande
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

Tout est déterministe : un niveau `acte-index` produit **la même carte pour tout le monde** (graine = hash(version du générateur, niveau), PRNG PCG32 maison). Chaque niveau est validé par un solveur BFS exact (position, boutons, pièges désamorcés, PV, cécité) :

| Acte | Coups min. (par) | Plafond | Espacement des points d'intérêt |
|------|-----------------|---------|-------------------------------|
| 1 | 10 | 14 → 18 | 4 |
| 2 | 15 | 21 → 26 | 4 |
| 3 | 20 | 28 → 33 | 4 |
| 4 | 25 | 35 → 41 | 5 |
| 5 | 30 | 44 → 52 | 5 |

Les interactions (portes/boutons, pièges, téléporteurs, étages, sols cassables, échelles) augmentent dans chaque acte et d'un acte à l'autre (`Core/Generation/DifficultyTable.cs`). Les tests vérifient pour **chaque** niveau : spec respectée, déterminisme, par rejoué à l'identique dans `GameSession`, portes réellement obligatoires.

## Tests

```bash
cd tools/CoreTests && dotnet test                       # ~208 tests
dotnet test --filter "TestCategory!=Slow"               # sans la vérification de la table « bakée »
```
Dans Unity : Window › General › Test Runner › EditMode.

## LevelLab

```bash
cd tools/LevelLab
dotnet run                 # tableau de tous les niveaux (par, interactions, tentatives)
dotnet run -- 2-7          # carte ASCII + solution optimale
dotnet run -- --bake       # régénère Core/Generation/LevelAttemptTable.cs
```

**Modifier la génération** : toute modification de `LevelGenerator` / `DifficultyTable` / `Rules` change les niveaux. Il faut alors
1. incrémenter `DifficultyTable.GeneratorVersion` (les leaderboards sont indexés par version → pas de scores incomparables),
2. `dotnet run -- --bake`, puis relancer les tests,
3. ré-exporter les leaderboards (ci-dessous).

## MCP / Pipeline

`.mcp.json` déclare le serveur `unity mcp` pour ce projet : redémarrer Claude Code et approuver le serveur. Il a besoin d'un éditeur ouvert (`unity open MummyEscape`). Commandes spécifiques au jeu :

```bash
unity command mummy_levels           # tableau des niveaux depuis l'éditeur
unity command mummy_level --id 1-6   # détail d'un niveau
unity command mummy_setup            # (re)crée scène + réglages
```

## En ligne (Unity Gaming Services)

Sans configuration, le jeu tourne **hors ligne** (sauvegarde locale, classement/amis désactivés proprement). Pour activer :
1. Lier le projet : Edit › Project Settings › Services (organisation + projet UGS).
2. Activer Authentication (anonyme), Leaderboards, Friends, Cloud Save dans le dashboard.
3. Menu **Mummy Escape › Online › Export leaderboard configs** → déployer `Assets/_Project/Online` via Services › Deployment (ou `ugs deploy`).

Score de classement = `coups×10000 + PV perdus×1000 + interactions` (plus bas = meilleur). La progression des amis est publiée dans Cloud Save (clé publique `progress`).

## Avant publication

- Installer les modules de build : `unity install-modules --module android --module ios`.
- `ShareService.GameUrl` est un lien factice à remplacer par celui du store.
- Le partage Android envoie du texte seulement (pas d'image) ; le bouton Quitter est masqué sur iOS (règle Apple).
- Graphismes (pixel-art) et sons sont générés par code (`ArtLibrary`, `AudioService`) : placeholders cohérents à remplacer par de vrais assets.
- Bundle id : `com.mummyescape.game`.
