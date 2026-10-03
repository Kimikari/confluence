using System.Linq;
using Confluence.Casting;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Sim.Casting;
using Confluence.Sim.Duel;
using Confluence.Sim.Input;
using Confluence.Sim.Players;
using Confluence.Sim.Spells;
using Confluence.Sim.Status;
using Confluence.Sim.Tests.Spells;
using InputButtons = Confluence.Casting.InputButtons;

namespace Confluence.Sim.Tests.Casting
{
    /// <summary>The Sim side of casting: the player's cast host, the cast frame, and cast events in the world stream.</summary>
    public class CastingIntegrationTests
    {
        private static SpellForm Bolt(string id = "SPL-T01", float mana = 20f, float cooldownSec = 2f, float castTimeSec = 0f, bool interruptRefund = false)
        {
            SpellFormBuilder b = SpellTestKit.Form(id, mana, cooldownSec, castTimeSec);
            b.Delivery = new ProjectileDelivery { Speed = 30f, MaxRange = 30f };
            b.Payload.Direct = 10f;
            b.InterruptRefund = interruptRefund;
            return b.Build();
        }

        [Fact]
        public void Mana_Is_Paid_All_Or_Nothing_At_Commitment_And_The_Cooldown_Starts_There()
        {
            var grammar = new TapGrammar(5);
            DuelWorld w = SpellTestKit.Duel(SpellTestKit.Loadout(grammar, Bolt()));
            SpellTestKit.Cast(w);
            SpellTestKit.Run(w, 4);
            Assert.Equal(CastPhase.Preparation, SpellTestKit.Machine(w, 0).Phase);
            Assert.Equal(100f, w.Player(0).Resources.Mana); // spec L90: preparation spends nothing

            w.Step(TestWorlds.A, TestWorlds.B);
            Assert.Equal(80f, w.Player(0).Resources.Mana);
            Assert.Equal(Ticks.FromSeconds(2.0), w.Player(0).Caster.CooldownRemainingTicks("SPL-T01"));
            CastDuelEvent commit = SpellTestKit.CastEvents(w, CastEventKind.Committed).Single();
            Assert.Equal(20f, commit.Cast.Mana);
            Assert.Single(w.EventLog.OfType<ResourceEvent>(), e => e.Change == ResourceChange.ManaSpent && e.SourceId == "SPL-T01");
        }

        [Fact]
        public void Not_Enough_Mana_At_Commitment_Cancels_For_Free()
        {
            DuelWorld w = SpellTestKit.Duel(SpellTestKit.Loadout(Bolt(mana: 30f)));
            w.TryCommitMana(0, 80f, "TEST");
            SpellTestKit.Cast(w);
            CastDuelEvent cancel = SpellTestKit.CastEvents(w, CastEventKind.PreparationCancelled).Single();
            Assert.Equal(CancelReason.InsufficientMana, cancel.Cast.Cancel);
            Assert.Equal(20f, w.Player(0).Resources.Mana, 3); // spec L78: the full commitment or nothing
            Assert.Equal(0, w.Player(0).Caster.CooldownRemainingTicks("SPL-T01"));
        }

        [Fact]
        public void A_Cooldown_Refuses_A_New_Cast_And_The_Stat_Sheet_Caps_Cooldown_Reduction()
        {
            DuelWorld w = SpellTestKit.Duel(SpellTestKit.Loadout(Bolt(cooldownSec: 2f)));
            w.Player(0).Stats.Add(StatKind.CooldownReduction, 0.5f, "TEST-CDR"); // capped at 25% (spec L124)
            SpellTestKit.Cast(w);
            Assert.Equal(Ticks.FromSeconds(1.5), w.Player(0).Caster.CooldownRemainingTicks("SPL-T01"));
            SpellTestKit.Run(w, 30);
            SpellTestKit.Cast(w);
            Assert.Equal(RejectReason.CooldownNotReady, SpellTestKit.CastEvents(w, CastEventKind.Rejected).Single().Cast.Reject);
            SpellTestKit.RunUntil(w, () => w.Player(0).Caster.CooldownRemainingTicks("SPL-T01") == 0, 200);
            SpellTestKit.Cast(w);
            Assert.Equal(2, SpellTestKit.CastEvents(w, CastEventKind.Committed).Count());
        }

