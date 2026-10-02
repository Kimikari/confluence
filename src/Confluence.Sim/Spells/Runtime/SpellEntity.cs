using Confluence.Core.Geometry;
using Confluence.Sim.Hashing;

namespace Confluence.Sim.Spells
{
    /// <summary>
    /// A live delivery in the duel world (projectile, beam, field, deployable, summon, wave, tether, buff, movement).
    /// The runtime steps every live entity once per tick in tick-order step 4, in creation order, so the simulation
    /// stays deterministic. An entity acts only through <see cref="SpellContext"/> and the public DuelWorld APIs.
    /// </summary>
    public abstract class SpellEntity
    {
        protected SpellEntity(CastRecord cast)
        {
            Cast = cast;
        }

        /// <summary>Runtime id, unique within the duel and increasing in creation order.</summary>
        public int Id { get; internal set; }

        public CastRecord Cast { get; }

        public int Owner => Cast.Caster;

        public string SpellId => Cast.SpellId;

        public int SpawnTick { get; internal set; }

        public bool Alive { get; private set; } = true;

        /// <summary>The spell's own recast acts on this entity (detonation, release, return; spec L94, L512).</summary>
        public virtual bool AcceptsRecast => false;

        /// <summary>Advances one tick (tick-order step 4).</summary>
        protected internal abstract void Step(SpellContext ctx);

        /// <summary>The selected spell's recast fired for this entity's cast.</summary>
        protected internal virtual void OnRecast(SpellContext ctx)
        {
        }

        /// <summary>Ends the entity; the runtime closes a lifetime recast window once the cast has none left (spec L94).</summary>
        public void End(SpellContext ctx)
        {
            if (!Alive) return;
            Alive = false;
            Cast.LiveEntities--;
            OnEnded(ctx);
        }

        protected virtual void OnEnded(SpellContext ctx)
        {
        }

        public void Hash(ref StateHasher h)
        {
            h.Add(Id);
            h.Add(Alive);
            h.Add(SpawnTick);
            h.Add((ulong)Cast.CastId.Packed);
            HashState(ref h);
        }

        protected abstract void HashState(ref StateHasher h);
    }

    /// <summary>
    /// A destructible spell body (summon, deployable anchor, rune) that hostile deliveries can strike (spec L496, L527:
    /// summons need destructible bodies). Bodies are spheres for hit tests.
    /// </summary>
    public interface ISpellBody
    {
        int Owner { get; }
        int Id { get; }
        bool IsIntact { get; }
        Vec3 BodyCenter { get; }
        float BodyRadius { get; }

        /// <summary>Durability damage from a delivery; returns true when the body broke.</summary>
        bool TakeDamage(SpellContext ctx, float amount, int attacker);
    }
}
