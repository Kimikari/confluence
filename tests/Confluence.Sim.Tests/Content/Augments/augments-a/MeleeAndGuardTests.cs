using System.Linq;
using Confluence.Casting;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Sim.Casting;
using Confluence.Sim.Combat;
using Confluence.Sim.Content.Augments.AugmentsA;
using Confluence.Sim.Duel;
using Confluence.Sim.Input;
using Confluence.Sim.Players;
using Confluence.Sim.Tests.World;
using InputButtons = Confluence.Casting.InputButtons;

namespace Confluence.Sim.Tests.Content.AugmentsA
{
    /// <summary>The "Melee and guard" shared augments in real duels (common melee L100-L102, guard L104-L106).</summary>
    public class MeleeAndGuardTests
    {
        private static DamageEvent OnlyWeaponHit(DuelWorld w) => AKit.Hits(w, 1, DamageKind.Melee).Single();

        /// <summary>Stamina player 0 spends on the strike begun this step.</summary>
        private static float StrikeCost(DuelWorld w, PlayerInput? a = null)
        {
            float before = w.Player(0).Resources.Stamina;
            Assert.NotNull(AKit.Strike(w, a));
            return before - w.Player(0).Resources.Stamina;
        }

        // ----- AUG-073 Close Curriculum -------------------------------------------------------------------

        [Theory]
        [InlineData(1, 0.04f)]
        [InlineData(2, 0.07f)]
        [Verifies("AUG-073")]
        public void Close_Curriculum_Raises_Weapon_Hits_Within_2_m_By_Rank(int rank, float bonus)
        {
            AKit.AssertQuotes(CloseCurriculum.Id, "within 2 m", "Damage increases 4%", "Damage increases 7%", "beyond 15 m decreases 3%");
            DuelWorld w = AKit.With(CloseCurriculum.Id, rank, AKit.Bram, null, AKit.Item(AKit.LightBlade));
            AKit.FaceOff(w, 1.5f);
            AKit.Strike(w);
            AKit.FinishStrike(w);
            Assert.Equal(GlobalRules.Melee.LightBladeDamage * (1f + bonus), OnlyWeaponHit(w).Resolution.Raw, 3);
        }

        [Fact]
        [Verifies("AUG-073")]
        public void Close_Curriculum_Leaves_Weapon_Hits_Beyond_2_m_And_Close_Spell_Hits_Unchanged()
        {
            DuelWorld spear = AKit.With(CloseCurriculum.Id, 2, AKit.Bram, null, AKit.Item(AKit.Spear));
            AKit.FaceOff(spear, 2.5f); // the 2.8 m spear reaches, but the hit is beyond 2 m
            AKit.Strike(spear);
            AKit.FinishStrike(spear);
            Assert.Equal(GlobalRules.Melee.SpearDamage, OnlyWeaponHit(spear).Resolution.Raw, 3);

            DuelWorld close = AKit.With(CloseCurriculum.Id, 2);
            AKit.FaceOff(close, 1.5f);
            AKit.HoldAndRelease(close, AKit.NormalHold);
            AKit.Step(close, 40);
            DamageEvent fireball = AKit.Hits(close, 1, DamageKind.Direct).Single();
            Assert.Equal(48f, fireball.Resolution.Raw, 3); // the bonus is for weapon hits only (spec L102: melee is not a spell)
        }

        [Theory]
        [InlineData(16f, 48f * 0.97f)]
        [InlineData(14f, 48f)]
        [Verifies("AUG-073")]
        public void Close_Curriculum_Drawback_Cuts_Direct_Spell_Damage_Beyond_15_m(float distance, float expected)
        {
            DuelWorld w = AKit.With(CloseCurriculum.Id, 1);
            AKit.FaceOff(w, distance);
            AKit.HoldAndRelease(w, AKit.NormalHold); // WIZ-002 normal release: Fireball 48 direct, no mastery
            AKit.Step(w, 90);
            Assert.Equal(expected, AKit.Hits(w, 1, DamageKind.Direct).Single().Resolution.Raw, 3);
        }

        // ----- AUG-074 / 075 / 077 profile records --------------------------------------------------------

        [Theory]
        [InlineData(1, 11f)]
        [InlineData(2, 10f)]
        [Verifies("AUG-074")]
        public void Light_Footwork_Cheapens_Light_Blade_Strikes_And_Costs_1_Damage(int rank, float stamina)
        {
            AKit.AssertQuotes(LightFootwork.Id, "light blade", "decreases 1.", "decreases 2.", "Strike damage decreases 1.");
            DuelWorld w = AKit.With(LightFootwork.Id, rank, AKit.Bram, null, AKit.Item(AKit.LightBlade));
            AKit.FaceOff(w, 1.5f);
            Assert.Equal(stamina, StrikeCost(w), 3);
            AKit.FinishStrike(w);
            Assert.Equal(GlobalRules.Melee.LightBladeDamage - 1f, OnlyWeaponHit(w).Resolution.Raw, 3);
        }

        [Fact]
        [Verifies("AUG-074")]
        public void Light_Footwork_Does_Not_Touch_Other_Profiles()
        {
            DuelWorld w = AKit.With(LightFootwork.Id, 2); // no weapon: the issued focus butt strike
            AKit.FaceOff(w, 1.5f);
            Assert.Equal(GlobalRules.Melee.FocusStamina, StrikeCost(w), 3);
            AKit.FinishStrike(w);
            Assert.Equal(GlobalRules.Melee.FocusDamage, OnlyWeaponHit(w).Resolution.Raw, 3);
        }

