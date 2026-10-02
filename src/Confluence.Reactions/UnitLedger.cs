using System.Text;

namespace Confluence.Reactions
{
    /// <summary>
    /// Source-strength accounting (spec L165 "Reactions cannot generate more energy than their source permits", L529
    /// "Repeated ticks consume the same cast budget"). Every unit in the grid entered through the initial arena, a
    /// paid deposit, an announced refill, or a reaction that returns material it previously consumed (melt returns
    /// the water an ice tile froze; condensing returns at most the water a cloud consumed). The invariant
    /// <c>Current == Initial + Deposited + Refilled + Produced - Consumed - Removed - Expired</c> holds after every step.
    /// </summary>
    public sealed class UnitLedger
    {
        private readonly long[] _initial = new long[Materials.Count];
        private readonly long[] _deposited = new long[Materials.Count];
        private readonly long[] _clipped = new long[Materials.Count];
        private readonly long[] _refilled = new long[Materials.Count];
        private readonly long[] _produced = new long[Materials.Count];
        private readonly long[] _consumed = new long[Materials.Count];
        private readonly long[] _removed = new long[Materials.Count];
        private readonly long[] _expired = new long[Materials.Count];

        /// <summary>Ice tiles present at combat start (map-owned frozen water, BIO-004).</summary>
        public long IceTilesInitial { get; internal set; }

        /// <summary>Ice tiles formed by frost-water reactions; each consumed one water unit (L145).</summary>
        public long IceTilesFormed { get; internal set; }

        /// <summary>Ice tiles melted by heat; each returned one water unit (L146).</summary>
        public long IceTilesMelted { get; internal set; }

        /// <summary>Water units consumed into steam (L144); the most condensing can ever return.</summary>
        public long SteamWater { get; internal set; }

        /// <summary>Water units returned by condensing clouds (L167).</summary>
        public long CondensedWater { get; internal set; }

        public long Initial(Material m) => _initial[(int)m];
        public long Deposited(Material m) => _deposited[(int)m];

        /// <summary>Requested units that did not fit under the four-unit cap (L163).</summary>
        public long Clipped(Material m) => _clipped[(int)m];

        public long Refilled(Material m) => _refilled[(int)m];
        public long Produced(Material m) => _produced[(int)m];
        public long Consumed(Material m) => _consumed[(int)m];
        public long Removed(Material m) => _removed[(int)m];
        public long Expired(Material m) => _expired[(int)m];

        /// <summary>Units the ledger says should be in the grid now.</summary>
        public long Expected(Material m) =>
            _initial[(int)m] + _deposited[(int)m] + _refilled[(int)m] + _produced[(int)m] -
            _consumed[(int)m] - _removed[(int)m] - _expired[(int)m];

        internal void AddInitial(Material m, int units) => _initial[(int)m] += units;
        internal void AddDeposited(Material m, int units) => _deposited[(int)m] += units;
        internal void AddClipped(Material m, int units) => _clipped[(int)m] += units;
        internal void AddRefilled(Material m, int units) => _refilled[(int)m] += units;
        internal void AddProduced(Material m, int units) => _produced[(int)m] += units;
        internal void AddConsumed(Material m, int units) => _consumed[(int)m] += units;
        internal void AddRemoved(Material m, int units) => _removed[(int)m] += units;
        internal void AddExpired(Material m, int units) => _expired[(int)m] += units;

        public override string ToString()
        {
            var sb = new StringBuilder();
            foreach (Material m in Materials.All)
            {
                sb.Append(m).Append(": init ").Append(Initial(m)).Append(" dep ").Append(Deposited(m))
                    .Append(" refill ").Append(Refilled(m)).Append(" prod ").Append(Produced(m))
                    .Append(" cons ").Append(Consumed(m)).Append(" rem ").Append(Removed(m))
                    .Append(" exp ").Append(Expired(m)).Append('\n');
            }

            return sb.ToString();
        }
    }
}
