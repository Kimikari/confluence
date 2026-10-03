using Confluence.Sim.Hashing;
using Confluence.Sim.Input;

namespace Confluence.Sim.Duel
{
    /// <summary>Why casting is currently forbidden.</summary>
    public enum CastBlockReason
    {
        None,
        AlcoveWard,     // spec L46: the ward prevents casting
        Dead,
        Stunned,        // spec L116
        Silenced,       // spec L116
        GuardRelease,   // spec L104: the punishable release window
        Melee,          // a common melee attack is in progress
        Swapping,       // spec L106: equip and swap take 0.35 s
        Recalling,      // spec L175: 0.5 s visible recall
        DuelEnded,
    }

    /// <summary>What the world allows a player's casting to do this tick (tick-order step 3).</summary>
    public struct CastPermissions
    {
        public bool CanCast;
        public bool CanUseSignature;
        public bool CanUseItems;

        /// <summary>A channel may start or continue: casting is allowed and the player is not sprinting (spec L80).</summary>
        public bool CanChannel;

        public CastBlockReason Reason;
    }

    /// <summary>
    /// The per-player casting seam: the casting state the world reads for movement and action gating. The spell
    /// runtime reports it; the default is idle.
    /// </summary>
    public struct CastingStatus
    {
        /// <summary>Movement factor of walk speed while casting: 1 none, 0.85 quick, 0.70 heavy/control/field, 0 stationary (spec L92).</summary>
        public float MovementFactor;

        /// <summary>A channel is running: it counts as mana spending and disallows sprint (spec L78, L80).</summary>
        public bool Channeling;

        /// <summary>
        /// In preparation, windup, channel, or recovery: blocks equip swaps. Common melee is blocked only by recovery
        /// (<see cref="InRecovery"/>); during the other phases a melee press performs the strike and the spell yields
        /// (spec L474, D-049).
        /// </summary>
        public bool Busy;

        /// <summary>In cast recovery: equip swaps and common melee cannot cancel it (spec L92, L106).</summary>
        public bool InRecovery;

        /// <summary>
        /// A non-quick or channeled cast is being prepared, winding up, or channeling: the motor ends or refuses sprint
        /// (spec L80; the cast is never refused for sprinting, DECISIONS D-048).
        /// </summary>
        public bool SuppressesSprint;

        public static CastingStatus Idle => new CastingStatus { MovementFactor = 1f };
    }

    /// <summary>Why the world asks the spell runtime to interrupt a cast.</summary>
    public enum InterruptCause
    {
        Stun,
        Silence,
        Displacement,
        Death,
        DuelEnded,
    }

    /// <summary>
    /// Seam to the spell stage. The world calls <see cref="UpdateCasting"/> for each player in tick-order step 3
    /// (cast phase progression; releases spawn deliveries) and <see cref="AdvanceDeliveries"/> once in step 4
    /// (deliveries advance and resolve hits through <see cref="DuelWorld.ApplyDamage"/>, queuing world deposits).
    /// Everything the runtime does goes through public DuelWorld APIs, so caps, budgets, and events stay central.
    /// </summary>
    public interface ISpellRuntime
    {
        /// <summary>Called once when the world is constructed.</summary>
        void Attach(DuelWorld world);

        void UpdateCasting(int player, in PlayerInput input, in CastPermissions permissions);

        void AdvanceDeliveries();

        /// <summary>The player's current casting state (read in steps 2 and 3 of the next tick).</summary>
        CastingStatus GetStatus(int player);

        /// <summary>Interrupts the player's cast. Returns true when a committed cast was interrupted (FEAT-019).</summary>
        bool Interrupt(int player, InterruptCause cause);

        void Hash(ref StateHasher hasher);
    }

    /// <summary>No spells: casting is always idle.</summary>
    public sealed class NullSpellRuntime : ISpellRuntime
    {
        public void Attach(DuelWorld world)
        {
        }

        public void UpdateCasting(int player, in PlayerInput input, in CastPermissions permissions)
        {
        }

        public void AdvanceDeliveries()
        {
        }

        public CastingStatus GetStatus(int player) => CastingStatus.Idle;

        public bool Interrupt(int player, InterruptCause cause) => false;

        public void Hash(ref StateHasher hasher)
        {
        }
    }
}
