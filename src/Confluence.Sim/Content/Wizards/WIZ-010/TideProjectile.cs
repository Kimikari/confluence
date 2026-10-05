using System;
using System.Collections.Generic;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Reactions;
using Confluence.Reactions.Volumes;
using Confluence.Sim.Duel;
using Confluence.Sim.Hashing;
using Confluence.Sim.Spells;
using Confluence.Sim.World;

namespace Confluence.Sim.Content.Wizards.HalenTide
{
    /// <summary>
    /// Halen's water projectiles (SPL-073 Pressure Jet, SPL-079 Deepglass Prison and their transformations). Like the
    /// framework projectile (spec L527: speed, footprint, lifetime and collision behavior come from the effect text) it
    /// flies from the eye along the aim, stops at the first solid, player, or hostile spell body, applies the payload's
    /// direct hit (guardable, spec L104) and is never lag-compensated (spec L423). In addition:
    /// <list type="bullet">
    /// <item>Its impact water lands on the ground below the impact (the record's "wets the first impact surface" / "Water 2
    /// at impact"), so the wet cell is the surface the impact touched (spec L136, L163).</item>
    /// <item>AUG-433: it may bank once from a wall whose base cell holds liquid water, consuming that water, with its own
    /// bank payload; a banked shot is still stopped by solids and acquires nothing it does not touch (spec L512).</item>
    /// <item>AUG-434: a shot that strikes a solid without hitting a player leaves a mist cloud at the impact.</item>
    /// <item>AUG-445: it can roll along the ground instead of flying, climbing at most the step height and stopped by
    /// obstacles; bodies whose feet are above its top (a jump) are not touched.</item>
    /// </list>
    /// </summary>
    public sealed class TideProjectileDelivery : DeliverySpec
    {
        /// <summary>Rolling bubbles ride this far above the ground (decision: contact without scraping).</summary>
        public const float RollClearance = 0.02f;

        /// <summary>A rolling bubble follows a drop of at most this much; beyond it, it has left the ground and ends.</summary>
        public const float RollMaxDrop = 3f;

        private float _speed;
        private float _radius = SpellRules.DefaultProjectileRadius;
        private float _maxRange;
        private bool _rolling;
        private bool _banksOffWetWalls;
        private PayloadSpec _bankPayload;
        private int _bankWaterCost = 1;
        private float _missCloudRadius;
        private float _missCloudLifetimeSec;

        public override DeliveryKind Kind => DeliveryKind.Projectile;

        public float Speed { get => _speed; set => _speed = Set(value); }

        public float Radius { get => _radius; set => _radius = Set(value); }

        /// <summary>Maximum travel (the record's range, spec L525).</summary>
        public float MaxRange { get => _maxRange; set => _maxRange = Set(value); }

        /// <summary>AUG-445 "Bubble rolls along ground ... instead of flying".</summary>
        public bool Rolling { get => _rolling; set => _rolling = Set(value); }

        /// <summary>AUG-433 "Jet may bank once from a Wet wall".</summary>
        public bool BanksOffWetWalls { get => _banksOffWetWalls; set => _banksOffWetWalls = Set(value); }

        /// <summary>What a banked shot applies (AUG-433 "at 24 damage"); null keeps the primary payload.</summary>
        public PayloadSpec BankPayload { get => _bankPayload; set => _bankPayload = Set(value); }

        /// <summary>Water consumed from the wall by a bank (AUG-433 "consumes Water 1 on wall").</summary>
        public int BankWaterCost { get => _bankWaterCost; set => _bankWaterCost = Set(value); }

        /// <summary>AUG-434 mist cloud radius at a missed shot's impact; 0 = none.</summary>
        public float MissCloudRadius { get => _missCloudRadius; set => _missCloudRadius = Set(value); }

        public float MissCloudLifetimeSec { get => _missCloudLifetimeSec; set => _missCloudLifetimeSec = Set(value); }

