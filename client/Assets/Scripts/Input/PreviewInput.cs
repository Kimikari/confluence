using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Confluence.Client.Input
{
    /// <summary>
    /// Input System actions for the arena preview, created in code (no .inputactions asset). Mouse look reads the
    /// raw delta with no acceleration (spec L369). Walk mode follows the default duel bindings of spec L84 where they
    /// apply (Shift sprint, Space jump, Ctrl crouch, Alt dodge); fly mode reuses Ctrl/C to descend. Remapping and
    /// controller layouts arrive with the accessibility stage (IMPLEMENTATION_PLAN Stage 7).
    /// </summary>
    public sealed class PreviewInput : IDisposable
    {
        private readonly InputActionMap _map;

        public PreviewInput()
        {
            _map = new InputActionMap("ArenaPreview");

            Move = _map.AddAction("Move", InputActionType.Value);
            Move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            Move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");

            Look = _map.AddAction("Look", InputActionType.Value, "<Mouse>/delta");
            Jump = _map.AddAction("Jump", InputActionType.Button, "<Keyboard>/space");
            Descend = _map.AddAction("Descend", InputActionType.Button, "<Keyboard>/leftCtrl");
            Descend.AddBinding("<Keyboard>/c");
            Sprint = _map.AddAction("Sprint", InputActionType.Button, "<Keyboard>/leftShift");
            Dodge = _map.AddAction("Dodge", InputActionType.Button, "<Keyboard>/leftAlt");
            ToggleFly = _map.AddAction("ToggleFly", InputActionType.Button, "<Keyboard>/f");
            ToggleOverview = _map.AddAction("ToggleOverview", InputActionType.Button, "<Keyboard>/tab");
            ToggleGenerator = _map.AddAction("ToggleGenerator", InputActionType.Button, "<Keyboard>/g");
            Back = _map.AddAction("Back", InputActionType.Button, "<Keyboard>/escape");
            Orbit = _map.AddAction("Orbit", InputActionType.Button, "<Mouse>/rightButton");
            Zoom = _map.AddAction("Zoom", InputActionType.Value, "<Mouse>/scroll/y");
            RotateLeft = _map.AddAction("RotateLeft", InputActionType.Button, "<Keyboard>/q");
            RotateRight = _map.AddAction("RotateRight", InputActionType.Button, "<Keyboard>/e");
            TimeBack = _map.AddAction("TimeBack", InputActionType.Button, "<Keyboard>/leftBracket");
            TimeForward = _map.AddAction("TimeForward", InputActionType.Button, "<Keyboard>/rightBracket");
            TimePause = _map.AddAction("TimePause", InputActionType.Button, "<Keyboard>/p");
            TimeReset = _map.AddAction("TimeReset", InputActionType.Button, "<Keyboard>/backspace");
        }

        public InputAction Move { get; }
        public InputAction Look { get; }
        public InputAction Jump { get; }

        /// <summary>Fly: move down. Walk: crouch (spec L84 Ctrl).</summary>
        public InputAction Descend { get; }

        public InputAction Sprint { get; }

        /// <summary>Walk: dodge (spec L84 Alt).</summary>
        public InputAction Dodge { get; }

        public InputAction ToggleFly { get; }
        public InputAction ToggleOverview { get; }

        /// <summary>Shows or hides the arena generator panel (and frees the cursor while it is open).</summary>
        public InputAction ToggleGenerator { get; }

        public InputAction Back { get; }
        public InputAction Orbit { get; }
        public InputAction Zoom { get; }
        public InputAction RotateLeft { get; }
        public InputAction RotateRight { get; }
        public InputAction TimeBack { get; }
        public InputAction TimeForward { get; }
        public InputAction TimePause { get; }
        public InputAction TimeReset { get; }

        public Vector2 MoveValue => Move.ReadValue<Vector2>();

        /// <summary>Raw mouse counts this frame.</summary>
        public Vector2 LookDelta => Look.ReadValue<Vector2>();

        public void Enable() => _map.Enable();

        public void Disable() => _map.Disable();

        public void Dispose()
        {
            _map.Disable();
            _map.Dispose();
        }
    }
}
