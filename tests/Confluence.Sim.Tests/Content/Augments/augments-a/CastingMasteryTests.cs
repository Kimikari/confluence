using System.Collections.Generic;
using System.Linq;
using Confluence.Casting;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Reactions.Grid;
using Confluence.Reactions.Operations;
using Confluence.Reactions.Volumes;
using Confluence.Sim.Casting;
using Confluence.Sim.Combat;
using Confluence.Sim.Content;
using Confluence.Sim.Content.Augments.AugmentsA;
using Confluence.Sim.Duel;
using Confluence.Sim.Effects;
using Confluence.Sim.Input;
using Confluence.Sim.Players;
using Confluence.Sim.Spells;
using Confluence.Sim.Tests.Spells;
using Confluence.Sim.Tests.World;
using InputButtons = Confluence.Casting.InputButtons;

namespace Confluence.Sim.Tests.Content.AugmentsA
{
    /// <summary>
    /// The "Casting mastery" shared augments with WIZ-002's real crest-release grammar: a crest release (0.45 s) earns the
    /// +12% direct-damage reward (an advanced variation); a 0.25 s release is a normal cast (EarlyRelease).
    /// </summary>
    public class CastingMasteryTests
    {
        private static CastDuelEvent Commit(DuelWorld w, int index = 0) => AKit.Casts(w, CastEventKind.Committed).ElementAt(index);

        private static int ReleaseMinusCommit(DuelWorld w, int index = 0) =>
            AKit.Casts(w, CastEventKind.Released).ElementAt(index).Tick - Commit(w, index).Tick;

        private static float LaunchSpeed(DuelWorld w, int index = 0) =>
            AKit.Events<SpellEvent>(w).Where(e => e.Kind == SpellEventKind.ProjectileLaunched && e.Player == 0).ElementAt(index).Amount;

        // ----- AUG-012 Perfect Landing ---------------------------------------------------------------------

        /// <summary>Ticks from the jump step to the landing tick on the flat floor.</summary>
        private static int LandingDelay()
        {
            DuelWorld w = AKit.Without();
            AKit.Step(w, 1, AKit.A.With(InputButtons.Jump));
            int jump = w.Tick;
            for (int i = 0; i < 90 && !AKit.Events<MovementEvent>(w).Any(m => m.Player == 0 && m.Change == MovementChange.Landed); i++) AKit.Step(w, 1);
            return AKit.Events<MovementEvent>(w).Single(m => m.Player == 0 && m.Change == MovementChange.Landed).Tick - jump;
        }

        /// <summary>Jumps, then commits a Fireball <paramref name="afterLanding"/> ticks after the landing with the given hold.</summary>
        private static void CommitAfterLanding(DuelWorld w, int afterLanding, int hold = AKit.NormalHold)
        {
            int delay = LandingDelay();
            AKit.Step(w, 1, AKit.A.With(InputButtons.Jump));
            int landing = w.Tick + delay;
            int press = landing + afterLanding - hold;
            AKit.Step(w, press - 1 - w.Tick);
            AKit.HoldAndRelease(w, hold);
            Assert.Equal(landing + afterLanding, Commit(w).Tick);
            Assert.Equal(landing, AKit.Events<MovementEvent>(w).Single(m => m.Player == 0 && m.Change == MovementChange.Landed).Tick);
        }

        /// <summary>Jumps, then commits a Fireball so that its release (0.35 s windup) falls <paramref name="afterLanding"/> ticks after the landing.</summary>
        private static void ReleaseAfterLanding(DuelWorld w, int afterLanding, int hold = AKit.NormalHold)
        {
            int windup = AKit.Ticks(0.35f);
            int delay = LandingDelay();
            AKit.Step(w, 1, AKit.A.With(InputButtons.Jump));
            int expectedLanding = w.Tick + delay;
            int press = expectedLanding + afterLanding - windup - hold;
            AKit.Step(w, press - 1 - w.Tick);
            AKit.HoldAndRelease(w, hold); // commits in the air; the windup runs through the landing
            AKit.Step(w, windup + 5);
            int landing = AKit.Events<MovementEvent>(w).Single(m => m.Player == 0 && m.Change == MovementChange.Landed).Tick;
            Assert.Equal(landing + afterLanding, AKit.Casts(w, CastEventKind.Released).Single().Tick);
        }

