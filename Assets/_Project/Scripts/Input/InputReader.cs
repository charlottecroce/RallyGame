using UnityEngine;
using RallyGame.Core;
using RallyGame.Vehicles.Controllers;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace RallyGame.Input
{
    /// Single input surface for the whole game. Compiles against either input backend,
    /// so swapping to an InputActions asset later means editing only this file.
    ///
    /// Sample() runs every frame. Move, Look, throttle, brake and steer are NEVER
    /// logged. Only one-shot presses (E, Esc, M, Tab, L, Space) are, and only at
    /// Verbose so they can be switched off in one click.
    [CreateAssetMenu(menuName = "Rally/Input Reader", fileName = "InputReader")]
    public class InputReader : ScriptableObject
    {
        public Vector2 Move { get; private set; }
        public Vector2 Look { get; private set; }
        public int LastSampleFrame { get; private set; }
        public bool JumpPressed { get; private set; }
        public bool InteractPressed { get; private set; }
        public bool MenuPressed { get; private set; }
        public bool MapPressed { get; private set; }
        public bool RaceBookPressed { get; private set; }
        public bool LightsPressed { get; private set; }
        public VehicleInput Vehicle { get; private set; }

        [SerializeField] private float lookSensitivity = 0.12f;

        [Header("Debug")]
        [Tooltip("Log one-shot key presses at Verbose. Axes are never logged regardless.")]
        [SerializeField] private bool logKeyPresses = true;

        private bool warnedNoKeyboard;

        /// Called once per frame by InputPump. Nothing else polls devices.
        public void Sample()
        {
            LastSampleFrame = Time.frameCount;
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null)
            {
                if (!warnedNoKeyboard)
                {
                    warnedNoKeyboard = true;
                    GameLog.Warn(LogCat.Input, "No keyboard device found — all input will read as zero.", this);
                }
                Vehicle = default;
                return;
            }
            warnedNoKeyboard = false;

            Move = new Vector2(
                (kb.dKey.isPressed ? 1 : 0) - (kb.aKey.isPressed ? 1 : 0),
                (kb.wKey.isPressed ? 1 : 0) - (kb.sKey.isPressed ? 1 : 0));
            Look = mouse != null ? mouse.delta.ReadValue() * lookSensitivity : Vector2.zero;

            JumpPressed = kb.spaceKey.wasPressedThisFrame;
            InteractPressed = kb.eKey.wasPressedThisFrame;
            MenuPressed = kb.escapeKey.wasPressedThisFrame;
            MapPressed = kb.mKey.wasPressedThisFrame;
            RaceBookPressed = kb.tabKey.wasPressedThisFrame;
            LightsPressed = kb.lKey.wasPressedThisFrame;

            Vehicle = new VehicleInput
            {
                throttle = kb.wKey.isPressed ? 1f : 0f,
                brake = kb.sKey.isPressed ? 1f : 0f,
                steer = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f),
                handbrake = kb.spaceKey.isPressed,
                shiftUp = kb.eKey.wasPressedThisFrame,
                shiftDown = kb.qKey.wasPressedThisFrame,
                lights = lightsOn
            };
#else
            Move = new Vector2(UnityEngine.Input.GetAxisRaw("Horizontal"), UnityEngine.Input.GetAxisRaw("Vertical"));
            Look = new Vector2(UnityEngine.Input.GetAxisRaw("Mouse X"), UnityEngine.Input.GetAxisRaw("Mouse Y")) * (lookSensitivity * 10f);

            JumpPressed = UnityEngine.Input.GetKeyDown(KeyCode.Space);
            InteractPressed = UnityEngine.Input.GetKeyDown(KeyCode.E);
            MenuPressed = UnityEngine.Input.GetKeyDown(KeyCode.Escape);
            MapPressed = UnityEngine.Input.GetKeyDown(KeyCode.M);
            RaceBookPressed = UnityEngine.Input.GetKeyDown(KeyCode.Tab);
            LightsPressed = UnityEngine.Input.GetKeyDown(KeyCode.L);

            Vehicle = new VehicleInput
            {
                throttle = Mathf.Max(0f, UnityEngine.Input.GetAxis("Vertical")),
                brake = Mathf.Max(0f, -UnityEngine.Input.GetAxis("Vertical")),
                steer = UnityEngine.Input.GetAxis("Horizontal"),
                handbrake = UnityEngine.Input.GetKey(KeyCode.Space),
                shiftUp = UnityEngine.Input.GetKeyDown(KeyCode.E),
                shiftDown = UnityEngine.Input.GetKeyDown(KeyCode.Q),
                lights = lightsOn
            };
#endif
            if (LightsPressed)
            {
                lightsOn = !lightsOn;
                GameLog.Action(LogCat.Input, "Lights toggled", lightsOn ? "ON" : "OFF", this);
            }

            LogPresses();
        }

        [System.NonSerialized] private bool lightsOn;

        /// Menus call this so held keys do not leak into gameplay.
        public void Clear()
        {
            GameLog.Verbose(LogCat.Input, "Input cleared (menu took focus)", this);
            Move = Vector2.zero; Look = Vector2.zero; Vehicle = default;
        }

        // ---- debug ---------------------------------------------------------

        /// One-shot presses only. Axes deliberately absent.
        private void LogPresses()
        {
            if (!logKeyPresses) return;

            if (InteractPressed) GameLog.Verbose(LogCat.Input, "Key: E (interact / shift up)", this);
            if (MenuPressed)     GameLog.Verbose(LogCat.Input, "Key: Escape (menu)", this);
            if (MapPressed)      GameLog.Verbose(LogCat.Input, "Key: M (map)", this);
            if (RaceBookPressed) GameLog.Verbose(LogCat.Input, "Key: Tab (race book)", this);
            if (JumpPressed)     GameLog.Verbose(LogCat.Input, "Key: Space (jump / handbrake)", this);
        }
    }
}