        [Fact]
        public void Cost_Reduction_From_The_Stat_Sheet_Never_Pays_Less_Than_Seventy_Percent()
        {
            DuelWorld w = SpellTestKit.Duel(SpellTestKit.Loadout(Bolt(mana: 20f)));
            w.Player(0).Stats.Add(StatKind.CostReduction, 0.6f, "TEST-COST");
            SpellTestKit.Cast(w);
            Assert.Equal(100f - 14f, w.Player(0).Resources.Mana, 3); // spec L124: at least 70% of the original cost
        }

        [Fact]
        public void Cancelling_Before_Commitment_Spends_Nothing_And_Applies_The_Recast_Lock()
        {
            DuelWorld w = SpellTestKit.Duel(SpellTestKit.Loadout(new TapGrammar(20), Bolt()));
            SpellTestKit.Cast(w);
            SpellTestKit.Run(w, 3);
            w.Step(TestWorlds.A.With(InputButtons.Dodge), TestWorlds.B); // a common action yields the spell (spec L474)
            CastDuelEvent cancel = SpellTestKit.CastEvents(w, CastEventKind.PreparationCancelled).Single();
            Assert.Equal(CancelReason.CommonAction, cancel.Cast.Cancel);
            Assert.Equal(100f, w.Player(0).Resources.Mana);

            w.Step(TestWorlds.A, TestWorlds.B);
            SpellTestKit.Cast(w); // inside the 0.15 s lock (spec L90)
            Assert.Equal(RejectReason.RecastLocked, SpellTestKit.CastEvents(w, CastEventKind.Rejected).Single().Cast.Reject);
            SpellTestKit.Run(w, 10);
            SpellTestKit.Cast(w);
            Assert.Equal(2, SpellTestKit.CastEvents(w, CastEventKind.PreparationStarted).Count());
        }

        [Fact]
        public void A_Stun_After_Commitment_Interrupts_Keeps_The_Mana_Spent_Runs_The_Cooldown_And_Credits_The_Interrupt()
        {
            DuelWorld w = SpellTestKit.Duel(SpellTestKit.Loadout(Bolt(castTimeSec: 0.5f)));
            SpellTestKit.Cast(w);
            Assert.Equal(CastPhase.Windup, SpellTestKit.Machine(w, 0).Phase);
            w.ApplyStatus(0, StatusApplication.Of(StatusKind.Stun, "SPL-STUN", 1, 30));
            CastDuelEvent interrupted = SpellTestKit.CastEvents(w, CastEventKind.Interrupted).Single();
            Assert.Equal(Confluence.Casting.InterruptCause.HardControl, interrupted.Cast.Interrupt);
            Assert.Equal(0f, interrupted.Cast.Mana); // no interrupt-refund tag: no refund (spec L90)
            Assert.Equal(80f, w.Player(0).Resources.Mana);
            Assert.True(w.Player(0).Caster.CooldownRemainingTicks("SPL-T01") > 0);
            Assert.Equal(1, w.Player(1).Feats.Interrupts); // FEAT-019

            SpellTestKit.Run(w, 60);
            Assert.Empty(SpellTestKit.CastEvents(w, CastEventKind.Released));
            Assert.Empty(SpellTestKit.SpellEvents(w, SpellEventKind.ProjectileLaunched));
        }

        [Fact]
        public void The_Interrupt_Refund_Tag_Returns_A_Quarter_Of_The_Payment_Through_The_World()
        {
            DuelWorld w = SpellTestKit.Duel(SpellTestKit.Loadout(Bolt(castTimeSec: 0.5f, interruptRefund: true)));
            SpellTestKit.Cast(w);
            w.ApplyStatus(0, StatusApplication.Of(StatusKind.Silence, "SPL-HUSH", 1, 20));
            Assert.Equal(85f, w.Player(0).Resources.Mana, 3); // 80 + 25% of 20
            Assert.Contains(w.EventLog.OfType<ResourceEvent>(), e => e.Change == ResourceChange.ManaGained && e.Amount == 5f);
            Assert.Equal(Confluence.Casting.InterruptCause.Silence, SpellTestKit.CastEvents(w, CastEventKind.Interrupted).Single().Cast.Interrupt);
        }

