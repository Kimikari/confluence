using System.Linq;
using Confluence.Casting;
using Confluence.Casting.Grammars;
using Confluence.Casting.Tests.Support;
using Confluence.Core.Content;

namespace Confluence.Casting.Tests.Grammars
{
    /// <summary>WIZ-002 Bram Cinder's crest-release grammar (catalogue input_sequence, normal_cast, mastery).</summary>
    [Verifies("WIZ-002")]
    public class PressureReleaseGrammarTests
    {
        private static CastDriver Bram(params string[] spellIds) =>
            new CastDriver(new PressureReleaseGrammar(), spellIds.Select(TestCatalogue.Spell).ToArray());

        /// <summary>Presses primary at tick 0 and releases it at tick <paramref name="releaseTick"/>.</summary>
        private static CastDriver HoldAndRelease(int releaseTick, string spellId = "SPL-009")
        {
            var d = Bram(spellId);
            d.Step(InputButtons.Primary).Hold(InputButtons.Primary, releaseTick - 1).Idle(60);
            return d;
        }

        [Fact]
        public void Windows_Match_The_Record()
        {
            var record = TestCatalogue.Real.Wizard("WIZ-002");
            Assert.Contains("Hold primary for 0.20–0.60 s", record.InputSequence);
            Assert.Contains("Any release after 0.20 s casts normally", record.NormalCast);
            Assert.Contains("Overholding at 0.60 s auto-vents a normal cast", record.NormalCast);
            Assert.Contains("100 ms crest window at 0.45 s", record.Mastery);
            Assert.Contains("direct damage gains 12%", record.Mastery);
            Assert.Contains("pay 12% less mana", record.Mastery);

            Assert.Equal(12, PressureReleaseGrammar.MinimumHoldTicks);
            Assert.Equal(36, PressureReleaseGrammar.AutoVentTicks);
            Assert.Equal(27, PressureReleaseGrammar.CrestTick);
            Assert.Equal(new TickWindow(24, 30), PressureReleaseGrammar.CrestWindow);
            Assert.Equal(GrammarFamily.ButtonTiming, new PressureReleaseGrammar().Family);
        }

        [Theory]
        [InlineData(12, MissReason.EarlyRelease, -12)]
        [InlineData(23, MissReason.EarlyRelease, -1)]
        [InlineData(24, MissReason.None, 0)]
        [InlineData(27, MissReason.None, 0)]
        [InlineData(30, MissReason.None, 0)]
        [InlineData(31, MissReason.LateRelease, 1)]
        [InlineData(35, MissReason.LateRelease, 5)]
        public void Release_Timing_Decides_The_One_Reward(int releaseTick, MissReason miss, int error)
        {
            var d = HoldAndRelease(releaseTick);
            var committed = d.Single(CastEventKind.Committed);
            Assert.Equal(releaseTick, committed.Tick);       // release selects the cast time
            Assert.False(committed.Auto);
            Assert.Equal(miss, committed.Mastery.Miss);
            Assert.Equal(error, committed.Mastery.TimingErrorTicks);
            if (miss == MissReason.None)
            {
                Assert.Equal(MasteryReward.DirectDamage, committed.Mastery.Reward); // Fireball is damaging
                Assert.Equal(0.12f, committed.Mastery.Magnitude);
            }
            else
            {
                Assert.False(committed.Mastery.IsMastered);
            }

            Assert.Equal(18f, committed.Mana);                // damage reward: full listed mana
            Assert.Equal(releaseTick + 21, d.TickOf(CastEventKind.Released)); // 0.35 s intrinsic windup is never removed
        }

        [Fact]
        public void Nondamaging_Spell_Pays_12_Percent_Less_At_The_Crest()
        {
            var d = HoldAndRelease(27, "SPL-011");             // Heat Haze, 18 mana, nondamaging
            var committed = d.Single(CastEventKind.Committed);
            Assert.Equal(MasteryReward.ManaEfficiency, committed.Mastery.Reward);
            Assert.Equal(18f * 0.88f, committed.Mana, 4);
            Assert.Equal(100f - 18f * 0.88f, d.Host.ManaPool, 4);

            var normal = HoldAndRelease(20, "SPL-011");
            Assert.Equal(18f, normal.Single(CastEventKind.Committed).Mana);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(5)]
        [InlineData(11)]
        public void Release_Before_0_20_s_Queues_A_Normal_Cast_At_0_20_s(int releaseTick)
        {
            var d = HoldAndRelease(releaseTick);
            var committed = d.Single(CastEventKind.Committed);
            Assert.Equal(12, committed.Tick);
            Assert.Equal(MissReason.EarlyRelease, committed.Mastery.Miss);
            Assert.Equal(releaseTick - 24, committed.Mastery.TimingErrorTicks);
            Assert.Equal(33, d.TickOf(CastEventKind.Released));
        }

        [Fact]
        public void A_Sub_Tick_Tap_Also_Queues_The_Normal_Cast()
        {
            var d = Bram("SPL-009");
            d.Tap(InputButtons.Primary).Idle(40);
            Assert.Equal(0, d.TickOf(CastEventKind.PreparationStarted));
            Assert.Equal(12, d.TickOf(CastEventKind.Committed));
        }

