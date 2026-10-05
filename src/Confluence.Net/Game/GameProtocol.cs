using System;
using Confluence.Net.Game.Snapshots;
using Confluence.Net.Game.Wire;
using Confluence.Net.Protocol;
using Confluence.Net.RateLimiting;
using Confluence.Net.Requests;

namespace Confluence.Net.Game
{
    /// <summary>
    /// Every game message of the Confluence protocol (ids from <see cref="MessageType.FirstGameMessageId"/>), shared by
    /// server and clients. Lanes: lobby and match flow on Match (reliable ordered), shop and draft transactions on their
    /// own lanes so a slow purchase never stalls combat events, duel events on Events, inputs and snapshots sequenced,
    /// large snapshots and spectator frames on Bulk (reliable, fragmented). Bump <see cref="NetRules.ProtocolVersion"/>
    /// whenever a layout changes (spec L427 unsupported-version state).
    /// </summary>
    public static class GameProtocol
    {
        /// <summary>
        /// Input packets arrive once per tick (60/s); the limit allows about twice that (1 per 8 ms) with a burst of 32 for
        /// catch-up after a frame hitch (research §2.6.7). The server scales it for the labeled accelerated pace.
        /// </summary>
        public static readonly RateLimit InputLimit = new RateLimit(32, 8);

        /// <summary>The input limit at a pace of <paramref name="ticksPerWallSecond"/> (unlimited beyond 4x: a labeled test mode).</summary>
        public static RateLimit InputLimitAt(int ticksPerWallSecond)
        {
            int factor = Math.Max(1, ticksPerWallSecond / Confluence.Core.Time.Ticks.PerSecond);
            if (factor == 1) return InputLimit;
            if (factor > 4) return RateLimit.Unlimited;
            return new RateLimit(InputLimit.Burst * factor, Math.Max(1, InputLimit.RefillIntervalMs / factor));
        }

        public static readonly RequestType<JoinLobbyRequest, JoinLobbyResponse> JoinLobby =
            new RequestType<JoinLobbyRequest, JoinLobbyResponse>(64, 65, "lobby.join", NetChannel.Match, NetRules.Transactions);

        public static readonly MessageType<LobbyStateMessage> LobbyState =
            new MessageType<LobbyStateMessage>(66, "lobby.state", NetChannel.Match, MessageDirection.ServerToClient);

        public static readonly MessageType<MatchSettingsMessage> MatchSettings =
            new MessageType<MatchSettingsMessage>(67, "match.settings", NetChannel.Match, MessageDirection.ServerToClient);

        public static readonly MessageType<PhaseMessage> Phase =
            new MessageType<PhaseMessage>(68, "match.phase", NetChannel.Match, MessageDirection.ServerToClient);

        public static readonly MessageType<RoundRevealMessage> RoundReveal =
            new MessageType<RoundRevealMessage>(69, "match.reveal", NetChannel.Match, MessageDirection.ServerToClient);

        public static readonly MessageType<ArenaMessage> Arena =
            new MessageType<ArenaMessage>(70, "match.arena", NetChannel.Match, MessageDirection.ServerToClient);

        public static readonly MessageType<FinalLoadoutsMessage> FinalLoadouts =
            new MessageType<FinalLoadoutsMessage>(71, "match.final_loadouts", NetChannel.Match, MessageDirection.ServerToClient);

        public static readonly MessageType<StandingsMessage> Standings =
            new MessageType<StandingsMessage>(72, "match.standings", NetChannel.Match, MessageDirection.ServerToClient);

        public static readonly MessageType<DuelResultMessage> DuelResult =
            new MessageType<DuelResultMessage>(73, "match.duel_result", NetChannel.Match, MessageDirection.ServerToClient);

        public static readonly MessageType<MatchEndMessage> MatchEnd =
            new MessageType<MatchEndMessage>(74, "match.end", NetChannel.Match, MessageDirection.ServerToClient);

        public static readonly MessageType<NoticeMessage> Notice =
            new MessageType<NoticeMessage>(75, "match.notice", NetChannel.Match, MessageDirection.ServerToClient);

        public static readonly MessageType<ShopStateMessage> ShopState =
            new MessageType<ShopStateMessage>(76, "shop.state", NetChannel.Shop, MessageDirection.ServerToClient);

        public static readonly MessageType<DraftStateMessage> DraftState =
            new MessageType<DraftStateMessage>(77, "draft.state", NetChannel.Draft, MessageDirection.ServerToClient);

        /// <summary>Purchases, sales, moves, spell selection, active choice, ready (spec L429 transactions).</summary>
        public static readonly RequestType<CommandRequest, CommandResponse> ShopCommand =
            new RequestType<CommandRequest, CommandResponse>(78, 79, "shop.command", NetChannel.Shop, NetRules.Transactions);

