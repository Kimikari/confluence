using System.Linq;
using Confluence.Casting;
using Confluence.Core.Arena;
using Confluence.Core.Geometry;
using Confluence.Core.Content;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Reactions;
using Confluence.Reactions.Volumes;
using Confluence.Sim.Combat;
using Confluence.Sim.Content.Wizards.Wiz002;
using Confluence.Sim.Duel;
using Confluence.Sim.Input;
using Confluence.Sim.Players;
using Confluence.Sim.Spells;
using Confluence.Sim.Status;
using Confluence.Sim.Tests.Spells;

namespace Confluence.Sim.Tests.Content.Wiz002
{
    /// <summary>SPL-011 Heat Haze and its transformations AUG-309 Steam Mantle and AUG-310 Haze Reservoir, cast through WIZ-002's grammar.</summary>
    public class HeatHazeTests
    {
        private const string Haze = "SPL-011";

        private static DuelWorld Duel(params ActiveAugment[] augments) =>
            BramKit.Duel(BramKit.Bram(null, augments), opponent: BramKit.Caster(BramKit.Bolt("SPL-T90", 50f), BramKit.Swipe("SPL-T92", 40f)));

        private static void CastHaze(DuelWorld w, int hold = BramKit.NormalHold)
        {
            BramKit.Cast(w, Haze, hold);
            BramKit.RunUntil(w, () => BramKit.Entity<BuffEntity>(w) != null, 40);
            Assert.NotNull(BramKit.Entity<BuffEntity>(w));
        }

        private static float BoltDamage(DuelWorld w)
        {
            int before = BramKit.DamageTo(w, 0, "SPL-T90").Count;
            BramKit.OpponentFires(w, 0, BramKit.Chest(w, 0));
            BramKit.RunUntil(w, () => BramKit.DamageTo(w, 0, "SPL-T90").Count > before, 40);
            return BramKit.DamageTo(w, 0, "SPL-T90").Last().Dealt;
        }

        [Fact]
        [Verifies("SPL-011")]
        public void The_Visible_Shimmer_Lasts_4_Seconds_Costs_18_Mana_And_Cools_Down_12_Seconds()
        {
            DuelWorld w = Duel();
            CastHaze(w);
            Assert.Equal(100f - 18f, BramKit.Mana(w), 3);
            Assert.Equal(Ticks.FromSeconds(12.0), w.Player(0).Caster.CooldownRemainingTicks(Haze) + (w.Tick - BramKit.Casts(w, CastEventKind.Committed).Single().Tick));
            SpellEvent started = BramKit.Spell(w, SpellEventKind.BuffStarted).Single();
            Assert.Equal(Ticks.FromSeconds(4.0), started.Ticks); // "A visible personal shimmer lasts 4 s"
            Assert.Equal(BramKit.Casts(w, CastEventKind.Committed).Single().Tick + Ticks.FromSeconds(0.3), started.Tick); // 0.3 s windup
            BramKit.RunUntil(w, () => BramKit.Spell(w, SpellEventKind.BuffEnded).Any(), 300);
            Assert.Equal(started.Tick + Ticks.FromSeconds(4.0), BramKit.Spell(w, SpellEventKind.BuffEnded).Single().Tick);
        }

        [Fact]
        [Verifies("SPL-011")]
        public void It_Reduces_Incoming_Projectile_Damage_12_Percent_But_Not_Melee_And_Not_After_It_Ends()
        {
            DuelWorld w = Duel();
            Assert.Equal(50f, BoltDamage(w), 3); // without the haze
            CastHaze(w);
            Assert.Equal(50f * 0.88f, BoltDamage(w), 3);

            // "Use melee": a cone strike is not a projectile and is not reduced.
            TestWorlds.Place(w, 1, new Vec3(0f, 0f, -3f), 180f);
            BramKit.OpponentFires(w, 1, BramKit.Chest(w, 0));
            BramKit.Run(w, 2);
            Assert.Equal(40f, BramKit.DamageTo(w, 0, "SPL-T92").Single().Dealt, 3);

            // "wait out duration": the reduction ends with the shimmer; hit volumes and aim never changed.
            TestWorlds.Place(w, 1, new Vec3(0f, 0f, 6f), 180f);
            BramKit.RunUntil(w, () => BramKit.Spell(w, SpellEventKind.BuffEnded).Any(), 300);
            Assert.Equal(50f, BoltDamage(w), 3);
            Assert.Equal(GlobalRules.Movement.CapsuleRadius, w.Player(0).HitVolume.Radius, 3);
        }

