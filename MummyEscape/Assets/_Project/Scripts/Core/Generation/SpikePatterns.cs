using System.Collections.Generic;

namespace MummyEscape.Core
{
    /// <summary>
    /// Hand-drawn shapes for spikes worth the risk: the walk crosses them, a spike-free way beside it is longer. The fast
    /// way costs a life (or a disarm), the safe way only time.
    ///
    /// Drawn in tiles (maze cells sit two tiles apart, walls between them), with the walk going left to right along the
    /// row that holds the spikes; the shape is turned to follow the walk, laid on either side of it and drawn mirrored:
    ///   <c>W</c> a tile of the walk, <c>^</c> spikes (on the walk), <c>+</c> the safe way, <c>#</c> rock that must stay
    ///   (between the two ways), <c>T</c> a wall torch lit from the safe way, <c>.</c> anything.
    ///
    /// The walk must go straight through the shape: a ring entered and left at opposite corners has two halves of the
    /// same length, and spikes on either save nothing (<see cref="LevelValidator.SpikeSaving"/> rejects it).
    ///
    /// The torch shapes come with dust on the walk 2 or 3 tiles before them: cross the spikes in the dark (a hit, no
    /// disarming without light) and carry on blind, or take the safe way past the torch and see again for the rest of
    /// the tomb.
    /// </summary>
    public static class SpikePatterns
    {
        public sealed class Pattern
        {
            public string Name;
            /// <summary>Top row first; the row holding the spikes is the walk.</summary>
            public string[] Rows;

            public int Width => Rows[0].Length;
            public int WalkRow
            {
                get
                {
                    for (int r = 0; r < Rows.Length; r++) if (Rows[r].IndexOf('^') >= 0) return r;
                    return Rows.Length - 1;
                }
            }

            /// <summary>Every tile: x along the walk, y away from it (0 = the walk, positive on the drawn top side).</summary>
            public IEnumerable<(int x, int y, char ch)> Tiles
            {
                get
                {
                    int walk = WalkRow;
                    for (int r = 0; r < Rows.Length; r++)
                        for (int x = 0; x < Width; x++) yield return (x, walk - r, Rows[r][x]);
                }
            }

            /// <summary>Spikes the shape lays (the snake has three).</summary>
            public int SpikeCount
            {
                get
                {
                    int n = 0;
                    foreach (var row in Rows) foreach (char ch in row) if (ch == '^') n++;
                    return n;
                }
            }

            public bool HasTorch
            {
                get
                {
                    foreach (var row in Rows) if (row.IndexOf('T') >= 0) return true;
                    return false;
                }
            }
        }

        public static readonly IReadOnlyList<Pattern> All = new[]
        {
            // 2 moves across, 6 round: the short loop.
            new Pattern
            {
                Name = "pont", Rows = new[]
                {
                    "+++",
                    "+#+",
                    "W^W",
                },
            },
            // 4 moves across, 8 round: a parallel corridor along a longer stretch.
            new Pattern
            {
                Name = "long pont", Rows = new[]
                {
                    "+++++",
                    "+###+",
                    "W^WWW",
                },
            },
            // 2 moves across, 10 round: the safe way is a real trek.
            new Pattern
            {
                Name = "pont profond", Rows = new[]
                {
                    "+++",
                    "+#+",
                    "+#+",
                    "+#+",
                    "W^W",
                },
            },
            // The short loop with a wall torch at the top of the safe way (after dust).
            new Pattern
            {
                Name = "pont à torche", Rows = new[]
                {
                    ".T.",
                    "+++",
                    "+#+",
                    "W^W",
                },
            },
            // The parallel corridor with a wall torch halfway along the safe way (after dust).
            new Pattern
            {
                Name = "long pont à torche", Rows = new[]
                {
                    "..T..",
                    "+++++",
                    "+###+",
                    "W^WWW",
                },
            },
            // The snake (after dust): three spikes in a row, each with its own short loop, over, under, over; the torch on
            // the loop underneath. Straight on is three hits (two kill), so the player picks which loops to walk.
            new Pattern
            {
                Name = "serpent", Rows = new[]
                {
                    "+++#+++",
                    "+#+#+#+",
                    "W^W^W^W",
                    "..+#+..",
                    "..+++..",
                    "...T...",
                },
            },
        };

        /// <summary>The same shape with the walk going the other way.</summary>
        public static Pattern Mirrored(Pattern p)
        {
            var rows = new string[p.Rows.Length];
            for (int y = 0; y < rows.Length; y++)
            {
                var chars = p.Rows[y].ToCharArray();
                System.Array.Reverse(chars);
                rows[y] = new string(chars);
            }
            return new Pattern { Name = p.Name, Rows = rows };
        }
    }
}
