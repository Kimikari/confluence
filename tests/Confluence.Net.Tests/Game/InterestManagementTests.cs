using System;
using System.Collections.Generic;
using System.Linq;
using Confluence.Core.Rules;
using Confluence.Match;
using Confluence.Net.Game;
using Confluence.Net.Game.Client;
using Confluence.Net.Game.Headless;
using Confluence.Net.Game.Server;
using Confluence.Net.Game.Snapshots;
using Confluence.Net.Game.Wire;
using Confluence.Net.Protocol;
using Confluence.Net.Requests;
using Confluence.Net.Serialization;
using Confluence.Net.Sync;
using Confluence.Net.Tests.TestSupport;
using Xunit.Abstractions;

namespace Confluence.Net.Tests.Game
{
    /// <summary>
    /// Hidden information never leaves the server (spec L72, L391, L425, L472; research §2.6.6 leak tests): every message
    /// the server sends during a real 4-seat Conclave over UDP is recorded and checked per recipient.
    /// </summary>
    public sealed class InterestManagementTests
    {
        private readonly ITestOutputHelper _output;

        public InterestManagementTests(ITestOutputHelper output)
        {
            _output = output;
        }

        private sealed class Sent
        {
            public int Seat;
            public MessageType Type;
            public INetMessage Message;
            public int MatchTick;
            public bool RecipientInLiveDuel;
            public bool AnyDuelLive;

            /// <summary>For events: positions of events about an opponent hidden from the recipient that lie near it (must stay 0).</summary>
            public int HiddenLeaks;

            /// <summary>Read at send time: a snapshot's body buffer is reused by the next encode.</summary>
            public int DuelId;
            public byte[] ExpectedPrivate;
            public byte[] ActualPrivate;
        }

        private static byte[] Bytes(INetMessage m)
        {
            var w = new NetWriter(1024);
            m.Serialize(w);
            return w.ToArray();
        }

