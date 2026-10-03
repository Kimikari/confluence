using System.Linq;
using Confluence.Casting;
using Confluence.Core.Arena;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Reactions.Volumes;
using Confluence.Sim.Combat;
using Confluence.Sim.Content.Wizards.Wiz002;
using Confluence.Sim.Duel;
using Confluence.Sim.Input;
using Confluence.Sim.Players;
using Confluence.Sim.Spells;
using Confluence.Sim.Status;
using Confluence.Sim.Tests.Spells;
using InputButtons = Confluence.Casting.InputButtons;

namespace Confluence.Sim.Tests.Content.Wiz002
{
    /// <summary>SPL-015 Flame Lash and its transformations AUG-317 Lash Knot and AUG-318 Lash Steam.</summary>
    public class FlameLashTests
    {
        private const string Lash = "SPL-015";

        private static DuelWorld Duel(ArenaLayout layout = null, params ActiveAugment[] augments)
        {
            DuelWorld w = BramKit.Duel(BramKit.Bram(BramKit.LateKit, augments), layout, BramKit.Caster(BramKit.Bolt("SPL-T90", 30f)));
            TestWorlds.Place(w, 1, new Vec3(0f, 0f, -1f), 180f); // 5 m ahead of Bram
            return w;
        }

        private static void Lashing(DuelWorld w, int hold = BramKit.NormalHold, PlayerInput? during = null, PlayerInput? other = null)
        {
            int slot = BramKit.SlotOf(w, Lash);
            BramKit.Cast(w, slot, hold, TestWorlds.A.Selecting(slot), other);
            BramKit.RunUntil(w, () => BramKit.Spell(w, SpellEventKind.ArcReleased).Any(), 40, (during ?? TestWorlds.A).Selecting(slot), other);
            BramKit.Run(w, 1, (during ?? TestWorlds.A).Selecting(slot), other);
        }

        private static MaterialPatch Growth(Vec2 center, int heat) => new MaterialPatch
        {
            Center = center,
            HalfSize = new Vec2(0.5f, 0.5f),
            Growth = 1,
            Heat = heat,
            Flags = MaterialFlags.Growth,
            NaturalSource = true,
        };

        [Fact]
        [Verifies("SPL-015")]
        public void The_Lash_Hits_For_42_Thermal_And_Adds_Burn_4_Per_Second_For_3_Seconds()
        {
            DuelWorld w = Duel();
            Lashing(w);
            DamageEvent hit = BramKit.DamageTo(w, 1, Lash).Single(d => d.Kind == DamageKind.Direct);
            Assert.Equal(42f, hit.Dealt, 3);
            Assert.Equal(DamageChannel.Thermal, hit.Resolution.Channels[0].Channel);
            StatusEvent burn = SpellTestKit.Events<StatusEvent>(w).Single(s => s.Kind == StatusKind.Burn && s.Player == 1);
            Assert.Equal(4f, burn.Intensity, 3);                       // "Burn 4/s"
            Assert.Equal(Ticks.FromSeconds(3.0), burn.ExpireTick - burn.Tick); // "for 3 s"
            BramKit.Run(w, Ticks.FromSeconds(3.0) + 5);
            Assert.Equal(12f, BramKit.DamageTo(w, 1, Lash).Where(d => d.Kind == DamageKind.Status).Sum(d => d.Dealt), 3);
            Assert.Equal(18f, BramKit.Casts(w, CastEventKind.Committed).Single().Cast.Mana, 3);
            Assert.True(w.Player(0).Caster.CooldownRemainingTicks(Lash) > 0); // 7 s cooldown
            Assert.Equal(Ticks.FromSeconds(7.0), w.Player(0).Caster.CooldownReadyTick(Lash) - BramKit.Casts(w, CastEventKind.Committed).Single().Tick);
        }

        [Fact]
        [Verifies("SPL-015")]
        public void The_Strike_Is_0_5_Meters_Wide_And_Reaches_6_Meters()
        {
            DuelWorld beside = Duel();
            TestWorlds.Place(beside, 1, new Vec3(0.6f, 0f, -1f), 180f); // body surface 0.2 m from the line: within the 0.25 m half width
            Lashing(beside);
            Assert.Single(BramKit.DamageTo(beside, 1, Lash));

            DuelWorld wide = Duel();
            TestWorlds.Place(wide, 1, new Vec3(1.0f, 0f, -1f), 180f); // 1 m to the side: outside the 0.5 m-wide arc
            Lashing(wide);
            Assert.Empty(BramKit.DamageTo(wide, 1, Lash));

            DuelWorld far = Duel();
            TestWorlds.Place(far, 1, new Vec3(0f, 0f, 0.8f), 180f); // body 6.4 m away
            Lashing(far);
            Assert.Empty(BramKit.DamageTo(far, 1, Lash));
        }

