namespace MummyEscape.Core
{
    /// <summary>
    /// Small, fast, platform-independent PRNG (PCG-XSH-RR). Never use System.Random or UnityEngine.Random for
    /// level generation: every player must get the exact same tomb for a given level on every device.
    /// </summary>
    public sealed class Pcg32
    {
        ulong _state;
        const ulong Increment = 1442695040888963407UL;

        public Pcg32(ulong seed)
        {
            _state = 0;
            NextUInt();
            unchecked { _state += seed; }
            NextUInt();
        }

        public uint NextUInt()
        {
            unchecked
            {
                ulong old = _state;
                _state = old * 6364136223846793005UL + Increment;
                uint xorShifted = (uint)(((old >> 18) ^ old) >> 27);
                int rot = (int)(old >> 59);
                return (xorShifted >> rot) | (xorShifted << ((-rot) & 31));
            }
        }

        /// <summary>Uniform integer in [min, maxExclusive).</summary>
        public int Range(int min, int maxExclusive)
        {
            if (maxExclusive <= min) return min;
            uint span = (uint)(maxExclusive - min);
            return min + (int)(NextUInt() % span);
        }

        /// <summary>True with probability perMille / 1000 (integer maths only, fully deterministic).</summary>
        public bool Chance(int perMille) => Range(0, 1000) < perMille;

        public T Pick<T>(System.Collections.Generic.IList<T> list) => list[Range(0, list.Count)];

        public void Shuffle<T>(System.Collections.Generic.IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        /// <summary>SplitMix64 style hash to derive independent seeds.</summary>
        public static ulong Hash(ulong a, ulong b)
        {
            unchecked
            {
                ulong z = a * 0x9E3779B97F4A7C15UL + b + 0x632BE59BD9B4E019UL;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }
    }
}
