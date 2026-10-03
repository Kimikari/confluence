using System;
using System.Linq;
using Confluence.Casting;
using Confluence.Core.Arena;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Reactions;
using Confluence.Sim.Combat;
using Confluence.Sim.Content.Wizards.Wiz002;
using Confluence.Sim.Duel;
using Confluence.Sim.Fields;
using Confluence.Sim.Input;
using Confluence.Sim.Players;
using Confluence.Sim.Spells;
using Confluence.Sim.Tests.Spells;
using InputButtons = Confluence.Casting.InputButtons;

namespace Confluence.Sim.Tests.Content.Wiz002
{
    /// <summary>
    /// SPL-010 Cinder Sweep re-checked against the merged reaction world, and its transformations AUG-307 Sweep Wake
    /// and AUG-308 Forked Sweep (exclusive group SPL-010-delivery), cast through WIZ-002's grammar.
    /// </summary>
    public class CinderSweepTransformTests
    {
        private const string Sweep = "SPL-010";
        private const int Slot = 1;

        private static readonly PlayerInput Aim = TestWorlds.A.Selecting(Slot);

        private static DuelWorld Duel(Vec3 target, ArenaLayout layout = null, params ActiveAugment[] augments)
        {
            DuelWorld w = BramKit.Duel(BramKit.Bram(null, augments), layout);
            TestWorlds.Place(w, 1, target, 180f);
            w.Step(Aim, TestWorlds.B); // select the sweep's slot first (no swap windup)
            return w;
        }

        private static void SweepNow(DuelWorld w, int hold = BramKit.NormalHold, PlayerInput? aim = null, PlayerInput? other = null)
        {
            BramKit.Cast(w, Slot, hold, aim ?? Aim, other);
            BramKit.RunUntil(w, () => BramKit.Spell(w, SpellEventKind.ConeReleased).Any(), 40, aim ?? Aim, other);
            BramKit.Run(w, 1, aim ?? Aim, other);
        }

        private static float Swept(Vec3 target, params ActiveAugment[] augments) => Swept(target, null, augments);

        private static float Swept(Vec3 target, PlayerInput? aim, params ActiveAugment[] augments)
        {
            DuelWorld w = Duel(target, null, augments);
            SweepNow(w, BramKit.NormalHold, aim);
            return BramKit.DamageTo(w, 1, Sweep).Where(d => d.Kind == DamageKind.Direct).Sum(d => d.Dealt);
        }

        private static Exception Unwrap(Exception e)
        {
            while (e != null && !(e is InvalidOperationException) && e.InnerException != null) e = e.InnerException;
            return e;
        }

        /// <summary>A target on the floor at a bearing (degrees right of +Z) and distance from Bram's feet at (0,0,-6).</summary>
        private static Vec3 At(float bearingDeg, float distance) =>
            new Vec3(0f, 0f, -6f) + CMath.ForwardFromYaw(bearingDeg) * distance;

        // ----- SPL-010 on the merged runtime ------------------------------------------------------------------

        [Fact]
        [Verifies("SPL-010")]
        public void Its_Heat_1_Dries_Thin_Wetness_Along_The_Cone_In_The_Reaction_World()
        {
            ArenaLayout wet = BramKit.FlatWith(BramKit.Water(new Vec2(0f, -3.5f), 0.75f, 0.75f, 1));
            DuelWorld w = Duel(new Vec3(4f, 0f, 6f), wet);
            Assert.True(BramKit.Reactions(w).UnitsAt(new Vec3(0f, 0f, -3.5f)).Water > 0);
            SweepNow(w);
            BramKit.Run(w, 2);
            Assert.Equal(0, BramKit.Reactions(w).UnitsAt(new Vec3(0f, 0f, -3.5f)).Water); // "dries thin wetness"
            Assert.False(BramKit.Reactions(w).IsWetAt(new Vec3(0f, 0f, -3.5f)));
        }

        // ----- AUG-307 Sweep Wake -----------------------------------------------------------------------------

