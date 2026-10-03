using System;
using System.Collections.Generic;
using Confluence.Core.Arena;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Reactions.Events;
using Confluence.Sim.Combat;
using Confluence.Sim.Duel;
using Confluence.Sim.Hashing;
using Confluence.Sim.Players;
using Confluence.Sim.World;

namespace Confluence.Sim.Props
{
    /// <summary>Prop motion numbers the spec leaves open (each interprets the cited line; see docs/notes/sim-world.md).</summary>
    public static class PropRules
    {
        /// <summary>
        /// A pushed prop slides on its supporting surface and needs support within the motor's step height under its
        /// center; at an unsupported edge it stops instead of falling (spec L171 "no procedural destruction can ...
        /// drop a player through the map"; decision: props never fall).
        /// </summary>
        public const float SupportProbeDepth = GlobalRules.Movement.StepHeight;

        /// <summary>Vertical overlap ignored when testing a sliding prop against solids, so resting on a floor does not block.</summary>
        public const float RestingTolerance = 0.02f;

        /// <summary>Horizontal overlap ignored against solids (a prop placed flush against a wall can slide away from it).</summary>
        public const float ContactTolerance = 0.005f;
    }

    /// <summary>
    /// Prop bodies of one duel (spec L155, L171-L173, L662): built from the layout's placements with the catalogue's
    /// mass class and durability, their collision boxes registered as dynamic solids, so the motor, melee, sight, and
    /// spells treat them like other cover. Force impulses from the reaction world (already clamped by mass class and
    /// the 8 m/s cap) set loose props sliding; a sliding prop that strikes a player deals its class damage (5/15/25)
    /// above the impact threshold, at most once per second per player, as prop damage that never triggers lifesteal or
    /// item effects. Updated at tick-order step 6.
    /// </summary>
    public sealed class PropSystem
    {
        private readonly DuelWorld _world;
        private readonly List<PropBody> _props = new List<PropBody>();
        private readonly Dictionary<int, PropBody> _byId = new Dictionary<int, PropBody>();
        private readonly Dictionary<int, PropBody> _bySolid = new Dictionary<int, PropBody>();

        /// <summary>
        /// Builds the bodies. <paramref name="definitions"/> resolves a catalogue id to its record; without one a prop is
        /// treated as anchored and indestructible (it still collides when its placement has a collision box).
        /// </summary>
        public PropSystem(DuelWorld world, IReadOnlyList<PropPlacement> placements, Func<string, PropDef> definitions)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            if (placements == null)
            {
                return;
            }

            for (int i = 0; i < placements.Count; i++)
            {
                PropPlacement placement = placements[i];
                if (placement == null || _byId.ContainsKey(placement.Id))
                {
                    continue;
                }

                PropDef def = placement.PropId != null ? definitions?.Invoke(placement.PropId) : null;
                PropMass mass = def != null && def.HasValidMass ? def.Mass : PropMass.Anchored;
                float durability = def != null ? (float)Math.Max(0.0, def.Durability) : 0f;
                CollisionSolid solid = null;
                if (placement.HasCollision && placement.CollisionBox.HalfExtents.X > 0f && placement.CollisionBox.HalfExtents.Z > 0f
                    && placement.CollisionBox.HalfExtents.Y > 0f)
                {
                    int id = world.Collision.AddTemporarySolid(new TemporarySolidSpec
                    {
                        Box = placement.CollisionBox,
                        Kind = SolidKind.Cover,
                        BlocksSight = true,
                        Walkable = true,
                        Durability = durability,
                        OwnerPlayer = -1,
                        SourceId = placement.PropId,
                    }, 0);
                    solid = world.Collision.Find(id);
                }

                var body = new PropBody(placement, mass, durability, solid);
                _props.Add(body);
                _byId.Add(body.Id, body);
                if (solid != null)
                {
                    _bySolid.Add(solid.Id, body);
                }
            }
        }

        /// <summary>Bodies for a layout, with definitions from the catalogue when one is given.</summary>
        public static PropSystem FromLayout(DuelWorld world, ArenaLayout layout, Catalogue catalogue)
        {
            Func<string, PropDef> lookup = null;
            if (catalogue != null)
            {
                lookup = id => catalogue.PropsById.TryGetValue(id, out PropDef def) ? def : null;
            }

            return new PropSystem(world, layout?.Props, lookup);
        }

        /// <summary>Props in layout order.</summary>
        public IReadOnlyList<PropBody> Props => _props;

        public PropBody Find(int propId) => _byId.TryGetValue(propId, out PropBody p) ? p : null;

        public PropBody FindBySolid(int solidId) => _bySolid.TryGetValue(solidId, out PropBody p) ? p : null;