        [Fact]
        public void A_Conclave_Sends_No_Other_Pairs_Duels_No_Other_Seats_Economy_And_Only_Delayed_Spectator_Frames()
        {
            // The unranked 8-seat lobby with six headless clients and two bots: four duels per round, at least two of them
            // between two clients, so every cross-duel stream leak between human recipients is exercised every round.
            var settings = new GameServerSettings
            {
                Mode = MatchMode.Conclave,
                PlayerCount = 8,
                Bots = 2,
                Seed = EndToEndTests.SeedWithFirstDiameter(MatchMode.Conclave, 24, 500),
                Laboratory = true,
                ArenaPrefetchRounds = 2,
                BotWizards = new[] { "WIZ-002" },
            };
            var sent = new List<Sent>();
            var duelSides = new Dictionary<int, int[]>();
            using (var rig = new GameRig(settings))
            {
                GameServer server = rig.Server;
                server.Sent += (seat, type, message) =>
                {
                    var s = new Sent
                    {
                        Seat = seat,
                        Type = type,
                        Message = message,
                        MatchTick = server.CurrentTick,
                        RecipientInLiveDuel = server.IsInLiveDuel(seat),
                        AnyDuelLive = server.AnyDuelLive,
                        DuelId = DuelOf(message),
                    };
                    if (message is DuelEventsMessage batch) s.HiddenLeaks = HiddenLeaks(server, seat, batch);
                    if (message is ShopStateMessage shop)
                    {
                        s.ActualPrivate = Bytes(shop);
                        s.ExpectedPrivate = Bytes(EconomyViews.Shop(server.Host, seat));
                    }
                    else if (message is DraftStateMessage draft)
                    {
                        s.ActualPrivate = Bytes(draft);
                        s.ExpectedPrivate = Bytes(EconomyViews.Draft(server.Host, seat));
                    }
                    else if (message is DuelStartMessage start)
                    {
                        duelSides[start.DuelId] = new[] { start.Sides[0].Seat, start.Sides[1].Seat };
                    }

                    sent.Add(s);
                };

                string[] names = { "Ash", "Brook", "Cedar", "Dune", "Ember", "Fern" };
                var players = new List<HeadlessPlayer>();
                for (int i = 0; i < names.Length; i++) players.Add(rig.AddPlayer(names[i], i % 2 == 0 ? "WIZ-002" : "WIZ-010", seed: (ulong)(i + 1)));
                int opponentVitalsSeen = 0, foreignPrivate = 0, attachedWithPosition = 0;
                // Two full rounds: shop, draft, alcove, combat, results, and the second round's start.
                bool done = rig.RunUntil(() =>
                {
                    foreach (HeadlessPlayer p in players)
                    {
                        ClientDuel duel = p.Client.Duel;
                        if (duel?.Latest == null) continue;
                        if (duel.Latest.Players[duel.OpponentSide].HealthDeci != -1) opponentVitalsSeen++;
                        if (duel.LatestPrivate != null && duel.LatestPrivate.Motor.Position != duel.OwnAuthoritativePosition) foreignPrivate++;
                        foreach (EntityEntry e in duel.Latest.Entities)
                            if (EntityEntry.IsOwnerAttached(e.Kind) && !e.Position.Equals(default(QuantizedVec3))) attachedWithPosition++;
                    }

                    return players.All(p => p.Client.View.Results.Count(r => r.Round >= 2) >= 1) && server.Host.Flow.Round >= 3;
                }, 60 * 60 * 30);

                foreach (string m in server.Metrics) _output.WriteLine(m);
                _output.WriteLine("messages " + sent.Count + ", duels " + duelSides.Count + ", spectator frames " + server.SpectatorFramesSent);
                Assert.True(done, "two Conclave rounds did not complete");
                Assert.Equal(0, opponentVitalsSeen);
                Assert.Equal(0, foreignPrivate);
                Assert.Equal(0, attachedWithPosition);

                // At least two duels per round were between two clients (the cross-duel checks below cover them).
                int humanPairs = duelSides.Values.Count(sides => sides.All(seat => seat < names.Length));
                Assert.True(humanPairs >= 4, "client-vs-client duels " + humanPairs);

                int checkedDuelMessages = 0;
                int checkedPrivate = 0;
                int checkedSpectator = 0;
                int checkedForeignResults = 0;
                int checkedStandings = 0;
                var resolvedAt = new Dictionary<int, int>();
                // Resolution ticks from the authoritative match log, so a bot-vs-bot duel (whose seats receive nothing) is
                // aged from its real resolution too, not from the first nonparticipant delivery.
                foreach (DuelResolvedEvent e in server.Host.Flow.Events.OfType<DuelResolvedEvent>()) resolvedAt[e.Setup.DuelId] = e.Tick;
                var lastOutcome = new Dictionary<int, string>();
                int delayTicks = NetRules.SpectatorDelayMinTicks;
                foreach (Sent s in sent)
                {
                    int duelId = s.DuelId;
                    if (duelId >= 0 && !(s.Message is SpectatorFrameMessage))
                    {
                        // Snapshots, events, cosmetic cues, duel starts, and reports only for the recipient's own duels.
                        Assert.True(duelSides.ContainsKey(duelId), "unknown duel " + duelId + " in " + s.Type.Name + " to seat " + s.Seat + " at " + s.MatchTick +
                            " (known " + string.Join(",", duelSides.Keys) + ")");
                        Assert.Contains(s.Seat, duelSides[duelId]);
                        checkedDuelMessages++;
                    }

                    if (s.ExpectedPrivate != null)
                    {
                        Assert.Equal(s.ExpectedPrivate, s.ActualPrivate); // the recipient's own shop or draft, never another seat's
                        checkedPrivate++;
                    }

                    if (s.Message is SpectatorFrameMessage frame)
                    {
                        Assert.False(s.RecipientInLiveDuel, "a live duelist received a spectator frame");
                        Assert.DoesNotContain(s.Seat, new[] { frame.Side0Seat, frame.Side1Seat });
                        var r = new NetReader(frame.FrameBody);
                        DuelFrame f = SnapshotCodec.DecodeBody(r, new SnapshotHeader(frame.FrameTick, 0), null, out PrivateEntry own);
                        Assert.Null(own);
                        Assert.True(s.MatchTick - f.MatchTick >= delayTicks, "spectator frame only " + (s.MatchTick - f.MatchTick) + " ticks old");
                        checkedSpectator++;
                    }

                    if (s.Message is DuelResultMessage result)
                    {
                        bool participant = s.Seat == result.Side0Seat || s.Seat == result.Side1Seat;
                        if (participant)
                        {
                            if (!resolvedAt.ContainsKey(result.DuelId)) resolvedAt[result.DuelId] = s.MatchTick;
                        }
                        else
                        {
                            // Other pairs' results reach every nonparticipant (fighting or not, L49 "delayed information")
                            // only when 15 s old or when no duel of the pass is live (§2.6.4).
                            if (s.AnyDuelLive && resolvedAt.TryGetValue(result.DuelId, out int at))
                            {
                                Assert.True(s.MatchTick - at >= delayTicks, "seat " + s.Seat + " got duel " + result.DuelId + "'s result " + (s.MatchTick - at) + " ticks after it resolved");
                            }

                            Assert.False(s.RecipientInLiveDuel && s.MatchTick - (resolvedAt.TryGetValue(result.DuelId, out int t0) ? t0 : s.MatchTick) < delayTicks && s.AnyDuelLive,
                                "a live duelist got another pair's fresh result");
                            checkedForeignResults++;
                        }
                    }

                    if (s.Message is StandingsMessage standings)
                    {
                        // Standings never carry an outcome a live duelist may not know yet: what a live recipient receives
                        // has the same outcome columns as the standings it held before.
                        string outcome = OutcomeColumns(standings);
                        if (s.RecipientInLiveDuel && lastOutcome.TryGetValue(s.Seat, out string before)) Assert.Equal(before, outcome);
                        lastOutcome[s.Seat] = outcome;
                        checkedStandings++;
                    }

                    if (s.HiddenLeaks > 0) Assert.Fail("seat " + s.Seat + " received " + s.HiddenLeaks + " event positions near a hidden opponent at " + s.MatchTick);

                    if (s.Message is DuelEventsMessage events)
                    {
                        foreach (WireDuelEvent e in events.Events)
                        {
                            if (e.Type != WireEventType.Damage) continue;
                            int side = duelSides[events.DuelId][0] == s.Seat ? 0 : 1;
                            if (e.Player != side && e.Target != side) Assert.Equal(0f, e.Amount + e.Amount2); // third-party damage has no amounts
                        }
                    }
                }

                _output.WriteLine("checked duel messages " + checkedDuelMessages + ", private " + checkedPrivate + ", spectator " + checkedSpectator +
                                  ", foreign results " + checkedForeignResults + ", standings " + checkedStandings);
                Assert.True(checkedDuelMessages > 1000);
                Assert.True(checkedPrivate > 4);
                Assert.True(checkedForeignResults > 10);
                Assert.True(checkedStandings > 6);
            }
        }

