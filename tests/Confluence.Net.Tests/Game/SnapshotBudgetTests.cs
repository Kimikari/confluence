using System;
using System.Collections.Generic;
using Confluence.Core.Geometry;
using Confluence.Net.Game.Snapshots;
using Confluence.Net.Serialization;
using Confluence.Net.Sync;
using Confluence.Sim.Duel;
using Confluence.Sim.Movement;

namespace Confluence.Net.Tests.Game
{
    /// <summary>
    /// The per-snapshot byte budget (docs/research/netcode.md §2.4.1, §2.9.4): partial deltas fit one datagram, the
    /// frame the encoder remembers is exactly what the receiver decodes, deferred entries converge, and the spec L421
    /// worst-case duel stays inside the budget.
    /// </summary>
    public sealed class SnapshotBudgetTests
    {
        /// <summary>About one LiteNetLib 2.1.4 sequenced datagram (1020 B) minus the message and header bytes.</summary>
        private const int Budget = 990;

        /// <summary>
        /// The L421 worst case: twelve equipped spell slots (one live delivery each), six player-owned fields, four
        /// summons, 128 active interaction-cell regions, 48 props, and a large drifting steam volume.
        /// </summary>
        private static DuelFrame WorstCase(int tick, int changedCells, bool deliveries = true)
        {
            var f = new DuelFrame { DuelId = 9, Tick = tick, MatchTick = 2000 + tick, Phase = DuelPhase.Combat, CombatTick = tick - 300 };
            for (int i = 0; i < 2; i++)
            {
                f.Players[i] = new PlayerEntry
                {
                    Position = Quantization.QuantizePosition(new Vec3(i * 6 + tick * 0.09f, 0f, 3f - tick * 0.05f)),
                    Velocity = new QuantizedVec3(352, 0, -64),
                    Yaw = Quantization.QuantizeAngle(tick * 3f + i * 180f),
                    Pitch = Quantization.QuantizePitch(-5f),
                    Bits = PlayerBits.Alive | PlayerBits.Grounded | PlayerBits.Sprinting,
                    CastPhase = 2,
                    CastSpellId = "SPL-009",
                    HealthDeci = 2500 - tick,
                    MaxHealthDeci = 3000,
                    ShieldDeci = 120,
                };
                for (int s = 0; s < 4; s++) f.Players[i].Statuses.Add(new StatusWire { Kind = (byte)(s + 3), DefinitionId = "SPL-07" + s, ExpireTick = tick + 100, IntensityCenti = 25 });
            }

            for (int e = 0; e < (deliveries ? 12 : 0); e++)
            {
                f.Entities.Add(new EntityEntry(100 + e, EntityKind.Projectile, e % 2, "SPL-0" + (10 + e), Quantization.QuantizePosition(new Vec3(e, 1.5f, tick * 0.5f + e)),
                    new QuantizedVec3(0, -30, 1920), 25, 0));
            }

            for (int s = 0; s < (deliveries ? 4 : 0); s++)
            {
                f.Entities.Add(new EntityEntry(200 + s, EntityKind.Summon, s % 2, "SPL-1" + (10 + s), Quantization.QuantizePosition(new Vec3(s * 2, 0.5f, 4 + tick * 0.05f)),
                    new QuantizedVec3(64, 0, 64), 60, 0));
            }

            for (int o = 0; o < (deliveries ? 6 : 0); o++)
            {
                f.Owned.Add(new OwnedEntry(10 + o, 0, o % 2, "SPL-07" + o, Quantization.QuantizePosition(new Vec3(o * 3, 0, 8)), 300, 900 + tick, 400 - tick));
            }

            // A 24 x 24-column steam volume drifting with the wind (offset changes every snapshot).
            var bits = new byte[(24 * 24 + 7) / 8];
            for (int b = 0; b < bits.Length; b++) bits[b] = (byte)(0xA5 ^ b);
            f.Volumes.Add(new VolumeEntry(2, 0, -1, 0, 300, tick * 5, -tick * 3, true, 4000, 30, 30, 24, 24, bits));

            for (int c = 0; c < 128; c++)
            {
                bool changed = c < changedCells;
                f.Cells.Add(new CellEntry(400 + c * 11, (byte)(changed ? (tick + c) % 5 : 1), (byte)(changed ? tick % 3 : 0), 0, 0, 0, 0, 0,
                    c % 4 == 0 ? CellBits.Wet : CellBits.None));
            }

            for (int p = 0; p < 48; p++)
            {
                bool moving = p < 4;
                f.Props.Add(new PropEntry(p + 1, "PROP-0" + (10 + p % 30), Quantization.QuantizePosition(new Vec3(p, 0f, moving ? tick * 0.02f : 0f)),
                    Quantization.QuantizeAngle(p * 7f), 500, false, moving));
            }

            for (int s = 0; s < 6; s++)
            {
                f.Solids.Add(new SolidEntry((1 << 20) + s, true, true, 2, 1 << 1, false, true, new YawBox(new Vec3(s, 0.5f, 6f), new Vec3(1f, 1f, 0.2f), 10f), 400));
            }

            return f;
        }

        private static PrivateEntry Own(int tick)
        {
            var own = new PrivateEntry
            {
                Health = 250f - tick * 0.1f,
                MaxHealth = 300f,
                Mana = 70f,
                MaxMana = 100f,
                Stamina = 40f,
                MaxStamina = 100f,
                CooldownTicks = new[] { 0, 30, 0, 0, 120, 0 },
                MachinePhase = 2,
                MachineSpellId = "SPL-009",
                Tuning = MotorTuning.Standard,
            };
            own.Motor = MotorState.At(new Vec3(tick * 0.09f, 0f, 3f), 33f);
            return own;
        }

