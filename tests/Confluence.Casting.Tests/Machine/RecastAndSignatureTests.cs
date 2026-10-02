using System.Linq;
using Confluence.Casting;
using Confluence.Casting.Grammars;
using Confluence.Casting.Tests.Support;

namespace Confluence.Casting.Tests.Machine
{
    public class RecastAndSignatureTests
    {
        private static readonly MasteryResult Crest = MasteryResult.Mastered(MasteryReward.DirectDamage, 0.12f);

        private static CastDriver Scripted(MasteryResult result, params CastableSpell[] spells) =>
            new CastDriver(new ScriptedGrammar(15, result), spells);

        private static CastableSpell Orb(RecastRule rule) => TestSpells.Quick("TEST-ORB", mana: 22f).WithRecast(rule);

        [Fact]
        public void Recast_Reuses_The_Original_Identity_And_Mastery_And_Costs_Nothing()
        {
            var d = Scripted(Crest, Orb(new RecastRule(maxRecasts: 2, windowTicks: 120)));
            d.Step(InputButtons.Primary).Idle(49);         // committed 15, released 30
            var committed = d.Single(CastEventKind.Committed);
            var opened = d.Single(CastEventKind.RecastWindowOpened);
            Assert.Equal(30, opened.Tick);
            Assert.Equal(2, opened.Count);
            float manaBefore = d.Host.ManaPool;

            d.Step(InputButtons.Recast);                   // tick 50
            var recast = d.Single(CastEventKind.Recast);
            Assert.Equal(committed.CastId, recast.CastId);
            Assert.Equal(Crest, recast.Mastery);
            Assert.Equal(1, recast.Count);
            Assert.Equal(manaBefore, d.Host.ManaPool);
            Assert.Equal(1, d.Host.Payments);
            Assert.Single(d.OfKind(CastEventKind.PreparationStarted)); // never starts a new spell (spec L94)

            d.Idle(1).Step(InputButtons.Recast);           // tick 52, second and last recast
            Assert.Equal(2, d.OfKind(CastEventKind.Recast).Count());
            Assert.Equal(52, d.TickOf(CastEventKind.RecastWindowClosed));
            d.Idle(1).Step(InputButtons.Recast);
            Assert.Equal(RejectReason.NoPendingRecast, d.Single(CastEventKind.Rejected).Reject);
        }

        [Fact]
        public void Recast_Does_Nothing_Without_A_Pending_State_Or_For_Another_Selected_Spell()
        {
            var d = Scripted(Crest, Orb(RecastRule.Default), TestSpells.Quick("B"));
            d.Step(InputButtons.Recast);
            Assert.Equal(RejectReason.NoPendingRecast, d.Single(CastEventKind.Rejected).Reject);
            Assert.False(d.Has(CastEventKind.PreparationStarted));

            d.Idle(1).Step(InputButtons.Primary).Idle(45);  // orb released at 32
            d.Step(InputButtons.None, select: 1);
            d.Step(InputButtons.Recast);                   // selected spell B has no pending recast
            Assert.Equal(2, d.OfKind(CastEventKind.Rejected).Count());
            Assert.False(d.Has(CastEventKind.Recast));
            Assert.NotNull(d.Machine.PendingRecastFor(0)); // the orb's state is untouched
        }

        [Fact]
        public void Recast_During_A_Windup_Is_Refused()
        {
            var d = Scripted(Crest, Orb(RecastRule.Default));
            d.Step(InputButtons.Primary).Idle(16);
            d.Step(InputButtons.Recast);                   // tick 17: windup, no pending state yet
            Assert.Equal(RejectReason.NoPendingRecast, d.Single(CastEventKind.Rejected).Reject);
        }

        [Fact]
        public void Explicit_Window_Expires_And_Default_Window_Lasts_Until_The_Effect_Ends()
        {
            var timed = Scripted(Crest, Orb(new RecastRule(windowTicks: 60)));
            timed.Step(InputButtons.Primary).Idle(90);     // released at 30; last allowed tick 90
            Assert.False(timed.Has(CastEventKind.RecastWindowClosed));
            timed.Idle(1);
            Assert.Equal(91, timed.TickOf(CastEventKind.RecastWindowClosed));

            var lifetime = Scripted(Crest, Orb(RecastRule.Default));
            lifetime.Step(InputButtons.Primary).Idle(500);
            Assert.NotNull(lifetime.Machine.PendingRecastFor(0));
            var id = lifetime.Single(CastEventKind.Released).CastId;
            Assert.True(lifetime.Machine.EndRecastWindow(id, lifetime.Tick, lifetime.Events));
            lifetime.Step(InputButtons.Recast);
            Assert.False(lifetime.Has(CastEventKind.Recast));
        }