        [Theory]
        [InlineData(1, 6, 2f)]
        [InlineData(2, 6, 4f)]
        [InlineData(2, 15, 4f)]
        [InlineData(2, 5, 0f)]
        [InlineData(2, 16, 0f)]
        [Verifies("AUG-012")]
        public void Perfect_Landing_Decreases_The_Cost_Of_A_Normal_Spell_Released_01_To_025_s_After_Landing(int rank, int afterLanding, float decrease)
        {
            AKit.AssertQuotes(PerfectLanding.Id, "Release a normal paid spell 0.1-0.25 s after landing.", "Cost decreases 2 mana.", "Cost decreases 4 mana.", "Landing outside the window grants nothing.");
            DuelWorld w = AKit.With(PerfectLanding.Id, rank);
            ReleaseAfterLanding(w, afterLanding);
            Assert.False(Commit(w).Cast.Mastery.IsMastered);
            Assert.Equal(18f, Commit(w).Cast.Mana, 3);  // paid at commitment (spec L90)
            Assert.Equal(decrease, Refunded(w), 3);      // settled at the qualifying release
            Assert.Equal(decrease > 0f ? 1 : 0, AKit.Activations(w, PerfectLanding.Id).Count());
        }

        [Fact]
        [Verifies("AUG-012")]
        public void Perfect_Landing_A_Commitment_In_The_Window_Whose_Release_Falls_Outside_It_Gets_Nothing()
        {
            DuelWorld w = AKit.With(PerfectLanding.Id, 2);
            CommitAfterLanding(w, 6); // committed 0.1 s after landing: released 0.45 s after it
            AKit.Step(w, 40);
            int landing = AKit.Events<MovementEvent>(w).Single(m => m.Player == 0 && m.Change == MovementChange.Landed).Tick;
            Assert.True(AKit.Casts(w, CastEventKind.Released).Single().Tick - landing > AKit.Ticks(PerfectLanding.WindowEndSec));
            Assert.Equal(18f, Commit(w).Cast.Mana, 3);
            Assert.Equal(0f, Refunded(w), 3);
        }

        [Fact]
        [Verifies("AUG-012")]
        public void Perfect_Landing_Gives_Nothing_To_An_Advanced_Variation_And_Respects_The_Refund_Cap()
        {
            DuelWorld crest = AKit.With(PerfectLanding.Id, 2);
            ReleaseAfterLanding(crest, 10, AKit.CrestHold);
            Assert.True(Commit(crest).Cast.Mastery.IsMastered);
            Assert.Equal(0f, Refunded(crest), 3);

            // At the 70% cost floor the 4 mana still comes back, but only up to 25% of the payment (spec L124).
            DuelPlayerSetup a = AKit.Setup("A", AKit.Bram, AKit.Aug(PerfectLanding.Id, 2));
            a.Modifiers.Add(new StatModifier(StatKind.CostReduction, 0.30f, "EQP-TEST"));
            DuelWorld floor = AKit.Duel(a);
            ReleaseAfterLanding(floor, 8);
            Assert.Equal(18f * 0.7f, Commit(floor).Cast.Mana, 3);
            Assert.Equal(18f * 0.7f * GlobalRules.Casting.RefundCapFraction, Refunded(floor), 3);
        }

        // ----- AUG-019 Pressure Examination ----------------------------------------------------------------

        private static DuelWorld Wounded(string id, int rank, float health = 85f)
        {
            DuelWorld w = AKit.With(id, rank);
            AKit.Step(w, 1);
            w.ApplyDamage(TestWorlds.Hit(w, 1, 0, 300f - health));
            AKit.Step(w, 1);
            return w;
        }

