using System;
using System.Globalization;
using System.Text;
using Confluence.Net.Sync;
using Confluence.Net.Transport;

namespace Confluence.Net.Game.Server
{
    /// <summary>A flat one-line JSON object writer (no System.Text.Json in the netstandard libraries).</summary>
    public sealed class JsonLine
    {
        private readonly StringBuilder _sb = new StringBuilder("{");
        private bool _first = true;

        public JsonLine Add(string key, string value)
        {
            Key(key);
            if (value == null)
            {
                _sb.Append("null");
                return this;
            }

            _sb.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': _sb.Append("\\\""); break;
                    case '\\': _sb.Append("\\\\"); break;
                    case '\n': _sb.Append("\\n"); break;
                    case '\r': _sb.Append("\\r"); break;
                    case '\t': _sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) _sb.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                        else _sb.Append(c);
                        break;
                }
            }

            _sb.Append('"');
            return this;
        }

        public JsonLine Add(string key, long value)
        {
            Key(key);
            _sb.Append(value.ToString(CultureInfo.InvariantCulture));
            return this;
        }

        public JsonLine Add(string key, double value)
        {
            Key(key);
            _sb.Append(double.IsNaN(value) || double.IsInfinity(value) ? "null" : Math.Round(value, 4).ToString("R", CultureInfo.InvariantCulture));
            return this;
        }

        public JsonLine Add(string key, bool value)
        {
            Key(key);
            _sb.Append(value ? "true" : "false");
            return this;
        }

        public override string ToString() => _sb.ToString() + "}";

        private void Key(string key)
        {
            if (!_first) _sb.Append(',');
            _first = false;
            _sb.Append('"').Append(key).Append("\":");
        }
    }

    /// <summary>
    /// One per-session metrics record written at every duel end (docs/research/netcode.md §2.9.5): link (application RTT,
    /// jitter, snapshot loss, time at 20 Hz), snapshots (count, delta and reliable-fallback ratios, bytes, deferred
    /// entries), inputs (every receive-buffer counter), lag compensation (claims, replacements, clamps, the rewind
    /// breakdown), and transport security counters. Tests read the same record for pass/fail; region analytics (L423)
    /// aggregate it.
    /// </summary>
    public static class SessionMetrics
    {
        public sealed class SnapshotStats
        {
            public long Sent;
            public long Delta;
            public long Reliable;
            public long BodyBytes;
            public int MaxBodyBytes;
            public long Deferred;
            public long Partial;
            public long LowRateTicks;
            public long Ticks;
        }

        public static string Format(int seat, int duelId, int round, SnapshotLinkMonitor link, SnapshotRateSelector rate, SnapshotStats snapshots,
            NetworkInputSource input, TransportCounters counters, LinkStats transport)
        {
            var j = new JsonLine();
            j.Add("type", "session-metrics").Add("seat", seat).Add("duel", duelId).Add("round", round);
            j.Add("rttMs", link.RttMs).Add("rttMinMs", link.RttMinMs).Add("jitterMs", link.JitterMs).Add("rttSamples", link.RttSamples);
            j.Add("snapshotLoss", link.LossFraction).Add("transportRttMs", transport.RttMs).Add("reliableQueued", transport.ReliableQueued);
            j.Add("rateHz", rate.CurrentRate).Add("downgrades", rate.Downgrades).Add("upgrades", rate.Upgrades);
            j.Add("lowRateShare", snapshots.Ticks > 0 ? snapshots.LowRateTicks / (double)snapshots.Ticks : 0.0);
            j.Add("snapshots", snapshots.Sent).Add("deltaShare", snapshots.Sent > 0 ? snapshots.Delta / (double)snapshots.Sent : 0.0);
            j.Add("reliableFallbacks", snapshots.Reliable).Add("partialSnapshots", snapshots.Partial).Add("deferredEntries", snapshots.Deferred);
            j.Add("meanBodyBytes", snapshots.Sent > 0 ? snapshots.BodyBytes / (double)snapshots.Sent : 0.0).Add("maxBodyBytes", snapshots.MaxBodyBytes);
            if (input != null)
            {
                InputReceiveBuffer<Sim.Input.PlayerInput> b = input.Buffer;
                InputTimingStats t = input.Stats;
                j.Add("inputsAccepted", b.Accepted).Add("inputDuplicates", b.Duplicates).Add("inputLate", b.Late).Add("inputLost", b.LostSubstituted);
                j.Add("inputRepeated", b.RepeatedTicks).Add("inputNeutral", b.NeutralTicks).Add("inputSkipped", b.Skipped).Add("inputSurplusCut", b.SurplusCut);
                j.Add("inputMerged", b.Merged).Add("commands", t.Commands).Add("meanQueueTicks", t.MeanQueueDepth).Add("sessionOffsetTicks", input.SessionOffset);
                j.Add("viewClaims", t.ViewClaims).Add("viewsAccepted", t.ViewsAccepted).Add("viewsReplaced", t.ViewsReplaced).Add("viewsClamped", t.ViewsClamped);
                j.Add("viewsBackward", t.ViewsBackward).Add("standIns", t.StandIns).Add("eventsClamped", t.EventsClamped);
                j.Add("meanRewindTicks", t.MeanRewindTicks).Add("maxRewindTicks", t.MaxRewindTicks).Add("meanOneWayTicks", t.MeanOneWayTicks);
                j.Add("meanInterpolationTicks", t.MeanInterpolationTicks);
            }

            if (counters != null)
            {
                j.Add("violations", counters.Violations).Add("malformed", counters.Malformed).Add("rateLimited", counters.RateLimited);
                j.Add("handlerFaults", counters.HandlerFaults).Add("bytesSent", counters.BytesSent).Add("bytesReceived", counters.BytesReceived);
            }

            return j.ToString();
        }
    }
}
