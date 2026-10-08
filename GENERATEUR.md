# Versions du générateur de tombeaux

`DifficultyTable.GeneratorVersion` (`MummyEscape/Assets/_Project/Scripts/Core/Generation/DifficultyTable.cs`) entre dans la graine de chaque labyrinthe, dans les ids des leaderboards (`v{version}s2_1-1`) et dans les duels. Le serveur répond `OUTDATED` à un jeu dont la version diffère. Deux versions différentes ne doivent donc jamais produire les mêmes ids pour des cartes différentes.

## Changer de version

Toute modification de `LevelGenerator`, `DifficultyTable`, `LevelSpec`, `Rules`, `Solver` ou `LevelValidator` qui change les cartes produites demande de :

1. incrémenter `GeneratorVersion` ;
2. lancer `dotnet test tools/CoreTests`. `GeneratorVersionTests` échoue et donne la nouvelle empreinte : l'ajouter à `Recorded` (`Tests/EditMode/GeneratorVersionTests.cs`) ;
3. relancer LevelLab (aucun niveau ne doit échouer) ;
4. décrire la version ci-dessous et poser le tag sur le commit : `git tag -a generator-vN <commit> -m "Générateur vN : …"` ;
5. ré-exporter et déployer les leaderboards (*Mummy Rush › Online › Export leaderboard configs*), puis redéployer le module Cloud Code (il compile le même générateur).

Le test d'empreinte porte sur la variante 0 de chaque niveau et sur quelques arènes de duel et de relais 2v2. Si le test échoue alors que la version n'a pas été touchée, une modification a changé les cartes sans le dire.

## Retrouver une ancienne version

Chaque version a son tag `generator-vN` (`git tag -n1 'generator-v*'`). Pour revoir les cartes d'une version sans toucher à la copie de travail :

```bash
git worktree add ../Escape-gen9 generator-v9
cd ../Escape-gen9/tools/LevelLab && dotnet run -- 2-7 0     # carte ASCII et solution de 2-7, variante 0, en v9
cd - && git worktree remove ../Escape-gen9
```

Le tag pointe sur le commit qui a introduit la version ; des commits ultérieurs de la même version ont pu toucher au jeu, mais pas aux cartes.

## Historique

| Version | Date | Commit | Changements |
|---------|------|--------|-------------|
| 11 | 2026-10-07 | `a81060d` | La torche murale vaut le détour : la poussière tombe tôt (premier tiers du chemin), deux pics impossibles à contourner après elle, à désamorcer à la lumière (deux coups tuent). Le validateur n'accepte des pics en travers du chemin que s'il y a une torche murale. Fenêtre de par jusqu'à 56 coups pour les tombeaux avec poussière. Relais 2v2 : pas de poussière, un piège d'obscurité à la place. |
| 10 | 2026-10-04 | `a0b390d` | La torche murale n'est plus le long du chemin idéal : dans un autre couloir ou au fond d'une alcôve creusée pour elle, à 2–6 coups (`LevelValidator.CheckTorches`). Finir dans le noir ou payer le détour. |
| 9 | 2026-10-04 | `a1d71f0` | Pièges **miroir de Seth** (acte 2+, inverse les commandes pendant 10 pas) et **dalle tournante** (acte 3+, le tombeau tourne d'un quart de tour à l'écran). État du solveur élargi ; le validateur rejette les tombeaux dont le chemin idéal laisse une poche. |
| 8 | 2026-10-04 | `e841d08` | Les pics ne gardent que des raccourcis, chacun doublé d'un détour sans pics ; seuls les pics se désamorcent. |
| 7 | 2026-10-03 | `9f6e934` | Paliers par acte : 1/2/3 mécanismes et pièges pour les niveaux 1–3 / 4–7 / 8–10 ; 2e étage dès 3-5 ; 3 étages à l'acte 5. |
| 6 | 2026-10-03 | `831963d` | Chaînes de portes construites à rebours depuis la sortie (allers-retours obligés). Le sol hors du chemin relie toujours deux points de ce chemin (chemins courts et longs) ; les poches redeviennent de la roche ; tombeau recadré sur son sol ; grilles des actes 4–5 réduites. |
| 5 | 2026-10-03 | `3a75343` | Tombeaux courts (≈ 30 coups max), difficulté par les mécaniques et non par la longueur. Jamais bloqué depuis aucune situation atteignable ; courants et dalles deviennent des raccourcis à sens unique ; chaque élément sert (plus de leurres). Repli déterministe avec une fenêtre de par élargie. Acte 5 : 2 étages, 4×4. |
| 4 | 2026-10-03 | `9dce3cd` | Téléporteurs des actes 1–2 au fond d'un cul-de-sac ; 2 étages maximum. |
| 3 | 2026-10-02 | `d892125` | Mécaniques d'acte dans le générateur et le solveur : courants, dalles fragiles, leviers et barrières laser, jets de flammes. Défaite « Emmurée » (`Solver.CanEscape`). |
| 2 | 2026-10-02 | `e581180` | Réécriture autour des mécanismes obligatoires (bouton ou portail sur chaque niveau), impasses qui servent, sortie loin de l'entrée ou derrière un bouton lointain, acte 1 ≥ 15 coups. Un nouveau labyrinthe à chaque partie (variante). Table de tentatives précalculée supprimée. |
| 1 | 2026-10-02 | `63383b5` | Générateur procédural déterministe initial (5 actes), solveur BFS, tests de résolution. |

Empreintes enregistrées (`GeneratorVersionTests.Recorded`) : à partir de la v11.