        [Theory]
        [InlineData(1, 7f)]
        [InlineData(2, 12f)]
        [Verifies("AUG-019")]
        public void Pressure_Examination_Shields_An_Advanced_Variation_Below_90_Health_For_1_s(int rank, float shield)
        {
            AKit.AssertQuotes(PressureExamination.Id, "advanced variation below 90 health", "Gain 7 shield for 1 s, 5 s cooldown.", "Gain 12 shield for 1 s, 5 s cooldown.", "No bonus for normal casts; shield follows 100 cap.");
            DuelWorld w = Wounded(PressureExamination.Id, rank);
            AKit.HoldAndRelease(w, AKit.CrestHold);
            Assert.True(Commit(w).Cast.Mastery.IsMastered);
            ShieldEntry entry = w.Player(0).Shields.Entries.Single(e => e.SourceId == PressureExamination.Id);
            Assert.Equal(shield, entry.Amount, 3);
            Assert.Equal(w.Tick + AKit.Ticks(1f), entry.ExpireTick);
            AKit.Step(w, AKit.Ticks(1f) + 1);
            Assert.DoesNotContain(w.Player(0).Shields.Entries, e => e.SourceId == PressureExamination.Id);
        }

        [Fact]
        [Verifies("AUG-019")]
        public void Pressure_Examination_Ignores_Normal_Casts_And_Healthy_Casters()
        {
            DuelWorld normal = Wounded(PressureExamination.Id, 2);
            AKit.HoldAndRelease(normal, AKit.NormalHold);
            Assert.Empty(normal.Player(0).Shields.Entries);

            DuelWorld healthy = Wounded(PressureExamination.Id, 2, 95f);
            AKit.HoldAndRelease(healthy, AKit.CrestHold);
            Assert.Empty(healthy.Player(0).Shields.Entries);
        }

        [Fact]
        [Verifies("AUG-019")]
        public void Pressure_Examination_Follows_The_100_Shield_Cap_And_Its_5_s_Cooldown()
        {
            DuelWorld w = Wounded(PressureExamination.Id, 2);
            w.GrantShield(0, "SPL-TEST", 95f, AKit.Ticks(8f));
            AKit.HoldAndRelease(w, AKit.CrestHold);
            Assert.Equal(GlobalRules.Shields.Cap, w.Player(0).Shields.Total, 3);
            Assert.Equal(5f, w.Player(0).Shields.Entries.Single(e => e.SourceId == PressureExamination.Id).Amount, 3);

            DuelWorld cd = Wounded(PressureExamination.Id, 2);
            AKit.HoldAndRelease(cd, AKit.CrestHold);
            AKit.Step(cd, 40);
            AKit.HoldAndRelease(cd, AKit.CrestHold, AKit.SelectSlot(AKit.A, 1)); // Cinder Sweep, inside 5 s
            Assert.Equal(2, AKit.Casts(cd, CastEventKind.Committed).Count(c => c.Cast.Mastery.IsMastered));
            Assert.Single(AKit.Activations(cd, PressureExamination.Id));
        }

        // ----- AUG-020 Magnum Opus -------------------------------------------------------------------------

        /// <summary>Bram with Fireball, Cinder Sweep and two test bolts under SPL-015/SPL-016 (four different damaging spells).</summary>
        private static DuelWorld OpusDuel(int rank, System.Func<string, Confluence.Casting.Grammars.IGrammar> grammar = null)
        {
            DuelPlayerSetup a = AKit.Setup("A", AKit.Bram, AKit.Aug(MagnumOpus.Id, rank));
            a.EquippedSpellIds.Clear();
            a.EquippedSpellIds.AddRange(new[] { "SPL-009", "SPL-010", "SPL-015", "SPL-016" });
            ContentRegistry registry = AKit.RegistryWith(a, AKit.Spell("SPL-015", new TestBolt(20f)), AKit.Spell("SPL-016", new TestBolt(30f)));
            DuelWorld w = AKit.Duel(a, null, null, registry, 0, grammar);
            AKit.FaceOff(w, 2f);
            return w;
        }

        private static void CastSlot(DuelWorld w, int slot, int hold, bool aimAway = false)
        {
            PlayerInput input = AKit.SelectSlot(aimAway ? PlayerInput.Idle(90f) : AKit.A, slot);
            AKit.Step(w, 1, input);
            AKit.HoldAndRelease(w, hold, input);
            for (int i = 0; i < 120 && w.Player(0).Caster.Machine.Phase != CastPhase.Idle; i++) AKit.Step(w, 1, input);
            AKit.Step(w, 8, input); // projectiles land
        }

