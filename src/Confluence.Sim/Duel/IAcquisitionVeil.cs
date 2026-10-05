namespace Confluence.Sim.Duel
{
    /// <summary>
    /// A content rule that hides a viewer's target acquisition beyond the shared steam path-length rule (spec L167),
    /// for example AUG-309's drawback "steam also hides caster's view": while the caster stands inside their own mantle
    /// puff they cannot acquire anyone outside it. It can only remove sight (stricter than the global rule, never
    /// looser). The spell runtime implements it by asking its live entities; <see cref="DuelWorld.CanAcquire"/> reads it.
    /// </summary>
    public interface IAcquisitionVeil
    {
        /// <summary>True when the viewer's view of the target is hidden this tick.</summary>
        bool HidesView(DuelWorld world, int viewer, int target);
    }
}
