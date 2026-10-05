using System.Linq;
using Confluence.Casting;
using Confluence.Casting.Grammars;
using Confluence.Core.Arena;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Sim.Casting;
using Confluence.Sim.Combat;
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
    /// <summary>
    /// Conditional augments-a records whose draft requirement names equipment (melee, guard, movement, mastery, precision,
    /// terrain items): their behavior in real duels on WIZ-002 (Fireball 48 direct, 24 m/s, 0.35 s windup).
    /// </summary>
    public class ConditionalTests
    {
        private static void Fire(DuelWorld w, PlayerInput? aim = null, PlayerInput? b = null, int flyTicks = 60)
        {
            PlayerInput input = aim ?? AKit.A;
            AKit.Step(w, 1, input, b);
            AKit.HoldAndRelease(w, AKit.NormalHold, input, b);
            AKit.Step(w, flyTicks, input, b);
        }

        private static float Direct(DuelWorld w) => AKit.Hits(w, 1, DamageKind.Direct).Last().Resolution.Raw;

        private static float LaunchSpeed(DuelWorld w) =>
            AKit.Events<SpellEvent>(w).Last(e => e.Kind == SpellEventKind.ProjectileLaunched && e.Player == 0).Amount;

        /// <summary>WIZ-002 whose grammar commits on the press with the given result (Fireball only, unless a spell is given).</summary>
        private static DuelWorld Tapped(string id, int rank, MasteryResult result, string spellId = "SPL-009", ISpellBehavior behavior = null, ArenaLayout layout = null)
        {
            DuelPlayerSetup a = AKit.Setup("A", AKit.Bram, id == null ? null : AKit.Aug(id, rank));
            a.EquippedSpellIds.Clear();
            a.EquippedSpellIds.Add(spellId);
            ContentRegistry registry = behavior == null ? AKit.RegistryWith(a) : AKit.RegistryWith(a, AKit.Spell(spellId, behavior));
            return AKit.Duel(a, null, layout, registry, 0, _ => new TapGrammar(0, result));
        }

        private static MasteryResult ManaReward => MasteryResult.Mastered(MasteryReward.ManaEfficiency, 0.15f);

        private static MasteryResult Normal => MasteryResult.Normal(MissReason.EarlyRelease);

        private static void Tap(DuelWorld w, PlayerInput? input = null, int afterTicks = 60, PlayerInput? b = null)
        {
            PlayerInput i = input ?? AKit.A;
            AKit.Step(w, 1, i.With(InputButtons.Primary), b);
            AKit.Step(w, afterTicks, i, b);
        }

        // ----- AUG-032 Range Compression / AUG-033 Range Extension -----------------------------------------

        [Theory]
        [InlineData(1, 0.06f)]
        [InlineData(2, 0.10f)]
        [Verifies("AUG-032")]
        public void Range_Compression_Boosts_Close_Projectile_Hits(int rank, float bonus)
        {
            AKit.AssertQuotes(RangeCompression.Id, "Direct projectile hits within 5 m.", "Damage increases 6%.", "Damage increases 10%.", "Damage beyond 15 m decreases 8%.");
            DuelWorld w = AKit.With(RangeCompression.Id, rank);
            AKit.FaceOff(w, 4f);
            Fire(w);
            Assert.Equal(48f * (1f + bonus), Direct(w), 3);
        }

        [Theory]
        [InlineData(10f, 1f)]
        [InlineData(16f, 0.92f)]
        [Verifies("AUG-032")]
        public void Range_Compression_Leaves_Mid_Range_And_Cuts_Long_Range(float distance, float factor)
        {
            DuelWorld w = AKit.With(RangeCompression.Id, 2);
            AKit.FaceOff(w, distance);
            Fire(w);
            Assert.Equal(48f * factor, Direct(w), 3);
        }

        [Theory]
        [InlineData(1, 0.06f)]
        [InlineData(2, 0.10f)]
        [Verifies("AUG-033")]
        public void Range_Extension_Boosts_Long_Projectile_Hits(int rank, float bonus)
        {
            AKit.AssertQuotes(RangeExtension.Id, "Direct projectile hits beyond 15 m.", "Damage increases 6%.", "Damage increases 10%.", "Damage within 5 m decreases 8%.");
            DuelWorld w = AKit.With(RangeExtension.Id, rank);
            AKit.FaceOff(w, 16f);
            Fire(w);
            Assert.Equal(48f * (1f + bonus), Direct(w), 3);
        }

        [Theory]
        [InlineData(10f, 1f)]
        [InlineData(4f, 0.92f)]
        [Verifies("AUG-033")]
        public void Range_Extension_Leaves_Mid_Range_And_Cuts_Close_Range(float distance, float factor)
        {
            DuelWorld w = AKit.With(RangeExtension.Id, 2);
            AKit.FaceOff(w, distance);
            Fire(w);
            Assert.Equal(48f * factor, Direct(w), 3);
        }

        // ----- AUG-079 Exact Riposte -----------------------------------------------------------------------

        /// <summary>Player 1 strikes player 0 at 1.5 m; player 0 guards from 6 ticks before the strike (perfect) or 20 (ordinary).</summary>
        internal static int GuardAgainstStrike(DuelWorld w, bool perfect)
        {
            AKit.FaceOff(w, 1.5f);
            if (!perfect)
            {
                AKit.Step(w, 1, AKit.A.With(InputButtons.Guard));
                for (int i = 0; i < 9; i++) AKit.Step(w, 1, AKit.A.With(InputButtons.Guard));
            }

            AKit.Strike(w, perfect ? (PlayerInput?)null : AKit.A.With(InputButtons.Guard), null, 1);
            int start = w.Tick;
            if (perfect) AKit.Step(w, 4);
            for (int i = 0; i < (perfect ? 8 : 12); i++) AKit.Step(w, 1, AKit.A.With(InputButtons.Guard));
            AKit.Step(w, 1);
            GuardEvent g = AKit.Events<GuardEvent>(w).Single(e => e.Player == 0 && (e.Change == GuardChange.PerfectBlocked || e.Change == GuardChange.Blocked));
            Assert.Equal(perfect ? GuardChange.PerfectBlocked : GuardChange.Blocked, g.Change);
            return g.Tick;
        }

        /// <summary>Player 0 presses melee until a strike starts, then finishes it; returns its hit on player 1.</summary>
        private static DamageEvent Riposte(DuelWorld w, int waitTicks = 0)
        {
            AKit.Step(w, waitTicks);
            for (int i = 0; i < 20 && AKit.Strike(w) == null; i++)
            {
                AKit.Step(w, 1); // a fresh press each attempt
            }

            AKit.FinishStrike(w);
            return AKit.Hits(w, 1, DamageKind.Melee).Single();
        }

        [Theory]
        [InlineData(1, 3f)]
        [InlineData(2, 5f)]
        [Verifies("AUG-079")]
        public void Exact_Riposte_Adds_Damage_To_A_Weapon_Hit_Soon_After_A_Perfect_Guard(int rank, float extra)
        {
            AKit.AssertQuotes(ExactRiposte.Id, "Perfect guard then direct weapon hit within 0.6 s.", "Add 3 physical damage.", "Add 5 physical damage.", "Ordinary guard followed by strike gains nothing.");
            DuelWorld w = AKit.With(ExactRiposte.Id, rank);
            int perfect = GuardAgainstStrike(w, true);
            DamageEvent hit = Riposte(w);
            Assert.True(hit.Tick - perfect <= AKit.Ticks(ExactRiposte.WindowSec));
            Assert.Equal(GlobalRules.Melee.FocusDamage + extra, hit.Resolution.Raw, 3);
            Assert.Single(AKit.Activations(w, ExactRiposte.Id));
        }

        [Fact]
        [Verifies("AUG-079")]
        public void Exact_Riposte_Needs_A_Perfect_Guard_And_The_06_Second_Window()
        {
            DuelWorld ordinary = AKit.With(ExactRiposte.Id, 2);
            GuardAgainstStrike(ordinary, false);
            Assert.Equal(GlobalRules.Melee.FocusDamage, Riposte(ordinary).Resolution.Raw, 3);

            DuelWorld late = AKit.With(ExactRiposte.Id, 2);
            int perfect = GuardAgainstStrike(late, true);
            DamageEvent hit = Riposte(late, AKit.Ticks(0.5f));
            Assert.True(hit.Tick - perfect > AKit.Ticks(ExactRiposte.WindowSec));
            Assert.Equal(GlobalRules.Melee.FocusDamage, hit.Resolution.Raw, 3);
        }

        // ----- AUG-082 Blade and Bolt ----------------------------------------------------------------------

        /// <summary>WIZ-002 with SPL-073 (0.25 s windup) as a 30-damage test bolt and a press-to-commit grammar.</summary>
        private static DuelWorld BladeDuel(int rank) =>
            Tapped(BladeAndBolt.Id, rank, Normal, "SPL-073", new TestBolt(30f, 30f));

        private static void StrikeOnce(DuelWorld w)
        {
            Assert.NotNull(AKit.Strike(w));
            AKit.FinishStrike(w);
        }

        [Theory]
        [InlineData(1, 3f)]
        [InlineData(2, 5f)]
        [Verifies("AUG-082")]
        public void Blade_And_Bolt_Adds_Direct_Damage_To_A_Projectile_Soon_After_A_Weapon_Hit(int rank, float extra)
        {
            AKit.AssertQuotes(BladeAndBolt.Id, "Weapon hit followed by direct projectile within 1 s.", "Projectile gains 3 direct damage.", "Projectile gains 5 direct damage.", "Second weapon hit before projectile resets setup.");
            DuelWorld w = BladeDuel(rank);
            AKit.FaceOff(w, 1.5f);
            StrikeOnce(w);
            int hitTick = AKit.Hits(w, 1, DamageKind.Melee).Single().Tick;
            Tap(w, afterTicks: 30);
            int release = AKit.Casts(w, CastEventKind.Released).Single().Tick;
            Assert.True(release - hitTick <= AKit.Ticks(BladeAndBolt.WindowSec));
            Assert.Equal(30f + extra, Direct(w), 3);
        }

        [Theory]
        [InlineData(1, 3f)]
        [InlineData(2, 5f)]
        [Verifies("AUG-082")]
        public void Blade_And_Bolt_Boosts_A_Projectile_That_Strikes_On_Its_Release_Tick(int rank, float extra)
        {
            // A 90 m/s bolt at point-blank range: the delivery advances (and hits) before the tick's effect dispatch.
            DuelWorld w = Tapped(BladeAndBolt.Id, rank, Normal, "SPL-073", new TestBolt(30f, 90f));
            AKit.FaceOff(w, 1.5f);
            StrikeOnce(w);
            Tap(w, afterTicks: 30);
            int release = AKit.Casts(w, CastEventKind.Released).Single().Tick;
            DamageEvent hit = AKit.Hits(w, 1, DamageKind.Direct).Single();
            Assert.Equal(release, hit.Tick);
            Assert.Equal(30f + extra, hit.Resolution.Raw, 3);
            Assert.Single(AKit.Activations(w, BladeAndBolt.Id)); // the activation is still recorded once, under the budget
        }

        [Fact]
        [Verifies("AUG-082")]
        public void Blade_And_Bolt_Second_Weapon_Hit_Resets_And_The_Window_Closes()
        {
            DuelWorld twice = BladeDuel(2);
            AKit.FaceOff(twice, 1.5f);
            StrikeOnce(twice);
            AKit.Step(twice, 1);
            Assert.NotNull(AKit.Strike(twice)); // the 0.8 s cycle just ended: the second hit lands inside 1 s
            AKit.FinishStrike(twice);
            Assert.Equal(2, AKit.Hits(twice, 1, DamageKind.Melee).Count);
            Assert.True(AKit.Hits(twice, 1, DamageKind.Melee)[1].Tick - AKit.Hits(twice, 1, DamageKind.Melee)[0].Tick <= AKit.Ticks(1f));
            Tap(twice, afterTicks: 30);
            Assert.Equal(30f, Direct(twice), 3);

            DuelWorld late = BladeDuel(2);
            AKit.FaceOff(late, 1.5f);
            StrikeOnce(late);
            AKit.Step(late, 20);
            Tap(late, afterTicks: 30);
            Assert.Equal(30f, Direct(late), 3);
        }

        // ----- AUG-088 Cover Duelist -----------------------------------------------------------------------

        private static ArenaLayout WallBeside() =>
            SpellTestKit.FlatWith(TestWorlds.Box(80, new Vec3(1.5f, 0f, 0f), new Vec3(1f, 3f, 6f)));

        private static float StrikeCost(DuelWorld w)
        {
            float before = w.Player(0).Resources.Stamina;
            Assert.NotNull(AKit.Strike(w));
            return before - w.Player(0).Resources.Stamina;
        }

        [Theory]
        [InlineData(1, 0.05f)]
        [InlineData(2, 0.08f)]
        [Verifies("AUG-088")]
        public void Cover_Duelist_Boosts_Weapon_Hits_Near_Cover(int rank, float bonus)
        {
            AKit.AssertQuotes(CoverDuelist.Id, "Strike enemy while within 2 m of solid cover.", "Damage increases 5%.", "Damage increases 8%.", "Weapon strikes in open ground cost 1 extra stamina.");
            DuelWorld w = AKit.With(CoverDuelist.Id, rank, layout: WallBeside());
            AKit.FaceOff(w, 1.5f);
            AKit.Step(w, 1);
            Assert.Equal(GlobalRules.Melee.FocusStamina, StrikeCost(w), 3);
            AKit.FinishStrike(w);
            Assert.Equal(GlobalRules.Melee.FocusDamage * (1f + bonus), AKit.Hits(w, 1, DamageKind.Melee).Single().Resolution.Raw, 3);
        }

        [Fact]
        [Verifies("AUG-088")]
        public void Cover_Duelist_Charges_Open_Ground_Strikes_And_Adds_Nothing()
        {
            DuelWorld w = AKit.With(CoverDuelist.Id, 2);
            AKit.FaceOff(w, 1.5f);
            AKit.Step(w, 1);
            Assert.Equal(GlobalRules.Melee.FocusStamina + 1f, StrikeCost(w), 3);
            AKit.FinishStrike(w);
            Assert.Equal(GlobalRules.Melee.FocusDamage, AKit.Hits(w, 1, DamageKind.Melee).Single().Resolution.Raw, 3);
        }

        // ----- AUG-006 Anchored Cast -----------------------------------------------------------------------

        [Theory]
        [InlineData(1, 0.07f)]
        [InlineData(2, 0.10f)]
        [Verifies("AUG-006")]
        public void Anchored_Cast_Boosts_A_Mastered_Release_After_Standing_Still(int rank, float bonus)
        {
            AKit.AssertQuotes(AnchoredCast.Id, "stationary and grounded for 0.4 s", "Gain 7% direct damage.", "Gain 10% direct damage.", "Moving before release removes bonus.");
            DuelWorld w = Tapped(AnchoredCast.Id, rank, ManaReward);
            AKit.FaceOff(w, 6f);
            AKit.Step(w, 10);
            Tap(w);
            Assert.Equal(48f * (1f + bonus), Direct(w), 3);
        }

        [Fact]
        [Verifies("AUG-006")]
        public void Anchored_Cast_Needs_Mastery_Stillness_And_Clamps_With_The_Mastery_Damage_Reward()
        {
            DuelWorld normal = Tapped(AnchoredCast.Id, 2, Normal);
            AKit.FaceOff(normal, 6f);
            AKit.Step(normal, 10);
            Tap(normal);
            Assert.Equal(48f, Direct(normal), 3);

            // A step sideways just before the press: the 0.35 s windup alone is shorter than the 0.4 s of stillness.
            DuelWorld moved = Tapped(AnchoredCast.Id, 2, ManaReward);
            AKit.FaceOff(moved, 6f);
            AKit.Step(moved, 30);
            AKit.Step(moved, 8, AKit.Walk(0f, 1f, 0f));
            AKit.Step(moved, 2);
            Tap(moved, SpellTestKit.AimAt(moved, 0, 1));
            Assert.Equal(48f, Direct(moved), 3);

            // WIZ-002's crest reward (+12% direct) and 10% more stay inside the 15% mastery attribute cap (spec L480).
            DuelWorld crest = AKit.With(AnchoredCast.Id, 2);
            AKit.FaceOff(crest, 6f);
            AKit.Step(crest, 30);
            AKit.HoldAndRelease(crest, AKit.CrestHold);
            AKit.Step(crest, 60);
            Assert.True(AKit.Casts(crest, CastEventKind.Committed).Single().Cast.Mastery.IsMastered);
            Assert.Equal(48f * (1f + GlobalRules.Modifiers.MasteryAttributeCap), Direct(crest), 3);
        }

        // ----- AUG-007 Flying Script -----------------------------------------------------------------------

        [Theory]
        [InlineData(1, 0.07f)]
        [InlineData(2, 0.11f)]
        [Verifies("AUG-007")]
        public void Flying_Script_Speeds_A_Projectile_Mastered_In_The_Air(int rank, float bonus)
        {
            AKit.AssertQuotes(FlyingScript.Id, "Complete advanced variation while airborne.", "Gain 7% projectile speed.", "Gain 11% projectile speed.", "Landing before release removes bonus; incoming airborne hits remain normal.");
            DuelWorld w = Tapped(FlyingScript.Id, rank, ManaReward);
            AKit.FaceOff(w, 10f);
            AKit.Step(w, 2);
            AKit.Step(w, 1, AKit.A.With(InputButtons.Jump));
            AKit.Step(w, 2);
            Assert.False(w.Player(0).Motor.Grounded);
            Tap(w);
            Assert.Equal(24f * (1f + bonus), LaunchSpeed(w), 3);
        }

        [Fact]
        [Verifies("AUG-007")]
        public void Flying_Script_Is_Lost_On_Landing_Needs_Mastery_And_Clamps_With_A_Speed_Reward()
        {
            // Committed 0.6 s into the 0.7 s jump: the owner lands before the 0.35 s windup releases.
            DuelWorld landed = Tapped(FlyingScript.Id, 2, ManaReward);
            AKit.FaceOff(landed, 10f);
            AKit.Step(landed, 2);
            AKit.Step(landed, 1, AKit.A.With(InputButtons.Jump));
            AKit.Step(landed, AKit.Ticks(0.6f));
            Assert.False(landed.Player(0).Motor.Grounded);
            Tap(landed);
            Assert.Equal(24f, LaunchSpeed(landed), 3);

            DuelWorld grounded = Tapped(FlyingScript.Id, 2, ManaReward);
            AKit.FaceOff(grounded, 10f);
            AKit.Step(grounded, 2);
            Tap(grounded);
            Assert.Equal(24f, LaunchSpeed(grounded), 3);

            DuelWorld normal = Tapped(FlyingScript.Id, 2, Normal);
            AKit.FaceOff(normal, 10f);
            AKit.Step(normal, 1, AKit.A.With(InputButtons.Jump));
            AKit.Step(normal, 2);
            Tap(normal);
            Assert.Equal(24f, LaunchSpeed(normal), 3);

            // A projectile-speed mastery reward of 10% and 11% more stay inside the 15% attribute cap (spec L480).
            DuelWorld speed = Tapped(FlyingScript.Id, 2, MasteryResult.Mastered(MasteryReward.ProjectileSpeed, 0.10f));
            AKit.FaceOff(speed, 10f);
            AKit.Step(speed, 1, AKit.A.With(InputButtons.Jump));
            AKit.Step(speed, 2);
            Tap(speed);
            Assert.Equal(24f * (1f + GlobalRules.Modifiers.MasteryAttributeCap), LaunchSpeed(speed), 3);
        }

        [Fact]
        [Verifies("AUG-007")]
        public void Flying_Script_Leaves_Incoming_Airborne_Hits_Normal()
        {
            DuelWorld w = Tapped(FlyingScript.Id, 2, ManaReward);
            AKit.FaceOff(w, 6f);
            AKit.Step(w, 1, AKit.A.With(InputButtons.Jump));
            AKit.Step(w, 2);
            w.ApplyDamage(TestWorlds.Hit(w, 1, 0, 30f, sourceId: "SPL-TEST"));
            Assert.Equal(30f, AKit.Hits(w, 0).Single().Resolution.Raw, 3);
        }

        // ----- AUG-103 Moving Release ----------------------------------------------------------------------

        [Theory]
        [InlineData(1, 0.04f)]
        [InlineData(2, 0.07f)]
        [Verifies("AUG-103")]
        public void Moving_Release_Speeds_A_Projectile_Released_While_Strafing(int rank, float bonus)
        {
            AKit.AssertQuotes(MovingRelease.Id, "Release a paid spell while walking sideways.", "Projectile speed increases 4%.", "Projectile speed increases 7%.", "Casting while sprinting costs 1 extra mana.");
            DuelWorld w = AKit.With(MovingRelease.Id, rank);
            AKit.FaceOff(w, 12f);
            Fire(w, AKit.Walk(0f, 1f, 0f));
            Assert.Equal(24f * (1f + bonus), LaunchSpeed(w), 3);
            Assert.Equal(18f, AKit.Casts(w, CastEventKind.Committed).Single().Cast.Mana, 3);
        }

        [Fact]
        [Verifies("AUG-103")]
        public void Moving_Release_Ignores_Forward_Walking_And_Charges_Sprinting_Casts()
        {
            DuelWorld forward = AKit.With(MovingRelease.Id, 2);
            AKit.FaceOff(forward, 12f);
            Fire(forward, AKit.Walk(0f));
            Assert.Equal(24f, LaunchSpeed(forward), 3);

            DuelWorld still = AKit.With(MovingRelease.Id, 2);
            AKit.FaceOff(still, 12f);
            Fire(still);
            Assert.Equal(24f, LaunchSpeed(still), 3);

            // Preparation begun while sprinting forward costs one more mana.
            DuelWorld sprint = AKit.With(MovingRelease.Id, 2);
            AKit.FaceOff(sprint, 14f);
            PlayerInput run = AKit.Walk(0f).With(InputButtons.Sprint);
            AKit.Step(sprint, 10, run);
            Assert.True(sprint.Player(0).Motor.Sprinting);
            AKit.HoldAndRelease(sprint, AKit.NormalHold, run);
            Assert.Equal(19f, AKit.Casts(sprint, CastEventKind.Committed).Single().Cast.Mana, 3);
        }
    }
}
