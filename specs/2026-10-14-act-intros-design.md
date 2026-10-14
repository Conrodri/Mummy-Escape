# Intros d'acte en mode Facile — design

Date : 2026-10-14. Statut : validé en discussion, à relire.

## Problème

Retour joueur : le tutoriel (un couloir de 106 cases, 19 leçons, `Core/Generation/Tutorial.cs`) est trop long et
arrive sans contexte : on y apprend le jet de flammes de l'acte 5 avant d'avoir vu l'acte 1.

## Décision

Le gros tutoriel disparaît. En mode **Facile seulement**, chaque acte s'ouvre sur un **niveau d'intro** court, fait
main, qui montre uniquement les éléments nouveaux de cet acte. Le générateur, `DifficultyTable` et `Rules` ne changent
pas : pas de nouvelle `GeneratorVersion`, pas de ré-export des leaderboards.

## Contenu des intros

Ids `LevelId(Easy, act, 0)` (affiché « 1-0 »…). Couloirs d'un seul étage, comme le Facile.

| Intro | Leçons |
|---|---|
| 1-0 Antichambre | déplacement, torche et mémoire de la carte, porte + dalle, piques + Désamorcer, portail, sable maudit, sortie |
| 2-0 Galeries inondées | poussière + torche murale, courant |
| 3-0 Ruines effondrées | portail scellé + levier, dalle friable, miroir de Seth |
| 4-0 Cité d'Anubis | barrière + interrupteur, portail maudit, dalle tournante |
| 5-0 Sanctuaire embrasé | jet de flammes, rappel de la dalle friable |

Retirés car absents du Facile : échelle, sol fragile, portail caché. Les textes de leçons existants sont réutilisés
quand ils conviennent (mêmes clés `Loc`), les nouveaux vont dans `Loc.En.cs`.

## Règles de progression

- F{a}-1 n'est débloqué qu'une fois l'intro F{a}-0 **réussie ou passée** (en plus de la règle actuelle : F{a-1}-10
  terminé pour a > 1).
- Intro gratuite : pas d'énergie, pas d'étoiles, pas de pièces, pas de leaderboard, pas de course enregistrée.
- Bouton **Passer** dans le menu pause de l'intro : marque l'intro faite et ouvre F{a}-1.
- Rejouable depuis la sélection de niveaux (case « intro » avant X-1 en Facile).
- Normal et Extrême : aucun changement.

## Sauvegarde et migration

- `SaveData.TutorialDone` (bool) est remplacé par `IntrosDone` (masque de bits par acte), fusionné par OU comme
  aujourd'hui.
- Migration au chargement : les intros des actes dont F{a}-1 est déjà débloqué ou terminé comptent comme faites ;
  `TutorialDone == true` marque l'intro 1-0 faite. Aucun joueur existant ne se retrouve bloqué.

## Code touché

- **Core** : `Tutorial.cs` devient `ActIntro` (`Build(act)`, `LessonAt`, `Id(act)`). `Progression.IsUnlocked`
  reçoit la notion d'intro (prédécesseur de F{a}-1 = F{a}-0 en Facile).
- **Runtime** :
  - `GameController` : `StartTutorial()` → `StartIntro(act)`, `InTutorial` → `InIntro` (+ acte), `MarkIntroDone`.
  - `HudScreen` : bandeau de leçon inchangé, titre « Intro — acte N ».
  - `TutorialEndScreen` : bouton « Jouer le tombeau X-1 ».
  - `MainMenuScreen` : JOUER lance l'intro de l'acte courant si elle n'est pas faite (remplace `TutorialPending`).
  - `LevelSelectScreen` : case intro avant X-1 en Facile, l'icône main actuelle disparaît.
  - `SettingsScreen` : « Rejouer le tutoriel » supprimé.
  - `PauseScreen` : recommencer sans énergie aussi pour les intros ; ajout de Passer.
  - `SaveService` : champ + migration ci-dessus.

## Tests

- Chaque intro est soluble (`Solver`) et finit non tournée.
- Chaque case marchable du couloir principal a une leçon.
- Chaque intro n'utilise que des éléments disponibles dans son acte en Facile.
- Progression : F{a}-1 verrouillé sans intro, ouvert après ; migration d'une sauvegarde `TutorialDone` existante.
- `mummy_loc_check` propre.

## Hors périmètre (livraison suivante, nouvelle `GeneratorVersion`)

Nouveaux éléments retenus le 2026-10-14, à concevoir en détail après les intros. Chacun aura sa leçon dans l'intro de
son acte :

- **Bandelettes (acte 2)** : bloquent le passage ; entrer dessus torche allumée les brûle (un tour). Torche éteinte par
  la poussière : il faut d'abord la rallumer à une torche murale.
- **Faux mur (mirage)** : mur traversable qui miroite quand la torche l'éclaire ; raccourci seulement, jamais requis
  en Facile.
- **Regard d'idole (acte 4)** : une idole dans un mur balaie sa ligne jusqu'au prochain mur un tour sur 3 (−1 PV si on
  s'y trouve) ; yeux qui brillent le tour d'avant ; rayon visible même dans le noir. Le solveur gère déjà
  `Tick mod 3` (jets de flammes).
