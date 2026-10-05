using System;
using System.Collections.Generic;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Reactions.Grid;
using Confluence.Sim.Combat;
using Confluence.Sim.Duel;
using Confluence.Sim.Players;
using Confluence.Sim.Status;
using Confluence.Sim.World;

namespace Confluence.Sim.Spells
{
    /// <summary>How one payload application reaches its target.</summary>
    public struct PayloadHit
    {
        /// <summary>Direct (impact, ray, cone, strike), Splash (area around an impact), or Area (field and trail contact).</summary>
        public CastHitKinds Kind;

        /// <summary>Where the hit comes from (guard arc, push origin, splash center).</summary>
        public Vec3 Source;

        /// <summary>Travel or facing direction of the delivery (pushes "along" it).</summary>
        public Vec3 Direction;

        /// <summary>Fraction of the payload's damage (reflections keep 70%, spec L527); 1 by default.</summary>
        public float Scale;

        /// <summary>The hit struck the stated weak point of a precision-tagged spell (spec L110).</summary>
        public bool WeakPoint;

        /// <summary>
        /// New secondary damage a transformation adds to its source (a recast swing or burst of a planted object, spec
        /// L508): submitted as generation 1, so it cannot trigger on-hit, resource, lifesteal or other proc rewards
        /// (spec L126). It keeps the cast's identity and source for attribution.
        /// </summary>
        public bool Secondary;

        /// <summary>
        /// Contact with heated or reacting terrain the spell left in the world (a neutral hot strip, a red-hot prop):
        /// submitted as <see cref="DamageKind.Environment"/> with the cast's source and original event for attribution,
        /// so it grants no direct spell-hit healing, refunds or equipment chains (spec L672).
        /// </summary>
        public bool Environmental;

        /// <summary>Reaction family of an <see cref="Environmental"/> hit (feat attribution).</summary>
        public ReactionTag Reaction;

        /// <summary>
        /// The hit is not projectile damage even though the form's delivery is a projectile (a planted cinder's burst):
        /// projectile-only reductions such as SPL-011's do not apply to it.
        /// </summary>
        public bool NotProjectile;

        public static PayloadHit Direct(Vec3 source, Vec3 direction) =>
            new PayloadHit { Kind = CastHitKinds.Direct, Source = source, Direction = direction, Scale = 1f };

        public static PayloadHit Splash(Vec3 center) => new PayloadHit { Kind = CastHitKinds.Splash, Source = center, Scale = 1f };

        public static PayloadHit Area(Vec3 source, Vec3 direction) =>
            new PayloadHit { Kind = CastHitKinds.Area, Source = source, Direction = direction, Scale = 1f };
    }

    /// <summary>
    /// What deliveries and entities may read and do (tick-order steps 3-4). Every effect goes through the public
    /// DuelWorld APIs, so damage order, guard, caps, budgets, statuses, ownership limits, and events stay central;
    /// world deposits go through the <see cref="IWorldMaterials"/> seam.
    /// </summary>
    public sealed class SpellContext
    {
        private readonly SpellRuntime _runtime;
        private readonly List<int> _overlap = new List<int>();

        internal SpellContext(DuelWorld world, SpellRuntime runtime)
        {
            World = world;
            _runtime = runtime;
        }

        public DuelWorld World { get; }
        public int Tick => World.Tick;
        public CollisionWorld Collision => World.Collision;

        public DuelPlayer Player(int index) => World.Player(index);

        // ----- aim ------------------------------------------------------------------------------------------

        /// <summary>Aim origin: the caster's eye, so deliveries follow the crosshair (spec L102: no snap or tracking).</summary>
        public Vec3 AimOrigin(int player) => World.Player(player).Eye;

        /// <summary>The caster's current view direction.</summary>
        public Vec3 AimDirection(int player)
        {
            DuelPlayer p = World.Player(player);
            return CMath.ForwardFromYawPitch(p.Motor.YawDeg, p.Motor.PitchDeg);
        }

        /// <summary>Origin of close deliveries (cones, strikes): the focus hand, below the eye like common melee.</summary>
        public Vec3 FocusOrigin(int player) => World.Player(player).Eye - new Vec3(0f, SpellRules.CastOriginDropBelowEye, 0f);

