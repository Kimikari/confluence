using System;
using Confluence.Casting;
using Confluence.Core.Content;
using Confluence.Core.Rules;
using Confluence.Sim.Casting;
using Confluence.Sim.Combat;
using Confluence.Sim.Duel;
using Confluence.Sim.Effects;
using Confluence.Sim.Hashing;
using Confluence.Sim.Movement;
using Confluence.Sim.Players;

namespace Confluence.Sim.Content.Augments.AugmentsA
{
    // Shared augments of the "Melee and guard" family (spec L100-L106 common melee and guard; L126 proc budget).
    // Every record attaches one effect host part per player per duel; rank 2 replaces the rank 1 amount and drawbacks
    // apply at both ranks (D-164).

    /// <summary>
    /// AUG-073 Close Curriculum (Silver, always). Condition "Direct weapon hit within 2 m." Rank 1 "Damage increases 4%.",
    /// rank 2 "Damage increases 7%." Drawback "Direct spell damage beyond 15 m decreases 3%." Ranges are the
    /// attacker-to-target distance of the hit (spec L535 range wording); the bonus multiplies the weapon hit before the
    /// pipeline like the weapon damage stat.
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class CloseCurriculum : IAugmentBehavior
    {
        public const string Id = "AUG-073";
        public const float Rank1Bonus = 0.04f;      // "Damage increases 4%."
        public const float Rank2Bonus = 0.07f;      // "Damage increases 7%."
        public const float CloseRange = 2f;         // "within 2 m"
        public const float FarRange = 15f;          // "beyond 15 m"
        public const float FarSpellPenalty = 0.03f; // "Direct spell damage beyond 15 m decreases 3%."

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
                if (hit.Kind == DamageKind.Melee && range <= CloseRange)
                {
                    fraction += _bonus;
                }
                else if (hit.Kind == DamageKind.Direct && hit.IsSpellHit && range > FarRange)
                {
                    fraction -= FarSpellPenalty;
                }
            }
        }
    }

    /// <summary>
    /// AUG-074 Light Footwork (Silver, always). Condition "Use light blade profile." Rank 1 "Strike stamina cost decreases
    /// 1.", rank 2 "Strike stamina cost decreases 2." Drawback "Strike damage decreases 1." Applied to the light-blade
    /// strike profile at strike begin (spec L100: 28 damage, 12 stamina); other profiles are unchanged.
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class LightFootwork : IAugmentBehavior
    {
        public const string Id = "AUG-074";
        public const float Rank1Stamina = 1f; // "Strike stamina cost decreases 1."
        public const float Rank2Stamina = 2f; // "Strike stamina cost decreases 2."
        public const float DamagePenalty = 1f; // "Strike damage decreases 1."

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.Attach(new ProfileStrikeHook(Id, MeleeProfile.LightBlade, -AugmentsAKit.ByRank(rank, Rank1Stamina, Rank2Stamina), -DamagePenalty, 0f));
        }
    }

    /// <summary>
    /// AUG-075 Heavy Economy (Silver, always). Condition "Use heavy weapon profile." Rank 1 "Strike stamina cost decreases
    /// 2.", rank 2 "Strike stamina cost decreases 3." Drawback "Windup increases 0.05 s." (the 0.35 s heavy windup, spec
    /// L102; the cycle lengthens by the same amount so the recovery is unchanged).
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class HeavyEconomy : IAugmentBehavior
    {
        public const string Id = "AUG-075";
        public const float Rank1Stamina = 2f;     // "Strike stamina cost decreases 2."
        public const float Rank2Stamina = 3f;     // "Strike stamina cost decreases 3."
        public const float WindupPenaltySec = 0.05f; // "Windup increases 0.05 s."

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.Attach(new ProfileStrikeHook(Id, MeleeProfile.Heavy, -AugmentsAKit.ByRank(rank, Rank1Stamina, Rank2Stamina), 0f, WindupPenaltySec));
        }
    }

    /// <summary>
    /// AUG-077 Buttstrike Lesson (Silver, always). Condition "Use wand or staff common strike." Rank 1 "Damage increases
    /// 1.", rank 2 "Damage increases 2." Drawback "Strike stamina cost increases 1." The wand/staff butt strike is the
    /// focus profile (spec L100: 18 damage, 8 stamina), which an empty or issued Main Focus also uses (L571).
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class ButtstrikeLesson : IAugmentBehavior
    {
        public const string Id = "AUG-077";
        public const float Rank1Damage = 1f;     // "Damage increases 1."
        public const float Rank2Damage = 2f;     // "Damage increases 2."
        public const float StaminaPenalty = 1f;  // "Strike stamina cost increases 1."

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.Attach(new ProfileStrikeHook(Id, MeleeProfile.Focus, StaminaPenalty, AugmentsAKit.ByRank(rank, Rank1Damage, Rank2Damage), 0f));
        }
    }

    /// <summary>Changes one melee profile's damage, stamina, and windup at strike begin.</summary>
    internal sealed class ProfileStrikeHook : EffectHook
    {
        private readonly MeleeProfile _kind;
        private readonly float _stamina;
        private readonly float _damage;
        private readonly float _windup;

        public ProfileStrikeHook(string sourceId, MeleeProfile kind, float stamina, float damage, float windupSec) : base(sourceId)
        {
            _kind = kind;
            _stamina = stamina;
            _damage = damage;
            _windup = windupSec;
        }

        public override MeleeProfile AdjustStrike(MeleeProfile profile) =>
            AugmentsAKit.IsProfile(profile, _kind) ? AugmentsAKit.With(profile, damage: _damage, stamina: _stamina, windupSec: _windup) : profile;
    }

    /// <summary>
    /// AUG-076 Spear Poise (Silver, always). Condition "Spear strike begins while grounded without sprint." Rank 1 "Reach
    /// increases 0.1 m.", rank 2 "Reach increases 0.2 m." Drawback "Movement during windup is 5% slower." The reach
    /// applies to a spear strike that begins on the ground while not sprinting (spec L80: sprint already forbids a normal
    /// strike); the 5% walking penalty applies on every motor step of a spear windup (spec L102: 0.25 s) and shares
    /// the +20% movement cap arithmetic with other walking modifiers (L124).
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class SpearPoise : IAugmentBehavior
    {
        public const string Id = "AUG-076";
        public const float Rank1Reach = 0.1f;        // "Reach increases 0.1 m."
        public const float Rank2Reach = 0.2f;        // "Reach increases 0.2 m."
        public const float WindupMovePenalty = 0.05f; // "Movement during windup is 5% slower."

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.Attach(new Hook(AugmentsAKit.ByRank(rank, Rank1Reach, Rank2Reach)));
        }

        private sealed class Hook : EffectHook
        {
            private readonly float _reach;

            public Hook(float reach) : base(Id)
            {
                _reach = reach;
            }

            public override MeleeProfile AdjustStrike(MeleeProfile profile)
            {
                if (!AugmentsAKit.IsProfile(profile, MeleeProfile.Spear) || !Self.Motor.Grounded || Self.Motor.Sprinting)
                {
                    return profile;
                }

                return AugmentsAKit.With(profile, reach: _reach);
            }

            public override void AdjustGait(in MotorCommand command, ref GaitAdjust gait)
            {
                MeleeState m = Self.Melee;
                if (m.Phase == MeleePhase.Windup && AugmentsAKit.IsProfile(m.Profile, MeleeProfile.Spear))
                {
                    gait.Walk -= WindupMovePenalty; // a penalty: applies after the +20% cap with the other gait changes
                }
            }
        }
    }

    /// <summary>
    /// AUG-078 Guard Economy (Silver, always). Condition "Use ordinary directional guard." Rank 1 "Upfront stamina cost
    /// decreases 2.", rank 2 "Upfront stamina cost decreases 4." Drawback "Guard cooldown increases 0.2 s." (spec L104:
    /// 25 stamina upfront, 3 s cooldown from release; the 0.5 s minimum recharge still applies, L124).
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class GuardEconomy : IAugmentBehavior
    {
        public const string Id = "AUG-078";
        public const float Rank1Stamina = 2f;         // "Upfront stamina cost decreases 2."
        public const float Rank2Stamina = 4f;         // "Upfront stamina cost decreases 4."
        public const float CooldownPenaltySec = 0.2f; // "Guard cooldown increases 0.2 s."

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.AddModifier(new StatModifier(StatKind.GuardStaminaCost, -AugmentsAKit.ByRank(rank, Rank1Stamina, Rank2Stamina), Id));
            host.AddModifier(new StatModifier(StatKind.GuardCooldownSec, CooldownPenaltySec, Id));
        }
    }

    /// <summary>
    /// AUG-080 Guarded Study (Silver, always). Condition "Perfect guard then paid spell within 0.5 s." Rank 1 "Spell
    /// costs 2 less mana.", rank 2 "Spell costs 4 less mana." Drawback "That spell has 0.05 s longer recovery." A
    /// perfect guard (spec L104) opens a 0.5 s window; the first equipped spell paid inside it — the payment is made at
    /// commitment (L90, L525), so its commitment must fall within 0.5 s of the perfect guard — pays 2 (4) less mana inside
    /// the 70% floor (L124) and its recovery is 0.05 s longer. A preparation begun inside the window but committed after
    /// it pays in full. Quick grammars (a WIZ-002 tap commits 0.2 s after the press) fit after the guard's release window.
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class GuardedStudy : IAugmentBehavior
    {
        public const string Id = "AUG-080";
        public const float Rank1Mana = 2f;            // "Spell costs 2 less mana."
        public const float Rank2Mana = 4f;            // "Spell costs 4 less mana."
        public const float WindowSec = 0.5f;          // "within 0.5 s"
        public const float RecoveryPenaltySec = 0.05f; // "That spell has 0.05 s longer recovery."

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.Attach(new Hook(AugmentsAKit.ByRank(rank, Rank1Mana, Rank2Mana)));
        }

        private sealed class Hook : EffectHook
        {
            private readonly float _mana;
            private GuardEvent _perfect;
            private int _windowEnd = -1;

            public Hook(float mana) : base(Id)
            {
                _mana = mana;
            }

            public override void OnEvent(DuelEvent e)
            {
                if (e is GuardEvent g && g.Player == Player && g.Change == GuardChange.PerfectBlocked)
                {
                    _perfect = g;
                }
                else if (CastFacts.IsSpellCommit(e, Player, out CastDuelEvent c) && _windowEnd >= 0 && c.Tick <= _windowEnd && c.Cast.Mana > 0f)
                {
                    _windowEnd = -1; // the first paid spell inside the window uses it
                }
            }

            public override void OnTick()
            {
                if (_perfect != null)
                {
                    int origin = _perfect.BlockedEventId;
                    _perfect = null;
                    if (InCombat && TryActivate(origin))
                    {
                        _windowEnd = Now + AugmentsAKit.Ticks(WindowSec);
                    }
                }

                if (_windowEnd >= 0 && Now > _windowEnd)
                {
                    _windowEnd = -1;
                }
            }

            /// <summary>The cast being paid now (prices are taken at commitment) falls inside the window.</summary>
            private bool Armed(in CastQuote quote) =>
                _windowEnd >= 0 && Now <= _windowEnd && AugmentsAKit.CurrentCast(Self).IsValid
                && Self.Caster.Machine.CurrentSpell != null && !Self.Caster.Machine.CurrentSpell.IsSignature;

            public override void AdjustCastPrice(in CastQuote quote, ref float reduction, ref float increase)
            {
                if (Armed(quote))
                {
                    reduction += _mana;
                }
            }

            public override void AdjustCastTiming(in CastQuote quote, ref CastTimingAdjust timing)
            {
                if (Armed(quote))
                {
                    timing.RecoveryDeltaTicks += AugmentsAKit.Ticks(RecoveryPenaltySec);
                }
            }

            public override void Hash(ref StateHasher h)
            {
                h.Add(_windowEnd);
                h.Add(_perfect != null ? _perfect.Id : 0);
            }
        }
    }

    /// <summary>
    /// AUG-083 Bolt and Blade (Gold, always). Condition "Direct spell hit followed by weapon hit within 1 s." Rank 1
    /// "Weapon hit costs 2 less stamina.", rank 2 "Weapon hit costs 4 less stamina." Drawback "Missed weapon hit consumes
    /// opportunity." The owner's direct spell hit (spec L102: common melee is not a spell) opens a 1 s opportunity; the
    /// first common strike begun inside it pays 2 (4) less stamina, and that strike uses the opportunity whether it
    /// hits or misses.
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class BoltAndBlade : IAugmentBehavior
    {
        public const string Id = "AUG-083";
        public const float Rank1Stamina = 2f; // "Weapon hit costs 2 less stamina."
        public const float Rank2Stamina = 4f; // "Weapon hit costs 4 less stamina."
        public const float WindowSec = 1f;    // "within 1 s"

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.Attach(new Hook(AugmentsAKit.ByRank(rank, Rank1Stamina, Rank2Stamina)));
        }

        private sealed class Hook : EffectHook
        {
            private readonly float _stamina;
            private DamageEvent _spellHit;
            private int _openUntil = -1;

            public Hook(float stamina) : base(Id)
            {
                _stamina = stamina;
            }

            public override void OnEvent(DuelEvent e)
            {
                if (e is DamageEvent d && AugmentsAKit.IsDirectSpellHit(d, Player))
                {
                    _spellHit = d;
                }
                else if (e is MeleeEvent m && m.Player == Player && m.Change == MeleeChange.Started && _openUntil >= 0)
                {
                    _openUntil = -1; // the strike begun inside the window uses the opportunity, hit or miss
                }
            }

            public override void OnTick()
            {
                if (_spellHit != null)
                {
                    DamageEvent hit = _spellHit;
                    _spellHit = null;
                    if (InCombat && TryActivate(hit.OriginEventId, hit.Target))
                    {
                        _openUntil = hit.Tick + AugmentsAKit.Ticks(WindowSec);
                    }
                }

                if (_openUntil >= 0 && Now >= _openUntil)
                {
                    _openUntil = -1;
                }
            }

            public override MeleeProfile AdjustStrike(MeleeProfile profile) =>
                _openUntil >= 0 && Now <= _openUntil ? AugmentsAKit.With(profile, stamina: -_stamina) : profile;

            public override void Hash(ref StateHasher h) => h.Add(_openUntil);
        }
    }

    /// <summary>
    /// AUG-084 Walking Pressure (Gold, always). Condition "Weapon hit lands while walking, not sprinting." Rank 1 "Gain 3%
    /// walk speed for 1 s.", rank 2 "Gain 5% walk speed for 1 s." Drawback "4 s cooldown; sprint cancels buff." The
    /// walking gait gains the bonus under the shared +20% cap (spec L124); starting a sprint ends it at once.
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class WalkingPressure : IAugmentBehavior
    {
        public const string Id = "AUG-084";
        public const float Rank1Speed = 0.03f; // "Gain 3% walk speed for 1 s."
        public const float Rank2Speed = 0.05f; // "Gain 5% walk speed for 1 s."
        public const float DurationSec = 1f;   // "for 1 s"
        public const float CooldownSec = 4f;   // "4 s cooldown"

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.Attach(new Hook(AugmentsAKit.ByRank(rank, Rank1Speed, Rank2Speed)));
        }

        private sealed class Hook : EffectHook
        {
            private readonly float _speed;
            private DamageEvent _hit;
            private bool _hitWhileWalking;
            private int _lastActivation = int.MinValue / 2;
            private int _buffUntil = -1;

            public Hook(float speed) : base(Id)
            {
                _speed = speed;
            }

            /// <summary>Tick the walk-speed bonus ends (exclusive), or -1.</summary>
            public int BuffUntil => _buffUntil;

            public override void OnEvent(DuelEvent e)
            {
                if (e is DamageEvent d && AugmentsAKit.IsWeaponHit(d, Player))
                {
                    _hit = d;
                    _hitWhileWalking = AugmentsAKit.IsWalking(Self);
                }
            }

            public override void OnTick()
            {
                if (_hit != null)
                {
                    DamageEvent hit = _hit;
                    _hit = null;
                    if (InCombat && _hitWhileWalking && Now - _lastActivation >= AugmentsAKit.Ticks(CooldownSec) && TryActivate(hit.OriginEventId, hit.Target))
                    {
                        _lastActivation = Now;
                        _buffUntil = Now + AugmentsAKit.Ticks(DurationSec);
                    }
                }

                if (_buffUntil >= 0 && (Self.Motor.Sprinting || Now >= _buffUntil))
                {
                    _buffUntil = -1; // "sprint cancels buff"
                }
            }

            public override void AdjustGait(in MotorCommand command, ref GaitAdjust gait)
            {
                if (_buffUntil >= 0 && Now < _buffUntil)
                {
                    gait.Walk += _speed; // under the shared +20% cap with every other bonus
                }
            }

            public override void Hash(ref StateHasher h)
            {
                h.Add(_lastActivation);
                h.Add(_buffUntil);
            }
        }
    }

    /// <summary>
    /// AUG-085 Guard Breaker (Gold, always). Condition "Weapon hit impacts active enemy guard." Rank 1 "Remove 3 additional
    /// stamina.", rank 2 "Remove 5 additional stamina." Drawback "Unguarded-target weapon damage decreases 3%." A weapon
    /// hit the enemy's guard blocks (spec L104, perfect or ordinary) removes 3 (5) stamina from the enemy on top of the
    /// guard's own cost; it is an effect output, once per original hit and 0.5 s (L126), and never spends the enemy's
    /// stamina (no regeneration delay, L78). Weapon hits the target's guard does not block deal 3% less.
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class GuardBreaker : IAugmentBehavior
    {
        public const string Id = "AUG-085";
        public const float Rank1Drain = 3f;           // "Remove 3 additional stamina."
        public const float Rank2Drain = 5f;           // "Remove 5 additional stamina."
        public const float UnguardedPenalty = 0.03f;  // "Unguarded-target weapon damage decreases 3%."

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.Attach(new Hook(AugmentsAKit.ByRank(rank, Rank1Drain, Rank2Drain)));
        }

        private sealed class Hook : EffectHook
        {
            private readonly float _drain;
            private DamageEvent _guarded;

            public Hook(float drain) : base(Id)
            {
                _drain = drain;
            }

            public override void OnEvent(DuelEvent e)
            {
                if (e is DamageEvent d && d.Attacker == Player && d.Target == 1 - Player && d.Kind == DamageKind.Melee && d.Generation == 0
                    && d.Resolution != null && d.Resolution.Guard != GuardOutcome.None)
                {
                    _guarded = d;
                }
            }

            public override void OnTick()
            {
                if (_guarded == null)
                {
                    return;
                }

                DamageEvent hit = _guarded;
                _guarded = null;
                if (InCombat && TryActivate(hit.OriginEventId, hit.Target, _drain))
                {
                    World.Player(hit.Target).Resources.LoseStamina(_drain);
                }
            }

            public override void AdjustOutgoing(DamageRequest hit, float range, ref float fraction, ref float flat)
            {
                if (hit.Kind == DamageKind.Melee && DamagePipeline.GuardOutcomeFor(hit, World.Player(hit.Target), Now) == GuardOutcome.None)
                {
                    fraction -= UnguardedPenalty;
                }
            }
        }
    }

    /// <summary>
    /// AUG-086 Shield Cut (Gold, always). Condition "Direct weapon hit damages a shield." Rank 1 "Shield-only damage
    /// increases 5.", rank 2 "Shield-only damage increases 8." Drawback "Unshielded player damage decreases 2%." A weapon
    /// hit whose damage a shield absorbed (spec L112 step 6) removes up to 5 (8) more shield capacity from the shields
    /// that covered it, never health; it is an effect output once per original hit and 0.5 s (L126). The owner's
    /// original hits on an opponent without any shield deal 2% less.
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class ShieldCut : IAugmentBehavior
    {
        public const string Id = "AUG-086";
        public const float Rank1ShieldDamage = 5f;  // "Shield-only damage increases 5."
        public const float Rank2ShieldDamage = 8f;  // "Shield-only damage increases 8."
        public const float UnshieldedPenalty = 0.02f; // "Unshielded player damage decreases 2%."

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.Attach(new Hook(AugmentsAKit.ByRank(rank, Rank1ShieldDamage, Rank2ShieldDamage)));
        }

        private sealed class Hook : EffectHook
        {
            private readonly float _extra;
            private DamageEvent _shieldHit;
            private Confluence.Core.Geometry.Vec3 _source;

            public Hook(float extra) : base(Id)
            {
                _extra = extra;
            }

            public override void OnEvent(DuelEvent e)
            {
                if (e is DamageEvent d && AugmentsAKit.IsWeaponHit(d, Player) && d.Resolution.ShieldAbsorbed > 0f)
                {
                    _shieldHit = d;
                    _source = Self.Position;
                }
            }

            public override void OnTick()
            {
                if (_shieldHit == null)
                {
                    return;
                }

                DamageEvent hit = _shieldHit;
                _shieldHit = null;
                DuelPlayer target = World.Player(hit.Target);
                if (!InCombat || target.Shields.Total <= 0f || !TryActivate(hit.OriginEventId, hit.Target))
                {
                    return;
                }

                var contact = new ShieldContact { HasSource = true, Source = _source, Defender = target.Position, DefenderYawDeg = target.Motor.YawDeg };
                float removed = target.Shields.Absorb(_extra, contact); // shield capacity only; health is never touched
                if (removed > 0f)
                {
                    EmitCue("shield-cut", removed);
                }
            }

            public override void AdjustOutgoing(DamageRequest hit, float range, ref float fraction, ref float flat)
            {
                if (World.Player(hit.Target).Shields.Total <= 0f)
                {
                    fraction -= UnshieldedPenalty;
                }
            }
        }
    }

    /// <summary>
    /// AUG-087 Measured Follow-Through (Gold, always). Condition "You miss a weapon strike, then use a different spell
    /// within 0.8 s." Rank 1 "Spell costs 2 less mana, 4 s cooldown.", rank 2 "Spell costs 3 less mana, 4 s cooldown."
    /// Drawback "Weapon recovery still applies; no cancellation." A missed common strike (its live window ended without
    /// hitting, spec L102) opens 0.8 s in which the first spell the owner commits pays 2 (3) less mana inside the 70%
    /// floor (L124). "Different" contrasts the spell with the weapon action. The strike's recovery still blocks casting
    /// (L92, L106), so a heavy weapon's long recovery can use up the window.
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class MeasuredFollowThrough : IAugmentBehavior
    {
        public const string Id = "AUG-087";
        public const float Rank1Mana = 2f;   // "Spell costs 2 less mana, 4 s cooldown."
        public const float Rank2Mana = 3f;   // "Spell costs 3 less mana, 4 s cooldown."
        public const float WindowSec = 0.8f; // "within 0.8 s"
        public const float CooldownSec = 4f; // "4 s cooldown"

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            var miss = new Effect(Id, EffectTrigger.MeleeMiss)
            {
                Label = "missed strike opens a cheaper spell",
                InternalCooldownTicks = AugmentsAKit.Ticks(CooldownSec),
            };
            miss.Actions.Add(new NextCastCostAction(AugmentsAKit.ByRank(rank, Rank1Mana, Rank2Mana), 0f, AugmentsAKit.Ticks(WindowSec)));
            host.Attach(miss);
        }
    }

    /// <summary>
    /// AUG-089 Patient Edge (Prismatic, always). Condition "No weapon attack for 2 s before next strike." Rank 1 "Next
    /// strike gains 3 damage.", rank 2 "Next strike gains 5 damage." Drawback "A miss spends the preparation." A common
    /// strike begun at least 2 s after the owner's previous strike began deals 3 (5) more damage; every strike, hit or
    /// miss, restarts the 2 s preparation.
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class PatientEdge : IAugmentBehavior
    {
        public const string Id = "AUG-089";
        public const float Rank1Damage = 3f;  // "Next strike gains 3 damage."
        public const float Rank2Damage = 5f;  // "Next strike gains 5 damage."
        public const float QuietSec = 2f;     // "No weapon attack for 2 s"

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.Attach(new Hook(AugmentsAKit.ByRank(rank, Rank1Damage, Rank2Damage)));
        }

        private sealed class Hook : EffectHook
        {
            private readonly float _damage;
            private int _lastStrike = int.MinValue / 2;

            public Hook(float damage) : base(Id)
            {
                _damage = damage;
            }

            public override void OnEvent(DuelEvent e)
            {
                if (e is MeleeEvent m && m.Player == Player && m.Change == MeleeChange.Started)
                {
                    _lastStrike = m.Tick;
                }
            }

            public override MeleeProfile AdjustStrike(MeleeProfile profile) =>
                Now - _lastStrike >= AugmentsAKit.Ticks(QuietSec) ? AugmentsAKit.With(profile, damage: _damage) : profile;

            public override void Hash(ref StateHasher h) => h.Add(_lastStrike);
        }
    }

    /// <summary>
    /// AUG-090 Desperate Guard (Prismatic, always). Condition "Guard below 90 health." Rank 1 "Cost decreases 3 stamina.",
    /// rank 2 "Cost decreases 5 stamina." Drawback "Guard duration decreases 0.1 s." Below 90 health the 25 upfront guard
    /// stamina (spec L104) costs 3 (5) less; the 0.8 s guard hold is 0.1 s shorter at all times (the hold never drops
    /// below the 0.12 s perfect window).
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class DesperateGuard : IAugmentBehavior
    {
        public const string Id = "AUG-090";
        public const float HealthThreshold = 90f; // "below 90 health"
        public const float Rank1Stamina = 3f;     // "Cost decreases 3 stamina."
        public const float Rank2Stamina = 5f;     // "Cost decreases 5 stamina."
        public const float HoldPenaltySec = 0.1f; // "Guard duration decreases 0.1 s."

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.Attach(new ConditionalModifier(
                Conditions.When("health below 90", (in EffectContext c) => AugmentsAKit.HealthBelow(c.Self, HealthThreshold)),
                new StatModifier(StatKind.GuardStaminaCost, -AugmentsAKit.ByRank(rank, Rank1Stamina, Rank2Stamina), Id)));
            host.AddModifier(new StatModifier(StatKind.GuardMaxHoldSec, -HoldPenaltySec, Id));
        }
    }

    /// <summary>
    /// AUG-092 Grand Riposte (Prismatic, always). Condition "Perfect guard, direct weapon hit, then a different paid spell
    /// within 2 s." Rank 1 "Spell gains 6% direct damage.", rank 2 "Spell gains 10% direct damage." Drawback "Any missed
    /// step resets sequence." In order inside 2 s of the perfect guard (spec L104): the owner's weapon hit lands, then
    /// the owner commits a paid spell; that cast's direct hits gain 6% (10%). A weapon strike that misses before the
    /// hit, or a spell committed before it, resets the sequence. "Different" contrasts the spell with the weapon step.
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class GrandRiposte : IAugmentBehavior
    {
        public const string Id = "AUG-092";
        public const float Rank1Bonus = 0.06f; // "Spell gains 6% direct damage."
        public const float Rank2Bonus = 0.10f; // "Spell gains 10% direct damage."
        public const float WindowSec = 2f;     // "within 2 s"

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.Attach(new Hook(AugmentsAKit.ByRank(rank, Rank1Bonus, Rank2Bonus)));
        }

        private sealed class Hook : EffectHook
        {
            private readonly float _bonus;
            private int _guardTick = -1;
            private int _guardEvent;
            private bool _weaponHit;
            private int _strikeStart = -1;
            private CastEventId _pendingCast = CastEventId.None;
            private CastEventId _boosted = CastEventId.None;

            public Hook(float bonus) : base(Id)
            {
                _bonus = bonus;
            }

            public override void OnEvent(DuelEvent e)
            {
                switch (e)
                {
                    case GuardEvent g when g.Player == Player && g.Change == GuardChange.PerfectBlocked:
                        _guardTick = g.Tick;
                        _guardEvent = g.Id;
                        _weaponHit = false;
                        _strikeStart = -1;
                        break;
                    case MeleeEvent m when m.Player == Player && m.Change == MeleeChange.Started && _guardTick >= 0 && !_weaponHit:
                        _strikeStart = m.Tick;
                        break;
                    case DamageEvent d when _guardTick >= 0 && !_weaponHit && AugmentsAKit.IsWeaponHit(d, Player):
                        _weaponHit = true;
                        break;
                    case CastDuelEvent c when c.Player == Player && c.Kind == CastEventKind.Committed && c.Cast.Slot >= 0 && _guardTick >= 0:
                        if (_weaponHit && c.Tick <= _guardTick + AugmentsAKit.Ticks(WindowSec))
                        {
                            _pendingCast = c.CastId;
                        }

                        _guardTick = -1; // the sequence ends with its spell, or resets when the spell came before the hit
                        break;
                }
            }

            public override void OnTick()
            {
                if (_pendingCast.IsValid)
                {
                    CastEventId cast = _pendingCast;
                    _pendingCast = CastEventId.None;
                    if (InCombat && TryActivate(_guardEvent))
                    {
                        _boosted = cast;
                    }
                }

                if (_guardTick < 0)
                {
                    return;
                }

                if (Now > _guardTick + AugmentsAKit.Ticks(WindowSec))
                {
                    _guardTick = -1; // the window ran out
                    return;
                }

                // A strike begun after the guard that finished its live window without the hit resets the sequence.
                MeleeState m = Self.Melee;
                if (!_weaponHit && _strikeStart >= 0 && m.StartTick == _strikeStart && Now >= m.StrikeEndTick && !m.HitOpponent)
                {
                    _guardTick = -1;
                }
            }

            public override void AdjustOutgoing(DamageRequest hit, float range, ref float fraction, ref float flat)
            {
                if (_boosted.IsValid && hit.CastId == _boosted && hit.Kind == DamageKind.Direct && hit.IsSpellHit)
                {
                    fraction += _bonus;
                }
            }

            public override void Hash(ref StateHasher h)
            {
                h.Add(_guardTick);
                h.Add(_weaponHit);
                h.Add(_strikeStart);
                h.Add((ulong)_boosted.Packed);
            }
        }
    }
}
