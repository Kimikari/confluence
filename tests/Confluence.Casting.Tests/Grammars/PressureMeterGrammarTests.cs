using System.Collections.Generic;
using System.Linq;
using Confluence.Casting;
using Confluence.Casting.Grammars;
using Confluence.Casting.Tests.Support;
using Confluence.Core.Content;

namespace Confluence.Casting.Tests.Grammars
{
    /// <summary>WIZ-010 Halen Tide's pressure-metering grammar (catalogue input_sequence, normal_cast, mastery).</summary>
    [Verifies("WIZ-010")]
    public class PressureMeterGrammarTests
    {
        private const InputButtons P = InputButtons.Primary;
        private const InputButtons A = InputButtons.ModifierA;

        private static CastDriver Halen(string spellId = "SPL-073") =>
            new CastDriver(new PressureMeterGrammar(), TestCatalogue.Spell(spellId));

        private static PressureMeterGrammar GrammarOf(CastDriver d) => (PressureMeterGrammar)d.Machine.Grammar;

        /// <summary>Runs a per-tick held-button script starting at tick 0, then idles.</summary>
        private static CastDriver Run(IList<InputButtons> script, string spellId = "SPL-073", int idleAfter = 60)
        {
            var d = Halen(spellId);
            foreach (var held in script) d.Step(held);
            d.Idle(idleAfter);
            return d;
        }

        /// <summary>Hold primary for ticks [0, release); vent with A on ticks [ventFrom, ventTo).</summary>
        private static List<InputButtons> Script(int release, int ventFrom = -1, int ventTo = -1)
        {
            var s = new List<InputButtons>();
            for (int t = 0; t < release; t++) s.Add(P | (t >= ventFrom && t < ventTo ? A : InputButtons.None));
            return s;
        }

        [Fact]
        public void Windows_Match_The_Record()
        {
            var record = TestCatalogue.Real.Wizard("WIZ-010");
            Assert.Contains("Hold primary to compress the gauge; Modifier A vents it", record.InputSequence);
            Assert.Contains("between 0.30 and 0.90 s", record.InputSequence);
            Assert.Contains("safe 20–80 pressure band", record.NormalCast);
            Assert.Contains("automatically vents and offers a normal cast after 0.30 s", record.NormalCast);
            Assert.Contains("within 45–55 for a continuous 0.25 s", record.Mastery);
            Assert.Contains("pay 15% less mana", record.Mastery);
            Assert.Contains("0.1 s accepted input interval", record.Mastery);

            Assert.Equal(new TickWindow(18, 54), PressureMeterGrammar.ReleaseWindow);
            Assert.Equal(15, PressureMeterGrammar.MasteryHoldTicks);
            Assert.Equal(6, PressureMeterGrammar.AcceptedIntervalTicks);
            Assert.Equal(18, PressureMeterGrammar.BandExitVentTicks);
        }

        [Fact]
        public void Pure_Holding_Casts_Normally_But_Cannot_Master()
        {
            // 30 + 1 per tick: inside 45-55 only from tick 15 to 25, shorter than the 15-tick requirement.
            for (int release = 18; release <= 50; release++)
            {
                var committed = Run(Script(release)).Single(CastEventKind.Committed);
                Assert.Equal(release, committed.Tick);
                Assert.False(committed.Mastery.IsMastered);
                Assert.Equal(16f, committed.Mana);
                Assert.Equal(release <= 26 ? MissReason.BandHeldTooShort : MissReason.OutsideMasteryBand, committed.Mastery.Miss);
            }
        }

        [Fact]
        public void Pressure_Follows_The_Documented_Rates()
        {
            var d = Halen();
            d.Step(P);
            Assert.Equal(30f, GrammarOf(d).Pressure);
            d.Hold(P, 10);                                 // ticks 1-10 compress at 60/s = 1 per tick
            Assert.Equal(40f, GrammarOf(d).Pressure);
            d.Hold(P | A, 6);                              // ticks 11-16 vent at 50/s
            Assert.Equal(35f, GrammarOf(d).Pressure, 4);
            Assert.True(d.Machine.GrammarUi.ModifierEngaged);
        }

