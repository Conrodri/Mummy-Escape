// Mummy Rush — les roues du casino de la boutique. C# pur, partagé : la roue des scarabées tourne dans le jeu (les
// scarabées sont locaux), celle des sceaux sur le serveur (qui détient les sceaux).
using System;
using System.Collections.Generic;

namespace MummyEscape.Pvp
{
    public enum PrizeKind { Nothing = 0, Currency = 1, Legendary = 2 }

    /// <summary>Une case de la roue : ce qu'elle rapporte et sa chance, en dix-millièmes.</summary>
    public sealed class WheelSegment
    {
        public PrizeKind Kind;
        /// <summary>Monnaie rendue (Currency), ou gros lot quand tous les légendaires sont déjà à soi (Legendary).</summary>
        public int Amount;
        public int Weight;
    }

    public sealed class WheelDef
    {
        public string Id;
        public int Price;
        /// <summary>Dans l'ordre de la roue (sens horaire depuis le haut). Les poids font <see cref="Casino.WeightTotal"/>.</summary>
        public WheelSegment[] Segments;
        /// <summary>Les skins exclusifs de cette roue : une case légendaire en donne un qu'on n'a pas encore.</summary>
        public string[] Legendaries;
        /// <summary>
        /// Prix d'achat direct d'un de ces légendaires, sans hasard (0 : seulement sur la roue). Calé sur ce que coûte un
        /// légendaire en moyenne à la roue : 200 tours à environ 23,5 net (la roue rend ~26,5 sur 50).
        /// </summary>
        public int DirectPrice;
    }

    [Serializable]
    public class SpinResult
    {
        /// <summary>Case où la roue s'arrête.</summary>
        public int Segment;
        public PrizeKind Kind;
        /// <summary>Monnaie gagnée (Currency, ou gros lot d'une case légendaire quand tout est déjà gagné).</summary>
        public int Amount;
        /// <summary>Le skin légendaire gagné, sinon null.</summary>
        public string Legendary;
    }

    /// <summary>Réponse de SpinSealWheel.</summary>
    [Serializable]
    public class WheelSpinResponse
    {
        public bool Ok;
        public SpinResult Result;
        /// <summary>Tous les tours, dans l'ordre (un seul, ou <see cref="Casino.MultiSpins"/>) ; <see cref="Result"/> est le meilleur.</summary>
        public List<SpinResult> Results = new List<SpinResult>();
        /// <summary>Solde de sceaux après les tours (prix payé, gains encaissés).</summary>
        public int Seals;
        public List<string> UnlockedRewards = new List<string>();
        public string Error;   // "SEALS"
    }

    public static class Casino
    {
        public const int WeightTotal = 10_000;
        /// <summary>Chance d'un skin légendaire à chaque tour : 0,5 %.</summary>
        public const int LegendaryWeight = 50;
        /// <summary>Tours d'un lancer groupé, payés <see cref="MultiSpinsPaid"/> (10 % de remise).</summary>
        public const int MultiSpins = 10, MultiSpinsPaid = 9;

        /// <summary>Prix de <paramref name="count"/> tours : 1 au prix normal, <see cref="MultiSpins"/> au prix de <see cref="MultiSpinsPaid"/>.</summary>
        public static int PriceOf(WheelDef wheel, int count) => count == MultiSpins ? wheel.Price * MultiSpinsPaid : wheel.Price * count;

        /// <summary>
        /// Plusieurs tours d'affilée : un légendaire gagné compte comme possédé pour les tours suivants (jamais deux fois le
        /// même). <paramref name="random"/> donne des tirages dans [0, 1).
        /// </summary>
        public static List<SpinResult> SpinMany(WheelDef wheel, int count, Func<double> random, ICollection<string> owned)
        {
            var have = new HashSet<string>();
            if (owned != null) foreach (var id in owned) have.Add(id);
            var results = new List<SpinResult>(count);
            for (int i = 0; i < count; i++)
            {
                var r = Spin(wheel, random(), random(), have);
                if (r.Legendary != null) have.Add(r.Legendary);
                results.Add(r);
            }
            return results;
        }

        /// <summary>Le tour le plus marquant d'une série : un légendaire, sinon le plus gros gain (celui que la roue montre).</summary>
        public static SpinResult Best(IList<SpinResult> results)
        {
            SpinResult best = null;
            foreach (var r in results)
                if (best == null || Rank(r) > Rank(best)) best = r;
            return best;
        }

        static int Rank(SpinResult r) => r.Legendary != null ? int.MaxValue : r.Amount;

