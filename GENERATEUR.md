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

## Tombeaux stockés (mode sans fin)

Les tombeaux du mode sans fin ne sont pas régénérés : ils sont écrits une fois dans `MummyEscape/Assets/_Project/Resources/Tombs/endless.txt` (`TombArchive`, une ligne par tombeau, ajout seulement) par `dotnet run -c Release --project tools/LevelLab -- --endless [n]`. Changer de version ne les touche pas, et leurs classements (`endless_f/n/x`) ne portent pas la version. En revanche une modification de `Rules` ou du `Solver` s'applique à eux : `EndlessTests.StoredTombs_AllLoadAndAreSolvable` vérifie que chacun a toujours une sortie. Ne jamais renuméroter `TileType`, `TrapKind` ni `TeleporterKind` (les valeurs sont stockées) : seulement ajouter à la fin.

L'acte Halloween suit la même règle : ses 10 tombeaux sont écrits une fois dans `Resources/Tombs/halloween.txt` par `-- --halloween` (l'outil refuse de réécrire un fichier existant), `HalloweenTests.StoredTombs_TenLoadAndAreSolvable_EasyThenNormal` les vérifie.

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
| 14 | 2026-10-14 | (à poser) | **Nouveautés par acte.** Acte 2 : **bandelettes** (`TileType.Wrappings`, 1 / 1 / 2 en F / N / X, sur un couloir du chemin) : avancer dedans torche allumée les brûle (un coup, la momie reste sur place, bit de `RuleState.Crumbled` partagé avec les dalles fragiles) ; torche éteinte, elles bloquent. Acte 3 : **faux mur** (`TileType.FalseWall`, 0 / 1 / 1) : un mur traversable entre deux points du chemin, raccourci de 4 à 14 coups sans portail ni bouton sauté ; jamais le seul chemin (`LevelValidator.CheckFalseWalls`), 0 point au barème ; après 150 tentatives sans place pour lui, le tombeau s'en passe (`LevelGenerator.MirageAttempts`). Acte 4 : **idoles** (`TileType.Idol`, 1 / 1 / 2, 3 points) : solides, leur regard balaie la ligne jusqu'au prochain mur (`Rules.Gaze`), au moins 2 cases de sol, jamais le départ. **Pièges périodiques en temps réel** : jets de flammes et regard d'idole sur un cycle d'1 s (0,85 s de charge, 0,15 s de tir), synchrones, depuis la chute du brouillard ; `RuleState.Tick` et la phase des jets disparaissent, les règles et le solveur traitent ces cases comme du sol, `GameSession.Tick` frappe (−1 PV par salve) et `RunReplay` rejoue à l'horloge des horodatages. Intros 2-0 (bandelettes), 3-0 (faux mur), 4-0 (idole) et 5-0 (jet en temps réel) mises à jour. |
| 13 | 2026-10-11 | `2f80c44` | **Campagne allégée, 10 niveaux par acte** dans chaque mode (`DifficultyTable.LevelsPerAct`, ids jusqu'à `X5-10`) ; progression remise à zéro (`SaveData.Version` 4). **Facile = découverte** : un étage partout, au plus 3 obstacles par tombeau en plus du premier mécanisme (mécanismes en plus, torche, pièges, chaque pic compté : `ModeDefinition.MaxObstacles`), la mécanique de l'acte hors compte ; plus de 2 pics imposés (0 à 2). **Normal** : les mécaniques du Facile (ni barrière bleue ni couloir leurre), 2 mécanismes et 3 pièges au plus, 2 pics qui barrent ; **Extrême** : 3 mécanismes, 3 pièges (au lieu de 4), barrières bleues, couloir leurre, tous les motifs. Étages en Normal et en Extrême : 1, 1, 1, 2, 2 (`ModeDefinition.MaxFloors` = 1 en Facile) ; l'acte 3 passe en 6×6 sur un étage, l'acte 5 en 5×5 sur deux. Une grande nouveauté par acte : le miroir passe de l'acte 2 à l'acte 3, la dalle tournante de l'acte 3 à l'acte 4. **Dent de scie** dans chaque acte (`DifficultyTable.Sawtooth`) au lieu d'une montée linéaire ; **combinaisons** dans le barème (`DifficultyScore.Combos` : miroir + dalle tournante, ombre + torche, portail maudit + étage, dalle tournante + étage). Seuils F / N / X par acte : 3·10·17·25, 5·14·21·30, 5·16·25·36, 4·16·28·42, 8·22·34·46. Pas de raccourci à pics à côté de plus de 2 mécanismes (un portail verrouillé en compte 2). Duels : Normal, actes 1–3, niveaux 3–8 ; relais 2v2 : niveaux 3–8 aux actes 1–2, 8–10 à l'acte 3. |
| 12 | 2026-10-09 | `f56beda` | **Trois modes solo** (Facile, Normal, Extrême), chacun avec les 5 actes de 5 niveaux ; ids `F1-1`, `N2-3`, `X5-5` (`LevelId.Mode`) ; finir un mode ouvre le suivant. **Barème de difficulté** (`DifficultyScore`) : chaque élément vaut des points (étage 8, miroir 5, dalle tournante 6, torche 5, laser 4, porte 3…). Chaque acte a 4 seuils : le maximum du Facile est le minimum du Normal, le maximum du Normal le minimum de l'Extrême, et le score monte du minimum au maximum au fil des 5 niveaux (`DifficultyTable.Target`). `DifficultyTable.FillBudget` choisit le mélange portes / torche / pièges / raccourcis le plus proche de la cible ; le validateur vérifie le score mesuré sur le tombeau (`CheckScore`). Étages : 1, 1, 2, 2, 3 selon l'acte. Chaque tombeau a **2 pièges à pics** qui barrent le chemin, à désamorcer (les pics de péage de la poussière en font partie) ; les raccourcis à pics (gain d'au moins 4 coups sur le trajet réel, `LevelValidator.SpikeSaving`) sont en plus, en Normal et en Extrême, sur les tombeaux à un étage et jamais avec une torche. Chaque étage d'un tombeau à plusieurs étages a au moins 12 cases de sol et 1 (Extrême : 2) point d'intérêt hors échelles (`CheckFloors`). Des pics déjà foulés se désamorcent à tâtons, torche éteinte ou aveuglé (`RuleState.Felt`, état du solveur dimensionné par tombeau : `StateLayout`). Motifs dessinés (`SpikePatterns`, voir plus bas) filtrés par mode, couloir à alcôves leurres en Normal et Extrême. 9 coups de plus par porte dans la fenêtre de par. Duels : Normal, actes 1–3, niveaux 2–4 ; relais 2v2 : Normal. |
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

## Motifs dessinés (v12)

`Core/Generation/SpikePatterns.cs` dessine, case par case, des formes que le générateur pose sur le chemin pour donner un choix au joueur : un chemin rapide mais dangereux par les pics, ou un chemin sûr mais plus long. Chaque forme est tournée pour suivre le chemin et peut être posée d'un côté ou de l'autre, ou en miroir. Ces formes complètent le tombeau sans le remplir.

Légende : `W` chemin, `^` pics, `+` chemin sûr, `#` roche à garder entre les deux, `T` torche murale, `.` indifférent. Le chemin traverse la forme de gauche à droite, sur la rangée des pics.

| Motif | Forme (de haut en bas) | Par les pics / par le détour |
|-------|------------------------|------------------------------|
| pont | `+++` / `+#+` / `W^W` | 2 coups / 6 coups |
| long pont | `+++++` / `+###+` / `W^WWW` | 4 coups / 8 coups |
| pont profond | `+++` / `+#+` ×3 / `W^W` | 2 coups / 10 coups |
| pont à torche | `.T.` / `+++` / `+#+` / `W^W` | comme le pont, poussière 2 ou 3 cases avant |
| long pont à torche | `..T..` / `+++++` / `+###+` / `W^WWW` | comme le long pont, poussière 2 ou 3 cases avant |
| serpent | `+++#+++` / `+#+#+#+` / `W^W^W^W` / `..+#+..` / `..+++..` / `...T...` | trois pics, chacun avec sa boucle (dessus, dessous, dessus) ; torche sur la boucle du dessous, poussière avant |

Avec torche, le joueur a deux options :
- aller tout droit, prendre les pics dans le noir (sans lumière, impossible de les désamorcer) et continuer à l'aveugle ;
- faire le détour, éviter les pics et récupérer la vue pour la suite.

`LevelGenerator.DrawPattern` pose une forme de deux façons :
- **ouvrir** : quand le chemin contourne déjà par le chemin sûr, on perce la rangée des pics et on y place les pics. Le chemin idéal passe alors par les pics ;
- **creuser** : on creuse le chemin sûr à côté d'une portion droite du chemin. Ce chemin ne doit jamais rejoindre une autre zone, pour qu'on ne puisse pas contourner une porte, ni déboucher à côté d'un téléporteur ou d'une échelle.

Chaque pic posé est vérifié sur le trajet complet (`RouteSaving`) : il doit faire gagner entre 4 et 14 coups. Les cases du motif sont réservées : les boucles ajoutées ensuite ne percent pas sa roche `#` et l'élagage ne les supprime pas.

**Couloir à alcôves leurres** (`DrawDecoyCorridor`) : une dalle tournante à l'entrée d'une portion droite, au moins 2 alcôves vides qui alternent d'un côté à l'autre, et un couloir prolongé d'une case après le virage. Une fois le tombeau tourné, la vraie sortie n'est qu'une ouverture parmi d'autres : il faut la retrouver de mémoire. Une variante place une torche au bout du prolongement et de la poussière avant la dalle. Ces alcôves sont la seule exception aux règles « chaque élément sert » et « aucune partie inutile ». Elles sont marquées `Level.IsDecoy`, ignorées par `CheckDeadEnds` et `CheckNoPocket`, et un tombeau compte au plus un couloir de ce type.
