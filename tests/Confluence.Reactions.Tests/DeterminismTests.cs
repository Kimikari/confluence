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
            for (int tick = 0; tick < 600; tick++)
            {
                List<Action<ReactionWorld>> ops = RandomOps(rng, a.Grid, tick);
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
                string eventsA = Describe(ra);
                StepResult rb = b.Step();
                Assert.Equal(eventsA, Describe(rb));
                Assert.Equal(a.World.StateHash(), b.World.StateHash());
            }
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
                foreach (Action<ReactionWorld> op in RandomOps(rng, live.Grid, tick))
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
                List<Action<ReactionWorld>> ops = RandomOps(rng, live.Grid, tick);
                foreach (Action<ReactionWorld> op in ops)
                {
                    op(live.World);
                    op(copy);
                }

                string expected = Describe(live.World.Step(tick, Bodies));
                Assert.Equal(expected, Describe(copy.Step(tick, Bodies)));
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

        // ------------------------------------------------------------------ helpers

        private static readonly List<ReactionBody> Bodies = new List<ReactionBody>
        {
            ReactionBody.Player(0, new Vec3(-2f, 0f, 1f)),
            ReactionBody.Player(1, new Vec3(3f, 0f, -2f)),
            ReactionBody.Player(2, new Vec3(0.25f, 0.8f, 0.25f), false),
        };

        /// <summary>A deterministic mix of every operation type for one tick.</summary>
        private static List<Action<ReactionWorld>> RandomOps(Rng rng, CellGrid grid, int tick)
        {
            var ops = new List<Action<ReactionWorld>>();
            int count = rng.NextInt(4);
            for (int k = 0; k < count; k++)
            {
                int source = tick * 10 + k + 1;
                int owner = rng.NextInt(2);
                Vec2 at = grid.CellCenter(rng.NextInt(8, grid.Width - 8), rng.NextInt(8, grid.Depth - 8));
                Vec3 at3 = at.ToXZ();
                switch (rng.NextInt(9))
                {
                    case 0:
                    case 1:
                    {
                        var m = (Material)rng.NextInt(Materials.Count);
                        int strength = 1 + rng.NextInt(3);
                        float radius = 0.5f + rng.NextInt(4) * 0.5f;
                        int life = rng.NextInt(400);
                        float dps = rng.NextInt(3) * 2f;
                        ops.Add(w =>
                        {
                            MaterialDeposit d = MaterialDeposit.Field(m, Footprint.Circle(at, radius), 0f, life / 60f, owner, source, strength, source);
                            d.DeclaredDamagePerSecond = dps;
                            d.Soluble = m == Material.Venom;
                            w.Deposit(d);
                        });
                        break;
                    }
                    case 2:
                    {
                        var m = rng.NextBool() ? Material.Heat : Material.Frost;
                        ops.Add(w => w.Deposit(MaterialDeposit.HeavyProjectile(m, at3, owner, source)));
                        break;
                    }
                    case 3:
                    {
                        float damage = 5f + rng.NextInt(30);
                        ops.Add(w => w.Impact(new DirectImpact { Point = at3, Direction = Vec3.Right, DirectDamage = damage, Owner = owner, SourceEventId = source }));
                        break;
                    }
                    case 4:
                    {
                        float damage = 4f + rng.NextInt(10);
                        ops.Add(w => w.Discharge(new ElectricDischarge { Point = at3, ReactionDamage = damage, Owner = owner, SourceEventId = source }));
                        break;
                    }
                    case 5:
                    {
                        int strength = 1 + rng.NextInt(3);
                        Vec2 dir = new Vec2(rng.NextFloat() - 0.5f, rng.NextFloat() - 0.5f);
                        int duration = 1 + rng.NextInt(60);
                        ops.Add(w => w.Wind(new WindGust { Direction = dir, Strength = strength, DurationTicks = duration, Owner = owner, SourceEventId = source }));
                        break;
                    }
                    case 6:
                    {
                        var kind = (VolumeKind)rng.NextInt(9);
                        int life = 30 + rng.NextInt(400);
                        ops.Add(w => w.CreateVolume(new VolumeSpec { Kind = kind, Footprint = Footprint.Circle(at, 1.5f), LifetimeTicks = life, Owner = owner, SourceEventId = source, DamagePerSecond = kind == VolumeKind.VenomCloud ? 4f : 0f, Soluble = true }));
                        break;
                    }
                    case 7:
                        ops.Add(w => w.Dispel(new DispelRequest { Area = Footprint.Circle(at, 1f), Owner = owner, SourceEventId = source }));
                        break;
                    default:
                        ops.Add(w => w.RaiseDust(new DustRaise { Footprint = Footprint.Circle(at, 1f), LifetimeTicks = 120, Owner = owner, SourceEventId = source }));
                        break;
                }
            }

            return ops;
        }

        private static string Describe(StepResult r)
        {
            var parts = new List<string>();
            foreach (ReactionEvent e in r.Events) parts.Add(e.ToString() + " @" + e.CellIndex);
            foreach (ContactDamage d in r.Damage) parts.Add(d.ToString());
            foreach (VolumeEvent v in r.VolumeEvents) parts.Add(v.ToString());
            foreach (PropImpulse p in r.PropImpulses) parts.Add(p.PropId + ":" + p.Distance);
            return string.Join("|", parts);
        }
    }
}
