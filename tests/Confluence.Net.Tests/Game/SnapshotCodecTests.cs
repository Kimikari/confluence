using System;
using System.Collections.Generic;
using Confluence.Core.Geometry;
using Confluence.Net.Game.Snapshots;
using Confluence.Net.Serialization;
using Confluence.Net.Sync;
using Confluence.Sim.Duel;

namespace Confluence.Net.Tests.Game
{
    /// <summary>Snapshot bodies: full and delta encodings decode to the same state; deltas only carry changes (spec L421).</summary>
    public sealed class SnapshotCodecTests
    {
        private static DuelFrame Frame(int tick, int cells = 40, int movedProp = -1, bool extraEntity = false, bool dropVolume = false)
        {
            var f = new DuelFrame { DuelId = 5, Tick = tick, MatchTick = 1000 + tick, Phase = DuelPhase.Combat, CombatTick = tick - 300 };
            for (int i = 0; i < 2; i++)
            {
                f.Players[i] = new PlayerEntry
                {
                    Position = Quantization.QuantizePosition(new Vec3(i * 3 + tick * 0.01f, 0f, 2f)),
                    Velocity = new QuantizedVec3(64, 0, -32),
                    Yaw = Quantization.QuantizeAngle(90f * i),
                    Bits = PlayerBits.Alive | PlayerBits.Grounded,
                    CastPhase = 2,
                    CastSpellId = "SPL-009",
                    HealthDeci = 2500,
                    MaxHealthDeci = 3000,
                    ShieldDeci = 0,
                };
                f.Players[i].Statuses.Add(new StatusWire { Kind = 6, DefinitionId = "SPL-073", ExpireTick = tick + 100, IntensityCenti = 25 });
            }

            f.Entities.Add(new EntityEntry(11, EntityKind.Projectile, 0, "SPL-009", Quantization.QuantizePosition(new Vec3(1, 1.5f, tick * 0.5f)), new QuantizedVec3(0, 0, 1920), 25, 0));
            if (extraEntity) f.Entities.Add(new EntityEntry(12, EntityKind.Field, 1, "SPL-073", Quantization.QuantizePosition(new Vec3(4, 0, 4)), default, 300, 0));
            f.Owned.Add(new OwnedEntry(3, 0, 1, "SPL-073", Quantization.QuantizePosition(new Vec3(4, 0, 4)), 300, 900, 0));
            if (!dropVolume)
            {
                var bits = new byte[] { 0x0F, 0x01 };
                f.Volumes.Add(new VolumeEntry(2, 0, 0, 0, 200, 15, -20, true, 2000, 10, 12, 3, 3, bits));
            }

            for (int c = 0; c < cells; c++) f.Cells.Add(new CellEntry(100 + c * 7, (byte)(c % 5), 0, 0, 0, 0, (byte)(c % 2), 0, c % 3 == 0 ? CellBits.Wet : CellBits.None));
            for (int p = 0; p < 30; p++)
            {
                Vec3 at = new Vec3(p, 0f, p == movedProp ? 2f + tick * 0.01f : 0f);
                f.Props.Add(new PropEntry(p + 1, "PROP-0" + (10 + p % 30), Quantization.QuantizePosition(at), Quantization.QuantizeAngle(p * 7f), 500, false, p == movedProp));
            }

            f.Solids.Add(new SolidEntry(5, false, false, 0, 0, false, true, default, 0));
            for (int s = 0; s < 30; s++)
            {
                f.Solids.Add(new SolidEntry((1 << 20) + s, true, true, 2, 1 << 1, false, true,
                    new YawBox(new Vec3(s, 0.5f, s == movedProp ? 2f + tick * 0.01f : 0f), new Vec3(0.5f, 0.5f, 0.5f), 10f), 400));
            }

            return f;
        }

        private static byte[] Encode(DuelFrame frame, DuelFrame baseline, PrivateEntry own = null)
        {
            var w = new NetWriter(4096);
            SnapshotCodec.EncodeBody(w, frame, baseline, own);
            return w.ToArray();
        }

        private static DuelFrame Decode(byte[] body, SnapshotHeader header, DuelFrame baseline, out PrivateEntry own)
        {
            var r = new NetReader(body);
            DuelFrame f = SnapshotCodec.DecodeBody(r, header, baseline, out own);
            r.ExpectEnd();
            return f;
        }

        [Fact]
        public void Full_And_Delta_Bodies_Decode_To_The_Same_State()
        {
            DuelFrame a = Frame(400);
            DuelFrame b = Frame(402, cells: 41, movedProp: 7, extraEntity: true, dropVolume: true);
            byte[] full = Encode(b, null);
            byte[] delta = Encode(b, a);
            DuelFrame decodedFull = Decode(full, new SnapshotHeader(402, 9), null, out _);
            DuelFrame decodedA = Decode(Encode(a, null), new SnapshotHeader(400, 8), null, out _);
            DuelFrame decodedDelta = Decode(delta, new SnapshotHeader(402, 9, 400), decodedA, out _);
            Assert.True(SnapshotCodec.SameState(b, decodedFull));
            Assert.True(SnapshotCodec.SameState(b, decodedDelta));
            Assert.Null(decodedDelta.Volumes.Find(v => v.Key == 2));
            Assert.NotNull(decodedDelta.Entities.Find(e => e.Key == 12));

            // Unchanged props, solids, and most cells cost nothing in the delta.
            Assert.True(delta.Length * 3 < full.Length, "delta " + delta.Length + " vs full " + full.Length);
        }

