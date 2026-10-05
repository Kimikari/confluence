using System.Linq;
using Confluence.Casting;
using Confluence.Core.Arena;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Reactions.Grid;
using Confluence.Reactions.Operations;
using Confluence.Reactions.Volumes;
using Confluence.Sim.Casting;
using Confluence.Sim.Combat;
using Confluence.Sim.Content.Augments.AugmentsA;
using Confluence.Sim.Duel;
using Confluence.Sim.Effects;
using Confluence.Sim.Input;
using Confluence.Sim.Players;
using Confluence.Sim.Status;
using Confluence.Sim.Tests.World;
using InputButtons = Confluence.Casting.InputButtons;

namespace Confluence.Sim.Tests.Content.AugmentsA
{
    /// <summary>The "Movement arts" shared augments in real duels (movement L80-L82, caps L124, surfaces L175).</summary>
    public class MovementArtsTests
    {
        private static readonly PlayerInput Forward = AKit.Walk(0f);
        private static readonly PlayerInput Strafe = AKit.Walk(0f, 1f, 0f);
        private static PlayerInput SprintInput => WorldDuels.Sprint(0f);

        private static float Stamina(DuelWorld w) => w.Player(0).Resources.Stamina;

        // ----- AUG-097 Economical Sprint -------------------------------------------------------------------

        [Theory]
        [InlineData(1, 11f)]
        [InlineData(2, 10f)]
        [Verifies("AUG-097")]
        public void Economical_Sprint_Lowers_The_Drain_And_Adds_1_To_The_Dodge(int rank, float drain)
        {
            AKit.AssertQuotes(EconomicalSprint.Id, "ordinary sprint", "Drain decreases 1 stamina/s.", "Drain decreases 2 stamina/s.", "Dodge costs 1 extra stamina.");
            DuelWorld w = AKit.With(EconomicalSprint.Id, rank);
            AKit.Step(w, 60, SprintInput);
            Assert.Equal(100f - drain, Stamina(w), 2);

            DuelWorld dodge = AKit.With(EconomicalSprint.Id, rank);
            AKit.Step(dodge, 1, WorldDuels.Dodge(0f));
            Assert.Equal(100f - GlobalRules.Movement.DodgeStaminaCost - 1f, Stamina(dodge), 3);
        }

        // ----- AUG-098 Deliberate Dodge --------------------------------------------------------------------

        [Theory]
        [InlineData(1, 23f)]
        [InlineData(2, 21f)]
        [Verifies("AUG-098")]
        public void Deliberate_Dodge_Cheapens_The_Dodge(int rank, float cost)
        {
            AKit.AssertQuotes(DeliberateDodge.Id, "ordinary dodge", "Cost decreases 2 stamina.", "Cost decreases 4 stamina.", "Dodge recovery increases 0.05 s.");
            DuelWorld w = AKit.With(DeliberateDodge.Id, rank);
            AKit.Step(w, 1, WorldDuels.Dodge(0f));
            Assert.True(w.Player(0).Motor.DodgeTicks > 0);
            Assert.Equal(100f - cost, Stamina(w), 3);
        }

        /// <summary>Dodges forward, steps until the travel ends, then returns the jump result one step later.</summary>
        private static (bool jumped, float speedAfter) DodgeThenJump(DuelWorld w)
        {
            AKit.Step(w, 1, WorldDuels.Dodge(0f));
            for (int i = 0; i < 30 && w.Player(0).Motor.DodgeTicks > 0; i++) AKit.Step(w, 1, Forward);
            AKit.Step(w, 1, Forward.With(InputButtons.Jump));
            bool jumped = !w.Player(0).Motor.Grounded;
            AKit.Step(w, 1, Forward);
            return (jumped, AKit.Speed(w, 0));
        }

        [Fact]
        [Verifies("AUG-098")]
        public void Deliberate_Dodge_Drawback_Adds_A_005_s_Recovery_At_The_Destination()
        {
            (bool jumped, float speed) withAug = DodgeThenJump(AKit.With(DeliberateDodge.Id, 1));
            (bool jumped, float speed) control = DodgeThenJump(AKit.Without());
            Assert.True(control.jumped);
            Assert.False(withAug.jumped); // no jump, move, or dodge in the 0.05 s after the travel
            Assert.True(withAug.speed < control.speed - 1f);

            DuelWorld w = AKit.With(DeliberateDodge.Id, 2);
            AKit.Step(w, 1, WorldDuels.Dodge(0f));
            for (int i = 0; i < 30 && w.Player(0).Motor.DodgeTicks > 0; i++) AKit.Step(w, 1, Forward);
            AKit.Step(w, AKit.Ticks(0.05f), Forward);
            AKit.Step(w, 1, Forward.With(InputButtons.Jump));
            Assert.False(w.Player(0).Motor.Grounded); // after the recovery the jump works
        }

