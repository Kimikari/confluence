using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Confluence.Core.Geometry;
using Confluence.Core.Random;
using Confluence.Core.Rules;
using Confluence.Hosting.Duels;
using Confluence.Net.Game.Headless;
using Confluence.Net.Game.Server;
using Confluence.Net.Game.Snapshots;
using Confluence.Net.Tests.TestSupport;
using Confluence.Reactions;
using Confluence.Reactions.Grid;
using Confluence.Reactions.Operations;
using Confluence.Sim.World.Materials;
using Xunit.Abstractions;

namespace Confluence.Net.Tests.Game
{
    /// <summary>The incremental cell capture (research §2.4.5) reproduces the full grid scan on every capture.</summary>
    public sealed class CellCaptureTests
    {
        private readonly ITestOutputHelper _output;

        public CellCaptureTests(ITestOutputHelper output)
        {
            _output = output;
        }

        /// <summary>Compares one incremental capture with the full scan and tracks how the cell set moved.</summary>
        private sealed class Comparer
        {
            private readonly List<CellEntry> _full = new List<CellEntry>();
            private readonly List<CellEntry> _incremental = new List<CellEntry>();
            private readonly Stopwatch _watch = new Stopwatch();
            private Dictionary<int, CellEntry> _last = new Dictionary<int, CellEntry>();

            public CellCapture Tracker { get; } = new CellCapture();
            public int Compared, MaxCells, Appeared, Vanished, Changed;
            public long FullTicks, IncrementalTicks;

            public void Compare(ReactionWorld reactions, int tick)
            {
                _full.Clear();
                _incremental.Clear();
                _watch.Restart();
                FrameCapture.CaptureCells(reactions, _full);
                FullTicks += _watch.ElapsedTicks;
                _watch.Restart();
                Tracker.Capture(reactions, _incremental);
                IncrementalTicks += _watch.ElapsedTicks;

                Assert.Equal(_full.Count, _incremental.Count);
                var now = new Dictionary<int, CellEntry>();
                for (int i = 0; i < _full.Count; i++)
                {
                    Assert.Equal(_full[i].Key, _incremental[i].Key);
                    Assert.True(_full[i].SameAs(_incremental[i]), "cell " + _full[i].Key + " differs at tick " + tick);
                    now[_full[i].Key] = _full[i];
                    if (!_last.TryGetValue(_full[i].Key, out CellEntry old)) Appeared++;
                    else if (!old.SameAs(_full[i])) Changed++;
                }

                foreach (int key in _last.Keys)
                    if (!now.ContainsKey(key)) Vanished++;
                _last = now;
                MaxCells = Math.Max(MaxCells, _full.Count);
                Compared++;
            }

            public string Describe()
            {
                double perFull = FullTicks * 1000.0 / Stopwatch.Frequency / Math.Max(1, Compared);
                double perInc = IncrementalTicks * 1000.0 / Stopwatch.Frequency / Math.Max(1, Compared);
                long scans = Tracker.FullScans + Tracker.IncrementalScans;
                return "compared " + Compared + " captures; max cells " + MaxCells + "; appeared " + Appeared + " vanished " + Vanished + " changed " + Changed +
                       "; full scan " + perFull.ToString("F3") + " ms, incremental " + perInc.ToString("F3") + " ms per capture; cells queried per capture " +
                       (Tracker.CellsQueried / (double)Math.Max(1, scans)).ToString("F0");
            }
        }

