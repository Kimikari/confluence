using System;
using System.Collections.Generic;
using Confluence.Casting;
using Confluence.Core.Arena;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Time;
using Confluence.Net.Game.Snapshots;
using Confluence.Net.Game.Wire;
using Confluence.Net.Serialization;
using Confluence.Net.Sync;
using Confluence.Reactions.Grid;
using Confluence.Sim.Content;
using Confluence.Sim.Duel;
using Confluence.Sim.Input;
using Confluence.Sim.Movement;

namespace Confluence.Net.Game.Client
{
    /// <summary>
    /// The client's live view of its own duel: authoritative snapshots (delta-decoded against acknowledged baselines),
    /// remote entities interpolated about 100 ms behind the server (spec L419-L421), the local player's movement predicted
    /// with the Sim motor and reconciled after every snapshot (unacknowledged inputs replayed, corrections smoothed), the
    /// predicted grammar overlay, and the duel's reliable event stream. Rendering (Unity) and bots read it; it never
    /// decides an outcome.
    /// </summary>
    public sealed class ClientDuel
    {
        public const int MaxSentHistory = 256;

        /// <summary>Largest input lead in ticks (200 ms).</summary>
        public const int MaxLeadTicks = 12;
        private const int DecodedHistory = 128;

        /// <summary>Corrections larger than this snap instead of being smoothed (a teleport, a knockback).</summary>
        public const float SnapDistance = 3f;

        /// <summary>
        /// Visual correction decay per 60 Hz frame (research §2.3.2, Gaffer "State Synchronization"): small errors keep
        /// 95% per frame (eased out over about 0.2 s), errors of <see cref="LargeCorrection"/> or more keep 85%.
        /// </summary>
        public const float SmallCorrectionKeep = 0.95f;

        public const float LargeCorrectionKeep = 0.85f;

        public const float LargeCorrection = 0.5f;

        private readonly Dictionary<int, DuelFrame> _decoded = new Dictionary<int, DuelFrame>();
        private readonly Queue<int> _decodedOrder = new Queue<int>();
        private readonly SnapshotBuffer<DuelFrame> _remote = new SnapshotBuffer<DuelFrame>(48);
        private readonly InputSendBuffer<PlayerInput> _inputs = new InputSendBuffer<PlayerInput>(NetRules.InputRedundancy, MaxSentHistory);
        private readonly PlayerInput[] _sent = new PlayerInput[MaxSentHistory];
        private readonly int[] _sentTick = new int[MaxSentHistory];
        private readonly uint[] _sentSeq = new uint[MaxSentHistory];
        private readonly PredictedCast[] _sentCast = new PredictedCast[MaxSentHistory];
        private readonly Catalogue _catalogue;
        private float _predictedStamina;
        private InputButtons _lastSentButtons;

        internal ClientDuel(DuelStartMessage start, Catalogue catalogue, ContentRegistry registry, double maxInterpolationTicks = InterpolationClock.MaxDelayTicks)
        {
            Start = start ?? throw new ArgumentNullException(nameof(start));
            _catalogue = catalogue;
            Interpolation = new InterpolationClock(Math.Max(InterpolationClock.MinDelayTicks, maxInterpolationTicks));
            Grammar = new GrammarPredictor(catalogue, registry, start.Sides[start.YourSide], start.YourSide);
        }

        public DuelStartMessage Start { get; }
        public int DuelId => Start.DuelId;
        public int Side => Start.YourSide;
        public int OpponentSide => 1 - Start.YourSide;
        public DuelSideWire Own => Start.Sides[Side];
        public DuelSideWire Opponent => Start.Sides[OpponentSide];

        /// <summary>The regenerated public layout, or null while it is still being generated.</summary>
        public ArenaLayout Layout { get; private set; }

        public CellGrid Grid { get; private set; }

        public MotorPredictor Predictor { get; private set; }

        public GrammarPredictor Grammar { get; }

        /// <summary>Predict the own cast's movement slow and sprint lock from the local cast machine (default on).</summary>
        public bool PredictCastMovement { get; set; } = true;

        /// <summary>The adaptive interpolation delay of remote entities (research §2.2.1).</summary>
        public InterpolationClock Interpolation { get; }