        // ----- AUG-099 Light Landing -----------------------------------------------------------------------

        [Theory]
        [InlineData(1, 9f)]
        [InlineData(2, 8f)]
        [Verifies("AUG-099")]
        public void Light_Landing_Cheapens_The_Jump(int rank, float cost)
        {
            AKit.AssertQuotes(LightLanding.Id, "ordinary jump", "Cost decreases 1 stamina.", "Cost decreases 2 stamina.", "Airborne incoming damage increases 2%.");
            DuelWorld w = AKit.With(LightLanding.Id, rank);
            AKit.Step(w, 1, AKit.A.With(InputButtons.Jump));
            Assert.False(w.Player(0).Motor.Grounded);
            Assert.Equal(100f - cost, Stamina(w), 3);
        }

        [Fact]
        [Verifies("AUG-099")]
        public void Light_Landing_Drawback_Makes_Airborne_Hits_2_Percent_Stronger_Inside_The_Pipeline()
        {
            DuelWorld w = AKit.With(LightLanding.Id, 2);
            AKit.Step(w, 1, AKit.A.With(InputButtons.Jump));
            AKit.Step(w, 4);
            Assert.False(w.Player(0).Motor.Grounded);
            DamageEvent air = w.ApplyDamage(TestWorlds.Hit(w, 1, 0, 100f));
            Assert.Equal(102f, air.Resolution.Mitigated, 3);
            AKit.Step(w, 60);
            Assert.True(w.Player(0).Motor.Grounded);
            DamageEvent ground = w.ApplyDamage(TestWorlds.Hit(w, 1, 0, 100f));
            Assert.Equal(100f, ground.Resolution.Mitigated, 3);
        }

        // ----- AUG-100 Walking Scholar ---------------------------------------------------------------------

        [Theory]
        [InlineData(1, 16f)]
        [InlineData(2, 15f)]
        [Verifies("AUG-100")]
        public void Walking_Scholar_Discounts_A_Cast_After_1_s_Of_Walking(int rank, float paid)
        {
            AKit.AssertQuotes(WalkingScholar.Id, "Walk without sprinting for 1 s, then cast.", "2 less mana", "3 less mana", "Sprint resets preparation.");
            DuelWorld w = AKit.With(WalkingScholar.Id, rank);
            AKit.Step(w, 70, Strafe);
            AKit.HoldAndRelease(w, AKit.NormalHold, Strafe);
            Assert.Equal(paid, AKit.Casts(w, CastEventKind.Committed).Single().Cast.Mana, 3);
            Assert.Single(AKit.Activations(w, WalkingScholar.Id));
        }

        [Fact]
        [Verifies("AUG-100")]
        public void Walking_Scholar_Is_Reset_By_Sprinting_And_Needs_A_Walk()
        {
            DuelWorld sprinted = AKit.With(WalkingScholar.Id, 2);
            AKit.Step(sprinted, 70, Strafe);
            AKit.Step(sprinted, 5, WorldDuels.Sprint(90f));
            AKit.Step(sprinted, 30, Strafe);
            AKit.HoldAndRelease(sprinted, AKit.NormalHold, Strafe);
            Assert.Equal(18f, AKit.Casts(sprinted, CastEventKind.Committed).Single().Cast.Mana, 3);

            DuelWorld standing = AKit.With(WalkingScholar.Id, 2);
            AKit.Step(standing, 70);
            AKit.HoldAndRelease(standing, AKit.NormalHold);
            Assert.Equal(18f, AKit.Casts(standing, CastEventKind.Committed).Single().Cast.Mana, 3);

            // One discounted cast per walk: the next cast needs another full second.
            DuelWorld twice = AKit.With(WalkingScholar.Id, 2);
            AKit.Step(twice, 70, Strafe);
            AKit.HoldAndRelease(twice, AKit.NormalHold, Strafe);
            AKit.Step(twice, 40, AKit.SelectSlot(Strafe, 1)); // past the Fireball windup and recovery
            AKit.HoldAndRelease(twice, AKit.NormalHold, AKit.SelectSlot(Strafe, 1));
            float[] mana = AKit.Casts(twice, CastEventKind.Committed).Select(c => c.Cast.Mana).ToArray();
            Assert.Equal(new[] { 15f, 15f }, mana); // SPL-009 18 - 3, then SPL-010 Cinder Sweep 15 in full
            Assert.Equal("SPL-010", AKit.Casts(twice, CastEventKind.Committed).Last().SpellId);
        }