        private static string OutcomeColumns(StandingsMessage m) =>
            string.Join(";", m.Seats.OrderBy(s => s.Seat).Select(s => s.Seat + ":" + s.Resolve + "/" + s.CombatWins + "/" + s.Losses + "/" + s.Draws + "/" + s.DuelHalfWins + "/" + s.Eliminated));

        /// <summary>
        /// Positions in an events batch for <paramref name="seat"/> that would locate an opponent hidden from it right now
        /// (spec L120): events about the opponent with a position within the conceal radius of its live position.
        /// </summary>
        private static int HiddenLeaks(GameServer server, int seat, DuelEventsMessage batch)
        {
            if (server.Host == null) return 0;
            foreach (Confluence.Hosting.Duels.RunningDuel duel in server.Host.Duels.Running)
            {
                if (duel.DuelId != batch.DuelId) continue;
                int side = duel.Seats[0].Player == seat ? 0 : duel.Seats[1].Player == seat ? 1 : -1;
                if (side < 0) return 0;
                int opponent = 1 - side;
                Confluence.Sim.Duel.DuelWorld world = duel.World;
                if (world.IsVisibleTo(side, opponent)) return 0;
                Confluence.Core.Geometry.Vec3 at = world.Player(opponent).Position;
                int leaks = 0;
                foreach (WireDuelEvent e in batch.Events)
                {
                    bool about = e.Player == opponent || (e.Type == WireEventType.Damage && e.Target == opponent);
                    if (about && e.PositionSet && Confluence.Core.Geometry.Vec3.Distance(e.Position, at) <= DuelEventFilter.ConcealRadiusMeters) leaks++;
                }

                return leaks;
            }

            return 0;
        }