        [Theory]
        [InlineData(1, 18f)]
        [InlineData(2, 17f)]
        [Verifies("AUG-075")]
        public void Heavy_Economy_Cheapens_Heavy_Strikes_And_Lengthens_The_Windup_By_005_s(int rank, float stamina)
        {
            AKit.AssertQuotes(HeavyEconomy.Id, "heavy weapon", "decreases 2.", "decreases 3.", "Windup increases 0.05 s.");
            DuelWorld w = AKit.With(HeavyEconomy.Id, rank, AKit.Bram, null, AKit.Item(AKit.Heavy));
            AKit.FaceOff(w, 1.5f);
            int start = w.Tick + 1;
            Assert.Equal(stamina, StrikeCost(w), 3);
            Assert.Equal(AKit.Ticks(0.40f), w.Player(0).Melee.Profile.WindupTicks);
            Assert.Equal(AKit.Ticks(1.30f), w.Player(0).Melee.Profile.CycleTicks); // the recovery is unchanged
            AKit.FinishStrike(w);
            DamageEvent hit = OnlyWeaponHit(w);
            Assert.Equal(GlobalRules.Melee.HeavyDamage, hit.Resolution.Raw, 3);

            DuelWorld control = AKit.Without(AKit.Bram, null, AKit.Item(AKit.Heavy));
            AKit.FaceOff(control, 1.5f);
            int controlStart = control.Tick + 1;
            Assert.Equal(GlobalRules.Melee.HeavyStamina, StrikeCost(control), 3);
            AKit.FinishStrike(control);

            // The heavy sweep reaches the target at the same point of its live window; it lands 0.05 s later.
            Assert.Equal(AKit.Ticks(0.05f), (hit.Tick - start) - (OnlyWeaponHit(control).Tick - controlStart));
            Assert.True(hit.Tick - start >= AKit.Ticks(0.40f));
        }

        [Fact]
        [Verifies("AUG-075")]
        public void Heavy_Economy_Leaves_The_Light_Blade_Alone()
        {
            DuelWorld w = AKit.With(HeavyEconomy.Id, 2, AKit.Bram, null, AKit.Item(AKit.LightBlade));
            AKit.FaceOff(w, 1.5f);
            Assert.Equal(GlobalRules.Melee.LightBladeStamina, StrikeCost(w), 3);
            Assert.Equal(AKit.Ticks(GlobalRules.Melee.LightBladeWindupSec), w.Player(0).Melee.Profile.WindupTicks);
        }

        [Theory]
        [InlineData(1, 19f)]
        [InlineData(2, 20f)]
        [Verifies("AUG-077")]
        public void Buttstrike_Lesson_Adds_Damage_To_The_Focus_Strike_And_Costs_1_Stamina(int rank, float damage)
        {
            AKit.AssertQuotes(ButtstrikeLesson.Id, "wand or staff", "Damage increases 1.", "Damage increases 2.", "Strike stamina cost increases 1.");
            DuelWorld w = AKit.With(ButtstrikeLesson.Id, rank); // the issued plain focus uses the butt strike (spec L571)
            AKit.FaceOff(w, 1.5f);
            Assert.Equal(GlobalRules.Melee.FocusStamina + 1f, StrikeCost(w), 3);
            AKit.FinishStrike(w);
            Assert.Equal(damage, OnlyWeaponHit(w).Resolution.Raw, 3);

            DuelWorld blade = AKit.With(ButtstrikeLesson.Id, rank, AKit.Bram, null, AKit.Item(AKit.LightBlade));
            AKit.FaceOff(blade, 1.5f);
            Assert.Equal(GlobalRules.Melee.LightBladeStamina, StrikeCost(blade), 3);
        }

        // ----- AUG-076 Spear Poise -------------------------------------------------------------------------

        [Theory]
        [InlineData(1, 3.25f, true)]
        [InlineData(2, 3.35f, true)]
        [InlineData(1, 3.35f, false)]
        [Verifies("AUG-076")]
        public void Spear_Poise_Extends_A_Grounded_Spear_Strike_By_Rank(int rank, float distance, bool hits)
        {
            AKit.AssertQuotes(SpearPoise.Id, "grounded without sprint", "Reach increases 0.1 m.", "Reach increases 0.2 m.", "5% slower");
            DuelWorld w = AKit.With(SpearPoise.Id, rank, AKit.Bram, null, AKit.Item(AKit.Spear));
            AKit.FaceOff(w, distance); // the spear's 2.8 m reach plus the 0.4 m body radius reaches 3.2 m
            AKit.Strike(w);
            Assert.Equal(GlobalRules.Melee.SpearReach + (rank == 2 ? 0.2f : 0.1f), w.Player(0).Melee.Profile.Reach, 4);
            AKit.FinishStrike(w);
            Assert.Equal(hits, AKit.Hits(w, 1, DamageKind.Melee).Count == 1);

            DuelWorld control = AKit.Without(AKit.Bram, null, AKit.Item(AKit.Spear));
            AKit.FaceOff(control, 3.25f);
            AKit.Strike(control);
            AKit.FinishStrike(control);
            Assert.Empty(AKit.Hits(control, 1, DamageKind.Melee));
        }