        [Fact]
        [Verifies("SPL-015")]
        public void Its_Fixed_Arc_Resolves_Where_It_Was_Committed_And_Walls_Stop_It()
        {
            DuelWorld w = Duel();
            Lashing(w, BramKit.NormalHold, PlayerInput.Idle(90f)); // turning away during the windup changes nothing
            Assert.Equal(42f, BramKit.DamageTo(w, 1, Lash).First().Dealt, 3);

            DuelWorld walled = Duel(SpellTestKit.FlatWith(SpellTestKit.Wall(50, new Vec3(0f, 0f, -3f), new Vec3(4f, 3f, 0.5f))));
            Lashing(walled);
            Assert.Empty(BramKit.DamageTo(walled, 1, Lash)); // no hits through solid geometry (spec L102)
        }

        [Fact]
        [Verifies("SPL-015")]
        public void A_Parry_Reduces_The_Lash_And_A_Crest_Release_Adds_12_Percent()
        {
            DuelWorld w = Duel();
            int slot = BramKit.SlotOf(w, Lash);
            BramKit.Cast(w, slot, BramKit.NormalHold, TestWorlds.A.Selecting(slot));
            BramKit.Run(w, 8, TestWorlds.A.Selecting(slot), PlayerInput.Idle(180f).With(InputButtons.Guard));
            BramKit.RunUntil(w, () => BramKit.Spell(w, SpellEventKind.ArcReleased).Any(), 40, TestWorlds.A.Selecting(slot), PlayerInput.Idle(180f).With(InputButtons.Guard));
            DamageEvent guarded = BramKit.DamageTo(w, 1, Lash).First();
            Assert.Equal(GuardOutcome.Guarded, guarded.Resolution.Guard); // an offensive melee spell (D-135)
            Assert.Equal(21f, guarded.Dealt, 3);

            DuelWorld crest = Duel();
            Lashing(crest, BramKit.CrestHold);
            Assert.Equal(42f * 1.12f, BramKit.DamageTo(crest, 1, Lash).First().Dealt, 3);
        }

        [Fact]
        [Verifies("SPL-015")]
        public void Its_Heat_1_Ignites_Dry_Growth_Touched_By_The_Arc()
        {
            // Dry growth already holds one heat unit; the lash's Heat 1 makes the two that ignite it (spec L153).
            DuelWorld w = Duel(BramKit.FlatWith(Growth(new Vec2(0f, -3.5f), 1), Growth(new Vec2(3f, -3.5f), 1), Growth(new Vec2(-3f, -3.5f), 0)));
            TestWorlds.Place(w, 1, new Vec3(6f, 0f, 6f), 180f);
            Assert.False(BramKit.Reactions(w).IsBurningAt(new Vec3(0f, 0f, -3.5f)));
            Lashing(w);
            BramKit.Run(w, 2);
            Assert.True(BramKit.Reactions(w).IsBurningAt(new Vec3(0f, 0f, -3.5f)));   // touched by the arc
            Assert.False(BramKit.Reactions(w).IsBurningAt(new Vec3(3f, 0f, -3.5f)));  // not touched

            // Fresh growth without heat does not ignite from one unit.
            DuelWorld fresh = Duel(BramKit.FlatWith(Growth(new Vec2(0f, -3.5f), 0)));
            TestWorlds.Place(fresh, 1, new Vec3(6f, 0f, 6f), 180f);
            Lashing(fresh);
            BramKit.Run(fresh, 2);
            Assert.False(BramKit.Reactions(fresh).IsBurningAt(new Vec3(0f, 0f, -3.5f)));
        }

        // ----- AUG-317 Lash Knot -------------------------------------------------------------------------------

        [Theory]
        [InlineData(1, 3f, 28f)]
        [InlineData(2, 4f, 32f)]
        [Verifies("AUG-317")]
        public void Lash_Knot_Leaves_A_20_Health_Knot_And_The_Recast_Swings_From_It_Along_The_Manual_Arc(int rank, float seconds, float swing)
        {
            DuelWorld w = Duel(null, BramKit.Aug("AUG-317", rank));
            int slot = BramKit.SlotOf(w, Lash);
            Lashing(w);
            Assert.Equal(30f, BramKit.DamageTo(w, 1, Lash).Single().Dealt, 3); // "First lash 42 becomes 30"
            Assert.DoesNotContain(SpellTestKit.Events<StatusEvent>(w), s => s.Kind == StatusKind.Burn); // "Burn removed"

            SpellEvent knot = BramKit.Spell(w, SpellEventKind.DeployablePlaced).Single();
            Assert.Equal(20f, knot.Amount);
            Assert.Equal(Ticks.FromSeconds(seconds), knot.Ticks);
            Assert.InRange(knot.Position.Z, -0.1f, 0.1f); // where the whip's tip lands, 6 m out
            Assert.Contains(BramKit.Spell(w, SpellEventKind.Marker), e => e.SpellId == Lash); // its marked arc

            TestWorlds.Place(w, 1, new Vec3(3f, 0f, 0f), 270f);
            PlayerInput east = PlayerInput.Idle(90f).Selecting(slot);
            BramKit.Run(w, 20, east);
            BramKit.Recast(w, slot, east);
            BramKit.Run(w, 1, east);
            DamageEvent hit = BramKit.DamageTo(w, 1, Lash).Last();
            Assert.Equal(swing, hit.Dealt, 3);
            Assert.Equal(DamageKind.Direct, hit.Kind);
            Assert.False(BramKit.Entity<KnotEntity>(w)?.Alive ?? false);
        }

