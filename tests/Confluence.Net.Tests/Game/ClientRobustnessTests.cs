using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Confluence.Core.Arena;
using Confluence.Core.Geometry;
using Confluence.Hosting.Arenas;
using Confluence.Hosting.Duels;
using Confluence.Net.Game.Client;
using Confluence.Net.Game.Server;
using Confluence.Net.Game.Snapshots;
using Confluence.Net.Game.Wire;
using Confluence.Net.Serialization;
using Confluence.Net.Sync;
using Confluence.Net.Tests.TestSupport;
using Confluence.Sim.Duel;
using Confluence.Sim.Input;
using Confluence.Sim.Movement;
using Confluence.Sim.Players;
using InputButtons = Confluence.Casting.InputButtons;

namespace Confluence.Net.Tests.Game
{
    /// <summary>
    /// Server-validated inputs stay authoritative under trims and the client reconciles to the server's rules (spec L419):
    /// recovered taps produce gameplay edges on both sides, the predicted motor holds during the 0.5 s recall (L175,
    /// D-109), and reliable duel messages that overtake their DuelStart, or an ended snapshot before the end event, are
    /// never lost.
    /// </summary>
    public sealed class ClientRobustnessTests
    {
        private static DuelWorld World()
        {
            var recipe = new ArenaRecipe(11UL, 1, 24, "BIO-001", new string[0]);
            ArenaLayout layout = GameRig.Generator.Generate(recipe.ToGeneratorRequest(GameRig.Catalogue)).Layout;
            return new SimWorldFactory(GameRig.Catalogue).Create(layout, new DuelPlayerSetup { PlayerId = "A", WizardId = "WIZ-002" },
                new DuelPlayerSetup { PlayerId = "B", WizardId = "WIZ-010" }, 0, 5400, true);
        }

        private static PlayerInput Tap(InputButtons b)
        {
            PlayerInput input = PlayerInput.Idle(0f);
            input.Tapped = b;
            return input;
        }

        [Fact]
        public void A_Tap_Inside_One_Tick_Is_A_Press_For_Melee_Jump_Dodge_And_Guard()
        {
            DuelWorld world = World();
            Assert.Equal(DuelPhase.Combat, world.Phase);
            for (int i = 0; i < 5; i++) world.Step(PlayerInput.Idle(0f), PlayerInput.Idle(180f));
            world.Step(Tap(InputButtons.Melee), PlayerInput.Idle(180f));
            Assert.Contains(world.TickEvents, e => e is MeleeEvent m && m.Change == MeleeChange.Started && m.Player == 0);

            DuelWorld w2 = World();
            for (int i = 0; i < 5; i++) w2.Step(PlayerInput.Idle(0f), PlayerInput.Idle(180f));
            Assert.True(w2.Player(0).Motor.Grounded);
            w2.Step(Tap(InputButtons.Jump), PlayerInput.Idle(180f));
            Assert.True(w2.Player(0).Motor.Velocity.Y > 0f, "a tapped jump jumps");

            DuelWorld w3 = World();
            for (int i = 0; i < 5; i++) w3.Step(PlayerInput.Idle(0f), PlayerInput.Idle(180f));
            PlayerInput dodge = Tap(InputButtons.Dodge);
            dodge.MoveX = 1f;
            w3.Step(dodge, PlayerInput.Idle(180f));
            Assert.True(w3.Player(0).Motor.IsDodging, "a tapped dodge dodges");

            DuelWorld w4 = World();
            for (int i = 0; i < 5; i++) w4.Step(PlayerInput.Idle(0f), PlayerInput.Idle(180f));
            w4.Step(Tap(InputButtons.Guard), PlayerInput.Idle(180f));
            Assert.Contains(w4.TickEvents, e => e is GuardEvent && e.Player == 0);

            // A tap of a button already held is no new press (it stays one press, never a repeat).
            DuelWorld w5 = World();
            for (int i = 0; i < 5; i++) w5.Step(PlayerInput.Idle(0f), PlayerInput.Idle(180f));
            PlayerInput hold = PlayerInput.Idle(0f);
            hold.Buttons = InputButtons.Melee;
            w5.Step(hold, PlayerInput.Idle(180f));
            Assert.Single(w5.TickEvents.OfType<MeleeEvent>().Where(m => m.Change == MeleeChange.Started));
            PlayerInput again = hold;
            again.Tapped = InputButtons.Melee;
            w5.Step(again, PlayerInput.Idle(180f));
            Assert.DoesNotContain(w5.TickEvents, e => e is MeleeEvent m && m.Change == MeleeChange.Started);
        }