        internal override void ApplyGlobalCaps()
        {
            _radius = Math.Max(0.01f, _radius);
            if (!(_speed > 0f)) throw new InvalidOperationException("A projectile needs a positive speed (spec L527).");
            if (!(_maxRange > 0f)) throw new InvalidOperationException("A projectile needs a range (spec L525, L527).");
            _missCloudLifetimeSec = Math.Min(Math.Max(0f, _missCloudLifetimeSec), GlobalRules.FieldsAndSummons.MaxLifetimeSec); // spec L128
            _bankWaterCost = Math.Max(0, _bankWaterCost);
            CapNested(_bankPayload);
        }

        protected override void OnFreeze() => _bankPayload?.Freeze();

        public override Vec3 PredictTarget(SpellContext ctx, SpellForm form, int caster)
        {
            if (!Rolling) return ctx.AimPoint(caster, MaxRange);
            Vec3 flat = FlatAim(ctx, caster, ctx.AimDirection(caster));
            return HalenRules.Ground(ctx.Collision, ctx.Player(caster).Position + flat * MaxRange + new Vec3(0f, 1f, 0f));
        }

        public override void Release(SpellContext ctx, CastRecord cast)
        {
            Vec3 aim = AimDirection(ctx, cast);
            float speed = Speed * cast.ProjectileSpeedMultiplier; // a projectile-speed mastery reward (spec L480)
            Vec3 origin;
            Vec3 dir;
            if (Rolling)
            {
                dir = FlatAim(ctx, cast.Caster, aim);
                Vec3 feet = ctx.Player(cast.Caster).Position;
                Vec3 start = feet + dir * (GlobalRules.Movement.CapsuleRadius + Radius + 0.05f);
                origin = new Vec3(start.X, HalenRules.GroundY(ctx.Collision, start + new Vec3(0f, GlobalRules.Movement.StepHeight, 0f)) + Radius + RollClearance, start.Z);
            }
            else
            {
                origin = ctx.AimOrigin(cast.Caster);
                dir = aim;
            }

            TideProjectileEntity p = ctx.Spawn(new TideProjectileEntity(cast, this, origin, dir, speed));
            ctx.Emit(SpellEventKind.ProjectileLaunched, cast, origin, dir, p.Id, speed);
        }

        private static Vec3 FlatAim(SpellContext ctx, int caster, Vec3 aim) =>
            new Vec3(aim.X, 0f, aim.Z).NormalizedOr(CMath.ForwardFromYaw(ctx.Player(caster).Motor.YawDeg));
    }

    /// <summary>A Halen projectile in flight or rolling (see <see cref="TideProjectileDelivery"/>).</summary>
    public sealed class TideProjectileEntity : SpellEntity
    {
        private readonly TideProjectileDelivery _spec;
        private PayloadSpec _payload;
        private float _travelled;
        private bool _banked;

        internal TideProjectileEntity(CastRecord cast, TideProjectileDelivery spec, Vec3 position, Vec3 direction, float speed)
            : base(cast)
        {
            _spec = spec;
            _payload = cast.Form.Payload;
            Position = position;
            Direction = direction.Normalized();
            Speed = speed;
        }

        public TideProjectileDelivery Spec => _spec;
        public Vec3 Position { get; private set; }
        public Vec3 Direction { get; private set; }
        public float Speed { get; }
        public float Travelled => _travelled;
        public bool Banked => _banked;

        protected internal override void Step(SpellContext ctx)
        {
            float length = Speed * Ticks.Dt;
            bool last = false;
            if (_travelled + length >= _spec.MaxRange)
            {
                length = Math.Max(0f, _spec.MaxRange - _travelled);
                last = true;
            }

            if (_spec.Rolling) StepRolling(ctx, length, last);
            else StepFlying(ctx, length, last);
        }

        private void StepFlying(SpellContext ctx, float length, bool last)
        {
            SweepContact hit = SweepQuery.First(ctx, Owner, Position, Direction, length, _spec.Radius, _spec.Radius, false, true);
            if (hit.Found)
            {
                Position = hit.Point;
                _travelled += hit.Distance;
                if (hit.IsSolid && TryBank(ctx, hit)) return;
                Impact(ctx, hit);
                return;
            }

            Position += Direction * length;
            _travelled += length;
            if (last) Expire(ctx);
        }