        [Fact]
        [Verifies("SPL-011")]
        public void Its_Reduction_Joins_The_45_Percent_Passive_Cap()
        {
            DuelWorld w = Duel();
            w.Player(0).Stats.Add(StatKind.Resistance, 0.35f, "TEST", DamageChannel.Physical);
            w.Player(0).Stats.Add(StatKind.PassiveReduction, 0.10f, "TEST", DamageChannel.Physical);
            CastHaze(w);
            // 1 - 0.65 * 0.90 * 0.88 = 48.5% is clamped to the 45% ceiling of all passive reductions (spec L112).
            Assert.Equal(50f * (1f - GlobalRules.Damage.PassiveReductionCap), BoltDamage(w), 3);
        }

        [Fact]
        [Verifies("SPL-011")]
        public void Its_Heat_1_Dries_The_Casters_Wetness_Once_At_Release()
        {
            DuelWorld w = Duel();
            w.ApplyStatus(0, StatusApplication.Of(StatusKind.Wet, "TEST-WET", -1, Ticks.FromSeconds(6.0)));
            Assert.True(w.Player(0).Statuses.IsWet);
            CastHaze(w);
            Assert.False(w.Player(0).Statuses.IsWet);
            Assert.Contains(SpellTestKit.Events<StatusEvent>(w), s => s.Kind == StatusKind.Wet && s.Change == StatusChange.Removed && s.Player == 0);

            // Once: a later wetting is not dried while the shimmer lasts.
            w.ApplyStatus(0, StatusApplication.Of(StatusKind.Wet, "TEST-WET", -1, Ticks.FromSeconds(6.0)));
            BramKit.Run(w, 30);
            Assert.True(w.Player(0).Statuses.IsWet);
        }

        [Fact]
        [Verifies("SPL-011")]
        public void A_Crest_Release_Pays_12_Percent_Less_Mana_Because_It_Deals_No_Damage()
        {
            DuelWorld w = Duel();
            CastHaze(w, BramKit.CrestHold);
            CastEvent commit = BramKit.Casts(w, CastEventKind.Committed).Single().Cast;
            Assert.Equal(MasteryReward.ManaEfficiency, commit.Mastery.Reward);
            Assert.Equal(18f * 0.88f, commit.Mana, 3);
            Assert.Equal(100f - 18f * 0.88f, BramKit.Mana(w), 3);
        }

        // ----- AUG-309 Steam Mantle ----------------------------------------------------------------------------

        private static ObscurationVolume MantlePuff(DuelWorld w) => BramKit.Reactions(w).Volumes.SingleOrDefault(v => v.Kind == VolumeKind.Steam);

