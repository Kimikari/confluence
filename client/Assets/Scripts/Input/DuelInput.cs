using System;
using System.Collections.Generic;
using Confluence.Sim.Input;
using UnityEngine;
using UnityEngine.InputSystem;
using PlayerInput = Confluence.Sim.Input.PlayerInput;

namespace Confluence.Client.Input
{
    /// <summary>
    /// The duel controls as Input System actions built in code, with the default bindings of spec L84: movement WASD,
    /// aim mouse, cast primary mouse, guard secondary mouse, sprint Shift, jump Space, crouch Ctrl, dodge Alt, spell
    /// slots 1-6 or wheel, common melee F, signature Q, active equipment E, consumable X, recast T, cast modifier R,
    /// Modifier A Z and Modifier B C. Every press between two ticks is latched into the next command, so a tap shorter
    /// than a tick still reaches the Sim as one held tick (the Sim derives press edges from held buttons). Mouse look
    /// uses the raw delta with acceleration off (spec L369). Remapping is Stage 7; the action names are the stable keys.
    /// </summary>
    public sealed class DuelInput : IDisposable
    {
        /// <summary>Spec L84 default binding of every logical button, in <see cref="InputButtons"/> order.</summary>
        public static readonly IReadOnlyList<KeyValuePair<InputButtons, string>> DefaultBindings = new[]
        {
            new KeyValuePair<InputButtons, string>(InputButtons.Primary, "<Mouse>/leftButton"),
            new KeyValuePair<InputButtons, string>(InputButtons.Guard, "<Mouse>/rightButton"),
            new KeyValuePair<InputButtons, string>(InputButtons.Sprint, "<Keyboard>/leftShift"),
            new KeyValuePair<InputButtons, string>(InputButtons.Jump, "<Keyboard>/space"),
            new KeyValuePair<InputButtons, string>(InputButtons.Crouch, "<Keyboard>/leftCtrl"),
            new KeyValuePair<InputButtons, string>(InputButtons.Dodge, "<Keyboard>/leftAlt"),
            new KeyValuePair<InputButtons, string>(InputButtons.Melee, "<Keyboard>/f"),
            new KeyValuePair<InputButtons, string>(InputButtons.Signature, "<Keyboard>/q"),
            new KeyValuePair<InputButtons, string>(InputButtons.ActiveItem, "<Keyboard>/e"),
            new KeyValuePair<InputButtons, string>(InputButtons.Consumable, "<Keyboard>/x"),
            new KeyValuePair<InputButtons, string>(InputButtons.Recast, "<Keyboard>/t"),
            new KeyValuePair<InputButtons, string>(InputButtons.CastModifier, "<Keyboard>/r"),
            new KeyValuePair<InputButtons, string>(InputButtons.ModifierA, "<Keyboard>/z"),
            new KeyValuePair<InputButtons, string>(InputButtons.ModifierB, "<Keyboard>/c"),
        };

        public const int SlotCount = PlayerInput.SlotCount;

        private readonly InputActionMap _map;
        private readonly InputAction[] _buttons;
        private readonly InputButtons[] _flags;
        private readonly InputAction[] _slotKeys = new InputAction[SlotCount];
        private InputButtons _latched;
        private uint _sequence;

        public DuelInput()
        {
            _map = new InputActionMap("Duel");
            Move = _map.AddAction("Move", InputActionType.Value);
            Move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            Look = _map.AddAction("Look", InputActionType.Value, "<Mouse>/delta");

            _buttons = new InputAction[DefaultBindings.Count];
            _flags = new InputButtons[DefaultBindings.Count];
            for (int i = 0; i < DefaultBindings.Count; i++)
            {
                _flags[i] = DefaultBindings[i].Key;
                _buttons[i] = _map.AddAction(DefaultBindings[i].Key.ToString(), InputActionType.Button, DefaultBindings[i].Value);
            }

            for (int i = 0; i < SlotCount; i++)
            {
                _slotKeys[i] = _map.AddAction("Slot" + (i + 1), InputActionType.Button, "<Keyboard>/" + (i + 1));
            }

            SlotWheel = _map.AddAction("SlotWheel", InputActionType.Value, "<Mouse>/scroll/y");
            Menu = _map.AddAction("Menu", InputActionType.Button, "<Keyboard>/escape");
            Inspect = _map.AddAction("Inspect", InputActionType.Button, "<Keyboard>/tab");
        }

        public InputAction Move { get; }
        public InputAction Look { get; }
        public InputAction SlotWheel { get; }

        /// <summary>Opens the Laboratory menu (not a duel action).</summary>
        public InputAction Menu { get; }

        /// <summary>Shows the build panel; the alcove lets players inspect their loadout (spec L46).</summary>
        public InputAction Inspect { get; }

        /// <summary>Selected spell slot 0-5; slot keys and the wheel change it (no swap windup, spec L106).</summary>
        public int SelectedSlot { get; set; }

        public Vector2 LookDelta => Look.ReadValue<Vector2>();

        /// <summary>Binding path of a logical button's action (for the controls legend and tests).</summary>
        public string BindingOf(InputButtons button)
        {
            for (int i = 0; i < _flags.Length; i++)
            {
                if (_flags[i] == button)
                {
                    return _buttons[i].bindings[0].path;
                }
            }

            return null;
        }

        public void Enable() => _map.Enable();

        public void Disable() => _map.Disable();

        /// <summary>Call once per rendered frame: latches presses and applies slot selection.</summary>
        public void Update()
        {
            for (int i = 0; i < _buttons.Length; i++)
            {
                if (_buttons[i].WasPressedThisFrame())
                {
                    _latched |= _flags[i];
                }
            }

            for (int i = 0; i < SlotCount; i++)
            {
                if (_slotKeys[i].WasPressedThisFrame())
                {
                    SelectedSlot = i;
                }
            }

            float wheel = SlotWheel.ReadValue<float>();
            if (wheel > 0.01f)
            {
                SelectedSlot = (SelectedSlot + SlotCount - 1) % SlotCount;
            }
            else if (wheel < -0.01f)
            {
                SelectedSlot = (SelectedSlot + 1) % SlotCount;
            }
        }

        /// <summary>The command for the next tick: held buttons plus presses latched since the last command.</summary>
        public PlayerInput Sample(int tick, float yawDeg, float pitchDeg)
        {
            InputButtons held = InputButtons.None;
            for (int i = 0; i < _buttons.Length; i++)
            {
                if (_buttons[i].IsPressed())
                {
                    held |= _flags[i];
                }
            }

            Vector2 move = Move.ReadValue<Vector2>();
            var input = new PlayerInput
            {
                Tick = tick,
                Sequence = ++_sequence,
                MoveX = move.x,
                MoveY = move.y,
                YawDeg = yawDeg,
                PitchDeg = pitchDeg,
                Buttons = held | _latched,
                SelectedSlot = SelectedSlot,
            };
            _latched = InputButtons.None;
            return input;
        }

        public void Dispose()
        {
            _map.Disable();
            _map.Dispose();
        }
    }
}