        [Theory]
        [InlineData(1, 2f, 4f)]
        [InlineData(2, 3f, 6f)]
        [Verifies("AUG-307")]
        public void Sweep_Wake_Leaves_A_1_Meter_Neutral_Heat_1_Strip_That_Burns_Whoever_Stands_On_It(int rank, float seconds, float dps)
        {
            DuelWorld w = Duel(new Vec3(0f, 0f, -3.1f), null, BramKit.Aug("AUG-307", rank));
            SweepNow(w);
            Assert.Equal(30f, BramKit.DamageTo(w, 1, Sweep).First(d => d.Kind == DamageKind.Direct).Dealt, 3); // "Direct 36 becomes 30"
            HeatStripEntity strip = BramKit.Entity<HeatStripEntity>(w);
            Assert.NotNull(strip);
            Assert.Equal(1, w.Owned.Count(0, OwnedEntityKind.Field)); // "field allocation required"
            Assert.True(strip.Owned.Neutral);
            Assert.Equal(Ticks.FromSeconds(seconds), BramKit.Spell(w, SpellEventKind.FieldCreated).Single().Ticks);
            Assert.True(strip.Contains(new Vec3(0f, 0f, -3.1f)));
            Assert.False(strip.Contains(new Vec3(0f, 0f, -4.0f)));  // 1 m wide (the chord at -3.17)
            Assert.False(strip.Contains(new Vec3(0f, 0f, -6f)));    // never under the caster
            Assert.True(BramKit.Reactions(w).UnitsAt(new Vec3(2f, 0f, -3.17f)).Heat >= 1); // Heat 1 over the strip

            BramKit.RunUntil(w, () => !strip.Alive, Ticks.FromSeconds(seconds) + 10);
            float contact = BramKit.DamageTo(w, 1, Sweep).Where(d => d.Kind != DamageKind.Direct).Sum(d => d.Dealt);
            Assert.Equal(dps * seconds, contact, 3); // "4 damage/s" for 2 s; rank 2 "3 s and 6/s"
            Assert.All(BramKit.DamageTo(w, 1, Sweep).Where(d => d.Kind != DamageKind.Direct), d => Assert.Equal(GuardOutcome.None, d.Resolution.Guard));
            Assert.Equal(0, w.Owned.Count(0, OwnedEntityKind.Field));
        }

        [Fact]
        [Verifies("AUG-307")]
        public void Sweep_Wake_Is_Neutral_So_Its_Owner_Burns_Too_And_It_Is_A_Field_Cast()
        {
            DuelWorld w = Duel(new Vec3(5f, 0f, 6f), null, BramKit.Aug("AUG-307"));
            SpellForm form = BramKit.Form(w, Sweep);
            Assert.True(form.HasTag("field")); // structural delta: the spell gains the field tag
            Assert.Equal(CastWeight.MajorFieldOrSummon, form.Castable.Weight);
            SweepNow(w);
            HeatStripEntity strip = BramKit.Entity<HeatStripEntity>(w);
            TestWorlds.Place(w, 0, new Vec3(1f, 0f, -3.17f), 0f);
            BramKit.Run(w, 70);
            Assert.True(BramKit.DamageTo(w, 0, Sweep).Sum(d => d.Dealt) >= 4f); // spec L496 neutral: the owner included
        }

        [Fact]
        [Verifies("AUG-307")]
        public void Sweep_Wake_Counterplay_Step_Out_Or_Cool_The_Strip()
        {
            DuelWorld w = Duel(new Vec3(0f, 0f, -3.1f), null, BramKit.Aug("AUG-307"));
            SweepNow(w);
            TestWorlds.Place(w, 1, new Vec3(0f, 0f, -1.5f), 180f); // step out
            BramKit.Run(w, 150);
            Assert.Equal(4f, BramKit.DamageTo(w, 1, Sweep).Where(d => d.Kind != DamageKind.Direct).Sum(d => d.Dealt), 3); // the entry tick only

            // Water consumes the strip's heat and the strip ends early.
            ArenaLayout wet = BramKit.FlatWith(BramKit.Water(new Vec2(0f, -3.17f), 4f, 1f, 3));
            DuelWorld v = Duel(new Vec3(6f, 0f, 6f), wet, BramKit.Aug("AUG-307"));
            SweepNow(v);
            HeatStripEntity strip = BramKit.Entity<HeatStripEntity>(v);
            BramKit.RunUntil(v, () => strip == null || !strip.Alive, 60);
            Assert.True(strip == null || !strip.Alive);
            Assert.True(v.Tick < 200);
            Assert.DoesNotContain(v.Owned.Entities, o => o.DefinitionId == Sweep); // the strip's field is gone (its steam remains)
            Assert.Single(BramKit.Spell(v, SpellEventKind.Cooled));
            TestWorlds.Place(v, 1, new Vec3(0f, 0f, -3.17f), 180f);
            BramKit.Run(v, 90);
            Assert.Empty(BramKit.DamageTo(v, 1, Sweep));
        }