        [Theory]
        [InlineData(1, 1.5f, 1f)]
        [InlineData(2, 2f, 1.5f)]
        [Verifies("AUG-309")]
        public void Steam_Mantle_Consumes_Wet_Into_A_Steam_Puff_Around_The_Caster(int rank, float radius, float seconds)
        {
            DuelWorld w = Duel(BramKit.Aug("AUG-309", rank));
            w.ApplyStatus(0, StatusApplication.Of(StatusKind.Wet, "TEST-WET", -1, Ticks.FromSeconds(6.0)));
            CastHaze(w);
            BramKit.Run(w, 1);
            Assert.False(w.Player(0).Statuses.IsWet);
            ObscurationVolume puff = MantlePuff(w);
            Assert.NotNull(puff);
            Assert.Equal(0, puff.Owner);
            Assert.True(puff.BlocksSight);
            Assert.Equal(Ticks.FromSeconds(seconds), puff.ExpiresTick - puff.CreatedTick);
            puff.GetBounds(out Vec2 min, out Vec2 max);
            Assert.InRange(max.X - min.X, 2f * radius - 0.5f, 2f * radius + 0.5f); // "1.5 m steam puff" / "radius 2 m"
            Assert.Contains(w.Owned.Entities, e => e.Owner == 0 && e.DefinitionId == "CLOUD:Steam"); // one field of the caster (L670)

            // A line through the whole puff crosses at least two meters of steam: hidden both ways (spec L167).
            Vec3 c = w.Player(0).Position;
            Assert.True(w.Materials.ObscuringPathLength(c + new Vec3(-radius - 1f, 1.2f, 0f), c + new Vec3(radius + 1f, 1.2f, 0f)) >= 2f);
            BramKit.Run(w, Ticks.FromSeconds(seconds) + 2);
            Assert.Null(MantlePuff(w));
        }

        [Fact]
        [Verifies("AUG-309")]
        public void Steam_Mantle_Makes_No_Puff_When_Dry_And_Its_Shimmer_Reduces_Only_8_Percent()
        {
            DuelWorld w = Duel(BramKit.Aug("AUG-309"));
            CastHaze(w);
            BramKit.Run(w, 1);
            Assert.Null(MantlePuff(w));
            Assert.Equal(50f * 0.92f, BoltDamage(w), 3); // "Projectile reduction 12% becomes 8%"
        }

        [Fact]
        [Verifies("AUG-309")]
        public void Steam_Mantle_Counterplay_Close_Range_Still_Acquires_The_Caster_Inside_The_Puff()
        {
            DuelWorld w = Duel(BramKit.Aug("AUG-309"));
            TestWorlds.Place(w, 1, new Vec3(0f, 0f, -5f), 180f); // 1 m away, inside the 1.5 m puff
            w.ApplyStatus(0, StatusApplication.Of(StatusKind.Wet, "TEST-WET", -1, Ticks.FromSeconds(6.0)));
            CastHaze(w);
            BramKit.Run(w, 1);
            Assert.NotNull(MantlePuff(w));
            Assert.True(w.CanAcquire(1, 0)); // "Use sound or close range": less than two meters of steam between them
            Assert.True(w.CanAcquire(0, 1)); // an opponent inside the same puff is not hidden from the caster either
        }

        [Theory]
        [InlineData(1, 1f)]
        [InlineData(2, 1.5f)]
        [Verifies("AUG-309")]
        public void Steam_Mantle_Drawback_Hides_The_Casters_View_From_Inside_The_Puff(int rank, float seconds)
        {
            // Bram at (0,0,-6), the opponent 12 m away at (0,0,6), nothing but the puff between them.
            DuelWorld w = Duel(BramKit.Aug("AUG-309", rank));
            w.ApplyStatus(0, StatusApplication.Of(StatusKind.Wet, "TEST-WET", -1, Ticks.FromSeconds(6.0)));
            Assert.True(w.CanAcquire(0, 1));
            CastHaze(w);
            BramKit.Run(w, 1);
            ObscurationVolume puff = MantlePuff(w);
            Assert.NotNull(puff);
            Assert.True(puff.ContainsPoint(w.Player(0).Eye));

            // "steam also hides caster's view": from inside the puff Bram cannot acquire the opponent outside it, even at
            // rank 1, where the line from the puff's centre crosses only 1.5 m of steam (below the 2 m of spec L167).
            Vec3 chest = w.Player(1).Position + new Vec3(0f, w.Player(1).Motor.Height * 0.6f, 0f);
            float path = w.Materials.ObscuringPathLength(w.Player(0).Eye, chest);
            if (rank == 1) Assert.True(path < 2f);
            Assert.False(w.CanAcquire(0, 1));

            // The opponent's view of Bram follows the shared rule only: hidden by the 2 m rank 2 puff, not by 1.5 m.
            Assert.Equal(rank >= 2, !w.CanAcquire(1, 0));

            // Stepping out of the puff (or the puff fading) gives the view back.
            BramKit.Run(w, Ticks.FromSeconds(seconds) + 2);
            Assert.Null(MantlePuff(w));
            Assert.True(w.CanAcquire(0, 1));
            Assert.Null(BramKit.Entity<MantlePuffEntity>(w));
        }

