using System.Collections.Generic;
using System.Linq;
using Confluence.Casting;
using Confluence.Core.Arena;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Sim.Casting;
using Confluence.Sim.Combat;
using Confluence.Sim.Content;
using Confluence.Sim.Content.Augments.AugmentsA;
using Confluence.Sim.Duel;
using Confluence.Sim.Effects;
using Confluence.Reactions.Grid;
using Confluence.Reactions.Operations;
using Confluence.Reactions.Volumes;
using Confluence.Sim.Input;
using Confluence.Sim.Players;
using Confluence.Sim.Spells;
using Confluence.Sim.Tests.Spells;
using Confluence.Sim.Tests.World;
using InputButtons = Confluence.Casting.InputButtons;

namespace Confluence.Sim.Tests.Content.AugmentsA
{
    /// <summary>
    /// Several augments-a records on one player at once: mastery-linked damage and projectile-speed bonuses clamp together
    /// once per cast at 15% (spec L480), and gait bonuses and penalties add up under the +20% movement cap (spec L124)
    /// whatever the attach order.
    /// </summary>
    public class StackingTests
    {
        private static ActiveAugment[] Augs(params (string Id, int Rank)[] list) => list.Select(a => new ActiveAugment(a.Id, a.Rank)).ToArray();

        private static float LaunchSpeed(DuelWorld w, int index) =>
            AKit.Events<SpellEvent>(w).Where(e => e.Kind == SpellEventKind.ProjectileLaunched && e.Player == 0).ElementAt(index).Amount;

        // ----- mastery-linked projectile speed (AUG-001 + AUG-018) -----------------------------------------

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        [Verifies("AUG-001")]
        [Verifies("AUG-018")]
        public void Two_Linked_Speed_Bonuses_On_One_Cast_Clamp_Together_At_15_Percent(bool reversed)
        {
            var list = new[] { (SteadyHand.Id, 2), (DeliberateRepetition.Id, 2) };
            if (reversed) list = list.Reverse().ToArray();
            DuelWorld w = AKit.Duel(AKit.Setup("A", AKit.Bram, Augs(list)));
            AKit.HoldAndRelease(w, AKit.CrestHold);
            AKit.Step(w, AKit.Ticks(5f));
            AKit.HoldAndRelease(w, AKit.CrestHold);
            AKit.Step(w, 30);
            Assert.Equal(24f * 1.10f, LaunchSpeed(w, 0), 3); // first cast: Steady Hand alone (10%)
            Assert.Equal(24f * (1f + GlobalRules.Modifiers.MasteryAttributeCap), LaunchSpeed(w, 1), 3); // 10% + 11% clamped at 15%
        }

        [Fact]
        [Verifies("AUG-001")]
        [Verifies("AUG-018")]
        public void Linked_Speed_Bonuses_Clamp_With_A_Projectile_Speed_Reward()
        {
            DuelWorld w = AKit.Duel(AKit.Setup("A", AKit.Bram, Augs((SteadyHand.Id, 1), (DeliberateRepetition.Id, 1))),
                grammar: _ => new TapGrammar(12, MasteryResult.Mastered(MasteryReward.ProjectileSpeed, 0.10f)));
            AKit.Step(w, 20);
            AKit.HoldAndRelease(w, 12);
            AKit.Step(w, AKit.Ticks(5f));
            AKit.HoldAndRelease(w, 12);
            AKit.Step(w, 30);
            Assert.Equal(24f * 1.15f, LaunchSpeed(w, 0), 3); // 10% reward + 6% clamped
            Assert.Equal(24f * 1.15f, LaunchSpeed(w, 1), 3); // 10% reward + 6% + 7% clamped
        }

        // ----- mastery-linked direct damage (AUG-006 + AUG-020) --------------------------------------------

        /// <summary>Bram with four different damaging spells (SPL-015/016 as test bolts) facing the dummy at 2 m.</summary>
        private static DuelWorld OpusDuel(ActiveAugment[] augs, System.Func<string, Confluence.Casting.Grammars.IGrammar> grammar = null)
        {
            DuelPlayerSetup a = AKit.Setup("A", AKit.Bram, augs);
            a.EquippedSpellIds.Clear();
            a.EquippedSpellIds.AddRange(new[] { "SPL-009", "SPL-010", "SPL-015", "SPL-016" });
            ContentRegistry registry = AKit.RegistryWith(a, AKit.Spell("SPL-015", new TestBolt(20f)), AKit.Spell("SPL-016", new TestBolt(30f)));
            DuelWorld w = AKit.Duel(a, null, null, registry, 0, grammar);
            AKit.FaceOff(w, 2f);
            return w;
        }