        /// <summary>The closed-loop input lead (research §2.1.1).</summary>
        public InputLeadControl Lead { get; } = new InputLeadControl();

        /// <summary>The input lead chosen at the duel's first command; <see cref="Lead"/> adjusts around it.</summary>
        public int BaseLeadTicks { get; internal set; } = -1;

        /// <summary>The newest server-reported input queue depth (commands waiting on the server).</summary>
        public int ServerInputQueue => Lead.LastQueue;

        // ---- Authoritative ------------------------------------------------------------------------------------------

        /// <summary>The newest decoded snapshot (per-viewer projection).</summary>
        public DuelFrame Latest { get; private set; }

        /// <summary>The newest private state (own exact motor, resources, cooldowns, grammar).</summary>
        public PrivateEntry LatestPrivate { get; private set; }

        public int LatestTick => Latest != null ? Latest.Tick : -1;

        /// <summary>Last input sequence the server processed at <see cref="LatestTick"/>.</summary>
        public uint LatestAck { get; private set; }

        public DuelPhase Phase => Latest != null ? Latest.Phase : DuelPhase.Alcove;

        public bool Ended { get; internal set; }

        public DuelReportMessage Report { get; internal set; }

        // ---- Prediction -----------------------------------------------------------------------------------------

        /// <summary>The duel tick of the newest command produced locally.</summary>
        public int LocalTick { get; internal set; }

        public bool HasPrediction { get; private set; }

        /// <summary>Predicted own motor state after the newest local command.</summary>
        public MotorState Predicted { get; private set; }

        /// <summary>Visual offset that eases a reconciliation correction out (never affects the simulation).</summary>
        public Vec3 CorrectionOffset { get; private set; }

        /// <summary>
        /// Where to draw the local player's body and camera: the prediction blended between the last two predicted ticks
        /// by <see cref="RenderAlpha"/> (smooth at 120 fps over the 60 Hz simulation) plus the decaying correction offset.
        /// Aim and shot origins use <see cref="Predicted"/> itself, never the offset (research §2.3.2).
        /// </summary>
        public Vec3 RenderPosition => HasPrediction
            ? (HasPrevious ? Vec3.Lerp(PreviousPredicted.Position, Predicted.Position, RenderAlpha) : Predicted.Position) + CorrectionOffset
            : OwnAuthoritativePosition;

        /// <summary>The predicted state one tick before <see cref="Predicted"/> (for sub-tick presentation).</summary>
        public MotorState PreviousPredicted { get; private set; }

        public bool HasPrevious { get; private set; }

        /// <summary>Fraction of the current simulation tick elapsed at the last render update (0-1).</summary>
        public float RenderAlpha { get; internal set; } = 1f;

        public Vec3 OwnAuthoritativePosition => LatestPrivate != null ? LatestPrivate.Motor.Position : (Latest?.Players[Side].PositionMeters ?? Vec3.Zero);

        public int Reconciliations { get; private set; }

        /// <summary>Distance the prediction moved at the last reconciliation.</summary>
        public float LastCorrection { get; private set; }

        public float MaxCorrection { get; private set; }

        /// <summary>Reconciliations whose correction exceeded 1 cm.</summary>
        public int Corrections { get; private set; }

        public int Snaps { get; private set; }

        /// <summary>
        /// Reconciliations (over 1 cm) caused by an authoritative displacement the client does not predict: spell dashes,
        /// leaps and their halts, and hostile knockback are cast and hit results the server owns (spec L419 predicts
        /// movement and preparation only). They are smoothed or snapped like any correction but kept out of the
        /// prediction-error metrics (<see cref="MaxCorrection"/>, <see cref="CorrectionP95"/>, <see cref="Snaps"/>).
        /// </summary>
        public int DisplacementCorrections { get; private set; }

        public float MaxDisplacementCorrection { get; private set; }

        /// <summary>Displacement corrections larger than <see cref="SnapDistance"/> (snapped).</summary>
        public int DisplacementSnaps { get; private set; }

        private bool _baseWasDisplaced;
        private readonly float[] _corrections = new float[1024];
        private int _correctionNext;
        private int _correctionCount;

