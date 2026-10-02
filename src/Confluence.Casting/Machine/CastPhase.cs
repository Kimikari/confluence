namespace Confluence.Casting
{
    /// <summary>
    /// Phases of one cast (spec L90-L92). Commitment and release are instants between phases: commitment ends
    /// preparation (mana paid, cooldown started) and release ends windup (the effect spawns).
    /// </summary>
    public enum CastPhase : byte
    {
        Idle = 0,

        /// <summary>Grammar-driven, unpaid, cancellable without mana (spec L90, L472).</summary>
        Preparation,

        /// <summary>Committed: intrinsic cast_time windup before release (spec L92, L525).</summary>
        Windup,

        /// <summary>Released channel, cancellable on any tick, at most 3 s (spec L92).</summary>
        Channel,

        /// <summary>After release or channel end; cannot be cancelled (spec L92, L106).</summary>
        Recovery,
    }

    /// <summary>Why an open preparation ended without commitment. No mana is spent (spec L90).</summary>
    public enum CancelReason : byte
    {
        None = 0,

        /// <summary>Guard, dodge, or common melee pressed (spec L474).</summary>
        CommonAction,

        /// <summary>A different spell was selected; there is no swap windup (spec L106).</summary>
        SelectionChanged,

        /// <summary>The grammar abandoned the attempt (invalid strokes, timeout, explicit grammar cancel).</summary>
        Grammar,

        Silenced,
        HardControlled,

        /// <summary>The spawn-alcove ward prevents casting (spec L46).</summary>
        Warded,

        InsufficientMana,
        CooldownNotReady,

        /// <summary>The simulation cancelled the preparation (for example a spell-specific rule).</summary>
        External,
    }

    /// <summary>Why a committed cast was cut short.</summary>
    public enum InterruptCause : byte
    {
        None = 0,
        Silence,
        HardControl,

        /// <summary>A spell-specific rule (for example "damage during windup cancels cast").</summary>
        SpellRule,

        /// <summary>The duel or round ended.</summary>
        DuelEnded,

        /// <summary>The caster died.</summary>
        Death,

        /// <summary>The caster fell out of the map and is held by the visible recall (spec L175).</summary>
        Recalled,
    }

    /// <summary>How a channel ended.</summary>
    public enum ChannelEndReason : byte
    {
        Completed = 0,

        /// <summary>The caster stopped it (primary, guard, dodge, melee, or selection; spec L92).</summary>
        Cancelled,

        Interrupted,
    }
}
