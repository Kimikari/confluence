using System.Collections.Generic;
using System.Linq;
using Confluence.Casting;
using Confluence.Core.Arena;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Sim.Casting;
using Confluence.Sim.Content;
using Confluence.Sim.Content.Augments.AugmentsA;
using Confluence.Sim.Duel;
using Confluence.Sim.Input;
using Confluence.Sim.Players;
using Confluence.Sim.Spells;
using Confluence.Sim.Tests.Spells;
using Confluence.Sim.Tests.World;
using InputButtons = Confluence.Casting.InputButtons;

namespace Confluence.Sim.Tests.Content.AugmentsA
{
    /// <summary>AUG-014, AUG-021, AUG-070 and AUG-117 in real duels on WIZ-002.</summary>
    public class ProofsAndGraduatesTests
    {
        /// <summary>WIZ-002 with Fireball (and SPL-073 as a 30-damage test bolt) and a grammar that commits on the press.</summary>
        private static DuelWorld Tapped(string id, int rank, MasteryResult result)
        {
            DuelPlayerSetup a = AKit.Setup("A", AKit.Bram, id == null ? null : AKit.Aug(id, rank));
            a.EquippedSpellIds.Clear();
            a.EquippedSpellIds.Add("SPL-009");
            a.EquippedSpellIds.Add("SPL-073");
            return AKit.Duel(a, null, null, AKit.RegistryWith(a, AKit.Spell("SPL-073", new TestBolt(30f))), 0, _ => new TapGrammar(0, result));
        }

        private static MasteryResult Mana(float fraction) => MasteryResult.Mastered(MasteryReward.ManaEfficiency, fraction);

        private static MasteryResult Normal => MasteryResult.Normal(MissReason.EarlyRelease);

        /// <summary>Presses primary on alternate ticks (fresh presses) until a commitment happens; returns its paid mana.</summary>
        private static float PressUntilCommit(DuelWorld w, int slot = 0, int maxTicks = 40)
        {
            int before = AKit.Casts(w, CastEventKind.Committed).Count();
            PlayerInput input = AKit.SelectSlot(AKit.A, slot);
            for (int i = 0; i < maxTicks && AKit.Casts(w, CastEventKind.Committed).Count() == before; i++)
            {
                AKit.Step(w, 1, i % 2 == 0 ? input.With(InputButtons.Primary) : input);
            }

            return AKit.Casts(w, CastEventKind.Committed).Last().Cast.Mana;
        }

        // ----- AUG-014 Parry Syntax ------------------------------------------------------------------------

        [Theory]
        [InlineData(1, 0.08f)]
        [InlineData(2, 0.12f)]
        [Verifies("AUG-014")]
        public void Parry_Syntax_Discounts_A_Mastered_Cast_Soon_After_A_Perfect_Guard(int rank, float cut)
        {
            AKit.AssertQuotes(ParrySyntax.Id, "within 0.5 s after perfect guard", "Cost decreases 8% mana.", "Cost decreases 12% mana.", "Ordinary guard grants no bonus.");
            DuelWorld w = Tapped(ParrySyntax.Id, rank, Mana(0.05f));
            int perfect = ConditionalTests.GuardAgainstStrike(w, true);
            float paid = PressUntilCommit(w);
            Assert.True(AKit.Casts(w, CastEventKind.Committed).Single().Tick - perfect <= AKit.Ticks(ParrySyntax.WindowSec));
            Assert.Equal(18f * (1f - 0.05f - cut), paid, 3);
            Assert.Single(AKit.Activations(w, ParrySyntax.Id));
        }

        [Fact]
        [Verifies("AUG-014")]
        public void Parry_Syntax_Clamps_With_The_Mana_Reward_At_20_Percent()
        {
            DuelWorld w = Tapped(ParrySyntax.Id, 2, Mana(0.15f));
            ConditionalTests.GuardAgainstStrike(w, true);
            Assert.Equal(18f * (1f - GlobalRules.Modifiers.MasteryEfficiencyCap), PressUntilCommit(w), 3);
        }

        [Fact]
        [Verifies("AUG-014")]
        public void Parry_Syntax_Needs_A_Perfect_Guard_A_Mastered_Cast_And_The_Window()
        {
            DuelWorld ordinary = Tapped(ParrySyntax.Id, 2, Mana(0.05f));
            ConditionalTests.GuardAgainstStrike(ordinary, false);
            Assert.Equal(18f * 0.95f, PressUntilCommit(ordinary), 3);

            DuelWorld normal = Tapped(ParrySyntax.Id, 2, Normal);
            ConditionalTests.GuardAgainstStrike(normal, true);
            Assert.Equal(18f, PressUntilCommit(normal), 3);

            DuelWorld late = Tapped(ParrySyntax.Id, 2, Mana(0.05f));
            int perfect = ConditionalTests.GuardAgainstStrike(late, true);
            AKit.Step(late, perfect + AKit.Ticks(0.5f) + 1 - late.Tick);
            Assert.Equal(18f * 0.95f, PressUntilCommit(late), 3);
        }

        // ----- AUG-021 Disciplined Loadout -----------------------------------------------------------------

