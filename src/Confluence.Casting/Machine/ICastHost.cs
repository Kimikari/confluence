namespace Confluence.Casting
{
    /// <summary>
    /// The caster's authoritative state as the cast machine sees it. Implemented by the simulation (and by the
    /// predicting client from its last snapshot). The machine decides when to pay, refund, and start cooldowns; the
    /// host owns the numbers (mana pool, cooldown timers with their reduction caps, statuses).
    /// </summary>
    public interface ICastHost
    {
        float Mana { get; }

        /// <summary>
        /// Deducts <paramref name="amount"/> if the full amount is available and returns true; otherwise changes
        /// nothing (spec L78 "cannot cast without enough resource to pay the full initial commitment"). The amount
        /// already honours the 70% cost floor (see <see cref="MasteryClamp.PaidCost"/>).
        /// </summary>
        bool TryPayMana(float amount);

        /// <summary>Returns mana; the machine has already applied the per-cast refund caps (spec L90, L124).</summary>
        void RefundMana(float amount);

        /// <summary>Ticks until the spell's cooldown is ready (0 = ready).</summary>
        int CooldownRemainingTicks(string spellId);

        /// <summary>Starts the spell's normal cooldown at commitment (spec L90); the host applies reduction caps (spec L124).</summary>
        void StartCooldown(CastableSpell spell);

        /// <summary>Equipment and augment modifiers that apply to this cast at commitment.</summary>
        CastModifiers ModifiersFor(CastableSpell spell);

        bool IsSilenced { get; }

        /// <summary>Stun, forced inability to act, or hostile displacement that prevents casting (spec L116).</summary>
        bool IsHardControlled { get; }

        /// <summary>Inside the spawn-alcove ward, which prevents casting (spec L46).</summary>
        bool IsWarded { get; }

        bool IsSprinting { get; }
    }
}