        [Fact]
        [Verifies("AUG-020")]
        public void Magnum_Opus_Boosts_The_Fourth_Different_Advanced_Hit_Inside_The_15_Percent_Mastery_Cap()
        {
            AKit.AssertQuotes(MagnumOpus.Id, "advanced variations of 4 different spells within 6 s", "Fourth cast gains 8% direct damage.", "Fourth cast gains 12% direct damage.", "A miss resets progress; total mastery damage/speed bonus capped at 15%.");
            DuelWorld w = OpusDuel(1);
            for (int slot = 0; slot < 4; slot++) CastSlot(w, slot, AKit.CrestHold);
            Assert.True(w.Tick < AKit.Ticks(6f));
            List<DamageEvent> hits = AKit.Hits(w, 1, DamageKind.Direct);
            Assert.Equal(4, hits.Count);
            Assert.Equal(48f * 1.12f, hits[0].Resolution.Raw, 2);
            Assert.Equal(20f * 1.12f, hits[2].Resolution.Raw, 2);
            Assert.Equal(30f * 1.15f, hits[3].Resolution.Raw, 2); // 12% reward + 8% would be 20%: clamped at 15% (spec L480)
            Assert.Single(AKit.Activations(w, MagnumOpus.Id));
        }

        [Theory]
        [InlineData(1, 0.08f)]
        [InlineData(2, 0.12f)]
        [Verifies("AUG-020")]
        public void Magnum_Opus_Gives_Its_Full_Rank_Bonus_When_The_Reward_Is_Not_Damage(int rank, float bonus)
        {
            // A grammar whose advanced variation pays 10% less mana (an efficiency reward): no damage reward to clamp with.
            DuelWorld w = OpusDuel(rank, _ => new TapGrammar(12, MasteryResult.Mastered(MasteryReward.ManaEfficiency, 0.10f)));
            for (int slot = 0; slot < 4; slot++) CastSlot(w, slot, 12);
            List<DamageEvent> hits = AKit.Hits(w, 1, DamageKind.Direct);
            Assert.Equal(4, hits.Count);
            Assert.Equal(20f, hits[2].Resolution.Raw, 2);
            Assert.Equal(30f * (1f + bonus), hits[3].Resolution.Raw, 2);
        }

        [Fact]
        [Verifies("AUG-020")]
        public void Magnum_Opus_A_Miss_Or_Normal_Casts_Leave_The_Fourth_Hit_Unboosted()
        {
            DuelWorld miss = OpusDuel(2);
            CastSlot(miss, 0, AKit.CrestHold);
            CastSlot(miss, 1, AKit.CrestHold);
            CastSlot(miss, 2, AKit.CrestHold, aimAway: true); // a mastered cast that misses resets
            CastSlot(miss, 3, AKit.CrestHold);
            Assert.Equal(30f * 1.12f, AKit.Hits(miss, 1, DamageKind.Direct).Last().Resolution.Raw, 2);
            Assert.Empty(AKit.Activations(miss, MagnumOpus.Id));

            DuelWorld normal = OpusDuel(2);
            CastSlot(normal, 0, AKit.CrestHold);
            CastSlot(normal, 1, AKit.NormalHold); // a normal cast does not count
            CastSlot(normal, 2, AKit.CrestHold);
            CastSlot(normal, 3, AKit.CrestHold);
            Assert.Equal(30f * 1.12f, AKit.Hits(normal, 1, DamageKind.Direct).Last().Resolution.Raw, 2);
        }

        [Fact]
        [Verifies("AUG-020")]
        public void Magnum_Opus_Needs_The_Four_Hits_Inside_6_s()
        {
            DuelWorld slow = OpusDuel(2);
            CastSlot(slow, 0, AKit.CrestHold);
            AKit.Step(slow, AKit.Ticks(3f));
            CastSlot(slow, 1, AKit.CrestHold);
            CastSlot(slow, 2, AKit.CrestHold);
            CastSlot(slow, 3, AKit.CrestHold);
            List<DamageEvent> hits = AKit.Hits(slow, 1, DamageKind.Direct);
            Assert.True(hits[3].Tick - hits[0].Tick > AKit.Ticks(6f));
            Assert.Equal(30f * 1.12f, hits[3].Resolution.Raw, 2);
        }