        /// <summary>First solid point along the aim within range, or the range end.</summary>
        public Vec3 AimPoint(int player, float range)
        {
            Vec3 origin = AimOrigin(player);
            Vec3 dir = AimDirection(player);
            if (range <= 0f) return origin;
            return Collision.Raycast(origin, dir, range, SolidMask.Movement, out WorldHit hit) ? hit.Point : origin + dir * range;
        }

        /// <summary>
        /// A ground-targeted point along the aim within range (placed fields, markers, teleports): the aim point dropped
        /// onto the first walkable surface below it. Falls back to the caster's feet height when nothing is below.
        /// </summary>
        public Vec3 GroundPoint(int player, float range)
        {
            Vec3 aim = AimPoint(player, range);
            Vec3 dir = AimDirection(player);
            Vec3 probe = aim - dir * 0.05f + new Vec3(0f, 0.05f, 0f);
            if (Collision.Raycast(probe, Vec3.Down, 30f, SolidMask.Movement, out WorldHit ground))
            {
                return ground.Point;
            }

            return new Vec3(aim.X, World.Player(player).Position.Y, aim.Z);
        }

        // ----- targets --------------------------------------------------------------------------------------

        /// <summary>
        /// A player's hit volume for a hit by <paramref name="attacker"/>: hitscan and strikes rewind it to the tick the
        /// attacker was viewing, at most 150 ms (spec L423); projectiles, fields, and waves use the present volume.
        /// </summary>
        public Capsule TargetVolume(int attacker, int target, bool lagCompensated)
        {
            DuelPlayer t = World.Player(target);
            if (lagCompensated && attacker >= 0 && attacker < DuelWorld.PlayerCount && attacker != target)
            {
                int viewTick = World.Player(attacker).Input.ViewTick;
                if (viewTick > 0 && viewTick < World.Tick && World.History.TryGet(target, viewTick, out Capsule rewound, out _))
                {
                    return rewound;
                }
            }

            return t.HitVolume;
        }

        /// <summary>Whether a player can be struck at all (alive, not mid-recall).</summary>
        public bool IsTargetable(int player)
        {
            DuelPlayer p = World.Player(player);
            return p.Alive && !p.IsRecalling;
        }

        /// <summary>No movement-blocking solid lies on the segment.</summary>
        public bool HasClearPath(Vec3 from, Vec3 to) => !Collision.SegmentCast(from, to, SolidMask.Movement, out _);

        /// <summary>
        /// Per-target cover (SPL-010 "check cover per target"): the target counts as covered from a point when solid
        /// geometry blocks both the line to its chest and the line to its head.
        /// </summary>
        public bool IsCovered(Vec3 from, Capsule body)
        {
            float bottom = body.A.Y - body.Radius;
            float top = body.B.Y + body.Radius;
            Vec3 axis = body.Center;
            Vec3 chest = new Vec3(axis.X, bottom + (top - bottom) * SpellRules.CoverChestFraction, axis.Z);
            Vec3 head = new Vec3(axis.X, top - body.Radius, axis.Z);
            return !HasClearPath(from, chest) && !HasClearPath(from, head);
        }

        /// <summary>Distance from a point to the surface of a capsule (negative inside).</summary>
        public static float DistanceToBody(Vec3 point, Capsule body) => Vec3.Distance(point, body.ClosestPointOnAxis(point)) - body.Radius;

        /// <summary>Live hostile spell bodies (summons, anchors) the attacker's deliveries can strike, in creation order.</summary>
        public IReadOnlyList<ISpellBody> HostileBodies(int attacker) => _runtime.BodiesHostileTo(attacker);

        /// <summary>Live spell entities in creation order.</summary>
        public IReadOnlyList<SpellEntity> Entities => _runtime.Entities;

        /// <summary>Ray reflection hooks (one reflection per ray, spec L527).</summary>
        public IReadOnlyList<IRayReflector> Reflectors => _runtime.Reflectors;

        // ----- entities and events --------------------------------------------------------------------------

        /// <summary>Adds a delivery entity; it first steps in this tick's step 4 when spawned at release.</summary>
        public T Spawn<T>(T entity) where T : SpellEntity => _runtime.AddEntity(entity);

