using System;
using Confluence.Casting;
using Confluence.Core.Content;
using Confluence.Sim.Casting;
using Confluence.Sim.Duel;
using Confluence.Sim.Effects;
using Confluence.Sim.Hashing;
using Confluence.Sim.Movement;
using Confluence.Sim.Spells;

namespace Confluence.Sim.Content.Augments.AugmentsA
{
    // Augments-a records whose draft requirement is a prior-duel feat, another augment, or equipment tags; the in-duel
    // behavior below does not depend on that requirement (Economy's eligibility and dormancy rules decide when the record
    // is active, spec L602-L646).

    /// <summary>
    /// AUG-014 Parry Syntax (Gold, FEAT-003 and a guard item). Condition "Complete advanced cast variation within 0.5 s
    /// after perfect guard." Rank 1 "Cost decreases 8% mana.", rank 2 "Cost decreases 12% mana." Drawback "Ordinary guard
    /// grants no bonus." A spell committed with its mastery reward within 0.5 s of the owner's perfect guard (spec L104)
    /// costs 8% (12%) of its listed cost less; this is mastery-linked efficiency, so it clamps together with a mana
    /// reward at 20% (L480) and stays inside the 70% floor (L124). One cast per perfect guard; a normal cast uses nothing.
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class ParrySyntax : IAugmentBehavior
    {
        public const string Id = "AUG-014";
        public const float WindowSec = 0.5f;   // "within 0.5 s after perfect guard"
        public const float Rank1Cut = 0.08f;   // "Cost decreases 8% mana."
        public const float Rank2Cut = 0.12f;   // "Cost decreases 12% mana."

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.Attach(new Hook(AugmentsAKit.ByRank(rank, Rank1Cut, Rank2Cut)));
        }

        private sealed class Hook : EffectHook
        {
            private readonly float _cut;
            private int _perfectTick = -1;
            private int _perfectEvent;
            private int _usedEvent;

            public Hook(float cut) : base(Id)
            {
                _cut = cut;
            }

            private bool Open => _perfectTick >= 0 && Now - _perfectTick <= AugmentsAKit.Ticks(WindowSec);

            public override void OnEvent(DuelEvent e)
            {
                if (e is GuardEvent g && g.Player == Player && g.Change == GuardChange.PerfectBlocked)
                {
                    _perfectTick = g.Tick;
                    _perfectEvent = g.Id;
                }
                else if (CastFacts.IsSpellCommit(e, Player, out CastDuelEvent commit) && Open && commit.Cast.Mastery.IsMastered)
                {
                    _perfectTick = -1; // one cast per perfect guard
                    _usedEvent = _perfectEvent;
                }
            }

            public override void OnTick()
            {
                if (_usedEvent != 0)
                {
                    TryActivate(_usedEvent);
                    _usedEvent = 0;
                }
            }

            public override void AdjustMasteryPrice(in CastQuote quote, ref float masteredReduction, ref float normalReduction)
            {
                if (Open && AugmentsAKit.CurrentCast(Self).IsValid)
                {
                    masteredReduction += quote.BaseCost * _cut;
                }
            }