        /// <summary>The duel a message belongs to, or -1 for match-level messages.</summary>
        private static int DuelOf(INetMessage m)
        {
            switch (m)
            {
                case SnapshotMessage snapshot:
                    return (int)new NetReader(snapshot.Body, 0, snapshot.BodyLength >= 0 ? snapshot.BodyLength : snapshot.Body.Length).ReadVarUInt();
                case DuelEventsMessage events:
                    return events.DuelId;
                case DuelStartMessage start:
                    return start.DuelId;
                case DuelReportMessage report:
                    return report.DuelId;
                case SpectatorFrameMessage frame:
                    return frame.DuelId;
                default:
                    return -1;
            }
        }

        [Fact]
        public void Client_To_Server_Messages_Carry_Intents_Only()
        {
            // Research §2.6.5: no client message may claim a position, an outcome, or a resource.
            // (CommandRequest.Position is a card position, an index; world positions are caught by their types.)
            string[] forbidden = { "Health", "Mana", "Stamina", "Crowns", "Damage", "Winner", "Outcome", "Result", "Resolve", "Shield", "Price", "Velocity" };
            Type[] forbiddenTypes =
            {
                typeof(Confluence.Core.Geometry.Vec3), typeof(QuantizedVec3), typeof(Confluence.Sim.Movement.MotorState), typeof(DuelFrame), typeof(PrivateEntry),
            };
            var bodies = new List<Type>();
            foreach (System.Reflection.FieldInfo f in typeof(GameProtocol).GetFields())
            {
                object value = f.GetValue(null);
                if (value is MessageType t && t.Direction == MessageDirection.ClientToServer) bodies.Add(t.BodyType);
                else if (value != null && value.GetType().IsGenericType && value.GetType().GetGenericTypeDefinition() == typeof(RequestType<,>))
                {
                    bodies.Add(value.GetType().GetGenericArguments()[0]);
                }
            }

            bodies.Add(typeof(Confluence.Sim.Input.PlayerInput));
            Assert.Contains(typeof(DuelInputMessage), bodies);
            Assert.Contains(typeof(CommandRequest), bodies);
            foreach (Type body in bodies.Distinct())
            {
                var members = body.GetProperties().Select(p => new KeyValuePair<string, Type>(p.Name, p.PropertyType))
                    .Concat(body.GetFields().Select(fi => new KeyValuePair<string, Type>(fi.Name, fi.FieldType)));
                foreach (KeyValuePair<string, Type> member in members)
                {
                    foreach (string word in forbidden) Assert.False(member.Key.Contains(word), body.Name + "." + member.Key + " looks like an authority field");
                    Assert.DoesNotContain(member.Value, forbiddenTypes);
                }
            }
        }
    }
}
