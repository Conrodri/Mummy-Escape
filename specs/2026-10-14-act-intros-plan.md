# Intros d'acte — plan d'implémentation

> For agentic workers: two tasks run in parallel (Core / Runtime); the interface below is the contract between them.
> Do NOT commit; the coordinator commits after review.

**Goal:** replace the long tutorial with one short hand-made intro level per act in Easy mode.
**Spec:** `specs/2026-10-14-act-intros-design.md` (read it first).
**Tech:** Unity 6, C# 9 for Core (no `UnityEngine`), NUnit tests run by `dotnet test tools/CoreTests`.

## Global constraints

- C# 9 in Core and tests. Core never references UnityEngine.
- Do not touch `LevelGenerator`, `DifficultyTable`, `Rules`: no `GeneratorVersion` bump.
- French text is the Loc key; English goes in `Runtime/Localization/Loc.En.cs`. UIKit labels are translated automatically, dynamic text goes through `Loc.T/F`.
- Comments in English, match surrounding style; commit messages are the coordinator's job.
- Never run `unity command recompile` (the user may be in Play mode); the coordinator checks the editor.

## Design choice (refines the spec, same player-facing behaviour)

`Progression.IsUnlocked` is NOT changed. Instead, starting F{a}-1 while its intro is needed plays the intro first:
`SaveService.NeedsIntro(id)` = Easy mode, index 1, intro bit of that act not set, and F{a}-1 never completed.
The "never completed" clause is the migration: existing players who already escaped F{a}-1 are never forced into an
intro, so no save migration is needed. `TutorialDone` is deleted (JsonUtility drops the unknown field).

## Contract (Core API, produced by Task A, consumed by Task B)

```csharp
namespace MummyEscape.Core
{
    public sealed class IntroLesson   // renamed TutorialLesson, same fields
    {
        public int Floor; public int FromX; public int ToX; public string Text;
        public bool Contains(Cell c);
    }

    public static class ActIntro
    {
        public static LevelId Id(int act);                                // new LevelId(Difficulty.Easy, act, 0)
        public static IReadOnlyList<IntroLesson> Lessons(int act);        // act 1..DifficultyTable.ActCount
        public static IntroLesson LessonAt(int act, Cell c);              // null between two stretches
        public static Level Build(int act);                               // Level.Id == Id(act)
    }
}
```

`Core/Generation/Tutorial.cs` is deleted (its `.meta` too; create `ActIntro.cs` without a .meta, Unity makes one).

---

### Task A — Core: `ActIntro` + tests

**Files:** delete `Scripts/Core/Generation/Tutorial.cs(.meta)`, create `Scripts/Core/Generation/ActIntro.cs`;
delete `Tests/EditMode/TutorialTests.cs(.meta)`, create `Tests/EditMode/ActIntroTests.cs`.
Check `tools/CoreTests/*.csproj` includes files by glob (no explicit Tutorial path); fix it if not.

Build each act as a single-floor corridor (`new Level(width, 5, 1)`, row 2, start x=1, all else Wall), reusing the
`Set/Corridor/Portal/Trap` helpers of the old `Tutorial.Build()`. MaxHp 3. Keep the stretches short (about 4-6 cells
per lesson). Side tiles (button, lever, switch, wall torch) go at row 3 next to what they open, as in the old file.
Lessons, in order (reuse these exact French strings from the old file where listed; the exit text is new):

| Act | Stretches |
|---|---|
| 1 | move/torch, door + plate, spikes + Désamorcer, cursed sand, visible portal, exit A |
| 2 | dust (2 tiles) + wall torch, current (3 tiles), exit B |
| 3 | locked portal + lever, crumbling slab, mirror of Seth, exit B |
| 4 | red barrier + switch, cursed portal, rotate slab, second rotate slab ("Une autre dalle le remet droit."), exit B |
| 5 | flame jet, crumbling slab (reuse its text), exit B |

- exit A = the old last lesson text ("La sortie ! Avant chaque vrai tombeau, mémorise\nsa carte : ensuite, tout est dans le noir.").
- exit B = new: `"La sortie ! Les tombeaux de cet acte\nt'attendent, mémorise bien leur carte."`
- Act 4 rotate slabs: same params as the old file (1 then 3) so the tomb ends straight.
- Act 5 flame jet: keep the current turn-based FireJet (Param = phase).
- Set `ChannelCount` / `TrapCount` to what the act uses (TrapIndex unique per trap, from 0).

**Tests (`ActIntroTests`, one `[TestCase]` per act 1..5):**
1. `Solver.Solve(ActIntro.Build(act))` is not null; replaying it through `GameSession.Apply` never gets `Blocked`, ends `Won` with Hp > 0, final `State.Rotation == 0`.
2. The solution meets the act's flags: act 1 `ButtonPressed, Blinded, Teleported`; act 2 `TorchSmothered, TorchRelit, Swept`; act 3 `Teleported, Collapsed, Reversed`; act 4 `Switched, FogReset, Rotated` (exactly 2 rotations); act 5 `Collapsed` and the level holds a `TileType.FireJet`.
3. Every walkable cell of row 2 has `ActIntro.LessonAt(act, c)`.
4. Easy-only content: the level has a single floor, no `LadderUp/LadderDown/BreakableFloor`, no `TeleporterKind.Hidden`.
5. `ActIntro.Build(act).Id.Equals(ActIntro.Id(act))` and `Lessons(act)` is not empty.

