# Réponses aux déclarations de la Play Console

Play Console › Règles et programmes › **Contenu de l'application**. Réponses cohérentes avec le code (version 0.1.6 : pubs récompensées AdMob, achats intégrés Google Play, tchat, duels en ligne) et la politique de confidentialité. À revoir à chaque changement des données collectées.

## Politique de confidentialité
URL : `https://conrodri.github.io/Mummy-Escape/confidentialite.html`

## Annonces
**Oui** : publicités récompensées Google AdMob, affichées seulement quand le joueur choisit d'en regarder une pour récupérer de l'énergie.

## Accès à l'application
**Toutes les fonctionnalités sont disponibles sans restrictions d'accès.** Le compte est facultatif et le jeu est entièrement jouable hors ligne : aucun identifiant de test n'est nécessaire.

## Classification du contenu (questionnaire IARC)
- Catégorie : **Jeu**.
- Violence : **pas de violence réaliste, pas de sang**. La momie (personnage fantastique en pixel art) peut perdre la partie sur des pièges (pics, flammes) : violence fantastique très légère, sans représentation de blessure.
- Peur : **non** (ambiance sombre, pas de contenu effrayant).
- Sexualité, langage grossier, drogues, alcool, tabac, jeux d'argent : **non**.
- Interactions entre utilisateurs : **oui**. Pseudonymes publics (classements, duels), ajout d'amis par code et **tchat** (global, guilde, messages privés entre amis) avec filtre de mots, blocage et signalement. Canal global fermé aux mineurs.
- Partage de la position : **non**. Achats numériques : **oui** (scarabées dorés, pass).
- Objets aléatoires achetables : **oui** (roue des scarabées : les tours se paient en scarabées, monnaie qui s'obtient aussi avec de l'argent réel). Pas de mise ni de gain d'argent réel. La roue est désactivée en Belgique.
- Résultat attendu : **PEGI 3 ou 7 / ESRB Everyone**.

## Public cible et contenu
- Tranche d'âge : **13 ans et plus** (13-15, 16-17, 18+). Ne pas cocher les tranches de moins de 13 ans : elles imposent le programme Familles (SDK certifiés, politique enfants, etc.).
- L'application peut-elle attirer involontairement les enfants ? **Non** (pas de personnages enfantins ni de marketing visant les enfants). Si Google estime le contraire, il faudra cibler aussi les moins de 13 ans et appliquer les exigences Familles.

## Applications d'actualités / santé / finances / gouvernement
**Non** pour toutes.

## Identifiant publicitaire
**Oui** : le SDK Google Mobile Ads (AdMob) utilise l'identifiant publicitaire pour les publicités récompensées. La permission `com.google.android.gms.permission.AD_ID` est présente dans le manifeste fusionné.

## Sécurité des données
### Collecte et partage
- L'application collecte-t-elle ou partage-t-elle des données utilisateur ? **Oui** (seulement si le joueur active le mode en ligne).
- Toutes les données sont-elles chiffrées en transit ? **Oui** (HTTPS, Unity Gaming Services).
- Les utilisateurs peuvent-ils demander la suppression de leurs données ? **Oui** : dans l'application (Paramètres › Confidentialité) et sur le web.
- URL de suppression du compte : `https://conrodri.github.io/Mummy-Escape/suppression-compte.html`
- Création de compte : **Nom d'utilisateur et mot de passe**. Le compte se supprime dans l'application.

### Types de données (toutes : *collectées*, *non partagées*, traitement *facultatif* : le joueur peut refuser le mode en ligne)
| Catégorie Play | Donnée | Finalités | Éphémère ? |
|---|---|---|---|
| Informations personnelles › **Nom** | Pseudonyme public | Fonctionnement de l'appli, Gestion du compte | Non |
| Informations personnelles › **ID utilisateur** | Identifiant de joueur Unity, identifiant de connexion | Fonctionnement de l'appli, Gestion du compte, Prévention des fraudes et sécurité | Non |
| Activité dans l'appli › **Autre contenu généré par l'utilisateur** | Scores, progression, sauvegarde en ligne | Fonctionnement de l'appli | Non |
| Activité dans l'appli › **Interactions avec l'appli** | Liste d'amis, statut en ligne | Fonctionnement de l'appli | Non |
| Infos sur l'appli et performances › **Journaux de plantage / Diagnostics** | Journaux techniques du prestataire | Fonctionnement de l'appli, Sécurité | Non |
| Appareil ou autres identifiants | Identifiant de session Unity | Fonctionnement de l'appli, Sécurité | Non |
| Achats › **Historique des achats** | Produit, identifiant de commande, jeton d'achat Google Play, date | Fonctionnement de l'appli, Prévention des fraudes et sécurité | Non |
| Messages › **Autres messages intégrés à l'appli** | Messages du tchat (100 derniers par canal), blocages, signalements | Fonctionnement de l'appli, Sécurité (modération) | Non |

Avec le consentement séparé « Statistiques d'usage anonymes » (Unity Analytics, sous-traitant, *collectées*, *non partagées*, *facultatif*) :

| Catégorie Play | Donnée | Finalités | Éphémère ? |
|---|---|---|---|
| Activité dans l'appli › **Interactions avec l'appli** | Tombeaux joués / gagnés / abandonnés, pubs regardées, achats, fin de duel | Analyse | Non |
| Infos sur l'appli et performances › **Diagnostics** | Modèle d'appareil, système, version du jeu | Analyse | Non |

Notifications : locales seulement (énergie pleine, ouverture d'Halloween), programmées sur l'appareil, aucune donnée envoyée. Permission `POST_NOTIFICATIONS` demandée après les premières étoiles.

Exceptions, **collectées et partagées** (SDK AdMob, seulement quand le joueur regarde une pub) :

| Catégorie Play | Donnée | Finalités | Facultatif ? |
|---|---|---|---|
| Appareil ou autres identifiants | Identifiant publicitaire | Publicité ou marketing, Analyse, Prévention des fraudes | Oui |
| Infos sur l'appli et performances › Diagnostics | Informations techniques sur l'appareil | Publicité ou marketing, Prévention des fraudes | Oui |
| Position › Position approximative | Déduite de l'adresse IP par AdMob | Publicité ou marketing, Prévention des fraudes | Oui |

« Partagées » = **non** : Unity agit comme sous-traitant pour notre compte, ce que Google n'assimile pas à un partage.
Pas de position précise, de contacts, de photos, d'e-mail, de coordonnées bancaires (le paiement est traité par Google) ni de santé. Les données de l'acheteur restent chez Google Play.

## Fonctionnalités financières, VPN, accès à tous les fichiers, alarmes exactes
**Non concerné.**

## Statut de professionnel (règlement européen DSA)
Play Console › Paramètres › Compte de développeur :
- **non-professionnel** si tu publies le jeu à titre personnel, gratuitement et sans le monétiser (aucune adresse publiée) ;
- **professionnel** si c'est une activité commerciale (société, auto-entreprise, monétisation future) : nom, adresse, e-mail et téléphone deviennent publics sur la fiche.