        [Fact]
        public void Incremental_Capture_Equals_The_Full_Scan_Under_Every_Reaction_Operation()
        {
            // Every tick and every second tick (the 30 Hz send cadence, cells can come and go between captures).
            var world = new ReactionWorld(CellGrid.ForDiameter(Vec2.Zero, 32f));
            var everyTick = new Comparer();
            var everyOther = new Comparer();
            var rng = new Rng(9001UL);
            var bodies = new List<ReactionBody> { ReactionBody.Player(0, new Vec3(-2f, 0f, 1f)), ReactionBody.Player(1, new Vec3(3f, 0f, -2f)) };
            CellGrid grid = world.Grid;
            for (int tick = 0; tick < 4000; tick++)
            {
                // Bursts of activity separated by quiet stretches, so whole regions decay to empty.
                bool active = (tick / 600) % 2 == 0;
                int count = active ? rng.NextInt(4) : 0;
                for (int k = 0; k < count; k++)
                {
                    int source = tick * 10 + k + 1;
                    int owner = rng.NextInt(2);
                    Vec2 at = grid.CellCenter(rng.NextInt(6, grid.Width - 6), rng.NextInt(6, grid.Depth - 6));
                    Vec3 at3 = at.ToXZ();
                    switch (rng.NextInt(8))
                    {
                        case 0:
                        case 1:
                        {
                            var m = (Material)rng.NextInt(Materials.Count);
                            MaterialDeposit d = MaterialDeposit.Field(m, Footprint.Circle(at, 0.5f + rng.NextInt(4) * 0.5f), 0f, rng.NextInt(300) / 60f, owner, source,
                                1 + rng.NextInt(3), source);
                            d.Soluble = m == Material.Venom;
                            world.Deposit(d);
                            break;
                        }
                        case 2:
                            world.Deposit(MaterialDeposit.HeavyProjectile(rng.NextBool() ? Material.Heat : Material.Frost, at3, owner, source));
                            break;
                        case 3:
                            world.Deposit(MaterialDeposit.QuickProjectile((Material)rng.NextInt(Materials.Count), at3, owner, source, rng.NextInt(30)));
                            break;
                        case 4:
                            world.Impact(new DirectImpact { Point = at3, Direction = Vec3.Right, DirectDamage = 5f + rng.NextInt(30), Owner = owner, SourceEventId = source });
                            break;
                        case 5:
                            world.Discharge(new ElectricDischarge { Point = at3, ReactionDamage = 4f + rng.NextInt(10), Owner = owner, SourceEventId = source });
                            break;
                        case 6:
                            world.RemoveUnits(new UnitRemoval { Footprint = Footprint.Circle(at, 1f), Owner = owner, SourceEventId = source });
                            break;
                        default:
                            world.RaiseDust(new DustRaise { Footprint = Footprint.Circle(at, 1f), LifetimeTicks = 120, Owner = owner, SourceEventId = source });
                            break;
                    }
                }

                world.Step(tick, bodies);
                everyTick.Compare(world, tick);
                if (tick % 2 == 0) everyOther.Compare(world, tick);
            }

            _output.WriteLine("every tick: " + everyTick.Describe());
            _output.WriteLine("every other tick: " + everyOther.Describe());
            foreach (Comparer c in new[] { everyTick, everyOther })
            {
                Assert.True(c.Appeared > 50 && c.Vanished > 50 && c.Changed > 50, c.Describe());
                Assert.True(c.Tracker.IncrementalScans > c.Tracker.FullScans * 10);
            }
        }

        [Fact]
        public void Incremental_Capture_Equals_The_Full_Scan_Every_Tick_Of_Networked_Duels()
        {
            var settings = new GameServerSettings
            {
                Mode = MatchMode.Duel,
                PlayerCount = 2,
                Seed = EndToEndTests.SeedWithFirstDiameter(MatchMode.Duel, 24, 300),
                Laboratory = true,
                ArenaPrefetchRounds = 2,
            };
            using (var rig = new GameRig(settings))
            {
                HeadlessPlayer a = rig.AddPlayer("Ash", "WIZ-002", seed: 1);
                HeadlessPlayer b = rig.AddPlayer("Brook", "WIZ-010", seed: 2);
                var comparers = new Dictionary<int, Comparer>();

                // Two complete duels: the third duel id appears, or the match ends.
                bool done = rig.RunUntil(() =>
                {
                    if (rig.Server.Host == null) return false;
                    foreach (RunningDuel duel in rig.Server.Host.Duels.Running)
                    {
                        ReactionWorld reactions = (duel.World.Materials as ReactionMaterials)?.World;
                        if (reactions == null) continue;
                        if (!comparers.TryGetValue(duel.DuelId, out Comparer c)) comparers[duel.DuelId] = c = new Comparer();
                        c.Compare(reactions, duel.World.Tick);
                    }

                    return comparers.Count >= 3 || (a.MatchOver && b.MatchOver);
                }, 60 * 60 * 30);

                foreach (KeyValuePair<int, Comparer> kv in comparers) _output.WriteLine("duel " + kv.Key + ": " + kv.Value.Describe());
                Assert.True(done, "two duels did not finish");
                Assert.True(comparers.Values.Sum(c => c.Compared) > 1000);
                Assert.True(comparers.Values.Sum(c => c.Appeared) > 0 && comparers.Values.Sum(c => c.Changed) > 0, "the duels exercised no cell changes");
            }
        }
    }
}