        // ----- AUG-101 Short Burst -------------------------------------------------------------------------

        [Theory]
        [InlineData(1, 0.03f)]
        [InlineData(2, 0.05f)]
        [Verifies("AUG-101")]
        public void Short_Burst_Speeds_The_First_Second_Of_A_Sprint_Then_Drains_1_More(int rank, float bonus)
        {
            AKit.AssertQuotes(ShortBurst.Id, "Sprint continuously for at most 1 s.", "Speed increases 3%.", "Speed increases 5%.", "After 1 s, sprint drains 1 extra stamina/s.");
            DuelWorld w = AKit.With(ShortBurst.Id, rank);
            AKit.Step(w, 20, SprintInput);
            Assert.Equal(GlobalRules.Movement.SprintSpeed * (1f + bonus), AKit.Speed(w, 0), 2);
            AKit.Step(w, 40, SprintInput);
            Assert.Equal(100f - GlobalRules.Movement.SprintStaminaPerSecond, Stamina(w), 2);
            AKit.Step(w, 10, SprintInput);
            Assert.Equal(GlobalRules.Movement.SprintSpeed, AKit.Speed(w, 0), 2);
            AKit.Step(w, 50, SprintInput);
            Assert.Equal(100f - GlobalRules.Movement.SprintStaminaPerSecond - 13f, Stamina(w), 2);

            // A new sprint starts a new burst.
            AKit.Step(w, 2, AKit.Walk(0f));
            AKit.Step(w, 15, SprintInput);
            Assert.Equal(GlobalRules.Movement.SprintSpeed * (1f + bonus), AKit.Speed(w, 0), 2);
        }

        [Fact]
        [Verifies("AUG-101")]
        public void Short_Burst_Respects_The_20_Percent_Sprint_Cap()
        {
            DuelPlayerSetup a = AKit.Setup("A", AKit.Bram, AKit.Aug(ShortBurst.Id, 2));
            a.Modifiers.Add(new StatModifier(StatKind.SprintSpeedBonus, 0.19f, "EQP-TEST"));
            DuelWorld w = AKit.Duel(a);
            AKit.Step(w, 20, SprintInput);
            Assert.Equal(GlobalRules.Movement.SprintSpeed * (1f + GlobalRules.Modifiers.MovementBonusCap), AKit.Speed(w, 0), 2);
        }

        // ----- AUG-102 Grounded Recovery -------------------------------------------------------------------

        private static int JumpAndLand(DuelWorld w)
        {
            AKit.Step(w, 1, AKit.A.With(InputButtons.Jump));
            for (int i = 0; i < 90 && !w.Player(0).Motor.Grounded; i++) AKit.Step(w, 1);
            Assert.True(w.Player(0).Motor.Grounded);
            return w.Tick;
        }

        [Theory]
        [InlineData(1, 2f)]
        [InlineData(2, 4f)]
        [Verifies("AUG-102")]
        public void Grounded_Recovery_Grants_Stamina_After_05_s_On_The_Ground_After_Landing(int rank, float stamina)
        {
            AKit.AssertQuotes(GroundedRecovery.Id, "Remain grounded after landing for 0.5 s.", "Gain 2 stamina, 4 s cooldown.", "Gain 4 stamina, 4 s cooldown.", "Moving above walk interrupts recovery.");
            DuelWorld w = AKit.With(GroundedRecovery.Id, rank);
            DuelWorld control = AKit.Without();
            int land = JumpAndLand(w);
            JumpAndLand(control);
            AKit.Step(w, AKit.Ticks(0.5f));
            AKit.Step(control, AKit.Ticks(0.5f));
            EffectEvent gained = AKit.Events<EffectEvent>(w).Single(e => e.SourceId == GroundedRecovery.Id && e.Change == EffectChange.StaminaGained);
            Assert.Equal(stamina, gained.Amount, 3);
            Assert.Equal(land + AKit.Ticks(0.5f), gained.Tick);
            Assert.Equal(stamina, Stamina(w) - Stamina(control), 3);
        }

