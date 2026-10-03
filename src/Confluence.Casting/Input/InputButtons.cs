using System;

namespace Confluence.Casting
{
    /// <summary>
    /// The one logical input flag set of the game (spec L84, L474). Every binding is remappable on the client; rules
    /// code only ever sees these logical bits. Sim, Net, and the client all use this enum, and Net serializes it by
    /// value, so the bit assigned to each action is part of the wire format and must never change. New actions take
    /// the next free bit.
    /// <para>
    /// Movement keys appear as bits so direction-key tracing (spec L369, L474) and declared-direction grammars read the
    /// same input the motor uses; the simulation derives them from the movement axes of each command.
    /// </para>
    /// </summary>
    [Flags]
    public enum InputButtons : uint
    {
        None = 0,
        Primary = 1u << 0,       // cast, primary mouse button
        Guard = 1u << 1,         // secondary mouse button
        Sprint = 1u << 2,        // Shift
        Jump = 1u << 3,          // Space
        Crouch = 1u << 4,        // Ctrl
        Dodge = 1u << 5,         // Alt
        Melee = 1u << 6,         // F, common melee
        Signature = 1u << 7,     // Q
        ActiveItem = 1u << 8,    // E, the one enabled equipment active
        Consumable = 1u << 9,    // X
        Recast = 1u << 10,       // T, spell recast
        CastModifier = 1u << 11, // R
        ModifierA = 1u << 12,    // Z
        ModifierB = 1u << 13,    // C
        MoveForward = 1u << 14,  // W
        MoveBack = 1u << 15,     // S
        MoveLeft = 1u << 16,     // A
        MoveRight = 1u << 17,    // D
        SwapFocus = 1u << 18,    // Main Focus / Sidearm equip swap (spec L106 "equip and swap actions")

        MovementKeys = MoveForward | MoveBack | MoveLeft | MoveRight,

        /// <summary>Every defined bit; anything else in a received command is discarded.</summary>
        All = (1u << 19) - 1u,
    }

    public static class InputButtonsExtensions
    {
        public static bool Has(this InputButtons set, InputButtons buttons) => (set & buttons) == buttons;

        public static bool Any(this InputButtons set, InputButtons buttons) => (set & buttons) != 0;
    }
}