        private static (DuelWorld w, DuelWorld control) Spent(string id, int rank, MasteryResult result)
        {
            DuelWorld w = Tapped(id, rank, result);
            DuelWorld control = Tapped(null, rank, result);
            foreach (DuelWorld d in new[] { w, control })
            {
                AKit.FaceOff(d, 10f);
                AKit.Step(d, 1);
                Assert.True(d.Player(0).Resources.TrySpendStamina(60f, d.Tick));
            }

            return (w, control);
        }

        [Theory]
        [InlineData(1, 2f)]
        [InlineData(2, 4f)]
        [Verifies("AUG-021")]
        public void Disciplined_Loadout_Grants_Stamina_For_A_Mastered_Cast(int rank, float stamina)
        {
            AKit.AssertQuotes(DisciplinedLoadout.Id, "Complete advanced variation while wearing two mastery-tagged items.", "Gain 2 stamina, 3 s cooldown.", "Gain 4 stamina, 3 s cooldown.", "Normal casts grant nothing; total mastery efficiency cap remains 20%.");
            (DuelWorld w, DuelWorld control) = Spent(DisciplinedLoadout.Id, rank, Mana(0.15f));
            PressUntilCommit(w);
            PressUntilCommit(control);
            AKit.Step(w, 2);
            AKit.Step(control, 2);
            Assert.Equal(control.Player(0).Resources.Stamina + stamina, w.Player(0).Resources.Stamina, 3);
            Assert.Equal(18f * 0.85f, AKit.Casts(w, CastEventKind.Committed).Single().Cast.Mana, 3); // efficiency unchanged
        }

        [Fact]
        [Verifies("AUG-021")]
        public void Disciplined_Loadout_Ignores_Normal_Casts_And_Waits_3_Seconds()
        {
            (DuelWorld normal, DuelWorld normalControl) = Spent(DisciplinedLoadout.Id, 2, Normal);
            PressUntilCommit(normal);
            PressUntilCommit(normalControl);
            AKit.Step(normal, 2);
            AKit.Step(normalControl, 2);
            Assert.Equal(normalControl.Player(0).Resources.Stamina, normal.Player(0).Resources.Stamina, 3);

            DuelWorld w = Tapped(DisciplinedLoadout.Id, 1, Mana(0.10f));
            AKit.FaceOff(w, 10f);
            PressUntilCommit(w, 0);
            AKit.Step(w, 50);
            PressUntilCommit(w, 1); // a second mastered cast 1 s later: inside the cooldown
            AKit.Step(w, 2);
            Assert.Equal(2, AKit.Casts(w, CastEventKind.Committed).Count());
            Assert.Single(AKit.Activations(w, DisciplinedLoadout.Id));
            AKit.Step(w, AKit.Ticks(4.3f)); // past the 3 s cooldown and the Fireball's own 5 s cooldown
            PressUntilCommit(w, 0);
            AKit.Step(w, 2);
            Assert.Equal(3, AKit.Casts(w, CastEventKind.Committed).Count());
            Assert.Equal(2, AKit.Activations(w, DisciplinedLoadout.Id).Count());
        }

        // ----- AUG-070 Graduate Survey ---------------------------------------------------------------------

        private static DuelWorld FieldDuel(int rank, ShapedField curtain, ShapedField rush)
        {
            DuelPlayerSetup a = AKit.Setup("A", AKit.Bram, rank > 0 ? AKit.Aug(GraduateSurvey.Id, rank) : null);
            a.EquippedSpellIds.Clear();
            a.EquippedSpellIds.AddRange(new[] { "SPL-012", "SPL-013" });
            DuelWorld w = AKit.Duel(a, null, TestWorlds.Flat(30f, 32f), AKit.RegistryWith(a, AKit.Spell("SPL-012", curtain), AKit.Spell("SPL-013", rush)));
            AKit.FaceOff(w, 25f);
            AKit.Step(w, 1);
            return w;
        }

        private static SpellForm Form(DuelWorld w, string id) => w.Player(0).Caster.FormFor(id);

        [Theory]
        [InlineData(1, 0.5f)]
        [InlineData(2, 1f)]
        [Verifies("AUG-070")]
        public void Graduate_Survey_Extends_Long_Field_Placement(int rank, float metres)
        {
            AKit.AssertQuotes(GraduateSurvey.Id, "place a field at least 10 m away", "Placement range increases 0.5 m.", "Placement range increases 1 m.", "Field duration decreases 0.5 s, minimum 1 s.");
            DuelWorld w = FieldDuel(rank, new ShapedField(), new ShapedField { Radius = 1.5f, LifetimeSec = 1.2f, Placement = PlacementMode.AtCaster });
            Assert.Equal(16f + metres, Form(w, "SPL-012").Range, 4);
            Assert.Equal(5.5f, ((FieldDelivery)Form(w, "SPL-012").Delivery).LifetimeSec, 4);
            Assert.Equal(1f, ((FieldDelivery)Form(w, "SPL-013").Delivery).LifetimeSec, 4); // 1.2 s stops at the 1 s minimum
            AKit.Step(w, 1, AKit.A);
            AKit.HoldAndRelease(w, AKit.NormalHold);
            AKit.Step(w, 40);
            FieldEntity field = ((SpellRuntime)w.Spells).Entities.OfType<FieldEntity>().Single();
            Assert.InRange(field.Center.Z, 16f + metres - 0.1f, 16f + metres + 0.05f);
        }

