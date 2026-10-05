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
    /// (the Sim only rewinds player capsules). The full rule set (expected-view check, monotonicity, stand-ins) is in
    /// <see cref="NetworkInputSource"/> and docs/notes/net.md "Lag compensation".
    /// </summary>
    public static class LagCompensation
    {
        public static int MaxRewindTicks => SimRules.LagCompensation.MaxRewindTicks;

        /// <summary>Allowed distance between a claimed view tick and the server's expectation, before jitter (ticks).</summary>
        public const int ViewToleranceTicks = 2;

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

        /// <summary>
        /// The view tick the server expects from an honest client (docs/research/netcode.md §2.2.3, the Source rule
        /// "command time = server time - packet latency - client interpolation"): the duel tick the command first arrived
        /// at, minus the one-way delay, minus the client's declared interpolation delay.
        /// </summary>
        public static int ExpectedViewTick(int arrivalTick, int oneWayTicks, int interpolationTicks)
        {
            return arrivalTick - Math.Max(0, oneWayTicks) - Math.Max(0, interpolationTicks);
        }

        /// <summary>
        /// Grammar events re-based from the client's command tick onto the server step, within the rewind bound
        /// (kept for callers without a session offset: equivalent to an offset of stepTick - clientTick).
        /// </summary>
        public static IReadOnlyList<GrammarInputEvent> RebaseEvents(IReadOnlyList<GrammarInputEvent> events, int clientTick, int stepTick)
        {
            int previous = int.MinValue;
            return RebaseEvents(events, stepTick - clientTick, stepTick, ref previous);
        }

        /// <summary>
        /// Grammar events placed on the server timeline (docs/research/netcode.md §2.1.2): client event tick plus the
        /// slowly tracked session offset, clamped to [max(previous event, step - 150 ms), step] and non-decreasing, so the
        /// spacing of two syllables survives a network hiccup between their commands.
        /// </summary>
        public static IReadOnlyList<GrammarInputEvent> RebaseEvents(IReadOnlyList<GrammarInputEvent> events, int sessionOffset, int stepTick, ref int previousEventTick)
        {
            if (events == null || events.Count == 0) return null;
            var copy = new GrammarInputEvent[events.Count];
            for (int i = 0; i < events.Count; i++)
            {
                GrammarInputEvent e = events[i];
                long tick = (long)e.Tick + sessionOffset;
                long min = Math.Max((long)stepTick - MaxRewindTicks, previousEventTick == int.MinValue ? long.MinValue : previousEventTick);
                if (tick < min) tick = min;
                if (tick > stepTick) tick = stepTick;
                if (tick < 0) tick = 0;
                e.Tick = (int)tick;
                previousEventTick = e.Tick;
                copy[i] = e;
            }

            return copy;
        }

        /// <summary>
        /// The latest a grammar event may be stamped before its own command (docs/research/netcode.md §2.1.2): taps,
        /// strokes ends, traces and widget picks belong to the tick they were made in (one tick of sub-tick slack); voice
        /// syllables and stroke samples may be older because recognition finishes after the onset, but never by more than
        /// the 150 ms rewind bound, equal for every client (spec L490 "a better microphone never grants a wider combat
        /// acceptance window").
        /// </summary>
        public static int MaxBackdateTicks(GrammarInputKind kind)
        {
            switch (kind)
            {
                case GrammarInputKind.Syllable:
                case GrammarInputKind.StrokeSample:
                    return MaxRewindTicks;
                default:
                    return 1;
            }
        }
    }

    /// <summary>Per-duel lag compensation and input timing diagnostics of one network player (the metrics record).</summary>
    public sealed class InputTimingStats
    {
        public long Commands;
        public long ViewClaims;
        public long ViewsAccepted;
        public long ViewsReplaced;
        public long ViewsClamped;
        public long ViewsBackward;
        public long StandIns;
        public long EventsClamped;
        public long RewindTickSum;
        public long RewindSamples;
        public int MaxRewindTicks;
        public long QueueDepthSum;
        public long OneWayTickSum;
        public long InterpolationTickSum;

        public double MeanRewindTicks => RewindSamples > 0 ? RewindTickSum / (double)RewindSamples : 0;
        public double MeanQueueDepth => Commands > 0 ? QueueDepthSum / (double)Commands : 0;
        public double MeanOneWayTicks => Commands > 0 ? OneWayTickSum / (double)Commands : 0;
        public double MeanInterpolationTicks => Commands > 0 ? InterpolationTickSum / (double)Commands : 0;
    }

    /// <summary>
    /// A human player's duel input, fed by the transport. Each duel step consumes exactly one command from the receive
    /// buffer (dedupe, ordering, loss substitution, short repetition, no banking ahead, sustained surplus cut), so the
    /// snapshot acknowledgement is exact: the state at a tick reflects every input up to
    /// <see cref="LastProcessedSequence"/>. Skipped commands hand their taps and grammar events to the next consumed one;
    /// repeated or lost substitutes never repeat one-shot data and get no lag compensation. While disconnected the
    /// player sends neutral input facing the current view (spec L55); nothing pauses.
    /// <para>
    /// View ticks (spec L423, docs/research/netcode.md §2.2.3): a claim is accepted only within
    /// <see cref="LagCompensation.ViewToleranceTicks"/> plus the measured jitter of the server's expectation (arrival
    /// tick minus one-way delay minus the client's declared interpolation delay) and only if it does not step back by
    /// more than one tick; otherwise the expectation replaces it. The hard bounds of
    /// <see cref="LagCompensation.ClampViewTick"/> apply last.
    /// </para>
    /// </summary>
    public sealed class NetworkInputSource : IInputSource
    {
        private const int ArrivalRing = 256;
        private const int OffsetWindow = 32;

        private readonly InputReceiveBuffer<PlayerInput> _buffer = new InputReceiveBuffer<PlayerInput>(
            mergeSkipped: MergeSkipped, surplusTargetTicks: NetRules.InputSurplusTargetTicks);

        private readonly uint[] _arrivalSeq = new uint[ArrivalRing];
        private readonly int[] _arrivalTick = new int[ArrivalRing];
        private readonly byte[] _arrivalInterp = new byte[ArrivalRing];
        private readonly int[] _offsetSamples = new int[OffsetWindow];
        private readonly int[] _offsetScratch = new int[OffsetWindow];
        private int _offsetCount;
        private int _offsetNext;
        private int _lastView;
        private int _lastEventTick = int.MinValue;

        public int DuelId { get; private set; } = -1;

        public bool Connected { get; set; } = true;

        /// <summary>Newest snapshot tick sent to this player (lag compensation upper bound).</summary>
        public int NewestShownTick { get; set; }

        /// <summary>Current one-way delay estimate in ticks (set by the server from snapshot-ack round trips).</summary>
        public int OneWayTicks { get; set; }

        /// <summary>Current jitter estimate in ticks (widens the view tolerance).</summary>
        public int JitterTicks { get; set; }

        public InputReceiveBuffer<PlayerInput> Buffer => _buffer;

        public uint LastProcessedSequence => _buffer.LastProcessedSequence;

        /// <summary>Commands waiting ahead of the next step (reported to the client for its lead control).</summary>
        public int QueueDepth => _buffer.QueueDepth;

        /// <summary>Commands whose view tick the policy moved (diagnostics, all duels).</summary>
        public long ClampedViewTicks { get; private set; }

        /// <summary>Accepted inputs for a duel other than the current one (stale packets after a duel ended).</summary>
        public long ForeignDuelPackets { get; private set; }

        /// <summary>The current (or last) duel's timing diagnostics.</summary>
        public InputTimingStats Stats { get; private set; } = new InputTimingStats();

        /// <summary>The session offset (server step minus client stamp) grammar events are placed with.</summary>
        public int SessionOffset { get; private set; }

        public void Accept(int duelId, IReadOnlyList<InputCommand<PlayerInput>> commands) => Accept(duelId, commands, -1, 0);

        /// <summary>
        /// Accepts a packet that arrived while the duel world was at <paramref name="arrivalTick"/>, from a client rendering
        /// <paramref name="interpolationTicks"/> behind its server clock estimate.
        /// </summary>
        public void Accept(int duelId, IReadOnlyList<InputCommand<PlayerInput>> commands, int arrivalTick, int interpolationTicks)
        {
            if (duelId != DuelId)
            {
                ForeignDuelPackets++;
                return;
            }

            for (int i = 0; i < commands.Count; i++)
            {
                InputCommand<PlayerInput> command = commands[i];
                if (command.Sequence <= _buffer.LastProcessedSequence && _buffer.HasStarted) continue;
                PlayerInput input = command.Input;
                if (input.GrammarEvents != null) input.GrammarEvents = BoundEventOffsets(input.GrammarEvents, input.Tick);
                if (_buffer.Accept(command.Sequence, input) && arrivalTick >= 0)
                {
                    int slot = (int)(command.Sequence % ArrivalRing);
                    _arrivalSeq[slot] = command.Sequence;
                    _arrivalTick[slot] = arrivalTick;
                    _arrivalInterp[slot] = (byte)Math.Max(0, Math.Min(255, interpolationTicks));
                }
            }
        }

        public void BeginDuel(DuelSeat seat)
        {
            DuelId = seat.DuelId;
            ResetTimeline();
            Stats = new InputTimingStats();
        }

        /// <summary>A duel start restarts the client's input stream: forget the queue, view, and event history.</summary>
        public void ResetTimeline()
        {
            NewestShownTick = 0;
            _buffer.Reset();
            _offsetCount = 0;
            _offsetNext = 0;
            _lastEventTick = int.MinValue;
            Array.Clear(_arrivalSeq, 0, ArrivalRing);
            ResetViewHistory();
        }

        /// <summary>A reconnect keeps the input stream but restarts the view-tick monotonicity rule (§2.2.3).</summary>
        public void ResetViewHistory()
        {
            _lastView = 0;
        }

        public PlayerInput NextInput(DuelSeat seat)
        {
            DuelWorld world = seat.World;
            int stepTick = world.Tick + 1;
            float yaw = world.Player(seat.Index).Motor.YawDeg;
            if (!Connected) return PlayerInput.Idle(yaw);

            ConsumedInput<PlayerInput> consumed = _buffer.ConsumeNext();
            PlayerInput input;
            bool received = consumed.Source == InputSource.Received;
            switch (consumed.Source)
            {
                case InputSource.None:
                case InputSource.Neutral:
                    input = PlayerInput.Idle(yaw);
                    input.SelectedSlot = world.Player(seat.Index).SelectedSlot;
                    break;
                default:
                    input = consumed.Input;
                    if (!received)
                    {
                        // A stand-in repeats held state only: one-shot events and taps were already applied or are lost.
                        input.GrammarEvents = null;
                        input.Tapped = InputButtons.None;
                    }

                    break;
            }

            if (consumed.Source != InputSource.None)
            {
                Stats.Commands++;
                Stats.QueueDepthSum += _buffer.QueueDepth;
                Stats.OneWayTickSum += OneWayTicks;
            }

            int clientTick = input.Tick;
            if (received) TrackOffset(stepTick - clientTick);

            input.ViewTick = received ? ValidateView(input.ViewTick, consumed.Sequence, stepTick) : 0;
            if (!received && consumed.Source != InputSource.None) Stats.StandIns++;
            input.GrammarEvents = LagCompensation.RebaseEvents(input.GrammarEvents, SessionOffset, stepTick, ref _lastEventTick);
            input.Tick = stepTick;
            input.Sequence = consumed.Sequence;
            return input;
        }

        public void EndDuel(DuelSeat seat, DuelReport report)
        {
            DuelId = -1;
            _buffer.Reset();
        }

        /// <summary>Folds a skipped command's one-shot data into the next consumed one (§2.1.1).</summary>
        public static PlayerInput MergeSkipped(PlayerInput skipped, PlayerInput next)
        {
            // A button held only in the skipped command would lose its press and release edges: it becomes a tap.
            next.Tapped |= skipped.Tapped | (skipped.Buttons & ~next.Buttons);
            if (skipped.GrammarEvents != null && skipped.GrammarEvents.Count > 0)
            {
                var events = new List<GrammarInputEvent>(skipped.GrammarEvents);
                if (next.GrammarEvents != null) events.AddRange(next.GrammarEvents);
                next.GrammarEvents = events;
            }

            return next;
        }

        private int ValidateView(int claimed, uint sequence, int stepTick)
        {
            if (claimed <= 0) return 0;
            Stats.ViewClaims++;
            int view = claimed;
            int slot = (int)(sequence % ArrivalRing);
            if (_arrivalSeq[slot] == sequence)
            {
                int interpolation = _arrivalInterp[slot];
                Stats.InterpolationTickSum += interpolation;
                int expected = LagCompensation.ExpectedViewTick(_arrivalTick[slot], OneWayTicks, interpolation);
                int tolerance = LagCompensation.ViewToleranceTicks + Math.Max(0, JitterTicks);
                if (Math.Abs(view - expected) > tolerance)
                {
                    view = expected;
                    Stats.ViewsReplaced++;
                }
            }

            if (_lastView > 0 && view < _lastView - 1)
            {
                // A view may step back by one tick at most (interpolation jitter); a larger backtrack is a cheat signal.
                view = _lastView - 1;
                Stats.ViewsBackward++;
            }

            int clamped = LagCompensation.ClampViewTick(view, stepTick, NewestShownTick);
            if (clamped != view) Stats.ViewsClamped++;
            if (clamped != claimed) ClampedViewTicks++;
            else Stats.ViewsAccepted++;
            if (clamped > 0)
            {
                _lastView = clamped;
                int rewind = stepTick - clamped;
                Stats.RewindTickSum += rewind;
                Stats.RewindSamples++;
                if (rewind > Stats.MaxRewindTicks) Stats.MaxRewindTicks = rewind;
            }

            return clamped;
        }

        private void TrackOffset(int sample)
        {
            _offsetSamples[_offsetNext] = sample;
            _offsetNext = (_offsetNext + 1) % OffsetWindow;
            if (_offsetCount < OffsetWindow) _offsetCount++;
            Array.Copy(_offsetSamples, _offsetScratch, _offsetCount);
            Array.Sort(_offsetScratch, 0, _offsetCount);
            SessionOffset = _offsetScratch[_offsetCount / 2];
        }

        private IReadOnlyList<GrammarInputEvent> BoundEventOffsets(IReadOnlyList<GrammarInputEvent> events, int commandTick)
        {
            GrammarInputEvent[] copy = null;
            for (int i = 0; i < events.Count; i++)
            {
                GrammarInputEvent e = events[i];
                int min = commandTick - LagCompensation.MaxBackdateTicks(e.Kind);
                int tick = e.Tick < min ? min : e.Tick > commandTick ? commandTick : e.Tick;
                if (tick == e.Tick) continue;
                if (copy == null)
                {
                    copy = new GrammarInputEvent[events.Count];
                    for (int k = 0; k < events.Count; k++) copy[k] = events[k];
                }

                e.Tick = tick;
                copy[i] = e;
                Stats.EventsClamped++;
            }

            return copy ?? events;
        }
    }
}