        [Fact]
        public void One_Deliberate_Vent_Inside_The_Band_Earns_15_Percent_Less_Mana()
        {
            // Compress to 51 by tick 21, vent ticks 22-27 (to 46), compress again, release at tick 31.
            var d = Run(Script(31, 22, 28));
            var committed = d.Single(CastEventKind.Committed);
            Assert.Equal(31, committed.Tick);
            Assert.Equal(MasteryReward.ManaEfficiency, committed.Mastery.Reward);
            Assert.Equal(0.15f, committed.Mastery.Magnitude);
            Assert.Equal(16f * 0.85f, committed.Mana, 4);
            Assert.Equal(31 + 15, d.TickOf(CastEventKind.Released)); // Pressure Jet keeps its 0.25 s windup
        }

        [Theory]
        [InlineData(29, false)]
        [InlineData(30, true)]
        [InlineData(36, true)]
        public void Mastery_Needs_The_Full_Continuous_0_25_s(int release, bool mastered)
        {
            var committed = Run(Script(release, 22, 28)).Single(CastEventKind.Committed);
            Assert.Equal(mastered, committed.Mastery.IsMastered);
            if (!mastered)
            {
                Assert.Equal(MissReason.BandHeldTooShort, committed.Mastery.Miss);
                Assert.Equal(-1, committed.Mastery.TimingErrorTicks);
            }
        }

        [Fact]
        public void Leaving_The_Band_Mid_Run_Resets_The_Requirement()
        {
            // Venting ticks 18-29 drops from 47 to 37; the run restarts when pressure climbs back to 45 at tick 37.
            Assert.Equal(MissReason.OutsideMasteryBand, Run(Script(34, 18, 30)).Single(CastEventKind.Committed).Mastery.Miss);
            var back = Run(Script(40, 18, 30)).Single(CastEventKind.Committed).Mastery;
            Assert.Equal(MissReason.BandHeldTooShort, back.Miss);
            Assert.Equal(3 - 15, back.TimingErrorTicks);
        }

        [Fact]
        public void Overcompression_Vents_Then_Commits_A_Normal_Cast_0_30_s_Later()
        {
            var d = Halen();
            d.Step(P).Hold(P, 51);                         // pressure 81 at tick 51 leaves the safe band
            Assert.True(d.Machine.GrammarUi.Venting);
            Assert.Equal(18, d.Machine.GrammarUi.VentRemainingTicks);
            Assert.Equal(MissReason.LeftSafeBand, d.Machine.GrammarUi.ReleaseNow);
            Assert.Equal(100f, d.Host.ManaPool);
            d.Hold(P, 3).Idle(40);                         // releasing during the vent changes nothing
            var committed = d.Single(CastEventKind.Committed);
            Assert.Equal(51 + 18, committed.Tick);
            Assert.True(committed.Auto);
            Assert.Equal(MissReason.LeftSafeBand, committed.Mastery.Miss);
            Assert.Equal(16f, committed.Mana);
            Assert.Equal(1, d.Host.Payments);              // mana is spent only on that cast
        }

        [Fact]
        public void Overventing_Below_20_Also_Vents_Into_A_Normal_Cast()
        {
            var d = Run(Script(60, 0, 60));
            var committed = d.Single(CastEventKind.Committed);
            Assert.Equal(13 + 18, committed.Tick);         // 30 - 13 * 50/60 = 19.2 at tick 13
            Assert.True(committed.Auto);
            Assert.Equal(MissReason.LeftSafeBand, committed.Mastery.Miss);
        }

        [Fact]
        public void The_Vent_Can_Be_Cancelled_For_Free()
        {
            var d = Halen();
            d.Step(P).Hold(P, 55);                         // venting since tick 51
            d.Step(InputButtons.Guard);
            Assert.Equal(CancelReason.CommonAction, d.Single(CastEventKind.PreparationCancelled).Cancel);
            d.Idle(30);
            Assert.False(d.Has(CastEventKind.Committed));
            Assert.Equal(100f, d.Host.ManaPool);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(10)]
        [InlineData(17)]
        public void Release_Before_0_30_s_Queues_A_Normal_Cast_At_0_30_s(int release)
        {
            var committed = Run(Script(release)).Single(CastEventKind.Committed);
            Assert.Equal(18, committed.Tick);
            Assert.False(committed.Auto);
            Assert.Equal(MissReason.EarlyRelease, committed.Mastery.Miss);
            Assert.Equal(release - 18, committed.Mastery.TimingErrorTicks);
        }

