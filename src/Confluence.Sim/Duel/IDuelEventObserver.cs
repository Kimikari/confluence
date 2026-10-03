namespace Confluence.Sim.Duel
{
    /// <summary>
    /// Sees every duel event at the moment it is emitted, in emission order (registered with
    /// <see cref="DuelWorld.AddEventObserver"/>). Observers record facts for later processing (for example the
    /// server-generated combat events grammars read on the next cast step); they must not change world state from
    /// inside the callback, because emission happens in the middle of rule resolution.
    /// </summary>
    public interface IDuelEventObserver
    {
        void OnEvent(DuelWorld world, DuelEvent e);
    }
}
