using System.Linq;
using Confluence.Casting;
using Confluence.Core.Arena;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Reactions.Grid;
using Confluence.Reactions.Operations;
using Confluence.Reactions.Volumes;
using Confluence.Sim.Combat;
using Confluence.Sim.Content.Wizards.Wiz002;
using Confluence.Sim.Duel;
using Confluence.Sim.Fields;
using Confluence.Sim.Input;
using Confluence.Sim.Players;
using Confluence.Sim.Spells;
using Confluence.Sim.Tests.Spells;
using Confluence.Sim.Tests.World;
using Confluence.Sim.World;
using InputButtons = Confluence.Casting.InputButtons;

namespace Confluence.Sim.Tests.Content.Wiz002
{
    /// <summary>SPL-016 Flashforge and its transformations AUG-319 Forge Plate and AUG-320 Forge Vacuum.</summary>
    public class FlashforgeTests
    {
        private const string Forge = "SPL-016";
        private static readonly Vec3 Point = new Vec3(0f, 0f, -2f); // 4 m ahead of Bram

        private static ArenaLayout WithProps(params PropPlacement[] props)
        {
            ArenaLayout layout = TestWorlds.Flat();
            layout.Props.AddRange(props);
            return layout;
        }

        // PROP-001 Copper Barrel is tagged metal; PROP-016 Timber Stack is not.
        private static PropPlacement Barrel(int id = 1, float z = -0.8f, float x = 0f) =>
            WorldDuels.Prop(id, "PROP-001", new Vec3(x, 0f, z), new Vec3(0.8f, 1f, 0.8f));

        private static PropPlacement Timber(int id = 2) => WorldDuels.Prop(id, "PROP-016", new Vec3(0f, 0f, -0.8f), new Vec3(0.8f, 1f, 0.8f));

        private static DuelWorld Duel(ArenaLayout layout = null, params ActiveAugment[] augments) =>
            BramKit.Duel(BramKit.Bram(BramKit.LateKit, augments), layout, BramKit.Caster(BramKit.Bolt("SPL-T90", 30f)));

        private static void Smash(DuelWorld w, int hold = BramKit.NormalHold, PlayerInput? during = null, PlayerInput? other = null)
        {
            int slot = BramKit.SlotOf(w, Forge);
            BramKit.Cast(w, slot, hold, BramKit.AimAt(w, Point, slot), other);
            BramKit.RunUntil(w, () => BramKit.Spell(w, SpellEventKind.AreaBurst).Any(e => e.SpellId == Forge), 80, (during ?? TestWorlds.A).Selecting(slot), other);
            BramKit.Run(w, 1, (during ?? TestWorlds.A).Selecting(slot), other);
        }

        [Fact]
        [Verifies("SPL-016")]
        public void It_Smashes_A_2_Meter_Ground_Area_For_58_Thermal_After_Its_0_65_Second_Windup()
        {
            DuelWorld w = Duel();
            TestWorlds.Place(w, 1, new Vec3(1.2f, 0f, -2f), 270f); // body within 2 m of the smash point
            Smash(w);
            DamageEvent hit = BramKit.DamageTo(w, 1, Forge).Single();
            Assert.Equal(58f, hit.Dealt, 3);
            Assert.Equal(DamageKind.Direct, hit.Kind);
            Assert.Equal(DamageChannel.Thermal, hit.Resolution.Channels[0].Channel);
            CastDuelEventOf(w, out int commitTick, out float mana);
            Assert.Equal(32f, mana, 3);
            Assert.Equal(commitTick + Ticks.FromSeconds(0.65), BramKit.Spell(w, SpellEventKind.AreaBurst).Single().Tick);
            Assert.Equal(Ticks.FromSeconds(16.0), w.Player(0).Caster.CooldownReadyTick(Forge) - commitTick);

            DuelWorld far = Duel();
            TestWorlds.Place(far, 1, new Vec3(0f, 0f, 1f), 180f); // 3 m from the point
            Smash(far);
            Assert.Empty(BramKit.DamageTo(far, 1, Forge));
        }

        private static void CastDuelEventOf(DuelWorld w, out int tick, out float mana)
        {
            var commit = BramKit.Casts(w, CastEventKind.Committed).Single(e => e.SpellId == Forge);
            tick = commit.Tick;
            mana = commit.Cast.Mana;
        }

