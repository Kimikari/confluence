namespace Confluence.Casting
{
    /// <summary>
    /// Optional extension of <see cref="ICastHost"/>: the simulation reports whether a spell's open recast state has
    /// anything to act on right now. Spec L94: a recast "acts only on the selected spell's currently pending recast
    /// state and does nothing when none exists" — so a press while the original effect is still on its way (an orb in
    /// flight, a projectile that has yet to plant its marker) is refused as <see cref="RejectReason.NoPendingRecast"/>
    /// and spends neither a recast use nor a surcharge. Hosts that do not implement it keep every open window ready.
    /// </summary>
    public interface IRecastGate
    {
        /// <summary>True when a recast of <paramref name="castId"/> would act on something this tick.</summary>
        bool HasRecastTarget(CastEventId castId);
    }
}