        [Fact]
        [Verifies("AUG-076")]
        public void Spear_Poise_Gives_No_Reach_To_A_Strike_Begun_In_The_Air()
        {
            DuelWorld w = AKit.With(SpearPoise.Id, 2, AKit.Bram, null, AKit.Item(AKit.Spear));
            AKit.FaceOff(w, 6f);
            AKit.Step(w, 1, AKit.A.With(InputButtons.Jump));
            AKit.Step(w, 3);
            Assert.False(w.Player(0).Motor.Grounded);
            Assert.NotNull(AKit.Strike(w));
            Assert.Equal(GlobalRules.Melee.SpearReach, w.Player(0).Melee.Profile.Reach, 4);
        }

        [Fact]
        [Verifies("AUG-076")]
        public void Spear_Poise_Drawback_Slows_Walking_5_Percent_During_The_Spear_Windup_Only()
        {
            DuelWorld w = AKit.With(SpearPoise.Id, 1, AKit.Bram, null, AKit.Item(AKit.Spear));
            PlayerInput walk = AKit.Walk(0f);
            AKit.Step(w, 40, walk);
            Assert.Equal(GlobalRules.Movement.WalkSpeed, AKit.Speed(w, 0), 2);
            AKit.Strike(w, walk);
            AKit.Step(w, 8, walk);
            Assert.Equal(MeleePhase.Windup, w.Player(0).Melee.Phase);
            Assert.Equal(GlobalRules.Movement.WalkSpeed * 0.95f, AKit.Speed(w, 0), 2);
            AKit.FinishStrike(w, 0, walk);
            AKit.Step(w, 2, walk);
            Assert.Equal(GlobalRules.Movement.WalkSpeed, AKit.Speed(w, 0), 2);
        }

        // ----- AUG-078 Guard Economy -----------------------------------------------------------------------

        [Theory]
        [InlineData(1, 23f)]
        [InlineData(2, 21f)]
        [Verifies("AUG-078")]
        public void Guard_Economy_Cheapens_The_Guard_And_Adds_02_s_Cooldown(int rank, float cost)
        {
            AKit.AssertQuotes(GuardEconomy.Id, "Upfront stamina cost decreases 2.", "Upfront stamina cost decreases 4.", "Guard cooldown increases 0.2 s.");
            DuelWorld w = AKit.With(GuardEconomy.Id, rank);
            AKit.Step(w, 1, AKit.A.With(InputButtons.Guard));
            Assert.True(w.Player(0).Guard.Active);
            Assert.Equal(100f - cost, w.Player(0).Resources.Stamina, 3);
            AKit.Step(w, 5, AKit.A.With(InputButtons.Guard));
            AKit.Step(w, 1);
            GuardState g = w.Player(0).Guard;
            Assert.False(g.Active);
            Assert.Equal(AKit.Ticks(GlobalRules.Guard.CooldownSec + 0.2f), g.CooldownUntil - g.ReleaseTick);

            AKit.Step(w, AKit.Ticks(GlobalRules.Guard.CooldownSec) - 1);
            AKit.Step(w, 1, AKit.A.With(InputButtons.Guard));
            Assert.False(w.Player(0).Guard.Active); // 3.0 s after release: still cooling down
            AKit.Step(w, 1);
            AKit.Step(w, AKit.Ticks(0.2f));
            AKit.Step(w, 1, AKit.A.With(InputButtons.Guard));
            Assert.True(w.Player(0).Guard.Active);
        }

        // ----- AUG-080 Guarded Study -----------------------------------------------------------------------

        /// <summary>Player 1 strikes player 0 at 1.5 m; player 0 raises a perfect guard and releases it. Returns the perfect tick.</summary>
        private static int PerfectGuardAgainstStrike(DuelWorld w)
        {
            AKit.FaceOff(w, 1.5f);
            AKit.Strike(w, null, null, 1);
            int start = w.Tick;
            AKit.Step(w, 4);
            for (int i = 0; i < 8; i++) AKit.Step(w, 1, AKit.A.With(InputButtons.Guard)); // guard from start+5, strike at start+11
            AKit.Step(w, 1);
            GuardEvent perfect = AKit.Events<GuardEvent>(w).Single(g => g.Player == 0 && g.Change == GuardChange.PerfectBlocked);
            Assert.Equal(start + AKit.Ticks(GlobalRules.Melee.FocusWindupSec), perfect.Tick);
            return perfect.Tick;
        }

        [Theory]
        [InlineData(1, 16f)]
        [InlineData(2, 14f)]
        [Verifies("AUG-080")]
        public void Guarded_Study_Discounts_A_Spell_Paid_Within_05_s_Of_A_Perfect_Guard_And_Adds_005_s_Recovery(int rank, float paid)
        {
            AKit.AssertQuotes(GuardedStudy.Id, "Perfect guard then paid spell within 0.5 s.", "costs 2 less mana", "costs 4 less mana", "0.05 s longer recovery");
            DuelWorld w = AKit.With(GuardedStudy.Id, rank);
            int perfect = PerfectGuardAgainstStrike(w);
            AKit.Step(w, perfect + 17 - w.Tick); // the 0.25 s guard release window blocks casting (spec L104)
            AKit.HoldAndRelease(w, QuickTap); // a tap: WIZ-002 commits (and pays) 0.2 s after the press
            AKit.Step(w, 12);
            CastDuelEvent commit = AKit.Casts(w, CastEventKind.Committed).Single();
            Assert.True(commit.Tick - perfect <= AKit.Ticks(GuardedStudy.WindowSec)); // paid within 0.5 s (spec L90)
            Assert.Equal(paid, commit.Cast.Mana, 3);
            Assert.Single(AKit.Activations(w, GuardedStudy.Id));
            AKit.Step(w, 60);
            int release = AKit.Casts(w, CastEventKind.Released).Single().Tick;
            int recoveryEnd = AKit.Casts(w, CastEventKind.RecoveryEnded).Single().Tick;
            Assert.Equal(AKit.Ticks(GlobalRules.Casting.RecoveryQuickProjectileSec + 0.05f), recoveryEnd - release);
        }