        [Fact]
        [Verifies("AUG-307")]
        public void Sweep_Wake_Strips_Respect_The_Three_Field_Allocation_With_Oldest_Retiring()
        {
            DuelWorld w = Duel(new Vec3(6f, 0f, 6f), null, BramKit.Aug("AUG-307", 2));
            // Three other fields of Bram's first; the strip then retires the oldest (spec L128, L510).
            var first = new int[GlobalRules.FieldsAndSummons.MaxFieldsPerPlayer];
            for (int i = 0; i < first.Length; i++)
            {
                first[i] = w.CreateOwnedEntity(new OwnedEntitySpec
                {
                    Kind = OwnedEntityKind.Field, Owner = 0, DefinitionId = "SPL-T97", LifetimeTicks = 400, Position = new Vec3(-6f + 3f * i, 0f, 4f), Radius = 1f,
                }).Id;
                w.Step(Aim, TestWorlds.B);
            }

            Assert.Equal(GlobalRules.FieldsAndSummons.MaxFieldsPerPlayer, w.Owned.Count(0, OwnedEntityKind.Field));
            SweepNow(w);
            Assert.NotNull(BramKit.Entity<HeatStripEntity>(w));
            Assert.Equal(GlobalRules.FieldsAndSummons.MaxFieldsPerPlayer, w.Owned.Count(0, OwnedEntityKind.Field)); // never a hidden extra
            Assert.Null(w.Owned.Find(first[0])); // the oldest retired
            Assert.NotNull(w.Owned.Find(first[1]));
        }

        // ----- AUG-308 Forked Sweep ---------------------------------------------------------------------------

        [Fact]
        [Verifies("AUG-308")]
        public void Forked_Sweep_Strikes_Two_45_Degree_Lobes_Around_A_30_Degree_Gap()
        {
            ActiveAugment fork = BramKit.Aug("AUG-308");
            Assert.Equal(0f, Swept(At(0f, 3f), fork));          // inside the 30-degree center gap
            Assert.Equal(0f, Swept(At(5f, 3f), fork));           // the body (7.7 degrees wide each side) stays inside the gap
            Assert.Equal(36f, Swept(At(37.5f, 3f), fork), 3);   // a lobe's center: "damage 36"
            Assert.Equal(36f, Swept(At(-37.5f, 3f), fork), 3);
            Assert.Equal(36f, Swept(At(55f, 3f), fork), 3);     // the lobe's outer edge (60 degrees) still holds the body
            Assert.Equal(0f, Swept(At(75f, 3f), fork));         // flank: outside both lobes
            Assert.Equal(0f, Swept(At(-37.5f, 3.95f), fork));   // "Reach 4 becomes 3.5 m"
            Assert.Equal(36f, Swept(At(37.5f, 1f), fork), 3);   // a close body overlapping both lobes is struck once
        }

        [Fact]
        [Verifies("AUG-308")]
        public void Forked_Sweep_Adds_0_1_Seconds_Of_Windup_And_Announces_Its_Gap_At_Commitment()
        {
            DuelWorld w = Duel(At(37.5f, 3f), null, BramKit.Aug("AUG-308"));
            SpellForm form = BramKit.Form(w, Sweep);
            Assert.Equal(3.5f, form.Range, 3);
            Assert.Equal(Ticks.FromSeconds(0.35), form.Castable.WindupTicks); // "cast_time+0.1 s"
            Assert.Equal(15f, form.Mana, 3);                                  // inherited mana
            Assert.False(form.HasTag("field"));                               // no structural change

            SweepNow(w);
            int commit = BramKit.Casts(w, CastEventKind.Committed).Single().Tick;
            SpellEvent gap = BramKit.Spell(w, SpellEventKind.Marker).Single();
            Assert.Equal(commit, gap.Tick);
            Assert.Equal(Ticks.FromSeconds(0.35), gap.Ticks);
            Assert.Equal(0f, CMath.YawOf(gap.Direction), 2);
            Assert.Equal(commit + Ticks.FromSeconds(0.35), BramKit.Spell(w, SpellEventKind.ConeReleased).Single().Tick);
        }

        [Fact]
        [Verifies("AUG-308")]
        public void Forked_Sweep_Fixes_Its_Fork_At_Commitment_So_The_Announced_Gap_Is_Safe()
        {
            // Bram turns 37.5 degrees during the windup; the fork stays where it was announced.
            DuelWorld w = Duel(At(0f, 3f), null, BramKit.Aug("AUG-308"));
            BramKit.Cast(w, Slot, BramKit.NormalHold, Aim);
            PlayerInput turned = TestWorlds.Face(-37.5f).Selecting(Slot);
            BramKit.RunUntil(w, () => BramKit.Spell(w, SpellEventKind.ConeReleased).Any(), 40, turned);
            Assert.Empty(BramKit.DamageTo(w, 1, Sweep));
        }

