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
            var settings = new GameServerSettings
            {
                Mode = MatchMode.Conclave,
                PlayerCount = 4,
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
                        DuelId = DuelOf(message),
                    };
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

                HeadlessPlayer a = rig.AddPlayer("Ash", "WIZ-002", seed: 1);
                HeadlessPlayer b = rig.AddPlayer("Brook", "WIZ-010", seed: 2);
                var opponentVitalsSeen = new int[2];
                var foreignPrivate = new int[2];
                // Two full rounds: shop, draft, alcove, combat, results, and the second round's start.
                bool done = rig.RunUntil(() =>
                {
                    foreach (HeadlessPlayer p in new[] { a, b })
                    {
                        ClientDuel duel = p.Client.Duel;
                        if (duel?.Latest == null) continue;
                        int index = p == a ? 0 : 1;
                        if (duel.Latest.Players[duel.OpponentSide].HealthDeci != -1) opponentVitalsSeen[index]++;
                        if (duel.LatestPrivate != null && duel.LatestPrivate.Motor.Position != duel.OwnAuthoritativePosition) foreignPrivate[index]++;
                    }

                    return a.Client.View.Results.Count(r => r.Round >= 2) >= 1 && b.Client.View.Results.Count(r => r.Round >= 2) >= 1 &&
                        server.Host.Flow.Round >= 3;
                }, 60 * 60 * 20);

                foreach (string m in server.Metrics) _output.WriteLine(m);
                _output.WriteLine("messages " + sent.Count + ", duels " + duelSides.Count + ", spectator frames " + server.SpectatorFramesSent);
                Assert.True(done, "two Conclave rounds did not complete");
                Assert.Equal(0, opponentVitalsSeen[0] + opponentVitalsSeen[1]);
                Assert.Equal(0, foreignPrivate[0] + foreignPrivate[1]);

                int checkedDuelMessages = 0;
                int checkedPrivate = 0;
                int checkedSpectator = 0;
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

                    if (s.Message is DuelResultMessage result && s.RecipientInLiveDuel)
                    {
                        Assert.Contains(s.Seat, new[] { result.Side0Seat, result.Side1Seat }); // other pairs' results wait (§2.6.4)
                    }

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

                _output.WriteLine("checked duel messages " + checkedDuelMessages + ", private " + checkedPrivate + ", spectator " + checkedSpectator);
                Assert.True(checkedDuelMessages > 1000);
                Assert.True(checkedPrivate > 4);
            }
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