        [Fact]
        [Verifies("AUG-080")]
        public void Guarded_Study_A_Spell_Begun_In_The_Window_But_Paid_After_It_Pays_In_Full()
        {
            DuelWorld w = AKit.With(GuardedStudy.Id, 2);
            int perfect = PerfectGuardAgainstStrike(w);
            AKit.Step(w, perfect + 17 - w.Tick);
            AKit.HoldAndRelease(w, AKit.NormalHold); // a 0.25 s hold commits at its release, 0.55 s after the guard
            CastDuelEvent commit = AKit.Casts(w, CastEventKind.Committed).Single();
            CastDuelEvent prep = AKit.Casts(w, CastEventKind.PreparationStarted).Single();
            Assert.True(prep.Tick - perfect <= AKit.Ticks(GuardedStudy.WindowSec));
            Assert.True(commit.Tick - perfect > AKit.Ticks(GuardedStudy.WindowSec));
            Assert.Equal(18f, commit.Cast.Mana, 3);
            AKit.Step(w, 60);
            int release = AKit.Casts(w, CastEventKind.Released).Single().Tick;
            Assert.Equal(AKit.Ticks(GlobalRules.Casting.RecoveryQuickProjectileSec), AKit.Casts(w, CastEventKind.RecoveryEnded).Single().Tick - release);
        }

        /// <summary>A WIZ-002 release before 0.20 s: the cast is queued and commits 0.2 s after the press (D-056).</summary>
        private const int QuickTap = 4;

        [Fact]
        [Verifies("AUG-080")]
        public void Guarded_Study_Gives_Nothing_After_The_Window_Or_Without_A_Perfect_Guard()
        {
            DuelWorld late = AKit.With(GuardedStudy.Id, 2);
            int perfect = PerfectGuardAgainstStrike(late);
            AKit.Step(late, perfect + AKit.Ticks(0.5f) + 2 - late.Tick);
            AKit.HoldAndRelease(late, AKit.NormalHold);
            Assert.Equal(18f, AKit.Casts(late, CastEventKind.Committed).Single().Cast.Mana, 3);
            AKit.Step(late, 60);
            int release = AKit.Casts(late, CastEventKind.Released).Single().Tick;
            Assert.Equal(AKit.Ticks(GlobalRules.Casting.RecoveryQuickProjectileSec), AKit.Casts(late, CastEventKind.RecoveryEnded).Single().Tick - release);

            DuelWorld plain = AKit.With(GuardedStudy.Id, 2);
            AKit.HoldAndRelease(plain, AKit.NormalHold);
            Assert.Equal(18f, AKit.Casts(plain, CastEventKind.Committed).Single().Cast.Mana, 3);
        }

        [Fact]
        [Verifies("AUG-080")]
        public void Guarded_Study_Discount_Stays_Inside_The_70_Percent_Cost_Floor()
        {
            DuelPlayerSetup a = AKit.Setup("A", AKit.Bram, AKit.Aug(GuardedStudy.Id, 2));
            a.Modifiers.Add(new StatModifier(StatKind.CostReduction, 0.30f, "EQP-TEST"));
            DuelWorld w = AKit.Duel(a);
            int perfect = PerfectGuardAgainstStrike(w);
            AKit.Step(w, perfect + 17 - w.Tick);
            AKit.HoldAndRelease(w, QuickTap);
            AKit.Step(w, 12);
            Assert.Equal(18f * 0.7f, AKit.Casts(w, CastEventKind.Committed).Single().Cast.Mana, 3);
        }

        // ----- AUG-083 Bolt and Blade ----------------------------------------------------------------------

        /// <summary>Player 0 hits player 1 at the distance with a normal Fireball and waits out the recovery; returns the hit tick.</summary>
        private static int FireballHit(DuelWorld w, float distance)
        {
            AKit.FaceOff(w, distance);
            AKit.HoldAndRelease(w, AKit.NormalHold);
            for (int i = 0; i < 60 && AKit.Hits(w, 1, DamageKind.Direct).Count == 0; i++) AKit.Step(w, 1);
            int hit = AKit.Hits(w, 1, DamageKind.Direct).Single().Tick;
            for (int i = 0; i < 60 && w.Player(0).Caster.Machine.Phase != CastPhase.Idle; i++) AKit.Step(w, 1);
            return hit;
        }

        [Theory]
        [InlineData(1, 6f)]
        [InlineData(2, 4f)]
        [Verifies("AUG-083")]
        public void Bolt_And_Blade_Cheapens_The_First_Strike_Within_1_s_Of_A_Direct_Spell_Hit(int rank, float stamina)
        {
            AKit.AssertQuotes(BoltAndBlade.Id, "Direct spell hit followed by weapon hit within 1 s.", "2 less stamina", "4 less stamina", "consumes opportunity");
            DuelWorld w = AKit.With(BoltAndBlade.Id, rank);
            int hit = FireballHit(w, 1.8f);
            Assert.True(w.Tick - hit < AKit.Ticks(1f));
            Assert.Equal(stamina, StrikeCost(w), 3);
            AKit.FinishStrike(w);
            Assert.Single(AKit.Hits(w, 1, DamageKind.Melee));
            Assert.Equal(GlobalRules.Melee.FocusStamina, StrikeCost(w), 3); // the opportunity is used
        }

