using Confluence.Casting;
using Confluence.Core.Geometry;
using Confluence.Sim.Duel;

namespace Confluence.Sim.Content.Wizards.HalenTide
{
    /// <summary>What a WIZ-010 kit event reports (HUD, audio cues, replays; spec L96, L549 altered cues).</summary>
    public enum TideEventKind
    {
        /// <summary>SPL-075: heat struck the bubble and consumed shield into a steam puff (<see cref="TideEvent.Amount"/> = shield lost).</summary>
        BubbleHeatReaction,

        /// <summary>SPL-075: the bubble broke before its duration ended (Amount 1 = a thermal break).</summary>
        BubbleBroken,

        /// <summary>AUG-437: the recast threw the remaining shield (Amount = shield thrown).</summary>
        BubbleReleased,

        /// <summary>SPL-074: the strike missed and its recovery was lengthened (Amount = added seconds).</summary>
        MissRecovery,

        /// <summary>AUG-433: the jet banked from a wet wall (Position = the bank point).</summary>
        WetWallBank,

        /// <summary>AUG-434: a missed jet left a mist cloud.</summary>
        MissCloud,

        /// <summary>AUG-444: the stream's arrow reversed (Amount 180) or turned (Amount 90).</summary>
        StreamTurned,

        /// <summary>AUG-442: the vault's landing point moved back along its arc (Position = new landing, Amount = meters).</summary>
        VaultBraked,

        /// <summary>SPL-079: the root ended (or never applied) and the follow-up ran (Amount 1 when the root had applied).</summary>
        PrisonReleased,

        /// <summary>SPL-080: the splash met a surface and neutralized units (Amount = units removed).</summary>
        SpringNeutralized,

        /// <summary>SPL-080: no surface within range; the water is spent with no effect.</summary>
        SpringNoSurface,

        /// <summary>The eddy, pool or stream bonus status changed for a player (Amount 1 on, 0 off).</summary>
        StreamBonus,
    }

    /// <summary>A WIZ-010 kit event in the duel's ordered stream (one id sequence, spec L359, L419).</summary>
    public sealed class TideEvent : DuelEvent
    {
        public TideEventKind Kind { get; internal set; }
        public string SpellId { get; internal set; }
        public CastEventId CastId { get; internal set; }
        public Vec3 Position { get; internal set; }
        public float Amount { get; internal set; }
        public int TargetPlayer { get; internal set; } = -1;

        public override DuelEventType Type => DuelEventType.Spell;

        public override string ToString() => base.ToString() + " " + Kind + " " + SpellId;
    }
}
