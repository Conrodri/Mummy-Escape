using System;
using System.Collections.Generic;
using MummyEscape.Core;
using MummyEscape.Pvp;
using UnityEngine;
using UnityEngine.UI;

namespace MummyEscape.UI.Screens
{
    /// <summary>
    /// What the 2v2 and guild screens share: a team battle drawn as a card (score, each round with its two runners,
    /// the replay of decided rounds), starting the player's round, and the server's team error codes in words.
    /// </summary>
    public static class TeamView
    {
        /// <summary>Back to where a battle of this kind is followed.</summary>
        public static void OpenBattleHome(UIRouter router, BattleKind kind)
        {
            if (kind == BattleKind.Duo) router.Open<DuoScreen>();
            else router.Open<GuildScreen>();
        }

        /// <summary>The viewer's side is A (the server tells whose view it is).</summary>
        public static bool MineIsA(TeamBattle b) => b.A.TeamId == b.ViewerTeam;

        /// <summary>A battle: both teams and the score, its state, one line per round, and the player's round to run.</summary>
        public static void BattleCard(Transform parent, TeamBattle b, string me, UIRouter router, Action<TeamBattle> run)
        {
            bool mineA = MineIsA(b);
            var mine = mineA ? b.A : b.B;
            var theirs = mineA ? b.B : b.A;
            var (wa, wb) = TeamLogic.Score(b);
            int myWins = mineA ? wa : wb, theirWins = mineA ? wb : wa;

            var card = UIKit.Panel(parent, "Battle"); // noloc
            card.raycastTarget = false;
            var col = UIKit.Column(card.transform, 8);
            col.padding = new RectOffset(24, 24, 20, 22);

            var head = UIKit.Row(card.transform, 64, 12);
            Name(head.transform, mine?.TeamName, UIKit.Gold, TextAnchor.MiddleRight);
            var score = UIKit.Title(head.transform, b.B == null ? "–" : myWins + " – " + theirWins, 46); // noloc
            UIKit.Size(score, -1, 150, 0);
            Name(head.transform, theirs?.TeamName ?? Loc.T("Adversaire à venir"), UIKit.Turquoise, TextAnchor.MiddleLeft);

            var status = UIKit.Label(card.transform, Status(b, mineA), 24, UIKit.Dim);
            UIKit.FitText(status, 16);
            UIKit.Size(status, 36);

            for (int k = 0; k < b.Slots; k++) Round(card.transform, b, k, mineA, router);

            int slot = TeamLogic.NextSlot(b, me);
            if (slot >= 0 && run != null)
            {
                var go = UIKit.Button(card.transform, Loc.F("Courir la manche {0}", slot + 1), () => run(b), 34, ButtonStyle.Primary);
                UIKit.Size(go, 96);
            }
        }

        static void Name(Transform parent, string text, Color color, TextAnchor align)
        {
            var t = UIKit.Label(parent, text ?? "", 28, color, align, FontStyle.Bold);
            UIKit.FitText(t, 16);
            UIKit.Size(t, -1, 0, 1);
        }

        static string Status(TeamBattle b, bool mineA)
        {
            if (b.B == null)
                return b.Finished ? Loc.T("Personne n'est venu : combat annulé.") : Loc.T("En attente d'une équipe adverse…");
            if (b.Finished)
            {
                var result = mineA ? b.Result : DuelResolver.Invert(b.Result);
                int delta = mineA ? b.EloDeltaA : b.EloDeltaB;
                string verdict = result == DuelResult.Win ? "<color=#E8C35A>" + Loc.T("Victoire") + "</color>"
                               : result == DuelResult.Loss ? "<color=#D65440>" + Loc.T("Défaite") + "</color>"
                               : Loc.T("Égalité");
                return verdict + "  ·  Elo " + (delta > 0 ? "+" : "") + delta; // noloc
            }
            long left = b.DeadlineUnixMs - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            int minutes = (int)Math.Max(0, left / 60_000);
            string time = Loc.F("Fin dans {0} h {1:00}", minutes / 60, minutes % 60);
            string awaited = TeamLogic.Awaited(b, mineA);
            if (awaited == null) return time + "  ·  " + Loc.T("Ton équipe a tout couru");
            var side = mineA ? b.A : b.B;
            return time + "  ·  " + Loc.F("Au tour de {0}", side.Names[side.Order.IndexOf(awaited)]);
        }