        private static DuelFrame Decode(byte[] body, SnapshotHeader header, DuelFrame baseline)
        {
            var r = new NetReader(body);
            DuelFrame f = SnapshotCodec.DecodeBody(r, header, baseline, out _);
            r.ExpectEnd();
            return f;
        }

        [Fact]
        public void A_Partial_Snapshot_Fits_And_The_Remembered_Frame_Is_What_The_Receiver_Decodes()
        {
            // A first snapshot (no baseline) at duel start: 128 cell regions, 48 props, and a steam volume, no deliveries yet.
            DuelFrame truth = WorstCase(400, 128, deliveries: false);
            var w = new NetWriter(8192);
            SnapshotCodec.EncodeBody(w, truth, null, Own(400));
            Assert.True(w.Length > Budget, "the worst-case keyframe is larger than a datagram: " + w.Length);

            w.Reset();
            DuelFrame held = SnapshotCodec.EncodeBody(w, truth, null, Own(400), Budget, null, out int deferred, out bool over);
            Assert.False(over);
            Assert.True(deferred > 0);
            Assert.True(w.Length <= Budget, "body " + w.Length);
            DuelFrame decoded = Decode(w.ToArray(), new SnapshotHeader(400, 1), null);
            Assert.True(SnapshotCodec.SameState(held, decoded));
            Assert.False(SnapshotCodec.SameState(truth, decoded));

            // Deltas against what the receiver holds converge on the truth within a few snapshots.
            DuelFrame client = decoded;
            DuelFrame server = held;
            int baselineTick = 400;
            for (int k = 1; k <= 10 && !SnapshotCodec.SameState(Retick(truth, 400 + 2 * k), client); k++)
            {
                DuelFrame next = Retick(truth, 400 + 2 * k);
                w.Reset();
                server = SnapshotCodec.EncodeBody(w, next, server, Own(400), Budget, null, out _, out over);
                Assert.True(w.Length <= Budget);
                client = Decode(w.ToArray(), new SnapshotHeader(next.Tick, 1, baselineTick), client);
                baselineTick = next.Tick;
                Assert.True(SnapshotCodec.SameState(server, client));
            }

            Assert.True(SnapshotCodec.SameState(Retick(truth, client.Tick), client), "the receiver caught up with the full state");
        }

        [Fact]
        public void The_Worst_Case_Duel_Stays_Inside_The_Budget_With_Priority_Deferral()
        {
            var sizes = new List<int>();
            int partial = 0;
            _ = partial;
            var w = new NetWriter(8192);
            DuelFrame baseline = null;
            DuelFrame clientHeld = null;
            int baselineTick = SnapshotHeader.NoBaseline;
            var waited = new Dictionary<int, int>();
            Vec3 viewer = new Vec3(0, 0, 3);
            int keyframeBytes = 0;
            for (int tick = 400; tick < 400 + 2 * 120; tick += 2)
            {
                DuelFrame truth = WorstCase(tick, 64);
                w.Reset();
                DuelFrame held = SnapshotCodec.EncodeBody(w, truth, baseline, Own(tick), Budget,
                    (section, e) => (e is PropEntry p ? Vec3.Distance(viewer, Quantization.DequantizePosition(p.Position)) : e.Key * 0.01f)
                        - 2f * (waited.TryGetValue(e.Key, out int n) ? n : 0), out int deferred, out bool over);
                if (baseline == null)
                {
                    // A mid-duel keyframe (a resume) of the full worst case may exceed one datagram: it travels reliably.
                    keyframeBytes = w.Length;
                }
                else
                {
                    Assert.False(over, "the mandatory part of a delta fits at tick " + tick);
                    sizes.Add(w.Length);
                }

                if (deferred > 0) partial++;
                clientHeld = Decode(w.ToArray(), new SnapshotHeader(tick, 1, baselineTick), clientHeld);
                Assert.True(SnapshotCodec.SameState(held, clientHeld));
                baseline = held;
                baselineTick = tick;
            }

            sizes.Sort();
            int p99 = sizes[(int)Math.Ceiling(sizes.Count * 0.99) - 1];
            Assert.True(p99 <= Budget, "p99 body " + p99);
            Assert.True(sizes[sizes.Count - 1] <= Budget);
            Assert.True(keyframeBytes > 0);
            Assert.True(sizes[sizes.Count / 2] <= Budget, "median delta " + sizes[sizes.Count / 2]);
        }

        [Fact]
        public void A_Mandatory_Part_Over_The_Budget_Is_A_Complete_Reliable_Keyframe()
        {
            DuelFrame truth = WorstCase(400, 128);
            var w = new NetWriter(8192);
            DuelFrame held = SnapshotCodec.EncodeBody(w, truth, null, Own(400), 200, null, out int deferred, out bool over);
            Assert.True(over);
            Assert.Equal(0, deferred);
            Assert.Same(truth, held);
            Assert.True(SnapshotCodec.SameState(truth, Decode(w.ToArray(), new SnapshotHeader(400, 1), null)));
        }

        private static DuelFrame Retick(DuelFrame f, int tick)
        {
            DuelFrame copy = f.ShallowCopy();
            copy.Tick = tick;
            copy.MatchTick = f.MatchTick - f.Tick + tick;
            return copy;
        }
    }
}
