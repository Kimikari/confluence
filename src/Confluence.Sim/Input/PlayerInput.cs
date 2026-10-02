using System;

namespace Confluence.Sim.Input
{
    /// <summary>
    /// Held logical buttons in one input command (spec L84 default bindings; every binding is remappable on the
    /// client, so the Sim only sees logical actions). The casting stage keeps its own equivalent flags until the
    /// two are unified.
    /// </summary>
    [Flags]
    public enum InputButtons : uint
    {
        None = 0,
        Primary = 1u << 0,      // cast (primary mouse)
        Guard = 1u << 1,        // secondary mouse
        Sprint = 1u << 2,       // Shift
        Jump = 1u << 3,         // Space
        Crouch = 1u << 4,       // Ctrl
        Dodge = 1u << 5,        // Alt
        Melee = 1u << 6,        // F
        Signature = 1u << 7,    // Q
        ActiveItem = 1u << 8,   // E
        Consumable = 1u << 9,   // X
        Recast = 1u << 10,      // T
        CastModifier = 1u << 11, // R
        ModifierA = 1u << 12,   // Z
        ModifierB = 1u << 13,   // C
        SwapFocus = 1u << 14,   // Sim addition: Main Focus / Sidearm swap (spec L106 "equip and swap actions")
    }

    /// <summary>
    /// One player's command for one authoritative tick (spec L419-L425). Clients send it every tick with a
    /// sequence number; the server sanitizes it in tick-order step 1 before any rule reads it.
    /// </summary>
    public struct PlayerInput
    {
        public const int SlotCount = 6; // spec L84: selected spell slots 1-6

        /// <summary>Tick the client intended this command for.</summary>
        public int Tick;

        /// <summary>Client sequence number, echoed in snapshots for reconciliation.</summary>
        public uint Sequence;

        /// <summary>Strafe axis, -1 (left) to 1 (right).</summary>
        public float MoveX;

        /// <summary>Forward axis, -1 (back) to 1 (forward).</summary>
        public float MoveY;

        /// <summary>View yaw in degrees (0 faces +Z, positive turns right).</summary>
        public float YawDeg;

        /// <summary>View elevation in degrees (positive looks up).</summary>
        public float PitchDeg;

        public InputButtons Buttons;

        /// <summary>Selected spell slot 0-5. Changing it has no swap windup (spec L106).</summary>
        public int SelectedSlot;

        /// <summary>
        /// Tick of the opponent state the client was displaying when it issued this command (interpolated about
        /// 100 ms behind). Lag compensation rewinds hit volumes to it, by at most 150 ms (spec L423). 0 = none.
        /// </summary>
        public int ViewTick;

        public bool Has(InputButtons button) => (Buttons & button) == button;

        public static PlayerInput Idle(float yawDeg = 0f)
        {
            return new PlayerInput { YawDeg = yawDeg };
        }

        public PlayerInput With(InputButtons buttons)
        {
            PlayerInput copy = this;
            copy.Buttons |= buttons;
            return copy;
        }

        public PlayerInput Moving(float x, float y)
        {
            PlayerInput copy = this;
            copy.MoveX = x;
            copy.MoveY = y;
            return copy;
        }

        public PlayerInput Facing(float yawDeg, float pitchDeg = 0f)
        {
            PlayerInput copy = this;
            copy.YawDeg = yawDeg;
            copy.PitchDeg = pitchDeg;
            return copy;
        }
    }
}