        /// <summary>One round: number, our runner, the verdict, theirs, and its replay once decided.</summary>
        static void Round(Transform parent, TeamBattle b, int k, bool mineA, UIRouter router)
        {
            var mine = mineA ? b.A : b.B;
            var theirs = mineA ? b.B : b.A;
            var row = UIKit.Row(parent, 76, 10);
            var n = UIKit.Label(row.transform, "M" + (k + 1), 26, UIKit.Dim, TextAnchor.MiddleLeft, FontStyle.Bold); // noloc
            UIKit.Size(n, -1, 56, 0);

            Runner(row.transform, mine.Names[k], mine.Runs[k], TextAnchor.MiddleRight);
            int r = b.SlotResults[k];
            var result = r < 0 ? (DuelResult?)null : mineA ? (DuelResult)r : DuelResolver.Invert((DuelResult)r);
            var badge = UIKit.Image(row.transform, UISprites.Circle, result == null ? new Color(1, 1, 1, 0.12f)
                                    : result == DuelResult.Win ? UIKit.Gold : result == DuelResult.Draw ? UIKit.Sand : UIKit.Danger, false, "Result"); // noloc
            UIKit.Size(badge, 56, 56, 0);
            var letter = UIKit.Title(badge.transform, result == null ? "" : result == DuelResult.Win ? Loc.T("V") : result == DuelResult.Draw ? Loc.T("N") : Loc.T("D"), 30, UIKit.Ink);
            UIKit.Stretch(letter.rectTransform);
            Runner(row.transform, theirs != null && k < theirs.Names.Count ? theirs.Names[k] : "—", theirs?.Runs[k], TextAnchor.MiddleLeft);

            bool watchable = result != null && mine.Runs[k] != null && theirs?.Runs[k] != null && !TeamLogic.IsHidden(theirs.Runs[k])
                             && ReplayScreen.Playable(b.GeneratorVersion);
            var watch = UIKit.IconButton(row.transform, UISprites.Play, () => Watch(router, b, k, mineA), 64);
            watch.interactable = watchable;
        }

        static void Runner(Transform parent, string name, SlotRun run, TextAnchor align)
        {
            var t = UIKit.Label(parent, "<b>" + (name ?? "") + "</b>\n<size=20><color=#9C8B70>" + RunText(run) + "</color></size>", 26, UIKit.Sand, align); // noloc
            UIKit.FitText(t, 16);
            UIKit.Size(t, -1, 0, 1);
        }

        public static string RunText(SlotRun r)
        {
            if (r == null) return Loc.T("à courir");
            if (TeamLogic.IsHidden(r)) return Loc.T("a couru (caché)");
            switch (r.Outcome)
            {
                case RunOutcome.Finished: return Loc.F("sorti en {0}", LevelResult.FormatTime(r.TimeMs));
                case RunOutcome.Died: return Loc.F("mort à {0} %", Mathf.RoundToInt(r.Progress * 100f));
                case RunOutcome.TimedOut: return Loc.F("temps écoulé, {0} %", Mathf.RoundToInt(r.Progress * 100f));
                default: return Loc.T("abandon");
            }
        }

