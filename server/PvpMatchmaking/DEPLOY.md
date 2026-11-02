# Module Cloud Code « PvpMatchmaking »

Le serveur des duels. Toute la logique (Elo, ligues, saisons, récompenses, boutique des sceaux, vérification des courses) vit dans
`MummyEscape/Assets/_Project/Scripts/Pvp/` et n'est écrite qu'une fois : le jeu et ce module compilent les mêmes fichiers, avec le
générateur de tombeaux et les règles du jeu (`Scripts/Core/`). Ce dossier ne contient que le branchement sur Unity Cloud :

- `PvpModule.cs` : les points d'entrée (duels, 2v2, guildes, tchat, énergie, porte-monnaie, données personnelles) :
  `FindDuel`, `SubmitRun`, `GetLadder`, `SetCountry`, `GetPvpProfile`, `ClaimSeasonRewards`, `BuyWithSeals`, `SpinSealWheel`, `GetPvpBoard`,
  `GetDuelHistory`, `ReportCheat`, `StartLiveDuel`, `GetLiveDuel`, `StartLiveBotDuel`, `StartLiveSearch`, `SubmitLiveDuel`, `GetLiveDuelResult`,
  `GetTeams`, `InviteDuo`, `RespondDuo`, `SearchMate`, `CancelMateSearch`, `LeaveDuo`, `FindDuoMatch`, `GetDuoBoard`, `StartBattleRun`, `GetBattle`,
  `GetGuild`, `CreateGuild`, `SearchGuilds`, `GetGuildBoard`, `JoinGuild`, `LeaveGuild`, `SetGuildRole`, `KickGuildMember`,
  `StartWar`, `ReplaceWarRunner`, `StartRelayMatch`, `StartRelayBots`, `SubmitRelay`, `GetRelayResult`, `ReportRelayQuit`, `GetRelayHistory`,
  `GetChat`, `SendChat`, `GetChatInbox`, `BlockChat`, `SyncChatProfile`, `ReportChat`, `ShareReplay`, `GetSharedReplay`,
  `RefillPvpEnergy`, `GetWallet`, `VerifyPurchase`, `BuyPass`, `BuyTiers`, `BuyScarabs`, `BuyGoldItem`, `ClaimPassRewards`,
  `OpenScarabs`, `BuyScarabPiece`, `BuyScarabSet`, `SpinScarabWheel`, `BuyWheelLegendary`,
  `GetQuests`, `VisitQuest`, `ClaimQuest`, `ExportPvpData`, `DeletePvpData` ;
- `CloudSavePvpStore.cs` : le stockage (Cloud Save, Leaderboards) ;
- `GooglePlayVerifier.cs` : la vérification des achats Google Play (API Android Publisher).

Un jeu trop ancien reçoit l'erreur `OUTDATED` : il affiche alors « Mettre à jour » avec un lien vers le Play Store.