        [Fact]
        [Verifies("AUG-070")]
        public void Graduate_Survey_Leaves_Short_Range_And_Caster_Fields_Range_Alone()
        {
            DuelWorld w = FieldDuel(2, new ShapedField(), new ShapedField { Radius = 1.5f, LifetimeSec = 3f, Placement = PlacementMode.AtCaster });
            Assert.Equal(GraduateSurvey.MinRange, 10f);
            Assert.Equal(AKit.Catalogue.Spell("SPL-013").Range, Form(w, "SPL-013").Range, 4);
            Assert.Equal(2.5f, ((FieldDelivery)Form(w, "SPL-013").Delivery).LifetimeSec, 4);
            DuelWorld control = FieldDuel(0, new ShapedField(), new ShapedField());
            Assert.Equal(16f, Form(control, "SPL-012").Range, 4);
        }

        // ----- AUG-117 Graduated Runner --------------------------------------------------------------------

        private static DuelWorld Runner(int rank)
        {
            DuelWorld w = rank > 0 ? AKit.With(GraduatedRunner.Id, rank, layout: TestWorlds.Flat(40f, 42f)) : AKit.Without(layout: TestWorlds.Flat(40f, 42f));
            TestWorlds.Place(w, 0, new Vec3(0f, 0f, -30f), 0f);
            TestWorlds.Place(w, 1, new Vec3(-10f, 0f, 30f), 180f);
            AKit.Step(w, 1);
            return w;
        }

        private static PlayerInput Run => AKit.Walk(0f).With(InputButtons.Sprint);

        [Theory]
        [InlineData(1, 2f)]
        [InlineData(2, 4f)]
        [Verifies("AUG-117")]
        public void Graduated_Runner_Discounts_The_First_Half_Second_Of_A_Sprint(int rank, float saved)
        {
            AKit.AssertQuotes(GraduatedRunner.Id, "First 0.5 s of sprint costs 2 less total stamina, 5 s cooldown.", "First 0.5 s costs 4 less total stamina, 5 s cooldown.", "Sprint speed after 2 s decreases 2%.");
            DuelWorld control = Runner(0);
            DuelWorld w = Runner(rank);
            AKit.Step(control, AKit.Ticks(0.5f), Run);
            AKit.Step(w, AKit.Ticks(0.5f), Run);
            Assert.True(w.Player(0).Motor.Sprinting);
            Assert.Equal(100f - GlobalRules.Movement.SprintStaminaPerSecond * 0.5f, control.Player(0).Resources.Stamina, 2);
            Assert.Equal(control.Player(0).Resources.Stamina + saved, w.Player(0).Resources.Stamina, 2);

            // Afterwards the drain is the ordinary 12 per second.
            AKit.Step(control, AKit.Ticks(0.5f), Run);
            AKit.Step(w, AKit.Ticks(0.5f), Run);
            Assert.Equal(control.Player(0).Resources.Stamina + saved, w.Player(0).Resources.Stamina, 2);
        }

        [Fact]
        [Verifies("AUG-117")]
        public void Graduated_Runner_Waits_5_Seconds_And_Slows_Long_Sprints()
        {
            DuelWorld w = Runner(2);
            AKit.Step(w, AKit.Ticks(0.5f), Run);
            AKit.Step(w, AKit.Ticks(1f));
            float before = w.Player(0).Resources.Stamina;
            AKit.Step(w, AKit.Ticks(0.5f), Run); // within 5 s: the full 6 stamina
            Assert.Equal(before - GlobalRules.Movement.SprintStaminaPerSecond * 0.5f, w.Player(0).Resources.Stamina, 2);
            AKit.Step(w, AKit.Ticks(3.2f)); // 5.2 s after the first sprint began: ready again
            before = w.Player(0).Resources.Stamina;
            AKit.Step(w, AKit.Ticks(0.5f), Run);
            Assert.Equal(before - GlobalRules.Movement.SprintStaminaPerSecond * 0.5f + 4f, w.Player(0).Resources.Stamina, 2);

            // A long sprint: from 2 s on the gait is 2% slower than without the augment.
            DuelWorld control = Runner(0);
            DuelWorld slow = Runner(1);
            AKit.Step(control, AKit.Ticks(1.5f), Run);
            AKit.Step(slow, AKit.Ticks(1.5f), Run);
            Assert.Equal(AKit.Speed(control, 0), AKit.Speed(slow, 0), 3);
            AKit.Step(control, AKit.Ticks(1f), Run);
            AKit.Step(slow, AKit.Ticks(1f), Run);
            Assert.True(slow.Player(0).Motor.Sprinting);
            Assert.Equal(AKit.Speed(control, 0) * 0.98f, AKit.Speed(slow, 0), 2);
        }
    }
}
