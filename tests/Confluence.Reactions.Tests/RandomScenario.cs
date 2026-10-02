using System;
using System.Collections.Generic;
using Confluence.Core.Geometry;
using Confluence.Core.Random;
using Confluence.Reactions.Events;
using Confluence.Reactions.Grid;
using Confluence.Reactions.Operations;
using Confluence.Reactions.Volumes;

namespace Confluence.Reactions.Tests
{
    /// <summary>Seeded random world operations (every operation type) for determinism and budget tests.</summary>
    internal static class RandomScenario
    {
        /// <summary>A deterministic mix of every operation type for one tick.</summary>
        public static List<Action<ReactionWorld>> Ops(Rng rng, CellGrid grid, int tick)
        {
            var ops = new List<Action<ReactionWorld>>();
            int count = rng.NextInt(4);
            for (int k = 0; k < count; k++)
            {
                int source = tick * 10 + k + 1;
                int owner = rng.NextInt(2);
                Vec2 at = grid.CellCenter(rng.NextInt(8, grid.Width - 8), rng.NextInt(8, grid.Depth - 8));
                Vec3 at3 = at.ToXZ();
                switch (rng.NextInt(11))
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
                    case 8:
                    {
                        int pick = rng.NextInt(4);
                        ops.Add(w =>
                        {
                            if (w.Volumes.Count > 0)
                            {
                                w.Condense(new CondenseRequest { VolumeId = w.Volumes[pick % w.Volumes.Count].Id, LifetimeTicks = 200, Owner = owner, SourceEventId = source });
                            }
                        });
                        break;
                    }
                    case 9:
                        ops.Add(w => w.RemoveUnits(new UnitRemoval { Footprint = Footprint.Circle(at, 1f), Owner = owner, SourceEventId = source }));
                        break;
                    default:
                        ops.Add(w => w.RaiseDust(new DustRaise { Footprint = Footprint.Circle(at, 1f), LifetimeTicks = 120, Owner = owner, SourceEventId = source }));
                        break;
                }
            }

            return ops;
        }

        public static string Describe(StepResult r)
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
