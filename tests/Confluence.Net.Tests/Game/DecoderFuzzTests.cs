using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Confluence.Casting;
using Confluence.Core.Geometry;
using Confluence.Core.Random;
using Confluence.Hosting.Commands;
using Confluence.Net.Game;
using Confluence.Net.Game.Snapshots;
using Confluence.Net.Game.Wire;
using Confluence.Net.Protocol;
using Confluence.Net.Serialization;
using Confluence.Net.Sync;
using Confluence.Sim.Duel;
using Confluence.Sim.Input;
using Confluence.Sim.Movement;

namespace Confluence.Net.Tests.Game
{
    /// <summary>
    /// Mutation fuzzing of every decoder (research §2.6.8, without SharpFuzz): random bit flips, byte replacements,
    /// truncations, insertions, and forged counts over valid encodings may only ever raise
    /// <see cref="NetReadException"/>, never another exception, a hang, or an unbounded allocation.
    /// </summary>
    public sealed class DecoderFuzzTests
    {
        private const int MutationsPerSample = 600;

        private static byte[] Encode(INetMessage m)
        {
            var w = new NetWriter(256);
            m.Serialize(w);
            return w.ToArray();
        }

        private static byte[] Mutate(byte[] source, Rng rng)
        {
            var bytes = new List<byte>(source);
            int edits = 1 + rng.NextInt(4);
            for (int e = 0; e < edits; e++)
            {
                int op = rng.NextInt(6);
                int at = bytes.Count == 0 ? 0 : rng.NextInt(bytes.Count);
                switch (op)
                {
                    case 0:
                        if (bytes.Count > 0) bytes[at] ^= (byte)(1 << rng.NextInt(8));
                        break;
                    case 1:
                        if (bytes.Count > 0) bytes[at] = (byte)rng.NextInt(256);
                        break;
                    case 2:
                        if (bytes.Count > 0) bytes.RemoveRange(at, bytes.Count - at);
                        break;
                    case 3:
                        bytes.Insert(at, (byte)rng.NextInt(256));
                        break;
                    case 4:
                        // A forged huge varint (counts, lengths, ticks).
                        bytes.InsertRange(at, new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x0F });
                        break;
                    default:
                        if (bytes.Count > 1) bytes.RemoveAt(at);
                        break;
                }
            }

            return bytes.ToArray();
        }

        private static void Fuzz(Func<byte[], object> decode, byte[] sample, ulong seed, string name)
        {
            var rng = new Rng(seed);
            for (int i = 0; i < MutationsPerSample; i++)
            {
                byte[] input = Mutate(sample, rng);
                try
                {
                    decode(input);
                }
                catch (NetReadException)
                {
                    // the only allowed failure
                }
                catch (Exception e)
                {
                    throw new Xunit.Sdk.XunitException(name + " threw " + e.GetType().Name + " on " + BitConverter.ToString(input) + ": " + e.Message);
                }
            }
        }

        private static object DecodeAs(Type type, byte[] bytes)
        {
            var m = (INetMessage)Activator.CreateInstance(type);
            var r = new NetReader(bytes);
            m.Deserialize(r);
            r.ExpectEnd();
            return m;
        }

        /// <summary>Rich samples of the messages a hostile peer is most likely to target.</summary>
        private static IEnumerable<INetMessage> Samples()
        {
            var input = new DuelInputMessage { DuelId = 3, AckSnapshotTick = 420, InterpolationTicks = 4, SnapshotsReceived = 77 };
            input.Commands.Add(new InputCommand<PlayerInput>(7, new PlayerInput { Tick = 500, MoveX = 0.5f, YawDeg = 271f, Buttons = InputButtons.Primary, ViewTick = 494 }));
            input.Commands.Add(new InputCommand<PlayerInput>(8, new PlayerInput
            {
                Tick = 501,
                Tapped = InputButtons.Melee,
                SelectedSlot = 5,
                GrammarEvents = new[] { new GrammarInputEvent(GrammarInputKind.Syllable, 500, 250, 3), GrammarInputEvent.Stroke(501, 9, 0.25f, 0.75f) },
            }));
            yield return input;
            yield return new JoinLobbyRequest { WizardId = "WIZ-010", Route = Casting.Grammars.InputRoute.Guided };
            yield return CommandRequest.Buy(2, Confluence.Core.Rules.EquipmentSlot.TrinketA);
            yield return CommandRequest.SelectSpells(new[] { "SPL-009", "SPL-010", "SPL-011" });
            yield return CommandRequest.DraftSelect(1, 2, true);
            yield return new SpectateRequest { DuelId = 4 };
            var events = new DuelEventsMessage { DuelId = 4 };
            events.Events.Add(new WireDuelEvent { Id = 10, Tick = 400, Type = WireEventType.Damage, Player = 0, Target = 1, SourceId = "SPL-009", Amount = 24f, PositionSet = true, Position = new Vec3(1, 1, 1) });
            yield return events;
            var snapshot = new SnapshotMessage { Header = new SnapshotHeader(420, 9, 418), Body = new byte[] { 4, 1, 2, 3 }, BodyLength = 4, InputQueue = 2, InputStarved = 5 };
            yield return snapshot;
        }