**Forcer la mise à jour** : `GetStatus` renvoie `MinVersion`, la plus ancienne version du jeu acceptée
(`Pvp/ClientVersion.cs`, `ClientVersion.Minimum`). Un jeu plus ancien (0.1.15 et suivants) bloque tout l'écran sur « Nouvelle
version ! » avec un bouton vers sa page du Play Store, au lancement et à chaque retour au premier plan (jamais dans l'éditeur).
Après la sortie d'une version sur le store : monter `ClientVersion.Minimum` puis redéployer le module (ou poser
`MinVersion` par `SetStatus` depuis un compte de l'équipe, sans redéployer). Ne jamais le monter avant que la version soit
téléchargeable : les joueurs resteraient bloqués. Le build refuse une version du jeu sous ce minimum.

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
3. **Leaderboards** (Dashboard › Leaderboards) : trois, rien d'autre. Le jeu ne les lit plus lui-même : il appelle
   `GetLadder(ladder, scope, seasonsAgo)` (`Pvp/PvpServer.Ladders.cs`), qui sert les quatre classements (duel, 2v2, tour,
   sans fin) pour le monde, le pays du joueur ou ses amis. Le pays est gardé par le serveur (`SetCountry`, code ISO à deux
   lettres, `""` pour aucun ; `COUNTRY` s'il est invalide) et écrit en métadonnée `c` de chaque entrée.
   - `pvp_elo` (inchangé) : tri du plus haut au plus bas ; mise à jour « garder le dernier score » (l'Elo peut baisser) ;
     remise à zéro chaque mois, le 1er à 00:00 UTC, **avec archivage** (les versions archivées servent au « Mois dernier »
     et au Top 100).
   - `ladder_tower` : tri **du plus bas au plus haut** (score encodé : le plus d'étages, puis le moins de temps) ; mise à
     jour **« garder le dernier score »** (le serveur envoie toujours le meilleur du joueur, et le réécrit quand il change de
     pays) ; pas de remise à zéro. Indépendant de la version du générateur.
   - `ladder_endless` : tri **du plus bas au plus haut** (score encodé : le plus de points, puis le moins de temps total ;
     points = tombeaux franchis × 1 en Facile, × 2 en Normal, × 3 en Extrême) ; **« garder le dernier score »** ; pas de
     remise à zéro.
   Avec « garder le meilleur » au lieu du dernier, un changement de pays ne suivrait pas : à éviter.
   Les anciens classements peuvent être **supprimés** : les niveaux (`v13s2_…`, `v14s2_…`), `endless_e`, `endless_n`,
   `endless_x` et `tower_v…`. Plus rien ne les écrit ; les jeux 0.1.14 qui les lisent encore voient des listes figées.
   Les pages pays filtrent les 1000 premiers de chaque classement (cache de 5 minutes, Cloud Save `pvp_ladders`) : au-delà
   de quelques milliers de joueurs par classement, passer à un classement par pays.
4. **Access Control** : interdire aux joueurs d'écrire dans **tous** les leaderboards (règle à garder). Règle `Deny`,
   principal `Player`, action `Write`, ressource `urn:ugs:leaderboards:/v1/projects/*/leaderboards/*/scores/players/*`.
   Seul ce module y écrit :
   - `pvp_elo` : l'Elo, après chaque duel jugé. Même sans la règle, un score falsifié ne tient pas : `GetLadder` (et
     l'ancien `GetPvpBoard`) compare chaque entrée à l'Elo protégé du joueur ;
   - `ladder_tower`, `ladder_endless` : `SubmitSoloRuns` rejoue chaque course envoyée (règles, animations) et écrit le
     meilleur du joueur. Une échelle sans fin avance de 10 tombeaux au plus au-delà du dernier vu ; la tour est suivie étage
     par étage. Les niveaux de la campagne n'ont plus de classement.
   Sans la règle, une version modifiée du jeu pourrait encore écrire un faux score solo : elle est indispensable.
   Le 2v2 n'a pas de leaderboard : il vient de l'index des duos (`pvp_team_index`), avec les pays des deux membres.
5. **Cloud Save** : rien à créer.
   - `pvp` et `pvp_pending` : données joueur **protégées** (le joueur les lit, seul le serveur les écrit).
   - les duels s'ouvrent quand le serveur a vu le niveau Facile 1-10 réussi (étoiles du porte-monnaie, `pvp`).
   - `pvp_queue` : données « custom » **privées** (illisibles par les joueurs), une clé par version du générateur et
     tranche de 100 Elo (`v8_b10`…).
   - `pvp_board` : données « custom » **privées**, le classement vérifié de chaque mois (`board_2026-10`), gardé 5 minutes.
   - `pvp_ladders` : données « custom » **privées**, les 1000 premiers de chaque classement (`Tower`, `Endless`,
     `Duel_2026-11`) et le duel vérifié de chaque pays (`Duel_2026-11_FR`), gardés 5 minutes.
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
     duos, guildes et files d'attente des combats. Les files sont par version du générateur (`duo_g<N>`, `war3_g<N>`…) :
     après un changement de `GeneratorVersion`, les combats en cours de l'ancienne version finissent annulés (sans Elo) et
     les anciennes clés (`duo`, `war3`, `war5`, `war10`) ne sont plus lues (à effacer à la main si besoin).
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

## Porte-monnaie doré

Le serveur fait foi pour les scarabées dorés, les pass achetés et les objets payants (`gold_…`, `pass_…`) : le jeu n'en garde
qu'une copie d'affichage, remplacée par chaque réponse du serveur (un pass ou un objet payant absent du serveur est retiré du
téléphone, et la copie Cloud Save d'un autre appareil n'en apporte plus). Toute dépense passe par un point d'entrée qui
vérifie le prix et débite (`BuyPass` 40 dorés, refusé avec `OWNED` si la saison est déjà premium ; `BuyTiers`, `BuyScarabs`,
`BuyGoldItem`) ; `GOLD` s'il en manque, sans rien débiter.

`BuyTiers` et `BuyScarabs` livrent sur le téléphone (paliers, scarabées) : ils prennent un `requestId` (facultatif pour les
anciens jeux). Une demande déjà acceptée est rendue telle quelle sans débiter de nouveau, et les 50 dernières sont listées
dans `Wallet.Requests` : un téléphone qui a perdu la réponse livre l'achat quand un portefeuille la liste (`GetWallet` au
lancement), et l'oublie s'il ne la liste toujours pas après 5 minutes.

Aucune migration : avant ce porte-monnaie, l'or du téléphone ne venait d'aucun achat réel (aucune boutique n'existait) ; il
n'est pas recrédité, la valeur du serveur le remplace.

## Scarabées

Les scarabées (la monnaie du jeu) sont gardés par le serveur une fois reportés. Au premier portefeuille d'un compte, le
téléphone envoie `OpenScarabs` : son solde (plafonné à 20 000, `PvpServer.ScarabsCarriedMax`), ses meilleures étoiles par
niveau, les tombeaux sans fin déjà franchis et les pièces de boutique ou de la roue qu'il possède (les autres sont ignorées).
Ce report n'a lieu qu'une fois (`Wallet.ScarabsOpen`). Ensuite, le serveur crédite lui-même les courses rejouées par
`SubmitSoloRuns` (un niveau paie ses nouvelles étoiles, 10 chacune + 2, comptées sur les coups ; un tombeau sans fin paie
une fois), le pass (`ClaimPassRewards`), le premier passage d'un tombeau 2v2 (`SubmitCoop`) et les offres en or
(`BuyScarabs`). Toute dépense passe par lui : `BuyScarabPiece`, `BuyScarabSet` (collection d'un acte à -20 %),
`SpinScarabWheel`, `BuyWheelLegendary` et `CreateGuild` (500). Les prix sont ceux de `Pvp/ScarabShop.cs`, que le catalogue
du jeu lit aussi. Erreurs : `CLOSED` (pas encore reporté), `SCARABS` (il en manque), `STARS` (étoiles de l'acte manquantes).

## Quêtes

Les quêtes du jour (remises à zéro à 00:00 UTC) et la quête de la semaine (lundi 00:00 UTC) sont dans
`PlayerPvpData.Quests` (`Pvp/Quests.cs`). Le serveur compte lui-même ce qu'il vérifie : niveau de campagne ou tombeau sans
fin réussi (`SubmitSoloRuns`, une fois par course), duel 1v1 jugé et gagné (duel asynchrone, en direct, contre un bot ;
une manche de combat de guilde compte comme duel joué), partie 2v2 (tombeau de la campagne 2v2 `SubmitCoop`, relais
jugé). Les visites sont signalées par le téléphone (`VisitQuest`). `ClaimQuest` paie une quête terminée une fois (XP du
pass, scarabées, sceaux, or) ; erreurs `UNKNOWN_QUEST`, `NOT_DONE`, `CLAIMED`, et `CLOSED` pour des scarabées pas encore
reportés (la quête reste à réclamer).

## Énergie

Le premier acte solo est libre. Ensuite, chaque partie solo coûte 1 énergie (10 au plus, gardée sur le téléphone) et chaque
duel ou combat 2v2 1 énergie de combat (3 au plus, gardée par le serveur, dépensée par `FindDuel`, `StartLiveDuel`,
`StartLiveBotDuel`, `StartRelayMatch`, `StartRelayBots` et `FindDuoMatch`, qui renvoient `ENERGY` quand il n'y en a plus).
Un point revient toutes les 2 minutes. Une pub rend 3 énergies solo (côté jeu) ou 3 de combat (`RefillPvpEnergy`), 5 fois par jour chacune.
Le pass premium de la saison lève la limite. Réglages : `Pvp/Energy.cs` (`EnergyConfig`).
Un duel en direct ou un match 2v2 **annulé** rend son point à chacun (voir ci-dessous).

## Duel en direct et 2v2 : matchs annulés, bots

- **Se montrer** : chaque joueur d'un duel (chaque duo d'un 2v2) doit appeler lui-même le serveur pour ce match
  (`StartLiveDuel`/`StartRelayMatch` pour l'hôte ; `GetLiveDuel`, `BanLiveDuel`, `SubmitLiveDuel`, `GetLiveDuelResult`,
  `BanRelay`, `SubmitRelay`, `GetRelayResult` pour les autres). Un camp qui ne s'est jamais montré : au jugement (45 s après la
  création au plus tôt) le match est **annulé** (`Void` dans la réponse, l'historique et le match) : ni Elo, ni sceaux, ni
  quêtes, ni points de guilde ; l'énergie est rendue. Un salon truqué (l'hôte qui « trouve » un joueur qui ne joue pas) ne
  fait donc plus perdre personne. Un match déjà créé n'est rendu qu'à ses joueurs.
- **Refus** : `StartLiveDuel` vérifie aussi que l'adversaire a ouvert les duels (`LOCKED`) ; `Refused` nomme le joueur à
  cause de qui le duel n'est pas créé (`LOCKED`, `ENERGY`, `DIVISION`), l'autre cherche à nouveau. Deux ligues voisines
  (duel) ou divisions voisines (2v2) se rencontrent (le jeu élargit sa recherche après 30 s), les cartes de la plus haute.
- **Bots** : `StartLiveSearch` (nouvelle fonction, à appeler au début de chaque recherche) note l'heure du serveur ;
  `StartLiveBotDuel` avec `protocol` ≥ 1 (jeu 0.1.15+) exige 55 s de recherche, sinon `SEARCH`, et chaque bot consomme la
  recherche. Un jeu 0.1.14 n'envoie pas `protocol` (0) : pas de contrôle tant que `ClientVersion.Minimum` les accepte.
  L'Elo gagné ou perdu contre un bot est divisé par deux (arrondi vers le haut, 1 au moins).
- `SearchMate` reçoit `generatorVersion` (absent chez 0.1.14 : accepté) ; un autre que celui du serveur reçoit `OUTDATED`.
- `UpdateSharedAsync` (`CloudSavePvpStore`) ne réécrit plus un objet inchangé : les tirages relus chaque seconde ne coûtent
  qu'une lecture.

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
