using System;

namespace MummyEscape.Core
{
    public enum ActionKind : byte { Move, Disarm }

    /// <summary>One player input: swipe (Move) or the disarm button next to visible spikes (Disarm).</summary>
    public readonly struct PlayerAction : IEquatable<PlayerAction>
    {
        public readonly ActionKind Kind;
        public readonly Dir Dir;

        public PlayerAction(ActionKind kind, Dir dir) { Kind = kind; Dir = dir; }

        public static PlayerAction Move(Dir d) => new PlayerAction(ActionKind.Move, d);
        public static PlayerAction Disarm(Dir d) => new PlayerAction(ActionKind.Disarm, d);

        public bool Equals(PlayerAction o) => Kind == o.Kind && Dir == o.Dir;
        public override bool Equals(object obj) => obj is PlayerAction a && Equals(a);
        public override int GetHashCode() => ((int)Kind << 4) | (int)Dir;
        public override string ToString() => Kind == ActionKind.Move ? Dir.ToString() : "Disarm" + Dir;
    }

    /// <summary>The gameplay-relevant state: everything the solver needs to reproduce a run exactly.</summary>
    public struct RuleState : IEquatable<RuleState>
    {
        public Cell Position;
        /// <summary>Bit i set = channel i activated (doors open, locked portals unsealed).</summary>
        public int Pressed;
        /// <summary>Bit i set = trap with TrapIndex i disarmed.</summary>
        public int Disarmed;
        public int Hp;
        /// <summary>Moves of blindness left (darkness trap). A blind player cannot see, hence cannot disarm.</summary>
        public int Blind;
        /// <summary>The torch was smothered by dust: only the own tile is lit (and traps cannot be seen to disarm).</summary>
        public bool TorchOut;
        /// <summary>Bit i set = spikes with TrapIndex i walked on: the mummy knows where they are, it can disarm them in the dark.</summary>
        public int Felt;
        /// <summary>Bit i set = fragile slab with Param i has collapsed (impassable rubble).</summary>
        public int Crumbled;
        /// <summary>Actions taken, modulo 3: the rhythm of the flame jets.</summary>
        public int Tick;
        /// <summary>Quarter turns (clockwise) of the tomb on screen, from turning slabs: a swipe moves the way it points on screen.</summary>
        public int Rotation;
        /// <summary>Steps left with reversed controls (mirror of Seth).</summary>
        public int Reversed;

        /// <summary>The torch lights the 4 neighbouring tiles: not while blinded, not while it is out.</summary>
        public bool SeesNeighbours => Blind == 0 && !TorchOut;

        /// <summary>The direction in the tomb a swipe stands for (screen turned, controls reversed).</summary>
        public Dir WorldDir(Dir swipe) => swipe.Turn(-Rotation + (Reversed > 0 ? 2 : 0));

        public bool Equals(RuleState o) => Position == o.Position && Pressed == o.Pressed && Disarmed == o.Disarmed && Hp == o.Hp && Blind == o.Blind
                                           && TorchOut == o.TorchOut && Felt == o.Felt && Crumbled == o.Crumbled && Tick == o.Tick
                                           && Rotation == o.Rotation && Reversed == o.Reversed;
        public override bool Equals(object obj) => obj is RuleState s && Equals(s);
        public override int GetHashCode() => Position.GetHashCode() ^ (Pressed * 397) ^ (Disarmed * 7919) ^ Hp ^ (Blind << 20) ^ (TorchOut ? 1 << 24 : 0)
                                             ^ (Felt * 104729) ^ (Crumbled * 1543) ^ (Tick << 28) ^ (Rotation << 26) ^ (Reversed << 16);
    }

    [Flags]
    public enum StepFlags
    {
        None = 0,
        Blocked = 1 << 0,
        Moved = 1 << 1,
        ButtonPressed = 1 << 2,
        TrapTriggered = 1 << 3,
        Damaged = 1 << 4,
        Blinded = 1 << 5,
        Teleported = 1 << 6,
        FogReset = 1 << 7,
        Fell = 1 << 8,
        Climbed = 1 << 9,
        Disarmed = 1 << 10,
        Won = 1 << 11,
        Died = 1 << 12,
        PortalSealed = 1 << 13,
        HiddenRevealed = 1 << 14,
        TorchSmothered = 1 << 15,
        TorchRelit = 1 << 16,
        /// <summary>A current carried the player further than the tile it walked onto.</summary>
        Swept = 1 << 17,
        /// <summary>The fragile slab the player just left collapsed.</summary>
        Collapsed = 1 << 18,
        /// <summary>A toggle switch flipped its channel (also flagged ButtonPressed).</summary>
        Switched = 1 << 19,
        /// <summary>Walked into a firing flame jet (also flagged Damaged).</summary>
        Burned = 1 << 20,
        /// <summary>Set by the session: no way to the exit is left (currents, collapsed slabs, barriers).</summary>
        Trapped = 1 << 21,
        /// <summary>A turning slab turned the tomb (see <see cref="RuleState.Rotation"/>).</summary>
        Rotated = 1 << 22,
        /// <summary>A mirror of Seth reversed the controls.</summary>
        Reversed = 1 << 23,
    }