        [Fact]
        [Verifies("AUG-102")]
        public void Grounded_Recovery_Is_Interrupted_By_Moving_Above_Walk_And_Has_A_4_s_Cooldown()
        {
            DuelWorld sprint = AKit.With(GroundedRecovery.Id, 2);
            JumpAndLand(sprint);
            AKit.Step(sprint, 10);
            AKit.Step(sprint, 3, SprintInput);
            AKit.Step(sprint, 40, Forward);
            Assert.Empty(AKit.Activations(sprint, GroundedRecovery.Id));

            DuelWorld walk = AKit.With(GroundedRecovery.Id, 2);
            JumpAndLand(walk);
            AKit.Step(walk, 40, Strafe); // walking keeps the recovery
            Assert.Single(AKit.Activations(walk, GroundedRecovery.Id));

            DuelWorld again = AKit.With(GroundedRecovery.Id, 2);
            JumpAndLand(again);
            AKit.Step(again, 35);
            JumpAndLand(again);
            AKit.Step(again, 35);
            Assert.Single(AKit.Activations(again, GroundedRecovery.Id)); // the second landing is inside 4 s
            AKit.Step(again, AKit.Ticks(4f));
            JumpAndLand(again);
            AKit.Step(again, 35);
            Assert.Equal(2, AKit.Activations(again, GroundedRecovery.Id).Count());
        }

        // ----- AUG-104 Inward Step -------------------------------------------------------------------------

        private static ArenaLayout ClosingLayout()
        {
            ArenaLayout layout = TestWorlds.Flat();
            layout.Boundary.ContractStartSec = 1f; // the closing boundary is active from 1 s
            return layout;
        }

        [Theory]
        [InlineData(1, 0f, 1f, 23f)]
        [InlineData(2, 0f, 1f, 21f)]
        [InlineData(2, 1f, 0f, 25f)]
        [InlineData(2, 0f, -1f, 27f)]
        [Verifies("AUG-104")]
        public void Inward_Step_Cheapens_Inward_Dodges_While_The_Boundary_Closes(int rank, float x, float y, float cost)
        {
            AKit.AssertQuotes(InwardStep.Id, "toward center while closing boundary is active", "Cost decreases 2 stamina.", "Cost decreases 4 stamina.", "Outward dodges cost 2 extra stamina.");
            DuelWorld w = AKit.Duel(AKit.Setup("A", AKit.Bram, AKit.Aug(InwardStep.Id, rank)), layout: ClosingLayout());
            AKit.Step(w, AKit.Ticks(1.5f));
            Assert.True(w.CombatSeconds >= w.Layout.Boundary.ContractStartSec);
            AKit.Step(w, 1, WorldDuels.Dodge(0f, x, y)); // player 0 stands at z = -6 facing the center (+Z)
            Assert.True(w.Player(0).Motor.DodgeTicks > 0);
            Assert.Equal(100f - cost, Stamina(w), 3);
        }

        [Fact]
        [Verifies("AUG-104")]
        public void Inward_Step_Gives_No_Discount_Before_The_Boundary_Closes_But_Keeps_The_Outward_Cost()
        {
            DuelWorld early = AKit.With(InwardStep.Id, 2);
            AKit.Step(early, 1, WorldDuels.Dodge(0f, 0f, 1f));
            Assert.Equal(100f - GlobalRules.Movement.DodgeStaminaCost, Stamina(early), 3);

            DuelWorld outward = AKit.With(InwardStep.Id, 2);
            AKit.Step(outward, 1, WorldDuels.Dodge(0f, 0f, -1f));
            Assert.Equal(100f - GlobalRules.Movement.DodgeStaminaCost - 2f, Stamina(outward), 3);
        }

        // ----- AUG-107 Exit Interview ----------------------------------------------------------------------

        private static void SteamAround(DuelWorld w, Vec2 center, float radius)
        {
            WorldDuels.Reactions(w).CreateVolume(new VolumeSpec
            {
                Kind = VolumeKind.Steam,
                Footprint = Footprint.Circle(center, radius),
                BaseY = 0f,
                LifetimeTicks = AKit.Ticks(8f),
            });
            AKit.Step(w, 1);
        }

