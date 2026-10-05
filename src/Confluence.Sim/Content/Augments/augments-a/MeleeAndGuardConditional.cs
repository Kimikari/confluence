using Confluence.Casting;
using Confluence.Core.Content;
using Confluence.Core.Rules;
using Confluence.Sim.Casting;
using Confluence.Sim.Combat;
using Confluence.Sim.Duel;
using Confluence.Sim.Effects;
using Confluence.Sim.Hashing;

namespace Confluence.Sim.Content.Augments.AugmentsA
{
    // Conditional shared augments of the "Melee and guard" family (spec L100-L106; L126 proc budget).

    /// <summary>
    /// AUG-079 Exact Riposte (Silver, melee item and guard item equipped). Condition "Perfect guard then direct weapon hit
    /// within 0.6 s." Rank 1 "Add 3 physical damage.", rank 2 "Add 5 physical damage." Drawback "Ordinary guard followed by
    /// strike gains nothing." The owner's first weapon hit within 0.6 s of its perfect guard (spec L104) deals 3 (5) more
    /// physical damage; an ordinary block arms nothing. One activation per perfect guard, through the proc budget (L126).
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class ExactRiposte : IAugmentBehavior
    {
        public const string Id = "AUG-079";
        public const float WindowSec = 0.6f;   // "within 0.6 s"
        public const float Rank1Damage = 3f;   // "Add 3 physical damage."
        public const float Rank2Damage = 5f;   // "Add 5 physical damage."

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.Attach(new Hook(AugmentsAKit.ByRank(rank, Rank1Damage, Rank2Damage)));
        }

        private sealed class Hook : EffectHook
        {
            private readonly float _damage;
            private int _perfectTick = -1;
            private int _perfectEvent;
            private bool _armed;

            public Hook(float damage) : base(Id)
            {
                _damage = damage;
            }

            private bool Open => _armed && _perfectTick >= 0 && Now - _perfectTick <= AugmentsAKit.Ticks(WindowSec);

            public override void OnEvent(DuelEvent e)
            {
                if (e is GuardEvent g && g.Player == Player && g.Change == GuardChange.PerfectBlocked)
                {
                    _perfectTick = g.Tick;
                    _perfectEvent = g.Id;
                    _armed = false;
                }
                else if (e is DamageEvent d && AugmentsAKit.IsWeaponHit(d, Player) && Open)
                {
                    _perfectTick = -1; // the riposte landed
                    _armed = false;
                }
            }

            public override void OnTick()
            {
                if (_perfectTick >= 0 && !_armed && _perfectEvent != 0)
                {
                    int origin = _perfectEvent;
                    _perfectEvent = 0;
                    _armed = InCombat && TryActivate(origin, 1 - Player, _damage);
                }

                if (_perfectTick >= 0 && Now - _perfectTick > AugmentsAKit.Ticks(WindowSec))
                {
                    _perfectTick = -1;
                    _armed = false;
                }
            }

            public override void AdjustOutgoing(DamageRequest hit, float range, ref float fraction, ref float flat)
            {
                if (hit.Kind == DamageKind.Melee && Open)
                {
                    flat += _damage;
                }
            }