        /// <summary>95th percentile of the last 1024 reconciliation corrections in meters (research §2.3.2 metric).</summary>
        public float CorrectionP95
        {
            get
            {
                if (_correctionCount == 0) return 0f;
                var sorted = new float[_correctionCount];
                Array.Copy(_corrections, sorted, _correctionCount);
                Array.Sort(sorted);
                return sorted[Math.Min(_correctionCount - 1, (int)Math.Ceiling(_correctionCount * 0.95) - 1)];
            }
        }

        // ---- Interpolation ----------------------------------------------------------------------------------------

        /// <summary>The duel tick remote entities are drawn at (about 100 ms behind the server estimate).</summary>
        public double RenderTick { get; private set; }

        /// <summary>The opponent as interpolated at <see cref="RenderTick"/>, or null before the first snapshot.</summary>
        public PlayerEntry InterpolatedOpponent { get; private set; }

        public Vec3 OpponentRenderPosition { get; private set; }

        public float OpponentRenderYaw { get; private set; }

        public SampleKind LastSampleKind { get; private set; }

        /// <summary>
        /// Whether the opponent may be drawn and targeted (spec L120): not hidden by the server, and when invisible only as
        /// the silhouette within 4 m of the local player. Hidden players are never outlined (spec L120, L349).
        /// </summary>
        public bool OpponentVisible
        {
            get
            {
                PlayerEntry o = InterpolatedOpponent;
                if (o == null || o.Has(PlayerBits.Hidden)) return false;
                if (!o.Has(PlayerBits.Invisible)) return true;
                return Vec3.Distance(RenderPosition, OpponentRenderPosition) <= Confluence.Core.Rules.GlobalRules.Statuses.InvisibilitySilhouetteRadius;
            }
        }

        /// <summary>Cosmetic reaction cues received on their own lane (research §2.5.1).</summary>
        public int CosmeticEvents { get; private set; }

        /// <summary>
        /// The opponent tick actually displayed (research §2.2.2): the render tick while interpolating, the held newest
        /// tick while starved, the oldest while before it; 0 before any snapshot. Commands report it as their view tick.
        /// </summary>
        public int DisplayedViewTick { get; private set; }

        /// <summary>Render updates that found no newer snapshot to interpolate towards.</summary>
        public int StarvedRenders { get; private set; }

        // ---- Events and counters ----------------------------------------------------------------------------------

        public List<WireDuelEvent> Events { get; } = new List<WireDuelEvent>();

        public event Action<ClientDuel, WireDuelEvent> EventReceived;

        public int SnapshotsReceived { get; private set; }
        public int DeltaSnapshots { get; private set; }
        public int MalformedSnapshots { get; private set; }
        public int MissingBaselines { get; private set; }
        public int StaleSnapshots { get; private set; }

        /// <summary>Damage events this player dealt to the opponent (authoritative hit confirmations, spec L349).</summary>
        public int HitsConfirmed { get; private set; }

        public float DamageDealt { get; private set; }

        public float DamageTaken { get; private set; }

        public InputSendBuffer<PlayerInput> Inputs => _inputs;

        /// <summary>Newest decoded snapshot tick, the server's delta baseline acknowledgement.</summary>
        public int AckTick => Latest != null ? Latest.Tick : -1;

        // ---- Layout -----------------------------------------------------------------------------------------------

        internal void AttachLayout(ArenaLayout layout)
        {
            if (Layout != null || layout == null) return;
            Layout = layout;
            Grid = CellGrid.ForArena(layout);
            Predictor = new MotorPredictor(layout, Start.AlcoveTicks, Own.Alcove);
            if (Latest != null) Predictor.ApplySolids(Latest.Solids);
            if (LatestPrivate != null) Reconcile();
        }

        // ---- Snapshots --------------------------------------------------------------------------------------------

        internal void OnSnapshot(SnapshotHeader header, byte[] body) => OnSnapshot(header, body, -1, 0, double.NaN);

