using System;

namespace MummyEscape.Core
{
    public enum ActionKind : byte { Move, Disarm }

    /// <summary>One player input: swipe (Move) or tap on an adjacent trap (Disarm).</summary>
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

        /// <summary>The torch lights the 4 neighbouring tiles: not while blinded, not while it is out.</summary>
        public bool SeesNeighbours => Blind == 0 && !TorchOut;

        public bool Equals(RuleState o) => Position == o.Position && Pressed == o.Pressed && Disarmed == o.Disarmed && Hp == o.Hp && Blind == o.Blind && TorchOut == o.TorchOut;
        public override bool Equals(object obj) => obj is RuleState s && Equals(s);
        public override int GetHashCode() => Position.GetHashCode() ^ (Pressed * 397) ^ (Disarmed * 7919) ^ Hp ^ (Blind << 20) ^ (TorchOut ? 1 << 24 : 0);
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
    }

    public struct StepResult
    {
        public RuleState State;
        public StepFlags Flags;
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

        public static RuleState Initial(Level level) => new RuleState { Position = level.Start, Hp = level.MaxHp };

        public static bool IsDoorOpen(Tile t, int pressed) => (pressed & (1 << t.Channel)) != 0;
        public static bool IsTrapArmed(Tile t, int disarmed) => t.Type == TileType.Trap && (disarmed & (1 << t.TrapIndex)) == 0;

        public static bool CanEnter(Level level, Cell c, int pressed)
        {
            if (!level.InBounds(c)) return false;
            var t = level[c];
            if (t.IsSolid) return false;
            if (t.Type == TileType.Door && !IsDoorOpen(t, pressed)) return false;
            return true;
        }

        public static StepResult Step(Level level, RuleState s, PlayerAction action)
        {
            var r = new StepResult { State = s, SteppedOn = s.Position };
            // Every accepted action ticks blindness down; a fresh darkness trap resets it below.
            r.State.Blind = s.Blind > 0 ? s.Blind - 1 : 0;
            var target = s.Position.Step(action.Dir);

            if (action.Kind == ActionKind.Disarm)
            {
                var tt = level.Get(target);
                if (!s.SeesNeighbours || !IsTrapArmed(tt, s.Disarmed)) { r.State = s; r.Flags = StepFlags.Blocked; return r; }
                r.State.Disarmed |= 1 << tt.TrapIndex;
                r.Flags = StepFlags.Disarmed;
                return r;
            }

            if (!CanEnter(level, target, s.Pressed)) { r.State = s; r.Flags = StepFlags.Blocked; return r; }

            r.Flags = StepFlags.Moved;
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

                case TileType.Trap:
                    if (IsTrapArmed(t, s.Disarmed))
                    {
                        r.Flags |= StepFlags.TrapTriggered;
                        if (t.Trap == TrapKind.Spikes)
                        {
                            r.State.Hp--;
                            r.Flags |= StepFlags.Damaged;
                            if (r.State.Hp <= 0) r.Flags |= StepFlags.Died;
                        }
                        else if (t.Trap == TrapKind.Darkness)
                        {
                            r.Flags |= StepFlags.Blinded;
                            r.State.Blind = BlindDuration;
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
