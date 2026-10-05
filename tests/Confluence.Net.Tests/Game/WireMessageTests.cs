using System;
using System.Collections.Generic;
using Confluence.Casting;
using Confluence.Casting.Grammars;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Economy;
using Confluence.Economy.Augments;
using Confluence.Economy.Shop;
using Confluence.Hosting.Commands;
using Confluence.Match;
using Confluence.Net.Game;
using Confluence.Net.Game.Snapshots;
using Confluence.Net.Game.Wire;
using Confluence.Net.Protocol;
using Confluence.Net.Requests;
using Confluence.Net.Serialization;
using Confluence.Net.Sync;
using Confluence.Sim.Input;

namespace Confluence.Net.Tests.Game
{
    /// <summary>
    /// Every game message round-trips byte for byte, and the registry rejects truncated, padded, and out-of-range bodies
    /// as malformed before any handler runs (spec L429 "rejects impossible ... values").
    /// </summary>
    public sealed class WireMessageTests
    {
        private static byte[] Encode(INetMessage message)
        {
            var w = new NetWriter(256);
            message.Serialize(w);
            return w.ToArray();
        }

        private static T Decode<T>(byte[] bytes) where T : INetMessage, new()
        {
            var r = new NetReader(bytes);
            var m = new T();
            m.Deserialize(r);
            r.ExpectEnd();
            return m;
        }

        /// <summary>Round trip, then every strict prefix and a padded copy must fail to decode through the registry.</summary>
        private static T RoundTrip<T>(T message, bool checkPrefixes = true) where T : INetMessage, new()
        {
            byte[] bytes = Encode(message);
            T decoded = Decode<T>(bytes);
            Assert.Equal(bytes, Encode(decoded));

            var type = new MessageType<T>(200, "test." + typeof(T).Name, NetChannel.Match, MessageDirection.Both);
            var registry = new MessageRegistry<object>(true);
            int handled = 0;
            registry.On(type, (s, m) => handled++);
            Assert.Equal(DispatchResult.Handled, Dispatch(registry, type, bytes));
            if (checkPrefixes)
            {
                for (int n = 0; n < bytes.Length; n++)
                {
                    var prefix = new byte[n];
                    Array.Copy(bytes, prefix, n);
                    Assert.Equal(DispatchResult.Malformed, Dispatch(registry, type, prefix));
                }
            }

            var padded = new byte[bytes.Length + 1];
            Array.Copy(bytes, padded, bytes.Length);
            Assert.Equal(DispatchResult.Malformed, Dispatch(registry, type, padded));
            Assert.Equal(1, handled);
            return decoded;
        }

        private static DispatchResult Dispatch<T>(MessageRegistry<object> registry, MessageType<T> type, byte[] body) where T : INetMessage, new()
        {
            var w = new NetWriter(body.Length + 4);
            w.WriteVarUInt(type.Id);
            w.WriteBytes(body);
            return registry.Dispatch(null, new NetReader(w.ToArray()), NetChannel.Match).Result;
        }

        private static void AssertMalformed<T>(byte[] bytes) where T : INetMessage, new()
        {
            Assert.Throws<NetReadException>(() => Decode<T>(bytes));
        }

        private static PublicLoadout Loadout() =>
            new PublicLoadout("WIZ-002", new[] { "SPL-009", "SPL-010" }, new[] { "EQP-051" }, new[] { "AUG-145" }, new[] { 2 });

        [Fact]
        public void Lobby_And_Settings_Round_Trip()
        {
            RoundTrip(new JoinLobbyRequest { WizardId = "WIZ-010", Route = InputRoute.Guided });
            RoundTrip(new JoinLobbyResponse { Seat = 3, Detail = "joined" });
            var lobby = new LobbyStateMessage { Mode = MatchMode.Conclave, PlayerCount = 4, Status = LobbyStatus.Waiting, LaboratoryLabel = "laboratory:x", SettingsFingerprint = "0123456789ABCDEF", Notice = "n" };
            lobby.Seats.Add(new LobbySeatWire { Seat = 0, Name = "Ash", WizardId = "WIZ-002", Joined = true, Connected = true });
            lobby.Seats.Add(new LobbySeatWire { Seat = 3, Name = "Bot 4", IsBot = true, Joined = true });
            lobby.Wizards.Add(new WizardChoiceWire { WizardId = "WIZ-002", Name = "Bram Cinder", Routes = InputRoute.Standard, KitImplemented = true });
            LobbyStateMessage l = RoundTrip(lobby);
            Assert.Equal("Bram Cinder", l.Wizards[0].Name);
            Assert.Equal("0123456789ABCDEF", l.SettingsFingerprint);
            Assert.True(l.Seats[1].IsBot);

            var settings = new MatchSettingsMessage { YourSeat = 1, Mode = MatchMode.Duel, HostFingerprint = "AB", ShopTicks = 2100, DraftTicks = 1800, AlcoveTicks = 300, CombatTicks = 5400, ResultsTicks = 480, TicksPerWallSecond = 240, Accelerated = true };
            settings.Seats.Add(new SeatWire { Seat = 0, Name = "A", WizardId = "WIZ-002" });
            MatchSettingsMessage s = RoundTrip(settings);
            Assert.Equal(240, s.TicksPerWallSecond);
            Assert.True(s.Accelerated);
        }

