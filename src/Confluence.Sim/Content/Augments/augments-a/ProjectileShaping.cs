using System;
using System.Collections.Generic;
using Confluence.Casting;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Sim.Casting;
using Confluence.Sim.Combat;
using Confluence.Sim.Duel;
using Confluence.Sim.Effects;
using Confluence.Sim.Hashing;
using Confluence.Sim.Movement;
using Confluence.Sim.Players;
using Confluence.Sim.Spells;
using Confluence.Sim.World;

namespace Confluence.Sim.Content.Augments.AugmentsA
{
    // Shared augments of the "Projectile and beam shaping" family (spec L525-L527 projectile numbers, L543 general
    // augments edit the resolved structure before mastery and caps). The form stages edit each equipped spell's form
    // after its wizard transformations (IAugmentFormStage); the hooks judge one cast or hit.

    internal static class ProjectileForms
    {
        /// <summary>
        /// The form's projectile delivery (the framework projectile or a kit's own, Spells/Deliveries/ProjectileShapes.cs)
        /// when its primary payload deals direct damage, else null.
        /// </summary>
        public static IProjectileDelivery DirectProjectile(SpellFormBuilder form) =>
            form.Delivery is IProjectileDelivery p && form.Payload != null && form.Payload.Direct > 0f ? p : null;

        /// <summary>Whether a hit is the direct impact of one of the attacker's projectile casts.</summary>
        public static bool IsProjectileDirectHit(DuelWorld world, DamageRequest hit)
        {
            if (hit.Kind != DamageKind.Direct || !hit.IsSpellHit) return false;
            CastRecord cast = AugmentsAKit.FindCast(world, hit.CastId);
            return cast != null && cast.Form.Delivery is IProjectileDelivery;
        }
    }

    /// <summary>
    /// AUG-025 Fast Release (Silver, projectile spell equipped). Condition "Cast a straight direct projectile." Rank 1
    /// "Projectile speed increases 8%.", rank 2 "Projectile speed increases 12%." Drawback "Projectile lifetime falls 8%,
    /// reducing maximum travel range." Straight (no gravity) direct projectiles fly 8% (12%) faster and their lifetime and
    /// maximum travel range (spec L525) are 8% shorter.
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class FastRelease : IAugmentBehavior, IAugmentFormStage
    {
        public const string Id = "AUG-025";
        public const float Rank1Speed = 0.08f;  // "Projectile speed increases 8%."
        public const float Rank2Speed = 0.12f;  // "Projectile speed increases 12%."
        public const float LifetimeCut = 0.08f; // "Projectile lifetime falls 8%, reducing maximum travel range."

        public void Attach(IEffectHost host, int rank) => ContentArgs.Require(host);

        public void ApplyToForm(SpellFormBuilder form, int rank)
        {
            IProjectileDelivery p = ProjectileForms.DirectProjectile(form);
            if (p == null || p.Gravity > 0f) return;
            p.Speed *= 1f + AugmentsAKit.ByRank(rank, Rank1Speed, Rank2Speed);
            if (p.MaxRange > 0f) p.MaxRange *= 1f - LifetimeCut;
            if (p is ProjectileDelivery timed && timed.LifetimeSec > 0f) timed.LifetimeSec *= 1f - LifetimeCut; // kit projectiles are range-limited only
        }
    }

    /// <summary>
    /// AUG-026 Heavy Core (Silver, projectile spell equipped). Condition "Cast a direct projectile." Rank 1 "Direct damage
    /// increases 5%.", rank 2 "Direct damage increases 8%." Drawback "Projectile speed decreases 8%." Direct projectiles'
    /// primary direct damage is 5% (8%) higher before mastery (spec L480, L543); they fly 8% slower.
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class HeavyCore : IAugmentBehavior, IAugmentFormStage
    {
        public const string Id = "AUG-026";
        public const float Rank1Damage = 0.05f; // "Direct damage increases 5%."
        public const float Rank2Damage = 0.08f; // "Direct damage increases 8%."
        public const float SpeedCut = 0.08f;    // "Projectile speed decreases 8%."

        public void Attach(IEffectHost host, int rank) => ContentArgs.Require(host);

        public void ApplyToForm(SpellFormBuilder form, int rank)
        {
            IProjectileDelivery p = ProjectileForms.DirectProjectile(form);
            if (p == null) return;
            form.Payload.Direct *= 1f + AugmentsAKit.ByRank(rank, Rank1Damage, Rank2Damage);
            p.Speed *= 1f - SpeedCut;
        }
    }