        public SpellEvent Emit(SpellEventKind kind, CastRecord cast, Vec3 position, Vec3 direction = default, int entityId = 0,
            float amount = 0f, int targetPlayer = -1, int solidId = -1, int ticks = 0)
        {
            return World.RecordEvent(new SpellEvent
            {
                Kind = kind,
                SpellId = cast.SpellId,
                CastId = cast.CastId,
                EntityId = entityId,
                Position = position,
                Direction = direction,
                Amount = amount,
                TargetPlayer = targetPlayer,
                SolidId = solidId,
                Ticks = ticks,
            }, cast.Caster);
        }

        // ----- payloads -------------------------------------------------------------------------------------

        /// <summary>
        /// Applies one payload to a player through the damage pipeline (spec L112): direct, splash, or area damage with
        /// the cast's identity, original event, and single mastery result; direct/splash exclusivity and "max N per
        /// target" caps (spec L525); push or pull with its guard-reduced distance (spec L104, L173); statuses (spec
        /// L116-L120). Returns the damage event, or null when nothing was dealt.
        /// </summary>
        public DamageEvent ApplyToPlayer(CastRecord cast, PayloadSpec payload, int target, in PayloadHit hit)
        {
            if (cast == null) throw new ArgumentNullException(nameof(cast));
            if (payload == null || !IsTargetable(target)) return null;

            CastHitKinds previous = cast.HitKindsOn(target);
            if (payload.SplashExcludesDirect)
            {
                // "direct and splash cannot both damage one target" (spec L525).
                if (hit.Kind == CastHitKinds.Splash && (previous & CastHitKinds.Direct) != 0) return null;
                if (hit.Kind == CastHitKinds.Direct && (previous & CastHitKinds.Splash) != 0) return null;
            }

            float scale = hit.Scale > 0f ? hit.Scale : 1f;
            float amount = DamageFor(cast, payload, hit.Kind) * scale;
            if (payload.MaxPerTarget > 0f)
            {
                amount = Math.Min(amount, Math.Max(0f, payload.MaxPerTarget - cast.RequestedOn(target)));
            }

            DuelPlayer victim = World.Player(target);
            Vec3 push = PushDirectionFor(cast, payload, victim, hit);
            DamageEvent ev = null;
            if (amount > 0f)
            {
                DamageKind kind = hit.Environmental ? DamageKind.Environment : hit.Kind == CastHitKinds.Direct ? DamageKind.Direct : DamageKind.Splash;
                DamageRequest request = payload.SplitFraction > 0f
                    ? DamageRequest.Split(cast.Caster, target, kind, cast.SpellId,
                        new DamagePart(payload.Channel, amount * (1f - payload.SplitFraction)), new DamagePart(payload.SplitChannel, amount * payload.SplitFraction))
                    : DamageRequest.Single(cast.Caster, target, kind, cast.SpellId, payload.Channel, amount);
                request.From(hit.Source);
                request.GuardEligible = hit.Kind == CastHitKinds.Direct && !hit.Environmental && payload.GuardEligible;
                request.CastId = cast.CastId;
                request.OriginEventId = cast.OriginEventId;
                if (hit.Secondary) request.Generation = 1; // spec L126, L508: one generation of secondary effects
                if (hit.Environmental) request.Reaction = hit.Reaction;
                request.Range = Vec3.Distance(World.Player(cast.Caster).Position, victim.Position);
                request.PrecisionTagged = hit.WeakPoint && cast.Form.HasTag("precision") && payload.PrecisionMultiplier > 1f;
                request.PrecisionMultiplier = payload.PrecisionMultiplier;
                request.ExtraPassiveReduction = cast.Form.Delivery.Kind == DeliveryKind.Projectile && !hit.NotProjectile && !hit.Environmental
                    ? _runtime.ProjectileReductionFor(target)
                    : 0f;
                if (payload.Push > 0f && push.LengthSquared() > 0f)
                {
                    request.Knockback = payload.Push;
                    request.KnockbackDirection = push;
                }

                cast.Record(target, hit.Kind, amount);
                ev = World.ApplyDamage(request);
            }
            else if (payload.IsDamaging)
            {
                return null; // a damaging payload at its per-target cap does nothing more
            }
            else if (payload.Push > 0f && push.LengthSquared() > 0f)
            {
                cast.Record(target, hit.Kind, 0f);
                World.ApplyKnockback(target, cast.Caster, push, payload.Push, cast.SpellId);
            }
            else
            {
                cast.Record(target, hit.Kind, 0f);
            }

            ApplyStatuses(cast, payload, target, hit.Kind);
            if (ev != null && cast.Form.Script != null) cast.Form.Script.OnPlayerHit(this, cast, target, ev);
            return ev;
        }