        [Theory]
        [InlineData(1, 0.03f)]
        [InlineData(2, 0.05f)]
        [Verifies("AUG-107")]
        public void Exit_Interview_Grants_Walk_Speed_For_1_s_After_Walking_Out_Of_Steam(int rank, float bonus)
        {
            AKit.AssertQuotes(ExitInterview.Id, "Leave steam at walking speed.", "Gain 3% walk speed for 1 s, 5 s cooldown.", "Gain 5% walk speed for 1 s, 5 s cooldown.", "Re-entering steam ends buff.");
            DuelWorld w = AKit.With(ExitInterview.Id, rank);
            SteamAround(w, new Vec2(0f, -6f), 1.5f);
            for (int i = 0; i < 60 && !AKit.Activations(w, ExitInterview.Id).Any(); i++) AKit.Step(w, 1, Strafe);
            EffectEvent left = AKit.Activations(w, ExitInterview.Id).Single();
            AKit.Step(w, 10, Strafe);
            Assert.Equal(GlobalRules.Movement.WalkSpeed * (1f + bonus), AKit.Speed(w, 0), 2);
            AKit.Step(w, left.Tick + AKit.Ticks(1f) + 2 - w.Tick, Strafe);
            Assert.Equal(GlobalRules.Movement.WalkSpeed, AKit.Speed(w, 0), 2);
        }

        [Fact]
        [Verifies("AUG-107")]
        public void Exit_Interview_Ends_On_Reentering_Steam_And_Ignores_A_Sprinting_Exit()
        {
            DuelWorld w = AKit.With(ExitInterview.Id, 2);
            SteamAround(w, new Vec2(0f, -6f), 1.5f);
            for (int i = 0; i < 60 && !AKit.Activations(w, ExitInterview.Id).Any(); i++) AKit.Step(w, 1, Strafe);
            PlayerInput back = AKit.Walk(0f, -1f, 0f);
            for (int i = 0; i < 30; i++) AKit.Step(w, 1, back); // walk back into the steam
            AKit.Step(w, 3, AKit.Walk(0f, -1f, 0f));
            Assert.Equal(GlobalRules.Movement.WalkSpeed, AKit.Speed(w, 0), 2);

            DuelWorld sprint = AKit.With(ExitInterview.Id, 2);
            SteamAround(sprint, new Vec2(0f, -6f), 1.5f);
            AKit.Step(sprint, 40, WorldDuels.Sprint(90f));
            Assert.Empty(AKit.Activations(sprint, ExitInterview.Id));
        }

        // ----- AUG-108 Ice Route ---------------------------------------------------------------------------

        private static ArenaLayout IceUnderSpawn()
        {
            ArenaLayout layout = TestWorlds.Flat();
            layout.Materials.Add(WorldDuels.IcePatch(new Vec2(0f, -6f), 8f, 8f));
            return layout;
        }

        [Theory]
        [InlineData(1, 11f)]
        [InlineData(2, 10f)]
        [Verifies("AUG-108")]
        public void Ice_Route_Cuts_The_Sprint_Drain_On_Ice_Only(int rank, float drain)
        {
            AKit.AssertQuotes(IceRoute.Id, "Sprint on ice.", "Sprint cost decreases 1 stamina/s.", "Sprint cost decreases 2 stamina/s.", "Normal ice traction still applies.");
            DuelWorld w = AKit.With(IceRoute.Id, rank, AKit.Bram, IceUnderSpawn());
            AKit.Step(w, 1);
            Assert.Equal(Confluence.Sim.World.SurfaceKind.Ice, w.SurfaceUnder(w.Player(0)).Kind);
            AKit.Step(w, 60, WorldDuels.Sprint(90f));
            Assert.Equal(100f - drain, Stamina(w), 2);

            DuelWorld dry = AKit.With(IceRoute.Id, rank);
            AKit.Step(dry, 60, WorldDuels.Sprint(90f));
            Assert.Equal(100f - GlobalRules.Movement.SprintStaminaPerSecond, Stamina(dry), 2);
        }

        [Fact]
        [Verifies("AUG-108")]
        public void Ice_Route_Keeps_The_Ice_Traction()
        {
            DuelWorld w = AKit.With(IceRoute.Id, 2, AKit.Bram, IceUnderSpawn());
            DuelWorld control = AKit.Without(AKit.Bram, IceUnderSpawn());
            DuelWorld dry = AKit.Without();
            foreach (DuelWorld d in new[] { w, control, dry }) AKit.Step(d, 6, WorldDuels.Sprint(90f));
            Assert.Equal(AKit.Speed(control, 0), AKit.Speed(w, 0), 3);
            Assert.True(AKit.Speed(w, 0) < AKit.Speed(dry, 0) - 1f); // reduced acceleration on ice
        }

        // ----- water under the spawn (AUG-110 drawback) --------------------------------------------------