        [Fact]
        public void Hostile_Displacement_Interrupts_And_Voluntary_Cancel_After_Commitment_Refunds_Nothing()
        {
            DuelWorld w = SpellTestKit.Duel(SpellTestKit.Loadout(Bolt(castTimeSec: 0.5f, interruptRefund: true)));
            SpellTestKit.Cast(w);
            w.Step(TestWorlds.A.With(InputButtons.Dodge), TestWorlds.B);
            Assert.Single(SpellTestKit.CastEvents(w, CastEventKind.CommittedCastCancelled));
            Assert.Equal(80f, w.Player(0).Resources.Mana); // spec L90: voluntary cancellation gives no refund

            DuelWorld v = SpellTestKit.Duel(SpellTestKit.Loadout(Bolt(castTimeSec: 0.5f)));
            SpellTestKit.Cast(v);
            Assert.True(v.ApplyKnockback(0, 1, Vec3.Back, 2f, "SPL-PUSH"));
            Assert.Single(SpellTestKit.CastEvents(v, CastEventKind.Interrupted));
            Assert.Equal(1, v.Player(1).Feats.Interrupts);
        }

        [Fact]
        public void A_Channel_Counts_As_Spending_So_Mana_Does_Not_Regenerate_Until_It_Ends()
        {
            SpellFormBuilder b = SpellTestKit.Form("SPL-T02", 20f, 3f);
            b.Delivery = new RayDelivery { Width = 0.2f };
            b.Payload.Direct = 2f;
            b.ChannelSec = 2.5f;
            DuelWorld w = SpellTestKit.Duel(SpellTestKit.Loadout(b.Build()));
            SpellTestKit.Cast(w, TestWorlds.Face(90f));
            Assert.True(SpellTestKit.Machine(w, 0).IsChanneling);
            SpellTestKit.Run(w, Ticks.FromSeconds(2.0), TestWorlds.Face(90f));
            Assert.Equal(80f, w.Player(0).Resources.Mana); // past the 1.5 s delay, but the channel counts (spec L78)

            SpellTestKit.RunUntil(w, () => !SpellTestKit.Machine(w, 0).IsChanneling, 60, TestWorlds.Face(90f));
            SpellTestKit.Run(w, Ticks.FromSeconds(1.4), TestWorlds.Face(90f));
            Assert.Equal(80f, w.Player(0).Resources.Mana);
            SpellTestKit.Run(w, Ticks.FromSeconds(0.5), TestWorlds.Face(90f));
            Assert.True(w.Player(0).Resources.Mana > 80f);
        }

        [Fact]
        public void Heavy_And_Channeled_Casts_Suppress_Sprint_While_Quick_Casts_Keep_It()
        {
            PlayerInput run = TestWorlds.A.Moving(0f, 1f).With(InputButtons.Sprint);
            DuelWorld heavy = SpellTestKit.Duel(SpellTestKit.Loadout(Bolt(castTimeSec: 0.6f)));
            SpellTestKit.Run(heavy, 20, run);
            Assert.True(heavy.Player(0).Motor.Sprinting);
            SpellTestKit.Cast(heavy, run);
            SpellTestKit.Run(heavy, 20, run);
            Assert.False(heavy.Player(0).Motor.Sprinting); // spec L80, D-048: the motor ends sprint, the cast continues
            Assert.Equal(CastPhase.Windup, SpellTestKit.Machine(heavy, 0).Phase);
            Assert.Equal(GlobalRules.Casting.MoveSpeedFactorHeavyCast * GlobalRules.Movement.WalkSpeed, heavy.Player(0).Motor.Velocity.LengthXZ(), 1);

            DuelWorld quick = SpellTestKit.Duel(SpellTestKit.Loadout(Bolt(castTimeSec: 0.3f)));
            SpellTestKit.Run(quick, 20, run);
            SpellTestKit.Cast(quick, run);
            SpellTestKit.Run(quick, 2, run);
            Assert.True(quick.Player(0).Motor.Sprinting);
        }

