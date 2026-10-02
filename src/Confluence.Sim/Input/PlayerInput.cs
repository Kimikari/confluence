using System.Collections.Generic;
using Confluence.Casting;

namespace Confluence.Sim.Input
{
    /// <summary>
    /// One player's command for one authoritative tick (spec L419-L425). Clients send it every tick with a
    /// sequence number; the server sanitizes it in tick-order step 1 before any rule reads it. Buttons use the one
    /// shared <see cref="InputButtons"/> enum (Confluence.Casting), serialized by value.
    /// </summary>
    public struct PlayerInput
    {
        public const int SlotCount = 6; // spec L84: selected spell slots 1-6

        /// <summary>A movement axis at or beyond this magnitude counts as its movement key being held.</summary>
        public const float MovementKeyThreshold = 0.5f;

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

        /// <summary>Buttons held at the end of the command's tick.</summary>
        public InputButtons Buttons;

        /// <summary>
        /// Buttons pressed and released again inside the command's tick (a tap shorter than one tick). Casting reads
        /// them as both a press and a release edge so a quick tap is never lost (see CastInputFrame).
        /// </summary>
        public InputButtons Tapped;

        /// <summary>Selected spell slot 0-5. Changing it has no swap windup (spec L106).</summary>
        public int SelectedSlot;

        /// <summary>
        /// Tick of the opponent state the client was displaying when it issued this command (interpolated about
        /// 100 ms behind). Lag compensation rewinds hit volumes to it, by at most 150 ms (spec L423). 0 = none.
        /// </summary>
        public int ViewTick;

        /// <summary>
        /// Client-originated discrete grammar input for this tick (syllables, strokes, widget picks; spec L365-L369,
        /// L490), or null. Combat events are never taken from the client; the server generates them.
        /// </summary>
        public IReadOnlyList<GrammarInputEvent> GrammarEvents;

        public bool Has(InputButtons button) => (Buttons & button) == button;

        /// <summary>The movement-key bits implied by the movement axes (W/S/A/D for direction-key grammars).</summary>
        public static InputButtons MovementKeysFromAxes(float moveX, float moveY)
        {
            InputButtons keys = InputButtons.None;
            if (moveY >= MovementKeyThreshold) keys |= InputButtons.MoveForward;
            if (moveY <= -MovementKeyThreshold) keys |= InputButtons.MoveBack;
            if (moveX <= -MovementKeyThreshold) keys |= InputButtons.MoveLeft;
            if (moveX >= MovementKeyThreshold) keys |= InputButtons.MoveRight;
            return keys;
        }

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

        public PlayerInput Selecting(int slot)
        {
            PlayerInput copy = this;
            copy.SelectedSlot = slot;
            return copy;
        }
    }
}
