using System.Linq;
using Confluence.Casting;
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
        }

        // ----- AUG-310 Haze Reservoir --------------------------------------------------------------------------

        private static DuelWorld ReservoirDuel(int rank, float waterAhead)
        {
            var layout = BramKit.FlatWith(BramKit.Water(new Vec2(0f, -6f + waterAhead), 0.25f, 0.25f, 1));
            var bram = BramKit.Bram(null, BramKit.Aug("AUG-310", rank));
            return BramKit.Duel(bram, layout, BramKit.Caster(BramKit.Bolt("SPL-T90", 50f)));
        }

        private static int SteamCount(DuelWorld w) => BramKit.Reactions(w).Volumes.Count(v => v.Kind == VolumeKind.Steam);

        [Theory]
        [InlineData(1, 1.5f, true)]
        [InlineData(1, 2.5f, false)]
        [InlineData(2, 2.5f, true)]
        [Verifies("AUG-310")]
        public void Haze_Reservoir_Vents_Heat_1_Over_A_Forward_Cone_Once_20_Damage_Was_Absorbed(int rank, float waterAhead, bool reaches)
        {
            DuelWorld w = ReservoirDuel(rank, waterAhead);
            int slot = BramKit.SlotOf(w, Haze);
            CastHaze(w);
            BramKit.Recast(w, slot); // not charged yet: nothing happens
            Assert.DoesNotContain(BramKit.Spell(w, SpellEventKind.ConeReleased), e => e.SpellId == Haze);
            Assert.Equal(0, SteamCount(w));

            BoltDamage(w); // 44 absorbed (>= 20)
            Assert.True(BramKit.Entity<HazeReservoirEntity>(w).Ready);
            float healthBefore = w.Player(1).Resources.Health;
            BramKit.Recast(w, slot, TestWorlds.A.Selecting(slot));
            BramKit.Run(w, 2, TestWorlds.A.Selecting(slot));
            SpellEvent vent = BramKit.Spell(w, SpellEventKind.ConeReleased).Single(e => e.SpellId == Haze);
            Assert.Equal(rank >= 2 ? 3f : 2f, vent.Amount, 3); // "2 m forward cone" / "Vent cone 3 m"
            Assert.Equal(reaches ? 1 : 0, SteamCount(w));    // Heat 1 meets the water only inside the cone
            Assert.Empty(BramKit.DamageTo(w, 1));             // "no damage"
            Assert.Equal(healthBefore, w.Player(1).Resources.Health);
            Assert.True(BramKit.Entity<HazeReservoirEntity>(w).Vented);
        }

        [Fact]
        [Verifies("AUG-310")]
        public void Haze_Reservoir_Lasts_3_Seconds_Adds_No_Reduction_And_Its_Vent_Ends_With_The_Mantle()
        {
            DuelWorld w = ReservoirDuel(1, 1.5f);
            int slot = BramKit.SlotOf(w, Haze);
            CastHaze(w);
            Assert.Equal(Ticks.FromSeconds(3.0), BramKit.Spell(w, SpellEventKind.BuffStarted).Single().Ticks); // "Buff 4 s becomes 3 s"
            Assert.Equal(50f * 0.88f, BoltDamage(w), 3); // the base 12% only ("no additional shield/reduction")
            Assert.Equal(0f, w.Player(0).Shields.Total, 3);
            Assert.Contains("cone", BramKit.Form(w, Haze).Tags); // structural delta (D-012)

            // "Wait out mantle": once the shimmer ended there is nothing to recast.
            BramKit.RunUntil(w, () => BramKit.Spell(w, SpellEventKind.BuffEnded).Any(), 300, TestWorlds.A.Selecting(slot));
            BramKit.Run(w, 2, TestWorlds.A.Selecting(slot));
            BramKit.Recast(w, slot);
            Assert.Equal(RejectReason.NoPendingRecast, BramKit.Casts(w, CastEventKind.Rejected).Last().Cast.Reject);
            Assert.Equal(0, SteamCount(w));
        }
    }
}