        private static void CastSlot(DuelWorld w, int slot, int hold)
        {
            PlayerInput input = AKit.SelectSlot(AKit.A, slot);
            AKit.Step(w, 1, input);
            AKit.HoldAndRelease(w, hold, input);
            for (int i = 0; i < 120 && w.Player(0).Caster.Machine.Phase != CastPhase.Idle; i++) AKit.Step(w, 1, input);
            AKit.Step(w, 8, input);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        [Verifies("AUG-006")]
        [Verifies("AUG-020")]
        public void Anchored_Cast_And_Magnum_Opus_Clamp_Together_With_The_Damage_Reward(bool reversed)
        {
            var list = new[] { (MagnumOpus.Id, 2), (AnchoredCast.Id, 2) };
            if (reversed) list = list.Reverse().ToArray();
            DuelWorld w = OpusDuel(Augs(list));
            for (int slot = 0; slot < 4; slot++) CastSlot(w, slot, AKit.CrestHold);
            List<DamageEvent> hits = AKit.Hits(w, 1, DamageKind.Direct);
            Assert.Equal(4, hits.Count);
            Assert.Equal(20f * 1.15f, hits[2].Resolution.Raw, 2);  // 12% reward + Anchored 10%: clamped
            Assert.Equal(30f * 1.15f, hits[3].Resolution.Raw, 2);  // 12% + 10% + 12%: one clamp at 15% (spec L480)
            Assert.Single(AKit.Activations(w, MagnumOpus.Id));
        }

        [Fact]
        [Verifies("AUG-006")]
        [Verifies("AUG-020")]
        public void Linked_Damage_Bonuses_On_An_Efficiency_Rewarded_Cast_Still_Total_At_Most_15_Percent()
        {
            DuelWorld w = OpusDuel(Augs((MagnumOpus.Id, 2), (AnchoredCast.Id, 2)),
                _ => new TapGrammar(12, MasteryResult.Mastered(MasteryReward.ManaEfficiency, 0.10f)));
            for (int slot = 0; slot < 4; slot++) CastSlot(w, slot, 12);
            List<DamageEvent> hits = AKit.Hits(w, 1, DamageKind.Direct);
            Assert.Equal(4, hits.Count);
            Assert.Equal(20f * 1.10f, hits[2].Resolution.Raw, 2); // Anchored alone
            Assert.Equal(30f * 1.15f, hits[3].Resolution.Raw, 2); // 10% + 12% clamped
        }

        // ----- gait bonuses and penalties (AUG-101, AUG-110, AUG-117, AUG-107, AUG-041) ---------------------

        private static ArenaLayout LongSand()
        {
            ArenaLayout layout = TestWorlds.Flat(40f, 42f);
            layout.Materials.Add(new MaterialPatch { Center = new Vec2(0f, -4f), HalfSize = new Vec2(10f, 15f), Sand = 2, NaturalSource = true });
            return layout;
        }

        private static DuelWorld Runner(params (string Id, int Rank)[] list)
        {
            DuelWorld w = AKit.Duel(AKit.Setup("A", AKit.Bram, Augs(list)), layout: LongSand());
            TestWorlds.Place(w, 0, new Vec3(0f, 0f, -18f), 0f);
            TestWorlds.Place(w, 1, new Vec3(-15f, 0f, 15f), 180f);
            AKit.Step(w, 1);
            return w;
        }

        private static PlayerInput Run => AKit.Walk(0f).With(InputButtons.Sprint);

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        [Verifies("AUG-101")]
        [Verifies("AUG-110")]
        public void Short_Burst_And_Sand_Route_Add_Up_In_Either_Attach_Order(bool reversed)
        {
            var list = new[] { (ShortBurst.Id, 2), (SandRoute.Id, 2) };
            if (reversed) list = list.Reverse().ToArray();
            DuelWorld w = Runner(list);
            AKit.Step(w, 20, Run);
            Assert.True(w.Player(0).Motor.Sprinting);
            Assert.Equal(GlobalRules.Movement.SprintSpeed * 1.10f, AKit.Speed(w, 0), 2); // 5% + 5%
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        [Verifies("AUG-110")]
        [Verifies("AUG-117")]
        public void Graduated_Runner_Penalty_Survives_Sand_Route_In_Either_Attach_Order(bool reversed)
        {
            var list = new[] { (GraduatedRunner.Id, 1), (SandRoute.Id, 2) };
            if (reversed) list = list.Reverse().ToArray();
            DuelWorld w = Runner(list);
            AKit.Step(w, AKit.Ticks(1f), Run);
            Assert.Equal(GlobalRules.Movement.SprintSpeed * 1.05f, AKit.Speed(w, 0), 2); // sand only
            AKit.Step(w, AKit.Ticks(1.5f), Run);
            Assert.True(w.Player(0).Motor.Sprinting);
            Assert.Equal(GlobalRules.Movement.SprintSpeed * 1.03f, AKit.Speed(w, 0), 2); // +5% sand, -2% after 2 s
        }

        [Fact]
        [Verifies("AUG-117")]
        public void A_Gait_Penalty_Applies_After_The_20_Percent_Cap()
        {
            DuelPlayerSetup a = AKit.Setup("A", AKit.Bram, Augs((GraduatedRunner.Id, 1), (SandRoute.Id, 2)));
            a.Modifiers.Add(new StatModifier(StatKind.SprintSpeedBonus, 0.19f, "EQP-TEST"));
            DuelWorld w = AKit.Duel(a, layout: LongSand());
            TestWorlds.Place(w, 0, new Vec3(0f, 0f, -18f), 0f);
            TestWorlds.Place(w, 1, new Vec3(-15f, 0f, 15f), 180f);
            AKit.Step(w, 1);
            AKit.Step(w, AKit.Ticks(1f), Run);
            Assert.Equal(GlobalRules.Movement.SprintSpeed * 1.20f, AKit.Speed(w, 0), 2); // 19% + 5% capped at 20%
            AKit.Step(w, AKit.Ticks(1.5f), Run);
            Assert.Equal(GlobalRules.Movement.SprintSpeed * 1.18f, AKit.Speed(w, 0), 2); // the drawback still bites
        }

        /// <summary>WIZ-002 with Boiler Orb (SPL-014, 0.45 s windup) as a test bolt, Exit Interview and Prepared Patience.</summary>
        private static DuelWorld ExitAndPatience(bool reversed)
        {
            var list = new[] { (ExitInterview.Id, 2), (PreparedPatience.Id, 2) };
            if (reversed) list = list.Reverse().ToArray();
            DuelPlayerSetup a = AKit.Setup("A", AKit.Bram, Augs(list));
            a.EquippedSpellIds.Clear();
            a.EquippedSpellIds.AddRange(new[] { "SPL-014", "SPL-009" });
            return AKit.Duel(a, null, null, AKit.RegistryWith(a, AKit.Spell("SPL-014", new TestBolt(32f, 18f))));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        [Verifies("AUG-107")]
        [Verifies("AUG-041")]
        public void Exit_Interview_Buff_And_Prepared_Patience_Penalty_Add_Up_In_Either_Order(bool reversed)
        {
            DuelWorld w = ExitAndPatience(reversed);
            WorldDuels.Reactions(w).CreateVolume(new VolumeSpec
            {
                Kind = VolumeKind.Steam,
                Footprint = Footprint.Circle(new Vec2(0f, -6f), 1.5f),
                BaseY = 0f,
                LifetimeTicks = AKit.Ticks(8f),
            });
            PlayerInput strafe = AKit.SelectSlot(AKit.Walk(0f, 1f, 0f), 0);
            AKit.Step(w, 1, strafe);
            for (int i = 0; i < 60 && !AKit.Activations(w, ExitInterview.Id).Any(); i++) AKit.Step(w, 1, strafe);
            EffectEvent left = AKit.Activations(w, ExitInterview.Id).Single();

            // Prepare Boiler Orb inside the 1 s buff and keep walking through its windup.
            AKit.HoldAndRelease(w, 4, strafe);
            for (int i = 0; i < 20 && w.Player(0).Caster.Machine.Phase != CastPhase.Windup; i++) AKit.Step(w, 1, strafe);
            AKit.Step(w, 10, strafe);
            Assert.Equal(CastPhase.Windup, w.Player(0).Caster.Machine.Phase);
            Assert.True(w.Tick < left.Tick + AKit.Ticks(1f), "the buff is still running");
            Assert.Equal(WalkDuringWindup(), AKit.Speed(w, 0), 2); // +5% buff and -5% penalty cancel
        }

        /// <summary>The walking speed during the Boiler Orb windup with neither record.</summary>
        private static float WalkDuringWindup()
        {
            DuelPlayerSetup a = AKit.Setup("A", AKit.Bram);
            a.EquippedSpellIds.Clear();
            a.EquippedSpellIds.AddRange(new[] { "SPL-014", "SPL-009" });
            DuelWorld w = AKit.Duel(a, null, null, AKit.RegistryWith(a, AKit.Spell("SPL-014", new TestBolt(32f, 18f))));
            PlayerInput strafe = AKit.SelectSlot(AKit.Walk(0f, 1f, 0f), 0);
            AKit.Step(w, 30, strafe);
            AKit.HoldAndRelease(w, 4, strafe);
            for (int i = 0; i < 20 && w.Player(0).Caster.Machine.Phase != CastPhase.Windup; i++) AKit.Step(w, 1, strafe);
            AKit.Step(w, 10, strafe);
            Assert.Equal(CastPhase.Windup, w.Player(0).Caster.Machine.Phase);
            return AKit.Speed(w, 0);
        }
    }
}