        [Fact]
        [Verifies("SPL-016")]
        public void The_Raised_Casting_Marker_Is_Fixed_At_Commitment_So_Leaving_It_Avoids_The_Smash()
        {
            DuelWorld w = Duel();
            TestWorlds.Place(w, 1, new Vec3(1.2f, 0f, -2f), 270f);
            int slot = BramKit.SlotOf(w, Forge);
            BramKit.Cast(w, slot, BramKit.NormalHold, BramKit.AimAt(w, Point, slot));
            SpellEvent marker = BramKit.Spell(w, SpellEventKind.Marker).Single(e => e.SpellId == Forge);
            Assert.Equal(2f, marker.Amount, 3);
            Assert.InRange(Vec3.DistanceXZ(marker.Position, Point), 0f, 0.1f);
            Assert.Equal(Ticks.FromSeconds(0.65), marker.Ticks);

            // The caster turns away during the windup; the opponent leaves the marker.
            BramKit.Run(w, 30, PlayerInput.Idle(90f).Selecting(slot), PlayerInput.Idle(0f).Moving(0f, 1f));
            BramKit.RunUntil(w, () => BramKit.Spell(w, SpellEventKind.AreaBurst).Any(), 20, PlayerInput.Idle(90f).Selecting(slot));
            Assert.InRange(Vec3.DistanceXZ(BramKit.Spell(w, SpellEventKind.AreaBurst).Single().Position, Point), 0f, 0.1f);
            Assert.Empty(BramKit.DamageTo(w, 1, Forge));
        }

        [Fact]
        [Verifies("SPL-016")]
        public void A_Guard_Toward_The_Smash_Reduces_It_And_A_Crest_Release_Adds_12_Percent()
        {
            DuelWorld w = Duel();
            TestWorlds.Place(w, 1, new Vec3(1.2f, 0f, -2f), 270f);
            PlayerInput guard = PlayerInput.Idle(270f).With(InputButtons.Guard);
            int slot = BramKit.SlotOf(w, Forge);
            BramKit.Cast(w, slot, BramKit.NormalHold, BramKit.AimAt(w, Point, slot), PlayerInput.Idle(270f));
            BramKit.Run(w, 25, TestWorlds.A.Selecting(slot), PlayerInput.Idle(270f));
            BramKit.RunUntil(w, () => BramKit.DamageTo(w, 1, Forge).Any(), 30, TestWorlds.A.Selecting(slot), guard);
            Assert.Equal(GuardOutcome.Guarded, BramKit.DamageTo(w, 1, Forge).Single().Resolution.Guard);
            Assert.Equal(29f, BramKit.DamageTo(w, 1, Forge).Single().Dealt, 3);

            DuelWorld crest = Duel();
            TestWorlds.Place(crest, 1, new Vec3(1.2f, 0f, -2f), 270f);
            Smash(crest, BramKit.CrestHold);
            Assert.Equal(58f * 1.12f, BramKit.DamageTo(crest, 1, Forge).Single().Dealt, 3);
        }

        [Fact]
        [Verifies("SPL-016")]
        public void An_Intact_Metal_Prop_In_The_Area_Turns_Red_Hot_For_4_Seconds_Dealing_8_Per_Second_On_Contact()
        {
            DuelWorld w = Duel(WithProps(Barrel()));
            Smash(w);
            HotPropEntity hot = BramKit.Entity<HotPropEntity>(w);
            Assert.NotNull(hot);
            Assert.Equal("PROP-001", hot.Prop.DefinitionId);
            SpellEvent heated = BramKit.Spell(w, SpellEventKind.Heated).Single();
            Assert.Equal(8f, heated.Amount);                      // "dealing 8/s on contact"
            Assert.Equal(Ticks.FromSeconds(4.0), heated.Ticks);   // "red-hot for 4 s"
            Assert.Equal(1, w.Owned.Count(0, OwnedEntityKind.Field));
            Assert.False(hot.Prop.Destroyed);                     // "metal is still destructible" and intact
            Assert.True(hot.Prop.HasCollision);                   // "walkable rules remain unchanged"

            TestWorlds.Place(w, 1, new Vec3(0f, 0f, 0.05f), 180f); // touching the barrel's far face
            BramKit.Run(w, 2);
            DamageEvent burn = BramKit.DamageTo(w, 1, Forge).Single();
            Assert.Equal(8f, burn.Dealt, 3);
            Assert.Equal(DamageKind.Environment, burn.Kind); // contact: environmental, never guarded (spec L104, L672)
            BramKit.Run(w, Ticks.PerSecond);
            Assert.Equal(2, BramKit.DamageTo(w, 1, Forge).Count);

            // The owner touching it is burned too ("cool or avoid the hot prop").
            TestWorlds.Place(w, 0, new Vec3(0f, 0f, -1.65f), 0f);
            BramKit.Run(w, 2);
            Assert.Single(BramKit.DamageTo(w, 0, Forge));

            BramKit.RunUntil(w, () => !hot.Alive, Ticks.FromSeconds(4.0));
            Assert.InRange(w.Tick - heated.Tick, Ticks.FromSeconds(4.0) - 1, Ticks.FromSeconds(4.0) + 2);
            int hits = BramKit.DamageTo(w, 1, Forge).Count;
            BramKit.Run(w, 2 * Ticks.PerSecond);
            Assert.Equal(hits, BramKit.DamageTo(w, 1, Forge).Count);
        }