        [Fact]
        public void An_Unchanged_Frame_Delta_Carries_Only_Players()
        {
            DuelFrame a = Frame(400);
            DuelFrame same = Frame(400);
            same.Tick = 402;
            byte[] delta = Encode(same, a);
            Assert.True(delta.Length < 150, "delta of an unchanged world is " + delta.Length + " bytes");
        }

        [Fact]
        public void Private_State_Round_Trips_With_The_Exact_Motor()
        {
            var own = new PrivateEntry
            {
                Health = 251.25f,
                MaxHealth = 300f,
                Mana = 73.5f,
                MaxMana = 100f,
                Stamina = 41.125f,
                MaxStamina = 100f,
                SelectedSlot = 3,
                CooldownTicks = new[] { 0, 30, 0, 0, 120, 0 },
                MachinePhase = 1,
                MachineSpellId = "SPL-010",
                CastMovementFactor = 0.85f,
                SpeedMultiplier = 0.75f,
                Locks = Confluence.Sim.Movement.MotorLocks.Sprint,
                Tuning = Confluence.Sim.Movement.MotorTuning.Standard,
                Judgement = 1,
            };
            own.Motor = Confluence.Sim.Movement.MotorState.At(new Vec3(1.234567f, 0.5f, -7.654321f), 33.3f);
            own.Motor.Velocity = new Vec3(0.1f, -2f, 5.5f);
            own.Grammar = new Confluence.Casting.Grammars.GrammarUiState { Active = true, ElapsedTicks = 14, Gauge = 0.4f, GaugeValue = 40f, HasMasteryWindow = true, MasteryWindow = new Confluence.Casting.TickWindow(24, 30) };
            DuelFrame frame = Frame(500);
            DuelFrame decoded = Decode(Encode(frame, null, own), new SnapshotHeader(500, 1), null, out PrivateEntry back);
            Assert.NotNull(back);
            Assert.Equal(own.Motor.Position, back.Motor.Position);
            Assert.Equal(own.Motor.Velocity, back.Motor.Velocity);
            Assert.Equal(own.Stamina, back.Stamina);
            Assert.Equal(30, back.CooldownTicks[1]);
            Assert.Equal(new Confluence.Casting.TickWindow(24, 30), back.Grammar.MasteryWindow);
            Assert.Equal(own.Tuning.SprintSpeed, back.Tuning.SprintSpeed);
            Assert.True(SnapshotCodec.SameState(frame, decoded));
        }

        [Fact]
        public void A_Delta_Needs_Its_Exact_Baseline()
        {
            DuelFrame a = Frame(400);
            DuelFrame b = Frame(402, movedProp: 3);
            byte[] delta = Encode(b, a);
            Assert.Throws<NetReadException>(() => Decode(delta, new SnapshotHeader(402, 0, 400), null, out _));
            Assert.Throws<NetReadException>(() => Decode(delta, new SnapshotHeader(402, 0, 400), Frame(398), out _));

            DuelFrame otherDuel = Frame(400);
            otherDuel.DuelId = 6;
            Assert.Throws<NetReadException>(() => Decode(delta, new SnapshotHeader(402, 0, 400), otherDuel, out _));
        }

        [Fact]
        public void Truncated_Or_Forged_Bodies_Are_Rejected()
        {
            byte[] full = Encode(Frame(400), null, new PrivateEntry());
            for (int n = 0; n < full.Length; n += Math.Max(1, full.Length / 97))
            {
                var prefix = new byte[n];
                Array.Copy(full, prefix, n);
                Assert.Throws<NetReadException>(() => Decode(prefix, new SnapshotHeader(400, 0), null, out _));
            }

            // Removing an entry the baseline does not hold is a forged delta.
            var w = new NetWriter();
            DuelFrame a = Frame(400);
            DuelFrame b = Frame(402, dropVolume: true);
            SnapshotCodec.EncodeBody(w, b, a, null);
            byte[] delta = w.ToArray();
            DuelFrame stranger = Frame(400, dropVolume: true);
            Assert.ThrowsAny<Exception>(() => Decode(delta, new SnapshotHeader(402, 0, 400), stranger, out _));
        }

        [Fact]
        public void Viewer_Projection_Hides_Only_The_Opponents_Vitals()
        {
            DuelFrame truth = Frame(400);
            DuelFrame forSide0 = FrameCapture.ForViewer(truth, 0);
            Assert.Equal(2500, forSide0.Players[0].HealthDeci);
            Assert.Equal(-1, forSide0.Players[1].HealthDeci);
            Assert.Equal(-1, forSide0.Players[1].ShieldDeci);
            Assert.Equal(truth.Players[1].Position, forSide0.Players[1].Position);
            Assert.Equal(2500, truth.Players[1].HealthDeci); // the truth is untouched
            DuelFrame spectator = FrameCapture.ForSpectator(truth);
            Assert.Equal(2500, spectator.Players[1].HealthDeci);
        }
    }
}
