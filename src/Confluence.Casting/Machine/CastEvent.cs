namespace Confluence.Casting
{
    public enum CastEventKind : byte
    {
        /// <summary>A preparation opened for the selected spell; shown to the caster and as hand pose to others (spec L472).</summary>
        PreparationStarted = 0,

        /// <summary>The anticipatory cue began; <see cref="CastEvent.Ticks"/> is its guaranteed minimum (spec L96).</summary>
        CueStarted,

        /// <summary>Preparation ended without commitment: no mana spent, re-cast lock applied (spec L90).</summary>
        PreparationCancelled,

        /// <summary>Mana paid and cooldown started (spec L90). Carries the cast's one mastery result.</summary>
        Committed,

        /// <summary>Windup finished; the simulation spawns the effect.</summary>
        Released,

        /// <summary>One channel pulse after release; <see cref="CastEvent.Count"/> is the 1-based pulse index.</summary>
        ChannelPulse,

        ChannelEnded,

        /// <summary>The caster abandoned a committed windup: resource spent, cooldown continues, no refund (spec L90).</summary>
        CommittedCastCancelled,

        /// <summary>A committed cast was interrupted; <see cref="CastEvent.Mana"/> is the refund (spec L90).</summary>
        Interrupted,

        RecoveryEnded,

        /// <summary>A pending recast state exists for the spell (spec L94); <see cref="CastEvent.Count"/> recasts allowed.</summary>
        RecastWindowOpened,

        /// <summary>The selected spell's pending recast fired, reusing the original cast identity and mastery (spec L94).</summary>
        Recast,

        RecastWindowClosed,

        /// <summary>An input that would begin a cast or recast was refused; see <see cref="CastEvent.Reject"/>.</summary>
        Rejected,

        /// <summary>A refund effect returned mana for an earlier cast (within the 25% / once rules, spec L90).</summary>
        Refunded,
    }

    /// <summary>Why a cast or recast input was refused.</summary>
    public enum RejectReason : byte
    {
        None = 0,
        CooldownNotReady,
        RecastLocked,
        Silenced,
        HardControlled,
        Warded,
        InsufficientMana,
        NoPendingRecast,
        NoSpellSelected,
        Busy,
    }

    /// <summary>
    /// One typed cast event for the simulation, HUD, and replay stream. Fields not used by a kind keep defaults.
    /// </summary>
    public readonly struct CastEvent
    {
        public CastEvent(CastEventKind kind, int tick, CastEventId castId, string spellId, int slot)
            : this(kind, tick, castId, spellId, slot, default, CancelReason.None, InterruptCause.None, ChannelEndReason.Completed, RejectReason.None, 0f, 0, 0, false)
        {
        }

        public CastEvent(CastEventKind kind, int tick, CastEventId castId, string spellId, int slot, MasteryResult mastery,
            CancelReason cancel, InterruptCause interrupt, ChannelEndReason channelEnd, RejectReason reject, float mana, int ticks, int count, bool auto)
        {
            Kind = kind;
            Tick = tick;
            CastId = castId;
            SpellId = spellId;
            Slot = slot;
            Mastery = mastery;
            Cancel = cancel;
            Interrupt = interrupt;
            ChannelEnd = channelEnd;
            Reject = reject;
            Mana = mana;
            Ticks = ticks;
            Count = count;
            Auto = auto;
        }

        public CastEventKind Kind { get; }
        public int Tick { get; }
        public CastEventId CastId { get; }
        public string SpellId { get; }

        /// <summary>Equipped slot 0-5, or -1 for the signature.</summary>
        public int Slot { get; }

        public MasteryResult Mastery { get; }
        public CancelReason Cancel { get; }
        public InterruptCause Interrupt { get; }
        public ChannelEndReason ChannelEnd { get; }
        public RejectReason Reject { get; }

        /// <summary>Mana paid (Committed, Recast), refunded (Interrupted, Refunded).</summary>
        public float Mana { get; }

        /// <summary>Windup ticks (Committed), exposed preparation ticks (PreparationCancelled), cue ticks (CueStarted), window ticks (RecastWindowOpened).</summary>
        public int Ticks { get; }

        /// <summary>Pulse index (ChannelPulse), recast number (Recast), recasts allowed (RecastWindowOpened).</summary>
        public int Count { get; }

        /// <summary>The grammar committed without a player release (WIZ-002 overhold vent, WIZ-010 band exit).</summary>
        public bool Auto { get; }

        public override string ToString() => Tick + " " + Kind + " " + CastId + " " + SpellId;
    }
}