        [Fact]
        [Verifies("SPL-016")]
        public void Only_Metal_Props_Heat_And_Destroying_Or_Cooling_The_Prop_Ends_It()
        {
            DuelWorld timber = Duel(WithProps(Timber()));
            Smash(timber);
            Assert.Null(BramKit.Entity<HotPropEntity>(timber)); // PROP-016 is not metal

            DuelWorld broken = Duel(WithProps(Barrel()));
            Smash(broken);
            HotPropEntity hot = BramKit.Entity<HotPropEntity>(broken);
            Assert.True(broken.Props.Damage(1, 1000f, 1)); // the opponent breaks the barrel
            BramKit.Run(broken, 2);
            Assert.False(hot.Alive);
            Assert.True(hot.Cooled);
            Assert.Contains(BramKit.Spell(broken, SpellEventKind.Cooled), e => e.SpellId == Forge);

            DuelWorld cooled = Duel(WithProps(Barrel()));
            Smash(cooled);
            HotPropEntity wet = BramKit.Entity<HotPropEntity>(cooled);
            cooled.QueueDeposit(new WorldDeposit { Kind = DepositKind.Water, Strength = 1, Position = new Vec3(0f, 0f, -0.8f), SourcePlayer = 1, SourceId = "SPL-T95", LifetimeTicks = 120 });
            BramKit.Run(cooled, 3);
            Assert.False(wet.Alive); // "cool ... the hot prop"
            Assert.True(wet.Cooled);
        }

        [Fact]
        [Verifies("SPL-016")]
        public void Its_Heat_3_Melts_Ice_Under_The_Smashed_Area()
        {
            DuelWorld w = Duel(BramKit.FlatWith(BramKit.Ice(new Vec2(0f, -2f), 1f, 1f)));
            Assert.True(BramKit.Reactions(w).HasIceAt(Point));
            Smash(w);
            BramKit.Run(w, 1);
            Assert.False(BramKit.Reactions(w).HasIceAt(Point)); // two heat units melt a tile; the third steams its water (L146)
        }

        // ----- AUG-319 Forge Plate -----------------------------------------------------------------------------

        [Theory]
        [InlineData(1, 25f, 6f)]
        [InlineData(2, 35f, 8f)]
        [Verifies("AUG-319")]
        public void Forge_Plate_Forges_A_2_Meter_Hot_Plate_Instead_Of_Heating_A_Prop(int rank, float health, float dps)
        {
            DuelWorld w = Duel(WithProps(Barrel()), BramKit.Aug("AUG-319", rank));
            int solids = w.Collision.Solids.Count;
            TestWorlds.Place(w, 1, new Vec3(1.2f, 0f, -2f), 270f);
            Smash(w);
            Assert.Equal(44f, BramKit.DamageTo(w, 1, Forge).Single(d => d.Kind == DamageKind.Direct).Dealt, 3); // "Initial 58 becomes 44"
            Assert.Null(BramKit.Entity<HotPropEntity>(w)); // the metal barrel is not selected
            ForgePlateEntity plate = BramKit.Entity<ForgePlateEntity>(w);
            Assert.NotNull(plate);
            Assert.Equal(health, plate.Health);
            SpellEvent placed = BramKit.Spell(w, SpellEventKind.DeployablePlaced).Single();
            Assert.Equal(Ticks.FromSeconds(4.0), placed.Ticks);
            Assert.Equal(1, w.Owned.Count(0, OwnedEntityKind.Field));
            Assert.Equal(solids, w.Collision.Solids.Count); // "plate never blocks routes"

            TestWorlds.Place(w, 1, new Vec3(0.5f, 0f, -2f), 270f); // standing on the plate
            BramKit.Run(w, 2);
            DamageEvent contact = BramKit.DamageTo(w, 1, Forge).Last();
            Assert.Equal(dps, contact.Dealt, 3);
            Assert.Equal(DamageKind.Environment, contact.Kind); // environmental contact (spec L672)
            BramKit.Run(w, Ticks.PerSecond);
            Assert.Equal(3, BramKit.DamageTo(w, 1, Forge).Count); // the smash and two contact seconds
        }