        [Fact]
        [Verifies("AUG-308")]
        public void Forked_Sweep_Rank_2_Chooses_The_Gap_Direction_Before_Release()
        {
            ActiveAugment fork2 = BramKit.Aug("AUG-308", 2);
            Assert.Equal(0f, Swept(At(0f, 3f), fork2));                                                          // default: centered gap
            Assert.Equal(36f, Swept(At(0f, 3f), Aim.With(InputButtons.ModifierA), fork2), 3);                     // gap turned left: a lobe on the aim
            Assert.Equal(0f, Swept(At(-37.5f, 3f), Aim.With(InputButtons.ModifierA), fork2));                     // ...the gap is now there
            Assert.Equal(36f, Swept(At(0f, 3f), Aim.With(InputButtons.ModifierB), fork2), 3);                     // gap turned right
            Assert.Equal(0f, Swept(At(37.5f, 3f), Aim.With(InputButtons.ModifierB), fork2));
            Assert.Equal(0f, Swept(At(0f, 3f), Aim.With(InputButtons.ModifierA), BramKit.Aug("AUG-308", 1)));     // rank 1 has no choice

            DuelWorld w = Duel(At(0f, 3f), null, fork2);
            SweepNow(w, BramKit.NormalHold, Aim.With(InputButtons.ModifierB));
            Assert.Equal(37.5f, CMath.YawOf(BramKit.Spell(w, SpellEventKind.Marker).Single().Direction), 2); // the announced gap
        }

        [Fact]
        [Verifies("AUG-308")]
        public void Forked_Sweep_Keeps_Cover_Guard_Heat_And_The_Crest_Bonus()
        {
            ActiveAugment fork = BramKit.Aug("AUG-308");
            DuelWorld w = Duel(At(37.5f, 3f), null, fork);
            Vec3 target = w.Player(1).Position;
            float faceBram = CMath.YawOf(new Vec3(0f, 0f, -6f) - target);
            TestWorlds.Place(w, 1, target, faceBram);
            PlayerInput guard = TestWorlds.Face(faceBram).With(InputButtons.Guard);
            SweepNow(w, BramKit.NormalHold, null, guard);
            DamageEvent hit = BramKit.DamageTo(w, 1, Sweep).Single();
            Assert.Equal(GuardOutcome.Guarded, hit.Resolution.Guard);
            Assert.Equal(18f, hit.Dealt, 3);

            // Heat 1 over both lobes dries thin wetness there, and nothing in the gap.
            Vec3 l = At(-37.5f, 2f), r = At(37.5f, 2f), g = At(0f, 2.5f);
            DuelWorld wet = Duel(At(90f, 3f), BramKit.FlatWith(BramKit.Water(l.XZ(), 0.25f, 0.25f, 1), BramKit.Water(r.XZ(), 0.25f, 0.25f, 1),
                BramKit.Water(g.XZ(), 0.25f, 0.25f, 1)), fork);
            SweepNow(wet);
            BramKit.Run(wet, 2);
            Assert.False(BramKit.Reactions(wet).IsWetAt(l));
            Assert.False(BramKit.Reactions(wet).IsWetAt(r));
            Assert.True(BramKit.Reactions(wet).IsWetAt(g));

            DuelWorld crest = Duel(At(-37.5f, 3f), null, fork);
            SweepNow(crest, BramKit.CrestHold);
            Assert.Equal(36f * 1.12f, BramKit.DamageTo(crest, 1, Sweep).Single().Dealt, 3);

            Vec3 behind = At(37.5f, 3f);
            ArenaLayout wall = SpellTestKit.FlatWith(SpellTestKit.Wall(70, new Vec3(0f, 0f, -6f) + CMath.ForwardFromYaw(37.5f) * 1.5f,
                new Vec3(1.6f, 2.5f, 0.2f), 37.5f));
            DuelWorld covered = Duel(behind, wall, fork);
            SweepNow(covered);
            Assert.Empty(BramKit.DamageTo(covered, 1, Sweep)); // per-target cover
        }

        [Fact]
        [Verifies("AUG-307")]
        [Verifies("AUG-308")]
        public void Sweep_Wake_And_Forked_Sweep_Share_The_Exclusive_Delivery_Group()
        {
            Exception e = Unwrap(Record.Exception(() => Duel(Vec3.Zero, null, BramKit.Aug("AUG-307"), BramKit.Aug("AUG-308"))));
            Assert.IsType<InvalidOperationException>(e);
            Assert.Contains("SPL-010-delivery", e.Message);
        }
    }
}
