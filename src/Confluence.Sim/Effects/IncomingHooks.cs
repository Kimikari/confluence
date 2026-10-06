using Confluence.Sim.Combat;
using Confluence.Sim.Status;

namespace Confluence.Sim.Effects
{
    // Adjust points for what a player RECEIVES (augments-b, Stage 2 batch 2). An EffectHook (EffectHooks.cs) implements one
    // of these interfaces to be asked at the exact point the rule computes the value; the owner's effect host asks its hooks
    // in attach order (EffectHost.Incoming.cs) and the world calls EffectSystem's entry points (EffectSystem.Incoming.cs).
    // Like every hook adjust method these are pure functions of the hook's recorded state and the arguments; the global
    // caps still apply where the rule computes the value (passive reduction cap, status caps, knockback cap).

    /// <summary>
    /// An original hit on the hook's owner before the damage pipeline (spec L112). <paramref name="reduction"/> is a
    /// conditional passive reduction (fraction) that joins the hit's <see cref="DamageRequest.ExtraPassiveReduction"/>, so it
    /// combines with resistance and the other passive reductions under the 45% total cap; <paramref name="increase"/> is a
    /// fractional vulnerability that scales the hit's parts ("direct hits above 40 damage deal 2% more damage").
    /// </summary>
    public interface IIncomingHitHook
    {
        void AdjustIncoming(DamageRequest hit, ref float reduction, ref float increase);
    }

    /// <summary>
    /// A status about to be applied to the hook's owner (spec L116-L118): the hook may change its duration ("burning duration
    /// decreases 10%", "wetness lasts 0.25 s longer"); the status rules' own maxima still clamp it.
    /// </summary>
    public interface IIncomingStatusHook
    {
        void AdjustIncomingStatus(ref StatusApplication application);
    }

    /// <summary>
    /// A hostile knockback about to displace the hook's owner (spec L116, L173): the returned distance replaces
    /// <paramref name="distance"/> ("displacement distance decreases 8%"); the 4 m knockback cap still applies.
    /// </summary>
    public interface IIncomingKnockbackHook
    {
        float AdjustIncomingKnockback(int sourcePlayer, float distance, string sourceId);
    }

    /// <summary>
    /// The recharge a commitment of the hook's owner starts for one of its spells, after permanent cooldown reduction (spec
    /// L124): the returned seconds replace <paramref name="seconds"/> ("cooldown increases 3%"). A hook may only lengthen it
    /// or keep it; reductions belong to the cooldown-reduction stat and its 25% cap.
    /// </summary>
    public interface ICastCooldownHook
    {
        float AdjustCooldownSec(string spellId, float seconds);
    }
}