Run: `dotnet test tools/CoreTests --filter "TestCategory!=Slow"` from the repo root; all green.

---

### Task B — Runtime + localisation

**Files:** `Runtime/Services/SaveService.cs`, `Runtime/Game/GameController.cs`, `Runtime/UI/PlayGate.cs` (file holding
`PlayGate`), `Runtime/UI/Screens/{HudScreen,MainMenuScreen,LevelSelectScreen,SettingsScreen,PauseScreen,TutorialEndScreen}.cs`,
`Runtime/Localization/Loc.En.cs`. Rename `TutorialEndScreen` → `IntroEndScreen` (file + class; delete old .meta, no new .meta).

1. **SaveService**: replace `public bool TutorialDone;` with
   `/// <summary>Act intros (Easy) walked or skipped, bit (act - 1).</summary> public int IntrosDone;`; merge with
   `Data.IntrosDone |= other.IntrosDone;`. Add
   `public bool NeedsIntro(LevelId id) => id.Mode == Difficulty.Easy && id.Index == 1 && (Data.IntrosDone & (1 << (id.Act - 1))) == 0 && !IsCompleted(id);`
   and `public void MarkIntroDone(int act)` (sets the bit, saves only if it changed).
2. **GameController**: `InTutorial` → `InIntro` (bool) + `public int IntroAct { get; private set; }`;
   `TutorialEnded` → `IntroEnded`; `StartTutorial()` → `StartIntro(int act)` using `ActIntro.Build(act)` /
   `ActIntro.Id(act)`; `MarkTutorialDone` → `_app.Save.MarkIntroDone(IntroAct)`. `Restart` → `StartIntro(IntroAct)`.
   At the top of `StartLevel(LevelId id)`: `if (_app.Save.NeedsIntro(id)) { StartIntro(id.Act); return; }`.
   `ResetForLoad` / `Abandon` reset `InIntro`. Keep: no preview, no RunRecorder, abandon marks it done.
3. **PlayGate.Solo**: if `app.Save.NeedsIntro(level)` call `start()` without spending energy (StartLevel turns it into the intro).
4. **HudScreen**: lesson from `ActIntro.LessonAt(App.Game.IntroAct, s.Position)`; both titles
   `Loc.F("Intro · acte {0}", App.Game.IntroAct)`; hint hidden in intro; `IntroEnded` opens `IntroEndScreen`.
5. **IntroEndScreen**: `Show(bool won)`; won title `"Intro terminée !"`, text
   `"Tu connais les pièges de cet acte.\nÀ toi de t'évader pour de vrai !"`, button `Loc.F("Jouer le tombeau {0}-1", act)`
   starting `new LevelId(Difficulty.Easy, act, 1)` through `PlayGate.Solo` as today; lost: keep `"La momie est tombée…"`,
   text `"Pas de panique : l'intro ne coûte rien.\nRecommence et lis bien chaque panneau."`, button "Réessayer".
6. **PauseScreen**: restart in intro costs nothing (as today). Add a ghost button `"Passer l'intro"` shown only in intro:
   mark done, close, then do exactly what the won end-screen button does (shared static helper in IntroEndScreen, e.g.
   `IntroEndScreen.PlayAct(GameApp app, UIRouter router, int act)`).
7. **MainMenuScreen**: replace `TutorialPending` with
   `IntroPending(save) => save.NeedsIntro(save.FurthestUnlocked())`; JOUER and the goal line start
   `ActIntro`'s act of that level via `App.Game.StartIntro(...)` after opening `HudScreen`; goal text
   `Loc.F("Apprends les pièges de l'acte {0} : intro", act)`. Keep the `Celebrate` guard using `IntroPending`.
8. **LevelSelectScreen**: remove the hand icon (tutorial). In Easy, give each act whose F{a}-1 is unlocked a small
   "Intro" button next to the act title (follow the screen's existing act header/button idiom) that opens HudScreen and
   calls `App.Game.StartIntro(act)`.
9. **SettingsScreen**: delete the "Rejouer le tutoriel" button and `ReplayTutorial`.
10. **Loc.En.cs**: add English for every new French key above (exit B, titles, end texts, buttons, goal line,
    "Intro · acte {0}", "Intro"); delete entries only used by removed lessons/screens ("Tutoriel", "Tutoriel terminé !",
    tutorial end texts, hidden portal, ladder, breakable floor lessons, "Rejouer le tutoriel",
    "Apprends à déjouer les pièges : tutoriel"). Grep first that a key is unused before deleting it.
11. Grep the whole `Scripts/` folder (Editor and Editor.Pipeline too) for `Tutorial` and fix every leftover.

No Unity compile available to you: re-read every edited call site for type/name consistency with the contract.

## Review focus

- Player who escaped F2-1 before the update: tapping F2-1 must start the level, not the intro (NeedsIntro clause).
- Act 2+ intro must never spend solo energy (PlayGate check) and never record a run or leaderboard score.
- Pause > Recommencer inside an intro restarts the intro, not F{a}-1.
- Cloud merge of two devices keeps the union of IntrosDone bits.
- Duel/coop/endless flows never hit NeedsIntro (only Easy index 1 campaign ids).
