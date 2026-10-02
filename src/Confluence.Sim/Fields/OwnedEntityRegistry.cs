using System;
using System.Collections.Generic;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Sim.Duel;
using Confluence.Sim.Hashing;

namespace Confluence.Sim.Fields
{
    /// <summary>Contact and lifetime behavior of a field or summon, run in tick-order step 6.</summary>
    public interface IOwnedEntityBehavior
    {
        void OnTick(DuelWorld world, OwnedEntity entity);
    }

    /// <summary>Request to create a gameplay field or summon.</summary>
    public struct OwnedEntitySpec
    {
        public OwnedEntityKind Kind;
        public int Owner;
        public string DefinitionId;

        /// <summary>Requested lifetime; capped at 8 s (spec L128).</summary>
        public int LifetimeTicks;

        public Vec3 Position;
        public float Radius;

        /// <summary>Neutral reaction field (steam, ice, water) that still counts against its creator (spec L128, L670).</summary>
        public bool Neutral;

        /// <summary>Summon body durability; summons are visible and destructible (spec L496, L527).</summary>
        public float Health;

        /// <summary>False for entities the oldest-retirement rule may not remove (none in the global rules).</summary>
        public bool NotRetirable;

        public int OriginEventId;
        public IOwnedEntityBehavior Behavior;
    }

    /// <summary>A live player-owned field or summon.</summary>
    public sealed class OwnedEntity
    {
        internal OwnedEntity(int id, in OwnedEntitySpec spec, int now, int expireTick)
        {
            Id = id;
            Kind = spec.Kind;
            Owner = spec.Owner;
            DefinitionId = spec.DefinitionId;
            CreatedTick = now;
            ExpireTick = expireTick;
            Position = spec.Position;
            Radius = spec.Radius;
            Neutral = spec.Neutral;
            MaxHealth = Math.Max(0f, spec.Health);
            Health = MaxHealth;
            Retirable = !spec.NotRetirable;
            OriginEventId = spec.OriginEventId;
            Behavior = spec.Behavior;
            Active = true;
        }

        public int Id { get; }
        public OwnedEntityKind Kind { get; }
        public int Owner { get; }
        public string DefinitionId { get; }
        public int CreatedTick { get; }
        public int ExpireTick { get; internal set; }
        public Vec3 Position { get; set; }
        public float Radius { get; set; }
        public bool Neutral { get; }
        public float MaxHealth { get; }
        public float Health { get; internal set; }
        public bool Retirable { get; }
        public int OriginEventId { get; }
        public IOwnedEntityBehavior Behavior { get; }
        public bool Active { get; internal set; }

        /// <summary>
        /// Whether a point is inside the entity's area. Ownership grants no immunity (spec L128, L496, L670): callers
        /// test the owner exactly like the opponent.
        /// </summary>
        public bool Contains(Vec3 point) => Vec3.DistanceXZ(point, Position) <= Radius;
    }

    /// <summary>
    /// Player-owned gameplay fields (at most three) and summons (at most two), each living at most 8 s (spec L128,
    /// L421). Creating one at the limit retires the owner's oldest eligible entity of that kind with a dissolution
    /// event. Entities are kept in creation order; ids are sequential.
    /// </summary>
    public sealed class OwnedEntityRegistry
    {
        private readonly List<OwnedEntity> _entities = new List<OwnedEntity>();
        private int _nextId = 1;

        public static int MaxLifetimeTicks => Ticks.FromSeconds(GlobalRules.FieldsAndSummons.MaxLifetimeSec);

        public static int LimitFor(OwnedEntityKind kind)
        {
            return kind == OwnedEntityKind.Field ? GlobalRules.FieldsAndSummons.MaxFieldsPerPlayer : GlobalRules.FieldsAndSummons.MaxSummonsPerPlayer;
        }

        /// <summary>Active entities in creation order.</summary>
        public IReadOnlyList<OwnedEntity> Entities => _entities;

        public int Count(int owner, OwnedEntityKind kind)
        {
            int count = 0;
            for (int i = 0; i < _entities.Count; i++)
            {
                if (_entities[i].Owner == owner && _entities[i].Kind == kind)
                {
                    count++;
                }
            }

            return count;
        }

        public OwnedEntity Find(int id)
        {
            for (int i = 0; i < _entities.Count; i++)
            {
                if (_entities[i].Id == id)
                {
                    return _entities[i];
                }
            }

            return null;
        }

        /// <summary>
        /// Creates an entity. At the owner's limit the oldest eligible entity of the same kind retires first and is
        /// appended to retired. Returns null when the limit is reached and nothing is eligible to retire.
        /// </summary>
        public OwnedEntity Create(in OwnedEntitySpec spec, int now, List<OwnedEntity> retired)
        {
            if (spec.LifetimeTicks <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(spec), "Fields and summons need a positive lifetime.");
            }

            int limit = LimitFor(spec.Kind);
            while (Count(spec.Owner, spec.Kind) >= limit)
            {
                OwnedEntity oldest = OldestEligible(spec.Owner, spec.Kind);
                if (oldest == null)
                {
                    return null;
                }

                Deactivate(oldest);
                retired?.Add(oldest);
            }

            int lifetime = Math.Min(spec.LifetimeTicks, MaxLifetimeTicks);
            var entity = new OwnedEntity(_nextId++, spec, now, now + lifetime);
            _entities.Add(entity);
            return entity;
        }

        /// <summary>Removes entities whose lifetime ended at or before now, appending them in creation order.</summary>
        public void Expire(int now, List<OwnedEntity> expired)
        {
            for (int i = 0; i < _entities.Count;)
            {
                OwnedEntity e = _entities[i];
                if (e.ExpireTick <= now)
                {
                    _entities.RemoveAt(i);
                    e.Active = false;
                    expired?.Add(e);
                    continue;
                }

                i++;
            }
        }

        /// <summary>Damages a summon's body; returns true when it was destroyed.</summary>
        public bool DamageSummon(int id, float amount)
        {
            OwnedEntity e = Find(id);
            if (e == null || e.Kind != OwnedEntityKind.Summon || !(amount > 0f))
            {
                return false;
            }

            e.Health = Math.Max(0f, e.Health - amount);
            if (e.Health > 0f)
            {
                return false;
            }

            Deactivate(e);
            return true;
        }

        public bool Remove(int id)
        {
            OwnedEntity e = Find(id);
            if (e == null)
            {
                return false;
            }

            Deactivate(e);
            return true;
        }

        public void Hash(ref StateHasher h)
        {
            h.Add(_entities.Count);
            for (int i = 0; i < _entities.Count; i++)
            {
                OwnedEntity e = _entities[i];
                h.Add(e.Id);
                h.Add((int)e.Kind);
                h.Add(e.Owner);
                h.Add(e.DefinitionId);
                h.Add(e.ExpireTick);
                h.Add(e.Position);
                h.Add(e.Radius);
                h.Add(e.Health);
            }
        }

        private OwnedEntity OldestEligible(int owner, OwnedEntityKind kind)
        {
            for (int i = 0; i < _entities.Count; i++)
            {
                OwnedEntity e = _entities[i];
                if (e.Owner == owner && e.Kind == kind && e.Retirable)
                {
                    return e; // creation order: the first match is the oldest
                }
            }

            return null;
        }

        private void Deactivate(OwnedEntity e)
        {
            _entities.Remove(e);
            e.Active = false;
        }
    }
}
