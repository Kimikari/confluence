using System;
using Confluence.Core.Arena;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Sim.Duel;
using Confluence.Sim.Hashing;
using Confluence.Sim.World;

namespace Confluence.Sim.Props
{
    /// <summary>
    /// The physical body of one placed prop (spec L171-L173, L662): mass class, durability, the collision box shared
    /// with the arena validators, and its push motion. Catalogue behaviors (PROP-xxx effects) build on this substrate.
    /// </summary>
    public sealed class PropBody
    {
        private readonly int[] _lastHitTick = new int[DuelWorld.PlayerCount];
        private float _durability;

        internal PropBody(PropPlacement placement, PropMass mass, float durability, CollisionSolid solid)
        {
            Id = placement.Id;
            DefinitionId = placement.PropId;
            Mass = mass;
            MaxDurability = Math.Max(0f, durability);
            _durability = MaxDurability;
            Solid = solid;
            Box = solid != null ? solid.Box : placement.CollisionBox;
            Origin = placement.Position;
            for (int i = 0; i < _lastHitTick.Length; i++)
            {
                _lastHitTick[i] = int.MinValue / 2;
            }
        }

        /// <summary>Placement id (layout).</summary>
        public int Id { get; }

        /// <summary>Catalogue id (PROP-xxx).</summary>
        public string DefinitionId { get; }

        public PropMass Mass { get; }

        /// <summary>Mass value of the class: light 10, medium 30, heavy 80; anchored objects have none (spec L173).</summary>
        public float MassValue => Mass == PropMass.Anchored ? float.PositiveInfinity : GlobalRules.Terrain.PropMassValue(Mass);

        /// <summary>Anchored props only shake or take durability damage (spec L173).</summary>
        public bool Anchored => Mass == PropMass.Anchored;

        public float MaxDurability { get; }

        /// <summary>Current durability (the collision solid's when the prop has one).</summary>
        public float Durability => Solid != null ? Solid.Durability : _durability;

        public bool Destroyed { get; private set; }

        /// <summary>The prop's solid in the collision world, or null when its placement has no collision (HasCollision false).</summary>
        public CollisionSolid Solid { get; }

        public bool HasCollision => Solid != null;

        public int SolidId => Solid != null ? Solid.Id : -1;

        /// <summary>Current body box (the placed collision box, moved by pushes).</summary>
        public YawBox Box { get; internal set; }

        /// <summary>Where the layout placed the prop.</summary>
        public Vec3 Origin { get; }

        public Vec3 Position => new Vec3(Box.Center.X, Box.Bottom, Box.Center.Z);

        // ----- motion ---------------------------------------------------------------------------------------

        public bool Moving { get; internal set; }

        /// <summary>Unit ground-plane direction of the current push.</summary>
        public Vec3 Direction { get; internal set; }

        /// <summary>Current speed, at most 8 m/s for a damaging prop (spec L173).</summary>
        public float Speed { get; internal set; }

        /// <summary>Distance still to travel in the current push.</summary>
        public float RemainingDistance { get; internal set; }

        /// <summary>Player whose force set the prop moving (attribution of its collisions), or -1.</summary>
        public int PushOwner { get; internal set; } = -1;

        /// <summary>Server event of the force (origin for audit), or 0.</summary>
        public int PushSourceEventId { get; internal set; }

        public Vec3 Velocity => Moving ? Direction * Speed : Vec3.Zero;

        internal bool CanHit(int player, int now, int intervalTicks) => now - _lastHitTick[player] >= intervalTicks;

        internal void MarkHit(int player, int now) => _lastHitTick[player] = now;

        internal void Stop()
        {
            Moving = false;
            Speed = 0f;
            RemainingDistance = 0f;
        }

        /// <summary>Applies durability damage to a prop without a collision body. Returns true when it broke now.</summary>
        internal bool DamageUncollided(float amount)
        {
            if (Destroyed || Solid != null || !(amount > 0f) || MaxDurability <= 0f)
            {
                return false;
            }

            _durability = Math.Max(0f, _durability - amount);
            if (_durability > 0f)
            {
                return false;
            }

            MarkDestroyed();
            return true;
        }

        internal void MarkDestroyed()
        {
            Destroyed = true;
            Stop();
        }

        internal void Hash(ref StateHasher h)
        {
            h.Add(Id);
            h.Add(Destroyed);
            h.Add(Durability);
            h.Add(Box.Center);
            h.Add(Moving);
            h.Add(Direction);
            h.Add(Speed);
            h.Add(RemainingDistance);
            h.Add(PushOwner);
            for (int i = 0; i < _lastHitTick.Length; i++)
            {
                h.Add(_lastHitTick[i]);
            }
        }
    }
}
