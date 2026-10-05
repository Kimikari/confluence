using System.Linq;
using Confluence.Casting;
using Confluence.Core.Arena;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Reactions;
using Confluence.Reactions.Volumes;
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
    /// <summary>SPL-014 Boiler Orb and its transformations AUG-315 Boiler Funnel and AUG-316 Boiler Mine.</summary>
    public class BoilerOrbTests
    {
        private const string Orb = "SPL-014";

        private static DuelWorld Duel(ArenaLayout layout = null, params ActiveAugment[] augments) =>
            BramKit.Duel(BramKit.Bram(null, augments), layout, BramKit.Caster(BramKit.Bolt("SPL-T90", 30f)));

        /// <summary>Throws the orb at a ground point and runs until it rests.</summary>
        private static BoilerOrbEntity Throw(DuelWorld w, Vec3 ground, int hold = BramKit.NormalHold)
        {
            int slot = BramKit.SlotOf(w, Orb);
            PlayerInput aim = BramKit.AimAt(w, ground, slot);
            BramKit.Cast(w, slot, hold, aim);
            BramKit.RunUntil(w, () => BramKit.Entity<BoilerOrbEntity>(w)?.Settled == true, 90, aim);
            return BramKit.Entity<BoilerOrbEntity>(w);
        }

        /// <summary>Puts the dummy on the floor at an offset from the resting orb, facing back toward Bram.</summary>
        private static void PlaceNear(DuelWorld w, BoilerOrbEntity orb, float dz, float dx = 0f, float yaw = 180f) =>
            TestWorlds.Place(w, 1, new Vec3(orb.Position.X + dx, 0f, orb.Position.Z + dz), yaw);

        private static void Detonate(DuelWorld w, PlayerInput? other = null)
        {
            int slot = BramKit.SlotOf(w, Orb);
            BramKit.Run(w, 30, TestWorlds.A.Selecting(slot), other); // past the cast's recovery
            BramKit.Recast(w, slot, TestWorlds.A.Selecting(slot), other);
            BramKit.Run(w, 1, TestWorlds.A.Selecting(slot), other);
        }

        private static Vec3 Floor(BoilerOrbEntity orb) => new Vec3(orb.Position.X, 0f, orb.Position.Z);

        [Fact]
        [Verifies("SPL-014")]
        public void The_18_Meter_Per_Second_Orb_Waits_On_The_First_Surface_For_Up_To_3_Seconds()
        {
            DuelWorld w = Duel();
            BoilerOrbEntity orb = Throw(w, Vec3.Zero);
            Assert.Equal(18f, BramKit.Spell(w, SpellEventKind.ProjectileLaunched).Single().Amount, 3); // "18 m/s orb"
            Assert.True(orb.Settled);
            Assert.InRange(orb.Position.Y, 0.2f, 0.3f); // resting on the first surface it touched: the floor
            Assert.InRange(orb.Position.Z, -1.5f, 0.1f);
            Assert.Equal(Ticks.FromSeconds(0.45), BramKit.Spell(w, SpellEventKind.ProjectileLaunched).Single().Tick - BramKit.Casts(w, CastEventKind.Committed).Single().Tick);
            Assert.Equal(100f - 22f, BramKit.Mana(w), 3);
            Assert.True(w.Player(0).Caster.CooldownRemainingTicks(Orb) > Ticks.FromSeconds(9.0)); // 10 s cooldown
            Assert.Equal(0, w.Owned.Count(0, OwnedEntityKind.Field)); // a deployable, not a field

            SpellEvent placed = BramKit.Spell(w, SpellEventKind.DeployablePlaced).Single();
            Assert.Equal(25f, placed.Amount); // the 25-health orb
            Assert.Equal(Ticks.FromSeconds(3.0), placed.Ticks);
            BramKit.RunUntil(w, () => !orb.Alive, 240);
            Assert.InRange(w.Tick - placed.Tick, Ticks.FromSeconds(3.0), Ticks.FromSeconds(3.0) + 1);
            Assert.Single(BramKit.Spell(w, SpellEventKind.DeployableExpired));
            Assert.Empty(BramKit.DamageTo(w, 1));
        }

        [Fact]
        [Verifies("SPL-014")]
        public void The_Recast_Detonates_32_Thermal_Within_2_Meters_Unguarded()
        {
            DuelWorld w = Duel();
            BoilerOrbEntity orb = Throw(w, Vec3.Zero);
            PlaceNear(w, orb, 1.5f);
            Detonate(w, PlayerInput.Idle(180f).With(InputButtons.Guard));
            DamageEvent hit = BramKit.DamageTo(w, 1, Orb).Single();
            Assert.Equal(32f, hit.Dealt, 3); // "32 explosion", not guarded
            Assert.Equal(DamageKind.Splash, hit.Kind);
            Assert.Equal(DamageChannel.Thermal, hit.Resolution.Channels[0].Channel);
            Assert.Single(BramKit.Spell(w, SpellEventKind.DeployableDetonated));
            Assert.Empty(BramKit.DamageTo(w, 0, Orb)); // the caster stays outside it

            DuelWorld far = Duel();
            BoilerOrbEntity farOrb = Throw(far, Vec3.Zero);
            PlaceNear(far, farOrb, 2.6f);
            Detonate(far);
            Assert.Empty(BramKit.DamageTo(far, 1, Orb)); // "within 2 m"
        }

        [Fact]
        [Verifies("SPL-014")]
        public void A_Crest_Release_Adds_12_Percent_To_The_Explosion()
        {
            DuelWorld w = Duel();
            BoilerOrbEntity orb = Throw(w, Vec3.Zero, BramKit.CrestHold);
            PlaceNear(w, orb, 1.5f);
            Detonate(w);
            Assert.Equal(32f * 1.12f, BramKit.DamageTo(w, 1, Orb).Single().Dealt, 3);
        }

        [Fact]
        [Verifies("SPL-014")]
        public void Shooting_The_25_Health_Orb_Destroys_It_Without_An_Explosion()
        {
            DuelWorld w = Duel();
            BoilerOrbEntity orb = Throw(w, Vec3.Zero);
            PlaceNear(w, orb, 1.5f, 1f);
            BramKit.OpponentFires(w, 0, orb.Position); // 30 damage against 25 health
            BramKit.RunUntil(w, () => !orb.Alive, 30);
            Assert.Single(BramKit.Spell(w, SpellEventKind.DeployableDestroyed));
            Detonate(w);
            Assert.Empty(BramKit.Spell(w, SpellEventKind.DeployableDetonated));
            Assert.Empty(BramKit.DamageTo(w, 1, Orb));
        }

        [Fact]
        [Verifies("SPL-014")]
        public void Water_Consumes_The_Orbs_Heat_Into_Steam_And_The_Explosion_Drops_To_16()
        {
            DuelWorld w = Duel(BramKit.FlatWith(BramKit.Water(new Vec2(0f, -0.5f), 1.5f, 1.5f, 2)));
            BoilerOrbEntity orb = Throw(w, Vec3.Zero);
            BramKit.Run(w, 2);
            Assert.True(orb.IsSteamed); // "premature steaming"
            Assert.Equal(0, orb.Heat);
            ObscurationVolume steam = BramKit.Reactions(w).Volumes.Single(v => v.Kind == VolumeKind.Steam);
            Assert.Equal(0, steam.Owner);
            Assert.Contains(BramKit.Spell(w, SpellEventKind.Steamed), e => e.SpellId == Orb);
            PlaceNear(w, orb, 1.5f);
            Detonate(w);
            Assert.Equal(16f, BramKit.DamageTo(w, 1, Orb).Single().Dealt, 3); // "reducing explosion to 16"
        }

        [Fact]
        [Verifies("SPL-014")]
        public void A_Dry_Orb_Releases_Its_Heat_3_Where_It_Explodes()
        {
            // Ice is not liquid water: the resting orb keeps its heat, and the explosion's Heat 3 melts the tile (L146).
            DuelWorld w = Duel(BramKit.FlatWith(BramKit.Ice(new Vec2(0f, -0.5f), 1.5f, 1.5f)));
            BoilerOrbEntity orb = Throw(w, Vec3.Zero);
            BramKit.Run(w, 5);
            Assert.False(orb.IsSteamed);
            Assert.Equal(3, orb.Heat);
            Vec3 cell = Floor(orb);
            Assert.True(BramKit.Reactions(w).HasIceAt(cell));
            Detonate(w);
            BramKit.Run(w, 2);
            Assert.False(BramKit.Reactions(w).HasIceAt(cell));
        }

        [Fact]
        [Verifies("SPL-014")]
        public void With_No_Surface_Within_Range_The_Orb_Falls_To_The_Ground_And_Never_Waits_In_Mid_Air()
        {
            // Aimed upward over open ground: the 22 m flight ends in the air, then the orb drops to the first surface below.
            DuelWorld w = Duel();
            int slot = BramKit.SlotOf(w, Orb);
            PlayerInput aim = BramKit.AimAt(w, new Vec3(0f, 9f, 14f), slot);
            BramKit.Cast(w, slot, BramKit.NormalHold, aim);
            BramKit.RunUntil(w, () => BramKit.Entity<BoilerOrbEntity>(w)?.Falling == true, 150, aim);
            BoilerOrbEntity orb = BramKit.Entity<BoilerOrbEntity>(w);
            Assert.True(orb.Falling);
            Assert.False(orb.Settled);
            Assert.True(orb.Position.Y > 3f); // where the flight ended
            BramKit.Recast(w, slot, TestWorlds.A.Selecting(slot)); // nothing to detonate in the air
            Assert.Empty(BramKit.Spell(w, SpellEventKind.DeployableDetonated));

            BramKit.RunUntil(w, () => orb.Settled || !orb.Alive, 120, TestWorlds.A.Selecting(slot));
            Assert.True(orb.Settled);
            Assert.InRange(orb.Position.Y, BoilerOrbEntity.OrbRadius - 0.05f, BoilerOrbEntity.OrbRadius + 0.05f); // resting on the floor
            SpellEvent placed = BramKit.Spell(w, SpellEventKind.DeployablePlaced).Single();
            Assert.InRange(placed.Position.Y, 0f, 0.35f);

            // The press in the air spent nothing: the orb waiting on the floor still detonates.
            PlaceNear(w, orb, 1.5f);
            Detonate(w);
            Assert.Single(BramKit.Spell(w, SpellEventKind.DeployableDetonated));
            Assert.Equal(32f, BramKit.DamageTo(w, 1, Orb).Single().Dealt, 3);

            // With nothing below at all (beyond the floor's edge) it fades without ever waiting.
            DuelWorld edge = Duel(TestWorlds.Flat(18f, 8f));
            int s2 = BramKit.SlotOf(edge, Orb);
            PlayerInput out_ = BramKit.AimAt(edge, new Vec3(0f, 3f, 16f), s2);
            BramKit.Cast(edge, s2, BramKit.NormalHold, out_);
            BramKit.RunUntil(edge, () => BramKit.Spell(edge, SpellEventKind.DeployableExpired).Any(), 240, out_);
            Assert.Single(BramKit.Spell(edge, SpellEventKind.DeployableExpired));
            Assert.Empty(BramKit.Spell(edge, SpellEventKind.DeployablePlaced));
        }

        [Theory]
        [InlineData(null, 0, 32f)]
        [InlineData("AUG-315", 1, 26f)]
        [InlineData("AUG-315", 2, 26f)]
        [Verifies("SPL-014")]
        [Verifies("AUG-315")]
        public void An_Early_Recast_While_The_Orb_Flies_Does_Nothing_And_The_Waiting_Orb_Still_Detonates(string aug, int rank, float explosion)
        {
            // Spec L94: a recast "does nothing when none exists"; the flying orb is not waiting yet, so the press is
            // refused and the cast's one recast stays for the orb once it rests.
            DuelWorld w = aug == null ? Duel() : Duel(null, BramKit.Aug(aug, rank));
            int slot = BramKit.SlotOf(w, Orb);
            PlayerInput aim = BramKit.AimAt(w, new Vec3(3f, 0f, 12f), slot); // 18 m ahead, beside the dummy
            BramKit.Cast(w, slot, BramKit.NormalHold, aim);
            BramKit.RunUntil(w, () => BramKit.Entity<BoilerOrbEntity>(w) != null, 60, aim);
            BoilerOrbEntity orb = BramKit.Entity<BoilerOrbEntity>(w);
            Assert.False(orb.Settled);
            BramKit.Recast(w, slot, aim);
            Assert.False(orb.Settled);
            Assert.Equal(RejectReason.NoPendingRecast, BramKit.Casts(w, CastEventKind.Rejected).Last().Cast.Reject);
            Assert.Equal(0, w.Player(0).Caster.Machine.PendingRecastFor(slot).Used);
            Assert.Empty(BramKit.Spell(w, SpellEventKind.DeployableDetonated));

            BramKit.RunUntil(w, () => orb.Settled, 90, aim);
            Assert.True(orb.Settled);
            PlaceNear(w, orb, 1.5f);
            Detonate(w);
            Assert.Single(BramKit.Spell(w, SpellEventKind.DeployableDetonated));
            Assert.Equal(explosion, BramKit.DamageTo(w, 1, Orb).Single().Dealt, 3);
        }

        // ----- AUG-315 Boiler Funnel -----------------------------------------------------------------------------

        [Theory]
        [InlineData(1, 4f)]
        [InlineData(2, 5f)]
        [Verifies("AUG-315")]
        public void Boiler_Funnel_Turns_The_Steam_Reaction_Into_A_Jet_Along_The_Throw(int rank, float length)
        {
            DuelWorld w = Duel(BramKit.FlatWith(BramKit.Water(new Vec2(0f, -0.5f), 1.5f, 1.5f, 2)), BramKit.Aug("AUG-315", rank));
            BoilerOrbEntity orb = Throw(w, Vec3.Zero);
            BramKit.Run(w, 2);
            Assert.True(orb.IsSteamed);
            ObscurationVolume jet = BramKit.Reactions(w).Volumes.Single(v => v.Kind == VolumeKind.Steam);
            jet.GetBounds(out Vec2 min, out Vec2 max);
            Assert.InRange(max.Y - min.Y, length - 0.6f, length + 0.6f); // along the +Z throw
            Assert.InRange(max.X - min.X, 1.5f, 2.6f);                  // "2 m-wide jet"
            Assert.InRange(min.Y, orb.Position.Z - 0.6f, orb.Position.Z + 0.3f); // it starts at the orb
            Assert.Equal(Ticks.FromSeconds(3.0), jet.ExpiresTick - jet.CreatedTick);
            Assert.Equal(0, jet.Owner);
            Assert.Equal(0, BramKit.Reactions(w).UnitsAt(Floor(orb)).Water); // "reaction consumes orb heat" (and its cell's water)
        }

        [Fact]
        [Verifies("AUG-315")]
        public void Boiler_Funnel_Dry_Explosion_Is_26_And_A_Steamed_One_16()
        {
            DuelWorld dry = Duel(null, BramKit.Aug("AUG-315"));
            BoilerOrbEntity dryOrb = Throw(dry, Vec3.Zero);
            PlaceNear(dry, dryOrb, 1.5f);
            Detonate(dry);
            Assert.Equal(26f, BramKit.DamageTo(dry, 1, Orb).Single().Dealt, 3); // "Dry explosion 32 becomes 26"

            DuelWorld wet = Duel(BramKit.FlatWith(BramKit.Water(new Vec2(0f, -0.5f), 1.5f, 1.5f, 2)), BramKit.Aug("AUG-315"));
            BoilerOrbEntity wetOrb = Throw(wet, Vec3.Zero);
            PlaceNear(wet, wetOrb, 1.5f, 1.5f); // beside the jet
            Detonate(wet);
            Assert.Equal(16f, BramKit.DamageTo(wet, 1, Orb).Single().Dealt, 3);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [Verifies("AUG-315")]
        public void Boiler_Funnel_Counterplay_Dodging_The_Jet_Line_Keeps_Sight_And_Shooting_The_Orb_Stops_The_Explosion(int rank)
        {
            DuelWorld w = Duel(BramKit.FlatWith(BramKit.Water(new Vec2(0f, -0.5f), 1.5f, 1.5f, 2)), BramKit.Aug("AUG-315", rank));
            BoilerOrbEntity orb = Throw(w, Vec3.Zero);
            BramKit.Run(w, 2);
            Assert.True(orb.IsSteamed);

            // Looking down the jet line (the jet runs from the orb along the +Z throw toward the dummy): hidden.
            TestWorlds.Place(w, 1, new Vec3(0f, 0f, 6f), 180f);
            BramKit.Run(w, 1);
            Assert.False(w.CanAcquire(1, 0));
            Assert.False(w.CanAcquire(0, 1));

            // "Dodge jet line": three meters to the side, the sight line passes beside the 2 m-wide jet.
            TestWorlds.Place(w, 1, new Vec3(3f, 0f, 6f), 180f);
            BramKit.Run(w, 1);
            Assert.True(w.CanAcquire(1, 0));

            // "shoot orb": the 25-health orb shot down never explodes, even when recast.
            TestWorlds.Place(w, 1, new Vec3(3f, 0f, 2f), 180f);
            BramKit.OpponentFires(w, 0, orb.Position);
            BramKit.RunUntil(w, () => !orb.Alive, 40);
            Assert.Single(BramKit.Spell(w, SpellEventKind.DeployableDestroyed));
            PlaceNear(w, orb, 1.2f);
            Detonate(w);
            Assert.Empty(BramKit.Spell(w, SpellEventKind.DeployableDetonated));
            Assert.Empty(BramKit.DamageTo(w, 1, Orb));
        }

        // ----- AUG-316 Boiler Mine -------------------------------------------------------------------------------

        [Theory]
        [InlineData(1, 35f, 24f)]
        [InlineData(2, 45f, 28f)]
        [Verifies("AUG-316")]
        public void Boiler_Mine_Sticks_5_Seconds_And_Its_First_Entrant_Triggers_A_Warning_Then_The_Burst(int rank, float health, float burst)
        {
            DuelWorld w = Duel(null, BramKit.Aug("AUG-316", rank));
            Assert.Equal(22f + 4f, BramKit.Form(w, Orb).Mana); // "mana+4"
            BoilerOrbEntity mine = Throw(w, Vec3.Zero);
            SpellEvent placed = BramKit.Spell(w, SpellEventKind.DeployablePlaced).Single();
            Assert.Equal(health, placed.Amount);
            Assert.Equal(Ticks.FromSeconds(5.0), placed.Ticks);
            Assert.Equal(1, w.Owned.Count(0, OwnedEntityKind.Field)); // a trap uses a field allocation

            PlaceNear(w, mine, 1.2f); // body within 1 m of the mine
            BramKit.Run(w, 2);
            SpellEvent warning = BramKit.Spell(w, SpellEventKind.DeployableWarning).Single();
            Assert.Equal(Ticks.FromSeconds(0.5), warning.Ticks); // "0.5 s warning"
            Assert.Empty(BramKit.DamageTo(w, 1, Orb));
            BramKit.Run(w, Ticks.FromSeconds(0.5) + 1);
            Assert.Equal(burst, BramKit.DamageTo(w, 1, Orb).Single().Dealt, 3);
            Assert.False(mine.Alive);
        }

        [Fact]
        [Verifies("AUG-316")]
        public void Boiler_Mine_Can_Be_Escaped_During_Its_Warning_Has_No_Recast_And_Is_A_Trap()
        {
            DuelWorld w = Duel(null, BramKit.Aug("AUG-316"));
            int slot = BramKit.SlotOf(w, Orb);
            Assert.Contains("trap", BramKit.Form(w, Orb).Tags);  // structural delta (D-012)
            Assert.Contains("field", BramKit.Form(w, Orb).Tags);
            BoilerOrbEntity mine = Throw(w, Vec3.Zero);
            BramKit.Run(w, 30, TestWorlds.A.Selecting(slot));
            BramKit.Recast(w, slot, TestWorlds.A.Selecting(slot)); // "Lose recast/manual burst"
            Assert.Equal(RejectReason.NoPendingRecast, BramKit.Casts(w, CastEventKind.Rejected).Last().Cast.Reject);

            PlaceNear(w, mine, 1.2f, 0f, 0f);
            BramKit.Run(w, 1);
            Assert.Single(BramKit.Spell(w, SpellEventKind.DeployableWarning));
            BramKit.Run(w, 40, TestWorlds.A, PlayerInput.Idle(0f).Moving(0f, 1f)); // "escape warning": out of the 2 m burst
            Assert.Single(BramKit.Spell(w, SpellEventKind.DeployableDetonated));
            Assert.Empty(BramKit.DamageTo(w, 1, Orb));
        }

        [Fact]
        [Verifies("AUG-316")]
        public void Boiler_Mine_Shot_Down_Never_Bursts_And_Water_Still_Steams_It_To_16()
        {
            DuelWorld w = Duel(null, BramKit.Aug("AUG-316"));
            BoilerOrbEntity mine = Throw(w, Vec3.Zero);
            PlaceNear(w, mine, 3f, 3f);
            BramKit.OpponentFires(w, 0, mine.Position);
            BramKit.Run(w, 30);
            BramKit.OpponentFires(w, 0, mine.Position); // 2 x 30 against 35 health
            BramKit.RunUntil(w, () => !mine.Alive, 60);
            Assert.Single(BramKit.Spell(w, SpellEventKind.DeployableDestroyed));
            Assert.Empty(BramKit.Spell(w, SpellEventKind.DeployableDetonated));

            DuelWorld wet = Duel(BramKit.FlatWith(BramKit.Water(new Vec2(0f, -0.5f), 1.5f, 1.5f, 2)), BramKit.Aug("AUG-316"));
            BoilerOrbEntity steamed = Throw(wet, Vec3.Zero);
            BramKit.Run(wet, 2);
            Assert.True(steamed.IsSteamed); // "Heat 3 total unchanged": the mine still carries the orb's heat
            PlaceNear(wet, steamed, 1.2f);
            BramKit.Run(wet, Ticks.FromSeconds(0.5) + 3);
            Assert.Equal(16f, BramKit.DamageTo(wet, 1, Orb).Single().Dealt, 3);
        }
    }
}
