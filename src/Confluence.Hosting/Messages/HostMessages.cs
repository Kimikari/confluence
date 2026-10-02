using System;
using System.Collections.Generic;
using Confluence.Economy.Augments;
using Confluence.Economy.Loadouts;
using Confluence.Economy.Shop;
using Confluence.Hosting.Arenas;
using Confluence.Hosting.Commands;
using Confluence.Hosting.Duels;
using Confluence.Match;

namespace Confluence.Hosting.Messages
{
    /// <summary>
    /// One outgoing message of the host for the Net layer. <see cref="Audience"/> is a player id for private messages
    /// (shop and draft state, command replies) or <see cref="Everyone"/> for public ones. Messages are appended in the
    /// order the host produced them, so a transport that sends them in queue order gives every client the same story.
    /// </summary>
    public abstract class HostMessage
    {
        public const int Everyone = -1;

        protected HostMessage(int tick, int audience)
        {
            Tick = tick;
            Audience = audience;
        }

        /// <summary>Match tick the message was produced on.</summary>
        public int Tick { get; }

        /// <summary>Player id for a private message, or <see cref="Everyone"/>.</summary>
        public int Audience { get; }

        public bool IsPrivate => Audience != Everyone;

        /// <summary>True when <paramref name="player"/> may receive it.</summary>
        public bool IsFor(int player) => Audience == Everyone || Audience == player;
    }

    /// <summary>
    /// A match flow event (phase changes, pairings, reveals, loadout finalization, duel starts and outcomes, Resolve,
    /// eliminations, connection changes, the final result). Shop and draft openings are private to their player
    /// because they carry that player's income; everything else is public (spec L44-L49, L391).
    /// </summary>
    public sealed class MatchEventMessage : HostMessage
    {
        internal MatchEventMessage(int tick, int audience, MatchEvent matchEvent) : base(tick, audience)
        {
            Event = matchEvent;
        }

        public MatchEvent Event { get; }

        public override string ToString() => "t" + Tick + " " + (IsPrivate ? "p" + Audience + " " : "") + Event;
    }

    /// <summary>
    /// The full public arena preview of a pass, sent right after its reveal (spec L44, L329). It has no hidden data
    /// (see <see cref="ArenaPreviewInfo"/>).
    /// </summary>
    public sealed class ArenaPreviewMessage : HostMessage
    {
        internal ArenaPreviewMessage(int tick, ArenaPreviewInfo preview) : base(tick, Everyone)
        {
            Preview = preview;
        }

        public ArenaPreviewInfo Preview { get; }
    }

    /// <summary>
    /// A player's private shop view (spec L45, L204-L213): balance, the three cards, the live loadout including the
    /// reserve, and whether the player has readied. Sent when the shop opens and after every accepted shop command.
    /// </summary>
    public sealed class ShopStateMessage : HostMessage
    {
        internal ShopStateMessage(int tick, int player, int crowns, IReadOnlyList<ShopCard> cards, PublicLoadout loadout,
            IReadOnlyList<string> reserveIds, bool ready) : base(tick, player)
        {
            Player = player;
            Crowns = crowns;
            Cards = cards;
            Loadout = loadout;
            ReserveIds = reserveIds;
            Ready = ready;
        }

        public int Player { get; }
        public int Crowns { get; }
        public IReadOnlyList<ShopCard> Cards { get; }

        /// <summary>The live loadout (equipped spells, equipped items, augments with ranks).</summary>
        public PublicLoadout Loadout { get; }

        /// <summary>Definition ids in the three reserve slots (L210), private to the player.</summary>
        public IReadOnlyList<string> ReserveIds { get; }

        public bool Ready { get; }
    }

    /// <summary>
    /// A player's private draft view (spec L216-L233): the five cards with their reroll state and the draft's outcome.
    /// Sent when the draft opens and after every accepted draft command. <see cref="Cards"/> is empty when no draft
    /// could be offered.
    /// </summary>
    public sealed class DraftStateMessage : HostMessage
    {
        internal DraftStateMessage(int tick, int player, DraftOutcome outcome, IReadOnlyList<DraftCard> cards, OwnedAugment selected)
            : base(tick, player)
        {
            Player = player;
            Outcome = outcome;
            Cards = cards;
            Selected = selected;
        }

        public int Player { get; }
        public DraftOutcome Outcome { get; }
        public IReadOnlyList<DraftCard> Cards { get; }

        /// <summary>The augment taken by a selection, or null.</summary>
        public OwnedAugment Selected { get; }
    }

    /// <summary>The reply to one player command (private; duplicates included, L429).</summary>
    public sealed class CommandResultMessage : HostMessage
    {
        internal CommandResultMessage(int tick, PlayerCommand command, CommandResult result) : base(tick, command.Player)
        {
            Command = command;
            Result = result;
        }

        public PlayerCommand Command { get; }
        public CommandResult Result { get; }
    }

    /// <summary>A finished duel's results screen data: outcome, both sides' feats and decisive sequences (L49, L359).</summary>
    public sealed class DuelReportMessage : HostMessage
    {
        internal DuelReportMessage(int tick, DuelReport report) : base(tick, Everyone)
        {
            Report = report;
        }

        public DuelReport Report { get; }
    }

    /// <summary>An operator notice (refused start, Laboratory label) for every client and the server log.</summary>
    public sealed class HostNoticeMessage : HostMessage
    {
        internal HostNoticeMessage(int tick, string text) : base(tick, Everyone)
        {
            Text = text;
        }

        public string Text { get; }

        public override string ToString() => "t" + Tick + " notice: " + Text;
    }

    /// <summary>
    /// The host's outgoing queue. Single-threaded like the host tick; the Net layer drains it after every tick and
    /// routes each message by its audience.
    /// </summary>
    public sealed class HostOutbox
    {
        private readonly List<HostMessage> _pending = new List<HostMessage>();

        /// <summary>Messages produced since the host was created (drained or not).</summary>
        public int TotalCount { get; private set; }

        public int PendingCount => _pending.Count;

        /// <summary>Optional observer of every message as it is queued (tests, logging).</summary>
        public event Action<HostMessage> Queued;

        internal void Add(HostMessage message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            _pending.Add(message);
            TotalCount++;
            Queued?.Invoke(message);
        }

        /// <summary>Removes and returns every pending message in production order.</summary>
        public List<HostMessage> Drain()
        {
            var drained = new List<HostMessage>(_pending);
            _pending.Clear();
            return drained;
        }

        /// <summary>The pending messages one player may receive, without removing anything.</summary>
        public List<HostMessage> PeekFor(int player)
        {
            var list = new List<HostMessage>();
            foreach (HostMessage m in _pending)
                if (m.IsFor(player)) list.Add(m);
            return list;
        }
    }
}
