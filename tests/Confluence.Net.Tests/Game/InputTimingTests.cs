using System;
using System.Collections.Generic;
using Confluence.Casting;
using Confluence.Core.Geometry;
using Confluence.Net.Game.Client;
using Confluence.Net.Game.Server;
using Confluence.Net.Sync;
using Confluence.Sim.Duel;
using Confluence.Sim.Input;

namespace Confluence.Net.Tests.Game
{
    /// <summary>
    /// Input timing and lag compensation rules of the game protocol (docs/research/netcode.md §2.1, §2.2 and its verifier
    /// checklist "Inputs and timing" / "Lag compensation").
    /// </summary>
    public sealed class InputTimingTests
    {
        private static PlayerInput Command(int tick, params GrammarInputEvent[] events)
        {
            var input = new PlayerInput { Tick = tick };
            if (events.Length > 0) input.GrammarEvents = events;
            return input;
        }

        private static GrammarInputEvent Syllable(int tick, int value = 1) => new GrammarInputEvent(GrammarInputKind.Syllable, tick, 0, value);

        private static List<InputCommand<PlayerInput>> One(uint sequence, PlayerInput input) =>
            new List<InputCommand<PlayerInput>> { new InputCommand<PlayerInput>(sequence, input) };

        [Fact]
        public void A_Hard_Trim_Never_Loses_Syllables_Or_Taps()
        {
            var source = new NetworkInputSource();
            source.BeginDuel(1);
            // The client banks 20 commands at once (a burst after a stall), each with a syllable; the 12-tick cap trims.
            var commands = new List<InputCommand<PlayerInput>>();
            for (uint s = 1; s <= 20; s++)
            {
                PlayerInput c = Command(100 + (int)s, Syllable(100 + (int)s, (int)s));
                if (s == 3) c.Tapped = InputButtons.Melee;
                commands.Add(new InputCommand<PlayerInput>(s, c));
            }

            source.Accept(1, commands, 50, 4);
            var seen = new List<int>();
            InputButtons taps = InputButtons.None;
            for (int t = 1; t <= 30; t++)
            {
                PlayerInput input = source.NextInput(200 + t, 0f, 0);
                taps |= input.Tapped;
                if (input.GrammarEvents != null)
                    foreach (GrammarInputEvent e in input.GrammarEvents) seen.Add(e.Value);
            }

            Assert.True(source.Buffer.Skipped > 0, "the burst was trimmed");
            Assert.Equal(20, seen.Count);
            for (int i = 0; i < 20; i++) Assert.Equal(i + 1, seen[i]); // in order, none lost
            Assert.Equal(InputButtons.Melee, taps & InputButtons.Melee);
        }

        [Fact]
        public void A_Sustained_Surplus_Is_Cut_Back_And_Keeps_One_Shot_Data()
        {
            var buffer = new InputReceiveBuffer<PlayerInput>(mergeSkipped: NetworkInputSource.MergeSkipped, surplusTargetTicks: 4, surplusPatienceTicks: 30);
            uint sequence = 0;
            // Prime a queue 8 deep, then keep it there: one new command per consumed one.
            for (int i = 0; i < 8; i++) buffer.Accept(++sequence, Command((int)sequence));
            int tapsSeen = 0;
            for (int t = 0; t < 60; t++)
            {
                PlayerInput next = Command((int)(sequence + 1));
                next.Tapped = InputButtons.Jump;
                buffer.Accept(++sequence, next);
                ConsumedInput<PlayerInput> c = buffer.ConsumeNext();
                if ((c.Input.Tapped & InputButtons.Jump) != 0) tapsSeen++;
            }

            Assert.True(buffer.SurplusCut >= 4, "the surplus over the target was cut");
            Assert.True(buffer.QueueDepth <= 5, "queue " + buffer.QueueDepth);
            Assert.True(buffer.Merged >= 4);
            Assert.True(tapsSeen > 0);
        }