        [Fact]
        public void A_Melee_Press_In_A_Trimmed_Command_Still_Strikes_On_The_Server()
        {
            DuelWorld world = World();
            var source = new NetworkInputSource();
            source.BeginDuel(1);
            for (int i = 0; i < 5; i++) world.Step(PlayerInput.Idle(0f), PlayerInput.Idle(180f));
            // A burst of 20 commands after a stall: the press is held only in command 3, then released (neutral). The
            // 12-tick bank cap trims commands 1-8 into the next consumed one.
            var burst = new List<Confluence.Net.Sync.InputCommand<PlayerInput>>();
            for (uint s = 1; s <= 20; s++)
            {
                PlayerInput c = PlayerInput.Idle(0f);
                c.Tick = (int)s;
                if (s == 3) c.Buttons = InputButtons.Melee;
                burst.Add(new Confluence.Net.Sync.InputCommand<PlayerInput>(s, c));
            }

            source.Accept(1, burst);
            bool struck = false;
            for (int k = 0; k < 14 && !struck; k++)
            {
                PlayerInput next = source.NextInput(world.Tick + 1, world.Player(0).Motor.YawDeg, world.Player(0).SelectedSlot);
                world.Step(next, PlayerInput.Idle(180f));
                struck = world.TickEvents.Any(e => e is MeleeEvent m && m.Change == MeleeChange.Started && m.Player == 0);
            }

            Assert.True(source.Buffer.Skipped > 0, "the burst was trimmed");
            Assert.True(struck, "the recovered melee press executed");
        }

        [Fact]
        public void The_Client_Predicts_Tapped_Edges_Like_The_Server_And_Holds_The_Motor_During_Recall()
        {
            PlayerInput tap = Tap(InputButtons.Jump);
            Assert.Equal(InputButtons.Jump, MotorPredictor.PressedEdges(tap, InputButtons.None));
            Assert.Equal(InputButtons.None, MotorPredictor.PressedEdges(tap, InputButtons.Jump)); // a held jump is no new press
            PlayerInput held = PlayerInput.Idle(0f);
            held.Buttons = InputButtons.Dodge;
            Assert.Equal(InputButtons.Dodge, MotorPredictor.PressedEdges(held, InputButtons.None));

            DuelWorld world = World();
            for (int i = 0; i < 5; i++) world.Step(PlayerInput.Idle(0f), PlayerInput.Idle(180f));
            var predictor = new MotorPredictor(world.Layout, 0, default);
            PrivateEntry context = FrameCapture.CapturePrivate(world, 0);
            float stamina = context.Stamina;
            MotorState start = context.Motor;
            PlayerInput walk = PlayerInput.Idle(0f);
            walk.MoveY = 1f;

            // Recalling until tick 50 (the server holds step 2 while Tick < RecallEndTick).
            context.RecallEndTick = 50;
            MotorState held49 = predictor.Step(start, walk, InputButtons.None, 49, context, ref stamina);
            Assert.Equal(start.Position, held49.Position);
            Assert.Equal(start.Velocity, held49.Velocity);
            MotorState moved = predictor.Step(start, walk, InputButtons.None, 50, context, ref stamina);
            Assert.NotEqual(start.Position, moved.Position);

            // The recall end tick reaches the client in the private entry, delta-coded.
            var w = new NetWriter(256);
            context.WriteAgainst(w, FrameCapture.CapturePrivate(world, 0));
            PrivateEntry back = PrivateEntry.ReadAgainst(new NetReader(w.ToArray(), 0, w.Length), FrameCapture.CapturePrivate(world, 0));
            Assert.Equal(50, back.RecallEndTick);
            var full = new NetWriter(512);
            context.Write(full);
            Assert.Equal(50, PrivateEntry.Read(new NetReader(full.ToArray(), 0, full.Length)).RecallEndTick);
            Assert.Equal(-1, FrameCapture.CapturePrivate(world, 0).RecallEndTick);
        }

