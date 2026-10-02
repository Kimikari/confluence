using System.Collections.Generic;
using Confluence.Net.Protocol;

namespace Confluence.Net.Transport
{
    /// <summary>
    /// Monotonic counters for one endpoint or one connection. Malformed and illegal traffic is counted rather
    /// than thrown, so operators can see abuse and tests can assert on it.
    /// </summary>
    public sealed class TransportCounters
    {
        private readonly long[] _dispatch = new long[8];
        private readonly Dictionary<RejectReason, long> _rejections = new Dictionary<RejectReason, long>();

        public long MessagesReceived { get; private set; }

        public long MessagesSent { get; private set; }

        public long BytesReceived { get; private set; }

        public long BytesSent { get; private set; }

        /// <summary>Messages dropped because an unreliable packet would not fit in one datagram.</summary>
        public long OversizedDrops { get; private set; }

        /// <summary>Messages queued before the welcome that overflowed the client's pre-welcome queue.</summary>
        public long PreWelcomeOverflow { get; private set; }

        public long Handled => Count(DispatchResult.Handled);

        public long UnknownMessages => Count(DispatchResult.UnknownMessage);

        public long WrongDirection => Count(DispatchResult.WrongDirection);

        public long WrongChannel => Count(DispatchResult.WrongChannel);

        public long RateLimited => Count(DispatchResult.RateLimited);

        public long Malformed => Count(DispatchResult.Malformed);

        public long HandlerFaults => Count(DispatchResult.HandlerFault);

        /// <summary>Unknown, wrong-direction, wrong-channel, and malformed messages together.</summary>
        public long Violations => UnknownMessages + WrongDirection + WrongChannel + Malformed;

        public long Rejections(RejectReason reason) => _rejections.TryGetValue(reason, out long n) ? n : 0;

        public long TotalRejections
        {
            get
            {
                long total = 0;
                foreach (KeyValuePair<RejectReason, long> pair in _rejections)
                {
                    total += pair.Value;
                }

                return total;
            }
        }

        public long Count(DispatchResult result) => _dispatch[(int)result];

        internal void Record(DispatchResult result, int bytes)
        {
            MessagesReceived++;
            BytesReceived += bytes;
            _dispatch[(int)result]++;
        }

        internal void RecordSent(int bytes)
        {
            MessagesSent++;
            BytesSent += bytes;
        }

        internal void RecordOversized() => OversizedDrops++;

        internal void RecordPreWelcomeOverflow() => PreWelcomeOverflow++;

        internal void RecordRejection(RejectReason reason)
        {
            _rejections.TryGetValue(reason, out long n);
            _rejections[reason] = n + 1;
        }
    }
}
