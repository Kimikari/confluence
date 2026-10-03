using System;
using System.Collections.Generic;
using Confluence.Casting;
using Confluence.Hosting.Duels;
using Confluence.Net.Sync;
using Confluence.Sim;
using Confluence.Sim.Duel;
using Confluence.Sim.Input;

namespace Confluence.Net.Game.Server
{
    /// <summary>
    /// Lag compensation policy (spec L423): a command may ask hitscan and melee validation to rewind the opponent's hit
    /// volume to the tick it was displaying, but never to a tick the server has not yet shown that client, never to the
    /// present or future, and never more than 150 ms back. Grammar input timestamps are re-based onto the server step and
    /// bounded the same way (spec L490 "bounded latency compensation"). Fields, props, and projectiles are never rewound
    /// (the Sim only rewinds player capsules).
    /// </summary>
    public static class LagCompensation
    {
        public static int MaxRewindTicks => SimRules.LagCompensation.MaxRewindTicks;

        /// <summary>
        /// The view tick the Sim may use for a command executed at <paramref name="stepTick"/>, or 0 for none.
        /// <paramref name="newestShownTick"/> is the newest snapshot tick sent to this client.
        /// </summary>
        public static int ClampViewTick(int requested, int stepTick, int newestShownTick)
        {
            if (requested <= 0) return 0;
            int max = Math.Min(stepTick - 1, newestShownTick);
            int min = stepTick - MaxRewindTicks;
            if (max < 1 || max < min) return 0;
            if (requested > max) return max;
            if (requested < min) return min;
            return requested;
        }

        /// <summary>Grammar events re-based from the client's command tick onto the server step, within the rewind bound.</summary>
        public static IReadOnlyList<GrammarInputEvent> RebaseEvents(IReadOnlyList<GrammarInputEvent> events, int clientTick, int stepTick)
        {
            if (events == null || events.Count == 0) return null;
            var copy = new GrammarInputEvent[events.Count];
            for (int i = 0; i < events.Count; i++)
            {
                GrammarInputEvent e = events[i];
                long offset = (long)e.Tick - clientTick;
                long tick = stepTick + offset;
                if (tick > stepTick) tick = stepTick;
                if (tick < stepTick - MaxRewindTicks) tick = stepTick - MaxRewindTicks;
                if (tick < 0) tick = 0;
                e.Tick = (int)tick;
                copy[i] = e;
            }

            return copy;
        }
    }

    /// <summary>
    /// A human player's duel input, fed by the transport. Each duel step consumes exactly one command from the receive
    /// buffer (dedupe, ordering, loss substitution, short repetition, no banking ahead), so the snapshot acknowledgement
    /// is exact: the state at a tick reflects every input up to <see cref="LastProcessedSequence"/>. Repeated or lost
    /// substitutes never repeat one-shot data (grammar events, taps). While disconnected the player sends neutral input
    /// facing the current view (spec L55); nothing pauses.
    /// </summary>
    public sealed class NetworkInputSource : IInputSource
    {
        private readonly InputReceiveBuffer<PlayerInput> _buffer = new InputReceiveBuffer<PlayerInput>();

        public int DuelId { get; private set; } = -1;

        public bool Connected { get; set; } = true;

        /// <summary>Newest snapshot tick sent to this player (lag compensation upper bound).</summary>
        public int NewestShownTick { get; set; }

        public InputReceiveBuffer<PlayerInput> Buffer => _buffer;

        public uint LastProcessedSequence => _buffer.LastProcessedSequence;

        /// <summary>Commands whose view tick the policy moved (diagnostics).</summary>
        public long ClampedViewTicks { get; private set; }

        /// <summary>Accepted inputs for a duel other than the current one (stale packets after a duel ended).</summary>
        public long ForeignDuelPackets { get; private set; }

        public void Accept(int duelId, IReadOnlyList<InputCommand<PlayerInput>> commands)
        {
            if (duelId != DuelId)
            {
                ForeignDuelPackets++;
                return;
            }

            _buffer.Accept(commands);
        }

        public void BeginDuel(DuelSeat seat)
        {
            DuelId = seat.DuelId;
            NewestShownTick = 0;
            _buffer.Reset();
        }

        public PlayerInput NextInput(DuelSeat seat)
        {
            DuelWorld world = seat.World;
            int stepTick = world.Tick + 1;
            float yaw = world.Player(seat.Index).Motor.YawDeg;
            if (!Connected) return PlayerInput.Idle(yaw);

            ConsumedInput<PlayerInput> consumed = _buffer.ConsumeNext();
            PlayerInput input;
            switch (consumed.Source)
            {
                case InputSource.None:
                case InputSource.Neutral:
                    input = PlayerInput.Idle(yaw);
                    input.SelectedSlot = world.Player(seat.Index).SelectedSlot;
                    break;
                default:
                    input = consumed.Input;
                    if (consumed.Source != InputSource.Received)
                    {
                        // A stand-in repeats held state only: one-shot events and taps were already applied or are lost.
                        input.GrammarEvents = null;
                        input.Tapped = InputButtons.None;
                    }

                    break;
            }

            int clientTick = input.Tick;
            int view = LagCompensation.ClampViewTick(input.ViewTick, stepTick, NewestShownTick);
            if (view != input.ViewTick && input.ViewTick > 0) ClampedViewTicks++;
            input.ViewTick = view;
            input.GrammarEvents = LagCompensation.RebaseEvents(input.GrammarEvents, clientTick, stepTick);
            input.Tick = stepTick;
            input.Sequence = consumed.Sequence;
            return input;
        }

        public void EndDuel(DuelSeat seat, DuelReport report)
        {
            DuelId = -1;
            _buffer.Reset();
        }
    }
}
