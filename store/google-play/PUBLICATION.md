# Publier Mummy Escape sur Google Play

Ce qui est **déjà prêt** dans le dépôt :
- **AAB signé :** menu *Mummy Escape › Build › Google Play (AAB signé)*, ou `unity command mummy_build_play`. Il produit `MummyEscape/Builds/Android/MummyEscape.aab` (IL2CPP, ARM64, API cible 36, min 25).
- **Clé d'envoi :** `~/.mummyescape/` (hors dépôt), avec le certificat `upload_certificate.pem`.
- **Code de version :** calculé depuis la version (`0.1.0` → `10001`). Monter la version dans *Project Settings › Player › Version* avant chaque envoi.
- **Fiche :** textes dans [`fiche-fr-FR.md`](fiche-fr-FR.md), visuels dans [`graphics/`](graphics/).
- **Questionnaires :** réponses dans [`declarations.md`](declarations.md).
- **Pages web publiques** (dossier `docs/`, GitHub Pages) :
  - accueil : https://conrodri.github.io/Mummy-Escape/
  - politique de confidentialité : https://conrodri.github.io/Mummy-Escape/confidentialite.html
  - conditions : https://conrodri.github.io/Mummy-Escape/conditions.html
  - suppression du compte : https://conrodri.github.io/Mummy-Escape/suppression-compte.html

## 0. Avant tout (une fois)
1. **Sauvegarder `~/.mummyescape/`** (gestionnaire de mots de passe, clé USB…). Sans cette clé, impossible d'envoyer une mise à jour sans passer par une réinitialisation auprès de Google.
2. **Remplir `LegalTexts.cs`** : éditeur, adresse, e-mail de contact, médiateur. Ensuite :
   - relancer *Mummy Escape › Legal › Export…* ;
   - committer et pousser (les pages se mettent à jour toutes seules) ;
   - reconstruire l'AAB.
3. **Unity Gaming Services :**
   - lier le projet (*Edit › Project Settings › Services*) ;
   - activer Authentication (anonyme + Username & Password), Leaderboards, Friends et Cloud Save ;
   - déployer les leaderboards (voir le README).
   Sans cela, le mode en ligne affiche « hors ligne », mais le jeu reste jouable.

## 1. Compte Google Play Console
- https://play.google.com/console : 25 $ (une fois), vérification d'identité (pièce d'identité) et d'un téléphone Android.
- Choisir un compte **personnel** ou **organisation**. Une organisation demande un numéro D-U-N-S, mais n'est pas soumise au test fermé obligatoire.

## 2. Créer l'application
*Créer une application* :
- nom « Mummy Escape », langue par défaut **Français (France) – fr-FR** ;
- type **Jeu**, **Gratuit** (un jeu gratuit ne peut plus devenir payant) ;
- accepter les déclarations.

## 3. Configurer l'application (tableau de bord › « Configurer votre application »)
1. Remplir tout **Contenu de l'application** avec [`declarations.md`](declarations.md).
2. **Fiche principale** avec [`fiche-fr-FR.md`](fiche-fr-FR.md) et `graphics/`.
3. **Paramètres de l'appli :**
   - catégorie Réflexion ;
   - e-mail de contact ;
   - pays : tous, ou au moins l'UE.

## 4. Test interne (immédiat, jusqu'à 100 testeurs)
1. *Tests › Test interne › Créer une release*.
2. Accepter **Play App Signing** (recommandé : Google conserve la clé de signature finale).
3. Importer `MummyEscape.aab` et rédiger des notes de version (« Première version »).
4. Ajouter ta propre adresse Gmail comme testeur, installer via le lien d'inscription et vérifier sur un vrai téléphone :
   - écran d'accueil ;
   - parties ;
   - création de compte ;
   - export et suppression des données.
5. Relire le **rapport de pré-lancement** (tests automatiques de Google sur de vrais appareils).

## 5. Test fermé (obligatoire pour un compte personnel créé après nov. 2023)
- *Tests › Test fermé* : créer une piste, importer le même AAB (ou un AAB avec une version plus récente).
- **Au moins 12 testeurs inscrits pendant 14 jours consécutifs** (liste d'adresses Gmail ou groupe Google). Ils doivent accepter l'invitation et garder l'appli installée.
- Recueillir les retours et corriger ; chaque nouvel AAB doit avoir une version plus élevée.

## 6. Accès à la production
- Après les 14 jours : tableau de bord › *Demander l'accès à la production*, puis répondre au questionnaire sur le test.
- Une fois l'accès accordé : *Production › Créer une release*, importer l'AAB et choisir les pays.
- Examen par Google : de quelques heures à environ 7 jours la première fois.

## 7. Après la publication
- Le lien `https://play.google.com/store/apps/details?id=com.mummyescape.game` est déjà utilisé par le bouton « Partager » du jeu.
- Surveiller *Qualité › Android vitals* (plantages, ANR).
- Chaque mise à jour se fait en quatre étapes :
  1. monter la version ;
  2. lancer *Build › Google Play (AAB signé)* ;
  3. créer une nouvelle release ;
  4. tenir l'**API cible** à jour : Google relève l'exigence chaque année vers août. La constante `TargetApi` est dans `BuildScript.cs`.

## Ce qui reste à améliorer (pas bloquant pour Google Play)
- Graphismes et sons placeholders : à remplacer par de vrais assets pour une meilleure note et de meilleures conversions.
- Jeu uniquement en français : ajouter une traduction anglaise (jeu + fiche) pour toucher plus de pays.
