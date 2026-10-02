using System;
using System.Collections.Generic;
using Confluence.Core.Arena;
using Confluence.Core.Geometry;
using Confluence.Core.Random;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Reactions.Electricity;
using Confluence.Reactions.Events;
using Confluence.Reactions.Grid;
using Confluence.Reactions.Operations;
using Confluence.Reactions.Volumes;

namespace Confluence.Reactions.Tests
{
    /// <summary>Fixed reaction order (spec L165), canonical queue order, snapshots and state hashes.</summary>
    public class DeterminismTests
    {
        [Fact]
        public void The_Phases_Run_In_The_Global_Order()
        {
            Assert.Equal(
                new[]
                {
                    ReactionPhase.DirectImpacts, ReactionPhase.MaterialConsumption, ReactionPhase.ElectricityConnectivity,
                    ReactionPhase.WindMovement, ReactionPhase.Decay,
                },
                GlobalRules.World.ReactionOrder);
        }

        /// <summary>
        /// Water 2, heat 1, and frost 2 arrive in one cell in the same tick. The documented in-cell order (heat-water
        /// before frost-water, each consuming its inputs first) makes one steam unit and then one ice tile from the
        /// remaining water, whatever order Sim queued the deposits in.
        /// </summary>
        [Fact]
        public void Simultaneous_Deposits_Give_The_Documented_Result_In_Any_Queue_Order()
        {
            var orders = new[] { new[] { 0, 1, 2 }, new[] { 2, 1, 0 }, new[] { 1, 2, 0 } };
            ulong hash = 0;
            foreach (int[] order in orders)
            {
                var h = new Harness();
                Vec3 p = h.CellPoint(24, 24);
                var ops = new[]
                {
                    MaterialDeposit.Field(Material.Water, Footprint.Point(p.XZ()), 0f, 8f, Harness.PlayerB, 1, 2),
                    MaterialDeposit.QuickProjectile(Material.Heat, p, Harness.PlayerA, 2),
                    MaterialDeposit.Field(Material.Frost, Footprint.Point(p.XZ()), 0f, 0f, Harness.PlayerA, 3, 2),
                };
                foreach (int i in order)
                {
                    h.World.Deposit(ops[i]);
                }

                StepResult r = h.Step();
                Assert.Equal(1, r.Count(ReactionKind.Steam));
                Assert.Equal(1, r.Count(ReactionKind.IceFormed));
                Assert.True(h.World.HasIceAt(p));
                Assert.Equal(new CellUnits(), h.World.UnitsAt(p)); // every input consumed exactly once
                Assert.Equal(Harness.PlayerA, h.OnlyVolume().Owner); // heat (source 2) is the later committing source

                ulong now = h.World.StateHash();
                if (hash != 0)
                {
                    Assert.Equal(hash, now);
                }

                hash = now;
            }
        }

        [Fact]
        public void Direct_Impacts_Precede_Consumption_Which_Precedes_Electricity()
        {
            // A heavy impact breaks the tile in phase 1, so heat in the same tick finds liquid-free ground (no melt),
            // and a bolt in the same tick finds no wet cell because the steam reaction consumed the water first.
            var h = new Harness();
            Vec3 p = h.CellPoint(24, 24);
            Vec3 q = h.CellPoint(30, 24);
            h.Field(Material.Water, Footprint.Point(p.XZ()), 1, 8f, Harness.PlayerB, 1);
            h.Field(Material.Water, Footprint.Point(q.XZ()), 1, 8f, Harness.PlayerB, 1);
            h.Step();
            h.Point(Material.Frost, p, 2, 0, Harness.PlayerB, 2);
            h.Step();
            Assert.True(h.World.HasIceAt(p));

            h.World.Impact(new DirectImpact { Point = p, Direction = Vec3.Right, DirectDamage = 30f, SourceEventId = 10 });
            h.Point(Material.Heat, p, 2, 0, Harness.PlayerA, 11);
            h.Point(Material.Heat, q, 1, 0, Harness.PlayerA, 12);
            h.World.Discharge(new ElectricDischarge { Point = q, ReactionDamage = 10f, SourceEventId = 13 });
            StepResult r = h.Step();
            Assert.Equal(1, r.Count(ReactionKind.IceShattered));
            Assert.Equal(0, r.Count(ReactionKind.IceMelted));
            Assert.Equal(1, r.Count(ReactionKind.Steam));
            Assert.Equal(0, r.Count(ReactionKind.WetCharge));
        }

        [Fact]
        public void Same_Operations_In_Shuffled_Queue_Order_Give_Identical_Hashes_And_Events_Over_Many_Ticks()
        {
            var a = new Harness();
            var b = new Harness();
            a.Bodies.AddRange(Bodies);
            b.Bodies.AddRange(Bodies);
            var rng = new Rng(12345UL);
            var shuffle = new Rng(999UL);
            int events = 0;
            int damage = 0;
            for (int tick = 0; tick < 600; tick++)
            {
                List<Action<ReactionWorld>> ops = RandomScenario.Ops(rng, a.Grid, tick);
                foreach (Action<ReactionWorld> op in ops)
                {
                    op(a.World);
                }

                for (int i = ops.Count - 1; i > 0; i--)
                {
                    int j = shuffle.NextInt(i + 1);
                    Action<ReactionWorld> t = ops[i];
                    ops[i] = ops[j];
                    ops[j] = t;
                }

                foreach (Action<ReactionWorld> op in ops)
                {
                    op(b.World);
                }

                StepResult ra = a.Step();
                string eventsA = RandomScenario.Describe(ra);
                events += ra.Events.Count;
                damage += ra.Damage.Count;
                StepResult rb = b.Step();
                Assert.Equal(eventsA, RandomScenario.Describe(rb));
                Assert.Equal(a.World.StateHash(), b.World.StateHash());
            }

            Assert.True(events > 200 && damage > 0, "the scenario should produce reactions and contact damage");
        }

