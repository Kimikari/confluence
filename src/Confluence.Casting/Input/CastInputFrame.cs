using System.Collections.Generic;
using Confluence.Core.Geometry;

namespace Confluence.Casting
{
    /// <summary>
    /// Everything the cast machine and grammars read for one simulation tick. The server builds it from the
    /// received input command plus authoritative body state and combat events; the client builds the same frame for
    /// prediction and HUD feedback (spec L419: clients predict preparation, then reconcile). Edge bits let a tap
    /// shorter than one tick register as both pressed and released.
    /// </summary>
    public struct CastInputFrame
    {
        /// <summary>No selection change this tick.</summary>
        public const int NoSelection = -1;

        public int Tick;

        /// <summary>Buttons down at the end of the tick.</summary>
        public InputButtons Held;

        /// <summary>Buttons that went down during the tick.</summary>
        public InputButtons Pressed;

        /// <summary>Buttons that went up during the tick.</summary>
        public InputButtons Released;

        /// <summary>Equipped spell slot (0-5) selected this tick by keys 1-6 or the wheel; <see cref="NoSelection"/> otherwise.</summary>
        public int SelectSlot;

        public float AimYawDeg;
        public float AimPitchDeg;

        /// <summary>Authoritative caster feet position and velocity (m, m/s) after this tick's movement.</summary>
        public Vec3 Position;
        public Vec3 Velocity;
        public bool Grounded;

        /// <summary>Client-originated discrete grammar input (syllables, strokes, widget picks); null when none.</summary>
        public IReadOnlyList<GrammarInputEvent> InputEvents;

        /// <summary>Server-generated combat events for the caster this tick; null when none. Never read from the client.</summary>
        public IReadOnlyList<CombatEvent> CombatEvents;

        /// <summary>A frame with edges derived from the previous and current held sets.</summary>
        public static CastInputFrame FromHeld(int tick, InputButtons previousHeld, InputButtons held)
        {
            return new CastInputFrame
            {
                Tick = tick,
                Held = held,
                Pressed = held & ~previousHeld,
                Released = previousHeld & ~held,
                SelectSlot = NoSelection,
            };
        }

        public bool IsHeld(InputButtons buttons) => (Held & buttons) == buttons;

        public bool WasPressed(InputButtons buttons) => (Pressed & buttons) != 0;

        public bool WasReleased(InputButtons buttons) => (Released & buttons) != 0;

        public int InputEventCount => InputEvents == null ? 0 : InputEvents.Count;

        public int CombatEventCount => CombatEvents == null ? 0 : CombatEvents.Count;

        public bool HasCombatEvent(CombatEventKind kind)
        {
            if (CombatEvents == null) return false;
            for (int i = 0; i < CombatEvents.Count; i++)
                if (CombatEvents[i].Kind == kind) return true;
            return false;
        }
    }
}