        [Fact]
        [Verifies("AUG-309")]
        public void Steam_Mantle_Rank_1_Puff_Screens_The_Caster_Who_Steps_Behind_It_Until_The_Opponent_Closes_In()
        {
            DuelWorld w = Duel(BramKit.Aug("AUG-309"));
            w.ApplyStatus(0, StatusApplication.Of(StatusKind.Wet, "TEST-WET", -1, Ticks.FromSeconds(6.0)));
            CastHaze(w);
            BramKit.Run(w, 1);
            Assert.NotNull(MantlePuff(w));

            // Bram steps 2 m back behind the puff: the opponent's line crosses its whole 3 m diameter (spec L167).
            TestWorlds.Place(w, 0, new Vec3(0f, 0f, -8f), 0f);
            Assert.False(w.CanAcquire(1, 0));

            // "close range": the opponent walks around the puff to Bram's side and acquires him again.
            TestWorlds.Place(w, 1, new Vec3(0f, 0f, -10f), 0f);
            Assert.True(w.CanAcquire(1, 0));
            Assert.True(w.CanAcquire(0, 1)); // Bram is outside his puff: his own view is not hidden
        }

        // ----- AUG-310 Haze Reservoir --------------------------------------------------------------------------

        // Player 1: a 15-damage test projectile (slot 0), a 60-damage test cone (slot 1) and the catalogue's own Fireball
        // (SPL-009, slot 2, 48 direct, cast through a tap grammar).
        private static DuelWorld ReservoirDuel(int rank, float waterAhead, ArenaLayout layout = null)
        {
            layout = layout ?? BramKit.FlatWith(BramKit.Water(new Vec2(0f, -6f + waterAhead), 0.25f, 0.25f, 1));
            var bram = BramKit.Bram(null, BramKit.Aug("AUG-310", rank));
            return BramKit.Duel(bram, layout, BramKit.Caster(BramKit.Bolt("SPL-T90", 15f), BramKit.Swipe("SPL-T92", 60f), CatalogueFireball()));
        }

        private static SpellForm CatalogueFireball() => BramKit.Form(BramKit.Duel(), "SPL-009");

        private const int FireballSlot = 2;

        private static float FireballDamage(DuelWorld w)
        {
            int before = BramKit.DamageTo(w, 0, "SPL-009").Count;
            BramKit.OpponentFires(w, FireballSlot, BramKit.Chest(w, 0));
            BramKit.RunUntil(w, () => BramKit.DamageTo(w, 0, "SPL-009").Count > before, 90);
            return BramKit.DamageTo(w, 0, "SPL-009").Last().Dealt;
        }

        private static float SmallBoltDamage(DuelWorld w)
        {
            int before = BramKit.DamageTo(w, 0, "SPL-T90").Count;
            BramKit.OpponentFires(w, 0, BramKit.Chest(w, 0));
            BramKit.RunUntil(w, () => BramKit.DamageTo(w, 0, "SPL-T90").Count > before, 40);
            return BramKit.DamageTo(w, 0, "SPL-T90").Last().Dealt;
        }

        private static int SteamCount(DuelWorld w) => BramKit.Reactions(w).Volumes.Count(v => v.Kind == VolumeKind.Steam);