    public struct StepResult
    {
        public RuleState State;
        public StepFlags Flags;
        /// <summary>The direction in the tomb the action went (a swipe after turning and reversal).</summary>
        public Dir Dir;
        /// <summary>Tile the player walked onto before any transport (teleporter, ladder, hole).</summary>
        public Cell SteppedOn;
        /// <summary>Channel activated, when ButtonPressed is set.</summary>
        public int Channel;

        public bool Has(StepFlags f) => (Flags & f) != 0;
        /// <summary>A step costs one move unless it was blocked.</summary>
        public bool CountsAsMove => !Has(StepFlags.Blocked);
        public bool CountsAsInteraction => Has(StepFlags.ButtonPressed) || Has(StepFlags.Disarmed);
    }

    /// <summary>
    /// The single source of truth for game rules. Used by both the solver (to compute par and validate
    /// difficulty) and the GameSession (to play), so what the tests verify is exactly what players get.
    /// </summary>
    public static class Rules
    {
        public const int BlindDuration = 3;
        /// <summary>Steps taken with reversed controls after a mirror of Seth.</summary>
        public const int ReverseDuration = 10;

        public static RuleState Initial(Level level) => new RuleState { Position = level.Start, Hp = level.MaxHp };

        public static bool IsDoorOpen(Tile t, int pressed) => (pressed & (1 << t.Channel)) != 0;
        public static bool IsTrapArmed(Tile t, int disarmed) => t.Type == TileType.Trap && (disarmed & (1 << t.TrapIndex)) == 0;
        /// <summary>Only traps that hurt (spikes) can be disarmed; cursed sand cannot.</summary>
        public static bool IsDisarmable(Tile t, int disarmed) => IsTrapArmed(t, disarmed) && t.Trap == TrapKind.Spikes;
        /// <summary>
        /// The mummy can reach for these spikes: it sees them by torchlight, or, blind or with its torch out, it has
        /// already walked on them and knows where they are.
        /// </summary>
        public static bool CanFeelFor(RuleState s, Tile t) => s.SeesNeighbours || (t.Type == TileType.Trap && (s.Felt & (1 << t.TrapIndex)) != 0);

        /// <summary>Doors open with their channel; red barriers too, blue barriers do the opposite.</summary>
        public static bool IsGateOpen(Tile t, int pressed)
        {
            if (t.Type == TileType.Door) return IsDoorOpen(t, pressed);
            if (t.Type == TileType.Barrier) return IsDoorOpen(t, pressed) == (t.Param == 0);
            return true;
        }

        public static bool IsCollapsed(Tile t, int crumbled) => t.Type == TileType.Crumbling && (crumbled & (1 << t.Param)) != 0;

        /// <summary>The flame jet is blasting in this state (a player standing in it at this tick gets burned).</summary>
        public static bool IsFiring(Tile t, int tick) => t.Type == TileType.FireJet && t.Param == tick;

        public const int FlameCycle = 3;

        public static bool CanEnter(Level level, Cell c, int pressed) => CanEnter(level, c, new RuleState { Pressed = pressed });

        public static bool CanEnter(Level level, Cell c, RuleState s)
        {
            if (!level.InBounds(c)) return false;
            var t = level[c];
            if (t.IsSolid) return false;
            if (t.IsGate && !IsGateOpen(t, s.Pressed)) return false;
            if (IsCollapsed(t, s.Crumbled)) return false;
            return true;
        }