        /// <summary>A decided round, watched as a spectator: both runs side by side, traps and points of interest hidden.</summary>
        static void Watch(UIRouter router, TeamBattle b, int k, bool mineA)
        {
            var mine = mineA ? b.A : b.B;
            var theirs = mineA ? b.B : b.A;
            int r = b.SlotResults[k];
            var result = mineA ? (DuelResult)r : DuelResolver.Invert((DuelResult)r);
            var record = new DuelRecord
            {
                MatchId = b.Id + "_" + k, Seed = b.Seeds[k], GeneratorVersion = b.GeneratorVersion, Resolved = true, Result = result,
                Me = Run(mine.Runs[k], mine.Elo), Rival = Run(theirs.Runs[k], theirs.Elo),
            };
            string verdict = Loc.F("Manche {0}", k + 1) + "  ·  " + Loc.T(result == DuelResult.Win ? "Victoire" : result == DuelResult.Draw ? "Match nul" : "Défaite");
            router.Open<ReplayScreen>().ShowRound(record, mine.TeamName, theirs.TeamName, verdict);
        }

        static DuelRun Run(SlotRun r, int elo) => new DuelRun
        {
            PlayerId = r.PlayerId, PlayerName = r.PlayerName, Look = r.Look, Elo = elo, Outcome = r.Outcome,
            TimeMs = r.TimeMs, Progress = r.Progress, Inputs = r.Inputs ?? new List<RunInput>(),
        };

        /// <summary>Starts the player's round: the VS screen, then the run; the result screen sends it like a duel.</summary>
        public static async void RunRound(UIRouter router, TeamBattle b, Action<string> onError)
        {
            var app = App.GameApp.I;
            if (app.Pvp == null) return;
            var start = await app.Pvp.StartBattleRunAsync(b.Id);
            if (start == null || start.Error != null)
            {
                onError?.Invoke(ErrorText(start?.Error));
                return;
            }
            PvpScreen.Versus(router, start);
        }

        /// <summary>The server's team error codes, in words.</summary>
        public static string ErrorText(string code)
        {
            switch (code)
            {
                case "LIMIT": return Loc.T("Limite atteinte."); // noloc
                case "SELF": return Loc.T("Choisis un ami."); // noloc
                case "EXISTS": return Loc.T("C'est déjà fait."); // noloc
                case "BUSY": return Loc.T("Un combat est déjà en cours."); // noloc
                case "RIGHTS": return Loc.T("Réservé au chef et aux officiers."); // noloc
                case "ORDER": return Loc.T("Ordre de passage incomplet."); // noloc
                case "NAME": return Loc.F("Nom de guilde : {0} à {1} caractères.", TeamConfig.GuildNameMin, TeamConfig.GuildNameMax); // noloc
                case "TAG": return Loc.F("Tag : {0} à {1} lettres ou chiffres.", TeamConfig.GuildTagMin, TeamConfig.GuildTagMax); // noloc
                case "TAKEN": return Loc.T("Ce nom ou ce tag est déjà pris."); // noloc
                case "IN_GUILD": return Loc.T("Tu es déjà dans une guilde."); // noloc
                case "NO_GUILD": return Loc.T("Tu n'es dans aucune guilde."); // noloc
                case "FULL": return Loc.F("Guilde complète ({0} membres).", TeamConfig.GuildMaxMembers); // noloc
                case "CLOSED": return Loc.T("Cette guilde est fermée : on n'y entre que sur invitation."); // noloc
                case "REQUESTED": return Loc.T("Demande envoyée : le chef ou un officier te répondra."); // noloc
                case "LEADER": return Loc.T("Réservé au chef de la guilde."); // noloc
                case "MEMBER": return Loc.T("Ce joueur est déjà dans ta guilde."); // noloc
                case "THEIR_GUILD": return Loc.T("Ce joueur est déjà dans une autre guilde."); // noloc
                case "NOT_YOUR_TURN": return Loc.T("Ce n'est pas encore ton tour."); // noloc
                case "UNKNOWN": return Loc.T("Introuvable : il a peut-être disparu."); // noloc
                default: return PvpScreen.ErrorText(code);
            }
        }
    }
}