        // ----- AUG-001 Steady Hand -------------------------------------------------------------------------

        [Theory]
        [InlineData(1, 0.06f)]
        [InlineData(2, 0.10f)]
        [Verifies("AUG-001")]
        public void Steady_Hand_Speeds_A_Projectile_Released_With_A_Steady_Aim(int rank, float bonus)
        {
            AKit.AssertQuotes(SteadyHand.Id, "within a 1-degree cone for 0.3 s", "Gain 6% projectile speed.", "Gain 10% projectile speed.", "Sprinting resets preparation.");
            DuelWorld w = AKit.With(SteadyHand.Id, rank);
            AKit.HoldAndRelease(w, AKit.NormalHold);
            AKit.Step(w, 30);
            Assert.Equal(24f * (1f + bonus), LaunchSpeed(w), 3);
        }

        [Fact]
        [Verifies("AUG-001")]
        public void Steady_Hand_Needs_The_Aim_To_Stay_Within_1_Degree_And_No_Sprint()
        {
            DuelWorld swing = AKit.With(SteadyHand.Id, 2);
            AKit.HoldAndRelease(swing, AKit.NormalHold);
            for (int i = 0; i < 30; i++) AKit.Step(swing, 1, PlayerInput.Idle(0.2f * (i + 1))); // drifts 0.2 degrees per tick
            Assert.Equal(24f, LaunchSpeed(swing), 3);

            DuelWorld sprint = AKit.With(SteadyHand.Id, 2);
            AKit.HoldAndRelease(sprint, AKit.NormalHold, WorldDuels.Sprint(0f));
            AKit.Step(sprint, 30, WorldDuels.Sprint(0f));
            Assert.Equal(24f, LaunchSpeed(sprint), 3);
        }

        [Fact]
        [Verifies("AUG-001")]
        public void Steady_Hand_Clamps_With_A_Projectile_Speed_Reward_At_15_Percent()
        {
            DuelPlayerSetup a = AKit.Setup("A", AKit.Bram, AKit.Aug(SteadyHand.Id, 2));
            DuelWorld w = AKit.Duel(a, grammar: _ => new TapGrammar(12, MasteryResult.Mastered(MasteryReward.ProjectileSpeed, 0.12f)));
            AKit.Step(w, 20);
            AKit.HoldAndRelease(w, 12);
            AKit.Step(w, 30);
            Assert.Equal(24f * 1.15f, LaunchSpeed(w), 3);
        }

        // ----- AUG-005 On the Beat -------------------------------------------------------------------------

        [Theory]
        [InlineData(1, 1)]
        [InlineData(2, 2)]
        [Verifies("AUG-005")]
        public void On_The_Beat_Shortens_The_Windup_Of_A_Crest_Release(int rank, int ticksSaved)
        {
            AKit.AssertQuotes(OnTheBeat.Id, "published timing window", "Gain 6% preparation speed for that cast.", "Gain 10% preparation speed for that cast.", "Window remains fixed; benefit does not shorten the telegraph floor.");
            DuelWorld w = AKit.With(OnTheBeat.Id, rank);
            AKit.HoldAndRelease(w, AKit.CrestHold);
            AKit.Step(w, 40);
            Assert.True(Commit(w).Cast.Mastery.IsMastered);
            Assert.Equal(AKit.Ticks(0.35f) - ticksSaved, ReleaseMinusCommit(w)); // 21 ticks less 6% (10%), rounded down

            DuelWorld normal = AKit.With(OnTheBeat.Id, rank);
            AKit.HoldAndRelease(normal, AKit.NormalHold);
            AKit.Step(normal, 40);
            Assert.Equal(AKit.Ticks(0.35f), ReleaseMinusCommit(normal));
        }

