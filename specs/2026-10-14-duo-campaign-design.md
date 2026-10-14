# Campagne 2v2 (co-op) : choix concrets

Les décisions de l'utilisateur (2026-10-07, complétées le 14) et où chacune vit dans le code. La plupart était déjà
construite (commits d266a64, 23d6546, 96099f6, a78de3b, 5fe95a2) ; ce document les fixe et ajoute ce qui manquait :
les skins 2v2.

## 1. Règles et tombeaux (Core, C# pur)

- `Core/Coop/CoopRules.cs` : un pas d'une momie = le pas solo de `Rules`, joué avec les canaux que tient la paire, puis
  partagé avec l'autre momie (leviers, boutons, pièges désarmés, dalles effondrées sont communs). Trois différences :
  dalle tenue (`Button` avec `Param = HeldPlate`, active seulement tant qu'une momie est dessus), changements du tombeau
  pour les deux, sortie qui ne laisse partir la paire qu'une fois les deux momies dessus. **Pas de blocage de corps** :
  deux momies peuvent partager une case. `Rules`/`Solver` solo ne sont pas touchés.
- `Core/Coop/CoopSolver.cs` : BFS exact sur l'état de la paire (chaque action de l'une ou l'autre coûte un coup) : prouve
  que le tombeau se résout, donne le par (étoiles), et prouve qu'une momie seule n'en sort pas (`CanReachExitAlone`).
- `Core/Coop/CoopCampaign.cs` : 20 tombeaux faits main, dessinés en texte, sur 3 actes, une mécanique à la fois :
  - Acte 1 « Le tombeau partagé » : un bouton ouvre la porte de l'autre, la sortie attend les deux.
  - Acte 2 « Les dalles de pression » : l'un tient la dalle pendant que l'autre passe.
  - Acte 3 « Leviers et barrières » : un levier ouvre une voie et en ferme une autre ; dalles fragiles qui ne portent
    qu'un passage.
  Jusqu'à 2× la taille d'un tombeau solo (21×15), vérifié par les tests.
- `Core/Coop/CoopSession.cs` : un essai, identique sur les deux téléphones. Chaque momie a sa `GameSession` (son
  brouillard) qui voit **ce que la torche de l'autre éclaire** (lumière partagée). Une momie morte termine l'essai.
- Étoiles comme en solo, par coups (contre le par) et par temps (`CoopTomb.TargetMs` = par × 700 ms).
- Tests : `Tests/EditMode/CoopTests.cs` (règles, chaque tombeau résolu par la paire et seulement par elle, tailles).

## 2. Synchronisation : hôte autoritaire sur le lien relais

Choix : **hôte autoritaire**, pas de lockstep. Le duel 2v2 en relais utilise déjà le même lien (`IRelayLink`,
`RelayMessage`, UGS Relay) avec un hôte ; le co-op y ajoute un ordre total des actions, ce qui suffit puisque
`CoopSession` est déterministe : appliquées dans le même ordre, les deux sessions restent identiques.

- `Runtime/Game/GameController.Coop.cs` : l'hôte applique chaque action (la sienne tout de suite, celle de l'invité à
  son arrivée) et envoie chaque action acceptée, numérotée (`Tick` = index dans l'historique), à l'invité. L'invité
  demande (`Tick = CoopWire.Wanted`) et n'applique que les réponses de l'hôte, dans l'ordre ; un refus le fait buter.
- Fin d'aperçu synchronisée (`Ready`), essai suivant quand les deux demandent le même tombeau (`Start` + numéro).
- Un départ (`Quit` ou lien perdu) termine la session des deux.
- Matchmaking : `Online.UGS.Relay/UgsCoopMatchmaker.cs` (lobby par duo ; l'équipier suit le chef, `FollowOnly`).
- Hors ligne / éditeur : coéquipier bot.

## 3. Progression, étoiles, récompenses (serveur, `Pvp/`)

- La progression vit sur le `Duo` existant (`TeamModels`, formé par invitation / groupe) : `Duo.Coop` = meilleur
  passage par tombeau. Jouer avec un autre ami = autre duo = on recommence.
- `PvpServer.SubmitCoopAsync(me, duoId, tomb, inputs, timeMs)` (Cloud Code `SubmitCoop`) : **rejoue** l'ordre de l'hôte
  (`CoopLogic.Replay`), refuse un passage qui ne sort pas les deux momies ou plus rapide que les animations
  (`RunTiming`), refuse un tombeau pas encore ouvert pour ce duo. Il garde le meilleur passage du duo et **les étoiles 2v2
  du joueur** (`PlayerPvpData.CoopStars`, à lui, quel que soit le duo). Récompense (`CoopConfig.FirstClearScarabs`
  scarabées ordinaires) au **premier passage de chaque joueur**, créditée par le serveur.
- `GetCoopBoardAsync` (Cloud Code `GetCoopBoard`) : classement des mêmes duos que le 2v2, par étoiles puis temps.
- **Nouveau** : skins 2v2 (`ScarabShop.CoopItems`) : quatre pièces vendues en scarabées ordinaires, chacune exigeant
  un minimum d'étoiles 2v2 ; `BuyScarabPieceAsync` vérifie ce minimum sur les étoiles que le serveur tient (erreur
  `COOP_STARS`). Pas de nouvelle fonction Cloud Code, mais le module doit être **redéployé** (ScarabShop et
  BuyScarabPiece changent).

## 4. Énergie

Un essai coûte un point d'énergie solo **à chacun** des deux joueurs (`CoopScreen` : `PlayGate.Solo` avant de chercher
la partie et avant chaque « Recommencer »). L'énergie solo est gardée par l'appareil, comme pour la campagne solo.

## 5. Entrée et écrans

- L'onglet Solo s'appelle « Campagne ». La campagne 2v2 se lance depuis le **groupe** (Accueil › Groupe : on invite un
  ami comme pour le duel 2v2, le chef lance « Campagne 2v2 », les deux téléphones suivent). Cette entrée a remplacé
  l'onglet 2v2 de la campagne (décision de l'utilisateur, commit 5fe95a2).
- `CoopScreen` : tombeaux du duo acte par acte, record du duo, étoiles du joueur, Elo 2v2 du duo, classement.
- Boutique : les skins 2v2 apparaissent dans leur emplacement, verrouillés tant que le joueur n'a pas les étoiles 2v2.