        [Theory]
        [InlineData(1, 1.5f, true)]
        [InlineData(1, 2.5f, false)]
        [InlineData(2, 2.5f, true)]
        [Verifies("AUG-310")]
        public void Haze_Reservoir_Vents_Heat_1_Over_A_Forward_Cone_Once_The_Shimmer_Absorbed_20_Damage(int rank, float waterAhead, bool reaches)
        {
            DuelWorld w = ReservoirDuel(rank, waterAhead);
            int slot = BramKit.SlotOf(w, Haze);
            CastHaze(w);
            BramKit.Recast(w, slot); // not charged yet: no recast target, refused, nothing spent (spec L94)
            Assert.Equal(RejectReason.NoPendingRecast, BramKit.Casts(w, CastEventKind.Rejected).Last().Cast.Reject);
            Assert.Equal(0, w.Player(0).Caster.Machine.PendingRecastFor(slot).Used);
            Assert.DoesNotContain(BramKit.Spell(w, SpellEventKind.ConeReleased), e => e.SpellId == Haze);
            Assert.Equal(0, SteamCount(w));

            // A small projectile the shimmer softens: 13.2 absorbed, below the threshold.
            Assert.Equal(15f * 0.88f, SmallBoltDamage(w), 3);
            HazeReservoirEntity reservoir = BramKit.Entity<HazeReservoirEntity>(w);
            Assert.Equal(15f * 0.88f, reservoir.Absorbed, 3);
            Assert.False(reservoir.Ready);
            BramKit.Recast(w, slot); // still not charged: refused again, the recast stays available
            Assert.Equal(0, w.Player(0).Caster.Machine.PendingRecastFor(slot).Used);

            // The opponent's catalogue Fireball (48 direct, 42.24 after the shimmer): "First 20 absorbed damage readies one
            // Heat 1 vent" - the reservoir stops counting at the threshold.
            Assert.Equal(48f * 0.88f, FireballDamage(w), 3);
            Assert.True(reservoir.Ready);
            Assert.True(reservoir.Absorbed >= 20f);
            float healthBefore = w.Player(1).Resources.Health;
            BramKit.Recast(w, slot, TestWorlds.A.Selecting(slot));
            BramKit.Run(w, 2, TestWorlds.A.Selecting(slot));
            SpellEvent vent = BramKit.Spell(w, SpellEventKind.ConeReleased).Single(e => e.SpellId == Haze);
            Assert.Equal(rank >= 2 ? 3f : 2f, vent.Amount, 3); // "2 m forward cone" / "Vent cone 3 m"
            Assert.Equal(reaches ? 1 : 0, SteamCount(w));    // Heat 1 meets the water only inside the cone
            Assert.Empty(BramKit.DamageTo(w, 1));             // "no damage"
            Assert.Equal(healthBefore, w.Player(1).Resources.Health);
            Assert.True(BramKit.Entity<HazeReservoirEntity>(w).Vented);

            // "maximum Heat 1 unchanged": one vent per cast.
            BramKit.Recast(w, slot);
            Assert.Single(BramKit.Spell(w, SpellEventKind.ConeReleased), e => e.SpellId == Haze);
        }

        [Fact]
        [Verifies("AUG-310")]
        public void Haze_Reservoir_Needs_The_Full_20_Two_Small_Projectiles_Fill_It()
        {
            DuelWorld w = ReservoirDuel(1, 1.5f);
            CastHaze(w);
            HazeReservoirEntity reservoir = BramKit.Entity<HazeReservoirEntity>(w);
            SmallBoltDamage(w);
            Assert.False(reservoir.Ready); // 13.2 < 20
            BramKit.Run(w, 30);           // the test projectile's 0.5 s cooldown
            SmallBoltDamage(w);
            Assert.Equal(2f * 15f * 0.88f, reservoir.Absorbed, 3);
            Assert.True(reservoir.Ready);   // 26.4 >= 20
        }

