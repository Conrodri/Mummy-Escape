// Mummy Escape PvP — tous les réglages à un seul endroit.
namespace MummyEscape.Pvp
{
    public enum League { Bronze = 0, Argent = 1, Or = 2, Platine = 3, Diamant = 4 }
    // « Top 100 » n'est pas une ligue de points : c'est le rang dans le classement mondial.

    public static class PvpConfig
    {
        // --- Elo ---
        public const int StartingElo = 1000;
        public const int PlacementDuels = 10;
        public const int KPlacement = 40;
        public const int KNormal = 24;
        public const int KHigh = 16;
        public const int KHighThreshold = 1800;
        public const int MinElo = 100;

        // --- Ligues (seuil minimal de chaque ligue) ---
        public const int ArgentMin = 900;
        public const int OrMin = 1100;
        public const int PlatineMin = 1300;
        public const int DiamantMin = 1500;
        public const int Top100Size = 100;
        public const int BoardMargin = 20;              // entrées lues en plus du Top 100 (celles écartées à la vérification)
        public const int BoardCacheMinutes = 5;         // classement vérifié gardé ce temps avant d'être relu

        // --- Course ---
        public const int TickRate = 50;                 // résolution des horodatages : 1/50 s (20 ms)
        public const int TimeLimitMs = 180_000;         // 3 minutes
        public const int MinPlausibleTimeMs = 4_000;    // sortie en moins de 4 s : course rejetée (un pas animé dure 0,13 s)
        public const int MinInputGapTicks = 4;          // 80 ms au moins entre deux actions (le jeu anime chaque pas)
        public const int DrawTimeThresholdMs = 200;     // écart < 0,2 s à l'arrivée = nul
        public const float DrawProgressThreshold = 0.02f; // écart < 2 % de progression = nul
        public const int MaxInputs = 2_500;             // 3 min ÷ 80 ms

        // --- Matchmaking (duel différé contre fantôme) ---
        public const int QueueBucketSize = 100;         // fantômes rangés par tranches de 100 Elo
        public const int MaxEloGap = 300;               // on n'affronte jamais un fantôme à plus de ±300
        public const int MaxGhostsPerBucket = 50;
        public const int GhostLifetimeHours = 24;       // fantôme non utilisé : supprimé
        public const int PendingDuelLifetimeMinutes = 10; // au-delà, le duel non envoyé = abandon
        public const int MaxDuelsVsSameOpponentPerDay = 3;

        // --- Accès ---
        public const int RequiredSoloStars = 35;        // acte 3 atteint en solo

        // --- Récompenses quotidiennes (sceaux de Maât) ---
        public const int FirstWinOfDaySeals = 30;
        public const int MinCountedDuelMs = 20_000;     // un duel compte pour la participation s'il dure 20 s
        public const int MaxCountedDuelsPerDay = 15;

        public static int DailyChestSeals(League league, bool isTop100)
        {
            if (isTop100) return 100;
            switch (league)
            {
                case League.Diamant: return 80;
                case League.Platine: return 60;
                case League.Or: return 45;
                case League.Argent: return 30;
                default: return 20;
            }
        }

        // --- Saison mensuelle ---
        public const float SeasonSoftResetFactor = 0.5f; // R' = 1000 + 0,5 × (R − 1000)
        public const int LastWeekDays = 7;
    }
}