        [Fact]
        public void Match_Flow_Messages_Round_Trip()
        {
            PhaseMessage phase = RoundTrip(new PhaseMessage { Phase = MatchPhase.Draft, Round = 5, BracketRound = 0, Attempt = 1, StartTick = 12345, EndTick = 14145 });
            Assert.Equal(5, phase.Round);
            Assert.Equal(MatchPhase.Draft, phase.Phase);
            RoundTrip(new PhaseMessage { Phase = MatchPhase.Finished, Round = 3, EndTick = -1 });
            RoundRevealMessage reveal = RoundTrip(new RoundRevealMessage { Round = 3, PassKind = PassKind.DrawReplay, OpponentSeat = 2, Side = 1, OwnResolve = 6, OpponentResolve = 4, ArenaId = "arena:r3.a0:00FF", OpponentLoadout = Loadout() });
            Assert.Equal(2, reveal.OpponentLoadout.AugmentRanks[0]);
            Assert.True(RoundTrip(new RoundRevealMessage { Round = 1, OpponentSeat = -1, Side = -1 }).IsBye);

            var arena = new ArenaMessage { ArenaId = "arena:r1.a0:01", Round = 1, RecipeSeed = 0xFEEDFACE12345678UL, Diameter = 24, BiomeId = "BIO-001", LayoutHash = 77UL, BiomeName = "Raincourt", EnvironmentRule = "rain", FinalFloorRadius = 4f };
            arena.AvailablePropIds.Add("PROP-001");
            arena.Spawns.Add(new SpawnWire { Side = 0, Position = new Vec3(1, 2, 3), YawDeg = 90f });
            arena.Modules.Add(new PreviewEntryWire { Id = "MOD-001", Name = "m", Category = "c", Position = new Vec3(4, 0, 4), YawDeg = 45f });
            arena.Props.Add(new PreviewEntryWire { Id = "PROP-001", Name = "p", Category = "Conductive", Effect = "conducts", Position = new Vec3(1, 0, 1) });
            arena.Shapes.Add(new PreviewShapeWire { Kind = 3, Center = new Vec2(1, 1), HalfExtents = new Vec2(2, 3), TopY = 1.5f, Material = "stone", Walkable = true });
            arena.Materials.Add(new MaterialNoteWire { Key = "water", Description = "shallow water", Count = 4 });
            ArenaMessage a = RoundTrip(arena);
            Assert.Equal(0xFEEDFACE12345678UL, a.RecipeSeed);
            Assert.Equal(24, a.Diameter);

            RoundTrip(new FinalLoadoutsMessage { Round = 2, OpponentSeat = 1, Own = Loadout(), Opponent = Loadout() });
            var standings = new StandingsMessage { Round = 4 };
            standings.Seats.Add(new StandingWire { Seat = 0, Resolve = 5, CombatWins = 2, Losses = 1, Eliminated = false, Connected = true });
            standings.Seats.Add(new StandingWire { Seat = 1, Resolve = -1, Eliminated = true });
            RoundTrip(standings);
            RoundTrip(new DuelResultMessage { DuelId = 9, Round = 2, Side0Seat = 1, Side1Seat = 0, WinnerSeat = 0, Reason = Confluence.Match.DuelEndReason.Time, Health0 = 0.4f, Health1 = 0.9f });
            var end = new MatchEndMessage { ChampionSeat = -1, DuelHalfWins0 = 7, DuelHalfWins1 = 7 };
            end.Placements.Add(new PlacementWire { Seat = 0, FirstPosition = 1, LastPosition = 2 });
            RoundTrip(end);
            RoundTrip(new NoticeMessage { Text = "Laboratory match" });
        }

