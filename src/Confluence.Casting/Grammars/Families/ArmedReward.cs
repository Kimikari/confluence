using System;

namespace Confluence.Casting.Grammars
{
    /// <summary>
    /// A window opened by a genuine combat event (CombatEvent family: WIZ-006 "press Cast Modifier within 0.5 s"
    /// of a successful parry, WIZ-015 "within 0.6 s" of a melee hit, WIZ-035 "within 100 ms after the declared
    /// event"). Events come only from the simulation (<see cref="CastInputFrame.CombatEvents"/>), so toggling
    /// buttons can never open it (spec L482, WIZ-035).
    /// </summary>
    public sealed class CombatEventWindow
    {
        private readonly CombatEventKind _kind;
        private readonly int _windowTicks;

        public CombatEventWindow(CombatEventKind kind, int windowTicks)
        {
            if (windowTicks < 0) throw new ArgumentOutOfRangeException(nameof(windowTicks));
            _kind = kind;
            _windowTicks = windowTicks;
            OpenedTick = int.MinValue;
        }

        public CombatEventKind Kind => _kind;

        /// <summary>Tick of the most recent qualifying event, or int.MinValue.</summary>
        public int OpenedTick { get; private set; }

        public void Observe(in CastInputFrame frame)
        {
            if (frame.CombatEvents == null) return;
            for (int i = 0; i < frame.CombatEvents.Count; i++)
                if (frame.CombatEvents[i].Kind == _kind) OpenedTick = frame.CombatEvents[i].Tick;
        }

        /// <summary>True from the event tick through <c>event + window</c> inclusive.</summary>
        public bool IsOpen(int tick) => OpenedTick != int.MinValue && tick >= OpenedTick && tick - OpenedTick <= _windowTicks;

        public void Close() => OpenedTick = int.MinValue;
    }

    /// <summary>
    /// A mastery result earned now and applied to the next committed spell within a window (WIZ-006 "arms one
    /// spell for 2 s", WIZ-015, WIZ-025). Arming again replaces the pending result instead of stacking, and it
    /// expires at the end of its window: rewards cannot be banked (spec L478).
    /// </summary>
    public sealed class ArmedReward
    {
        private MasteryResult _result;
        private int _expiresTick = int.MinValue;

        public bool IsArmed(int tick) => _result.IsMastered && tick <= _expiresTick;

        public MasteryResult Pending => _result;
        public int ExpiresTick => _expiresTick;

        public void Arm(MasteryResult result, int tick, int durationTicks)
        {
            if (!result.IsMastered) throw new ArgumentException("Only an earned reward can be armed.", nameof(result));
            if (durationTicks < 0) throw new ArgumentOutOfRangeException(nameof(durationTicks));
            _result = result;
            _expiresTick = tick + durationTicks;
        }

        /// <summary>Takes the armed reward for the cast committing at <paramref name="tick"/>, once.</summary>
        public bool TryConsume(int tick, out MasteryResult result)
        {
            if (IsArmed(tick))
            {
                result = _result;
                Clear();
                return true;
            }

            result = default;
            Clear();
            return false;
        }

        public void Clear()
        {
            _result = default;
            _expiresTick = int.MinValue;
        }
    }
}