    /// <summary>
    /// AUG-027 Broad Tip (Silver, projectile spell equipped). Condition "Cast a single direct projectile with radius below
    /// 0.3 m." Rank 1 "Radius increases 0.02 m.", rank 2 "Radius increases 0.04 m." Drawback "Direct damage decreases 5%;
    /// never enlarges beams or fields." A single (one-projectile) direct projectile narrower than 0.3 m grows by 0.02
    /// (0.04) m and deals 5% less direct damage; rays, cones, and fields are never edited.
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class BroadTip : IAugmentBehavior, IAugmentFormStage
    {
        public const string Id = "AUG-027";
        public const float MaxRadius = 0.3f;       // "radius below 0.3 m"
        public const float Rank1Radius = 0.02f;    // "Radius increases 0.02 m."
        public const float Rank2Radius = 0.04f;    // "Radius increases 0.04 m."
        public const float DamageCut = 0.05f;      // "Direct damage decreases 5%"

        public void Attach(IEffectHost host, int rank) => ContentArgs.Require(host);

        public void ApplyToForm(SpellFormBuilder form, int rank)
        {
            IProjectileDelivery p = ProjectileForms.DirectProjectile(form);
            if (p == null || p.Count != 1 || !(p.Radius < MaxRadius)) return;
            p.Radius += AugmentsAKit.ByRank(rank, Rank1Radius, Rank2Radius);
            form.Payload.Direct *= 1f - DamageCut;
        }
    }

    /// <summary>
    /// AUG-028 Fine Tip (Silver, projectile spell equipped). Condition "Cast a direct projectile with radius above 0.08 m."
    /// Rank 1 "Direct damage increases 6%.", rank 2 "Direct damage increases 9%." Drawback "Radius decreases 15%, minimum
    /// 0.08 m." Direct projectiles wider than 0.08 m deal 6% (9%) more direct damage and shrink by 15%, never below 0.08 m.
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class FineTip : IAugmentBehavior, IAugmentFormStage
    {
        public const string Id = "AUG-028";
        public const float MinRadius = 0.08f;   // "radius above 0.08 m", "minimum 0.08 m"
        public const float Rank1Damage = 0.06f; // "Direct damage increases 6%."
        public const float Rank2Damage = 0.09f; // "Direct damage increases 9%."
        public const float RadiusCut = 0.15f;   // "Radius decreases 15%"

        public void Attach(IEffectHost host, int rank) => ContentArgs.Require(host);

        public void ApplyToForm(SpellFormBuilder form, int rank)
        {
            IProjectileDelivery p = ProjectileForms.DirectProjectile(form);
            if (p == null || !(p.Radius > MinRadius)) return;
            form.Payload.Direct *= 1f + AugmentsAKit.ByRank(rank, Rank1Damage, Rank2Damage);
            p.Radius = Math.Max(MinRadius, p.Radius * (1f - RadiusCut));
        }
    }

    /// <summary>
    /// AUG-031 High Arc (Silver, projectile spell equipped). Condition "Projectile release occurs at least 1 m above enemy
    /// feet." Rank 1 "Prop damage increases 12%.", rank 2 "Prop damage increases 20%." Drawback "Player damage decreases 3%."
    /// "Release ... above enemy feet" is an elevated (or airborne) release: the caster's feet are at least 1 m above the
    /// enemy's feet when the projectile cast releases (a vantage platform or ledge, spec L312, or near a jump's 1.1 m apex,
    /// D-024). Measuring from the eye (1.6 m above the caster's own feet, L527) would make every level-ground release
    /// qualify and remove the gate. Such a cast deals 12% (20%) more durability damage to props and 3% less to players.
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class HighArc : IAugmentBehavior
    {
        public const string Id = "AUG-031";
        public const float MinHeight = 1f;       // "at least 1 m above enemy feet"
        public const float Rank1Prop = 0.12f;    // "Prop damage increases 12%."
        public const float Rank2Prop = 0.20f;    // "Prop damage increases 20%."
        public const float PlayerCut = 0.03f;    // "Player damage decreases 3%."

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.Attach(new Hook(AugmentsAKit.ByRank(rank, Rank1Prop, Rank2Prop)));
        }

        private sealed class Hook : EffectHook
        {
            private readonly float _prop;
            private readonly List<CastEventId> _high = new List<CastEventId>();

            public Hook(float prop) : base(Id)
            {
                _prop = prop;
            }

            public override void OnEvent(DuelEvent e)
            {
                if (e is CastDuelEvent c && c.Player == Player && c.Kind == CastEventKind.Released
                    && AugmentsAKit.FormOf(Self, c.SpellId)?.Delivery is IProjectileDelivery
                    && Self.Position.Y - Opponent.Position.Y >= MinHeight - 1e-4f)
                {
                    _high.Add(c.CastId);
                    if (_high.Count > 16) _high.RemoveAt(0);
                }
            }

            public override float AdjustSolidDamage(int solidId, float amount, string sourceId, CastEventId castId) =>
                _high.Contains(castId) && World.Props.IsPropSolid(solidId) ? amount * (1f + _prop) : amount;

            public override void AdjustOutgoing(DamageRequest hit, float range, ref float fraction, ref float flat)
            {
                if (hit.IsSpellHit && _high.Contains(hit.CastId))
                {
                    fraction -= PlayerCut;
                }
            }