        [Fact]
        public void Holding_To_0_90_s_Inside_The_Safe_Band_Auto_Vents_A_Normal_Cast()
        {
            var d = Run(Script(70, 40, 70));               // vent from tick 40 keeps pressure near 57 at 0.90 s
            var committed = d.Single(CastEventKind.Committed);
            Assert.Equal(54, committed.Tick);
            Assert.True(committed.Auto);
            Assert.Equal(MissReason.HeldPastWindow, committed.Mastery.Miss);
        }

        [Fact]
        public void Spamming_The_Vent_Gives_Exactly_What_A_0_1_s_Toggler_Gets()
        {
            // Spam: A toggles every tick from tick 15. The grammar applies at most one change per 0.1 s.
            var spam = new List<InputButtons>();
            for (int t = 0; t < 40; t++) spam.Add(P | (t >= 15 && t % 2 == 1 ? A : InputButtons.None));
            var spammer = Halen();
            var effective = new List<bool>();
            var pressures = new List<float>();
            foreach (var held in spam)
            {
                spammer.Step(held);
                effective.Add(spammer.Machine.GrammarUi.ModifierEngaged);
                pressures.Add(GrammarOf(spammer).Pressure);
            }

            // Replay the effective vent state as clean input: every change at least 6 ticks apart.
            var clean = effective.Select(e => P | (e ? A : InputButtons.None)).ToList();
            var flips = Enumerable.Range(1, clean.Count - 1).Where(i => clean[i] != clean[i - 1]).ToList();
            for (int i = 1; i < flips.Count; i++) Assert.True(flips[i] - flips[i - 1] >= 6);
            var deliberate = Halen();
            var cleanPressures = new List<float>();
            foreach (var held in clean)
            {
                deliberate.Step(held);
                cleanPressures.Add(GrammarOf(deliberate).Pressure);
            }

            Assert.Equal(cleanPressures, pressures);
            Assert.True(spammer.Machine.GrammarUi.ThrottledInputs > 10);
            Assert.Equal(0, deliberate.Machine.GrammarUi.ThrottledInputs);
        }

        [Fact]
        public void Spamming_From_The_Start_Misses_With_Too_Fast_Toggling()
        {
            var spam = new List<InputButtons>();
            for (int t = 0; t < 40; t++) spam.Add(P | (t % 2 == 1 ? A : InputButtons.None));
            var committed = Run(spam).Single(CastEventKind.Committed);
            Assert.False(committed.Mastery.IsMastered);
            Assert.Equal(MissReason.TooFastToggling, committed.Mastery.Miss);
        }

        [Fact]
        public void Hud_Reports_Bands_Progress_And_Release_Outcome()
        {
            var d = Halen();
            d.Step(P).Hold(P, 20);                         // tick 20, pressure 50, in band since tick 15
            var ui = d.Machine.GrammarUi;
            Assert.True(ui.HasSafeBand && ui.HasMasteryBand);
            Assert.Equal(20f, ui.SafeBandLow);
            Assert.Equal(80f, ui.SafeBandHigh);
            Assert.Equal(45f, ui.MasteryBandLow);
            Assert.Equal(55f, ui.MasteryBandHigh);
            Assert.Equal(50f, ui.GaugeValue);
            Assert.Equal(5, ui.ProgressTicks);
            Assert.Equal(15, ui.RequiredTicks);
            Assert.Equal(MissReason.BandHeldTooShort, ui.ReleaseNow);
            d.Hold(P, 2).Hold(P | A, 6).Hold(P, 2);         // tick 30
            Assert.Equal(MissReason.None, d.Machine.GrammarUi.ReleaseNow);
        }

        [Fact]
        public void Identical_Inputs_Give_Identical_Events()
        {
            var script = Script(31, 22, 28);
            var a = Run(script).Events.Select(e => e.ToString() + e.Mastery + e.Mana).ToList();
            var b = Run(script).Events.Select(e => e.ToString() + e.Mastery + e.Mana).ToList();
            Assert.Equal(a, b);
        }
    }
}
