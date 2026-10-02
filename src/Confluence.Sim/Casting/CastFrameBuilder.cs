using System.Collections.Generic;
using Confluence.Casting;
using Confluence.Sim.Input;
using Confluence.Sim.Players;
using InputButtons = Confluence.Casting.InputButtons;

namespace Confluence.Sim.Casting
{
    /// <summary>
    /// Builds the cast machine's input for one tick from the sanitized <see cref="PlayerInput"/> and authoritative state
    /// (spec L419): held buttons plus the movement-key bits implied by the movement axes, press/release edges (a tap
    /// inside one tick counts as both), the selected slot, aim, the body's position after this tick's movement, the
    /// client's discrete grammar input, and only server-generated combat events.
    /// </summary>
    public static class CastFrameBuilder
    {
        public static CastInputFrame Build(int tick, in PlayerInput input, DuelPlayer player, InputButtons previousHeld, IReadOnlyList<CombatEvent> combatEvents)
        {
            InputButtons held = (input.Buttons & ~InputButtons.MovementKeys) | PlayerInput.MovementKeysFromAxes(input.MoveX, input.MoveY);
            held &= InputButtons.All;
            InputButtons tapped = input.Tapped & ~held & InputButtons.All;

            CastInputFrame frame = CastInputFrame.FromHeld(tick, previousHeld, held);
            frame.Pressed |= tapped & ~previousHeld;
            frame.Released |= tapped;
            frame.SelectSlot = input.SelectedSlot;
            frame.AimYawDeg = input.YawDeg;
            frame.AimPitchDeg = input.PitchDeg;
            frame.Position = player.Position;
            frame.Velocity = player.Motor.Velocity;
            frame.Grounded = player.Motor.Grounded;
            frame.InputEvents = input.GrammarEvents;
            frame.CombatEvents = combatEvents != null && combatEvents.Count > 0 ? Copy(combatEvents) : null;
            return frame;
        }

        // A frame owns its events: grammars may keep frames, and the runtime reuses its scratch list.
        private static CombatEvent[] Copy(IReadOnlyList<CombatEvent> events)
        {
            var copy = new CombatEvent[events.Count];
            for (int i = 0; i < copy.Length; i++) copy[i] = events[i];
            return copy;
        }
    }
}