        private static ArenaLayout WaterUnderSpawn(float half = 6f)
        {
            ArenaLayout layout = TestWorlds.Flat();
            layout.Materials.Add(WorldDuels.WaterBed(new Vec2(0f, -6f), half, half));
            return layout;
        }

        // ----- AUG-110 Sand Route --------------------------------------------------------------------------

        private static ArenaLayout SandUnderSpawn()
        {
            ArenaLayout layout = TestWorlds.Flat();
            layout.Materials.Add(new MaterialPatch { Center = new Vec2(0f, -6f), HalfSize = new Vec2(8f, 8f), Sand = 2, NaturalSource = true });
            return layout;
        }

        [Theory]
        [InlineData(1, 0.03f)]
        [InlineData(2, 0.05f)]
        [Verifies("AUG-110")]
        public void Sand_Route_Speeds_Sprinting_On_Sand(int rank, float bonus)
        {
            AKit.AssertQuotes(SandRoute.Id, "Sprint on sand.", "Gain 3% sprint speed.", "Gain 5% sprint speed.", "Sprint on wet ground costs 1 extra stamina/s.");
            DuelWorld w = AKit.With(SandRoute.Id, rank, AKit.Bram, SandUnderSpawn());
            AKit.Step(w, 20, WorldDuels.Sprint(90f));
            Assert.Equal(GlobalRules.Movement.SprintSpeed * (1f + bonus), AKit.Speed(w, 0), 2);

            DuelWorld stone = AKit.With(SandRoute.Id, rank);
            AKit.Step(stone, 20, WorldDuels.Sprint(90f));
            Assert.Equal(GlobalRules.Movement.SprintSpeed, AKit.Speed(stone, 0), 2);
        }

        [Fact]
        [Verifies("AUG-110")]
        public void Sand_Route_Drawback_Adds_1_Stamina_Per_Second_Sprinting_On_Wet_Ground()
        {
            DuelWorld wet = AKit.With(SandRoute.Id, 1, AKit.Bram, WaterUnderSpawn(9f));
            AKit.Step(wet, 1);
            AKit.Step(wet, 60, WorldDuels.Sprint(90f));
            Assert.Equal(100f - GlobalRules.Movement.SprintStaminaPerSecond - 1f, Stamina(wet), 2);
        }

        // ----- AUG-115 Leapfrog ----------------------------------------------------------------------------

        [Theory]
        [InlineData(1, 8f)]
        [InlineData(2, 6f)]
        [Verifies("AUG-115")]
        public void Leapfrog_Cheapens_A_Jump_Within_04_s_Of_A_Weapon_Hit_Without_Ending_The_Recovery(int rank, float jumpCost)
        {
            AKit.AssertQuotes(Leapfrog.Id, "Jump after a direct weapon hit within 0.4 s.", "Jump costs 2 less stamina.", "Jump costs 4 less stamina.", "recovery still completes");
            DuelWorld w = AKit.With(Leapfrog.Id, rank);
            AKit.FaceOff(w, 1.5f);
            AKit.Strike(w);
            for (int i = 0; i < 20 && AKit.Hits(w, 1, DamageKind.Melee).Count == 0; i++) AKit.Step(w, 1);
            int hit = AKit.Hits(w, 1, DamageKind.Melee).Single().Tick;
            AKit.Step(w, 5);
            float before = Stamina(w);
            AKit.Step(w, 1, AKit.A.With(InputButtons.Jump));
            Assert.True(w.Tick - hit <= AKit.Ticks(0.4f));
            Assert.False(w.Player(0).Motor.Grounded);
            Assert.Equal(jumpCost, before - Stamina(w), 3);
            Assert.True(w.Player(0).Melee.Busy); // the strike's recovery still completes
            Assert.Equal(MeleePhase.Recovery, w.Player(0).Melee.Phase);
        }

        [Fact]
        [Verifies("AUG-115")]
        public void Leapfrog_Gives_Nothing_After_04_s()
        {
            DuelWorld w = AKit.With(Leapfrog.Id, 2);
            AKit.FaceOff(w, 1.5f);
            AKit.Strike(w);
            for (int i = 0; i < 20 && AKit.Hits(w, 1, DamageKind.Melee).Count == 0; i++) AKit.Step(w, 1);
            AKit.Step(w, AKit.Ticks(0.4f) + 1);
            float before = Stamina(w);
            AKit.Step(w, 1, AKit.A.With(InputButtons.Jump));
            Assert.Equal(GlobalRules.Movement.JumpStaminaCost, before - Stamina(w), 3);
        }
    }
}