        [Fact]
        public void A_Stated_Windup_Slow_Replaces_The_Class_Movement_Factor()
        {
            SpellFormBuilder b = SpellTestKit.Form("SPL-T03", 10f, 1f, 0.6f);
            b.Delivery = new ConeDelivery { HalfAngleDeg = 45f, Reach = 4f };
            b.Payload.Direct = 5f;
            b.CastingMoveFactor = 0.8f;
            DuelWorld w = SpellTestKit.Duel(SpellTestKit.Loadout(b.Build()));
            PlayerInput walk = TestWorlds.A.Moving(0f, 1f);
            SpellTestKit.Cast(w, walk);
            SpellTestKit.Run(w, 20, walk);
            Assert.Equal(0.8f, w.Spells.GetStatus(0).MovementFactor);
            Assert.Equal(0.8f * GlobalRules.Movement.WalkSpeed, w.Player(0).Motor.Velocity.LengthXZ(), 2);
        }

        [Fact]
        public void The_Alcove_Ward_And_Silence_Refuse_Casting()
        {
            DuelWorld w = SpellTestKit.Duel(SpellTestKit.Loadout(Bolt()), alcoveTicks: 60);
            SpellTestKit.Cast(w);
            Assert.Equal(RejectReason.Warded, SpellTestKit.CastEvents(w, CastEventKind.Rejected).Single().Cast.Reject); // spec L46
            Assert.Equal(100f, w.Player(0).Resources.Mana);

            DuelWorld s = SpellTestKit.Duel(SpellTestKit.Loadout(Bolt()));
            s.ApplyStatus(0, StatusApplication.Of(StatusKind.Silence, "SPL-HUSH", 1, 30));
            SpellTestKit.Cast(s);
            Assert.Equal(RejectReason.Silenced, SpellTestKit.CastEvents(s, CastEventKind.Rejected).Single().Cast.Reject);
        }

        [Fact]
        public void The_Guard_Release_Window_Locks_New_Casts()
        {
            DuelWorld w = SpellTestKit.Duel(SpellTestKit.Loadout(Bolt()));
            w.Step(TestWorlds.A.With(InputButtons.Guard), TestWorlds.B);
            w.Step(TestWorlds.A, TestWorlds.B); // released: 0.25 s punish window (spec L104)
            SpellTestKit.Cast(w);
            Assert.Equal(RejectReason.ActionLocked, SpellTestKit.CastEvents(w, CastEventKind.Rejected).Single().Cast.Reject);
        }

        [Fact]
        public void A_Melee_Press_During_Preparation_Performs_The_Strike_And_The_Spell_Yields()
        {
            DuelWorld w = SpellTestKit.Duel(SpellTestKit.Loadout(new TapGrammar(30), Bolt()));
            SpellTestKit.Cast(w);
            w.Step(TestWorlds.A.With(InputButtons.Melee), TestWorlds.B);
            Assert.True(w.Player(0).Melee.Busy); // spec L474: no wizard overrides common melee
            Assert.Equal(CancelReason.CommonAction, SpellTestKit.CastEvents(w, CastEventKind.PreparationCancelled).Single().Cast.Cancel);
        }

        [Fact]
        public void Changing_The_Selected_Slot_Cancels_An_Open_Preparation()
        {
            DuelWorld w = SpellTestKit.Duel(SpellTestKit.Loadout(new TapGrammar(30), Bolt("SPL-T01"), Bolt("SPL-T04")));
            SpellTestKit.Cast(w);
            w.Step(TestWorlds.A.Selecting(1), TestWorlds.B);
            Assert.Equal(CancelReason.SelectionChanged, SpellTestKit.CastEvents(w, CastEventKind.PreparationCancelled).Single().Cast.Cancel);
            Assert.Equal(1, SpellTestKit.Machine(w, 0).SelectedSlot);
            SpellTestKit.Run(w, 10, TestWorlds.A.Selecting(1));
            SpellTestKit.Cast(w, TestWorlds.A.Selecting(1));
            Assert.Equal("SPL-T04", SpellTestKit.CastEvents(w, CastEventKind.PreparationStarted).Last().SpellId);
        }