        [Fact]
        [Verifies("AUG-319")]
        public void Forge_Plate_Breaks_Or_Cools_And_Then_Stops_Burning()
        {
            DuelWorld w = Duel(null, BramKit.Aug("AUG-319"));
            Smash(w);
            ForgePlateEntity plate = BramKit.Entity<ForgePlateEntity>(w);
            TestWorlds.Place(w, 1, new Vec3(2.5f, 0f, 0f), 225f);
            BramKit.OpponentFires(w, 0, plate.BodyCenter); // 30 against 25 health: "Break/cool plate"
            BramKit.RunUntil(w, () => !plate.Alive, 30);
            Assert.True(plate.Cooled);
            Assert.Contains(BramKit.Spell(w, SpellEventKind.DeployableDestroyed), e => e.EntityId == plate.Id);

            DuelWorld v = Duel(null, BramKit.Aug("AUG-319"));
            Smash(v);
            ForgePlateEntity wet = BramKit.Entity<ForgePlateEntity>(v);
            v.QueueDeposit(new WorldDeposit { Kind = DepositKind.Water, Strength = 1, Position = Point, SourcePlayer = 1, SourceId = "SPL-T95", LifetimeTicks = 120 });
            BramKit.Run(v, 3);
            Assert.False(wet.Alive);
            TestWorlds.Place(v, 1, new Vec3(0.5f, 0f, -2f), 270f);
            BramKit.Run(v, Ticks.PerSecond);
            Assert.Empty(BramKit.DamageTo(v, 1, Forge));
        }

        // ----- AUG-320 Forge Vacuum ----------------------------------------------------------------------------

        [Theory]
        [InlineData(1, 2f)]
        [InlineData(2, 3f)]
        [Verifies("AUG-320")]
        public void Forge_Vacuum_Pulls_Loose_Steam_Toward_The_Impact_Point_Once(int rank, float pull)
        {
            DuelWorld w = Duel(null, BramKit.Aug("AUG-320", rank));
            BramKit.Materials(w).CreateVolume(new VolumeSpec
            {
                Kind = VolumeKind.Steam,
                Footprint = Footprint.Circle(new Vec2(5f, -2f), 1f), // 5 m east of the smash point
                LifetimeTicks = Ticks.FromSeconds(3.0),
                Owner = 1,
                SourceEventId = 99,
            });
            BramKit.Run(w, 1);
            ObscurationVolume steam = BramKit.Reactions(w).Volumes.Single();
            int expires = steam.ExpiresTick;
            Vec3 dummy = w.Player(1).Position;
            Smash(w);
            BramKit.Run(w, 80);
            Assert.InRange(-steam.Offset.X, pull - 0.2f, pull + 0.05f); // moved west toward the impact point
            Assert.InRange(System.Math.Abs(steam.Offset.Y), 0f, 0.05f);
            Assert.Equal(expires, steam.ExpiresTick); // "steam lifetime unchanged"
            Assert.Equal(dummy, w.Player(1).Position); // "no player pull"
        }

        [Fact]
        [Verifies("AUG-320")]
        public void Forge_Vacuum_Adds_0_2_Seconds_Of_Windup_And_Hits_For_52()
        {
            DuelWorld w = Duel(null, BramKit.Aug("AUG-320"));
            TestWorlds.Place(w, 1, new Vec3(1.2f, 0f, -2f), 270f);
            Smash(w);
            CastDuelEventOf(w, out int commitTick, out _);
            Assert.Equal(commitTick + Ticks.FromSeconds(0.85), BramKit.Spell(w, SpellEventKind.AreaBurst).Single().Tick);
            Assert.Equal(52f, BramKit.DamageTo(w, 1, Forge).Single().Dealt, 3); // "initial 58 becomes 52"

            // With AUG-319 both reductions apply to one number: the stricter wins (spec L510).
            DuelWorld both = Duel(null, BramKit.Aug("AUG-319"), BramKit.Aug("AUG-320"));
            Assert.Equal(44f, BramKit.Form(both, Forge).Payload.Direct);
        }
    }
}
