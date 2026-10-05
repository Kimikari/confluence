using LiteNetLib;

namespace Confluence.Net.Protocol
{
    /// <summary>
    /// Logical lanes. Reliable-ordered lanes are independent, so a slow shop transaction never stalls combat
    /// events and vice versa. Inputs and snapshots are sequenced: a late packet older than one already
    /// received is dropped, which is safe because each input packet repeats recent inputs and each snapshot
    /// supersedes the previous one.
    /// </summary>
    public enum NetChannel : byte
    {
        /// <summary>Session control: welcome, kicks, and other transport-level messages. Reliable ordered.</summary>
        Control = 0,

        /// <summary>Match and round flow (phases, pairing, results). Reliable ordered.</summary>
        Match = 1,

        /// <summary>Equipment shop transactions. Reliable ordered.</summary>
        Shop = 2,

        /// <summary>Augment draft transactions. Reliable ordered.</summary>
        Draft = 3,

        /// <summary>Authoritative combat events (casts, damage, reactions). Reliable ordered.</summary>
        Events = 4,

        /// <summary>Independent reliable payloads where order is irrelevant (codex data, replay chunks). Reliable unordered.</summary>
        Bulk = 5,

        /// <summary>Client input commands with redundancy. Sequenced.</summary>
        Input = 6,

        /// <summary>World state snapshots. Sequenced.</summary>
        Snapshot = 7,

        /// <summary>Clock synchronization probes. Unreliable.</summary>
        Time = 8,

        /// <summary>
        /// Cosmetic one-shot cues (reaction flashes) at snapshot cadence. Sequenced, on its own lane so a lost cue never
        /// delays a damage confirmation on <see cref="Events"/> and never displaces a snapshot (docs/research/netcode.md
        /// §2.5.1).
        /// </summary>
        Cosmetic = 9,
    }

    /// <summary>Mapping between logical channels and LiteNetLib lane numbers and delivery methods.</summary>
    public static class NetChannels
    {
        /// <summary>LiteNetLib channel count (each lane carries one delivery method).</summary>
        public const byte LaneCount = 10;

        public static DeliveryMethod DeliveryOf(NetChannel channel)
        {
            switch (channel)
            {
                case NetChannel.Control:
                case NetChannel.Match:
                case NetChannel.Shop:
                case NetChannel.Draft:
                case NetChannel.Events:
                    return DeliveryMethod.ReliableOrdered;
                case NetChannel.Bulk:
                    return DeliveryMethod.ReliableUnordered;
                case NetChannel.Input:
                case NetChannel.Snapshot:
                case NetChannel.Cosmetic:
                    return DeliveryMethod.Sequenced;
                default:
                    return DeliveryMethod.Unreliable;
            }
        }

        public static byte LaneOf(NetChannel channel) => (byte)channel;

        public static bool IsReliable(NetChannel channel)
        {
            DeliveryMethod method = DeliveryOf(channel);
            return method == DeliveryMethod.ReliableOrdered || method == DeliveryMethod.ReliableUnordered;
        }

        /// <summary>
        /// The logical channel a packet arrived on. Unreliable packets carry no lane number in LiteNetLib, so the
        /// unreliable delivery method alone identifies <see cref="NetChannel.Time"/>.
        /// </summary>
        public static bool TryFromLane(byte lane, DeliveryMethod method, out NetChannel channel)
        {
            if (method == DeliveryMethod.Unreliable)
            {
                channel = NetChannel.Time;
                return true;
            }

            channel = (NetChannel)lane;
            return lane < LaneCount && DeliveryOf(channel) == method;
        }
    }
}
