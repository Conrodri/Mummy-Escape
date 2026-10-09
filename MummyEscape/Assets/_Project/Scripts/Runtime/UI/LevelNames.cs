using MummyEscape.Core;

namespace MummyEscape.UI
{
    /// <summary>How a level is named to the player: its mode, then act-level ("Facile 1-3").</summary>
    public static class LevelNames
    {
        public static string Mode(Difficulty mode) => Loc.T(DifficultyTable.GetMode(mode).Name);

        public static string Of(LevelId id) => Mode(id.Mode) + " " + id.Short;
    }
}