        [Fact]
        [Verifies("AUG-005")]
        public void On_The_Beat_Never_Shortens_The_Anticipatory_Cue()
        {
            // A 0.25 s spell above 70 direct damage needs the 0.25 s cue (spec L96); a grammar committing at once.
            DuelPlayerSetup a = AKit.Setup("A", AKit.Bram, AKit.Aug(OnTheBeat.Id, 2));
            a.EquippedSpellIds.Clear();
            a.EquippedSpellIds.Add("SPL-073");
            ContentRegistry registry = AKit.RegistryWith(a, AKit.Spell("SPL-073", new TestBolt(80f)));
            DuelWorld w = AKit.Duel(a, null, null, registry, 0, _ => new TapGrammar(0, MasteryResult.Mastered(MasteryReward.DirectDamage, 0.10f)));
            AKit.Step(w, 1, AKit.A.With(InputButtons.Primary));
            AKit.Step(w, 30);
            int cue = AKit.Casts(w, CastEventKind.CueStarted).Single().Tick;
            int release = AKit.Casts(w, CastEventKind.Released).Single().Tick;
            Assert.Equal(AKit.Ticks(GlobalRules.Casting.AnticipatoryCueMinSec), release - cue); // 15 ticks less 10% would be 14

            // When the preparation already covered the cue, the same cast does release one tick sooner.
            DuelWorld later = AKit.Duel(a, null, null, registry, 0, _ => new TapGrammar(10, MasteryResult.Mastered(MasteryReward.DirectDamage, 0.10f)));
            AKit.Step(later, 1, AKit.A.With(InputButtons.Primary));
            AKit.Step(later, 40);
            Assert.Equal(AKit.Ticks(0.25f) - 1, ReleaseMinusCommit(later));
        }

        // ----- AUG-017 Recovered Rhythm --------------------------------------------------------------------

        private static float Refunded(DuelWorld w) => AKit.Casts(w, CastEventKind.Refunded).Sum(c => c.Cast.Mana);

        [Theory]
        [InlineData(1, 2f)]
        [InlineData(2, 4f)]
        [Verifies("AUG-017")]
        public void Recovered_Rhythm_Refunds_A_Mastered_Cast_Of_A_Different_Spell_After_A_Failed_One(int rank, float refund)
        {
            AKit.AssertQuotes(RecoveredRhythm.Id, "Fail one optional timing variation, then succeed on a different spell within 4 s.", "refunds 2 mana", "refunds 4 mana", "still pays normal cast costs and cooldown");
            DuelWorld w = AKit.With(RecoveredRhythm.Id, rank);
            PlayerInput cinder = AKit.SelectSlot(AKit.A, 1);
            AKit.Step(w, 1, cinder);
            AKit.HoldAndRelease(w, AKit.NormalHold, cinder); // a failed variation of Cinder Sweep: full cost and cooldown
            Assert.Equal(MissReason.EarlyRelease, Commit(w).Cast.Mastery.Miss);
            Assert.Equal(15f, Commit(w).Cast.Mana, 3);
            Assert.True(w.Player(0).Caster.CooldownRemainingTicks("SPL-010") > 0);
            AKit.Step(w, 40, cinder);
            AKit.Step(w, 1);
            AKit.HoldAndRelease(w, AKit.CrestHold); // Fireball crest within 4 s
            Assert.True(Commit(w, 1).Cast.Mastery.IsMastered);
            Assert.Equal(refund, Refunded(w), 3);
        }

        [Fact]
        [Verifies("AUG-017")]
        public void Recovered_Rhythm_Refund_Stays_Inside_The_25_Percent_Cap()
        {
            DuelWorld w = AKit.With(RecoveredRhythm.Id, 2);
            AKit.HoldAndRelease(w, AKit.NormalHold); // fail Fireball
            AKit.Step(w, 40);
            PlayerInput cinder = AKit.SelectSlot(AKit.A, 1);
            AKit.Step(w, 1, cinder);
            AKit.HoldAndRelease(w, AKit.CrestHold, cinder); // Cinder Sweep crest pays 15: at most 3.75 back
            Assert.Equal(15f * GlobalRules.Casting.RefundCapFraction, Refunded(w), 3);
        }

