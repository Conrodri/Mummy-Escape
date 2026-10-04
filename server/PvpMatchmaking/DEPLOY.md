# Module Cloud Code « PvpMatchmaking »

Le serveur des duels. Toute la logique (Elo, ligues, saisons, récompenses, boutique des sceaux, vérification des courses) vit dans
`MummyEscape/Assets/_Project/Scripts/Pvp/` et n'est écrite qu'une fois : le jeu et ce module compilent les mêmes fichiers, avec le
générateur de tombeaux et les règles du jeu (`Scripts/Core/`). Ce dossier ne contient que le branchement sur Unity Cloud :

- `PvpModule.cs` : les points d'entrée `FindDuel`, `SubmitRun`, `GetPvpProfile`, `ClaimSeasonRewards`, `BuyWithSeals`,
  `GetPvpBoard` ;
- `CloudSavePvpStore.cs` : le stockage (Cloud Save, Leaderboards).

## Compiler

```
dotnet build server/PvpMatchmaking.sln
```

Les tests de la logique se lancent avec le reste : `dotnet test tools/CoreTests`.

## Mise en route (une fois)

1. **Lier le projet Unity** à un projet Unity Cloud (Edit › Project Settings › Services). Sans ça, le jeu reste hors ligne :
   l'éditeur et les builds de développement jouent alors les duels contre des adversaires simulés.
2. **Déployer le module** : Services › Deployment, cocher `PvpMatchmaking` (référence
   `Assets/_Project/CloudCode/PvpMatchmaking.ccmr`, qui pointe vers `server/PvpMatchmaking.sln`), puis « Deploy Selected ».
   Unity publie le projet pour Linux (profil `Properties/PublishProfiles/FolderProfile.pubxml`) et l'envoie. Il faut le
   SDK .NET 8 ou plus récent sur la machine. Le module doit garder le nom `PvpMatchmaking` : le jeu appelle ce nom.
3. **Leaderboard `pvp_elo`** (Dashboard › Leaderboards) :
   - tri du plus haut au plus bas ;
   - mise à jour : garder le dernier score (l'Elo peut baisser) ;
   - remise à zéro chaque mois, le 1er à 00:00 UTC, **avec archivage** (les versions archivées servent au classement
     « Mois dernier » et au calcul du Top 100).
4. **Access Control** : interdire aux joueurs d'écrire dans `pvp_elo` uniquement (pas dans tous les leaderboards : les
   classements solo sont écrits par le jeu). Règle `Deny`, principal `Player`, action `Write`, ressource
   `urn:ugs:leaderboards:/v1/projects/*/leaderboards/pvp_elo/scores/players/*`.
   Même sans cette règle, un score falsifié ne tient pas : le jeu lit le classement par `GetPvpBoard`, qui compare chaque
   entrée à l'Elo protégé du joueur, réécrit les scores faux et met à 0 les entrées sans duel.
5. **Cloud Save** : rien à créer.
   - `pvp` et `pvp_pending` : données joueur **protégées** (le joueur les lit, seul le serveur les écrit).
   - `solo_total_stars` : écrit par le jeu, lu par le serveur pour ouvrir les duels à 35 étoiles.
   - `pvp_queue` : données « custom » **privées** (illisibles par les joueurs), une clé par version du générateur et
     tranche de 100 Elo (`v8_b10`…).
   - `pvp_board` : données « custom » **privées**, le classement vérifié de chaque mois (`board_2026-10`), gardé 5 minutes.

## Versions

Les fantômes et les tombeaux dépendent de `DifficultyTable.GeneratorVersion` : un jeu d'une autre version reçoit
`OUTDATED` et ne croise jamais un fantôme d'une autre version. Redéployer le module à chaque changement du générateur.