        [Fact]
        public void Economy_Messages_Round_Trip()
        {
            var shop = new ShopStateMessage { Round = 2, Open = true, Crowns = 1100, Ready = false, HighestTier = 2 };
            var card = new ShopCardWire { Position = 1, State = ShopCardState.Offered, DefinitionId = "EQP-051", Price = 300, Tier = 1, RerollAvailable = true };
            card.Slots.Add(EquipmentSlot.TrinketA);
            card.Slots.Add(EquipmentSlot.TrinketB);
            shop.Cards.Add(card);
            shop.Items.Add(new OwnedItemWire { InstanceId = 7, DefinitionId = "EQP-066", Slot = (int)EquipmentSlot.Boots, SellValue = 150, IsActiveItem = true, ActivationEnabled = true });
            shop.Items.Add(new OwnedItemWire { InstanceId = 8, DefinitionId = "EQP-096", Slot = -1, Charges = 1 });
            shop.KitSpellIds.Add("SPL-009");
            shop.EquippedSpellIds.Add("SPL-009");
            shop.Augments.Add(new OwnedAugmentWire { Slot = 0, AugmentId = "AUG-145", Rank = 1, Dormant = true });
            ShopStateMessage s = RoundTrip(shop);
            Assert.Equal(EquipmentSlot.TrinketB, s.Cards[0].Slots[1]);
            Assert.Equal(-1, s.Items[1].Slot);

            var draft = new DraftStateMessage { Round = 2, Open = true, Rarity = Rarity.Gold, Outcome = DraftOutcome.Open, SelectedAugmentId = null };
            draft.Cards.Add(new DraftCardWire { Position = 4, AugmentId = "AUG-169", Rank = 2, IsUpgrade = true, Rarity = Rarity.Gold, TargetSpellId = "SPL-009", RerollAvailable = false, EligibilityText = "needs fire" });
            RoundTrip(draft);

            foreach (CommandRequest c in new[]
            {
                CommandRequest.Buy(2), CommandRequest.Buy(0, EquipmentSlot.Armor), CommandRequest.Buy(1, null, true), CommandRequest.Sell(12),
                CommandRequest.Reroll(1), CommandRequest.SelectSpells(new[] { "SPL-009", "SPL-010" }), CommandRequest.Ready(),
                CommandRequest.DraftReroll(3), CommandRequest.DraftSelect(2, 5, true), CommandRequest.DraftPass(), CommandRequest.Surrender(),
                new CommandRequest { Kind = CommandKind.ShopSetActive, ClearActive = true },
                new CommandRequest { Kind = CommandKind.ShopEquipFromReserve, InstanceId = 3, Slot = EquipmentSlot.Sidearm },
            })
            {
                CommandRequest d = RoundTrip(c);
                Assert.Equal(c.ToCommand(1, 42).CanonicalText(), d.ToCommand(1, 42).CanonicalText());
            }

            RoundTrip(new CommandResponse { Code = CommandCode.TransactionRejected, Transaction = TransactionCode.InsufficientCrowns, Duplicate = true });
        }

        [Fact]
        public void Duel_Messages_Round_Trip()
        {
            var start = new DuelStartMessage { DuelId = 4, Round = 2, ArenaId = "arena:r2.a0:01", YourSide = 1, AlcoveTicks = 300, CombatTicks = 5400, StartMatchTick = 9000 };
            for (int s = 0; s < 2; s++)
            {
                start.Sides[s].Seat = s;
                start.Sides[s].Name = "P" + s;
                start.Sides[s].WizardId = "WIZ-002";
                start.Sides[s].SpellIds.Add("SPL-009");
                start.Sides[s].EquipmentIds.Add("EQP-051");
                start.Sides[s].EquipmentSlots.Add(EquipmentSlot.TrinketA);
                start.Sides[s].AugmentIds.Add("AUG-145");
                start.Sides[s].AugmentRanks.Add(2);
                start.Sides[s].Alcove = new YawBox(new Vec3(0, 1.5f, -6), new Vec3(2, 1.5f, 2), 30f);
            }

            DuelStartMessage d = RoundTrip(start);
            Assert.Equal(30f, d.Sides[1].Alcove.YawDeg);
            Assert.Equal(9000, d.StartMatchTick);

            var events = new DuelEventsMessage { DuelId = 4 };
            events.Events.Add(new WireDuelEvent { Id = 10, Tick = 400, Type = WireEventType.Damage, Player = 0, Target = 1, Kind = 0, SourceId = "SPL-009", Amount = 24f, Amount2 = 6f, PositionSet = true, Position = new Vec3(1, 1, 1), EntityId = 3, Extra = 256 });
            events.Events.Add(new WireDuelEvent { Id = 11, Tick = 401, Type = WireEventType.Reaction, Player = -1, Kind = 1, DirectionSet = true, Direction = new Vec3(0, 0, 1), Text = "steam" });
            RoundTrip(events);

            var report = new DuelReportMessage { DuelId = 4, WinnerSide = -1, Reason = 3, Health0 = 0f, Health1 = 0f, FinalTick = 5700 };
            report.Feats[0].Add("FEAT-001");
            report.Decisive[1].Add(new WireDuelEvent { Id = 3, Tick = 5000, Type = WireEventType.Melee, Player = 0, Target = 1, Text = "focus" });
            RoundTrip(report);

            var spectator = new SpectatorFrameMessage { DuelId = 2, Side0Seat = 3, Side1Seat = 5, FrameTick = 1200, FrameBody = new byte[] { 1, 2, 3 } };
            spectator.Events.Add(new WireDuelEvent { Id = 1, Tick = 2, Type = WireEventType.Phase, Kind = 1 });
            RoundTrip(spectator);
            RoundTrip(new SpectateRequest { DuelId = -1 });
            RoundTrip(new SpectateRequest { DuelId = 12 });
        }

