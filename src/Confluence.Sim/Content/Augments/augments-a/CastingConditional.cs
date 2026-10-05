using System;
using System.Collections.Generic;
using Confluence.Casting;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Sim.Casting;
using Confluence.Sim.Combat;
using Confluence.Sim.Duel;
using Confluence.Sim.Effects;
using Confluence.Sim.Hashing;
using Confluence.Sim.Players;
using Confluence.Sim.Spells;

namespace Confluence.Sim.Content.Augments.AugmentsA
{
    // Conditional shared augments of "Casting mastery" and "Movement arts" that read the cast's grammar result or the
    // caster's motion at commitment and release (spec L80-L96, L480 mastery clamps).

    /// <summary>
    /// AUG-006 Anchored Cast (Silver, mastery-tagged equipment and round 3 or later). Condition "Release a paid advanced
    /// variation while stationary and grounded for 0.4 s." Rank 1 "Gain 7% direct damage.", rank 2 "Gain 10% direct
    /// damage." Drawback "Moving before release removes bonus." A mastered cast of an equipped spell released after the
    /// owner stood grounded and still (horizontal speed below <see cref="StillSpeed"/>) for 0.4 s deals 7% (10%) more
    /// direct damage, clamped with the cast's own mastery damage reward at 15% (spec L480); any movement in those 0.4 s
    /// (including during the windup) removes it.
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class AnchoredCast : IAugmentBehavior
    {
        public const string Id = "AUG-006";
        public const float StillSec = 0.4f;     // "stationary and grounded for 0.4 s"
        public const float Rank1Bonus = 0.07f;  // "Gain 7% direct damage."
        public const float Rank2Bonus = 0.10f;  // "Gain 10% direct damage."

        /// <summary>Below this horizontal speed (m/s) a grounded body counts as stationary (residual drift only).</summary>
        public const float StillSpeed = 0.1f;

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.Attach(new Hook(AugmentsAKit.ByRank(rank, Rank1Bonus, Rank2Bonus)));
        }

        private sealed class Hook : EffectHook
        {
            private const int MaxTracked = 8;
            private readonly float _bonus;
            private readonly List<CastEventId> _anchored = new List<CastEventId>();
            private int _still;

            public Hook(float bonus) : base(Id)
            {
                _bonus = bonus;
            }

            private bool StillNow => Self.Motor.Grounded && !Self.Motor.IsDisplaced && AugmentsAKit.HorizontalSpeed(Self) < StillSpeed;

            public override void OnEvent(DuelEvent e)
            {
                if (e is CastDuelEvent c && c.Player == Player && c.Kind == CastEventKind.Released && c.Cast.Slot >= 0
                    && c.Cast.Mastery.IsMastered && _still >= AugmentsAKit.Ticks(StillSec) && StillNow)
                {
                    _anchored.Add(c.CastId);
                    if (_anchored.Count > MaxTracked) _anchored.RemoveAt(0);
                }
            }

            public override void OnTick() => _still = StillNow ? _still + 1 : 0;

            // Mastery-linked: summed with the other linked bonuses and clamped once with the cast's reward (L480).
            public override float LinkedDirectDamage(DamageRequest hit, CastRecord cast) =>
                hit.Kind == DamageKind.Direct && hit.IsSpellHit && _anchored.Contains(hit.CastId) ? _bonus : 0f;