        [Fact]
        [Verifies("AUG-083")]
        public void Bolt_And_Blade_Opportunity_Is_Spent_By_A_Missed_Strike_And_Lapses_After_1_s()
        {
            DuelWorld miss = AKit.With(BoltAndBlade.Id, 2);
            FireballHit(miss, 2.5f); // the focus strike reaches 2 m: this strike misses
            Assert.Equal(4f, StrikeCost(miss), 3);
            AKit.FinishStrike(miss);
            Assert.Empty(AKit.Hits(miss, 1, DamageKind.Melee));
            Assert.Equal(GlobalRules.Melee.FocusStamina, StrikeCost(miss), 3);

            DuelWorld late = AKit.With(BoltAndBlade.Id, 2);
            int hit = FireballHit(late, 1.8f);
            AKit.Step(late, hit + AKit.Ticks(1f) + 1 - late.Tick);
            Assert.Equal(GlobalRules.Melee.FocusStamina, StrikeCost(late), 3);
        }

        // ----- AUG-084 Walking Pressure --------------------------------------------------------------------

        /// <summary>Player 0 walks toward player 1 and lands a focus strike while walking; returns the hit tick.</summary>
        private static int WalkingHit(DuelWorld w)
        {
            AKit.FaceOff(w, 2.8f);
            PlayerInput walk = AKit.Walk(0f);
            AKit.Step(w, 4, walk);
            AKit.Strike(w, walk);
            for (int i = 0; i < 30 && AKit.Hits(w, 1, DamageKind.Melee).Count == 0; i++) AKit.Step(w, 1, walk);
            return AKit.Hits(w, 1, DamageKind.Melee).Single().Tick;
        }

        [Theory]
        [InlineData(1, 0.03f)]
        [InlineData(2, 0.05f)]
        [Verifies("AUG-084")]
        public void Walking_Pressure_Grants_Walk_Speed_For_1_s_After_A_Walking_Hit(int rank, float bonus)
        {
            AKit.AssertQuotes(WalkingPressure.Id, "while walking, not sprinting", "Gain 3% walk speed for 1 s.", "Gain 5% walk speed for 1 s.", "4 s cooldown; sprint cancels buff.");
            DuelWorld w = AKit.With(WalkingPressure.Id, rank);
            int hit = WalkingHit(w);
            PlayerInput strafe = AKit.Walk(0f, 1f, 0f);
            AKit.Step(w, 20, strafe);
            Assert.Equal(GlobalRules.Movement.WalkSpeed * (1f + bonus), AKit.Speed(w, 0), 2);
            AKit.Step(w, hit + AKit.Ticks(1f) + 2 - w.Tick, strafe);
            Assert.Equal(GlobalRules.Movement.WalkSpeed, AKit.Speed(w, 0), 2);
            Assert.Single(AKit.Activations(w, WalkingPressure.Id));
        }

        [Fact]
        [Verifies("AUG-084")]
        public void Walking_Pressure_Sprint_Cancels_The_Buff_And_A_Standing_Hit_Grants_Nothing()
        {
            DuelWorld w = AKit.With(WalkingPressure.Id, 2);
            int hit = WalkingHit(w);
            PlayerInput walk = AKit.Walk(90f);
            AKit.FinishStrike(w, 0, walk); // a strike's cycle forbids sprinting (spec L80)
            AKit.Step(w, 2, WorldDuels.Sprint(90f));
            Assert.True(w.Player(0).Motor.Sprinting);
            AKit.Step(w, 12, walk);
            Assert.True(w.Tick < hit + AKit.Ticks(1f));
            Assert.Equal(GlobalRules.Movement.WalkSpeed, AKit.Speed(w, 0), 2);

            DuelWorld standing = AKit.With(WalkingPressure.Id, 2);
            AKit.FaceOff(standing, 1.5f);
            AKit.Strike(standing);
            AKit.FinishStrike(standing);
            Assert.Single(AKit.Hits(standing, 1, DamageKind.Melee));
            Assert.Empty(AKit.Activations(standing, WalkingPressure.Id));
        }

        [Fact]
        [Verifies("AUG-084")]
        public void Walking_Pressure_Has_A_4_s_Cooldown_And_Respects_The_20_Percent_Movement_Cap()
        {
            DuelWorld w = AKit.With(WalkingPressure.Id, 2);
            WalkingHit(w);
            AKit.FinishStrike(w);
            AKit.Step(w, 20); // stand, then a second walking hit inside 4 s
            WalkingHitAgain(w);
            Assert.Single(AKit.Activations(w, WalkingPressure.Id));

            DuelPlayerSetup a = AKit.Setup("A", AKit.Bram, AKit.Aug(WalkingPressure.Id, 2));
            a.Modifiers.Add(new StatModifier(StatKind.WalkSpeedBonus, 0.19f, "EQP-TEST"));
            DuelWorld capped = AKit.Duel(a);
            WalkingHit(capped);
            AKit.Step(capped, 20, AKit.Walk(0f, 1f, 0f));
            Assert.Equal(GlobalRules.Movement.WalkSpeed * (1f + GlobalRules.Modifiers.MovementBonusCap), AKit.Speed(capped, 0), 2);
        }