            public override void Hash(ref StateHasher h)
            {
                h.Add(_high.Count);
                for (int i = 0; i < _high.Count; i++) h.Add((ulong)_high[i].Packed);
            }
        }
    }

    /// <summary>
    /// AUG-038 First Contact (Gold, projectile spell equipped). Condition "A direct projectile hits an enemy with full
    /// health." Rank 1 "Damage increases 5%.", rank 2 "Damage increases 8%." Drawback "Damage against enemies below 75
    /// health decreases 3%." The owner's direct projectile hits on an enemy at full health deal 5% (8%) more; on an enemy
    /// below 75 health they deal 3% less.
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class FirstContact : IAugmentBehavior
    {
        public const string Id = "AUG-038";
        public const float Rank1Bonus = 0.05f;    // "Damage increases 5%."
        public const float Rank2Bonus = 0.08f;    // "Damage increases 8%."
        public const float LowHealth = 75f;       // "below 75 health"
        public const float LowPenalty = 0.03f;    // "decreases 3%"

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.Attach(new Hook(AugmentsAKit.ByRank(rank, Rank1Bonus, Rank2Bonus)));
        }

        private sealed class Hook : EffectHook
        {
            private readonly float _bonus;

            public Hook(float bonus) : base(Id)
            {
                _bonus = bonus;
            }

            public override void AdjustOutgoing(DamageRequest hit, float range, ref float fraction, ref float flat)
            {
                if (!ProjectileForms.IsProjectileDirectHit(World, hit)) return;
                DuelPlayer target = World.Player(hit.Target);
                if (target.Resources.Health >= target.Resources.MaxHealth - 1e-3f)
                {
                    fraction += _bonus;
                }
                else if (AugmentsAKit.HealthBelow(target, LowHealth))
                {
                    fraction -= LowPenalty;
                }
            }
        }
    }

    /// <summary>
    /// AUG-039 Last Crossing (Gold, projectile spell equipped). Condition "A direct projectile hits during enemy landing
    /// recovery." Rank 1 "Damage increases 6%.", rank 2 "Damage increases 9%." Drawback "Projectiles hitting grounded
    /// walking enemies deal 3% less damage." The base rules give a landing no recovery of its own, so the enemy's landing
    /// recovery is the first 0.25 s after it lands (the punishable window length of spec L104); direct projectile hits
    /// in it deal 6% (9%) more. Direct projectile hits on an enemy walking on the ground otherwise deal 3% less.
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class LastCrossing : IAugmentBehavior
    {
        public const string Id = "AUG-039";
        public const float Rank1Bonus = 0.06f;      // "Damage increases 6%."
        public const float Rank2Bonus = 0.09f;      // "Damage increases 9%."
        public const float RecoverySec = 0.25f;     // landing recovery (interpretation, see summary)
        public const float WalkingPenalty = 0.03f;  // "deal 3% less damage"

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.Attach(new Hook(AugmentsAKit.ByRank(rank, Rank1Bonus, Rank2Bonus)));
        }

        private sealed class Hook : EffectHook
        {
            private readonly float _bonus;
            private int _enemyLanded = int.MinValue / 2;

            public Hook(float bonus) : base(Id)
            {
                _bonus = bonus;
            }

            public override void OnEvent(DuelEvent e)
            {
                if (e is MovementEvent m && m.Player == 1 - Player && m.Change == MovementChange.Landed)
                {
                    _enemyLanded = m.Tick;
                }
            }

            public override void AdjustOutgoing(DamageRequest hit, float range, ref float fraction, ref float flat)
            {
                if (!ProjectileForms.IsProjectileDirectHit(World, hit)) return;
                DuelPlayer target = World.Player(hit.Target);
                if (target.Motor.Grounded && Now - _enemyLanded <= AugmentsAKit.Ticks(RecoverySec))
                {
                    fraction += _bonus;
                }
                else if (AugmentsAKit.IsWalking(target))
                {
                    fraction -= WalkingPenalty;
                }
            }

            public override void Hash(ref StateHasher h) => h.Add(_enemyLanded);
        }
    }

    /// <summary>
    /// AUG-041 Prepared Patience (Prismatic, a spell with at least 0.4 s cast time equipped). Condition "A paid spell has at
    /// least 0.4 s declared windup and caster stays grounded through release." Rank 1 "Cost decreases 6% mana.", rank 2
    /// "Cost decreases 10% mana." Drawback "Walking speed during this preparation decreases 5%." The condition completes at
    /// release, after the commitment paid the cost (spec L90), so the decrease is returned then: 6% (10%) of the listed
    /// cost through the cast's refund ledger (at most 25% of the payment, once, L124) when the caster stayed on the ground
    /// from commitment to release. During such a spell's windup walking is 5% slower (under the L124 arithmetic).
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class PreparedPatience : IAugmentBehavior
    {
        public const string Id = "AUG-041";
        public const float MinWindupSec = 0.4f;   // "at least 0.4 s declared windup"
        public const float Rank1Cost = 0.06f;     // "Cost decreases 6% mana."
        public const float Rank2Cost = 0.10f;     // "Cost decreases 10% mana."
        public const float WalkPenalty = 0.05f;   // "Walking speed during this preparation decreases 5%."

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.Attach(new Hook(AugmentsAKit.ByRank(rank, Rank1Cost, Rank2Cost)));
        }

        private sealed class Hook : EffectHook
        {
            private readonly float _fraction;
            private readonly PaymentLog _payments = new PaymentLog();
            private CastEventId _cast = CastEventId.None;
            private bool _grounded;
            private CastDuelEvent _released;

            public Hook(float fraction) : base(Id)
            {
                _fraction = fraction;
            }

            private static bool Qualifies(SpellForm form) => form != null && !form.IsSignature && form.CastTimeSec >= MinWindupSec - 1e-4f;

            public override void OnEvent(DuelEvent e)
            {
                _payments.Observe(e, Player);
                if (!(e is CastDuelEvent c) || c.Player != Player)
                {
                    return;
                }

                if (c.Kind == CastEventKind.Committed && Qualifies(AugmentsAKit.FormOf(Self, c.SpellId)))
                {
                    _cast = c.CastId;
                    _grounded = Self.Motor.Grounded;
                }
                else if (c.CastId == _cast && c.Kind == CastEventKind.Released)
                {
                    if (_grounded && Self.Motor.Grounded) _released = c;
                    _cast = CastEventId.None;
                }
                else if (c.CastId == _cast && (c.Kind == CastEventKind.Interrupted || c.Kind == CastEventKind.CommittedCastCancelled))
                {
                    _cast = CastEventId.None;
                }
            }

            public override void OnTick()
            {
                if (_cast.IsValid && !Self.Motor.Grounded)
                {
                    _grounded = false; // left the ground before release
                }

                if (_released == null)
                {
                    return;
                }

                CastDuelEvent c = _released;
                _released = null;
                SpellForm form = AugmentsAKit.FormOf(Self, c.SpellId);
                int payment = _payments.PaymentOf(c.CastId);
                if (InCombat && form != null && payment != 0 && TryActivate(c.Id))
                {
                    World.Effects.RefundCast(Player, payment, form.Mana * _fraction, Key);
                }
            }

            public override void AdjustGait(in MotorCommand command, ref GaitAdjust gait)
            {
                CastMachine m = Self.Caster != null ? Self.Caster.Machine : null;
                if (m != null && m.Phase == CastPhase.Windup && m.CurrentSpell != null && Qualifies(AugmentsAKit.FormOf(Self, m.CurrentSpell.Id)))
                {
                    gait.Walk -= WalkPenalty; // a penalty: applies after the +20% cap with the other gait changes
                }
            }

            public override void Hash(ref StateHasher h)
            {
                h.Add((ulong)_cast.Packed);
                h.Add(_grounded);
                h.Add(_released != null ? _released.Id : 0);
                _payments.Hash(ref h);
            }
        }
    }

    /// <summary>
    /// AUG-042 Quick Recovery (Prismatic, a spell with at most 0.3 s cast time equipped). Condition "A paid spell with no
    /// more than 0.3 s windup releases." Rank 1 "Post-release recovery decreases 0.05 s, minimum 0.1 s.", rank 2
    /// "Post-release recovery decreases 0.1 s, minimum 0.1 s." Drawback "That spell direct damage decreases 5%; cooldown
    /// unchanged." Spells with an intrinsic windup of at most 0.3 s (spec L92) recover 0.05 (0.1) s sooner, never below
    /// 0.1 s, and their primary direct damage is 5% lower; cooldowns are untouched.
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class QuickRecovery : IAugmentBehavior, IAugmentFormStage
    {
        public const string Id = "AUG-042";
        public const float MaxWindupSec = 0.3f;      // "no more than 0.3 s windup"
        public const float Rank1RecoverySec = 0.05f; // "Post-release recovery decreases 0.05 s, minimum 0.1 s."
        public const float Rank2RecoverySec = 0.1f;  // "Post-release recovery decreases 0.1 s, minimum 0.1 s."
        public const float MinRecoverySec = 0.1f;
        public const float DamageCut = 0.05f;        // "That spell direct damage decreases 5%"

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.Attach(new Hook(AugmentsAKit.ByRank(rank, Rank1RecoverySec, Rank2RecoverySec)));
        }

        public void ApplyToForm(SpellFormBuilder form, int rank)
        {
            if (form.CastTimeSec <= MaxWindupSec + 1e-4f && form.Payload != null && form.Payload.Direct > 0f)
            {
                form.Payload.Direct *= 1f - DamageCut;
            }
        }

        private sealed class Hook : EffectHook
        {
            private readonly float _cut;

            public Hook(float cut) : base(Id)
            {
                _cut = cut;
            }

            public override void AdjustCastTiming(in CastQuote quote, ref CastTimingAdjust timing)
            {
                CastableSpell spell = Self.Caster != null ? Self.Caster.Machine.CurrentSpell : null;
                if (spell != null && !spell.IsSignature && spell.WindupTicks <= AugmentsAKit.Ticks(MaxWindupSec))
                {
                    timing.RecoveryDeltaTicks -= AugmentsAKit.Ticks(_cut);
                    timing.RecoveryFloorTicks = Math.Max(timing.RecoveryFloorTicks, AugmentsAKit.Ticks(MinRecoverySec));
                }
            }
        }
    }

    /// <summary>
    /// AUG-043 Visible Afterimage (Prismatic, projectile spell equipped). Condition "An untransformed projectile misses an
    /// enemy within 1 m." Rank 1 "Leave inert glowing path for 0.5 s; next different spell within 1 s costs 2 less mana.",
    /// rank 2 "Leave inert glowing path for 0.75 s; next different spell within 1 s costs 3 less mana." Drawback "Trail helps
    /// opponent track casting direction; 4 s cooldown." A projectile of a spell without wizard transformations that ends
    /// without striking the enemy after passing within 1 m of the enemy's body leaves an inert glowing path for 0.5 s
    /// (0.75 s): a public <see cref="MarkerEvent"/> both players receive, from the projectile's launch point (the caster's
    /// release position) to where its flight ended, with the launch direction — so the opponent can read where the cast
    /// came from and in which direction it was aimed (the drawback). It has no collision or rule effect. The next different
    /// spell committed within 1 s costs 2 (3) less inside the 70% floor (spec L124). At most once per 4 s.
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class VisibleAfterimage : IAugmentBehavior
    {
        public const string Id = "AUG-043";
        public const float NearMissMeters = 1f;      // "misses an enemy within 1 m"
        public const float Rank1TrailSec = 0.5f;     // "inert glowing path for 0.5 s"
        public const float Rank2TrailSec = 0.75f;    // "inert glowing path for 0.75 s"
        public const float Rank1Mana = 2f;           // "costs 2 less mana"
        public const float Rank2Mana = 3f;           // "costs 3 less mana"
        public const float WindowSec = 1f;           // "within 1 s"
        public const float CooldownSec = 4f;         // "4 s cooldown"

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.Attach(new Hook(AugmentsAKit.ByRank(rank, Rank1TrailSec, Rank2TrailSec), AugmentsAKit.ByRank(rank, Rank1Mana, Rank2Mana)));
        }

        /// <summary>The trail marker's name in <see cref="MarkerEvent.Cue"/>; its Amount is the trail's duration in seconds.</summary>
        public const string TrailCue = "afterimage-trail";

        private sealed class Hook : EffectHook
        {
            private readonly float _trailSec;
            private readonly float _mana;
            private readonly List<Flight> _flights = new List<Flight>();
            private int _lastActivation = int.MinValue / 2;
            private string _missedSpell;
            private int _windowEnd = -1;
            private bool _used;

            public Hook(float trailSec, float mana) : base(Id)
            {
                _trailSec = trailSec;
                _mana = mana;
            }

            public override void OnEvent(DuelEvent e)
            {
                if (e is SpellEvent s && s.Player == Player && s.Kind == SpellEventKind.ProjectileLaunched)
                {
                    CastRecord cast = AugmentsAKit.FindCast(World, s.CastId);
                    if (cast != null && cast.Form.AppliedTransforms.Count == 0)
                    {
                        _flights.Add(new Flight { EntityId = s.EntityId, Cast = s.CastId, SpellId = s.SpellId, Launch = s.Position, Direction = s.Direction, Last = s.Position, Nearest = float.MaxValue });
                    }
                }
                else if (e is DamageEvent d && d.Attacker == Player && d.Target == 1 - Player && d.Kind == DamageKind.Direct)
                {
                    for (int i = 0; i < _flights.Count; i++) if (_flights[i].Cast == d.CastId) _flights[i].Hit = true;
                }
                else if (_windowEnd >= 0 && CastFacts.IsSpellCommit(e, Player, out CastDuelEvent c) && !string.Equals(c.SpellId, _missedSpell, StringComparison.Ordinal))
                {
                    _used = true;
                }
            }

            public override void OnTick()
            {
                if (_used || (_windowEnd >= 0 && Now > _windowEnd))
                {
                    _used = false;
                    _windowEnd = -1;
                }

                SpellRuntime runtime = AugmentsAKit.Runtime(World);
                Capsule body = Opponent.HitVolume;
                for (int i = _flights.Count - 1; i >= 0; i--)
                {
                    Flight f = _flights[i];
                    IProjectileBody p = Find(runtime, f.EntityId);
                    if (p != null)
                    {
                        // The closest approach of this tick's travel to the enemy's body surface.
                        float d2 = Intersections.SegmentSegmentClosest(f.Last, p.Position, body.A, body.B, out _, out _, out _, out _);
                        f.Nearest = Math.Min(f.Nearest, (float)Math.Sqrt(d2) - body.Radius - p.Radius);
                        f.Last = p.Position;
                        continue;
                    }

                    _flights.RemoveAt(i);
                    if (!f.Hit && f.Nearest <= NearMissMeters && InCombat && Now - _lastActivation >= AugmentsAKit.Ticks(CooldownSec) && TryActivate())
                    {
                        _lastActivation = Now;
                        _missedSpell = f.SpellId;
                        _windowEnd = Now + AugmentsAKit.Ticks(WindowSec);
                        // The inert path from the release point along the flight, visible to both players (the drawback).
                        EmitMarker(TrailCue, f.Launch, f.Last, f.Direction, AugmentsAKit.Ticks(_trailSec), _trailSec);
                    }
                }
            }

            private static IProjectileBody Find(SpellRuntime runtime, int entityId)
            {
                if (runtime == null) return null;
                IReadOnlyList<SpellEntity> entities = runtime.Entities;
                for (int i = 0; i < entities.Count; i++)
                {
                    if (entities[i].Id == entityId && entities[i].Alive && entities[i] is IProjectileBody p) return p;
                }

                return null;
            }

            public override void AdjustCastPrice(in CastQuote quote, ref float reduction, ref float increase)
            {
                if (_windowEnd >= 0 && Now <= _windowEnd && AugmentsAKit.CurrentCast(Self).IsValid && !string.Equals(quote.SpellId, _missedSpell, StringComparison.Ordinal))
                {
                    reduction += _mana;
                }
            }

            public override void Hash(ref StateHasher h)
            {
                h.Add(_flights.Count);
                for (int i = 0; i < _flights.Count; i++)
                {
                    Flight f = _flights[i];
                    h.Add(f.EntityId);
                    h.Add((ulong)f.Cast.Packed);
                    h.Add(f.SpellId);
                    h.Add(f.Launch);
                    h.Add(f.Direction);
                    h.Add(f.Last);
                    h.Add(f.Nearest);
                    h.Add(f.Hit);
                }

                h.Add(_lastActivation);
                h.Add(_missedSpell);
                h.Add(_windowEnd);
                h.Add(_used);
            }

            private sealed class Flight
            {
                public int EntityId;
                public CastEventId Cast;
                public string SpellId;
                public Vec3 Launch;
                public Vec3 Direction;
                public Vec3 Last;
                public float Nearest;
                public bool Hit;
            }
        }
    }

    /// <summary>
    /// AUG-044 Narrow Verdict (Prismatic, projectile spell equipped). Condition "Direct projectile hits an enemy visible
    /// through a gap narrower than 1 m." Rank 1 "Damage increases 7%.", rank 2 "Damage increases 11%." Drawback "Projectile
    /// radius decreases 10%; no bonus through concealment." A direct projectile hit gains 7% (11%) when its path from the
    /// launch point passed through an opening narrower than 1 m (solid geometry on both sides, measured level and
    /// across the path) and the enemy was acquirable at the hit (spec L120, L167: concealment gives no bonus). Every
    /// projectile of the owner is 10% narrower.
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class NarrowVerdict : IAugmentBehavior, IAugmentFormStage
    {
        public const string Id = "AUG-044";
        public const float GapMeters = 1f;      // "a gap narrower than 1 m"
        public const float Rank1Bonus = 0.07f;  // "Damage increases 7%."
        public const float Rank2Bonus = 0.11f;  // "Damage increases 11%."
        public const float RadiusCut = 0.10f;   // "Projectile radius decreases 10%"
        public const float SampleStep = 0.25f;  // path samples every 0.25 m (half a reaction cell)

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.Attach(new Hook(AugmentsAKit.ByRank(rank, Rank1Bonus, Rank2Bonus)));
        }

        public void ApplyToForm(SpellFormBuilder form, int rank)
        {
            if (form.Delivery is IProjectileDelivery p)
            {
                p.Radius *= 1f - RadiusCut;
            }
        }

        private sealed class Hook : EffectHook
        {
            private readonly float _bonus;
            private readonly Dictionary<int, Vec3> _launches = new Dictionary<int, Vec3>();
            private readonly Dictionary<int, CastEventId> _casts = new Dictionary<int, CastEventId>();
            private Vec3 _impact;
            private CastEventId _impactCast = CastEventId.None;
            private int _impactEntity;
            private int _impactTick = -1;

            public Hook(float bonus) : base(Id)
            {
                _bonus = bonus;
            }

            public override void OnEvent(DuelEvent e)
            {
                if (!(e is SpellEvent s) || s.Player != Player)
                {
                    return;
                }

                if (s.Kind == SpellEventKind.ProjectileLaunched)
                {
                    _launches[s.EntityId] = s.Position;
                    _casts[s.EntityId] = s.CastId;
                    if (_launches.Count > 32) { _launches.Clear(); _casts.Clear(); _launches[s.EntityId] = s.Position; _casts[s.EntityId] = s.CastId; }
                }
                else if (s.Kind == SpellEventKind.ProjectileImpact && s.TargetPlayer == 1 - Player)
                {
                    _impact = s.Position;
                    _impactCast = s.CastId;
                    _impactEntity = s.EntityId;
                    _impactTick = s.Tick;
                }
            }

            public override void AdjustOutgoing(DamageRequest hit, float range, ref float fraction, ref float flat)
            {
                if (hit.Kind != DamageKind.Direct || !hit.IsSpellHit || hit.CastId != _impactCast || _impactTick != Now)
                {
                    return;
                }

                if (!_launches.TryGetValue(_impactEntity, out Vec3 from) || !World.CanAcquire(Player, hit.Target))
                {
                    return; // concealment gives no bonus
                }

                if (ThroughNarrowGap(World.Collision, from, _impact))
                {
                    fraction += _bonus;
                }
            }

            public override void Hash(ref StateHasher h)
            {
                h.Add(_impactTick);
                h.Add(_impact);
                h.Add((ulong)_impactCast.Packed);
                h.Add(_impactEntity);
                var keys = new List<int>(_launches.Keys);
                keys.Sort(); // stable order whatever the dictionary's internal layout
                h.Add(keys.Count);
                for (int i = 0; i < keys.Count; i++)
                {
                    h.Add(keys[i]);
                    h.Add(_launches[keys[i]]);
                    h.Add((ulong)_casts[keys[i]].Packed);
                }
            }
        }

        /// <summary>
        /// Whether the segment passes an opening narrower than 1 m: at some sample along it, level rays across the path
        /// meet solids on both sides with a combined clearance below the gap width.
        /// </summary>
        public static bool ThroughNarrowGap(CollisionWorld collision, Vec3 from, Vec3 to)
        {
            Vec3 d = to - from;
            Vec3 flat = new Vec3(d.X, 0f, d.Z);
            float length = d.Length();
            if (length < 1e-3f || flat.LengthSquared() < 1e-6f) return false;
            Vec3 across = new Vec3(-flat.Z, 0f, flat.X).Normalized();
            Vec3 dir = d / length;
            for (float t = SampleStep; t < length - 1e-3f; t += SampleStep)
            {
                Vec3 p = from + dir * t;
                if (!collision.Raycast(p, across, GapMeters, SolidMask.Movement, out WorldHit left)) continue;
                if (!collision.Raycast(p, -across, GapMeters, SolidMask.Movement, out WorldHit right)) continue;
                if (left.Distance + right.Distance < GapMeters) return true;
            }

            return false;
        }
    }
}