        /// <summary>
        /// A snapshot with the server's input status for the lead control and the estimated server duel tick at arrival
        /// (NaN when unknown) for the interpolation delay.
        /// </summary>
        internal void OnSnapshot(SnapshotHeader header, byte[] body, int inputQueue, uint inputStarved, double serverDuelTickAtArrival)
        {
            DuelFrame baseline = null;
            if (header.HasBaseline && !_decoded.TryGetValue(header.BaselineTick, out baseline))
            {
                MissingBaselines++;
                return;
            }

            DuelFrame frame;
            PrivateEntry own;
            try
            {
                var reader = new NetReader(body, 0, body.Length);
                frame = SnapshotCodec.DecodeBody(reader, header, baseline, out own);
                reader.ExpectEnd();
            }
            catch (NetReadException)
            {
                MalformedSnapshots++;
                return;
            }

            if (frame.DuelId != DuelId)
            {
                StaleSnapshots++;
                return;
            }

            SnapshotsReceived++;
            if (header.HasBaseline) DeltaSnapshots++;
            if (!double.IsNaN(serverDuelTickAtArrival)) Interpolation.OnSnapshot(frame.Tick, serverDuelTickAtArrival - frame.Tick);
            if (inputQueue >= 0 && BaseLeadTicks >= 0 && (Latest == null || frame.Tick > Latest.Tick))
            {
                Lead.OnSnapshot(inputQueue, inputStarved, -BaseLeadTicks, MaxLeadTicks - BaseLeadTicks);
            }

            if (!_decoded.ContainsKey(frame.Tick))
            {
                _decoded[frame.Tick] = frame;
                _decodedOrder.Enqueue(frame.Tick);
                while (_decodedOrder.Count > DecodedHistory) _decoded.Remove(_decodedOrder.Dequeue());
            }

            _remote.Add(frame.Tick, frame);
            if (Latest != null && frame.Tick <= Latest.Tick) return; // an older reliable keyframe overtaken by newer ones

            Latest = frame;
            if (frame.Phase == DuelPhase.Ended) Ended = true; // the authoritative final state (sent reliably), even if its events are late
            Predictor?.ApplySolids(frame.Solids);
            if (own != null)
            {
                LatestPrivate = own;
                LatestAck = header.LastProcessedInput;
                Grammar.SetContext(own, frame.Players[Side], frame.Tick, frame.Phase == DuelPhase.Alcove);
                Grammar.Reconcile(LocalTickOf(header.LastProcessedInput), (CastPhase)own.MachinePhase);
                Reconcile();
            }
        }

        /// <summary>Re-applies every unacknowledged input over the newest authoritative motor state (spec L419).</summary>
        private void Reconcile()
        {
            _inputs.Acknowledge(LatestAck);
            if (Predictor == null || LatestPrivate == null) return;
            MotorState state = LatestPrivate.Motor;
            float stamina = LatestPrivate.Stamina;
            InputButtons previous = PreviousButtonsBefore(LatestAck + 1);
            int tick = Latest.Tick;
            foreach (InputCommand<PlayerInput> command in _inputs.Unacknowledged)
            {
                if (command.Sequence <= LatestAck) continue;
                tick++;
                int slot = (int)(command.Sequence % MaxSentHistory);
                PredictedCast cast = _sentSeq[slot] == command.Sequence ? _sentCast[slot] : default;
                state = Predictor.Step(state, command.Input, previous, tick, LatestPrivate, cast, ref stamina);
                previous = command.Input.Buttons;
            }

            Reconciliations++;
            // A displacement in the authoritative base state, or one that ended since the previous base (its halt), was
            // started by a cast or a hit on the server; the local motor never begins one, so the gap is not a prediction error.
            bool displaced = LatestPrivate.Motor.IsDisplaced || _baseWasDisplaced;
            _baseWasDisplaced = LatestPrivate.Motor.IsDisplaced;
            if (HasPrediction)
            {
                Vec3 delta = Predicted.Position - state.Position;
                float distance = delta.Length();
                LastCorrection = distance;
                if (displaced)
                {
                    if (distance > MaxDisplacementCorrection) MaxDisplacementCorrection = distance;
                    if (distance > 0.01f) DisplacementCorrections++;
                    if (distance > SnapDistance) DisplacementSnaps++;
                }
                else
                {
                    _corrections[_correctionNext] = distance;
                    _correctionNext = (_correctionNext + 1) % _corrections.Length;
                    if (_correctionCount < _corrections.Length) _correctionCount++;
                    if (distance > MaxCorrection) MaxCorrection = distance;
                    if (distance > 0.01f) Corrections++;
                    if (distance > SnapDistance) Snaps++;
                }

                if (distance > SnapDistance)
                {
                    CorrectionOffset = Vec3.Zero;
                }
                else
                {
                    CorrectionOffset += delta;
                }

                // The replayed previous tick moves with the correction so the sub-tick blend stays continuous.
                if (HasPrevious) PreviousPredicted = Shifted(PreviousPredicted, state.Position - Predicted.Position);
            }

            Predicted = state;
            _predictedStamina = stamina;
            HasPrediction = true;
        }

