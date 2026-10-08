# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

Mummy Rush : puzzle-labyrinthe mobile (Unity 6000.3.23f1, URP 2D, C#). Le `README.md` (en français) est la référence détaillée : contrat de difficulté par acte, garanties du générateur, localisation, RGPD, builds. Ce fichier n'en reprend que l'essentiel pour travailler dans le code.

## Disposition

- **Le projet Unity est `MummyEscape/`**, pas la racine. `Assets/`, `Packages/`, `ProjectSettings/`… à la racine sont un projet vide créé par erreur et ignoré par git : ne rien y modifier.
- `MummyEscape/Assets/_Project/` contient tout le code du jeu ; `server/PvpMatchmaking/` est le module Cloud Code ; `tools/` contient les outils .NET hors Unity ; `store/google-play/` la fiche du store ; `docs/` les textes légaux publiés par GitHub Pages (générés depuis l'éditeur, ne pas les modifier à la main).

## Commandes

```bash
dotnet test tools/CoreTests                                          # tous les tests Core + Pvp, sans Unity
dotnet test tools/CoreTests --filter "TestCategory!=Slow"            # sans le balayage de 60 variantes par niveau
dotnet test tools/CoreTests --filter "FullyQualifiedName~Solver"     # un sous-ensemble
dotnet build server/PvpMatchmaking.sln                               # module serveur
dotnet run -c Release --project tools/LevelLab                       # stats du générateur ; autres options dans README › LevelLab
dotnet run --project tools/LevelLab -- 2-7 0                         # carte ASCII + solution optimale du niveau 2-7, variante 0
dotnet run -c Release --project tools/PvpSim -- solo                 # simulation de joueurs / bots PvP
```

Éditeur (via la CLI `unity` ; un éditeur ouvert sur `MummyEscape/` est requis, MCP déclaré dans `.mcp.json`) :
`unity command editor_status`, `unity command recompile`, `unity command mummy_levels`, `unity command mummy_level --id 1-6 --variant 0`, `unity command mummy_autoplay --steps 40`, `unity command mummy_loc_check`, `unity command mummy_build_play` (AAB signé). Build Android batch : `-executeMethod MummyEscape.EditorTools.BuildScript.AndroidDev` (voir README).

**Mode Play de l'utilisateur** : il joue souvent dans l'éditeur. `unity command recompile` pendant le Play arrête sa session. Toujours lancer `unity command editor_status` d'abord ; si `playMode` vaut `playing`, ne pas recompiler dans Unity, vérifier avec `dotnet test` / `dotnet build`, et le dire.

## Architecture

Les assemblies (asmdef) imposent les couches :

- **`MummyEscape.Core`** (`Scripts/Core/`, `noEngineReferences`) : C# pur, sans `UnityEngine`. Modèle (`Level`, `Tile`, `Rules`), génération (`LevelGenerator`, `DifficultyTable`, `Pcg32`), résolution (`Solver` BFS exact, `LevelValidator`), session de jeu (`GameSession`). Toute la logique de règles vit ici.
- **`MummyEscape.Pvp`** (`Scripts/Pvp/`, C# pur) : logique serveur des duels/2v2/guildes/économie (Elo, vérification des courses `RunReplay`/`RunTiming`, bots `HumanPlayer`). **Compilée trois fois** : par Unity, par `tools/CoreTests` et par le module Cloud Code `server/PvpMatchmaking/` (qui lie ces sources + Core). Une modification ici change le serveur : l'utilisateur doit redéployer le module.
- **`MummyEscape.Runtime`** (`Scripts/Runtime/`) : le jeu Unity. `App/GameApp` est la racine de composition (singleton `GameApp.I` qui crée tous les services une seule fois). `Game/GameController` (classe partielle : solo, duel en direct, relais) pilote une partie à partir de `GameSession`. `UI/UIRouter` gère les écrans (`UI/Screens/`), tous construits en code via `UIKit` (référence 1080×1920, `FitInParent` pour les écrans allongés). `Visual/ArtLibrary` génère le pixel-art de façon procédurale, `Visual/TombTheme` donne l'ambiance de chaque acte, `World/FxRig` joue les effets.
- **Implémentations optionnelles**, chacune dans son asmdef et compilée seulement si son paquet / define est présent : `Online.UGS` (Auth, Leaderboards, Friends, Cloud Save), `Online.UGS.Pvp` (Cloud Code), `Online.UGS.Relay`, `Online.GooglePlay` (`MUMMY_GPGS`), `Ads.AdMob` (`MUMMY_ADMOB`), `Store.UnityIap` (`MUMMY_IAP`). Le Runtime ne voit que les interfaces (`IOnlineService`, `IPvpService`…) ; sans configuration, `OfflineOnlineService` / `LocalPvpService` (bots, éditeur et builds de dev seulement) prennent le relais.
- **Éditeur** : `Scripts/Editor/` (setup, `BuildScript`, exports leaderboards et textes légaux, `LocCheck`) et `Scripts/Editor.Pipeline/` (commandes `mummy_*` de la CLI `unity`).

### Contraintes à respecter

- **C# 9** (`LangVersion` 9.0 dans `CoreTests.csproj`) pour Core, Pvp et les tests : pas de fonctionnalités C# 10+.
- **Déterminisme** : (niveau, variante) donne la même carte sur tous les appareils et sur le serveur (graine = hash(`GeneratorVersion`, niveau, variante), PCG32 maison). Toute source d'aléa passe par `Pcg32` (jamais `System.Random`) et rien ne doit dépendre d'un ordre d'itération non garanti.
- **Modifier `LevelGenerator`, `DifficultyTable` ou `Rules` change les labyrinthes** : incrémenter `DifficultyTable.GeneratorVersion` (les leaderboards et les duels sont indexés dessus), enregistrer la nouvelle empreinte dans `GeneratorVersionTests` (le test donne la valeur), décrire la version et poser le tag `generator-vN` (procédure dans `GENERATEUR.md`), relancer LevelLab et les tests, puis ré-exporter les leaderboards.
- **Localisation** : le texte français est la clé (`Loc.T("…")`, `Loc.F`, `Loc.P`), traductions dans `Runtime/Localization/Loc.En.cs`. Les libellés passés à `UIKit` sont traduits automatiquement ; les textes dynamiques passent par `Loc`. Dans Core, utiliser `CoreText`. Vérifier avec `mummy_loc_check`.
- **Confidentialité** : ni analytics ni traceurs. Changer les données collectées implique de revoir la politique (`Runtime/UI/Legal/LegalTexts*.cs`, `PrivacyService.PolicyVersion`) et `Plugins/iOS/PrivacyInfo.xcprivacy`.
- **Version** : `PlayerSettings.bundleVersion` ; le code de version Android en dérive (`1.2.3` → `1020301`, `BuildScript.VersionCode`). La clé de signature est dans `~/.mummyescape/`, hors du dépôt.

## Captures depuis l'éditeur

En Play, le player loop est gelé quand Unity n'a pas le focus : exécuter `UnityEngine.Application.runInBackground = true;` via `unity command eval`, puis `ScreenCapture.CaptureScreenshot(@"<chemin absolu>.png")`. Ne jamais appeler `.Result` / `.Wait()` sur une tâche de service du jeu dans un `eval` : la tâche reprend sur le thread principal que l'eval bloque, et l'éditeur se fige.