        /// <summary>Durability damage to a hostile spell body (summons and anchors are destructible, spec L527).</summary>
        public bool ApplyToBody(CastRecord cast, PayloadSpec payload, ISpellBody body, CastHitKinds kind, float scale = 1f)
        {
            if (payload == null || body == null || !body.IsIntact) return false;
            float amount = DamageFor(cast, payload, kind) * (scale > 0f ? scale : 1f);
            return amount > 0f && body.TakeDamage(this, amount, cast.Caster);
        }

        /// <summary>
        /// Splash around an impact (spec L525): every player whose body is within the radius, except the caster unless the
        /// payload says otherwise, and except a directly hit target when the record makes them exclusive. Solid cover
        /// between the impact and the body stops it unless the payload ignores cover.
        /// </summary>
        public void ApplySplash(CastRecord cast, PayloadSpec payload, Vec3 center, Vec3 surfaceNormal)
        {
            if (payload == null || !(payload.Splash > 0f) || !(payload.SplashRadius > 0f)) return;
            Vec3 from = center + surfaceNormal * 0.05f;
            for (int i = 0; i < DuelWorld.PlayerCount; i++)
            {
                if ((i == cast.Caster && !payload.SplashHitsCaster) || !IsTargetable(i)) continue;
                Capsule body = World.Player(i).HitVolume;
                if (DistanceToBody(center, body) > payload.SplashRadius) continue;
                if (payload.SplashBlockedByCover && !HasClearPath(from, body.ClosestPointOnAxis(center))) continue;
                ApplyToPlayer(cast, payload, i, PayloadHit.Splash(center));
            }

            IReadOnlyList<ISpellBody> bodies = HostileBodies(cast.Caster);
            for (int i = 0; i < bodies.Count; i++)
            {
                ISpellBody b = bodies[i];
                if (b.IsIntact && Vec3.Distance(center, b.BodyCenter) - b.BodyRadius <= payload.SplashRadius)
                {
                    ApplyToBody(cast, payload, b, CastHitKinds.Splash);
                }
            }
        }

        /// <summary>
        /// Emits the payload's world deposits declared for a placement through the materials seam (spec L136, L163):
        /// element, units per cell, footprint, owner, and the cast's original event.
        /// </summary>
        public void EmitDeposits(CastRecord cast, PayloadSpec payload, DepositPlacement placement, Vec3 point, Vec3 direction,
            Footprint? footprint = null, int fieldId = 0, int lifetimeTicks = -1)
        {
            if (payload == null) return;
            for (int i = 0; i < payload.Deposits.Count; i++)
            {
                DepositSpec d = payload.Deposits[i];
                if (d.Placement != placement || d.Strength <= 0) continue;
                var deposit = new WorldDeposit
                {
                    Kind = d.Kind,
                    Strength = d.Strength,
                    Position = point,
                    Radius = d.Radius,
                    SourcePlayer = cast.Caster,
                    SourceId = cast.SpellId,
                    OriginEventId = cast.OriginEventId,
                    LifetimeTicks = lifetimeTicks >= 0 ? lifetimeTicks : Ticks.FromSeconds(d.LifetimeSec),
                    FieldId = fieldId,
                };

                Vec2 flat = new Vec2(direction.X, direction.Z);
                deposit.Direction = flat.LengthSquared() > 0f ? flat.Normalized() : Vec2.Zero;
                if (d.HeavyImpact && placement == DepositPlacement.Impact)
                {
                    // Spec L163: the stated units in the impact cell, the default ring in the other cells within 1 m.
                    deposit.AdjacentStrength = GlobalRules.World.HeavyProjectileAdjacentDeposit;
                    deposit.AdjacentRadius = GlobalRules.World.HeavyProjectileAdjacentRadius;
                    deposit.Footprint = Footprint.Circle(point.XZ(), deposit.AdjacentRadius);
                    deposit.HasFootprint = true;
                }
                else if (footprint.HasValue)
                {
                    deposit.Footprint = footprint.Value;
                    deposit.HasFootprint = true;
                }
                else if (d.Radius > 0f)
                {
                    deposit.Footprint = Footprint.Circle(point.XZ(), d.Radius);
                    deposit.HasFootprint = true;
                }
                else
                {
                    deposit.Footprint = Footprint.Point(point.XZ());
                    deposit.HasFootprint = true;
                }

                World.QueueDeposit(deposit);
            }
        }

