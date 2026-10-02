using System;

namespace Confluence.Casting
{
    /// <summary>
    /// Logical input bindings sent every tick (spec L84, L474). Every binding is remappable on the client; rules code
    /// only ever sees these logical bits. Movement keys appear as bits so direction-key tracing alternatives
    /// (spec L369, L474) and declared-direction grammars read the same input the motor uses. Shared by Sim and Net.
    /// </summary>
    [Flags]
    public enum InputButtons : uint
    {
        None = 0,
        Primary = 1u << 0,      // cast, primary mouse button
        Guard = 1u << 1,        // secondary mouse button
        Sprint = 1u << 2,       // Shift
        Jump = 1u << 3,         // Space
        Crouch = 1u << 4,       // Ctrl
        Dodge = 1u << 5,        // Alt
        Melee = 1u << 6,        // F, common melee
        Signature = 1u << 7,    // Q
        ActiveItem = 1u << 8,   // E, the one enabled equipment active
        Consumable = 1u << 9,   // X
        Recast = 1u << 10,      // T, spell recast
        CastModifier = 1u << 11, // R
        ModifierA = 1u << 12,   // Z
        ModifierB = 1u << 13,   // C
        MoveForward = 1u << 14, // W
        MoveBack = 1u << 15,    // S
        MoveLeft = 1u << 16,    // A
        MoveRight = 1u << 17,   // D

        MovementKeys = MoveForward | MoveBack | MoveLeft | MoveRight,
    }

    public static class InputButtonsExtensions
    {
        public static bool Has(this InputButtons set, InputButtons buttons) => (set & buttons) == buttons;

        public static bool Any(this InputButtons set, InputButtons buttons) => (set & buttons) != 0;
    }
}
