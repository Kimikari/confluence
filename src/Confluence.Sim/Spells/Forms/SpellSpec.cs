using System;

namespace Confluence.Sim.Spells
{
    /// <summary>
    /// Base of the typed parts of a spell form (delivery, payload, statuses, deposits). A spec is edited by the spell
    /// behavior and its transformations while the form is being resolved and is frozen when the form is built, so a
    /// resolved form is immutable for the rest of the duel.
    /// </summary>
    public abstract class SpellSpec
    {
        public bool IsFrozen { get; private set; }

        internal void Freeze()
        {
            if (IsFrozen) return;
            IsFrozen = true;
            OnFreeze();
        }

        /// <summary>Freezes nested specs.</summary>
        protected virtual void OnFreeze()
        {
        }

        /// <summary>Property setter guard: returns the value, or throws once the form is resolved.</summary>
        protected T Set<T>(T value)
        {
            if (IsFrozen) throw new InvalidOperationException(GetType().Name + " belongs to a resolved spell form and cannot change.");
            return value;
        }
    }
}