        [Fact]
        public void Input_Packets_Round_Trip_With_Grammar_Events_And_View_Ticks()
        {
            var msg = new DuelInputMessage { DuelId = 3, AckSnapshotTick = 420 };
            var a = new PlayerInput { Tick = 500, MoveX = 0.5f, MoveY = -1f, YawDeg = 271f, PitchDeg = -12f, Buttons = InputButtons.Primary | InputButtons.Sprint, SelectedSlot = 2, ViewTick = 494 };
            var b = new PlayerInput
            {
                Tick = 501,
                MoveX = 1f,
                YawDeg = 10f,
                Buttons = InputButtons.Guard,
                Tapped = InputButtons.Melee,
                SelectedSlot = 5,
                GrammarEvents = new[]
                {
                    new GrammarInputEvent(GrammarInputKind.Syllable, 500, 250, 3) { Confidence = 0.8f },
                    GrammarInputEvent.Stroke(501, 999, 0.25f, 0.75f),
                    new GrammarInputEvent(GrammarInputKind.DirectionTrace, 501, 0, (int)TraceDirection.UpLeft),
                },
            };
            msg.Commands.Add(new InputCommand<PlayerInput>(7, a));
            msg.Commands.Add(new InputCommand<PlayerInput>(8, b));
            DuelInputMessage d = RoundTrip(msg);
            Assert.Equal(2, d.Commands.Count);
            Assert.Equal(8u, d.Commands[1].Sequence);
            PlayerInput a2 = d.Commands[0].Input;
            PlayerInput b2 = d.Commands[1].Input;
            Assert.Equal(494, a2.ViewTick);
            Assert.Equal(InputButtons.Melee, b2.Tapped);
            Assert.Equal(3, b2.GrammarEvents.Count);
            Assert.Equal(GrammarInputKind.StrokeSample, b2.GrammarEvents[1].Kind);
            Assert.Equal(999, b2.GrammarEvents[1].SubTick);
            Assert.InRange(b2.GrammarEvents[1].X, 0.2499f, 0.2501f);
            Assert.InRange(b2.GrammarEvents[0].Confidence, 0.79f, 0.81f);
            Assert.Equal(500, b2.GrammarEvents[0].Tick);
            Assert.InRange(a2.MoveX, 0.5f - 1f / 254f, 0.5f + 1f / 254f);
        }

        [Fact]
        public void Canonical_Inputs_Are_Stable_And_Exactly_What_The_Server_Decodes()
        {
            var raw = new PlayerInput { Tick = 77, MoveX = 0.3333f, MoveY = 0.71f, YawDeg = -45.17f, PitchDeg = 33.3f, Buttons = InputButtons.Jump, SelectedSlot = 1, ViewTick = 70 };
            PlayerInput c = PlayerInputCodec.Canonicalize(raw);
            PlayerInput cc = PlayerInputCodec.Canonicalize(c);
            Assert.Equal(c.MoveX, cc.MoveX);
            Assert.Equal(c.MoveY, cc.MoveY);
            Assert.Equal(c.YawDeg, cc.YawDeg);
            Assert.Equal(c.PitchDeg, cc.PitchDeg);
            var msg = new DuelInputMessage { DuelId = 1 };
            msg.Commands.Add(new InputCommand<PlayerInput>(1, raw));
            PlayerInput decoded = Decode<DuelInputMessage>(Encode(msg)).Commands[0].Input;
            Assert.Equal(c.MoveX, decoded.MoveX);
            Assert.Equal(c.YawDeg, decoded.YawDeg);
            Assert.Equal(c.PitchDeg, decoded.PitchDeg);
            Assert.Equal(70, decoded.ViewTick);
        }

