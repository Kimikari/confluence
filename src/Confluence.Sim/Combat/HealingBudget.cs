using System;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Sim.Hashing;

namespace Confluence.Sim.Combat
{
    /// <summary>
    /// Shared healing limits of one player for one duel (spec L114): direct healing from all sources at most 35 per
    /// second and 120 per duel; lifesteal additionally at most 20 per second inside the same budget. "Per second" is
    /// a rolling window of the last 60 ticks. The remaining duel budget is visible in inspection (spec L347).
    /// </summary>
    public sealed class HealingBudget
    {
        private readonly float[] _healWindow = new float[Ticks.PerSecond];
        private readonly float[] _lifestealWindow = new float[Ticks.PerSecond];
        private float _healWindowTotal;
        private float _lifestealWindowTotal;
        private int _currentTick = int.MinValue;

        public float RemainingDuel { get; private set; } = GlobalRules.Healing.MaxPerDuel;

        public float RestoredTotal => GlobalRules.Healing.MaxPerDuel - RemainingDuel;

        public float HealedInLastSecond => _healWindowTotal;

        public float LifestealInLastSecond => _lifestealWindowTotal;

        /// <summary>Most healing that may be granted right now (before missing health is considered).</summary>
        public float Available(bool lifesteal)
        {
            float available = Math.Min(RemainingDuel, GlobalRules.Healing.MaxPerSecond - _healWindowTotal);
            if (lifesteal)
            {
                available = Math.Min(available, GlobalRules.Healing.LifestealMaxPerSecond - _lifestealWindowTotal);
            }

            return Math.Max(0f, available);
        }

        /// <summary>Grants up to requested healing at tick and records it. Returns the granted amount.</summary>
        public float Grant(float requested, bool lifesteal, int tick)
        {
            Advance(tick);
            if (!(requested > 0f))
            {
                return 0f;
            }

            float granted = Math.Min(requested, Available(lifesteal));
            if (granted <= 0f)
            {
                return 0f;
            }

            int slot = Slot(tick);
            _healWindow[slot] += granted;
            _healWindowTotal += granted;
            if (lifesteal)
            {
                _lifestealWindow[slot] += granted;
                _lifestealWindowTotal += granted;
            }

            RemainingDuel = Math.Max(0f, RemainingDuel - granted);
            return granted;
        }

        /// <summary>Rolls the window forward to tick, forgetting healing older than one second.</summary>
        public void Advance(int tick)
        {
            if (_currentTick == int.MinValue || tick - _currentTick >= Ticks.PerSecond)
            {
                Array.Clear(_healWindow, 0, _healWindow.Length);
                Array.Clear(_lifestealWindow, 0, _lifestealWindow.Length);
                _healWindowTotal = 0f;
                _lifestealWindowTotal = 0f;
                _currentTick = tick;
                return;
            }

            if (_currentTick >= tick)
            {
                return;
            }

            while (_currentTick < tick)
            {
                _currentTick++;
                int slot = Slot(_currentTick);
                _healWindow[slot] = 0f;
                _lifestealWindow[slot] = 0f;
            }

            // Re-sum instead of subtracting so the window never drifts.
            _healWindowTotal = 0f;
            _lifestealWindowTotal = 0f;
            for (int i = 0; i < _healWindow.Length; i++)
            {
                _healWindowTotal += _healWindow[i];
                _lifestealWindowTotal += _lifestealWindow[i];
            }
        }

        public void Hash(ref StateHasher h)
        {
            h.Add(RemainingDuel);
            h.Add(_healWindowTotal);
            h.Add(_lifestealWindowTotal);
        }

        private static int Slot(int tick) => ((tick % Ticks.PerSecond) + Ticks.PerSecond) % Ticks.PerSecond;
    }
}
