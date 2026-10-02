using System;
using System.Collections.Generic;

namespace Confluence.Casting
{
    /// <summary>
    /// Refund accounting for recent committed casts (spec L90 "No refund effect may exceed 25 percent of the original
    /// paid resource or fire more than once per cast"; L124 refunds "separately capped at 25% of that payment").
    /// All refunds for one cast share the 25% cap, and each refund effect key fires at most once per cast.
    /// </summary>
    public sealed class RefundLedger
    {
        private readonly Entry[] _entries;
        private int _next;

        public RefundLedger(int capacity = CastingRules.RefundLedgerHistory)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            _entries = new Entry[capacity];
        }

        /// <summary>Records a committed cast's payment; the oldest record is forgotten when full.</summary>
        public void Open(CastEventId id, float paid)
        {
            _entries[_next] = new Entry(id, paid);
            _next = (_next + 1) % _entries.Length;
        }

        public bool Contains(CastEventId id) => Find(id) != null;

        /// <summary>Mana still refundable for the cast (0 when unknown).</summary>
        public float Remaining(CastEventId id)
        {
            var e = Find(id);
            return e == null ? 0f : Math.Max(0f, MasteryClamp.MaxRefund(e.Paid) - e.Refunded);
        }

        /// <summary>
        /// Grants up to <paramref name="amount"/> for <paramref name="effectKey"/> within the cast's remaining cap.
        /// Returns the granted amount (0 if the effect already fired for this cast, the cast is unknown, or the cap is spent).
        /// </summary>
        public float Grant(CastEventId id, string effectKey, float amount)
        {
            if (effectKey == null) throw new ArgumentNullException(nameof(effectKey));
            var e = Find(id);
            if (e == null || !(amount > 0f) || e.HasFired(effectKey)) return 0f;
            float grant = Math.Min(amount, Math.Max(0f, MasteryClamp.MaxRefund(e.Paid) - e.Refunded));
            e.MarkFired(effectKey);
            if (grant <= 0f) return 0f;
            e.Refunded += grant;
            return grant;
        }

        private Entry Find(CastEventId id)
        {
            for (int i = 0; i < _entries.Length; i++)
                if (_entries[i] != null && _entries[i].Id == id) return _entries[i];
            return null;
        }

        private sealed class Entry
        {
            private readonly List<string> _fired = new List<string>(2);

            public Entry(CastEventId id, float paid)
            {
                Id = id;
                Paid = paid;
            }

            public CastEventId Id { get; }
            public float Paid { get; }
            public float Refunded { get; set; }

            public bool HasFired(string key)
            {
                for (int i = 0; i < _fired.Count; i++)
                    if (string.Equals(_fired[i], key, StringComparison.Ordinal)) return true;
                return false;
            }

            public void MarkFired(string key) => _fired.Add(key);
        }
    }

    /// <summary>A spell's open recast state (spec L94): the original cast identity and mastery, plus uses left.</summary>
    public sealed class PendingRecast
    {
        internal PendingRecast(CastEventId castId, string spellId, MasteryResult mastery, RecastRule rule, int openedTick)
        {
            CastId = castId;
            SpellId = spellId;
            Mastery = mastery;
            Rule = rule;
            OpenedTick = openedTick;
        }

        public CastEventId CastId { get; }
        public string SpellId { get; }

        /// <summary>The source cast's one mastery result, reused by every recast (spec L94, L480).</summary>
        public MasteryResult Mastery { get; }

        public RecastRule Rule { get; }
        public int OpenedTick { get; }
        public int Used { get; internal set; }

        public int Remaining => Rule.MaxRecasts - Used;

        /// <summary>Last tick a recast is allowed, or int.MaxValue while the original effect lasts.</summary>
        public int LastTick => Rule.HasExplicitWindow ? OpenedTick + Rule.WindowTicks : int.MaxValue;
    }
}