        /// <summary>
        /// Reports a delivery's direct impact on a solid or the ground to the reactions stage (spec L145-L147, L153):
        /// struck ice and brittle cover wear or shatter by the impact's direct damage, and combustible solids receive the
        /// impact heat. Bodies and players are not reaction solids.
        /// </summary>
        public void ReportImpact(CastRecord cast, PayloadSpec payload, Vec3 point, Vec3 direction, int solidId, float scale = 1f)
        {
            if (payload == null) return;
            int heat = 0;
            for (int i = 0; i < payload.Deposits.Count; i++)
            {
                DepositSpec d = payload.Deposits[i];
                if (d.Kind == DepositKind.Heat && d.Placement == DepositPlacement.Impact) heat = Math.Max(heat, d.Strength);
            }

            World.Materials.QueueImpact(new WorldImpact
            {
                Point = point,
                Direction = direction,
                DirectDamage = DamageFor(cast, payload, CastHitKinds.Direct) * scale,
                SolidId = solidId,
                HeatStrength = heat,
                SourcePlayer = cast.Caster,
                SourceId = cast.SpellId,
                OriginEventId = cast.OriginEventId,
            });
        }

        /// <summary>Durability damage to a destructible solid struck by a direct impact (spec L171).</summary>
        public void DamageSolid(CastRecord cast, PayloadSpec payload, int solidId)
        {
            if (payload == null || solidId < 0) return;
            CollisionSolid solid = Collision.Find(solidId);
            if (solid != null && solid.Destructible && payload.SolidDamage > 0f)
            {
                float amount = World.Effects.AdjustSolidDamage(cast.Caster, solidId, payload.SolidDamage * cast.DirectDamageMultiplier, cast.SpellId, cast.CastId);
                World.DamageSolid(solidId, amount, cast.Caster);
            }
        }

        private float DamageFor(CastRecord cast, PayloadSpec payload, CastHitKinds kind)
        {
            switch (kind)
            {
                case CastHitKinds.Direct:
                    // Mastery applies to the primary payload's direct damage only (spec L480).
                    return payload.Direct * (ReferenceEquals(payload, cast.Form.Payload) ? cast.DirectDamageMultiplier : 1f);
                case CastHitKinds.Splash:
                    return payload.Splash;
                default:
                    return payload.Direct;
            }
        }

        private Vec3 PushDirectionFor(CastRecord cast, PayloadSpec payload, DuelPlayer victim, in PayloadHit hit)
        {
            if (!(payload.Push > 0f)) return Vec3.Zero;
            Vec3 dir;
            switch (payload.PushDirection)
            {
                case PushDirection.AlongDelivery:
                    dir = hit.Direction;
                    break;
                case PushDirection.TowardCaster:
                    dir = World.Player(cast.Caster).Position - victim.Position;
                    break;
                default:
                    dir = victim.Position - hit.Source;
                    if (new Vec3(dir.X, 0f, dir.Z).LengthSquared() < 1e-6f) dir = hit.Direction;
                    break;
            }

            dir = new Vec3(dir.X, 0f, dir.Z);
            return dir.LengthSquared() > 1e-8f ? dir.Normalized() : Vec3.Zero;
        }

        private void ApplyStatuses(CastRecord cast, PayloadSpec payload, int target, CastHitKinds kind)
        {
            for (int i = 0; i < payload.Statuses.Count; i++)
            {
                StatusSpec s = payload.Statuses[i];
                if (s.Trigger == StatusTrigger.DirectHitOnly && kind != CastHitKinds.Direct) continue;
                StatusApplication app = StatusApplication.Of(s.Kind, cast.SpellId, cast.Caster, s.DurationTicks, s.Intensity);
                app.Channel = s.Channel;
                app.OriginEventId = cast.OriginEventId;
                World.ApplyStatus(target, app);
            }
        }
    }
}
