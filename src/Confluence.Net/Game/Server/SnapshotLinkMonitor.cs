using System;
using Confluence.Net.Transport;

namespace Confluence.Net.Game.Server
{
    /// <summary>
    /// Application-level link measurement of one duelist's snapshot stream (docs/research/netcode.md §2.8.4): the round
    /// trip from a snapshot's send to the first input packet acknowledging it (windowed median, minimum, and jitter), and
    /// snapshot loss from the client's received counter against what was sent one round trip earlier. LiteNetLib's
    /// RoundTripTime (a running mean in which a lost pong counts a whole ping interval) and its mixed-direction
    /// PacketLoss stay diagnostics only; these numbers drive the 30/20 Hz selector (spec L421, D-115) and the expected
    /// view of lag compensation (spec L423).
    /// </summary>
    public sealed class SnapshotLinkMonitor
    {
        private const int SentRing = 128;
        private const int RttWindow = 32;
        private const long LossWindowMs = 1000;

        private readonly int[] _sentTick = new int[SentRing];
        private readonly long[] _sentMs = new long[SentRing];
        private readonly long[] _sentCount = new long[SentRing];
        private readonly int[] _rtt = new int[RttWindow];
        private readonly int[] _scratch = new int[RttWindow];
        private int _sentNext;
        private long _sent;
        private int _rttCount;
        private int _rttNext;
        private int _lastAckTick = -1;
        private long _received;
        private long _lossStartMs = -1;
        private long _lossSent0;
        private long _lossReceived0;

        public SnapshotLinkMonitor()
        {
            for (int i = 0; i < SentRing; i++) _sentTick[i] = -1;
        }

        /// <summary>Median round trip over the window (ms), or 0 before the first sample.</summary>
        public int RttMs { get; private set; }

        public int RttMinMs { get; private set; }

        /// <summary>Half the spread between the 90th percentile and the minimum (ms).</summary>
        public int JitterMs { get; private set; }

        /// <summary>Snapshot loss over the last window (0-1).</summary>
        public float LossFraction { get; private set; }

        public long Sent => _sent;

        public long ReceivedReported => _received;

        public int RttSamples { get; private set; }

        public void OnSent(int tick, long nowMs)
        {
            _sent++;
            _sentTick[_sentNext] = tick;
            _sentMs[_sentNext] = nowMs;
            _sentCount[_sentNext] = _sent;
            _sentNext = (_sentNext + 1) % SentRing;
        }

        /// <summary>An input packet acknowledging <paramref name="ackTick"/> and reporting the received count.</summary>
        public void OnAck(int ackTick, long receivedCount, long nowMs)
        {
            if (receivedCount > _received && receivedCount <= _sent) _received = receivedCount;
            if (ackTick <= _lastAckTick) return;
            _lastAckTick = ackTick;
            for (int i = 0; i < SentRing; i++)
            {
                if (_sentTick[i] != ackTick) continue;
                AddRtt((int)Math.Max(0, Math.Min(10000, nowMs - _sentMs[i])));
                break;
            }
        }

        /// <summary>Updates the loss window; call regularly (every few ticks).</summary>
        public void Update(long nowMs)
        {
            if (_lossStartMs < 0)
            {
                _lossStartMs = nowMs;
                _lossSent0 = SentBefore(nowMs - RttMs);
                _lossReceived0 = _received;
                return;
            }

            if (nowMs - _lossStartMs < LossWindowMs) return;
            long sent = SentBefore(nowMs - RttMs);
            long sentDelta = sent - _lossSent0;
            long receivedDelta = _received - _lossReceived0;
            LossFraction = sentDelta > 0 ? (float)Math.Max(0.0, Math.Min(1.0, 1.0 - receivedDelta / (double)sentDelta)) : 0f;
            _lossStartMs = nowMs;
            _lossSent0 = sent;
            _lossReceived0 = _received;
        }

        /// <summary>The selector's link signals: application RTT and snapshot loss, the transport's reliable backlog.</summary>
        public LinkStats ToLinkStats(LinkStats transport)
        {
            return new LinkStats
            {
                RttMs = RttSamples > 0 ? RttMs : transport.RttMs,
                LossFraction = LossFraction,
                ReliableQueued = transport.ReliableQueued,
            };
        }

        public void Reset()
        {
            for (int i = 0; i < SentRing; i++) _sentTick[i] = -1;
            _sent = 0;
            _received = 0;
            _lastAckTick = -1;
            _lossStartMs = -1;
            LossFraction = 0f;
        }

        private long SentBefore(long timeMs)
        {
            long best = 0;
            long bestMs = long.MinValue;
            for (int i = 0; i < SentRing; i++)
            {
                if (_sentTick[i] < 0) continue;
                if (_sentMs[i] <= timeMs && _sentMs[i] >= bestMs)
                {
                    bestMs = _sentMs[i];
                    best = Math.Max(best, _sentCount[i]);
                }
            }

            return best;
        }

        private void AddRtt(int ms)
        {
            RttSamples++;
            _rtt[_rttNext] = ms;
            _rttNext = (_rttNext + 1) % RttWindow;
            if (_rttCount < RttWindow) _rttCount++;
            Array.Copy(_rtt, _scratch, _rttCount);
            Array.Sort(_scratch, 0, _rttCount);
            RttMs = _scratch[_rttCount / 2];
            RttMinMs = _scratch[0];
            int p90 = _scratch[Math.Min(_rttCount - 1, (int)Math.Ceiling(_rttCount * 0.9) - 1)];
            JitterMs = Math.Max(0, (p90 - RttMinMs) / 2);
        }
    }
}
