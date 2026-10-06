# Module Cloud Code « PvpMatchmaking »

Le serveur des duels. Toute la logique (Elo, ligues, saisons, récompenses, boutique des sceaux, vérification des courses) vit dans
`MummyEscape/Assets/_Project/Scripts/Pvp/` et n'est écrite qu'une fois : le jeu et ce module compilent les mêmes fichiers, avec le
générateur de tombeaux et les règles du jeu (`Scripts/Core/`). Ce dossier ne contient que le branchement sur Unity Cloud :

- `PvpModule.cs` : les points d'entrée (duels, 2v2, guildes, tchat, énergie, porte-monnaie, données personnelles) :
  `FindDuel`, `SubmitRun`, `GetPvpProfile`, `ClaimSeasonRewards`, `BuyWithSeals`, `SpinSealWheel`, `GetPvpBoard`,
  `GetDuelHistory`, `ReportCheat`, `StartLiveDuel`, `GetLiveDuel`, `StartLiveBotDuel`, `SubmitLiveDuel`, `GetLiveDuelResult`,
  `GetTeams`, `InviteDuo`, `RespondDuo`, `LeaveDuo`, `FindDuoMatch`, `GetDuoBoard`, `StartBattleRun`, `GetBattle`,
  `GetGuild`, `CreateGuild`, `SearchGuilds`, `GetGuildBoard`, `JoinGuild`, `LeaveGuild`, `SetGuildRole`, `KickGuildMember`,
  `StartWar`, `StartRelayMatch`, `StartRelayBots`, `SubmitRelay`, `GetRelayResult`, `ReportRelayQuit`, `GetRelayHistory`,
  `GetChat`, `SendChat`, `GetChatInbox`, `BlockChat`, `SyncChatProfile`, `ReportChat`, `ShareReplay`, `GetSharedReplay`,
  `RefillPvpEnergy`, `GetWallet`, `VerifyPurchase`, `BuyPass`, `BuyTiers`, `BuyScarabs`, `BuyGoldItem`, `ClaimPassRewards`,
  `ExportPvpData`, `DeletePvpData` ;
- `CloudSavePvpStore.cs` : le stockage (Cloud Save, Leaderboards) ;
- `GooglePlayVerifier.cs` : la vérification des achats Google Play (API Android Publisher).

Un jeu trop ancien reçoit l'erreur `OUTDATED` : il affiche alors « Mettre à jour » avec un lien vers le Play Store.

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
   - `pvp_history` : donnée joueur **protégée**, les 10 derniers duels du joueur avec les deux courses de chacun (les replays).
     Le premier sur un tombeau y a un duel « en attente », complété quand quelqu'un affronte son fantôme.
   - `pvp_reports` : données « custom » **privées**, un dossier de triche par joueur signalé (clé = son identifiant).
   - `pvp_relay_history` : données « custom » **privées**, les 10 derniers matchs 2v2 jugés de chaque joueur (clé = son
     identifiant), les deux relais complets : l'historique et les replays 2v2 (`GetRelayHistory`).
   - `pvp_chat` : données « custom » **privées**, un canal par clé (`global`, `guild_<id>`, `dm_<id1>_<id2>`), ses 100
     derniers messages.
   - `pvp_chat_inbox` : données « custom » **privées**, par joueur : conversations privées, joueurs bloqués, anti-spam,
     signalements du jour et suspension (`BannedUntilUnixMs`).
   - `pvp_chat_reports` : données « custom » **privées**, un dossier de messages signalés par auteur (clé = son identifiant).
   - `pvp_shared_replays` : données « custom » **privées**, la copie de chaque replay partagé dans le tchat (clé = son id).
   - `pvp_live`, `pvp_relays`, `pvp_quit_reports` : données « custom » **privées**, les duels en direct, les matchs 2v2 et
     les abandons signalés.
   - `pvp_battles`, `pvp_duos`, `pvp_guilds`, `pvp_battle_queue` : données « custom » **privées**, combats 2v2 et guerres,
     duos, guildes et files d'attente des combats.
   - `pvp_team_index` : données « custom » **privées**, l'index des duos et des guildes (recherche, classements), réparti
     sur 16 clés (`duos_0`…`duos_15`, `guilds_0`…`guilds_15`) ; les anciennes clés `duos` et `guilds` sont encore lues.
   - `pvp_orders` : données « custom » **privées**, le registre des achats Google Play (une clé par commande) : un achat
     n'est crédité qu'une fois, et le registre sert de preuve d'achat (10 ans, voir la politique de confidentialité).
   L'énergie de combat et le porte-monnaie (scarabées dorés, pass, objets payants) sont dans la donnée joueur `pvp`.