        [Fact]
        [Verifies("AUG-017")]
        public void Recovered_Rhythm_Needs_A_Different_Spell_Within_4_s()
        {
            DuelWorld same = AKit.With(RecoveredRhythm.Id, 2);
            AKit.HoldAndRelease(same, AKit.NormalHold);
            AKit.Step(same, AKit.Ticks(5f));
            AKit.HoldAndRelease(same, AKit.CrestHold); // the same spell
            Assert.Equal(0f, Refunded(same), 3);

            DuelWorld late = AKit.With(RecoveredRhythm.Id, 2);
            PlayerInput cinder = AKit.SelectSlot(AKit.A, 1);
            AKit.Step(late, 1, cinder);
            AKit.HoldAndRelease(late, AKit.NormalHold, cinder);
            AKit.Step(late, AKit.Ticks(4f));
            AKit.HoldAndRelease(late, AKit.CrestHold);
            Assert.Equal(0f, Refunded(late), 3);
        }

        // ----- AUG-009 Needle Discipline -------------------------------------------------------------------

        [Theory]
        [InlineData(1, 0.05f)]
        [InlineData(2, 0.08f)]
        [Verifies("AUG-009")]
        public void Needle_Discipline_Boosts_A_Head_Hit_And_Cuts_Body_Hits(int rank, float bonus)
        {
            AKit.AssertQuotes(NeedleDiscipline.Id, "within 0.2 m of a head weak point", "Gain 5% direct damage, within 1.25 precision cap.", "Gain 8% direct damage, within 1.25 precision cap.", "Non-precision projectile damage decreases 3%.");
            DuelWorld head = AKit.With(NeedleDiscipline.Id, rank);
            AKit.FaceOff(head, 6f);
            AKit.HoldAndRelease(head, AKit.NormalHold); // a level shot from eye height meets the head
            AKit.Step(head, 40);
            Assert.Equal(48f * (1f + bonus), AKit.Hits(head, 1, DamageKind.Direct).Single().Resolution.Raw, 3);

            DuelWorld body = AKit.With(NeedleDiscipline.Id, rank);
            AKit.FaceOff(body, 6f);
            PlayerInput low = SpellTestKit.Aim(body, 0, new Vec3(0f, 0.7f, 6f));
            AKit.HoldAndRelease(body, AKit.NormalHold, low);
            AKit.Step(body, 40, low);
            Assert.Equal(48f * 0.97f, AKit.Hits(body, 1, DamageKind.Direct).Single().Resolution.Raw, 3);
        }

        [Fact]
        [Verifies("AUG-009")]
        public void Needle_Discipline_Stays_Inside_The_125_Precision_Cap()
        {
            DuelPlayerSetup a = AKit.Setup("A", AKit.Bram, AKit.Aug(NeedleDiscipline.Id, 2));
            a.EquippedSpellIds.Clear();
            a.EquippedSpellIds.Add("SPL-015");
            ContentRegistry registry = AKit.RegistryWith(a, AKit.Spell("SPL-015", new TestBolt(40f, 30f, 1.2f)));
            DuelWorld w = AKit.Duel(a, null, null, registry);
            AKit.FaceOff(w, 6f);
            AKit.HoldAndRelease(w, AKit.NormalHold);
            AKit.Step(w, 40);
            DamageEvent hit = AKit.Hits(w, 1, DamageKind.Direct).Single();
            Assert.Equal(40f * GlobalRules.Damage.PrecisionMultiplierMax, hit.Resolution.AfterPrecision, 3); // 1.2 x 1.08 would be 1.296
        }

        // ----- AUG-013 Patient Cursor ----------------------------------------------------------------------

        [Theory]
        [InlineData(1, 0.05f)]
        [InlineData(2, 0.08f)]
        [Verifies("AUG-013")]
        public void Patient_Cursor_Boosts_A_Projectile_After_06_s_Of_Tracking(int rank, float bonus)
        {
            AKit.AssertQuotes(PatientCursor.Id, "within 2-degree aim cone for 0.6 s", "Next direct projectile gains 5% damage.", "Next direct projectile gains 8% damage.", "Cover or loss of aim resets preparation.");
            DuelWorld w = AKit.With(PatientCursor.Id, rank);
            AKit.FaceOff(w, 8f);
            PlayerInput aim = SpellTestKit.AimAt(w, 0, 1);
            AKit.Step(w, 10, aim);
            AKit.HoldAndRelease(w, AKit.NormalHold, aim);
            AKit.Step(w, 40, aim);
            Assert.Equal(48f * (1f + bonus), AKit.Hits(w, 1, DamageKind.Direct).Single().Resolution.Raw, 3);
            Assert.Single(AKit.Activations(w, PatientCursor.Id));
        }

