using System;
using Confluence.Core.Content;
using Confluence.Sim.Casting;
using Confluence.Sim.Combat;
using Confluence.Sim.Content.Augments.AugmentsA;
using Confluence.Sim.Duel;
using Confluence.Sim.Effects;
using Confluence.Sim.Hashing;
using Confluence.Sim.Movement;
using Confluence.Sim.Players;

namespace Confluence.Sim.Content.Augments.AugmentsB
{
    // Resource and recovery family (augments-b). AUG-145 Large Vessel is the Stage 1b proof record (Augments/SilverAugments.cs).

    /// <summary>
    /// AUG-146 Long Lungs (Silver, always). Condition "Always." Rank 1 "Maximum stamina increases 8.", rank 2 "Maximum stamina
    /// increases 12." Drawback "Maximum mana decreases 4." Static capacity (spec L78, L579).
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class LongLungs : IAugmentBehavior
    {
        public const string Id = "AUG-146";
        public const float Rank1Stamina = 8f;   // "Maximum stamina increases 8."
        public const float Rank2Stamina = 12f;  // "Maximum stamina increases 12."
        public const float ManaCut = 4f;        // "Maximum mana decreases 4."

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.AddModifier(new StatModifier(StatKind.MaxStaminaBonus, AugmentsBKit.ByRank(rank, Rank1Stamina, Rank2Stamina), Id));
            host.AddModifier(new StatModifier(StatKind.MaxManaBonus, -ManaCut, Id));
        }
    }

    /// <summary>
    /// AUG-147 Quiet Recharge (Silver, always). Condition "Ordinary mana regeneration is active." Rank 1 "Regeneration
    /// increases 6%.", rank 2 "Regeneration increases 10%." Drawback "Maximum health decreases 6." The ordinary mana
    /// regeneration (10/s after the 1.5 s delay, spec L78) runs 6% (10%) faster while it is active; the 14/s ceiling holds.
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class QuietRecharge : IAugmentBehavior
    {
        public const string Id = "AUG-147";
        public const float Rank1Regen = 0.06f;  // "Regeneration increases 6%."
        public const float Rank2Regen = 0.10f;  // "Regeneration increases 10%."
        public const float HealthCut = 6f;      // "Maximum health decreases 6."

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.AddModifier(new StatModifier(StatKind.ManaRegenPercent, AugmentsBKit.ByRank(rank, Rank1Regen, Rank2Regen), Id));
            host.AddModifier(new StatModifier(StatKind.MaxHealthBonus, -HealthCut, Id));
        }
    }

    /// <summary>
    /// AUG-148 Steady Recovery (Silver, always). Condition "Ordinary stamina regeneration is active." Rank 1 "Regeneration
    /// increases 8%.", rank 2 "Regeneration increases 12%." Drawback "Maximum mana decreases 4." Ordinary stamina regeneration
    /// (20/s after the 1 s delay, spec L78) runs 8% (12%) faster.
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class SteadyRecovery : IAugmentBehavior
    {
        public const string Id = "AUG-148";
        public const float Rank1Regen = 0.08f;  // "Regeneration increases 8%."
        public const float Rank2Regen = 0.12f;  // "Regeneration increases 12%."
        public const float ManaCut = 4f;        // "Maximum mana decreases 4."

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.AddModifier(new StatModifier(StatKind.StaminaRegenPercent, AugmentsBKit.ByRank(rank, Rank1Regen, Rank2Regen), Id));
            host.AddModifier(new StatModifier(StatKind.MaxManaBonus, -ManaCut, Id));
        }
    }

    /// <summary>
    /// AUG-149 First Savings (Silver, always). Condition "First paid cast each duel." Rank 1 "Cost decreases 2 mana.", rank 2
    /// "Cost decreases 4 mana." Drawback "Second paid cast costs 1 additional mana." The owner's first paid spell commitment of
    /// the duel costs 2 (4) less (inside the 70% floor, spec L124); the second costs 1 more (after the floor). A commitment
    /// that fails to pay is not a paid cast.
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class FirstSavings : IAugmentBehavior
    {
        public const string Id = "AUG-149";
        public const float Rank1Cut = 2f;      // "Cost decreases 2 mana."
        public const float Rank2Cut = 4f;      // "Cost decreases 4 mana."
        public const float SecondExtra = 1f;   // "Second paid cast costs 1 additional mana."

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.Attach(new Hook(AugmentsBKit.ByRank(rank, Rank1Cut, Rank2Cut)));
        }

        private sealed class Hook : RecordHook
        {
            private readonly float _cut;
            private int _paidCasts;

            public Hook(float cut) : base(Id)
            {
                _cut = cut;
            }

            public override void OnEvent(DuelEvent e)
            {
                if (AugmentsBKit.IsPaidSpellCommit(e, Player, out _)) _paidCasts++;
            }

            public override void AdjustCastPrice(in CastQuote quote, ref float reduction, ref float increase)
            {
                if (!AugmentsBKit.IsSpellQuote(quote)) return;
                if (_paidCasts == 0) reduction += _cut;
                else if (_paidCasts == 1) increase += SecondExtra;
            }

            public override void Hash(ref StateHasher h) => h.Add(_paidCasts);
        }
    }

    /// <summary>
    /// AUG-150 Last Reserve (Silver, always). Condition "Current mana below 20 before a paid cast." Rank 1 "Cost decreases 1
    /// mana.", rank 2 "Cost decreases 2 mana." Drawback "Maximum mana decreases 5; costs cannot become negative." A spell
    /// committed while the owner holds less than 20 mana costs 1 (2) less; every reduction stays inside the 70% floor, so a
    /// cost never becomes negative (spec L124).
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class LastReserve : IAugmentBehavior
    {
        public const string Id = "AUG-150";
        public const float Threshold = 20f;    // "Current mana below 20"
        public const float Rank1Cut = 1f;      // "Cost decreases 1 mana."
        public const float Rank2Cut = 2f;      // "Cost decreases 2 mana."
        public const float ManaCut = 5f;       // "Maximum mana decreases 5"

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.AddModifier(new StatModifier(StatKind.MaxManaBonus, -ManaCut, Id));
            host.Attach(new Hook(AugmentsBKit.ByRank(rank, Rank1Cut, Rank2Cut)));
        }

        private sealed class Hook : RecordHook
        {
            private readonly float _cut;

            public Hook(float cut) : base(Id)
            {
                _cut = cut;
            }

            public override void AdjustCastPrice(in CastQuote quote, ref float reduction, ref float increase)
            {
                if (AugmentsBKit.IsSpellQuote(quote) && Self.Resources.Mana < Threshold) reduction += _cut;
            }
        }
    }

    /// <summary>
    /// AUG-151 Clean Rotation (Silver, always). Condition "Cast a different spell than your previous paid cast." Rank 1 "Cost
    /// decreases 1 mana.", rank 2 "Cost decreases 2 mana." Drawback "Repeated spell costs 1 extra mana." A spell committed after
    /// a paid cast of a different spell costs 1 (2) less; the same spell twice in a row costs 1 more. The first paid cast of
    /// the duel has no previous cast and is neither.
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class CleanRotation : IAugmentBehavior
    {
        public const string Id = "AUG-151";
        public const float Rank1Cut = 1f;      // "Cost decreases 1 mana."
        public const float Rank2Cut = 2f;      // "Cost decreases 2 mana."
        public const float RepeatExtra = 1f;   // "Repeated spell costs 1 extra mana."

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.Attach(new RotationHook(Id, AugmentsBKit.ByRank(rank, Rank1Cut, Rank2Cut), RepeatExtra));
        }
    }

    /// <summary>
    /// A price hook over the owner's previous paid spell: a different spell is cheaper by <c>different</c>, a repeat costs
    /// <c>repeat</c> more (AUG-151; the repeat drawbacks of AUG-250 and AUG-269 use it with no reduction).
    /// </summary>
    internal sealed class RotationHook : RecordHook
    {
        private readonly float _different;
        private readonly float _repeat;
        private string _previous;

        public RotationHook(string id, float different, float repeat) : base(id)
        {
            _different = different;
            _repeat = repeat;
        }

        public string Previous => _previous;

        public override void OnEvent(DuelEvent e)
        {
            if (AugmentsBKit.IsPaidSpellCommit(e, Player, out CastDuelEvent c)) _previous = c.SpellId;
        }

        public override void AdjustCastPrice(in CastQuote quote, ref float reduction, ref float increase)
        {
            if (!AugmentsBKit.IsSpellQuote(quote) || _previous == null) return;
            if (string.Equals(_previous, quote.SpellId, StringComparison.Ordinal)) increase += _repeat;
            else reduction += _different;
        }

        public override void Hash(ref StateHasher h) => h.Add(_previous ?? "");
    }

    /// <summary>
    /// AUG-152 Walking Rest (Silver, always). Condition "Walk without sprint, dodge or cast for 1 s." Rank 1 "Stamina
    /// regeneration delay decreases 0.1 s.", rank 2 "Delay decreases 0.2 s." Drawback "Maximum stamina decreases 4." After 1 s
    /// of continuous walking (grounded, not sprinting, moving; augments-a's walking test) with no dodge and no cast in any
    /// phase, the 1 s stamina regeneration delay (spec L78) is 0.1 (0.2) s shorter until the walk is broken — so a stamina cost
    /// paid during a calm walk (a guard, a strike) starts regenerating sooner. Sprinting, dodging, a cast, a jump or standing
    /// still restarts the 1 s.
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class WalkingRest : IAugmentBehavior
    {
        public const string Id = "AUG-152";
        public const float WalkSec = 1f;        // "for 1 s"
        public const float Rank1Delay = 0.1f;   // "Stamina regeneration delay decreases 0.1 s."
        public const float Rank2Delay = 0.2f;   // "Delay decreases 0.2 s."
        public const float StaminaCut = 4f;     // "Maximum stamina decreases 4."

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.AddModifier(new StatModifier(StatKind.MaxStaminaBonus, -StaminaCut, Id));
            host.Attach(new Hook(AugmentsBKit.ByRank(rank, Rank1Delay, Rank2Delay)));
        }

        private sealed class Hook : RecordHook
        {
            private readonly float _delayCut;
            private int _walkTicks;
            private bool _present;

            public Hook(float delayCut) : base(Id)
            {
                _delayCut = delayCut;
            }

            private string Source => Key + ":delay";

            public override void OnTick()
            {
                DuelPlayer p = Self;
                bool calm = InCombat && AugmentsAKit.IsWalking(p) && p.Motor.DodgeTicks == 0 && !AugmentsBKit.IsCasting(World, Player);
                _walkTicks = calm ? _walkTicks + 1 : 0;
                bool rested = _walkTicks >= AugmentsBKit.Ticks(WalkSec);
                if (rested == _present) return;
                if (rested) p.Stats.Add(new StatModifier(StatKind.StaminaRegenDelaySec, -_delayCut, Source));
                else p.Stats.RemoveSource(Source);
                _present = rested;
            }

            public override void Hash(ref StateHasher h)
            {
                h.Add(_walkTicks);
                h.Add(_present);
            }
        }
    }

    /// <summary>
    /// AUG-154 Melee Recharge (Gold, always). Condition "Direct weapon hit while mana below 30." Rank 1 "Gain 2 mana, 3 s
    /// cooldown.", rank 2 "Gain 3 mana, 3 s cooldown." Drawback "Weapon hit costs 1 extra stamina." A common melee hit on the
    /// opponent while the owner holds less than 30 mana grants 2 (3) mana within the maximum, at most once per 3 s and through
    /// the proc budget (spec L126). Every weapon hit of the owner removes 1 stamina when it lands.
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class MeleeRecharge : IAugmentBehavior
    {
        public const string Id = "AUG-154";
        public const float Threshold = 30f;     // "while mana below 30"
        public const float Rank1Mana = 2f;      // "Gain 2 mana, 3 s cooldown."
        public const float Rank2Mana = 3f;      // "Gain 3 mana, 3 s cooldown."
        public const float CooldownSec = 3f;
        public const float HitStamina = 1f;     // "Weapon hit costs 1 extra stamina."

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.Attach(new Hook(AugmentsBKit.ByRank(rank, Rank1Mana, Rank2Mana)));
        }

        private sealed class Hook : RecordHook
        {
            private readonly float _mana;
            private int _hitEvent;
            private int _hitOrigin;
            private bool _qualifies;
            private int _last = int.MinValue / 2;

            public Hook(float mana) : base(Id)
            {
                _mana = mana;
            }

            public override void OnEvent(DuelEvent e)
            {
                if (e is DamageEvent d && AugmentsAKit.IsWeaponHit(d, Player) && _hitEvent == 0)
                {
                    _hitEvent = d.Id;
                    _hitOrigin = d.OriginEventId;
                    _qualifies = Self.Resources.Mana < Threshold; // judged at the hit
                }
            }

            public override void OnTick()
            {
                if (_hitEvent == 0) return;
                bool qualifies = _qualifies;
                int origin = _hitOrigin;
                _hitEvent = 0;
                AugmentsBKit.SpendStamina(World, Player, HitStamina); // the drawback applies to every weapon hit
                if (qualifies && Ready(_last, CooldownSec) && TryActivate(origin, 1 - Player, _mana))
                {
                    _last = Now;
                    GainMana(_mana);
                }
            }

            public override void Hash(ref StateHasher h)
            {
                h.Add(_hitEvent);
                h.Add(_hitOrigin);
                h.Add(_qualifies);
                h.Add(_last);
            }
        }
    }

    /// <summary>
    /// AUG-155 Spell Recharge (Gold, always). Condition "Direct spell hit while stamina below 30." Rank 1 "Gain 2 stamina, 3 s
    /// cooldown.", rank 2 "Gain 3 stamina, 3 s cooldown." Drawback "Spell costs 1 extra mana." A direct spell impact on the
    /// opponent while the owner holds less than 30 stamina grants 2 (3) stamina, at most once per 3 s (proc budget, L126).
    /// Every paid spell of the owner costs 1 more mana (after the 70% floor, L124).
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class SpellRecharge : IAugmentBehavior
    {
        public const string Id = "AUG-155";
        public const float Threshold = 30f;     // "while stamina below 30"
        public const float Rank1Stamina = 2f;   // "Gain 2 stamina, 3 s cooldown."
        public const float Rank2Stamina = 3f;   // "Gain 3 stamina, 3 s cooldown."
        public const float CooldownSec = 3f;
        public const float SpellExtra = 1f;     // "Spell costs 1 extra mana."

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.Attach(new Hook(AugmentsBKit.ByRank(rank, Rank1Stamina, Rank2Stamina)));
        }

        private sealed class Hook : RecordHook
        {
            private readonly float _stamina;
            private int _hitEvent;
            private int _hitOrigin;
            private int _last = int.MinValue / 2;

            public Hook(float stamina) : base(Id)
            {
                _stamina = stamina;
            }

            public override void OnEvent(DuelEvent e)
            {
                if (e is DamageEvent d && AugmentsAKit.IsDirectSpellHit(d, Player) && _hitEvent == 0 && Self.Resources.Stamina < Threshold)
                {
                    _hitEvent = d.Id;
                    _hitOrigin = d.OriginEventId;
                }
            }

            public override void OnTick()
            {
                if (_hitEvent == 0) return;
                int origin = _hitOrigin;
                _hitEvent = 0;
                if (Ready(_last, CooldownSec) && TryActivate(origin, 1 - Player, _stamina))
                {
                    _last = Now;
                    GainStamina(_stamina);
                }
            }

            public override void AdjustCastPrice(in CastQuote quote, ref float reduction, ref float increase)
            {
                if (AugmentsBKit.IsSpellQuote(quote)) increase += SpellExtra;
            }

            public override void Hash(ref StateHasher h)
            {
                h.Add(_hitEvent);
                h.Add(_hitOrigin);
                h.Add(_last);
            }
        }
    }

    /// <summary>
    /// AUG-156 Full Breath (Gold, always). Condition "Cast with stamina above 90." Rank 1 "Cost decreases 2 mana.", rank 2 "Cost
    /// decreases 3 mana." Drawback "Cast removes 1 additional stamina." A spell committed while the owner holds more than 90
    /// stamina costs 2 (3) mana less (70% floor, L124); every paid spell commitment of the owner also removes 1 stamina.
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class FullBreath : IAugmentBehavior
    {
        public const string Id = "AUG-156";
        public const float Threshold = 90f;     // "stamina above 90"
        public const float Rank1Cut = 2f;       // "Cost decreases 2 mana."
        public const float Rank2Cut = 3f;       // "Cost decreases 3 mana."
        public const float CastStamina = 1f;    // "Cast removes 1 additional stamina."

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.Attach(new Hook(AugmentsBKit.ByRank(rank, Rank1Cut, Rank2Cut)));
        }

        private sealed class Hook : RecordHook
        {
            private readonly float _cut;
            private int _pendingCasts;

            public Hook(float cut) : base(Id)
            {
                _cut = cut;
            }

            public override void OnEvent(DuelEvent e)
            {
                if (AugmentsBKit.IsPaidSpellCommit(e, Player, out _)) _pendingCasts++;
            }

            public override void OnTick()
            {
                for (; _pendingCasts > 0; _pendingCasts--) AugmentsBKit.SpendStamina(World, Player, CastStamina);
            }

            public override void AdjustCastPrice(in CastQuote quote, ref float reduction, ref float increase)
            {
                if (AugmentsBKit.IsSpellQuote(quote) && Self.Resources.Stamina > Threshold) reduction += _cut;
            }

            public override void Hash(ref StateHasher h) => h.Add(_pendingCasts);
        }
    }

    /// <summary>
    /// AUG-157 Full Mind (Gold, always). Condition "Dodge with mana above 90." Rank 1 "Cost decreases 2 stamina.", rank 2 "Cost
    /// decreases 3 stamina." Drawback "Dodge removes 1 additional mana." A dodge started while the owner holds more than 90 mana
    /// costs 2 (3) stamina less than the 25 (spec L82); every dodge of the owner also removes 1 mana (never below zero).
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class FullMind : IAugmentBehavior
    {
        public const string Id = "AUG-157";
        public const float Threshold = 90f;     // "mana above 90"
        public const float Rank1Cut = 2f;       // "Cost decreases 2 stamina."
        public const float Rank2Cut = 3f;       // "Cost decreases 3 stamina."
        public const float DodgeMana = 1f;      // "Dodge removes 1 additional mana."

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.Attach(new Hook(AugmentsBKit.ByRank(rank, Rank1Cut, Rank2Cut)));
        }

        private sealed class Hook : RecordHook
        {
            private readonly float _cut;
            private int _dodges;

            public Hook(float cut) : base(Id)
            {
                _cut = cut;
            }

            public override void AdjustMotor(ref MotorCommand command)
            {
                if (command.DodgePressed && Self.Resources.Mana > Threshold)
                {
                    command.Tuning.DodgeStaminaCost = Math.Max(0f, command.Tuning.DodgeStaminaCost - _cut);
                }
            }

            public override void OnEvent(DuelEvent e)
            {
                if (e is MovementEvent m && m.Player == Player && m.Change == MovementChange.Dodged) _dodges++;
            }

            public override void OnTick()
            {
                for (; _dodges > 0; _dodges--) ChargeMana(DodgeMana);
            }

            public override void Hash(ref StateHasher h) => h.Add(_dodges);
        }
    }

    /// <summary>
    /// AUG-159 Emergency Composure (Gold, always). Condition "Take at least 30 direct damage while below 30 stamina." Rank 1
    /// "Gain 3 stamina, 8 s cooldown.", rank 2 "Gain 5 stamina, 8 s cooldown." Drawback "Maximum health decreases 6." One
    /// original direct hit (spell impact or common melee) that deals at least 30 to the owner (shields included, spec L112)
    /// while the owner holds less than 30 stamina grants 3 (5) stamina, at most once per 8 s.
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class EmergencyComposure : IAugmentBehavior
    {
        public const string Id = "AUG-159";
        public const float MinDamage = 30f;     // "at least 30 direct damage"
        public const float Threshold = 30f;     // "while below 30 stamina"
        public const float Rank1Stamina = 3f;   // "Gain 3 stamina, 8 s cooldown."
        public const float Rank2Stamina = 5f;   // "Gain 5 stamina, 8 s cooldown."
        public const float CooldownSec = 8f;
        public const float HealthCut = 6f;      // "Maximum health decreases 6."

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.AddModifier(new StatModifier(StatKind.MaxHealthBonus, -HealthCut, Id));
            host.Attach(new Hook(AugmentsBKit.ByRank(rank, Rank1Stamina, Rank2Stamina)));
        }

        private sealed class Hook : RecordHook
        {
            private readonly float _stamina;
            private int _hitOrigin;
            private int _last = int.MinValue / 2;

            public Hook(float stamina) : base(Id)
            {
                _stamina = stamina;
            }

            public override void OnEvent(DuelEvent e)
            {
                if (e is DamageEvent d && AugmentsBKit.IsDirectHitTaken(d, Player) && d.Dealt >= MinDamage - 1e-4f
                    && Self.Resources.Stamina < Threshold && _hitOrigin == 0)
                {
                    _hitOrigin = d.OriginEventId;
                }
            }

            public override void OnTick()
            {
                if (_hitOrigin == 0) return;
                int origin = _hitOrigin;
                _hitOrigin = 0;
                if (InCombat && Self.Alive && Ready(_last, CooldownSec) && TryActivate(origin, -1, _stamina))
                {
                    _last = Now;
                    GainStamina(_stamina);
                }
            }

            public override void Hash(ref StateHasher h)
            {
                h.Add(_hitOrigin);
                h.Add(_last);
            }
        }
    }

    /// <summary>
    /// AUG-161 Direct Leech (Prismatic, always). Condition "Direct spell or weapon damages enemy player." Rank 1 "Heal 2% of
    /// damage.", rank 2 "Heal 3% of damage." Drawback "Maximum health decreases 10; global 10% and 20/s healing caps apply."
    /// Lifesteal of the owner's original direct hits on the enemy player (spell impacts and common melee, spec L114: direct
    /// damage only, at most 10%, 20 per second, inside the 35/s and 120 duel healing budget; never from props, summons, or
    /// training targets).
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class DirectLeech : IAugmentBehavior
    {
        public const string Id = "AUG-161";
        public const float Rank1Leech = 0.02f;  // "Heal 2% of damage."
        public const float Rank2Leech = 0.03f;  // "Heal 3% of damage."
        public const float HealthCut = 10f;     // "Maximum health decreases 10"

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.AddModifier(new StatModifier(StatKind.Lifesteal, AugmentsBKit.ByRank(rank, Rank1Leech, Rank2Leech), Id));
            host.AddModifier(new StatModifier(StatKind.MaxHealthBonus, -HealthCut, Id));
        }
    }

    /// <summary>
    /// AUG-162 Frugal Focus (Prismatic, always). Condition "Paid cast costs at least 25 mana." Rank 1 "Refund 2 mana once after
    /// release.", rank 2 "Refund 3 mana once after release." Drawback "Cooldown increases 3%; refunds share 25% cap." A spell
    /// whose commitment paid at least 25 mana returns 2 (3) mana once when it releases, through the cast's refund ledger (one
    /// 25% cap shared with every refund of the cast, spec L90, L124). Every spell recharge the owner starts is 3% longer.
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class FrugalFocus : IAugmentBehavior
    {
        public const string Id = "AUG-162";
        public const float MinPaid = 25f;        // "costs at least 25 mana"
        public const float Rank1Refund = 2f;     // "Refund 2 mana once after release."
        public const float Rank2Refund = 3f;     // "Refund 3 mana once after release."
        public const float CooldownIncrease = 0.03f; // "Cooldown increases 3%"

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.Attach(new Hook(AugmentsBKit.ByRank(rank, Rank1Refund, Rank2Refund)));
        }

        private sealed class Hook : RecordHook, ICastCooldownHook
        {
            private readonly float _refund;
            private readonly PaymentLog _payments = new PaymentLog();
            private readonly System.Collections.Generic.List<Confluence.Casting.CastEventId> _qualifying = new System.Collections.Generic.List<Confluence.Casting.CastEventId>();
            private CastDuelEvent _release;

            public Hook(float refund) : base(Id)
            {
                _refund = refund;
            }

            public override void OnEvent(DuelEvent e)
            {
                _payments.Observe(e, Player);
                if (AugmentsBKit.IsPaidSpellCommit(e, Player, out CastDuelEvent c) && c.Cast.Mana >= MinPaid - 1e-4f)
                {
                    _qualifying.Add(c.CastId);
                    if (_qualifying.Count > 8) _qualifying.RemoveAt(0);
                }
                else if (AugmentsBKit.IsSpellRelease(e, Player, out CastDuelEvent r) && _qualifying.Remove(r.CastId))
                {
                    _release = r;
                }
            }

            public override void OnTick()
            {
                if (_release == null) return;
                CastDuelEvent r = _release;
                _release = null;
                int payment = _payments.PaymentOf(r.CastId);
                if (InCombat && payment != 0 && TryActivate(r.Id, -1, _refund))
                {
                    World.Effects.RefundCast(Player, payment, _refund, Key); // one 25% cap per cast (L90, L124)
                }
            }

            public float AdjustCooldownSec(string spellId, float seconds) =>
                AugmentsAKit.IsSpellSource(spellId) ? seconds * (1f + CooldownIncrease) : seconds;

            public override void Hash(ref StateHasher h)
            {
                _payments.Hash(ref h);
                h.Add(_qualifying.Count);
                for (int i = 0; i < _qualifying.Count; i++) h.Add((ulong)_qualifying[i].Packed);
                h.Add(_release != null ? _release.Id : 0);
            }
        }
    }

    /// <summary>
    /// AUG-163 Unused Tonic (Prismatic, always). Condition "No consumable used this duel." Rank 1 "Maximum mana increases 6
    /// until consumable use.", rank 2 "Maximum mana increases 10 until consumable use." Drawback "After using consumable, mana
    /// regeneration decreases 5% for 2 s." The bonus is static capacity (spec L579) present from the round start until the
    /// owner's first consumable use of the duel, which removes it for the rest of the duel; each consumable use slows mana
    /// regeneration by 5% for 2 s.
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class UnusedTonic : IAugmentBehavior
    {
        public const string Id = "AUG-163";
        public const float Rank1Mana = 6f;       // "Maximum mana increases 6 until consumable use."
        public const float Rank2Mana = 10f;      // "Maximum mana increases 10 until consumable use."
        public const float RegenCut = 0.05f;     // "mana regeneration decreases 5% for 2 s"
        public const float RegenCutSec = 2f;

        internal const string VesselSource = Id + ":unused";

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.AddModifier(new StatModifier(StatKind.MaxManaBonus, AugmentsBKit.ByRank(rank, Rank1Mana, Rank2Mana), VesselSource));
            host.Attach(new Hook());
        }

        private sealed class Hook : RecordHook
        {
            private int _uses;
            private bool _spent;

            public Hook() : base(Id)
            {
            }

            public override void OnEvent(DuelEvent e)
            {
                if (e is ResourceEvent r && r.Player == Player && r.Change == ResourceChange.ConsumableUsed) _uses++;
            }

            public override void OnTick()
            {
                if (_uses == 0) return;
                _uses = 0;
                StatSheet stats = Self.Stats;
                if (!_spent)
                {
                    stats.RemoveSource(VesselSource);
                    _spent = true;
                }

                stats.RemoveSource(Key + ":regen"); // a new use restarts the 2 s
                stats.Add(new StatModifier(StatKind.ManaRegenPercent, -RegenCut, Key + ":regen", Core.Rules.DamageChannel.None, Now + AugmentsBKit.Ticks(RegenCutSec)));
                TryActivate();
            }

            public override void Hash(ref StateHasher h)
            {
                h.Add(_uses);
                h.Add(_spent);
            }
        }
    }
}