        [Fact]
        public void Recast_Payment_And_Added_Recovery_Come_From_The_Rule()
        {
            var d = Scripted(Crest, Orb(new RecastRule(extraMana: 5f, addedRecoveryTicks: 12)));
            d.Step(InputButtons.Primary).Idle(49);
            float before = d.Host.ManaPool;
            d.Step(InputButtons.Recast);
            Assert.Equal(before - 5f, d.Host.ManaPool);
            Assert.Equal(5f, d.Single(CastEventKind.Recast).Mana);
            Assert.Equal(CastPhase.Recovery, d.Machine.Phase);
            d.Idle(12);
            Assert.Equal(CastPhase.Idle, d.Machine.Phase);
        }

        [Fact]
        public void Silenced_Players_Cannot_Recast()
        {
            var d = Scripted(Crest, Orb(RecastRule.Default));
            d.Step(InputButtons.Primary).Idle(49);
            d.Host.IsSilenced = true;
            d.Step(InputButtons.Recast);
            Assert.Equal(RejectReason.Silenced, d.Single(CastEventKind.Rejected).Reject);
            Assert.NotNull(d.Machine.PendingRecastFor(0));
        }

        [Fact]
        public void Signature_Uses_Its_Listed_Windup_Without_A_Grammar()
        {
            // WIZ-002 Kiln Vent: 14 s cooldown, 25 mana, 0.35 s windup (spec L472: no second mastery puzzle).
            var kiln = CastableSpell.Signature("WIZ-002-SIG", "WIZ-002", 25f, 14.0, 0.35, CastWeight.Quick, true);
            var grammar = new ScriptedGrammar(15, Crest);
            var d = CastDriver.WithSignature(grammar, kiln, TestSpells.Quick());
            d.Step(InputButtons.Signature);
            var committed = d.Single(CastEventKind.Committed);
            Assert.Equal(0, committed.Tick);
            Assert.Equal(-1, committed.Slot);
            Assert.Equal(21, committed.Ticks);
            Assert.Equal(MissReason.NotApplicable, committed.Mastery.Miss);
            Assert.Equal(75f, d.Host.ManaPool);
            Assert.Equal(0, grammar.Begins);
            d.Idle(40);
            Assert.Equal(21, d.TickOf(CastEventKind.Released));
            Assert.Equal(840, d.Host.CooldownRemainingTicks("WIZ-002-SIG") + d.Host.CurrentTick); // started at tick 0
        }

        [Fact]
        public void Mastery_Efficiency_Reduces_Payment_Under_The_Shared_Caps()
        {
            var spell = TestSpells.Quick(mana: 20f);
            var mastered = Scripted(MasteryResult.Mastered(MasteryReward.ManaEfficiency, 0.15f), spell);
            mastered.Step(InputButtons.Primary).Idle(20);
            Assert.Equal(17f, mastered.Single(CastEventKind.Committed).Mana, 4);

            // Mastery-linked equipment efficiency clamps with the reward at 20% (spec L480).
            var linked = Scripted(MasteryResult.Mastered(MasteryReward.ManaEfficiency, 0.15f), spell);
            linked.Host.Modifiers = new CastModifiers { MasteryLinkedEfficiency = 0.10f };
            linked.Step(InputButtons.Primary).Idle(20);
            Assert.Equal(16f, linked.Single(CastEventKind.Committed).Mana, 4);

            // All cost reduction leaves at least 70% to pay (spec L124).
            var floor = Scripted(MasteryResult.Mastered(MasteryReward.ManaEfficiency, 0.15f), spell);
            floor.Host.Modifiers = new CastModifiers { CostReduction = 0.25f };
            floor.Step(InputButtons.Primary).Idle(20);
            Assert.Equal(14f, floor.Single(CastEventKind.Committed).Mana, 4);

            // A damage reward never also reduces cost, even with linked efficiency (spec L124 "never both").
            var damage = Scripted(MasteryResult.Mastered(MasteryReward.DirectDamage, 0.12f), spell);
            damage.Host.Modifiers = new CastModifiers { MasteryLinkedEfficiency = 0.10f };
            damage.Step(InputButtons.Primary).Idle(20);
            Assert.Equal(20f, damage.Single(CastEventKind.Committed).Mana, 4);
        }

        [Fact]
        public void Windup_Reward_Shortens_Only_The_Intrinsic_Windup_Within_Its_Caps()
        {
            var spell = TestSpells.Heavy(windupTicks: 40);
            var d = Scripted(MasteryResult.Mastered(MasteryReward.WindupReduction, 0.10f, windupCapTicks: 4), spell);
            d.Step(InputButtons.Primary).Idle(80);
            Assert.Equal(36, d.Single(CastEventKind.Committed).Ticks); // 10% of 40 = 4 ticks, at the 0.06 s cap (WIZ-005)
            Assert.Equal(15 + 36, d.TickOf(CastEventKind.Released));
        }

        [Fact]
        public void Auto_Commit_Is_Flagged_On_The_Committed_Event()
        {
            var d = new CastDriver(new ScriptedGrammar(5, MasteryResult.Normal(MissReason.AutoVented), auto: true), TestSpells.Quick());
            d.Step(InputButtons.Primary).Idle(10);
            Assert.True(d.Single(CastEventKind.Committed).Auto);
        }
    }
}
