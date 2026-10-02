using System;
using System.Globalization;
using System.Linq;
using Confluence.Casting;
using Confluence.Casting.Grammars;
using Confluence.Casting.Tests.Support;
using Confluence.Core.Rules;

namespace Confluence.Casting.Tests.Grammars
{
    public class GrammarRegistryTests
    {
        [Fact]
        public void Every_Catalogue_Wizard_Has_A_Descriptor()
        {
            var wizards = TestCatalogue.Real.Wizards.Select(w => w.Id).ToList();
            Assert.Equal(36, wizards.Count);
            Assert.Equal(wizards, GrammarRegistry.All.Select(d => d.WizardId));
            foreach (var id in wizards)
            {
                Assert.True(GrammarRegistry.TryDescribe(id, out var d), id);
                Assert.False(string.IsNullOrEmpty(d.Language));
                Assert.NotEqual(InputButtons.None, d.Opens);
                Assert.NotEqual(InputRoute.None, d.Routes);
            }

            Assert.False(GrammarRegistry.TryDescribe("WIZ-037", out _));
            Assert.Throws<System.Collections.Generic.KeyNotFoundException>(() => GrammarRegistry.Describe("WIZ-000"));
        }

        [Fact]
        public void Languages_Follow_The_Roster_List_In_Order()
        {
            // Spec L468 names the 36 casting languages in roster order.
            string line = SpecText.Line(468);
            int at = line.IndexOf("held focus", StringComparison.Ordinal);
            Assert.True(at >= 0);
            foreach (var d in GrammarRegistry.All)
            {
                int next = line.IndexOf(d.Language, at, StringComparison.Ordinal);
                Assert.True(next >= at, d.WizardId + " " + d.Language);
                at = next + d.Language.Length;
            }
        }

        [Fact]
        public void Only_Implemented_Grammars_Can_Be_Created_And_Others_Throw()
        {
            Assert.IsType<PressureReleaseGrammar>(GrammarRegistry.Create("WIZ-002"));
            Assert.IsType<PressureMeterGrammar>(GrammarRegistry.Create("WIZ-010"));
            Assert.NotSame(GrammarRegistry.Create("WIZ-002"), GrammarRegistry.Create("WIZ-002"));
            foreach (var d in GrammarRegistry.All.Where(d => !d.Executable))
            {
                var ex = Assert.Throws<NotImplementedException>(() => GrammarRegistry.Create(d.WizardId));
                Assert.Contains(d.WizardId, ex.Message);
            }

            Assert.Equal(new[] { "WIZ-002", "WIZ-010" }, GrammarRegistry.All.Where(d => d.Executable).Select(d => d.WizardId));
        }

        [Fact]
        public void Every_Family_Is_Used_And_Assignments_Match_The_Record_Mechanics()
        {
            foreach (GrammarFamily f in Enum.GetValues(typeof(GrammarFamily)))
                Assert.Contains(GrammarRegistry.All, d => d.Family == f);

            Assert.Equal(GrammarFamily.VoiceRhythm, GrammarRegistry.Describe(VoiceLexicon.WizardId).Family);
            Assert.True(GrammarRegistry.Describe("WIZ-004").Routes.HasFlag(InputRoute.Voice | InputRoute.Rhythm)); // spec L367
            Assert.True(GrammarRegistry.Describe("WIZ-003").Routes.HasFlag(InputRoute.DirectionKeys));            // spec L369
            Assert.True(GrammarRegistry.Describe("WIZ-009").Routes.HasFlag(InputRoute.DirectionKeys));
            Assert.True(GrammarRegistry.Describe("WIZ-022").Routes.HasFlag(InputRoute.SingleSelector));           // spec L490
            Assert.True(GrammarRegistry.Describe("WIZ-003").CameraLock && GrammarRegistry.Describe("WIZ-009").CameraLock);
            Assert.Equal(new[] { "WIZ-006", "WIZ-015", "WIZ-035" }, GrammarRegistry.All.Where(d => d.RequiresCombatEvent).Select(d => d.WizardId));
            Assert.Equal(0.1, GrammarRegistry.Describe("WIZ-010").AcceptedIntervalSec);
        }

        [Fact]
        public void Stated_Rewards_Appear_In_The_Mastery_Text_And_Respect_The_Caps()
        {
            foreach (var d in GrammarRegistry.All)
            {
                string mastery = TestCatalogue.Real.Wizard(d.WizardId).Mastery;
                AssertPercent(mastery, d.Magnitude, d.WizardId);
                Assert.True(d.Magnitude <= MasteryClamp.CategoryCap(d.Reward), d.WizardId);
                Assert.True(d.Magnitude >= GlobalRules.Modifiers.TypicalMasteryRewardMin - 1e-6f, d.WizardId); // spec L478 "normally 10-15%"
                if (d.Choice != RewardChoice.Single)
                {
                    Assert.NotEqual(MasteryReward.None, d.AlternateReward);
                    AssertPercent(mastery, d.AlternateMagnitude, d.WizardId);
                    Assert.True(d.AlternateMagnitude <= MasteryClamp.CategoryCap(d.AlternateReward), d.WizardId);
                }
                else
                {
                    Assert.Equal(MasteryReward.None, d.AlternateReward);
                }
            }
        }

        [Fact]
        public void Stated_Normal_Preparations_Appear_In_The_Record()
        {
            foreach (var d in GrammarRegistry.All.Where(d => d.NormalPreparationSec.HasValue))
            {
                var w = TestCatalogue.Real.Wizard(d.WizardId);
                string text = w.InputSequence + " " + w.NormalCast + " " + w.Mastery;
                string sec = d.NormalPreparationSec.Value.ToString("0.00", CultureInfo.InvariantCulture) + " s";
                Assert.True(text.Contains(sec) || text.Contains(sec.Replace(" s", "–")), d.WizardId + " " + sec);
            }
        }

        [Fact]
        public void Guided_Routes_Use_The_Stated_Normal_Preparation()
        {
            var bram = GrammarRegistry.CreateGuided("WIZ-002");
            Assert.Equal(12, bram.PreparationTicks);
            Assert.Equal(InputRoute.Guided, bram.Route);
            Assert.Equal("WIZ-002", bram.WizardId);
            Assert.Equal(18, GrammarRegistry.CreateGuided("WIZ-003").PreparationTicks); // "guided 0.30 s normal inscription"
            Assert.Equal(36, GrammarRegistry.CreateGuided("WIZ-036").PreparationTicks); // "same 0.60 s preparation"
            Assert.Throws<NotImplementedException>(() => GrammarRegistry.CreateGuided("WIZ-005"));

            // A guided cast is reliable and never rewarded (spec L470).
            var d = new CastDriver(GrammarRegistry.CreateGuided("WIZ-007"), TestCatalogue.Spell("SPL-049"));
            d.Step(InputButtons.Primary).Idle(80);
            var committed = d.Single(CastEventKind.Committed);
            Assert.Equal(30, committed.Tick);
            Assert.Equal(MissReason.Guided, committed.Mastery.Miss);
        }

        private static void AssertPercent(string text, float magnitude, string id)
        {
            string percent = Math.Round(magnitude * 100f).ToString(CultureInfo.InvariantCulture) + "%";
            Assert.True(text.Contains(percent), id + " expects " + percent + " in: " + text);
        }
    }
}