        private static void WalkingHitAgain(DuelWorld w)
        {
            int before = AKit.Hits(w, 1, DamageKind.Melee).Count;
            Vec3 a = w.Player(0).Position;
            TestWorlds.Place(w, 1, a + new Vec3(0f, 0f, 2.8f), 180f);
            PlayerInput walk = AKit.Walk(0f);
            AKit.Step(w, 4, walk);
            AKit.Strike(w, walk);
            for (int i = 0; i < 30 && AKit.Hits(w, 1, DamageKind.Melee).Count == before; i++) AKit.Step(w, 1, walk);
            Assert.Equal(before + 1, AKit.Hits(w, 1, DamageKind.Melee).Count);
        }

        // ----- AUG-085 Guard Breaker -----------------------------------------------------------------------

        [Theory]
        [InlineData(1, 3f)]
        [InlineData(2, 5f)]
        [Verifies("AUG-085")]
        public void Guard_Breaker_Removes_Extra_Stamina_From_A_Guard_That_Blocks_The_Weapon(int rank, float drain)
        {
            AKit.AssertQuotes(GuardBreaker.Id, "impacts active enemy guard", "Remove 3 additional stamina.", "Remove 5 additional stamina.", "Unguarded-target weapon damage decreases 3%.");
            DuelWorld w = AKit.With(GuardBreaker.Id, rank);
            AKit.FaceOff(w, 1.5f);
            AKit.Strike(w, null, AKit.B.With(InputButtons.Guard)); // player 1 guards from the same tick (ordinary at impact)
            for (int i = 0; i < 20; i++) AKit.Step(w, 1, null, AKit.B.With(InputButtons.Guard));
            DamageEvent hit = AKit.Hits(w, 1, DamageKind.Melee).Single();
            Assert.Equal(GuardOutcome.Guarded, hit.Resolution.Guard);
            Assert.Equal(GlobalRules.Melee.FocusDamage, hit.Resolution.Raw, 3); // no penalty on a guarded target
            Assert.Equal(100f - GlobalRules.Guard.StaminaCost - drain, w.Player(1).Resources.Stamina, 3);
            Assert.Single(AKit.Activations(w, GuardBreaker.Id));
        }

        [Fact]
        [Verifies("AUG-085")]
        public void Guard_Breaker_Drawback_Cuts_Weapon_Damage_On_An_Unguarded_Target()
        {
            DuelWorld w = AKit.With(GuardBreaker.Id, 2);
            AKit.FaceOff(w, 1.5f);
            AKit.Strike(w);
            AKit.FinishStrike(w);
            Assert.Equal(GlobalRules.Melee.FocusDamage * 0.97f, OnlyWeaponHit(w).Resolution.Raw, 3);
            Assert.Equal(100f, w.Player(1).Resources.Stamina, 3);

            // A guard facing away does not block: the target is unguarded for this hit.
            DuelWorld flank = AKit.With(GuardBreaker.Id, 2);
            AKit.FaceOff(flank, 1.5f);
            PlayerInput away = PlayerInput.Idle(0f).With(InputButtons.Guard);
            AKit.Strike(flank, null, away);
            for (int i = 0; i < 20; i++) AKit.Step(flank, 1, null, away);
            DamageEvent hit = AKit.Hits(flank, 1, DamageKind.Melee).Single();
            Assert.Equal(GuardOutcome.None, hit.Resolution.Guard);
            Assert.Equal(GlobalRules.Melee.FocusDamage * 0.97f, hit.Resolution.Raw, 3);
            Assert.Equal(100f - GlobalRules.Guard.StaminaCost, flank.Player(1).Resources.Stamina, 3);
        }

        // ----- AUG-086 Shield Cut --------------------------------------------------------------------------

        [Theory]
        [InlineData(1, 5f)]
        [InlineData(2, 8f)]
        [Verifies("AUG-086")]
        public void Shield_Cut_Removes_Extra_Shield_When_A_Weapon_Hit_Damages_A_Shield(int rank, float extra)
        {
            AKit.AssertQuotes(ShieldCut.Id, "Direct weapon hit damages a shield.", "Shield-only damage increases 5.", "Shield-only damage increases 8.", "Unshielded player damage decreases 2%.");
            DuelWorld w = AKit.With(ShieldCut.Id, rank);
            AKit.FaceOff(w, 1.5f);
            AKit.Step(w, 1);
            w.GrantShield(1, "SPL-TEST", 30f, AKit.Ticks(6f));
            AKit.Strike(w);
            AKit.FinishStrike(w);
            DamageEvent hit = OnlyWeaponHit(w);
            Assert.Equal(GlobalRules.Melee.FocusDamage, hit.Resolution.Raw, 3); // shielded: no drawback
            Assert.Equal(GlobalRules.Melee.FocusDamage, hit.Resolution.ShieldAbsorbed, 3);
            Assert.Equal(30f - GlobalRules.Melee.FocusDamage - extra, w.Player(1).Shields.Total, 3);
            Assert.Equal(300f, w.Player(1).Resources.Health, 3);
            Assert.Single(AKit.Activations(w, ShieldCut.Id));
        }