        private static GameClient NewClient() => new GameClient(new GameClientOptions
        {
            DisplayName = "Probe",
            Catalogue = GameRig.Catalogue,
            Ruleset = LoopbackRig.Ruleset,
            Transport = GameRig.FastTransport(),
        });

        private static void Invoke(GameClient client, string handler, object message) =>
            typeof(GameClient).GetMethod(handler, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(client, new[] { message });

        private static DuelStartMessage Start(int duelId)
        {
            var start = new DuelStartMessage { DuelId = duelId, Round = 1, ArenaId = "arena:test", YourSide = 0, AlcoveTicks = 0, CombatTicks = 5400 };
            start.Sides[0].WizardId = "WIZ-002";
            start.Sides[1].WizardId = "WIZ-010";
            start.Sides[1].Seat = 1;
            return start;
        }

        [Fact]
        public void Events_Reports_And_Reliable_Snapshots_Arriving_Before_Their_DuelStart_Are_Applied_When_It_Arrives()
        {
            using (GameClient client = NewClient())
            {
                var events = new DuelEventsMessage { DuelId = 7 };
                events.Events.Add(new WireDuelEvent { Id = 1, Tick = 400, Type = WireEventType.Damage, Player = 0, Target = 1, Amount = 12f });
                events.Events.Add(new WireDuelEvent { Id = 2, Tick = 401, Type = WireEventType.DuelEnd, Target = 0 });
                Invoke(client, "OnEvents", events);
                Invoke(client, "OnReport", new DuelReportMessage { DuelId = 7, WinnerSide = 0, FinalTick = 401 });

                var frame = new DuelFrame { DuelId = 7, Tick = 401, MatchTick = 1000, Phase = DuelPhase.Ended, CombatTick = 101 };
                frame.Players[0] = new PlayerEntry();
                frame.Players[1] = new PlayerEntry();
                var body = new NetWriter(1024);
                SnapshotCodec.EncodeBody(body, frame, null, null);
                Invoke(client, "OnSnapshot", new SnapshotMessage { Header = new SnapshotHeader(401, 0), Body = body.ToArray(), BodyLength = body.Length });
                Assert.Null(client.Duel);
                Assert.Equal(3, client.HeldMessages);

                // An older duel's late events are not held.
                Invoke(client, "OnDuelStart", Start(7));
                var stale = new DuelEventsMessage { DuelId = 6 };
                stale.Events.Add(new WireDuelEvent { Id = 9, Tick = 10, Type = WireEventType.Damage, Player = 1, Target = 0 });
                Invoke(client, "OnEvents", stale);

                ClientDuel duel = client.Duel;
                Assert.Equal(7, duel.DuelId);
                Assert.Equal(2, duel.Events.Count);
                Assert.Equal(1, duel.HitsConfirmed);
                Assert.True(duel.Ended);
                Assert.NotNull(duel.Report);
                Assert.Equal(401, duel.LatestTick);
                Assert.Equal(3, client.HeldMessages);
            }
        }

        [Fact]
        public void An_Ended_Snapshot_Ends_The_Client_Duel_Even_Before_The_End_Event()
        {
            using (GameClient client = NewClient())
            {
                int ended = 0;
                client.DuelEnded += (c, d) => ended++;
                Invoke(client, "OnDuelStart", Start(3));
                var frame = new DuelFrame { DuelId = 3, Tick = 900, MatchTick = 2000, Phase = DuelPhase.Ended, CombatTick = 600 };
                frame.Players[0] = new PlayerEntry();
                frame.Players[1] = new PlayerEntry();
                var body = new NetWriter(1024);
                SnapshotCodec.EncodeBody(body, frame, null, null);
                Invoke(client, "OnSnapshot", new SnapshotMessage { Header = new SnapshotHeader(900, 0), Body = body.ToArray(), BodyLength = body.Length });
                Assert.True(client.Duel.Ended);
                Assert.Equal(1, ended);
                var events = new DuelEventsMessage { DuelId = 3 };
                events.Events.Add(new WireDuelEvent { Id = 5, Tick = 900, Type = WireEventType.DuelEnd });
                Invoke(client, "OnEvents", events);
                Assert.Equal(1, ended); // raised once
            }
        }
    }
}