namespace Confluence.Sim.Content.Augments.AugmentsA
{
    /// <summary>
    /// AUG-030 Ground Skimmer (Silver, projectile spell equipped). Condition "A projectile travels within 0.3 m of solid
    /// ground for at least 2 m." Rank 1 "Speed increases 7%.", rank 2 "Speed increases 11%." Drawback "Airborne-target
    /// damage decreases 5%." Each of the owner's live projectiles whose underside stays within 0.3 m of a solid surface
    /// below it (floors, platforms, cover tops) for 2 m of continuous travel flies 7% (11%) faster from then on (once per
    /// projectile); rising above 0.3 m restarts its count. The owner's spell hits on an airborne target deal 5% less.
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class GroundSkimmer : IAugmentBehavior
    {
        public const string Id = "AUG-030";
        public const float SkimHeight = 0.3f;    // "within 0.3 m of solid ground"
        public const float SkimDistance = 2f;    // "for at least 2 m"
        public const float Rank1Speed = 0.07f;   // "Speed increases 7%."
        public const float Rank2Speed = 0.11f;   // "Speed increases 11%."
        public const float AirborneCut = 0.05f;  // "Airborne-target damage decreases 5%."

        public void Attach(IEffectHost host, int rank)
        {
            Confluence.Sim.Content.ContentArgs.Require(host);
            host.Attach(new Hook(AugmentsAKit.ByRank(rank, Rank1Speed, Rank2Speed)));
        }

        private sealed class Hook : EffectHook
        {
            private const int MaxTracked = 16;
            private readonly float _bonus;
            private readonly List<int> _ids = new List<int>();
            private readonly List<Vec3> _last = new List<Vec3>();
            private readonly List<float> _skimmed = new List<float>(); // negative once boosted

            public Hook(float bonus) : base(Id)
            {
                _bonus = bonus;
            }

            public override void OnTick()
            {
                SpellRuntime runtime = AugmentsAKit.Runtime(World);
                if (runtime == null) return;
                for (int i = _ids.Count - 1; i >= 0; i--)
                {
                    if (Find(runtime, _ids[i]) == null) Forget(i);
                }

                IReadOnlyList<SpellEntity> all = runtime.Entities;
                for (int e = 0; e < all.Count; e++)
                {
                    if (!(all[e] is IProjectileBody p) || !p.Alive || p.Owner != Player) continue;
                    int i = _ids.IndexOf(p.Id);
                    if (i < 0)
                    {
                        if (_ids.Count >= MaxTracked) Forget(0);
                        _ids.Add(p.Id);
                        _last.Add(p.Position);
                        _skimmed.Add(0f);
                        i = _ids.Count - 1;
                    }

                    float moved = Vec3.Distance(_last[i], p.Position);
                    _last[i] = p.Position;
                    if (_skimmed[i] < 0f) continue; // already boosted
                    float clearance = p.Position.Y - p.Radius;
                    bool skimming = World.Collision.Raycast(p.Position, Vec3.Down, p.Radius + SkimHeight, SolidMask.Movement, out WorldHit ground)
                        && clearance - ground.Point.Y <= SkimHeight + 1e-4f;
                    _skimmed[i] = skimming ? _skimmed[i] + moved : 0f;
                    if (_skimmed[i] >= SkimDistance)
                    {
                        _skimmed[i] = -1f;
                        ((IProjectileSpeedScale)p).ScaleSpeed(1f + _bonus);
                        EmitCue("skim", _bonus);
                    }
                }
            }

            private static IProjectileBody Find(SpellRuntime runtime, int id)
            {
                IReadOnlyList<SpellEntity> all = runtime.Entities;
                for (int e = 0; e < all.Count; e++)
                {
                    if (all[e].Id == id && all[e].Alive && all[e] is IProjectileBody p) return p;
                }

                return null;
            }

            private void Forget(int i)
            {
                _ids.RemoveAt(i);
                _last.RemoveAt(i);
                _skimmed.RemoveAt(i);
            }

            public override void AdjustOutgoing(DamageRequest hit, float range, ref float fraction, ref float flat)
            {
                if (hit.IsSpellHit && !World.Player(hit.Target).Motor.Grounded)
                {
                    fraction -= AirborneCut;
                }
            }

            public override void Hash(ref StateHasher h)
            {
                h.Add(_ids.Count);
                for (int i = 0; i < _ids.Count; i++)
                {
                    h.Add(_ids[i]);
                    h.Add(_last[i]);
                    h.Add(_skimmed[i]);
                }
            }
        }
    }
}

