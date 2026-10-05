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

        /// <summary>A 40 m floor with a circular boundary and two open spawns 12 m apart.</summary>
        private static ArenaLayout FlatLayout()
        {
            var layout = new ArenaLayout
            {
                Seed = 12345UL,
                Diameter = 40,
                Center = Vec3.Zero,
                BiomeId = "BIO-001",
                Boundary = BoundaryContour.Circle(Vec3.Zero, 18f),
            };
            layout.Solids.Add(new Solid { Id = 1, Kind = SolidKind.Floor, Box = YawBox.OnFloor(new Vec3(0f, -1f, 0f), new Vec3(40f, 1f, 40f), 0f) });
            layout.Spawns = new[]
            {
                new SpawnPoint { Side = 0, Position = new Vec3(0f, 0f, -6f), YawDeg = 0f, Alcove = new YawBox(new Vec3(0f, 1.5f, -6f), new Vec3(2f, 1.5f, 2f), 0f) },
                new SpawnPoint { Side = 1, Position = new Vec3(0f, 0f, 6f), YawDeg = 180f, Alcove = new YawBox(new Vec3(0f, 1.5f, 6f), new Vec3(2f, 1.5f, 2f), 180f) },
            };
            return layout;
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

        /// <summary>
        /// Record markers (augments-a's AUG-043 trail: "Trail helps opponent track casting direction") and a kit's world cues
        /// (WIZ-010's TideEvent) are public: the opponent receives them, not only their owner.
        /// </summary>
        [Fact]
        public void A_Record_Marker_And_A_Kit_Cue_Reach_The_Opponent()
        {
            ArenaLayout layout = FlatLayout(); // a clear line between the spawns
            DuelPlayerSetup halen = DuelPlayerSetup.FromCatalogue(GameRig.Catalogue, "A", "WIZ-010",
                augments: new[] { new ActiveAugment("AUG-043", 1) });
            DuelPlayerSetup bram = DuelPlayerSetup.FromCatalogue(GameRig.Catalogue, "B", "WIZ-002");
            DuelWorld world = new SimWorldFactory(GameRig.Catalogue).Create(layout, halen, bram, 0, 5400, true);
            PlayerInput idle = PlayerInput.Idle(180f);

            // A Pressure Jet aimed 1 m beside Bram's body (an untransformed near miss), then a Water Hammer at the air.
            Vec3 beside = world.Player(1).Position + new Vec3(1.0f, 1.1f, 0f) - world.Player(0).Eye;
            PlayerInput jet = PlayerInput.Idle(0f).Facing(CMath.YawOf(beside), CMath.PitchOf(beside));
            jet.SelectedSlot = 0;
            for (int k = 0; k < 24; k++) world.Step(jet.With(InputButtons.Primary), idle);
            for (int k = 0; k < 90; k++) world.Step(jet, idle);
            PlayerInput hammer = PlayerInput.Idle(0f).Facing(CMath.YawOf(-beside), 0f);
            hammer.SelectedSlot = 1;
            for (int k = 0; k < 24; k++) world.Step(hammer.With(InputButtons.Primary), idle);
            for (int k = 0; k < 90; k++) world.Step(hammer, idle);

            Confluence.Sim.Effects.MarkerEvent trail = world.EventLog.OfType<Confluence.Sim.Effects.MarkerEvent>().FirstOrDefault(m => m.SourceId == "AUG-043");
            Assert.NotNull(trail);
            Confluence.Net.Game.Wire.WireDuelEvent seen = Confluence.Net.Game.Server.DuelEventFilter.Convert(trail, 1);
            Assert.NotNull(seen);
            Assert.Equal(Confluence.Net.Game.Wire.WireEventType.Marker, seen.Type);
            Assert.Equal("AUG-043", seen.SourceId);
            Assert.True(Vec3.Distance(trail.To, seen.Position + seen.Direction) < 1e-3f);
            Assert.Equal(trail.DurationTicks, seen.Extra);

            TideEvent miss = world.EventLog.OfType<TideEvent>().FirstOrDefault(e => e.Kind == TideEventKind.MissRecovery);
            Assert.NotNull(miss);
            Confluence.Net.Game.Wire.WireDuelEvent cue = Confluence.Net.Game.Server.DuelEventFilter.Convert(miss, 1);
            Assert.NotNull(cue);
            Assert.Equal(Confluence.Net.Game.Wire.WireEventType.Cue, cue.Type);
            Assert.Equal((byte)TideEventKind.MissRecovery, cue.Kind);
            Assert.Equal(WaterHammer.Id, cue.SourceId);

            // Both survive the wire round trip.
            foreach (Confluence.Net.Game.Wire.WireDuelEvent e in new[] { seen, cue })
            {
                var w = new Confluence.Net.Serialization.NetWriter(256);
                e.Write(w);
                var back = Confluence.Net.Game.Wire.WireDuelEvent.Read(new Confluence.Net.Serialization.NetReader(w.ToArray(), 0, w.Length));
                Assert.Equal(e.Type, back.Type);
                Assert.Equal(e.SourceId, back.SourceId);
            }
        }
    }
}