        [Fact]
        [Verifies("AUG-086")]
        public void Shield_Cut_Never_Reaches_Health_And_Its_Drawback_Cuts_Hits_On_Unshielded_Targets()
        {
            DuelWorld w = AKit.With(ShieldCut.Id, 2);
            AKit.FaceOff(w, 1.5f);
            AKit.Step(w, 1);
            w.GrantShield(1, "SPL-TEST", 10f, AKit.Ticks(6f));
            AKit.Strike(w);
            AKit.FinishStrike(w);
            Assert.Equal(0f, w.Player(1).Shields.Total, 3);
            Assert.Equal(300f - (GlobalRules.Melee.FocusDamage - 10f), w.Player(1).Resources.Health, 3); // the extra is shield-only

            DuelWorld bare = AKit.With(ShieldCut.Id, 1);
            AKit.FaceOff(bare, 1.5f);
            AKit.Strike(bare);
            AKit.FinishStrike(bare);
            Assert.Equal(GlobalRules.Melee.FocusDamage * 0.98f, OnlyWeaponHit(bare).Resolution.Raw, 3);
            Assert.Empty(AKit.Activations(bare, ShieldCut.Id));
        }

        // ----- AUG-087 Measured Follow-Through -------------------------------------------------------------

        [Theory]
        [InlineData(1, 16f)]
        [InlineData(2, 15f)]
        [Verifies("AUG-087")]
        public void Measured_Follow_Through_Discounts_A_Spell_Committed_Within_08_s_Of_A_Missed_Strike(int rank, float paid)
        {
            AKit.AssertQuotes(MeasuredFollowThrough.Id, "miss a weapon strike", "within 0.8 s", "2 less mana, 4 s cooldown", "3 less mana, 4 s cooldown", "Weapon recovery still applies");
            DuelWorld w = AKit.With(MeasuredFollowThrough.Id, rank);
            AKit.FaceOff(w, 6f);
            AKit.Strike(w);
            AKit.FinishStrike(w); // the recovery still applies: no cast before the cycle ends
            Assert.Empty(AKit.Hits(w, 1, DamageKind.Melee));
            AKit.HoldAndRelease(w, 12);
            CastDuelEvent commit = AKit.Casts(w, CastEventKind.Committed).Single();
            Assert.Equal(paid, commit.Cast.Mana, 3);
        }

        [Fact]
        [Verifies("AUG-087")]
        public void Measured_Follow_Through_Lapses_After_08_s_And_Has_A_4_s_Cooldown()
        {
            DuelWorld late = AKit.With(MeasuredFollowThrough.Id, 2);
            AKit.FaceOff(late, 6f);
            AKit.Strike(late);
            AKit.FinishStrike(late);
            AKit.Step(late, 20);
            AKit.HoldAndRelease(late, 12);
            Assert.Equal(18f, AKit.Casts(late, CastEventKind.Committed).Single().Cast.Mana, 3);

            DuelWorld w = AKit.With(MeasuredFollowThrough.Id, 2);
            AKit.FaceOff(w, 6f);
            AKit.Strike(w);
            AKit.FinishStrike(w);
            AKit.HoldAndRelease(w, 12); // SPL-009 at 15
            AKit.Step(w, 40);

            // A second miss inside the 4 s cooldown: Cinder Sweep (slot 1, 15 mana) pays in full.
            PlayerInput slot1 = AKit.SelectSlot(AKit.A, 1);
            AKit.Strike(w, slot1);
            AKit.FinishStrike(w, 0, slot1);
            AKit.HoldAndRelease(w, 12, slot1);
            AKit.Step(w, AKit.Ticks(5f)); // past 4 s since the first miss and the SPL-009 cooldown
            AKit.Strike(w);
            AKit.FinishStrike(w);
            AKit.HoldAndRelease(w, 12);
            float[] paid = AKit.Casts(w, CastEventKind.Committed).Select(c => c.Cast.Mana).ToArray();
            Assert.Equal(new[] { 15f, 15f, 15f }, paid);
            Assert.Equal(new[] { "SPL-009", "SPL-010", "SPL-009" }, AKit.Casts(w, CastEventKind.Committed).Select(c => c.SpellId).ToArray());
            Assert.Equal(2, AKit.Activations(w, MeasuredFollowThrough.Id).Count()); // the second miss was inside the cooldown
        }

        // ----- AUG-089 Patient Edge ------------------------------------------------------------------------

        [Theory]
        [InlineData(1, 21f)]
        [InlineData(2, 23f)]
        [Verifies("AUG-089")]
        public void Patient_Edge_Adds_Damage_To_A_Strike_After_2_s_Without_A_Weapon_Attack(int rank, float damage)
        {
            AKit.AssertQuotes(PatientEdge.Id, "No weapon attack for 2 s", "gains 3 damage", "gains 5 damage", "A miss spends the preparation.");
            DuelWorld w = AKit.With(PatientEdge.Id, rank);
            AKit.FaceOff(w, 1.5f);
            AKit.Strike(w);
            AKit.FinishStrike(w);
            AKit.Strike(w); // straight after: no preparation
            AKit.FinishStrike(w);
            AKit.Step(w, AKit.Ticks(2f));
            AKit.Strike(w);
            AKit.FinishStrike(w);
            float[] raw = AKit.Hits(w, 1, DamageKind.Melee).Select(h => h.Resolution.Raw).ToArray();
            Assert.Equal(new[] { damage, GlobalRules.Melee.FocusDamage, damage }, raw);
        }