        [Fact]
        [Verifies("AUG-013")]
        public void Patient_Cursor_Is_Reset_By_Losing_Sight_Or_Aim()
        {
            DuelWorld off = AKit.With(PatientCursor.Id, 2);
            AKit.FaceOff(off, 8f);
            PlayerInput wide = PlayerInput.Idle(3f); // 3 degrees off: still hits the body, never tracked
            AKit.Step(off, 10, wide);
            AKit.HoldAndRelease(off, AKit.NormalHold, wide);
            AKit.Step(off, 40, wide);
            Assert.Equal(48f, AKit.Hits(off, 1, DamageKind.Direct).Single().Resolution.Raw, 3);

            // Steam between them blocks acquisition (spec L167) but not the projectile.
            DuelWorld steam = AKit.With(PatientCursor.Id, 2);
            AKit.FaceOff(steam, 8f);
            WorldDuels.Reactions(steam).CreateVolume(new VolumeSpec { Kind = VolumeKind.Steam, Footprint = Footprint.Circle(new Vec2(0f, 4f), 1.5f), BaseY = 0f, LifetimeTicks = AKit.Ticks(8f) });
            PlayerInput aim = SpellTestKit.AimAt(steam, 0, 1);
            AKit.Step(steam, 10, aim);
            Assert.False(steam.CanAcquire(0, 1));
            AKit.HoldAndRelease(steam, AKit.NormalHold, aim);
            AKit.Step(steam, 40, aim);
            Assert.Equal(48f, AKit.Hits(steam, 1, DamageKind.Direct).Single().Resolution.Raw, 3);
        }

        // ----- AUG-018 Deliberate Repetition ---------------------------------------------------------------

        [Theory]
        [InlineData(1, 0.07f)]
        [InlineData(2, 0.11f)]
        [Verifies("AUG-018")]
        public void Deliberate_Repetition_Speeds_The_Second_Advanced_Variation_Of_The_Same_Spell(int rank, float bonus)
        {
            AKit.AssertQuotes(DeliberateRepetition.Id, "same spell twice, at least 1 s apart", "Second variation gains 7% projectile speed.", "Second variation gains 11% projectile speed.", "A different spell resets progress.");
            DuelWorld w = AKit.With(DeliberateRepetition.Id, rank);
            AKit.HoldAndRelease(w, AKit.CrestHold);
            AKit.Step(w, AKit.Ticks(5f));
            AKit.HoldAndRelease(w, AKit.CrestHold);
            AKit.Step(w, 30);
            Assert.Equal(24f, LaunchSpeed(w, 0), 3);
            Assert.Equal(24f * (1f + bonus), LaunchSpeed(w, 1), 3);
        }

        [Fact]
        [Verifies("AUG-018")]
        public void Deliberate_Repetition_Is_Reset_By_A_Different_Spell_And_Needs_Two_Variations()
        {
            DuelWorld w = AKit.With(DeliberateRepetition.Id, 2);
            AKit.HoldAndRelease(w, AKit.CrestHold);
            AKit.Step(w, 60);
            PlayerInput cinder = AKit.SelectSlot(AKit.A, 1);
            AKit.Step(w, 1, cinder);
            AKit.HoldAndRelease(w, AKit.NormalHold, cinder); // a different spell resets
            AKit.Step(w, AKit.Ticks(4f));
            AKit.HoldAndRelease(w, AKit.CrestHold);
            AKit.Step(w, 30);
            Assert.Equal(24f, LaunchSpeed(w, 1), 3);

            DuelWorld normal = AKit.With(DeliberateRepetition.Id, 2);
            AKit.HoldAndRelease(normal, AKit.CrestHold);
            AKit.Step(normal, AKit.Ticks(5f));
            AKit.HoldAndRelease(normal, AKit.NormalHold); // the second cast is not an advanced variation
            AKit.Step(normal, 30);
            Assert.Equal(24f, LaunchSpeed(normal, 1), 3);
        }
    }
}
