using System.Collections.Generic;
using Confluence.Client.Arena;
using Confluence.Client.Laboratory;
using Confluence.Client.Presentation;
using Confluence.Client.Settings;
using Confluence.Core.Arena;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Reactions;
using Confluence.Reactions.Grid;
using Confluence.Reactions.Operations;
using Confluence.Reactions.Volumes;
using Confluence.Sim.Duel;
using Confluence.Sim.Players;
using NUnit.Framework;
using UnityEngine;
using Material = Confluence.Reactions.Material;

namespace Confluence.Client.Tests
{
    /// <summary>
    /// Confluence.Reactions state drawn by the client: the generated arena's own environment over the preview clock
    /// (BIO-001 rainfall, spec L335), cell tiles, and obscuration volumes (spec L134, L144, L167).
    /// </summary>
    public sealed class ReactionPresentationTests
    {
        private static Catalogue Cat => TestCatalogue.Instance;

        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in _created)
            {
                if (o != null)
                {
                    Object.DestroyImmediate(o);
                }
            }

            _created.Clear();
        }

        [Test]
        public void EnvironmentFollowsThePreviewClockAndReplaysWhenScrubbedBack()
        {
            var environment = new ArenaEnvironment(TestArenas.Raincourt32.Layout);
            Assert.AreEqual(ArenaEnvironment.AlcoveTicks, ArenaEnvironment.TickAt(0f), "combat starts after the 5 s alcove (spec L46-L47)");
            Assert.AreEqual(0, ArenaEnvironment.TickAt(-5f));

            Advance(environment, 0f);
            int atStart = WaterUnits(environment.World);
            Assert.Greater(atStart, 0, "the gutter beds start with natural water");

            Advance(environment, 20f);
            int later = WaterUnits(environment.World);
            Assert.Greater(later, atStart, "the 8 s rainfall pulse adds a unit to the gutters (BIO-001)");
            StringAssert.Contains("water " + later, environment.Summary());

            Advance(environment, 0f);
            Assert.AreEqual(ArenaEnvironment.TickAt(0f), environment.Tick, "scrubbing back replays from the first tick");
            Assert.AreEqual(atStart, WaterUnits(environment.World), "the replay is deterministic");
        }

        [Test]
        public void CellTilesGrowWithTheGutterWater()
        {
            ArenaLayout layout = TestArenas.Raincourt32.Layout;
            ReactionViews views = CreateViews(layout);
            var environment = new ArenaEnvironment(layout);
            Advance(environment, 0f);
            views.Sync(environment.World);
            int cells = views.CountOf(ReactionViews.CellLook.Water);
            Assert.Greater(cells, 0, "wet gutter cells are drawn");
            Assert.AreEqual(cells, views.Root.Find("Cells Water").GetComponent<MeshFilter>().sharedMesh.vertexCount / 4, "one tile per cell");
            float before = TileWidth(views);

            Advance(environment, 20f);
            views.Sync(environment.World);
            Assert.AreEqual(cells, views.CountOf(ReactionViews.CellLook.Water), "the same gutters");
            Assert.Greater(TileWidth(views), before, "more units draw a larger tile");

            views.Sync(null);
            Assert.AreEqual(0, views.CellCount);
            Assert.IsNull(views.Root.Find("Cells Water"));
        }

        [Test]
        public void SteamIsDrawnAsItsColumnsAndDisappearsWhenItExpires()
        {
            ArenaLayout layout = TestArenas.Raincourt32.Layout;
            ReactionViews views = CreateViews(layout);
            var world = new ReactionWorld(layout, 0);
            Vec2 spot = layout.Center.XZ() + new Vec2(2.25f, 0.25f);
            int tick = 0;
            world.Deposit(MaterialDeposit.Field(Material.Water, Footprint.Circle(spot, 0.75f), 0f, 6f, 0, 1, 2));
            world.Step(tick++, System.Array.Empty<ReactionBody>());
            world.Deposit(MaterialDeposit.Field(Material.Heat, Footprint.Circle(spot, 0.75f), 0f, 0f, 1, 2, 1));
            world.Step(tick++, System.Array.Empty<ReactionBody>());
            Assert.AreEqual(1, world.Volumes.Count, "heat on water makes steam (spec L144)");
            ObscurationVolume steam = world.Volumes[0];
            Assert.AreEqual(VolumeKind.Steam, steam.Kind);

            views.Sync(world);
            Assert.AreEqual(1, views.VolumeCount);
            Mesh mesh = views.FindVolume(steam.Id).GetComponent<MeshFilter>().sharedMesh;
            Assert.AreEqual(steam.BaseY, mesh.bounds.min.y, 1e-3f);
            Assert.AreEqual(steam.TopY, mesh.bounds.max.y, 1e-3f, "the cloud rises to its stated height (spec L167)");
            Vec2 c = steam.ColumnCenter(steam.Columns.MinX, steam.Columns.MinZ);
            Assert.LessOrEqual(mesh.bounds.min.x, c.X - CellGrid.CellSize * 0.5f + 1e-3f, "columns are 0.5 m wide");

            for (int i = 0; i < Ticks.FromSeconds(GlobalRules.World.SteamLifetimeSec) + 2; i++)
            {
                world.Step(tick++, System.Array.Empty<ReactionBody>());
            }

            views.Sync(world);
            Assert.AreEqual(0, world.Volumes.Count);
            Assert.AreEqual(0, views.VolumeCount, "an expired cloud is removed");
        }

        [Test]
        public void TheOfflineDuelDrawsAReactionWorldOnlyWhenItHasOne()
        {
            ArenaLayout layout = SparringLayout.Create(TestArenas.Raincourt32.Layout);
            ArenaRenderer renderer = ArenaRenderer.Create(layout, Cat);
            _created.Add(renderer.gameObject);
            var world = new DuelWorld(layout, DuelPlayerSetup.FromCatalogue(Cat, "p0", "WIZ-002"), DuelPlayerSetup.FromCatalogue(Cat, "p1", "WIZ-010"));
            OfflineDuel duel = OfflineDuel.Create(null, renderer, world, Cat, new ClientSettings(new MemorySettingsStore()), new TrainingDummyPilot(DummyMode.Idle),
                LaboratorySession.Label, "keys");
            _created.Add(duel.gameObject);
            duel.FastForward(1);
            Assert.IsNull(duel.ReactionState, "the Sim's materials are a null seam: nothing to draw");

            var reactions = new ReactionWorld(layout, world.AlcoveTicks);
            reactions.Step(0, System.Array.Empty<ReactionBody>());
            duel.Reactions = reactions;
            duel.FastForward(1);
            Assert.IsNotNull(duel.ReactionState);
            Assert.Greater(duel.ReactionState.CountOf(ReactionViews.CellLook.Water), 0, "the map's wet gutters appear");

            duel.Reactions = null;
            duel.FastForward(1);
            Assert.AreEqual(0, duel.ReactionState.CellCount);
        }

        private ReactionViews CreateViews(ArenaLayout layout)
        {
            ArenaRenderer renderer = ArenaRenderer.Create(layout, Cat);
            _created.Add(renderer.gameObject);
            var views = new ReactionViews(null, renderer.Materials, layout);
            _created.Add(views.Root.gameObject);
            return views;
        }

        private static void Advance(ArenaEnvironment environment, float seconds)
        {
            for (int guard = 0; guard < 20 && !environment.AdvanceTo(seconds); guard++)
            {
            }

            Assert.AreEqual(ArenaEnvironment.TickAt(seconds), environment.Tick);
        }

        private static int WaterUnits(ReactionWorld world)
        {
            int units = 0;
            foreach (int index in world.ActiveCells)
            {
                units += world.UnitsAtCell(index).Water;
            }

            return units;
        }

        // Width of the first water tile (tiles grow with the unit count).
        private static float TileWidth(ReactionViews views)
        {
            Mesh mesh = views.Root.Find("Cells Water").GetComponent<MeshFilter>().sharedMesh;
            Vector3[] v = mesh.vertices;
            return v[2].x - v[0].x;
        }
    }
}