        [Fact]
        public void Lead_Control_Removes_A_Three_Tick_Slip_Within_One_Second()
        {
            // Discrete model: 2-tick one-way delay both ways, snapshots every 2 ticks, a 3-tick delivery stall at tick 600.
            const int oneWay = 2;
            var source = new NetworkInputSource();
            source.BeginDuel(1);
            var lead = new InputLeadControl();
            const int baseLead = 3;
            var toServer = new List<KeyValuePair<int, InputCommand<PlayerInput>>>();
            var toClient = new List<KeyValuePair<int, int[]>>();
            uint sequence = 0;
            int localTick = 0;
            int queueAfter = -1;
            int queueBefore = -1;
            for (int t = 1; t <= 1200; t++)
            {
                // Client at server time t (exact clock): produce up to target, send each command.
                foreach (KeyValuePair<int, int[]> s in toClient.FindAll(p => p.Key <= t)) lead.OnSnapshot(s.Value[0], s.Value[1], -baseLead, 12 - baseLead);
                toClient.RemoveAll(p => p.Key <= t);
                int target = t + baseLead + lead.Adjust;
                while (localTick < target)
                {
                    localTick++;
                    toServer.Add(new KeyValuePair<int, InputCommand<PlayerInput>>(t + oneWay, new InputCommand<PlayerInput>(++sequence, Command(localTick))));
                }

                // Network: the stall holds every packet due in [600, 603) until 603.
                bool stalled = t >= 600 && t < 603;
                var due = toServer.FindAll(p => p.Key <= t && !stalled);
                toServer.RemoveAll(p => p.Key <= t && !stalled);
                foreach (KeyValuePair<int, InputCommand<PlayerInput>> p in due) source.Accept(1, new List<InputCommand<PlayerInput>> { p.Value }, t, 4);

                source.NextInput(t, 0f, 0);
                if (t % 2 == 0)
                {
                    toClient.Add(new KeyValuePair<int, int[]>(t + oneWay, new[] { source.QueueDepth, (int)source.Buffer.StarvedTicks }));
                }

                if (t == 603 + 60) queueAfter = source.QueueDepth;
                if (t == 599) queueBefore = source.QueueDepth;
            }

            Assert.True(source.Buffer.StarvedTicks >= 2, "the stall starved the server: " + source.Buffer.StarvedTicks + " queue before " + queueBefore + " adjust " + lead.Adjust + " dec " + lead.Decreases + " inc " + lead.Increases);
            Assert.True(queueAfter >= 0 && queueAfter <= 2, "queue one second after the stall: " + queueAfter);
            Assert.True(lead.Decreases >= 3, "decreases " + lead.Decreases);
            Assert.Equal(0, source.Buffer.SurplusCut);
        }

        [Fact]
        public void A_Syllable_Stamped_Five_Ticks_Early_Is_Accepted_And_A_Backdated_Tap_Is_Clamped()
        {
            var source = new NetworkInputSource();
            source.BeginDuel(1);
            for (uint s = 1; s <= 4; s++)
            {
                source.Accept(1, One(s, Command(100 + (int)s)), 10, 4);
                source.NextInput(10 + (int)s, 0f, 0);
            }

            var tap = new GrammarInputEvent(GrammarInputKind.RhythmTap, 105 - 5, 0, 0);
            source.Accept(1, One(5, Command(105, Syllable(105 - 5), tap)), 14, 4);
            PlayerInput input = source.NextInput(15, 0f, 0);
            Assert.Equal(2, input.GrammarEvents.Count);
            int offset = source.SessionOffset;
            Assert.Equal(100 + offset, input.GrammarEvents[0].Tick); // 5 ticks before its command, kept
            Assert.Equal(104 + offset, input.GrammarEvents[1].Tick); // a tap may be at most 1 tick early
            Assert.Equal(1, source.Stats.EventsClamped);
        }

        [Fact]
        public void Syllable_Spacing_Survives_A_Starvation_Between_Their_Commands()
        {
            var source = new NetworkInputSource();
            source.BeginDuel(1);
            uint s = 0;
            int step = 1000;
            var placed = new List<int>();
            for (int clientTick = 1; clientTick <= 60; clientTick++)
            {
                bool syllable = clientTick == 10 || clientTick == 30;
                PlayerInput c = syllable ? Command(clientTick, Syllable(clientTick)) : Command(clientTick);
                if (clientTick == 20)
                {
                    // Three ticks starve before command 20 arrives: the server repeats, then runs every later command late.
                    for (int k = 0; k < 3; k++) source.NextInput(++step, 0f, 0);
                }

                source.Accept(1, One(++s, c), step, 4);
                PlayerInput input = source.NextInput(++step, 0f, 0);
                if (input.GrammarEvents != null) placed.Add(input.GrammarEvents[0].Tick);
            }

            Assert.Equal(2, placed.Count);
            Assert.Equal(20, placed[1] - placed[0]);
        }

