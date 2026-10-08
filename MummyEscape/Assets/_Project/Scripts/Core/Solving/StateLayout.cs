using System;

namespace MummyEscape.Core
{
    /// <summary>
    /// How a <see cref="RuleState"/> is packed into a long for one tomb: each field only as wide as the tomb needs (no
    /// bits for fragile slabs in a tomb without any, no flame rhythm without flame jets...). Built once per search.
    /// </summary>
    internal sealed class StateLayout
    {
        readonly int _posBits;
        readonly int _pressedShift, _disarmedShift, _hpShift, _blindShift, _torchShift, _crumbledShift, _tickShift, _rotationShift, _reversedShift, _feltShift;
        readonly int _pressedMask, _disarmedMask, _blindMask, _torchMask, _crumbledMask, _tickMask, _rotationMask, _reversedMask, _spikes;
        readonly bool _felt;
        readonly Level _level;

        /// <summary>Bits the tomb needs; more than 64 cannot be searched.</summary>
        public readonly int Bits;

        /// <param name="tick">The flame rhythm matters (tomb with flame jets, and the search cares).</param>
        public StateLayout(Level level, bool tick)
        {
            _level = level;
            int channels = 0, traps = 0, crumbling = 0;
            bool dust = false, dark = false, turn = false, mirror = false;
            for (int i = 0; i < level.CellCount; i++)
            {
                var t = level[level.CellAt(i)];
                channels = Math.Max(channels, t.Channel + 1);
                if (t.Type == TileType.Crumbling) crumbling = Math.Max(crumbling, t.Param + 1);
                if (t.Type == TileType.Dust) dust = true;
                if (t.Type != TileType.Trap) continue;
                traps = Math.Max(traps, t.TrapIndex + 1);
                if (t.Trap == TrapKind.Spikes) _spikes |= 1 << t.TrapIndex;
                dark |= t.Trap == TrapKind.Darkness;
                turn |= t.Trap == TrapKind.Rotate;
                mirror |= t.Trap == TrapKind.Reverse;
            }
            // Spikes felt underfoot only matter when the mummy can lose its sight.
            _felt = _spikes != 0 && (dust || dark);

            int at = _posBits = BitsFor(level.CellCount);
            int Field(int width, out int mask)
            {
                mask = width >= 31 ? -1 : (1 << width) - 1;
                int shift = at;
                at += width;
                return shift;
            }
            _pressedShift = Field(channels, out _pressedMask);
            _disarmedShift = Field(traps, out _disarmedMask);
            _hpShift = Field(3, out _);
            _blindShift = Field(dark ? 2 : 0, out _blindMask);
            _torchShift = Field(dust ? 1 : 0, out _torchMask);
            _crumbledShift = Field(crumbling, out _crumbledMask);
            _tickShift = Field(tick ? 2 : 0, out _tickMask);
            _rotationShift = Field(turn ? 2 : 0, out _rotationMask);
            _reversedShift = Field(mirror ? 4 : 0, out _reversedMask);
            _feltShift = Field(_felt ? traps : 0, out _);
            Bits = at;
        }

        static int BitsFor(int count)
        {
            int bits = 0;
            while ((1 << bits) < count) bits++;
            return bits;
        }

        public long Pack(RuleState s)
        {
            long k = _level.IndexOf(s.Position)
                     | ((long)(s.Pressed & _pressedMask) << _pressedShift)
                     | ((long)(s.Disarmed & _disarmedMask) << _disarmedShift)
                     | ((long)(s.Hp & 0x7) << _hpShift)
                     | ((long)(s.Blind & _blindMask) << _blindShift)
                     | ((long)((s.TorchOut ? 1 : 0) & _torchMask) << _torchShift)
                     | ((long)(s.Crumbled & _crumbledMask) << _crumbledShift)
                     | ((long)(s.Tick & _tickMask) << _tickShift)
                     | ((long)(s.Rotation & _rotationMask) << _rotationShift)
                     | ((long)(s.Reversed & _reversedMask) << _reversedShift);
            // Only spikes still armed: once disarmed, having felt them changes nothing.
            if (_felt) k |= (long)(s.Felt & _spikes & ~s.Disarmed & _disarmedMask) << _feltShift;
            return k;
        }
    }
}
