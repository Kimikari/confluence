using System.Collections.Generic;
using Confluence.Sim.Duel;

namespace Confluence.Sim.Spells
{
    /// <summary>
    /// A live spell entity that records facts from the duel's event stream while it lives (for example damage its owner
    /// takes while a mantle is up, SPL-011 / AUG-310). The runtime forwards every emitted event, in emission order, to
    /// each live entity implementing this. Like any <see cref="IDuelEventObserver"/> the listener must only record: it
    /// may not change world state inside the callback (emission happens in the middle of rule resolution); it acts on
    /// what it recorded in its next <see cref="SpellEntity.Step"/>.
    /// </summary>
    public interface ISpellEventListener
    {
        void OnWorldEvent(DuelWorld world, DuelEvent e);
    }

    /// <summary>Forwards duel events to live <see cref="ISpellEventListener"/> entities (registered once per runtime).</summary>
    internal sealed class ListeningEntityForwarder : IDuelEventObserver
    {
        private readonly SpellRuntime _runtime;
        private readonly List<ISpellEventListener> _scratch = new List<ISpellEventListener>();

        public ListeningEntityForwarder(SpellRuntime runtime)
        {
            _runtime = runtime;
        }

        public void OnEvent(DuelWorld world, DuelEvent e)
        {
            IReadOnlyList<SpellEntity> entities = _runtime.Entities;
            _scratch.Clear();
            for (int i = 0; i < entities.Count; i++)
            {
                if (entities[i].Alive && entities[i] is ISpellEventListener listener) _scratch.Add(listener);
            }

            for (int i = 0; i < _scratch.Count; i++) _scratch[i].OnWorldEvent(world, e);
            _scratch.Clear();
        }
    }
}