        [Fact]
        public void Clamp_View_Tick_Bounds()
        {
            int max = LagCompensation.MaxRewindTicks;
            Assert.Equal(9, max); // 150 ms at 60 ticks/s (spec L423)
            Assert.Equal(0, LagCompensation.ClampViewTick(0, 100, 99));
            Assert.Equal(0, LagCompensation.ClampViewTick(-5, 100, 99));
            Assert.Equal(99, LagCompensation.ClampViewTick(150, 100, 120));    // never the present or future
            Assert.Equal(95, LagCompensation.ClampViewTick(98, 100, 95));      // never a tick not yet shown
            Assert.Equal(100 - max, LagCompensation.ClampViewTick(10, 100, 99)); // never older than 150 ms
            Assert.Equal(0, LagCompensation.ClampViewTick(50, 100, 80));       // newest shown older than the window
        }

        [Fact]
        public void A_Maximal_Rewind_Claim_On_A_Fast_Link_Is_Replaced_By_The_Expected_View()
        {
            var source = new NetworkInputSource { OneWayTicks = 0, JitterTicks = 0, NewestShownTick = 1000 };
            source.BeginDuel(1);
            source.NewestShownTick = 1000;
            // An honest view: arrival 500, interpolation 3 -> expected 497.
            source.Accept(1, One(1, new PlayerInput { Tick = 501, ViewTick = 497 }), 500, 3);
            Assert.Equal(497, source.NextInput(501, 0f, 0).ViewTick);
            // A backtrack cheat claims the oldest rewindable tick.
            source.Accept(1, One(2, new PlayerInput { Tick = 502, ViewTick = 502 - 9 }), 501, 3);
            Assert.Equal(498, source.NextInput(502, 0f, 0).ViewTick);
            Assert.Equal(1, source.Stats.ViewsReplaced);
            Assert.Equal(1, source.ClampedViewTicks);
        }

        [Fact]
        public void View_Ticks_May_Not_Step_Back_And_Stand_Ins_Get_No_Lag_Compensation()
        {
            var source = new NetworkInputSource { OneWayTicks = 1, JitterTicks = 2, NewestShownTick = 1000 };
            source.BeginDuel(1);
            source.NewestShownTick = 1000;
            source.Accept(1, One(1, new PlayerInput { Tick = 501, ViewTick = 497 }), 500, 3);
            Assert.Equal(497, source.NextInput(501, 0f, 0).ViewTick);
            // Within tolerance of the expectation (497) but 3 ticks behind the previous view: held at previous - 1.
            source.Accept(1, One(2, new PlayerInput { Tick = 502, ViewTick = 494 }), 498, 3);
            Assert.Equal(496, source.NextInput(502, 0f, 0).ViewTick);
            Assert.Equal(1, source.Stats.ViewsBackward);
            // Nothing new arrives: the repeated stand-in carries no view.
            PlayerInput standIn = source.NextInput(503, 0f, 0);
            Assert.Equal(0, standIn.ViewTick);
            Assert.True(source.Stats.StandIns >= 1);
            // A reconnect restarts the monotonicity rule.
            source.ResetViewHistory();
            source.Accept(1, One(3, new PlayerInput { Tick = 504, ViewTick = 495 }), 499, 3);
            Assert.Equal(495, source.NextInput(504, 0f, 0).ViewTick);
        }

        [Fact]
        public void A_Rewind_Never_Crosses_A_Teleport()
        {
            var history = new HitVolumeHistory(2, 9);
            Capsule At(float x) => Capsule.Vertical(new Vec3(x, 0f, 0f), 1.8f, 0.4f);
            for (int t = 1; t <= 10; t++) history.Record(t, new[] { At(0.05f * t), At(0f) });
            history.Record(11, new[] { At(10.5f), At(0f) }); // a 10 m teleport
            for (int t = 12; t <= 14; t++) history.Record(t, new[] { At(10.5f), At(0f) });
            // A 5-tick-old view of player 0 would be the old position; the discontinuity holds it at the teleport tick.
            Assert.True(history.TryGet(0, 9, out Capsule volume, out int used));
            Assert.Equal(11, used);
            Assert.Equal(10.5f, volume.A.X, 3);
            // The other player is unaffected.
            Assert.True(history.TryGet(1, 9, out _, out int used1));
            Assert.Equal(9, used1);
            // Walking speed never trips the rule.
            Assert.Equal(11, history.DiscontinuityTick(0));
            Assert.Equal(int.MinValue, history.DiscontinuityTick(1));
        }
    }
}