6. **Secrets** (Dashboard › Cloud Code › Secrets, ou Secret Manager) :
   - `UGS_SERVICE_ACCOUNT_KEY` et `UGS_SERVICE_ACCOUNT_SECRET` : un compte de service UGS (Dashboard › Administration ›
     Service Accounts) avec le rôle « Leaderboards Admin ». Sert à retirer un joueur de tous les classements quand il
     supprime ses données en ligne.
   - `GOOGLE_PLAY_SERVICE_ACCOUNT` : le fichier JSON entier de la clé d'un compte de service Google Cloud, invité dans la
     Play Console (Utilisateurs et autorisations) avec « Afficher les données financières » et « Gérer les commandes ».
     L'API « Google Play Android Developer » doit être activée sur son projet Google Cloud. Sans ce secret, aucun achat
     n'est crédité (le jeu affiche « vérification impossible, ton achat n'est pas perdu »).
7. **Achats** : le plugin de paiement du jeu doit renvoyer le jeton d'achat Google Play (`purchaseToken`) comme
   `TransactionId` ; le serveur le vérifie auprès de Google (package `com.mummyrush.game`), le consomme puis crédite le
   porte-monnaie. Les achats demandent un compte Google Play Games connecté.

## Énergie

Le premier acte solo est libre. Ensuite, chaque partie solo coûte 1 énergie (10 au plus, gardée sur le téléphone) et chaque
duel ou combat 2v2 1 énergie de combat (3 au plus, gardée par le serveur, dépensée par `FindDuel`, `StartLiveDuel`,
`StartLiveBotDuel`, `StartRelayMatch`, `StartRelayBots` et `FindDuoMatch`, qui renvoient `ENERGY` quand il n'y en a plus).
Un point revient toutes les 6 minutes. Une pub rend 3 énergies solo (côté jeu) ou 1 de combat (`RefillPvpEnergy`), 5 fois par jour chacune.
Le pass premium de la saison lève la limite. Réglages : `Pvp/Energy.cs` (`EnergyConfig`).

## Signalements de triche

Un joueur peut signaler l'adversaire d'un duel de son historique (`ReportCheat`, 5 par jour). Le serveur copie le duel entier
(graine du tombeau, version du générateur, les deux courses horodatées) dans le dossier du joueur signalé, dans
`pvp_reports`. Un dossier signalé par 3 joueurs différents passe à `Flagged: true` (« à vérifier ») et le module l'écrit
dans les logs (Dashboard › Cloud Code › Logs). Aucune sanction automatique : on examine les dossiers depuis le Dashboard
(Cloud Save › Custom Items › `pvp_reports`). Pour revoir un duel signalé, la même graine et les mêmes actions rejouent la
course à l'identique (`RunReplay`).

## Tchat

Trois canaux : global (fermé aux mineurs, vérifié par le serveur), guilde (membres seulement, vérifié par le serveur) et messages privés
(`GetChat`, `SendChat`, `GetChatInbox`, `BlockChat`, `ReportChat`). Le jeu interroge le serveur toutes les 4 s quand le
tchat est ouvert, puis de plus en plus rarement (jusqu'à 15 s) tant que rien ne bouge. Les messages privés ne passent
qu'entre amis : le jeu envoie au serveur la liste d'amis du joueur (`SyncChatProfile`, 200 au plus) et s'il est mineur
(le canal global lui est alors refusé). Le serveur nettoie chaque message (200 caractères, liens et insultes masqués), limite le débit
(1,5 s entre deux messages, 12 par minute) et refuse un message privé si le destinataire a bloqué l'auteur.
`ShareReplay` copie un duel de `pvp_history` ou un match 2v2 jugé dans `pvp_shared_replays` et le poste dans le canal ;
`GetSharedReplay` le rend à qui le touche.

Modération : un message signalé par 3 joueurs différents est masqué et copié dans `pvp_chat_reports` (10 signalements par
joueur et par jour). Pour suspendre un joueur, mettre `BannedUntilUnixMs` (millisecondes Unix) dans sa clé de
`pvp_chat_inbox` (Cloud Save › Custom Items) ; le jeu affiche la date de fin. Pour retirer un message, passer son
`Hidden` à `true` dans le canal de `pvp_chat`. Au-delà de quelques milliers de joueurs actifs dans le tchat, passer à
Vivox (Unity) plutôt que d'interroger Cloud Save.

## Versions

Les fantômes et les tombeaux dépendent de `DifficultyTable.GeneratorVersion` : un jeu d'une autre version reçoit
`OUTDATED` et ne croise jamais un fantôme d'une autre version. Redéployer le module à chaque changement du générateur.