        [Fact]
        public void A_Queued_Early_Release_Can_Be_Cancelled_For_Free()
        {
            var d = Bram("SPL-009");
            d.Step(InputButtons.Primary).Hold(InputButtons.Primary, 4).Idle(3); // released at tick 5
            Assert.True(d.Machine.GrammarUi.Queued);
            Assert.Equal(12, d.Machine.GrammarUi.QueuedCommitTick);
            d.Step(InputButtons.Dodge);                                         // tick 8
            Assert.Equal(CancelReason.CommonAction, d.Single(CastEventKind.PreparationCancelled).Cancel);
            d.Idle(30);
            Assert.False(d.Has(CastEventKind.Committed));
            Assert.Equal(100f, d.Host.ManaPool);
        }

        [Fact]
        public void Overholding_To_0_60_s_Auto_Vents_A_Normal_Cast_Without_Misfire()
        {
            var d = Bram("SPL-009");
            d.Step(InputButtons.Primary).Hold(InputButtons.Primary, 80);
            var committed = d.Single(CastEventKind.Committed);
            Assert.Equal(36, committed.Tick);
            Assert.True(committed.Auto);
            Assert.Equal("SPL-009", committed.SpellId);     // the requested spell, never another (spec L470)
            Assert.Equal(MissReason.AutoVented, committed.Mastery.Miss);
            Assert.Equal(6, committed.Mastery.TimingErrorTicks);
            Assert.Equal(18f, committed.Mana);
            Assert.Equal(82f, d.Host.ManaPool);             // no self-damage or extra cost
            Assert.Equal(57, d.TickOf(CastEventKind.Released));
            Assert.Single(d.OfKind(CastEventKind.Committed));
        }

        [Fact]
        public void Release_Exactly_At_0_60_s_Is_A_Late_Player_Release()
        {
            var d = HoldAndRelease(36);
            var committed = d.Single(CastEventKind.Committed);
            Assert.Equal(36, committed.Tick);
            Assert.False(committed.Auto);
            Assert.Equal(MissReason.LateRelease, committed.Mastery.Miss);
        }

        [Fact]
        public void Gauge_Rises_To_The_Crest_And_Falls()
        {
            Assert.Equal(0f, PressureReleaseGrammar.GaugeAt(0));
            Assert.Equal(1f, PressureReleaseGrammar.GaugeAt(27));
            Assert.Equal(PressureReleaseGrammar.GaugeAt(18), PressureReleaseGrammar.GaugeAt(36));
            Assert.Equal(2f / 3f, PressureReleaseGrammar.GaugeAt(36), 4);
            for (int t = 1; t <= 27; t++) Assert.True(PressureReleaseGrammar.GaugeAt(t) > PressureReleaseGrammar.GaugeAt(t - 1));
            for (int t = 28; t <= 36; t++) Assert.True(PressureReleaseGrammar.GaugeAt(t) < PressureReleaseGrammar.GaugeAt(t - 1));
        }

        [Fact]
        public void Hud_Shows_Windows_And_What_A_Release_Would_Give_Now()
        {
            var d = Bram("SPL-009");
            d.Step(InputButtons.Primary).Hold(InputButtons.Primary, 9);         // tick 9
            var ui = d.Machine.GrammarUi;
            Assert.True(ui.Active);
            Assert.Equal(new TickWindow(12, 36), ui.ReleaseWindow);
            Assert.Equal(new TickWindow(24, 30), ui.MasteryWindow);
            Assert.Equal(MissReason.EarlyRelease, ui.ReleaseNow);
            d.Hold(InputButtons.Primary, 16);                                   // tick 25
            Assert.Equal(MissReason.None, d.Machine.GrammarUi.ReleaseNow);
            Assert.Equal(PressureReleaseGrammar.GaugeAt(25), d.Machine.GrammarUi.Gauge);
            d.Hold(InputButtons.Primary, 7);                                    // tick 32
            Assert.Equal(MissReason.LateRelease, d.Machine.GrammarUi.ReleaseNow);
        }

        [Fact]
        public void Miss_Reasons_Are_Exact_Never_Bad_Cast()
        {
            var late = HoldAndRelease(31).Single(CastEventKind.Committed).Mastery;
            Assert.Equal("late release (+0.017 s)", late.Describe());
            var early = HoldAndRelease(20).Single(CastEventKind.Committed).Mastery;
            Assert.Equal("early release (-0.067 s)", early.Describe());
        }

        [Fact]
        public void Changing_Spell_While_Holding_Cancels_Without_Cost()
        {
            var d = Bram("SPL-009", "SPL-010");
            d.Step(InputButtons.Primary).Hold(InputButtons.Primary, 10);
            d.Step(InputButtons.Primary, select: 1);
            Assert.Equal(CancelReason.SelectionChanged, d.Single(CastEventKind.PreparationCancelled).Cancel);
            d.Hold(InputButtons.Primary, 40);
            Assert.False(d.Has(CastEventKind.Committed));
            Assert.Equal(100f, d.Host.ManaPool);
        }

        [Fact]
        public void One_Reward_Per_Spell_Is_Inherited_By_Its_Recast()
        {
            // Boiler Orb (SPL-014) detonates on recast; the detonation reuses the crest result (spec L94, L480).
            var orb = TestCatalogue.Spell("SPL-014").WithRecast(RecastRule.Default);
            var d = new CastDriver(new PressureReleaseGrammar(), orb);
            d.Step(InputButtons.Primary).Hold(InputButtons.Primary, 26).Idle(60); // released at the crest, tick 27
            var committed = d.Single(CastEventKind.Committed);
            Assert.True(committed.Mastery.IsMastered);
            d.Step(InputButtons.Recast);
            var recast = d.Single(CastEventKind.Recast);
            Assert.Equal(committed.CastId, recast.CastId);
            Assert.Equal(committed.Mastery, recast.Mastery);
            Assert.Single(d.OfKind(CastEventKind.Committed));
        }
    }
}