            public override void Hash(ref StateHasher h)
            {
                h.Add(_perfectTick);
                h.Add(_perfectEvent);
                h.Add(_armed);
            }
        }
    }

    /// <summary>
    /// AUG-082 Blade and Bolt (Gold, melee item and projectile spell equipped). Condition "Weapon hit followed by direct
    /// projectile within 1 s." Rank 1 "Projectile gains 3 direct damage.", rank 2 "Projectile gains 5 direct damage."
    /// Drawback "Second weapon hit before projectile resets setup." After a weapon hit, the owner's next direct projectile
    /// released within 1 s deals 3 (5) more direct damage on its hit; a second weapon hit inside that second cancels the
    /// setup (the hit after it starts a new one). A projectile that strikes on its release tick (deliveries advance before
    /// the effect dispatch, so before the activation is recorded) is boosted when the record's proc budget will accept the
    /// activation this tick (its own 0.5 s interval; each weapon hit opens one setup); the activation is then recorded at
    /// the dispatch point.
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class BladeAndBolt : IAugmentBehavior
    {
        public const string Id = "AUG-082";
        public const float WindowSec = 1f;     // "within 1 s"
        public const float Rank1Damage = 3f;   // "Projectile gains 3 direct damage."
        public const float Rank2Damage = 5f;   // "Projectile gains 5 direct damage."

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.Attach(new Hook(AugmentsAKit.ByRank(rank, Rank1Damage, Rank2Damage)));
        }

        private sealed class Hook : EffectHook
        {
            private readonly float _damage;
            private int _hitTick = -1;
            private int _hitEvent;
            private CastEventId _pending = CastEventId.None;
            private CastEventId _boosted = CastEventId.None;
            private int _lastActivation = int.MinValue / 2;

            public Hook(float damage) : base(Id)
            {
                _damage = damage;
            }

            /// <summary>The budget will accept this record's activation now: its group's 0.5 s interval has run (spec L126).</summary>
            private bool BudgetOpen => Now - _lastActivation >= ProcBudget.IntervalTicks;

            public override void OnEvent(DuelEvent e)
            {
                if (e is DamageEvent d && AugmentsAKit.IsWeaponHit(d, Player))
                {
                    if (_hitTick >= 0 && Now - _hitTick <= AugmentsAKit.Ticks(WindowSec))
                    {
                        _hitTick = -1; // "Second weapon hit before projectile resets setup."
                    }
                    else
                    {
                        _hitTick = d.Tick;
                        _hitEvent = d.Id;
                    }
                }
                else if (e is CastDuelEvent c && c.Player == Player && c.Kind == CastEventKind.Released && c.Cast.Slot >= 0
                    && _hitTick >= 0 && Now - _hitTick <= AugmentsAKit.Ticks(WindowSec) && CastFacts.IsDirectProjectile(AugmentsAKit.FormOf(Self, c.SpellId)))
                {
                    _pending = c.CastId;
                    _hitTick = -1;
                }
            }

            public override void OnTick()
            {
                if (_pending.IsValid)
                {
                    CastEventId cast = _pending;
                    _pending = CastEventId.None;
                    if (InCombat && TryActivate(_hitEvent, 1 - Player, _damage))
                    {
                        _boosted = cast;
                        _lastActivation = Now;
                    }
                }

                if (_hitTick >= 0 && Now - _hitTick > AugmentsAKit.Ticks(WindowSec)) _hitTick = -1;
            }

            public override void AdjustOutgoing(DamageRequest hit, float range, ref float fraction, ref float flat)
            {
                bool boosted = _boosted.IsValid && hit.CastId == _boosted;
                bool releasedThisTick = _pending.IsValid && hit.CastId == _pending && InCombat && BudgetOpen; // a release-tick impact
                if ((boosted || releasedThisTick) && ProjectileForms.IsProjectileDirectHit(World, hit))
                {
                    flat += _damage;
                }
            }

            public override void Hash(ref StateHasher h)
            {
                h.Add(_hitTick);
                h.Add(_hitEvent);
                h.Add((ulong)_pending.Packed);
                h.Add((ulong)_boosted.Packed);
                h.Add(_lastActivation);
            }
        }
    }

    /// <summary>
    /// AUG-088 Cover Duelist (Gold, melee and terrain items equipped). Condition "Strike enemy while within 2 m of solid
    /// cover." Rank 1 "Damage increases 5%.", rank 2 "Damage increases 8%." Drawback "Weapon strikes in open ground cost 1
    /// extra stamina." The owner's weapon hits deal 5% (8%) more while a vertical solid face (wall, cover, pillar, prop) is
    /// within 2 m of its body; a strike begun with no such face within 2 m costs 1 more stamina (spec L100).
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class CoverDuelist : IAugmentBehavior
    {
        public const string Id = "AUG-088";
        public const float CoverRange = 2f;    // "within 2 m of solid cover"
        public const float Rank1Bonus = 0.05f; // "Damage increases 5%."
        public const float Rank2Bonus = 0.08f; // "Damage increases 8%."
        public const float OpenStamina = 1f;   // "cost 1 extra stamina"

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

            private bool NearCover => AugmentsAKit.NearVerticalSolid(World.Collision, Self.Position, CoverRange, out _);

            public override MeleeProfile AdjustStrike(MeleeProfile profile) =>
                NearCover ? profile : AugmentsAKit.With(profile, stamina: OpenStamina);

            public override void AdjustOutgoing(DamageRequest hit, float range, ref float fraction, ref float flat)
            {
                if (hit.Kind == DamageKind.Melee && NearCover)
                {
                    fraction += _bonus;
                }
            }
        }
    }
}