        [Fact]
        public void Every_Message_Decoder_Fails_Only_With_A_Read_Exception()
        {
            var types = typeof(GameProtocol).Assembly.GetTypes()
                .Where(t => typeof(INetMessage).IsAssignableFrom(t) && !t.IsAbstract && !t.IsGenericTypeDefinition && t.GetConstructor(Type.EmptyTypes) != null)
                .OrderBy(t => t.FullName, StringComparer.Ordinal)
                .ToList();
            Assert.True(types.Count >= 25, "message types " + types.Count);
            ulong seed = 1;
            foreach (Type type in types)
            {
                byte[] sample;
                try
                {
                    sample = Encode((INetMessage)Activator.CreateInstance(type));
                }
                catch (Exception)
                {
                    continue; // a default instance that cannot be encoded (e.g. a required list) is covered by the samples
                }

                Fuzz(b => DecodeAs(type, b), sample, seed++, type.Name);
            }

            foreach (INetMessage m in Samples()) Fuzz(b => DecodeAs(m.GetType(), b), Encode(m), seed++, m.GetType().Name + " sample");
        }

        [Fact]
        public void Snapshot_Bodies_Fail_Only_With_A_Read_Exception()
        {
            DuelFrame baseline = Frame(400, 0);
            DuelFrame next = Frame(402, 3);
            var own = new PrivateEntry { Health = 200, MaxHealth = 300, CooldownTicks = new[] { 0, 5 }, Tuning = MotorTuning.Standard };
            own.Motor = MotorState.At(new Vec3(1, 0, 2), 10f);
            var w = new NetWriter(4096);
            SnapshotCodec.EncodeBody(w, baseline, null, own);
            byte[] full = w.ToArray();
            baseline.Own = own;
            w.Reset();
            var own2 = new PrivateEntry { Health = 190, MaxHealth = 300, CooldownTicks = new[] { 0, 3 }, Tuning = MotorTuning.Standard };
            own2.Motor = MotorState.At(new Vec3(1.2f, 0, 2), 12f);
            SnapshotCodec.EncodeBody(w, next, baseline, own2);
            byte[] delta = w.ToArray();
            Fuzz(b => SnapshotCodec.DecodeBody(new NetReader(b), new SnapshotHeader(400, 1), null, out _), full, 900, "full snapshot body");
            Fuzz(b => SnapshotCodec.DecodeBody(new NetReader(b), new SnapshotHeader(402, 1, 400), baseline, out _), delta, 901, "delta snapshot body");
        }

        private static DuelFrame Frame(int tick, int shift)
        {
            var f = new DuelFrame { DuelId = 5, Tick = tick, MatchTick = 1000 + tick, Phase = DuelPhase.Combat, CombatTick = tick - 300 };
            for (int i = 0; i < 2; i++)
            {
                f.Players[i] = new PlayerEntry { Position = Quantization.QuantizePosition(new Vec3(i + shift, 0, 0)), Bits = PlayerBits.Alive, HealthDeci = 2000 - shift };
                f.Players[i].Statuses.Add(new StatusWire { Kind = 3, DefinitionId = "SPL-073", ExpireTick = tick + 60 });
            }

            f.Entities.Add(new EntityEntry(11, EntityKind.Projectile, 0, "SPL-009", Quantization.QuantizePosition(new Vec3(1, 1.5f, shift)), new QuantizedVec3(0, 0, 1920), 25, 0));
            f.Owned.Add(new OwnedEntry(3, 0, 1, "SPL-073", Quantization.QuantizePosition(new Vec3(4, 0, 4)), 300, 900, shift));
            f.Volumes.Add(new VolumeEntry(2, 0, 0, 0, 200, 15 + shift, -20, true, 2000, 10, 12, 3, 3, new byte[] { 0x0F, 0x01 }));
            for (int c = 0; c < 6; c++) f.Cells.Add(new CellEntry(100 + c * 7, (byte)((c + shift) % 5), 0, 0, 0, 0, 1, 0, CellBits.Wet));
            for (int p = 0; p < 4; p++) f.Props.Add(new PropEntry(p + 1, "PROP-010", Quantization.QuantizePosition(new Vec3(p, 0, shift)), 0, 500, false, p == 0));
            f.Solids.Add(new SolidEntry(5, false, false, 0, 0, false, true, default, 0));
            f.Solids.Add(new SolidEntry(1 << 20, true, true, 2, 2, false, true, new YawBox(new Vec3(1, 0.5f, 1), new Vec3(0.5f, 0.5f, 0.5f), 10f), 400));
            return f;
        }
    }
}
