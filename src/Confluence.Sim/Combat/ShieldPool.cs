using System;
using System.Collections.Generic;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Sim.Hashing;

namespace Confluence.Sim.Combat
{
    /// <summary>One shield capacity entry.</summary>
    public sealed class ShieldEntry
    {
        internal ShieldEntry(int id, string sourceId, float amount, int expireTick, bool reserve)
        {
            Id = id;
            SourceId = sourceId;
            Amount = amount;
            ExpireTick = expireTick;
            Reserve = reserve;
        }

        public int Id { get; }
        public string SourceId { get; }
        public float Amount { get; internal set; }

        /// <summary>Expiry tick; int.MaxValue for a reserve shield that persists until broken (spec L114).</summary>
        public int ExpireTick { get; internal set; }

        /// <summary>Explicit round-start reserve shield from equipment or augments, issued once (spec L114, L579).</summary>
        public bool Reserve { get; }
    }

    /// <summary>
    /// Temporary and reserve shields of one player (spec L114, L579): they share a 100 point cap; cast and triggered
    /// shields expire within 8 s; reserve shields persist until broken and never regenerate; gaining a shield from
    /// the same source tops up instead of stacking; absorption spends the earliest-expiring capacity first.
    /// </summary>
    public sealed class ShieldPool
    {
        private readonly List<ShieldEntry> _entries = new List<ShieldEntry>();
        private int _nextId = 1;

        public static int MaxTimedTicks => Ticks.FromSeconds(GlobalRules.Shields.MaxTimedDurationSec);

        /// <summary>Entries in absorption order (earliest expiry first, then oldest).</summary>
        public IReadOnlyList<ShieldEntry> Entries => _entries;

        public float Total
        {
            get
            {
                float sum = 0f;
                for (int i = 0; i < _entries.Count; i++)
                {
                    sum += _entries[i].Amount;
                }

                return sum;
            }
        }

        public float ReserveTotal => Sum(true);

        public float TemporaryTotal => Sum(false);

        /// <summary>
        /// Grants shield capacity and returns the capacity actually added after the shared cap. A timed shield
        /// lasts min(duration, 8 s); a reserve shield persists until broken.
        /// </summary>
        public float Add(string sourceId, float amount, int durationTicks, bool reserve, int now)
        {
            if (!(amount > 0f))
            {
                return 0f;
            }

            int expire = reserve ? int.MaxValue : now + Math.Max(1, Math.Min(durationTicks, MaxTimedTicks));
            ShieldEntry entry = FindCompatible(sourceId, reserve);
            float before = Total;
            if (entry != null)
            {
                // Top up compatible capacity rather than stacking another layer (spec L114).
                _entries.Remove(entry);
                entry.Amount = Math.Max(entry.Amount, amount);
                entry.ExpireTick = expire;
            }
            else
            {
                entry = new ShieldEntry(_nextId++, sourceId, amount, expire, reserve);
            }

            float room = Math.Max(0f, GlobalRules.Shields.Cap - TotalExcluding(entry));
            entry.Amount = Math.Min(entry.Amount, room);
            if (entry.Amount > 0f)
            {
                Insert(entry);
            }

            return Math.Max(0f, Total - before);
        }

        /// <summary>Absorbs damage, earliest-expiring capacity first. Returns the amount absorbed.</summary>
        public float Absorb(float amount)
        {
            float absorbed = 0f;
            for (int i = 0; i < _entries.Count && amount > 0f; i++)
            {
                ShieldEntry e = _entries[i];
                float take = Math.Min(e.Amount, amount);
                e.Amount -= take;
                amount -= take;
                absorbed += take;
            }

            _entries.RemoveAll(e => e.Amount <= 0f);
            return absorbed;
        }

        /// <summary>Removes timed shields whose lifetime ended at or before now; returns the capacity lost.</summary>
        public float Expire(int now)
        {
            float lost = 0f;
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                if (_entries[i].ExpireTick <= now)
                {
                    lost += _entries[i].Amount;
                    _entries.RemoveAt(i);
                }
            }

            return lost;
        }

        public void Clear() => _entries.Clear();

        public void Hash(ref StateHasher h)
        {
            h.Add(_entries.Count);
            for (int i = 0; i < _entries.Count; i++)
            {
                ShieldEntry e = _entries[i];
                h.Add(e.Id);
                h.Add(e.SourceId);
                h.Add(e.Amount);
                h.Add(e.ExpireTick);
                h.Add(e.Reserve);
            }
        }

        private ShieldEntry FindCompatible(string sourceId, bool reserve)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Reserve == reserve && string.Equals(_entries[i].SourceId, sourceId, StringComparison.Ordinal))
                {
                    return _entries[i];
                }
            }

            return null;
        }

        private float TotalExcluding(ShieldEntry entry)
        {
            float sum = 0f;
            for (int i = 0; i < _entries.Count; i++)
            {
                if (!ReferenceEquals(_entries[i], entry))
                {
                    sum += _entries[i].Amount;
                }
            }

            return sum;
        }

        private float Sum(bool reserve)
        {
            float sum = 0f;
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Reserve == reserve)
                {
                    sum += _entries[i].Amount;
                }
            }

            return sum;
        }

        private void Insert(ShieldEntry entry)
        {
            int index = 0;
            while (index < _entries.Count && Precedes(_entries[index], entry))
            {
                index++;
            }

            _entries.Insert(index, entry);
        }

        private static bool Precedes(ShieldEntry a, ShieldEntry b)
        {
            if (a.ExpireTick != b.ExpireTick)
            {
                return a.ExpireTick < b.ExpireTick;
            }

            return a.Id < b.Id;
        }
    }
}
