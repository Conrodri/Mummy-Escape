# Réponses aux déclarations de la Play Console

Play Console › Règles et programmes › **Contenu de l'application**. Réponses cohérentes avec le code (version 0.1.0) et la politique de confidentialité. À revoir si on ajoute pubs, analytics ou achats.

## Politique de confidentialité
URL : `https://conrodri.github.io/Mummy-Escape/confidentialite.html`

## Annonces
**Non**, l'application ne contient pas d'annonces.

## Accès à l'application
**Toutes les fonctionnalités sont disponibles sans restrictions d'accès.** Le compte est facultatif et le jeu est entièrement jouable hors ligne : aucun identifiant de test n'est nécessaire.

## Classification du contenu (questionnaire IARC)
- Catégorie : **Jeu**.
- Violence : **pas de violence réaliste, pas de sang**. La momie (personnage fantastique en pixel art) peut perdre la partie sur des pièges (pics, flammes) : violence fantastique très légère, sans représentation de blessure.
- Peur : **non** (ambiance sombre, pas de contenu effrayant).
- Sexualité, langage grossier, drogues, alcool, tabac, jeux d'argent : **non**.
- Interactions entre utilisateurs : **oui, limitées**. Pseudonymes publics dans les classements et ajout d'amis par code, **sans chat ni messagerie**. Les pseudonymes sont choisis par les joueurs.
- Partage de la position : **non**. Achats numériques : **non**.
- Résultat attendu : **PEGI 3 ou 7 / ESRB Everyone**.

## Public cible et contenu
- Tranche d'âge : **13 ans et plus** (13-15, 16-17, 18+). Ne pas cocher les tranches de moins de 13 ans : elles imposent le programme Familles (SDK certifiés, politique enfants, etc.).
- L'application peut-elle attirer involontairement les enfants ? **Non** (pas de personnages enfantins ni de marketing visant les enfants). Si Google estime le contraire, il faudra cibler aussi les moins de 13 ans et appliquer les exigences Familles.

## Applications d'actualités / santé / finances / gouvernement
**Non** pour toutes.

## Identifiant publicitaire
**Non**, l'application n'utilise pas l'identifiant publicitaire. Le manifeste fusionné ne contient pas la permission `com.google.android.gms.permission.AD_ID` (vérifié sur le build 0.1.0).

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

« Partagées » = **non** : Unity agit comme sous-traitant pour notre compte, ce que Google n'assimile pas à un partage.
Pas de localisation, de contacts, de photos, d'e-mail, de paiement ni de santé.

## Fonctionnalités financières, VPN, accès à tous les fichiers, alarmes exactes
**Non concerné.**

## Statut de professionnel (règlement européen DSA)
Play Console › Paramètres › Compte de développeur :
- **non-professionnel** si tu publies le jeu à titre personnel, gratuitement et sans le monétiser (aucune adresse publiée) ;
- **professionnel** si c'est une activité commerciale (société, auto-entreprise, monétisation future) : nom, adresse, e-mail et téléphone deviennent publics sur la fiche.