        [Fact]
        public void A_Restored_Snapshot_Continues_Identically()
        {
            ArenaLayout layout = TestArenas.Empty(biomeId: "BIO-001");
            layout.Materials.Add(TestArenas.Bed(new Vec2(-5f, 0f), 0.5f, 1f, water: 1, refillRule: "rain"));
            layout.Materials.Add(TestArenas.Bed(new Vec2(5f, 0f), 0.5f, 1f, water: 1, refillRule: "rain"));
            var live = new Harness(new ReactionWorld(layout));
            var rng = new Rng(77UL);
            for (int tick = 0; tick < 400; tick++)
            {
                foreach (Action<ReactionWorld> op in RandomScenario.Ops(rng, live.Grid, tick))
                {
                    op(live.World);
                }

                live.Step();
            }

            ReactionSnapshot snapshot = ReactionSnapshot.FromBytes(live.World.CaptureSnapshot().ToBytes());
            Assert.Equal(live.World.StateHash(), snapshot.Hash);
            Assert.Equal(live.World.ActiveCellCount, snapshot.ActiveCellCount);
            Assert.Equal(live.World.Volumes.Count, snapshot.VolumeCount);

            var copy = new ReactionWorld(layout);
            Assert.NotEqual(live.World.StateHash(), copy.StateHash());
            copy.RestoreSnapshot(snapshot);
            Assert.Equal(live.World.StateHash(), copy.StateHash());
            Assert.Equal(live.World.Tick, copy.Tick);
            for (int tick = 400; tick < 900; tick++)
            {
                List<Action<ReactionWorld>> ops = RandomScenario.Ops(rng, live.Grid, tick);
                foreach (Action<ReactionWorld> op in ops)
                {
                    op(live.World);
                    op(copy);
                }

                string expected = RandomScenario.Describe(live.World.Step(tick, Bodies));
                Assert.Equal(expected, RandomScenario.Describe(copy.Step(tick, Bodies)));
                Assert.Equal(live.World.StateHash(), copy.StateHash());
            }
        }

        [Fact]
        public void The_Hash_Changes_With_State_And_Ignores_Rebuildable_Bookkeeping()
        {
            var h = new Harness();
            ulong empty = h.World.StateHash();
            Assert.Equal(empty, new Harness().World.StateHash());
            h.Point(Material.Water, h.CellPoint(3, 3), 1, 600);
            h.Step();
            ulong wet = h.World.StateHash();
            Assert.NotEqual(empty, wet);
            Assert.Equal(wet, h.World.StateHash()); // stable between steps
        }

        [Fact]
        public void Inactive_Cells_Keep_No_Deposit_Surface_Height()
        {
            var h = new Harness();
            Vec3 floor = h.CellPoint(24, 24);
            MaterialDeposit high = MaterialDeposit.Field(Material.Venom, Footprint.Point(floor.XZ()), 2f, 0.2f, Harness.PlayerA, 1);
            high.DeclaredDamagePerSecond = 5f;
            h.World.Deposit(high); // residue on a 2 m platform edge above this cell
            h.StepFor(20);
            Assert.Equal(0, h.World.ActiveCellCount);

            MaterialDeposit low = MaterialDeposit.Field(Material.Venom, Footprint.Point(floor.XZ()), 0f, 2f, Harness.PlayerA, 2);
            low.DeclaredDamagePerSecond = 5f;
            h.World.Deposit(low);
            h.Bodies.Add(ReactionBody.Player(1, floor));
            Assert.Equal(5f, h.Step().DamageTo(1)); // the new residue lies on the floor, not on the old height
        }

        [Fact]
        public void Snapshots_Carry_Gravity_Deflection_History()
        {
            var field = new Queries.GravityFieldDef { Id = 1, Center = Vec3.Zero, Radius = 2f };
            var live = new ReactionWorld(CellGrid.ForDiameter(Vec2.Zero, 24f));
            live.GravityFields.Register(field);
            Assert.True(live.GravityFields.TryDeflect(7, new Vec3(1f, 0f, 0f), new Vec3(0f, 0f, 20f), Queries.ProjectileClass.Physical, 0, out _, out _));
            live.Step(0, null);

            var copy = new ReactionWorld(CellGrid.ForDiameter(Vec2.Zero, 24f));
            copy.GravityFields.Register(field);
            copy.RestoreSnapshot(live.CaptureSnapshot());
            Assert.Equal(live.GravityFields.TotalDeflectionDeg(7), copy.GravityFields.TotalDeflectionDeg(7));
            Assert.False(copy.GravityFields.TryDeflect(7, new Vec3(1f, 0f, 0f), new Vec3(0f, 0f, 20f), Queries.ProjectileClass.Physical, 1, out _, out _));
            Assert.Equal(live.StateHash(), copy.StateHash());
        }

        // ------------------------------------------------------------------ helpers

        private static readonly List<ReactionBody> Bodies = new List<ReactionBody>
        {
            ReactionBody.Player(0, new Vec3(-2f, 0f, 1f)),
            ReactionBody.Player(1, new Vec3(3f, 0f, -2f)),
            ReactionBody.Player(2, new Vec3(0.25f, 0.8f, 0.25f), false),
        };
    }
}