        /// <summary>Shop card rerolls (spec L429 "rate-limits rerolls").</summary>
        public static readonly RequestType<CommandRequest, CommandResponse> ShopReroll =
            new RequestType<CommandRequest, CommandResponse>(80, 81, "shop.reroll", NetChannel.Shop, NetRules.Rerolls);

        public static readonly RequestType<CommandRequest, CommandResponse> DraftCommand =
            new RequestType<CommandRequest, CommandResponse>(82, 83, "draft.command", NetChannel.Draft, NetRules.Transactions);

        public static readonly RequestType<CommandRequest, CommandResponse> DraftReroll =
            new RequestType<CommandRequest, CommandResponse>(84, 85, "draft.reroll", NetChannel.Draft, NetRules.Rerolls);

        /// <summary>A surrender is a normal loss (spec L53).</summary>
        public static readonly RequestType<CommandRequest, CommandResponse> Surrender =
            new RequestType<CommandRequest, CommandResponse>(86, 87, "duel.surrender", NetChannel.Match, NetRules.Transactions);

        public static readonly MessageType<DuelStartMessage> DuelStart =
            new MessageType<DuelStartMessage>(88, "duel.start", NetChannel.Match, MessageDirection.ServerToClient);

        public static readonly MessageType<DuelInputMessage> DuelInput =
            new MessageType<DuelInputMessage>(89, "duel.input", NetChannel.Input, MessageDirection.ClientToServer, InputLimit);

        public static readonly MessageType<SnapshotMessage> Snapshot =
            new MessageType<SnapshotMessage>(90, "duel.snapshot", NetChannel.Snapshot, MessageDirection.ServerToClient);

        /// <summary>The same body as <see cref="Snapshot"/> when it exceeds one datagram (full keyframes).</summary>
        public static readonly MessageType<SnapshotMessage> SnapshotReliable =
            new MessageType<SnapshotMessage>(91, "duel.snapshot_reliable", NetChannel.Bulk, MessageDirection.ServerToClient);

        public static readonly MessageType<DuelEventsMessage> DuelEvents =
            new MessageType<DuelEventsMessage>(92, "duel.events", NetChannel.Events, MessageDirection.ServerToClient);

        public static readonly MessageType<DuelReportMessage> DuelReport =
            new MessageType<DuelReportMessage>(93, "duel.report", NetChannel.Events, MessageDirection.ServerToClient);

        public static readonly MessageType<SpectateRequest> Spectate =
            new MessageType<SpectateRequest>(94, "spectate.request", NetChannel.Match, MessageDirection.ClientToServer, NetRules.Chat);

        public static readonly MessageType<SpectatorFrameMessage> SpectatorFrame =
            new MessageType<SpectatorFrameMessage>(95, "spectate.frame", NetChannel.Bulk, MessageDirection.ServerToClient);

        /// <summary>
        /// Cosmetic reaction cues of the receiver's duel, batched at snapshot cadence on their own sequenced lane
        /// (research §2.5.1): losing one never delays a damage confirmation on the reliable Events lane.
        /// </summary>
        public static readonly MessageType<DuelEventsMessage> DuelCosmetic =
            new MessageType<DuelEventsMessage>(96, "duel.cosmetic", NetChannel.Cosmetic, MessageDirection.ServerToClient);

        /// <summary>A client asks for the exact layout of an announced arena it could not regenerate (research §2.7.1).</summary>
        public static readonly MessageType<ArenaLayoutRequest> ArenaLayoutRequest =
            new MessageType<ArenaLayoutRequest>(97, "match.arena_layout_request", NetChannel.Match, MessageDirection.ClientToServer, NetRules.Chat);

        /// <summary>The exact layout of an announced arena (deflated, on the reliable Bulk lane).</summary>
        public static readonly MessageType<ArenaLayoutMessage> ArenaLayout =
            new MessageType<ArenaLayoutMessage>(98, "match.arena_layout", NetChannel.Bulk, MessageDirection.ServerToClient);

        /// <summary>The request type a command kind travels on.</summary>
        public static RequestType<CommandRequest, CommandResponse> RequestFor(Hosting.Commands.CommandKind kind)
        {
            switch (kind)
            {
                case Hosting.Commands.CommandKind.ShopReroll: return ShopReroll;
                case Hosting.Commands.CommandKind.DraftReroll: return DraftReroll;
                case Hosting.Commands.CommandKind.DraftSelect:
                case Hosting.Commands.CommandKind.DraftPass: return DraftCommand;
                case Hosting.Commands.CommandKind.Surrender: return Surrender;
                default: return ShopCommand;
            }
        }
    }
}