        // AUG-445: ride the ground; rising ground above the step height or a wall stops the bubble.
        private void StepRolling(SpellContext ctx, float length, bool last)
        {
            float r = _spec.Radius;
            float groundHere = Position.Y - r - TideProjectileDelivery.RollClearance;
            Vec3 ahead = Position + Direction * length;
            float probeTop = groundHere + GlobalRules.Movement.StepHeight + r;
            float groundNext;
            if (!ctx.Collision.Raycast(new Vec3(ahead.X, probeTop, ahead.Z), Vec3.Down, GlobalRules.Movement.StepHeight + r + TideProjectileDelivery.RollMaxDrop,
                    SolidMask.Movement, out WorldHit floor))
            {
                // Rolled off an edge: no ground to follow within the drop.
                Position = ahead;
                _travelled += length;
                Expire(ctx);
                return;
            }

            groundNext = floor.Point.Y;
            Vec3 target = new Vec3(ahead.X, groundNext + r + TideProjectileDelivery.RollClearance, ahead.Z);
            Vec3 seg = target - Position;
            float segLength = seg.Length();
            if (segLength < 1e-6f)
            {
                if (last) Expire(ctx);
                return;
            }

            Vec3 segDir = seg / segLength;
            var best = new SweepContact { Player = -1, SolidId = -1, Distance = float.PositiveInfinity };
            // The wall check runs level at the higher of the two heights: a rise within the step height is stepped onto
            // (lifted first, like a body's step-up, spec L80), a drop is rolled off; only a face above the step stops it.
            float sweepY = Math.Max(Position.Y, target.Y);
            Vec3 sweepFrom = new Vec3(Position.X, sweepY, Position.Z);
            Vec3 sweepTo = new Vec3(target.X, sweepY, target.Z);
            if (ctx.Collision.SphereSweep(sweepFrom, sweepTo, r * 0.8f, SolidMask.Movement, out WorldHit wall) && wall.Normal.Y < 0.7f &&
                Vec3.Dot(new Vec3(wall.Normal.X, 0f, wall.Normal.Z), Direction) < 0f) // a face it is leaving (a step down) never stops it
            {
                best = new SweepContact { Found = true, Distance = wall.Distance, Player = -1, SolidId = wall.SolidId, Point = Position + segDir * wall.Distance, Normal = wall.Normal };
            }

            for (int i = 0; i < DuelWorld.PlayerCount; i++)
            {
                if (i == Owner || !ctx.IsTargetable(i)) continue;
                Capsule body = ctx.Player(i).HitVolume; // never lag-compensated (spec L423)
                if (Intersections.RayCapsule(Position, segDir, body.A, body.B, body.Radius + r, segLength, out float t) && t < best.Distance)
                {
                    best = new SweepContact { Found = true, Distance = t, Player = i, SolidId = -1, Point = Position + segDir * t };
                }
            }

            IReadOnlyList<ISpellBody> bodies = ctx.HostileBodies(Owner);
            for (int i = 0; i < bodies.Count; i++)
            {
                ISpellBody b = bodies[i];
                if (Intersections.RaySphere(Position, segDir, b.BodyCenter, b.BodyRadius + r, segLength, out float t) && t < best.Distance)
                {
                    best = new SweepContact { Found = true, Distance = t, Player = -1, SolidId = -1, Body = b, Point = Position + segDir * t };
                }
            }

            if (best.Found)
            {
                Position = best.Point;
                _travelled += best.Distance * (length / segLength);
                Impact(ctx, best);
                return;
            }

            Position = target;
            _travelled += length;
            if (last) Expire(ctx);
        }