        [Fact]
        public void The_Signature_Commits_On_Its_Key_With_Its_Own_Cost_Cooldown_And_Windup()
        {
            SpellFormBuilder sig = SpellFormBuilder.ForSignature("WIZ-002");
            sig.Mana = 25f;
            sig.CooldownSec = 14f;
            sig.CastTimeSec = 0.35f;
            sig.Range = 4f;
            sig.Channel = DamageChannel.Thermal;
            sig.Payload.Channel = DamageChannel.Thermal;
            sig.Delivery = new ConeDelivery { HalfAngleDeg = 45f, Reach = 4f };
            sig.Payload.Direct = 28f;
            var loadout = new SpellLoadout(new TapGrammar(), new[] { Bolt() }, sig.Build());
            DuelWorld w = SpellTestKit.Duel(loadout);
            TestWorlds.Place(w, 1, new Vec3(0f, 0f, -3f), 180f);

            w.Step(TestWorlds.A.With(InputButtons.Signature), TestWorlds.B);
            CastDuelEvent commit = SpellTestKit.CastEvents(w, CastEventKind.Committed).Single();
            Assert.Equal(-1, commit.Cast.Slot);
            Assert.Equal(Ticks.FromSeconds(0.35), commit.Cast.Ticks); // listed windup, no grammar (spec L472)
            Assert.Equal(75f, w.Player(0).Resources.Mana);
            Assert.Equal(Ticks.FromSeconds(14.0), w.Player(0).Caster.CooldownRemainingTicks("WIZ-002"));

            SpellTestKit.Run(w, Ticks.FromSeconds(0.35) + 1);
            Assert.Equal(28f, SpellTestKit.TotalDealt(w, 1, "WIZ-002"), 3);
            SpellTestKit.Run(w, 30);
            w.Step(TestWorlds.A.With(InputButtons.Signature), TestWorlds.B);
            Assert.Equal(RejectReason.CooldownNotReady, SpellTestKit.CastEvents(w, CastEventKind.Rejected).Single().Cast.Reject);
        }

        [Fact]
        public void Cast_Phases_Become_World_Events_And_Hits_Carry_The_Cast_Identity_And_Original_Event()
        {
            DuelWorld w = SpellTestKit.Duel(SpellTestKit.Loadout(Bolt()));
            SpellTestKit.Cast(w);
            SpellTestKit.Run(w, 40);
            var kinds = w.EventLog.OfType<CastDuelEvent>().Where(e => e.Player == 0).Select(e => e.Kind).ToList();
            Assert.Equal(new[] { CastEventKind.PreparationStarted, CastEventKind.Committed, CastEventKind.Released, CastEventKind.RecoveryEnded }, kinds);

            CastDuelEvent commit = SpellTestKit.CastEvents(w, CastEventKind.Committed).Single();
            DamageEvent hit = SpellTestKit.DamageTo(w, 1, "SPL-T01").Single();
            Assert.Equal(commit.CastId, hit.CastId);
            Assert.Equal(commit.Id, hit.OriginEventId); // one original event for proc budgets (spec L126)
            Assert.True(commit.Id < hit.Id);
        }

        [Fact]
        public void Each_Player_Needs_Its_Own_Grammar_Instance()
        {
            var shared = new TapGrammar();
            Assert.Throws<System.InvalidOperationException>(() => SpellTestKit.Duel(SpellTestKit.Loadout(shared, Bolt()), SpellTestKit.Loadout(shared)));
        }

        [Fact]
        public void The_Decisive_Sequence_Includes_The_Opponents_Commitment_Behind_The_Final_Hit()
        {
            SpellFormBuilder b = SpellTestKit.Form("SPL-T05", 10f, 0.5f);
            b.Delivery = new ProjectileDelivery { Speed = 40f, MaxRange = 30f };
            b.Payload.Direct = 400f;
            DuelWorld w = SpellTestKit.Duel(SpellTestKit.Loadout(b.Build()));
            SpellTestKit.Cast(w);
            SpellTestKit.Run(w, 40);
            Assert.True(w.IsOver);
            var sequence = w.Result.DecisiveSequences[1];
            Assert.Contains(sequence, e => e is CastDuelEvent c && c.Kind == CastEventKind.Committed && c.Player == 0);
        }
    }
}