        [Fact]
        [Verifies("AUG-089")]
        public void Patient_Edge_A_Missed_Strike_Spends_The_Preparation()
        {
            DuelWorld w = AKit.With(PatientEdge.Id, 2);
            AKit.FaceOff(w, 6f);
            AKit.Strike(w); // a miss
            AKit.FinishStrike(w);
            AKit.FaceOff(w, 1.5f);
            AKit.Step(w, 10);
            AKit.Strike(w);
            AKit.FinishStrike(w);
            Assert.Equal(GlobalRules.Melee.FocusDamage, OnlyWeaponHit(w).Resolution.Raw, 3);
        }

        // ----- AUG-090 Desperate Guard ---------------------------------------------------------------------

        [Theory]
        [InlineData(1, 22f)]
        [InlineData(2, 20f)]
        [Verifies("AUG-090")]
        public void Desperate_Guard_Cheapens_A_Guard_Below_90_Health_And_Shortens_Every_Guard_By_01_s(int rank, float cost)
        {
            AKit.AssertQuotes(DesperateGuard.Id, "Guard below 90 health.", "Cost decreases 3 stamina.", "Cost decreases 5 stamina.", "Guard duration decreases 0.1 s.");
            DuelWorld w = AKit.With(DesperateGuard.Id, rank);
            AKit.Step(w, 1);
            w.ApplyDamage(TestWorlds.Hit(w, 1, 0, 215f));
            AKit.Step(w, 1);
            Assert.True(w.Player(0).Resources.Health < 90f);
            AKit.Step(w, 1, AKit.A.With(InputButtons.Guard));
            Assert.Equal(100f - cost, w.Player(0).Resources.Stamina, 3);
            int start = w.Player(0).Guard.StartTick;
            AKit.Step(w, 60, AKit.A.With(InputButtons.Guard));
            Assert.Equal(AKit.Ticks(0.7f), w.Player(0).Guard.ReleaseTick - start);

            DuelWorld healthy = AKit.With(DesperateGuard.Id, rank);
            AKit.Step(healthy, 1, AKit.A.With(InputButtons.Guard));
            Assert.Equal(100f - GlobalRules.Guard.StaminaCost, healthy.Player(0).Resources.Stamina, 3);
            int s = healthy.Player(0).Guard.StartTick;
            AKit.Step(healthy, 60, AKit.A.With(InputButtons.Guard));
            Assert.Equal(AKit.Ticks(0.7f), healthy.Player(0).Guard.ReleaseTick - s); // the drawback applies at any health
        }

        // ----- AUG-092 Grand Riposte -----------------------------------------------------------------------

        [Theory]
        [InlineData(1, 0.06f)]
        [InlineData(2, 0.10f)]
        [Verifies("AUG-092")]
        public void Grand_Riposte_Boosts_The_Spell_After_A_Perfect_Guard_And_A_Weapon_Hit(int rank, float bonus)
        {
            AKit.AssertQuotes(GrandRiposte.Id, "Perfect guard, direct weapon hit, then a different paid spell within 2 s.", "6% direct damage", "10% direct damage", "Any missed step resets sequence.");
            DuelWorld w = AKit.With(GrandRiposte.Id, rank);
            int perfect = PerfectGuardAgainstStrike(w);
            AKit.Step(w, perfect + 16 - w.Tick);
            AKit.Strike(w);
            AKit.FinishStrike(w);
            Assert.Single(AKit.Hits(w, 1, DamageKind.Melee));
            AKit.HoldAndRelease(w, AKit.NormalHold);
            Assert.True(AKit.Casts(w, CastEventKind.Committed).Single().Tick <= perfect + AKit.Ticks(2f));
            AKit.Step(w, 40);
            Assert.Equal(48f * (1f + bonus), AKit.Hits(w, 1, DamageKind.Direct).Single().Resolution.Raw, 3);
            Assert.Single(AKit.Activations(w, GrandRiposte.Id));
        }

        [Fact]
        [Verifies("AUG-092")]
        public void Grand_Riposte_Resets_When_A_Step_Is_Missed()
        {
            // The weapon strike misses: the sequence resets and the spell is normal.
            DuelWorld missed = AKit.With(GrandRiposte.Id, 2);
            int perfect = PerfectGuardAgainstStrike(missed);
            AKit.Step(missed, perfect + 16 - missed.Tick);
            TestWorlds.Place(missed, 1, new Vec3(0f, 0f, 4f), 180f);
            AKit.Strike(missed);
            AKit.FinishStrike(missed);
            AKit.FaceOff(missed, 1.5f);
            AKit.Strike(missed);
            AKit.FinishStrike(missed);
            AKit.HoldAndRelease(missed, AKit.NormalHold);
            AKit.Step(missed, 40);
            Assert.Equal(48f, AKit.Hits(missed, 1, DamageKind.Direct).Single().Resolution.Raw, 3);

            // No perfect guard at all: nothing.
            DuelWorld plain = AKit.With(GrandRiposte.Id, 2);
            AKit.FaceOff(plain, 1.5f);
            AKit.Strike(plain);
            AKit.FinishStrike(plain);
            AKit.HoldAndRelease(plain, AKit.NormalHold);
            AKit.Step(plain, 40);
            Assert.Equal(48f, AKit.Hits(plain, 1, DamageKind.Direct).Single().Resolution.Raw, 3);
            Assert.Empty(AKit.Activations(plain, GrandRiposte.Id));
        }
    }
}