        private static MotorState Shifted(MotorState m, Vec3 by)
        {
            m.Position += by;
            return m;
        }

        // ---- Inputs -----------------------------------------------------------------------------------------------

        /// <summary>Records and predicts the canonical command for <paramref name="tick"/>; returns its sequence.</summary>
        internal uint Produce(int tick, PlayerInput input)
        {
            input.Tick = tick;
            input = PlayerInputCodec.Canonicalize(input);
            input.Tick = tick;
            uint sequence = _inputs.Push(input);
            input.Sequence = sequence;
            int slot = (int)(sequence % MaxSentHistory);
            _sent[slot] = input;
            _sentTick[slot] = tick;
            _sentSeq[slot] = sequence;
            // The cast state this tick's motor reads, kept for every replay of this command (research §2.3.1).
            PredictedCast cast = PredictCastMovement ? Grammar.MovementStatus : default;
            _sentCast[slot] = cast;
            LocalTick = tick;

            if (Predictor != null && HasPrediction)
            {
                MotorState state = Predicted;
                PreviousPredicted = state;
                HasPrevious = true;
                float stamina = _predictedStamina;
                state = Predictor.Step(state, input, _lastSentButtons, tick, LatestPrivate, cast, ref stamina);
                Predicted = state;
                _predictedStamina = stamina;
            }

            Grammar.Step(tick, input, HasPrediction ? Predicted : (LatestPrivate != null ? LatestPrivate.Motor : default));
            _lastSentButtons = input.Buttons;
            return sequence;
        }

        /// <summary>Fills an input packet with the newest command and its redundant predecessors.</summary>
        internal void FillPacket(DuelInputMessage message)
        {
            message.DuelId = DuelId;
            message.AckSnapshotTick = AckTick;
            message.InterpolationTicks = (int)Math.Round(Interpolation.DelayTicks);
            message.SnapshotsReceived = (uint)SnapshotsReceived;
            _inputs.FillPacket(message.Commands);
        }

        private InputButtons PreviousButtonsBefore(uint sequence)
        {
            if (sequence <= 1) return InputButtons.None;
            uint previous = sequence - 1;
            int slot = (int)(previous % MaxSentHistory);
            return _sentSeq[slot] == previous ? _sent[slot].Buttons : InputButtons.None;
        }

        private int LocalTickOf(uint sequence)
        {
            if (sequence == 0) return 0;
            int slot = (int)(sequence % MaxSentHistory);
            return _sentSeq[slot] == sequence ? _sentTick[slot] : 0;
        }

        // ---- Rendering ----------------------------------------------------------------------------------------------