            public override void Hash(ref StateHasher h)
            {
                h.Add(_still);
                h.Add(_anchored.Count);
                for (int i = 0; i < _anchored.Count; i++) h.Add((ulong)_anchored[i].Packed);
            }
        }
    }

    /// <summary>
    /// AUG-007 Flying Script (Silver, projectile spell and movement-tagged equipment). Condition "Complete advanced variation
    /// while airborne." Rank 1 "Gain 7% projectile speed.", rank 2 "Gain 11% projectile speed." Drawback "Landing before
    /// release removes bonus; incoming airborne hits remain normal." A direct projectile cast committed with its mastery
    /// reward while the owner is airborne launches 7% (11%) faster, clamped with a projectile-speed mastery reward at 15%
    /// (spec L480), unless the owner lands between commitment and release. Hits the owner takes while airborne are
    /// unchanged.
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class FlyingScript : IAugmentBehavior
    {
        public const string Id = "AUG-007";
        public const float Rank1Speed = 0.07f;  // "Gain 7% projectile speed."
        public const float Rank2Speed = 0.11f;  // "Gain 11% projectile speed."

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.Attach(new Hook(AugmentsAKit.ByRank(rank, Rank1Speed, Rank2Speed)));
        }

        private sealed class Hook : EffectHook
        {
            private readonly float _bonus;
            private CastEventId _airborne = CastEventId.None;
            private bool _released;

            public Hook(float bonus) : base(Id)
            {
                _bonus = bonus;
            }

            public override void OnEvent(DuelEvent e)
            {
                if (CastFacts.IsSpellCommit(e, Player, out CastDuelEvent commit))
                {
                    bool qualifies = commit.Cast.Mastery.IsMastered && !Self.Motor.Grounded && CastFacts.IsDirectProjectile(AugmentsAKit.FormOf(Self, commit.SpellId));
                    _airborne = qualifies ? commit.CastId : CastEventId.None;
                    _released = false;
                }
                else if (e is CastDuelEvent c && c.Player == Player && c.CastId == _airborne && c.Kind == CastEventKind.Released)
                {
                    _released = true;
                }
                else if (e is MovementEvent m && m.Player == Player && m.Change == MovementChange.Landed && _airborne.IsValid && !_released)
                {
                    _airborne = CastEventId.None; // "Landing before release removes bonus."
                }
            }

            // Mastery-linked: summed with the other linked bonuses and clamped once with the cast's reward (L480).
            public override float LinkedProjectileSpeed(CastRecord cast) =>
                _airborne.IsValid && cast.CastId == _airborne ? _bonus : 0f;

            public override void Hash(ref StateHasher h)
            {
                h.Add((ulong)_airborne.Packed);
                h.Add(_released);
            }
        }
    }

    /// <summary>
    /// AUG-103 Moving Release (Silver, projectile spell and movement item equipped). Condition "Release a paid spell while
    /// walking sideways." Rank 1 "Projectile speed increases 4%.", rank 2 "Projectile speed increases 7%." Drawback "Casting
    /// while sprinting costs 1 extra mana." A projectile of an equipped spell released while the owner walks (grounded, not
    /// sprinting, at least 1 m/s) with more of its horizontal velocity across its facing than along it launches 4% (7%)
    /// faster. A cast whose preparation began, or that is paid, while sprinting costs 1 more mana (after the 70% floor).
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class MovingRelease : IAugmentBehavior
    {
        public const string Id = "AUG-103";
        public const float Rank1Speed = 0.04f;  // "Projectile speed increases 4%."
        public const float Rank2Speed = 0.07f;  // "Projectile speed increases 7%."
        public const float SprintMana = 1f;     // "Casting while sprinting costs 1 extra mana."

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.Attach(new Hook(AugmentsAKit.ByRank(rank, Rank1Speed, Rank2Speed)));
        }

        /// <summary>Walking with more of the horizontal velocity across the facing than along it.</summary>
        public static bool WalkingSideways(DuelPlayer p)
        {
            if (!AugmentsAKit.IsWalking(p)) return false;
            Vec3 v = new Vec3(p.Motor.Velocity.X, 0f, p.Motor.Velocity.Z);
            float along = Math.Abs(Vec3.Dot(v, CMath.ForwardFromYaw(p.Motor.YawDeg)));
            float across = Math.Abs(Vec3.Dot(v, CMath.RightFromYaw(p.Motor.YawDeg)));
            return across > along;
        }

        private sealed class Hook : EffectHook
        {
            private readonly float _bonus;
            private CastEventId _sprintPrep = CastEventId.None;

            public Hook(float bonus) : base(Id)
            {
                _bonus = bonus;
            }

            public override void OnEvent(DuelEvent e)
            {
                if (e is CastDuelEvent c && c.Player == Player && c.Kind == CastEventKind.PreparationStarted && c.Cast.Slot >= 0)
                {
                    _sprintPrep = Self.Motor.Sprinting ? c.CastId : CastEventId.None;
                }
            }

            public override void AdjustCastPrice(in CastQuote quote, ref float reduction, ref float increase)
            {
                CastEventId current = AugmentsAKit.CurrentCast(Self);
                if (current.IsValid && (Self.Motor.Sprinting || current == _sprintPrep))
                {
                    increase += SprintMana;
                }
            }

            public override float AdjustProjectileSpeed(CastRecord cast, float speed) =>
                !cast.Form.IsSignature && cast.Form.Mana > 0f && WalkingSideways(Self) ? speed * (1f + _bonus) : speed;

            public override void Hash(ref StateHasher h) => h.Add((ulong)_sprintPrep.Packed);
        }
    }
}