            public override void Hash(ref StateHasher h)
            {
                h.Add(_perfectTick);
                h.Add(_perfectEvent);
                h.Add(_usedEvent);
            }
        }
    }

    /// <summary>
    /// AUG-021 Disciplined Loadout (Prismatic, two mastery-tagged items and round 9 or later). Condition "Complete advanced
    /// variation while wearing two mastery-tagged items." Rank 1 "Gain 2 stamina, 3 s cooldown.", rank 2 "Gain 4 stamina, 3 s
    /// cooldown." Drawback "Normal casts grant nothing; total mastery efficiency cap remains 20%." Each spell the owner
    /// commits with its mastery reward grants 2 (4) stamina within the maximum (spec L78), at most once per 3 s and
    /// through the proc budget (L126); normal casts grant nothing. The stamina is not a cost reduction, so the cast's
    /// mastery efficiency stays clamped at 20% (L480).
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class DisciplinedLoadout : IAugmentBehavior
    {
        public const string Id = "AUG-021";
        public const float Rank1Stamina = 2f;   // "Gain 2 stamina, 3 s cooldown."
        public const float Rank2Stamina = 4f;   // "Gain 4 stamina, 3 s cooldown."
        public const float CooldownSec = 3f;    // "3 s cooldown"

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.Attach(new Hook(AugmentsAKit.ByRank(rank, Rank1Stamina, Rank2Stamina)));
        }

        private sealed class Hook : EffectHook
        {
            private readonly float _stamina;
            private int _commitEvent;
            private int _lastGrant = int.MinValue / 2;

            public Hook(float stamina) : base(Id)
            {
                _stamina = stamina;
            }

            public override void OnEvent(DuelEvent e)
            {
                if (CastFacts.IsSpellCommit(e, Player, out CastDuelEvent commit) && commit.Cast.Mastery.IsMastered && _commitEvent == 0)
                {
                    _commitEvent = commit.Id;
                }
            }

            public override void OnTick()
            {
                if (_commitEvent == 0) return;
                int origin = _commitEvent;
                _commitEvent = 0;
                if (InCombat && Now - _lastGrant >= AugmentsAKit.Ticks(CooldownSec) && TryActivate(origin, -1, _stamina))
                {
                    _lastGrant = Now;
                    Host.System.GainResource(Player, EffectResource.Stamina, _stamina, Id);
                }
            }

            public override void Hash(ref StateHasher h)
            {
                h.Add(_commitEvent);
                h.Add(_lastGrant);
            }
        }
    }

    /// <summary>
    /// AUG-070 Graduate Survey (Prismatic, AUG-053 Remote Survey rank 1 and a field spell). Condition "Own Remote Survey rank
    /// 1 and place a field at least 10 m away." Rank 1 "Placement range increases 0.5 m.", rank 2 "Placement range increases
    /// 1 m." Drawback "Field duration decreases 0.5 s, minimum 1 s." The owner's placed fields whose range reaches at least
    /// 10 m can be placed 0.5 m (1 m) farther; every field of the owner lasts 0.5 s less, never below 1 s (or its own
    /// shorter lifetime).
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class GraduateSurvey : IAugmentBehavior, IAugmentFormStage
    {
        public const string Id = "AUG-070";
        public const float MinRange = 10f;       // "at least 10 m away"
        public const float Rank1Range = 0.5f;    // "Placement range increases 0.5 m."
        public const float Rank2Range = 1f;      // "Placement range increases 1 m."
        public const float LifetimeCut = 0.5f;   // "Field duration decreases 0.5 s"
        public const float MinLifetime = 1f;     // "minimum 1 s"

        public void Attach(IEffectHost host, int rank) => ContentArgs.Require(host);

        public void ApplyToForm(SpellFormBuilder form, int rank)
        {
            FieldDelivery f = FieldStages.FieldOf(form);
            if (f == null) return;
            if (f.Placement == PlacementMode.AtAimPoint && form.Range >= MinRange - 1e-4f)
            {
                form.Range += AugmentsAKit.ByRank(rank, Rank1Range, Rank2Range);
            }

            f.LifetimeSec = Math.Max(Math.Min(MinLifetime, f.LifetimeSec), f.LifetimeSec - LifetimeCut);
        }
    }

    /// <summary>
    /// AUG-117 Graduated Runner (Prismatic, AUG-097 Economical Sprint rank 1 and two movement items). Condition "Own
    /// Economical Sprint rank 1 and two movement-tagged items." Rank 1 "First 0.5 s of sprint costs 2 less total stamina, 5 s
    /// cooldown.", rank 2 "First 0.5 s costs 4 less total stamina, 5 s cooldown." Drawback "Sprint speed after 2 s decreases
    /// 2%." The first 0.5 s of a sprint begun with the cooldown ready drains 2 (4) stamina less in total, spread evenly over
    /// those 30 ticks; the cooldown runs 5 s from that sprint's start. From the third second of a continuous sprint the
    /// sprint gait is 2% slower (under the shared +20% movement cap, spec L124).
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class GraduatedRunner : IAugmentBehavior
    {
        public const string Id = "AUG-117";
        public const float OpeningSec = 0.5f;    // "First 0.5 s of sprint"
        public const float Rank1Stamina = 2f;    // "costs 2 less total stamina"
        public const float Rank2Stamina = 4f;    // "costs 4 less total stamina"
        public const float CooldownSec = 5f;     // "5 s cooldown"
        public const float SlowAfterSec = 2f;    // "Sprint speed after 2 s"
        public const float SpeedCut = 0.02f;     // "decreases 2%"

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.Attach(new Hook(AugmentsAKit.ByRank(rank, Rank1Stamina, Rank2Stamina)));
        }

        private sealed class Hook : EffectHook
        {
            private readonly float _stamina;
            private int _sprintTicks;
            private bool _discountRun;
            private int _lastUse = int.MinValue / 2;

            public Hook(float stamina) : base(Id)
            {
                _stamina = stamina;
            }

            private bool Ready => Now - _lastUse >= AugmentsAKit.Ticks(CooldownSec);

            public override void AdjustMotor(ref MotorCommand command)
            {
                int opening = AugmentsAKit.Ticks(OpeningSec);
                bool discount = InCombat && ((_sprintTicks == 0 && Ready) || (_discountRun && _sprintTicks < opening));
                if (discount)
                {
                    command.Tuning.SprintStaminaPerSecond = Math.Max(0f, command.Tuning.SprintStaminaPerSecond - _stamina / OpeningSec);
                }
            }

            public override void AdjustGait(in MotorCommand command, ref GaitAdjust gait)
            {
                if (_sprintTicks >= AugmentsAKit.Ticks(SlowAfterSec))
                {
                    gait.Sprint -= SpeedCut; // a penalty: applies after the +20% cap with the other gait changes
                }
            }

            public override void OnTick()
            {
                if (!Self.Motor.Sprinting)
                {
                    _sprintTicks = 0;
                    _discountRun = false;
                    return;
                }

                if (_sprintTicks == 0)
                {
                    _discountRun = InCombat && Ready; // this sprint's opening was discounted
                    if (_discountRun) _lastUse = Now;
                }

                _sprintTicks++;
            }

            public override void Hash(ref StateHasher h)
            {
                h.Add(_sprintTicks);
                h.Add(_discountRun);
                h.Add(_lastUse);
            }
        }
    }
}