        [Fact]
        [Verifies("AUG-310")]
        public void Haze_Reservoir_Lasts_3_Seconds_Adds_No_Reduction_And_Its_Vent_Ends_With_The_Mantle()
        {
            DuelWorld w = ReservoirDuel(1, 1.5f);
            int slot = BramKit.SlotOf(w, Haze);
            CastHaze(w);
            Assert.Equal(Ticks.FromSeconds(3.0), BramKit.Spell(w, SpellEventKind.BuffStarted).Single().Ticks); // "Buff 4 s becomes 3 s"
            Assert.Equal(0f, w.Player(0).Shields.Total, 3); // the reservoir grants no shield
            Assert.Equal(48f * 0.88f, FireballDamage(w), 3); // the base 12% only ("no additional shield/reduction")
            Assert.True(BramKit.Entity<HazeReservoirEntity>(w).Ready);
            Assert.Contains("cone", BramKit.Form(w, Haze).Tags); // structural delta (D-012)

            // "Wait out mantle": once the shimmer ended the charged vent is gone and there is nothing to recast.
            BramKit.RunUntil(w, () => BramKit.Spell(w, SpellEventKind.BuffEnded).Any(), 300, TestWorlds.A.Selecting(slot));
            BramKit.Run(w, 2, TestWorlds.A.Selecting(slot));
            BramKit.Recast(w, slot);
            Assert.Equal(RejectReason.NoPendingRecast, BramKit.Casts(w, CastEventKind.Rejected).Last().Cast.Reject);
            Assert.Equal(0, SteamCount(w));
        }

        [Fact]
        [Verifies("AUG-310")]
        public void Haze_Reservoir_Counts_Only_What_The_Shimmer_Absorbs_Never_Unmitigated_Melee()
        {
            DuelWorld w = ReservoirDuel(1, 1.5f);
            CastHaze(w);
            HazeReservoirEntity reservoir = BramKit.Entity<HazeReservoirEntity>(w);

            // A 60-damage cone strike (not a projectile) is not reduced by the haze, so nothing is absorbed.
            TestWorlds.Place(w, 1, new Vec3(0f, 0f, -3f), 180f);
            BramKit.OpponentFires(w, 1, BramKit.Chest(w, 0));
            BramKit.Run(w, 2);
            Assert.Equal(60f, BramKit.DamageTo(w, 0, "SPL-T92").Single().Dealt, 3);
            Assert.Equal(0f, reservoir.Absorbed, 3);
            Assert.False(reservoir.Ready);
        }

        [Fact]
        [Verifies("AUG-310")]
        public void Haze_Reservoir_Is_Never_Charged_By_The_Casters_Own_Damage()
        {
            // Bram's own neutral Phoenix Rush trail burns him while the shimmer lasts: no charge (no self-generated reward).
            var bram = BramKit.Bram(null, BramKit.Aug("AUG-310"));
            DuelWorld w = BramKit.Duel(bram, BramKit.FlatWith(), BramKit.Caster(BramKit.Bolt("SPL-T90", 100f)));
            TestWorlds.Place(w, 1, new Vec3(6f, 0f, 6f), 180f);
            int rush = BramKit.SlotOf(w, "SPL-013");
            BramKit.Cast(w, rush, BramKit.NormalHold, TestWorlds.A.Selecting(rush));
            BramKit.RunUntil(w, () => BramKit.Entity<HeatStripEntity>(w) != null, 60, TestWorlds.A.Selecting(rush));
            Assert.NotNull(BramKit.Entity<HeatStripEntity>(w));
            BramKit.Run(w, 30, TestWorlds.A.Selecting(rush)); // the rush's recovery
            CastHaze(w);
            HazeReservoirEntity reservoir = BramKit.Entity<HazeReservoirEntity>(w);
            TestWorlds.Place(w, 0, new Vec3(0f, 0f, -4f), 0f); // back on the hot trail
            BramKit.Run(w, Ticks.FromSeconds(1.5));
            Assert.True(BramKit.DamageTo(w, 0, "SPL-013").Count >= 2);
            Assert.Equal(0f, reservoir.Absorbed, 3);

            // Even a self-inflicted hit the haze would reduce is ignored.
            var self = DamageRequest.Single(0, 0, DamageKind.Direct, "SPL-009", DamageChannel.Thermal, 200f);
            self.ExtraPassiveReduction = 0.12f;
            DamageEvent ev = w.ApplyDamage(self);
            Assert.True(ev.Resolution.ConditionalPrevented > 20f);
            BramKit.Run(w, 1);
            Assert.Equal(0f, reservoir.Absorbed, 3);
            Assert.False(reservoir.Ready);
        }
    }
}