        [Fact]
        public void Impossible_Values_Are_Malformed()
        {
            // Two input routes at once.
            var w = new NetWriter();
            w.WriteId("WIZ-002");
            w.WriteByte((byte)(InputRoute.Standard | InputRoute.Guided));
            AssertMalformed<JoinLobbyRequest>(w.ToArray());

            // Undefined input buttons, a slot past 6, an axis of -128, a view lag over the bound.
            AssertMalformed<DuelInputMessage>(InputBytes(buttons: 1u << 25));
            AssertMalformed<DuelInputMessage>(InputBytes(slot: 6));
            AssertMalformed<DuelInputMessage>(InputBytes(axis: -128));
            AssertMalformed<DuelInputMessage>(InputBytes(viewLag: PlayerInputCodec.MaxViewLagTicks + 1));
            Assert.NotNull(Decode<DuelInputMessage>(InputBytes()));

            // An undefined phase, an inverted placement range, a purchase into both a slot and the reserve.
            w = new NetWriter();
            new PhaseMessage { Phase = MatchPhase.Shop, Round = 1 }.Serialize(w);
            byte[] phase = w.ToArray();
            phase[0] = 42;
            AssertMalformed<PhaseMessage>(phase);
            var end = new MatchEndMessage();
            end.Placements.Add(new PlacementWire { Seat = 0, FirstPosition = 3, LastPosition = 2 });
            AssertMalformed<MatchEndMessage>(Encode(end));
            AssertMalformed<CommandRequest>(Encode(new CommandRequest { Kind = CommandKind.ShopBuy, Position = 0, Slot = EquipmentSlot.Armor, ToReserve = true }));

            // A non-finite float in a fixed-point-free field.
            var result = new DuelResultMessage { DuelId = 1, Health0 = float.NaN };
            AssertMalformed<DuelResultMessage>(Encode(result));

            // A forged list count larger than the remaining bytes never allocates.
            w = new NetWriter();
            w.WriteVarUInt(1);
            w.WriteVarUInt(200);
            AssertMalformed<DuelEventsMessage>(w.ToArray());
        }

        private static byte[] InputBytes(uint buttons = 1, int slot = 0, int axis = 0, int viewLag = -1)
        {
            var w = new NetWriter();
            w.WriteVarUInt(1);         // duel
            w.WriteVarInt(-1);         // ack
            w.WriteByte(6);            // interpolation ticks
            w.WriteVarUInt(0);         // snapshots received
            w.WriteVarUInt(1);         // newest sequence
            w.WriteVarUInt(1);         // count
            w.WriteVarInt(700);        // tick
            w.WriteSByte((sbyte)axis); // move x
            w.WriteSByte(0);           // move y
            w.WriteUShort(0);
            w.WriteUShort(32768);
            w.WriteVarUInt(buttons);
            w.WriteByte((byte)(viewLag >= 0 ? 1 : 0));
            w.WriteByte((byte)slot);
            if (viewLag >= 0) w.WriteVarUInt((uint)viewLag);
            return w.ToArray();
        }

        [Fact]
        public void Every_Game_Message_Type_Has_A_Unique_Id_At_Or_Above_The_Game_Range()
        {
            var ids = new HashSet<ushort>();
            foreach (System.Reflection.FieldInfo f in typeof(GameProtocol).GetFields())
            {
                object value = f.GetValue(null);
                if (value is MessageType t)
                {
                    Assert.True(t.Id >= MessageType.FirstGameMessageId);
                    Assert.True(ids.Add(t.Id), "duplicate id " + t.Id);
                }
                else if (value != null && value.GetType().IsGenericType && value.GetType().GetGenericTypeDefinition() == typeof(RequestType<,>))
                {
                    var request = (MessageType)value.GetType().GetProperty("RequestMessage").GetValue(value);
                    var response = (MessageType)value.GetType().GetProperty("ResponseMessage").GetValue(value);
                    Assert.True(ids.Add(request.Id), "duplicate id " + request.Id);
                    Assert.True(ids.Add(response.Id), "duplicate id " + response.Id);
                }
            }

            Assert.Equal(33, ids.Count); // ids 64..96
        }
    }
}