        [Fact]
        [Verifies("AUG-317")]
        public void Lash_Knot_Breaks_Under_Fire_And_Then_Never_Swings()
        {
            DuelWorld w = Duel(null, BramKit.Aug("AUG-317"));
            int slot = BramKit.SlotOf(w, Lash);
            Assert.DoesNotContain("burn", BramKit.Form(w, Lash).Tags); // structural delta (D-012)
            Lashing(w);
            KnotEntity knot = BramKit.Entity<KnotEntity>(w);
            TestWorlds.Place(w, 1, new Vec3(4f, 0f, 2f), 180f);
            BramKit.OpponentFires(w, 0, knot.Position); // 30 against 20 health: "Break knot"
            BramKit.RunUntil(w, () => !knot.Alive, 30);
            Assert.Contains(BramKit.Spell(w, SpellEventKind.DeployableDestroyed), e => e.EntityId == knot.Id);
            TestWorlds.Place(w, 1, new Vec3(3f, 0f, 0f), 270f);
            BramKit.Recast(w, slot, PlayerInput.Idle(90f).Selecting(slot));
            BramKit.Run(w, 2);
            Assert.Single(BramKit.DamageTo(w, 1, Lash)); // only the first lash
        }

        // ----- AUG-318 Lash Steam ------------------------------------------------------------------------------

        [Theory]
        [InlineData(1, 1f, 2f)]
        [InlineData(2, 1.5f, 3f)]
        [Verifies("AUG-318")]
        public void Lash_Steam_Consumes_The_Crossed_Water_Into_A_Steam_Strip(int rank, float width, float seconds)
        {
            DuelWorld w = Duel(BramKit.FlatWith(BramKit.Water(new Vec2(0f, -3f), 1f, 1f, 2)), BramKit.Aug("AUG-318", rank));
            TestWorlds.Place(w, 1, new Vec3(6f, 0f, 6f), 180f);
            Lashing(w);
            BramKit.Run(w, 1);
            ObscurationVolume strip = BramKit.Reactions(w).Volumes.Single(v => v.Kind == VolumeKind.Steam);
            Assert.Equal(0, strip.Owner);
            Assert.Equal(Ticks.FromSeconds(seconds), strip.ExpiresTick - strip.CreatedTick);
            strip.GetBounds(out Vec2 min, out Vec2 max);
            Assert.InRange(max.X - min.X, width - 0.1f, width + 0.6f);   // "1 m steam strip" / "1.5 m"
            Assert.InRange(max.Y - min.Y, 1.5f, 3.6f);                  // the crossed stretch of the 2 m pool
            Assert.Equal(1, BramKit.Reactions(w).UnitsAt(new Vec3(0f, 0f, -3f)).Water); // "consumes it": Water 2 - 1
            Assert.Equal(2, BramKit.Reactions(w).UnitsAt(new Vec3(0.75f, 0f, -3f)).Water); // off the track: untouched
        }

        [Fact]
        [Verifies("AUG-318")]
        public void Lash_Steam_Reach_Is_5_Meters_With_No_Extra_Damage()
        {
            DuelWorld w = Duel(null, BramKit.Aug("AUG-318"));
            Assert.Equal(5f, BramKit.Form(w, Lash).Range); // "Reach 6 becomes 5 m"
            TestWorlds.Place(w, 1, new Vec3(0f, 0f, -0.2f), 180f); // body 5.4 m away
            Lashing(w);
            Assert.Empty(BramKit.DamageTo(w, 1, Lash));

            DuelWorld near = Duel(null, BramKit.Aug("AUG-318"));
            Lashing(near);
            Assert.Equal(42f, BramKit.DamageTo(near, 1, Lash).First(d => d.Kind == DamageKind.Direct).Dealt, 3); // "no extra damage"
        }
    }
}