        /// <summary>Monnaie rendue par une série de tours.</summary>
        public static int Winnings(IEnumerable<SpinResult> results)
        {
            int sum = 0;
            foreach (var r in results) if (r.Kind == PrizeKind.Currency) sum += r.Amount;
            return sum;
        }

        static WheelSegment[] Segments(int jackpot, int big, int good, int refund, int half, int small) => new[]
        {
            new WheelSegment { Kind = PrizeKind.Legendary, Amount = jackpot, Weight = LegendaryWeight },
            new WheelSegment { Kind = PrizeKind.Currency, Amount = small, Weight = 3_000 },
            new WheelSegment { Kind = PrizeKind.Currency, Amount = good, Weight = 600 },
            new WheelSegment { Kind = PrizeKind.Nothing, Weight = 2_200 },
            new WheelSegment { Kind = PrizeKind.Currency, Amount = big, Weight = 150 },
            new WheelSegment { Kind = PrizeKind.Currency, Amount = half, Weight = 2_500 },
            new WheelSegment { Kind = PrizeKind.Currency, Amount = refund, Weight = 1_500 },
        };

        /// <summary>Roue des scarabées : 50 scarabées le tour, environ la moitié rendue en moyenne.</summary>
        public static readonly WheelDef Scarabs = new WheelDef
        {
            Id = "scarabs", Price = 50,
            Segments = Segments(500, 250, 100, 50, 25, 10),
            Legendaries = new[] { "leg_ra", "leg_sekhmet", "leg_nut", "leg_aurora", "leg_gold" },
            DirectPrice = 5000,
        };

        /// <summary>Roue des sceaux de Maât : mêmes chances, d'autres légendaires (gardés par le serveur, d'où le préfixe pvp_).</summary>
        public static readonly WheelDef Seals = new WheelDef
        {
            Id = "seals", Price = 50,
            Segments = Segments(500, 250, 100, 50, 25, 10),
            Legendaries = new[] { "pvp_leg_seth", "pvp_leg_anubis", "pvp_leg_horus", "pvp_leg_prism", "pvp_leg_apophis" },
        };

        /// <summary>
        /// Un tour de roue. <paramref name="roll"/> et <paramref name="pick"/> sont deux tirages dans [0, 1) : le premier choisit
        /// la case, le second le légendaire parmi ceux que le joueur n'a pas (<paramref name="owned"/>).
        /// </summary>
        public static SpinResult Spin(WheelDef wheel, double roll, double pick, ICollection<string> owned)
        {
            int ticket = Math.Min(WeightTotal - 1, Math.Max(0, (int)(roll * WeightTotal)));
            int index = 0;
            for (int acc = 0; index < wheel.Segments.Length; index++)
            {
                acc += wheel.Segments[index].Weight;
                if (ticket < acc) break;
            }
            if (index >= wheel.Segments.Length) index = wheel.Segments.Length - 1;
            var seg = wheel.Segments[index];
            var result = new SpinResult { Segment = index, Kind = seg.Kind, Amount = seg.Kind == PrizeKind.Currency ? seg.Amount : 0 };
            if (seg.Kind != PrizeKind.Legendary) return result;

            var missing = new List<string>();
            foreach (var id in wheel.Legendaries) if (owned == null || !owned.Contains(id)) missing.Add(id);
            if (missing.Count == 0)
            {
                // Every legendary already won: the jackpot instead.
                result.Kind = PrizeKind.Currency;
                result.Amount = seg.Amount;
                return result;
            }
            result.Legendary = missing[Math.Min(missing.Count - 1, Math.Max(0, (int)(pick * missing.Count)))];
            return result;
        }

        /// <summary>
        /// Le serveur fait tourner la roue des sceaux <paramref name="count"/> fois (1 ou <see cref="MultiSpins"/>) : paie, tire,
        /// encaisse. Null en cas de succès, sinon l'erreur.
        /// </summary>
        public static string SpinSeals(PlayerPvpData d, int count, Func<double> random, out List<SpinResult> results)
        {
            results = null;
            if (count != 1 && count != MultiSpins) return "COUNT";
            int price = PriceOf(Seals, count);
            if (d.Seals < price) return "SEALS";
            d.Seals -= price;
            results = SpinMany(Seals, count, random, d.UnlockedRewards);
            foreach (var r in results)
            {
                if (r.Kind == PrizeKind.Currency) d.Seals += r.Amount;
                else if (r.Legendary != null && !d.UnlockedRewards.Contains(r.Legendary)) d.UnlockedRewards.Add(r.Legendary);
            }
            return null;
        }
    }
}