        public bool IsPropSolid(int solidId) => _bySolid.ContainsKey(solidId);

        public static int HitIntervalTicks => Ticks.FromSeconds(GlobalRules.Terrain.PropHitIntervalPerPlayerSec);

        // ----- forces ---------------------------------------------------------------------------------------

        /// <summary>
        /// Applies a clamped impulse from the reaction world (spec L155, L173). Loose props with a body start sliding
        /// along the push; anchored props and props without a collision body only shake. A new push replaces the
        /// current motion.
        /// </summary>
        public void ApplyImpulse(in PropImpulse impulse)
        {
            PropBody prop = Find(impulse.PropId);
            if (prop == null || prop.Destroyed)
            {
                return;
            }

            int owner = impulse.Owner >= 0 && impulse.Owner < DuelWorld.PlayerCount ? impulse.Owner : -1;
            Vec3 dir = new Vec3(impulse.Direction.X, 0f, impulse.Direction.Z).Normalized();
            float speed = Math.Min(impulse.MaxSpeed, GlobalRules.Terrain.DamagingPropVelocityCap);
            float distance = Math.Min(impulse.Distance, GlobalRules.Terrain.PushDistance(prop.Mass));
            if (impulse.ShakeOnly || prop.Anchored || !prop.HasCollision || !(distance > 0f) || !(speed > 0f) || dir.LengthSquared() == 0f)
            {
                Emit(prop, PropChange.Shaken, owner, 0f, -1);
                return;
            }

            prop.Moving = true;
            prop.Direction = dir;
            prop.Speed = speed;
            prop.RemainingDistance = distance;
            prop.PushOwner = owner;
            prop.PushSourceEventId = impulse.SourceEventId;
            Emit(prop, PropChange.Pushed, owner, distance, -1);
        }

        // ----- durability -----------------------------------------------------------------------------------

        /// <summary>Durability damage to a prop (spells, props, melee through its solid). Returns true when it broke.</summary>
        public bool Damage(int propId, float amount, int attacker)
        {
            PropBody prop = Find(propId);
            if (prop == null || prop.Destroyed || _world.Phase != DuelPhase.Combat)
            {
                return false;
            }

            if (prop.HasCollision)
            {
                return _world.DamageSolid(prop.SolidId, amount, attacker); // reports back through OnSolidDamaged
            }

            if (prop.MaxDurability <= 0f || !(amount > 0f))
            {
                return false;
            }

            bool destroyed = prop.DamageUncollided(amount);
            Emit(prop, destroyed ? PropChange.Destroyed : PropChange.Damaged, attacker, amount, -1);
            return destroyed;
        }

        /// <summary>The world damaged a solid; a prop's body reports its own damage and destruction.</summary>
        public void OnSolidDamaged(int solidId, float amount, bool destroyed, int attacker)
        {
            PropBody prop = FindBySolid(solidId);
            if (prop == null || prop.Destroyed)
            {
                return;
            }

            if (destroyed)
            {
                prop.MarkDestroyed();
            }

            Emit(prop, destroyed ? PropChange.Destroyed : PropChange.Damaged, attacker, amount, -1);
        }

        // ----- motion (tick-order step 6) -------------------------------------------------------------------

        public void Step()
        {
            for (int i = 0; i < _props.Count; i++)
            {
                PropBody prop = _props[i];
                if (!prop.Moving)
                {
                    continue;
                }

                if (prop.Destroyed || prop.Solid == null || !prop.Solid.Active)
                {
                    prop.Stop();
                    continue;
                }

                float step = Math.Min(prop.Speed * Ticks.Dt, prop.RemainingDistance);
                YawBox next = prop.Box.Translated(prop.Direction * step);
                if (StrikePlayer(prop, next))
                {
                    continue;
                }

                if (BlockedBySolid(prop, next) || !Supported(prop, next))
                {
                    prop.Stop();
                    Emit(prop, PropChange.Stopped, prop.PushOwner, 0f, -1);
                    continue;
                }

                prop.Box = next;
                _world.Collision.MoveSolid(prop.SolidId, next);
                prop.RemainingDistance -= step;
                if (prop.RemainingDistance <= 1e-4f)
                {
                    prop.Stop();
                    Emit(prop, PropChange.Stopped, prop.PushOwner, 0f, -1);
                }
            }
        }

