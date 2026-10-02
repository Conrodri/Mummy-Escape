using System;
using System.Collections.Generic;

namespace MummyEscape.Core
{
    /// <summary>
    /// Checks a generated level against its spec. Returns null when valid, otherwise "bucket: details".
    /// This is the contract that guarantees every player gets a fair, comparable level.
    /// </summary>
    public static class LevelValidator
    {
        public static string Validate(Level level, LevelSpec spec, Solution solution)
        {
            if (solution == null) return "unsolvable: no path to the exit";
            if (solution.Moves < spec.MinMoves) return $"too-short: par {solution.Moves} < {spec.MinMoves}";
            if (solution.Moves > spec.MaxMoves) return $"too-long: par {solution.Moves} > {spec.MaxMoves}";
            if (solution.HpLeft < spec.MinHpLeftForPar) return $"too-deadly: par leaves {solution.HpLeft} hp";

            if (spec.RequiredButtons > 0)
            {
                if (solution.ButtonsPressed < spec.RequiredButtons)
                    return $"interactions: par presses {solution.ButtonsPressed} < {spec.RequiredButtons} buttons";
                var noButtons = SolverOptions.Default;
                noButtons.AllowButtons = false;
                if (Solver.Solve(level, noButtons) != null) return "interactions: exit reachable without buttons";
            }

            if (level.Start.Manhattan(level.Exit) < 3) return "too-close: exit next to start";

            var spacingError = CheckSpacing(level, spec);
            if (spacingError != null) return spacingError;
            return null;
        }

        static string CheckSpacing(Level level, LevelSpec spec)
        {
            var pois = new List<Cell>();
            foreach (var c in level.AllCells())
                if (level[c].IsPointOfInterest) pois.Add(c);

            // Doors sit in corridors and are allowed one tile of slack (see generator).
            for (int i = 0; i < pois.Count; i++)
                for (int j = i + 1; j < pois.Count; j++)
                {
                    var a = pois[i];
                    var b = pois[j];
                    if (a.Floor != b.Floor) continue;
                    int d = Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
                    int min = spec.MinPoiSpacing;
                    if (level[a].Type == TileType.Door || level[b].Type == TileType.Door) min--;
                    if (d < min) return $"spacing: {level[a].Type}{a} and {level[b].Type}{b} are {d} apart (< {min})";
                }
            return null;
        }
    }
}
