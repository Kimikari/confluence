using System.Linq;
using Confluence.Core.Arena;
using Confluence.Core.Geometry;
using Confluence.Hosting.Arenas;
using Confluence.Hosting.Duels;
using Confluence.Net.Game.Snapshots;
using Confluence.Net.Tests.TestSupport;
using Confluence.Sim.Content.Wizards.HalenTide;
using Confluence.Sim.Duel;
using Confluence.Sim.Input;
using Confluence.Sim.Players;
using Confluence.Sim.Spells;
using InputButtons = Confluence.Casting.InputButtons;

namespace Confluence.Net.Tests.Game
{
    /// <summary>
    /// Stage 2 merge: spell entities of the merged kits reach the snapshot as what they are (spec L419 "spell entities").
    /// WIZ-010's Pressure Jet flies as its own TideProjectileEntity; it is captured as a positioned projectile (its own
    /// position, velocity and radius), never as an owner-attached entry drawn on the caster.
    /// </summary>
    public sealed class KitEntityCaptureTests
    {
        private static DuelWorld World()
        {
            var recipe = new ArenaRecipe(11UL, 1, 24, "BIO-001", new string[0]);
            ArenaLayout layout = GameRig.Generator.Generate(recipe.ToGeneratorRequest(GameRig.Catalogue)).Layout;
            DuelPlayerSetup halen = DuelPlayerSetup.FromCatalogue(GameRig.Catalogue, "A", "WIZ-010");
            DuelPlayerSetup bram = DuelPlayerSetup.FromCatalogue(GameRig.Catalogue, "B", "WIZ-002");
            return new SimWorldFactory(GameRig.Catalogue).Create(layout, halen, bram, 0, 5400, true);
        }

        [Fact]
        public void A_Pressure_Jet_In_Flight_Is_Captured_As_A_Positioned_Projectile()
        {
            DuelWorld world = World();
            Assert.Equal(DuelPhase.Combat, world.Phase);
            Vec3 aim = world.Player(1).Position + new Vec3(0f, 1.1f, 0f) - world.Player(0).Eye;
            PlayerInput halen = PlayerInput.Idle(0f).Facing(CMath.YawOf(aim), CMath.PitchOf(aim));
            halen.SelectedSlot = 0; // SPL-073 Pressure Jet
            PlayerInput bram = PlayerInput.Idle(180f);
            for (int k = 0; k < 24; k++) world.Step(halen.With(InputButtons.Primary), bram); // pressure 54: a normal release
            TideProjectileEntity jet = null;
            for (int k = 0; k < 60 && jet == null; k++)
            {
                world.Step(halen, bram);
                jet = ((SpellRuntime)world.Spells).Entities.OfType<TideProjectileEntity>().FirstOrDefault(e => e.Alive);
            }

            Assert.NotNull(jet);
            DuelFrame frame = FrameCapture.Capture(world, 1, world.Tick);
            EntityEntry entry = Assert.Single(frame.Entities, e => e.Key == jet.Id);
            Assert.Equal(EntityKind.Projectile, entry.Kind);
            Assert.False(EntityEntry.IsOwnerAttached(entry.Kind));
            Assert.Equal(PressureJet.Id, entry.SpellId);
            Assert.True(Vec3.Distance(jet.Position, entry.PositionMeters) < 0.05f, "captured at " + entry.PositionMeters + ", flying at " + jet.Position);
            Assert.True(Vec3.Distance(world.Player(0).Position, entry.PositionMeters) > 0.5f);
            Assert.Equal((int)System.Math.Round(jet.Spec.Radius * 100f), entry.RadiusCm);
        }
    }
}