        public static StepResult Step(Level level, RuleState s, PlayerAction action)
        {
            var r = new StepResult { State = s, SteppedOn = s.Position };
            // Every accepted action ticks blindness down; a fresh darkness trap resets it below.
            r.State.Blind = s.Blind > 0 ? s.Blind - 1 : 0;
            r.State.Tick = (s.Tick + 1) % FlameCycle;
            // A swipe follows the screen (turned tomb) and the mirror; the disarm button names the trap's side directly.
            r.Dir = action.Kind == ActionKind.Move ? s.WorldDir(action.Dir) : action.Dir;
            var target = s.Position.Step(r.Dir);

            if (action.Kind == ActionKind.Disarm)
            {
                var tt = level.Get(target);
                if (!IsDisarmable(tt, s.Disarmed) || !CanFeelFor(s, tt)) { r.State = s; r.Flags = StepFlags.Blocked; return r; }
                r.State.Disarmed |= 1 << tt.TrapIndex;
                r.Flags = StepFlags.Disarmed;
                return r;
            }

            if (!CanEnter(level, target, s)) { r.State = s; r.Flags = StepFlags.Blocked; return r; }
            // Reversed controls wear off step by step (bumping into a wall does not count).
            r.State.Reversed = s.Reversed > 0 ? s.Reversed - 1 : 0;

            r.Flags = StepFlags.Moved;
            // A fragile slab gives way as soon as the mummy steps off it.
            var from = level[s.Position];
            if (from.Type == TileType.Crumbling)
            {
                r.State.Crumbled |= 1 << from.Param;
                r.Flags |= StepFlags.Collapsed;
            }

            // Currents carry the mummy downstream, tile after tile, until still ground or an obstacle.
            for (int guard = 0; guard < 64 && level[target].Type == TileType.Current; guard++)
            {
                var next = target.Step((Dir)level[target].Param);
                if (!CanEnter(level, next, r.State)) break;
                target = next;
                r.Flags |= StepFlags.Swept;
            }

            r.SteppedOn = target;
            r.State.Position = target;
            var t = level[target];

            switch (t.Type)
            {
                case TileType.Exit:
                    r.Flags |= StepFlags.Won;
                    break;

                case TileType.Button:
                    if ((s.Pressed & (1 << t.Channel)) == 0)
                    {
                        r.State.Pressed |= 1 << t.Channel;
                        r.Flags |= StepFlags.ButtonPressed;
                        r.Channel = t.Channel;
                    }
                    break;

                case TileType.Switch:
                    r.State.Pressed ^= 1 << t.Channel;
                    r.Flags |= StepFlags.ButtonPressed | StepFlags.Switched;
                    r.Channel = t.Channel;
                    break;

                case TileType.FireJet:
                    if (IsFiring(t, r.State.Tick))
                    {
                        r.State.Hp--;
                        r.Flags |= StepFlags.TrapTriggered | StepFlags.Damaged | StepFlags.Burned;
                        if (r.State.Hp <= 0) r.Flags |= StepFlags.Died;
                    }
                    break;

                case TileType.Trap:
                    if (IsTrapArmed(t, s.Disarmed))
                    {
                        r.Flags |= StepFlags.TrapTriggered;
                        if (t.Trap == TrapKind.Spikes)
                        {
                            r.State.Hp--;
                            r.State.Felt |= 1 << t.TrapIndex;
                            r.Flags |= StepFlags.Damaged;
                            if (r.State.Hp <= 0) r.Flags |= StepFlags.Died;
                        }
                        else if (t.Trap == TrapKind.Darkness)
                        {
                            r.Flags |= StepFlags.Blinded;
                            r.State.Blind = BlindDuration;
                        }
                        else if (t.Trap == TrapKind.Rotate)
                        {
                            r.State.Rotation = (s.Rotation + t.Param) & 3;
                            r.State.Disarmed |= 1 << t.TrapIndex; // single use
                            r.Flags |= StepFlags.Rotated;
                        }
                        else if (t.Trap == TrapKind.Reverse)
                        {
                            r.State.Reversed = ReverseDuration;
                            r.State.Disarmed |= 1 << t.TrapIndex; // single use
                            r.Flags |= StepFlags.Reversed;
                        }
                    }
                    break;

                case TileType.Teleporter:
                    if (t.Teleporter == TeleporterKind.Hidden) r.Flags |= StepFlags.HiddenRevealed;
                    if (t.Teleporter == TeleporterKind.Locked && !IsDoorOpen(t, s.Pressed))
                    {
                        r.Flags |= StepFlags.PortalSealed;
                    }
                    else if (level.TryGetTeleportTarget(target, out var dest))
                    {
                        r.State.Position = dest;
                        r.Flags |= StepFlags.Teleported;
                        if (t.Teleporter == TeleporterKind.Cursed) r.Flags |= StepFlags.FogReset;
                    }
                    break;

                case TileType.BreakableFloor:
                    r.State.Position = target.WithFloor(target.Floor - 1);
                    r.Flags |= StepFlags.Fell;
                    break;

                case TileType.LadderUp:
                    r.State.Position = target.WithFloor(target.Floor + 1);
                    r.Flags |= StepFlags.Climbed;
                    break;

                case TileType.LadderDown:
                    r.State.Position = target.WithFloor(target.Floor - 1);
                    r.Flags |= StepFlags.Climbed;
                    break;
            }

            // Torch: dust smothers it, a wall torch next to where the mummy ends up relights it.
            if (level[r.State.Position].Type == TileType.Dust)
            {
                if (!r.State.TorchOut) r.Flags |= StepFlags.TorchSmothered;
                r.State.TorchOut = true;
            }
            else if (r.State.TorchOut && NextToWallTorch(level, r.State.Position))
            {
                r.State.TorchOut = false;
                r.Flags |= StepFlags.TorchRelit;
            }
            return r;
        }

        public static bool NextToWallTorch(Level level, Cell c)
        {
            foreach (var d in DirExt.All)
                if (level.Get(c.Step(d)).Type == TileType.WallTorch) return true;
            return false;
        }
    }
}