        // AUG-433: one bank from a wall standing in liquid water; the wall's water is consumed.
        private bool TryBank(SpellContext ctx, in SweepContact hit)
        {
            if (!_spec.BanksOffWetWalls || _banked || hit.Normal.Y >= 0.7f || hit.Normal.LengthSquared() < 1e-6f) return false;
            if (!WetWall(ctx, hit.Point, hit.Normal, out Vec3 wet)) return false;
            _banked = true;
            HalenRules.RemoveWater(ctx, Cast, wet, _spec.BankWaterCost);
            Vec3 n = hit.Normal;
            Direction = (Direction - n * (2f * Vec3.Dot(Direction, n))).Normalized();
            Position = hit.Point + n * 0.01f;
            if (_spec.BankPayload != null) _payload = _spec.BankPayload;
            ctx.Emit(SpellEventKind.ProjectileBounced, Cast, Position, Direction, Id, solidId: hit.SolidId);
            HalenRules.Emit(ctx, TideEventKind.WetWallBank, Cast, wet);
            return true;
        }

        /// <summary>
        /// A wall is Wet when the ground cell at its struck face holds liquid water (cells are 2D, spec L136; the wall's
        /// base is the cell under the contact or the cell just in front of the face).
        /// </summary>
        private static bool WetWall(SpellContext ctx, Vec3 contact, Vec3 normal, out Vec3 wet)
        {
            wet = contact;
            ReactionWorld world = HalenRules.Reactions(ctx);
            if (world == null) return false;
            Vec3 flatNormal = new Vec3(normal.X, 0f, normal.Z).NormalizedOr(Vec3.Zero);
            Vec3 a = contact;
            Vec3 b = contact + flatNormal * (GlobalRules.World.CellSize * 0.6f);
            if (world.IsWetAt(a))
            {
                wet = a;
                return true;
            }

            if (world.IsWetAt(b))
            {
                wet = b;
                return true;
            }

            return false;
        }

        private void Impact(SpellContext ctx, in SweepContact hit)
        {
            Vec3 point = hit.Point;
            Vec3 normal = hit.IsSolid ? hit.Normal : -Direction;
            if (hit.IsPlayer)
            {
                ctx.Emit(SpellEventKind.ProjectileImpact, Cast, point, Direction, Id, targetPlayer: hit.Player);
                ctx.ApplyToPlayer(Cast, _payload, hit.Player, PayloadHit.Direct(point - Direction * _spec.Radius, Direction));
            }
            else if (hit.IsBody)
            {
                ctx.Emit(SpellEventKind.ProjectileImpact, Cast, point, Direction, Id);
                ctx.ApplyToBody(Cast, _payload, hit.Body, CastHitKinds.Direct);
            }
            else
            {
                ctx.Emit(SpellEventKind.ProjectileImpact, Cast, point, Direction, Id, solidId: hit.SolidId);
                ctx.DamageSolid(Cast, _payload, hit.SolidId);
                ctx.ReportImpact(Cast, _payload, point, Direction, hit.SolidId); // ice, brittle and combustible cover (L145-L147)
                if (_spec.MissCloudRadius > 0f && !Cast.HitDirectly(1 - Owner))
                {
                    // AUG-434 "Missed shot creates 1 m mist cloud ... at impact" (a miss: the shot struck no player).
                    if (HalenRules.CreateCloud(ctx, Cast, VolumeKind.Mist, point, _spec.MissCloudRadius, _spec.MissCloudLifetimeSec, 0))
                    {
                        HalenRules.Emit(ctx, TideEventKind.MissCloud, Cast, point, _spec.MissCloudRadius);
                    }
                }
            }

            ctx.ApplySplash(Cast, _payload, point, normal);
            ctx.EmitDeposits(Cast, _payload, DepositPlacement.Impact, HalenRules.Ground(ctx.Collision, point), Direction);
            End(ctx);
        }

        private void Expire(SpellContext ctx)
        {
            ctx.Emit(SpellEventKind.ProjectileExpired, Cast, Position, Direction, Id, _travelled);
            End(ctx);
        }

        protected override void HashState(ref StateHasher h)
        {
            h.Add(Position);
            h.Add(Direction);
            h.Add(Speed);
            h.Add(_travelled);
            h.Add(_banked);
        }
    }
}