        // A sliding prop that reaches a player stops against them. Above the impact threshold (relative speed along the
        // push) it deals its mass class damage, at most once per second per player (spec L173). Ownership grants no
        // exemption (L670); prop damage never triggers lifesteal or item cascades (DamageKind.Prop).
        private bool StrikePlayer(PropBody prop, YawBox next)
        {
            for (int p = 0; p < DuelWorld.PlayerCount; p++)
            {
                DuelPlayer player = _world.Player(p);
                if (!player.Alive || !Intersections.CapsuleVsYawBox(player.HitVolume, next, out Penetration pen) || pen.Depth <= 1e-4f)
                {
                    continue;
                }

                int now = _world.Tick;
                Vec3 v = player.Motor.Velocity;
                float relative = prop.Speed - (v.X * prop.Direction.X + v.Z * prop.Direction.Z);
                float damage = 0f;
                if (relative >= GlobalRules.Terrain.PropDamageImpactSpeedMin && prop.CanHit(p, now, HitIntervalTicks))
                {
                    prop.MarkHit(p, now);
                    damage = GlobalRules.Terrain.CollisionDamage(prop.Mass);
                }

                prop.Stop();
                Emit(prop, PropChange.Collided, prop.PushOwner, damage, p);
                if (damage > 0f)
                {
                    DamageRequest hit = DamageRequest.Single(prop.PushOwner, p, DamageKind.Prop, prop.DefinitionId, DamageChannel.Physical, damage)
                        .From(prop.Box.Center);
                    hit.Reaction = ReactionTag.Prop;
                    hit.OriginEventId = prop.PushSourceEventId;
                    _world.ApplyDamage(hit);
                }

                return true;
            }

            return false;
        }

        private bool BlockedBySolid(PropBody prop, YawBox next)
        {
            Aabb bounds = next.Bounds();
            IReadOnlyList<CollisionSolid> solids = _world.Collision.Solids;
            for (int i = 0; i < solids.Count; i++)
            {
                CollisionSolid s = solids[i];
                if (!s.Active || s.Id == prop.SolidId || !Overlaps(bounds, s.Bounds))
                {
                    continue;
                }

                if (BoxesOverlap(next, s.Box))
                {
                    return true;
                }
            }

            return false;
        }

        private bool Supported(PropBody prop, YawBox next)
        {
            float bottom = next.Bottom;
            float x = next.Center.X;
            float z = next.Center.Z;
            IReadOnlyList<CollisionSolid> solids = _world.Collision.Solids;
            for (int i = 0; i < solids.Count; i++)
            {
                CollisionSolid s = solids[i];
                if (!s.Active || s.Id == prop.SolidId)
                {
                    continue;
                }

                float top = s.SurfaceHeightAt(x, z);
                if (!float.IsNaN(top) && top <= bottom + PropRules.RestingTolerance && top >= bottom - PropRules.SupportProbeDepth)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool Overlaps(Aabb a, Aabb b) =>
            a.Min.X <= b.Max.X && b.Min.X <= a.Max.X && a.Min.Y <= b.Max.Y && b.Min.Y <= a.Max.Y && a.Min.Z <= b.Max.Z && b.Min.Z <= a.Max.Z;

        // Two yaw boxes overlap when their height intervals overlap beyond the resting tolerance and their footprints
        // overlap on every separating axis of the two rectangles.
        private static bool BoxesOverlap(YawBox a, YawBox b)
        {
            if (a.Bottom >= b.Top - PropRules.RestingTolerance || b.Bottom >= a.Top - PropRules.RestingTolerance)
            {
                return false;
            }

            Vec3[] axes = { a.Right, a.Forward, b.Right, b.Forward };
            for (int i = 0; i < axes.Length; i++)
            {
                Vec3 axis = axes[i];
                float ra = a.HalfExtents.X * Math.Abs(Dot(a.Right, axis)) + a.HalfExtents.Z * Math.Abs(Dot(a.Forward, axis));
                float rb = b.HalfExtents.X * Math.Abs(Dot(b.Right, axis)) + b.HalfExtents.Z * Math.Abs(Dot(b.Forward, axis));
                float distance = Math.Abs(Dot(b.Center - a.Center, axis));
                if (distance >= ra + rb - PropRules.ContactTolerance)
                {
                    return false;
                }
            }

            return true;
        }

        private static float Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Z * b.Z;

        private void Emit(PropBody prop, PropChange change, int player, float amount, int target)
        {
            _world.RecordEvent(new PropEvent
            {
                Change = change,
                PropId = prop.Id,
                DefinitionId = prop.DefinitionId,
                Position = prop.Position,
                Amount = amount,
                TargetPlayer = target,
                DurabilityAfter = prop.Durability,
            }, player);
        }

        public void Hash(ref StateHasher h)
        {
            h.Add(_props.Count);
            for (int i = 0; i < _props.Count; i++)
            {
                _props[i].Hash(ref h);
            }
        }
    }
}