        /// <summary>Advances interpolation to <paramref name="renderTick"/> (duel ticks) and eases the correction offset.</summary>
        internal void UpdateRender(double renderTick, float dtSeconds)
        {
            RenderTick = renderTick;
            if (dtSeconds > 0f && CorrectionOffset != Vec3.Zero)
            {
                float size = CorrectionOffset.Length();
                float perFrame = SmallCorrectionKeep - (SmallCorrectionKeep - LargeCorrectionKeep) * Math.Min(1f, size / LargeCorrection);
                float keep = (float)Math.Pow(perFrame, dtSeconds * 60.0);
                CorrectionOffset = CorrectionOffset * keep;
                if (CorrectionOffset.Length() < 1e-4f) CorrectionOffset = Vec3.Zero;
            }

            SnapshotSample<DuelFrame> sample = _remote.Sample(renderTick);
            LastSampleKind = sample.Kind;
            switch (sample.Kind)
            {
                case SampleKind.Interpolated:
                    DisplayedViewTick = Math.Max(sample.FromTick, Math.Min(sample.ToTick, (int)Math.Floor(renderTick)));
                    break;
                case SampleKind.Starved:
                    DisplayedViewTick = sample.ToTick;
                    StarvedRenders++;
                    break;
                case SampleKind.BeforeOldest:
                    DisplayedViewTick = sample.FromTick;
                    break;
                default:
                    DisplayedViewTick = 0;
                    break;
            }

            if (sample.Kind == SampleKind.Empty) return;
            PlayerEntry a = sample.From.Players[OpponentSide];
            PlayerEntry b = sample.To.Players[OpponentSide];
            float t = sample.Kind == SampleKind.Interpolated ? sample.Alpha : 0f;
            bool aHidden = a.Has(PlayerBits.Hidden), bHidden = b.Has(PlayerBits.Hidden);
            if (aHidden != bHidden)
            {
                // Never blend a frozen hidden pose with a live one (spec L120): the silhouette appears where the first
                // replicated frame puts it, and vanishes at the first hidden one.
                PlayerEntry live = aHidden ? b : a;
                InterpolatedOpponent = aHidden ? b : (t < 0.5f ? a : b);
                OpponentRenderPosition = live.PositionMeters;
                OpponentRenderYaw = live.YawDeg;
            }
            else
            {
                InterpolatedOpponent = t < 0.5f ? a : b;
                OpponentRenderPosition = Vec3.Lerp(a.PositionMeters, b.PositionMeters, t);
                OpponentRenderYaw = a.YawDeg + CMath.DeltaAngle(a.YawDeg, b.YawDeg) * t;
            }

            _remote.DiscardBefore((int)Math.Floor(renderTick) - Ticks.PerSecond);
        }

        /// <summary>The interpolated position of a spell entity at the render tick, when it is in the bracketing frames.</summary>
        public bool TryGetEntityRenderPosition(int entityId, out Vec3 position)
        {
            position = Vec3.Zero;
            SnapshotSample<DuelFrame> sample = _remote.Sample(RenderTick);
            if (sample.Kind == SampleKind.Empty) return false;
            EntityEntry a = sample.From.Entities.Find(e => e.Key == entityId);
            EntityEntry b = sample.To.Entities.Find(e => e.Key == entityId);
            if (a == null && b == null) return false;
            EntityEntry any = a ?? b;
            if (EntityEntry.IsOwnerAttached(any.Kind))
            {
                // Movement and buff entities ride on their owner's replicated pose (no position on the wire).
                position = any.Owner == Side ? RenderPosition : OpponentRenderPosition;
                return true;
            }

            if (a == null || b == null)
            {
                position = (a ?? b).PositionMeters;
                return true;
            }

            position = Vec3.Lerp(a.PositionMeters, b.PositionMeters, sample.Kind == SampleKind.Interpolated ? sample.Alpha : 0f);
            return true;
        }

        // ---- Events -----------------------------------------------------------------------------------------------

        /// <summary>Cosmetic cues (reaction flashes): shown, never counted as authoritative outcomes.</summary>
        internal void OnCosmetic(DuelEventsMessage message)
        {
            foreach (WireDuelEvent e in message.Events)
            {
                CosmeticEvents++;
                Events.Add(e);
                EventReceived?.Invoke(this, e);
            }
        }

        internal void OnEvents(DuelEventsMessage message)
        {
            foreach (WireDuelEvent e in message.Events)
            {
                Events.Add(e);
                if (e.Type == WireEventType.Damage)
                {
                    float dealt = e.Amount + e.Amount2;
                    if (e.Player == Side && e.Target == OpponentSide)
                    {
                        HitsConfirmed++;
                        DamageDealt += dealt;
                    }
                    else if (e.Target == Side)
                    {
                        DamageTaken += dealt;
                    }
                }

                if (e.Type == WireEventType.DuelEnd) Ended = true;
                EventReceived?.Invoke(this, e);
            }
        }

        /// <summary>Remaining combat seconds at the newest snapshot (spec L47: at most 90 s), or the alcove countdown.</summary>
        public float TimerSeconds
        {
            get
            {
                if (Latest == null) return 0f;
                if (Latest.Phase == DuelPhase.Alcove) return Math.Max(0, Start.AlcoveTicks - Latest.Tick) / (float)Ticks.PerSecond;
                return Math.Max(0, Start.CombatTicks - Latest.CombatTick) / (float)Ticks.PerSecond;
            }
        }
    }
}