namespace Confluence.Sim.Content.Augments.AugmentsA
{
    /// <summary>
    /// AUG-032 Range Compression (Silver, projectile spell and melee item equipped). Condition "Direct projectile hits within
    /// 5 m." Rank 1 "Damage increases 6%.", rank 2 "Damage increases 10%." Drawback "Damage beyond 15 m decreases 8%." The
    /// owner's direct projectile hits at a caster-target range of at most 5 m deal 6% (10%) more; beyond 15 m 8% less.
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class RangeCompression : IAugmentBehavior
    {
        public const string Id = "AUG-032";
        public const float CloseRange = 5f;    // "within 5 m"
        public const float FarRange = 15f;     // "beyond 15 m"
        public const float Rank1Bonus = 0.06f; // "Damage increases 6%."
        public const float Rank2Bonus = 0.10f; // "Damage increases 10%."
        public const float FarCut = 0.08f;     // "Damage beyond 15 m decreases 8%."

        public void Attach(IEffectHost host, int rank)
        {
            Confluence.Sim.Content.ContentArgs.Require(host);
            host.Attach(new RangeBandHook(Id, CloseRange, AugmentsAKit.ByRank(rank, Rank1Bonus, Rank2Bonus), FarRange, -FarCut));
        }
    }

    /// <summary>
    /// AUG-033 Range Extension (Gold, projectile spell and precision item equipped). Condition "Direct projectile hits beyond
    /// 15 m." Rank 1 "Damage increases 6%.", rank 2 "Damage increases 10%." Drawback "Damage within 5 m decreases 8%." The
    /// owner's direct projectile hits at a range beyond 15 m deal 6% (10%) more; within 5 m 8% less.
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class RangeExtension : IAugmentBehavior
    {
        public const string Id = "AUG-033";
        public const float CloseRange = 5f;    // "within 5 m"
        public const float FarRange = 15f;     // "beyond 15 m"
        public const float Rank1Bonus = 0.06f; // "Damage increases 6%."
        public const float Rank2Bonus = 0.10f; // "Damage increases 10%."
        public const float CloseCut = 0.08f;   // "Damage within 5 m decreases 8%."

        public void Attach(IEffectHost host, int rank)
        {
            Confluence.Sim.Content.ContentArgs.Require(host);
            host.Attach(new RangeBandHook(Id, CloseRange, -CloseCut, FarRange, AugmentsAKit.ByRank(rank, Rank1Bonus, Rank2Bonus)));
        }
    }

    /// <summary>Direct projectile hits: one fraction at range &lt;= close, another beyond far.</summary>
    internal sealed class RangeBandHook : EffectHook
    {
        private readonly float _close;
        private readonly float _closeFraction;
        private readonly float _far;
        private readonly float _farFraction;

        public RangeBandHook(string id, float close, float closeFraction, float far, float farFraction) : base(id)
        {
            _close = close;
            _closeFraction = closeFraction;
            _far = far;
            _farFraction = farFraction;
        }

        public override void AdjustOutgoing(DamageRequest hit, float range, ref float fraction, ref float flat)
        {
            if (!ProjectileForms.IsProjectileDirectHit(World, hit)) return;
            if (range <= _close) fraction += _closeFraction;
            else if (range > _far) fraction += _farFraction;
        }
    }
}
